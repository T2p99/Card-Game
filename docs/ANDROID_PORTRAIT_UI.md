# Portrait duel interface

The runtime creates a portrait uGUI Canvas automatically in the existing scene.
Android is locked to portrait. Desktop and Unity use the same interface, with
mouse press/drag/release standing in for touch. A landscape desktop window shows
a centred portrait table. The layout uses the device safe area.

## Controls

- The opening menu offers **New Game**, **Customize Deck**, and **Tutorial**, without pack
  selection. New Game uses the selected saved deck and generates a fresh computer
  deck from one randomly chosen civilization.
- Tap a hand card: open it at the centre at half the screen's height. Swipe left
  or right across the enlarged card to browse the entire hand; arrows also work.
  X closes the viewer.
- Hold a hand card for 0.35 seconds: highlight it, then slide across the hand to
  change the highlighted card. Release to open that card. Hold at the edges to
  scroll a crowded hand.
- Drag a ready battle creature towards an enemy creature, shield, or avatar:
  draw a red arrow and highlight legal targets. Release over a legal target to
  attack. Returning to the source, releasing elsewhere, losing app focus, or
  cancelling the pointer makes no attack. Horizontal drags along the battle row
  scroll it; an upward drag starts aiming an attack.
- An attack on a player can be blocked normally. The attacker normally chooses
  which shield breaks. A drop on a shield supplies that first choice at break
  resolution; multiple breakers prompt for the remaining shields. Effects that
  give the defender the choice override the preference and make the shield zone
  one attack target. Changed/removed shields invalidate the saved preference.
  Face-down shields never show names or art in a chooser.
- Summon/cast uses a valid minimum-cost automatic mana payment. Use **Choose
  mana manually** to change the selected payment or pay a higher permitted cost.
  Charge mana is a separate action and respects the turn's charge step.
- Tap the graveyard to inspect its face-up cards. Decks show only
  counts. Evolution tops display their material count and have a read-only
  **View cards under this evolution** action.
- Optional abilities, evolution bases, spell targets, Shield Triggers, blocker
  choices, ordering, race/civilization/player choices, and number choices use the
  existing transactional decision engine through portrait overlays.
- Mana cards are face up, upside down, and arranged in a horizontally scrollable
  row. Tap a card to inspect it; the enlarged preview is upright. Mana headers
  show ready/total counts. A tapped mana card turns a further quarter turn.
- Graveyards show up to four scattered face-up cards, a total count, and the full
  **Graveyard** label. An empty graveyard shows an empty footprint, not a card back.
- Power above a battle creature is its current power. When attack power differs,
  an additional ATK value appears below it. Tapped battle cards turn sideways;
  the power labels remain upright. The row reserves room for the rotated cards.
- Shields are rectangular, with a cyan rim drawn as scalable geometry.
- Opponent attacks resolve automatically when the engine finds no legal blocker.
  If a legal blocker exists, the player still chooses; mandatory blocks remain enforced.
- Attack clips use a full-screen black overlay with a centered 16:9 video and a
  bottom Skip button. Landscape clips retain black space above and below on a
  portrait screen instead of stretching or cropping.

## Interactive tutorial

**Tutorial** starts a reproducible Fire-versus-Water battle, separate from the
saved deck library. Both decks have 40 cards and obey the four-copy limit.
Opening hands, shields, draws, and the training opponent's moves are fixed.

The coach sits above the table, remains available during card and ability
overlays, and highlights the current card, zone or control. Introductory hand
and deck steps require actually inspecting them. Explanations use **Next**;
action lessons advance only after the real rules command succeeds. Other
game-changing actions are disabled during these lessons. Card inspection,
**Exit**, and the menu's **Restart Tutorial** remain available.

The sequence covers mana and matching civilization payment, ending turns,
automatic draws, untapping, and summoning Immortal Baron, Vorg for two mana.
Inspecting the newly summoned Vorg demonstrates that summoning sickness
prevents attacking. The player then breaks a shield; the opponent later breaks
one of the player's shields, showing that each broken card enters its owner's
hand. Vorg battles a tapped Aqua Vehicle to demonstrate power and the graveyard.
Crimson Hammer then demonstrates paying for a spell, destroying a legal target,
and placing a resolved spell in the caster's graveyard.

Finally, Rikabu, the Dismantler demonstrates Speed Attacker by attacking on its
summon turn. After that attack all normal controls unlock. The training opponent
gives the player room to practice freely over additional turns. Hints adapt when
no shields remain: breaking the fifth shield is not victory, and a subsequent
successful attack on the opponent is required. Completion offers **Start New
Game**, **Replay Tutorial**, and **Main Menu**. A loss can also be replayed.

