import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets" / "action-batches" / "WK-AUTONOMOUS-PRONE-MICROEXPRESSIONS-v1"
EXPECTED_DURATIONS = [520, 150, 150, 160, 260, 220, 160, 160, 170, 240, 320, 520]


class FrontProneMicroexpressionsV1Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_owner_enabled_gate_records_real_renderer_evidence(self):
        self.assertTrue(self.manifest["owner_preview_approved"])
        self.assertTrue(self.manifest["visual_approved"])
        self.assertEqual("passed_windows_renderer_qa", self.manifest["runtime_validation"])
        self.assertTrue(self.manifest["runtime_approved"])
        self.assertTrue(self.manifest["runtime_use"])
        self.assertTrue(self.manifest["production_asset"])
        self.assertFalse(self.manifest["prototype_use"])
        self.assertTrue(self.manifest["developer_preview"])
        self.assertTrue(self.manifest["autonomous_binding_enabled"])
        self.assertEqual(3, len(self.manifest["approval_evidence"]["results"]))

    def test_all_production_frames_match_inventory_and_alpha_contract(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(36, len(inventory))
        self.assertEqual(36, len(list((BATCH / "sequences").glob("*/*.png"))))
        for item in inventory:
            path = BATCH / item["path"]
            payload = path.read_bytes()
            self.assertEqual(item["bytes"], len(payload), item["path"])
            self.assertEqual(item["sha256"], hashlib.sha256(payload).hexdigest(), item["path"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual("PNG", image.format)
                self.assertEqual("RGBA", image.mode)
                self.assertEqual((1024, 1024), image.size)
                self.assertEqual((323, 477, 700, 900), image.getchannel("A").getbbox())
                self.assertEqual(0, image.getchannel("A").crop((0, 0, 1024, 1)).getextrema()[1])
                self.assertEqual(0, image.getchannel("A").crop((0, 1023, 1024, 1024)).getextrema()[1])

    def test_sequences_keep_manifest_order_timing_and_anchor(self):
        self.assertEqual(
            {"prone-satisfied-smile", "prone-curious-observe", "prone-knowing-look"},
            {item["behavior_id"] for item in self.manifest["actions"]},
        )
        for action in self.manifest["actions"]:
            self.assertEqual(12, action["frame_count"])
            self.assertEqual(3030, action["total_duration_ms"])
            self.assertFalse(action["loop"])
            self.assertEqual(EXPECTED_DURATIONS, [frame["duration_ms"] for frame in action["frames"]])
            first = (BATCH / action["frames"][0]["path"]).read_bytes()
            last = (BATCH / action["frames"][-1]["path"]).read_bytes()
            self.assertEqual(first, last)
            self.assertEqual("prone.awake.front", action["from_pose"])
            self.assertEqual("prone.awake.front", action["to_pose"])
            self.assertEqual("forbidden_front_facing_asset", action["mirror_policy"])

    def test_runtime_directory_excludes_review_artifacts(self):
        forbidden = {".gif", ".apng", ".webp"}
        self.assertFalse(any(path.suffix.lower() in forbidden for path in BATCH.rglob("*")))
        self.assertFalse((BATCH / "review").exists())


if __name__ == "__main__":
    unittest.main()
