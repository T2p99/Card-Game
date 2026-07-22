using System;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm08Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("After you cast a spell by using its \"shield trigger\" ability, put it into your hand instead of your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.ReturnShieldTriggerSpellToHand) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Choose a creature in the battle zone and return it to its owner's hand. If it has Dragon in its race, you may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Either,
                    RuleZone.BattleZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true,
                    RequiresPreviousSuccess = true,
                    Condition = PreviousSelectionHasDragon()
                });
                return true;
            }

            if (text.Equals("Choose one of your opponent's creatures in the battle zone and tap it. If it has Dragon in its race, add the top card of your deck to your shields face down.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Tap,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });
                var shield = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Deck, RuleSelectionMode.Top,
                    0, 1, new CardFilterDefinition(), false);
                shield.Destination = RuleZone.ShieldZone;
                shield.RequiresPreviousSuccess = true;
                shield.Condition = PreviousSelectionHasDragon();
                AppendRule(card, shield);
                return true;
            }

            if (text.Equals("Choose one of your opponent's creatures in the battle zone and tap it. Then you may choose one of your creatures in the battle zone. If you do, it can't be blocked this turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Tap,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });
                var grant = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.GrantKeyword,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true }, true);
                grant.Keyword = CardKeyword.Unblockable;
                grant.Duration = AbilityDuration.UntilEndOfTurn;
                // "Then" starts the next independent instruction. The own
                // creature may still be chosen when the first tap instruction
                // could not affect anything; "if you do" refers to this
                // optional choice, not to the earlier tap.
                AppendRule(card, grant);
                return true;
            }

            if (text.Equals("Choose up to 2 of your creatures in the battle zone. At the end of the turn, you may untap them.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.ScheduleSelectedUntapAtEndOfTurn,
                    RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 0, 2,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.StartsWith("Creatures that have power 3000 or more can't attack.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.PreventAttack, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtLeast = 3000 }, false));
                return true;
            }

            if (text.StartsWith("Dragon blocker ", StringComparison.OrdinalIgnoreCase))
                return AddRelationshipRule(card, line, AbilityOperation.AllowBlockAgainst,
                    new CardFilterDefinition { CreaturesOnly = true, RaceContains = "Dragon" });

            if (text.StartsWith("Each creature in the battle zone has \"slayer.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticKeywordAura(card, line, CardKeyword.Slayer,
                    RuleController.Either, new CardFilterDefinition { CreaturesOnly = true });

            if (text.StartsWith("Each creature in the battle zone that has Dragon in its race has \"speed attacker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticKeywordAura(card, line, CardKeyword.SpeedAttacker,
                    RuleController.Either, new CardFilterDefinition
                    { CreaturesOnly = true, RaceContains = "Dragon" });

            if (text.StartsWith("Each of your creatures in the battle zone has \"speed attacker.\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Keyword = CardKeyword.SpeedAttacker;
                ability.Condition = TurboRushCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Search your deck. You may take a creature that has Dragon in its race from your deck and put it into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.TapAbility, RuleController.Source,
                    RuleZone.Deck, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    { CreaturesOnly = true, RaceContains = "Dragon" }, true, true);

                var speed = NewSelectionAbility(line, AbilityTiming.TapAbility,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.PreviousSelection,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
                speed.Keyword = CardKeyword.SpeedAttacker;
                speed.Duration = AbilityDuration.UntilEndOfTurn;
                speed.RequiresPreviousSuccess = true;
                AppendRule(card, speed);

                var destroyAtEnd = NewSelectionAbility(line, AbilityTiming.EndOfOwnerTurn,
                    AbilityOperation.Destroy, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                var grant = NewSelectionAbility(line, AbilityTiming.TapAbility,
                    AbilityOperation.GrantAbility, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.PreviousSelection,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
                grant.GrantedAbility = destroyAtEnd;
                grant.Duration = AbilityDuration.UntilEndOfTurn;
                grant.RequiresPreviousSuccess = true;
                AppendRule(card, grant);
                return true;
            }

            if (text.StartsWith("This creature can attack untapped creatures and has \"Power attacker +4000",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = TurboRushCondition();
                AppendConditionalSelfKeyword(card, line, CardKeyword.AttackUntappedCreature, condition);
                var power = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                power.Value = 4000;
                power.Condition = condition;
                AppendRule(card, power);
                return true;
            }

            if (text.Equals("This creature can't be attacked by any creature that has Dragon in its race.",
                    StringComparison.OrdinalIgnoreCase))
                return AddRelationshipRule(card, line, AbilityOperation.PreventAttackBy,
                    new CardFilterDefinition { CreaturesOnly = true, RaceContains = "Dragon" });

            if (text.Equals("This creature gets +2000 power for each creature in your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPower, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                ability.ValuePerMatchingCard = 2000;
                ability.ValueSelector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.Graveyard,
                    Mode = RuleSelectionMode.All,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("This creature gets +5000 power and has \"double breaker",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = TurboRushCondition();
                var power = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPower, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                power.Value = 5000;
                power.Condition = condition;
                AppendRule(card, power);
                AppendConditionalSelfKeyword(card, line, CardKeyword.DoubleBreaker, condition);
                return true;
            }

            if (text.StartsWith("Until the end of the turn, each of your Dragonoids in the battle zone is an Armored Dragon in addition to its other races",
                    StringComparison.OrdinalIgnoreCase))
            {
                var filter = new CardFilterDefinition { CreaturesOnly = true, Race = "Dragonoid" };
                var race = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.SetRace,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, filter, false);
                race.TextValue = "Armored Dragon";
                race.Duration = AbilityDuration.UntilEndOfTurn;
                AppendRule(card, race);
                AddValueRule(card, line, AbilityTiming.OnCast, AbilityOperation.ModifyPower,
                    RuleSelectionMode.All, 4000, filter);
                AddKeywordRule(card, line, AbilityTiming.OnCast, RuleSelectionMode.All,
                    CardKeyword.DoubleBreaker, filter);
                return true;
            }

            if (text.StartsWith("When this creature attacks, search your deck. You may take a card from your deck and put it into your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAttack, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Deck, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition(), true);
                ability.Destination = RuleZone.Hand;
                ability.ShuffleAfter = true;
                ability.Condition = TurboRushCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When this creature is destroyed, you may put a creature that has Dragon in its race from your hand into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnDestroyed, RuleController.Source,
                    RuleZone.Hand, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    { CreaturesOnly = true, RaceContains = "Dragon" }, true);

            if (text.StartsWith("When this creature would be destroyed, if you have a creature that has Dragon in its race in your mana zone, put this creature into your mana zone instead of destroying it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var replacement = new RuleAbilityDefinition(line.Id, AbilityTiming.ReplacementDestroy,
                    AbilityOperation.Move)
                {
                    RulesText = line.Text,
                    Destination = RuleZone.ManaZone,
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.ManaZone,
                        Filter = new CardFilterDefinition
                        { CreaturesOnly = true, RaceContains = "Dragon" },
                        CountAtLeast = 1
                    }
                };
                AppendRule(card, replacement);
                var returnDragon = NewSelectionAbility(line, AbilityTiming.ReplacementDestroy,
                    AbilityOperation.Move, RuleController.Source, RuleZone.ManaZone,
                    RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true, RaceContains = "Dragon" }, false);
                returnDragon.Destination = RuleZone.Hand;
                returnDragon.RequiresPreviousSuccess = true;
                AppendRule(card, returnDragon);
                return true;
            }

            if (text.Equals("When you put another creature into the battle zone, destroy this creature.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.Destroy, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Condition = new AbilityConditionDefinition { EventControlledByOwner = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, look at your opponent's hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition());

            if (text.Equals("When you put this creature into the battle zone, your opponent may choose a creature in his hand and put it into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRuleForOpponentChoice(card, line);

            if (text.StartsWith("Whenever a player casts a spell, this creature gets +3000 power until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddValueRule(card, line, AbilityTiming.OnSpellCast,
                    AbilityOperation.ModifyPower, RuleSelectionMode.SourceCard,
                    3000, new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Whenever another creature is put into the battle zone, this creature gets +3000 power until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddValueRule(card, line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.ModifyPower, RuleSelectionMode.SourceCard,
                    3000, new CardFilterDefinition { CreaturesOnly = true });

            if (text.StartsWith("Whenever any of your creatures attacks, you may choose one of your opponent's creatures in the battle zone and tap it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAnyCreatureAttack,
                    AbilityOperation.Tap, RuleController.Opponent, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                ability.Condition = new AbilityConditionDefinition { EventControlledByOwner = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever this creature attacks, choose up to 2 cards in your opponent's mana zone. Your opponent puts those cards into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Opponent,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    0, 2, new CardFilterDefinition(), true);

            if (text.Equals("Whenever this creature attacks, you may draw a card for each other tapped creature you have in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnAttack,
                    AbilityOperation.Draw)
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
                        { CreaturesOnly = true, TappedOnly = true, OtherThanSource = true }
                    }
                });
                return true;
            }

            if (text.Equals("Whenever this creature attacks, you may look at 2 of your opponent's shields. Then put them back where they were.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnAttack,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    0, 2, new CardFilterDefinition(), true);

            if (text.Equals("Whenever this creature attacks, you may look at your opponent's hand and at the top card of his deck.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnAttack,
                    AbilityOperation.InspectOpponentHandAndTopDeck)
                {
                    RulesText = line.Text,
                    Optional = true
                });
                return true;
            }

            if (text.Equals("Whenever this creature attacks, your opponent discards his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAttack,
                    AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition(), false);
                ability.Condition = TurboRushCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever this creature becomes blocked, you may put the top card of your deck into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnBlocked, RuleController.Source,
                    RuleZone.Deck, RuleZone.ManaZone, RuleSelectionMode.Top,
                    0, 1, new CardFilterDefinition(), true);

            if (text.Equals("Whenever this creature is attacking your opponent and becomes blocked, it breaks one of your opponent's shields.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnBlocked,
                    AbilityOperation.BreakShield, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Condition = TurboRushCondition();
                ability.Condition.PendingAttackTargetsPlayer = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever this creature is attacking your opponent and isn't blocked, untap all your creatures in the battle zone except Solar Grasses.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAttackPlayerUnblocked,
                    AbilityOperation.Untap, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    { CreaturesOnly = true, ExcludedNames = new[] { "Solar Grass" } }, false);
                ability.Condition = TurboRushCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever this creature is attacking your opponent and isn't blocked, you may destroy a creature.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAttackPlayerUnblocked,
                    AbilityOperation.Destroy, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                ability.Condition = TurboRushCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever you put a Dragonoid or a creature that has Dragon in its race into the battle zone, destroy the creature that has the least power in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.DestroyLowestPower, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Condition = AlliedDragonoidOrDragonEventCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever you put a Dragonoid or a creature that has Dragon in its race into the battle zone, put the top card of your deck into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.Move, RuleController.Source, RuleZone.Deck,
                    RuleSelectionMode.Top, 0, 1, new CardFilterDefinition(), false);
                ability.Destination = RuleZone.ManaZone;
                ability.Condition = AlliedDragonoidOrDragonEventCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever your opponent uses the \"shield trigger\" ability of one of his shields, draw up to 2 cards.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnShieldTriggerCastByOpponent, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 2,
                    Optional = true,
                    ChooseAmount = true,
                    Condition = new AbilityConditionDefinition { EventControlledByOpponent = true }
                });
                return true;
            }

            if (text.Equals("While battling a creature that has Dragon in its race, this creature gets +6000 power.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileBattling, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Value = 6000;
                ability.RelatedFilter = new CardFilterDefinition
                { CreaturesOnly = true, RaceContains = "Dragon" };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Your creatures in the battle zone can't be blocked this turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeywordRule(card, line, AbilityTiming.OnCast,
                    RuleSelectionMode.All, CardKeyword.Unblockable,
                    new CardFilterDefinition { CreaturesOnly = true });

            return false;
        }

        private static AbilityConditionDefinition PreviousSelectionHasDragon() =>
            new AbilityConditionDefinition
            {
                PreviousSelectionMatchesFilter = true,
                Filter = new CardFilterDefinition { CreaturesOnly = true, RaceContains = "Dragon" }
            };

        private static AbilityConditionDefinition TurboRushCondition() =>
            new AbilityConditionDefinition { AnotherCreatureBrokeShieldThisTurn = true };

        private static AbilityConditionDefinition AlliedDragonoidOrDragonEventCondition() =>
            new AbilityConditionDefinition
            {
                EventControlledByOwner = true,
                EventCardMatchesFilter = true,
                Filter = new CardFilterDefinition
                {
                    CreaturesOnly = true,
                    Races = new[] { "Dragonoid" },
                    RaceContains = "Dragon",
                    AnyRaceCriterion = true
                }
            };

        private static bool AddMoveRuleForOpponentChoice(CardDefinition card, PrintedRuleLine line)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                AbilityOperation.Move, RuleController.Opponent, RuleZone.Hand,
                RuleSelectionMode.Choose, 0, 1,
                new CardFilterDefinition { CreaturesOnly = true }, true);
            ability.Chooser = RuleController.Opponent;
            ability.Destination = RuleZone.BattleZone;
            AppendRule(card, ability);
            return true;
        }
    }
}
