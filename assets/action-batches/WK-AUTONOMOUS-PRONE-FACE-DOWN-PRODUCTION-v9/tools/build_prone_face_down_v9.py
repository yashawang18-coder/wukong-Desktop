#!/usr/bin/env python3
"""Deterministic assembly helpers for the prone-face-down v9 candidate.

Image generation is used only for the coherent pose artwork.  This script
performs repeatable chroma removal, canvas alignment, byte-copy phases, calm
holds, and review derivatives without regenerating Wukong pixels.
"""

from __future__ import annotations

import argparse
from collections import deque
import hashlib
import json
import os
from pathlib import Path
import shutil

from PIL import Image, ImageDraw, ImageFont


CANVAS = (1024, 1024)
SETTLE_DURATIONS = [180, 140, 130, 130, 120, 120, 110, 110, 110, 120, 130, 220]
CALM_DURATIONS = [2200, 1000, 500, 1000, 1800, 1200, 900, 500, 900, 1000, 1000, 2000]
RISE_DURATIONS = [220, 130, 120, 110, 110, 110, 120, 120, 130, 130, 140, 180]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _save_png_atomic(image: Image.Image, output: Path, *, optimize: bool) -> None:
    """Write, fsync, fully decode, then atomically install a PNG."""

    temporary = output.with_suffix(output.suffix + ".tmp")
    for attempt in range(3):
        image.save(temporary, format="PNG", optimize=optimize)
        with temporary.open("rb") as stream:
            os.fsync(stream.fileno())
        try:
            with Image.open(temporary) as check:
                check.load()
                if check.size != image.size:
                    raise OSError("PNG dimensions changed during write")
            temporary.replace(output)
            temporary.unlink(missing_ok=True)
            return
        except OSError:
            temporary.unlink(missing_ok=True)
            if attempt == 2:
                raise


