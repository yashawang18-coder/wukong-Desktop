import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-P0-GENERATED-ACTIONS-2026-08-06"


def load_json(relative_path):
    return json.loads((BATCH / relative_path).read_text(encoding="utf-8"))


class GeneratedActionCandidateImportTest(unittest.TestCase):
    def test_import_manifest_keeps_runtime_gates_closed(self):
        manifest = load_json("manifest.json")
        self.assertEqual(manifest["status"], "runtime-candidate-import")
        self.assertEqual(manifest["runtime_validation"], "pending")
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])

        for package in manifest["packages"]:
            with self.subTest(package=package["package_id"]):
                self.assertEqual(package["runtime_validation"], "pending")
                self.assertFalse(package["runtime_approved"])
                self.assertFalse(package["runtime_use"])

    def test_selected_png_files_match_manifests(self):
        manifests = [
            (BATCH / "sit-stand", load_json("sit-stand/keyposes-approved.json")),
            (BATCH / "sit-stand", load_json("sit-stand/runtime-candidate-v2.json")),
            (BATCH / "walk-transitions", load_json("walk-transitions/keyposes-approved.json")),
            (BATCH / "walk-transitions", load_json("walk-transitions/runtime-candidate-v3-walk-start.json")),
            (BATCH / "walk-transitions", load_json("walk-transitions/runtime-candidate-v4-geometry-stabilized.json")),
        ]

        frames = []
        frames.extend((manifests[0][0], frame) for frame in manifests[0][1]["frames"])
        frames.extend((manifests[1][0], frame) for frame in manifests[1][1]["candidate_intermediate_frames"])
        frames.extend((manifests[2][0], frame) for frame in manifests[2][1]["frames"])
        frames.extend((manifests[3][0], frame) for frame in manifests[3][1]["candidate_intermediate_frames"])
        frames.extend((manifests[4][0], frame) for frame in manifests[4][1]["candidate_intermediate_frames"])

        self.assertEqual(len(frames), 13)
        for base, frame in frames:
            path = base / frame["path"] if not frame["path"].startswith("assets/") else ROOT / frame["path"]
            with self.subTest(path=path):
                data = path.read_bytes()
                self.assertEqual(len(data), frame["size_bytes"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), frame["sha256"])
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual(image.format, "PNG")
                    self.assertEqual(image.mode, "RGBA")
                    self.assertEqual(image.size, (1024, 1024))

    def test_forbidden_import_artifacts_are_absent(self):
        forbidden_suffixes = {".gif", ".jpg", ".jpeg", ".tmp", ".mp4", ".zip"}
        forbidden_tokens = {
            "__pycache__",
            "contact",
            "stop-i1",
            "attempt2",
            "Library",
            "Wukong-Seedance-Base-Actions-video-v2",
        }

        for path in BATCH.rglob("*"):
            relative = path.relative_to(BATCH).as_posix()
            with self.subTest(path=relative):
                self.assertFalse(any(relative.endswith(suffix) for suffix in forbidden_suffixes))
                self.assertFalse(any(token in relative for token in forbidden_tokens))

    def test_new_action_manifests_are_not_runtime_approved(self):
        action_ids = [
            "WK-CORE-STAND-IDLE-LF-v1",
            "WK-CORE-STAND-TO-SIT-LF-v1",
            "WK-CORE-SIT-TO-STAND-LF-v1",
            "WK-CORE-WALK-LEFT-TRANSITIONS-v1",
        ]

        for action_id in action_ids:
            path = ROOT / "assets/actions" / action_id / "asset.json"
            with self.subTest(path=path):
                asset = json.loads(path.read_text(encoding="utf-8"))
                self.assertEqual(asset["runtime_validation"], "pending")
                self.assertFalse(asset["runtime_approved"])
                self.assertFalse(asset["runtime_use"])


if __name__ == "__main__":
    unittest.main()
