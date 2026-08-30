#!/usr/bin/env python3
"""Build reference-only autonomous behavior bindings to approved motions."""

from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
COMMAND_BATCH = ROOT / "assets/action-mocks/WK-COMMAND-PRODUCTION-CANDIDATES-v4"
LIFECYCLE_BATCH = ROOT / "assets/action-batches/WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2"
OUTPUT = ROOT / "assets/action-batches/WK-AUTONOMOUS-DAILY-BEHAVIORS-v1"


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def sequence_sha256(source_root: Path, frames: list[dict]) -> str:
    digest = hashlib.sha256()
    for frame in frames:
        source_path = source_root / frame["path"]
        data = source_path.read_bytes()
        if hashlib.sha256(data).hexdigest() != frame["sha256"] or len(data) != frame["bytes"]:
            raise ValueError(f"source frame no longer matches its manifest: {source_path}")
        digest.update(data)
    return digest.hexdigest()


def source_binding(source_root: Path, frames: list[dict], source_batch: str, source_behavior: str, phase: str, start_frame: int) -> dict:
    return {
        "asset_batch": source_batch,
        "behavior_id": source_behavior,
        "phase": phase,
        "start_frame": start_frame,
        "frame_count": len(frames),
        "sequence_sha256": sequence_sha256(source_root, frames),
    }


def command_action(command_manifest: dict, source_action: str, action: str, behavior_id: str, display_name: str, daily_role: str) -> dict:
    source = next(item for item in command_manifest["actions"] if item["action"] == source_action)
    frames = source["frames"]
    return {
        "action": action,
        "behavior_id": behavior_id,
        "display_name": display_name,
        "daily_role": daily_role,
        "from_posture": source["from_posture"],
        "to_posture": source["to_posture"],
        "frame_count": len(frames),
        "loop": False,
        "source_motion_design_approved": True,
        "autonomous_semantics_owner_approved": True,
        "runtime_approved": False,
        "runtime_use": False,
        "source_binding": source_binding(
            COMMAND_BATCH,
            frames,
            command_manifest["batch_id"],
            source["behavior_id"],
            "mock",
            1,
        ),
    }


def lifecycle_action(lifecycle_manifest: dict, source_frames: list[dict], start_frame: int, action: str, behavior_id: str, display_name: str, from_posture: str, to_posture: str, daily_role: str) -> dict:
    source_behavior = lifecycle_manifest["actions"][0]["behavior_id"]
    return {
        "action": action,
        "behavior_id": behavior_id,
        "display_name": display_name,
        "daily_role": daily_role,
        "from_posture": from_posture,
        "to_posture": to_posture,
        "frame_count": len(source_frames),
        "loop": False,
        "source_motion_design_approved": True,
        "autonomous_semantics_owner_approved": True,
        "runtime_approved": False,
        "runtime_use": False,
        "source_binding": source_binding(
            LIFECYCLE_BATCH,
            source_frames,
            lifecycle_manifest["batch_id"],
            source_behavior,
            "exit",
            start_frame,
        ),
    }


def main() -> None:
    command_manifest = read_json(COMMAND_BATCH / "manifest.json")
    lifecycle_manifest = read_json(LIFECYCLE_BATCH / "manifest.json")
    lifecycle = lifecycle_manifest["actions"][0]
    exit_frames = next(phase["frames"] for phase in lifecycle["phases"] if phase["name"] == "exit")

    actions = [
        command_action(command_manifest, "sit", "stand-to-sit", "wk.daily.stand_to_sit", "日常站立转坐下", "posture_transition"),
        command_action(command_manifest, "down", "sit-to-prone", "wk.daily.sit_to_prone", "日常坐姿转趴卧", "posture_transition"),
        lifecycle_action(lifecycle_manifest, exit_frames[:4], 1, "prone-to-sit", "wk.daily.prone_to_sit", "日常趴卧转坐起", "Prone", "Sit", "posture_transition"),
        lifecycle_action(lifecycle_manifest, exit_frames[3:], 4, "sit-to-stand", "wk.daily.sit_to_stand", "日常坐姿转站立", "Sit", "Stand", "posture_transition"),
    ]

    manifest = {
        "batch_id": "WK-AUTONOMOUS-DAILY-BEHAVIORS-v1",
        "category": "autonomous_daily_behavior_candidate",
        "asset_stage": "runtime_candidate_owner_visual_qa_pending",
        "source_status": "reference_binding_to_runtime_approved_light_malt_gold_assets",
        "source_batches": [command_manifest["batch_id"], lifecycle_manifest["batch_id"]],
        "identity_style": "wukong_light_malt_gold_lively",
        "motion_design_approved_at_source": True,
        "autonomous_semantics_owner_approved": True,
        "production_asset": False,
        "visual_approved": False,
        "runtime_validation": "pending_owner_windows_renderer_qa",
        "runtime_approved": False,
        "runtime_use": False,
        "may_enter_autonomous_pool_by_default": False,
        "may_enter_command_pool": False,
        "may_enter_context_menu": False,
        "storage_policy": "reference_only_no_duplicate_png",
        "pixel_policy": "source frames are resolved in place; no pixel copy, recolor, redraw, blur, sharpen, rescale, or alpha edit",
        "actions": actions,
        "notes": [
            "This batch stores behavior semantics only; every candidate resolves an immutable range from an approved source motion.",
            "Owner removed the command-derived playful hop and spin from autonomous daily review; the original command actions remain unchanged.",
            "The four posture transitions are enabled only for explicit developer review and remain outside production runtime until real Windows renderer QA and runtime approval pass.",
            "No expired red/standard-Shiba asset contributes pixels, color, identity, or fur style.",
        ],
    }
    OUTPUT.mkdir(parents=True, exist_ok=True)
    duplicate_frames = OUTPUT / "frames"
    if duplicate_frames.exists():
        shutil.rmtree(duplicate_frames)
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
