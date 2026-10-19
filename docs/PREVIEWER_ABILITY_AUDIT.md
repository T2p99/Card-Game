# Previewer ability audit — 2026-10-19

The audit covers all 916 original English TCG printings (DM-01 through DM-12 and PROMO), all 723 distinct printed rule lines, their configured keywords and evolution requirements, and all 881 executable top-level rule steps. It includes nested granted and scheduled abilities.

[The complete 723-row ledger](PREVIEWER_ABILITY_COVERAGE.csv) records the exact rule text, handler, condition flag, preview route, player controls, and example cards. The implementation status in the engine's original ability inventory is not used as proof that a human can operate an ability through the previewer.

## Findings and fixes

| Ability category | Gap found | Result |
|---|---|---|
| Conditional and granted combat abilities | Printed keyword checks hid live Speed Attacker, untapped targets, blockers, and restrictions. | Combat actions and targets use the engine's current validation. |
| Current power and mana cost | Printed values missed continuous and temporary changes. | Current values and legal payment ranges come from the engine. |
| Optional triggers and Silent Skill | The previewer accepted optional effects automatically. | A shared dialog offers use/decline, including decisions during the opponent's action or turn transition. |
| End-of-turn self untap | The parser converted both mandatory and optional untap into an automatic keyword. | These are timed rule abilities; optional versions, including conditional versions, ask the player. |
| Single and multi-card effects | Most selections fell back to the lowest card instance IDs; early single-target pickers could evaluate conditions before the triggering event. | Choices occur at actual resolution time, using the resolver's current legal candidates and selection counts. Sequential effects ask separately; visible candidates identify their controller and zone. |
| Power-budget destruction | A legal count of selected cards could still exceed combined power. | The dialog and answer validator enforce the live power budget. |
| Shared, inherited, and granted tap abilities | The previewer exposed only a creature's first single-target ability. | The resolver asks which legal source to use and then asks for that ability's choices. Conditional sharing is checked by both availability and resolution. |
| Race, civilization, number, and player choices | The engine supported these interfaces but the previewer supplied no human implementation. | All decision-provider interfaces have a human dialog. |
| Trigger and replacement order | Independent effects used automatic ordering. | Human-controlled ordering decisions are presented when the engine requests them. |
| Evolution and Vortex evolution | The picker checked one printed race and one base. | The picker uses live races, universal bases, and complete legal combinations. All five Vortex creatures can select their two bases. Evolution shield triggers also ask for their base, including Q-tronic Omnistrain, and play for zero mana. |
| Searches, reveal effects, and deck ordering | Effects could execute without showing the player the information or ordering choice. | Search/order choices are shown, inspected information appears with decisions and in a review panel. Shields turned face up by effects are visible and inspectable on the board; the viewer only navigates other face-up shields. Face-down shields and uninspected opponent hands stay masked. Inspection permission is cleared between commands. |
| Shield triggers outside normal combat | The control panel assumed pending human triggers occurred during the opponent's turn. | Prompts are based on card ownership, regardless of whose turn it is. Declining remaining triggers affects only that player’s cards. Computer-trigger resolution also uses the human-choice bridge for resulting effects. |
| Passive restrictions, replacements, delayed effects, and state-based effects | These need rules evaluation rather than a new activation button. | They continue through the same resolver and command validation; the live ability view includes effective keywords, inherited rules, and condition/timing information. |

## How choices work

`AbilityActionSession` runs a command against an isolated match snapshot. If the engine asks a human question, the trial pauses without altering the displayed match. Answering replays that command with the same random state and earlier answers; only the completed successful result is committed. Failed commands leave the match untouched. This preserves the existing synchronous rules implementation and avoids spending mana, drawing, shuffling, or triggering effects twice.

Computer decisions retain the existing deterministic policy. This change exposes human decisions; it does not make the computer strategically evaluate every ability.

The card viewer defaults to **live abilities**, with a switch to printed text. Continuous conditions show whether their condition currently holds. Event-triggered abilities show their timing and explicitly say that their conditions are checked at that event; the view does not claim a future trigger is already active.

## Verification and limits

- **3,065 automated core tests pass**, with no skipped or inconclusive tests.
- Each of the 916 printings has a read-only live-preview query check and a play/choice replay comparison against automatic engine resolution in a controlled fixture, with matching evolution bases supplied. Every one of the 87 printed shield-trigger cards also has a zero-mana choice replay check.
- Focused tests cover optional multi-step resolution, invalid answers, single commitment of mana and effects, Silent Skill accept/decline, optional end-turn untap, hidden shield selection, all five Vortex creatures, granted races and universal evolution bases, conditional shared tap abilities, power budgets, race/civilization/number/player choices, trigger ordering, replacement ordering, and snapshot reference isolation.
- The presentation source passes C# syntax and type checking against temporary Unity API stubs. This is **not** an actual Unity compilation or play-mode test.

No unmatched ability category was identified in the current inventory's preview routes. The ledger is a route audit and the catalogue tests are smoke checks; neither proves every possible board state or every combination of conditions. Actual Unity EditMode tests and visual interaction testing remain necessary after pulling these changes, especially dialog layout, turn transitions, reveal/search flows, and combat involving triggered choices.
