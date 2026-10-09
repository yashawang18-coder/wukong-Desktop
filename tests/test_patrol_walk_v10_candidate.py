import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
V8 = ROOT / "assets" / "action-batches" / "WK-AUTONOMOUS-PATROL-WALK-v8"
V10 = ROOT / "assets" / "action-batches" / "WK-AUTONOMOUS-PATROL-WALK-v10"
EXPECTED_REPLACEMENT_SHA256 = "373b4dae88823d4e86616385ad1ded5d1d9845c8a421812167149106e1f5f453"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class PatrolWalkV10CandidateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((V10 / "manifest.json").read_text(encoding="utf-8"))

    def test_owner_approved_runtime_gate_is_enabled(self):
        manifest = self.manifest
        self.assertTrue(manifest["owner_preview_approved"])
        self.assertTrue(manifest["visual_approved"])
        self.assertEqual(manifest["runtime_validation"], "passed_windows_renderer_qa")
        self.assertTrue(manifest["runtime_approved"])
        self.assertTrue(manifest["runtime_use"])
        self.assertTrue(manifest["production_asset"])
        self.assertFalse(manifest["prototype_use"])
        self.assertTrue(manifest["developer_preview"])
        self.assertTrue(manifest["autonomous_binding_enabled"])
        self.assertEqual(manifest["allowed_sources"], ["AutonomousTick", "DeveloperPreview", "OwnerDialogue"])

    def test_inventory_hashes_dimensions_alpha_and_transparent_border(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(len(inventory), 13)
        self.assertEqual(len({item["path"] for item in inventory}), 13)
        for item in inventory:
            path = V10 / item["path"]
            self.assertTrue(path.is_file(), item["path"])
            self.assertEqual(path.stat().st_size, item["bytes"], item["path"])
            self.assertEqual(sha256(path), item["sha256"], item["path"])
            with Image.open(path) as image:
                self.assertEqual(image.size, (1024, 1024), item["path"])
                self.assertEqual(image.mode, "RGBA", item["path"])
                alpha = image.getchannel("A")
                self.assertIsNotNone(alpha.getbbox(), item["path"])
                border_maxima = [
                    alpha.crop((0, 0, 1024, 1)).getextrema()[1],
                    alpha.crop((0, 1023, 1024, 1024)).getextrema()[1],
                    alpha.crop((0, 0, 1, 1024)).getextrema()[1],
                    alpha.crop((1023, 0, 1024, 1024)).getextrema()[1],
                ]
                self.assertEqual(max(border_maxima), 0, item["path"])

    def test_only_cycle_three_differs_from_approved_v8(self):
        new_frames = sorted((V10 / "frames").glob("*.png"))
        self.assertEqual(len(new_frames), 13)
        for current in new_frames:
            previous = V8 / "frames" / current.name
            self.assertTrue(previous.is_file(), current.name)
            if current.name == "cycle-003.png":
                self.assertEqual(sha256(current), EXPECTED_REPLACEMENT_SHA256)
                self.assertNotEqual(sha256(current), sha256(previous))
            else:
                self.assertEqual(sha256(current), sha256(previous), current.name)

    def test_midpoint_anchor_matches_adjacent_frames(self):
        inventory = {item["path"]: item for item in self.manifest["frame_inventory"]}
        before = inventory["frames/cycle-002.png"]["alpha_bbox"]
        midpoint = inventory["frames/cycle-003.png"]["alpha_bbox"]
        after = inventory["frames/cycle-004.png"]["alpha_bbox"]
        self.assertEqual(midpoint, [37, 219, 1011, 905])
        self.assertLessEqual(abs(midpoint[0] - before[0]), 2)
        self.assertLessEqual(abs(midpoint[1] - min(before[1], after[1])), 4)
        self.assertLessEqual(abs(midpoint[2] - after[2]), 2)
        self.assertLessEqual(abs(midpoint[3] - max(before[3], after[3])), 1)

    def test_timeline_references_the_replacement_without_reordering(self):
        for action in self.manifest["actions"]:
            self.assertEqual(action["frame_count"], 16)
            self.assertEqual([phase["name"] for phase in action["phases"]], ["intro", "loop", "exit"])
            loop = action["phases"][1]
            self.assertEqual([frame["path"] for frame in loop["frames"]],
                             [f"frames/cycle-{index:03d}.png" for index in range(1, 9)])
            self.assertEqual(loop["frames"][2]["sha256"], EXPECTED_REPLACEMENT_SHA256)


if __name__ == "__main__":
    unittest.main()
