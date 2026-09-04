#!/usr/bin/env python3
"""Build the side-prone front-observation v5 review/runtime candidate."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-AUTONOMOUS-SIDE-PRONE-FRONT-PRODUCTION-v5"
V3_SIDE = (
    ROOT
    / "assets/action-batches/WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED"
    / "frames/microloops/prone-idle-legacy-side"
)

SOURCE_NAMES = {
    "turn_52": "turn-52.png",
    "turn_35": "turn-35.png",
    "turn_15": "turn-15.png",
    "front": "front-neutral.png",
    "blink": "front-blink.png",
    "ear": "front-ear-twitch.png",
}

BRIDGE_POSES = [None, None, "turn_52", "turn_52", "turn_35", "turn_35", "turn_15", "turn_15", "front", "front", "front", "front"]
CALM_POSES = ["front", "front", "front", "front", "front", "blink", "front", "front", "ear", "ear", "front", "front"]
CALM_Y_OFFSETS = [0, 0, -1, -1, -1, -1, 0, 0, 1, 1, 0, 0]
CALM_DURATIONS = [1300, 800, 320, 320, 220, 240, 220, 320, 320, 700, 1000, 1500]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def chroma_cutout(path: Path) -> Image.Image:
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    red, green, blue = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dominance = green - np.maximum(red, blue)
    border = np.concatenate(
        (rgb[:32].reshape(-1, 3), rgb[-32:].reshape(-1, 3), rgb[:, :32].reshape(-1, 3), rgb[:, -32:].reshape(-1, 3)),
        axis=0,
    )
    background = np.median(border, axis=0)
    background_dominance = float(background[1] - max(background[0], background[2]))
    alpha_unit = np.clip(1.0 - (dominance - 12.0) / max(background_dominance - 12.0, 1.0), 0.0, 1.0)
    alpha_unit[(green < 105.0) | (dominance < 16.0)] = 1.0
    far_background = (green > 135.0) & (dominance > 108.0) & (green > red * 1.45) & (green > blue * 1.45)
    alpha_unit[far_background] = 0.0
    alpha_unit[alpha_unit < 0.055] = 0.0

    # Undo the green-screen mixture before grading so feathered fur does not
    # acquire a dark or green collar when composited over the V3R1 chest.
    safe_alpha = np.maximum(alpha_unit, 0.035)[..., None]
    rgb = (rgb - (1.0 - safe_alpha) * background[None, None, :]) / safe_alpha
    residual_spill = rgb[..., 1] > np.maximum(rgb[..., 0], rgb[..., 2]) + 8.0
    rgb[..., 1][residual_spill] = np.maximum(rgb[..., 0], rgb[..., 2])[residual_spill] + 8.0
    rgb[alpha_unit < 0.01] = 0.0
    alpha_image = Image.fromarray((alpha_unit * 255.0).astype(np.uint8), "L")
    alpha_image = alpha_image.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(0.75))
    alpha = np.asarray(alpha_image, dtype=np.float32)

    # Bring the generated source closer to the approved V3R1 warm-malt rendering.
    luminance = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    rgb = luminance[..., None] + 0.86 * (rgb - luminance[..., None])
    rgb[..., 0] *= 0.98
    rgb[..., 1] *= 0.96
    rgb[..., 2] *= 0.94

    rgba = np.dstack((np.clip(rgb, 0, 255), alpha)).astype(np.uint8)
    image = Image.fromarray(rgba, "RGBA")
    bbox = image.getchannel("A").point(lambda value: 255 if value > 8 else 0).getbbox()
    if bbox is None:
        raise ValueError(f"No foreground extracted from {path}")

    left, top, right, bottom = bbox
    # The generated busts contain more lower chest than the side-prone composite needs.
    bottom = top + round((bottom - top) * 0.88)
    image = image.crop((left, top, right, bottom))
    scale = min(285 / image.width, 370 / image.height)
    size = (round(image.width * scale), round(image.height * scale))
    image = image.resize(size, Image.Resampling.LANCZOS)

    # The source bust has a hard lower crop; feather it into the frozen V3R1 chest.
    array = np.asarray(image).copy()
    height, width = array.shape[:2]
    yy = np.arange(height, dtype=np.float32)[:, None]
    xx = np.arange(width, dtype=np.float32)[None, :]
    center_distance = np.abs(xx - (width - 1) / 2.0) / max((width - 1) / 2.0, 1.0)
    end = height - 3.0 - 72.0 * center_distance**1.45 - 3.0 * np.sin(xx * 0.17)
    start = end - 104.0
    progress = np.clip((yy - start) / np.maximum(end - start, 1.0), 0.0, 1.0)
    curved_fade = np.cos(progress * np.pi / 2.0) ** 2
    curved_fade[yy >= end] = 0.0
    array[..., 3] = (array[..., 3].astype(np.float32) * curved_fade).astype(np.uint8)
    image = Image.fromarray(array, "RGBA")

    canvas = Image.new("RGBA", (310, 380), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((canvas.width - image.width) // 2, canvas.height - image.height - 3))
    return canvas


def erase_legacy_head(base: Image.Image) -> Image.Image:
    mask = Image.new("L", base.size, 0)
    draw = ImageDraw.Draw(mask)
    draw.polygon(
        [(162, 408), (424, 396), (490, 528), (472, 650), (420, 698), (234, 706), (166, 668)],
        fill=255,
    )
    mask = mask.filter(ImageFilter.GaussianBlur(5.0))
    array = np.asarray(base.convert("RGBA")).copy()
    keep = 1.0 - np.asarray(mask, dtype=np.float32) / 255.0
    array[..., 3] = (array[..., 3].astype(np.float32) * keep).astype(np.uint8)
    return Image.fromarray(array, "RGBA")


def composite(base: Image.Image, cutout: Image.Image, y_offset: int = 0) -> Image.Image:
    image = erase_legacy_head(base)
    image.alpha_composite(cutout, (170, 375 + y_offset))
    return image


def save_frame(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    image.save(temporary, format="PNG")
    temporary.replace(path)


def make_board(title: str, frames: list[Image.Image], path: Path, background: tuple[int, int, int]) -> None:
    columns, cell_w, cell_h, header = 6, 256, 246, 42
    board = Image.new("RGB", (columns * cell_w, header + 2 * cell_h), background)
    draw = ImageDraw.Draw(board)
    font = ImageFont.load_default()
    ink = (242, 244, 247) if sum(background) < 384 else (24, 29, 35)
    draw.text((12, 14), title, fill=ink, font=font)
    for index, frame in enumerate(frames):
        thumb = frame.copy()
        thumb.thumbnail((238, 215), Image.Resampling.LANCZOS)
        x = (index % columns) * cell_w + (cell_w - thumb.width) // 2
        y = header + (index // columns) * cell_h + 20
        tile = Image.new("RGBA", board.size, (0, 0, 0, 0))
        tile.alpha_composite(thumb, (x, y))
        board.paste(tile.convert("RGB"), (0, 0), tile.getchannel("A"))
        draw.text(((index % columns) * cell_w + 8, header + (index // columns) * cell_h + 6), f"F{index + 1:02d}", fill=ink, font=font)
    path.parent.mkdir(parents=True, exist_ok=True)
    board.save(path, format="PNG", optimize=True)


def gif_frame(frame: Image.Image, background: tuple[int, int, int]) -> Image.Image:
    thumb = frame.copy()
    thumb.thumbnail((320, 320), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (360, 360), (*background, 255))
    canvas.alpha_composite(thumb, ((360 - thumb.width) // 2, (360 - thumb.height) // 2))
    return canvas.convert("P", palette=Image.Palette.ADAPTIVE, colors=255)


def frame_record(path: Path, duration_ms: int) -> dict[str, object]:
    return {
        "frame": int(path.stem.rsplit("-", 1)[-1]),
        "path": path.relative_to(BATCH).as_posix(),
        "duration_ms": duration_ms,
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
    }


def update_manifest(raw_sources: dict[str, Path], phase_paths: dict[str, list[Path]]) -> None:
    path = BATCH / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    manifest["status"] = "production_candidate_owner_visual_review_pending"
    manifest["asset_stage"] = "production_candidate_owner_visual_review_pending"
    manifest["runtime_validation"] = "pending_owner_visual_review_and_windows_renderer_ci"
    manifest["visual_approved"] = False
    manifest["runtime_approved"] = False
    manifest["runtime_use"] = False
    manifest["production_asset"] = False
    manifest["autonomous_binding_enabled"] = False
    manifest["allowed_sources"] = ["DeveloperPreview"]
    manifest["source_policy"] = {
        "pose_references": "three owner-supplied Shiba photos guide side-prone anatomy only; they are not repository/runtime assets",
        "identity": "wukong-current-adult-v1 identity board plus the approved V3R1 lively rendering",
        "body": "corresponding V3R1 side-prone microloop pixels remain unchanged at x >= 560 and y >= 760",
        "forward_loop": "new V3R1-body composite with a shared front head/neck master, slow blink and subtle ear twitch",
        "transition_heads": "six built-in image edits, chroma-keyed, despilled, warm-malt graded, lower-neck feathered and composited deterministically",
        "legacy_red_assets": "forbidden",
        "runtime_mirror_used": False,
        "v3r1_source_modified": False,
    }
    specs = [
        ("bridge-to-front", [100] * 12, False),
        ("side-prone-front-calm", CALM_DURATIONS, True),
        ("bridge-to-legacy", [100] * 12, False),
    ]
    manifest["phases"] = []
    for name, durations, loop in specs:
        records = [frame_record(frame, duration) for frame, duration in zip(phase_paths[name], durations)]
        manifest["phases"].append(
            {
                "name": name,
                "frame_count": len(records),
                "total_duration_ms": sum(durations),
                "loop": loop,
                "frames": records,
            }
        )
    manifest["generation_provenance"] = {
        SOURCE_NAMES[key].removesuffix(".png") + "-original": {
            "sha256": sha256(source),
            "committed": False,
        }
        for key, source in raw_sources.items()
    }
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_checksums() -> None:
    paths = sorted(
        path
        for path in BATCH.rglob("*")
        if path.is_file() and path.name != "SHA256SUMS" and not path.name.endswith(".tmp")
    )
    content = "".join(f"{sha256(path)}  {path.relative_to(BATCH).as_posix()}\n" for path in paths)
    (BATCH / "SHA256SUMS").write_text(content, encoding="utf-8", newline="\n")


def build(args: argparse.Namespace) -> None:
    raw_sources = {key: Path(getattr(args, key)).resolve() for key in SOURCE_NAMES}
    source_dir = BATCH / "source/transition-heads"
    source_dir.mkdir(parents=True, exist_ok=True)
    cutouts: dict[str, Image.Image] = {}
    for key, source in raw_sources.items():
        cutout = chroma_cutout(source)
        cutout.save(source_dir / SOURCE_NAMES[key], format="PNG", optimize=True)
        cutouts[key] = cutout

    bases = [Image.open(V3_SIDE / f"frame-{index:03d}.png").convert("RGBA") for index in range(1, 13)]
    forward = [base.copy() if pose is None else composite(base, cutouts[pose]) for base, pose in zip(bases, BRIDGE_POSES)]
    calm = [composite(base, cutouts[pose], offset) for base, pose, offset in zip(bases, CALM_POSES, CALM_Y_OFFSETS)]
    reverse = list(reversed(forward))

    phase_images = {
        "bridge-to-front": forward,
        "side-prone-front-calm": calm,
        "bridge-to-legacy": reverse,
    }
    phase_paths: dict[str, list[Path]] = {}
    for phase, images in phase_images.items():
        phase_paths[phase] = []
        for index, image in enumerate(images, 1):
            path = BATCH / "frames" / phase / f"frame-{index:03d}.png"
            save_frame(image, path)
            phase_paths[phase].append(path)

    for phase, images in phase_images.items():
        for suffix, background in (("dark", (31, 36, 43)), ("light", (244, 241, 234))):
            make_board(phase, images, BATCH / "review" / f"{phase}-{suffix}-board.png", background)

    lifecycle = forward + calm + reverse
    durations = [100] * 12 + CALM_DURATIONS + [100] * 12
    gif = [gif_frame(frame, (31, 36, 43)) for frame in lifecycle]
    gif[0].save(
        BATCH / "review/full-lifecycle.gif",
        save_all=True,
        append_images=gif[1:],
        duration=durations,
        loop=0,
        optimize=False,
        disposal=2,
    )

    update_manifest(raw_sources, phase_paths)
    write_checksums()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    for key in SOURCE_NAMES:
        parser.add_argument("--" + key.replace("_", "-"), dest=key, required=True)
    return parser.parse_args()


if __name__ == "__main__":
    build(parse_args())
