#!/usr/bin/env python3
"""Import the eight v19 coin masters and derive deterministic flip frames.

The source images already define the approved candidate artwork and shared alpha
silhouette. This tool does not redraw or recolor visible pixels. It only clears
RGB values under fully transparent pixels and derives the narrow flip views by
premultiplied-alpha horizontal resampling.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-MAGIC-SPECIALS-CANDIDATE-v1"
COIN = BATCH / "petrificus_coin"
VERSION_ROOT = COIN / "v19"
FACE_BOUNDS = (62, 70, 962, 952)
FLIP_WIDTHS = (900, 845, 672, 415, 112, 415, 672, 845, 900)
STATE_FILES = {
    "vivid": ("state-01-vivid.png", "dog_coin_front_state_01.png", "dog_coin_back_state_01.png"),
    "flat": ("state-02-flat.png", "dog_coin_front_state_02.png", "dog_coin_back_state_02.png"),
    "faded": ("state-03-faded.png", "dog_coin_front_state_03.png", "dog_coin_back_state_03.png"),
    "exhausted": ("state-04-exhausted.png", "dog_coin_front_state_04.png", "dog_coin_back_state_04.png"),
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save_png(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    image.save(temporary, format="PNG", compress_level=6)
    with Image.open(temporary) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (1024, 1024):
            raise ValueError(f"invalid generated PNG: {path}")
    temporary.replace(path)


def canonical_face(source: Path) -> Image.Image:
    with Image.open(source) as opened:
        opened.load()
        if opened.format != "PNG" or opened.mode != "RGBA" or opened.size != (1024, 1024):
            raise ValueError(f"source must be a 1024x1024 RGBA PNG: {source}")
        if opened.getchannel("A").getbbox() != FACE_BOUNDS:
            raise ValueError(f"source visible bounds differ from {FACE_BOUNDS}: {source}")
        rgba = np.asarray(opened).copy()

    rgba[rgba[:, :, 3] == 0, :3] = 0
    return Image.fromarray(rgba, "RGBA")


def resize_rgba_premultiplied(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA")).astype(np.float32) / 255.0
    alpha = rgba[:, :, 3:4]
    premultiplied = np.concatenate((rgba[:, :, :3] * alpha, alpha), axis=2)
    packed = Image.fromarray(np.clip(np.rint(premultiplied * 255.0), 0, 255).astype(np.uint8), "RGBA")
    resized = np.asarray(packed.resize(size, Image.Resampling.LANCZOS)).astype(np.float32) / 255.0
    resized_alpha = resized[:, :, 3:4]
    straight_rgb = np.divide(
        resized[:, :, :3],
        resized_alpha,
        out=np.zeros_like(resized[:, :, :3]),
        where=resized_alpha > 1e-6,
    )
    straight = np.concatenate((straight_rgb, resized_alpha), axis=2)
    result = np.clip(np.rint(straight * 255.0), 0, 255).astype(np.uint8)
    result[result[:, :, 3] == 0, :3] = 0
    return Image.fromarray(result, "RGBA")


def flip_frame(face: Image.Image, width: int) -> Image.Image:
    x0, y0, x1, y1 = FACE_BOUNDS
    compressed = resize_rgba_premultiplied(face.crop(FACE_BOUNDS), (width, y1 - y0))
    frame = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    frame.paste(compressed, ((1024 - width) // 2, y0))
    return frame


def build_flip(front: Image.Image, back: Image.Image, state: str) -> list[Path]:
    destination = VERSION_ROOT / "flip" / state / "front-to-back"
    destination.mkdir(parents=True, exist_ok=True)
    paths = []
    for index, width in enumerate(FLIP_WIDTHS, start=1):
        if index == 1:
            frame = front.copy()
        elif index == len(FLIP_WIDTHS):
            frame = back.copy()
        else:
            frame = flip_frame(front if index <= 5 else back, width)
        path = destination / f"frame-{index:03d}.png"
        save_png(frame, path)
        paths.append(path)
    return paths


def build_review_gif(frames: list[Image.Image], path: Path, duration_ms: int) -> None:
    reviews = []
    for frame in frames:
        compact = frame.resize((256, 256), Image.Resampling.LANCZOS)
        review = Image.new("RGB", (512, 256), (246, 247, 249))
        review.paste((31, 35, 42), (256, 0, 512, 256))
        review.paste(compact, (0, 0), compact)
        review.paste(compact, (256, 0), compact)
        reviews.append(review)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    reviews[0].save(
        temporary,
        format="GIF",
        save_all=True,
        append_images=reviews[1:],
        duration=duration_ms,
        loop=0,
        disposal=2,
    )
    temporary.replace(path)


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def update_manifests(
    source_root: Path,
    source_records: list[dict[str, object]],
    runtime_paths: list[Path],
    previews: list[Path],
) -> None:
    runtime_records = [
        {
            "path": path.relative_to(BATCH).as_posix(),
            "bytes": path.stat().st_size,
            "sha256": sha256(path),
        }
        for path in sorted(runtime_paths)
    ]
    checksum_text = "".join(f"{item['sha256']}  {item['path']}\n" for item in runtime_records)
    (BATCH / "coin-checksums.sha256").write_text(checksum_text, encoding="utf-8")
    (VERSION_ROOT / "SHA256SUMS.txt").write_text(checksum_text, encoding="utf-8")
    (VERSION_ROOT / "SOURCE-SHA256SUMS.txt").write_text(
        "".join(f"{item['sha256']}  {item['source_file']}\n" for item in source_records),
        encoding="utf-8",
    )

    states = []
    flip_directories = {}
    for state, (target_name, _, _) in STATE_FILES.items():
        states.append(
            {
                "id": state,
                "front": f"petrificus_coin/v19/front/{target_name}",
                "back": f"petrificus_coin/v19/back/{target_name}",
            }
        )
        flip_directories[state] = f"petrificus_coin/v19/flip/{state}/front-to-back"

    coin_manifest_path = BATCH / "coin-manifest.json"
    coin_manifest = json.loads(coin_manifest_path.read_text(encoding="utf-8"))
    coin_manifest.update(
        {
            "asset_id": "WK-MAGIC-PETRIFY-COIN-v19-candidate",
            "display_name": "Petrified coin v19 candidate",
            "status": "runtime-candidate",
            "visual_approved": False,
            "runtime_validation": "pending_windows_renderer_qa",
            "runtime_approved": False,
            "runtime_use": False,
            "prototype_use": True,
            "production_asset": False,
            "states": states,
        }
    )
    coin_manifest["flip"]["front_to_back"]["directories_by_state"] = flip_directories
    coin_manifest["state_machine"]["settle"]["after_ms"] = coin_manifest["timing"]["settle_to_flat_ms"]
    coin_manifest["integration"].update(
        {
            "prototype_use": True,
            "production_asset": False,
            "renderer_qa": "pending_windows_renderer_qa",
            "runtime_use": False,
        }
    )
    coin_manifest["source"] = {
        "label": source_root.name,
        "external_path_not_persisted": True,
        "masters": source_records,
    }
    coin_manifest["derivation"] = {
        "method": "deterministic_premultiplied_alpha_horizontal_compression",
        "visible_pixels_repainted": False,
        "static_face_change": "RGB cleared only where alpha equals zero",
        "flip_widths": list(FLIP_WIDTHS),
        "swap_face_at_frame": 6,
    }
    coin_manifest["previews"] = [
        {
            "kind": "v19-master-faces-light-dark",
            "path": previews[0].relative_to(BATCH).as_posix(),
            "frames": 8,
            "frame_duration_ms": 700,
            "sha256": sha256(previews[0]),
        },
        {
            "kind": "v19-flip-sequences-light-dark",
            "path": previews[1].relative_to(BATCH).as_posix(),
            "frames": 36,
            "frame_duration_ms": 80,
            "sha256": sha256(previews[1]),
        },
    ]
    revision_id = "2026-09-07-v19-eight-master-import"
    revision = {
        "id": revision_id,
        "date": "2026-09-07",
        "change": "Imported eight v19 coin masters into a versioned candidate path, cleared hidden RGB under zero alpha, and derived four nine-frame flip sequences without redrawing visible artwork.",
        "visual_approved": False,
        "runtime_validation": "pending_windows_renderer_qa",
        "runtime_approved": False,
        "runtime_use": False,
    }
    coin_manifest["revision_notes"] = [
        item for item in coin_manifest.get("revision_notes", []) if item.get("id") != revision_id
    ] + [revision]
    coin_manifest["edge_baseline"] = {
        "profile": "v19_source_shared_alpha",
        "visible_bounds": {"x": 62, "y": 70, "width": 900, "height": 882},
        "faces_share_exact_alpha": True,
        "transparent_rgb_zeroed": True,
        "flip_widths": list(FLIP_WIDTHS),
        "repair_scope": "zero-alpha RGB sanitation and deterministic flip derivation only",
    }
    write_json(coin_manifest_path, coin_manifest)

    vivid_front = VERSION_ROOT / "front/state-01-vivid.png"
    manifest_path = BATCH / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    replaced = 0
    for action in manifest["actions"]:
        for phase in action["phases"]:
            for frame in phase["frames"]:
                if frame["path"] in {
                    "petrificus_coin/front/state-01-vivid.png",
                    "petrificus_coin/v19/front/state-01-vivid.png",
                }:
                    frame.update(
                        {
                            "path": "petrificus_coin/v19/front/state-01-vivid.png",
                            "sha256": sha256(vivid_front),
                            "bytes": vivid_front.stat().st_size,
                        }
                    )
                    replaced += 1
    if replaced != 1:
        raise ValueError(f"expected one initial petrified coin reference, replaced {replaced}")
    write_json(manifest_path, manifest)

    batch_asset_path = BATCH / "asset.json"
    batch_asset = json.loads(batch_asset_path.read_text(encoding="utf-8"))
    batch_revision = {
        "id": revision_id,
        "date": "2026-09-07",
        "change": "Pointed the owner-only petrified coin prototype at the versioned v19 eight-master set and deterministic flip derivatives. Existing magic gates remain closed.",
        "visual_approved": False,
        "runtime_validation": "pending_windows_renderer_qa",
        "runtime_approved": False,
        "runtime_use": False,
    }
    batch_asset["revision_notes"] = [
        item for item in batch_asset.get("revision_notes", []) if item.get("id") != revision_id
    ] + [batch_revision]
    write_json(batch_asset_path, batch_asset)

    asset = {
        "asset_id": "WK-MAGIC-PETRIFY-COIN-v19-candidate",
        "behavior_id": "wk.magic.petrificus_totalus",
        "asset_stage": "runtime-candidate",
        "visual_approved": False,
        "runtime_validation": "pending_windows_renderer_qa",
        "runtime_approved": False,
        "runtime_use": False,
        "prototype_use": True,
        "production_asset": False,
        "allowed_sources": ["OwnerContextMenu", "ControlPanel"],
        "canvas": {
            "width": 1024,
            "height": 1024,
            "mode": "RGBA",
            "visible_bounds": {"x": 62, "y": 70, "width": 900, "height": 882},
            "anchor": {"x": 512, "y": 952, "meaning": "bottom_center"},
        },
        "states": list(STATE_FILES),
        "runtime_png_count": len(runtime_records),
        "master_count": len(source_records),
        "derived_flip_frame_count": len(runtime_records) - len(source_records),
        "flip": {
            "frames_per_state": 9,
            "frame_duration_ms": 80,
            "widths": list(FLIP_WIDTHS),
            "back_to_front_strategy": "reverse_front_to_back",
        },
        "source": {
            "label": source_root.name,
            "external_path_not_persisted": True,
            "sha256_manifest": "SOURCE-SHA256SUMS.txt",
        },
        "runtime_checksums": "SHA256SUMS.txt",
        "validation_report": "IMPORT-VALIDATION-REPORT.json",
        "notes": "Eight owner-supplied v19 masters plus deterministic flip derivatives. Windows WPF visual QA is still required before runtime approval.",
    }
    write_json(VERSION_ROOT / "asset.json", asset)

    report = {
        "asset_id": asset["asset_id"],
        "validated_at": "2026-09-07",
        "result": "passed_static_candidate_validation",
        "source_master_count": len(source_records),
        "runtime_png_count": len(runtime_records),
        "derived_flip_frame_count": len(runtime_records) - len(source_records),
        "all_png_decode": True,
        "all_png_rgba_1024": True,
        "shared_face_alpha": True,
        "shared_visible_bounds": [62, 70, 962, 952],
        "transparent_rgb_zeroed": True,
        "visible_source_pixels_preserved": True,
        "runtime_validation": "pending_windows_renderer_qa",
        "records": runtime_records,
    }
    write_json(VERSION_ROOT / "IMPORT-VALIDATION-REPORT.json", report)

    readme = """# Petrified Coin v19 Candidate

