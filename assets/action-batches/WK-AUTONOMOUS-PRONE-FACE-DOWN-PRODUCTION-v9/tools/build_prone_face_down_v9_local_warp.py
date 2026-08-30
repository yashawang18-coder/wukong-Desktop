#!/usr/bin/env python3
"""Build the v9 head/neck lowering trajectory while preserving source pixels."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil

import numpy as np
from PIL import Image


def sigmoid(value: np.ndarray) -> np.ndarray:
    return 1.0 / (1.0 + np.exp(-np.clip(value, -60.0, 60.0)))


def smoothstep(value: np.ndarray) -> np.ndarray:
    value = np.clip(value, 0.0, 1.0)
    return value * value * (3.0 - 2.0 * value)


def sample_bilinear(channel: np.ndarray, source_x: np.ndarray, source_y: np.ndarray) -> np.ndarray:
    """Sample a 2D channel with deterministic bilinear interpolation."""

    height, width = channel.shape
    x0 = np.floor(source_x).astype(np.int32)
    y0 = np.floor(source_y).astype(np.int32)
    x1 = x0 + 1
    y1 = y0 + 1
    wx = source_x - x0
    wy = source_y - y0

    result = np.zeros(source_x.shape, dtype=np.float32)
    for sample_x, sample_y, weight in (
        (x0, y0, (1.0 - wx) * (1.0 - wy)),
        (x1, y0, wx * (1.0 - wy)),
        (x0, y1, (1.0 - wx) * wy),
        (x1, y1, wx * wy),
    ):
        valid = (
            (sample_x >= 0)
            & (sample_x < width)
            & (sample_y >= 0)
            & (sample_y < height)
        )
        clipped_x = np.clip(sample_x, 0, width - 1)
        clipped_y = np.clip(sample_y, 0, height - 1)
        result += np.where(valid, channel[clipped_y, clipped_x] * weight, 0.0)
    return result


def remove_blue_spill(image: Image.Image) -> Image.Image:
    """Neutralize chroma-blue spill without changing alpha or non-blue pixels."""

    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    red = rgba[..., 0].astype(np.int16)
    green = rgba[..., 1].astype(np.int16)
    blue = rgba[..., 2].astype(np.int16)
    limit = np.maximum(red, green) + 2
    spill = (rgba[..., 3] > 0) & (blue > limit)
    rgba[..., 2][spill] = np.clip(limit[spill], 0, 255).astype(np.uint8)
    return Image.fromarray(rgba)


def save_png_atomic(image: Image.Image, output: Path) -> None:
    temporary = output.with_suffix(output.suffix + ".tmp")
    for attempt in range(3):
        image.save(temporary, format="PNG", optimize=True)
        with temporary.open("rb") as stream:
            os.fsync(stream.fileno())
        try:
            with Image.open(temporary) as check:
                check.load()
                if check.size != image.size or check.mode != "RGBA":
                    raise OSError("PNG contract changed during write")
            temporary.replace(output)
            temporary.unlink(missing_ok=True)
            return
        except OSError:
            temporary.unlink(missing_ok=True)
            if attempt == 2:
                raise


def displacement(
    source_x: np.ndarray,
    source_y: np.ndarray,
    amount: float,
    horizontal: float,
) -> tuple[np.ndarray, np.ndarray]:
    # Lock the torso at the right, the paws below, and all transparent surroundings.
    left = sigmoid((source_x - 180.0) / 18.0)
    # Compact-support ramps make the protected rear torso (x >= 650) and paws
    # (y >= 790) exactly motionless, not merely visually close to motionless.
    right = smoothstep((650.0 - source_x) / 100.0)
    top = sigmoid((source_y - 405.0) / 18.0)
    above_paws = smoothstep((790.0 - source_y) / 70.0)
    weight = left * right * top * above_paws

    # The face moves almost rigidly.  The neck/shoulder transition moves less so
    # that it remains joined to the frozen torso rather than becoming a cutout.
    neck_falloff = 0.68 + 0.32 * sigmoid((470.0 - source_x) / 28.0)
    dy = amount * weight * neck_falloff

    # A tiny forward drift makes the down motion pivot from the shoulder instead
    # of looking like a vertical elevator.  It remains far smaller than dy.
    dx = (
        -0.08 * amount * weight * sigmoid((455.0 - source_x) / 32.0)
        + horizontal * weight
    )
    return dx, dy


def warp(image: Image.Image, amount: float, horizontal: float = 0.0) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.float32)
    height, width = rgba.shape[:2]
    dest_y, dest_x = np.mgrid[0:height, 0:width].astype(np.float32)

    source_x = dest_x.copy()
    source_y = dest_y.copy()
    for _ in range(7):
        dx, dy = displacement(source_x, source_y, amount, horizontal)
        source_x = dest_x - dx
        source_y = dest_y - dy

    channels = [sample_bilinear(rgba[..., channel], source_x, source_y) for channel in range(4)]
    result = np.stack(channels, axis=-1)

    # Pixels wholly outside the moving field remain byte-identical to the source.
    dx, dy = displacement(dest_x, dest_y, amount, horizontal)
    frozen = (np.abs(dx) + np.abs(dy)) < 0.02
    result[frozen] = rgba[frozen]
    return remove_blue_spill(
        Image.fromarray(np.clip(np.rint(result), 0, 255).astype(np.uint8))
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument("--amounts", type=float, nargs="+", default=[45, 70, 95, 120])
    parser.add_argument("--horizontal", type=float, default=0.0)
    parser.add_argument("--production-sequence", action="store_true")
    parser.add_argument("--anchor", type=Path)
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    with Image.open(args.source) as source:
        source.load()
        if args.production_sequence:
            if args.anchor is None:
                parser.error("--anchor is required with --production-sequence")
            schedule = [
                (-36, 28),
                (-24, 26),
                (-12, 24),
                (0, 22),
                (15, 20),
                (30, 18),
                (45, 16),
                (60, 14),
                (70, 12),
                (80, 10),
            ]
            shutil.copyfile(args.anchor, args.output_dir / "frame-001.png")
            for frame_index, (amount, horizontal) in enumerate(schedule, start=2):
                output = args.output_dir / f"frame-{frame_index:03d}.png"
                save_png_atomic(warp(source, amount, horizontal), output)
                print(output)
            shutil.copyfile(
                args.output_dir / "frame-011.png", args.output_dir / "frame-012.png"
            )
            return
        for amount in args.amounts:
            output = args.output_dir / f"lower-{int(amount):03d}.png"
            save_png_atomic(warp(source, amount, args.horizontal), output)
            print(output)


if __name__ == "__main__":
    main()
