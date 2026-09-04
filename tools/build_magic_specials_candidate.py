#!/usr/bin/env python3
"""Build the owner-preview magic candidate batch from reviewed local packages."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import struct
from pathlib import Path


BATCH_ID = "WK-MAGIC-SPECIALS-CANDIDATE-v1"


def png_size(path: Path) -> tuple[int, int]:
    with path.open("rb") as stream:
        if stream.read(8) != b"\x89PNG\r\n\x1a\n":
            raise ValueError(f"not a PNG: {path}")
        length = struct.unpack(">I", stream.read(4))[0]
        if stream.read(4) != b"IHDR" or length < 8:
            raise ValueError(f"missing PNG IHDR: {path}")
        return struct.unpack(">II", stream.read(8))


def frame_record(batch_root: Path, path: Path) -> dict[str, object]:
    width, height = png_size(path)
    data = path.read_bytes()
    return {
        "path": path.relative_to(batch_root).as_posix(),
        "width": width,
        "height": height,
        "sha256": hashlib.sha256(data).hexdigest(),
        "bytes": len(data),
    }


def copy_tree(source: Path, destination: Path) -> None:
    if not source.is_dir():
        raise FileNotFoundError(source)
    shutil.copytree(source, destination, dirs_exist_ok=True)


def phase(batch_root: Path, name: str, directory: Path, loop: bool) -> dict[str, object]:
    frames = sorted(directory.glob("*.png"))
    if not frames:
        raise ValueError(f"no PNG frames in {directory}")
    return {
        "name": name,
        "loop": loop,
        "frame_count": len(frames),
        "frames": [frame_record(batch_root, item) for item in frames],
    }


def action(
    batch_root: Path,
    behavior_id: str,
    display_name: str,
    description: str,
    source_folder: str,
    frame_duration_ms: int,
    effect: str,
    phases: list[dict[str, object]],
    *,
    interruptible: bool = True,
) -> dict[str, object]:
    frame_count = sum(int(item["frame_count"]) for item in phases)
    return {
        "behavior_id": behavior_id,
        "action_id": behavior_id,
        "display_name": display_name,
        "description": description,
        "source_folder": source_folder,
        "frame_count": frame_count,
        "frame_duration_ms": frame_duration_ms,
        "total_duration_ms": frame_count * frame_duration_ms,
        "from_pose": "prone.awake.left_front",
        "to_pose": "prone.awake.left_front",
        "direction": "left_front",
        "interruptible": interruptible,
        "runtime_validation": "pending",
        "runtime_approved": False,
        "runtime_use": False,
        "prototype_use": True,
        "production_asset": False,
        "effect": effect,
        "interrupt_exit_strategy": "cancel_restore_idle",
        "phases": phases,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()

    source_root = args.source_root.resolve()
    repo_root = args.repo_root.resolve()
    batch_root = repo_root / "assets" / "action-batches" / BATCH_ID
    if batch_root.exists():
        shutil.rmtree(batch_root)
    batch_root.mkdir(parents=True)

    broom_source = source_root / "Wukong-Magic-Broom-Complete-v8-recovered" / "runtime"
    invisibility_source = source_root / "Wukong-Magic-Invisibility-v8-candidate" / "runtime"
    petrification_source = source_root / "Wukong-Magic-Petrification-v8-candidate" / "runtime"
    coin_source = source_root / "Wukong-Magic-Coin-v1-candidate"
    mock_root = repo_root / "assets" / "action-batches" / "WK-MAGIC-SPECIALS-MOCK-v1"

    copy_tree(broom_source, batch_root / "accio_broom")
    copy_tree(invisibility_source, batch_root / "apparate")
    copy_tree(petrification_source, batch_root / "petrificus")
    copy_tree(coin_source / "petrificus_coin", batch_root / "petrificus_coin")
    copy_tree(mock_root / "scourgify", batch_root / "scourgify")
    shutil.copy2(coin_source / "manifest.json", batch_root / "coin-manifest.json")
    shutil.copy2(coin_source / "checksums.sha256", batch_root / "coin-checksums.sha256")

    actions = [
        action(
            batch_root,
            "wk.magic.accio_broom",
            "Accio Broom",
            "Reviewed 1024px broom takeoff, right-flight loop, and seated landing candidate.",
            "accio_broom",
            115,
            "BroomFlight",
            [
                phase(batch_root, "intro", batch_root / "accio_broom" / "takeoff", False),
                phase(batch_root, "loop", batch_root / "accio_broom" / "flight" / "flight-right", True),
                phase(batch_root, "exit", batch_root / "accio_broom" / "landing", False),
            ],
        ),
        action(
            batch_root,
            "wk.magic.apparate",
            "Apparate",
            "Reviewed 1024px disappear, relocation cut, and appear candidate.",
            "apparate",
            83,
            "Apparate",
            [
                phase(batch_root, "intro", batch_root / "apparate" / "disappear", False),
                phase(batch_root, "loop", batch_root / "apparate" / "invisible", True),
                phase(batch_root, "exit", batch_root / "apparate" / "appear", False),
            ],
        ),
        action(
            batch_root,
            "wk.magic.petrificus_totalus",
            "Petrificus Totalus",
            "Reviewed 1024px living-to-stone transition followed by the vivid interactive coin hold.",
            "petrificus",
            95,
            "Petrify",
            [
                phase(batch_root, "intro", batch_root / "petrificus" / "petrify", False),
                {
                    "name": "loop",
                    "loop": True,
                    "frame_count": 1,
                    "frames": [frame_record(batch_root, batch_root / "petrificus_coin" / "front" / "state-01-vivid.png")],
                },
            ],
            interruptible=False,
        ),
        action(
            batch_root,
            "wk.magic.petrificus_release",
            "Finite Incantatem",
            "Reviewed 1024px reverse stone-to-living release candidate.",
            "petrificus",
            95,
            "PetrifyRelease",
            [phase(batch_root, "exit", batch_root / "petrificus" / "restore", False)],
        ),
        action(
            batch_root,
            "wk.magic.scourgify",
            "Scourgify",
            "Existing code-drawn Scourgify placeholder retained until reviewed artwork is available.",
            "scourgify",
            90,
            "Scourgify",
            [phase(batch_root, "loop", batch_root / "scourgify", True)],
        ),
    ]

    directional = {}
    for directory in sorted((batch_root / "accio_broom" / "flight").iterdir()):
        if directory.is_dir():
            directional[directory.name.removeprefix("flight-")] = [
                frame_record(batch_root, item) for item in sorted(directory.glob("*.png"))
            ]

    manifest = {
        "schema_version": 1,
        "batch_id": BATCH_ID,
        "identity_profile": "wukong-magic-v8-candidate",
        "status": "runtime-candidate",
        "runtime_validation": "pending",
        "runtime_approved": False,
        "runtime_use": False,
        "prototype_use": True,
        "production_asset": False,
        "notes": [
            "OwnerContextMenu and ControlPanel may use PrototypePreview only.",
            "Coin pointer interaction is allowed only while the owner-started petrification preview is active.",
            "Windows transparent-renderer QA is still required before production runtime use.",
            "Scourgify remains mock artwork; all other actions use the reviewed local candidate packages.",
        ],
        "broom_directional_flight": directional,
        "coin_manifest": "coin-manifest.json",
        "actions": actions,
    }
    (batch_root / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    asset = {
        "asset_id": BATCH_ID,
        "asset_type": "action-batch",
        "status": "runtime-candidate",
        "runtime_validation": "pending",
        "runtime_approved": False,
        "runtime_use": False,
        "prototype_use": True,
        "production_asset": False,
        "owner_preview_entry_points": ["OwnerContextMenu", "ControlPanel"],
        "notes": "Integrated V8 magic and interactive coin candidate; production registry remains closed.",
    }
    (batch_root / "asset.json").write_text(json.dumps(asset, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"built {batch_root} with {sum(1 for _ in batch_root.rglob('*.png'))} PNG files")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
