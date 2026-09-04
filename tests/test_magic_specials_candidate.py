import hashlib
import json
import struct
import unittest
from pathlib import Path

from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
BATCH = ROOT / "assets" / "action-batches" / "WK-MAGIC-SPECIALS-CANDIDATE-v1"


def png_header(path: Path):
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise AssertionError(f"invalid PNG header: {path}")
    width, height = struct.unpack(">II", data[16:24])
    return width, height, data[25]


class MagicSpecialsCandidateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((BATCH / "manifest.json").read_text(encoding="utf-8"))
        cls.coin = json.loads((BATCH / "coin-manifest.json").read_text(encoding="utf-8"))

    def test_runtime_gate_stays_closed(self):
        self.assertEqual(self.manifest["status"], "runtime-candidate")
        self.assertFalse(self.manifest["runtime_approved"])
        self.assertFalse(self.manifest["runtime_use"])
        self.assertFalse(self.manifest["production_asset"])
        self.assertTrue(self.manifest["prototype_use"])
        self.assertEqual(len(self.manifest["actions"]), 5)
        for action in self.manifest["actions"]:
            self.assertFalse(action["runtime_approved"])
            self.assertFalse(action["runtime_use"])
            self.assertTrue(action["prototype_use"])

    def test_action_manifest_hashes_and_dimensions(self):
        for action in self.manifest["actions"]:
            self.assertEqual(action["frame_count"], sum(x["frame_count"] for x in action["phases"]))
            for phase in action["phases"]:
                self.assertEqual(phase["frame_count"], len(phase["frames"]))
                for frame in phase["frames"]:
                    path = BATCH / frame["path"]
                    data = path.read_bytes()
                    width, height, color_type = png_header(path)
                    self.assertEqual((width, height), (frame["width"], frame["height"]))
                    self.assertEqual(color_type, 6, f"expected RGBA PNG: {frame['path']}")
                    self.assertEqual(len(data), frame["bytes"])
                    self.assertEqual(hashlib.sha256(data).hexdigest(), frame["sha256"])

    def test_all_eight_broom_directions_are_packaged(self):
        directions = self.manifest["broom_directional_flight"]
        self.assertEqual(set(directions), {"left", "up-left", "up", "up-right", "right", "down-right", "down", "down-left"})
        self.assertTrue(all(len(frames) == 8 for frames in directions.values()))

    def test_coin_faces_flips_and_checksums_are_complete(self):
        expected = {}
        for line in (BATCH / "coin-checksums.sha256").read_text(encoding="utf-8").splitlines():
            digest, relative = line.split("  ", 1)
            expected[relative] = digest
        paths = []
        self.assertEqual([x["id"] for x in self.coin["states"]], ["vivid", "flat", "faded", "exhausted"])
        for state in self.coin["states"]:
            paths.extend([state["front"], state["back"]])
        for relative in self.coin["flip"]["front_to_back"]["directories_by_state"].values():
            frames = sorted((BATCH / relative).glob("*.png"))
            self.assertEqual(len(frames), 9)
            paths.extend(path.relative_to(BATCH).as_posix() for path in frames)
        self.assertEqual(len(paths), 44)
        shared = self.coin["canvas"]["shared_visible_bounds"]
        expected_bounds = (
            shared["x"],
            shared["y"],
            shared["x"] + shared["width"],
            shared["y"] + shared["height"],
        )
        for relative in paths:
            path = BATCH / relative
            width, height, color_type = png_header(path)
            self.assertEqual((width, height, color_type), (1024, 1024, 6))
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), expected[relative])
        for state in self.coin["states"]:
            for relative in (state["front"], state["back"]):
                with Image.open(BATCH / relative) as image:
                    self.assertEqual(image.getchannel("A").getbbox(), expected_bounds, f"coin size drift: {relative}")

    def test_every_runtime_png_decodes_with_transparent_edges(self):
        paths = sorted(BATCH.rglob("*.png"))
        self.assertEqual(len(paths), 207)
        allowed_empty = {
            "apparate/disappear/frame-014.png",
            "apparate/invisible/frame-001-relocation-cut.png",
        }
        actual_empty = set()
        for path in paths:
            with Image.open(path) as image:
                image.load()
                self.assertEqual(image.mode, "RGBA", path)
                alpha = image.getchannel("A")
                if alpha.getbbox() is None:
                    actual_empty.add(path.relative_to(BATCH).as_posix())
                corners = [alpha.getpixel((0, 0)), alpha.getpixel((image.width - 1, 0)), alpha.getpixel((0, image.height - 1)), alpha.getpixel((image.width - 1, image.height - 1))]
                self.assertEqual(corners, [0, 0, 0, 0], f"opaque canvas corner: {path}")
        self.assertEqual(actual_empty, allowed_empty, "only the declared Apparate relocation cut may be fully transparent")

    def test_coin_edge_baseline_is_shared_complete_and_has_no_cutout_fringe(self):
        baseline = self.coin["edge_baseline"]
        self.assertEqual(baseline["profile"], "shared_complete_antialiased_ellipse_v1")
        self.assertTrue(baseline["faces_share_exact_alpha"])
        self.assertTrue(baseline["transparent_rgb_zeroed"])

        face_relatives = [side for state in self.coin["states"] for side in (state["front"], state["back"])]
        with Image.open(BATCH / face_relatives[0]) as image:
            shared_face_alpha = image.getchannel("A").tobytes()
        for relative in face_relatives[1:]:
            with Image.open(BATCH / relative) as image:
                self.assertEqual(image.getchannel("A").tobytes(), shared_face_alpha, f"face alpha drift: {relative}")

        for index in range(1, 10):
            alphas = []
            for state in ("vivid", "flat", "faded", "exhausted"):
                relative = f"petrificus_coin/flip/{state}/front-to-back/frame-{index:03d}.png"
                with Image.open(BATCH / relative) as image:
                    alphas.append(image.getchannel("A").tobytes())
            self.assertTrue(all(alpha == alphas[0] for alpha in alphas[1:]), f"flip alpha drift at frame {index}")

        relatives = face_relatives + [
            f"petrificus_coin/flip/{state}/front-to-back/frame-{index:03d}.png"
            for state in ("vivid", "flat", "faded", "exhausted")
            for index in range(1, 10)
        ]
        for relative in relatives:
            with self.subTest(path=relative), Image.open(BATCH / relative) as image:
                rgba = image.convert("RGBA")
                alpha = rgba.getchannel("A")
                eroded = alpha.point(lambda value: 255 if value else 0).filter(ImageFilter.MinFilter(11))
                pale_boundary = 0
                transparent_rgb = 0
                for (red, green, blue, visible), interior in zip(rgba.getdata(), eroded.getdata()):
                    if not visible and (red or green or blue):
                        transparent_rgb += 1
                    if visible and not interior and red >= 225 and green >= 215 and blue >= 158 and red - blue <= 96:
                        pale_boundary += 1
                self.assertEqual(pale_boundary, 0, f"pale cutout fringe remains: {relative}")
                self.assertEqual(transparent_rgb, 0, f"transparent RGB is not clean: {relative}")


if __name__ == "__main__":
    unittest.main()