Tutorials do not change saved decks or the selected duel deck. They use the same
local battle logging and latest-three-duel retention as normal matches.
Attack videos are skipped in tutorial battles to keep the lessons visible;
the normal-duel animation preference is preserved.

## Deck customization

The five starter decks begin as single-civilization decks with 24 cards costing
five mana or less and 16 costing more. The closest legal split is used if a future
catalogue cannot provide that distribution. Both creatures and spells are used.
The starters are saved once and remain editable; changes are not regenerated on
the next session. The mana split is a starter preference, not a restriction on edits.

Up to five additional decks can be saved. Any deck may combine civilizations.
Use **Use for duel** to persist the selected deck. Additional decks can be renamed
or deleted; deleting the selected additional deck selects the Fire starter.

The editor shows all cards in the deck first, with duplicate counts at their
top-right corners, followed by catalogue cards not currently in the deck.
Catalogue pages contain up to 24 cards, keeping image loading bounded. Sorting
supports A-Z, Z-A, and ascending mana cost. **Filters** opens a full-page view with
category headings on the left and scrollable multi-select values on the right.
Mana cost lists every exact cost from 1 to the catalogue maximum, one row per cost.
Card type offers Creature (including evolution creatures) and Spell. Civilization
offers all five civilizations and Multi-civilization; selecting a civilization also
includes multicolored cards containing it. Shields offers Normal (including spells),
Double Breaker, and Triple Breaker. Special abilities includes printed keywords such
as Speed Attacker, Blocker, Turbo Rush, Shield Trigger, and Water Stealth, including
conditional self-granted keywords.

Any selected value within a category may match, including special abilities;
all active categories must match. An empty category imposes no restriction.
**Apply Filters** commits the selections; **Back** discards unapplied changes.
**Clear All** clears the filter draft, without changing the deck or applied filters
until Apply. Each category retains its scroll position. Filters do not hide cards
already in the deck.

Tap a card to enlarge it to the available width or height while preserving its
aspect ratio; **+ / -** below the preview changes its copy count. **X** in the header
closes the viewer. Use the left/right arrow icons over the image edges or swipe
horizontally across the enlarged card to browse; swipe left for the next card and
right for the previous card. Browsing
wraps at the ends. Deck cards follow the deck grid's order, while catalogue cards
follow the active filters and sort across all pages. The opening browse order is
retained while adding/removing copies, so editing does not skip the next card.
Draft totals other than 40 are red, and Save stays disabled until exactly 40 cards
are present. The four-copy limit counts all printings of the same card name.
Unsaved changes require confirmation before being discarded. The save bar stays
fixed while the card grid scrolls, and grid rebuilds retain its scroll position.

Decks and the selected duel deck are stored in `decks-v1.json` under
`Application.persistentDataPath`. Save errors retain the draft and previous saved
library. Invalid saved data is reported instead of silently resetting the decks.

## Battle investigation logs

Each duel has a local JSON transcript in `Application.persistentDataPath/battle-logs`.
The latest three duels are retained, including the current or interrupted duel.
Each resolved command atomically updates its transcript with the turn, RNG state,
board snapshot, failed-command details, and per-instance card movements. Combat
records distinguish the original target from an intercepting blocker and include
actual battle powers, effective keywords, destruction outcomes, and ability
decisions. Uncommitted choice replays do not duplicate committed events.

Unity's Console prints the current file path. Storage errors are reported in the
Console and duel status; the game remains playable, and a later successful write
preserves buffered events. Records survive controller restarts but cannot recover
attacks made before logging was enabled. The transcripts include hidden hands,
deck order, and shield identities for diagnosis and are not uploaded automatically.

## Input settings

Both supported input backends have a UI path:

- **Input System Package (New)**: the runtime creates an
  `InputSystemUIInputModule`. This is the preferred setting for future builds.
- **Input Manager (Old)**: the runtime creates a `StandaloneInputModule`, allowing
  the previous Android test configuration to keep working.

There is no need to choose **Both** for this interface. Player settings are local
to the Unity project; this change does not overwrite an existing settings asset.
No scene objects, Canvas wiring, or input action asset must be created manually.

## Verification in Unity and Android

1. Pull `agent/tcg-rules-checkpoint`. Let Unity finish compiling.
2. Open the scene you already run and choose a portrait aspect in the Game view:
   9:16, then a taller phone aspect such as 9:20. Press Play.
