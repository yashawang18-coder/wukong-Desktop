#!/usr/bin/env python3
"""Normalize the petrified coin silhouette and rebuild deterministic flip frames.

This is a non-generative asset repair. It preserves each face's interior artwork,
replaces only the outer cutout band from inward rim samples, applies one shared
anti-aliased silhouette, and derives every flip from the repaired face pair.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-MAGIC-SPECIALS-CANDIDATE-v1"
COIN = BATCH / "petrificus_coin"
FACE_BOUNDS = (62, 70, 962, 952)
FLIP_WIDTHS = (900, 845, 672, 415, 112, 415, 672, 845, 900)
STATES = ("vivid", "flat", "faded", "exhausted")
STATE_FILES = {
    "vivid": "state-01-vivid.png",
    "flat": "state-02-flat.png",
    "faded": "state-03-faded.png",
    "exhausted": "state-04-exhausted.png",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save_png(image: Image.Image, path: Path) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    image.save(temporary, format="PNG", compress_level=6)
    with Image.open(temporary) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (1024, 1024):
            raise ValueError(f"invalid repaired PNG: {path}")
    temporary.replace(path)


def shared_alpha() -> Image.Image:
    scale = 4
    large = Image.new("L", (1024 * scale, 1024 * scale), 0)
    x0, y0, x1, y1 = FACE_BOUNDS
    ImageDraw.Draw(large).ellipse(
        (x0 * scale, y0 * scale, x1 * scale - 1, y1 * scale - 1),
        fill=255,
    )
    alpha = large.resize((1024, 1024), Image.Resampling.LANCZOS)
    values = np.asarray(alpha).copy()
    values[:y0, :] = 0
    values[y1:, :] = 0
    values[:, :x0] = 0
    values[:, x1:] = 0
    return Image.fromarray(values, "L")


def inward_samples(rgb: np.ndarray, rho: np.ndarray, target_rho: float) -> np.ndarray:
    yy, xx = np.mgrid[:1024, :1024]
    center_x, center_y = 511.5, 510.5
    target = np.minimum(rho, target_rho)
    scale = np.divide(target, rho, out=np.zeros_like(rho), where=rho > 0)
    sample_x = np.clip(np.rint(center_x + (xx - center_x) * scale).astype(int), 0, 1023)
    sample_y = np.clip(np.rint(center_y + (yy - center_y) * scale).astype(int), 0, 1023)
    return rgb[sample_y, sample_x]


def cutout_boundary(alpha: np.ndarray, kernel_size: int = 11) -> np.ndarray:
    visible = alpha > 0
    radius = kernel_size // 2
    padded = np.pad(visible.astype(np.uint8), ((radius, radius), (radius, radius)))
    integral = np.pad(padded, ((1, 0), (1, 0))).cumsum(axis=0).cumsum(axis=1)
    counts = (
        integral[kernel_size:, kernel_size:]
        - integral[:-kernel_size, kernel_size:]
        - integral[kernel_size:, :-kernel_size]
        + integral[:-kernel_size, :-kernel_size]
    )
    return visible & (counts != kernel_size * kernel_size)


def repair_face(path: Path, alpha: Image.Image) -> Image.Image:
    source = Image.open(path).convert("RGBA")
    rgba = np.asarray(source).copy()
    rgb = rgba[:, :, :3]
    old_alpha = rgba[:, :, 3]
    new_alpha = np.asarray(alpha)

    yy, xx = np.mgrid[:1024, :1024]
    rho = np.sqrt(((xx - 511.5) / 450.0) ** 2 + ((yy - 510.5) / 441.0) ** 2)
    near = inward_samples(rgb, rho, 0.975)
    deep = inward_samples(rgb, rho, 0.94)
    deep_clean = inward_samples(rgb, rho, 0.90)

    near_max = near.max(axis=2)
    near_min = near.min(axis=2)
    near_pale = (near_min > 220) & ((near_max - near_min) < 35)
    sampled = near.copy()
    sampled[near_pale] = deep[near_pale]

    boundary = cutout_boundary(new_alpha)
    sampled_pale = (
        boundary
        & (sampled[:, :, 0] >= 225)
        & (sampled[:, :, 1] >= 215)
        & (sampled[:, :, 2] >= 158)
        & ((sampled[:, :, 0].astype(np.int16) - sampled[:, :, 2].astype(np.int16)) <= 96)
    )
    sampled[sampled_pale, 1] = np.minimum(sampled[sampled_pale, 1], sampled[sampled_pale, 0] - 12)
    sampled[sampled_pale, 2] = np.minimum(sampled[sampled_pale, 2], sampled[sampled_pale, 0] - 100)

    maximum = rgb.max(axis=2)
    minimum = rgb.min(axis=2)
    saturation = maximum - minimum
    pale = (minimum > 230) & (saturation < 25)
    red_spill = (rgb[:, :, 0] > 190) & (rgb[:, :, 0] > rgb[:, :, 1] * 1.55) & (rgb[:, :, 2] < 70)
    yellow_spill = (rgb[:, :, 0] > 210) & (rgb[:, :, 1] > 205) & (rgb[:, :, 2] < 22)
    visible = new_alpha > 0
    extreme_spill = visible & (rho >= 0.92) & (
        ((rgb[:, :, 0] >= 235) & (rgb[:, :, 1] <= 45) & (rgb[:, :, 2] <= 85))
        | ((rgb[:, :, 0] >= 235) & (rgb[:, :, 1] >= 210) & (rgb[:, :, 2] <= 40))
    )

    contaminated = visible & (rho >= 0.965) & (pale | red_spill | yellow_spill)
    newly_revealed = new_alpha > (old_alpha.astype(np.int16) + 8)
    outer_rim = visible & (rho >= 0.95)
    repair = boundary | contaminated | newly_revealed | extreme_spill
    rgb[repair] = sampled[repair]
    rgb[extreme_spill] = deep_clean[extreme_spill]
    rgb[outer_rim] = deep_clean[outer_rim]

    final_pale_boundary = (
        boundary
        & (rgb[:, :, 0] >= 225)
        & (rgb[:, :, 1] >= 215)
        & (rgb[:, :, 2] >= 158)
        & ((rgb[:, :, 0].astype(np.int16) - rgb[:, :, 2].astype(np.int16)) <= 96)
    )
    rgb[final_pale_boundary, 1] = np.minimum(rgb[final_pale_boundary, 1], rgb[final_pale_boundary, 0] - 12)
    rgb[final_pale_boundary, 2] = np.minimum(rgb[final_pale_boundary, 2], rgb[final_pale_boundary, 0] - 100)

    rgba[:, :, :3] = rgb
    rgba[:, :, 3] = new_alpha
    rgba[new_alpha == 0, :3] = 0
    repaired = Image.fromarray(rgba, "RGBA")
    if repaired.getchannel("A").getbbox() != FACE_BOUNDS:
        raise ValueError(f"shared coin bounds drifted: {path}")
    return repaired


def resize_rgba_premultiplied(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA")).astype(np.float32) / 255.0
    alpha = rgba[:, :, 3:4]
    premultiplied = np.concatenate((rgba[:, :, :3] * alpha, alpha), axis=2)
    packed = Image.fromarray(np.clip(np.rint(premultiplied * 255.0), 0, 255).astype(np.uint8), "RGBA")
    resized = np.asarray(packed.resize(size, Image.Resampling.LANCZOS)).astype(np.float32) / 255.0
    resized_alpha = resized[:, :, 3:4]
    straight_rgb = np.divide(
        resized[:, :, :3],
        resized_alpha,
        out=np.zeros_like(resized[:, :, :3]),
        where=resized_alpha > 1e-6,
    )
    straight = np.concatenate((straight_rgb, resized_alpha), axis=2)
    result = np.clip(np.rint(straight * 255.0), 0, 255).astype(np.uint8)
    boundary = cutout_boundary(result[:, :, 3])
    pale = (
        boundary
        & (result[:, :, 0] >= 225)
        & (result[:, :, 1] >= 215)
        & (result[:, :, 2] >= 158)
        & ((result[:, :, 0].astype(np.int16) - result[:, :, 2].astype(np.int16)) <= 96)
    )
    result[pale, 1] = np.minimum(result[pale, 1], result[pale, 0] - 12)
    result[pale, 2] = np.minimum(result[pale, 2], result[pale, 0] - 100)
    result[result[:, :, 3] == 0, :3] = 0
    return Image.fromarray(result, "RGBA")


def sanitize_canvas_boundary(image: Image.Image) -> Image.Image:
    result = np.asarray(image.convert("RGBA")).copy()
    boundary = cutout_boundary(result[:, :, 3])
    pale = (
        boundary
        & (result[:, :, 0] >= 225)
        & (result[:, :, 1] >= 215)
        & (result[:, :, 2] >= 158)
        & ((result[:, :, 0].astype(np.int16) - result[:, :, 2].astype(np.int16)) <= 96)
    )
    result[pale, 1] = np.minimum(result[pale, 1], result[pale, 0] - 12)
    result[pale, 2] = np.minimum(result[pale, 2], result[pale, 0] - 100)
    result[result[:, :, 3] == 0, :3] = 0
    return Image.fromarray(result, "RGBA")


def rebuild_flip(front: Image.Image, back: Image.Image, state: str) -> None:
    destination = COIN / "flip" / state / "front-to-back"
    destination.mkdir(parents=True, exist_ok=True)
    for index, width in enumerate(FLIP_WIDTHS, start=1):
        face = front if index <= 5 else back
        compressed = resize_rgba_premultiplied(face.crop(FACE_BOUNDS), (width, FACE_BOUNDS[3] - FACE_BOUNDS[1]))
        frame = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
        frame.paste(compressed, ((1024 - width) // 2, FACE_BOUNDS[1]))
        save_png(sanitize_canvas_boundary(frame), destination / f"frame-{index:03d}.png")


def build_review_preview(faces: list[tuple[str, str, Image.Image]], path: Path) -> None:
    frames: list[Image.Image] = []
    for _, _, face in faces:
        compact = face.resize((256, 256), Image.Resampling.LANCZOS)
        review = Image.new("RGB", (512, 256), (245, 245, 245))
        review.paste((32, 36, 43), (256, 0, 512, 256))
        review.paste(compact, (0, 0), compact)
        review.paste(compact, (256, 0), compact)
        frames.append(review)
    temporary = path.with_suffix(path.suffix + ".tmp")
    frames[0].save(
        temporary,
        format="GIF",
        save_all=True,
        append_images=frames[1:],
        duration=700,
        loop=0,
        disposal=2,
    )
    temporary.replace(path)


def refresh_manifests(preview: Path) -> None:
    checksum_lines = []
    runtime_paths = sorted(
        list((COIN / "front").glob("*.png"))
        + list((COIN / "back").glob("*.png"))
        + list((COIN / "flip").glob("*/front-to-back/*.png"))
    )
    if len(runtime_paths) != 44:
        raise ValueError(f"expected 44 coin runtime PNGs, found {len(runtime_paths)}")
    for path in runtime_paths:
        checksum_lines.append(f"{sha256(path)}  {path.relative_to(BATCH).as_posix()}")
    (BATCH / "coin-checksums.sha256").write_text("\n".join(checksum_lines) + "\n", encoding="utf-8")

    coin_manifest_path = BATCH / "coin-manifest.json"
    coin_manifest = json.loads(coin_manifest_path.read_text(encoding="utf-8"))
    coin_manifest["edge_baseline"] = {
        "profile": "shared_complete_antialiased_ellipse_v1",
        "visible_bounds": {"x": 62, "y": 70, "width": 900, "height": 882},
        "faces_share_exact_alpha": True,
        "transparent_rgb_zeroed": True,
        "flip_widths": list(FLIP_WIDTHS),
        "repair_scope": "outer cutout band and deterministic flip derivation only",
    }
    revision_id = "2026-08-24-shared-complete-edge-baseline"
    note = {
        "id": revision_id,
        "date": "2026-08-24",
        "change": "Unified all eight coin faces on one complete anti-aliased outer contour, removed white/red/yellow cutout contamination from inward rim samples, zeroed transparent RGB, and rebuilt all 36 flip frames from the repaired face pairs. Interior coin artwork and runtime gates remain unchanged.",
        "runtime_validation": "pending",
        "runtime_approved": False,
        "runtime_use": False,
    }
    revisions = [item for item in coin_manifest.get("revision_notes", []) if item.get("id") != revision_id]
    revisions.append(note)
    coin_manifest["revision_notes"] = revisions
    preview_record = {
        "kind": "final-shared-edge-baseline-review",
        "path": preview.relative_to(BATCH).as_posix(),
        "frames": 8,
        "frame_duration_ms": 700,
        "sha256": sha256(preview),
    }
    previews = [item for item in coin_manifest.get("previews", []) if item.get("kind") != preview_record["kind"]]
    previews.append(preview_record)
    coin_manifest["previews"] = previews
    coin_manifest_path.write_text(json.dumps(coin_manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    manifest_path = BATCH / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    vivid_front = COIN / "front/state-01-vivid.png"
    for action in manifest["actions"]:
        for phase in action["phases"]:
            for frame in phase["frames"]:
                if frame["path"] == "petrificus_coin/front/state-01-vivid.png":
                    frame["sha256"] = sha256(vivid_front)
                    frame["bytes"] = vivid_front.stat().st_size
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    alpha = shared_alpha()
    repaired: dict[tuple[str, str], Image.Image] = {}
    review_faces: list[tuple[str, str, Image.Image]] = []
    for state in STATES:
        filename = STATE_FILES[state]
        for side in ("front", "back"):
            path = COIN / side / filename
            face = repair_face(path, alpha)
            save_png(face, path)
            repaired[(state, side)] = face
            review_faces.append((state, side, face))
        rebuild_flip(repaired[(state, "front")], repaired[(state, "back")], state)

    preview = COIN / "previews/coin-edge-baseline-preview.gif"
    build_review_preview(review_faces, preview)
    refresh_manifests(preview)
    for temporary in COIN.rglob("*.tmp"):
        temporary.unlink()
    print("repaired 8 coin faces, rebuilt 36 flip frames, and refreshed manifests/checksums")


if __name__ == "__main__":
    main()
