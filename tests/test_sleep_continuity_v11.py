import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH_ID = "WK-AUTONOMOUS-SLEEP-CONTINUITY-PRODUCTION-v11"
BATCH = ROOT / "assets" / "action-batches" / BATCH_ID
RETIRED_V10 = ROOT / "assets" / "action-batches" / "WK-AUTONOMOUS-SLEEP-RUNTIME-FINAL-CANDIDATE-v10"


class SleepContinuityV11Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.asset = json.loads((BATCH / "asset.json").read_text(encoding="utf-8"))
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_runtime_approval_and_walk_exclusion_are_explicit(self):
        for document in (self.asset, self.manifest):
            self.assertEqual(BATCH_ID, document["asset_id"])
            self.assertEqual(11, document["asset_version"])
            self.assertTrue(document["owner_preview_approved"])
            self.assertTrue(document["visual_approved"])
            self.assertEqual("passed_windows_renderer_qa", document["runtime_validation"])
            self.assertTrue(document["runtime_approved"])
            self.assertTrue(document["runtime_use"])
            self.assertTrue(document["production_asset"])
            self.assertFalse(document["prototype_use"])
            self.assertFalse(document["walk_review_included"])
            self.assertEqual(0.78, document["runtime_render_scale"])

    def test_29_runtime_pngs_are_valid_and_match_inventory(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(29, len(inventory))
        self.assertEqual(29, len({item["path"] for item in inventory}))
        self.assertEqual(29, len(list((BATCH / "frames").glob("*/*.png"))))
        for item in inventory:
            self.assertNotIn("walk", item["path"].lower())
            path = BATCH / item["path"]
            payload = path.read_bytes()
            self.assertEqual(item["bytes"], len(payload), item["path"])
            self.assertEqual(item["sha256"], hashlib.sha256(payload).hexdigest(), item["path"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual("PNG", image.format)
                self.assertEqual("RGBA", image.mode)
                self.assertEqual((1024, 1024), image.size)
                alpha = image.getchannel("A")
                self.assertEqual(0, alpha.crop((0, 0, 1024, 1)).getextrema()[1])
                self.assertEqual(0, alpha.crop((0, 1023, 1024, 1024)).getextrema()[1])
                self.assertEqual(0, alpha.crop((0, 0, 1, 1024)).getextrema()[1])
                self.assertEqual(0, alpha.crop((1023, 0, 1024, 1024)).getextrema()[1])

    def test_sequence_order_timing_and_compatibility_policy_are_stable(self):
        expected = {
            "wk.candidate.sleep.main_lifecycle_v2": (16, 5000, False, True),
            "wk.candidate.sleep.prone_to_side_roll_v2": (8, 2620, False, False),
            "wk.candidate.sleep.sprawled_front_breath_v2": (4, 2600, True, True),
            "wk.candidate.sleep.sprawled_left_side_breath_v2": (4, 2600, True, False),
            "wk.candidate.sleep.sprawled_right_side_breath_v2": (4, 2600, True, False),
            "wk.candidate.sleep.compact_prone_breath_v2": (4, 2600, True, False),
            "wk.candidate.sleep.curled_side_breath_v2": (4, 2600, True, False),
        }
        actions = {action["behavior_id"]: action for action in self.manifest["actions"]}
        self.assertEqual(set(expected), set(actions))
        self.assertEqual(44, sum(action["frame_count"] for action in actions.values()))
        for behavior_id, (count, duration, loop, autonomous) in expected.items():
            action = actions[behavior_id]
            phase = action["phases"][0]
            self.assertEqual(count, action["frame_count"])
            self.assertEqual(count, len(phase["frames"]))
            self.assertEqual(duration, action["total_duration_ms"])
            self.assertEqual(duration, sum(frame["duration_ms"] for frame in phase["frames"]))
            self.assertEqual(loop, action["loop"])
            self.assertEqual(autonomous, action["autonomous_binding_enabled"])
            self.assertTrue(action["runtime_use"])
            self.assertEqual(0.78, action["runtime_render_scale"])
        rules = self.manifest["sequence_rules"]
        self.assertFalse(rules["append_prone_to_side_roll_after_main"])
        self.assertFalse(rules["hard_cut_between_incompatible_views"])
        self.assertFalse(rules["reverse_main_as_wake"])
        self.assertFalse(rules["legacy_sleep_visual_fallback_allowed"])

    def test_v10_remains_retired_and_is_excluded_from_publish(self):
        retired = json.loads((RETIRED_V10 / "asset.json").read_text(encoding="utf-8"))
        self.assertTrue(retired["deprecated"])
        self.assertFalse(retired["runtime_approved"])
        self.assertFalse(retired["runtime_use"])
        project = (ROOT / "src" / "Wukong.Desktop" / "Wukong.Desktop.csproj").read_text(encoding="utf-8")
        self.assertIn("WK-AUTONOMOUS-SLEEP-RUNTIME-FINAL-CANDIDATE-v10", project)
        self.assertIn("<Content Remove=", project)


if __name__ == "__main__":
    unittest.main()