def _smoothstep(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def chroma_key_blue(source: Image.Image) -> Image.Image:
    """Remove a blue-screen background using a deterministic color rule."""

    rgb = source.convert("RGB")
    output = Image.new("RGBA", rgb.size)
    keyed = []
    for red, green, blue in rgb.get_flattened_data():
        blue_dominance = blue - max(red, green)
        # Solid/gradient blue is transparent at >= 62.  The 18..62 interval
        # preserves fine edge hairs with a deterministic soft transition.
        alpha = round(255 * (1.0 - _smoothstep((blue_dominance - 18) / 44)))
        if alpha:
            # Remove blue spill only; never alter red/green coat channels.
            blue = min(blue, max(red, green) + 8)
        keyed.append((red, green, blue, alpha))
    output.putdata(keyed)
    return output


def keep_largest_alpha_component(source: Image.Image, threshold: int = 16) -> Image.Image:
    """Discard disconnected chroma-key fragments from neighboring storyboard cells."""

    image = source.copy()
    alpha = image.getchannel("A")
    width, height = image.size
    visible = bytearray(1 if value > threshold else 0 for value in alpha.get_flattened_data())
    visited = bytearray(width * height)
    largest: list[int] = []

    for start, is_visible in enumerate(visible):
        if not is_visible or visited[start]:
            continue
        visited[start] = 1
        queue = deque([start])
        component: list[int] = []
        while queue:
            index = queue.popleft()
            component.append(index)
            x = index % width
            y = index // width
            for neighbor in (
                index - 1 if x else -1,
                index + 1 if x + 1 < width else -1,
                index - width if y else -1,
                index + width if y + 1 < height else -1,
            ):
                if neighbor >= 0 and visible[neighbor] and not visited[neighbor]:
                    visited[neighbor] = 1
                    queue.append(neighbor)
        if len(component) > len(largest):
            largest = component

    keep = bytearray(width * height)
    for index in largest:
        keep[index] = 1
    pixels = []
    for index, (red, green, blue, value) in enumerate(image.get_flattened_data()):
        pixels.append((red, green, blue, value if keep[index] else 0))
    image.putdata(pixels)
    return image


def normalize_to_anchor(
    source: Image.Image, anchor: Image.Image, target_width: int | None = None
) -> Image.Image:
    """Scale the keyed subject to the anchor width and ground baseline."""

    source_box = source.getchannel("A").getbbox()
    anchor_box = anchor.getchannel("A").getbbox()
    if source_box is None or anchor_box is None:
        raise ValueError("source and anchor must contain non-empty alpha")

    subject = source.crop(source_box)
    anchor_width = anchor_box[2] - anchor_box[0]
    width = target_width or anchor_width
    height = round(subject.height * width / subject.width)
    subject = subject.resize((width, height), Image.Resampling.LANCZOS)

    anchor_center_x = (anchor_box[0] + anchor_box[2]) / 2
    left = round(anchor_center_x - width / 2)
    top = anchor_box[3] - height
    if left < 0 or top < 0 or left + width > CANVAS[0] or top + height > CANVAS[1]:
        raise ValueError("normalized subject would leave the 1024x1024 canvas")

    canvas = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    canvas.alpha_composite(subject, (left, top))
    return canvas


def calm_breath(source: Image.Image, strength: float) -> Image.Image:
    """Move only internal rear-rib color by at most one pixel; alpha is frozen."""

    image = source.convert("RGBA")
    output = image.copy()
    source_pixels = image.load()
    output_pixels = output.load()
    center_x, center_y = 700.0, 720.0
    radius_x, radius_y = 145.0, 105.0
    for y in range(580, 850):
        for x in range(520, 875):
            distance = ((x - center_x) / radius_x) ** 2 + ((y - center_y) / radius_y) ** 2
            if distance >= 1.0:
                continue
            feather = (1.0 - distance) ** 2 * strength
            red, green, blue, alpha = source_pixels[x, y]
            shifted = source_pixels[x, min(1023, y + 1)]
            output_pixels[x, y] = (
                round(red * (1 - feather) + shifted[0] * feather),
                round(green * (1 - feather) + shifted[1] * feather),
                round(blue * (1 - feather) + shifted[2] * feather),
                alpha,
            )
    return output


def _global_palette(frames: list[Image.Image], background: tuple[int, int, int], colors: int) -> Image.Image:
    rendered = []
    for frame in frames:
        matte = Image.new("RGBA", frame.size, (*background, 255))
        matte.alpha_composite(frame)
        rendered.append(matte.convert("RGB").resize((256, 256), Image.Resampling.LANCZOS))
    sheet = Image.new("RGB", (256 * len(rendered), 256))
    for index, frame in enumerate(rendered):
        sheet.paste(frame, (index * 256, 0))
    return sheet.quantize(colors=colors, method=Image.Quantize.MEDIANCUT)


def write_gif(
    frames: list[Image.Image],
    durations: list[int],
    output: Path,
    background: tuple[int, int, int] | None,
) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    matte = background or (0, 0, 0)
    # Reserve palette index 255 as a duplicate-color sentinel.  Pillow merges
    # consecutive identical GIF frames even with optimize=False; alternating a
    # same-RGB index at one stable pixel preserves the required physical frame
    # count without changing the rendered image.
    master = _global_palette(frames, matte, 254 if background is None else 255)
    encoded = []
    for frame in frames:
        rgba = frame.resize((384, 384), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", rgba.size, (*matte, 255))
        canvas.alpha_composite(rgba)
        quantized = canvas.convert("RGB").quantize(palette=master, dither=Image.Dither.NONE)
        if background is None:
            old_palette = master.getpalette()[: 255 * 3]
            new_palette = [0, 0, 0] + old_palette
            new_palette.extend([0] * (768 - len(new_palette)))
            values = bytearray(value + 1 for value in quantized.get_flattened_data())
            alpha = rgba.getchannel("A")
            for index, value in enumerate(alpha.get_flattened_data()):
                if value < 16:
                    values[index] = 0
            quantized.putdata(values)
            quantized.putpalette(new_palette)
            quantized.info["transparency"] = 0
        encoded.append(quantized)

    if background is None:
        # Find a fully opaque pixel whose quantized color is stable across the
        # lifecycle (normally in the frozen rear torso).  Index 255 duplicates
        # that exact RGB value, so toggling indices is visually lossless.
        alpha_frames = [
            frame.resize((384, 384), Image.Resampling.LANCZOS).getchannel("A")
            for frame in frames
        ]
        sentinel = None
        for y in range(383, -1, -1):
            for x in range(383, -1, -1):
                if not all(alpha.getpixel((x, y)) > 250 for alpha in alpha_frames):
                    continue
                indices = [frame.getpixel((x, y)) for frame in encoded]
                if len(set(indices)) == 1 and indices[0] not in (0, 255):
                    sentinel = (x, y, indices[0])
                    break
            if sentinel:
                break
        if sentinel is None:
            raise ValueError("could not find a stable opaque GIF sentinel pixel")
    else:
        # The top-left pixel is fixed matte in every opaque review GIF.
        sentinel = (0, 0, encoded[0].getpixel((0, 0)))

    sentinel_x, sentinel_y, base_index = sentinel
    for frame_index, frame in enumerate(encoded):
        palette = frame.getpalette()
        base_offset = base_index * 3
        palette[255 * 3 : 255 * 3 + 3] = palette[base_offset : base_offset + 3]
        frame.putpalette(palette)
        frame.putpixel(
            (sentinel_x, sentinel_y), 255 if frame_index % 2 else base_index
        )
    save_options = {
        "save_all": True,
        "append_images": encoded[1:],
        "duration": durations,
        "loop": 0,
        "optimize": False,
        "disposal": 2,
    }
    if background is None:
        save_options["transparency"] = 0
    encoded[0].save(output, **save_options)


def write_review_board(
    frames: list[Image.Image],
    labels: list[str],
    output: Path,
    background: tuple[int, int, int],
    columns: int,
    tile_size: int = 256,
    crop: tuple[int, int, int, int] | None = None,
) -> None:
    """Write a fixed-background contact sheet without changing source PNGs."""

    output.parent.mkdir(parents=True, exist_ok=True)
    font = ImageFont.load_default(size=22)
    tiles: list[Image.Image] = []
    for frame, label in zip(frames, labels, strict=True):
        canvas = Image.new("RGBA", frame.size, (*background, 255))
        canvas.alpha_composite(frame)
        rendered = canvas.convert("RGB")
        if crop is not None:
            rendered = rendered.crop(crop)
            if rendered.size != (tile_size, tile_size):
                rendered = rendered.resize((tile_size, tile_size), Image.Resampling.LANCZOS)
            tile = rendered
            text_origin = (10, 8)
        else:
            rendered = rendered.resize((tile_size, tile_size), Image.Resampling.LANCZOS)
            tile = Image.new("RGB", (tile_size, tile_size + 30), background)
            tile.paste(rendered, (0, 30))
            text_origin = (10, 5)
        ink = (28, 28, 28) if sum(background) > 400 else (238, 238, 238)
        ImageDraw.Draw(tile).text(text_origin, label, fill=ink, font=font)
        tiles.append(tile)

    rows = (len(tiles) + columns - 1) // columns
    board = Image.new("RGB", (columns * tiles[0].width, rows * tiles[0].height), background)
    for index, tile in enumerate(tiles):
        board.paste(tile, ((index % columns) * tile.width, (index // columns) * tile.height))
    _save_png_atomic(board, output, optimize=True)


def write_review_artifacts(package: Path, frames: list[Image.Image]) -> None:
    labels = [f"F{index:02d}" for index in range(1, 37)]
    settle = frames[:12]
    review = package / "review"
    for name, color in (("light", (242, 240, 236)), ("dark", (31, 35, 39))):
        write_review_board(
            frames,
            labels,
            review / f"full-36-{name}-board.png",
            color,
            columns=6,
        )
        write_review_board(
            settle,
            labels[:12],
            review / f"settle-12-{name}-board.png",
            color,
            columns=4,
        )

    face_crop = (112, 400, 624, 912)
    write_review_board(
        settle,
        labels[:12],
        review / "face-continuity-12-1x-board.png",
        (242, 240, 236),
        columns=4,
        tile_size=512,
        crop=face_crop,
    )
    write_review_board(
        settle[4:8],
        labels[4:8],
        review / "f05-f08-face-mouth-1x-board.png",
        (242, 240, 236),
        columns=4,
        tile_size=512,
        crop=face_crop,
    )


def assemble_package(package: Path) -> None:
    settle = package / "frames" / "settle-to-face-down"
    calm = package / "frames" / "face-down-calm"
    rise = package / "frames" / "rise-to-down-anchor"
    calm.mkdir(parents=True, exist_ok=True)
    rise.mkdir(parents=True, exist_ok=True)

    endpoint = Image.open(settle / "frame-011.png").convert("RGBA")
    calm_strengths = [0.0, 0.45, 1.0, 0.0, 0.0, 0.0, 0.45, 1.0, 0.0, 0.0, 0.0, 0.0]
    for index, strength in enumerate(calm_strengths, start=1):
        output = calm / f"frame-{index:03d}.png"
        if strength == 0:
            shutil.copyfile(settle / "frame-011.png", output)
        else:
            _save_png_atomic(calm_breath(endpoint, strength), output, optimize=False)

    for index, source_index in enumerate(range(12, 0, -1), start=1):
        shutil.copyfile(settle / f"frame-{source_index:03d}.png", rise / f"frame-{index:03d}.png")

    ordered_paths = (
        [settle / f"frame-{index:03d}.png" for index in range(1, 13)]
        + [calm / f"frame-{index:03d}.png" for index in range(1, 13)]
        + [rise / f"frame-{index:03d}.png" for index in range(1, 13)]
    )
    frames = [Image.open(path).convert("RGBA") for path in ordered_paths]
    durations = SETTLE_DURATIONS + CALM_DURATIONS + RISE_DURATIONS
    animation = package / "animations"
    write_gif(frames, durations, animation / "full-lifecycle-transparent.gif", None)
    write_gif(frames, durations, animation / "full-lifecycle-light.gif", (242, 240, 236))
    write_gif(frames, durations, animation / "full-lifecycle-dark.gif", (28, 31, 36))
    write_review_artifacts(package, frames)

    timing = {
        "frame_count": 36,
        "total_duration_ms": sum(durations),
        "durations_ms": durations,
        "phases": {
            "settle-to-face-down": SETTLE_DURATIONS,
            "face-down-calm": CALM_DURATIONS,
            "rise-to-down-anchor": RISE_DURATIONS,
        },
    }
    (package / "frame-timing.json").write_text(
        json.dumps(timing, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    hashes = []
    for path in sorted(package.rglob("*.png")) + sorted(package.rglob("*.gif")):
        hashes.append(f"{_sha256(path)}  {path.relative_to(package).as_posix()}")
    (package / "SHA256SUMS").write_text("\n".join(hashes) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--generated", type=Path)
    parser.add_argument("--anchor", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--target-width", type=int)
    parser.add_argument("--assemble-package", type=Path)
    args = parser.parse_args()

    if args.assemble_package:
        assemble_package(args.assemble_package)
        return
    if not args.generated or not args.anchor or not args.output:
        parser.error("--generated, --anchor, and --output are required for normalization")

    anchor = Image.open(args.anchor).convert("RGBA")
    generated = Image.open(args.generated)
    normalized = normalize_to_anchor(
        keep_largest_alpha_component(chroma_key_blue(generated)),
        anchor,
        target_width=args.target_width,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    _save_png_atomic(normalized, args.output, optimize=False)

    print(f"output={args.output}")
    print(f"size={normalized.size}")
    print(f"mode={normalized.mode}")
    print(f"alpha_bbox={normalized.getchannel('A').getbbox()}")
    print(f"sha256={_sha256(args.output)}")


if __name__ == "__main__":
    main()
