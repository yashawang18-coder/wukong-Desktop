import hashlib
import json
import unittest
from pathlib import Path

import numpy as np
from PIL import Image, ImageSequence


ROOT = Path(__file__).resolve().parents[1]
BATCH_ID = "WK-AUTONOMOUS-PATROL-WALK-v1-candidate"
BATCH = ROOT / "assets" / "action-batches" / BATCH_ID
SOURCE_ZIP_SHA256 = "a96ff60c48c0fe79e8c7a20d1d62b1658eebc8175e959b70d6b81f40c4f958ed"


class PatrolWalkCandidateV1Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.asset = json.loads((BATCH / "asset.json").read_text(encoding="utf-8"))
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))
        cls.source_report = json.loads((BATCH / "qa-report.json").read_text(encoding="utf-8"))

    def test_owner_approved_gate_allows_autonomous_gait_without_window_motion(self):
        for document in (self.asset, self.manifest):
            self.assertEqual(BATCH_ID, document["asset_id"])
            self.assertEqual(SOURCE_ZIP_SHA256, document["source_zip_sha256"])
            self.assertTrue(document["owner_preview_approved"])
            self.assertTrue(document["visual_approved"])
            self.assertEqual("passed_windows_renderer_qa", document["runtime_validation"])
            self.assertTrue(document["runtime_approved"])
            self.assertTrue(document["runtime_use"])
            self.assertTrue(document["production_asset"])
            self.assertFalse(document["prototype_use"])
            self.assertTrue(document["developer_preview"])
            self.assertTrue(document["autonomous_binding_enabled"])
            self.assertFalse(document["window_motion_enabled"])
            self.assertEqual(["AutonomousTick", "DeveloperPreview"], document["allowed_sources"])

    def test_all_runtime_frames_are_original_rgba_and_have_transparent_edges(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(24, len(inventory))
        self.assertEqual(24, len({item["path"] for item in inventory}))
        self.assertEqual(24, len(list((BATCH / "frames").glob("*/*.png"))))

        declared = {}
        for line in (BATCH / "SOURCE-FRAME-SHA256SUMS.sha256").read_text(encoding="ascii").splitlines():
            digest, relative = line.split("  ", 1)
            declared[relative] = digest
        self.assertEqual(24, len(declared))

        for item in inventory:
            path = BATCH / item["path"]
            payload = path.read_bytes()
            digest = hashlib.sha256(payload).hexdigest()
            self.assertEqual(item["bytes"], len(payload), item["path"])
            self.assertEqual(item["sha256"], digest, item["path"])
            self.assertEqual(item["source_sha256"], digest, item["path"])
            self.assertEqual(declared[item["path"]], digest, item["path"])
            with Image.open(path) as image:
                image.load()
                self.assertEqual("PNG", image.format, item["path"])
                self.assertEqual("RGBA", image.mode, item["path"])
                self.assertEqual((1024, 1024), image.size, item["path"])
                alpha = np.asarray(image, dtype=np.uint8)[..., 3]
                self.assertGreater(int(np.count_nonzero(alpha)), 0, item["path"])
                self.assertTrue(np.all(alpha[0, :] == 0), item["path"])
                self.assertTrue(np.all(alpha[-1, :] == 0), item["path"])
                self.assertTrue(np.all(alpha[:, 0] == 0), item["path"])
                self.assertTrue(np.all(alpha[:, -1] == 0), item["path"])

    def test_right_sequence_is_exact_horizontal_mirror_and_preview_timing_matches(self):
        self.assertTrue(self.source_report["all_mirror_pixel_equal"])
        for index in range(1, 13):
            left_path = BATCH / "frames" / "walk-left" / f"frame-{index:03d}.png"
            right_path = BATCH / "frames" / "walk-right" / f"frame-{index:03d}.png"
            with Image.open(left_path) as left, Image.open(right_path) as right:
                self.assertTrue(np.array_equal(np.asarray(left)[:, ::-1], np.asarray(right)), f"mirror pair {index}")

        for direction in ("left", "right"):
            with Image.open(BATCH / "review" / f"walk-{direction}-checker.gif") as preview:
                durations = [frame.info["duration"] for frame in ImageSequence.Iterator(preview)]
                self.assertEqual([110] * 12, durations)

    def test_actions_are_loop_only_and_in_explicit_autonomous_allowlist(self):
        actions = self.manifest["actions"]
        expected = {
            "wk.candidate.autonomous.patrol_walk_left_v1",
            "wk.candidate.autonomous.patrol_walk_right_v1",
        }
        self.assertEqual(expected, {action["behavior_id"] for action in actions})
        for action in actions:
            self.assertEqual(12, action["frame_count"])
            self.assertEqual(110, action["frame_duration_ms"])
            self.assertEqual(1320, action["total_duration_ms"])
            self.assertTrue(action["loop"])
            self.assertEqual(["loop"], [phase["name"] for phase in action["phases"]])

        source = (ROOT / "src" / "Wukong.Desktop" / "DesktopPetRuntime.cs").read_text(encoding="utf-8")
        allowlist = source.split("AutonomousRuntimeAllowlist", 1)[1].split("};", 1)[0]
        self.assertIn("PatrolWalkCandidateBehaviorIds.WalkLeft", allowlist)
        self.assertIn("PatrolWalkCandidateBehaviorIds.WalkRight", allowlist)
        self.assertIn("LoadPatrolWalkCandidates", source)


if __name__ == "__main__":
    unittest.main()
