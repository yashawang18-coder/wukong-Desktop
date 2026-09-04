from __future__ import annotations

import argparse
import hashlib
import io
import json
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image


BATCH_ID = "WK-AUTONOMOUS-SLEEP-RUNTIME-FINAL-CANDIDATE-v10"
SOURCE_ZIP_NAME = "wukong-sleep-runtime-final-transparent-v10.zip"
SOURCE_ZIP_SHA256 = "174350b0aaa7d01a6639d8ce189fb7a12d3541e5dc5ce4460b1461d8f0d1c701"
CANVAS_SIZE = (1024, 1024)
RUNTIME_RENDER_SCALE = 0.92


@dataclass(frozen=True)
class SequenceSpec:
    source_name: str
    behavior_id: str
    display_name: str
    durations_ms: tuple[int, ...]
    phase: str
    loop: bool
    from_pose: str
    to_pose: str
    direction: str
    entry_policy: str


MAIN_DURATIONS = (260,) * 15 + (1100,)
ROLL_DURATIONS = (260,) * 7 + (800,)
LOOP_DURATIONS = (650,) * 4
SEQUENCES = (
    SequenceSpec("01-main-sleep-lifecycle", "wk.candidate.sleep.main_lifecycle_v2", "Sleep lifecycle v10 review", MAIN_DURATIONS, "intro", False, "prone.awake.left_front", "sleep.side.stable", "front-prone-to-side-sleep", "developer review only; current runtime prone bridge requires Windows QA"),
    SequenceSpec("02-prone-to-side-roll", "wk.candidate.sleep.prone_to_side_roll_v2", "Prone-to-side roll v10 review", ROLL_DURATIONS, "intro", False, "sleep.prone.low_head", "sleep.side.stable", "prone-to-side", "developer review only; never append after the complete main lifecycle"),
    SequenceSpec("03-sprawled-front-breath", "wk.candidate.sleep.sprawled_front_breath_v2", "Sprawled front breathing v10 review", LOOP_DURATIONS, "loop", True, "sleep.prone.sprawled.front", "sleep.prone.sprawled.front", "front", "developer review only; compatible posture entry required"),
    SequenceSpec("04-sprawled-left-side-breath", "wk.candidate.sleep.sprawled_left_side_breath_v2", "Sprawled left-side breathing v10 review", LOOP_DURATIONS, "loop", True, "sleep.side.sprawled.left", "sleep.side.sprawled.left", "left-side", "developer review only; compatible posture entry required"),
    SequenceSpec("05-sprawled-right-side-breath", "wk.candidate.sleep.sprawled_right_side_breath_v2", "Sprawled right-side breathing v10 review", LOOP_DURATIONS, "loop", True, "sleep.side.sprawled.right", "sleep.side.sprawled.right", "right-side", "developer review only; compatible posture entry required"),
    SequenceSpec("06-compact-prone-breath", "wk.candidate.sleep.compact_prone_breath_v2", "Compact prone breathing v10 review", LOOP_DURATIONS, "loop", True, "sleep.prone.compact", "sleep.prone.compact", "front-three-quarter", "developer review only; compatible posture entry required"),
    SequenceSpec("07-curled-side-breath", "wk.candidate.sleep.curled_side_breath_v2", "Curled side breathing v10 review", LOOP_DURATIONS, "loop", True, "sleep.side.curled", "sleep.side.curled", "side-curled", "developer review only; compatible posture entry required"),
    SequenceSpec("08-top-down-prone-breath", "wk.candidate.sleep.top_down_prone_breath_v2", "Top-down prone breathing v10 review", LOOP_DURATIONS, "loop", True, "sleep.prone.top_down", "sleep.prone.top_down", "top-down", "developer review only; runtime entry requires offscreen re-entry or an approved bridge"),
)


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def frame_metrics(payload: bytes) -> dict[str, object]:
    with Image.open(io.BytesIO(payload)) as image:
        image.load()
        if image.format != "PNG" or image.mode != "RGBA" or image.size != CANVAS_SIZE:
            raise ValueError(f"invalid frame: format={image.format} mode={image.mode} size={image.size}")
        alpha = image.getchannel("A")
        bounds = alpha.getbbox()
        if bounds is None:
            raise ValueError("frame has no visible pixels")
        if alpha.getextrema()[0] != 0:
            raise ValueError("frame has no transparent pixels")
        edge_alpha = max(
            alpha.crop((0, 0, 1024, 1)).getextrema()[1],
            alpha.crop((0, 1023, 1024, 1024)).getextrema()[1],
            alpha.crop((0, 0, 1, 1024)).getextrema()[1],
            alpha.crop((1023, 0, 1024, 1024)).getextrema()[1],
        )
        blue_pixels = sum(
            1
            for red, green, blue, opacity in image.get_flattened_data()
            if opacity > 32 and blue > 120 and blue > red * 1.2 and blue > green * 1.12
        )
        if edge_alpha != 0:
            raise ValueError(f"frame canvas edge is not transparent: max alpha={edge_alpha}")
        if blue_pixels > 32:
            raise ValueError(f"frame contains possible blue carrier pixels: {blue_pixels}")
        left, top, right, bottom = bounds
        return {
            "width": image.width,
            "height": image.height,
            "mode": image.mode,
            "alpha_bbox": [left, top, right, bottom],
            "visible_width": right - left,
            "visible_height": bottom - top,
            "visible_center_x": round((left + right - 1) / 2, 3),
            "visible_center_y": round((top + bottom - 1) / 2, 3),
            "ground_baseline_y": bottom - 1,
            "canvas_edges_transparent": True,
            "possible_blue_carrier_pixels": blue_pixels,
        }


