from __future__ import annotations

import hashlib
import json
import unittest
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ACTION_DIR = ROOT / "assets" / "actions" / "WK-CORE-PRONE-IDLE-LF-v1"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ProneIdleRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = json.loads((ACTION_DIR / "asset-runtime-candidate-v3.json").read_text())

    def test_runtime_contract(self) -> None:
        sequence = self.manifest["runtime_sequence"]
        self.assertEqual(self.manifest["status"], "runtime-candidate")
        self.assertFalse(self.manifest["runtime_use"])
        self.assertEqual(sequence["version"], 3)
        self.assertEqual(sequence["frame_count"], 24)
        self.assertEqual(sequence["fps"], 8)
        self.assertEqual(sequence["loop_duration_ms"], 3000)
        self.assertEqual(self.manifest["review"]["owner_preview_review"], "passed-on-2026-08-05")
        self.assertEqual(
            self.manifest["review"]["desktop_runtime_playback"],
            "pending-no-application-source",
        )

    def test_every_frame_decodes_and_matches_manifest(self) -> None:
        for item in self.manifest["runtime_sequence"]["frames"]:
            path = ACTION_DIR / item["path"]
            self.assertEqual(sha256(path), item["sha256"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual(image.mode, "RGBA")
                self.assertEqual(image.size, (1024, 1024))
                self.assertEqual(list(image.getchannel("A").getbbox()), item["alpha_bbox"])

    def test_neutral_anchor_is_byte_exact_and_expressive_sources_are_excluded(self) -> None:
        approved = ACTION_DIR / "approved-keyframes" / "v1" / "frame-001.png"
        runtime = ACTION_DIR / "runtime-frames" / "v3" / "frame-001.png"
        self.assertEqual(sha256(approved), sha256(runtime))
        used_sources = {
            item["provenance"]["source_from"] for item in self.manifest["runtime_sequence"]["frames"]
        } | {
            item["provenance"]["source_to"] for item in self.manifest["runtime_sequence"]["frames"]
        }
        self.assertNotIn("approved-keyframes/v1/frame-003.png", used_sources)
        self.assertNotIn("approved-keyframes/v1/frame-004.png", used_sources)

    def test_breathing_is_full_strength_and_blink_is_eyes_only_variant(self) -> None:
        frames = self.manifest["runtime_sequence"]["frames"]
        self.assertEqual(frames[12]["provenance"]["t"], 1.0)
        neutral = ACTION_DIR / "runtime-frames" / "v3" / "frame-001.png"
        peak = ACTION_DIR / "runtime-frames" / "v3" / "frame-013.png"
        self.assertNotEqual(sha256(neutral), sha256(peak))
        with Image.open(neutral) as neutral_image, Image.open(peak) as peak_image:
            neutral_pixels = np.asarray(neutral_image.convert("RGBA"), dtype=np.int16)
            peak_pixels = np.asarray(peak_image.convert("RGBA"), dtype=np.int16)
        delta = np.max(np.abs(neutral_pixels - peak_pixels), axis=2)
        self.assertLessEqual(int((delta[150:500, 150:520] > 3).sum()), 20)
        self.assertLessEqual(int((delta[650:850, 70:650] > 3).sum()), 100)
        self.assertGreater(int((delta[400:760, 430:930] > 3).sum()), 50000)

        variants = self.manifest["variants"]
        self.assertEqual(len(variants), 1)
        blink = variants[0]
        self.assertEqual(blink["variant_id"], "occasional-blink-v1")
        self.assertFalse(blink["loop"])
        self.assertEqual(blink["frame_count"], 4)
        self.assertEqual(blink["scheduling"]["proposed_min_interval_ms"], 15000)
        self.assertEqual(blink["scheduling"]["proposed_max_interval_ms"], 30000)
        for item in blink["frames"]:
            path = ACTION_DIR / item["path"]
            self.assertEqual(sha256(path), item["sha256"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual(image.mode, "RGBA")
                self.assertEqual(image.size, (1024, 1024))
        with Image.open(neutral) as neutral_image:
            neutral_pixels = np.asarray(neutral_image.convert("RGBA"), dtype=np.int16)
        for item in blink["frames"]:
            with Image.open(ACTION_DIR / item["path"]) as blink_image:
                blink_pixels = np.asarray(blink_image.convert("RGBA"), dtype=np.int16)
            delta = np.max(np.abs(neutral_pixels - blink_pixels), axis=2)
            changed_y, changed_x = np.where(delta > 0)
            if len(changed_x):
                self.assertGreaterEqual(int(changed_x.min()), 200)
                self.assertLessEqual(int(changed_x.max()), 440)
                self.assertGreaterEqual(int(changed_y.min()), 300)
                self.assertLessEqual(int(changed_y.max()), 445)

    def test_previews_decode_and_have_expected_timing(self) -> None:
        expected = {
            "loop-actual-speed-github-v3.gif": (24, 3000),
            "occasional-blink-demo-github-v3.gif": (96, 12000),
            "entry-loop-exit-seam-github-v3.gif": (26, 4000),
        }
        for name, (expected_frames, expected_ms) in expected.items():
            path = ACTION_DIR / "previews" / name
            with Image.open(path) as image:
                durations = []
                for index in range(image.n_frames):
                    image.seek(index)
                    image.load()
                    durations.append(image.info.get("duration", 0))
                self.assertEqual(image.n_frames, expected_frames)
                self.assertEqual(sum(durations), expected_ms)

        contact_sheet = ACTION_DIR / "previews" / "contact-sheet-v3.png"
        with Image.open(contact_sheet) as image:
            image.load()
            self.assertEqual(image.size, (1536, 1024))


if __name__ == "__main__":
    unittest.main()
