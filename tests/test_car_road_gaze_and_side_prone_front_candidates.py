import hashlib
import json
import unittest
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCHES = ROOT / "assets" / "action-batches"
CAR_V8 = BATCHES / "WK-INTERACTION-CAR-RIDE-CANDIDATE-v8"
CAR_V9 = BATCHES / "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v9"
SIDE = BATCHES / "WK-AUTONOMOUS-SIDE-PRONE-FRONT-CANDIDATE-v1"
SIDE_V5 = BATCHES / "WK-AUTONOMOUS-SIDE-PRONE-FRONT-PRODUCTION-v5"
V3_SIDE = BATCHES / "WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED" / "frames" / "microloops" / "prone-idle-legacy-side"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class CarRoadGazeAndSideProneFrontCandidateTests(unittest.TestCase):
    def test_car_road_gaze_has_two_native_non_mirrored_wheel_aligned_sequences(self):
        manifest = json.loads((CAR_V9 / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("WK-INTERACTION-CAR-RIDE-CANDIDATE-v8", manifest["parent_asset"])
        self.assertEqual("production_candidate_windows_ci_pending", manifest["status"])
        self.assertEqual("pending_windows_renderer_ci", manifest["runtime_validation"])
        self.assertTrue(manifest["visual_approved"])
        self.assertTrue(manifest["owner_runtime_enable_requested"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["pixel_policy"]["runtime_mirror_used"])
        self.assertEqual({"road-gaze/left", "road-gaze/right"}, set(manifest["sequences"]))

        for direction, entries in manifest["sequences"].items():
            self.assertEqual(18, len(entries), direction)
            self.assertEqual([1, 2, 3, 4, 5, 6] * 3, [entry["wheel_phase"] for entry in entries])
            native_direction = direction.rsplit("/", 1)[-1]
            for index, entry in enumerate(entries, 1):
                path = CAR_V9 / entry["path"]
                self.assertEqual(entry["bytes"], path.stat().st_size)
                self.assertEqual(entry["sha256"], sha256(path))
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual((1024, 1024), image.size)
                    self.assertEqual("RGBA", image.mode)
                    self.assertEqual(901, image.getchannel("A").getbbox()[3])
                    candidate = np.asarray(image)
                phase = (index - 1) % 6 + 1
                with Image.open(CAR_V8 / "sequences" / "directions" / native_direction / f"frame-{phase:03d}.png") as source:
                    source.load()
                    approved = np.asarray(source.convert("RGBA"))
                self.assertTrue(np.array_equal(candidate[600:, :, :], approved[600:, :, :]), f"car/wheels changed: {path}")

    def test_side_prone_front_loop_keeps_side_body_and_stays_runtime_closed_without_bridge(self):
        manifest = json.loads((SIDE / "manifest.json").read_text(encoding="utf-8"))
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["autonomous_binding_enabled"])
        self.assertIn("no hard splice", manifest["bridge_policy"])
        self.assertFalse(manifest["source_policy"]["runtime_mirror_used"])
        action = manifest["actions"][0]
        self.assertEqual(12, action["frame_count"])

        frames = [SIDE / entry["path"] for entry in action["frames"]]
        self.assertEqual(frames[0].read_bytes(), frames[-1].read_bytes())
        for index, (path, entry) in enumerate(zip(frames, action["frames"]), 1):
            self.assertEqual(entry["sha256"], sha256(path))
            with Image.open(path) as image:
                image.load()
                self.assertEqual((1024, 1024), image.size)
                self.assertEqual("RGBA", image.mode)
                candidate = np.asarray(image)
            with Image.open(V3_SIDE / f"frame-{index:03d}.png") as source:
                source.load()
                approved_body = np.asarray(source.convert("RGBA"))
            self.assertTrue(np.array_equal(candidate[:, 560:, :], approved_body[:, 560:, :]), f"side body changed outside head/neck region: {path}")

    def test_candidate_checksum_inventories_match(self):
        for batch in (CAR_V9, SIDE, SIDE_V5):
            for raw in (batch / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
                expected, relative = raw.split(None, 1)
                path = batch / relative.strip()
                self.assertTrue(path.is_file(), path)
                self.assertEqual(expected, sha256(path), path)

    def test_side_prone_front_v5_has_bidirectional_bridges_and_a_calm_loop(self):
        manifest = json.loads((SIDE_V5 / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("wk.candidate.lifecycle.side_prone_front_observe_v5", manifest["behavior_id"])
        self.assertEqual(36, manifest["frame_count"])
        self.assertEqual("production_candidate_owner_visual_review_pending", manifest["status"])
        self.assertEqual("pending_owner_visual_review_and_windows_renderer_ci", manifest["runtime_validation"])
        self.assertFalse(manifest["visual_approved"])
        self.assertTrue(manifest["owner_runtime_enable_requested"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["autonomous_binding_enabled"])
        self.assertIn("no hard splice", manifest["bridge_policy"])
        self.assertFalse(manifest["source_policy"]["runtime_mirror_used"])
        self.assertFalse(manifest["source_policy"]["v3r1_source_modified"])

        phases = manifest["phases"]
        self.assertEqual(
            ["bridge-to-front", "side-prone-front-calm", "bridge-to-legacy"],
            [phase["name"] for phase in phases],
        )
        self.assertEqual([12, 12, 12], [phase["frame_count"] for phase in phases])
        self.assertEqual([False, True, False], [phase["loop"] for phase in phases])

        forward = [SIDE_V5 / entry["path"] for entry in phases[0]["frames"]]
        calm = [SIDE_V5 / entry["path"] for entry in phases[1]["frames"]]
        reverse = [SIDE_V5 / entry["path"] for entry in phases[2]["frames"]]
        self.assertEqual([path.read_bytes() for path in forward[::-1]], [path.read_bytes() for path in reverse])
        self.assertEqual((V3_SIDE / "frame-001.png").read_bytes(), forward[0].read_bytes())
        self.assertEqual(forward[-1].read_bytes(), calm[0].read_bytes())
        self.assertEqual(calm[0].read_bytes(), calm[-1].read_bytes())
        self.assertNotEqual(calm[0].read_bytes(), calm[5].read_bytes())
        self.assertNotEqual(calm[0].read_bytes(), calm[8].read_bytes())

        for index, path in enumerate(forward + calm, 1):
            body_index = (index - 1) % 12 + 1
            with Image.open(path) as image:
                image.load()
                self.assertEqual((1024, 1024), image.size)
                self.assertEqual("RGBA", image.mode)
                candidate = np.asarray(image)
                bounds = image.getchannel("A").getbbox()
            with Image.open(V3_SIDE / f"frame-{body_index:03d}.png") as source:
                source.load()
                frozen = np.asarray(source.convert("RGBA"))
                frozen_bounds = source.getchannel("A").getbbox()
            self.assertTrue(np.array_equal(candidate[:, 560:, :], frozen[:, 560:, :]), f"right-side body changed: {path}")
            self.assertTrue(np.array_equal(candidate[760:, :, :], frozen[760:, :, :]), f"lower body changed: {path}")
            self.assertLessEqual(abs(bounds[0] - frozen_bounds[0]), 12, path)
            self.assertLessEqual(abs(bounds[1] - frozen_bounds[1]), 45, path)

        sources = sorted((SIDE_V5 / "source/transition-heads").glob("*.png"))
        self.assertEqual(6, len(sources))
        for path in sources:
            with Image.open(path) as image:
                image.load()
                self.assertEqual((310, 380), image.size)
                self.assertEqual("RGBA", image.mode)
                rgba = np.asarray(image)
            visible = rgba[..., 3] > 64
            rgb16 = rgba[..., :3].astype(np.int16)
            green_spill = rgb16[..., 1] > np.maximum(rgb16[..., 0], rgb16[..., 2]) + 18
            self.assertEqual(0, int(np.count_nonzero(visible & green_spill)), path)


if __name__ == "__main__":
    unittest.main()
