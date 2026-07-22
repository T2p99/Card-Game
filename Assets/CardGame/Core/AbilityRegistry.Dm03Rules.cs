using System;
using System.Text.RegularExpressions;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm03Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("After you cast this spell, put it into your mana zone instead of your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.Charger);

            if (text.Equals("Choose any number of your shields and put them into your hand. You can't use the \"shield trigger\" abilities of those shields.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Source,
                    RuleZone.ShieldZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition(), true);

            if (text.Equals("Choose any number of your shields and put them into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Source,
                    RuleZone.ShieldZone, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition(), true);

            var otherCivilizationPowerAttacker = Regex.Match(text,
                @"^Each of your other (light|water|darkness|fire|nature) creatures has \""power attacker \+(\d+)\""\.",
                RegexOptions.IgnoreCase);
            if (otherCivilizationPowerAttacker.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        Civilizations = new[] { ParseCivilization(otherCivilizationPowerAttacker.Groups[1].Value) }
                    }, false);
                ability.Value = int.Parse(otherCivilizationPowerAttacker.Groups[2].Value);
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Each of your other light creatures in the battle zone has \"blocker\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.GrantKeyword,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        Civilizations = new[] { Civilization.Light }
                    }, false);
                ability.Keyword = CardKeyword.Blocker;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Look at your opponent's hand and shields. Then put the shields back where they were.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.LookAtCards,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition(), false);
                ability.Selector.Zones = new[] { RuleZone.Hand, RuleZone.ShieldZone };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Return a creature that costs 6 or more from your mana zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, DefaultActionTiming(card), RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true, CostAtLeast = 6 });

            if (text.Equals("Reveal the top 4 cards of your deck. Put all water cards from among them into your hand and the rest into your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.RevealAndPartition, RuleController.Source,
                    RuleZone.Deck, RuleSelectionMode.Top, 0, 4,
                    new CardFilterDefinition(), false);
                ability.Destination = RuleZone.Hand;
                ability.SecondaryDestination = RuleZone.Graveyard;
                ability.PartitionFilter = new CardFilterDefinition
                    { Civilizations = new[] { Civilization.Water } };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("This creature can't attack while your opponent has more creatures in the battle zone than you do.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = SelfStaticKeyword(line, CardKeyword.CannotAttack);
                ability.Condition = CompareCreatureCounts(RuleCountComparison.LessThan);
                AppendRule(card, ability);
                return true;
            }

            var perOtherCivilization = Regex.Match(text,
                @"^This creature gets \+(\d+) power for each (?:of your )?other (light|water|darkness|fire|nature) creatures?(?: you have)? in the battle zone\.$",
                RegexOptions.IgnoreCase);
            if (perOtherCivilization.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                ability.ValuePerMatchingCard = int.Parse(perOtherCivilization.Groups[1].Value);
                ability.ValueSelector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Mode = RuleSelectionMode.All,
                    Filter = new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        Civilizations = new[] { ParseCivilization(perOtherCivilization.Groups[2].Value) }
                    }
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When this creature is put into your graveyard from the battle zone, you may return a creature from your mana zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnDestroyed, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true }, true);

            if (text.Equals("When this creature would be put into your graveyard from the battle zone, add it to your shields face down instead.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelfDestructionReplacement(card, line, RuleZone.ShieldZone, false);

            if (text.Equals("When you put this creature into the battle zone, choose one of your shields and return it to your hand. You can't use the \"shield trigger\" ability of that shield.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.ShieldZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            var bothManaBounce = Regex.Match(text,
                @"^When you put this creature into the battle zone, return (?:(\d+) cards|a card) from your mana zone to your hand\. Then your opponent chooses (?:(\d+) cards|a card) (?:in|from) his mana zone and returns (?:them|it) to his hand\.$",
                RegexOptions.IgnoreCase);
            if (bothManaBounce.Success)
            {
                var ownAmount = bothManaBounce.Groups[1].Success ? int.Parse(bothManaBounce.Groups[1].Value) : 1;
                var opponentAmount = bothManaBounce.Groups[2].Success ? int.Parse(bothManaBounce.Groups[2].Value) : 1;
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    ownAmount, ownAmount, new CardFilterDefinition());
                var opponentRule = NewSelectionAbility(line, AbilityTiming.OnSummon, AbilityOperation.Move,
                    RuleController.Opponent, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    opponentAmount, opponentAmount, new CardFilterDefinition(), false);
                opponentRule.Chooser = RuleController.Opponent;
                opponentRule.Destination = RuleZone.Hand;
                AppendRule(card, opponentRule);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, you may add a card from your hand to your shields face down. If you do, choose one of your shields and put it into your hand. You can't use the \"shield trigger\" ability of that shield.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Hand, RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition(), true);
                var followup = NewSelectionAbility(line, AbilityTiming.OnSummon, AbilityOperation.Move,
                    RuleController.Source, RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition(), false);
                followup.Destination = RuleZone.Hand;
                followup.RequiresPreviousSuccess = true;
                AppendRule(card, followup);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, you may choose one of your opponent's evolution creatures in the battle zone and put the top card of that creature into your opponent's graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.MoveEvolutionTop, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true, EvolutionOnly = true }, true)
                    && SetLastDestination(card, RuleZone.Graveyard);

            if (text.Equals("Whenever another of your darkness creatures would be put into your graveyard from the battle zone, you may return it to your hand instead.",
                    StringComparison.OrdinalIgnoreCase))
                return AddAuraDestructionReplacement(card, line, Civilization.Darkness, RuleZone.Hand, true);

            if (text.Equals("Whenever another of your nature creatures would be put into your graveyard from the battle zone, put it into your mana zone instead.",
                    StringComparison.OrdinalIgnoreCase))
                return AddAuraDestructionReplacement(card, line, Civilization.Nature, RuleZone.ManaZone, false);

            if (text.Equals("Whenever this creature attacks, put a non-fire card from your mana zone into your graveyard. Then your opponent chooses a non-fire card in his mana zone and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var filter = new CardFilterDefinition { ExcludedCivilizations = new[] { Civilization.Fire } };
                AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose, 1, 1, filter);
                var opponentRule = NewSelectionAbility(line, AbilityTiming.OnAttack, AbilityOperation.Move,
                    RuleController.Opponent, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    1, 1, filter, false);
                opponentRule.Chooser = RuleController.Opponent;
                opponentRule.Destination = RuleZone.Graveyard;
                AppendRule(card, opponentRule);
                return true;
            }

            if (text.Equals("Whenever this creature attacks, return all creatures from your mana zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true });

            if (text.StartsWith("Whenever this creature attacks, you may choose a creature in the battle zone that has power 2000 or less and return it",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Either,
                    RuleZone.BattleZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 2000 }, true);

            var attackDrawPerOtherCivilization = Regex.Match(text,
                @"^Whenever this creature attacks, you may draw a card for each (?:of your )?other (light|water|darkness|fire|nature) creatures? in the battle zone\.$",
                RegexOptions.IgnoreCase);
            if (attackDrawPerOtherCivilization.Success)
            {
                var ability = new RuleAbilityDefinition(line.Id, AbilityTiming.OnAttack, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Amount = 0,
                    AmountPerMatchingCard = 1,
                    AmountSelector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true,
                            Civilizations = new[] { ParseCivilization(attackDrawPerOtherCivilization.Groups[1].Value) }
                        }
                    }
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever this creature attacks, you may put a card from your mana zone into your graveyard. If you do, this creature gets \"power attacker +3000\" and \"double breaker\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition(), true);
                var power = NewSelectionAbility(line, AbilityTiming.OnAttack,
                    AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                power.Value = 3000;
                power.RequiresPreviousSuccess = true;
                AppendRule(card, power);
                var breaker = NewSelectionAbility(line, AbilityTiming.OnAttack,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                breaker.Keyword = CardKeyword.DoubleBreaker;
                breaker.RequiresPreviousSuccess = true;
                AppendRule(card, breaker);
                return true;
            }

            var attackMove = Regex.Match(text,
                @"^Whenever this creature attacks, you may (?:put|return) a (nature card|darkness creature|light spell) from your (graveyard) (?:into|to) your (mana zone|hand)\.$",
                RegexOptions.IgnoreCase);
            if (attackMove.Success)
            {
                var noun = attackMove.Groups[1].Value.ToLowerInvariant();
                var filter = new CardFilterDefinition
                {
                    CreaturesOnly = noun.Contains("creature"),
                    SpellsOnly = noun.Contains("spell"),
                    Civilizations = new[] { ParseCivilization(noun.Split(' ')[0]) }
                };
                return AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Source,
                    RuleZone.Graveyard, ParseZone(attackMove.Groups[3].Value), RuleSelectionMode.Choose,
                    0, 1, filter, true);
            }

            var allManaCivilizationPower = Regex.Match(text,
                @"^While all the cards in your mana zone are (light|water|darkness|fire|nature) cards, this creature gets \+(\d+) power(?: and has \""Double breaker.*)?\.$",
                RegexOptions.IgnoreCase);
            if (allManaCivilizationPower.Success)
            {
                var civilization = ParseCivilization(allManaCivilizationPower.Groups[1].Value);
                var condition = AllManaMatches(civilization);
                var power = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                power.Value = int.Parse(allManaCivilizationPower.Groups[2].Value);
                power.Condition = condition;
                AppendRule(card, power);
                if (text.IndexOf("double breaker", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var breaker = SelfStaticKeyword(line, CardKeyword.DoubleBreaker);
                    breaker.Condition = condition;
                    AppendRule(card, breaker);
                }
                return true;
            }

            var allManaBlocker = Regex.Match(text,
                @"^While all the cards in your mana zone are (light|water|darkness|fire|nature) cards, this creature has \""blocker\""",
                RegexOptions.IgnoreCase);
            if (allManaBlocker.Success)
            {
                var ability = SelfStaticKeyword(line, CardKeyword.Blocker);
                ability.Condition = AllManaMatches(ParseCivilization(allManaBlocker.Groups[1].Value));
                AppendRule(card, ability);
                return true;
            }

            var castOnlyAllMana = Regex.Match(text,
                @"^You can cast this spell only if all the cards in your mana zone are (light|water|darkness|fire|nature) cards\.$",
                RegexOptions.IgnoreCase);
            if (castOnlyAllMana.Success)
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.PlayRestriction,
                    AbilityOperation.RequireCondition)
                {
                    RulesText = line.Text,
                    Condition = AllManaMatches(ParseCivilization(castOnlyAllMana.Groups[1].Value))
                });
                return true;
            }

            if (text.Equals("You may destroy one of your darkness creatures. If you do, return a creature from your mana zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Darkness }
                    }, true);
                var followup = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Source, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
                followup.Destination = RuleZone.Hand;
                followup.RequiresPreviousSuccess = true;
                AppendRule(card, followup);
                return true;
            }

            return false;
        }

        private static RuleAbilityDefinition SelfStaticKeyword(PrintedRuleLine line, CardKeyword keyword)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.GrantKeyword,
                RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition(), false);
            ability.Keyword = keyword;
            return ability;
        }

        private static AbilityConditionDefinition CompareCreatureCounts(RuleCountComparison comparison) =>
            new AbilityConditionDefinition
            {
                CountComparison = comparison,
                ComparisonLeft = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Mode = RuleSelectionMode.All,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                },
                ComparisonRight = new RuleSelectorDefinition
                {
                    Controller = RuleController.Opponent,
                    Zone = RuleZone.BattleZone,
                    Mode = RuleSelectionMode.All,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                }
            };

        private static AbilityConditionDefinition AllManaMatches(Civilization civilization) =>
            new AbilityConditionDefinition
            {
                Controller = RuleController.Source,
                Zone = RuleZone.ManaZone,
                Filter = new CardFilterDefinition { Civilizations = new[] { civilization } },
                AllCardsMustMatchFilter = true
            };

        private static bool AddSelfDestructionReplacement(CardDefinition card, PrintedRuleLine line,
            RuleZone destination, bool optional)
        {
            AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.ReplacementDestroy,
                AbilityOperation.Move)
            {
                RulesText = line.Text,
                Destination = destination,
                Optional = optional
            });
            return true;
        }

        private static bool AddAuraDestructionReplacement(CardDefinition card, PrintedRuleLine line,
            Civilization civilization, RuleZone destination, bool optional)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.ReplacementDestroy,
                AbilityOperation.Move, RuleController.Source, RuleZone.BattleZone,
                RuleSelectionMode.All, 0, int.MaxValue,
                new CardFilterDefinition
                {
                    CreaturesOnly = true,
                    OtherThanSource = true,
                    Civilizations = new[] { civilization }
                }, optional);
            ability.Destination = destination;
            AppendRule(card, ability);
            return true;
        }

        private static bool SetLastDestination(CardDefinition card, RuleZone destination)
        {
            card.RuleAbilities[card.RuleAbilities.Length - 1].Destination = destination;
            return true;
        }
    }
}
