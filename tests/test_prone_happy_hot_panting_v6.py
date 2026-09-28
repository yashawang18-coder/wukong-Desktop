import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets" / "action-batches" / "WK-AUTONOMOUS-PRONE-HAPPY-HOT-PANTING-SEQUENCE-v6"


class ProneHappyHotPantingV6Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))
        cls.action = cls.manifest["action"]

    def test_gate_and_timing_remain_candidate_only(self):
        self.assertTrue(self.manifest["owner_preview_approved"])
        self.assertTrue(self.manifest["visual_approved"])
        self.assertEqual("pending_windows_renderer_qa", self.manifest["runtime_validation"])
        self.assertFalse(self.manifest["runtime_approved"])
        self.assertFalse(self.manifest["runtime_use"])
        self.assertFalse(self.manifest["production_asset"])
        self.assertFalse(self.manifest["prototype_use"])
        self.assertTrue(self.manifest["developer_preview"])
        self.assertFalse(self.manifest["autonomous_binding_enabled"])
        self.assertEqual(17, len(self.action["frames"]))
        self.assertEqual(5680, sum(frame["duration_ms"] for frame in self.action["frames"]))
        self.assertEqual(4100, sum(frame["duration_ms"] for frame in self.action["frames"][3:13]))

    def test_frames_are_byte_exact_rgba_with_stable_anchor(self):
        bounds = []
        baselines = []
        for entry in self.action["frames"]:
            path = BATCH / entry["path"]
            payload = path.read_bytes()
            self.assertEqual(entry["bytes"], len(payload), entry["path"])
            self.assertEqual(entry["sha256"], hashlib.sha256(payload).hexdigest(), entry["path"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual((1024, 1024), image.size, entry["path"])
                self.assertEqual("RGBA", image.mode, entry["path"])
                alpha = image.getchannel("A")
                box = alpha.getbbox()
                self.assertIsNotNone(box, entry["path"])
                bounds.append(box)
                baselines.append(box[3] - 1)
                self.assertEqual(0, max(alpha.crop((0, 0, 1024, 1)).getextrema()), entry["path"])
                self.assertEqual(0, max(alpha.crop((0, 1023, 1024, 1024)).getextrema()), entry["path"])
                self.assertEqual(0, max(alpha.crop((0, 0, 1, 1024)).getextrema()), entry["path"])
                self.assertEqual(0, max(alpha.crop((1023, 0, 1024, 1024)).getextrema()), entry["path"])

        self.assertEqual({899}, set(baselines))
        self.assertLessEqual(max(box[0] for box in bounds) - min(box[0] for box in bounds), 1)
        self.assertLessEqual(max(box[2] for box in bounds) - min(box[2] for box in bounds), 1)
        first = (BATCH / self.action["frames"][0]["path"]).read_bytes()
        last = (BATCH / self.action["frames"][-1]["path"]).read_bytes()
        self.assertEqual(first, last)


if __name__ == "__main__":
    unittest.main()
