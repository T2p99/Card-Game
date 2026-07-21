#!/usr/bin/env python3
"""Resumable importer for the licensed original English TCG media manifest."""

from __future__ import annotations

import argparse
import csv
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST_DIR = ROOT / "Assets/Resources/CardGame/Data/Tcg"
ART_DIR = ROOT / "Assets/Resources/CardGame/CardArt"
VIDEO_DIR = ROOT / "Assets/Resources/CardGame/Videos"
USER_AGENT = "Card-Game licensed media importer/1.0"


def request(url: str, method: str = "GET", timeout: int = 30):
    return urllib.request.urlopen(
        urllib.request.Request(url, method=method, headers={"User-Agent": USER_AGENT}),
        timeout=timeout,
    )


def download(url: str, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".part")
    with request(url) as response, temporary.open("wb") as output:
        shutil.copyfileobj(response, output)
    temporary.replace(destination)


def convert_webp(source: Path, destination: Path) -> None:
    try:
        from PIL import Image

        with Image.open(source) as image:
            image.convert("RGBA").save(destination, "PNG")
        return
    except ImportError:
        pass

    commands = []
    if shutil.which("magick"):
        commands.append(["magick", str(source), str(destination)])
    if shutil.which("convert"):
        commands.append(["convert", str(source), str(destination)])
    if shutil.which("ffmpeg"):
        commands.append(["ffmpeg", "-loglevel", "error", "-y", "-i", str(source), str(destination)])

    for command in commands:
        if subprocess.run(command, check=False).returncode == 0:
            return
    raise RuntimeError("WebP conversion needs Pillow, ImageMagick, or ffmpeg. Try: python -m pip install pillow")


def import_image(card: dict[str, str]) -> str:
    media_id = int(card["media_id"])
    destination = ART_DIR / f"{media_id:04d}.png"
    if destination.exists() and destination.stat().st_size > 0:
        return f"scan {media_id:04d} already present"
    with tempfile.TemporaryDirectory() as directory:
        webp = Path(directory) / f"{media_id:04d}.webp"
        download(card["image_url"], webp)
        destination.parent.mkdir(parents=True, exist_ok=True)
        convert_webp(webp, destination)
    return f"scan {media_id:04d} imported"


def import_video(card: dict[str, str]) -> str:
    media_id = int(card["media_id"])
    destination = VIDEO_DIR / f"{media_id:04d}.mp4"
    if destination.exists() and destination.stat().st_size > 0:
        return f"video {media_id:04d} already present"
    try:
        with request(card["video_url"], method="HEAD", timeout=15) as response:
            if response.status != 200:
                return f"video {media_id:04d} unavailable"
        download(card["video_url"], destination)
        return f"video {media_id:04d} imported"
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return f"video {media_id:04d} unavailable"
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--set", action="append", dest="sets", help="Limit to a set such as DM-01; repeatable")
    parser.add_argument("--images", action="store_true", help="Import and convert card scans")
    parser.add_argument("--videos", action="store_true", help="Probe and import available MP4 animations")
    parser.add_argument("--all", action="store_true", help="Import scans and available animations")
    parser.add_argument("--workers", type=int, default=6, help="Concurrent downloads (default: 6)")
    args = parser.parse_args()
    if not (args.images or args.videos or args.all):
        parser.error("choose --images, --videos, or --all")

    cards = []
    for manifest in sorted(MANIFEST_DIR.glob("*.csv")):
        with manifest.open(encoding="utf-8-sig", newline="") as source:
            cards.extend(csv.DictReader(source))
    if args.sets:
        selected = {value.upper() for value in args.sets}
        cards = [card for card in cards if card["card_set"].upper() in selected]
    print(f"Selected {len(cards)} printings from {MANIFEST_DIR.relative_to(ROOT)}")

    jobs = []
    with ThreadPoolExecutor(max_workers=max(1, min(args.workers, 12))) as pool:
        if args.images or args.all:
            jobs.extend(pool.submit(import_image, card) for card in cards)
        if args.videos or args.all:
            jobs.extend(pool.submit(import_video, card) for card in cards)
        failures = 0
        for future in as_completed(jobs):
            try:
                print(future.result())
            except Exception as error:
                failures += 1
                print(f"ERROR: {error}", file=sys.stderr)

    print(f"Finished with {failures} failed operation(s). The importer is resumable; rerun the same command after fixing connectivity.")
    if args.videos or args.all:
        print("MP4 files belong in Git LFS before committing: git lfs track '*.mp4'")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
