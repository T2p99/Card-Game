using System;
using System.Text.RegularExpressions;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm05Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.StartsWith("At the end of this turn, search your deck. For each of your opponent's shields your creatures broke this turn",
                    StringComparison.OrdinalIgnoreCase))
            {
                var delayed = NewSelectionAbility(line, AbilityTiming.EndOfOwnerTurn,
                    AbilityOperation.Move, RuleController.Source, RuleZone.Deck,
                    RuleSelectionMode.Choose, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                delayed.Destination = RuleZone.Hand;
                delayed.Amount = 0;
                delayed.AmountPerShieldBrokenThisTurn = 1;
                delayed.SelectionMaximumUsesAmount = true;
                delayed.ShuffleAfter = true;
                return AddScheduledAbility(card, line, delayed, AbilityDuration.UntilEndOfTurn);
            }

            if (text.Equals("During your opponent's next turn, each of his creatures attacks if able.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.ForceOpponentCreaturesAttackNextTurn) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Each player counts the cards in his hand, shuffles those cards into his deck, then draws that many cards.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.RefreshHand) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Each player returns all cards from his mana zone to his hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.EachPlayer,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition());

            var civilizationBlocker = Regex.Match(text,
                @"^(Light|Water|Darkness|Fire|Nature)(?: and (light|water|darkness|fire|nature))? blocker ",
                RegexOptions.IgnoreCase);
            if (civilizationBlocker.Success)
            {
                var civilizations = civilizationBlocker.Groups[2].Success
                    ? new[]
                    {
                        ParseCivilization(civilizationBlocker.Groups[1].Value),
                        ParseCivilization(civilizationBlocker.Groups[2].Value)
                    }
                    : new[] { ParseCivilization(civilizationBlocker.Groups[1].Value) };
                return AddRelationshipRule(card, line, AbilityOperation.AllowBlockAgainst,
                    new CardFilterDefinition { CreaturesOnly = true, Civilizations = civilizations });
            }

            var forEachCivilizationEffect = Regex.Match(text,
                @"^For each (light|water|darkness|fire|nature) creature you have in the battle zone, you may choose (a card in your opponent's mana zone and put it into his graveyard|one of your opponent's creatures in the battle zone and tap it)\.$",
                RegexOptions.IgnoreCase);
            if (forEachCivilizationEffect.Success)
            {
                var tap = forEachCivilizationEffect.Groups[2].Value.IndexOf("tap", StringComparison.OrdinalIgnoreCase) >= 0;
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    tap ? AbilityOperation.Tap : AbilityOperation.Move,
                    RuleController.Opponent, tap ? RuleZone.BattleZone : RuleZone.ManaZone,
                    RuleSelectionMode.Choose, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = tap }, true);
                if (!tap) ability.Destination = RuleZone.Graveyard;
                ability.Amount = 0;
                ability.AmountPerMatchingCard = 1;
                ability.AmountSelector = CivilizationSelector(RuleController.Source,
                    RuleZone.BattleZone,
                    ParseCivilization(forEachCivilizationEffect.Groups[1].Value), true);
                ability.SelectionMaximumUsesAmount = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("If your opponent has more cards in his mana zone than you have in yours, put the top 2 cards of your deck into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Deck, RuleSelectionMode.Top,
                    0, 2, new CardFilterDefinition(), false);
                ability.Destination = RuleZone.ManaZone;
                ability.Condition = CompareZoneCounts(RuleZone.ManaZone, RuleCountComparison.LessThan);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Look at your opponent's hand and choose a card from it. Your opponent discards that card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddSelectionRule(card, line, timing, AbilityOperation.LookAtCards,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition());
                return AddSelectionRule(card, line, timing, AbilityOperation.Discard,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());
            }

            var cannotAttackByTwoCivilizations = Regex.Match(text,
                @"^This creature can't be attacked by (light|water|darkness|fire|nature) or (light|water|darkness|fire|nature) creatures\.$",
                RegexOptions.IgnoreCase);
            if (cannotAttackByTwoCivilizations.Success)
                return AddRelationshipRule(card, line, AbilityOperation.PreventAttackBy,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[]
                        {
                            ParseCivilization(cannotAttackByTwoCivilizations.Groups[1].Value),
                            ParseCivilization(cannotAttackByTwoCivilizations.Groups[2].Value)
                        }
                    });

            var blockerPowerMinimum = Regex.Match(text,
                @"^This creature can't be blocked by creatures that have power (\d+) or more\.$",
                RegexOptions.IgnoreCase);
            if (blockerPowerMinimum.Success)
                return AddRelationshipRule(card, line, AbilityOperation.PreventBlockBy,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        PowerAtLeast = int.Parse(blockerPowerMinimum.Groups[1].Value)
                    });

            if (text.Equals("This creature gets +1000 power for each water creature and darkness creature your opponent has in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                ability.ValuePerMatchingCard = 1000;
                ability.ValueSelector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Opponent,
                    Zone = RuleZone.BattleZone,
                    Mode = RuleSelectionMode.All,
                    Filter = new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Water, Civilization.Darkness }
                    }
                };
                AppendRule(card, ability);
                return true;
            }

            var simplePower = Regex.Match(text, @"^This creature gets \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (simplePower.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                ability.Value = int.Parse(simplePower.Groups[1].Value);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When one of your Armored Dragons would be destroyed, you may destroy this creature instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.ReplacementDestroy,
                    AbilityOperation.DestroySourceInstead, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        Race = "Armored Dragon"
                    }, true);
                AppendRule(card, ability);
                return true;
            }

            var destroyedNamedManaCreature = Regex.Match(text,
                @"^When this creature is destroyed, you may (?:choose )?(?:put )?(?:an? )?(.+?) (?:in|from) your mana zone(?: and put it)? into the battle zone\.$",
                RegexOptions.IgnoreCase);
            if (destroyedNamedManaCreature.Success)
                return AddMoveRule(card, line, AbilityTiming.OnDestroyed, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Name = destroyedNamedManaCreature.Groups[1].Value.Trim()
                    }, true);

            if (text.Equals("When this creature is destroyed, you may choose one of your shields and put it into your hand. You can't use the \"shield trigger\" ability of that shield.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnDestroyed, RuleController.Source,
                    RuleZone.ShieldZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition(), true);

            if (text.Equals("When you put this creature into the battle zone, if your opponent has more cards in his mana zone than you have in yours, put the top card of your deck into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Deck, RuleSelectionMode.Top,
                    0, 1, new CardFilterDefinition(), false);
                ability.Destination = RuleZone.ManaZone;
                ability.Condition = CompareZoneCounts(RuleZone.ManaZone, RuleCountComparison.LessThan);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, return all other creatures from the battle zone to their owners' hands.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Either,
                    RuleZone.BattleZone, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    { CreaturesOnly = true, OtherThanSource = true });

            if (text.Equals("When you put this creature into the battle zone, search your deck. You may take a Giant Insect from your deck, show that Giant Insect to your opponent, and put it into your hand. Then shuffle your deck.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true, Race = "Giant Insect" },
                    true, true);

            if (text.Equals("Whenever any of your creatures finishes attacking this turn, you may draw 2 cards for each shield it broke.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var delayed = new RuleAbilityDefinition(line.Id, AbilityTiming.AfterAnyCreatureAttack,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Amount = 0,
                    AmountPerShieldBrokenByEvent = 2
                };
                return AddScheduledAbility(card, line, delayed, AbilityDuration.UntilEndOfTurn);
            }

            if (text.StartsWith("Whenever this creature attacks, you may choose a darkness or fire creature in the battle zone and tap it.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnAttack, AbilityOperation.Tap,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Darkness, Civilization.Fire }
                    }, true);

            if (text.Equals("Whenever this creature becomes blocked, it breaks one of your opponent's shields.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnBlocked, AbilityOperation.BreakShield,
                    RuleController.Opponent, RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.Equals("Whenever your opponent causes a card to be put into your graveyard from your mana zone, you may return that card to your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line,
                    AbilityTiming.OnOwnManaMovedToGraveyardByOpponent, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Graveyard, RuleSelectionMode.EventCard,
                    0, 1, new CardFilterDefinition(), true);
                ability.Destination = RuleZone.ManaZone;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever your opponent uses the \"shield trigger\" ability of a shield broken by this creature, you may untap this creature.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnOpponentShieldTriggerUsed,
                    AbilityOperation.Untap, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                ability.Condition = new AbilityConditionDefinition { EventCardIsSource = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("While your opponent has no shields, this creature can't attack.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = SelfStaticKeyword(line, CardKeyword.CannotAttack);
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Opponent,
                    Zone = RuleZone.ShieldZone,
                    CountAtMost = 0,
                    HasCountAtMost = true
                };
                AppendRule(card, ability);
                return true;
            }

            return false;
        }

        private static bool AddScheduledAbility(CardDefinition card, PrintedRuleLine line,
            RuleAbilityDefinition delayed, AbilityDuration duration)
        {
            AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                AbilityOperation.ScheduleAbility)
            {
                RulesText = line.Text,
                GrantedAbility = delayed,
                Duration = duration
            });
            return true;
        }

        private static AbilityConditionDefinition CompareZoneCounts(RuleZone zone,
            RuleCountComparison comparison) => new AbilityConditionDefinition
        {
            CountComparison = comparison,
            ComparisonLeft = new RuleSelectorDefinition
            {
                Controller = RuleController.Source,
                Zone = zone,
                Mode = RuleSelectionMode.All
            },
            ComparisonRight = new RuleSelectorDefinition
            {
                Controller = RuleController.Opponent,
                Zone = zone,
                Mode = RuleSelectionMode.All
            }
        };
    }
}
