using System;
using System.Text.RegularExpressions;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm04Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            var twoRacePower = Regex.Match(text,
                @"^(Angel Commands and Demon Commands|Light and darkness creatures) in the battle zone each get \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (twoRacePower.Success)
            {
                var descriptor = twoRacePower.Groups[1].Value;
                var filter = descriptor.StartsWith("Angel", StringComparison.OrdinalIgnoreCase)
                    ? new CardFilterDefinition { CreaturesOnly = true, Races = new[] { "Angel Command", "Demon Command" } }
                    : new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Light, Civilization.Darkness }
                    };
                return AddStaticValueAura(card, line, AbilityOperation.ModifyPower,
                    RuleController.Either, int.Parse(twoRacePower.Groups[2].Value), filter);
            }

            if (text.StartsWith("Discard any number of cards from your hand. Then choose the same number",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Discard,
                    RuleController.Source, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition(), true);
                var grant = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.GrantKeyword,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, false);
                grant.Keyword = CardKeyword.DoubleBreaker;
                grant.Duration = AbilityDuration.UntilEndOfTurn;
                grant.UsePreviousSelectionCountAsAmount = true;
                grant.SelectionCountUsesAmount = true;
                grant.RequiresPreviousSuccess = true;
                AppendRule(card, grant);
                return true;
            }

            var racePowerAttacker = Regex.Match(text,
                @"^Each (Angel Command|Demon Command) in the battle zone (?:gets|has) \""power attacker \+(\d+)",
                RegexOptions.IgnoreCase);
            if (racePowerAttacker.Success)
            {
                AddStaticValueAura(card, line, AbilityOperation.ModifyPowerWhileAttacking,
                    RuleController.Either, int.Parse(racePowerAttacker.Groups[2].Value),
                    new CardFilterDefinition { CreaturesOnly = true, Race = racePowerAttacker.Groups[1].Value });
                if (text.IndexOf("double breaker", StringComparison.OrdinalIgnoreCase) >= 0)
                    AddStaticKeywordAura(card, line, CardKeyword.DoubleBreaker, RuleController.Either,
                        new CardFilterDefinition { CreaturesOnly = true, Race = racePowerAttacker.Groups[1].Value });
                return true;
            }

            var simpleRaceAura = Regex.Match(text,
                @"^Each (Armored Dragon) in the battle zone gets \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (simpleRaceAura.Success)
                return AddStaticValueAura(card, line, AbilityOperation.ModifyPower,
                    RuleController.Either, int.Parse(simpleRaceAura.Groups[2].Value),
                    new CardFilterDefinition { CreaturesOnly = true, Race = simpleRaceAura.Groups[1].Value });

            var civilizationTax = Regex.Match(text,
                @"^Each (light|water|darkness|fire|nature) creature costs (\d+) more to summon,? and each \1 spell costs \2 more to cast\.$",
                RegexOptions.IgnoreCase);
            if (civilizationTax.Success)
            {
                var civilization = ParseCivilization(civilizationTax.Groups[1].Value);
                var value = int.Parse(civilizationTax.Groups[2].Value);
                AddGlobalCostModifier(card, line, civilization, true, value);
                AddGlobalCostModifier(card, line, civilization, false, value);
                return true;
            }

            if (text.StartsWith("Each Demon Command in the battle zone gets +2000 power and has \"blocker\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var filter = new CardFilterDefinition { CreaturesOnly = true, Race = "Demon Command" };
                AddStaticValueAura(card, line, AbilityOperation.ModifyPower,
                    RuleController.Either, 2000, filter);
                AddStaticKeywordAura(card, line, CardKeyword.Blocker, RuleController.Either, filter);
                return true;
            }

            if (text.StartsWith("Each of your creatures in the battle zone gets +1000 power until the end of the turn for each light creature",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.ValuePerMatchingCard = 1000;
                ability.ValueSelector = CivilizationSelector(RuleController.Source, RuleZone.BattleZone,
                    Civilization.Light, true);
                ability.Duration = AbilityDuration.UntilEndOfTurn;
                AppendRule(card, ability);
                return true;
            }

            var eachOtherCivilization = Regex.Match(text,
                @"^Each other (light|water|darkness|fire|nature) creature in the battle zone gets \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (eachOtherCivilization.Success)
                return AddStaticValueAura(card, line, AbilityOperation.ModifyPower,
                    RuleController.Either, int.Parse(eachOtherCivilization.Groups[2].Value),
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        Civilizations = new[] { ParseCivilization(eachOtherCivilization.Groups[1].Value) }
                    });

            var forEachCivilizationMove = Regex.Match(text,
                @"^For each (light|water|darkness|fire|nature) creature you have in the battle zone, you may choose (one of your opponent's creatures|a card in your opponent's mana zone) and return it to his hand\.$",
                RegexOptions.IgnoreCase);
            if (forEachCivilizationMove.Success)
            {
                var fromBattle = forEachCivilizationMove.Groups[2].Value.IndexOf("creature", StringComparison.OrdinalIgnoreCase) >= 0;
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Opponent, fromBattle ? RuleZone.BattleZone : RuleZone.ManaZone,
                    RuleSelectionMode.Choose, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = fromBattle }, true);
                ability.Destination = RuleZone.Hand;
                ability.Amount = 0;
                ability.AmountPerMatchingCard = 1;
                ability.AmountSelector = CivilizationSelector(RuleController.Source, RuleZone.BattleZone,
                    ParseCivilization(forEachCivilizationMove.Groups[1].Value), true);
                // "For each ... you may choose" permits any number from zero
                // through the counted maximum; choosing one does not commit
                // the player to every available target.
                ability.SelectionMaximumUsesAmount = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Players can't cast spells other than light spells.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.PreventPlay,
                    RuleController.Either, RuleZone.None, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    {
                        SpellsOnly = true,
                        ExcludedCivilizations = new[] { Civilization.Light }
                    }, false);
                AppendRule(card, ability);
                return true;
            }

            var preventShieldTrigger = Regex.Match(text,
                @"^Players can't use the \""shield trigger\"" abilities of (light|water|darkness|fire|nature) cards\.$",
                RegexOptions.IgnoreCase);
            if (preventShieldTrigger.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.PreventShieldTrigger, RuleController.Either,
                    RuleZone.None, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        Civilizations = new[] { ParseCivilization(preventShieldTrigger.Groups[1].Value) }
                    }, false);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Put any number of cards from your mana zone into your graveyard. Then draw that many cards.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition(), true);
                var draw = new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    UsePreviousSelectionCountAsAmount = true,
                    RequiresPreviousSuccess = true
                };
                AppendRule(card, draw);
                return true;
            }

            if (text.Equals("Tap all creatures in the battle zone except light creatures.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Tap,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        ExcludedCivilizations = new[] { Civilization.Light }
                    });

            var attackUntappedCivilization = Regex.Match(text,
                @"^This creature can attack untapped (light|water|darkness|fire|nature) creatures\.$",
                RegexOptions.IgnoreCase);
            if (attackUntappedCivilization.Success)
                return AddRelationshipRule(card, line, AbilityOperation.AllowAttackUntapped,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { ParseCivilization(attackUntappedCivilization.Groups[1].Value) }
                    });

            var cannotBeAttackedBy = Regex.Match(text,
                @"^This creature can't be attacked by (light|water|darkness|fire|nature) creatures\.$",
                RegexOptions.IgnoreCase);
            if (cannotBeAttackedBy.Success)
                return AddRelationshipRule(card, line, AbilityOperation.PreventAttackBy,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { ParseCivilization(cannotBeAttackedBy.Groups[1].Value) }
                    });

            var cannotBeBlockedBy = Regex.Match(text,
                @"^This creature can't be blocked by (light|water|darkness|fire|nature) creatures\.$",
                RegexOptions.IgnoreCase);
            if (cannotBeBlockedBy.Success)
                return AddRelationshipRule(card, line, AbilityOperation.PreventBlockBy,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { ParseCivilization(cannotBeBlockedBy.Groups[1].Value) }
                    });

            var shieldPower = Regex.Match(text,
                @"^This creature gets \+(\d+) power for each shield you have\.$",
                RegexOptions.IgnoreCase);
            if (shieldPower.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                ability.ValuePerMatchingCard = int.Parse(shieldPower.Groups[1].Value);
                ability.ValueSelector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.ShieldZone,
                    Mode = RuleSelectionMode.All
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Until the end of the turn, each of your creatures in the battle zone gets \"While attacking, this creature gets +1000 power for each darkness card",
                    StringComparison.OrdinalIgnoreCase))
            {
                var granted = new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileAttacking)
                {
                    RulesText = line.Text,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard
                    },
                    ValuePerMatchingCard = 1000,
                    ValueSelector = CivilizationSelector(RuleController.Source,
                        RuleZone.ManaZone, Civilization.Darkness, false)
                };
                return AddGrantedAbilityToOwnCreatures(card, line, granted, AbilityDuration.UntilEndOfTurn);
            }

            if (text.StartsWith("Until the start of your next turn, each of your creatures in the battle zone gets \"blocker\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.GrantKeyword,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Keyword = CardKeyword.Blocker;
                ability.Duration = AbilityDuration.UntilStartOfOwnersNextTurn;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When this creature would be destroyed, add it to your shields face down instead.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelfDestructionReplacement(card, line, RuleZone.ShieldZone, false);

            if (text.Equals("When you put this creature into the battle zone, destroy all creatures except darkness creatures.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        ExcludedCivilizations = new[] { Civilization.Darkness }
                    });

            if (text.Equals("When you put this creature into the battle zone, destroy all light creatures that have power 4000 or less.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Light },
                        PowerAtMost = 4000
                    });

            if (text.Equals("When you put this creature into the battle zone, search your deck. You may take a nature creature from your deck, show that creature to your opponent, and put it into your hand. Then shuffle your deck.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Nature }
                    }, true, true);

            if (text.Equals("When you put this creature into the battle zone, tap one of your opponent's creatures in the battle zone for each untapped light card in your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon, AbilityOperation.Tap,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Amount = 0;
                ability.AmountPerMatchingCard = 1;
                ability.AmountSelector = CivilizationSelector(RuleController.Source,
                    RuleZone.ManaZone, Civilization.Light, false, true);
                ability.SelectionCountUsesAmount = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, you may return a Living Dead from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Graveyard, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true, Race = "Living Dead" }, true);

            if (text.Equals("When you put this creature into the battle zone, you may return all Angel Commands and all Demon Commands from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Graveyard, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Races = new[] { "Angel Command", "Demon Command" }
                    }, true);

            if (text.Equals("When you put this creature into the battle zone, you may reveal the top 3 cards of your deck. Put all light and darkness cards from the revealed cards into your hand, and put the rest into your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.RevealAndPartition, RuleController.Source,
                    RuleZone.Deck, RuleSelectionMode.Top, 0, 3,
                    new CardFilterDefinition(), true);
                ability.Destination = RuleZone.Hand;
                ability.SecondaryDestination = RuleZone.Graveyard;
                ability.PartitionFilter = new CardFilterDefinition
                    { Civilizations = new[] { Civilization.Light, Civilization.Darkness } };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, your opponent discards a card at random from his hand for each other darkness creature you have in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon, AbilityOperation.Discard,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.Random,
                    0, int.MaxValue, new CardFilterDefinition(), false);
                ability.Amount = 0;
                ability.AmountPerMatchingCard = 1;
                ability.AmountSelector = CivilizationSelector(RuleController.Source,
                    RuleZone.BattleZone, Civilization.Darkness, true, false, true);
                ability.SelectionCountUsesAmount = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever another creature is destroyed, you may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnOtherCreatureDestroyed, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true
                });
                return true;
            }

            if (text.Equals("Whenever another creature is put into the battle zone, you may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true
                });
                return true;
            }

            if (text.Equals("Whenever this creature attacks, you may choose a card in your opponent's mana zone and return it to his hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Opponent,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition(), true);

            if (text.StartsWith("Whenever this creature attacks, you may destroy one of your other creatures. If you do, this creature gets +2000 power and \"double breaker\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnAttack, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true }, true);
                var power = NewSelectionAbility(line, AbilityTiming.OnAttack, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                power.Value = 2000;
                power.RequiresPreviousSuccess = true;
                AppendRule(card, power);
                var breaker = NewSelectionAbility(line, AbilityTiming.OnAttack, AbilityOperation.GrantKeyword,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                breaker.Keyword = CardKeyword.DoubleBreaker;
                breaker.Duration = AbilityDuration.UntilEndOfTurn;
                breaker.RequiresPreviousSuccess = true;
                AppendRule(card, breaker);
                return true;
            }

            var whileCivilization = Regex.Match(text,
                @"^While you have a (light|water|darkness|fire|nature) creature in the battle zone, this creature gets \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (whileCivilization.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                ability.Value = int.Parse(whileCivilization.Groups[2].Value);
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Filter = new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { ParseCivilization(whileCivilization.Groups[1].Value) }
                    },
                    CountAtLeast = 1
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Your opponent chooses and discards a card from his hand for each light creature he has in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Discard,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition(), false);
                ability.Chooser = RuleController.Opponent;
                ability.Amount = 0;
                ability.AmountPerMatchingCard = 1;
                ability.AmountSelector = CivilizationSelector(RuleController.Opponent,
                    RuleZone.BattleZone, Civilization.Light, true);
                ability.SelectionCountUsesAmount = true;
                AppendRule(card, ability);
                return true;
            }

            return false;
        }

        private static bool AddStaticValueAura(CardDefinition card, PrintedRuleLine line,
            AbilityOperation operation, RuleController controller, int value, CardFilterDefinition filter)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static, operation, controller,
                RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue, filter, false);
            ability.Value = value;
            AppendRule(card, ability);
            return true;
        }

        private static bool AddStaticKeywordAura(CardDefinition card, PrintedRuleLine line,
            CardKeyword keyword, RuleController controller, CardFilterDefinition filter)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.GrantKeyword,
                controller, RuleZone.BattleZone, RuleSelectionMode.All,
                0, int.MaxValue, filter, false);
            ability.Keyword = keyword;
            AppendRule(card, ability);
            return true;
        }

        private static void AddGlobalCostModifier(CardDefinition card, PrintedRuleLine line,
            Civilization civilization, bool creatures, int value)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyCost,
                RuleController.Either, RuleZone.None, RuleSelectionMode.All,
                0, int.MaxValue, new CardFilterDefinition
                {
                    CreaturesOnly = creatures,
                    SpellsOnly = !creatures,
                    Civilizations = new[] { civilization }
                }, false);
            ability.Value = value;
            AppendRule(card, ability);
        }

        private static bool AddRelationshipRule(CardDefinition card, PrintedRuleLine line,
            AbilityOperation operation, CardFilterDefinition relatedFilter)
        {
            AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static, operation)
            {
                RulesText = line.Text,
                RelatedFilter = relatedFilter,
                Selector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Mode = RuleSelectionMode.SourceCard
                }
            });
            return true;
        }

        private static RuleSelectorDefinition CivilizationSelector(RuleController controller,
            RuleZone zone, Civilization civilization, bool creaturesOnly,
            bool untappedOnly = false, bool otherThanSource = false) => new RuleSelectorDefinition
        {
            Controller = controller,
            Zone = zone,
            Mode = RuleSelectionMode.All,
            Filter = new CardFilterDefinition
            {
                CreaturesOnly = creaturesOnly,
                UntappedOnly = untappedOnly,
                OtherThanSource = otherThanSource,
                Civilizations = new[] { civilization }
            }
        };

        private static bool AddGrantedAbilityToOwnCreatures(CardDefinition card, PrintedRuleLine line,
            RuleAbilityDefinition granted, AbilityDuration duration)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.GrantAbility,
                RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, false);
            ability.GrantedAbility = granted;
            ability.Duration = duration;
            AppendRule(card, ability);
            return true;
        }
    }
}
