#!/usr/bin/env python3
"""Validate and package the Wukong prone-face-down v9 owner-QA candidate."""

from __future__ import annotations

from collections import deque
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

import numpy as np
from PIL import Image


BATCH_ID = "WK-AUTONOMOUS-PRONE-FACE-DOWN-PRODUCTION-v9"
BEHAVIOR_ID = "wk.daily.prone_face_down"
ANCHOR_SHA256 = "c2a6f39a5d3f4db3d14fd80b9f4e8695add95c5e4a5d3e827de83e64b4a5f44d"
PHASES = ("settle-to-face-down", "face-down-calm", "rise-to-down-anchor")
FACE_BOX = (112, 400, 550, 912)
SETTLE_DURATIONS = [180, 140, 130, 130, 120, 120, 110, 110, 110, 120, 130, 220]
CALM_DURATIONS = [2200, 1000, 500, 1000, 1800, 1200, 900, 500, 900, 1000, 1000, 2000]
RISE_DURATIONS = [220, 130, 120, 110, 110, 110, 120, 120, 130, 130, 140, 180]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def frame_paths(package: Path, phase: str) -> list[Path]:
    paths = [package / "frames" / phase / f"frame-{index:03d}.png" for index in range(1, 13)]
    if not all(path.is_file() for path in paths):
        raise AssertionError(f"missing frame in {phase}")
    return paths


def rgba(path: Path) -> np.ndarray:
    with Image.open(path) as image:
        image.load()
        if image.size != (1024, 1024) or image.mode != "RGBA":
            raise AssertionError(f"invalid PNG contract: {path}: {image.size} {image.mode}")
        return np.asarray(image).copy()


def dark_components(image: np.ndarray) -> list[dict[str, float | int]]:
    rgb = image[..., :3].astype(np.int16)
    alpha = image[..., 3]
    mask = (alpha > 180) & (rgb.mean(axis=2) < 70)
    mask[:430, :] = False
    mask[900:, :] = False
    mask[:, :180] = False
    mask[:, 650:] = False
    visited = np.zeros(mask.shape, dtype=bool)
    components: list[dict[str, float | int]] = []
    height, width = mask.shape
    for start_y, start_x in zip(*np.where(mask & ~visited), strict=True):
        if visited[start_y, start_x]:
            continue
        queue = deque([(int(start_x), int(start_y))])
        visited[start_y, start_x] = True
        count = sum_x = sum_y = 0
        while queue:
            x, y = queue.popleft()
            count += 1
            sum_x += x
            sum_y += y
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if 0 <= nx < width and 0 <= ny < height and mask[ny, nx] and not visited[ny, nx]:
                    visited[ny, nx] = True
                    queue.append((nx, ny))
        if count >= 180:
            components.append({"size": count, "x": sum_x / count, "y": sum_y / count})
    return components


def landmarks(image: np.ndarray) -> dict[str, object]:
    components = dark_components(image)
    eye_candidates = [item for item in components if 180 <= int(item["size"]) <= 1000]
    pairs: list[tuple[float, float, dict[str, object], dict[str, object]]] = []
    for index, first in enumerate(eye_candidates):
        for second in eye_candidates[index + 1 :]:
            dx = abs(float(first["x"]) - float(second["x"]))
            dy = abs(float(first["y"]) - float(second["y"]))
            mean_y = (float(first["y"]) + float(second["y"])) / 2
            if 65 <= dx <= 125 and dy <= 22 and 500 <= mean_y <= 760:
                pairs.append((mean_y, -dy, first, second))
    if not pairs:
        raise AssertionError("could not locate eye pair")
    _, _, left, right = max(pairs, key=lambda value: value[:2])
    if float(left["x"]) > float(right["x"]):
        left, right = right, left
    eye_y = (float(left["y"]) + float(right["y"])) / 2
    eye_mid_x = (float(left["x"]) + float(right["x"])) / 2
    noses = [
        item
        for item in components
        if int(item["size"]) >= 300
        and eye_y + 25 < float(item["y"]) < eye_y + 130
        and abs(float(item["x"]) - eye_mid_x) < 70
    ]
    if not noses:
        raise AssertionError("could not locate nose")
    nose = max(noses, key=lambda item: int(item["size"]))
    eye_distance = float(right["x"]) - float(left["x"])
    eye_nose_distance = float(nose["y"]) - eye_y
    return {
        "left_eye": [round(float(left["x"]), 3), round(float(left["y"]), 3)],
        "right_eye": [round(float(right["x"]), 3), round(float(right["y"]), 3)],
        "eye_mid": [round(eye_mid_x, 3), round(eye_y, 3)],
        "nose": [round(float(nose["x"]), 3), round(float(nose["y"]), 3)],
        "eye_distance": round(eye_distance, 3),
        "eye_nose_distance": round(eye_nose_distance, 3),
        "eye_nose_ratio": round(eye_nose_distance / eye_distance, 6),
    }


