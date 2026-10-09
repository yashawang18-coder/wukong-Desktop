from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


BATCH_ID = "WK-AUTONOMOUS-PATROL-WALK-v10"
SOURCE_BATCH = "WK-AUTONOMOUS-PATROL-WALK-v8"
REPLACED_FRAME = "frames/cycle-003.png"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def clean_detached_low_alpha(rgba: np.ndarray) -> np.ndarray:
    result = rgba.copy()
    result[:, :, 3][result[:, :, 3] <= 1] = 0
    labels, count = ndimage.label(result[:, :, 3] > 0, structure=np.ones((3, 3), dtype=np.uint8))
    for label in range(1, count + 1):
        component = labels == label
        if int(result[:, :, 3][component].max(initial=0)) <= 8:
            result[component] = 0
    result[result[:, :, 3] == 0, :3] = 0
    return result


def resize_channel(channel: np.ndarray, size: tuple[int, int]) -> np.ndarray:
    return np.asarray(Image.fromarray(channel.astype(np.float32), mode="F").resize(size, Image.Resampling.LANCZOS))


def normalize_generated_master(source: Path, destination: Path) -> None:
    image = Image.open(source).convert("RGBA")
    if image.size != (1254, 1254):
        raise ValueError(f"generated master must be 1254x1254, got {image.size}")
    rgba = clean_detached_low_alpha(np.asarray(image, dtype=np.uint8)).astype(np.float32) / 255.0
    alpha = rgba[:, :, 3]
    premultiplied = rgba[:, :, :3] * alpha[:, :, None]
    resized_alpha = np.clip(resize_channel(alpha, (1024, 1024)), 0, 1)
    resized_premultiplied = np.stack(
        [np.clip(resize_channel(premultiplied[:, :, channel], (1024, 1024)), 0, 1) for channel in range(3)],
        axis=2,
    )
    straight = np.zeros((1024, 1024, 4), dtype=np.float32)
    visible = resized_alpha > (1 / 255.0)
    straight[:, :, 3] = resized_alpha
    straight[:, :, :3][visible] = resized_premultiplied[visible] / resized_alpha[visible, None]
    encoded = np.clip(np.rint(straight * 255.0), 0, 255).astype(np.uint8)
    encoded = clean_detached_low_alpha(encoded)
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(".tmp.png")
    Image.fromarray(encoded, mode="RGBA").save(temporary, format="PNG", optimize=False, compress_level=9)
    Image.open(temporary).load()
    temporary.replace(destination)


def alpha_bbox(path: Path) -> list[int]:
    alpha = np.asarray(Image.open(path).convert("RGBA"), dtype=np.uint8)[:, :, 3]
    ys, xs = np.where(alpha > 0)
    if len(xs) == 0:
        raise ValueError(f"empty alpha: {path}")
    return [int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)]


def update_manifest(source_manifest: dict, source_manifest_path: Path, output: Path, generated_master: Path) -> dict:
    document = json.loads(json.dumps(source_manifest))
    document.update(
        {
            "batch_id": BATCH_ID,
            "asset_id": BATCH_ID,
            "asset_stage": "runtime-candidate",
            "candidate_profile": "patrol-walk-v10-frame3-midpoint-review",
            "source_package": f"{SOURCE_BATCH} plus generated midpoint from cycle-002 and cycle-004",
            "source_manifest_sha256": sha256(source_manifest_path),
            "source_png_byte_identity": False,
            "owner_preview_approved": False,
            "visual_approved": False,
            "runtime_validation": "pending_owner_windows_renderer_qa",
            "runtime_approved": False,
            "runtime_use": False,
            "production_asset": False,
            "prototype_use": False,
            "developer_preview": True,
            "autonomous_binding_enabled": False,
            "allowed_sources": ["DeveloperPreview"],
            "window_motion_validation": "pending_owner_windows_renderer_qa",
        }
    )
    document["normalization"]["generated_midpoint_source_sha256"] = sha256(generated_master)
    document["normalization"]["generated_midpoint_inputs"] = ["cycle-002.png", "cycle-004.png"]
    document["normalization"]["generated_midpoint_prompt_scope"] = "whole-dog temporal midpoint; preserve identity, scale, baseline, coat and alpha"
    document["approval_evidence"] = {
        "owner_requested_frame_repair": "2026-10-09",
        "owner_art_accepted": None,
        "automated_windows_playback": False,
        "owner_final_desktop_review": "pending",
    }
    frame_path = output / BATCH_ID / REPLACED_FRAME
    frame_hash = sha256(frame_path)
    frame_bytes = frame_path.stat().st_size
    frame_bbox = alpha_bbox(frame_path)
    for frame in document["frame_inventory"]:
        if frame["path"] == REPLACED_FRAME:
            frame.update(
                {
                    "bytes": frame_bytes,
                    "sha256": frame_hash,
                    "source_sha256": sha256(generated_master),
                    "alpha_bbox": frame_bbox,
                }
            )
    for action in document["actions"]:
        action.update(
            {
                "owner_preview_approved": False,
                "visual_approved": False,
                "runtime_validation": "pending_owner_windows_renderer_qa",
                "runtime_approved": False,
                "runtime_use": False,
                "production_asset": False,
                "prototype_use": False,
                "developer_preview": True,
                "autonomous_binding_enabled": False,
                "allowed_sources": ["DeveloperPreview"],
            }
        )
        for phase in action["phases"]:
            for frame in phase["frames"]:
                if frame["path"] == REPLACED_FRAME:
                    frame.update({"bytes": frame_bytes, "sha256": frame_hash})
    return document


