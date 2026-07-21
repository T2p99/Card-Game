# Licensed card media

The repository owner has confirmed permission to copy the selected card scans and videos from the Duel Masters Card Database for this project.

## Sources

- Card scans: `https://img.duelmasters.us/{media-id}.webp`
- Card videos: `https://media.duelmasters.us/{media-id}.mp4`
- Database project: `https://github.com/bmenneni/SimpleJavaDmdb`

The import workflow converts database WebP scans to PNG for Unity compatibility. MP4 files are stored with Git LFS. These media permissions are distinct from the software licenses used by this repository.

## Media scopes

- Starter milestone: 41 unique scans used by Shobu's Fire Deck and Kokujo's Darkness Deck.
- Phase 1 manifest: all 916 original English TCG printings from DM-01 through DM-12 and the 16 English-era promotional printings returned by the database's `tcg_only` filter.
- `tools/import_tcg_media.py` performs the resumable scan/video download. Missing video URLs are expected and skipped.
- The manifest being present does not imply that a card's printed rules are implemented. Implementation status is tracked separately.