3. Run Test Runner **EditMode**, including `SavedDeckLibraryTests`,
   `TcgCatalogCoverageTests`, `StarterDeckCatalogTests`, `CoreRulesTests`, and
   `TutorialBattleTests`.
4. Run Test Runner **PlayMode**, fixture `PortraitGestureTests`. These tests
   exercise Canvas creation, hidden shield labels, valid attack drops, invalid
   drops, returning to the source, long-press hand browsing, pointer cancel,
   renderer requirements for custom graphics, and deck editor scroll preservation
   with the fixed Save bar. Deck cases cover menu navigation, enlarged preview layout
   and counts, arrows and swipes across pages, full-page multi-select filtering,
   Apply/Back/Clear behavior, exact-size saves, persistence across controller restarts,
   selected-deck duels, discard confirmation, and retaining data on load/write failures.
   Other cases cover tapped-card rotation,
   inverted mana, face-up graveyard piles, centered video, automatic resolution
   without legal blockers, and retaining the prompt when blocking is possible.
   Tutorial cases cover fixed replay, protected saved decks, inspection and action
   guards, quitting during an opponent turn, unavailable saved decks, the complete
   touch-driven lesson sequence at logical 720x1280 and 720x1600 table sizes,
   free charging/summoning and practice turns, and a real final winning attack.
5. Build And Run the Android Development Build using the previously saved scene.
   Check the status bar/notch and bottom gesture area do not overlap controls.
6. On the phone, test short and large hands, crowded battle zones, preview swipes,
   manual mana, evolution material inspection, and ending the turn. Test a normal
   attack, Double Breaker, blocker interception, Shield Trigger, tap ability,
   and any effect that gives the defender shield selection. In Customize Deck,
   check the enlarged preview and count controls at 9:16 and 9:20, then combine
   exact mana costs, civilizations, card types, shields, and special abilities.
   Run Tutorial at both aspects: inspect the highlighted hand card and deck,
   charge and summon, inspect summoning sickness, drag each guided attack, then
   finish freely after the Speed Attacker lesson. Check that the coach never
   covers the viewer controls or attack targets, and try Exit and Restart.

## Android packaging failures

Card art uses an Android-specific ASTC 4x4 import override at its original
dimensions. UI-card mipmaps are disabled: mipmapped non-power-of-two images
otherwise fall back to uncompressed RGBA32 even with an ASTC override.
Let Unity finish reimporting textures after import-policy changes.
Keep the Android build profile's **Texture Compression** set to **ASTC** rather
than Generic/uncompressed, and do not enable **Force Uncompressed** in Asset Import
Overrides. The project's default Android texture format is ASTC.

A `packageDebug` failure saying `Zip32 cannot handle entry` means a packed asset
exceeds the APK archive's size limit. Check the complete error in `Logs\Editor.log`;
Kotlin/Jetifier deprecation warnings and SDK metadata warnings alone do not
identify the failing task's cause. Rebuild after the card-art imports finish,
without editing generated Gradle files or changing SDK permissions to address
an unrelated warning.

## Validation limits

The pure-C# core suite must use separate Core and test assemblies to match Unity's
access boundaries. Compiling both into one assembly does not check access to
internal engine helpers. Syntax checks, layout calculations, and compilation
against API stubs do not replace a real Unity compile or Android device test.
The deck and portrait tests run in the real Unity editor. Visual quality, mobile
keyboard behavior, and touch input still require a real Android/iOS build and
device check; editor tests are not a substitute for those checks.

## Visual quality

The interface still uses Unity's legacy runtime font. A production typography
pass should use TextMeshPro SDF fonts with deliberately sized headings, controls,
and body text. The new decorative masks are 256 pixels with correct circle
rendering; shield rims use geometry. Neither change increases card-art detail.
Sample bundled card scans are 491 by 682 pixels, which limits enlarged previews.
Higher-resolution source images and a coherent icon/panel art set are needed for
finished mobile-game artwork. Judge the device build at its native resolution;
a reduced Editor Game view or downscaled screenshot loses detail.

## Shield rule sources

- Takara Tomy, basic rules Q&A: the attacking player chooses the card to break:
  <https://dm.takaratomy.co.jp/rule/qa_old/37317/>.
- Original English rulebook mirror, “Breaking Your Opponent's Shields”:
  <https://acashic.orgfree.com/rules/>.

The interface follows the existing English TCG card set and engine. It does not
adopt unrelated modern Japanese mechanics.
