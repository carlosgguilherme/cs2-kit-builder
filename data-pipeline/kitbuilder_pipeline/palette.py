"""Dominant palette extraction: area-weighted k-means in OKLab."""
import math
import numpy as np
from PIL import Image

from .oklab import rgb255_to_oklab, oklab_to_rgb255, hex_of

# Finishes whose colors vary per item (pattern/seed), so a single palette from
# the catalog image is only indicative.
MULTICOLOR_FINISHES = ("fade", "case hardened", "doppler", "marble fade",
                       "crimson web", "slaughter", "acid fade", "amber fade")
ALPHA_MIN = 200          # pixels with lower alpha are background/antialiasing
MERGE_DELTA_E = 0.04     # clusters closer than this are merged
MIN_WEIGHT = 0.03        # clusters below this share are dropped
CHROMA_COLORED = 0.05    # OKLab chroma above which a cluster counts as "colored"
NEUTRAL_CHROMA = 0.04    # pixel chroma below which a pixel is treated as neutral


def opaque_pixels(path, size=128):
    im = Image.open(path).convert("RGBA")
    im.thumbnail((size, size))
    px = np.asarray(im).reshape(-1, 4)
    return px[px[:, 3] >= ALPHA_MIN][:, :3]


def kmeans(points, k, iterations=25, seed=7):
    """Deterministic k-means++ / Lloyd. Returns (centers, labels)."""
    rng = np.random.default_rng(seed)
    n = len(points)
    k = min(k, n)
    centers = [points[rng.integers(n)]]
    for _ in range(1, k):
        d2 = np.min(((points[:, None, :] - np.array(centers)[None]) ** 2).sum(-1), axis=1)
        total = d2.sum()
        if total <= 0:
            break
        centers.append(points[rng.choice(n, p=d2 / total)])
    centers = np.array(centers)
    labels = np.zeros(n, dtype=int)
    for _ in range(iterations):
        dist = ((points[:, None, :] - centers[None]) ** 2).sum(-1)
        new_labels = dist.argmin(1)
        if _ > 0 and np.array_equal(new_labels, labels):
            break
        labels = new_labels
        for i in range(len(centers)):
            members = points[labels == i]
            if len(members):
                centers[i] = members.mean(0)
    return centers, labels


def extract(path, name="", k=5):
    """Palette of up to k colors (2 neutral + k-2 colored) with area weights."""
    rgb = opaque_pixels(path)
    if len(rgb) == 0:
        return None
    lab = rgb255_to_oklab(rgb)
    # Cluster neutral (gray-ish) and colored pixels separately: weapon bodies are
    # mostly black/gray and would otherwise absorb small but visible accents
    # (e.g. the thin red stripes of a Redline).
    chroma = np.hypot(lab[:, 1], lab[:, 2])
    colored_mask = chroma >= NEUTRAL_CHROMA
    clusters = []
    colored_share = colored_mask.mean()
    # Fewer colored clusters when the colored area is small, so an accent is
    # kept as one visible color instead of several fragments below MIN_WEIGHT.
    colored_k = 1 if colored_share < 0.10 else (2 if colored_share < 0.25 else k - 2)
    for mask, kk in ((~colored_mask, 2), (colored_mask, colored_k)):
        pts = lab[mask]
        if len(pts) < max(20, MIN_WEIGHT * len(lab)):
            continue
        centers, labels = kmeans(pts, kk)
        counts = np.bincount(labels, minlength=len(centers))
        clusters += [[c, n / len(lab)] for c, n in zip(centers, counts) if n > 0]
    # Merge near-identical clusters (area-weighted).
    merged = True
    while merged:
        merged = False
        clusters.sort(key=lambda x: -x[1])
        for i in range(len(clusters)):
            for j in range(i + 1, len(clusters)):
                if np.linalg.norm(clusters[i][0] - clusters[j][0]) < MERGE_DELTA_E:
                    wi, wj = clusters[i][1], clusters[j][1]
                    clusters[i] = [(clusters[i][0] * wi + clusters[j][0] * wj) / (wi + wj), wi + wj]
                    del clusters[j]
                    merged = True
                    break
            if merged:
                break
    clusters = [c for c in clusters if c[1] >= MIN_WEIGHT] or clusters[:1]
    total = sum(w for _, w in clusters)
    colors = []
    for c, w in sorted(clusters, key=lambda x: -x[1]):
        colors.append({"hex": hex_of(oklab_to_rgb255(c)), "l": round(float(c[0]), 4),
                       "a": round(float(c[1]), 4), "b": round(float(c[2]), 4),
                       "weight": round(float(w / total), 4)})
    # Means: lightness/chroma weighted by area; hue = circular mean weighted by
    # area x chroma (gray clusters carry no hue information).
    mean_l = sum(c["l"] * c["weight"] for c in colors)
    chromas = [math.hypot(c["a"], c["b"]) for c in colors]
    mean_c = sum(ch * c["weight"] for ch, c in zip(chromas, colors))
    hx = sum(c["a"] * c["weight"] for c in colors)
    hy = sum(c["b"] * c["weight"] for c in colors)
    mean_h = (math.degrees(math.atan2(hy, hx)) + 360) % 360 if math.hypot(hx, hy) > 1e-6 else None
    colored = [(math.degrees(math.atan2(c["b"], c["a"])) % 360, c["weight"])
               for c, ch in zip(colors, chromas) if ch >= CHROMA_COLORED and c["weight"] >= 0.08]
    lname = name.lower()
    is_multi = any(f in lname for f in MULTICOLOR_FINISHES) or _hue_spread(colored) >= 90
    return {"colors": colors, "meanLightness": round(mean_l, 4), "meanChroma": round(mean_c, 4),
            "meanHue": round(mean_h, 1) if mean_h is not None else None, "isMultiColor": bool(is_multi)}


def _hue_spread(hues):
    """Largest angular distance between two colored clusters (degrees)."""
    best = 0.0
    for i in range(len(hues)):
        for j in range(i + 1, len(hues)):
            d = abs(hues[i][0] - hues[j][0]) % 360
            best = max(best, min(d, 360 - d))
    return best