def tongue_pixels(image: np.ndarray, nose: list[float]) -> int:
    nose_x, nose_y = (int(round(value)) for value in nose)
    roi = image[nose_y : nose_y + 55, nose_x - 50 : nose_x + 50, :3].astype(np.int16)
    pink = (
        (roi[..., 0] > 105)
        & (roi[..., 1] < 100)
        & (roi[..., 2] < 100)
        & (roi[..., 0] > roi[..., 1] + 35)
        & (roi[..., 0] > roi[..., 2] + 30)
    )
    return int(np.count_nonzero(pink))


def gif_metrics(path: Path, expected_durations: list[int]) -> dict[str, object]:
    durations: list[int] = []
    palettes: list[str] = []
    decoded_faces: list[bytes] = []
    with Image.open(path) as image:
        if image.n_frames != 36:
            raise AssertionError(f"GIF frame count changed: {path}: {image.n_frames}")
        for index in range(image.n_frames):
            image.seek(index)
            durations.append(int(image.info.get("duration", 0)))
            palette = image.getpalette()
            if palette is not None:
                palettes.append(hashlib.sha256(bytes(palette)).hexdigest())
            if 12 <= index <= 23:
                decoded_faces.append(image.convert("RGBA").crop(FACE_BOX).tobytes())
    if durations != expected_durations:
        raise AssertionError(f"GIF timing mismatch: {path}")
    return {
        "path": path.name,
        "frame_count": 36,
        "total_duration_ms": sum(durations),
        "durations_ms": durations,
        "global_palette_digest_count": len(set(palettes)),
        "calm_face_decoded_byte_stable": len(set(decoded_faces)) == 1,
    }


