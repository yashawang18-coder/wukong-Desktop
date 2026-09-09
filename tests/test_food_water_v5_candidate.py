import hashlib
import json
import unittest
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH_ID = "WK-INTERACTION-FOOD-WATER-COAT-SEAM-CANDIDATE-v5"
BATCH = ROOT / "assets" / "action-batches" / BATCH_ID
SOURCE_MANIFEST_SHA256 = "db81cb11a0dd4e3bc3b10dd6f013c4a0d9cbc99561a7fd3e75f6fb9c6e860449"


class FoodWaterV5CandidateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.asset = json.loads((BATCH / "asset.json").read_text(encoding="utf-8"))
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_owner_runtime_gate_is_approved_but_autonomous_stays_closed(self):
        for document in (self.asset, self.manifest):
            self.assertEqual(BATCH_ID, document["batch_id"])
            self.assertEqual(BATCH_ID, document["asset_id"])
            self.assertEqual("WK-FOOD-WATER-COAT-SEAM-v5", document["source_package"])
            self.assertEqual(SOURCE_MANIFEST_SHA256, document["source_sha256_manifest_sha256"])
            self.assertTrue(document["owner_preview_approved"])
            self.assertTrue(document["visual_approved"])
            self.assertEqual("passed_windows_renderer_qa", document["runtime_validation"])
            self.assertTrue(document["runtime_approved"])
            self.assertTrue(document["runtime_use"])
            self.assertTrue(document["production_asset"])
            self.assertFalse(document["prototype_use"])
            self.assertTrue(document["developer_preview"])
            self.assertFalse(document["autonomous_binding_enabled"])
            self.assertTrue(document["normal_runtime_available"])
            self.assertEqual(["OwnerContextMenu", "ControlPanel", "DeveloperPreview"], document["allowed_sources"])

    def test_exact_source_rgba_bytes_are_preserved(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(48, self.manifest["source_frame_count"])
        self.assertEqual(48, self.manifest["runtime_frame_count"])
        self.assertEqual(48, len(inventory))
        self.assertEqual(48, len({entry["path"] for entry in inventory}))
        self.assertEqual(48, len(list((BATCH / "frames").glob("*/*.png"))))

        runtime_sums = self._read_sums(BATCH / "RUNTIME-FRAME-SHA256SUMS.sha256")
        source_sums = self._read_sums(BATCH / "SOURCE-FRAME-SHA256SUMS.sha256")
        self.assertEqual(runtime_sums, source_sums)
        self.assertEqual(48, len(runtime_sums))

        blue_advantage_total = 0
        for entry in inventory:
            path = BATCH / entry["path"]
            payload = path.read_bytes()
            digest = hashlib.sha256(payload).hexdigest()
            self.assertEqual(entry["bytes"], len(payload), entry["path"])
            self.assertEqual(entry["sha256"], digest, entry["path"])
            self.assertEqual(runtime_sums[entry["path"]], digest, entry["path"])
            blue_advantage_total += entry["visible_blue_advantage_pixels"]

            with Image.open(path) as image:
                image.load()
                self.assertEqual("PNG", image.format, entry["path"])
                self.assertEqual("RGBA", image.mode, entry["path"])
                self.assertEqual((1024, 1024), image.size, entry["path"])
                array = np.asarray(image)
            alpha = array[..., 3]
            self.assertEqual(0, int(alpha[0, :].max()), entry["path"])
            self.assertEqual(0, int(alpha[-1, :].max()), entry["path"])
            self.assertEqual(0, int(alpha[:, 0].max()), entry["path"])
            self.assertEqual(0, int(alpha[:, -1].max()), entry["path"])
            self.assertGreater(int(alpha.max()), 0, entry["path"])
        self.assertEqual(63, blue_advantage_total, "known sparse source-edge colour audit changed")

    def test_sequence_references_order_timing_and_pose_contract(self):
        self.assertEqual(194, self.manifest["sequence_frame_reference_count"])
        self.assertEqual(44, self.manifest["referenced_unique_frame_count"])
        self.assertEqual(
            [
                "frames/drink-pause/frame-05.png",
                "frames/drink-pause/frame-06.png",
                "frames/eat-pause/frame-05.png",
                "frames/eat-pause/frame-06.png",
            ],
            self.manifest["unreferenced_source_frames"],
        )
        expected = {
            "wk.interaction.drink_water": (91, 11_375, [6, 75, 10]),
            "wk.interaction.eat_kibble": (103, 12_875, [6, 87, 10]),
        }
        actions = self.manifest["actions"]
        self.assertEqual(set(expected), {action["behavior_id"] for action in actions})
        references = []
        inventory = {entry["path"]: entry for entry in self.manifest["frame_inventory"]}
        for action in actions:
            count, total_ms, phase_counts = expected[action["behavior_id"]]
            self.assertEqual(count, action["frame_count"])
            self.assertEqual(total_ms, action["total_duration_ms"])
            self.assertEqual(125, action["frame_duration_ms"])
            self.assertEqual("stand.neutral.left_front", action["from_pose"])
            self.assertEqual("stand.neutral.left_front", action["to_pose"])
            self.assertFalse(action["interruptible"])
            self.assertFalse(action["loop"])
            self.assertEqual(["intro", "action", "exit"], [phase["name"] for phase in action["phases"]])
            self.assertEqual(phase_counts, [phase["frame_count"] for phase in action["phases"]])
            frames = [frame for phase in action["phases"] for frame in phase["frames"]]
            self.assertEqual(count, len(frames))
            self.assertTrue(all(frame["duration_ms"] == 125 for frame in frames))
            for frame in frames:
                self.assertIn(frame["path"], inventory)
                self.assertEqual(inventory[frame["path"]]["sha256"], frame["sha256"])
                references.append(frame["path"])
        self.assertEqual(194, len(references))
        self.assertEqual(44, len(set(references)))

    def test_source_qa_and_import_limits_are_preserved(self):
        source_qa = json.loads((BATCH / "provenance" / "SOURCE-QA-summary.json").read_text(encoding="utf-8"))
        self.assertEqual(48, source_qa["unique_RGBA_frames"])
        self.assertEqual(5, source_qa["modified_frames"])
        self.assertEqual(43, source_qa["unchanged_frames"])
        self.assertTrue(source_qa["alpha_and_geometry_unchanged"])
        self.assertFalse(source_qa["owner_visual_approved"])
        self.assertFalse(source_qa["EXE_tested"])

        report = json.loads((BATCH / "IMPORT-VALIDATION-REPORT.json").read_text(encoding="utf-8"))
        self.assertTrue(report["byte_preserving_copy"])
        self.assertTrue(report["all_source_checksums_passed"])
        self.assertEqual("passed_windows_renderer_qa", report["runtime_validation"])

    def test_desktop_integration_uses_owner_normal_route(self):
        desktop = (ROOT / "src" / "Wukong.Desktop" / "DesktopPetRuntime.cs").read_text(encoding="utf-8")
        panel = (ROOT / "src" / "Wukong.Desktop" / "ControlPanelWindow.xaml").read_text(encoding="utf-8")
        self.assertIn("FoodWaterCandidateBehaviorIds", desktop)
        self.assertIn("FoodWaterCandidateMotions", desktop)
        menu = (ROOT / "src" / "Wukong.Desktop" / "MainWindow.xaml").read_text(encoding="utf-8")
        self.assertIn('Content="吃一下"', panel)
        self.assertIn('x:Name="FoodWaterCandidateList"', panel)
        self.assertIn('Click="ShowFoodWater_Click"', panel)
        self.assertIn('Header="&#x559D;&#x6C34;" Tag="wk.interaction.drink_water"', menu)
        self.assertIn('Header="&#x5403;&#x996D;" Tag="wk.interaction.eat_kibble"', menu)
        self.assertIn("SubmitFoodWaterAsync", desktop)
        self.assertIn("food_water_source_forbidden", desktop)
        self.assertNotIn('Content="餐饮候选（开发者）"', panel)
        self.assertNotIn('Click="ForceFoodWaterCandidate_Click"', panel)

    @staticmethod
    def _read_sums(path: Path) -> dict[str, str]:
        result = {}
        for line in path.read_text(encoding="ascii").splitlines():
            digest, relative = line.split("  ", 1)
            result[relative] = digest
        return result


if __name__ == "__main__":
    unittest.main()
