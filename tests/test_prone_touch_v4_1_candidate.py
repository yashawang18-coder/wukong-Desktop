import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-INTERACTION-PRONE-TOUCH-v4-1"


def load_json(name):
    return json.loads((BATCH / name).read_text(encoding="utf-8"))


class ProneTouchV41CandidateTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = load_json("manifest.json")
        cls.asset = load_json("asset.json")
        cls.contract = load_json("action-contract.json")

    def test_owner_rejected_asset_is_deprecated_and_all_runtime_gates_remain_closed(self):
        for document in (self.manifest, self.asset):
            self.assertEqual(document["runtime_validation"], "failed_owner_rejected")
            self.assertFalse(document["runtime_approved"])
            self.assertFalse(document["runtime_use"])
            self.assertTrue(document["deprecated"])
            self.assertEqual(document["deprecated_reason"], "owner_rejected_and_removed_from_use_2026_08_26")
            self.assertFalse(document["visual_approved"])
            self.assertFalse(document["production_asset"])
            self.assertFalse(document["autonomous_binding_enabled"])
            self.assertFalse(document["command_binding_enabled"])
            self.assertFalse(document["appearance_reference_usable"])
            self.assertFalse(document["motion_reference_usable"])
            self.assertFalse(document["fallback_eligible"])

        gate = self.contract["runtime_gate"]
        self.assertEqual(gate["runtime_validation"], "failed_owner_rejected")
        self.assertFalse(gate["runtime_approved"])
        self.assertFalse(gate["runtime_use"])
        self.assertTrue(gate["must_not_register_until_all_gates_pass"])
        self.assertTrue(gate["permanently_removed_from_use"])
        self.assertTrue(self.contract["deprecated"])
        self.assertFalse(self.contract["fallback_eligible"])
        self.assertFalse(self.asset["runtime_registration"]["registered"])
        self.assertFalse(self.asset["runtime_registration"]["permitted"])

    def test_manifest_lists_exactly_seventy_png_frames(self):
        files = self.manifest["files"]
        png_entries = [item for item in files if item["path"].endswith(".png")]
        self.assertEqual(self.manifest["png_sequence_frame_count"], 70)
        self.assertEqual(len(png_entries), 70)
        self.assertEqual(len(files), self.manifest["file_count_excluding_manifest"])

        paths = [item["path"] for item in files]
        self.assertEqual(len(paths), len(set(paths)))

    def test_png_files_decode_and_match_manifest(self):
        for item in self.manifest["files"]:
            if not item["path"].endswith(".png"):
                continue

            path = BATCH / item["path"]
            with self.subTest(path=item["path"]):
                data = path.read_bytes()
                self.assertEqual(len(data), item["bytes"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), item["sha256"])
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual(image.format, "PNG")
                    self.assertEqual(image.mode, item["mode"])
                    self.assertEqual(image.size, (item["width"], item["height"]))
                    self.assertEqual(image.size, (1024, 1024))
                    self.assertEqual(list(image.getchannel("A").getbbox()), item["alpha_bbox"])

    def test_sequence_counts_match_asset_manifest(self):
        self.assertEqual(self.asset["asset_id"], "wk.interaction.prone_touch.v4.1")
        self.assertEqual(self.asset["behavior_id"], "wk.interaction.prone_touch")
        self.assertEqual(self.contract["behavior_id"], "wk.interaction.prone_touch")
        self.assertEqual(self.contract["fallback_behavior"], "wk.core.prone_idle")

        total = 0
        for sequence in self.asset["sequences"]:
            path = BATCH / sequence["path"]
            frames = sorted(path.glob("frame-*.png"))
            self.assertEqual(len(frames), sequence["frame_count"])
            total += len(frames)
        self.assertEqual(total, 70)

    def test_candidate_is_additive_and_excludes_rejected_sources(self):
        preservation = self.asset["preservation"]
        self.assertEqual(preservation["previously_approved_keyframe_count"], 17)
        self.assertFalse(preservation["previously_approved_keyframes_modified"])
        self.assertFalse(preservation["previously_approved_keyframes_included"])
        self.assertNotIn("V3.4", [path.name for path in BATCH.rglob("*")])


if __name__ == "__main__":
    unittest.main()
