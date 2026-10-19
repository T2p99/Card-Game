using System;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm07Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("Choose a card in your opponent's mana zone and return it to his hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, DefaultActionTiming(card), RuleController.Opponent,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.StartsWith("Choose one of your creatures in the battle zone. This turn, it can't be blocked and you ignore any effects that would prevent that creature from attacking your opponent.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeywordRule(card, line, AbilityTiming.OnCast, RuleSelectionMode.Choose,
                    CardKeyword.IgnoreAttackRestrictionsAgainstPlayer,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Choose one of your opponent's water or darkness creatures in the battle zone. Your opponent puts that creature into his mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Opponent,
                    RuleZone.BattleZone, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Water, Civilization.Darkness }
                    });

            if (text.Equals("Destroy any number of your opponent's creatures that have total power 8000 or less.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.PowerBudget,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, true);
                ability.Selector.MaximumCombinedPower = 8000;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Destroy one of your opponent's untapped light or untapped nature creatures.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        UntappedOnly = true,
                        Civilizations = new[] { Civilization.Light, Civilization.Nature }
                    });

            if (text.Equals("Each of your fire creatures gets \"This creature can attack untapped creatures\" until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeywordRule(card, line, DefaultActionTiming(card), RuleSelectionMode.All,
                    CardKeyword.AttackUntappedCreature,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Fire }
                    });

            if (text.Equals("Each player puts a card from his mana zone into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, DefaultActionTiming(card), RuleController.EachPlayer,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.Equals("Look at the top 3 cards of your deck. Then put them back in any order.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, DefaultActionTiming(card),
                    AbilityOperation.LookAtCards, RuleController.Source,
                    RuleZone.Deck, RuleSelectionMode.Top, 3, 3,
                    new CardFilterDefinition(), false);
                ability.ReorderSelectedDeckCards = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Tap all darkness creatures in the battle zone, or tap all fire creatures in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.TapOneCivilizationGroup, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Darkness, Civilization.Fire }
                    }, false));
                return true;
            }

            if (text.Equals("This creature can't attack if you have no cards in your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = SelfStaticKeyword(line, CardKeyword.CannotAttack);
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.Hand,
                    CountAtMost = 0,
                    HasCountAtMost = true
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("This turn, whenever any of your nature creatures is attacking your opponent and becomes blocked, it breaks one of his shields.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, DefaultActionTiming(card),
                    AbilityOperation.BlockedAttackerBreaksShieldThisTurn)
                {
                    RulesText = line.Text,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Civilizations = new[] { Civilization.Nature }
                        }
                    }
                });
                return true;
            }

            if (text.StartsWith("Until the end of the turn, each of your darkness creatures in the battle zone gets +4000 power and \"double breaker.\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var filter = new CardFilterDefinition
                {
                    CreaturesOnly = true,
                    Civilizations = new[] { Civilization.Darkness }
                };
                var timing = DefaultActionTiming(card);
                AddValueRule(card, line, timing, AbilityOperation.ModifyPower,
                    RuleSelectionMode.All, 4000, filter);
                AddKeywordRule(card, line, timing, RuleSelectionMode.All,
                    CardKeyword.DoubleBreaker, filter);
                return AddKeywordRule(card, line, timing, RuleSelectionMode.All,
                    CardKeyword.DestroyAfterBattle, filter);
            }

            if (text.Equals("When one of your shields would be broken, you may destroy this creature instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.ReplacementShieldBreak, AbilityOperation.DestroySourceInstead)
                {
                    RulesText = line.Text,
                    Optional = true
                });
                return true;
            }

            if (text.Equals("When this creature is destroyed, you may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnDestroyed,
                    AbilityOperation.Draw) { RulesText = line.Text, Amount = 1, Optional = true });
                return true;
            }

            if (text.Equals("When this creature would be destroyed, you may return it to your hand instead.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelfDestructionReplacement(card, line, RuleZone.Hand, true);

            if (text.Equals("When this creature would be destroyed, you may return it to your hand instead. If you do, put a card from your hand into your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelfDestructionReplacement(card, line, RuleZone.Hand, true);
                var discard = NewSelectionAbility(line, AbilityTiming.ReplacementDestroy,
                    AbilityOperation.Discard, RuleController.Source, RuleZone.Hand,
                    RuleSelectionMode.Choose, 1, 1, new CardFilterDefinition(), false);
                discard.RequiresPreviousSuccess = true;
                AppendRule(card, discard);
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, choose one of your shields and put it into your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.ShieldZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.StartsWith("When you put this creature into the battle zone, you may add up to 2 cards from your hand to your shields face down.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Hand, RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    0, 2, new CardFilterDefinition(), true);
                var swapBack = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Move, RuleController.Source, RuleZone.ShieldZone,
                    RuleSelectionMode.Choose, 0, 2, new CardFilterDefinition(), false);
                swapBack.Destination = RuleZone.Hand;
                swapBack.RequiresPreviousSuccess = true;
                swapBack.UsePreviousSelectionCountAsAmount = true;
                swapBack.SelectionCountUsesAmount = true;
                AppendRule(card, swapBack);
                return true;
            }

            if (text.Equals("Whenever any of your creatures would be destroyed this turn, put it into your mana zone instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.ReplaceOwnDestructionWithManaThisTurn) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Whenever this creature attacks, discard a card from your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnAttack, AbilityOperation.Discard,
                    RuleController.Source, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.StartsWith("Whenever this creature is attacked, it gets \"slayer\" until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeywordRule(card, line, AbilityTiming.OnAttacked,
                    RuleSelectionMode.SourceCard, CardKeyword.Slayer,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Whenever you draw the card at the start of your turn, you may draw an extra card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.AfterStartTurnDraw,
                    AbilityOperation.Draw) { RulesText = line.Text, Amount = 1, Optional = true });
                return true;
            }

            if (text.Equals("While this creature is tapped, your opponent can't use the \"shield trigger\" ability of his shields.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.PreventShieldTrigger, RuleController.Opponent,
                    RuleZone.None, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition(), false);
                ability.Condition = new AbilityConditionDefinition { SourceTapped = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("While you have no other creatures in the battle zone, this creature has \"Power attacker +4000",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = NoOtherCreaturesCondition();
                AddConditionalSelfPower(card, line, 4000, true, condition);
                return true;
            }

            if (text.StartsWith("While you have no shields, this creature can attack untapped creatures and has \"Power attacker +3000",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = NoShieldsCondition();
                AddConditionalSelfPower(card, line, 3000, false, condition);
                AppendConditionalSelfKeyword(card, line, CardKeyword.AttackUntappedCreature, condition);
                return true;
            }

            if (text.StartsWith("While you have no shields, this creature gets +4000 power and has \"double breaker",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = NoShieldsCondition();
                var power = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                power.Value = 4000;
                power.Condition = condition;
                AppendRule(card, power);
                AppendConditionalSelfKeyword(card, line, CardKeyword.DoubleBreaker, condition);
                return true;
            }

            if (text.StartsWith("While you have no shields, this creature has \"blocker\" and \"At the end of each of your turns, you may untap this creature.\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = NoShieldsCondition();
                AppendConditionalSelfKeyword(card, line, CardKeyword.Blocker, condition);
                AddEndTurnSelfUntap(card, line, true, condition);
                return true;
            }

            if (text.StartsWith("While you have no shields, this creature has \"blocker\".",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendConditionalSelfKeyword(card, line, CardKeyword.Blocker, NoShieldsCondition());
                return true;
            }

            return false;
        }

        private static AbilityConditionDefinition NoShieldsCondition() => new AbilityConditionDefinition
        {
            Controller = RuleController.Source,
            Zone = RuleZone.ShieldZone,
            CountAtMost = 0,
            HasCountAtMost = true
        };

        private static AbilityConditionDefinition NoOtherCreaturesCondition() => new AbilityConditionDefinition
        {
            Controller = RuleController.Source,
            Zone = RuleZone.BattleZone,
            Filter = new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true },
            CountAtMost = 0,
            HasCountAtMost = true
        };

        private static void AddConditionalSelfPower(CardDefinition card, PrintedRuleLine line,
            int value, bool doubleBreaker, AbilityConditionDefinition condition)
        {
            var power = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition(), false);
            power.Value = value;
            power.Condition = condition;
            AppendRule(card, power);
            if (doubleBreaker)
                AppendConditionalSelfKeyword(card, line, CardKeyword.DoubleBreaker, condition);
        }

        private static void AppendConditionalSelfKeyword(CardDefinition card, PrintedRuleLine line,
            CardKeyword keyword, AbilityConditionDefinition condition)
        {
            var ability = SelfStaticKeyword(line, keyword);
            ability.Condition = condition;
            AppendRule(card, ability);
        }
    }
}
