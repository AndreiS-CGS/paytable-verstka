#!/usr/bin/env python3
"""
Step 2 of cgs-atlas-builder: pack processed (cropped+resized) PNGs into one atlas texture plus a
JSON of sprite rects, in Unity's texture coordinate convention (Y origin at the atlas's bottom).

Usage:
  python3 pack_atlas.py <processed_src_folder> <out_png> <out_json> [options]

<processed_src_folder> is process_pngs.py's output folder. Names are upper-cased for the sprite
table (matches this project's naming convention). Raises if the atlas overflows --atlas-size —
shrink source sprite heights (process_pngs.py --height) and re-run both steps.
"""
import argparse
import json
import os
from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("processed_src")
    parser.add_argument("out_png")
    parser.add_argument("out_json")
    parser.add_argument("--atlas-size", type=int, default=1024)
    parser.add_argument("--pad", type=int, default=4)
    args = parser.parse_args()

    atlas_w = atlas_h = args.atlas_size
    pad = args.pad

    sprites = []
    for f in sorted(os.listdir(args.processed_src)):
        if not f.lower().endswith(".png"):
            continue
        img = Image.open(os.path.join(args.processed_src, f)).convert("RGBA")
        sprites.append({"name": os.path.splitext(f)[0].upper(), "img": img, "w": img.width, "h": img.height})

    if not sprites:
        raise SystemExit(f"No PNGs found in {args.processed_src}")

    atlas = Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0))
    rects, x, y, row_h = [], 0, 0, 0
    for sp in sprites:
        if x + sp["w"] + pad > atlas_w:
            x = 0
            y += row_h + pad
            row_h = 0
        atlas.paste(sp["img"], (x, y))
        # Unity texture Y is bottom-up — flip here so downstream (TMP glyph rects, sprite slicing)
        # never has to think about it again.
        rects.append({"name": sp["name"], "x": x, "y": atlas_h - y - sp["h"], "w": sp["w"], "h": sp["h"]})
        row_h = max(row_h, sp["h"])
        x += sp["w"] + pad

    used_h = y + row_h
    if used_h > atlas_h:
        raise SystemExit(f"Atlas overflow: used {used_h}px of {atlas_h}px — shrink sprite heights "
                          f"(process_pngs.py --height) and re-run both steps")

    atlas.save(args.out_png)
    with open(args.out_json, "w") as f:
        json.dump(rects, f, indent=2)
    print(f"Packed {len(sprites)} sprites, used {used_h}px of {atlas_h}px -> {args.out_png}")


if __name__ == "__main__":
    main()
