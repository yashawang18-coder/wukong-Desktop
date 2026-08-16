import hashlib
import json
import struct
import unittest
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets" / "action-batches" / "WK-INTERACTION-CAR-RIDE-CANDIDATE-v8"
EXPECTED_ZIP_SHA = "bf92f38e3cc976236584d8581cbb8f0f1965257c31837c0d1fd69c7670e9f7e1"


def png_header(path: Path):
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise AssertionError(f"invalid PNG header: {path}")
    width, height = struct.unpack(">II", data[16:24])
    return width, height, data[25]


class CarRideCandidateV8Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))
        cls.validation = json.loads((BATCH / "IMPORT-VALIDATION-REPORT.json").read_text(encoding="utf-8"))

    def test_runtime_gate_is_approved_for_manual_owner_car_ride(self):
        self.assertEqual(self.manifest["asset_id"], "WK-INTERACTION-CAR-RIDE-CANDIDATE-v8")
        self.assertEqual(self.manifest["behavior_id"], "wk.interaction.car_ride")
        self.assertEqual(self.manifest["source_zip_sha256"], EXPECTED_ZIP_SHA)
        self.assertTrue(self.manifest["visual_approved"])
        self.assertEqual(self.manifest["runtime_validation"], "passed_windows_renderer_qa")
        self.assertTrue(self.manifest["runtime_approved"])
        self.assertTrue(self.manifest["runtime_use"])
        self.assertFalse(self.manifest["prototype_use"])
        self.assertTrue(self.manifest["production_asset"])

    def test_runtime_png_count_and_sequence_shape(self):
        all_sequences = self.manifest["all_sequences"]
        self.assertEqual(len([x for x in all_sequences if x.startswith("directions/")]), 8)
        self.assertEqual(len([x for x in all_sequences if x.startswith("start/")]), 8)
        self.assertEqual(len([x for x in all_sequences if x.startswith("brake/")]), 8)
        self.assertEqual(len([x for x in all_sequences if x.startswith("turn/")]), 16)
        self.assertEqual(len([x for x in all_sequences if x.startswith("expressions/")]), 5)
        frame_refs = [frame for frames in all_sequences.values() for frame in frames]
        self.assertEqual(len(frame_refs), 222)
        self.assertEqual(len(set(frame_refs)), 222)
        self.assertEqual(self.validation["runtime_png_count"], 222)
        self.assertEqual(self.validation["errors"], [])

    def test_manifest_frames_decode_and_match_hashes(self):
        paths = []
        for phase in self.manifest["phases"]:
            self.assertEqual(phase["frame_count"], len(phase["frames"]))
            for frame in phase["frames"]:
                paths.append(frame["path"])
                path = BATCH / frame["path"]
                data = path.read_bytes()
                width, height, color_type = png_header(path)
                self.assertEqual((width, height, color_type), (1024, 1024, 6), frame["path"])
                self.assertEqual(len(data), frame["bytes"], frame["path"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), frame["sha256"], frame["path"])
                with Image.open(path) as image:
                    image.load()
                    self.assertEqual(image.mode, "RGBA", frame["path"])
                    self.assertIsNotNone(image.getchannel("A").getbbox(), frame["path"])
        self.assertEqual(len(paths), 102)

    def test_all_runtime_pngs_are_rgba_with_declared_baseline(self):
        frame_refs = [frame for frames in self.manifest["all_sequences"].values() for frame in frames]
        for relative in frame_refs:
            path = BATCH / relative
            width, height, color_type = png_header(path)
            self.assertEqual((width, height, color_type), (1024, 1024, 6), relative)
            with Image.open(path) as image:
                image.load()
                self.assertEqual(image.mode, "RGBA", relative)
                alpha = image.getchannel("A")
                bbox = alpha.getbbox()
                self.assertIsNotNone(bbox, relative)
                self.assertEqual(bbox[3], 901, f"wheel baseline must be y=900: {relative}")
                corners = [
                    alpha.getpixel((0, 0)),
                    alpha.getpixel((image.width - 1, 0)),
                    alpha.getpixel((0, image.height - 1)),
                    alpha.getpixel((image.width - 1, image.height - 1)),
                ]
                self.assertEqual(corners, [0, 0, 0, 0], f"opaque canvas corner: {relative}")

    def test_freeze_manifest_covers_source_package(self):
        freeze = BATCH / "SOURCE-FREEZE-SHA256SUMS.txt"
        self.assertTrue(freeze.exists(), "source freeze SHA manifest missing")
        lines = [line for line in freeze.read_text(encoding="utf-8").splitlines() if line.strip()]
        self.assertGreaterEqual(len(lines), 298)
        self.assertFalse(any(line.endswith(".zip") for line in lines), "source zip must not be copied into repository")


if __name__ == "__main__":
    unittest.main()