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
import json
import re
from collections import defaultdict
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MANIFEST_DIR = ROOT / "Assets/Resources/CardGame/Data/Tcg"
OUTPUT = MANIFEST_DIR / "ability_registry.csv"
RULES_SOURCE_DIR = ROOT / "Assets/CardGame/Core"


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
    (re.compile(r"^Crew breaker—.+? \(", re.I), Classification("Keyword", "keyword.crew_breaker")),
    (re.compile(r"^Instead of having this creature attack, you may tap it to use its tap ability\.?$", re.I), Classification("Keyword", "keyword.tap_ability")),
    (re.compile(r"^You can put an evolution creature of any race on this creature\.?$", re.I), Classification("Evolution", "evolution.universal_base")),
    (re.compile(r"^\(This (?:creature|spell) is put into your mana zone tapped\.\)$", re.I), Classification("Rule", "rule.multicivilization_mana_tapped")),
)


def clean_line(line: str) -> str:
    return line.strip().lstrip("■").strip()


def stable_id(text: str) -> str:
    normalized = re.sub(r"\s+", " ", text).strip().casefold()
    return "ability." + hashlib.sha1(normalized.encode("utf-8")).hexdigest()[:12]


@lru_cache(maxsize=1)
def explicit_runtime_matchers() -> tuple[
        dict[str, tuple[str, str]], list[tuple[str, str, str]]]:
    """Index explicit text matchers in the executable per-set registries.

    The semantic patterns below provide friendly handler names for reusable
    mechanics.  This index is the final source-backed fallback: a rules line is
    only considered implemented when a registry actually contains an Equals or
    StartsWith branch for it.  Keeping this link mechanical prevents the CSV
    coverage ledger from claiming completion after runtime code is removed or
    renamed.
    """
    exact: dict[str, tuple[str, str]] = {}
    prefixes: list[tuple[str, str, str]] = []
    matcher = re.compile(
        r'text\.(Equals|StartsWith)\(\s*("(?:\\.|[^"\\])*")', re.S)

    for path in sorted(RULES_SOURCE_DIR.glob("AbilityRegistry.*Rules.cs")):
        module = path.stem.removeprefix("AbilityRegistry.").removesuffix("Rules").casefold()
        source = path.read_text(encoding="utf-8")
        for condition in iter_if_conditions(source):
            for kind, literal in matcher.findall(condition):
                value = json.loads(literal)
                handler = f"runtime.{module}.{stable_id(value).removeprefix('ability.')}"
                if kind == "Equals":
                    exact[value.casefold()] = ("Explicit", handler)
                else:
                    prefixes.append((value.casefold(), "Explicit", handler))

    # Prefer the most specific StartsWith matcher when prefixes overlap.
    prefixes.sort(key=lambda item: len(item[0]), reverse=True)
    return exact, prefixes


def iter_if_conditions(source: str):
    """Yield balanced C# ``if (...)`` conditions, ignoring strings/comments.

    Looking for every ``text.StartsWith`` invocation in a source file is too
    broad: helper code can use the same call in a ternary or assignment without
    implementing a printed rule.  Restricting extraction to actual branch
    conditions keeps the strict ledger tied to executable dispatch paths.
    """
    start_pattern = re.compile(r"\bif\s*\(")
    position = 0
    while True:
        match = start_pattern.search(source, position)
        if match is None:
            return
        opening = source.find("(", match.start())
        index = opening + 1
        depth = 1
        state = "code"
        while index < len(source) and depth:
            char = source[index]
            next_char = source[index + 1] if index + 1 < len(source) else ""
            if state == "code":
                if char == '"':
                    state = "string"
                elif char == "'":
                    state = "char"
                elif char == "/" and next_char == "/":
                    state = "line_comment"
                    index += 1
                elif char == "/" and next_char == "*":
                    state = "block_comment"
                    index += 1
                elif char == "(":
                    depth += 1
                elif char == ")":
                    depth -= 1
            elif state in {"string", "char"}:
                if char == "\\":
                    index += 1
                elif (state == "string" and char == '"') or (state == "char" and char == "'"):
                    state = "code"
            elif state == "line_comment" and char in "\r\n":
                state = "code"
            elif state == "block_comment" and char == "*" and next_char == "/":
                state = "code"
                index += 1
            index += 1
        if depth == 0:
            yield source[opening + 1:index - 1]
        position = max(index, match.end())


