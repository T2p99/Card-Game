# English TCG rules and implementation scope

This project targets the discontinued English Duel Masters TCG card pool, not the evolving Japanese ruleset. The supplied rules video is the intended onboarding reference; the implementation is cross-checked against the English quick-start flow.

## Core match setup

- Two players.
- Each deck contains at least 40 cards and no more than four copies of a card name.
- Shuffle each deck independently.
- Each player places five cards face down as shields, then draws five cards.
- Randomly choose the starting player.
- The starting player skips the draw step of the first turn.

## Turn flow

1. Untap all cards in the active player's mana and battle zones.
2. Draw one card, except on the starting player's first turn.
3. Optionally place one card from hand into the mana zone.
4. Summon creatures and cast spells in any order while costs can be paid.
5. Attack with eligible creatures, one at a time.
6. End the turn.

Once the first attack is declared, the player cannot return to playing cards that turn.

## Mana and playing cards

- Every card can be placed into the mana zone and normally supplies one mana.
- Playing a card taps mana cards equal to its cost.
- At least one tapped mana card must share a civilization with the card being played.
- A creature enters the battle zone and normally cannot attack on the turn it was summoned.
- A spell resolves its text in order and then goes to the graveyard.

## Attacking and battles

- Declaring an attack taps the attacking creature.
- A creature can normally attack the opponent or one of the opponent's tapped creatures.
- A creature with **Blocker** may tap to redirect an eligible attack to itself.
- In a creature battle, the lower-power creature is destroyed. Equal power destroys both.
- An unblocked attack on a player breaks shields according to the creature's breaker count.
- Broken shields go to their owner's hand. Eligible **Shield Trigger** cards may be used immediately without paying their mana cost.
- An unblocked attack against a player with no shields wins the game.

## Other loss condition

A player loses when their deck becomes empty under the English rules interpretation used by this simulator.

## Initial implementation boundary

The first vertical slice includes:

- deterministic setup and shuffling;
- turn, draw, mana charge, mana payment, summon, attack, block, battle, shield break, and win logic;
- core keyword hooks for Blocker, Speed Attacker, Shield Trigger, Double Breaker, Triple Breaker, Power Attacker, and Slayer;
- local rules-engine tests;
- a small starter dataset using identifiers and stats only.

It does not yet include:

- every card-specific effect;
- full multi-civilization, evolution, cross-gear, and set-specific mechanics;
- online multiplayer, matchmaking, accounts, collections, or monetization;
- copyrighted card artwork, audio, video, logos, or fonts in the repository;
- production-quality attack cinematics.

## Architecture rule

Gameplay logic must remain independent of Unity scenes and animation. Presentation listens to rule-engine events. This keeps local play, AI, replays, tests, and future host-authoritative multiplayer on one deterministic rules implementation.
