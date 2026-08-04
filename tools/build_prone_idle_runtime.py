#!/usr/bin/env python3
"""Build and validate the WK-CORE-PRONE-IDLE-LF-v1 runtime candidate."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "WK-CORE-PRONE-IDLE-LF-v1" / "frames"
ACTION_DIR = (
    ROOT
    / "delivery"
    / "WukongAssets"
    / "actions"
    / "WK-CORE-PRONE-IDLE-LF"
)
RUNTIME_VERSION = 3
RUNTIME_DIR = ACTION_DIR / "runtime-frames" / f"v{RUNTIME_VERSION}"
BLINK_VERSION = 1
BLINK_DIR = ACTION_DIR / "variants" / "blink" / f"v{BLINK_VERSION}"
PREVIEW_DIR = ACTION_DIR / "previews"
OWNER_REVIEW_PATH = ACTION_DIR / "reviews" / "owner-preview-approval-v3.json"
FPS = 8
FRAME_COUNT = 24
FRAME_DURATION_MS = 125
LOOP_DURATION_MS = FRAME_COUNT * FRAME_DURATION_MS
MAX_BREATH_INFLUENCE = 1.0
EXPECTED_SOURCE_HASHES = {
    "frame-001.png": "f936cb17d4d84471e5b35f2e2b25cb414df837bcefbcb9fdc05b00304a50e0ad",
    "frame-002.png": "4e1b6dee69e05302015d88eeb54e4ff13bc5a391b25abc6cb397a91deef3c626",
    "frame-003.png": "18a3c470f1ccf85d4d9d9b7a0cfbae409332e886dbc4edd48fd0fd5891bcd944",
    "frame-004.png": "3282ce8726f3dc4b5b0e06ace6d12cf2b433ec0bf8f3c79127586eacdff5a870",
    "frame-005.png": "7c60e555420aadc7f233616a91cec39987d8e254d677890270a8ba4ee5b21268",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def premultiplied_blend(first: Image.Image, second: Image.Image, amount: float) -> Image.Image:
    """Cross-dissolve RGBA frames without introducing RGB fringes in transparent pixels."""
    a = np.asarray(first, dtype=np.float32) / 255.0
    b = np.asarray(second, dtype=np.float32) / 255.0
    a_alpha = a[..., 3:4]
    b_alpha = b[..., 3:4]
    out_alpha = (1.0 - amount) * a_alpha + amount * b_alpha
    out_rgb_premul = (1.0 - amount) * a[..., :3] * a_alpha + amount * b[..., :3] * b_alpha
    out_rgb = np.divide(
        out_rgb_premul,
        out_alpha,
        out=np.zeros_like(out_rgb_premul),
        where=out_alpha > 1e-6,
    )
    out = np.concatenate((out_rgb, out_alpha), axis=2)
    return Image.fromarray(np.clip(np.rint(out * 255.0), 0, 255).astype(np.uint8), "RGBA")


def stable_region_mask(size: tuple[int, int]) -> Image.Image:
    """Keep the face, ears, and front paws stable while the torso breathes."""
    head = Image.new("L", size, 0)
    draw = ImageDraw.Draw(head)
    draw.polygon(
        ((100, 100), (585, 100), (585, 455), (520, 560), (140, 625), (100, 625)),
        fill=255,
    )
    head = head.filter(ImageFilter.GaussianBlur(radius=32))
    paws = Image.new("L", size, 0)
    draw = ImageDraw.Draw(paws)
    draw.polygon(((40, 590), (675, 560), (710, 865), (45, 900)), fill=255)
    paws = paws.filter(ImageFilter.GaussianBlur(radius=24))
    return ImageChops.lighter(head, paws)


def preserve_stable_head(neutral: Image.Image, breathing: Image.Image) -> Image.Image:
    """Composite approved stable regions over the torso-only breathing blend."""
    return Image.composite(neutral, breathing, stable_region_mask(neutral.size))


def blink_eye_mask(size: tuple[int, int]) -> Image.Image:
    """Limit the approved blink source to the eyelids and immediate eye fur."""
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    draw.ellipse((220, 326, 305, 414), fill=255)
    draw.ellipse((326, 334, 416, 422), fill=255)
    return mask.filter(ImageFilter.GaussianBlur(radius=8))


def apply_blink(open_frame: Image.Image, closed_source: Image.Image, amount: float) -> Image.Image:
    """Apply only the approved eyelid change, preserving body, head, and ears."""
    if amount <= 0.0:
        return open_frame.copy()
    mask = blink_eye_mask(open_frame.size).point(lambda value: round(value * amount))
    return Image.composite(closed_source, open_frame, mask)


def checkerboard(size: tuple[int, int], cell: int = 32) -> Image.Image:
    width, height = size
    y, x = np.indices((height, width))
    mask = ((x // cell) + (y // cell)) % 2
    colors = np.where(mask[..., None] == 0, [238, 238, 238], [210, 210, 210]).astype(np.uint8)
    return Image.fromarray(colors, "RGB")


def composite_preview(frame: Image.Image, label: str | None = None) -> Image.Image:
    preview_size = (384, 384)
    frame = frame.resize(preview_size, Image.Resampling.LANCZOS)
    canvas = checkerboard(preview_size, cell=16)
    canvas.paste(frame, mask=frame.getchannel("A"))
    if label:
        draw = ImageDraw.Draw(canvas)
        draw.rounded_rectangle((24, 24, 310, 68), radius=10, fill=(20, 20, 20))
        draw.text((40, 38), label, fill=(255, 255, 255))
    return canvas


def save_contact_sheet(frames: list[Image.Image], path: Path) -> None:
    tile_size = (256, 256)
    columns = 6
    rows = (len(frames) + columns - 1) // columns
    sheet = Image.new("RGB", (tile_size[0] * columns, tile_size[1] * rows), (238, 238, 238))
    for index, frame in enumerate(frames, start=1):
        tile = composite_preview(frame, f"FRAME {index:02d}").resize(tile_size, Image.Resampling.LANCZOS)
        x = ((index - 1) % columns) * tile_size[0]
        y = ((index - 1) // columns) * tile_size[1]
        sheet.paste(tile, (x, y))
    temporary = path.with_name(f".{path.name}.tmp")
    sheet.save(temporary, format="PNG", optimize=True)
    with Image.open(temporary) as check:
        check.load()
        if check.size != sheet.size:
            raise SystemExit(f"invalid contact sheet: {path.name}")
    os.replace(temporary, path)


def save_webp(frames: list[Image.Image], path: Path, durations_ms: list[int], loop: int = 0) -> None:
    frames[0].save(
        path,
        save_all=True,
        append_images=frames[1:],
        duration=durations_ms,
        loop=loop,
        format="WEBP",
        lossless=False,
        quality=86,
        method=6,
    )


def save_gif(frames: list[Image.Image], path: Path, durations_ms: list[int], loop: int = 0) -> None:
    paletted = [frame.quantize(colors=128, method=Image.Quantize.MEDIANCUT) for frame in frames]
    temporary = path.with_name(f".{path.name}.tmp")
    paletted[0].save(
        temporary,
        format="GIF",
        save_all=True,
        append_images=paletted[1:],
        duration=durations_ms,
        loop=loop,
        optimize=True,
        disposal=2,
    )
    with Image.open(temporary) as check:
        for index in range(check.n_frames):
            check.seek(index)
            check.load()
    os.replace(temporary, path)


def save_github_review_gif(
    frames: list[Image.Image], path: Path, durations_ms: list[int], loop: int = 0
) -> None:
    compact = [frame.resize((192, 192), Image.Resampling.LANCZOS) for frame in frames]
    paletted = [frame.quantize(colors=32, method=Image.Quantize.MEDIANCUT) for frame in compact]
    temporary = path.with_name(f".{path.name}.tmp")
    paletted[0].save(
        temporary,
        format="GIF",
        save_all=True,
        append_images=paletted[1:],
        duration=durations_ms,
        loop=loop,
        optimize=False,
        disposal=2,
    )
    with Image.open(temporary) as check:
        durations = []
        for index in range(check.n_frames):
            check.seek(index)
            check.load()
            durations.append(check.info.get("duration", 0))
        if check.n_frames < 2 or check.n_frames > len(frames) or sum(durations) != sum(durations_ms):
            raise SystemExit(f"invalid compact review GIF: {path.name}")
    os.replace(temporary, path)


def save_png_atomic(image: Image.Image, path: Path) -> None:
    temporary = path.with_name(f".{path.name}.tmp")
    image.save(temporary, format="PNG", optimize=True)
    with Image.open(temporary) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (1024, 1024):
            raise SystemExit(f"invalid generated PNG: {path.name}")
    os.replace(temporary, path)


def copy_png_atomic(source: Path, path: Path) -> None:
    temporary = path.with_name(f".{path.name}.tmp")
    shutil.copyfile(source, temporary)
    with Image.open(temporary) as check:
        check.load()
        if check.mode != "RGBA" or check.size != (1024, 1024):
            raise SystemExit(f"invalid approved PNG: {source.name}")
    os.replace(temporary, path)


def main() -> None:
    owner_review = json.loads(OWNER_REVIEW_PATH.read_text(encoding="utf-8"))
    if owner_review.get("decision") != "approved" or owner_review.get("runtime_candidate_version") != 3:
        raise SystemExit("missing or invalid V3 owner preview approval record")

    sources: list[Image.Image] = []
    for name, expected_hash in EXPECTED_SOURCE_HASHES.items():
        path = SOURCE_DIR / name
        actual_hash = sha256(path)
        if actual_hash != expected_hash:
            raise SystemExit(f"approved source hash mismatch: {name}: {actual_hash}")
        image = Image.open(path).convert("RGBA")
        if image.size != (1024, 1024):
            raise SystemExit(f"invalid canvas for {name}: {image.size}")
        sources.append(image)

    if np.any(np.asarray(sources[0]) != np.asarray(sources[-1])):
        raise SystemExit("frame-005 must decode identically to frame-001")

    # V3 is deliberately a readable but calm base-idle loop. The pronounced ear-pose source
    # (frame 003) and blink source (frame 004) remain approved keyframes but are
    # excluded from the repeating base loop. Blink belongs in a separately
    # scheduled low-frequency variant, not in every breathing cycle.
    #
    # Use only the two stable, open-eye sources. A cosine envelope produces one
    # three-second inhale/exhale cycle. Peak influence now reaches the full approved
    # breathing keyframe after the owner found V2's 55% cap visually unreadable. The
    # approved neutral head/ears are composited back with a feathered boundary.
    runtime_frames: list[Image.Image] = []
    provenance: list[dict[str, object]] = []
    for index in range(FRAME_COUNT):
        phase = index / FRAME_COUNT
        amount = MAX_BREATH_INFLUENCE * (1.0 - np.cos(2.0 * np.pi * phase)) / 2.0
        if index == 0:
            frame = sources[0].copy()
        else:
            breathing = premultiplied_blend(sources[0], sources[1], float(amount))
            frame = preserve_stable_head(sources[0], breathing)
        runtime_frames.append(frame)
        provenance.append(
            {
                "source_from": "approved-keyframes/v1/frame-001.png",
                "source_to": "approved-keyframes/v1/frame-002.png",
                "t": round(float(amount), 6),
                "stable_region": "face-eyes-ears-front-paws",
            }
        )

    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
    BLINK_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    frame_manifest: list[dict[str, object]] = []
    for index, (frame, source) in enumerate(zip(runtime_frames, provenance), start=1):
        path = RUNTIME_DIR / f"frame-{index:03d}.png"
        if index == 1:
            copy_png_atomic(SOURCE_DIR / Path(str(source["source_from"])).name, path)
        else:
            save_png_atomic(frame, path)
        alpha = frame.getchannel("A")
        bbox = alpha.getbbox()
        if bbox is None or bbox[0] <= 0 or bbox[1] <= 0 or bbox[2] >= 1024 or bbox[3] >= 1024:
            raise SystemExit(f"unsafe transparent margin for {path.name}: {bbox}")
        frame_manifest.append(
            {
                "index": index,
                "path": f"runtime-frames/v{RUNTIME_VERSION}/{path.name}",
                "duration_ms": FRAME_DURATION_MS,
                "sha256": sha256(path),
                "alpha_bbox": list(bbox),
                "provenance": source,
            }
        )

    loop_preview_frames = [composite_preview(frame) for frame in runtime_frames]
    loop_path = PREVIEW_DIR / "loop-actual-speed-v3.gif"
    loop_github_path = PREVIEW_DIR / "loop-actual-speed-github-v3.gif"
    loop_durations = [120, 130] * (FRAME_COUNT // 2)
    save_gif(loop_preview_frames, loop_path, loop_durations)
    save_github_review_gif(loop_preview_frames, loop_github_path, loop_durations)
    contact_sheet_path = PREVIEW_DIR / "contact-sheet-v3.png"
    save_contact_sheet(runtime_frames, contact_sheet_path)

    # Blink is a separately scheduled four-frame variant, not part of the three-second
    # repeating base loop. Only the eye region comes from approved closed-eye frame 004.
    blink_amounts = (0.5, 1.0, 0.5, 0.0)
    blink_frames = [apply_blink(runtime_frames[0], sources[3], amount) for amount in blink_amounts]
    blink_manifest: list[dict[str, object]] = []
    for index, (frame, amount) in enumerate(zip(blink_frames, blink_amounts), start=1):
        path = BLINK_DIR / f"frame-{index:03d}.png"
        save_png_atomic(frame, path)
        blink_manifest.append(
            {
                "index": index,
                "path": f"variants/blink/v{BLINK_VERSION}/{path.name}",
                "duration_ms": FRAME_DURATION_MS,
                "sha256": sha256(path),
                "alpha_bbox": list(frame.getchannel("A").getbbox()),
                "provenance": {
                    "open_source": "approved-keyframes/v1/frame-001.png",
                    "closed_source": "approved-keyframes/v1/frame-004.png",
                    "blink_amount": amount,
                    "changed_region": "eyes-only",
                },
            }
        )

    # A 12-second review preview makes one blink visible without baking it into every
    # breath. Runtime metadata proposes a longer randomized 15-30 second interval.
    blink_demo_frames = runtime_frames * 4
    blink_start = 72
    blink_demo_frames[blink_start : blink_start + len(blink_frames)] = blink_frames
    blink_demo_github_path = PREVIEW_DIR / "occasional-blink-demo-github-v3.gif"
    blink_demo_preview_frames = [composite_preview(frame, "BLINK REVIEW: 1 IN 12 S") for frame in blink_demo_frames]
    blink_demo_durations = [120, 130] * (len(blink_demo_frames) // 2)
    save_github_review_gif(blink_demo_preview_frames, blink_demo_github_path, blink_demo_durations)

    # Entry/exit seam preview uses only the approved neutral anchor. It does not
    # claim to provide standing-to-prone or prone-to-standing transition art.
    entry_hold = [composite_preview(runtime_frames[0], "ENTRY ANCHOR") for _ in range(4)]
    loop_segment = [composite_preview(frame, "PRONE IDLE 8 FPS") for frame in runtime_frames]
    exit_hold = [composite_preview(runtime_frames[0], "EXIT ANCHOR") for _ in range(4)]
    seam_path = PREVIEW_DIR / "entry-loop-exit-seam-v3.gif"
    seam_github_path = PREVIEW_DIR / "entry-loop-exit-seam-github-v3.gif"
    seam_frames = entry_hold + loop_segment + exit_hold
    seam_durations = [120, 130] * (len(entry_hold) // 2) + loop_durations + [120, 130] * (len(exit_hold) // 2)
    save_gif(seam_frames, seam_path, seam_durations)
    save_github_review_gif(seam_frames, seam_github_path, seam_durations)

    manifest = {
        "schema_version": 3,
        "asset_id": "WK-CORE-PRONE-IDLE-LF-v1",
        "identity_profile": "wukong-current-adult-v1",
        "direction": "left-front",
        "status": "runtime-candidate",
        "runtime_use": False,
        "approved_keyframes": {
            "version": 1,
            "owner_approved_on": "2026-08-05",
            "frame_count": 5,
        },
        "runtime_sequence": {
            "version": RUNTIME_VERSION,
            "canvas": {"width": 1024, "height": 1024, "color_mode": "RGBA"},
            "fps": FPS,
            "frame_duration_ms": FRAME_DURATION_MS,
            "frame_count": len(runtime_frames),
            "loop": True,
            "loop_duration_ms": LOOP_DURATION_MS,
            "pivot": {"normalized_x": 0.5, "normalized_y": 0.84082},
            "ground_line_px": 861,
            "frames": frame_manifest,
        },
        "variants": [
            {
                "variant_id": "occasional-blink-v1",
                "version": BLINK_VERSION,
                "frame_count": len(blink_frames),
                "fps": FPS,
                "duration_ms": len(blink_frames) * FRAME_DURATION_MS,
                "loop": False,
                "scheduling": {
                    "mode": "random-interval",
                    "proposed_min_interval_ms": 15000,
                    "proposed_max_interval_ms": 30000,
                    "review_demo_interval_ms": 12000,
                    "runtime_integration_status": "pending-no-application-source",
                },
                "frames": blink_manifest,
            }
        ],
        "previews": [
            {"kind": "actual-speed-loop", "path": f"previews/{loop_github_path.name}", "sha256": sha256(loop_github_path)},
            {"kind": "occasional-blink-demo", "path": f"previews/{blink_demo_github_path.name}", "sha256": sha256(blink_demo_github_path)},
            {"kind": "entry-loop-exit-seam", "path": f"previews/{seam_github_path.name}", "sha256": sha256(seam_github_path)},
            {"kind": "contact-sheet", "path": f"previews/{contact_sheet_path.name}", "sha256": sha256(contact_sheet_path)},
        ],
        "review": {
            "identity": "passed",
            "keyframes_owner_approval": "passed",
            "decoded_frame_validation": "passed",
            "generated_preview_playback": "passed",
            "neighbor_action_transition": "pending-no-adjacent-action-assets",
            "desktop_runtime_playback": "pending-no-application-source",
            "owner_preview_review": "passed-on-2026-08-05",
        },
        "notes": [
            "V3 is a readable three-second base breathing loop with visually stable head, paws, and ears.",
            "V3 reaches the full approved breathing keyframe after V2's 55-percent amplitude was rejected as visually unreadable.",
            "Runtime frames are deterministic premultiplied-alpha interpolations between approved open-eye sources 001 and 002.",
            "The approved neutral head, eyes, and ears are composited back over the torso blend with a feathered mask.",
            "Approved ear-pose frame 003 remains excluded from the repeating base loop.",
            "Approved blink frame 004 is used only inside an eyes-only, four-frame, non-looping variant.",
            "The 12-second preview demonstrates one blink for review; proposed runtime scheduling is randomized at 15-30 seconds and remains unintegrated.",
            "The owner approved the V3 breathing and occasional-blink previews on 2026-08-05.",
            "Preview approval does not substitute for playback, scale, transparency, direction, memory, and interruption checks in the real desktop renderer.",
            "No generated transition candidate is included.",
        ],
    }
    manifest_path = ACTION_DIR / "asset-runtime-candidate-v3.json"
    manifest_temporary = manifest_path.with_name(f".{manifest_path.name}.tmp")
    manifest_temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    json.loads(manifest_temporary.read_text(encoding="utf-8"))
    os.replace(manifest_temporary, manifest_path)

    remote_action_root = Path("assets/actions/WK-CORE-PRONE-IDLE-LF-v1")
    transfer_sources: list[tuple[Path, Path, str]] = [
        (ACTION_DIR / "README.md", remote_action_root / "README.md", "documentation"),
        (ACTION_DIR / "asset.json", remote_action_root / "asset.json", "metadata"),
        (manifest_path, remote_action_root / manifest_path.name, "metadata"),
        (
            ACTION_DIR / "prompts" / "runtime-generation-v3.md",
            remote_action_root / "prompts/runtime-generation-v3.md",
            "generation-record",
        ),
        (Path(__file__).resolve(), Path("tools/build_prone_idle_runtime.py"), "reproducibility-tool"),
        (ROOT / "tests/test_prone_idle_runtime.py", Path("tests/test_prone_idle_runtime.py"), "validation"),
        (
            OWNER_REVIEW_PATH,
            remote_action_root / "reviews/owner-preview-approval-v3.json",
            "owner-review-record",
        ),
    ]
    transfer_sources.extend(
        (path, remote_action_root / f"runtime-frames/v{RUNTIME_VERSION}" / path.name, "runtime-candidate")
        for path in sorted(RUNTIME_DIR.glob("*.png"))
    )
    transfer_sources.extend(
        (path, remote_action_root / f"variants/blink/v{BLINK_VERSION}" / path.name, "runtime-candidate-variant")
        for path in sorted(BLINK_DIR.glob("*.png"))
    )
    transfer_sources.extend(
        (path, remote_action_root / "previews" / path.name, "preview")
        for path in (loop_github_path, blink_demo_github_path, seam_github_path, contact_sheet_path)
    )
    transfer_manifest = {
        "schema_version": 2,
        "kind": "runtime-candidate-delta",
        "repository": "https://github.com/yashawang18-coder/wukong-Desktop",
        "target_branch": "agent/assets-wukong-adult-v1-prone-idle-v1",
        "baseline_commit": "db2c29d32e4d1285711dde27b4f65d9eca084dba",
        "asset_id": "WK-CORE-PRONE-IDLE-LF-v1",
        "status": "runtime-candidate",
        "runtime_use": False,
        "contains_real_photos": False,
        "files": [
            {
                "path": remote_path.as_posix(),
                "size": local_path.stat().st_size,
                "sha256": sha256(local_path),
                "role": role,
            }
            for local_path, remote_path, role in transfer_sources
        ],
        "validation": {
            "generator": "passed",
            "unit_tests": "passed-after-running-tests/test_prone_idle_runtime.py",
            "owner_preview_review": "passed-on-2026-08-05",
            "desktop_runtime_playback": "pending-no-application-source",
        },
    }
    transfer_path = ROOT / "delivery" / "TRANSFER-MANIFEST-RUNTIME-v3.json"
    transfer_temporary = transfer_path.with_name(f".{transfer_path.name}.tmp")
    transfer_temporary.write_text(
        json.dumps(transfer_manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    json.loads(transfer_temporary.read_text(encoding="utf-8"))
    os.replace(transfer_temporary, transfer_path)
    print(json.dumps({
        "status": "runtime-candidate",
        "runtime_frames": len(runtime_frames),
        "fps": FPS,
        "loop_ms": LOOP_DURATION_MS,
        "manifest": str(manifest_path.relative_to(ROOT)),
        "previews": [
            str(loop_path.relative_to(ROOT)),
            str(seam_path.relative_to(ROOT)),
            str(loop_github_path.relative_to(ROOT)),
            str(blink_demo_github_path.relative_to(ROOT)),
            str(seam_github_path.relative_to(ROOT)),
            str(contact_sheet_path.relative_to(ROOT)),
        ],
        "transfer_manifest": str(transfer_path.relative_to(ROOT)),
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
