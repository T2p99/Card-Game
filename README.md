# Card Game

Private, non-commercial Duel Masters fan simulator built with Unity 6.5 for Android and iOS.

## Current milestone

The first vertical slice implements the English TCG core rules as a deterministic C# engine. It now includes the exact 40-card lists for Shobu's Fire Deck and Kokujo's Darkness Deck, with database-backed identity and stat fields. Card-specific effects remain explicitly marked as data-only until their rules are implemented and tested.

## Open locally

1. Clone this repository.
2. In Unity Hub, select **Add project from disk** and choose the repository folder.
3. Open with Unity `6000.5.4f1`.
4. Let Unity restore packages and generate missing local cache files.

See [docs/RULES_AND_SCOPE.md](docs/RULES_AND_SCOPE.md) for implemented rules and known gaps.
