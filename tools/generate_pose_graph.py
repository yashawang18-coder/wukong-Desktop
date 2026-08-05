#!/usr/bin/env python3
"""Generate a reviewable pose graph and P0 gap report from behavior contracts."""

from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "contracts"
GENERATED = CONTRACTS / "generated"


def load(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def build() -> tuple[dict, str, str]:
    poses = {item["pose_id"]: item for item in load(CONTRACTS / "poses.json")["poses"]}
    behaviors = [load(path) for path in sorted((CONTRACTS / "behaviors").glob("*.json"))]
    edges = []
    gaps = []
    for behavior in behaviors:
        missing = [name for name, value in behavior["lifecycle"].items() if value["strategy"] == "missing"]
        edge = {
            "behavior_id": behavior["behavior_id"],
            "phase": behavior["phase"],
            "from_pose": behavior["from_pose"],
            "to_pose": behavior["to_pose"],
            "availability": behavior["availability"],
            "missing_segments": missing,
        }
        edges.append(edge)
        if missing or behavior["availability"] != "available":
            gaps.append(edge)

    graph = {"schema_version": 1, "poses": list(poses.values()), "edges": edges, "gaps": gaps}
    p0_edges = [edge for edge in edges if edge["phase"] == "P0"]
    lines = ["flowchart TD"]
    pose_nodes: dict[str, str] = {}
    for index, pose_id in enumerate(sorted({p for edge in p0_edges for p in (edge["from_pose"], edge["to_pose"])}), 1):
        node = f"P{index}"
        pose_nodes[pose_id] = node
        lines.append(f'    {node}["{pose_id}"]')
    for index, edge in enumerate(p0_edges, 1):
        label = edge["behavior_id"]
        if edge["missing_segments"]:
            label += " / GAP"
        lines.append(f'    {pose_nodes[edge["from_pose"]]} -->|"{label}"| {pose_nodes[edge["to_pose"]]}')

    report = ["# P0 contract gap report", "", "Generated from contract files; it does not approve any asset.", ""]
    for edge in p0_edges:
        missing = ", ".join(edge["missing_segments"]) or "none"
        report.append(f'- `{edge["behavior_id"]}`: `{edge["availability"]}`; missing lifecycle segments: {missing}.')
    report.extend([
        "",
        "## Catalog-level P0 gaps",
        "",
        "- Stable sitting pose and sit/stand transitions are not yet defined.",
        "- Standing idle has no visual candidate sidecar.",
        "- Walk start, walk stop, and safe interrupted stop are absent.",
        "- Only one walk direction is represented.",
        "- The available turn video and approved turn keyframes describe opposite directions.",
        "- Touch, drag/drop, and forced-stop actions are not yet contract-complete.",
    ])
    return graph, "\n".join(lines) + "\n", "\n".join(report) + "\n"


def main() -> int:
    graph, mermaid, report = build()
    GENERATED.mkdir(parents=True, exist_ok=True)
    (GENERATED / "pose-graph.json").write_text(json.dumps(graph, indent=2) + "\n", encoding="utf-8")
    (GENERATED / "pose-graph.mmd").write_text(mermaid, encoding="utf-8")
    (GENERATED / "P0_GAPS.md").write_text(report, encoding="utf-8")
    print(f"Generated {GENERATED.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
