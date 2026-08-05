import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
APPROVAL = ROOT / "assets/action-batches/WK-BASIC-ACTIONS-BATCH-v2/approval.json"


class BasicActionKeyframesTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(APPROVAL.read_text(encoding="utf-8"))
        cls.frames = [frame for action in cls.manifest["actions"] for frame in action["frames"]]

    def test_approval_is_not_runtime_approval(self):
        self.assertEqual(self.manifest["status"], "approved-keyframes")
        self.assertTrue(self.manifest["owner_preview_approved"])
        self.assertEqual(self.manifest["runtime_validation"], "pending")
        self.assertFalse(self.manifest["runtime_approved"])
        self.assertFalse(self.manifest["runtime_use"])

    def test_expected_frame_count_and_unique_paths(self):
        self.assertEqual(len(self.frames), 17)
        self.assertEqual(len({frame["path"] for frame in self.frames}), 17)

    def test_pngs_decode_as_rgba_with_transparent_edges(self):
        for frame in self.frames:
            path = ROOT / frame["path"]
            with self.subTest(path=path):
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual(image.format, "PNG")
                    self.assertEqual(image.mode, "RGBA")
                    self.assertEqual(image.size, (1024, 1024))
                    alpha = image.getchannel("A")
                    self.assertIsNotNone(alpha.getbbox())
                    self.assertEqual(alpha.crop((0, 0, 1024, 1)).getextrema(), (0, 0))
                    self.assertEqual(alpha.crop((0, 1023, 1024, 1024)).getextrema(), (0, 0))
                    self.assertEqual(alpha.crop((0, 0, 1, 1024)).getextrema(), (0, 0))
                    self.assertEqual(alpha.crop((1023, 0, 1024, 1024)).getextrema(), (0, 0))

    def test_sizes_and_sha256_match_manifest(self):
        for frame in self.frames:
            path = ROOT / frame["path"]
            with self.subTest(path=path):
                data = path.read_bytes()
                self.assertEqual(len(data), frame["size_bytes"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), frame["sha256"])

    def test_action_manifests_match_approval_state(self):
        for action in self.manifest["actions"]:
            path = ROOT / "assets/actions" / action["action_id"] / "asset.json"
            with self.subTest(path=path):
                asset = json.loads(path.read_text(encoding="utf-8"))
                self.assertEqual(asset["status"], "approved-keyframes")
                self.assertTrue(asset["owner_preview_approved"])
                self.assertEqual(asset["runtime_validation"], "pending")
                self.assertFalse(asset["runtime_approved"])
                self.assertFalse(asset["runtime_use"])
                self.assertEqual(asset["approved_keyframes"]["frame_count"], len(action["frames"]))


if __name__ == "__main__":
    unittest.main()
