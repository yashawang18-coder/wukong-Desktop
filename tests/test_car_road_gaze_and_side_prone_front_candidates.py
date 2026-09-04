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
CAR_V10 = BATCHES / "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v10"
CAR_V11 = BATCHES / "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v11"
CAR_V12 = BATCHES / "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v12"
CAR_V13 = BATCHES / "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v13"
SIDE = BATCHES / "WK-AUTONOMOUS-SIDE-PRONE-FRONT-CANDIDATE-v1"
SIDE_V5 = BATCHES / "WK-AUTONOMOUS-SIDE-PRONE-FRONT-PRODUCTION-v5"
V3_SIDE = BATCHES / "WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED" / "frames" / "microloops" / "prone-idle-legacy-side"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def repository_sha256(path: Path) -> str:
    data = path.read_bytes()
    if path.suffix.lower() in {".json", ".md", ".py", ".txt"}:
        data = data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    return hashlib.sha256(data).hexdigest()


class CarRoadGazeAndSideProneFrontCandidateTests(unittest.TestCase):
    def test_car_road_gaze_v12_rejection_is_fail_closed_and_preserves_evidence(self):
        manifest = json.loads((CAR_V12 / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("WK-INTERACTION-CAR-RIDE-CANDIDATE-v8", manifest["parent_asset"])
        self.assertEqual("WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v11", manifest["supersedes"])
        self.assertEqual("failed_owner_visual_qa_identity_consistency", manifest["status"])
        self.assertEqual("failed_owner_visual_qa", manifest["runtime_validation"])
        self.assertFalse(manifest["visual_approved"])
        self.assertFalse(manifest["owner_runtime_enable_requested"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["prototype_use"])
        self.assertFalse(manifest["production_asset"])
        self.assertIn("vehicle_design_and_color_drift_from_approved_v8", manifest["rejected_reason"])
        self.assertIn("harness_design_drift_from_approved_v8", manifest["rejected_reason"])
        self.assertIn("dog_identity_and_body_proportion_drift_from_approved_v8", manifest["rejected_reason"])
        self.assertFalse(manifest["pixel_policy"]["runtime_mirror_used"])
        self.assertFalse(manifest["pixel_policy"]["local_region_composite_used"])
        self.assertFalse(manifest["pixel_policy"]["head_only_edit_used"])
        self.assertEqual({"road-gaze/left", "road-gaze/right"}, set(manifest["sequences"]))

        for direction, entries in manifest["sequences"].items():
            self.assertEqual(18, len(entries), direction)
            self.assertEqual(2030, sum(entry["duration_ms"] for entry in entries))
            for entry in entries:
                path = CAR_V12 / entry["path"]
                self.assertEqual(entry["bytes"], path.stat().st_size)
                self.assertEqual(entry["sha256"], sha256(path))
                self.assertTrue((CAR_V12 / entry["source_master"]).is_file())
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual((1024, 1024), image.size)
                    self.assertEqual("RGBA", image.mode)
                    self.assertEqual(900, image.getchannel("A").getbbox()[3])
                    self.assertEqual(0, image.getchannel("A").getpixel((0, 0)))
                    pixels = np.asarray(image)
                visible = pixels[:, :, 3] >= 16
                magenta = (
                    (pixels[:, :, 0] >= 120)
                    & (pixels[:, :, 2] >= 120)
                    & (pixels[:, :, 1] <= 160)
                    & (np.abs(pixels[:, :, 0].astype(np.int16) - pixels[:, :, 2]) <= 70)
                    & (np.minimum(pixels[:, :, 0], pixels[:, :, 2]).astype(np.int16) - pixels[:, :, 1] >= 60)
                )
                self.assertFalse(np.any(visible & magenta), f"residual key pixels: {path}")

    def test_v12_complete_scene_bounds_and_baseline_are_stable(self):
        manifest = json.loads((CAR_V12 / "manifest.json").read_text(encoding="utf-8"))
        for direction, entries in manifest["sequences"].items():
            widths = []
            bottoms = []
            for entry in entries:
                with Image.open(CAR_V12 / entry["path"]) as image:
                    bounds = image.getchannel("A").getbbox()
                self.assertIsNotNone(bounds)
                widths.append(bounds[2] - bounds[0])
                bottoms.append(bounds[3])
            self.assertLessEqual(max(widths) - min(widths), 2, (direction, widths))
            self.assertEqual({900}, set(bottoms), (direction, bottoms))

    def test_car_road_gaze_v13_is_whole_scene_and_review_only(self):
        manifest = json.loads((CAR_V13 / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("WK-INTERACTION-CAR-RIDE-CANDIDATE-v8", manifest["parent_asset"])
        self.assertEqual("runtime_candidate_owner_visual_qa_pending", manifest["status"])
        self.assertEqual("pending_owner_windows_renderer_qa", manifest["runtime_validation"])
        self.assertFalse(manifest["visual_approved"])
        self.assertFalse(manifest["owner_runtime_enable_requested"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertTrue(manifest["prototype_use"])
        self.assertFalse(manifest["production_asset"])
        self.assertTrue(manifest["developer_preview"])
        self.assertEqual("whole_scene_reference_conditioned_generation", manifest["generation_workflow"]["method"])
        self.assertFalse(manifest["generation_workflow"]["head_or_neck_compositing"])
        self.assertFalse(manifest["generation_workflow"]["runtime_interpolation"])
        self.assertFalse(manifest["generation_workflow"]["runtime_mirroring"])
        self.assertFalse(manifest["pixel_policy"]["local_region_composite_used"])
        self.assertFalse(manifest["pixel_policy"]["head_only_edit_used"])
        self.assertEqual({"road-gaze/left", "road-gaze/right"}, set(manifest["sequences"]))

        for direction, entries in manifest["sequences"].items():
            self.assertEqual(18, len(entries), direction)
            self.assertEqual(2770, sum(entry["duration_ms"] for entry in entries))
            widths = []
            for entry in entries:
                path = CAR_V13 / entry["path"]
                self.assertEqual(entry["bytes"], path.stat().st_size)
                self.assertEqual(entry["sha256"], sha256(path))
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual((1024, 1024), image.size)
                    self.assertEqual("RGBA", image.mode)
                    bbox = image.getchannel("A").getbbox()
                    self.assertIsNotNone(bbox)
                    self.assertEqual(901, bbox[3])
                    widths.append(bbox[2] - bbox[0])
            self.assertLessEqual(max(widths) - min(widths), 20, (direction, widths))

    def test_rejected_v11_is_superseded_and_closed_to_every_playback_mode(self):
        manifest = json.loads((CAR_V11 / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("superseded_owner_visual_qa_failed", manifest["status"])
        self.assertEqual("failed_owner_visual_qa", manifest["runtime_validation"])
        self.assertEqual("WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v12", manifest["superseded_by"])
        self.assertIn("owner_rejected_head_only_rebuild_and_composite_strategy", manifest["rejected_reason"])
        self.assertFalse(manifest["visual_approved"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["prototype_use"])
        self.assertFalse(manifest["production_asset"])

    def test_rejected_v10_is_superseded_and_closed_to_every_playback_mode(self):
        manifest = json.loads((CAR_V10 / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("superseded_owner_visual_qa_failed", manifest["status"])
        self.assertEqual("failed_owner_visual_qa", manifest["runtime_validation"])
        self.assertEqual("WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v11", manifest["superseded_by"])
        self.assertIn("head_vertical_anchor_jitter", manifest["rejected_reason"])
        self.assertFalse(manifest["visual_approved"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["prototype_use"])
        self.assertFalse(manifest["production_asset"])

    def test_defective_v9_is_superseded_and_closed_to_every_playback_mode(self):
        manifest = json.loads((CAR_V9 / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("superseded_visual_rework_required", manifest["status"])
        self.assertEqual("failed_owner_visual_qa", manifest["runtime_validation"])
        self.assertEqual("WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v10", manifest["superseded_by"])
        self.assertFalse(manifest["visual_approved"])
        self.assertFalse(manifest["runtime_approved"])
        self.assertFalse(manifest["runtime_use"])
        self.assertFalse(manifest["prototype_use"])
        self.assertFalse(manifest["production_asset"])

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
        inventories = (
            (CAR_V9, "SHA256SUMS"),
            (CAR_V10, "SHA256SUMS"),
            (CAR_V11, "SHA256SUMS"),
            (CAR_V12, "SHA256SUMS"),
            (CAR_V13, "SHA256SUMS.txt"),
            (SIDE, "SHA256SUMS"),
            (SIDE_V5, "SHA256SUMS"),
        )
        for batch, inventory in inventories:
            for raw in (batch / inventory).read_text(encoding="utf-8").splitlines():
                expected, relative = raw.split(None, 1)
                path = batch / relative.strip()
                self.assertTrue(path.is_file(), path)
                self.assertEqual(expected, repository_sha256(path), path)

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
