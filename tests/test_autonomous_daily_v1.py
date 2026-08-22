import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-AUTONOMOUS-DAILY-BEHAVIORS-v1"


class AutonomousDailyV1Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_runtime_gates_remain_closed_until_owner_semantic_review(self):
        manifest = self.manifest
        self.assertEqual(manifest["asset_stage"], "production_candidate_owner_qa_pending")
        self.assertFalse(manifest["autonomous_semantics_owner_approved"])
        self.assertFalse(manifest["visual_approved"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["may_enter_autonomous_pool_by_default"])

    def test_expected_daily_action_inventory(self):
        expected = {
            "wk.daily.stand_to_sit": 10,
            "wk.daily.sit_to_prone": 12,
            "wk.daily.prone_to_sit": 4,
            "wk.daily.sit_to_stand": 5,
            "wk.daily.playful_hop": 12,
            "wk.daily.playful_spin": 16,
        }
        actual = {action["behavior_id"]: action["frame_count"] for action in self.manifest["actions"]}
        self.assertEqual(actual, expected)
        self.assertTrue(all(not action["runtime_use"] for action in self.manifest["actions"]))
        self.assertTrue(all(not action["autonomous_semantics_owner_approved"] for action in self.manifest["actions"]))

    def test_every_frame_is_rgba_and_byte_identical_to_approved_source(self):
        approved_batches = {
            "WK-COMMAND-PRODUCTION-CANDIDATES-v4": ROOT / "assets/action-mocks/WK-COMMAND-PRODUCTION-CANDIDATES-v4",
            "WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2": ROOT / "assets/action-batches/WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2",
        }
        for action in self.manifest["actions"]:
            self.assertEqual(action["frame_count"], len(action["frames"]))
            for frame in action["frames"]:
                with self.subTest(action=action["behavior_id"], frame=frame["path"]):
                    derived = BATCH / frame["path"]
                    source_info = frame["derived_from"]
                    source = approved_batches[source_info["batch_id"]] / source_info["path"]
                    data = derived.read_bytes()
                    self.assertEqual(data, source.read_bytes())
                    self.assertEqual(hashlib.sha256(data).hexdigest(), frame["sha256"])
                    self.assertEqual(frame["sha256"], source_info["sha256"])
                    self.assertEqual(len(data), frame["bytes"])
                    with Image.open(derived) as image:
                        image.load()
                        self.assertEqual(image.size, (1024, 1024))
                        self.assertEqual(image.mode, "RGBA")

    def test_no_expired_red_asset_is_a_provenance_source(self):
        provenance = json.dumps(
            [frame["derived_from"] for action in self.manifest["actions"] for frame in action["frames"]],
            ensure_ascii=False,
        ).lower()
        self.assertNotIn("expired", provenance)
        self.assertNotIn("red-shiba", provenance)
        self.assertNotIn("v3.4", provenance)


if __name__ == "__main__":
    unittest.main()
