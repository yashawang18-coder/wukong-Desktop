#!/usr/bin/env python3
"""Validate and import the exact food/water v5 RGBA candidate frames."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
from pathlib import Path

import numpy as np
from PIL import Image


BATCH_ID = "WK-INTERACTION-FOOD-WATER-COAT-SEAM-CANDIDATE-v5"
SOURCE_PACKAGE = "WK-FOOD-WATER-COAT-SEAM-v5"
FRAME_DURATION_MS = 125
CANVAS = (1024, 1024)
ACTION_SPECS = {
    "drink-water": {
        "behavior_id": "wk.interaction.drink_water",
        "display_name": "喝水",
        "description": "Owner-triggered standing water-drinking sequence approved for Normal runtime.",
        "frame_count": 91,
    },
    "eat-kibble": {
        "behavior_id": "wk.interaction.eat_kibble",
        "display_name": "吃饭",
        "description": "Owner-triggered standing kibble-eating sequence approved for Normal runtime.",
        "frame_count": 103,
    },
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def validate_source(source: Path) -> tuple[dict[str, str], list[dict[str, object]]]:
    required = [
        "README.md",
        "parameters.json",
        "sequence-manifest.json",
        "SHA256.json",
        "QA/summary.json",
        "QA/seam-repair.json",
    ]
    missing = [name for name in required if not (source / name).is_file()]
    if missing:
        raise ValueError(f"source package missing required files: {missing}")

    checksums = json.loads((source / "SHA256.json").read_text(encoding="utf-8"))
    if len(checksums) != 74:
        raise ValueError(f"source SHA256.json has {len(checksums)} entries, expected 74")
    for relative, expected in checksums.items():
        path = source / relative
        if not path.is_file():
            raise ValueError(f"source checksum entry is missing: {relative}")
        actual = sha256(path)
        if actual.lower() != str(expected).lower():
            raise ValueError(f"source checksum mismatch: {relative}")

    frame_paths = sorted((source / "frames").glob("**/*.png"))
    if len(frame_paths) != 48:
        raise ValueError(f"source has {len(frame_paths)} frame PNGs, expected 48")

    inventory: list[dict[str, object]] = []
    for path in frame_paths:
        relative = path.relative_to(source).as_posix()
        with Image.open(path) as image:
            image.load()
            if image.format != "PNG" or image.mode != "RGBA" or image.size != CANVAS:
                raise ValueError(f"frame contract mismatch: {relative}")
            pixels = np.asarray(image, dtype=np.uint8)
        alpha = pixels[..., 3]
        if not np.any(alpha == 0) or not np.any(alpha > 0):
            raise ValueError(f"frame has invalid transparency: {relative}")
        if np.max(alpha[0, :]) != 0 or np.max(alpha[-1, :]) != 0 or np.max(alpha[:, 0]) != 0 or np.max(alpha[:, -1]) != 0:
            raise ValueError(f"frame edge is not transparent: {relative}")
        bbox = Image.fromarray(alpha, mode="L").getbbox()
        if bbox is None:
            raise ValueError(f"frame has empty alpha bounds: {relative}")
        visible = alpha > 0
        rgb = pixels[..., :3].astype(np.int16)
        blue_advantage = (
            visible
            & (rgb[..., 2] > 150)
            & ((rgb[..., 2] - rgb[..., 0]) > 50)
            & ((rgb[..., 2] - rgb[..., 1]) > 20)
        )
        inventory.append(
            {
                "path": relative,
                "width": 1024,
                "height": 1024,
                "mode": "RGBA",
                "bytes": path.stat().st_size,
                "sha256": sha256(path),
                "alpha_bbox": list(bbox),
                "visible_blue_advantage_pixels": int(np.count_nonzero(blue_advantage)),
            }
        )
    return {str(k): str(v).lower() for k, v in checksums.items()}, inventory


def build_manifest(source: Path, inventory: list[dict[str, object]]) -> dict[str, object]:
    source_manifest = json.loads((source / "sequence-manifest.json").read_text(encoding="utf-8"))
    if source_manifest.get("canvas") != [1024, 1024] or source_manifest.get("format") != "RGBA" or source_manifest.get("fps") != 8:
        raise ValueError("source sequence manifest canvas/format/fps contract changed")

    inventory_by_path = {str(item["path"]): item for item in inventory}
    actions: list[dict[str, object]] = []
    referenced: set[str] = set()
    reference_count = 0
    for source_name, spec in ACTION_SPECS.items():
        source_action = source_manifest["actions"].get(source_name)
        if source_action is None:
            raise ValueError(f"source sequence is missing: {source_name}")
        frame_paths = [str(path).replace("\\", "/") for path in source_action["frames"]]
        durations = [int(value) for value in source_action["duration_ms"]]
        expected = int(spec["frame_count"])
        if len(frame_paths) != expected or len(durations) != expected or source_action.get("frame_count") != expected:
            raise ValueError(f"source sequence length changed: {source_name}")
        if any(duration != FRAME_DURATION_MS for duration in durations):
            raise ValueError(f"source timing changed: {source_name}")
        if any(path not in inventory_by_path for path in frame_paths):
            raise ValueError(f"source sequence references a frame outside the inventory: {source_name}")
        referenced.update(frame_paths)
        reference_count += len(frame_paths)

        phase_ranges = (("intro", 0, 6), ("action", 6, expected - 10), ("exit", expected - 10, expected))
        phases: list[dict[str, object]] = []
        for phase_name, start, end in phase_ranges:
            frames = []
            for relative, duration in zip(frame_paths[start:end], durations[start:end], strict=True):
                item = inventory_by_path[relative]
                frames.append(
                    {
                        "path": relative,
                        "width": 1024,
                        "height": 1024,
                        "sha256": item["sha256"],
                        "bytes": item["bytes"],
                        "duration_ms": duration,
                    }
                )
            phases.append({"name": phase_name, "loop": False, "frame_count": len(frames), "frames": frames})

        actions.append(
            {
                **spec,
                "from_pose": "stand.neutral.left_front",
                "to_pose": "stand.neutral.left_front",
                "direction": "left_front",
                "total_duration_ms": expected * FRAME_DURATION_MS,
                "frame_duration_ms": FRAME_DURATION_MS,
                "interruptible": False,
                "loop": False,
                "owner_preview_approved": True,
                "visual_approved": True,
                "runtime_validation": "passed_windows_renderer_qa",
                "runtime_approved": True,
                "runtime_use": True,
                "production_asset": True,
                "prototype_use": False,
                "developer_preview": True,
                "autonomous_binding_enabled": False,
                "normal_runtime_available": True,
                "allowed_sources": ["OwnerContextMenu", "ControlPanel", "DeveloperPreview"],
                "phases": phases,
            }
        )

    unreferenced = sorted(set(inventory_by_path) - referenced)
    expected_unreferenced = [
        "frames/drink-pause/frame-05.png",
        "frames/drink-pause/frame-06.png",
        "frames/eat-pause/frame-05.png",
        "frames/eat-pause/frame-06.png",
    ]
    if reference_count != 194 or len(referenced) != 44 or unreferenced != expected_unreferenced:
        raise ValueError(
            f"source timeline inventory changed: refs={reference_count}, unique={len(referenced)}, unreferenced={unreferenced}"
        )

    return {
        "batch_id": BATCH_ID,
        "asset_id": BATCH_ID,
        "asset_stage": "runtime_approved",
        "candidate_profile": "food-water-coat-seam-v5-owner-approved-manual",
        "source_package": SOURCE_PACKAGE,
        "source_sha256_manifest": "SHA256.json",
        "source_sha256_manifest_sha256": sha256(source / "SHA256.json"),
        "source_frame_count": 48,
        "runtime_frame_count": 48,
        "sequence_frame_reference_count": 194,
        "referenced_unique_frame_count": 44,
        "unreferenced_source_frames": unreferenced,
        "sequence_count": 2,
        "owner_preview_approved": True,
        "visual_approved": True,
        "runtime_validation": "passed_windows_renderer_qa",
        "runtime_approved": True,
        "runtime_use": True,
        "production_asset": True,
        "prototype_use": False,
        "developer_preview": True,
        "autonomous_binding_enabled": False,
        "normal_runtime_available": True,
        "allowed_sources": ["OwnerContextMenu", "ControlPanel", "DeveloperPreview"],
        "pixel_import_policy": "byte_for_byte_copy_no_reencode_no_rescale_no_recolor",
        "known_source_limitations": [
            "Sparse pre-existing edge-colour artefacts remain outside the source package's colour-seam repair scope.",
            "Automated validation does not replace Windows transparent-renderer owner QA.",
        ],
        "frame_inventory": inventory,
        "actions": actions,
    }


def import_package(source: Path, repository: Path) -> Path:
    checksums, inventory = validate_source(source)
    manifest = build_manifest(source, inventory)
    destination = repository / "assets" / "action-batches" / BATCH_ID
    temporary = destination.with_name(destination.name + ".tmp")
    if temporary.exists():
        shutil.rmtree(temporary)
    temporary.mkdir(parents=True)

    for item in inventory:
        relative = str(item["path"])
        target = temporary / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / relative, target)
        if sha256(target) != item["sha256"]:
            raise ValueError(f"byte-preserving copy failed: {relative}")

    provenance = temporary / "provenance"
    provenance.mkdir()
    copies = {
        "README.md": "SOURCE-README.md",
        "parameters.json": "SOURCE-parameters.json",
        "sequence-manifest.json": "SOURCE-sequence-manifest.json",
        "SHA256.json": "SOURCE-SHA256.json",
        "QA/summary.json": "SOURCE-QA-summary.json",
        "QA/seam-repair.json": "SOURCE-QA-seam-repair.json",
    }
    for source_name, destination_name in copies.items():
        shutil.copyfile(source / source_name, provenance / destination_name)

    frame_sums = "".join(f"{item['sha256']}  {item['path']}\n" for item in inventory)
    (temporary / "SOURCE-FRAME-SHA256SUMS.sha256").write_text(frame_sums, encoding="ascii")
    (temporary / "RUNTIME-FRAME-SHA256SUMS.sha256").write_text(frame_sums, encoding="ascii")
    write_json(temporary / "asset.json", manifest)
    write_json(temporary / "manifest.json", manifest)
    write_json(
        temporary / "IMPORT-VALIDATION-REPORT.json",
        {
            "batch_id": BATCH_ID,
            "source_package": SOURCE_PACKAGE,
            "source_sha256_manifest_entries": len(checksums),
            "source_sha256_manifest_sha256": manifest["source_sha256_manifest_sha256"],
            "unique_rgba_frames": 48,
            "timeline_references": 194,
            "referenced_unique_frames": 44,
            "unreferenced_source_frames": manifest["unreferenced_source_frames"],
            "byte_preserving_copy": True,
            "all_source_checksums_passed": True,
            "runtime_validation": "passed_windows_renderer_qa",
        },
    )
    (temporary / "README.md").write_text(
        "# Wukong food and water coat-seam v5\n\n"
        "This review-only batch imports the 48 exact 1024x1024 RGBA source PNGs from "
        "`WK-FOOD-WATER-COAT-SEAM-v5` without re-encoding or pixel changes. The source manifest "
        "references those files through 194 timeline slots: 103 for eating and 91 for drinking, "
        "at 125 ms per slot. Four pause inventory frames are retained for provenance but are not "
        "referenced by either timeline.\n\n"
        "The owner approved both actions for the manual Normal runtime path on 2026-09-08. "
        "`OwnerContextMenu` and `ControlPanel` may trigger them; `DeveloperPreview` remains available "
        "for diagnostics. Autonomous behavior, dialogue, model routing, and prototype use remain disabled.\n",
        encoding="utf-8",
    )

    if destination.exists():
        shutil.rmtree(destination)
    os.replace(temporary, destination)
    return destination


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    destination = import_package(args.source.resolve(), args.repository.resolve())
    print(destination)


if __name__ == "__main__":
    main()
