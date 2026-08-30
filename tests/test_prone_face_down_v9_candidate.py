import hashlib
import json
import unittest
from pathlib import Path
import zipfile

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets/action-batches/WK-AUTONOMOUS-PRONE-FACE-DOWN-PRODUCTION-v9"
ANCHOR = ROOT / "assets/action-mocks/WK-COMMAND-PRODUCTION-CANDIDATES-v4/frames/down-v2/frame-012.png"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ProneFaceDownV9CandidateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))

    def test_candidate_gates_remain_closed(self):
        self.assertEqual("production_candidate_owner_qa_pending", self.manifest["status"])
        self.assertTrue(self.manifest["owner_visual_review_requested"])
        self.assertFalse(self.manifest["visual_approved"])
        self.assertFalse(self.manifest["owner_runtime_enable_requested"])
        self.assertFalse(self.manifest["runtime_approved"])
        self.assertFalse(self.manifest["runtime_use"])
        self.assertFalse(self.manifest["production_asset"])
        self.assertFalse(self.manifest["autonomous_binding_enabled"])
        self.assertFalse(self.manifest["runtime_mapping_modified"])
        self.assertFalse(self.manifest["main_modified"])

    def test_all_thirty_six_png_frames_decode(self):
        self.assertEqual(36, self.manifest["frame_count"])
        for phase in self.manifest["phases"]:
            self.assertEqual(12, phase["frame_count"])
            for entry in phase["frames"]:
                path = BATCH / entry["path"]
                self.assertEqual(entry["sha256"], sha256(path))
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual((1024, 1024), image.size)
                    self.assertEqual("RGBA", image.mode)

    def test_anchor_endpoint_calm_and_reverse_contracts(self):
        settle = [BATCH / f"frames/settle-to-face-down/frame-{i:03d}.png" for i in range(1, 13)]
        calm = [BATCH / f"frames/face-down-calm/frame-{i:03d}.png" for i in range(1, 13)]
        rise = [BATCH / f"frames/rise-to-down-anchor/frame-{i:03d}.png" for i in range(1, 13)]
        self.assertEqual(ANCHOR.read_bytes(), settle[0].read_bytes())
        self.assertEqual(ANCHOR.read_bytes(), rise[-1].read_bytes())
        self.assertEqual(settle[10].read_bytes(), settle[11].read_bytes())
        self.assertEqual([path.read_bytes() for path in settle[::-1]], [path.read_bytes() for path in rise])
        endpoint = Image.open(settle[10]).convert("RGBA")
        endpoint_face = endpoint.crop((112, 400, 550, 912)).tobytes()
        endpoint_alpha = endpoint.getchannel("A").tobytes()
        for path in calm:
            frame = Image.open(path).convert("RGBA")
            self.assertEqual(endpoint_face, frame.crop((112, 400, 550, 912)).tobytes())
            self.assertEqual(endpoint_alpha, frame.getchannel("A").tobytes())

    def test_gif_frame_count_and_duration(self):
        for name in ("transparent", "light", "dark"):
            with Image.open(BATCH / f"animations/full-lifecycle-{name}.gif") as image:
                durations = []
                self.assertEqual(36, image.n_frames)
                for index in range(image.n_frames):
                    image.seek(index)
                    durations.append(image.info["duration"])
            self.assertEqual(17240, sum(durations))

    def test_metric_reports_pass_without_granting_visual_approval(self):
        ratio = json.loads((BATCH / "reports/eye-nose-ratio.json").read_text(encoding="utf-8"))
        trajectory = json.loads((BATCH / "reports/head-descent-trajectory.json").read_text(encoding="utf-8"))
        alpha = json.loads((BATCH / "reports/alpha-transparency.json").read_text(encoding="utf-8"))
        color = json.loads((BATCH / "reports/color-calibration.json").read_text(encoding="utf-8"))
        self.assertTrue(ratio["pass"])
        self.assertLessEqual(ratio["max_adjacent_ratio_change_percent"], 5.0)
        self.assertTrue(trajectory["nose_y_monotonic_non_decreasing"])
        self.assertLessEqual(trajectory["max_adjacent_eye_mid_x_shift_px"], 3.0)
        self.assertTrue(alpha["calm_alpha_byte_stable"])
        self.assertTrue(alpha["calm_face_head_front_paw_region_byte_stable"])
        self.assertTrue(color["generated_frame_blue_spill_pass"])
        self.assertFalse(self.manifest["visual_approved"])

    def test_checksum_inventory_and_zip(self):
        lines = (BATCH / "SHA256SUMS").read_text(encoding="utf-8").splitlines()
        self.assertFalse(any("__pycache__" in line or line.endswith(".pyc") for line in lines))
        for line in lines:
            expected, relative = line.split(None, 1)
            path = BATCH / relative.strip()
            self.assertTrue(path.is_file(), path)
            self.assertEqual(expected, sha256(path), path)
        expected_zip, name = (BATCH / "ZIP-SHA256.txt").read_text(encoding="utf-8").split()
        self.assertEqual(expected_zip, sha256(BATCH / name))
        with zipfile.ZipFile(BATCH / name) as archive:
            self.assertFalse(any("__pycache__" in path or path.endswith(".pyc") for path in archive.namelist()))


if __name__ == "__main__":
    unittest.main()
