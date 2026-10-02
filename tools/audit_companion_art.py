"""Read-only pixel audit and review index. Never transforms or writes source PNGs."""
import argparse
import hashlib
import html
import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
REFERENCES = {
    "standing_identity": "assets/action-mocks/WK-COMMAND-PRODUCTION-CANDIDATES-v4/frames/sit/frame-001.png",
    "sitting_identity": "assets/action-mocks/WK-COMMAND-PRODUCTION-CANDIDATES-v4/frames/sit/frame-010.png",
    "front_prone_pose": "assets/action-batches/WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4/frames/prone-idle-front-calm/frame-001.png",
}
REVIEW_BATCHES = (
    "WK-AUTONOMOUS-PATROL-WALK-v1-candidate",
    "WK-AUTONOMOUS-SLEEP-RUNTIME-FINAL-CANDIDATE-v10",
    "WK-AUTONOMOUS-PRONE-HEAD-MICROEVENT-CANDIDATE-v4",
)


def metrics(path):
    payload = path.read_bytes()
    with Image.open(path) as image:
        image.load()
        if image.mode != "RGBA":
            return {"sha256": hashlib.sha256(payload).hexdigest(), "errors": ["not_rgba"]}
        pixels = np.asarray(image).astype(np.int16)
        a = pixels[:, :, 3]
        rgb = pixels[:, :, :3]
        errors = []
        if image.size != (1024, 1024):
            errors.append("canvas_not_1024")
        if any(np.any(edge) for edge in (a[0], a[-1], a[:, 0], a[:, -1])):
            errors.append("nonzero_alpha_at_canvas_edge")
        boxes = {}
        for cutoff in (1, 16, 128):
            y, x = np.nonzero(a >= cutoff)
            boxes[str(cutoff)] = [int(x.min()), int(y.min()), int(x.max()) + 1, int(y.max()) + 1] if x.size else None
        if boxes["1"] is None:
            errors.append("empty_alpha")
        opaque = a == 255
        gold = opaque & (rgb[:, :, 0] > rgb[:, :, 1] + 12) & (rgb[:, :, 1] > rgb[:, :, 2] + 12)
        median_gold = np.median(rgb[gold], axis=0).tolist() if np.any(gold) else None
        blue = (a >= 16) & (rgb[:, :, 2] > rgb[:, :, 0] + 25) & (rgb[:, :, 2] > rgb[:, :, 1] + 25)
        return {
            "sha256": hashlib.sha256(payload).hexdigest(), "bytes": len(payload),
            "size": list(image.size), "mode": image.mode, "alpha_bounds": boxes,
            "baseline_nonzero": boxes["1"][3] - 1 if boxes["1"] else None,
            "baseline_alpha16": boxes["16"][3] - 1 if boxes["16"] else None,
            "gold_region_rgb_median": median_gold,
            "blue_dominant_visible_pixels": int(blue.sum()),
            "faint_alpha_pixels": int(((a > 0) & (a < 16)).sum()),
            "errors": errors,
        }


def audit(root=ROOT):
    references = [{"role": role, "path": name, **metrics(root / name)} for role, name in REFERENCES.items()]
    groups = []
    for batch in REVIEW_BATCHES:
        folder = root / "assets/action-batches" / batch / "frames"
        for sequence in sorted(folder.iterdir()):
            if not sequence.is_dir():
                continue
            frames = [{"path": path.relative_to(root).as_posix(), **metrics(path)} for path in sorted(sequence.glob("*.png"))]
            if not frames:
                continue
            visible = [entry["alpha_bounds"]["16"] for entry in frames if entry["alpha_bounds"]["16"]]
            widths = [box[2] - box[0] for box in visible]
            heights = [box[3] - box[1] for box in visible]
            groups.append({
                "batch": batch, "sequence": sequence.name, "frames": frames,
                "frame_count": len(frames),
                "visible_width_range": [min(widths), max(widths)],
                "visible_height_range": [min(heights), max(heights)],
                "baseline_range_alpha16": [min(box[3] - 1 for box in visible), max(box[3] - 1 for box in visible)],
                "review_priority": "P0_gait" if "PATROL" in batch else "P1_identity_and_continuity",
                "automated_visual_approval": False,
            })
    return {
        "schema_version": 1,
        "scope": "reference_selection_and_read_only_repair_triage",
        "source_pixels_modified": False,
        "runtime_approval_changed": False,
        "reference_selection": "existing owner-approved command and early prone anchors; not a new identity approval",
        "references": references,
        "groups": groups,
        "limitations": [
            "Alpha statistics cannot prove identity, gait correctness or absence of anatomical holes.",
            "Different poses legitimately change silhouette bounds; ranges are review signals, not automatic failures.",
            "Coat medians are diagnostic only; do not automatically recolor frames to match RGB averages.",
            "A new walk needs a complete four-beat contact/swing annotation and owner playback review.",
        ],
    }


def review_html(report, output, root):
    import os
    def card(frame, caption):
        url = Path(os.path.relpath(root / frame["path"], output.parent)).as_posix()
        return '<figure><img loading="lazy" src="' + html.escape(url, quote=True) + '"><figcaption>' + html.escape(caption) + '</figcaption></figure>'
    sections = '<h1>Wukong reference and sequence audit</h1><p>Source pixels unchanged. Metrics do not confer visual or runtime approval.</p><h2>Frozen reference selection</h2><section>'
    sections += ''.join(card(frame, frame["role"]) for frame in report["references"]) + '</section>'
    for group in report["groups"]:
        sections += '<h2>' + html.escape(group["batch"] + ' / ' + group["sequence"]) + '</h2><section>'
        sections += ''.join(card(frame, Path(frame["path"]).name + ' | alpha16 ' + str(frame["alpha_bounds"]["16"])) for frame in group["frames"])
        sections += '</section>'
    return '<!doctype html><html lang="en"><meta charset="utf-8"><title>Wukong art review</title><style>body{font:14px Segoe UI,sans-serif;margin:24px;background:#eef1f2;color:#202624}h2{font-size:16px;overflow-wrap:anywhere;margin-top:32px}section{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}figure{margin:0;background:white;border:1px solid #bac3c8;border-radius:4px}img{width:100%;aspect-ratio:1;object-fit:contain;background:#d9dfe1}figcaption{padding:8px;overflow-wrap:anywhere}img:hover{background:#292e32}</style>' + sections + '</html>'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = audit()
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "audit.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    review = args.output / "review.html"
    review.write_text(review_html(report, review, ROOT), encoding="utf-8")
    print(json.dumps({"frames_audited": sum(group["frame_count"] for group in report["groups"]),
        "sequences": len(report["groups"]), "references": len(report["references"]),
        "technical_error_frames": sum(bool(frame["errors"]) for group in report["groups"] for frame in group["frames"])}, indent=2))


if __name__ == "__main__":
    main()
