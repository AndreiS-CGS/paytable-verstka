#!/usr/bin/env python3
"""
Step 2 of cgs-atlas-builder: pack processed (cropped+resized) PNGs into one atlas texture plus a
JSON of sprite rects, in Unity's texture coordinate convention (Y origin at the atlas's bottom).

Usage:
  python3 pack_atlas.py <processed_src_folder> <out_png> <out_json> [options]

<processed_src_folder> is process_pngs.py's output folder. Names are upper-cased for the sprite
table (matches this project's naming convention).

Packing is row-based ("shelf"), which is near-optimal here *because* every sprite is the same
height by the font standard — rows come out uniform, so the only waste is the tail of the last
row. A smarter packer (MaxRects and friends) buys back single-digit percentages on uniform input
and is not worth the complexity.

`--pow2` shrinks the finished atlas to the smallest power-of-two that still fits the content, so
you can pass a generous `--atlas-size` as a ceiling instead of guessing the right one up front.
"""
import re

import argparse
import json
import os
import _bootstrap                       # MUST precede every third-party import
_bootstrap.require('PIL')

from PIL import Image



def sprite_name(stem):
    """PNG filename stem -> TMP sprite name.

    THIS RULE IS SHARED WITH paytable-pipeline's sprite_name(), which applies it to GDD tokens.
    Both ends must normalise identically: if they drift, the rules text asks for a name the atlas
    does not contain and TMP quietly substitutes a fallback glyph — no error, just the same wrong
    picture wherever a symbol should be. Change one, change the other.

        ' ' -> '_'      DARK ACE.png      -> DARK_ACE
        '&' -> '_'      DIAMOND&ACE.png   -> DIAMOND_ACE
        '+' -> 'PLUS'   +1 SPIN.png       -> PLUS1_SPIN

    Filenames used to be taken as-is (just uppercased), so whatever an artist happened to type
    became the sprite name. That is how one atlas ended up holding both DIAMOND_SIGNBOARD and
    2_WILD&SIGNBOARD.
    """
    return re.sub(r'[\s&]+', '_', stem.strip()).replace('+', 'PLUS').upper()

def next_pow2(n):
    p = 1
    while p < n:
        p *= 2
    return p


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("processed_src")
    parser.add_argument("out_png")
    parser.add_argument("out_json")
    parser.add_argument("--atlas-size", type=int, default=1024,
                        help="Atlas size, or the MAXIMUM size when --pow2 is used (default 1024)")
    parser.add_argument("--pad", type=int, default=4)
    parser.add_argument("--pow2", action="store_true",
                        help="Shrink the atlas to the smallest power-of-two that fits the content")
    args = parser.parse_args()

    limit = args.atlas_size
    pad = args.pad

    sprites = []
    for f in sorted(os.listdir(args.processed_src)):
        if not f.lower().endswith(".png"):
            continue
        img = Image.open(os.path.join(args.processed_src, f)).convert("RGBA")
        sprites.append({"name": sprite_name(os.path.splitext(f)[0]), "img": img,
                        "w": img.width, "h": img.height})

    if not sprites:
        raise SystemExit(f"No PNGs found in {args.processed_src}")

    def layout(width):
        """Row-pack into `width`; returns (placements, used_w, used_h). Top-down Y —
        the flip to Unity's bottom-up Y needs the FINAL atlas height, which isn't
        known until the size is settled."""
        placed, x, y, row_h, used_w = [], 0, 0, 0, 0
        for sp in sprites:
            if x + sp["w"] + pad > width:
                x = 0
                y += row_h + pad
                row_h = 0
            placed.append((sp, x, y))
            row_h = max(row_h, sp["h"])
            x += sp["w"] + pad
            used_w = max(used_w, x - pad)
        return placed, used_w, y + row_h

    if args.pow2:
        # SEARCH for the smallest square that fits, don't just shrink a max-width
        # layout: --atlas-size doubles as the row width, so packing at the ceiling
        # makes rows wide and flat and the shrink can't recover that. Widest single
        # sprite sets the floor.
        widest = max(s["w"] for s in sprites) + 2 * pad
        atlas_w = atlas_h = 0
        size = next_pow2(max(widest, 64))
        while size <= limit:
            placed, used_w, used_h = layout(size)
            if used_h <= size:
                atlas_w = size
                # Height gets its own power of two — rows are uniform by the font
                # standard, so the content is usually far shorter than it is wide and
                # a square atlas would waste half the texture.
                atlas_h = next_pow2(used_h)
                break
            size *= 2
        if not atlas_w:
            raise SystemExit(
                f"Atlas overflow: content does not fit a {limit}x{limit} atlas.\n"
                f"Raise --atlas-size. Do NOT shrink sprite heights to fit — 128px is part of "
                f"the sprite-font standard and changing it breaks spriteHeight = fontSize x P/100.")
    else:
        atlas_w = atlas_h = limit
        placed, used_w, used_h = layout(limit)
        if used_h > limit:
            raise SystemExit(
                f"Atlas overflow: content needs {used_h}px of height, limit is {limit}px.\n"
                f"Raise --atlas-size (1280 / 1536 / 2048), or pass --pow2 to size it "
                f"automatically. Do NOT shrink sprite heights to fit — 128px is part of the "
                f"sprite-font standard and changing it breaks spriteHeight = fontSize x P/100.")

    atlas = Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0))
    rects = []
    for sp, px, py in placed:
        atlas.paste(sp["img"], (px, py))
        # Unity texture Y is bottom-up — flip here so downstream (TMP glyph rects, sprite
        # slicing) never has to think about it again.
        rects.append({"name": sp["name"], "x": px, "y": atlas_h - py - sp["h"],
                      "w": sp["w"], "h": sp["h"]})

    atlas.save(args.out_png)
    with open(args.out_json, "w") as f:
        json.dump(rects, f, indent=2)

    fill = sum(s["w"] * s["h"] for s in sprites) / float(atlas_w * atlas_h) * 100
    print(f"Packed {len(sprites)} sprites into {atlas_w}x{atlas_h} "
          f"(content {used_w}x{used_h}, {fill:.0f}% fill) -> {args.out_png}")


if __name__ == "__main__":
    main()
