from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image

from upscale_card_art import upscale_image


class UpscaleCardArtTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.path = Path(self.directory.name) / "0001.png"
        with Image.new("RGBA", (8, 12), (255, 0, 0, 128)) as image:
            image.save(self.path)

    def tearDown(self):
        self.directory.cleanup()

    def test_preserves_rgba_and_transparency(self):
        self.assertTrue(upscale_image(self.path, (16, 24)))
        with Image.open(self.path) as image:
            self.assertEqual(image.size, (16, 24))
            self.assertEqual(image.mode, "RGBA")
            self.assertEqual(image.getpixel((8, 12)), (255, 0, 0, 128))

    def test_uses_lanczos_without_extra_sharpening(self):
        with Image.new("RGB", (8, 12)) as image:
            image.putdata([(x * 30, y * 20, (x + y) * 10) for y in range(12) for x in range(8)])
            image.save(self.path)
            with image.resize((16, 24), Image.Resampling.LANCZOS) as expected:
                upscale_image(self.path, (16, 24))
                with Image.open(self.path) as actual:
                    self.assertEqual(actual.tobytes(), expected.tobytes())

    def test_skips_already_upscaled_images_without_rewriting(self):
        upscale_image(self.path, (16, 24))
        original = self.path.read_bytes()
        self.assertFalse(upscale_image(self.path, (16, 24)))
        self.assertEqual(self.path.read_bytes(), original)

    def test_rejects_downscaling_aspect_changes_and_invalid_sizes(self):
        original = self.path.read_bytes()
        for size in ((4, 6), (16, 16), (0, 24)):
            with self.subTest(size=size), self.assertRaises(ValueError):
                upscale_image(self.path, size)
            self.assertEqual(self.path.read_bytes(), original)

    def test_preserves_unity_metadata(self):
        metadata = self.path.with_suffix(".png.meta")
        metadata.write_bytes(b"guid: original-card-reference\n")
        upscale_image(self.path, (16, 24))
        self.assertEqual(metadata.read_bytes(), b"guid: original-card-reference\n")

    def test_preserves_color_profile_and_dpi(self):
        with Image.open(self.path) as image:
            image.load()
            image.save(self.path, icc_profile=b"original-profile", dpi=(96, 96))
        with Image.open(self.path) as image:
            dpi = image.info["dpi"]
        upscale_image(self.path, (16, 24))
        with Image.open(self.path) as image:
            self.assertEqual(image.info["icc_profile"], b"original-profile")
            self.assertEqual(image.info["dpi"], dpi)

    def test_failed_write_leaves_original_and_removes_temporary_file(self):
        original = self.path.read_bytes()
        with patch("PIL.Image.Image.save", side_effect=OSError("disk write failed")):
            with self.assertRaisesRegex(OSError, "disk write failed"):
                upscale_image(self.path, (16, 24))
        self.assertEqual(self.path.read_bytes(), original)
        self.assertEqual(list(self.path.parent.iterdir()), [self.path])

    def test_corrupt_image_is_not_replaced(self):
        self.path.write_bytes(b"not a PNG")
        with self.assertRaises(OSError):
            upscale_image(self.path, (16, 24))
        self.assertEqual(self.path.read_bytes(), b"not a PNG")


if __name__ == "__main__":
    unittest.main()
