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

    def test_owner_approved_posture_transitions_are_runtime_enabled(self):
        manifest = self.manifest
        self.assertEqual(manifest["asset_stage"], "runtime-approved")
        self.assertTrue(manifest["autonomous_semantics_owner_approved"])
        self.assertTrue(manifest["visual_approved"])
        self.assertEqual(manifest["runtime_validation"], "passed_windows_renderer_qa")
        self.assertTrue(manifest["runtime_approved"])
        self.assertTrue(manifest["runtime_use"])
        self.assertTrue(manifest["production_asset"])
        self.assertFalse(manifest["prototype_use"])
        self.assertTrue(manifest["autonomous_binding_enabled"])
        self.assertTrue(manifest["may_enter_autonomous_pool_by_default"])
        self.assertEqual(["AutonomousTick", "DeveloperPreview"], manifest["allowed_sources"])

    def test_expected_daily_action_inventory(self):
        expected = {
            "wk.daily.stand_to_sit": 10,
            "wk.daily.sit_to_prone": 12,
            "wk.daily.prone_to_sit": 4,
            "wk.daily.sit_to_stand": 5,
        }
        actual = {action["behavior_id"]: action["frame_count"] for action in self.manifest["actions"]}
        self.assertEqual(actual, expected)
        self.assertTrue(all(action["visual_approved"] for action in self.manifest["actions"]))
        self.assertTrue(all(action["runtime_validation"] == "passed_windows_renderer_qa" for action in self.manifest["actions"]))
        self.assertTrue(all(action["runtime_approved"] and action["runtime_use"] for action in self.manifest["actions"]))
        self.assertTrue(all(action["production_asset"] and action["autonomous_binding_enabled"] for action in self.manifest["actions"]))
        self.assertTrue(all(action["autonomous_semantics_owner_approved"] for action in self.manifest["actions"]))
        self.assertNotIn("wk.daily.playful_hop", actual)
        self.assertNotIn("wk.daily.playful_spin", actual)

    def test_bindings_resolve_exact_approved_source_ranges_without_duplicate_png(self):
        approved_batches = {
            "WK-COMMAND-PRODUCTION-CANDIDATES-v4": ROOT / "assets/action-mocks/WK-COMMAND-PRODUCTION-CANDIDATES-v4",
            "WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2": ROOT / "assets/action-batches/WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2",
        }
        self.assertEqual(self.manifest["storage_policy"], "reference_only_no_duplicate_png")
        self.assertEqual(list(BATCH.rglob("*.png")), [])

        for action in self.manifest["actions"]:
            binding = action["source_binding"]
            source_root = approved_batches[binding["asset_batch"]]
            source_manifest = json.loads((source_root / "manifest.json").read_text(encoding="utf-8"))
            source_action = next(item for item in source_manifest["actions"] if item["behavior_id"] == binding["behavior_id"])
            if binding["asset_batch"] == "WK-COMMAND-PRODUCTION-CANDIDATES-v4":
                self.assertEqual(binding["phase"], "mock")
                phase_frames = source_action["frames"]
            else:
                source_phase = next(phase for phase in source_action["phases"] if phase["name"] == binding["phase"])
                phase_frames = source_phase["frames"]

            start = binding["start_frame"] - 1
            frames = phase_frames[start : start + binding["frame_count"]]
            self.assertEqual(binding["frame_count"], action["frame_count"])
            self.assertEqual(len(frames), action["frame_count"])
            sequence_digest = hashlib.sha256()
            for frame in frames:
                with self.subTest(action=action["behavior_id"], frame=frame["path"]):
                    source = source_root / frame["path"]
                    data = source.read_bytes()
                    sequence_digest.update(data)
                    self.assertEqual(hashlib.sha256(data).hexdigest(), frame["sha256"])
                    self.assertEqual(len(data), frame["bytes"])
                    with Image.open(source) as image:
                        image.load()
                        self.assertEqual(image.size, (1024, 1024))
                        self.assertEqual(image.mode, "RGBA")
            self.assertEqual(sequence_digest.hexdigest(), binding["sequence_sha256"])

    def test_no_expired_red_asset_is_a_provenance_source(self):
        provenance = json.dumps([action["source_binding"] for action in self.manifest["actions"]], ensure_ascii=False).lower()
        self.assertNotIn("expired", provenance)
        self.assertNotIn("red-shiba", provenance)
        self.assertNotIn("v3.4", provenance)


if __name__ == "__main__":
    unittest.main()