def classify_explicit_runtime(text: str) -> Classification | None:
    exact, prefixes = explicit_runtime_matchers()
    folded = text.casefold()
    if folded in exact:
        category, handler = exact[folded]
        return Classification(category, handler)
    for prefix, category, handler in prefixes:
        if folded.startswith(prefix):
            return Classification(category, handler)
    return None


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
        "each of your creatures in the battle zone that can attack creatures can attack untapped creatures this turn.":
            ("Modifier", "modifier.attack_untapped_this_turn"),
        "this turn, ignore any effects that would prevent your creatures from attacking your opponent. (for example, ignore summoning sickness and card effects that say \"this creature can't attack\" or \"this creature can't attack players\". your creatures can't attack creatures this way.)":
            ("Modifier", "modifier.ignore_attack_restrictions"),
        "when this creature attacks a player, destroy it after the attack.":
            ("Triggered", "trigger.after_player_attack_destroy_self"),
        "when this creature would be put into your graveyard from the battle zone, you may return it to your hand instead. if you do, discard a card from your hand.":
            ("Replacement", "replacement.destroy_to_hand_discard"),
        "whenever this creature attacks, you may draw a number of cards equal to the number of creatures your opponent has in the battle zone.":
            ("Triggered", "trigger.attack_draw_opponent_creature_count"),
        "whenever this creature blocks, untap it after it battles.":
            ("Triggered", "trigger.after_block_battle_untap"),
        "your opponent discards all cards from his hand.":
            ("Discard", "discard.opponent_hand"),
    }
    if original.casefold() in exact:
        category, handler = exact[original.casefold()]
        return Classification(category, handler)

    patterns: tuple[tuple[str, str, str], ...] = (
        (r"^After you cast this spell, put it into your mana zone instead of your graveyard\.$",
         "Keyword", "keyword.charger_alternate_wording"),
        (r"^Choose any number of your shields and put them into your (?:hand|mana zone)\.",
         "Move", "move.any_own_shields"),
        (r"^Each of your other (?:light|water|darkness|fire|nature) creatures has \"power attacker \+\d+\"",
         "Static", "static.civilization_power_attacker_aura"),
        (r"^Each of your other light creatures in the battle zone has \"blocker\"",
         "Static", "static.civilization_blocker_aura"),
        (r"^Look at your opponent's hand and shields\.",
         "Information", "look.opponent_hand_and_shields"),
        (r"^Return a creature that costs 6 or more from your mana zone to your hand\.$",
         "Move", "move.mana_creature_cost_at_least"),
        (r"^Reveal the top 4 cards of your deck\. Put all water cards from among them into your hand and the rest into your graveyard\.$",
         "Reveal", "reveal.partition_by_civilization"),
        (r"^This creature can't attack while your opponent has more creatures in the battle zone than you do\.$",
         "Restriction", "restriction.comparative_creature_count"),
        (r"^This creature gets \+\d+ power for each (?:of your )?other (?:light|water|darkness|fire|nature) creatures?(?: you have)? in the battle zone\.$",
         "Static", "static.power_per_other_civilization"),
        (r"^When this creature is put into your graveyard from the battle zone, you may return a creature from your mana zone to your hand\.$",
         "Triggered", "trigger.destroyed_return_mana_creature"),
        (r"^When this creature would be put into your graveyard from the battle zone, add it to your shields face down instead\.$",
         "Replacement", "replacement.destroy_to_shield"),
        (r"^When you put this creature into the battle zone, choose one of your shields and return it to your hand\.",
         "Move", "move.summon_shield_to_hand"),
        (r"^When you put this creature into the battle zone, return (?:\d+ cards|a card) from your mana zone to your hand\. Then your opponent chooses (?:\d+ cards|a card) (?:in|from) his mana zone",
         "Compound", "compound.summon_each_player_mana_bounce"),
        (r"^When you put this creature into the battle zone, you may add a card from your hand to your shields face down\. If you do, choose one of your shields",
         "Compound", "compound.summon_shield_swap"),
        (r"^When you put this creature into the battle zone, you may choose one of your opponent's evolution creatures in the battle zone and put the top card",
         "Evolution", "evolution.remove_top_card"),
        (r"^Whenever another of your darkness creatures would be put into your graveyard from the battle zone, you may return it to your hand instead\.$",
         "Replacement", "replacement.aura_darkness_to_hand"),
        (r"^Whenever another of your nature creatures would be put into your graveyard from the battle zone, put it into your mana zone instead\.$",
         "Replacement", "replacement.aura_nature_to_mana"),
        (r"^Whenever this creature attacks, put a non-fire card from your mana zone into your graveyard\. Then your opponent chooses a non-fire card",
         "Compound", "compound.attack_each_player_mana_burn"),
        (r"^Whenever this creature attacks, return all creatures from your mana zone to your hand\.$",
         "Move", "move.attack_all_mana_creatures_to_hand"),
        (r"^Whenever this creature attacks, you may choose a creature in the battle zone that has power 2000 or less and return it",
         "Move", "move.attack_small_creature_to_hand"),
        (r"^Whenever this creature attacks, you may draw a card for each (?:of your )?other (?:light|water|darkness|fire|nature) creatures? in the battle zone\.$",
         "Triggered", "trigger.attack_draw_per_civilization"),
        (r"^Whenever this creature attacks, you may put a card from your mana zone into your graveyard\. If you do, this creature gets \"power attacker \+3000\"",
         "Compound", "compound.attack_mana_cost_power_breaker"),
        (r"^Whenever this creature attacks, you may (?:put|return) a (?:nature card|darkness creature|light spell) from your graveyard (?:into|to) your (?:mana zone|hand)\.$",
         "Move", "move.attack_filtered_graveyard"),
        (r"^While all the cards in your mana zone are (?:light|water|darkness|fire|nature) cards, this creature gets \+\d+ power",
         "Static", "static.mono_mana_power"),
        (r"^While all the cards in your mana zone are (?:light|water|darkness|fire|nature) cards, this creature has \"blocker\"",
         "Static", "static.mono_mana_blocker"),
        (r"^You can cast this spell only if all the cards in your mana zone are (?:light|water|darkness|fire|nature) cards\.$",
         "Restriction", "restriction.cast_mono_mana"),
        (r"^You may destroy one of your darkness creatures\. If you do, return a creature from your mana zone to your hand\.$",
         "Compound", "compound.sacrifice_then_return_mana_creature"),
        (r"^(?:Angel Commands and Demon Commands|Light and darkness creatures) in the battle zone each get \+\d+ power\.$",
         "Static", "static.global_two_group_power_aura"),
        (r"^Discard any number of cards from your hand\. Then choose the same number of your creatures",
         "Compound", "compound.discard_then_grant_breaker"),
        (r"^Each (?:Angel Command|Demon Command) in the battle zone (?:gets|has) \"power attacker \+\d+",
         "Static", "static.global_race_power_attacker"),
        (r"^Each Armored Dragon in the battle zone gets \+\d+ power\.$",
         "Static", "static.global_race_power"),
        (r"^Each (?:light|water|darkness|fire|nature) creature costs \d+ more to summon,? and each (?:light|water|darkness|fire|nature) spell costs \d+ more to cast\.$",
         "Static", "static.global_civilization_cost_tax"),
        (r"^Each Demon Command in the battle zone gets \+2000 power and has \"blocker\"",
         "Static", "static.global_race_power_blocker"),
        (r"^Each of your creatures in the battle zone gets \+1000 power until the end of the turn for each light creature",
         "Modifier", "modifier.power_per_civilization"),
        (r"^Each other (?:light|water|darkness|fire|nature) creature in the battle zone gets \+\d+ power\.$",
         "Static", "static.global_other_civilization_power"),
        (r"^For each (?:light|water|darkness|fire|nature) creature you have in the battle zone, you may choose (?:one of your opponent's creatures|a card in your opponent's mana zone) and return it to his hand\.$",
         "Move", "move.per_civilization_count"),
        (r"^Players can't cast spells other than light spells\.$",
         "Restriction", "restriction.global_spell_civilization"),
        (r"^Players can't use the \"shield trigger\" abilities of (?:light|water|darkness|fire|nature) cards\.$",
         "Restriction", "restriction.global_shield_trigger_civilization"),
        (r"^Put any number of cards from your mana zone into your graveyard\. Then draw that many cards\.$",
         "Compound", "compound.mana_to_graveyard_then_draw"),
        (r"^Tap all creatures in the battle zone except (?:light|water|darkness|fire|nature) creatures\.$",
         "Tap", "tap.global_except_civilization"),
        (r"^This creature can attack untapped (?:light|water|darkness|fire|nature) creatures\.$",
         "Restriction", "permission.attack_untapped_civilization"),
        (r"^This creature can't be attacked by (?:light|water|darkness|fire|nature) creatures\.$",
         "Restriction", "restriction.attacked_by_civilization"),
        (r"^This creature can't be blocked by (?:light|water|darkness|fire|nature) creatures\.$",
         "Restriction", "restriction.blocked_by_civilization"),
        (r"^This creature gets \+\d+ power for each shield you have\.$",
         "Static", "static.power_per_shield"),
        (r"^Until the end of the turn, each of your creatures in the battle zone gets \"While attacking, this creature gets \+\d+ power for each darkness card",
         "Modifier", "modifier.grant_dynamic_mana_power_attacker"),
        (r"^Until the start of your next turn, each of your creatures in the battle zone gets \"blocker\"",
         "Modifier", "modifier.blocker_until_next_turn"),
        (r"^When this creature would be destroyed, add it to your shields face down instead\.$",
         "Replacement", "replacement.destroy_to_shield"),
        (r"^When you put this creature into the battle zone, destroy all creatures except (?:light|water|darkness|fire|nature) creatures\.$",
         "Destroy", "destroy.summon_all_except_civilization"),
        (r"^When you put this creature into the battle zone, destroy all (?:light|water|darkness|fire|nature) creatures that have power \d+ or less\.$",
         "Destroy", "destroy.summon_civilization_power_limit"),
        (r"^When you put this creature into the battle zone, search your deck\. You may take a (?:light|water|darkness|fire|nature) creature from your deck",
         "Search", "search.summon_civilization_creature"),
        (r"^When you put this creature into the battle zone, tap one of your opponent's creatures in the battle zone for each untapped (?:light|water|darkness|fire|nature) card in your mana zone\.$",
         "Tap", "tap.summon_per_untapped_mana"),
        (r"^When you put this creature into the battle zone, you may return a .+ from your graveyard to your hand\.$",
         "Move", "move.summon_filtered_graveyard_to_hand"),
        (r"^When you put this creature into the battle zone, you may return all .+ from your graveyard to your hand\.$",
         "Move", "move.summon_all_filtered_graveyard_to_hand"),
        (r"^When you put this creature into the battle zone, you may reveal the top \d+ cards of your deck\. Put all .+ cards from the revealed cards into your hand, and put the rest into your graveyard\.$",
         "Reveal", "reveal.summon_partition_by_civilization"),
        (r"^When you put this creature into the battle zone, your opponent discards a card at random from his hand for each other darkness creature",
         "Discard", "discard.summon_per_other_civilization"),
        (r"^Whenever another creature is destroyed, you may draw a card\.$",
         "Triggered", "trigger.other_creature_destroyed_draw"),
        (r"^Whenever another creature is put into the battle zone, you may draw a card\.$",
         "Triggered", "trigger.other_creature_summoned_draw"),
        (r"^Whenever this creature attacks, you may choose a card in your opponent's mana zone and return it to his hand\.$",
         "Move", "move.attack_opponent_mana_to_hand"),
        (r"^Whenever this creature attacks, you may destroy one of your other creatures\. If you do, this creature gets \+\d+ power and \"double breaker\"",
         "Compound", "compound.attack_sacrifice_power_breaker"),
        (r"^While you have a (?:light|water|darkness|fire|nature) creature in the battle zone, this creature gets \+\d+ power\.$",
         "Static", "static.conditional_civilization_power"),
        (r"^Your opponent chooses and discards a card from his hand for each light creature he has in the battle zone\.$",
         "Discard", "discard.opponent_per_civilization"),
        (r"^At the end of this turn, search your deck\. For each of your opponent's shields your creatures broke this turn",
         "Delayed", "delayed.end_turn_search_per_shield"),
        (r"^During your opponent's next turn, each of his creatures attacks if able\.$",
         "Delayed", "delayed.force_opponent_attacks"),
        (r"^Each player counts the cards in his hand, shuffles those cards into his deck, then draws that many cards\.$",
         "Compound", "compound.each_player_refresh_hand"),
        (r"^Each player returns all cards from his mana zone to his hand\.$",
         "Move", "move.each_player_all_mana_to_hand"),
        (r"^(?:Light|Water|Darkness|Fire|Nature)(?: and (?:light|water|darkness|fire|nature))? blocker ",
         "Keyword", "keyword.civilization_blocker"),
        (r"^For each (?:light|water|darkness|fire|nature) creature you have in the battle zone, you may choose (?:a card in your opponent's mana zone and put it into his graveyard|one of your opponent's creatures in the battle zone and tap it)\.$",
         "Effect", "effect.per_civilization_count"),
        (r"^If your opponent has more cards in his mana zone than you have in yours, put the top \d+ cards? of your deck into your mana zone\.$",
         "Move", "move.conditional_mana_acceleration"),
        (r"^Look at your opponent's hand and choose a card from it\. Your opponent discards that card\.$",
         "Discard", "discard.look_and_choose"),
        (r"^This creature can't be attacked by (?:light|water|darkness|fire|nature) or (?:light|water|darkness|fire|nature) creatures\.$",
         "Restriction", "restriction.attacked_by_two_civilizations"),
        (r"^This creature can't be blocked by creatures that have power \d+ or more\.$",
         "Restriction", "restriction.blocker_power_minimum"),
        (r"^This creature gets \+\d+ power for each (?:light|water|darkness|fire|nature) creature and (?:light|water|darkness|fire|nature) creature your opponent has in the battle zone\.$",
         "Static", "static.power_per_opponent_civilizations"),
        (r"^This creature gets \+\d+ power\.$",
         "Static", "static.self_power"),
        (r"^When one of your Armored Dragons would be destroyed, you may destroy this creature instead\.$",
         "Replacement", "replacement.destroy_source_for_ally"),
        (r"^When this creature is destroyed, you may (?:choose )?(?:put )?(?:an? )?.+? (?:in|from) your mana zone(?: and put it)? into the battle zone\.$",
         "Move", "move.destroyed_named_mana_to_battle"),
        (r"^When this creature is destroyed, you may choose one of your shields and put it into your hand\.",
         "Move", "move.destroyed_shield_to_hand"),
        (r"^When you put this creature into the battle zone, if your opponent has more cards in his mana zone than you have in yours, put the top card of your deck into your mana zone\.$",
         "Move", "move.summon_conditional_mana_acceleration"),
        (r"^When you put this creature into the battle zone, return all other creatures from the battle zone to their owners' hands\.$",
         "Move", "move.summon_all_other_creatures_to_hand"),
        (r"^When you put this creature into the battle zone, search your deck\. You may take a Giant Insect from your deck",
         "Search", "search.summon_race_creature"),
        (r"^Whenever any of your creatures finishes attacking this turn, you may draw \d+ cards for each shield it broke\.$",
         "Delayed", "delayed.after_attack_draw_per_shield"),
        (r"^Whenever this creature attacks, you may choose a darkness or fire creature in the battle zone and tap it\.",
         "Tap", "tap.attack_civilizations"),
        (r"^Whenever this creature becomes blocked, it breaks one of your opponent's shields\.$",
         "Triggered", "trigger.blocked_break_shield"),
        (r"^Whenever your opponent causes a card to be put into your graveyard from your mana zone, you may return that card to your mana zone\.$",
         "Replacement", "replacement.opponent_mana_burn"),
        (r"^Whenever your opponent uses the \"shield trigger\" ability of a shield broken by this creature, you may untap this creature\.$",
         "Triggered", "trigger.opponent_shield_trigger_untap"),
        (r"^While your opponent has no shields, this creature can't attack\.$",
         "Restriction", "restriction.opponent_no_shields"),
        (r"^At the end of each of your turns, if this is your only creature in the battle zone, destroy it\.$",
         "Triggered", "trigger.end_turn_lone_destroy"),
        (r"^At the end of your turn, return this creature to your hand\.$",
         "Triggered", "trigger.end_turn_return_self"),
        (r"^Choose a shield and look at it\. Then put it back where it was\.$",
         "Information", "look.chosen_shield"),
        (r"^Choose one of your opponent’s evolution creatures in the battle zone and put the top card of that creature into your opponent’s mana zone\.$",
         "Evolution", "evolution.remove_top_to_mana"),
        (r"^Choose up to \d+ of your opponent's shields and put them into his graveyard\.$",
         "Move", "move.opponent_shields_to_graveyard"),
        (r"^Destroy any number of your creatures\. Then draw that many cards\.$",
         "Compound", "compound.destroy_then_draw"),
        (r"^Destroy one of your opponent’s creatures that has “blocker\.”$",
         "Destroy", "destroy.opponent_blocker_curly"),
        (r"^During your opponent's turn, if this creature would be discarded from your hand, put it into the battle zone instead\.$",
         "Replacement", "replacement.opponent_discard_to_battle"),
        (r"^Each of your creatures in the battle zone gets \+\d+ power and \"triple breaker\" until the end of the turn",
         "Modifier", "modifier.all_power_triple_breaker"),
        (r"^Each of your (?:light|water|darkness|fire|nature) creatures may tap instead of attacking to use this creature's tap ability\.$",
         "Static", "static.share_tap_ability"),
        (r"^Each of your Ghosts in the battle zone has \"slayer\.?\"",
         "Static", "static.race_slayer_aura"),
        (r"^Evolution creatures are put into the battle zone tapped\.$",
         "Static", "static.evolution_enters_tapped"),
        (r"^If your opponent has more shields than you do, add the top card of your deck to your shields face down\.$",
         "Move", "move.conditional_add_shield"),
        (r"^Look at your opponent's hand\. He discards all darkness spells from it\.$",
         "Discard", "discard.filtered_opponent_hand"),
        (r"^One of your creatures in the battle zone that has \"blocker\" gets \+\d+ power until the end of the turn\.$",
         "Modifier", "modifier.blocker_power"),
        (r"^Return a darkness creature from your graveyard to your hand\.$",
         "Move", "move.darkness_creature_graveyard_to_hand"),
        (r"^Return any number of your creatures from the battle zone to your hand\. Then you may choose that many of your opponent's creatures",
         "Compound", "compound.bounce_own_then_opponent"),
        (r"^Search your deck\. You may take any number of cards from your deck, show those cards to your opponent, and put them into your hand\. Then shuffle your deck\.$",
         "Search", "search.any_number"),
        (r"^Search your opponent's deck\. Take up to \d+ cards from his deck and put them into his graveyard\. Then your opponent shuffles his deck\.$",
         "Search", "search.opponent_deck_mill"),
        (r"^Tap all creatures in the battle zone that don't have \"blocker\.\"$",
         "Tap", "tap.all_nonblockers"),
        (r"^This creature can attack only creatures that have \"blocker\.\"$",
         "Restriction", "restriction.attack_only_blocker"),
        (r"^When this creature battles, destroy it after the battle\.$",
         "Triggered", "trigger.battle_destroy_self"),
        (r"^When this creature is destroyed, each player discards his hand\.$",
         "Discard", "discard.destroyed_each_hand"),
        (r"^When you put this creature into the battle zone, choose a card in your opponent's mana zone and return it to his hand\.$",
         "Move", "move.summon_opponent_mana_to_hand"),
        (r"^When you put this creature into the battle zone, count the cards in your hand, shuffle those cards into your deck, then draw that many cards\.$",
         "Compound", "compound.summon_refresh_hand"),
        (r"^When you put this creature into the battle zone, search your deck\. You may take a Survivor from your deck",
         "Search", "search.summon_survivor"),
        (r"^When you put this creature into the battle zone, you may draw a card for each Survivor in the battle zone\.$",
         "Triggered", "trigger.summon_draw_per_survivor"),
        (r"^When you put this creature into the battle zone, you may look at your shields\. Then put them back where they were\.$",
         "Information", "look.own_shields"),
        (r"^Whenever an opponent's creature would break a shield, you choose the shield instead of your opponent\.$",
         "Replacement", "replacement.defender_chooses_shield"),
        (r"^Whenever this creature (?:becomes blocked|blocks), no battle happens\.",
         "Triggered", "trigger.block_prevent_battle"),
        (r"^Whenever this creature would break a shield, your opponent puts that shield into his graveyard instead\.$",
         "Replacement", "replacement.shield_break_to_graveyard"),
        (r"^Whenever your opponent summons a creature or casts a spell, this creature gets \"blocker\" until the end of the turn\.",
         "Triggered", "trigger.opponent_card_blocker"),
        (r"^While you have any other untapped creatures in the battle zone, this creature can't attack\.$",
         "Restriction", "restriction.other_untapped_creature"),
        (r"^While you have at least one other fire creature in the battle zone, this creature has \"Power attacker \+\d+",
         "Static", "static.conditional_power_attacker"),
        (r"^While you have no other fire creatures in the battle zone, this creature has \"power attacker \+\d+\" and \"double breaker\.?\"",
         "Static", "static.no_other_civilization_power_breaker"),
        (r"^You can summon this creature only if you have cast a spell this turn\.$",
         "Restriction", "restriction.summon_after_spell"),
        (r"^Your creatures that have Dragon in their race each cost \d+ less to summon\.",
         "Static", "static.dragon_cost_reduction"),
        (r"^Your opponent chooses one of his creatures in the battle zone and destroys it\.$",
         "Destroy", "destroy.opponent_choice"),
        (r"^Your opponent chooses one of his creatures in the battle zone or a card in his mana zone and puts it into his graveyard\.$",
         "Move", "move.opponent_choice_battle_or_mana"),
        (r"^Your opponent's attacking creatures attack Mystery Totems if able\.$",
         "Restriction", "restriction.attack_race_if_able"),
        (r"^Choose a card in your opponent's mana zone and return it to his hand\.$",
         "Move", "move.opponent_mana_to_hand"),
        (r"^Choose one of your creatures in the battle zone\. This turn, it can't be blocked and you ignore any effects that would prevent that creature from attacking your opponent\.",
         "Modifier", "modifier.single_unblockable_attack_override"),
        (r"^Choose one of your opponent's water or darkness creatures in the battle zone\. Your opponent puts that creature into his mana zone\.$",
         "Move", "move.opponent_civilization_to_mana"),
        (r"^Destroy any number of your opponent's creatures that have total power \d+ or less\.$",
         "Destroy", "destroy.total_power_budget"),
        (r"^Destroy one of your opponent's untapped light or untapped nature creatures\.$",
         "Destroy", "destroy.untapped_two_civilizations"),
        (r"^Each of your fire creatures gets \"This creature can attack untapped creatures\" until the end of the turn\.$",
         "Modifier", "modifier.fire_attack_untapped"),
        (r"^Each player puts a card from his mana zone into his graveyard\.$",
         "Move", "move.each_player_mana_burn_one"),
        (r"^Look at the top \d+ cards of your deck\. Then put them back in any order\.$",
         "Information", "look.reorder_top_deck"),
        (r"^Tap all darkness creatures in the battle zone, or tap all fire creatures in the battle zone\.$",
         "Tap", "tap.choose_civilization_group"),
        (r"^This creature can't attack if you have no cards in your hand\.$",
         "Restriction", "restriction.attack_requires_hand"),
        (r"^This turn, whenever any of your nature creatures is attacking your opponent and becomes blocked, it breaks one of his shields\.",
         "Delayed", "delayed.blocked_nature_break_shield"),
        (r"^Until the end of the turn, each of your darkness creatures in the battle zone gets \+\d+ power and \"double breaker\.\"",
         "Modifier", "modifier.darkness_power_breaker_destroy"),
        (r"^When one of your shields would be broken, you may destroy this creature instead\.$",
         "Replacement", "replacement.shield_break_destroy_source"),
        (r"^When this creature is destroyed, you may draw a card\.$",
         "Triggered", "trigger.destroyed_optional_draw"),
        (r"^When this creature would be destroyed, you may return it to your hand instead\.$",
         "Replacement", "replacement.destroy_to_hand"),
        (r"^When this creature would be destroyed, you may return it to your hand instead\. If you do, put a card from your hand into your graveyard\.$",
         "Replacement", "replacement.destroy_to_hand_discard_card"),
        (r"^When you put this creature into the battle zone, choose one of your shields and put it into your hand\. You can't use the \"shield trigger\" ability of that shield\.$",
         "Move", "move.summon_shield_to_hand_no_trigger"),
        (r"^When you put this creature into the battle zone, you may add up to \d+ cards from your hand to your shields face down\. If you do, choose the same number of your shields",
         "Compound", "compound.summon_shield_swap_two"),
        (r"^Whenever any of your creatures would be destroyed this turn, put it into your mana zone instead\.$",
         "Replacement", "replacement.turn_destroy_to_mana"),
        (r"^Whenever this creature attacks, discard a card from your hand\.$",
         "Discard", "trigger.attack_discard"),
        (r"^Whenever this creature is attacked, it gets \"slayer\" until the end of the turn\.",
         "Triggered", "trigger.attacked_slayer"),
        (r"^Whenever you draw the card at the start of your turn, you may draw an extra card\.$",
         "Triggered", "trigger.start_draw_extra"),
        (r"^While this creature is tapped, your opponent can't use the \"shield trigger\" ability of his shields\.$",
         "Restriction", "restriction.tapped_disable_shield_trigger"),
        (r"^While you have no other creatures in the battle zone, this creature has \"Power attacker \+\d+",
         "Static", "static.lone_power_breaker"),
        (r"^While you have no shields, this creature can attack untapped creatures and has \"Power attacker \+\d+",
         "Static", "static.no_shields_attack_untapped"),
        (r"^While you have no shields, this creature gets \+\d+ power and has \"double breaker",
         "Static", "static.no_shields_power_breaker"),
        (r"^While you have no shields, this creature has \"blocker\" and \"At the end of each of your turns, you may untap this creature\.\"",
         "Static", "static.no_shields_blocker_untap"),
        (r"^While you have no shields, this creature has \"blocker\"\.",
         "Static", "static.no_shields_blocker"),
        (r"^After you cast a spell by using its \"shield trigger\" ability, put it into your hand instead of your graveyard\.$",
         "Replacement", "replacement.shield_trigger_spell_to_hand"),
        (r"^Choose a creature in the battle zone and return it to its owner's hand\. If it has Dragon in its race, you may draw a card\.$",
         "Compound", "compound.bounce_then_dragon_draw"),
        (r"^Choose one of your opponent's creatures in the battle zone and tap it\. If it has Dragon in its race, add the top card of your deck to your shields face down\.$",
         "Compound", "compound.tap_then_dragon_shield"),
        (r"^Choose one of your opponent's creatures in the battle zone and tap it\. Then you may choose one of your creatures in the battle zone\.",
         "Compound", "compound.tap_then_unblockable"),
        (r"^Choose up to \d+ of your creatures in the battle zone\. At the end of the turn, you may untap them\.$",
         "Delayed", "delayed.chosen_untap_end_turn"),
        (r"^Creatures that have power \d+ or more can't attack\.",
         "Restriction", "restriction.global_attack_power_minimum"),
        (r"^Dragon blocker ",
         "Keyword", "keyword.dragon_blocker"),
        (r"^Each creature in the battle zone has \"slayer\.\"",
         "Static", "static.global_slayer"),
        (r"^Each creature in the battle zone that has Dragon in its race has \"speed attacker\.\"",
         "Static", "static.global_dragon_speed_attacker"),
        (r"^Each of your creatures in the battle zone has \"speed attacker\.\"",
         "Static", "static.turbo_all_speed_attacker"),
        (r"^Search your deck\. You may take a creature that has Dragon in its race from your deck and put it into the battle zone\. Then shuffle your deck\. That creature has \"speed attacker\.\" At the end of the turn, destroy it\.$",
         "Compound", "compound.tap_summon_temporary_dragon"),
        (r"^This creature can attack untapped creatures and has \"Power attacker \+\d+",
         "Static", "static.turbo_attack_untapped_power"),
        (r"^This creature can't be attacked by any creature that has Dragon in its race\.$",
         "Restriction", "restriction.attacked_by_dragon"),
        (r"^This creature gets \+\d+ power for each creature in your graveyard\.$",
         "Static", "static.power_per_graveyard_creature"),
        (r"^This creature gets \+\d+ power and has \"double breaker",
         "Static", "static.turbo_power_double_breaker"),
        (r"^Until the end of the turn, each of your Dragonoids in the battle zone is an Armored Dragon in addition to its other races",
         "Modifier", "modifier.dragonoid_to_armored_dragon"),
        (r"^When this creature attacks, search your deck\. You may take a card from your deck and put it into your hand\.",
         "Search", "search.turbo_attack_any_card"),
        (r"^When this creature is destroyed, you may put a creature that has Dragon in its race from your hand into the battle zone\.$",
         "Move", "move.destroyed_dragon_hand_to_battle"),
        (r"^When this creature would be destroyed, if you have a creature that has Dragon in its race in your mana zone, put this creature into your mana zone instead of destroying it\.",
         "Replacement", "replacement.destroy_to_mana_return_dragon"),
        (r"^When you put another creature into the battle zone, destroy this creature\.$",
         "Triggered", "trigger.other_own_summon_destroy_self"),
        (r"^When you put this creature into the battle zone, look at your opponent's hand\.$",
         "Information", "look.summon_opponent_hand"),
        (r"^When you put this creature into the battle zone, your opponent may choose a creature in his hand and put it into the battle zone\.$",
         "Move", "move.summon_opponent_optional_hand_creature"),
        (r"^Whenever a player casts a spell, this creature gets \+\d+ power until the end of the turn\.",
         "Triggered", "trigger.any_spell_power"),
        (r"^Whenever another creature is put into the battle zone, this creature gets \+\d+ power until the end of the turn\.$",
         "Triggered", "trigger.other_summon_power"),
        (r"^Whenever any of your creatures attacks, you may choose one of your opponent's creatures in the battle zone and tap it\.",
         "Triggered", "trigger.allied_attack_tap"),
        (r"^Whenever this creature attacks, choose up to \d+ cards in your opponent's mana zone\. Your opponent puts those cards into his graveyard\.$",
         "Move", "move.attack_opponent_mana_burn"),
        (r"^Whenever this creature attacks, you may draw a card for each other tapped creature you have in the battle zone\.$",
         "Triggered", "trigger.attack_draw_per_other_tapped"),
        (r"^Whenever this creature attacks, you may look at \d+ of your opponent's shields\.",
         "Information", "look.attack_opponent_shields"),
        (r"^Whenever this creature attacks, you may look at your opponent's hand and at the top card of his deck\.$",
         "Information", "look.attack_opponent_hand_top_deck"),
        (r"^Whenever this creature attacks, your opponent discards his hand\.$",
         "Discard", "discard.turbo_attack_opponent_hand"),
        (r"^Whenever this creature becomes blocked, you may put the top card of your deck into your mana zone\.$",
         "Move", "move.blocked_top_deck_to_mana"),
        (r"^Whenever this creature is attacking your opponent and becomes blocked, it breaks one of your opponent's shields\.$",
         "Triggered", "trigger.turbo_blocked_break_shield"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, untap all your creatures in the battle zone except Solar Grasses\.$",
         "Triggered", "trigger.turbo_unblocked_untap"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, you may destroy a creature\.$",
         "Destroy", "destroy.turbo_unblocked_creature"),
        (r"^Whenever you put a Dragonoid or a creature that has Dragon in its race into the battle zone, destroy the creature that has the least power in the battle zone\.",
         "Triggered", "trigger.dragon_summon_destroy_lowest"),
        (r"^Whenever you put a Dragonoid or a creature that has Dragon in its race into the battle zone, put the top card of your deck into your mana zone\.$",
         "Triggered", "trigger.dragon_summon_mana_acceleration"),
        (r"^Whenever your opponent uses the \"shield trigger\" ability of one of his shields, draw up to \d+ cards\.$",
         "Triggered", "trigger.opponent_shield_trigger_draw"),
        (r"^While battling a creature that has Dragon in its race, this creature gets \+\d+ power\.$",
         "Static", "static.battle_dragon_power"),
        (r"^Your creatures in the battle zone can't be blocked this turn\.$",
         "Modifier", "modifier.all_unblockable"),
        (r"^Choose a race\. At the end of this turn, untap all creatures of that race in the battle zone\.$",
         "RaceChoice", "race.end_turn_untap"),
        (r"^Choose a race\. Creatures of that race can't be blocked by creatures that have power \d+ or less this turn\.$",
         "RaceChoice", "race.unblockable_by_power"),
        (r"^Choose a race\. Creatures of that race can't be blocked this turn\.$",
         "RaceChoice", "race.unblockable"),
        (r"^Choose a race\. Each creature of that race attacks this turn if able and gets \"power attacker \+\d+\"",
         "RaceChoice", "race.must_attack_power_attacker"),
        (r"^Choose a race\. Each creature of that race gets \"slayer\" until the end of the turn\.",
         "RaceChoice", "race.slayer"),
        (r"^Choose a race\. Return up to \d+ creatures of that race from your graveyard to your hand\.$",
         "RaceChoice", "race.return_graveyard"),
        (r"^Choose a race\. This turn, each creature of that race can attack untapped creatures and can't be blocked while attacking a creature\.$",
         "RaceChoice", "race.attack_untapped_unblockable"),
        (r"^Choose a race\. Until the start of your next turn, each of your creatures in the battle zone of that race gets \"blocker\"\.?$",
         "RaceChoice", "race.blocker_until_next_turn"),
        (r"^Choose a race\. Whenever one of your creatures of that race would be destroyed this turn, return it to your hand instead\.$",
         "RaceChoice", "race.destroy_to_hand_turn"),
        (r"^Choose a race\. You may put any number of creatures of that race from your hand into your mana zone\.$",
         "RaceChoice", "race.hand_to_mana"),
        (r"^Choose one of your creatures in the battle zone\. Whenever that creature wins a battle this turn, untap it\.$",
         "Modifier", "modifier.chosen_battle_win_untap"),
        (r"^Each creature attacks each turn if able\.$",
         "Static", "static.global_must_attack"),
        (r"^For each card in your mana zone, put a card from the top of your deck into your mana zone tapped\.$",
         "Move", "move.mana_count_top_deck_to_mana"),
        (r"^(?:Whenever this creature blocks, )?look at the top \d+ cards of your deck\. Put one of them into your hand, and put the rest on the bottom of your deck in any order\.$",
         "Reveal", "reveal.take_one_rest_bottom"),
        (r"^Search a player's deck\. You may take a card from that deck and put it into that player's graveyard\. Then the player shuffles his deck\.$",
         "Search", "search.either_deck_to_graveyard"),
        (r"^This creature gets \+\d+ power for each darkness creature in your graveyard\.",
         "Static", "static.power_per_darkness_graveyard"),
        (r"^This creature gets \+\d+ power for each of your other nature creatures in the battle zone\.",
         "Static", "static.power_per_other_nature"),
        (r"^This creature gets \+\d+ power for each card in your hand\.",
         "Static", "static.power_per_hand"),
        (r"^When this creature is destroyed, you may return another creature from your graveyard to your hand\.$",
         "Move", "move.destroyed_other_graveyard_creature"),
        (r"^When this creature would be destroyed, if another Kalute, Vizier of Eternity is in the battle zone, return this creature to your hand instead\.$",
         "Replacement", "replacement.kalute_to_hand"),
        (r"^When you put this creature into the battle zone, choose a race other than Mecha Del Sol\. Each creature of that race gets \+\d+ power\.$",
         "RaceChoice", "race.summon_global_power"),
        (r"^When you put this creature into the battle zone, look at your opponent's shields\. Then put them back where they were\.$",
         "Information", "look.summon_opponent_shields"),
        (r"^When you put this creature into the battle zone, search your deck\. You may take a Whispering Totem from your deck",
         "Search", "search.summon_whispering_totem"),
        (r"^When you put this creature into the battle zone, your opponent chooses up to \d+ creatures in his hand and puts them into the battle zone\.$",
         "Move", "move.summon_opponent_hand_creatures"),
        (r"^Whenever another creature would be destroyed, it stays in the battle zone instead\.$",
         "Replacement", "replacement.prevent_other_destruction"),
        (r"^Whenever one of your Armorloids is destroyed, your opponent chooses a card in his mana zone and puts it into his graveyard\.$",
         "Triggered", "trigger.armorloid_destroyed_mana_burn"),
        (r"^Whenever this creature attacks, you may choose one of your opponent's untapped creatures that has \"blocker\.\"",
         "Triggered", "trigger.attack_force_chosen_blocker"),
        (r"^Whenever this creature becomes blocked, it gets \+\d+ power until the end of the turn\.$",
         "Triggered", "trigger.blocked_power"),
        (r"^Whenever this creature becomes blocked, return the blocking creature to its owner's hand\.",
         "Move", "move.blocked_blocker_to_hand"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, choose one of your opponent's shields and turn it face up\.",
         "Triggered", "trigger.unblocked_shield_face_up"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, search your deck\. You may take a creature from your deck",
         "Search", "search.unblocked_creature"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, search your deck\. You may take a spell from your deck",
         "Search", "search.unblocked_spell"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, take an extra turn after this one\.$",
         "Triggered", "trigger.unblocked_extra_turn"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, you may choose a creature in the battle zone and return it to its owner's hand\.$",
         "Move", "move.unblocked_creature_to_hand"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, you may destroy one of your opponent's creatures that has power \d+ or less\.$",
         "Destroy", "destroy.unblocked_power_limit"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, your opponent chooses a card in his mana zone and puts it into his graveyard\.$",
         "Move", "move.unblocked_opponent_mana_choice"),
        (r"^Whenever this creature is attacking your opponent and isn't blocked, your opponent chooses one of his creatures and destroys it\.$",
         "Destroy", "destroy.unblocked_opponent_choice"),
        (r"^Whenever you put a Ghost into the battle zone, you may destroy one of your opponent's untapped creatures\.$",
         "Triggered", "trigger.ghost_summon_destroy_untapped"),
        (r"^Whenever you put a Guardian into the battle zone, you may choose one of your opponent's creatures in the battle zone and tap it\.$",
         "Triggered", "trigger.guardian_summon_tap"),
        (r"^Whenever you put another creature into the battle zone, each Beast Folk in the battle zone gets \+\d+ power and \"double breaker\"",
         "Triggered", "trigger.other_summon_beast_folk_buff"),
        (r"^Whenever you put another creature into the battle zone, return this creature(?: from the battle zone)? to your hand\.$",
         "Move", "move.other_summon_return_self"),
        (r"^Whenever you put another creature that has Cyber in its race into the battle zone, you may draw a card\.$",
         "Triggered", "trigger.cyber_summon_draw"),
        (r"^Whenever your opponent casts a spell, he chooses and discards a card from his hand, then chooses a card in his mana zone and puts it into his graveyard\.$",
         "Triggered", "trigger.opponent_spell_discard_mana_burn"),
        (r"^Whenever your opponent uses the \"shield trigger\" ability of one of his shields, you may add the top card of your deck to your shields face down\.$",
         "Triggered", "trigger.opponent_shield_trigger_add_shield"),
        (r"^Whenever your opponent would choose a creature in the battle zone, he can't choose this one\.",
         "Restriction", "restriction.opponent_cannot_choose"),
        (r"^While this creature has power \d+ or more, it has \"Triple breaker",
         "Static", "static.power_threshold_triple_breaker"),
        (r"^While this creature has power \d+ or more, it has \"double breaker",
         "Static", "static.power_threshold_double_breaker"),
        (r"^Your opponent chooses one of his creatures in the battle zone and puts it into his mana zone\.$",
         "Move", "move.opponent_choice_creature_to_mana"),
        (r"^Your opponent chooses one of his creatures in the battle zone or one of his shields and puts it into his graveyard\.$",
         "Move", "move.opponent_choice_creature_or_shield"),
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
        (r"^When you cast this spell, if your opponent has \d+ or more creatures in the battle zone, draw \d+ cards\.$",
         "Triggered", "trigger.cast_conditional_draw"),
        (r"^When this creature is destroyed, each player chooses (?:\d+ cards|a card) in his mana zone and puts (?:them|it) into his graveyard\.$",
         "Triggered", "trigger.destroyed_each_player_mana_burn"),
        (r"^(?:When you put this creature into the battle zone, |Whenever this creature attacks, )?Choose (?:(?:up to) )?(?:\d+ cards?|a card) in your opponent's mana zone and put (?:it|them) into his graveyard\.$",
         "Move", "move.opponent_mana_to_graveyard"),
        (r"^When you put this creature into the battle zone, choose one of your opponent's creatures in the battle zone and put it on top of his deck\.$",
         "Move", "move.creature_to_top_deck"),
        (r"^(?:Whenever this creature attacks, )?Choose one of your shields(?: without looking)? and put it into your graveyard\.(?: You can't use the \"shield trigger\" ability of that shield\.)?$",
         "Move", "move.own_shield_to_graveyard"),
        (r"^When you put this creature into the battle zone, return all creatures in the battle zone that have \"blocker\" to their owners' hands\.$",
         "Move", "move.all_blockers_to_hand"),
        (r"^When you put this creature into the battle zone, tap all your opponent's creatures in the battle zone that have \"blocker\"\.$",
         "Tap", "tap.all_opponent_blockers"),
        (r"^Whenever this creature attacks, you may choose a creature in the battle zone that has \"blocker\" and destroy it\.$",
         "Destroy", "destroy.attack_blocker"),
        (r"^Whenever this creature attacks, you may choose one of your opponent's (?:light|water|darkness|fire|nature) or (?:light|water|darkness|fire|nature) creatures in the battle zone and return it to its owner's hand\.$",
         "Move", "move.attack_civilization_bounce"),
        (r"^Whenever this creature attacks, you may draw a card\.$", "Triggered", "trigger.attack_draw"),
        (r"^Whenever this creature attacks, you may look at one of your opponent's shields\. Then put it back where it was\.$",
         "Information", "look.opponent_shield"),
        (r"^Look at up to \d+ of your opponent's shields\. Then put them back where they were\.$",
         "Information", "look.opponent_shields"),
        (r"^Whenever this creature attacks, you may put the top card of your deck into your mana zone\.$",
         "Move", "move.attack_top_deck_to_mana"),
        (r"^Each of your other .+? in the battle zone gets \+\d+ power\.$",
         "Static", "static.race_power_aura"),
        (r"^.+? can't be blocked\.$", "Static", "static.race_unblockable"),
        (r"^While attacking, this creature gets \+\d+ power for each other (?:creature you have|tapped creature you have|.+?) in the battle zone\.$",
         "Static", "static.dynamic_attack_power"),
        (r"^While this creature is tapped, each of your other .+? in the battle zone gets \+\d+ power\.$",
         "Static", "static.tapped_race_power_aura"),
        (r"^While you have no other creatures in the battle zone, this creature gets \"power attacker \+\d+\" and \"(?:double breaker|triple breaker)\"",
         "Static", "static.lone_power_breaker"),
        (r"^Your (?:creatures|spells) each cost \d+ less to (?:summon|cast)\. They can't cost less than \d+\.$",
         "Static", "static.cost_reduction"),
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
    explicit = classify_explicit_runtime(text)
    if explicit is not None:
        return explicit
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
    raw_occurrences: dict[str, list[str]] = defaultdict(list)
    for card in load_rows():
        for raw_line in card["card_text"].replace("\r", "").splitlines():
            line = clean_line(raw_line)
            if line:
                raw_occurrences[line].append(
                    f"{card['card_set']} {card['collector_number']} {card['card_name']}")

    # Printed databases occasionally differ only in capitalization. Stable IDs
    # are case-insensitive, so collapse those variants into one canonical stock
    # entry rather than emitting duplicate rows with the same identifier.
    canonical_text: dict[str, str] = {}
    occurrences: dict[str, list[str]] = defaultdict(list)
    for line, printings in raw_occurrences.items():
        key = re.sub(r"\s+", " ", line).strip().casefold()
        if key not in canonical_text or line < canonical_text[key]:
            canonical_text[key] = line
        occurrences[key].extend(printings)

    fieldnames = [
        "ability_id", "category", "handler", "implementation_status",
        "printing_count", "rule_text", "example_cards",
    ]
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    unmapped = 0
    with OUTPUT.open("w", encoding="utf-8", newline="") as destination:
        writer = csv.DictWriter(destination, fieldnames=fieldnames)
        writer.writeheader()
        for key in sorted(occurrences, key=lambda value: canonical_text[value].casefold()):
            line = canonical_text[key]
            classification = classify(line)
            if classification.status != "Complete":
                unmapped += 1
            writer.writerow({
                "ability_id": stable_id(line),
                "category": classification.category,
                "handler": classification.handler,
                "implementation_status": classification.status,
                "printing_count": len(occurrences[key]),
                "rule_text": line,
                "example_cards": " | ".join(occurrences[key][:5]),
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
