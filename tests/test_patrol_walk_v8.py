import hashlib
import json
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-AUTONOMOUS-PATROL-WALK-v8"


class PatrolWalkV8Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_thirteen_byte_exact_rgba_frames(self):
        inventory = self.manifest["frame_inventory"]
        self.assertEqual(13, len(inventory))
        self.assertEqual(13, len(list((BATCH / "frames").glob("*.png"))))
        declared = dict(line.split("  ")[::-1] for line in (BATCH / "SOURCE-FRAME-SHA256SUMS.sha256").read_text().splitlines())
        for item in inventory:
            path = BATCH / item["path"]
            payload = path.read_bytes()
            digest = hashlib.sha256(payload).hexdigest()
            self.assertEqual(item["bytes"], len(payload))
            self.assertEqual(item["sha256"], digest)
            self.assertEqual(item["source_sha256"], digest)
            self.assertEqual(declared[item["path"]], digest)
            with Image.open(path) as image:
                image.load()
                self.assertEqual(("PNG", "RGBA", (1024, 1024)), (image.format, image.mode, image.size))
                pixels = np.asarray(image)
                alpha = pixels[..., 3]
                self.assertTrue(alpha.any())
                self.assertFalse(alpha[0].any() or alpha[-1].any() or alpha[:, 0].any() or alpha[:, -1].any())
                self.assertFalse(pixels[alpha == 0, :3].any())
                self.assertFalse((alpha == 1).any())

    def test_timeline_and_mirror_share_one_canonical_set(self):
        left, right = self.manifest["actions"]
        self.assertEqual(left["phases"], right["phases"])
        self.assertFalse(left["mirror_horizontally"])
        self.assertTrue(right["mirror_horizontally"])
        self.assertEqual(["left", "right"], [left["direction"], right["direction"]])
        expected = [[500, 180, 180, 200], [200] * 8, [200, 240, 300, 700]]
        for phase, durations in zip(left["phases"], expected):
            self.assertEqual(durations, [f["duration_ms"] for f in phase["frames"]])
        self.assertEqual(["intro", "loop", "exit"], [p["name"] for p in left["phases"]])
        self.assertEqual(4100, sum(sum(d) for d in expected))
        self.assertEqual(left["phases"][0]["frames"][0]["path"], left["phases"][2]["frames"][-1]["path"])
        self.assertEqual([512, 900], self.manifest["anchor"])
        self.assertFalse(self.manifest["normalization"]["per_frame_fit"])
        self.assertEqual([0, 0], self.manifest["normalization"]["translation"])

    def test_superseded_provenance_is_preserved_but_runtime_is_closed(self):
        self.assertEqual(self.manifest, json.loads((BATCH / "asset.json").read_text(encoding="utf-8")))
        self.assertTrue(self.manifest["owner_preview_approved"])
        self.assertTrue(self.manifest["visual_approved"])
        self.assertFalse(self.manifest["prototype_use"])
        self.assertTrue(self.manifest["runtime_approved"])
        self.assertFalse(self.manifest["runtime_use"])
        self.assertFalse(self.manifest["production_asset"])
        self.assertFalse(self.manifest["autonomous_binding_enabled"])
        self.assertTrue(self.manifest["deprecated"])
        self.assertEqual("WK-AUTONOMOUS-PATROL-WALK-v10", self.manifest["superseded_by"])
        self.assertEqual(["DeveloperPreview"], self.manifest["allowed_sources"])
        for action in self.manifest["actions"]:
            self.assertFalse(action["runtime_use"])
            self.assertFalse(action["autonomous_binding_enabled"])
        source = ROOT / ".publish-check/art-rebuild-walk-v8-normalized-review"
        if source.exists():
            for item in self.manifest["frame_inventory"]:
                self.assertEqual((source / item["path"]).read_bytes(), (BATCH / item["path"]).read_bytes())

    def test_old_patrol_not_loaded_or_published(self):
        source = (ROOT / "src/Wukong.Desktop/DesktopPetRuntime.cs").read_text(encoding="utf-8")
        binding = source.split("public static class PatrolWalkCandidateBehaviorIds", 1)[1].split("public static class", 1)[0]
        self.assertIn('AssetBatch = "WK-AUTONOMOUS-PATROL-WALK-v10"', binding)
        self.assertIn('PreviousAssetBatch = "WK-AUTONOMOUS-PATROL-WALK-v8"', binding)
        self.assertNotIn("v1-candidate", binding)
        project = (ROOT / "src/Wukong.Desktop/Wukong.Desktop.csproj").read_text(encoding="utf-8")
        self.assertIn('Content Remove="..\\..\\assets\\action-batches\\WK-AUTONOMOUS-PATROL-WALK-v1-candidate\\**\\*"', project)
        self.assertTrue((ROOT / "assets/action-batches/WK-AUTONOMOUS-PATROL-WALK-v1-candidate/manifest.json").exists())
