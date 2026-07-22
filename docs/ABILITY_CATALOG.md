# Implemented ability catalog

This is the generated index of executable ability handlers for the original English TCG pool.
The canonical CSV ledger contains **723 distinct printed rules lines**, all marked `Complete`,
backed by **485 runtime handlers** across **916 card printings**.

The exact rule text, stable ability ID, printing count, implementation status, and example cards are in
[`Assets/Resources/CardGame/Data/Tcg/ability_registry.csv`](../Assets/Resources/CardGame/Data/Tcg/ability_registry.csv).
Do not edit either generated index by hand; regenerate them with:

```bash
python tools/rules/generate_rule_inventory.py --require-complete
python tools/rules/generate_ability_stock.py
python tools/rules/generate_tcg_catalog.py
```

## Coverage by category

| Category | Printed rule lines |
|---|---:|
| Compound | 19 |
| Delayed | 6 |
| Destroy | 38 |
| Discard | 16 |
| Effect | 3 |
| Evolution | 43 |
| Explicit | 158 |
| Information | 10 |
| Keyword | 36 |
| Modifier | 34 |
| Move | 98 |
| RaceChoice | 11 |
| Replacement | 21 |
| Restriction | 33 |
| Reveal | 4 |
| Rule | 2 |
| Search | 21 |
| Spell | 12 |
| Static | 88 |
| Tap | 11 |
| Triggered | 59 |

## Runtime handler stock

