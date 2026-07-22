# Card Game

Private, non-commercial Duel Masters fan simulator built with Unity 6.5 for Android and iOS.

## Current milestone

The first vertical slice implements the English TCG core rules as a deterministic C# engine. Normal Play Mode matches now use a fresh shuffle seed; automated tests continue to pass explicit seeds so failures are reproducible. The playable slice includes the exact 40-card lists for Shobu's Fire Deck and Kokujo's Darkness Deck, with database-backed identity, stats, and progressively implemented rule fields. Each card is marked data-only, core-keyword supported, or complete so incomplete effects cannot be mistaken for working behavior.

Implemented starter spells currently include Burning Power, Crimson Hammer, Magma Gazer, Tornado Flame, Dark Reversal, Death Smoke, Ghost Touch, and Terror Pit, including their target restrictions and applicable shield triggers.

## Open locally

1. Clone this repository.
2. In Unity Hub, select **Add project from disk** and choose the repository folder.
3. Open with Unity `6000.5.4f1`.
4. Let Unity restore packages and generate missing local cache files.

The duel interface is landscape-first. In the Unity **Game** tab, select a `16:9` aspect ratio to preview the same centered tabletop layout used on mobile. Before each duel, both players can independently filter their generated deck pools by card set and civilization; a multicolored card is eligible only when every civilization printed on it is selected.

See [docs/RULES_AND_SCOPE.md](docs/RULES_AND_SCOPE.md) for implemented rules and known gaps.

## Original TCG card and media import

The complete Phase 1 manifest contains 916 original English TCG printings from DM-01 through DM-12 plus 16 promotional printings. Import DM-01 first while its rules are implemented:

```bash
python tools/import_tcg_media.py --set DM-01 --all
```

Resume or import the entire original TCG media pool with:

```bash
python tools/import_tcg_media.py --all
```

The importer skips existing files, converts WebP scans to PNG, probes for optional MP4 animations, and can safely be rerun. Pillow, ImageMagick, or ffmpeg is required for WebP conversion. MP4 files are configured for Git LFS by `.gitattributes`.
