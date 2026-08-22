import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets" / "action-mocks" / "WK-COMMAND-PRODUCTION-CANDIDATES-v4"


class CommandV4AssetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_down_uses_versioned_blue_background_repair(self):
        down = next(action for action in self.manifest["actions"] if action["action"] == "down")
        self.assertEqual(down["source_folder"], "frames/down-v2")
        self.assertEqual(len(down["frames"]), 12)
        self.assertTrue((BATCH / "frames" / "down" / "frame-001.png").is_file(), "approved source frame must be retained")

        old = BATCH / "frames" / "down" / "frame-001.png"
        repaired = BATCH / down["frames"][0]["path"]

        def blue_defect_pixels(path: Path) -> int:
            with Image.open(path) as source:
                image = source.convert("RGBA")
            count = 0
            for red, green, blue, alpha in image.crop((465, 695, 556, 841)).getdata():
                if alpha and blue > 75 and blue - red > 25 and blue - green > 12:
                    count += 1
            return count

        self.assertGreater(blue_defect_pixels(old), 1000, "source frame no longer preserves the recorded defect")
        self.assertEqual(blue_defect_pixels(repaired), 0, "versioned Down frame still contains the blue background defect")

    def test_down_v2_manifest_hashes_match_and_other_frames_are_byte_identical(self):
        down = next(action for action in self.manifest["actions"] if action["action"] == "down")
        for index, record in enumerate(down["frames"], start=1):
            path = BATCH / record["path"]
            data = path.read_bytes()
            self.assertEqual(len(data), record["bytes"])
            self.assertEqual(hashlib.sha256(data).hexdigest(), record["sha256"])
            with Image.open(path) as image:
                self.assertEqual((image.size, image.mode), ((1024, 1024), "RGBA"))
            if index > 1:
                original = BATCH / "frames" / "down" / f"frame-{index:03d}.png"
                self.assertEqual(data, original.read_bytes(), f"unaffected Down frame {index} changed")


if __name__ == "__main__":
    unittest.main()