| Handler | Category | Rule variants | Example printed rule |
|---|---|---:|---|
| `compound.attack_each_player_mana_burn` | Compound | 1 | Whenever this creature attacks, put a non-fire card from your mana zone into your graveyard. Then your opponent chooses a non-fire card in his mana zone and puts it into his graveyard. |
| `compound.attack_mana_cost_power_breaker` | Compound | 1 | Whenever this creature attacks, you may put a card from your mana zone into your graveyard. If you do, this creature gets "power attacker +3000" and "double breaker" until the end of the turn. (A creature that has "power attacker +3000" and "double breaker" gets +3000 power while attacking and breaks 2 shields.) |
| `compound.attack_sacrifice_power_breaker` | Compound | 1 | Whenever this creature attacks, you may destroy one of your other creatures. If you do, this creature gets +2000 power and "double breaker" until the end of the turn. (A creature that has "double breaker" breaks 2 shields.) |
| `compound.bounce_own_then_opponent` | Compound | 1 | Return any number of your creatures from the battle zone to your hand. Then you may choose that many of your opponent's creatures in the battle zone and return them to your opponent's hand. |
| `compound.bounce_then_dragon_draw` | Compound | 1 | Choose a creature in the battle zone and return it to its owner's hand. If it has Dragon in its race, you may draw a card. |
| `compound.destroy_each_player` | Compound | 1 | When you put this creature into the battle zone, destroy one of your creatures. Then your opponent chooses one of his creatures and destroys it. |
| `compound.destroy_then_draw` | Compound | 1 | Destroy any number of your creatures. Then draw that many cards. |
| `compound.discard_then_grant_breaker` | Compound | 1 | Discard any number of cards from your hand. Then choose the same number of your creatures in the battle zone. Each of those creatures gets "double breaker" until the end of the turn. (A creature that has "double breaker" breaks 2 shields.) |
| `compound.each_player_refresh_hand` | Compound | 1 | Each player counts the cards in his hand, shuffles those cards into his deck, then draws that many cards. |
| `compound.mana_to_graveyard_then_draw` | Compound | 1 | Put any number of cards from your mana zone into your graveyard. Then draw that many cards. |
| `compound.sacrifice_then_return_mana_creature` | Compound | 1 | You may destroy one of your darkness creatures. If you do, return a creature from your mana zone to your hand. |
| `compound.summon_each_player_mana_bounce` | Compound | 2 | When you put this creature into the battle zone, return 2 cards from your mana zone to your hand. Then your opponent chooses 2 cards in his mana zone and returns them to his hand. |
| `compound.summon_refresh_hand` | Compound | 1 | When you put this creature into the battle zone, count the cards in your hand, shuffle those cards into your deck, then draw that many cards. |
| `compound.summon_shield_swap` | Compound | 1 | When you put this creature into the battle zone, you may add a card from your hand to your shields face down. If you do, choose one of your shields and put it into your hand. You can't use the "shield trigger" ability of that shield. |
| `compound.summon_shield_swap_two` | Compound | 1 | When you put this creature into the battle zone, you may add up to 2 cards from your hand to your shields face down. If you do, choose the same number of your shields and put them into your hand. You can't use the "shield trigger" ability of those shields. |
| `compound.tap_summon_temporary_dragon` | Compound | 1 | Search your deck. You may take a creature that has Dragon in its race from your deck and put it into the battle zone. Then shuffle your deck. That creature has "speed attacker." At the end of the turn, destroy it. |
| `compound.tap_then_dragon_shield` | Compound | 1 | Choose one of your opponent's creatures in the battle zone and tap it. If it has Dragon in its race, add the top card of your deck to your shields face down. |
| `compound.tap_then_unblockable` | Compound | 1 | Choose one of your opponent's creatures in the battle zone and tap it. Then you may choose one of your creatures in the battle zone. If you do, it can't be blocked this turn. |
| `delayed.after_attack_draw_per_shield` | Delayed | 1 | Whenever any of your creatures finishes attacking this turn, you may draw 2 cards for each shield it broke. |
| `delayed.blocked_creature_slayer` | Delayed | 1 | Whenever any of your creatures becomes blocked this turn, it gets "slayer" until the end of the turn. (When a creature that has "slayer" loses a battle, destroy the other creature.) |
| `delayed.blocked_nature_break_shield` | Delayed | 1 | This turn, whenever any of your nature creatures is attacking your opponent and becomes blocked, it breaks one of his shields. (If a creature has "double breaker" or "triple breaker," it still breaks only one shield.) |
| `delayed.chosen_untap_end_turn` | Delayed | 1 | Choose up to 2 of your creatures in the battle zone. At the end of the turn, you may untap them. |
| `delayed.end_turn_search_per_shield` | Delayed | 1 | At the end of this turn, search your deck. For each of your opponent's shields your creatures broke this turn, you may take a creature from your deck, show it to your opponent, and put it into your hand. Then shuffle your deck. |
| `delayed.force_opponent_attacks` | Delayed | 1 | During your opponent's next turn, each of his creatures attacks if able. |
| `destroy.attack_blocker` | Destroy | 1 | Whenever this creature attacks, you may choose a creature in the battle zone that has "blocker" and destroy it. |
| `destroy.opponent_blocker_curly` | Destroy | 1 | Destroy one of your opponent’s creatures that has “blocker.” |
| `destroy.opponent_choice` | Destroy | 2 | When you put this creature into the battle zone, your opponent chooses one of his creatures and destroys it. |
| `destroy.required_or_source` | Destroy | 1 | When you put this creature into the battle zone, destroy 2 of your creatures or destroy this creature. |
| `destroy.selected_or_all` | Destroy | 25 | Destroy a creature that has power 6000 or less. |
| `destroy.source` | Destroy | 1 | When this creature wins a battle, destroy it. |
| `destroy.summon_all_except_civilization` | Destroy | 1 | When you put this creature into the battle zone, destroy all creatures except darkness creatures. |
| `destroy.summon_civilization_power_limit` | Destroy | 1 | When you put this creature into the battle zone, destroy all light creatures that have power 4000 or less. |
| `destroy.total_power_budget` | Destroy | 1 | Destroy any number of your opponent's creatures that have total power 8000 or less. |
| `destroy.turbo_unblocked_creature` | Destroy | 1 | Whenever this creature is attacking your opponent and isn't blocked, you may destroy a creature. |
| `destroy.unblocked_opponent_choice` | Destroy | 1 | Whenever this creature is attacking your opponent and isn't blocked, your opponent chooses one of his creatures and destroys it. |
| `destroy.unblocked_power_limit` | Destroy | 1 | Whenever this creature is attacking your opponent and isn't blocked, you may destroy one of your opponent's creatures that has power 3000 or less. |
| `destroy.untapped_two_civilizations` | Destroy | 1 | Destroy one of your opponent's untapped light or untapped nature creatures. |
| `discard.destroyed_each_hand` | Discard | 1 | When this creature is destroyed, each player discards his hand. |
| `discard.filtered_opponent_hand` | Discard | 1 | Look at your opponent's hand. He discards all darkness spells from it. |
| `discard.look_and_choose` | Discard | 1 | Look at your opponent's hand and choose a card from it. Your opponent discards that card. |
| `discard.opponent_choice` | Discard | 2 | Whenever this creature attacks, your opponent chooses and discards a card from his hand. |
| `discard.opponent_hand` | Discard | 1 | Your opponent discards all cards from his hand. |
| `discard.opponent_per_civilization` | Discard | 1 | Your opponent chooses and discards a card from his hand for each light creature he has in the battle zone. |
| `discard.opponent_random` | Discard | 6 | When this creature is destroyed, your opponent discards a card at random from his hand. |
| `discard.summon_per_other_civilization` | Discard | 1 | When you put this creature into the battle zone, your opponent discards a card at random from his hand for each other darkness creature you have in the battle zone. |
| `discard.turbo_attack_opponent_hand` | Discard | 1 | Whenever this creature attacks, your opponent discards his hand. |
| `effect.per_civilization_count` | Effect | 2 | For each nature creature you have in the battle zone, you may choose a card in your opponent's mana zone and put it into his graveyard. |
| `effect.untap_creatures` | Triggered | 3 | At the end of each of your turns, you may untap all your creatures in the battle zone. |
| `evolution.remove_top_card` | Evolution | 1 | When you put this creature into the battle zone, you may choose one of your opponent's evolution creatures in the battle zone and put the top card of that creature into your opponent's graveyard. |
| `evolution.remove_top_to_mana` | Evolution | 1 | Choose one of your opponent’s evolution creatures in the battle zone and put the top card of that creature into your opponent’s mana zone. |
| `evolution.single_race` | Evolution | 35 | Evolution—Put on one of your Angel Commands. |
| `evolution.universal_base` | Evolution | 1 | You can put an evolution creature of any race on this creature. |
| `evolution.vortex` | Evolution | 5 | Vortex evolution—Put on one of your Fire Birds and one of your Earth Dragons. |
| `keyword.attack_untapped` | Keyword | 1 | This creature can attack untapped creatures. |
| `keyword.blocker` | Keyword | 1 | Blocker (Whenever an opponent's creature attacks, you may tap this creature to stop the attack. Then the 2 creatures battle.) |
| `keyword.charger` | Keyword | 1 | Charger (After you cast this spell, put it into your mana zone instead of your graveyard.) |
| `keyword.charger_alternate_wording` | Keyword | 1 | After you cast this spell, put it into your mana zone instead of your graveyard. |
| `keyword.civilization_blocker` | Keyword | 3 | Darkness blocker (Whenever an opponent's darkness creature attacks, you may tap this creature to stop the attack. Then the 2 creatures battle.) |
| `keyword.crew_breaker` | Keyword | 3 | Crew breaker—Armored Dragon (This creature breaks one more shield for each of your other Armored Dragons in the battle zone.) |
| `keyword.double_breaker` | Keyword | 2 | Double breaker |
| `keyword.dragon_blocker` | Keyword | 1 | Dragon blocker (Whenever an opponent's creature that has Dragon in its race attacks, you may tap this creature to stop the attack. Then the 2 creatures battle.) |
| `keyword.must_attack` | Keyword | 1 | This creature attacks each turn if able. |
| `keyword.power_attacker` | Keyword | 6 | Power attacker +1000 (While attacking, this creature gets +1000 power.) |
| `keyword.shield_trigger` | Keyword | 1 | Shield trigger (When this card is put into your hand from your shield zone, you may play it immediately for no cost.) |
| `keyword.silent_skill` | Keyword | 1 | Silent skill (At the start of each of your turns, if this creature is tapped, you may keep it tapped and use its silent skill ability.) |
| `keyword.slayer` | Keyword | 3 | Nature and light slayer (Whenever this creature battles a nature or light creature, destroy the other creature after the battle.) |
| `keyword.speed_attacker` | Keyword | 1 | Speed attacker (This creature doesn't get summoning sickness.) |
| `keyword.stealth` | Keyword | 5 | Darkness stealth (This creature can't be blocked while your opponent has any darkness cards in his mana zone.) |
| `keyword.survivor` | Keyword | 1 | Survivor (Each of your Survivors has this creature's Survivor ability.) |
| `keyword.tap_ability` | Keyword | 1 | Instead of having this creature attack, you may tap it to use its tap ability. |
| `keyword.triple_breaker` | Keyword | 1 | Triple breaker (This creature breaks 3 shields.) |
| `keyword.turbo_rush` | Keyword | 1 | Turbo rush (If any of your other creatures broke any shields this turn, this creature gets its turbo rush ability until the end of the turn.) |
| `keyword.wave_striker` | Keyword | 1 | Wave striker (While 2 or more other creatures in the battle zone have "wave striker," this creature has its wave striker ability.) |
| `look.attack_opponent_hand_top_deck` | Information | 1 | Whenever this creature attacks, you may look at your opponent's hand and at the top card of his deck. |
| `look.attack_opponent_shields` | Information | 1 | Whenever this creature attacks, you may look at 2 of your opponent's shields. Then put them back where they were. |
| `look.chosen_shield` | Information | 1 | Choose a shield and look at it. Then put it back where it was. |
| `look.opponent_hand_and_shields` | Information | 1 | Look at your opponent's hand and shields. Then put the shields back where they were. |
| `look.opponent_shield` | Information | 1 | Whenever this creature attacks, you may look at one of your opponent's shields. Then put it back where it was. |
| `look.opponent_shields` | Information | 1 | Look at up to 3 of your opponent's shields. Then put them back where they were. |
| `look.own_shields` | Information | 1 | When you put this creature into the battle zone, you may look at your shields. Then put them back where they were. |
| `look.reorder_top_deck` | Information | 1 | Look at the top 3 cards of your deck. Then put them back in any order. |
| `look.summon_opponent_hand` | Information | 1 | When you put this creature into the battle zone, look at your opponent's hand. |
| `look.summon_opponent_shields` | Information | 1 | When you put this creature into the battle zone, look at your opponent's shields. Then put them back where they were. |
| `modifier.all_power_triple_breaker` | Modifier | 1 | Each of your creatures in the battle zone gets +8000 power and "triple breaker" until the end of the turn. (A creature that has "triple breaker" breaks 3 shields.) |
| `modifier.all_unblockable` | Modifier | 1 | Your creatures in the battle zone can't be blocked this turn. |
| `modifier.attack_untapped_this_turn` | Modifier | 1 | Each of your creatures in the battle zone that can attack creatures can attack untapped creatures this turn. |
| `modifier.attackable_untapped` | Modifier | 1 | Choose 1 of your opponent's untapped creatures in the battle zone. Your creatures can attack it this turn as though it were tapped. |
| `modifier.blocker_power` | Modifier | 1 | One of your creatures in the battle zone that has "blocker" gets +4000 power until the end of the turn. |
| `modifier.blocker_until_next_turn` | Modifier | 1 | Until the start of your next turn, each of your creatures in the battle zone gets "blocker" |
| `modifier.chosen_battle_win_untap` | Modifier | 1 | Choose one of your creatures in the battle zone. Whenever that creature wins a battle this turn, untap it. |
| `modifier.darkness_power_breaker_destroy` | Modifier | 1 | Until the end of the turn, each of your darkness creatures in the battle zone gets +4000 power and "double breaker." Whenever any of those creatures battles this turn, destroy it after the battle. |
| `modifier.dragonoid_to_armored_dragon` | Modifier | 1 | Until the end of the turn, each of your Dragonoids in the battle zone is an Armored Dragon in addition to its other races, gets +4000 power, and has "Double breaker (This creature breaks 2 shields)." |
| `modifier.fire_attack_untapped` | Modifier | 1 | Each of your fire creatures gets "This creature can attack untapped creatures" until the end of the turn. |
| `modifier.grant_dynamic_mana_power_attacker` | Modifier | 1 | Until the end of the turn, each of your creatures in the battle zone gets "While attacking, this creature gets +1000 power for each darkness card in your mana zone." |
| `modifier.ignore_attack_restrictions` | Modifier | 1 | This turn, ignore any effects that would prevent your creatures from attacking your opponent. (For example, ignore summoning sickness and card effects that say "This creature can't attack" or "This creature can't attack players". Your creatures can't attack creatures this way.) |
| `modifier.power_per_civilization` | Modifier | 1 | Each of your creatures in the battle zone gets +1000 power until the end of the turn for each light creature you have in the battle zone. |
| `modifier.single_unblockable_attack_override` | Modifier | 1 | Choose one of your creatures in the battle zone. This turn, it can't be blocked and you ignore any effects that would prevent that creature from attacking your opponent. (For example, ignore summoning sickness and card effects that say "This creature can't attack" or "This creature can't attack players." Your creatures can't attack creatures this way.) |
| `modifier.temporary_keyword` | Modifier | 7 | Each of your creatures in the battle zone gets "double breaker" until the end of the turn. (A creature that has "double breaker" breaks 2 shields.) |
| `modifier.temporary_power` | Modifier | 6 | Each of your creatures in the battle zone gets +1000 power until the end of the turn. |
| `modifier.temporary_power_attacker` | Modifier | 5 | Each of your creatures gets "power attacker +1000" until the end of the turn. (While attacking, a creature that has "power attacker +1000" gets +1000 power.) |
| `modifier.temporary_unblockable` | Modifier | 2 | Choose one of your creatures in the battle zone. It can't be blocked this turn. |
| `move.add_shield` | Move | 6 | Add a card from your hand to your shields face down. |
| `move.all_blockers_to_hand` | Move | 1 | When you put this creature into the battle zone, return all creatures in the battle zone that have "blocker" to their owners' hands. |
| `move.any_own_shields` | Move | 2 | Choose any number of your shields and put them into your hand. You can't use the "shield trigger" abilities of those shields. |
| `move.attack_all_mana_creatures_to_hand` | Move | 1 | Whenever this creature attacks, return all creatures from your mana zone to your hand. |
| `move.attack_civilization_bounce` | Move | 1 | Whenever this creature attacks, you may choose one of your opponent's fire or nature creatures in the battle zone and return it to its owner's hand. |
| `move.attack_filtered_graveyard` | Move | 3 | Whenever this creature attacks, you may put a nature card from your graveyard into your mana zone. |
| `move.attack_opponent_mana_burn` | Move | 1 | Whenever this creature attacks, choose up to 2 cards in your opponent's mana zone. Your opponent puts those cards into his graveyard. |
| `move.attack_opponent_mana_to_hand` | Move | 1 | Whenever this creature attacks, you may choose a card in your opponent's mana zone and return it to his hand. |
| `move.attack_small_creature_to_hand` | Move | 1 | Whenever this creature attacks, you may choose a creature in the battle zone that has power 2000 or less and return it to its owner's hand. (Return the creature to its owner's hand before your opponent can block with it.) |
| `move.attack_top_deck_to_mana` | Move | 1 | Whenever this creature attacks, you may put the top card of your deck into your mana zone. |
| `move.blocked_blocker_to_hand` | Move | 1 | Whenever this creature becomes blocked, return the blocking creature to its owner's hand. (This creature stays tapped.) |
| `move.blocked_top_deck_to_mana` | Move | 1 | Whenever this creature becomes blocked, you may put the top card of your deck into your mana zone. |
| `move.conditional_add_shield` | Move | 1 | If your opponent has more shields than you do, add the top card of your deck to your shields face down. |
| `move.conditional_mana_acceleration` | Move | 1 | If your opponent has more cards in his mana zone than you have in yours, put the top 2 cards of your deck into your mana zone. |
| `move.creature_to_top_deck` | Move | 1 | When you put this creature into the battle zone, choose one of your opponent's creatures in the battle zone and put it on top of his deck. |
| `move.darkness_creature_graveyard_to_hand` | Move | 1 | Return a darkness creature from your graveyard to your hand. |
| `move.destroyed_dragon_hand_to_battle` | Move | 1 | When this creature is destroyed, you may put a creature that has Dragon in its race from your hand into the battle zone. |
| `move.destroyed_named_mana_to_battle` | Move | 2 | When this creature is destroyed, you may choose an Ambush Scorpion in your mana zone and put it into the battle zone. |
| `move.destroyed_other_graveyard_creature` | Move | 1 | When this creature is destroyed, you may return another creature from your graveyard to your hand. |
| `move.destroyed_shield_to_hand` | Move | 1 | When this creature is destroyed, you may choose one of your shields and put it into your hand. You can't use the "shield trigger" ability of that shield. |
| `move.each_player_all_mana_to_hand` | Move | 1 | Each player returns all cards from his mana zone to his hand. |
| `move.each_player_mana_burn_one` | Move | 1 | Each player puts a card from his mana zone into his graveyard. |
| `move.filtered_all` | Move | 1 | When you put this creature into the battle zone, return all creatures in the battle zone that have power 2000 or less to its owner's hand. |
| `move.mana_count_top_deck_to_mana` | Move | 1 | For each card in your mana zone, put a card from the top of your deck into your mana zone tapped. |
| `move.mana_creature_cost_at_least` | Move | 1 | Return a creature that costs 6 or more from your mana zone to your hand. |
| `move.opponent_choice_battle_or_mana` | Move | 1 | Your opponent chooses one of his creatures in the battle zone or a card in his mana zone and puts it into his graveyard. |
| `move.opponent_choice_creature_or_shield` | Move | 1 | Your opponent chooses one of his creatures in the battle zone or one of his shields and puts it into his graveyard. |
| `move.opponent_choice_creature_to_mana` | Move | 2 | When you put this creature into the battle zone, your opponent chooses one of his creatures in the battle zone and puts it into his mana zone. |
| `move.opponent_civilization_to_mana` | Move | 1 | Choose one of your opponent's water or darkness creatures in the battle zone. Your opponent puts that creature into his mana zone. |
| `move.opponent_mana_to_graveyard` | Move | 3 | Choose a card in your opponent's mana zone and put it into his graveyard. |
| `move.opponent_mana_to_hand` | Move | 1 | Choose a card in your opponent's mana zone and return it to his hand. |
| `move.opponent_shields_to_graveyard` | Move | 1 | Choose up to 3 of your opponent's shields and put them into his graveyard. |
| `move.other_summon_return_self` | Move | 2 | Whenever you put another creature into the battle zone, return this creature from the battle zone to your hand. |
| `move.own_shield_to_graveyard` | Move | 4 | Choose one of your shields and put it into your graveyard. |
| `move.per_civilization_count` | Move | 2 | For each darkness creature you have in the battle zone, you may choose one of your opponent's creatures and return it to his hand. |
| `move.return_creature_hand` | Move | 1 | Choose a creature in the battle zone and return it to its owner's hand. |
| `move.selected` | Move | 31 | Choose one of your opponent's creatures in the battle zone and put it into his mana zone. |
| `move.summon_all_filtered_graveyard_to_hand` | Move | 2 | When you put this creature into the battle zone, you may return all Angel Commands and all Demon Commands from your graveyard to your hand. |
| `move.summon_all_other_creatures_to_hand` | Move | 1 | When you put this creature into the battle zone, return all other creatures from the battle zone to their owners' hands. |
| `move.summon_conditional_mana_acceleration` | Move | 1 | When you put this creature into the battle zone, if your opponent has more cards in his mana zone than you have in yours, put the top card of your deck into your mana zone. |
| `move.summon_filtered_graveyard_to_hand` | Move | 3 | When you put this creature into the battle zone, you may return a Living Dead from your graveyard to your hand. |
| `move.summon_opponent_hand_creatures` | Move | 1 | When you put this creature into the battle zone, your opponent chooses up to 2 creatures in his hand and puts them into the battle zone. |
| `move.summon_opponent_mana_to_hand` | Move | 1 | When you put this creature into the battle zone, choose a card in your opponent's mana zone and return it to his hand. |
| `move.summon_opponent_optional_hand_creature` | Move | 1 | When you put this creature into the battle zone, your opponent may choose a creature in his hand and put it into the battle zone. |
| `move.summon_shield_to_hand` | Move | 1 | When you put this creature into the battle zone, choose one of your shields and return it to your hand. You can't use the "shield trigger" ability of that shield. |
| `move.summon_shield_to_hand_no_trigger` | Move | 1 | When you put this creature into the battle zone, choose one of your shields and put it into your hand. You can't use the "shield trigger" ability of that shield. |
| `move.unblocked_creature_to_hand` | Move | 1 | Whenever this creature is attacking your opponent and isn't blocked, you may choose a creature in the battle zone and return it to its owner's hand. |
| `move.unblocked_opponent_mana_choice` | Move | 1 | Whenever this creature is attacking your opponent and isn't blocked, your opponent chooses a card in his mana zone and puts it into his graveyard. |
| `permission.attack_untapped_civilization` | Restriction | 3 | This creature can attack untapped darkness creatures. |
| `race.attack_untapped_unblockable` | RaceChoice | 1 | Choose a race. This turn, each creature of that race can attack untapped creatures and can't be blocked while attacking a creature. |
| `race.blocker_until_next_turn` | RaceChoice | 1 | Choose a race. Until the start of your next turn, each of your creatures in the battle zone of that race gets "blocker". |
| `race.destroy_to_hand_turn` | RaceChoice | 1 | Choose a race. Whenever one of your creatures of that race would be destroyed this turn, return it to your hand instead. |
| `race.end_turn_untap` | RaceChoice | 1 | Choose a race. At the end of this turn, untap all creatures of that race in the battle zone. |
| `race.hand_to_mana` | RaceChoice | 1 | Choose a race. You may put any number of creatures of that race from your hand into your mana zone. |
| `race.must_attack_power_attacker` | RaceChoice | 1 | Choose a race. Each creature of that race attacks this turn if able and gets "power attacker +4000" until the end of the turn. (While attacking, a creature that has "power attacker +4000" gets +4000 power.) |
| `race.return_graveyard` | RaceChoice | 1 | Choose a race. Return up to 3 creatures of that race from your graveyard to your hand. |
| `race.slayer` | RaceChoice | 1 | Choose a race. Each creature of that race gets "slayer" until the end of the turn. (Whenever a creature that has "slayer" battles, destroy the other creature after the battle.) |
| `race.summon_global_power` | RaceChoice | 1 | When you put this creature into the battle zone, choose a race other than Mecha Del Sol. Each creature of that race gets +4000 power. |
| `race.unblockable` | RaceChoice | 1 | Choose a race. Creatures of that race can't be blocked this turn. |
| `race.unblockable_by_power` | RaceChoice | 1 | Choose a race. Creatures of that race can't be blocked by creatures that have power 3000 or less this turn. |
| `replacement.aura_darkness_to_hand` | Replacement | 1 | Whenever another of your darkness creatures would be put into your graveyard from the battle zone, you may return it to your hand instead. |
| `replacement.aura_nature_to_mana` | Replacement | 1 | Whenever another of your nature creatures would be put into your graveyard from the battle zone, put it into your mana zone instead. |
| `replacement.defender_chooses_shield` | Replacement | 1 | Whenever an opponent's creature would break a shield, you choose the shield instead of your opponent. |
| `replacement.destroy_move` | Replacement | 3 | When this creature would be destroyed, put it into your hand instead. |
| `replacement.destroy_source_for_ally` | Replacement | 1 | When one of your Armored Dragons would be destroyed, you may destroy this creature instead. |
| `replacement.destroy_to_hand` | Replacement | 1 | When this creature would be destroyed, you may return it to your hand instead. |
| `replacement.destroy_to_hand_discard` | Replacement | 1 | When this creature would be put into your graveyard from the battle zone, you may return it to your hand instead. If you do, discard a card from your hand. |
| `replacement.destroy_to_hand_discard_card` | Replacement | 1 | When this creature would be destroyed, you may return it to your hand instead. If you do, put a card from your hand into your graveyard. |
| `replacement.destroy_to_mana_return_dragon` | Replacement | 1 | When this creature would be destroyed, if you have a creature that has Dragon in its race in your mana zone, put this creature into your mana zone instead of destroying it. Then return a creature that has Dragon in its race from your mana zone to your hand. |
| `replacement.destroy_to_shield` | Replacement | 2 | When this creature would be destroyed, add it to your shields face down instead. |
| `replacement.kalute_to_hand` | Replacement | 1 | When this creature would be destroyed, if another Kalute, Vizier of Eternity is in the battle zone, return this creature to your hand instead. |
| `replacement.opponent_discard_to_battle` | Replacement | 1 | During your opponent's turn, if this creature would be discarded from your hand, put it into the battle zone instead. |
| `replacement.opponent_mana_burn` | Replacement | 1 | Whenever your opponent causes a card to be put into your graveyard from your mana zone, you may return that card to your mana zone. |
| `replacement.prevent_other_destruction` | Replacement | 1 | Whenever another creature would be destroyed, it stays in the battle zone instead. |
| `replacement.shield_break_destroy_source` | Replacement | 1 | When one of your shields would be broken, you may destroy this creature instead. |
| `replacement.shield_break_to_graveyard` | Replacement | 1 | Whenever this creature would break a shield, your opponent puts that shield into his graveyard instead. |
| `replacement.shield_trigger_spell_to_hand` | Replacement | 1 | After you cast a spell by using its "shield trigger" ability, put it into your hand instead of your graveyard. |
| `replacement.turn_destroy_to_mana` | Replacement | 1 | Whenever any of your creatures would be destroyed this turn, put it into your mana zone instead. |
| `restriction.attack_only_blocker` | Restriction | 1 | This creature can attack only creatures that have "blocker." |
| `restriction.attack_race_if_able` | Restriction | 1 | Your opponent's attacking creatures attack Mystery Totems if able. |
| `restriction.attack_requires_hand` | Restriction | 1 | This creature can't attack if you have no cards in your hand. |
| `restriction.attacked_by_civilization` | Restriction | 2 | This creature can't be attacked by darkness creatures. |
| `restriction.attacked_by_dragon` | Restriction | 1 | This creature can't be attacked by any creature that has Dragon in its race. |
| `restriction.attacked_by_two_civilizations` | Restriction | 1 | This creature can't be attacked by fire or nature creatures. |
| `restriction.blocked_by_civilization` | Restriction | 2 | This creature can't be blocked by darkness creatures. |
| `restriction.blocker_power_limit` | Restriction | 4 | This creature can't be blocked by any creature that has power 3000 or less. |
| `restriction.blocker_power_minimum` | Restriction | 1 | This creature can't be blocked by creatures that have power 4000 or more. |
| `restriction.cannot_attack` | Restriction | 1 | This creature can't attack. |
| `restriction.cannot_attack_creatures` | Restriction | 1 | This creature can't attack creatures. |
| `restriction.cannot_attack_players` | Restriction | 1 | This creature can't attack players. |
| `restriction.cannot_be_attacked` | Restriction | 1 | This creature can't be attacked. |
| `restriction.cast_mono_mana` | Restriction | 1 | You can cast this spell only if all the cards in your mana zone are fire cards. |
| `restriction.comparative_creature_count` | Restriction | 1 | This creature can't attack while your opponent has more creatures in the battle zone than you do. |
| `restriction.global_attack_power_minimum` | Restriction | 1 | Creatures that have power 3000 or more can't attack. (Creatures that have power less than 3000 and get extra power while attacking can still attack.) |
| `restriction.global_shield_trigger_civilization` | Restriction | 2 | Players can't use the "shield trigger" abilities of darkness cards. |
| `restriction.global_spell_civilization` | Restriction | 1 | Players can't cast spells other than light spells. |
| `restriction.opponent_cannot_choose` | Restriction | 1 | Whenever your opponent would choose a creature in the battle zone, he can't choose this one. (It can still be attacked and blocked.) |
| `restriction.opponent_no_shields` | Restriction | 1 | While your opponent has no shields, this creature can't attack. |
| `restriction.other_untapped_creature` | Restriction | 1 | While you have any other untapped creatures in the battle zone, this creature can't attack. |
| `restriction.summon_after_spell` | Restriction | 1 | You can summon this creature only if you have cast a spell this turn. |
| `restriction.tapped_disable_shield_trigger` | Restriction | 1 | While this creature is tapped, your opponent can't use the "shield trigger" ability of his shields. |
| `restriction.unblockable` | Restriction | 1 | This creature can't be blocked. |
| `reveal.partition_by_civilization` | Reveal | 1 | Reveal the top 4 cards of your deck. Put all water cards from among them into your hand and the rest into your graveyard. |
| `reveal.summon_partition_by_civilization` | Reveal | 1 | When you put this creature into the battle zone, you may reveal the top 3 cards of your deck. Put all light and darkness cards from the revealed cards into your hand, and put the rest into your graveyard. |
| `reveal.take_one_rest_bottom` | Reveal | 2 | Look at the top 4 cards of your deck. Put one of them into your hand, and put the rest on the bottom of your deck in any order. |
| `rule.multicivilization_mana_tapped` | Rule | 2 | (This creature is put into your mana zone tapped.) |
| `runtime.dm10.02fb8a7c775a` | Explicit | 1 | While attacking, this creature gets +2000 power for each of your other tapped creatures in the battle zone. |
| `runtime.dm10.039e37e93d40` | Explicit | 1 | When you put this creature into the battle zone, destroy one of your opponent's creatures that has "blocker." Then your opponent discards a card at random from his hand. |
| `runtime.dm10.04e6af16f7a6` | Explicit | 1 | This creature gets +3000 power for each civilization in your mana zone. |
| `runtime.dm10.10a0737b1d91` | Explicit | 1 | Choose a creature in your graveyard. Return all creatures that have that name from your graveyard to your hand. |
| `runtime.dm10.1137f70ec548` | Explicit | 1 | Whenever one of your creatures attacks, your opponent discards a card at random from their hand. |
| `runtime.dm10.1f30f97c2304` | Explicit | 1 | This creature gets +1000 power for each tapped card in your opponent's mana zone. |
| `runtime.dm10.2d5e0982b1e8` | Explicit | 1 | This creature gets +5000 power for each of your other creatures in the battle zone that has Dragon in its race. |
| `runtime.dm10.2efcf66a91d3` | Explicit | 1 | Your opponent can't tap this creature. |
| `runtime.dm10.2f649c060ad6` | Explicit | 1 | During your opponent's turn, each of your other creatures gets +2000 power. |
| `runtime.dm10.3588280db659` | Explicit | 1 | You may destroy a creature. If you do, its owner reveals cards from the top of his deck until he reveals a non-evolution creature. He puts that creature into the battle zone and puts the rest of those cards into his graveyard. |
| `runtime.dm10.3d83695b2c86` | Explicit | 1 | When you put this creature into the battle zone, look at up to 5 cards from the top of your deck and put them back in any order. |
| `runtime.dm10.3dc5b0462871` | Explicit | 1 | Whenever this creature would break one of your opponent's shields, your opponent shows that shield to you instead of putting it into his hand. If it's a spell that has "shield trigger," you cast it for no cost, then put it into your opponent's graveyard. If it's not, your opponent puts it into his hand. |
| `runtime.dm10.3e73c33ae6fe` | Explicit | 1 | While attacking, this creature gets +1000 power for each of your other tapped creatures in the battle zone. |
| `runtime.dm10.45b0db27aa54` | Explicit | 1 | When you put this creature into the battle zone, add the top card of your deck to your shields face down. Then look at one of your opponent's shields. |
| `runtime.dm10.45babb2fae53` | Explicit | 1 | This creature gets +2000 power for each shield you and your opponent have. |
| `runtime.dm10.508376e4c1b0` | Explicit | 1 | While this creature is tapped during your opponent's turn, each of his creatures attacks if able. |
| `runtime.dm10.509b7e8ce971` | Explicit | 1 | Each of your creatures in the battle zone that has "silent skill" has "speed attacker." (A creature that has "speed attacker" doesn't get summoning sickness.) |
| `runtime.dm10.50f1a753a7c4` | Explicit | 1 | When you put this creature into the battle zone, put all the cards from your hand into your mana zone. Then put that many cards from your mana zone into your hand. |
| `runtime.dm10.529570c56c3b` | Explicit | 1 | When you put this creature into the battle zone, put all your shields into your hand. (You can use the "shield trigger" abilities of those shields.) |
| `runtime.dm10.537f486c94ff` | Explicit | 1 | Each of your water creatures and nature creatures in the battle zone has "blocker." (Whenever one of your opponent's creatures attacks, you may tap a creature that has "blocker" to stop the attack. Then the 2 creatures battle.) |
| `runtime.dm10.5466092ff79a` | Explicit | 1 | You may destroy one of your creatures. If you do, choose a creature in your hand that costs the same as or less than the number of cards in your mana zone and put it into the battle zone. |
| `runtime.dm10.59d55050724d` | Explicit | 1 | When you put this creature into the battle zone, put all the cards from your mana zone into your hand and, at the same time, put all the cards from your hand into your mana zone tapped. |
| `runtime.dm10.5a32d85e2ca7` | Explicit | 1 | Choose one of your opponent's creatures in the battle zone. It gets "this creature attacks if able" until the start of your next turn. |
| `runtime.dm10.5a60fe15f9e9` | Explicit | 1 | When you put this creature into the battle zone, if your opponent has 2 or fewer shields, you may destroy one of your opponent's creatures that has power 3000 or less. |
| `runtime.dm10.60688e3e8931` | Explicit | 1 | Whenever one of your shields would be broken, you may discard 2 cards from your hand instead. |
| `runtime.dm10.66a85419305a` | Explicit | 1 | Choose a creature in the battle zone or a card in either player's mana zone and return it to its owner's hand. |
| `runtime.dm10.67696fadbfa3` | Explicit | 1 | When you put this creature into the battle zone, if you have 5 or more shields, add the top card of your deck to your shields face down. |
| `runtime.dm10.69edac3fa5cb` | Explicit | 1 | Each player puts all the cards from his mana zone into his hand and, at the same time, puts all the cards from his hand into his mana zone tapped. |
| `runtime.dm10.6a4d3c10d0a8` | Explicit | 1 | Each of your darkness creatures and nature creatures in the battle zone has "speed attacker." (A creature that has "speed attacker" doesn't get summoning sickness.) |
| `runtime.dm10.6e442669225f` | Explicit | 1 | Your creatures that have "silent skill" are put into the battle zone tapped. |
| `runtime.dm10.706450165566` | Explicit | 1 | This creature gets +2000 power for each tapped card in your opponent's mana zone. |
| `runtime.dm10.724cd9114009` | Explicit | 1 | When you put this creature into the battle zone, destroy all other creatures that have 6000 power. Take an extra turn after this one. You lose the game at the end of that turn. |
| `runtime.dm10.760aaa4f1e34` | Explicit | 1 | While battling a fire creature, this creature gets +4000 power. |
| `runtime.dm10.763c423d10f5` | Explicit | 1 | At the start of each of your opponent's turns, he chooses one of his creatures in the battle zone that can attack. That creature attacks that turn if able. |
| `runtime.dm10.7c694fb91399` | Explicit | 1 | When you put this creature into the battle zone, look at one of your opponent's shields. Then your opponent discards a card at random from his hand. |
| `runtime.dm10.87499fd2680f` | Explicit | 1 | This creature breaks 2 of your opponent's shields. |
| `runtime.dm10.89870b851802` | Explicit | 1 | When you put this creature into the battle zone, if you have 5 or more shields, untap all the cards in your mana zone. |
| `runtime.dm10.8f5d073f8cbd` | Explicit | 1 | This creature gets +1000 power for each of your other untapped creatures in the battle zone. |
| `runtime.dm10.944eecd59e86` | Explicit | 1 | Each player chooses one of his creatures in the battle zone. Tap the rest of the creatures in the battle zone. |
| `runtime.dm10.968efcf0ecee` | Explicit | 1 | Whenever an opponent's creature attacks, this creature blocks if able. |
| `runtime.dm10.9a02dde1dd79` | Explicit | 1 | When you put this creature into the battle zone, choose one of your opponent's shields and put it into his graveyard. |
| `runtime.dm10.a02724bc6e05` | Explicit | 1 | When you put this creature into the battle zone, destroy one of your creatures and put a card from your mana zone into your graveyard. Then your opponent chooses and destroys one of his creatures and chooses a card in his mana zone and puts it into his graveyard. |
| `runtime.dm10.a3eef889d1fa` | Explicit | 1 | Each of your opponent's creatures gets "This creature attacks if able" until the start of your next turn. |
| `runtime.dm10.af3b7fb0ae59` | Explicit | 1 | Whenever this creature blocks, choose one of your creatures in the battle zone. Untap it after the battle. |
| `runtime.dm10.b090002296e7` | Explicit | 1 | While attacking, this creature gets +1000 power for each tapped card in your mana zone. |
| `runtime.dm10.b2776e5ffaa3` | Explicit | 1 | Each of your water creatures and fire creatures in the battle zone has "slayer." (Whenever a creature that has "slayer" battles, destroy the other creature after the battle.) |
| `runtime.dm10.ba74c04f5782` | Explicit | 1 | Whenever one of your creatures that has "silent skill" would be destroyed, put it into your hand instead. |
| `runtime.dm10.bbf1ce2377fa` | Explicit | 1 | When this creature would be discarded from your hand during your opponent's turn, you may put it into the battle zone instead. |
| `runtime.dm10.c09941a1577b` | Explicit | 1 | This creature breaks one of your opponent's shields. |
| `runtime.dm10.c40c64fb9751` | Explicit | 1 | While this creature is tapped, each of your other creatures has "power attacker +1500." (While attacking, a creature that has "power attacker +1500" gets +1500 power.) |
| `runtime.dm10.c57dd3445bfb` | Explicit | 1 | Whenever one of your creatures is attacked, you may discard a card from your hand. If you do, that creature gets +3000 power until the end of the turn. |
| `runtime.dm10.c5d0bb07f5b8` | Explicit | 1 | Destroy all creatures that have power 3000 or less. Then look at your opponent's hand. He discards all creatures from it that have power 3000 or less. |
| `runtime.dm10.d1ddc690fbd1` | Explicit | 1 | You may add a card from your hand to your shields face down. If you do, choose one of your shields and put it into your hand. You can't use the "shield trigger" ability of that shield. |
| `runtime.dm10.d4611e2dd26a` | Explicit | 1 | You may choose a creature in the battle zone and put it into its owner's mana zone. If you do, choose a non-evolution creature in that player's mana zone that costs the same as or less than the number of cards in that mana zone. That player puts that creature into the battle zone. |
| `runtime.dm10.d69c250a3f39` | Explicit | 1 | Destroy one of your opponent's creatures that has "blocker." |
| `runtime.dm10.dada9dcc0208` | Explicit | 1 | This creature gets +2000 power for each of your other untapped creatures in the battle zone. |
| `runtime.dm10.dbc1bb3ea510` | Explicit | 1 | When this creature would be destroyed, shuffle it into your deck instead. |
| `runtime.dm10.de866a06bb68` | Explicit | 1 | When you put this creature into the battle zone, return all tapped cards from your mana zone to your hand. |
| `runtime.dm10.e0703a917185` | Explicit | 1 | Your opponent chooses one of his creatures and destroys it. |
| `runtime.dm10.e5b3e2dbe227` | Explicit | 1 | Whenever you put a Cyber Virus into the battle zone, you may discard up to 2 cards from your hand. For each card you discard, choose a creature in the battle zone and return it to its owner's hand. |
| `runtime.dm10.e7492747c698` | Explicit | 1 | Put a card from your mana zone into your hand. Then put a card from your hand into your mana zone. |
| `runtime.dm10.e7f1ee92f108` | Explicit | 1 | While attacking, this creature gets +2000 power for each tapped card in your mana zone. |
| `runtime.dm10.ea6e77d8e787` | Explicit | 1 | Each of your light creatures and fire creatures has "power attacker +3000." (While attacking, a creature that has "power attacker +3000" gets +3000 power.) |
| `runtime.dm10.ec5e064f5916` | Explicit | 1 | This creature gets +1000 power for each water card in your opponent's mana zone. |
| `runtime.dm10.efa337e360e4` | Explicit | 1 | When you put this creature into the battle zone, look at your opponent's hand. You may choose a card in it and put it into his mana zone. If you do, choose a card in his mana zone and put it into his hand. |
| `runtime.dm10.efae386afd77` | Explicit | 1 | You can't untap the cards in your mana zone at the start of each of your turns. |
| `runtime.dm10.f2bb31d557ee` | Explicit | 1 | Whenever this creature attacks a creature that has power 6000 or more, this creature gets +9000 power until the end of the turn. |
| `runtime.dm10.f4dd87f2de78` | Explicit | 1 | When you put this creature into the battle zone, if your opponent has 3 or fewer shields, he discards a card at random from his hand. |
| `runtime.dm10.f5c5cc68e341` | Explicit | 1 | When you put this creature into the battle zone, put the top card of your deck into your mana zone. Then add the top card of your deck to your shields face down. |
| `runtime.dm10.f942b3e4c066` | Explicit | 1 | When you put this creature into the battle zone, destroy one of your opponent's creatures that has "blocker." Then put the top card of your deck into your mana zone. |
| `runtime.dm10.f9bf10cfb196` | Explicit | 1 | When you put this creature into the battle zone, return all the cards from your mana zone to your hand. |
| `runtime.dm10.fad9652ce5ac` | Explicit | 1 | Each player discards a card at random from his hand. |
| `runtime.dm10.fd4e40356e91` | Explicit | 1 | When you put this creature into the battle zone, choose one of your shields and put it into your hand. (You can use the "shield trigger" ability of that shield.) |
| `runtime.dm10.fea38b9f44e1` | Explicit | 1 | While this creature is tapped during your opponent's turn, if it hasn't been attacked that turn, your opponent's creatures must attack it if able. |
| `runtime.dm11.059040a66e08` | Explicit | 1 | Choose up to 2 of your opponent's tapped creatures in the battle zone. If they have total power less than this creature's power, destroy them. |
| `runtime.dm11.0e953b9be1b8` | Explicit | 1 | Choose a civilization. Until the start of your next turn, creatures of that civilization can't attack you even if your opponent puts them into the battle zone after you cast this spell. |
| `runtime.dm11.138f9e770072` | Explicit | 1 | Choose a non-evolution creature in the battle zone and add it to its owner's shields face down. |
| `runtime.dm11.14eb4399f694` | Explicit | 1 | Players can't use the  abilities of their creatures. |
| `runtime.dm11.2564bae63bbc` | Explicit | 1 | Destroy one of your opponent's creatures that has power 5000 or less. When your opponent puts that creature into his graveyard, search your deck. You may take a creature from your deck that has the same cost as that creature and put it into the battle zone. Then shuffle your deck. |
| `runtime.dm11.25fdee1be295` | Explicit | 1 | When each player untaps his cards at the start of his turn, he may then tap any number of cards in his mana zone. For every 2 cards he taps this way, he untaps one of his creatures in the battle zone. |
| `runtime.dm11.297c6ca8772b` | Explicit | 1 | You can cast this spell only if your opponent has more shields than you do. |
| `runtime.dm11.33eece76f602` | Explicit | 1 | When you put this creature into the battle zone, put the top card of each evolution creature from the battle zone into its owner's mana zone. |
| `runtime.dm11.3c9c656648b1` | Explicit | 1 | This creature gets +4000 power and has "Darkness stealth (This creature can't be blocked while your opponent has any darkness cards in his mana zone)." |
| `runtime.dm11.440d839e9a32` | Explicit | 1 | At the end of each of your turns, if this creature broke any shields that turn, return it to your hand. |
| `runtime.dm11.4ee27b111df6` | Explicit | 1 | Whenever any of your opponent's creatures is destroyed this turn, your opponent chooses a card in his mana zone and puts it into his graveyard. Then he chooses one of his shields and puts it into his graveyard. |
| `runtime.dm11.5a855d24d585` | Explicit | 1 | When you put this creature into the battle zone, look at the top card of your opponent's deck. |
| `runtime.dm11.6cf4753f11ad` | Explicit | 1 | At the start of each player's turn, that player chooses one of his creatures and destroys it. |
| `runtime.dm11.7458e2c1e600` | Explicit | 1 | When you put this creature into the battle zone, return all Snow Faeries from your graveyard and your mana zone to your hand. |
| `runtime.dm11.75bb278280e1` | Explicit | 1 | Creatures in the battle zone don't untap at the start of each player's turn. |
| `runtime.dm11.862fc7c81ddb` | Explicit | 1 | Whenever this creature attacks a creature, this creature gets +3000 power until the end of the turn. |
| `runtime.dm11.8a661584ba3a` | Explicit | 1 | When you put this creature into the battle zone, each player draws a card for each of his creatures in the battle zone that has "wave striker." |
| `runtime.dm11.96a8eddbd3fd` | Explicit | 1 | Draw up to 2 cards. Then discard a card from your hand. |
| `runtime.dm11.9ac4acef9c66` | Explicit | 1 | Each of your creatures in the battle zone gets +1000 power. |
| `runtime.dm11.9c9f422da0c8` | Explicit | 1 | When you put this creature into the battle zone, destroy up to 2 of your opponent's creatures that have "blocker." |
| `runtime.dm11.abffb6172ddd` | Explicit | 1 | Your opponent chooses one of his shields for each shield you have. He puts the rest of his shields into his hand. (He can use the "shield trigger" abilities of those shields.) |
| `runtime.dm11.ba7875a0da69` | Explicit | 1 | Choose a card in your opponent's mana zone and put it into his graveyard. Then put the top card of your deck into your mana zone. |
| `runtime.dm11.c169d0782903` | Explicit | 1 | This creature gets +4000 power and can attack untapped creatures. |
| `runtime.dm11.cdbffa13900f` | Explicit | 1 | Put a creature that has Dragon in its race from your mana zone into the battle zone. |
| `runtime.dm11.d20d15ced1ca` | Explicit | 1 | When you put this creature into the battle zone, if your opponent has 3 or fewer shields, draw up to 2 cards. |
| `runtime.dm11.d2ba55909e6c` | Explicit | 1 | Whenever you summon a creature this turn, search your deck. You may take a creature from your deck that has the same name as that creature and put it into the battle zone. Then shuffle your deck. |
| `runtime.dm11.d4974736115c` | Explicit | 1 | Whenever one of your creatures is destroyed during your opponent's turn, you may draw a card. |
| `runtime.dm11.e09671157cf0` | Explicit | 1 | This creature gets +4000 power and has "blocker". (Whenever an opponent's creature attacks, you may tap this creature to stop the attack. Then the 2 creatures battle.) |
| `runtime.dm11.ec3ce5609369` | Explicit | 1 | This creature gets +2000 power for each tapped creature your opponent has in the battle zone. |
| `runtime.dm11.ed4b4a629524` | Explicit | 1 | Your light creatures can't be blocked this turn. |
| `runtime.dm11.f04bb9176d7a` | Explicit | 1 | Choose 2 of your opponent's creatures in the battle zone. Your opponent chooses one of them, puts it into his hand, and destroys the other one. Then choose 2 cards in your opponent's mana zone. Your opponent chooses one of them, puts it into his hand, and puts the other one into his graveyard. |
| `runtime.dm11.f5bd8d5a2150` | Explicit | 1 | This creature gets +4000 power and can't be blocked by any creature that has power 5000 or less. |
| `runtime.dm11.f61d691a0402` | Explicit | 1 | Choose one of your opponent's non-evolution creatures in the battle zone and return it to his hand. Then he discards a card at random from his hand. |
| `runtime.dm11.f785bc609066` | Explicit | 1 | Reveal the top 4 cards of your deck. Put one of them that has "blocker" into your hand, and put the rest on the bottom of your deck in any order. |
| `runtime.dm11.f84e98e21b97` | Explicit | 1 | Choose a number. Show your hand to your opponent and discard from it each card that has that cost. Then your opponent shows you his hand and discards from it each card that has that cost. |
| `runtime.dm12.05ca76348393` | Explicit | 1 | When you put this creature into the battle zone, put the top card of your deck into your mana zone. Then put a creature from your mana zone into your hand. |
| `runtime.dm12.194d3ccd0a96` | Explicit | 1 | Choose a creature in the battle zone. Then, for each Cloned Spiral in each graveyard, you may choose another creature in the battle zone. Return all those creatures to their owners' hands. |
| `runtime.dm12.1a91de7f7ac5` | Explicit | 1 | When a player evolves another creature, destroy this creature. |
| `runtime.dm12.1da00315fb36` | Explicit | 1 | When this creature blocks, destroy it after it battles. |
| `runtime.dm12.2d2fbab3dd9a` | Explicit | 1 | When you put this creature into the battle zone, each player may draw a card. |
| `runtime.dm12.30f0616f85fc` | Explicit | 1 | Whenever one of your Gladiators or Earth Eaters blocks, you may draw a card. |
| `runtime.dm12.3b336830c0a0` | Explicit | 1 | At the end of each of your turns, you may untap any number of your Wild Veggies and Rainbow Phantoms in the battle zone. |
| `runtime.dm12.43d4834de780` | Explicit | 1 | Choose one of your opponent's creatures in the battle zone. Then, for each Cloned Deflector in each graveyard, you may choose another of your opponent's creatures in the battle zone. Tap all those creatures. |
| `runtime.dm12.49150e75130f` | Explicit | 1 | Whenever you draw the card at the start of your turn, if one of your creatures in the battle zone has higher power then each of your opponent's creatures in the battle zone, you may draw an extra card. |
| `runtime.dm12.4e76c5e84b8c` | Explicit | 1 | Whenever your opponent uses the "shield trigger" ability of one of his shields, search your deck. You may take a creature from your deck, show that creature to your opponent, and put it into your hand. Then shuffle your deck. |
| `runtime.dm12.4e95e27c563d` | Explicit | 1 | Whenever another creature is put into the battle zone, tap this creature. |
| `runtime.dm12.525b050cb153` | Explicit | 1 | When you put this creature into the battle zone, destroy the creature that has the lowest power in the battle zone. If there's a tie, you choose from among the tied creatures. |
| `runtime.dm12.552c1d79bf63` | Explicit | 1 | When you put this creature into the battle zone, for each creature you have in the battle zone, you may choose a shield and turn it face up. (Those shields stay face up while they're in the shield zone.) |
| `runtime.dm12.58e4dbf4fb2e` | Explicit | 1 | Each of your Chimeras and Armorloids in the battle zone has "double breaker." (A creature that has "double breaker" breaks 2 shields.) |
| `runtime.dm12.64b3cc8047e8` | Explicit | 1 | Your opponent chooses one of your shields. Look at it. If it's a spell, you may cast it immediately for no cost. If it's not a spell or if you don't cast it, put it back where it was. |
| `runtime.dm12.67aa7c464d4b` | Explicit | 1 | When you put this creature into the battle zone, your opponent draws a card. |
| `runtime.dm12.6dcf6ded2ff6` | Explicit | 1 | Each of your creatures in the battle zone gets +5000 power and has "blocker" and "double breaker". |
| `runtime.dm12.70aebb2da07f` | Explicit | 1 | When this creature leaves the battle zone, destroy all creatures. |
| `runtime.dm12.76b93434b558` | Explicit | 1 | When you put this creature into the battle zone, your may return a water or fire creature from your graveyard to your hand. |
| `runtime.dm12.7b9ed0fe6b2b` | Explicit | 1 | Choose a card at random in your opponent's hand. Then, for each Cloned Nightmare in each graveyard, you may choose another card at random in your opponent's hand. Your opponent discards all those cards. |
| `runtime.dm12.7d9e31c4511b` | Explicit | 1 | When this creature would leave the battle zone, only the top card leaves the battle zone instead. (Separate the other cards into 2 creatures.) |
| `runtime.dm12.7e0ed2237b9f` | Explicit | 1 | When you put this creature into the battle zone, return one of your creatures that costs 4 or less from the battle zone to your hand. |
| `runtime.dm12.84c6731e0614` | Explicit | 1 | When you put this creature into the battle zone, you may choose a darkness or fire creature in the battle zone and tap it. |
| `runtime.dm12.8953b5ff527d` | Explicit | 1 | Your creatures and your opponent's creatures each cost up to 1 less to summon. They can't cost less than 1. |
| `runtime.dm12.91ac5d990f78` | Explicit | 1 | At the end of each turn, if your opponent drew 2 or more cards that turn, he discards his hand. |
| `runtime.dm12.98ac5447da9c` | Explicit | 1 | Whenever any of your XenoParts or Giant Insects is attacking your opponent and isn't blocked, your opponent chooses a card in his mana zone and puts it into his graveyard. |
| `runtime.dm12.98ebddc21461` | Explicit | 1 | Whenever this creature attacks or leaves the battle zone, reveal the top card of your deck. If it's a non-evolution creature, put it into the battle zone. Otherwise, put it into your hand. |
| `runtime.dm12.a53f63541628` | Explicit | 1 | Choose a number less than or equal to 6000. Destroy all creatures that have that power. |
| `runtime.dm12.a7e93898becd` | Explicit | 1 | Discard any number of cards from your hand. Then draw that many cards. |
| `runtime.dm12.b060e97a7cad` | Explicit | 1 | Whenever you would choose one of your opponent's shields, your opponent chooses instead. |
| `runtime.dm12.bfc83d374103` | Explicit | 1 | Choose one of your opponent's creatures in the battle zone that has power 3000 or less. Then, for each Cloned Blade in each graveyard, you may choose another of your opponent's creatures in the battle zone that has power 3000 or less. Destroy all those creatures. |
| `runtime.dm12.c7dba8f5f2fc` | Explicit | 1 | When you put this creature is put into battle zone, destroy one of your opponent's creatures that has power 5000 or less. |
| `runtime.dm12.d3321128d130` | Explicit | 1 | Whenever this creature attacks or leaves the battle zone, add the top card of your deck to your shields face down. |
| `runtime.dm12.e816f5b8f96f` | Explicit | 1 | Whenever this creature blocks, your opponent chooses one of his creatures in the battle zone and returns it to his hand. |
| `runtime.dm12.eb10bbbcd8ce` | Explicit | 1 | When this creature leaves the battle zone, your opponent discards his hand. |
| `runtime.dm12.ed496e047c67` | Explicit | 1 | Whenever your opponent would choose one of your shields, you choose instead. |
| `runtime.dm12.ede5ce16ff97` | Explicit | 1 | When you put this creature into the battle zone, return all your other creatures from the battle zone to your hand. |
| `runtime.dm12.f6fb70c6b41c` | Explicit | 1 | This creature gets +3000 power for each Cloned Spike-Horn in each graveyard. |
| `runtime.dm12.fd1819811f70` | Explicit | 1 | When you put this creature into the battle zone, draw up to 2 cards. Then discard 2 cards from your hand. |
| `runtime.dm12.ff2cbf303969` | Explicit | 1 | This creature gets +2000 power for each card in your opponent's hand. |
| `runtime.promo.249529f9514e` | Explicit | 1 | Whenever this creature attacks, reveal the top card of your deck. If it's a Survivor, put it into your hand. Otherwise, put it into your graveyard. |
| `runtime.promo.59679680f2f2` | Explicit | 1 | Whenever this creature loses a battle against a light creature, this creature isn't destroyed. |
| `runtime.promo.6c48e1994c7e` | Explicit | 1 | When you put this creature into the battle zone, if your opponent has more creatures in the battle zone than you do, search your deck. For each extra creature he has, you may take a creature from your deck, show that creature to your opponent, and put it into your hand. Then shuffle your deck. |
| `runtime.promo.71e6308a1250` | Explicit | 1 | Each of your creatures in the battle zone is a Survivor in addition to its other races. |
| `runtime.promo.79e28f068b3f` | Explicit | 1 | When you put this creature into the battle zone, search your deck. You may take an Armored Dragon from your deck, show that Armored Dragon to your opponent, and put it into your hand. Then shuffle your deck. |
| `runtime.promo.82ace3cae723` | Explicit | 1 | When this creature loses a battle against a darkness creature, this creature isn't destroyed. |
| `runtime.promo.989b99c422d0` | Explicit | 1 | Each of your other creatures in the battle zone that has power 5000 or more breaks one more shield. |
| `runtime.promo.a52274d75456` | Explicit | 1 | Whenever one of your creatures is destroyed, you may untap this creature. |
| `runtime.promo.c84bcfc575c1` | Explicit | 1 | Whenever one of your other creatures that has Dragon in its race attacks, you may choose one of your opponent's creatures in the battle zone that has power 5000 or less and put it into his mana zone. |
| `search.any_number` | Search | 1 | Search your deck. You may take any number of cards from your deck, show those cards to your opponent, and put them into your hand. Then shuffle your deck. |
| `search.deck_filtered` | Search | 11 | Search your deck. You may take a card from your deck and put it into your hand. Then shuffle your deck. |
| `search.either_deck_to_graveyard` | Search | 1 | Search a player's deck. You may take a card from that deck and put it into that player's graveyard. Then the player shuffles his deck. |
| `search.opponent_deck_mill` | Search | 1 | Search your opponent's deck. Take up to 2 cards from his deck and put them into his graveyard. Then your opponent shuffles his deck. |
| `search.summon_civilization_creature` | Search | 1 | When you put this creature into the battle zone, search your deck. You may take a nature creature from your deck, show that creature to your opponent, and put it into your hand. Then shuffle your deck. |
| `search.summon_race_creature` | Search | 1 | When you put this creature into the battle zone, search your deck. You may take a Giant Insect from your deck, show that Giant Insect to your opponent, and put it into your hand. Then shuffle your deck. |
| `search.summon_survivor` | Search | 1 | When you put this creature into the battle zone, search your deck. You may take a Survivor from your deck, show that Survivor to your opponent, and put it into your hand. Then shuffle your deck. |
| `search.summon_whispering_totem` | Search | 1 | When you put this creature into the battle zone, search your deck. You may take a Whispering Totem from your deck, show that creature to your opponent, and put it into your hand. Then shuffle your deck. |
| `search.turbo_attack_any_card` | Search | 1 | When this creature attacks, search your deck. You may take a card from your deck and put it into your hand. Then shuffle your deck. |
| `search.unblocked_creature` | Search | 1 | Whenever this creature is attacking your opponent and isn't blocked, search your deck. You may take a creature from your deck, show that creature to your opponent, and put it into your hand. Then shuffle your deck. |
| `search.unblocked_spell` | Search | 1 | Whenever this creature is attacking your opponent and isn't blocked, search your deck. You may take a spell from your deck, show that spell to your opponent, and put it into your hand. Then shuffle your deck. |
| `spell.destroy_opponent` | Spell | 1 | Destroy one of your opponent's creatures. |
| `spell.destroy_power` | Spell | 4 | Destroy 1 of your opponent's creatures that has power 2000 or less. |
| `spell.destroy_untapped` | Spell | 1 | Destroy one of your opponent's untapped creatures. |
| `spell.draw` | Spell | 3 | Draw 2 cards. |
| `spell.mana_acceleration` | Spell | 2 | Put the top 2 cards of your deck into your mana zone. |
| `spell.tap_all_opponent` | Spell | 1 | Tap all your opponent's creatures in the battle zone. |
| `spell.tap_opponent` | Effect | 1 | Choose one of your opponent's creatures in the battle zone and tap it. |
| `static.battle_dragon_power` | Static | 1 | While battling a creature that has Dragon in its race, this creature gets +6000 power. |
| `static.civilization_blocker_aura` | Static | 1 | Each of your other light creatures in the battle zone has "blocker". (Whenever one of your opponent's creatures attacks, you may tap a creature that has "blocker" to stop the attack. Then the 2 creatures battle.) |
| `static.civilization_power_attacker_aura` | Static | 1 | Each of your other fire creatures has "power attacker +2000". (While attacking, a creature that has "power attacker +2000" gets +2000 power.) |
| `static.conditional_civilization_power` | Static | 2 | While you have a darkness creature in the battle zone, this creature gets +2000 power. |
| `static.conditional_power` | Static | 3 | While you have at least 1 Angel Command in the battle zone, this creature gets +2000 power. |
| `static.conditional_power_attacker` | Static | 1 | While you have at least one other fire creature in the battle zone, this creature has "Power attacker +3000 (While attacking, this creature gets +3000 power)." |
| `static.conditional_unblockable` | Static | 1 | This creature can't be blocked while you have at least 2 other creatures in the battle zone. |
| `static.cost_reduction` | Static | 2 | Your creatures each cost 1 less to summon. They can't cost less than 1. |
| `static.dragon_cost_reduction` | Static | 1 | Your creatures that have Dragon in their race each cost 2 less to summon. (Dragonoids don't count.) They can't cost less than 2. |
| `static.dynamic_attack_power` | Static | 4 | While attacking, this creature gets +1000 power for each other creature you have in the battle zone. |
| `static.evolution_enters_tapped` | Static | 1 | Evolution creatures are put into the battle zone tapped. |
| `static.global_civilization_cost_tax` | Static | 5 | Each darkness creature costs 1 more to summon and each darkness spell costs 1 more to cast. |
| `static.global_dragon_speed_attacker` | Static | 1 | Each creature in the battle zone that has Dragon in its race has "speed attacker." (A creature that has "speed attacker" doesn't get summoning sickness.) |
| `static.global_must_attack` | Static | 1 | Each creature attacks each turn if able. |
| `static.global_other_civilization_power` | Static | 2 | Each other darkness creature in the battle zone gets +2000 power. |
| `static.global_race_power` | Static | 1 | Each Armored Dragon in the battle zone gets +1000 power. |
| `static.global_race_power_attacker` | Static | 2 | Each Angel Command in the battle zone gets "power attacker +4000." (While attacking, a creature that has "power attacker +4000" gets +4000 power.) |
| `static.global_race_power_blocker` | Static | 1 | Each Demon Command in the battle zone gets +2000 power and has "blocker". (Whenever one of your opponent's creatures attacks, you may tap a creature that has "blocker" to stop the attack. Then the 2 creatures battle.) |
| `static.global_slayer` | Static | 1 | Each creature in the battle zone has "slayer." (Whenever a creature that has "slayer" battles, destroy the other creature after the battle.) |
| `static.global_two_group_power_aura` | Static | 2 | Angel Commands and Demon Commands in the battle zone each get +2000 power. |
| `static.graveyard_power` | Static | 1 | While attacking, this creature gets +1000 power for each fire card in your graveyard. |
| `static.lone_power_breaker` | Static | 2 | While you have no other creatures in the battle zone, this creature gets "power attacker +4000" and "double breaker". (A creature that has "power attacker +4000" and "double breaker" gets +4000 power while attacking and breaks 2 shields.) |
| `static.mono_mana_blocker` | Static | 1 | While all the cards in your mana zone are light cards, this creature has "blocker" |
| `static.mono_mana_power` | Static | 5 | While all the cards in your mana zone are darkness cards, this creature gets +2000 power. |
| `static.no_other_civilization_power_breaker` | Static | 1 | While you have no other fire creatures in the battle zone, this creature has "power attacker +3000" and "double breaker." (A creature that has "power attacker +3000" and "double breaker" gets +3000 power while attacking and breaks 2 shields.) |
| `static.no_shields_attack_untapped` | Static | 1 | While you have no shields, this creature can attack untapped creatures and has "Power attacker +3000 (While attacking, this creature gets +3000 power)." |
| `static.no_shields_blocker` | Static | 1 | While you have no shields, this creature has "blocker". (Whenever an opponent's creature attacks, you may tap this creature to stop the attack. Then the 2 creatures battle.) |
| `static.no_shields_blocker_untap` | Static | 1 | While you have no shields, this creature has "blocker" and "At the end of each of your turns, you may untap this creature." |
| `static.no_shields_power_breaker` | Static | 1 | While you have no shields, this creature gets +4000 power and has "double breaker (This creature breaks 2 shields)." |
| `static.power_per_darkness_graveyard` | Static | 1 | This creature gets +2000 power for each darkness creature in your graveyard. (A creature that has power 0 or less is destroyed.) |
| `static.power_per_graveyard_creature` | Static | 1 | This creature gets +2000 power for each creature in your graveyard. |
| `static.power_per_hand` | Static | 1 | This creature gets +3000 power for each card in your hand. (A creature that has power 0 or less is destroyed.) |
| `static.power_per_opponent_civilizations` | Static | 1 | This creature gets +1000 power for each water creature and darkness creature your opponent has in the battle zone. |
| `static.power_per_other_civilization` | Static | 5 | This creature gets +1000 power for each of your other nature creatures in the battle zone. |
| `static.power_per_other_nature` | Static | 1 | This creature gets +2000 power for each of your other nature creatures in the battle zone. (A creature that has power 0 or less is destroyed.) |
| `static.power_per_shield` | Static | 2 | This creature gets +1000 power for each shield you have. |
| `static.power_threshold_double_breaker` | Static | 2 | While this creature has power 6000 or more, it has "double breaker (This creature break 2 shields)." |
| `static.power_threshold_triple_breaker` | Static | 2 | While this creature has power 15000 or more, it has "Triple breaker (This creature breaks 3 shields)" instead of "double breaker." |
| `static.race_power_aura` | Static | 7 | Each of your other Armored Dragons in the battle zone gets +3000 power. |
| `static.race_slayer_aura` | Static | 1 | Each of your Ghosts in the battle zone has "slayer." (Whenever a creature that has "slayer" battles, destroy the other creature after the battle.) |
| `static.race_unblockable` | Static | 5 | Liquid People can't be blocked. |
| `static.self_power` | Static | 2 | This creature gets +1000 power. |
| `static.share_tap_ability` | Static | 5 | Each of your darkness creatures may tap instead of attacking to use this creature's tap ability. |
| `static.tapped_race_power_aura` | Static | 1 | While this creature is tapped, each of your other Beast Folk in the battle zone gets +2000 power. |
| `static.turbo_all_speed_attacker` | Static | 1 | Each of your creatures in the battle zone has "speed attacker." (A creature that has "speed attacker" doesn't get summoning sickness.) |
| `static.turbo_attack_untapped_power` | Static | 1 | This creature can attack untapped creatures and has "Power attacker +4000 (While attacking, this creature gets +4000 power)." |
| `static.turbo_power_double_breaker` | Static | 1 | This creature gets +5000 power and has "double breaker (This creature breaks 2 shields)." |
| `tap.all_nonblockers` | Tap | 1 | Tap all creatures in the battle zone that don't have "blocker." |
| `tap.all_opponent_blockers` | Tap | 1 | When you put this creature into the battle zone, tap all your opponent's creatures in the battle zone that have "blocker". |
| `tap.attack_civilizations` | Tap | 1 | Whenever this creature attacks, you may choose a darkness or fire creature in the battle zone and tap it. (First choose what this creature is attacking. Then choose a creature to tap.) |
| `tap.choose_civilization_group` | Tap | 1 | Tap all darkness creatures in the battle zone, or tap all fire creatures in the battle zone. |
| `tap.global_except_civilization` | Tap | 1 | Tap all creatures in the battle zone except light creatures. |
| `tap.selected_or_all` | Tap | 5 | Choose up to 2 of your opponent's creatures in the battle zone and tap them. |
| `tap.summon_per_untapped_mana` | Tap | 1 | When you put this creature into the battle zone, tap one of your opponent's creatures in the battle zone for each untapped light card in your mana zone. |
| `trigger.after_block_battle_untap` | Triggered | 1 | Whenever this creature blocks, untap it after it battles. |
| `trigger.after_player_attack_destroy_self` | Triggered | 1 | When this creature attacks a player, destroy it after the attack. |
| `trigger.allied_attack_tap` | Triggered | 1 | Whenever any of your creatures attacks, you may choose one of your opponent's creatures in the battle zone and tap it. (First choose what your creature is attacking. Then choose a creature to tap.) |
| `trigger.any_spell_power` | Triggered | 1 | Whenever a player casts a spell, this creature gets +3000 power until the end of the turn. (Do what the spell says before this creature gets the extra power.) |
| `trigger.armorloid_destroyed_mana_burn` | Triggered | 1 | Whenever one of your Armorloids is destroyed, your opponent chooses a card in his mana zone and puts it into his graveyard. |
| `trigger.attack_discard` | Discard | 1 | Whenever this creature attacks, discard a card from your hand. |
| `trigger.attack_draw` | Triggered | 1 | Whenever this creature attacks, you may draw a card. |
| `trigger.attack_draw_opponent_creature_count` | Triggered | 1 | Whenever this creature attacks, you may draw a number of cards equal to the number of creatures your opponent has in the battle zone. |
| `trigger.attack_draw_per_civilization` | Triggered | 1 | Whenever this creature attacks, you may draw a card for each of your other water creatures in the battle zone. |
| `trigger.attack_draw_per_other_tapped` | Triggered | 1 | Whenever this creature attacks, you may draw a card for each other tapped creature you have in the battle zone. |
| `trigger.attack_force_chosen_blocker` | Triggered | 1 | Whenever this creature attacks, you may choose one of your opponent's untapped creatures that has "blocker." This turn, that creature blocks this creature if able and this creature can't be blocked by other creatures. |
| `trigger.attacked_slayer` | Triggered | 1 | Whenever this creature is attacked, it gets "slayer" until the end of the turn. (Whenever a creature that has "slayer" battles, destroy the other creature after the battle.) |
| `trigger.battle_destroy_self` | Triggered | 1 | When this creature battles, destroy it after the battle. |
| `trigger.block_prevent_battle` | Triggered | 2 | Whenever this creature becomes blocked, no battle happens. (Both creatures stay tapped.) |
| `trigger.blocked_break_shield` | Triggered | 1 | Whenever this creature becomes blocked, it breaks one of your opponent's shields. |
| `trigger.blocked_power` | Triggered | 1 | Whenever this creature becomes blocked, it gets +3000 power until the end of the turn. |
| `trigger.cast_conditional_draw` | Triggered | 1 | When you cast this spell, if your opponent has 3 or more creatures in the battle zone, draw 3 cards. |
| `trigger.cyber_summon_draw` | Triggered | 1 | Whenever you put another creature that has Cyber in its race into the battle zone, you may draw a card. |
| `trigger.destroyed_each_player_mana_burn` | Triggered | 2 | When this creature is destroyed, each player chooses 2 cards in his mana zone and puts them into his graveyard. |
| `trigger.destroyed_optional_draw` | Triggered | 1 | When this creature is destroyed, you may draw a card. |
| `trigger.destroyed_return_mana_creature` | Triggered | 1 | When this creature is put into your graveyard from the battle zone, you may return a creature from your mana zone to your hand. |
| `trigger.dragon_summon_destroy_lowest` | Triggered | 1 | Whenever you put a Dragonoid or a creature that has Dragon in its race into the battle zone, destroy the creature that has the least power in the battle zone. If there's a tie, you choose from among the tied creatures. |
| `trigger.dragon_summon_mana_acceleration` | Triggered | 1 | Whenever you put a Dragonoid or a creature that has Dragon in its race into the battle zone, put the top card of your deck into your mana zone. |
| `trigger.end_turn_lone_destroy` | Triggered | 1 | At the end of each of your turns, if this is your only creature in the battle zone, destroy it. |
| `trigger.end_turn_return_self` | Triggered | 1 | At the end of your turn, return this creature to your hand. |
| `trigger.end_turn_untap_self` | Triggered | 2 | At the end of each of your turns, untap this creature. |
| `trigger.ghost_summon_destroy_untapped` | Triggered | 1 | Whenever you put a Ghost into the battle zone, you may destroy one of your opponent's untapped creatures. |
| `trigger.guardian_summon_tap` | Triggered | 1 | Whenever you put a Guardian into the battle zone, you may choose one of your opponent's creatures in the battle zone and tap it. |
| `trigger.opponent_card_blocker` | Triggered | 1 | Whenever your opponent summons a creature or casts a spell, this creature gets "blocker" until the end of the turn. (Whenever an opponent's creature attacks, you may tap a creature that has "blocker" to stop the attack. Then the 2 creatures battle.) |
| `trigger.opponent_shield_trigger_add_shield` | Triggered | 1 | Whenever your opponent uses the "shield trigger" ability of one of his shields, you may add the top card of your deck to your shields face down. |
| `trigger.opponent_shield_trigger_draw` | Triggered | 1 | Whenever your opponent uses the "shield trigger" ability of one of his shields, draw up to 2 cards. |
| `trigger.opponent_shield_trigger_untap` | Triggered | 1 | Whenever your opponent uses the "shield trigger" ability of a shield broken by this creature, you may untap this creature. |
| `trigger.opponent_spell_discard_mana_burn` | Triggered | 1 | Whenever your opponent casts a spell, he chooses and discards a card from his hand, then chooses a card in his mana zone and puts it into his graveyard. |
| `trigger.other_creature_destroyed_draw` | Triggered | 1 | Whenever another creature is destroyed, you may draw a card. |
| `trigger.other_creature_summoned_draw` | Triggered | 1 | Whenever another creature is put into the battle zone, you may draw a card. |
| `trigger.other_own_summon_destroy_self` | Triggered | 1 | When you put another creature into the battle zone, destroy this creature. |
| `trigger.other_summon_beast_folk_buff` | Triggered | 1 | Whenever you put another creature into the battle zone, each Beast Folk in the battle zone gets +2000 power and "double breaker" until the end of the turn. (A creature that has "double breaker" breaks 2 shields.) |
| `trigger.other_summon_power` | Triggered | 1 | Whenever another creature is put into the battle zone, this creature gets +3000 power until the end of the turn. |
| `trigger.start_draw_extra` | Triggered | 1 | Whenever you draw the card at the start of your turn, you may draw an extra card. |
| `trigger.summon_conditional_draw` | Triggered | 1 | When you put this creature into the battle zone, if you have a Cyber Lord in the battle zone, draw up to 3 cards. |
| `trigger.summon_destroy_power` | Triggered | 4 | When you put this creature into the battle zone, destroy one of your opponent's creatures that has power 1000 or less. |
| `trigger.summon_draw` | Triggered | 3 | When you put this creature into the battle zone, draw up to 2 cards. |
| `trigger.summon_draw_per_survivor` | Triggered | 1 | When you put this creature into the battle zone, you may draw a card for each Survivor in the battle zone. |
| `trigger.summon_mana_acceleration` | Triggered | 2 | When you put this creature into the battle zone, put the top 2 cards of your deck into your mana zone. |
| `trigger.turbo_blocked_break_shield` | Triggered | 1 | Whenever this creature is attacking your opponent and becomes blocked, it breaks one of your opponent's shields. |
| `trigger.turbo_unblocked_untap` | Triggered | 1 | Whenever this creature is attacking your opponent and isn't blocked, untap all your creatures in the battle zone except Solar Grasses. |
| `trigger.unblocked_extra_turn` | Triggered | 1 | Whenever this creature is attacking your opponent and isn't blocked, take an extra turn after this one. |
| `trigger.unblocked_shield_face_up` | Triggered | 1 | Whenever this creature is attacking your opponent and isn't blocked, choose one of your opponent's shields and turn it face up. (It stays face up while it's in the shield zone.) |
