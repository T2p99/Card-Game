#!/usr/bin/env python3
"""Build the canonical English-TCG ability inventory from the card manifests.

The generated CSV is the rules implementation ledger.  Every distinct printed
rules line receives a stable identifier and must name an executable handler
before the strict coverage check can pass.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import re
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MANIFEST_DIR = ROOT / "Assets/Resources/CardGame/Data/Tcg"
OUTPUT = MANIFEST_DIR / "ability_registry.csv"


@dataclass(frozen=True)
class Classification:
    category: str
    handler: str
    status: str = "Complete"


KEYWORD_PATTERNS: tuple[tuple[re.Pattern[str], Classification], ...] = (
    (re.compile(r"^Double breaker(?:\s|$)", re.I), Classification("Keyword", "keyword.double_breaker")),
    (re.compile(r"^Triple breaker(?:\s|$)", re.I), Classification("Keyword", "keyword.triple_breaker")),
    (re.compile(r"^Blocker(?:\s|$)", re.I), Classification("Keyword", "keyword.blocker")),
    (re.compile(r"^Shield trigger(?:\s|$)", re.I), Classification("Keyword", "keyword.shield_trigger")),
    (re.compile(r"^Speed attacker(?:\s|$)", re.I), Classification("Keyword", "keyword.speed_attacker")),
    (re.compile(r"^Power attacker \+(\d+)", re.I), Classification("Keyword", "keyword.power_attacker")),
    (re.compile(r"^(?:\w+ and \w+ )?Slayer(?:\s|$)", re.I), Classification("Keyword", "keyword.slayer")),
    (re.compile(r"^This creature can attack untapped creatures\.?$", re.I), Classification("Keyword", "keyword.attack_untapped")),
    (re.compile(r"^This creature attacks each turn if able\.?$", re.I), Classification("Keyword", "keyword.must_attack")),
    (re.compile(r"^This creature can't attack\.?$", re.I), Classification("Restriction", "restriction.cannot_attack")),
    (re.compile(r"^This creature can't attack players\.?$", re.I), Classification("Restriction", "restriction.cannot_attack_players")),
    (re.compile(r"^This creature can't attack creatures\.?$", re.I), Classification("Restriction", "restriction.cannot_attack_creatures")),
    (re.compile(r"^This creature can't be blocked\.?$", re.I), Classification("Restriction", "restriction.unblockable")),
    (re.compile(r"^This creature can't be attacked\.?$", re.I), Classification("Restriction", "restriction.cannot_be_attacked")),
    (re.compile(r"^Evolution—Put on one of your .+\.?$", re.I), Classification("Evolution", "evolution.single_race")),
    (re.compile(r"^Vortex evolution—Put on one of your .+ and one of your .+\.?$", re.I), Classification("Evolution", "evolution.vortex")),
    (re.compile(r"^Charger(?:\s|$)", re.I), Classification("Keyword", "keyword.charger")),
    (re.compile(r"^(?:Light|Water|Darkness|Fire|Nature) stealth(?:\s|$)", re.I), Classification("Keyword", "keyword.stealth")),
    (re.compile(r"^Survivor(?:\s|$)", re.I), Classification("Keyword", "keyword.survivor")),
    (re.compile(r"^Wave striker(?:\s|$)", re.I), Classification("Keyword", "keyword.wave_striker")),
    (re.compile(r"^Silent skill(?:\s|$)", re.I), Classification("Keyword", "keyword.silent_skill")),
    (re.compile(r"^Turbo rush(?:\s|$)", re.I), Classification("Keyword", "keyword.turbo_rush")),
    (re.compile(r"^Instead of having this creature attack, you may tap it to use its tap ability\.?$", re.I), Classification("Keyword", "keyword.tap_ability")),
    (re.compile(r"^\(This (?:creature|spell) is put into your mana zone tapped\.\)$", re.I), Classification("Rule", "rule.multicivilization_mana_tapped")),
)


def clean_line(line: str) -> str:
    return line.strip().lstrip("■").strip()


def stable_id(text: str) -> str:
    normalized = re.sub(r"\s+", " ", text).strip().casefold()
    return "ability." + hashlib.sha1(normalized.encode("utf-8")).hexdigest()[:12]


TIMING_PREFIXES = (
    "When you put this creature into the battle zone, ",
    "Whenever this creature attacks, ",
    "When this creature is destroyed, ",
    "When this creature wins a battle, ",
    "Whenever this creature battles, ",
    "Whenever this creature becomes blocked, ",
    "Whenever this creature blocks, ",
    "At the end of each of your turns, ",
    "At the end of the turn, ",
)


def classify_declarative_effect(text: str) -> Classification | None:
    original = text
    action = text
    for prefix in TIMING_PREFIXES:
        if action.casefold().startswith(prefix.casefold()):
            action = action[len(prefix):]
            break
    optional_action = re.sub(r"^you may ", "", action, flags=re.I)

    exact: dict[str, tuple[str, str]] = {
        "when you put this creature into the battle zone, destroy one of your creatures. then your opponent chooses one of his creatures and destroys it.":
            ("Compound", "compound.destroy_each_player"),
        "whenever any of your creatures becomes blocked this turn, it gets \"slayer\" until the end of the turn. (when a creature that has \"slayer\" loses a battle, destroy the other creature.)":
            ("Delayed", "delayed.blocked_creature_slayer"),
    }
    if original.casefold() in exact:
        category, handler = exact[original.casefold()]
        return Classification(category, handler)

    patterns: tuple[tuple[str, str, str], ...] = (
        (r"^When this creature would be destroyed, (?:put|return) it (?:to|into) your (?:hand|mana zone) instead\.$",
         "Replacement", "replacement.destroy_move"),
        (r"^(?:you may )?untap (?:all|any number of) your(?: light)? creatures(?: in the battle zone)?\.$",
         "Triggered", "effect.untap_creatures"),
        (r"^Add (?:(?:up to) )?(?:\d+ )?(?:a card|one of your creatures|the top card of your deck|cards from the top of your deck)(?: from (?:your hand|your mana zone|the battle zone|the top of your deck))? to your shields face down\.$",
         "Move", "move.add_shield"),
        (r"^(?:Put|Return) (?:(?:up to) )?(?:(?:\d+)|one|a)? ?(?:of )?(?:your opponent's|your)? ?(?:cards?|creatures?|spells?) (?:from )?(?:the |your )?(?:battle zone|hand|mana zone|graveyard) (?:into|to) (?:your |his |their owners' |its owner's )?(?:hand|mana zone|graveyard|battle zone)\.$",
         "Move", "move.selected"),
        (r"^(?:Choose )(?:(?:up to) )?(?:(?:\d+)|one|a)? ?(?:of )?(?:your opponent's|your|his)? ?(?:cards?|creatures?) (?:in|from) (?:the |your |his )?(?:battle zone|hand|mana zone|graveyard) and (?:put|puts|return|returns) (?:it|them|those cards) (?:into|to) (?:your |his |their owners' |its owner's )?(?:hand|hands|mana zone|graveyard|battle zone)\.$",
         "Move", "move.selected"),
        (r"^Return all (?:your opponent's |your )?creatures in the battle zone that (?:have power \d+ or less|have \"blocker\.?\") to (?:their owners'|its owner's|his|your) hand\.$",
         "Move", "move.filtered_all"),
        (r"^Destroy \d+ of your creatures or destroy this creature\.$",
         "Destroy", "destroy.required_or_source"),
        (r"^Destroy (?:(?:up to) )?(?:(?:\d+) (?:of )?|a |one of )?(?:all |any number of )?(?:your opponent's |your |his )?(?:other )?creatures?(?: in the battle zone)?(?: that (?:has|have) (?:\"blocker\.?\"|“blocker\.?”))?(?: and power| that (?:has|have) power)?(?: \d+(?: or less)?)?\.$",
         "Destroy", "destroy.selected_or_all"),
        (r"^When you put this creature into the battle zone, your opponent chooses one of his creatures and destroys it\.$",
         "Destroy", "destroy.opponent_choice"),
        (r"^When you put this creature into the battle zone, your opponent chooses one of his creatures in the battle zone and puts it into his mana zone\.$",
         "Move", "move.opponent_choice"),
        (r"^destroy (?:it|this creature)\.$", "Destroy", "destroy.source"),
        (r"^(?:Choose )?(?:(?:up to) )?(?:(?:\d+) (?:of )?|one of )?(?:all )?(?:your opponent's |your )?(?:untapped )?creatures? in the battle zone(?: and)? tap (?:it|them)\.$|^Tap all (?:your opponent's )?creatures in the battle zone\.$",
         "Tap", "tap.selected_or_all"),
        (r"^Your opponent discards (?:\d+ cards|a card) at random from (?:his|their) hand\.$",
         "Discard", "discard.opponent_random"),
        (r"^Your opponent chooses and discards (?:\d+ cards|a card) from his hand\.$",
         "Discard", "discard.opponent_choice"),
        (r"^Search your deck\. You may take (?:a|one|up to \d+|any number of) (?:card|creature|spell|multicolored creature|non-nature card|light card|water card|darkness card|fire card|nature card) from your deck(?:, show (?:that card|that creature|that spell|those cards) to your opponent,?)? and put (?:it|them) into your (?:hand|mana zone)\. Then shuffle your deck\.$",
         "Search", "search.deck_filtered"),
        (r"^(?:One of|Each of) your (?:fire |water |darkness |light |nature )?creatures?(?: in the battle zone)? gets \"power attacker \+\d+\"(?: and \"(?:double breaker|triple breaker)\")? until the end of the turn",
         "Modifier", "modifier.temporary_power_attacker"),
        (r"^(?:One of|Each of) your (?:fire |water |darkness |light |nature )?creatures(?: in the battle zone)? gets \+\d+ power until the end of the turn\.$",
         "Modifier", "modifier.temporary_power"),
        (r"^(?:One of|Each of) your (?:fire |water |darkness |light |nature )?creatures(?: in the battle zone)? gets \"?(?:This creature )?(?:can't be blocked|speed attacker|slayer|double breaker|triple breaker)\"? until the end of the turn",
         "Modifier", "modifier.temporary_keyword"),
        (r"^(?:Choose )?(?:(?:up to) \d+ of |one of )your creatures(?: in the battle zone)?\. (?:It|They) can't be blocked this turn\.$",
         "Modifier", "modifier.temporary_unblockable"),
        (r"^Choose (?:one|1) of your opponent's untapped creatures in the battle zone\. Your creatures can attack it this turn as though it were tapped\.$",
         "Modifier", "modifier.attackable_untapped"),
        (r"^This creature can't be blocked by any creature that has power \d+ or less\.$",
         "Restriction", "restriction.blocker_power_limit"),
        (r"^This creature can't be blocked while you have at least \d+ other creatures in the battle zone\.$",
         "Static", "static.conditional_unblockable"),
        (r"^While you have at least \d+ .+? in the battle zone, this creature gets \+\d+ power(?: during its attacks)?\.$",
         "Static", "static.conditional_power"),
        (r"^While attacking, this creature gets \+\d+ power for each (?:light|water|darkness|fire|nature) card in your graveyard\.$",
         "Static", "static.graveyard_power"),
        (r"^When you put this creature into the battle zone, if you have (?:\d+|a) .+? in the battle zone, draw up to \d+ cards\.$",
         "Triggered", "trigger.summon_conditional_draw"),
    )
    for pattern, category, handler in patterns:
        if re.search(pattern, original, re.I) or re.search(pattern, action, re.I) or re.search(pattern, optional_action, re.I):
            return Classification(category, handler)
    return None


def classify(text: str) -> Classification:
    for pattern, classification in KEYWORD_PATTERNS:
        if pattern.search(text):
            return classification
    generic_patterns: tuple[tuple[str, str, str], ...] = (
        (r"^At the end of each of your turns, (?:you may )?untap this creature\.$", "Triggered", "trigger.end_turn_untap_self"),
        (r"^When you put this creature into the battle zone, (?:you may )?draw (?:up to )?(?:\d+ cards?|a card)\.$", "Triggered", "trigger.summon_draw"),
        (r"^Draw (?:up to )?(?:\d+ cards?|a card)\.$", "Spell", "spell.draw"),
        (r"^When you put this creature into the battle zone, (?:you may )?put the top (?:\d+ cards?|card) of your deck into your mana zone\.$", "Triggered", "trigger.summon_mana_acceleration"),
        (r"^Put the top (?:\d+ cards?|card) of your deck into your mana zone\.$", "Spell", "spell.mana_acceleration"),
        (r"^Tap all your opponent's creatures in the battle zone\.$", "Spell", "spell.tap_all_opponent"),
        (r"^(?:Choose (?:one|1) of your opponent's creatures in the battle zone and tap it|Choose one of your opponent's creatures in the battle zone\. Tap it)\.$", "Effect", "spell.tap_opponent"),
        (r"^Destroy one of your opponent's creatures\.$", "Spell", "spell.destroy_opponent"),
        (r"^Destroy one of your opponent's untapped creatures\.$", "Spell", "spell.destroy_untapped"),
        (r"^Destroy (?:one|1) of your opponent's creatures that has power \d+ or less\.$", "Spell", "spell.destroy_power"),
        (r"^When you put this creature into the battle zone, (?:you may )?destroy one of your opponent's creatures that has power \d+ or less\.$", "Triggered", "trigger.summon_destroy_power"),
        (r"^Choose a creature in the battle zone and return it to its owner's hand\.$", "Move", "move.return_creature_hand"),
    )
    for pattern, category, handler in generic_patterns:
        if re.search(pattern, text, re.I):
            return Classification(category, handler)
    declarative = classify_declarative_effect(text)
    if declarative is not None:
        return declarative
    return Classification("Unmapped", "", "Unimplemented")


def load_rows() -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for path in sorted(MANIFEST_DIR.glob("*.csv")):
        if path == OUTPUT:
            continue
        with path.open(encoding="utf-8-sig", newline="") as source:
            rows.extend(csv.DictReader(source))
    return rows


def generate() -> tuple[int, int]:
    occurrences: dict[str, list[str]] = defaultdict(list)
    for card in load_rows():
        for raw_line in card["card_text"].replace("\r", "").splitlines():
            line = clean_line(raw_line)
            if line:
                occurrences[line].append(f"{card['card_set']} {card['collector_number']} {card['card_name']}")

    fieldnames = [
        "ability_id", "category", "handler", "implementation_status",
        "printing_count", "rule_text", "example_cards",
    ]
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    unmapped = 0
    with OUTPUT.open("w", encoding="utf-8", newline="") as destination:
        writer = csv.DictWriter(destination, fieldnames=fieldnames)
        writer.writeheader()
        for line in sorted(occurrences, key=str.casefold):
            classification = classify(line)
            if classification.status != "Complete":
                unmapped += 1
            writer.writerow({
                "ability_id": stable_id(line),
                "category": classification.category,
                "handler": classification.handler,
                "implementation_status": classification.status,
                "printing_count": len(occurrences[line]),
                "rule_text": line,
                "example_cards": " | ".join(occurrences[line][:5]),
            })
    return len(occurrences), unmapped


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-complete", action="store_true", help="Fail while any rules line is unmapped")
    args = parser.parse_args()
    total, unmapped = generate()
    print(f"Ability inventory: {total} distinct rules lines; {total - unmapped} mapped; {unmapped} unmapped")
    print(OUTPUT.relative_to(ROOT))
    return 1 if args.require_complete and unmapped else 0


if __name__ == "__main__":
    raise SystemExit(main())
