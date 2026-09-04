import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets" / "action-batches" / "WK-AUTONOMOUS-PRONE-HEAD-MICROEVENT-CANDIDATE-v4"


class ProneHeadCandidateV4Tests(unittest.TestCase):
    def setUp(self):
        self.asset = json.loads((BATCH / "asset.json").read_text(encoding="utf-8"))
        self.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_owner_approved_gate_is_limited_to_compatible_prone_profile(self):
        for document in (self.asset, self.manifest):
            self.assertTrue(document["visual_approved"])
            self.assertEqual("passed_windows_renderer_qa", document["runtime_validation"])
            self.assertTrue(document["runtime_approved"])
            self.assertTrue(document["runtime_use"])
            self.assertTrue(document["production_asset"])
            self.assertFalse(document["prototype_use"])
            self.assertTrue(document["autonomous_binding_enabled"])
            self.assertEqual("non_front_prone_owner_validated", document["approved_runtime_profile"])
        self.assertEqual(["AutonomousTick", "DeveloperPreview"], self.manifest["allowed_sources"])
        self.assertFalse(self.manifest["current_runtime_prone_anchor_exact"])

    def test_all_source_frames_are_frozen_rgba_and_baseline_aligned(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(24, len(inventory))
        self.assertEqual(24, len({item["path"] for item in inventory}))
        for item in inventory:
            path = BATCH / item["path"]
            self.assertTrue(path.is_file(), item["path"])
            payload = path.read_bytes()
            self.assertEqual(item["bytes"], len(payload), item["path"])
            self.assertEqual(item["sha256"], hashlib.sha256(payload).hexdigest(), item["path"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual((1024, 1024), image.size, item["path"])
                self.assertEqual("RGBA", image.mode, item["path"])
                alpha = image.getchannel("A")
                bounds = alpha.getbbox()
                self.assertIsNotNone(bounds, item["path"])
                self.assertEqual(771, bounds[3], item["path"])
                self.assertEqual(0, alpha.crop((0, 0, 1024, 1)).getextrema()[1], item["path"])
                self.assertEqual(0, alpha.crop((0, 1023, 1024, 1024)).getextrema()[1], item["path"])
                self.assertEqual(0, alpha.crop((0, 0, 1, 1024)).getextrema()[1], item["path"])
                self.assertEqual(0, alpha.crop((1023, 0, 1024, 1024)).getextrema()[1], item["path"])

    def test_playback_is_closed_and_uses_exact_internal_handoff(self):
        action = self.manifest["actions"][0]
        self.assertEqual("wk.candidate.daily.prone_head_lower_turn_v4", action["behavior_id"])
        self.assertEqual(44, action["frame_count"])
        self.assertEqual([12, 22, 10], [len(phase["frames"]) for phase in action["phases"]])
        self.assertEqual(["intro", "action", "exit"], [phase["name"] for phase in action["phases"]])
        first = action["phases"][0]["frames"][0]["path"]
        last = action["phases"][-1]["frames"][-1]["path"]
        self.assertEqual(first, last)
        inventory = {item["path"]: item for item in self.manifest["frame_inventory"]}
        self.assertEqual(
            inventory["frames/head-lower/frame-011.png"]["sha256"],
            inventory["frames/head-turn/frame-001.png"]["sha256"],
        )
        self.assertEqual(
            self.manifest["internal_handoff_sha256"],
            inventory["frames/head-turn/frame-001.png"]["sha256"],
        )


if __name__ == "__main__":
    unittest.main()
