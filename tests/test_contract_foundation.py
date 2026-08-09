import json
import unittest

from tools.generate_pose_graph import build
from tools.validate_contracts import ROOT, validate


class ContractFoundationTests(unittest.TestCase):
    def test_contracts_have_no_validation_errors(self):
        result = validate(ROOT)
        self.assertEqual(result.errors, [])

    def test_runtime_registry_is_empty_until_assets_are_approved(self):
        registry = json.loads((ROOT / "contracts/runtime/asset-registry.json").read_text(encoding="utf-8"))
        self.assertEqual(registry["bindings"], [])

    def test_motion_only_video_candidates_cannot_claim_runtime_use(self):
        for path in (ROOT / "contracts/asset-sidecars").glob("*.json"):
            sidecar = json.loads(path.read_text(encoding="utf-8"))
            if sidecar["runtime_policy"]["reference_use"] == "motion_only":
                self.assertFalse(sidecar["runtime_policy"]["runtime_use"])
                self.assertNotEqual(sidecar["review"]["status"], "runtime-approved")

    def test_local_unpublished_sources_cannot_claim_runtime_readiness(self):
        for path in (ROOT / "contracts/asset-sidecars").glob("*.json"):
            sidecar = json.loads(path.read_text(encoding="utf-8"))
            if sidecar["manifest_availability"] == "local_unpublished":
                self.assertFalse(sidecar["runtime_policy"]["runtime_use"])
                self.assertNotEqual(sidecar["review"]["status"], "runtime-approved")

    def test_behavior_ids_are_decoupled_from_asset_versions(self):
        for path in (ROOT / "contracts/behaviors").glob("*.json"):
            behavior = json.loads(path.read_text(encoding="utf-8"))
            self.assertFalse(behavior["behavior_id"].endswith((".v1", ".v2", ".v3")))

    def test_pose_graph_exposes_known_p0_gaps(self):
        graph, mermaid, report = build()
        self.assertTrue(any(edge["missing_segments"] for edge in graph["edges"] if edge["phase"] == "P0"))
        self.assertIn("wk.core.walk_left / GAP", mermaid)
        self.assertIn("Standing idle has no visual candidate sidecar", report)


if __name__ == "__main__":
    unittest.main()
