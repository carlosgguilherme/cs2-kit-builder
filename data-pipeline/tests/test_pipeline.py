import json, os, sys, tempfile, unittest
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from kitbuilder_pipeline.oklab import rgb255_to_oklab, oklab_to_rgb255  # noqa: E402
from kitbuilder_pipeline.palette import extract  # noqa: E402

FIXTURES = os.path.join(HERE, "..", "..", "shared", "test-fixtures")


class OkLabTest(unittest.TestCase):
    def test_published_reference_values(self):
        L, a, b = rgb255_to_oklab([255, 0, 0])
        self.assertAlmostEqual(L, 0.627955, places=4)
        self.assertAlmostEqual(a, 0.224863, places=4)
        self.assertAlmostEqual(b, 0.125846, places=4)
        self.assertAlmostEqual(rgb255_to_oklab([255, 255, 255])[0], 1.0, places=4)

    def test_shared_fixture(self):
        fx = json.load(open(os.path.join(FIXTURES, "oklab.json")))
        for c in fx["cases"]:
            rgb = [int(c["hex"][i:i + 2], 16) for i in (1, 3, 5)]
            L, a, b = rgb255_to_oklab(rgb)
            self.assertLess(max(abs(L - c["l"]), abs(a - c["a"]), abs(b - c["b"])), fx["tolerance"], c)

    def test_round_trip(self):
        for rgb in ([12, 200, 99], [255, 215, 0], [3, 3, 3]):
            self.assertEqual(list(oklab_to_rgb255(rgb255_to_oklab(rgb))), rgb)


class PaletteTest(unittest.TestCase):
    def _image(self, regions):
        """regions: list of (rgba, fraction of the image rows)."""
        img = np.zeros((100, 100, 4), dtype=np.uint8)
        row = 0
        for rgba, frac in regions:
            n = int(round(frac * 100))
            img[row:row + n] = rgba
            row += n
        path = os.path.join(tempfile.mkdtemp(), "t.png")
        Image.fromarray(img, "RGBA").save(path)
        return path

    def test_ignores_transparent_background_and_weights_by_area(self):
        path = self._image([((255, 0, 0, 255), 0.30), ((20, 20, 20, 255), 0.10), ((0, 0, 255, 0), 0.60)])
        res = extract(path, "Test | Plain")
        self.assertEqual(len(res["colors"]), 2)
        top = res["colors"][0]
        self.assertEqual(top["hex"], "#ff0000")
        self.assertAlmostEqual(top["weight"], 0.75, places=2)
        self.assertFalse(res["isMultiColor"])

    def test_multicolor_by_name_and_by_hue_spread(self):
        path = self._image([((255, 0, 0, 255), 0.5), ((0, 0, 255, 255), 0.5)])
        self.assertTrue(extract(path, "Glock-18 | Something")["isMultiColor"])
        mono = self._image([((200, 30, 30, 255), 1.0)])
        self.assertTrue(extract(mono, "★ Karambit | Fade")["isMultiColor"])


if __name__ == "__main__":
    unittest.main()
