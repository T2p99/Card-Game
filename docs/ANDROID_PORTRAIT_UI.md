# Portrait duel interface

The runtime creates a portrait uGUI Canvas automatically in the existing scene.
Android is locked to portrait. Desktop and Unity use the same interface, with
mouse press/drag/release standing in for touch. A landscape desktop window shows
a centred portrait table. The layout uses the device safe area.

## Controls

- Deck setup keeps its scroll position when sets or civilizations change. The
  **Start duel** bar stays fixed at the bottom while both decks are configured.
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
3. Run Test Runner **EditMode / Run All** (3,076 core tests at this checkpoint).
4. Run Test Runner **PlayMode**, fixture `PortraitGestureTests`. These fourteen tests
   exercise Canvas creation, hidden shield labels, valid attack drops, invalid
   drops, returning to the source, long-press hand browsing, pointer cancel,
   renderer requirements for custom graphics, and deck setup scroll preservation
   with the fixed Start duel bar. Additional cases cover tapped-card rotation,
   inverted mana, face-up graveyard piles, centered video, automatic resolution
   without legal blockers, and retaining the prompt when blocking is possible.
5. Build And Run the Android Development Build using the previously saved scene.
   Check the status bar/notch and bottom gesture area do not overlap controls.
6. On the phone, test short and large hands, crowded battle zones, preview swipes,
   manual mana, evolution material inspection, and ending the turn. Test a normal
   attack, Double Breaker, blocker interception, Shield Trigger, tap ability,
   and any effect that gives the defender shield selection.

## Validation limits

The pure-C# core suite must use separate Core and test assemblies to match Unity's
access boundaries. Compiling both into one assembly does not check access to
internal engine helpers. Syntax checks, layout calculations, and compilation
against API stubs do not replace a real Unity compile or Android device test.
The latest zone/rotation/video revision has passed C# syntax checks and portrait
zone-separation calculations at 720-wide heights of 1280, 1440, 1600, and 1760.
The PlayMode tests and visual/touch checks must run in the real Unity editor/device
environment; this development environment has no Unity editor or connected Android device.

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
