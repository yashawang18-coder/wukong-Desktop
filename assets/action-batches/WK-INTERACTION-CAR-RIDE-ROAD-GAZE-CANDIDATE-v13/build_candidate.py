from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent
V8 = ROOT.parent / "WK-INTERACTION-CAR-RIDE-CANDIDATE-v8"
CANVAS_SIZE = (1024, 1024)
TARGET_CENTER_X = 512
TARGET_BASELINE_Y = 900
GENERATED_GLOBAL_SCALE = 0.8135

FRAME_DURATIONS_MS = [
    140, 110, 110, 110, 110, 110, 180, 220, 260,
    260, 220, 180, 110, 110, 110, 110, 140, 180,
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def alpha_bbox(path: Path) -> tuple[int, int, int, int]:
    with Image.open(path) as image:
        rgba = image.convert("RGBA")
        bbox = rgba.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError(f"No visible pixels: {path}")
    return bbox


def normalize_generated(source: Path, destination: Path) -> None:
    with Image.open(source) as image:
        rgba = image.convert("RGBA")
        size = (
            round(rgba.width * GENERATED_GLOBAL_SCALE),
            round(rgba.height * GENERATED_GLOBAL_SCALE),
        )
        scaled = rgba.resize(size, Image.Resampling.LANCZOS)
        bbox = scaled.getchannel("A").getbbox()
        if bbox is None:
            raise ValueError(f"No visible pixels after scaling: {source}")

        visible_center_x = (bbox[0] + bbox[2]) / 2
        offset_x = round(TARGET_CENTER_X - visible_center_x)
        offset_y = (TARGET_BASELINE_Y + 1) - bbox[3]
        canvas = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
        canvas.alpha_composite(scaled, (offset_x, offset_y))
        destination.parent.mkdir(parents=True, exist_ok=True)
        canvas.save(destination, format="PNG", optimize=False)


def copy_master(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)


def build_preview(frame_paths: list[Path], durations: list[int], destination: Path) -> None:
    preview_frames: list[Image.Image] = []
    for path in frame_paths:
        with Image.open(path) as image:
            rgba = image.convert("RGBA")
            background = Image.new("RGBA", rgba.size, (44, 48, 54, 255))
            background.alpha_composite(rgba)
            preview_frames.append(
                background.convert("RGB").resize((384, 384), Image.Resampling.LANCZOS)
            )
    destination.parent.mkdir(parents=True, exist_ok=True)
    preview_frames[0].save(
        destination,
        save_all=True,
        append_images=preview_frames[1:],
        duration=durations,
        loop=0,
        optimize=False,
    )
    for frame in preview_frames:
        frame.close()


def build_contact_sheet(master_paths: list[Path], destination: Path) -> None:
    tile_size = 256
    sheet = Image.new("RGB", (tile_size * 4, tile_size * 2), (44, 48, 54))
    draw = ImageDraw.Draw(sheet)
    for index, path in enumerate(master_paths):
        with Image.open(path) as image:
            rgba = image.convert("RGBA").resize((tile_size, tile_size), Image.Resampling.LANCZOS)
        tile = Image.new("RGBA", (tile_size, tile_size), (44, 48, 54, 255))
        tile.alpha_composite(rgba)
        x = (index % 4) * tile_size
        y = (index // 4) * tile_size
        sheet.paste(tile.convert("RGB"), (x, y))
        draw.rectangle((x + 5, y + 5, x + 176, y + 24), fill=(22, 24, 28))
        draw.text((x + 9, y + 8), path.stem, fill=(245, 245, 245))
    destination.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(destination, format="PNG", optimize=False)


def frame_entry(path: Path, duration_ms: int, pose: str) -> dict[str, object]:
    bbox = alpha_bbox(path)
    return {
        "path": path.relative_to(ROOT).as_posix(),
        "duration_ms": duration_ms,
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
        "head_pose": pose,
        "alpha_bbox": list(bbox),
    }


def main() -> None:
    masters = ROOT / "masters"
    frames_root = ROOT / "frames"
    previews = ROOT / "previews"

    copy_master(V8 / "master/directions/right.png", masters / "right-original.png")
    copy_master(V8 / "master/directions/left.png", masters / "left-original.png")
    copy_master(V8 / "master/expressions/side-glance.png", masters / "right-slight.png")

    selected_generated = {
        "right-half": ROOT / "raw-alpha/right-half.png",
        "right-profile": ROOT / "raw-alpha/right-profile.png",
        "left-slight": ROOT / "raw-alpha/left-slight.png",
        "left-half": ROOT / "raw-alpha/left-half.png",
        "left-profile": ROOT / "raw-alpha/left-profile-v3.png",
    }
    for name, source in selected_generated.items():
        normalize_generated(source, masters / f"{name}.png")

    right_pattern = [
        "right-original", "right-original", "right-slight", "right-slight",
        "right-half", "right-half", "right-profile", "right-profile",
        "right-profile", "right-profile", "right-profile", "right-profile",
        "right-half", "right-half", "right-slight", "right-slight",
        "right-original", "right-original",
    ]
    left_pattern = [
        "left-original", "left-original", "left-slight", "left-slight",
        "left-half", "left-half", "left-profile", "left-profile",
        "left-profile", "left-profile", "left-profile", "left-profile",
        "left-half", "left-half", "left-slight", "left-slight",
        "left-original", "left-original",
    ]

    sequences: dict[str, list[dict[str, object]]] = {}
    for direction, pattern in (("right", right_pattern), ("left", left_pattern)):
        output_dir = frames_root / f"road-gaze-{direction}"
        output_dir.mkdir(parents=True, exist_ok=True)
        frame_paths: list[Path] = []
        entries: list[dict[str, object]] = []
        for index, (master_name, duration) in enumerate(
            zip(pattern, FRAME_DURATIONS_MS, strict=True), start=1
        ):
            destination = output_dir / f"frame-{index:03d}.png"
            copy_master(masters / f"{master_name}.png", destination)
            pose = master_name.removeprefix(f"{direction}-")
            frame_paths.append(destination)
            entries.append(frame_entry(destination, duration, pose))
        sequences[f"road-gaze/{direction}"] = entries
        build_preview(frame_paths, FRAME_DURATIONS_MS, previews / f"road-gaze-{direction}.gif")

    build_contact_sheet(
        [
            masters / "right-original.png",
            masters / "right-slight.png",
            masters / "right-half.png",
            masters / "right-profile.png",
            masters / "left-original.png",
            masters / "left-slight.png",
            masters / "left-half.png",
            masters / "left-profile.png",
        ],
        previews / "master-contact-sheet.png",
    )

    manifest = {
        "schema_version": 1,
        "asset_id": "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v13",
        "parent_asset": "WK-INTERACTION-CAR-RIDE-CANDIDATE-v8",
        "status": "runtime_candidate_owner_visual_qa_pending",
        "runtime_validation": "pending_owner_windows_renderer_qa",
        "visual_approved": False,
        "owner_runtime_enable_requested": False,
        "runtime_approved": False,
        "runtime_use": False,
        "prototype_use": True,
        "production_asset": False,
        "developer_preview": True,
        "behavior_id": "wk.interaction.car_ride",
        "extension_role": "optional_side_cruise_road_gaze",
        "generation_workflow": {
            "method": "whole_scene_reference_conditioned_generation",
            "generation_surface": "Codex built-in image_gen",
            "backend_model": "not_exposed_by_tool",
            "seed": None,
            "source_background": "model-generated blue chroma source",
            "alpha_method": "deterministic chroma removal with despill",
            "normalization": "single global scale plus whole-frame translation",
            "head_or_neck_compositing": False,
            "runtime_interpolation": False,
            "runtime_mirroring": False,
        },
        "pixel_policy": {
            "generation_strategy": "complete dog, harness, and car scene generation",
            "local_region_composite_used": False,
            "head_only_edit_used": False,
            "runtime_mirror_used": False,
        },
        "canvas": {
            "width": 1024,
            "height": 1024,
            "format": "PNG",
            "mode": "RGBA",
            "wheel_baseline_y": TARGET_BASELINE_Y,
            "generated_global_scale": GENERATED_GLOBAL_SCALE,
        },
        "trigger_contract": {
            "eligible_directions": ["left", "right"],
            "min_segment_ms": 1800,
            "max_events_per_ride": 2,
            "cooldown_ms": [6000, 12000],
            "forbidden_during": ["turn", "brake", "offscreen_exit", "offscreen_reentry"],
        },
        "known_candidate_limits": [
            "backend image checkpoint and reusable seed are not exposed",
            "generated whole-scene masters are visually close but not byte-identical to v8",
            "wheel phase is static during the short road-gaze event",
            "Windows owner visual QA is required before any runtime promotion",
        ],
        "sequences": sequences,
    }
    (ROOT / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    asset = {
        key: manifest[key]
        for key in (
            "asset_id", "parent_asset", "status", "runtime_validation",
            "visual_approved", "owner_runtime_enable_requested",
            "runtime_approved", "runtime_use",
            "prototype_use", "production_asset", "developer_preview",
        )
    }
    asset["whole_scene_generation"] = True
    asset["selected_master_count"] = 8
    asset["runtime_frame_count"] = 36
    (ROOT / "asset.json").write_text(
        json.dumps(asset, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    qa = {
        "asset_id": manifest["asset_id"],
        "canvas": list(CANVAS_SIZE),
        "mode": "RGBA",
        "frame_count": sum(len(items) for items in sequences.values()),
        "all_frames_decoded": True,
        "all_frames_have_alpha": True,
        "wheel_baseline_y": TARGET_BASELINE_Y,
        "selected_masters": {
            path.name: {
                "sha256": sha256(path),
                "alpha_bbox": list(alpha_bbox(path)),
            }
            for path in sorted(masters.glob("*.png"))
        },
        "owner_visual_qa": "pending",
    }
    (ROOT / "IMPORT-VALIDATION-REPORT.json").write_text(
        json.dumps(qa, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    readme = """# WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v13

Whole-scene road-gaze candidate for the approved v8 car ride.

## Workflow

- The approved v8 left/right complete scenes are immutable identity and geometry anchors.
- New turn poses were generated as complete dog + harness + car scenes.
- No head/neck patch, pasted head, runtime mirroring, crossfade, or AI interpolation is used.
- Generated blue-screen sources are retained under `source-generated/`.
- Deterministic chroma removal creates `raw-alpha/`; one fixed global scale and whole-frame translation produce 1024x1024 RGBA masters with wheel baseline y=900.
- Eighteen-frame left and right review sequences are assembled from complete-scene masters.

## Recovered v8 facts

- The v8 package contains approved direction/expression masters plus deterministic `build_transitions.py` processing.
- The original image backend checkpoint, sampler controls, generation seed, and fixed consistency adapter values are not present in the PNG metadata or package.
- They must not be invented. This candidate records the actual tool surface and uses explicit reference locks instead.

## Gate

This package is developer/PrototypePreview review material only:

- `visual_approved=false`
- `runtime_validation=pending_owner_windows_renderer_qa`
- `runtime_approved=false`
- `runtime_use=false`
- `prototype_use=true`
- `production_asset=false`

It must not replace v8, enter Normal runtime, AutonomousTick, Dialogue, model routing, or owner command routing before Windows owner visual QA.
"""
    (ROOT / "README.md").write_text(readme, encoding="utf-8")

    checksum_paths = [
        path for path in ROOT.rglob("*")
        if path.is_file() and path.name != "SHA256SUMS.txt"
    ]
    lines = [f"{sha256(path)}  {path.relative_to(ROOT).as_posix()}" for path in sorted(checksum_paths)]
    (ROOT / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
