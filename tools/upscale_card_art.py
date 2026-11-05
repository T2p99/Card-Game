#!/usr/bin/env python3
"""Faithfully enlarge bundled card PNGs in place, skipping the target size."""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
import sys
import tempfile

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ART_DIR = ROOT / "Assets" / "Resources" / "CardGame" / "CardArt"


def upscale_image(path: Path, size: tuple[int, int]) -> bool:
    if min(size) <= 0:
        raise ValueError("Target dimensions must be positive")
    with Image.open(path) as image:
        if image.size == size:
            image.verify()
            return False
        if image.width >= size[0] or image.height >= size[1]:
            raise ValueError("Refusing to downscale an existing image")
        if image.width * size[1] != image.height * size[0]:
            raise ValueError("Target dimensions must preserve the original aspect ratio")
        if image.mode not in ("RGB", "RGBA"):
            raise ValueError(f"Expected RGB or RGBA card art, got {image.mode}")
        metadata = {key: image.info[key] for key in ("icc_profile", "exif", "dpi") if key in image.info}
        resized = image.resize(size, Image.Resampling.LANCZOS)

    with resized:
        with tempfile.NamedTemporaryFile(prefix=f".{path.stem}-", suffix=".part", dir=path.parent,
                                         delete=False) as temporary:
            temporary_path = Path(temporary.name)
        try:
            resized.save(temporary_path, format="PNG", **metadata)
            with Image.open(temporary_path) as written:
                if written.size != size:
                    raise ValueError("Written PNG does not have the requested dimensions")
                written.verify()
            temporary_path.replace(path)
        finally:
            temporary_path.unlink(missing_ok=True)
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--width", type=int, default=982)
    parser.add_argument("--height", type=int, default=1364)
    parser.add_argument("--workers", type=int, default=4)
    args = parser.parse_args()
    if min(args.width, args.height, args.workers) <= 0:
        parser.error("dimensions and worker count must be positive")
    paths = sorted(ART_DIR.glob("*.png"))
    if not paths:
        parser.error(f"No card PNGs found in {ART_DIR}")
    print(f"Resizing {len(paths)} card images to {args.width} x {args.height} with Lanczos.", flush=True)
    upscaled = present = failures = 0
    with ThreadPoolExecutor(max_workers=min(args.workers, 12)) as pool:
        jobs = {pool.submit(upscale_image, path, (args.width, args.height)): path for path in paths}
        for completed, future in enumerate(as_completed(jobs), 1):
            try:
                if future.result():
                    upscaled += 1
                else:
                    present += 1
            except (OSError, ValueError) as error:
                failures += 1
                print(f"ERROR: {jobs[future].name}: {error}", file=sys.stderr, flush=True)
            if completed % 100 == 0:
                print(f"Processed {completed} / {len(paths)}", flush=True)
    print(f"Summary: {upscaled} upscaled, {present} already at target size, {failures} failed.")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
