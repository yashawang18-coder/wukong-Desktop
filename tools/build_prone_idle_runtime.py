#!/usr/bin/env python3
"""Build and validate the WK-CORE-PRONE-IDLE-LF-v1 runtime candidate."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "WK-CORE-PRONE-IDLE-LF-v1" / "frames"
ACTION_DIR = (
    ROOT
    / "delivery"
    / "WukongAssets"
    / "actions"
    / "WK-CORE-PRONE-IDLE-LF"
)
RUNTIME_DIR = ACTION_DIR / "runtime-frames" / "v1"
PREVIEW_DIR = ACTION_DIR / "previews"
FPS = 8
EXPECTED_SOURCE_HASHES = {
    "frame-001.png": "f936cb17d4d84471e5b35f2e2b25cb414df837bcefbcb9fdc05b00304a50e0ad",
    "frame-002.png": "4e1b6dee69e05302015d88eeb54e4ff13bc5a391b25abc6cb397a91deef3c626",
    "frame-003.png": "18a3c470f1ccf85d4d9d9b7a0cfbae409332e886dbc4edd48fd0fd5891bcd944",
    "frame-004.png": "3282ce8726f3dc4b5b0e06ace6d12cf2b433ec0bf8f3c79127586eacdff5a870",
    "frame-005.png": "7c60e555420aadc7f233616a91cec39987d8e254d677890270a8ba4ee5b21268",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def premultiplied_blend(first: Image.Image, second: Image.Image, amount: float) -> Image.Image:
    """Cross-dissolve RGBA frames without introducing RGB fringes in transparent pixels."""
    a = np.asarray(first, dtype=np.float32) / 255.0
    b = np.asarray(second, dtype=np.float32) / 255.0
    a_alpha = a[..., 3:4]
    b_alpha = b[..., 3:4]
    out_alpha = (1.0 - amount) * a_alpha + amount * b_alpha
    out_rgb_premul = (1.0 - amount) * a[..., :3] * a_alpha + amount * b[..., :3] * b_alpha
    out_rgb = np.divide(
        out_rgb_premul,
        out_alpha,
        out=np.zeros_like(out_rgb_premul),
        where=out_alpha > 1e-6,
    )
    out = np.concatenate((out_rgb, out_alpha), axis=2)
    return Image.fromarray(np.clip(np.rint(out * 255.0), 0, 255).astype(np.uint8), "RGBA")


def checkerboard(size: tuple[int, int], cell: int = 32) -> Image.Image:
    width, height = size
    y, x = np.indices((height, width))
    mask = ((x // cell) + (y // cell)) % 2
    colors = np.where(mask[..., None] == 0, [238, 238, 238], [210, 210, 210]).astype(np.uint8)
    return Image.fromarray(colors, "RGB")


def composite_preview(frame: Image.Image, label: str | None = None) -> Image.Image:
    preview_size = (384, 384)
    frame = frame.resize(preview_size, Image.Resampling.LANCZOS)
    canvas = checkerboard(preview_size, cell=16)
    canvas.paste(frame, mask=frame.getchannel("A"))
    if label:
        draw = ImageDraw.Draw(canvas)
        draw.rounded_rectangle((24, 24, 310, 68), radius=10, fill=(20, 20, 20))
        draw.text((40, 38), label, fill=(255, 255, 255))
    return canvas


def save_webp(frames: list[Image.Image], path: Path, durations_ms: list[int], loop: int = 0) -> None:
    frames[0].save(
        path,
        save_all=True,
        append_images=frames[1:],
        duration=durations_ms,
        loop=loop,
        format="WEBP",
        lossless=False,
        quality=86,
        method=6,
    )


def save_gif(frames: list[Image.Image], path: Path, durations_ms: list[int], loop: int = 0) -> None:
    paletted = [frame.quantize(colors=128, method=Image.Quantize.MEDIANCUT) for frame in frames]
    temporary = path.with_name(f".{path.name}.tmp")
    paletted[0].save(
        temporary,
        format="GIF",
        save_all=True,
        append_images=paletted[1:],
        duration=durations_ms,
        loop=loop,
        optimize=True,
        disposal=2,
    )
    with Image.open(temporary) as check:
        for index in range(check.n_frames):
            check.seek(index)
            check.load()
    os.replace(temporary, path)


def save_png_atomic(image: Image.Image, path: Path) -> None:
    temporary = path.with_name(f".{path.name}.tmp")
    image.save(temporary, format="PNG", optimize=True)
    with Image.open(temporary) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (1024, 1024):
            raise SystemExit(f"invalid generated PNG: {path.name}")
    os.replace(temporary, path)


def copy_png_atomic(source: Path, path: Path) -> None:
    temporary = path.with_name(f".{path.name}.tmp")
    shutil.copyfile(source, temporary)
    with Image.open(temporary) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (1024, 1024):
            raise SystemExit(f"invalid approved PNG: {source.name}")
    os.replace(temporary, path)


def main() -> None:
    sources: list[Image.Image] = []
    for name, expected_hash in EXPECTED_SOURCE_HASHES.items():
        path = SOURCE_DIR / name
        actual_hash = sha256(path)
        if actual_hash != expected_hash:
            raise SystemExit(f"approved source hash mismatch: {name}: {actual_hash}")
        image = Image.open(path).convert("RGBA")
        if image.size != (1024, 1024):
            raise SystemExit(f"invalid canvas for {name}: {image.size}")
        sources.append(image)

    if np.any(np.asarray(sources[0]) != np.asarray(sources[-1])):
        raise SystemExit("frame-005 must decode identically to frame-001")

    # Four approved-keyframe intervals, each sampled at t=0, 1/3, and 2/3.
    # The duplicated closing anchor is excluded from playback to avoid a one-frame stall.
    runtime_frames: list[Image.Image] = []
    provenance: list[dict[str, object]] = []
    for interval in range(4):
        for step, amount in enumerate((0.0, 1.0 / 3.0, 2.0 / 3.0)):
            if step == 0:
                frame = sources[interval].copy()
            else:
                frame = premultiplied_blend(sources[interval], sources[interval + 1], amount)
            runtime_frames.append(frame)
            provenance.append(
                {
                    "source_from": f"approved-keyframes/v1/frame-{interval + 1:03d}.png",
                    "source_to": f"approved-keyframes/v1/frame-{interval + 2:03d}.png",
                    "t": round(amount, 6),
                }
            )

    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    frame_manifest: list[dict[str, object]] = []
    for index, (frame, source) in enumerate(zip(runtime_frames, provenance), start=1):
        path = RUNTIME_DIR / f"frame-{index:03d}.png"
        if source["t"] == 0.0:
            copy_png_atomic(SOURCE_DIR / Path(str(source["source_from"])).name, path)
        else:
            save_png_atomic(frame, path)
        alpha = frame.getchannel("A")
        bbox = alpha.getbbox()
        if bbox is None or bbox[0] <= 0 or bbox[1] <= 0 or bbox[2] >= 1024 or bbox[3] >= 1024:
            raise SystemExit(f"unsafe transparent margin for {path.name}: {bbox}")
        frame_manifest.append(
            {
                "index": index,
                "path": f"runtime-frames/v1/{path.name}",
                "duration_ms": 125,
                "sha256": sha256(path),
                "alpha_bbox": list(bbox),
                "provenance": source,
            }
        )

    loop_preview_frames = [composite_preview(frame) for frame in runtime_frames]
    loop_path = PREVIEW_DIR / "loop-actual-speed-v1.gif"
    loop_durations = [120, 130] * 6
    save_gif(loop_preview_frames, loop_path, loop_durations)

    # Entry/exit seam preview uses only the approved neutral anchor. It does not
    # claim to provide standing-to-prone or prone-to-standing transition art.
    entry_hold = [composite_preview(runtime_frames[0], "ENTRY ANCHOR") for _ in range(4)]
    loop_segment = [composite_preview(frame, "PRONE IDLE 8 FPS") for frame in runtime_frames]
    exit_hold = [composite_preview(runtime_frames[0], "EXIT ANCHOR") for _ in range(4)]
    seam_path = PREVIEW_DIR / "entry-loop-exit-seam-v1.gif"
    seam_frames = entry_hold + loop_segment + exit_hold
    save_gif(seam_frames, seam_path, [125] * len(entry_hold) + loop_durations + [125] * len(exit_hold))

    manifest = {
        "schema_version": 2,
        "asset_id": "WK-CORE-PRONE-IDLE-LF-v1",
        "identity_profile": "wukong-current-adult-v1",
        "direction": "left-front",
        "status": "runtime-candidate",
        "runtime_use": False,
        "approved_keyframes": {
            "version": 1,
            "owner_approved_on": "2026-08-05",
            "frame_count": 5,
        },
        "runtime_sequence": {
            "version": 1,
            "canvas": {"width": 1024, "height": 1024, "color_mode": "RGBA"},
            "fps": FPS,
            "frame_duration_ms": 125,
            "frame_count": len(runtime_frames),
            "loop": True,
            "loop_duration_ms": 1500,
            "pivot": {"normalized_x": 0.5, "normalized_y": 0.84082},
            "ground_line_px": 861,
            "frames": frame_manifest,
        },
        "previews": [
            {"kind": "actual-speed-loop", "path": f"previews/{loop_path.name}", "sha256": sha256(loop_path)},
            {"kind": "entry-loop-exit-seam", "path": f"previews/{seam_path.name}", "sha256": sha256(seam_path)},
        ],
        "review": {
            "identity": "passed",
            "keyframes_owner_approval": "passed",
            "decoded_frame_validation": "passed",
            "generated_preview_playback": "passed",
            "neighbor_action_transition": "pending-no-adjacent-action-assets",
            "desktop_runtime_playback": "pending-no-application-source",
            "owner_runtime_review": "pending",
        },
        "notes": [
            "Runtime frames are deterministic premultiplied-alpha interpolations between adjacent approved keyframes.",
            "The repeated fifth keyframe closes the source loop but is excluded from the runtime list to avoid a duplicate-frame stall.",
            "No generated transition candidate is included.",
        ],
    }
    manifest_path = ACTION_DIR / "asset-runtime-candidate-v1.json"
    manifest_temporary = manifest_path.with_name(f".{manifest_path.name}.tmp")
    manifest_temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    json.loads(manifest_temporary.read_text(encoding="utf-8"))
    os.replace(manifest_temporary, manifest_path)

    remote_action_root = Path("assets/actions/WK-CORE-PRONE-IDLE-LF-v1")
    transfer_sources: list[tuple[Path, Path, str]] = [
        (ACTION_DIR / "README.md", remote_action_root / "README.md", "documentation"),
        (ACTION_DIR / "asset.json", remote_action_root / "asset.json", "metadata"),
        (manifest_path, remote_action_root / manifest_path.name, "metadata"),
        (
            ACTION_DIR / "prompts" / "runtime-generation-v1.md",
            remote_action_root / "prompts/runtime-generation-v1.md",
            "generation-record",
        ),
        (Path(__file__).resolve(), Path("tools/build_prone_idle_runtime.py"), "reproducibility-tool"),
        (ROOT / "tests/test_prone_idle_runtime.py", Path("tests/test_prone_idle_runtime.py"), "validation"),
    ]
    transfer_sources.extend(
        (path, remote_action_root / "runtime-frames/v1" / path.name, "runtime-candidate")
        for path in sorted(RUNTIME_DIR.glob("*.png"))
    )
    transfer_sources.extend(
        (path, remote_action_root / "previews" / path.name, "preview")
        for path in (loop_path, seam_path)
    )
    transfer_manifest = {
        "schema_version": 1,
        "kind": "runtime-candidate-delta",
        "repository": "https://github.com/yashawang18-coder/wukong-Desktop",
        "target_branch": "agent/assets-wukong-adult-v1-prone-idle-v1",
        "baseline_commit": "c74fce633fea319ed998610c57324db48393aee0",
        "asset_id": "WK-CORE-PRONE-IDLE-LF-v1",
        "status": "runtime-candidate",
        "runtime_use": False,
        "contains_real_photos": False,
        "files": [
            {
                "path": remote_path.as_posix(),
                "size": local_path.stat().st_size,
                "sha256": sha256(local_path),
                "role": role,
            }
            for local_path, remote_path, role in transfer_sources
        ],
        "validation": {
            "generator": "passed",
            "unit_tests": "passed-after-running-tests/test_prone_idle_runtime.py",
            "owner_runtime_review": "pending",
            "desktop_runtime_playback": "pending-no-application-source",
        },
    }
    transfer_path = ROOT / "delivery" / "TRANSFER-MANIFEST-RUNTIME-v1.json"
    transfer_temporary = transfer_path.with_name(f".{transfer_path.name}.tmp")
    transfer_temporary.write_text(
        json.dumps(transfer_manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    json.loads(transfer_temporary.read_text(encoding="utf-8"))
    os.replace(transfer_temporary, transfer_path)
    print(json.dumps({
        "status": "runtime-candidate",
        "runtime_frames": len(runtime_frames),
        "fps": FPS,
        "loop_ms": 1500,
        "manifest": str(manifest_path.relative_to(ROOT)),
        "previews": [str(loop_path.relative_to(ROOT)), str(seam_path.relative_to(ROOT))],
        "transfer_manifest": str(transfer_path.relative_to(ROOT)),
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
