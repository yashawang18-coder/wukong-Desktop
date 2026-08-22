#!/usr/bin/env python3
"""Build the immutable autonomous-daily candidate from approved source pixels."""

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


def copy_frame(source_root: Path, source: dict, target_relative: str, source_batch: str, source_behavior: str) -> dict:
    source_path = source_root / source["path"]
    target_path = OUTPUT / target_relative
    target_path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source_path, target_path)
    data = target_path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != source["sha256"] or len(data) != source["bytes"]:
        raise ValueError(f"source bytes changed while copying {source_path}")
    return {
        "path": target_relative.replace("\\", "/"),
        "width": source["width"],
        "height": source["height"],
        "mode": source["mode"],
        "sha256": digest,
        "bytes": len(data),
        "duration_ms": source["duration_ms"],
        "derived_from": {
            "batch_id": source_batch,
            "behavior_id": source_behavior,
            "path": source["path"],
            "sha256": source["sha256"],
        },
    }


def command_action(command_manifest: dict, source_action: str, action: str, behavior_id: str, display_name: str, daily_role: str) -> dict:
    source = next(item for item in command_manifest["actions"] if item["action"] == source_action)
    frames = [
        copy_frame(
            COMMAND_BATCH,
            frame,
            f"frames/{action}/frame-{index:03d}.png",
            command_manifest["batch_id"],
            source["behavior_id"],
        )
        for index, frame in enumerate(source["frames"], start=1)
    ]
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
        "autonomous_semantics_owner_approved": False,
        "runtime_approved": False,
        "runtime_use": False,
        "frames": frames,
    }


def lifecycle_action(lifecycle_manifest: dict, source_frames: list[dict], action: str, behavior_id: str, display_name: str, from_posture: str, to_posture: str, daily_role: str) -> dict:
    source_behavior = lifecycle_manifest["actions"][0]["behavior_id"]
    frames = [
        copy_frame(
            LIFECYCLE_BATCH,
            frame,
            f"frames/{action}/frame-{index:03d}.png",
            lifecycle_manifest["batch_id"],
            source_behavior,
        )
        for index, frame in enumerate(source_frames, start=1)
    ]
    return {
        "action": action,
        "behavior_id": behavior_id,
        "display_name": display_name,
        "daily_role": daily_role,
        "from_posture": from_posture,
        "to_posture": to_posture,
        "frame_count": len(frames),
        "loop": False,
        "source_motion_design_approved": True,
        "autonomous_semantics_owner_approved": False,
        "runtime_approved": False,
        "runtime_use": False,
        "frames": frames,
    }


def main() -> None:
    command_manifest = read_json(COMMAND_BATCH / "manifest.json")
    lifecycle_manifest = read_json(LIFECYCLE_BATCH / "manifest.json")
    lifecycle = lifecycle_manifest["actions"][0]
    exit_frames = next(phase["frames"] for phase in lifecycle["phases"] if phase["name"] == "exit")

    actions = [
        command_action(command_manifest, "sit", "stand-to-sit", "wk.daily.stand_to_sit", "日常站立转坐下", "posture_transition"),
        command_action(command_manifest, "down", "sit-to-prone", "wk.daily.sit_to_prone", "日常坐姿转趴卧", "posture_transition"),
        lifecycle_action(lifecycle_manifest, exit_frames[:4], "prone-to-sit", "wk.daily.prone_to_sit", "日常趴卧转坐起", "Prone", "Sit", "posture_transition"),
        lifecycle_action(lifecycle_manifest, exit_frames[3:], "sit-to-stand", "wk.daily.sit_to_stand", "日常坐姿转站立", "Sit", "Stand", "posture_transition"),
        command_action(command_manifest, "jump", "playful-hop", "wk.daily.playful_hop", "日常开心轻跳", "playful_release"),
        command_action(command_manifest, "spin", "playful-spin", "wk.daily.playful_spin", "日常开心转圈", "playful_release"),
    ]

    manifest = {
        "batch_id": "WK-AUTONOMOUS-DAILY-BEHAVIORS-v1",
        "category": "autonomous_daily_behavior_candidate",
        "asset_stage": "production_candidate_owner_qa_pending",
        "source_status": "byte_identical_derivative_of_runtime_approved_light_malt_gold_assets",
        "source_batches": [command_manifest["batch_id"], lifecycle_manifest["batch_id"]],
        "identity_style": "wukong_light_malt_gold_lively",
        "motion_design_approved_at_source": True,
        "autonomous_semantics_owner_approved": False,
        "production_asset": False,
        "visual_approved": False,
        "runtime_validation": "pending_owner_semantic_and_windows_renderer_qa",
        "runtime_approved": False,
        "runtime_use": False,
        "may_enter_autonomous_pool_by_default": False,
        "may_enter_command_pool": False,
        "may_enter_context_menu": False,
        "pixel_policy": "copied_byte_for_byte; no recolor, redraw, blur, sharpen, rescale, or alpha edit",
        "actions": actions,
        "notes": [
            "This batch changes action semantics and storage paths only; every PNG is byte-identical to an approved source frame.",
            "Command-derived motion is not automatically approved for spontaneous daily use.",
            "The batch must remain outside production runtime until owner semantic review and real Windows renderer QA pass.",
            "No expired red/standard-Shiba asset contributes pixels, color, identity, or fur style.",
        ],
    }
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
