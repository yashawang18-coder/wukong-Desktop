from __future__ import annotations

import os
import shutil
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent
DIRECTIONS = (
    "right",
    "front-right",
    "front",
    "front-left",
    "left",
    "rear-left",
    "rear",
    "rear-right",
)
PAIRS = (
    ("right", "front-right", "right--front-right"),
    ("front-right", "front", "front-right--front"),
    ("front", "front-left", "front--front-left"),
    ("front-left", "left", "front-left--left"),
    ("left", "rear-left", "left--rear-left"),
    ("rear-left", "rear", "rear-left--rear"),
    ("rear", "rear-right", "rear--rear-right"),
    ("rear-right", "right", "rear-right--right"),
)


def atomic_copy(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(".atomic.png")
    shutil.copyfile(source, temporary)
    with Image.open(temporary) as image:
        image.verify()
    os.replace(temporary, destination)


def render_pose(source: Path, destination: Path, height: int, angle: float) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(source) as opened:
        image = opened.convert("RGBA")
    alpha_box = image.getchannel("A").getbbox()
    if alpha_box is None:
        raise ValueError(f"Empty alpha channel: {source}")
    image = image.crop(alpha_box)
    if angle:
        image = image.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)
        rotated_box = image.getchannel("A").getbbox()
        if rotated_box is None:
            raise ValueError(f"Rotation removed subject: {source}")
        image = image.crop(rotated_box)
    width = round(image.width * height / image.height)
    image = image.resize((width, height), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((1024 - width) // 2, 901 - height))
    temporary = destination.with_suffix(".atomic.png")
    canvas.save(temporary, format="PNG", compress_level=6)
    with Image.open(temporary) as check:
        check.verify()
    os.replace(temporary, destination)


def pitch_arrays(direction: str) -> tuple[list[float], list[float]]:
    if direction in {"right", "front-right", "rear-right"}:
        return (
            [0.0, 0.08, 0.18, 0.32, 0.14, 0.0],
            [0.0, -0.08, -0.18, -0.34, -0.14, 0.0],
        )
    if direction in {"left", "front-left", "rear-left"}:
        return (
            [0.0, -0.08, -0.18, -0.32, -0.14, 0.0],
            [0.0, 0.08, 0.18, 0.34, 0.14, 0.0],
        )
    return ([0.0] * 6, [0.0] * 6)


def make_gif(frame_paths: list[Path], destination: Path) -> None:
    frames: list[Image.Image] = []
    for path in frame_paths:
        with Image.open(path) as opened:
            rgba = opened.convert("RGBA")
        background = Image.new("RGBA", rgba.size, (238, 238, 238, 255))
        background.alpha_composite(rgba)
        frame = background.convert("RGB").resize((512, 512), Image.Resampling.LANCZOS)
        frames.append(frame)
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(".atomic.gif")
    frames[0].save(
        temporary,
        save_all=True,
        append_images=frames[1:],
        duration=90,
        loop=0,
        disposal=2,
        optimize=False,
    )
    with Image.open(temporary) as check:
        if getattr(check, "n_frames", 1) < 1:
            raise ValueError(f"Empty GIF: {destination}")
    os.replace(temporary, destination)


def make_contact_sheet(frame_paths: list[Path], destination: Path, columns: int, cell_size: int) -> None:
    rows = (len(frame_paths) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_size, rows * cell_size), (217, 217, 217))
    for index, path in enumerate(frame_paths):
        with Image.open(path) as opened:
            rgba = opened.convert("RGBA")
        background = Image.new("RGBA", rgba.size, (217, 217, 217, 255))
        background.alpha_composite(rgba)
        cell = background.convert("RGB").resize((cell_size, cell_size), Image.Resampling.LANCZOS)
        sheet.paste(cell, ((index % columns) * cell_size, (index // columns) * cell_size))
    temporary = destination.with_suffix(".atomic.png")
    sheet.save(temporary, format="PNG", compress_level=6)
    with Image.open(temporary) as check:
        check.load()
    os.replace(temporary, destination)


def main() -> None:
    transition_root = ROOT / "sequences" / "transitions"
    for leftover in transition_root.rglob("*.atomic.*"):
        leftover.unlink()
    for leftover in transition_root.rglob("*.work.png"):
        leftover.unlink()

    start_heights = [605, 602, 599, 607, 606, 605]
    brake_heights = [605, 607, 602, 599, 603, 605]

    for direction in DIRECTIONS:
        source = ROOT / "master" / "directions" / f"{direction}.png"
        start_angles, brake_angles = pitch_arrays(direction)
        start_dir = transition_root / "start" / direction
        brake_dir = transition_root / "brake" / direction
        for index in range(6):
            start_frame = start_dir / f"frame-{index:02d}.png"
            brake_frame = brake_dir / f"frame-{index:02d}.png"
            if index in {0, 5}:
                atomic_copy(source, start_frame)
                atomic_copy(source, brake_frame)
            else:
                render_pose(source, start_frame, start_heights[index], start_angles[index])
                render_pose(source, brake_frame, brake_heights[index], brake_angles[index])

    for start, end, midpoint in PAIRS:
        start_master = ROOT / "master" / "directions" / f"{start}.png"
        end_master = ROOT / "master" / "directions" / f"{end}.png"
        midpoint_master = ROOT / "master" / "transitions" / "midpoints" / f"{midpoint}.png"
        forward = transition_root / "turn" / f"{start}-to-{end}"
        reverse = transition_root / "turn" / f"{end}-to-{start}"
        for directory, frames in (
            (forward, (start_master, midpoint_master, end_master)),
            (reverse, (end_master, midpoint_master, start_master)),
        ):
            for index, source in enumerate(frames):
                atomic_copy(source, directory / f"frame-{index:02d}.png")

    for leftover in transition_root.rglob("*.atomic.*"):
        leftover.unlink()

    preview_root = ROOT / "previews" / "transitions"
    for direction in DIRECTIONS:
        for state in ("start", "brake"):
            sequence = transition_root / state / direction
            make_gif(sorted(sequence.glob("frame-[0-9][0-9].png")), preview_root / f"{state}-{direction}.gif")
    for sequence in sorted((transition_root / "turn").iterdir()):
        if sequence.is_dir():
            make_gif(sorted(sequence.glob("frame-[0-9][0-9].png")), preview_root / f"turn-{sequence.name}.gif")

    make_gif(sorted((transition_root / "start").glob("*/frame-[0-9][0-9].png")), preview_root / "start-all.gif")
    make_gif(sorted((transition_root / "brake").glob("*/frame-[0-9][0-9].png")), preview_root / "brake-all.gif")
    make_gif(sorted((transition_root / "turn").glob("*/frame-[0-9][0-9].png")), preview_root / "turn-all.gif")

    keyframes = [transition_root / "start" / direction / "frame-03.png" for direction in DIRECTIONS]
    keyframes += [transition_root / "brake" / direction / "frame-03.png" for direction in DIRECTIONS]
    make_contact_sheet(keyframes, preview_root / "start-brake-keyframes-contact-sheet.png", 4, 320)
    detailed = sorted((transition_root / "start" / "right").glob("frame-[0-9][0-9].png"))
    detailed += sorted((transition_root / "brake" / "right").glob("frame-[0-9][0-9].png"))
    make_contact_sheet(detailed, preview_root / "right-start-brake-sequence-contact-sheet.png", 6, 256)
    midpoint_paths = [ROOT / "master" / "transitions" / "midpoints" / f"{midpoint}.png" for _, _, midpoint in PAIRS]
    make_contact_sheet(midpoint_paths, preview_root / "midpoints-contact-sheet.png", 4, 400)


if __name__ == "__main__":
    main()
