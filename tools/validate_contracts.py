#!/usr/bin/env python3
"""Validate Wukong pose, behavior, sidecar, and runtime registry contracts."""

from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass, field
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "contracts"
STABLE_ID = re.compile(r"^wk\.[a-z0-9_]+(?:\.[a-z0-9_]+)+$")
POSE_ID = re.compile(r"^[a-z0-9_]+(?:\.[a-z0-9_]+)+$")
SEGMENTS = ("intro", "loop", "exit", "interrupt_exit")
STRATEGIES = {"asset", "pose_graph", "hold", "not_applicable", "missing"}


@dataclass
class Result:
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def validate(root: Path = ROOT) -> Result:
    result = Result()
    contracts = root / "contracts"
    poses_doc = load_json(contracts / "poses.json")
    pose_items = poses_doc.get("poses", [])
    pose_ids = [item.get("pose_id") for item in pose_items]
    if len(pose_ids) != len(set(pose_ids)):
        result.errors.append("pose catalog contains duplicate pose_id values")
    for pose in pose_items:
        if not POSE_ID.fullmatch(pose.get("pose_id", "")):
            result.errors.append(f"invalid pose_id: {pose.get('pose_id')}")
        if pose.get("stable") is not True:
            result.errors.append(f"pose is not declared stable: {pose.get('pose_id')}")

    behavior_files = sorted((contracts / "behaviors").glob("*.json"))
    behaviors = {load_json(path)["behavior_id"]: (path, load_json(path)) for path in behavior_files}
    if len(behaviors) != len(behavior_files):
        result.errors.append("behavior catalog contains duplicate behavior_id values")

    for behavior_id, (path, behavior) in behaviors.items():
        if not STABLE_ID.fullmatch(behavior_id) or re.search(r"\.v\d+$", behavior_id):
            result.errors.append(f"unstable behavior_id in {path}: {behavior_id}")
        for pose_field in ("from_pose", "to_pose"):
            if behavior.get(pose_field) not in pose_ids:
                result.errors.append(f"{behavior_id} references unknown {pose_field}: {behavior.get(pose_field)}")
        allowed = behavior.get("eligibility", {}).get("allowed_poses", [])
        for pose_id in allowed:
            if pose_id not in pose_ids:
                result.errors.append(f"{behavior_id} eligibility references unknown pose: {pose_id}")
        lifecycle = behavior.get("lifecycle", {})
        for segment in SEGMENTS:
            strategy = lifecycle.get(segment, {}).get("strategy")
            if strategy not in STRATEGIES:
                result.errors.append(f"{behavior_id} has invalid or missing lifecycle.{segment}.strategy")
            if strategy == "missing":
                result.warnings.append(f"{behavior_id}: lifecycle.{segment} is missing")
        fallback = behavior.get("fallback")
        if fallback is not None and fallback not in behaviors:
            result.errors.append(f"{behavior_id} references unknown fallback: {fallback}")

    sidecar_by_asset: dict[tuple[str, int], dict] = {}
    for path in sorted((contracts / "asset-sidecars").glob("*.json")):
        sidecar = load_json(path)
        key = (sidecar.get("asset_id"), sidecar.get("asset_version"))
        if key in sidecar_by_asset:
            result.errors.append(f"duplicate asset sidecar: {key}")
        sidecar_by_asset[key] = sidecar
        if sidecar.get("behavior_id") not in behaviors:
            result.errors.append(f"{path.name} references unknown behavior: {sidecar.get('behavior_id')}")
        for pose_field in ("from_pose", "to_pose"):
            if sidecar.get(pose_field) not in pose_ids:
                result.errors.append(f"{path.name} references unknown {pose_field}: {sidecar.get(pose_field)}")
        availability = sidecar.get("manifest_availability")
        legacy = root / sidecar.get("legacy_manifest", "")
        repository_manifest_value = sidecar.get("repository_manifest")
        repository_manifest = root / repository_manifest_value if repository_manifest_value else None
        if availability == "repository":
            if repository_manifest is None:
                result.errors.append(f"{path.name} repository source is missing repository_manifest")
            elif not repository_manifest.is_file() and not legacy.is_file():
                result.errors.append(
                    f"{path.name} repository manifest is unavailable in both repository and local provenance paths"
                )
        elif availability == "local_unpublished":
            if sidecar.get("runtime_policy", {}).get("runtime_use"):
                result.errors.append(f"{path.name} local-unpublished source cannot enable runtime use")
            if sidecar.get("review", {}).get("status") == "runtime-approved":
                result.errors.append(f"{path.name} local-unpublished source cannot be runtime-approved")
        else:
            result.errors.append(f"{path.name} has invalid manifest_availability: {availability}")
        policy = sidecar.get("runtime_policy", {})
        review = sidecar.get("review", {})
        if policy.get("runtime_use"):
            if review.get("status") != "runtime-approved":
                result.errors.append(f"{path.name} enables runtime use without runtime-approved status")
            if not review.get("owner_preview_approved") or review.get("renderer_qa") != "passed":
                result.errors.append(f"{path.name} enables runtime use without owner and renderer approval")

    registry = load_json(contracts / "runtime" / "asset-registry.json")
    seen_selectors: set[tuple[str, str]] = set()
    for binding in registry.get("bindings", []):
        behavior_id = binding.get("behavior_id")
        key = (binding.get("asset_id"), binding.get("asset_version"))
        selector_key = (behavior_id, json.dumps(binding.get("selectors", {}), sort_keys=True))
        if selector_key in seen_selectors:
            result.errors.append(f"duplicate runtime selector: {selector_key}")
        seen_selectors.add(selector_key)
        if behavior_id not in behaviors:
            result.errors.append(f"registry references unknown behavior: {behavior_id}")
        sidecar = sidecar_by_asset.get(key)
        if sidecar is None:
            result.errors.append(f"registry references unknown asset sidecar: {key}")
            continue
        review = sidecar["review"]
        policy = sidecar["runtime_policy"]
        if review["status"] != "runtime-approved" or not policy["runtime_use"]:
            result.errors.append(f"registry reaches non-runtime-approved asset: {key}")
        if not review["owner_preview_approved"] or review["renderer_qa"] != "passed":
            result.errors.append(f"registry reaches asset without required QA: {key}")
        if policy["reference_use"] == "motion_only":
            result.errors.append(f"registry reaches motion-only reference: {key}")

    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    result = validate(args.root.resolve())
    for warning in result.warnings:
        print(f"WARNING: {warning}")
    for error in result.errors:
        print(f"ERROR: {error}")
    print(f"Contract validation: {len(result.errors)} error(s), {len(result.warnings)} known gap(s)")
    return 0 if result.ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
