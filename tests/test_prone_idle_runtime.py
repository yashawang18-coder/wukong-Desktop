from __future__ import annotations

import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ACTION_DIR = ROOT / "delivery" / "WukongAssets" / "actions" / "WK-CORE-PRONE-IDLE-LF"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ProneIdleRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = json.loads((ACTION_DIR / "asset-runtime-candidate-v1.json").read_text())

    def test_runtime_contract(self) -> None:
        sequence = self.manifest["runtime_sequence"]
        self.assertEqual(self.manifest["status"], "runtime-candidate")
        self.assertFalse(self.manifest["runtime_use"])
        self.assertEqual(sequence["frame_count"], 12)
        self.assertEqual(sequence["fps"], 8)
        self.assertEqual(sequence["loop_duration_ms"], 1500)

    def test_every_frame_decodes_and_matches_manifest(self) -> None:
        for item in self.manifest["runtime_sequence"]["frames"]:
            path = ACTION_DIR / item["path"]
            self.assertEqual(sha256(path), item["sha256"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual(image.mode, "RGBA")
                self.assertEqual(image.size, (1024, 1024))
                self.assertEqual(list(image.getchannel("A").getbbox()), item["alpha_bbox"])

    def test_approved_anchors_are_byte_exact(self) -> None:
        mapping = ((1, 1), (2, 4), (3, 7), (4, 10))
        for approved_index, runtime_index in mapping:
            approved = ACTION_DIR / "approved-keyframes" / "v1" / f"frame-{approved_index:03d}.png"
            runtime = ACTION_DIR / "runtime-frames" / "v1" / f"frame-{runtime_index:03d}.png"
            self.assertEqual(sha256(approved), sha256(runtime))

    def test_previews_decode_and_have_expected_timing(self) -> None:
        expected = {
            "loop-actual-speed-v1.gif": (12, 1500),
            "entry-loop-exit-seam-v1.gif": (14, 2500),
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


if __name__ == "__main__":
    unittest.main()
