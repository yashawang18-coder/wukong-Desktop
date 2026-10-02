import hashlib
import json
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from audit_companion_art import ROOT, REFERENCES, REVIEW_BATCHES, metrics


class CompanionArtAuditTests(unittest.TestCase):
    def test_references_decode_without_changing_source_bytes(self):
        for relative in REFERENCES.values():
            path = ROOT / relative
            before = hashlib.sha256(path.read_bytes()).hexdigest()
            result = metrics(path)
            self.assertEqual(before, result["sha256"])
            self.assertEqual(before, hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertEqual([1024, 1024], result["size"])
            self.assertEqual("RGBA", result["mode"])
            self.assertIsNotNone(result["alpha_bounds"]["1"])

    def test_repair_scope_excludes_commands_magic_and_runtime_promotion(self):
        self.assertTrue(all("COMMAND" not in batch and "MAGIC" not in batch for batch in REVIEW_BATCHES))
        self.assertEqual(3, len(REFERENCES))
        self.assertNotIn("prone_coat_texture", REFERENCES)
        self.assertIn("WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4", REFERENCES["front_prone_pose"])
        selection = json.loads((ROOT / "assets/identity/wukong-early-approved-reference-v1/manifest.json").read_text(encoding="utf-8"))
        self.assertFalse(selection["runtime_approved"])
        self.assertFalse(selection["runtime_use"])
        self.assertFalse(selection["may_register_in_runtime_manifest"])
        for reference in selection["references"]:
            self.assertEqual(reference["sha256"], hashlib.sha256((ROOT / reference["path"]).read_bytes()).hexdigest())


if __name__ == "__main__":
    unittest.main()
