#!/usr/bin/env python3
"""Fail-closed audit for horizontal-mirror asset derivation.

The audit never creates PNG files.  It records whether a package has an
explicit, reviewable contract that permits a mirrored derivative.  Runtime
code must prefer native opposite-direction art and must never infer safety
from a filename alone.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


DIRECTIONAL_KEYS = {
    "directions",
    "direction_ring",
    "broom_directional_flight",
}
PROP_TOKENS = {
    "broom",
    "car",
    "coin",
    "magic",
    "petrific",
    "scourg",
    "apparate",
}
HANDED_TOKENS = {
    "paw",
    "shake",
    "hand",
    "left",
    "right",
    "turn",
    "spin",
}
EXPIRED_TOKENS = {"deprecated", "expired", "rejected", "failed"}


def load_json(path: Path) -> dict[str, Any] | None:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None
    return value if isinstance(value, dict) else None


def package_identity(path: Path, documents: list[dict[str, Any]]) -> str:
    for document in documents:
        for key in ("asset_id", "batch_id", "package_id"):
            if document.get(key):
                return str(document[key])
    return path.name


def text_tokens(document: dict[str, Any]) -> str:
    values = []
    for key in ("asset_id", "batch_id", "behavior_id", "status", "asset_stage", "display_name"):
        if document.get(key) is not None:
            values.append(str(document[key]))
    for action in document.get("actions", []) if isinstance(document.get("actions"), list) else []:
        if isinstance(action, dict):
            values.extend(str(action.get(key, "")) for key in ("action_id", "behavior_id", "name", "display_name"))
    return " ".join(values).lower()


def audit_package(path: Path) -> dict[str, Any]:
    source_paths = [candidate for candidate in (path / "asset.json", path / "manifest.json", path / "runtime-review-manifest.json") if candidate.is_file()]
    documents = [document for candidate in source_paths if (document := load_json(candidate)) is not None]
    identity = package_identity(path, documents)
    merged_text = " ".join(text_tokens(document) for document in documents)
    explicit_flags = [document.get("mirror_safe") for document in documents if "mirror_safe" in document]
    explicit_safe = bool(explicit_flags) and all(flag is True for flag in explicit_flags)
    native_directional = any(any(key in document for key in DIRECTIONAL_KEYS) for document in documents)
    runtime_approved = any(document.get("runtime_approved") is True for document in documents)
    reasons: list[str] = []

    if any(token in merged_text for token in EXPIRED_TOKENS):
        reasons.append("expired_or_rejected_source")
    if any(token in merged_text for token in PROP_TOKENS):
        reasons.append("prop_or_effect_orientation_is_semantic")
    if any(token in merged_text for token in HANDED_TOKENS):
        reasons.append("handed_or_directional_motion_semantics")
    if native_directional:
        reasons.append("native_directional_variants_exist_prefer_native_art")
    if not explicit_safe:
        reasons.append("missing_explicit_mirror_safe_contract")
    if runtime_approved:
        reasons.append("approved_pixels_require_versioned_derivative_and_new_owner_qa")

    eligible = explicit_safe and not reasons
    return {
        "asset_batch": identity,
        "path": path.as_posix(),
        "documents": [candidate.name for candidate in source_paths],
        "runtime_approved_source": runtime_approved,
        "native_directional_variants": native_directional,
        "explicit_mirror_safe": explicit_safe,
        "eligible_for_generated_horizontal_mirror": eligible,
        "runtime_integration_allowed": False,
        "reasons": reasons or ["owner_visual_qa_required_before_runtime_binding"],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    output = (args.output or root / "docs" / "review" / "MIRROR_ELIGIBILITY_AUDIT.json").resolve()
    roots = [root / "assets" / "action-batches", root / "assets" / "action-mocks", root / "assets" / "actions"]
    packages = []
    for package_root in roots:
        if not package_root.is_dir():
            continue
        for path in sorted(package_root.iterdir()):
            if not path.is_dir():
                continue
            item = audit_package(path)
            item["path"] = path.relative_to(root).as_posix()
            packages.append(item)

    report = {
        "schema_version": 1,
        "policy": "fail_closed_explicit_mirror_safe_only",
        "scope": "all repository action packages",
        "native_opposite_direction_preferred": True,
        "approved_source_pixels_modified_in_place": False,
        "eligible_package_count": sum(item["eligible_for_generated_horizontal_mirror"] for item in packages),
        "runtime_integrated_mirror_count": 0,
        "packages": packages,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"mirror audit: {len(packages)} packages, {report['eligible_package_count']} eligible, 0 runtime integrated")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
