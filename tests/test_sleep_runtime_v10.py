import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH_ID = "WK-AUTONOMOUS-SLEEP-RUNTIME-FINAL-CANDIDATE-v10"
BATCH = ROOT / "assets" / "action-batches" / BATCH_ID
OLD_V5_BATCH = ROOT / "assets" / "action-batches" / "WK-AUTONOMOUS-SLEEP-MOTION-REFINEMENT-CANDIDATE-v5"
SOURCE_ZIP_SHA256 = "174350b0aaa7d01a6639d8ce189fb7a12d3541e5dc5ce4460b1461d8f0d1c701"


class SleepRuntimeV10Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.asset = json.loads((BATCH / "asset.json").read_text(encoding="utf-8"))
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_v10_approved_actions_and_deprecated_variants_have_distinct_gates(self):
        for document in (self.asset, self.manifest):
            self.assertEqual(BATCH_ID, document["asset_id"])
            self.assertEqual(10, document["asset_version"])
            self.assertEqual(SOURCE_ZIP_SHA256, document["source_zip_sha256"])
            self.assertTrue(document["owner_preview_approved"])
            self.assertTrue(document["owner_material_visual_confirmed"])
            self.assertTrue(document["visual_approved"])
            self.assertEqual("passed_windows_renderer_qa", document["runtime_validation"])
            self.assertTrue(document["runtime_approved"])
            self.assertTrue(document["runtime_use"])
            self.assertTrue(document["production_asset"])
            self.assertFalse(document["prototype_use"])
            self.assertTrue(document["developer_preview"])
            self.assertTrue(document["autonomous_binding_enabled"])
            self.assertEqual(["AutonomousTick", "DeveloperPreview"], document["allowed_sources"])

    def test_all_48_runtime_pngs_match_manifest_and_checksum_inventory(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(48, len(inventory))
        self.assertEqual(48, len({item["path"] for item in inventory}))
        self.assertEqual(48, len(list((BATCH / "frames").glob("*/*.png"))))

        checksum_lines = (BATCH / "SOURCE-FRAME-SHA256SUMS.sha256").read_text(encoding="ascii").splitlines()
        self.assertEqual(48, len(checksum_lines))
        declared = dict(line.split("  ", 1)[::-1] for line in checksum_lines)
        for item in inventory:
            path = BATCH / item["path"]
            payload = path.read_bytes()
            digest = hashlib.sha256(payload).hexdigest()
            self.assertEqual(item["bytes"], len(payload), item["path"])
            self.assertEqual(item["sha256"], digest, item["path"])
            self.assertEqual(item["source_sha256"], digest, item["path"])
            self.assertEqual(declared[item["path"]], digest, item["path"])
            self.assertEqual(item["source_entry"], item["path"].removeprefix("frames/"))

            with Image.open(path) as image:
                image.load()
                self.assertEqual("PNG", image.format, item["path"])
                self.assertEqual("RGBA", image.mode, item["path"])
                self.assertEqual((1024, 1024), image.size, item["path"])
                alpha = image.getchannel("A")
                self.assertEqual(0, alpha.getextrema()[0], item["path"])
                self.assertGreater(alpha.getextrema()[1], 0, item["path"])
                self.assertEqual(0, alpha.crop((0, 0, 1024, 1)).getextrema()[1], item["path"])
                self.assertEqual(0, alpha.crop((0, 1023, 1024, 1024)).getextrema()[1], item["path"])
                self.assertEqual(0, alpha.crop((0, 0, 1, 1024)).getextrema()[1], item["path"])
                self.assertEqual(0, alpha.crop((1023, 0, 1024, 1024)).getextrema()[1], item["path"])

    def test_sequence_counts_order_and_preview_timings_are_explicit(self):
        expected = {
            "wk.candidate.sleep.main_lifecycle_v2": ("01-main-sleep-lifecycle", 16, [260] * 15 + [1100], False),
            "wk.candidate.sleep.prone_to_side_roll_v2": ("02-prone-to-side-roll", 8, [260] * 7 + [800], False),
            "wk.candidate.sleep.sprawled_front_breath_v2": ("03-sprawled-front-breath", 4, [650] * 4, True),
            "wk.candidate.sleep.sprawled_left_side_breath_v2": ("04-sprawled-left-side-breath", 4, [650] * 4, True),
            "wk.candidate.sleep.sprawled_right_side_breath_v2": ("05-sprawled-right-side-breath", 4, [650] * 4, True),
            "wk.candidate.sleep.compact_prone_breath_v2": ("06-compact-prone-breath", 4, [650] * 4, True),
            "wk.candidate.sleep.curled_side_breath_v2": ("07-curled-side-breath", 4, [650] * 4, True),
            "wk.candidate.sleep.top_down_prone_breath_v2": ("08-top-down-prone-breath", 4, [650] * 4, True),
        }
        actions = self.manifest["actions"]
        expected_scales = {
            "wk.candidate.sleep.main_lifecycle_v2": 0.61,
            "wk.candidate.sleep.prone_to_side_roll_v2": 0.64,
            "wk.candidate.sleep.sprawled_front_breath_v2": 0.63,
            "wk.candidate.sleep.sprawled_left_side_breath_v2": 0.78,
            "wk.candidate.sleep.sprawled_right_side_breath_v2": 0.80,
            "wk.candidate.sleep.compact_prone_breath_v2": 1.01,
            "wk.candidate.sleep.curled_side_breath_v2": 0.92,
            "wk.candidate.sleep.top_down_prone_breath_v2": 1.04,
        }
        deprecated_ids = {
            "wk.candidate.sleep.sprawled_right_side_breath_v2",
            "wk.candidate.sleep.compact_prone_breath_v2",
            "wk.candidate.sleep.curled_side_breath_v2",
            "wk.candidate.sleep.top_down_prone_breath_v2",
        }
        autonomous_ids = {
            "wk.candidate.sleep.main_lifecycle_v2",
            "wk.candidate.sleep.sprawled_front_breath_v2",
        }
        self.assertEqual(set(expected), {action["behavior_id"] for action in actions})
        self.assertEqual(48, sum(action["frame_count"] for action in actions))
        for action in actions:
            source_name, count, durations, loop = expected[action["behavior_id"]]
            phase = action["phases"][0]
            self.assertEqual([f"F{index:02d}.png" for index in range(1, count + 1)], [Path(frame["path"]).name for frame in phase["frames"]])
            self.assertTrue(all(frame["path"].startswith(f"frames/{source_name}/") for frame in phase["frames"]))
            self.assertEqual(durations, [frame["duration_ms"] for frame in phase["frames"]])
            self.assertEqual(sum(durations), action["total_duration_ms"])
            self.assertEqual(loop, action["loop"])
            self.assertEqual(expected_scales[action["behavior_id"]], action["runtime_render_scale"])
            deprecated = action["behavior_id"] in deprecated_ids
            self.assertEqual(deprecated, action["deprecated"])
            if deprecated:
                self.assertFalse(action["owner_preview_approved"])
                self.assertFalse(action["visual_approved"])
                self.assertFalse(action["runtime_approved"])
                self.assertFalse(action["runtime_use"])
                self.assertFalse(action["production_asset"])
                self.assertFalse(action["autonomous_binding_enabled"])
                self.assertEqual("failed_owner_visual_qa", action["runtime_validation"])
                self.assertFalse(action["developer_preview"])
                self.assertEqual([], action["allowed_sources"])
                self.assertEqual("owner_rejected_color_and_fur_texture_2026_09_05", action["deprecated_reason"])
            else:
                self.assertTrue(action["owner_preview_approved"])
                self.assertTrue(action["visual_approved"])
                self.assertEqual("passed_windows_renderer_qa", action["runtime_validation"])
                self.assertTrue(action["runtime_approved"])
                self.assertTrue(action["runtime_use"])
                self.assertTrue(action["production_asset"])
                self.assertFalse(action["prototype_use"])
                self.assertTrue(action["developer_preview"])
                autonomous = action["behavior_id"] in autonomous_ids
                self.assertEqual(autonomous, action["autonomous_binding_enabled"])
                self.assertEqual(["AutonomousTick", "DeveloperPreview"] if autonomous else ["DeveloperPreview"], action["allowed_sources"])
                self.assertIsNone(action["deprecated_reason"])

    def test_v5_and_omitted_views_are_not_active(self):
        self.assertFalse(OLD_V5_BATCH.exists())
        behavior_ids = {action["behavior_id"] for action in self.manifest["actions"]}
        self.assertNotIn("wk.candidate.sleep.front_three_quarter_side_breath_v2", behavior_ids)
        self.assertNotIn("wk.candidate.sleep.right_rear_butt_breath_v2", behavior_ids)
        desktop_sources = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / "src" / "Wukong.Desktop").glob("*.cs"))
        self.assertNotIn("WK-AUTONOMOUS-SLEEP-MOTION-REFINEMENT-CANDIDATE-v5", desktop_sources)
        self.assertNotIn('"actions/WK-CORE-SLEEP-BREATH-v2/approved-keyframes/v1"', desktop_sources)
        rules = self.manifest["sequence_rules"]
        self.assertFalse(rules["append_prone_to_side_roll_after_main"])
        self.assertTrue(rules["each_sleep_entry_selects_one_compatible_view"])
        self.assertFalse(rules["hard_cut_between_incompatible_views"])
        self.assertFalse(rules["reverse_main_as_wake"])
        self.assertFalse(rules["approved_wake_sequence_available"])
        self.assertFalse(rules["legacy_sleep_visual_fallback_allowed"])

    def test_runtime_prone_visible_body_area_is_owner_approved(self):
        comparison = self.manifest["current_runtime_prone_bridge"]
        self.assertTrue(comparison["candidate_f01_bytes_preserved"])
        self.assertEqual("passed_windows_renderer_qa", comparison["windows_transition_review"])
        ratio = comparison["candidate_scaled_visible_major_axis_px"] / comparison["runtime_scaled_visible_major_axis_px"]
        self.assertGreaterEqual(ratio, 1.00)
        self.assertLessEqual(ratio, 1.10)

        reference_path = ROOT / comparison["runtime_anchor_path"]
        with Image.open(reference_path) as image:
            alpha_histogram = image.getchannel("A").histogram()
            reference_body_size = sum(alpha_histogram[1:]) ** 0.5 * 0.68
        for action in self.manifest["actions"]:
            if action["deprecated"]:
                continue
            visible_body_sizes = []
            for frame in action["phases"][0]["frames"]:
                with Image.open(BATCH / frame["path"]) as image:
                    alpha_histogram = image.getchannel("A").histogram()
                    visible_body_sizes.append(sum(alpha_histogram[1:]) ** 0.5)
            median_body_size = sorted(visible_body_sizes)[len(visible_body_sizes) // 2]
            ratio = median_body_size * action["runtime_render_scale"] / reference_body_size
            self.assertGreaterEqual(ratio, 0.88, action["behavior_id"])
            self.assertLessEqual(ratio, 1.12, action["behavior_id"])


if __name__ == "__main__":
    unittest.main()
