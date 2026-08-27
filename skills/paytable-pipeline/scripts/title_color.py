#!/usr/bin/env python3
"""Extract Title colour from a paytable screenshot — OS-independent.

Reads images via Pillow (works on macOS / Windows / Linux alike); no `sips`,
no platform-specific shelling out.

Usage:
  python3 title_color.py <image>                 # auto-locate title band(s) + colours
  python3 title_color.py <image> --band Y0 Y1    # probe one explicit band
  python3 title_color.py <image> --json          # machine-readable output
"""
import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

# A title glyph is a *pure* primary: one channel near-max, another near-zero.
# This is what separates it from the gold chrome frame (~#96721E), which sits in
# the same rows on every page and would otherwise dominate the sample.
PURE_MAX = 200
PURE_MIN = 80

MIN_BAND_HEIGHT = 12      # px; thinner vivid runs are body sprites, not titles
MIN_RUN_WIDTH = 10        # px; per-glyph horizontal runs narrower than this are noise
ROW_COVERAGE = 0.04       # fraction of width that must be pure for a row to count

NAMED = {
    (255, 255, 0): "yellow",
    (0, 255, 0): "green",
    (255, 0, 0): "red",
    (0, 255, 255): "cyan",
    (255, 0, 255): "magenta",
    (0, 0, 255): "blue",
}


def load_rgb(path):
    with Image.open(path) as im:
        return np.asarray(im.convert("RGB"), dtype=np.int16)


def pure_mask(a):
    return (a.max(axis=2) >= PURE_MAX) & (a.min(axis=2) <= PURE_MIN)


def snap(rgb):
    """Snap a sampled colour to the nearest pure primary and name it."""
    best, best_d = None, None
    for ref in NAMED:
        d = sum((int(rgb[i]) - ref[i]) ** 2 for i in range(3))
        if best_d is None or d < best_d:
            best, best_d = ref, d
    return best, NAMED[best]


def dominant(px):
    keys, counts = np.unique(px.reshape(-1, 3), axis=0, return_counts=True)
    return tuple(int(v) for v in keys[int(np.argmax(counts))])


def colour_runs(a, y0, y1):
    """Per-glyph-run colours inside a band, left to right."""
    band = a[y0:y1]
    m = pure_mask(band)
    if not m.any():
        return []
    active = m.sum(axis=0) > 0
    runs, x, w = [], 0, band.shape[1]
    while x < w:
        if not active[x]:
            x += 1
            continue
        x0 = x
        while x < w and active[x]:
            x += 1
        if x - x0 < MIN_RUN_WIDTH:
            continue
        seg_mask = m[:, x0:x]
        raw = dominant(band[:, x0:x][seg_mask])
        pure, name = snap(raw)
        runs.append({"x0": x0, "x1": x, "raw": raw, "colour": pure,
                     "name": name, "px": int(seg_mask.sum())})
    return runs


def group_runs(runs):
    """Merge adjacent runs of the same colour into words/phrases."""
    out = []
    for r in runs:
        if out and out[-1]["name"] == r["name"]:
            out[-1]["x1"] = r["x1"]
            out[-1]["px"] += r["px"]
            out[-1]["runs"] += 1
        else:
            out.append({"x0": r["x0"], "x1": r["x1"], "colour": r["colour"],
                        "name": r["name"], "px": r["px"], "runs": 1})
    return out


def find_bands(a):
    m = pure_mask(a)
    h, w = m.shape
    per_row = m.sum(axis=1)
    hot = per_row > max(40, int(w * ROW_COVERAGE))
    bands, y = [], 0
    while y < h:
        if not hot[y]:
            y += 1
            continue
        y0 = y
        while y < h and hot[y]:
            y += 1
        if y - y0 >= MIN_BAND_HEIGHT:
            bands.append((y0, y))
    return bands


# A title line is one centred run of text. Inline symbol sprites inside the body
# are pure primaries too, so position/shape is what separates them:
TITLE_MIN_H, TITLE_MAX_H = 20, 60      # observed 29-35 px
TITLE_CENTRE_TOL = 0.09                # x-midpoint must sit near the page centre
TITLE_MAX_SEGMENTS = 4                 # multi-colour titles top out at 3 runs
TITLE_MIN_SPAN = 0.25                  # must span a real headline width


def is_title_band(a, y0, y1, segments):
    if not segments:
        return False
    h_ok = TITLE_MIN_H <= (y1 - y0) <= TITLE_MAX_H
    if not h_ok or len(segments) > TITLE_MAX_SEGMENTS:
        return False
    w = a.shape[1]
    x0 = min(s["x0"] for s in segments)
    x1 = max(s["x1"] for s in segments)
    span = (x1 - x0) / float(w)
    centred = abs((x0 + x1) / 2.0 - w / 2.0) / float(w) <= TITLE_CENTRE_TOL
    return centred and span >= TITLE_MIN_SPAN


def describe(path, bands, a, titles_only=True):
    result = {"image": os.path.basename(path),
              "size": [int(a.shape[1]), int(a.shape[0])], "titles": []}
    for y0, y1 in bands:
        segs = group_runs(colour_runs(a, y0, y1))
        if not segs:
            continue
        if titles_only and not is_title_band(a, y0, y1, segs):
            continue
        result["titles"].append({
            "y0": y0, "y1": y1,
            "segments": [{"x0": s["x0"], "x1": s["x1"], "name": s["name"],
                          "hex": "#%02X%02X%02X" % s["colour"], "px": s["px"]}
                         for s in segs],
        })
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("--band", nargs=2, type=int, metavar=("Y0", "Y1"))
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--all-bands", action="store_true",
                    help="don't filter to title-shaped bands (shows body sprite rows too)")
    args = ap.parse_args()

    a = load_rgb(args.image)
    explicit = bool(args.band)
    bands = [tuple(args.band)] if explicit else find_bands(a)
    result = describe(args.image, bands, a,
                      titles_only=not (args.all_bands or explicit))

    if args.json:
        json.dump(result, sys.stdout, indent=2)
        print()
        return

    print("%s  (%dx%d)" % (result["image"], result["size"][0], result["size"][1]))
    if not result["titles"]:
        print("  no title-like band found")
        return
    for t in result["titles"]:
        joined = " · ".join("%s %s (x%d-%d)" % (s["hex"], s["name"], s["x0"], s["x1"])
                            for s in t["segments"])
        print("  y=%4d-%4d  %s" % (t["y0"], t["y1"], joined))


if __name__ == "__main__":
    main()
