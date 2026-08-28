#!/usr/bin/env python3
"""Find coloured text runs in a paytable body — candidates for feature-name highlighting.

Stage 1 of the agreed flow: locate every run of *coloured text* (as opposed to
white body copy or multi-hue sprite art) and name its hue. Stage 2 is reading
the run (agent vision) and matching it against the page's known Title strings.

Two things drive the implementation:
  * Body copy is small and heavily anti-aliased, so the pure primaries that the
    Title sampler relies on never survive here — classification is by hue
    family, then snapped to the Title palette.
  * Coloured pixels appear on almost every row (inline sprites), so row-banding
    collapses into one block. Glyphs are isolated as connected components and
    separated from sprite art by height instead.

OS-independent: Pillow + numpy + scipy.

Usage:
  python3 feature_color.py <image> [--y0 N] [--y1 N] [--json] [--crops DIR]
"""
import argparse
import json
import os

import numpy as np
from PIL import Image
from scipy import ndimage

# "Coloured" = clearly not the white body copy. Looser than the Title sampler.
SAT_MIN = 45
VAL_MIN = 70

# Glyph geometry. Body caps run ~20-28 px; symbol sprites are 50 px and up.
GLYPH_MIN_H, GLYPH_MAX_H = 9, 40
GLYPH_MIN_PX = 12
GLYPH_MAX_W = 60           # wider single blobs are art, not a letter

# Grouping glyphs into words/phrases
SAME_LINE_TOL = 10         # px of baseline wobble allowed
WORD_GAP = 34              # px of horizontal gap still counted as one run
RUN_MIN_GLYPHS = 3
RUN_MIN_W = 40

HUES = {
    "yellow":  (255, 255, 0),
    "green":   (0, 255, 0),
    "red":     (255, 0, 0),
    "cyan":    (0, 255, 255),
    "magenta": (255, 0, 255),
    "blue":    (0, 0, 255),
    "orange":  (255, 140, 0),
}
_HUE_UNIT = {k: np.array(v, dtype=float) / np.linalg.norm(v) for k, v in HUES.items()}


def hue_family(mean_rgb):
    v = np.asarray(mean_rgb, dtype=float)
    n = np.linalg.norm(v)
    if n < 1e-6:
        return "yellow"
    v = v / n
    return min(_HUE_UNIT, key=lambda k: float(np.sum((v - _HUE_UNIT[k]) ** 2)))


def coloured_mask(a):
    mx = a.max(axis=2)
    mn = a.min(axis=2)
    return ((mx - mn) >= SAT_MIN) & (mx >= VAL_MIN)


def glyphs(a, y0, y1):
    """Connected components that look like coloured letters."""
    sub = a[y0:y1]
    m = coloured_mask(sub)
    if not m.any():
        return []
    lab, n = ndimage.label(m)
    out = []
    for sl_y, sl_x in ndimage.find_objects(lab):
        h, w = sl_y.stop - sl_y.start, sl_x.stop - sl_x.start
        if not (GLYPH_MIN_H <= h <= GLYPH_MAX_H) or w > GLYPH_MAX_W:
            continue
        cell_mask = m[sl_y, sl_x]
        if int(cell_mask.sum()) < GLYPH_MIN_PX:
            continue
        px = sub[sl_y, sl_x][cell_mask]
        out.append({
            "y0": y0 + sl_y.start, "y1": y0 + sl_y.stop,
            "x0": sl_x.start, "x1": sl_x.stop,
            "mean": px.mean(axis=0),
            "px": int(cell_mask.sum()),
        })
    return out


def group(glyph_list):
    """Merge glyphs sharing a baseline and hue into word/phrase runs."""
    runs = []
    # Sort purely left-to-right. Sorting by y first would split a word whose
    # glyphs differ by a pixel of baseline; line identity is already enforced
    # by SAME_LINE_TOL when attaching, and real lines sit ~50 px apart.
    for g in sorted(glyph_list, key=lambda g: g["x0"]):
        g_hue = hue_family(g["mean"])
        placed = False
        for r in runs:
            if (abs(r["y0"] - g["y0"]) <= SAME_LINE_TOL
                    and r["hue"] == g_hue
                    and 0 <= g["x0"] - r["x1"] <= WORD_GAP):
                r["x1"] = max(r["x1"], g["x1"])
                r["y0"] = min(r["y0"], g["y0"])
                r["y1"] = max(r["y1"], g["y1"])
                r["glyphs"] += 1
                r["px"] += g["px"]
                r["_sum"] += g["mean"] * g["px"]
                placed = True
                break
        if not placed:
            runs.append({"x0": g["x0"], "x1": g["x1"], "y0": g["y0"], "y1": g["y1"],
                         "hue": g_hue, "glyphs": 1, "px": g["px"],
                         "_sum": g["mean"] * g["px"]})
    final = []
    for r in runs:
        if r["glyphs"] < RUN_MIN_GLYPHS or (r["x1"] - r["x0"]) < RUN_MIN_W:
            continue
        mean = (r.pop("_sum") / r["px"]).astype(int)
        r["mean_hex"] = "#%02X%02X%02X" % tuple(mean)
        final.append(r)
    return sorted(final, key=lambda r: (r["y0"], r["x0"]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("--y0", type=int, default=680, help="skip the Title band")
    ap.add_argument("--y1", type=int, default=None)
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--crops", metavar="DIR",
                    help="write a PNG per run, for reading by vision")
    args = ap.parse_args()

    im = Image.open(args.image).convert("RGB")
    a = np.asarray(im, dtype=np.int16)
    y1 = args.y1 or a.shape[0]

    runs = group(glyphs(a, args.y0, y1))

    if args.crops and runs:
        os.makedirs(args.crops, exist_ok=True)
        for i, r in enumerate(runs):
            pad = 5
            im.crop((max(0, r["x0"] - pad), max(0, r["y0"] - pad),
                     min(a.shape[1], r["x1"] + pad),
                     min(a.shape[0], r["y1"] + pad))).save(
                os.path.join(args.crops, "run%02d_%s.png" % (i, r["hue"])))

    if args.json:
        print(json.dumps({"image": os.path.basename(args.image), "runs": runs}, indent=2))
        return

    print("%s — %d coloured text run(s)" % (os.path.basename(args.image), len(runs)))
    for r in runs:
        print("  y=%4d-%4d x=%4d-%4d  %-8s %2d glyphs  mean=%s"
              % (r["y0"], r["y1"], r["x0"], r["x1"], r["hue"], r["glyphs"], r["mean_hex"]))


if __name__ == "__main__":
    main()
