using System;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm11Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("At the end of each of your turns, if this creature broke any shields that turn, return it to your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.EndOfOwnerTurn,
                    AbilityOperation.Move, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Destination = RuleZone.Hand;
                ability.Condition = new AbilityConditionDefinition { SourceBrokeShieldThisTurn = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("At the start of each player's turn, that player chooses one of his creatures and destroys it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var owner = NewSelectionAbility(line, AbilityTiming.StartOfOwnerTurn,
                    AbilityOperation.Destroy, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                AppendRule(card, owner);
                var opponent = NewSelectionAbility(line, AbilityTiming.StartOfOpponentTurn,
                    AbilityOperation.Destroy, RuleController.Opponent, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                opponent.Chooser = RuleController.Opponent;
                AppendRule(card, opponent);
                return true;
            }

            if (text.StartsWith("Choose 2 of your opponent's creatures in the battle zone. Your opponent chooses one of them, puts it into his hand, and destroys the other one.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var creatures = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.SplitPairOpponentChoice, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 2, 2,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                creatures.Destination = RuleZone.Hand;
                creatures.SecondaryDestination = RuleZone.Graveyard;
                creatures.SecondaryIsDestruction = true;
                AppendRule(card, creatures);

                var mana = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.SplitPairOpponentChoice, RuleController.Opponent,
                    RuleZone.ManaZone, RuleSelectionMode.Choose, 2, 2,
                    new CardFilterDefinition(), false);
                mana.Destination = RuleZone.Hand;
                mana.SecondaryDestination = RuleZone.Graveyard;
                AppendRule(card, mana);
                return true;
            }

            if (text.Equals("Choose a card in your opponent's mana zone and put it into his graveyard. Then put the top card of your deck into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Opponent,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Source,
                    RuleZone.Deck, RuleZone.ManaZone, RuleSelectionMode.Top,
                    1, 1, new CardFilterDefinition());
            }

            if (text.StartsWith("Choose a civilization. Until the start of your next turn, creatures of that civilization can't attack you",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.PreventCivilizationAttackPlayer) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Choose a non-evolution creature in the battle zone and add it to its owner's shields face down.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Either,
                    RuleZone.BattleZone, RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition
                    { CreaturesOnly = true, NonEvolutionOnly = true });

            if (text.StartsWith("Choose a number. Show your hand to your opponent and discard from it each card that has that cost.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.EachPlayerDiscardChosenCost) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("Choose one of your opponent's non-evolution creatures in the battle zone and return it to his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Opponent,
                    RuleZone.BattleZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition
                    { CreaturesOnly = true, NonEvolutionOnly = true });
                return AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.Random, 1, 1,
                    new CardFilterDefinition());
            }

            if (text.StartsWith("Choose up to 2 of your opponent's tapped creatures in the battle zone. If they have total power less than this creature's power, destroy them.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.DestroyIfCombinedPowerBelowSource,
                    RuleController.Opponent, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 0, 2,
                    new CardFilterDefinition { CreaturesOnly = true, TappedOnly = true }, true);

            if (text.Equals("Creatures in the battle zone don't untap at the start of each player's turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreventCreatureUntap) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("Destroy one of your opponent's creatures that has power 5000 or less. When your opponent puts that creature into his graveyard, search your deck.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.DestroyAndRecruitSameCost, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 5000 });

            if (text.Equals("Draw up to 2 cards. Then discard a card from your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 2,
                    ChooseAmount = true
                });
                return AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.Discard, RuleController.Source,
                    RuleZone.Hand, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition());
            }

            if (text.Equals("Each of your creatures in the battle zone gets +1000 power.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm11StaticPower(card, line, 1000, RuleController.Source,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Players can't use the  abilities of their creatures.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreventTapAbilities) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Put a creature that has Dragon in its race from your mana zone into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, DefaultActionTiming(card), RuleController.Source,
                    RuleZone.ManaZone, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition
                    { CreaturesOnly = true, RaceContains = "Dragon" });

            if (text.StartsWith("Reveal the top 4 cards of your deck. Put one of them that has \"blocker\" into your hand, and put the rest on the bottom of your deck",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.TakeOneTopRestBottom, RuleController.Source,
                    RuleZone.Deck, RuleSelectionMode.Top, 0, 4,
                    new CardFilterDefinition(), false);
                ability.PartitionFilter = new CardFilterDefinition
                { CreaturesOnly = true, HasBlocker = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("This creature gets +2000 power for each tapped creature your opponent has in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = Dm10DynamicPowerAbility(line,
                    BattleZoneSelector(RuleController.Opponent,
                        new CardFilterDefinition { CreaturesOnly = true, TappedOnly = true }));
                ability.ValuePerMatchingCard = 2000;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("This creature gets +4000 power and can attack untapped creatures.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddDm11SelfPower(card, line, 4000);
                return AddDm11SelfKeyword(card, line, CardKeyword.AttackUntappedCreature);
            }

            if (text.Equals("This creature gets +4000 power and can't be blocked by any creature that has power 5000 or less.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddDm11SelfPower(card, line, 4000);
                return AddRelationshipRule(card, line, AbilityOperation.PreventBlockBy,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 5000 });
            }

            if (text.StartsWith("This creature gets +4000 power and has \"blocker\".",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddDm11SelfPower(card, line, 4000);
                return AddDm11SelfKeyword(card, line, CardKeyword.Blocker);
            }

            if (text.StartsWith("This creature gets +4000 power and has \"Darkness stealth",
                    StringComparison.OrdinalIgnoreCase))
            {
                card.StealthCivilization = Civilization.Darkness;
                card.HasStealthCivilization = true;
                AddDm11SelfPower(card, line, 4000);
                return AddDm11SelfKeyword(card, line, CardKeyword.Stealth);
            }

            if (text.StartsWith("When each player untaps his cards at the start of his turn, he may then tap any number of cards in his mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var owner = new RuleAbilityDefinition(line.Id, AbilityTiming.StartOfOwnerTurn,
                    AbilityOperation.TapManaPairsToUntapCreatures)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Selector = new RuleSelectorDefinition { Controller = RuleController.Source }
                };
                AppendRule(card, owner);
                var opponent = new RuleAbilityDefinition(line.Id, AbilityTiming.StartOfOpponentTurn,
                    AbilityOperation.TapManaPairsToUntapCreatures)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Selector = new RuleSelectorDefinition { Controller = RuleController.Opponent },
                    Chooser = RuleController.Opponent
                };
                AppendRule(card, opponent);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, destroy up to 2 of your opponent's creatures that have \"blocker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 2,
                    new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true }, true);

            if (text.StartsWith("When you put this creature into the battle zone, each player draws a card for each of his creatures in the battle zone that has \"wave striker.\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.EachPlayerDrawPerMatchingCard)
                {
                    RulesText = line.Text,
                    AmountSelector = new RuleSelectorDefinition
                    {
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            HasRequiredKeyword = true,
                            RequiredKeyword = CardKeyword.WaveStriker
                        }
                    }
                });
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, if your opponent has 3 or fewer shields, draw up to 2 cards.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 2,
                    ChooseAmount = true,
                    Condition = ZoneCountCondition(RuleController.Opponent,
                        RuleZone.ShieldZone, 3, true)
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, look at the top card of your opponent's deck.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.Deck, RuleSelectionMode.Top, 1, 1,
                    new CardFilterDefinition());

            if (text.Equals("When you put this creature into the battle zone, put the top card of each evolution creature from the battle zone into its owner's mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.MoveEvolutionTop, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, EvolutionOnly = true }, false);
                ability.Destination = RuleZone.ManaZone;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, return all Snow Faeries from your graveyard and your mana zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Move, RuleController.Source, RuleZone.Graveyard,
                    RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, Race = "Snow Faerie" }, false);
                ability.Selector.Zones = new[] { RuleZone.Graveyard, RuleZone.ManaZone };
                ability.Destination = RuleZone.Hand;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("You can cast this spell only if your opponent has more shields than you do.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.PlayRestriction,
                    AbilityOperation.RequireCondition)
                {
                    RulesText = line.Text,
                    Condition = CompareZoneCounts(RuleZone.ShieldZone,
                        RuleCountComparison.LessThan)
                });
                return true;
            }

            if (text.Equals("Your light creatures can't be blocked this turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeywordRule(card, line, DefaultActionTiming(card),
                    RuleSelectionMode.All, CardKeyword.Unblockable,
                    new CardFilterDefinition
                    { CreaturesOnly = true, Civilizations = new[] { Civilization.Light } });

            if (text.StartsWith("Your opponent chooses one of his shields for each shield you have. He puts the rest of his shields into his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.ReduceOpponentShieldsToOwnCount) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Whenever any of your opponent's creatures is destroyed this turn, your opponent chooses a card in his mana zone and puts it into his graveyard. Then he chooses one of his shields and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = new AbilityConditionDefinition
                {
                    EventControlledByOpponent = true,
                    EventCardMatchesFilter = true,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                };
                var mana = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureDestroyed,
                    AbilityOperation.Move, RuleController.Opponent, RuleZone.ManaZone,
                    RuleSelectionMode.Choose, 1, 1, new CardFilterDefinition(), false);
                mana.Chooser = RuleController.Opponent;
                mana.Destination = RuleZone.Graveyard;
                mana.Condition = condition;
                var shield = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureDestroyed,
                    AbilityOperation.Move, RuleController.Opponent, RuleZone.ShieldZone,
                    RuleSelectionMode.Choose, 1, 1, new CardFilterDefinition(), false);
                shield.Chooser = RuleController.Opponent;
                shield.Destination = RuleZone.Graveyard;
                shield.Condition = condition;
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.ScheduleAbility)
                {
                    RulesText = line.Text,
                    GrantedAbility = mana,
                    Duration = AbilityDuration.UntilEndOfTurn
                });
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.ScheduleAbility)
                {
                    RulesText = line.Text,
                    GrantedAbility = shield,
                    Duration = AbilityDuration.UntilEndOfTurn
                });
                return true;
            }

            if (text.Equals("Whenever one of your creatures is destroyed during your opponent's turn, you may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = new AbilityConditionDefinition
                {
                    DuringOpponentsTurn = true,
                    EventControlledByOwner = true,
                    EventCardMatchesFilter = true,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                };
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnOtherCreatureDestroyed, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true,
                    Condition = condition
                });
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnDestroyed, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true,
                    Condition = new AbilityConditionDefinition { DuringOpponentsTurn = true }
                });
                return true;
            }

            if (text.Equals("Whenever this creature attacks a creature, this creature gets +3000 power until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAttack,
                    AbilityOperation.ModifyPower, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Value = 3000;
                ability.Condition = new AbilityConditionDefinition
                {
                    PendingAttackTargetsPlayer = true,
                    Invert = true
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever you summon a creature this turn, search your deck. You may take a creature from your deck that has the same name as that creature and put it into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var recruit = new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnAnyCreatureSummonedByPlay,
                    AbilityOperation.SearchSameNameAsEventToBattle)
                {
                    RulesText = line.Text,
                    Condition = new AbilityConditionDefinition
                    {
                        EventControlledByOwner = true,
                        EventCardMatchesFilter = true,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                };
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.ScheduleAbility)
                {
                    RulesText = line.Text,
                    GrantedAbility = recruit,
                    Duration = AbilityDuration.UntilEndOfTurn
                });
                return true;
            }

            return false;
        }

        private static bool AddDm11StaticPower(CardDefinition card, PrintedRuleLine line,
            int value, RuleController controller, CardFilterDefinition filter)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPower, controller, RuleZone.BattleZone,
                RuleSelectionMode.All, 0, int.MaxValue, filter, false);
            ability.Value = value;
            AppendRule(card, ability);
            return true;
        }

        private static void AddDm11SelfPower(CardDefinition card, PrintedRuleLine line, int value)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPower, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
            ability.Value = value;
            AppendRule(card, ability);
        }

        private static bool AddDm11SelfKeyword(CardDefinition card, PrintedRuleLine line,
            CardKeyword keyword)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.GrantKeyword, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
            ability.Keyword = keyword;
            AppendRule(card, ability);
            return true;
        }
    }
}
