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

The duel interface is portrait-first. In the Unity **Game** tab, select `9:16` or a taller phone aspect such as `9:20`. Android touch and desktop mouse use the same interface: tap to inspect, hold and slide to browse the hand, and drag a battle creature to attack with a targeting arrow. Summon/cast automatically selects suitable mana, with a manual override. See [Android portrait controls and verification](docs/ANDROID_PORTRAIT_UI.md).

The opening menu offers **New Game**, **Customize Deck**, and **Tutorial**. New Game uses your selected saved deck; the computer randomly chooses a civilization and generates a fresh single-civilization deck. Only the order of your saved cards is shuffled, not the deck list.

Tutorial is a fixed, interactive Fire-versus-Water battle using real rules and legal 40-card decks. A coach highlights the hand, deck and shields, then guides mana charging, costs, drawing, untapping, normal-creature summoning sickness, shield cards entering their owner's hand, power-based creature battles, the graveyard, a spell, and Speed Attacker. The key lessons allow only the intended action while keeping inspection and exit available. After Rikabu attacks on its summon turn, normal controls unlock for as many practice turns as needed. Winning requires breaking all five shields and a subsequent direct attack. Restart or replay restores the same setup; tutorial play never edits your saved decks or selected duel deck.

Customize Deck starts with five editable civilization decks, each containing 40 cards and targeting 24 cards costing at most five mana plus 16 higher-cost cards. Starter lists are saved once rather than regenerated each session. You can also save up to five additional decks, mix civilizations, rename or delete additional decks, and select any saved deck for duels.

The editor shows deck cards with duplicate-count badges above a paged catalogue. Tap a card to view it at the largest size that fits, with **+ / -** below it. Browse with the arrows over the image edges or horizontal swipes, including across catalogue pages. Catalogue sorting supports A-Z, Z-A, and mana cost.

**Filters** opens a full-page view with categories on the left and multi-select values on the right: exact mana costs from 1 to the catalogue maximum, Creature/Spell card type, civilizations plus Multi-civilization, Normal/Double/Triple Breaker shields, and special abilities including civilization-specific stealth. Creature includes evolution creatures; Normal shields includes spells. Selected values match any within a category, while all active categories must match. Apply commits filter selections, Back cancels them, and Clear All clears the filter draft. Filters never hide cards already in the deck.

Drafts may exceed 40 cards, but saving requires exactly 40 and at most four copies per card name across all printings. Deck edits and the duel selection are stored locally in `decks-v1.json` under Unity's persistent data directory.

### Battle investigation logs

The latest three duels, including the current duel, are recorded as JSON files in `battle-logs` under Unity's persistent data directory. On Windows with the current project settings, this is `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Card-Game\battle-logs`. Unity's Console prints the current log path when recording starts.

Logs are updated atomically after each resolved command. They include the original attack target, actual blocker and battle participant, combat powers and keywords, human and computer ability decisions, individual card IDs and zone changes, RNG state, and board snapshots. Interrupted duels remain available across restarts. Failed commands and logging errors are recorded or reported without silently resetting saved logs or changing gameplay.

Logging applies to new duels and cannot reconstruct earlier unlogged attacks. These files contain both players' hidden card identities for investigation; they stay local and are not uploaded automatically.

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

### Card image resolution

Bundled card scans are faithfully upscaled from 491 x 682 to 982 x 1364 using Lanczos, without AI reconstruction or extra sharpening. This smooths enlarged images but cannot restore detail absent from the originals. Unity's card-art import policy preserves their dimensions rather than rescaling them to powers of two.

Android builds target ASTC, and card-art imports use high-quality ASTC 4x4 compression at those same dimensions. Mipmaps are disabled for these 2D UI images so their non-power-of-two sizes can actually use compression instead of falling back to RGBA32. The import policy reapplies automatically to existing images, avoiding an uncompressed full catalogue that exceeds APK packaging limits.

After importing additional scans, back up the originals outside `Assets` and run the following command (requires Pillow). Images already at the target size are skipped; filenames and Unity references are preserved:

```powershell
python .\tools\upscale_card_art.py
```
