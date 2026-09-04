import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class MirrorEligibilityAuditTests(unittest.TestCase):
    def test_repository_audit_is_fail_closed_and_does_not_runtime_integrate_mirrors(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "mirror-audit.json"
            subprocess.run(
                [sys.executable, str(ROOT / "scripts" / "audit_mirror_eligibility.py"), "--root", str(ROOT), "--output", str(output)],
                check=True,
                cwd=ROOT,
            )
            report = json.loads(output.read_text(encoding="utf-8"))

        self.assertEqual("fail_closed_explicit_mirror_safe_only", report["policy"])
        self.assertTrue(report["native_opposite_direction_preferred"])
        self.assertFalse(report["approved_source_pixels_modified_in_place"])
        self.assertGreaterEqual(len(report["packages"]), 20)
        self.assertEqual(0, report["runtime_integrated_mirror_count"])
        self.assertTrue(all(not item["runtime_integration_allowed"] for item in report["packages"]))

        packages = {item["asset_batch"]: item for item in report["packages"]}
        for asset in ("WK-INTERACTION-CAR-RIDE-CANDIDATE-v8", "WK-MAGIC-SPECIALS-CANDIDATE-v1"):
            self.assertTrue(packages[asset]["native_directional_variants"])
            self.assertIn("native_directional_variants_exist_prefer_native_art", packages[asset]["reasons"])


if __name__ == "__main__":
    unittest.main()