This versioned candidate contains eight owner-supplied coin masters: front and
back faces for vivid, flat, faded, and exhausted states. It also contains four
deterministically derived nine-frame front-to-back flip sequences.

The import does not redraw, recolor, crop, or resize visible master artwork.
RGB is cleared only where alpha is exactly zero to avoid fringe during WPF
resampling. Intermediate flip frames are horizontal compressions of the matching
front/back pair using premultiplied-alpha Lanczos resampling. Frame 1 and frame 9
are exact pixel copies of the canonical front and back faces.

The active owner preview remains behind the existing magic PrototypePreview gate.
This package is not production-approved and requires Windows transparent WPF
renderer review before `runtime_approved` or `runtime_use` can change.
"""
    (VERSION_ROOT / "README.md").write_text(readme, encoding="utf-8")


def validate_outputs(runtime_paths: list[Path], faces: dict[tuple[str, str], Image.Image]) -> None:
    if len(runtime_paths) != 44 or len(set(runtime_paths)) != 44:
        raise ValueError(f"expected 44 unique runtime PNGs, found {len(runtime_paths)}")
    shared_alpha = faces[("vivid", "front")].getchannel("A").tobytes()
    for face in faces.values():
        if face.getchannel("A").tobytes() != shared_alpha:
            raise ValueError("v19 source masters do not share an exact alpha mask")
    for path in runtime_paths:
        with Image.open(path) as image:
            image.load()
            if image.mode != "RGBA" or image.size != (1024, 1024):
                raise ValueError(f"invalid runtime output: {path}")
            rgba = np.asarray(image)
            if np.any(rgba[rgba[:, :, 3] == 0, :3]):
                raise ValueError(f"hidden RGB remains in transparent pixels: {path}")
    for state, (target_name, _, _) in STATE_FILES.items():
        front = VERSION_ROOT / "front" / target_name
        back = VERSION_ROOT / "back" / target_name
        flip = VERSION_ROOT / "flip" / state / "front-to-back"
        if (flip / "frame-001.png").read_bytes() != front.read_bytes():
            with Image.open(flip / "frame-001.png") as actual, Image.open(front) as expected:
                if actual.tobytes() != expected.tobytes():
                    raise ValueError(f"flip does not start at front face: {state}")
        if (flip / "frame-009.png").read_bytes() != back.read_bytes():
            with Image.open(flip / "frame-009.png") as actual, Image.open(back) as expected:
                if actual.tobytes() != expected.tobytes():
                    raise ValueError(f"flip does not end at back face: {state}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True, help="Directory containing the eight v19 master PNGs")
    args = parser.parse_args()
    source_root = args.source.resolve()
    expected_names = sorted(name for _, front, back in STATE_FILES.values() for name in (front, back))
    actual_names = sorted(path.name for path in source_root.glob("*.png"))
    if actual_names != expected_names:
        raise ValueError(f"expected exactly the eight v19 masters; got {actual_names}")

    source_records = []
    faces: dict[tuple[str, str], Image.Image] = {}
    runtime_paths: list[Path] = []
    master_review_frames: list[Image.Image] = []
    flip_review_frames: list[Image.Image] = []
    for state, (target_name, front_name, back_name) in STATE_FILES.items():
        for side, source_name in (("front", front_name), ("back", back_name)):
            source = source_root / source_name
            source_records.append(
                {"state": state, "side": side, "source_file": source_name, "bytes": source.stat().st_size, "sha256": sha256(source)}
            )
            face = canonical_face(source)
            target = VERSION_ROOT / side / target_name
            save_png(face, target)
            faces[(state, side)] = face
            runtime_paths.append(target)
            master_review_frames.append(face)
        flip_paths = build_flip(faces[(state, "front")], faces[(state, "back")], state)
        runtime_paths.extend(flip_paths)
        for path in flip_paths:
            with Image.open(path) as image:
                flip_review_frames.append(image.convert("RGBA"))

    validate_outputs(runtime_paths, faces)
    master_preview = VERSION_ROOT / "previews/coin-v19-masters-light-dark.gif"
    flip_preview = VERSION_ROOT / "previews/coin-v19-flips-light-dark.gif"
    build_review_gif(master_review_frames, master_preview, 700)
    build_review_gif(flip_review_frames, flip_preview, 80)
    update_manifests(source_root, source_records, runtime_paths, [master_preview, flip_preview])
    print(f"imported 8 v19 masters and generated 36 flip frames under {VERSION_ROOT}")


if __name__ == "__main__":
    main()
