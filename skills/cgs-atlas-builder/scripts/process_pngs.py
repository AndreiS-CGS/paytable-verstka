#!/usr/bin/env python3
"""
Step 1 of cgs-atlas-builder: crop source symbol PNGs by alpha and resize to a uniform height.

Usage:
  python3 process_pngs.py <src_folder> [options]

Reads all *.png in <src_folder>, crops each to its alpha bounding box, resizes proportionally to
--height, and writes the result into <src_folder>_<height>/ (a sibling of the source folder, never
overwriting it).

EVERY sprite gets the same height. That uniformity is what makes the sprite font standard work
(glyph.height == faceInfo.pointSize, so spriteHeight = fontSize x P/100); per-use sizing belongs in
a <size=P%> tag at the point of use, never in the atlas. The --small-height* options below exist
only to reproduce an older two-tier atlas and default to OFF.
"""
import argparse
import os
from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("src_folder", nargs="?", default=os.environ.get("ATLAS_SRC"),
                         help="Folder of source PNGs. Falls back to the ATLAS_SRC env var.")
    parser.add_argument("--height", type=int, default=128)
    parser.add_argument("--small-height", type=int, default=100,
                         help="DEPRECATED. Only used when --small-height-names is non-empty.")
    parser.add_argument("--small-height-names", default="",
                         help="DEPRECATED, empty by default. Names listed here are resized to "
                              "--small-height instead of --height, which breaks the uniform-height "
                              "standard. Kept only for rebuilding an older two-tier atlas.")
    parser.add_argument("--alpha-threshold", type=int, default=127)
    args = parser.parse_args()

    if not args.src_folder:
        parser.error("src_folder is required (positionally, or via the ATLAS_SRC env var)")

    folder_src = args.src_folder.rstrip("/")
    folder_dst = f"{folder_src}_{args.height}"
    os.makedirs(folder_dst, exist_ok=True)

    small_names = {n.strip().lower() for n in args.small_height_names.split(",") if n.strip()}

    count = 0
    for fname in sorted(os.listdir(folder_src)):
        if not fname.lower().endswith(".png"):
            continue
        img = Image.open(os.path.join(folder_src, fname)).convert("RGBA")
        _, _, _, a = img.split()
        bbox = a.point(lambda x: 255 if x >= args.alpha_threshold else 0).getbbox()
        if bbox:
            img = img.crop(bbox)
        name = os.path.splitext(fname)[0].lower()
        target_h = args.small_height if name in small_names else args.height
        w, h = img.size
        img = img.resize((round(w * target_h / h), target_h), Image.LANCZOS)
        img.save(os.path.join(folder_dst, fname))
        print(f"  {fname} -> {img.size}")
        count += 1

    print(f"Done: {count} sprites -> {folder_dst}")


if __name__ == "__main__":
    main()