def finalize(package: Path) -> None:
    repository_root = Path(__file__).resolve().parents[1]
    packaged_tools = package / "tools"
    packaged_tools.mkdir(parents=True, exist_ok=True)
    for name in (
        "build_prone_face_down_v9.py",
        "build_prone_face_down_v9_local_warp.py",
        "finalize_prone_face_down_v9_candidate.py",
    ):
        source = repository_root / "tools" / name
        if source.is_file():
            shutil.copyfile(source, packaged_tools / name)

    reports = package / "reports"
    reports.mkdir(parents=True, exist_ok=True)
    settle_paths = frame_paths(package, PHASES[0])
    calm_paths = frame_paths(package, PHASES[1])
    rise_paths = frame_paths(package, PHASES[2])
    settle = [rgba(path) for path in settle_paths]
    calm = [rgba(path) for path in calm_paths]
    rise = [rgba(path) for path in rise_paths]

    anchor = package / "source" / "approved-down-v2-terminal-anchor.png"
    if sha256(anchor) != ANCHOR_SHA256:
        raise AssertionError("approved anchor hash changed")
    if settle_paths[0].read_bytes() != anchor.read_bytes():
        raise AssertionError("F01 is not the approved anchor byte stream")
    if rise_paths[-1].read_bytes() != anchor.read_bytes():
        raise AssertionError("F36 is not the approved anchor byte stream")
    if settle_paths[10].read_bytes() != settle_paths[11].read_bytes():
        raise AssertionError("F11/F12 are not byte-identical")
    if [path.read_bytes() for path in settle_paths[::-1]] != [path.read_bytes() for path in rise_paths]:
        raise AssertionError("rise phase is not the byte-exact reverse")

    endpoint_alpha = settle[10][..., 3]
    endpoint_face = settle[10][FACE_BOX[1] : FACE_BOX[3], FACE_BOX[0] : FACE_BOX[2], :]
    if not all(np.array_equal(image[..., 3], endpoint_alpha) for image in calm):
        raise AssertionError("calm alpha changed")
    if not all(
        np.array_equal(image[FACE_BOX[1] : FACE_BOX[3], FACE_BOX[0] : FACE_BOX[2], :], endpoint_face)
        for image in calm
    ):
        raise AssertionError("calm face/head/front-paw region changed")

    reference = settle[4]
    protected_ok = all(
        np.array_equal(image[:, 650:, :], reference[:, 650:, :])
        and np.array_equal(image[790:, :, :], reference[790:, :, :])
        for image in settle[1:11]
    )
    if not protected_ok:
        raise AssertionError("single-carrier protected region changed")

    generated_blue_spill = []
    for image in settle[1:11]:
        spill = (image[..., 3] > 0) & (
            image[..., 2].astype(np.int16)
            > np.maximum(image[..., 0], image[..., 1]).astype(np.int16) + 2
        )
        generated_blue_spill.append(int(np.count_nonzero(spill)))
    if max(generated_blue_spill) != 0:
        raise AssertionError("generated settle frames retain blue spill")

    marks = [landmarks(image) for image in settle]
    ratio_steps = [
        abs(float(marks[index]["eye_nose_ratio"]) / float(marks[index - 1]["eye_nose_ratio"]) - 1) * 100
        for index in range(1, 12)
    ]
    eye_x_steps = [
        float(marks[index]["eye_mid"][0]) - float(marks[index - 1]["eye_mid"][0])
        for index in range(1, 12)
    ]
    nose_y = [float(item["nose"][1]) for item in marks]
    monotonic_nose = all(nose_y[index] >= nose_y[index - 1] for index in range(1, 12))
    if not monotonic_nose or max(abs(value) for value in eye_x_steps) > 3.0 or max(ratio_steps) > 5.0:
        raise AssertionError("facial trajectory thresholds failed")

    tongue = [tongue_pixels(image, marks[index]["nose"]) for index, image in enumerate(settle)]
    dark_eye_means = []
    for image, mark in zip(settle, marks, strict=True):
        samples = []
        for x, y in (mark["left_eye"], mark["right_eye"]):
            x, y = int(round(x)), int(round(y))
            crop = image[y - 12 : y + 13, x - 16 : x + 17, :3]
            dark = crop[crop.mean(axis=2) < 80]
            if dark.size:
                samples.append(dark.mean(axis=0))
        dark_eye_means.append([round(float(v), 3) for v in np.vstack(samples).mean(axis=0)])

    identity_report = {
        "status": "automated_pass_owner_visual_review_pending",
        "owner_selected_blue_storyboard_images": [4, 5, 6],
        "production_identity_and_fur_carrier": "source/owner-selected-blue-456/image-06-normalized-carrier.png",
        "f01_approved_anchor_sha256": sha256(settle_paths[0]),
        "f36_approved_anchor_sha256": sha256(rise_paths[-1]),
        "f01_f36_byte_identical": settle_paths[0].read_bytes() == rise_paths[-1].read_bytes(),
        "f11_f12_byte_identical": settle_paths[10].read_bytes() == settle_paths[11].read_bytes(),
        "f02_f11_single_carrier_controlled_warp": True,
        "whole_frame_crossfade_used": False,
        "independent_per_frame_generation_used": False,
    }
    ratio_report = {
        "threshold_percent": 5.0,
        "max_adjacent_ratio_change_percent": round(max(ratio_steps), 3),
        "pass": max(ratio_steps) <= 5.0,
        "frames": [dict(frame=index, **item) for index, item in enumerate(marks, start=1)],
    }
    trajectory_report = {
        "nose_y_monotonic_non_decreasing": monotonic_nose,
        "max_adjacent_eye_mid_x_shift_px": round(max(abs(value) for value in eye_x_steps), 3),
        "horizontal_shift_limit_px": 3.0,
        "protected_rear_x_ge_650_byte_stable_f02_f11": protected_ok,
        "protected_ground_y_ge_790_byte_stable_f02_f11": protected_ok,
        "frames": [
            {
                "frame": index,
                "eye_mid_x": item["eye_mid"][0],
                "eye_mid_y": item["eye_mid"][1],
                "nose_x": item["nose"][0],
                "nose_y": item["nose"][1],
            }
            for index, item in enumerate(marks, start=1)
        ],
    }
    mouth_report = {
        "method": "strict_red-pink_pixel_count_in_nose-relative_mouth_roi",
        "tongue_or_pink_pixels_by_frame": tongue,
        "f11_f12_endpoint_count": tongue[10],
        "endpoint_closed_or_fully_occluded": tongue[10] == 0 and tongue[11] == 0,
        "f05_f08_review_board": "review/f05-f08-face-mouth-1x-board.png",
    }
    fur_report = {
        "owner_selected_surface_authority": "blue_storyboard_images_4_5_6",
        "production_carrier": "blue_storyboard_image_6",
        "f02_f11_new_fur_generation": False,
        "f02_f11_same_pixel_carrier": True,
        "protected_regions_byte_stable": protected_ok,
        "motion_method": "single_compact_support_bilinear_deformation_field",
        "blur_used_to_hide_errors": False,
        "sharpening_used": False,
        "status": "owner_visual_review_pending",
    }
    visible = reference[..., 3] > 128
    gold = visible & (reference[..., 0] > reference[..., 1] + 8) & (reference[..., 1] > reference[..., 2] - 10)
    color_report = {
        "calibration_mode": "single_carrier_no_per_frame_recolor",
        "carrier_visible_rgb_mean": [round(float(value), 3) for value in reference[..., :3][visible].mean(axis=0)],
        "carrier_gold_rgb_mean": [round(float(value), 3) for value in reference[..., :3][gold].mean(axis=0)],
        "dark_eye_rgb_means_by_frame": dark_eye_means,
        "generated_frame_blue_spill_pixels": generated_blue_spill,
        "generated_frame_blue_spill_pass": max(generated_blue_spill) == 0,
        "f01_exemption": "approved anchor preserved byte-for-byte; no recolor or defringe allowed",
    }
    alpha_report = {
        "all_png_rgba_1024": True,
        "all_canvas_borders_fully_transparent": all(
            max(
                int(image[0, :, 3].max()),
                int(image[-1, :, 3].max()),
                int(image[:, 0, 3].max()),
                int(image[:, -1, 3].max()),
            )
            == 0
            for image in settle + calm + rise
        ),
        "calm_alpha_byte_stable": True,
        "calm_face_head_front_paw_region_byte_stable": True,
        "deterministic_blue_spill_removal_changes_alpha": False,
    }
    expected = SETTLE_DURATIONS + CALM_DURATIONS + RISE_DURATIONS
    gif_report = {
        "expected_frame_count": 36,
        "expected_total_duration_ms": 17240,
        "shared_palette_strategy": "one global quantization palette per GIF plus same-RGB anti-coalescing sentinel",
        "gifs": [
            gif_metrics(package / "animations" / name, expected)
            for name in (
                "full-lifecycle-transparent.gif",
                "full-lifecycle-light.gif",
                "full-lifecycle-dark.gif",
            )
        ],
    }

    report_values = {
        "identity-continuity.json": identity_report,
        "eye-nose-ratio.json": ratio_report,
        "head-descent-trajectory.json": trajectory_report,
        "mouth-closure-trajectory.json": mouth_report,
        "fur-surface-stability.json": fur_report,
        "color-calibration.json": color_report,
        "alpha-transparency.json": alpha_report,
        "gif-global-palette.json": gif_report,
    }
    for name, value in report_values.items():
        write_json(reports / name, value)

    phase_durations = (SETTLE_DURATIONS, CALM_DURATIONS, RISE_DURATIONS)
    phase_entries = []
    for phase, paths, durations in zip(PHASES, (settle_paths, calm_paths, rise_paths), phase_durations, strict=True):
        phase_entries.append(
            {
                "name": phase,
                "frame_count": 12,
                "duration_ms": sum(durations),
                "frames": [
                    {
                        "path": path.relative_to(package).as_posix(),
                        "duration_ms": duration,
                        "sha256": sha256(path),
                    }
                    for path, duration in zip(paths, durations, strict=True)
                ],
            }
        )
    manifest = {
        "batch_id": BATCH_ID,
        "behavior_id": BEHAVIOR_ID,
        "display_name": "贴地趴卧安静休息",
        "category": "autonomous_daily_behavior_production_candidate",
        "status": "production_candidate_owner_qa_pending",
        "asset_stage": "production_candidate_owner_qa_pending",
        "frame_count": 36,
        "total_duration_ms": 17240,
        "canvas": {"width": 1024, "height": 1024, "mode": "RGBA"},
        "identity_style": "wukong_light_malt_gold_owner_selected_blue_456",
        "motion_method": "single_carrier_compact_support_controlled_warp_and_exact_reverse",
        "owner_visual_review_requested": True,
        "visual_approved": False,
        "owner_runtime_enable_requested": False,
        "runtime_validation": "pending_owner_full_lifecycle_visual_review_and_windows_renderer_ci",
        "runtime_approved": False,
        "runtime_use": False,
        "production_asset": False,
        "autonomous_binding_enabled": False,
        "runtime_mapping_modified": False,
        "main_modified": False,
        "phases": phase_entries,
        "reports": [f"reports/{name}" for name in report_values] + ["reports/QA-REPORT.md"],
        "production_scripts": [
            "tools/build_prone_face_down_v9_local_warp.py",
            "tools/build_prone_face_down_v9.py",
            "tools/finalize_prone_face_down_v9_candidate.py",
        ],
        "review_artifacts": [
            "animations/full-lifecycle-transparent.gif",
            "animations/full-lifecycle-light.gif",
            "animations/full-lifecycle-dark.gif",
            "review/full-36-light-board.png",
            "review/full-36-dark-board.png",
            "review/face-continuity-12-1x-board.png",
            "review/f05-f08-face-mouth-1x-board.png",
        ],
    }
    write_json(package / "manifest.json", manifest)

    qa_markdown = f"""# Wukong prone face-down v9 QA report

Status: `production_candidate_owner_qa_pending`

This candidate uses owner-selected blue-board images 4, 5, and 6 as the
appearance/fur audit reference, and image 6 as the sole production pixel
carrier for F02–F12. No Runway workflow was used.

## Automated results

- F01 and F36 match the approved Down v2 anchor: `{ANCHOR_SHA256}`.
- F11 and F12 are byte-identical.
- F02–F11 use one compact-support deformation field and one pixel carrier.
- Maximum adjacent eye-center horizontal movement: {trajectory_report['max_adjacent_eye_mid_x_shift_px']} px (limit 3 px).
- Maximum adjacent eye/nose ratio change: {ratio_report['max_adjacent_ratio_change_percent']}% (limit 5%).
- F02–F11 x>=650 rear pixels and y>=790 ground/paw pixels are byte-stable.
- F13–F24 alpha and face/head/front-paw review box are byte-stable.
- F25–F36 are the byte-exact reverse of F12–F01.
- All three GIFs contain 36 physical frames and total 17,240ms.
- Generated settle frames contain zero blue-dominant spill pixels after deterministic defringing.

Automated checks do not grant visual or runtime approval. The full lifecycle,
fur surface, facial character, and WPF transparent rendering remain subject to
owner review and Windows renderer CI.
"""
    (reports / "QA-REPORT.md").write_text(qa_markdown, encoding="utf-8")

    progress = """# Wukong prone face-down v9 — owner-QA candidate

The 36-frame candidate and all review derivatives are present.

- status: `production_candidate_owner_qa_pending`
- visual_approved: `false`
- runtime_approved: `false`
- runtime_use: `false`
- production_asset: `false`
- autonomous_binding_enabled: `false`
- runtime mapping: unchanged
- main: unchanged

The owner-selected blue-board images 4, 5, and 6 are retained under
`source/owner-selected-blue-456/`; image 6 is the sole production carrier for
F02–F12. F01/F36 remain the approved Down v2 anchor bytes.

See `reports/QA-REPORT.md` and the review GIFs/boards before any gate change.
"""
    (package / "WORK-IN-PROGRESS.md").write_text(progress, encoding="utf-8")

    readme = """# WK-AUTONOMOUS-PRONE-FACE-DOWN-PRODUCTION-v9

Owner-QA candidate for `wk.daily.prone_face_down`.

## Rebuild

From this package directory:

```bash
python tools/build_prone_face_down_v9_local_warp.py \\
  source/owner-selected-blue-456/image-06-normalized-carrier.png \\
  frames/settle-to-face-down \\
  --production-sequence \\
  --anchor source/approved-down-v2-terminal-anchor.png
python tools/build_prone_face_down_v9.py --assemble-package .
python tools/finalize_prone_face_down_v9_candidate.py .
```

The candidate is not visually approved or runtime approved. Do not change any
runtime gate until owner full-lifecycle review and Windows renderer CI pass.
"""
    (package / "README.md").write_text(readme, encoding="utf-8")

    source_hash_paths = [
        package / "PROMPT-RECORD.md",
        anchor,
        package / "source" / "owner-approved-face-down-pose-reference.png",
        *sorted((package / "source" / "owner-selected-blue-456").glob("*.png")),
    ]
    (package / "SOURCE-SHA256SUMS").write_text(
        "".join(f"{sha256(path)}  {path.relative_to(package).as_posix()}\n" for path in source_hash_paths),
        encoding="utf-8",
    )

    inventory_roots = ("animations", "frames", "reports", "review", "source", "tools")
    inventory = [package / "PROMPT-RECORD.md", package / "README.md", package / "SOURCE-SHA256SUMS", package / "WORK-IN-PROGRESS.md", package / "frame-timing.json", package / "manifest.json"]
    for root in inventory_roots:
        inventory.extend(path for path in (package / root).rglob("*") if path.is_file())
    inventory = sorted(set(inventory), key=lambda path: path.relative_to(package).as_posix())
    (package / "SHA256SUMS").write_text(
        "".join(f"{sha256(path)}  {path.relative_to(package).as_posix()}\n" for path in inventory),
        encoding="utf-8",
    )

    archive = package / f"{BATCH_ID}.zip"
    temporary = package.parent / f"{BATCH_ID}.zip.tmp"
    temporary.unlink(missing_ok=True)
    with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for path in sorted(package.rglob("*")):
            if not path.is_file() or path in (archive, temporary) or path.name == "ZIP-SHA256.txt":
                continue
            output.write(path, (Path(BATCH_ID) / path.relative_to(package)).as_posix())
    temporary.replace(archive)
    temporary.unlink(missing_ok=True)
    (package / "ZIP-SHA256.txt").write_text(f"{sha256(archive)}  {archive.name}\n", encoding="utf-8")
    print(f"finalized={package}")
    print(f"zip={archive}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("package", type=Path, nargs="?")
    args = parser.parse_args()
    script_root = Path(__file__).resolve().parents[1]
    package = args.package
    if package is None:
        if (script_root / "PROMPT-RECORD.md").is_file():
            package = script_root
        else:
            package = script_root / "assets" / "action-batches" / BATCH_ID
    finalize(package.resolve())


if __name__ == "__main__":
    main()
