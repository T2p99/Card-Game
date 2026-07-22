using System;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm09Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("Choose a race. At the end of this turn, untap all creatures of that race in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddRaceChoice(card, line, timing);
                var delayed = NewSelectionAbility(line, AbilityTiming.EndOfOwnerTurn,
                    AbilityOperation.Untap, RuleController.Either, RuleZone.BattleZone,
                    RuleSelectionMode.All, 0, int.MaxValue, ChosenRaceFilter(), false);
                AppendRule(card, new RuleAbilityDefinition(line.Id, timing,
                    AbilityOperation.ScheduleAbility)
                {
                    RulesText = line.Text,
                    GrantedAbility = delayed,
                    Duration = AbilityDuration.UntilEndOfTurn
                });
                return true;
            }

            if (text.Equals("Choose a race. Creatures of that race can't be blocked by creatures that have power 3000 or less this turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRaceChoice(card, line, DefaultActionTiming(card));
                return AddChosenRaceKeyword(card, line, CardKeyword.UnblockableByPowerAtMost,
                    RuleController.Either, AbilityDuration.UntilEndOfTurn, 3000);
            }

            if (text.Equals("Choose a race. Creatures of that race can't be blocked this turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRaceChoice(card, line, DefaultActionTiming(card));
                return AddChosenRaceKeyword(card, line, CardKeyword.Unblockable,
                    RuleController.Either, AbilityDuration.UntilEndOfTurn);
            }

            if (text.StartsWith("Choose a race. Each creature of that race attacks this turn if able and gets \"power attacker +4000\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRaceChoice(card, line, DefaultActionTiming(card));
                AddChosenRaceKeyword(card, line, CardKeyword.MustAttackIfAble,
                    RuleController.Either, AbilityDuration.UntilEndOfTurn);
                return AddChosenRaceKeyword(card, line, CardKeyword.PowerAttacker,
                    RuleController.Either, AbilityDuration.UntilEndOfTurn, 4000);
            }

            if (text.StartsWith("Choose a race. Each creature of that race gets \"slayer\" until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRaceChoice(card, line, DefaultActionTiming(card));
                return AddChosenRaceKeyword(card, line, CardKeyword.Slayer,
                    RuleController.Either, AbilityDuration.UntilEndOfTurn);
            }

            if (text.Equals("Choose a race. Return up to 3 creatures of that race from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddRaceChoice(card, line, timing);
                return AddMoveRule(card, line, timing, RuleController.Source,
                    RuleZone.Graveyard, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 3, ChosenRaceFilter(), true);
            }

            if (text.Equals("Choose a race. This turn, each creature of that race can attack untapped creatures and can't be blocked while attacking a creature.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRaceChoice(card, line, DefaultActionTiming(card));
                AddChosenRaceKeyword(card, line, CardKeyword.AttackUntappedCreature,
                    RuleController.Either, AbilityDuration.UntilEndOfTurn);
                return AddChosenRaceKeyword(card, line, CardKeyword.UnblockableWhenAttackingCreature,
                    RuleController.Either, AbilityDuration.UntilEndOfTurn);
            }

            if (text.Equals("Choose a race. Until the start of your next turn, each of your creatures in the battle zone of that race gets \"blocker\".",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRaceChoice(card, line, DefaultActionTiming(card));
                return AddChosenRaceKeyword(card, line, CardKeyword.Blocker,
                    RuleController.Source, AbilityDuration.UntilStartOfOwnersNextTurn);
            }

            if (text.Equals("Choose a race. Whenever one of your creatures of that race would be destroyed this turn, return it to your hand instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddRaceChoice(card, line, timing);
                AppendRule(card, new RuleAbilityDefinition(line.Id, timing,
                    AbilityOperation.ReplaceDestroyedRaceWithHandThisTurn) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Choose a race. You may put any number of creatures of that race from your hand into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddRaceChoice(card, line, timing);
                return AddMoveRule(card, line, timing, RuleController.Source,
                    RuleZone.Hand, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    0, int.MaxValue, ChosenRaceFilter(), true);
            }

            if (text.Equals("Choose one of your creatures in the battle zone. Whenever that creature wins a battle this turn, untap it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var untap = NewSelectionAbility(line, AbilityTiming.OnBattleWon,
                    AbilityOperation.Untap, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                var grant = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.GrantAbility, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                grant.GrantedAbility = untap;
                grant.Duration = AbilityDuration.UntilEndOfTurn;
                AppendRule(card, grant);
                return true;
            }

            if (text.Equals("Each creature attacks each turn if able.",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticKeywordAura(card, line, CardKeyword.MustAttackIfAble,
                    RuleController.Either, new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("For each card in your mana zone, put a card from the top of your deck into your mana zone tapped.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Deck, RuleSelectionMode.Top,
                    0, int.MaxValue, new CardFilterDefinition(), false);
                ability.Destination = RuleZone.ManaZone;
                ability.DestinationTapped = true;
                ability.Amount = 0;
                ability.AmountPerMatchingCard = 1;
                ability.AmountSelector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.ManaZone,
                    Mode = RuleSelectionMode.All
                };
                ability.SelectionCountUsesAmount = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Look at the top 4 cards of your deck. Put one of them into your hand, and put the rest on the bottom of your deck in any order.",
                    StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Whenever this creature blocks, look at the top 4 cards of your deck. Put one of them into your hand, and put the rest on the bottom of your deck in any order.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = text.StartsWith("Whenever", StringComparison.OrdinalIgnoreCase)
                    ? AbilityTiming.OnBlock : AbilityTiming.OnCast;
                AppendRule(card, NewSelectionAbility(line, timing,
                    AbilityOperation.TakeOneTopRestBottom, RuleController.Source,
                    RuleZone.Deck, RuleSelectionMode.Top, 1, 4,
                    new CardFilterDefinition(), false));
                return true;
            }

            if (text.Equals("Search a player's deck. You may take a card from that deck and put it into that player's graveyard. Then the player shuffles his deck.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnCast,
                    AbilityOperation.SearchPlayerDeckToGraveyard)
                {
                    RulesText = line.Text
                });
                return true;
            }

            if (text.StartsWith("This creature gets +2000 power for each darkness creature in your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDynamicSelfPower(card, line, 2000,
                    new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition
                        { CreaturesOnly = true, Civilizations = new[] { Civilization.Darkness } }
                    });

            if (text.StartsWith("This creature gets +2000 power for each of your other nature creatures in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDynamicSelfPower(card, line, 2000,
                    new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true,
                            Civilizations = new[] { Civilization.Nature }
                        }
                    });

            if (text.StartsWith("This creature gets +3000 power for each card in your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDynamicSelfPower(card, line, 3000,
                    new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All
                    });

            if (text.Equals("When this creature is destroyed, you may return another creature from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnDestroyed, RuleController.Source,
                    RuleZone.Graveyard, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    { CreaturesOnly = true, OtherThanSource = true }, true);

            if (text.Equals("When this creature would be destroyed, if another Kalute, Vizier of Eternity is in the battle zone, return this creature to your hand instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var replacement = new RuleAbilityDefinition(line.Id,
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    RulesText = line.Text,
                    Destination = RuleZone.Hand,
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true,
                            Name = "Kalute, Vizier of Eternity"
                        },
                        CountAtLeast = 1
                    }
                };
                AppendRule(card, replacement);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, choose a race other than Mecha Del Sol. Each creature of that race gets +4000 power.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddRaceChoice(card, line, AbilityTiming.OnSummon, "Mecha Del Sol");
                var power = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPower, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    ChosenRaceFilter(), false);
                power.Value = 4000;
                AppendRule(card, power);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, look at your opponent's shields. Then put them back where they were.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition());

            if (text.StartsWith("When you put this creature into the battle zone, search your deck. You may take a Whispering Totem from your deck",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    { CreaturesOnly = true, Name = "Whispering Totem" }, true, true);

            if (text.Equals("When you put this creature into the battle zone, your opponent chooses up to 2 creatures in his hand and puts them into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.Choose, 0, 2,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                ability.Chooser = RuleController.Opponent;
                ability.Destination = RuleZone.BattleZone;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever another creature would be destroyed, it stays in the battle zone instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.PreventDestruction, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true }, false));
                return true;
            }

            if (text.Equals("Whenever one of your Armorloids is destroyed, your opponent chooses a card in his mana zone and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureDestroyed,
                    AbilityOperation.Move, RuleController.Opponent, RuleZone.ManaZone,
                    RuleSelectionMode.Choose, 1, 1, new CardFilterDefinition(), false);
                ability.Chooser = RuleController.Opponent;
                ability.Destination = RuleZone.Graveyard;
                ability.Condition = AlliedRaceEventCondition("Armorloid");
                AppendRule(card, ability);
                var self = NewSelectionAbility(line, AbilityTiming.OnDestroyed,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.ManaZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                self.Chooser = RuleController.Opponent;
                self.Destination = RuleZone.Graveyard;
                AppendRule(card, self);
                return true;
            }

            if (text.StartsWith("Whenever this creature attacks, you may choose one of your opponent's untapped creatures that has \"blocker.\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, NewSelectionAbility(line, AbilityTiming.OnAttack,
                    AbilityOperation.RequireChosenBlocker, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition
                    { CreaturesOnly = true, UntappedOnly = true, HasBlocker = true }, true));
                return true;
            }

            if (text.Equals("Whenever this creature becomes blocked, it gets +3000 power until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddValueRule(card, line, AbilityTiming.OnBlocked,
                    AbilityOperation.ModifyPower, RuleSelectionMode.SourceCard,
                    3000, new CardFilterDefinition { CreaturesOnly = true });

            if (text.StartsWith("Whenever this creature becomes blocked, return the blocking creature to its owner's hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnBlocked,
                    AbilityOperation.Move, RuleController.Either, RuleZone.BattleZone,
                    RuleSelectionMode.BlockingCreature, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Destination = RuleZone.Hand;
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever this creature is attacking your opponent and isn't blocked, choose one of your opponent's shields and turn it face up.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnAttackPlayerUnblocked,
                    AbilityOperation.TurnFaceUp, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition());

            if (text.StartsWith("Whenever this creature is attacking your opponent and isn't blocked, search your deck. You may take a creature from your deck",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnAttackPlayerUnblocked,
                    RuleController.Source, RuleZone.Deck, RuleZone.Hand,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true, true);

            if (text.StartsWith("Whenever this creature is attacking your opponent and isn't blocked, search your deck. You may take a spell from your deck",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnAttackPlayerUnblocked,
                    RuleController.Source, RuleZone.Deck, RuleZone.Hand,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { SpellsOnly = true }, true, true);

            if (text.Equals("Whenever this creature is attacking your opponent and isn't blocked, take an extra turn after this one.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnAttackPlayerUnblocked, AbilityOperation.ExtraTurn)
                    { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Whenever this creature is attacking your opponent and isn't blocked, you may choose a creature in the battle zone and return it to its owner's hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnAttackPlayerUnblocked,
                    RuleController.Either, RuleZone.BattleZone, RuleZone.Hand,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);

            if (text.Equals("Whenever this creature is attacking your opponent and isn't blocked, you may destroy one of your opponent's creatures that has power 3000 or less.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnAttackPlayerUnblocked,
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 3000 }, true);

            if (text.Equals("Whenever this creature is attacking your opponent and isn't blocked, your opponent chooses a card in his mana zone and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddOpponentChosenMove(card, line, AbilityTiming.OnAttackPlayerUnblocked,
                    new[] { RuleZone.ManaZone }, RuleZone.Graveyard, false);

            if (text.Equals("Whenever this creature is attacking your opponent and isn't blocked, your opponent chooses one of his creatures and destroys it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAttackPlayerUnblocked,
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Chooser = RuleController.Opponent;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever you put a Ghost into the battle zone, you may destroy one of your opponent's untapped creatures.",
                    StringComparison.OrdinalIgnoreCase))
                return AddAlliedRaceSummonEffect(card, line, "Ghost",
                    AbilityOperation.Destroy, new CardFilterDefinition
                    { CreaturesOnly = true, UntappedOnly = true });

            if (text.Equals("Whenever you put a Guardian into the battle zone, you may choose one of your opponent's creatures in the battle zone and tap it.",
                    StringComparison.OrdinalIgnoreCase))
                return AddAlliedRaceSummonEffect(card, line, "Guardian",
                    AbilityOperation.Tap, new CardFilterDefinition { CreaturesOnly = true });

            if (text.StartsWith("Whenever you put another creature into the battle zone, each Beast Folk in the battle zone gets +2000 power and \"double breaker\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = new AbilityConditionDefinition { EventControlledByOwner = true };
                var filter = new CardFilterDefinition { CreaturesOnly = true, Race = "Beast Folk" };
                var power = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.ModifyPower, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue, filter, false);
                power.Value = 2000;
                power.Condition = condition;
                AppendRule(card, power);
                var breaker = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.GrantKeyword, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue, filter, false);
                breaker.Keyword = CardKeyword.DoubleBreaker;
                breaker.Duration = AbilityDuration.UntilEndOfTurn;
                breaker.Condition = condition;
                AppendRule(card, breaker);
                return true;
            }

            if (text.Equals("Whenever you put another creature into the battle zone, return this creature from the battle zone to your hand.",
                    StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Whenever you put another creature into the battle zone, return this creature to your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.Move, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Destination = RuleZone.Hand;
                ability.Condition = new AbilityConditionDefinition { EventControlledByOwner = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever you put another creature that has Cyber in its race into the battle zone, you may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true,
                    Condition = AlliedRaceContainsEventCondition("Cyber")
                });
                return true;
            }

            if (text.Equals("Whenever your opponent casts a spell, he chooses and discards a card from his hand, then chooses a card in his mana zone and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = new AbilityConditionDefinition { EventControlledByOpponent = true };
                var discard = NewSelectionAbility(line, AbilityTiming.OnSpellCast,
                    AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                discard.Chooser = RuleController.Opponent;
                discard.Condition = condition;
                AppendRule(card, discard);
                var mana = NewSelectionAbility(line, AbilityTiming.OnSpellCast,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.ManaZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                mana.Chooser = RuleController.Opponent;
                mana.Destination = RuleZone.Graveyard;
                mana.Condition = condition;
                AppendRule(card, mana);
                return true;
            }

            if (text.Equals("Whenever your opponent uses the \"shield trigger\" ability of one of his shields, you may add the top card of your deck to your shields face down.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line,
                    AbilityTiming.OnShieldTriggerCastByOpponent, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Deck, RuleSelectionMode.Top,
                    0, 1, new CardFilterDefinition(), true);
                ability.Destination = RuleZone.ShieldZone;
                ability.Condition = new AbilityConditionDefinition { EventControlledByOpponent = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever your opponent would choose a creature in the battle zone, he can't choose this one.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreventOpponentChoice) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("While this creature has power 15000 or more, it has \"Triple breaker",
                    StringComparison.OrdinalIgnoreCase))
                return AddPowerThresholdBreaker(card, line, 15000, CardKeyword.TripleBreaker);

            if (text.StartsWith("While this creature has power 6000 or more, it has \"Double breaker",
                    StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("While this creature has power 6000 or more, it has \"double breaker",
                    StringComparison.OrdinalIgnoreCase))
                return AddPowerThresholdBreaker(card, line, 6000, CardKeyword.DoubleBreaker);

            if (text.Equals("Your opponent chooses one of his creatures in the battle zone and puts it into his mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddOpponentChosenMove(card, line, DefaultActionTiming(card),
                    new[] { RuleZone.BattleZone }, RuleZone.ManaZone, true);

            if (text.Equals("Your opponent chooses one of his creatures in the battle zone or one of his shields and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddOpponentChosenMove(card, line, AbilityTiming.OnCast,
                    new[] { RuleZone.BattleZone, RuleZone.ShieldZone }, RuleZone.Graveyard, false);

            return false;
        }

        private static void AddRaceChoice(CardDefinition card, PrintedRuleLine line,
            AbilityTiming timing, params string[] exclusions)
        {
            AppendRule(card, new RuleAbilityDefinition(line.Id, timing, AbilityOperation.ChooseRace)
            {
                RulesText = line.Text,
                ChoiceExclusions = exclusions ?? Array.Empty<string>()
            });
        }

        private static CardFilterDefinition ChosenRaceFilter() =>
            new CardFilterDefinition { CreaturesOnly = true, UseSourceChosenRace = true };

        private static bool AddChosenRaceKeyword(CardDefinition card, PrintedRuleLine line,
            CardKeyword keyword, RuleController controller, AbilityDuration duration, int value = 0)
        {
            var ability = NewSelectionAbility(line, DefaultActionTiming(card),
                AbilityOperation.GrantKeyword, controller, RuleZone.BattleZone,
                RuleSelectionMode.All, 0, int.MaxValue, ChosenRaceFilter(), false);
            ability.Keyword = keyword;
            ability.Value = value;
            ability.Duration = duration;
            ability.RequiresPreviousSuccess = true;
            AppendRule(card, ability);
            return true;
        }

        private static bool AddDynamicSelfPower(CardDefinition card, PrintedRuleLine line,
            int valuePerCard, RuleSelectorDefinition valueSelector)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPower, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition(), false);
            ability.ValuePerMatchingCard = valuePerCard;
            ability.ValueSelector = valueSelector;
            AppendRule(card, ability);
            return true;
        }

        private static AbilityConditionDefinition AlliedRaceEventCondition(string race) =>
            new AbilityConditionDefinition
            {
                EventControlledByOwner = true,
                EventCardMatchesFilter = true,
                Filter = new CardFilterDefinition { CreaturesOnly = true, Race = race }
            };

        private static AbilityConditionDefinition AlliedRaceContainsEventCondition(string value) =>
            new AbilityConditionDefinition
            {
                EventControlledByOwner = true,
                EventCardMatchesFilter = true,
                Filter = new CardFilterDefinition { CreaturesOnly = true, RaceContains = value }
            };

        private static bool AddAlliedRaceSummonEffect(CardDefinition card, PrintedRuleLine line,
            string race, AbilityOperation operation, CardFilterDefinition targetFilter)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                operation, RuleController.Opponent, RuleZone.BattleZone,
                RuleSelectionMode.Choose, 0, 1, targetFilter, true);
            ability.Condition = AlliedRaceEventCondition(race);
            AppendRule(card, ability);
            return true;
        }

        private static bool AddOpponentChosenMove(CardDefinition card, PrintedRuleLine line,
            AbilityTiming timing, RuleZone[] sourceZones, RuleZone destination, bool creaturesOnly)
        {
            var ability = NewSelectionAbility(line, timing, AbilityOperation.Move,
                RuleController.Opponent, sourceZones[0], RuleSelectionMode.Choose,
                1, 1, new CardFilterDefinition { CreaturesOnly = creaturesOnly }, false);
            ability.Selector.Zones = sourceZones;
            ability.Chooser = RuleController.Opponent;
            ability.Destination = destination;
            AppendRule(card, ability);
            return true;
        }

        private static bool AddPowerThresholdBreaker(CardDefinition card, PrintedRuleLine line,
            int threshold, CardKeyword keyword)
        {
            var ability = SelfStaticKeyword(line, keyword);
            ability.Condition = new AbilityConditionDefinition { SourcePowerAtLeast = threshold };
            AppendRule(card, ability);
            return true;
        }
    }
}