def import_package(zip_path: Path, repository_root: Path) -> Path:
    actual_zip_sha = sha256_file(zip_path)
    if actual_zip_sha != SOURCE_ZIP_SHA256:
        raise ValueError(f"source ZIP SHA-256 mismatch: {actual_zip_sha}")

    batch_root = repository_root / "assets" / "action-batches" / BATCH_ID
    if batch_root.exists():
        raise FileExistsError(f"refusing to overwrite existing candidate directory: {batch_root}")

    expected_entries = {
        f"{spec.source_name}/F{index:02d}.png"
        for spec in SEQUENCES
        for index in range(1, len(spec.durations_ms) + 1)
    }
    frame_inventory: list[dict[str, object]] = []
    actions: list[dict[str, object]] = []
    source_hash_lines: list[str] = []

    with zipfile.ZipFile(zip_path) as archive:
        bad_entry = archive.testzip()
        if bad_entry is not None:
            raise ValueError(f"source ZIP integrity failure: {bad_entry}")
        source_entries = {name for name in archive.namelist() if not name.endswith("/")}
        if source_entries != expected_entries:
            missing = sorted(expected_entries - source_entries)
            unexpected = sorted(source_entries - expected_entries)
            raise ValueError(f"source ZIP structure mismatch; missing={missing}, unexpected={unexpected}")

        batch_root.mkdir(parents=True)
        for spec in SEQUENCES:
            phase_frames: list[dict[str, object]] = []
            for index, duration_ms in enumerate(spec.durations_ms, start=1):
                source_entry = f"{spec.source_name}/F{index:02d}.png"
                payload = archive.read(source_entry)
                metrics = frame_metrics(payload)
                relative = f"frames/{source_entry}"
                destination = batch_root / Path(relative)
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(payload)
                digest = sha256_bytes(payload)
                source_hash_lines.append(f"{digest}  {relative}")
                frame_record = {
                    "path": relative,
                    "source_entry": source_entry,
                    "source_sha256": digest,
                    "bytes": len(payload),
                    "sha256": digest,
                    **metrics,
                }
                frame_inventory.append(frame_record)
                phase_frames.append({"path": relative, "duration_ms": duration_ms, "bytes": len(payload), "sha256": digest})

            actions.append({
                "behavior_id": spec.behavior_id,
                "asset_version": 10,
                "display_name": spec.display_name,
                "description": "Byte-preserved v10 transparent PNGs for isolated Windows renderer review.",
                "from_pose": spec.from_pose,
                "to_pose": spec.to_pose,
                "direction": spec.direction,
                "entry_policy": spec.entry_policy,
                "frame_count": len(spec.durations_ms),
                "total_duration_ms": sum(spec.durations_ms),
                "interruptible": True,
                "loop": spec.loop,
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
                "phases": [{"name": spec.phase, "loop": spec.loop, "frame_count": len(spec.durations_ms), "frames": phase_frames}],
            })

    if len(frame_inventory) != 48:
        raise ValueError("runtime frame inventory must contain exactly 48 PNGs")

    runtime_anchor = repository_root / "assets" / "action-batches" / "WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2" / "frames" / "microloops" / "prone-idle" / "01.png"
    with Image.open(runtime_anchor) as image:
        runtime_bounds = image.getchannel("A").getbbox()
    first = frame_inventory[0]
    comparison = {
        "runtime_anchor_path": runtime_anchor.relative_to(repository_root).as_posix(),
        "runtime_anchor_sha256": sha256_file(runtime_anchor),
        "runtime_alpha_bbox": list(runtime_bounds) if runtime_bounds else None,
        "candidate_f01_alpha_bbox": first["alpha_bbox"],
        "candidate_f01_bytes_preserved": True,
        "runtime_render_scale": RUNTIME_RENDER_SCALE,
        "scaled_visible_height_px": round(first["visible_height"] * RUNTIME_RENDER_SCALE, 3),
        "runtime_visible_height_px": runtime_bounds[3] - runtime_bounds[1] if runtime_bounds else None,
        "windows_transition_review": "pending",
    }
    common = {
        "schema_version": 1,
        "asset_id": BATCH_ID,
        "asset_version": 10,
        "asset_stage": "runtime-candidate",
        "source_package": SOURCE_ZIP_NAME,
        "source_zip_sha256": SOURCE_ZIP_SHA256,
        "source_frame_count": 48,
        "runtime_frame_count": 48,
        "sequence_count": 8,
        "source_png_byte_identity": True,
        "owner_preview_approved": False,
        "owner_material_visual_confirmed": False,
        "visual_approved": False,
        "automated_validation": "passed_import_integrity",
        "runtime_validation": "pending_owner_windows_renderer_qa",
        "runtime_approved": False,
        "runtime_use": False,
        "production_asset": False,
        "prototype_use": False,
        "developer_preview": True,
        "autonomous_binding_enabled": False,
        "normal_runtime_available": False,
        "allowed_sources": ["DeveloperPreview"],
        "runtime_render_scale": RUNTIME_RENDER_SCALE,
        "current_runtime_prone_bridge": comparison,
    }
    manifest = {
        **common,
        "batch_id": BATCH_ID,
        "candidate_profile": "sleep-runtime-final-v10-owner-review-pending",
        "visual_approval_scope": "not yet approved; v10 source frames are awaiting owner Windows review",
        "timing_source": "existing stable sleep preview semantics; source v10 ZIP contains PNG files only",
        "sequence_rules": {
            "main_lifecycle_includes_roll": True,
            "append_prone_to_side_roll_after_main": False,
            "each_sleep_entry_selects_one_compatible_view": True,
            "special_view_loops_require_compatible_pose_or_offscreen_reentry": True,
            "hard_cut_between_incompatible_views": False,
            "reverse_main_as_wake": False,
            "approved_wake_sequence_available": False,
            "missing_wake_result": "Deferred_or_MissingAsset",
            "legacy_sleep_visual_fallback_allowed": False,
        },
        "frame_inventory": frame_inventory,
        "actions": actions,
    }
    asset = {
        **common,
        "runtime_frame_format": "1024x1024 RGBA PNG copied byte-for-byte from the supplied v10 ZIP",
        "missing_content": ["approved wake exit", "approved interrupt exit", "Windows transparent-renderer continuity approval"],
        "notes": "v10 replaces the local unapproved v5 preview. Only the eight supplied sequences are registered.",
    }
    report = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_zip": SOURCE_ZIP_NAME,
        "source_zip_sha256": SOURCE_ZIP_SHA256,
        "zip_integrity": "passed",
        "source_frame_count": 48,
        "runtime_frame_count": 48,
        "sequence_count": 8,
        "all_png_decode": True,
        "all_png_1024_rgba": True,
        "all_frames_have_transparency": True,
        "all_canvas_edges_transparent": True,
        "maximum_possible_blue_carrier_pixels": max(item["possible_blue_carrier_pixels"] for item in frame_inventory),
        "source_png_byte_identity": True,
        "runtime_prone_anchor_comparison": comparison,
        "windows_renderer_qa": "pending",
    }

    write_json(batch_root / "asset.json", asset)
    write_json(batch_root / "manifest.json", manifest)
    write_json(batch_root / "IMPORT-VALIDATION-REPORT.json", report)
    (batch_root / "SOURCE-FRAME-SHA256SUMS.sha256").write_text("\n".join(source_hash_lines) + "\n", encoding="ascii", newline="\n")
    (batch_root / "README.md").write_text(
        "# Wukong sleep runtime final v10 candidate\n\n"
        "This candidate contains only the 48 transparent PNG files supplied in `wukong-sleep-runtime-final-transparent-v10.zip`. Files are copied byte-for-byte; no frame is generated, recolored, resized, cropped, filtered, or re-encoded.\n\n"
        "The source archive contains eight sequences and no manifest, report, GIF, or timing metadata. Preview timing retains the existing stable sleep semantics: the 16-frame lifecycle uses 260 ms for F01-F15 and 1100 ms for F16; the eight-frame roll uses 260 ms for F01-F07 and 800 ms for F08; breathing loops use 650 ms per frame.\n\n"
        "Runtime gates remain closed: `runtime_validation=pending_owner_windows_renderer_qa`, `runtime_approved=false`, `runtime_use=false`, `production_asset=false`, and `prototype_use=false`. Only isolated `DeveloperPreview` is allowed.\n\n"
        "The v5 front-three-quarter-side and right-rear breathing views are absent from v10 and are not carried forward. The main lifecycle already includes its roll; the independent roll must not be appended. Incompatible camera views must not be hard-cut together. No approved wake or interrupt-exit sequence exists, and legacy sleep artwork is not an allowed fallback.\n",
        encoding="utf-8",
        newline="\n",
    )
    return batch_root


def main() -> None:
    parser = argparse.ArgumentParser(description="Import the immutable Wukong sleep runtime v10 candidate package.")
    parser.add_argument("zip_path", type=Path)
    parser.add_argument("--repository-root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    print(import_package(args.zip_path.resolve(), args.repository_root.resolve()))


if __name__ == "__main__":
    main()
