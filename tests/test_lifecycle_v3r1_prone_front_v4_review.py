import hashlib
import json
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH_ROOT = ROOT / "assets" / "action-batches"
V3 = BATCH_ROOT / "WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED"
V4 = BATCH_ROOT / "WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4"
V2_MANIFEST_SHA256 = "bc6cb9ed8c41d72f0d21c30e827ea487271d716f193064b1fabf609cf118467b"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class LifecycleV3R1ProneFrontV4ReviewTests(unittest.TestCase):
    def test_source_freeze_checksums_remain_valid(self):
        for batch in (V3, V4):
            checked = 0
            for raw in (batch / "SHA256SUMS").read_text(encoding="utf-8-sig").splitlines():
                line = raw.strip()
                if not line or line.startswith("#"):
                    continue
                expected, relative = line.split(None, 1)
                path = batch / relative.lstrip("*").strip()
                self.assertTrue(path.is_file(), f"missing frozen source file: {path}")
                self.assertEqual(expected.lower(), sha256(path), f"source package changed: {path}")
                checked += 1
            self.assertGreater(checked, 0, f"empty checksum inventory: {batch}")

    def test_runtime_frames_are_complete_rgba_and_keep_strict_baselines(self):
        expectations = ((V3, 68, {930, 931}), (V4, 24, {899}))
        for batch, expected_count, expected_last_alpha_rows in expectations:
            frames = sorted((batch / "frames").rglob("*.png"))
            self.assertEqual(expected_count, len(frames), batch.name)
            last_alpha_rows = set()
            for path in frames:
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual((1024, 1024), image.size, path)
                    self.assertEqual("RGBA", image.mode, path)
                    alpha_bounds = image.getchannel("A").getbbox()
                    self.assertIsNotNone(alpha_bounds, path)
                    last_alpha_rows.add(alpha_bounds[3] - 1)
            self.assertEqual(expected_last_alpha_rows, last_alpha_rows, batch.name)

    def test_forward_prone_anchor_and_microevent_contract(self):
        anchor = V4 / "source" / "prone-front-stable-anchor.png"
        for sequence in ("prone-idle-front-calm", "prone-idle-front-lick"):
            frames = sorted((V4 / "frames" / sequence).glob("*.png"))
            self.assertEqual(12, len(frames))
            self.assertEqual(anchor.read_bytes(), frames[0].read_bytes())
            self.assertEqual(anchor.read_bytes(), frames[-1].read_bytes())

        review = json.loads((V4 / "runtime-review-manifest.json").read_text(encoding="utf-8"))
        actions = {action["behavior_id"]: action for action in review["actions"]}
        calm = actions["wk.candidate.lifecycle.prone_idle_front_microloop_v4"]
        lick = actions["wk.candidate.daily.prone_front_lick_microevent_v4"]
        self.assertTrue(calm["phases"][0]["loop"])
        self.assertFalse(lick["phases"][0]["loop"])
        self.assertEqual(["Prone"], lick["eligible_postures"])
        self.assertEqual((45000, 120000), (lick["cooldown_min_ms"], lick["cooldown_max_ms"]))
        self.assertFalse(lick["autonomous_binding_enabled"])

    def test_both_profiles_are_review_only_and_never_hard_spliced(self):
        for batch in (V3, V4):
            asset = json.loads((batch / "asset.json").read_text(encoding="utf-8"))
            review = json.loads((batch / "runtime-review-manifest.json").read_text(encoding="utf-8"))
            for document in (asset, review):
                self.assertEqual("production_candidate_owner_qa_pending", document["asset_stage"])
                self.assertEqual("owner_visual_qa_passed_runtime_behavior_pending", document["runtime_validation"])
                self.assertTrue(document["visual_approved"])
                self.assertFalse(document["runtime_approved"])
                self.assertFalse(document["runtime_use"])
                self.assertFalse(document["production_asset"])
                self.assertFalse(document["autonomous_binding_enabled"])
                self.assertIn("no", document["bridge_policy"].lower())
            for action in review["actions"]:
                self.assertEqual(["DeveloperPreview"], action["allowed_sources"])
                self.assertFalse(action["prototype_use"])
                self.assertFalse(action["expired_pixel_contribution"])

        v3_review = json.loads((V3 / "runtime-review-manifest.json").read_text(encoding="utf-8"))
        v4_review = json.loads((V4 / "runtime-review-manifest.json").read_text(encoding="utf-8"))
        self.assertTrue(any(action["legacy_side_prone"] for action in v3_review["actions"]))
        self.assertTrue(all("front" not in action["from_pose"] or not action["legacy_side_prone"] for action in v3_review["actions"]))
        self.assertTrue(all(action["from_pose"] == "prone.awake.front" for action in v4_review["actions"]))
        self.assertTrue(all(not action["legacy_side_prone"] for action in v4_review["actions"]))

    def test_v2_runtime_manifest_is_byte_unchanged(self):
        current = BATCH_ROOT / "WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2" / "manifest.json"
        self.assertEqual(V2_MANIFEST_SHA256, sha256(current))

    def test_review_publish_includes_both_profiles_and_keeps_powershell_51_compatibility(self):
        script = (ROOT / "tools" / "publish-autonomous-daily-review.ps1").read_text(encoding="utf-8-sig")
        self.assertIn(V3.name, script)
        self.assertIn(V4.name, script)
        self.assertIn("runtime-review-manifest.json", script)
        self.assertNotIn("Path]::GetRelativePath", script)
        self.assertNotIn("Path.GetRelativePath", script)


if __name__ == "__main__":
    unittest.main()