def build(source_root: Path, generated_master: Path, output_root: Path) -> None:
    source_batch = source_root / SOURCE_BATCH
    output_batch = output_root / BATCH_ID
    if output_batch.exists():
        raise FileExistsError(f"refusing to overwrite {output_batch}")
    output_frames = output_batch / "frames"
    output_frames.mkdir(parents=True)
    for source in sorted((source_batch / "frames").glob("*.png")):
        if source.name == "cycle-003.png":
            continue
        shutil.copyfile(source, output_frames / source.name)
    normalize_generated_master(generated_master, output_frames / "cycle-003.png")

    source_manifest = json.loads((source_batch / "manifest.json").read_text(encoding="utf-8"))
    document = update_manifest(source_manifest, source_batch / "manifest.json", output_root, generated_master)
    serialized = json.dumps(document, ensure_ascii=False, indent=2) + "\n"
    (output_batch / "asset.json").write_text(serialized, encoding="utf-8", newline="\n")
    (output_batch / "manifest.json").write_text(serialized, encoding="utf-8", newline="\n")
    inventory = []
    for frame in sorted(output_frames.glob("*.png")):
        inventory.append(f"{sha256(frame)}  frames/{frame.name}")
    (output_batch / "SOURCE-FRAME-SHA256SUMS.sha256").write_text("\n".join(inventory) + "\n", encoding="ascii")
    (output_batch / "README.md").write_text(
        "# Patrol walk v10 frame-3 review\n\n"
        "This candidate preserves the approved v8 intro, loop and exit contract. Twelve runtime PNGs are byte-identical "
        "to v8. Only `cycle-003.png` is a new whole-dog temporal midpoint generated from approved `cycle-002.png` and "
        "`cycle-004.png`, then normalized once with the recorded v8 full-canvas parameters.\n\n"
        "The v8 package remains immutable and active. This package is DeveloperPreview-only until owner Windows playback "
        "confirms gait, coat continuity, alpha edges, anchor and window translation.\n",
        encoding="utf-8",
        newline="\n",
    )

    report = {
        "batch_id": BATCH_ID,
        "generated_master": {"sha256": sha256(generated_master), "size": list(Image.open(generated_master).size)},
        "runtime_frame": {
            "path": REPLACED_FRAME,
            "sha256": sha256(output_frames / "cycle-003.png"),
            "bytes": (output_frames / "cycle-003.png").stat().st_size,
            "size": list(Image.open(output_frames / "cycle-003.png").size),
            "mode": Image.open(output_frames / "cycle-003.png").mode,
            "alpha_bbox": alpha_bbox(output_frames / "cycle-003.png"),
        },
        "unchanged_v8_frames": 12,
        "runtime_gate": "developer_preview_only",
    }
    (output_batch / "BUILD-VALIDATION.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--generated-master", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    build(args.source_root.resolve(), args.generated_master.resolve(), args.output_root.resolve())


if __name__ == "__main__":
    main()
