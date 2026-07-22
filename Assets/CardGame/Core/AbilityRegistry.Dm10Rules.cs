using System;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm10Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.StartsWith("At the start of each of your opponent's turns, he chooses one of his creatures in the battle zone that can attack.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.StartOfOpponentTurn,
                    AbilityOperation.GrantKeyword, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true, CanAttackOnly = true }, false);
                ability.Chooser = RuleController.Opponent;
                ability.Keyword = CardKeyword.MustAttackIfAble;
                ability.Duration = AbilityDuration.UntilEndOfTurn;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Choose a creature in the battle zone or a card in either player's mana zone and return it to its owner's hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, DefaultActionTiming(card),
                    AbilityOperation.Move, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Selector.Zones = new[] { RuleZone.BattleZone, RuleZone.ManaZone };
                ability.Destination = RuleZone.Hand;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Choose a creature in your graveyard. Return all creatures that have that name from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddSelectionRule(card, line, timing, AbilityOperation.LookAtCards,
                    RuleController.Source, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });
                var move = NewSelectionAbility(line, timing,
                    AbilityOperation.Move, RuleController.Source, RuleZone.Graveyard,
                    RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    { CreaturesOnly = true, UsePreviousSelectionName = true }, false);
                move.Destination = RuleZone.Hand;
                move.RequiresPreviousSuccess = true;
                AppendRule(card, move);
                return true;
            }

            if (text.Equals("Choose one of your opponent's creatures in the battle zone. It gets \"this creature attacks if able\" until the start of your next turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.GrantKeyword, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Keyword = CardKeyword.MustAttackIfAble;
                ability.Duration = AbilityDuration.UntilStartOfOwnersNextTurn;
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Destroy all creatures that have power 3000 or less. Then look at your opponent's hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddSelectionRule(card, line, timing, AbilityOperation.Destroy,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 3000 });
                AddSelectionRule(card, line, timing, AbilityOperation.LookAtCards,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition());
                return AddSelectionRule(card, line, timing, AbilityOperation.Discard,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 3000 });
            }

            if (text.Equals("Destroy one of your opponent's creatures that has \"blocker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true });

            if (text.Equals("During your opponent's turn, each of your other creatures gets +2000 power.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPower, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true }, false);
                ability.Value = 2000;
                ability.Condition = new AbilityConditionDefinition { DuringOpponentsTurn = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Each of your creatures in the battle zone that has \"silent skill\" has \"speed attacker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticKeywordAura(card, line, CardKeyword.SpeedAttacker,
                    RuleController.Source, new CardFilterDefinition
                    { CreaturesOnly = true, HasSilentSkill = true });

            if (text.StartsWith("Each of your darkness creatures and nature creatures in the battle zone has \"speed attacker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticCivilizationKeyword(card, line, CardKeyword.SpeedAttacker,
                    Civilization.Darkness, Civilization.Nature);

            if (text.StartsWith("Each of your light creatures and fire creatures has \"power attacker +3000.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticCivilizationKeyword(card, line, CardKeyword.PowerAttacker,
                    Civilization.Light, Civilization.Fire, 3000);

            if (text.Equals("Each of your opponent's creatures gets \"This creature attacks if able\" until the start of your next turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.GrantKeyword, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Keyword = CardKeyword.MustAttackIfAble;
                ability.Duration = AbilityDuration.UntilStartOfOwnersNextTurn;
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Each of your water creatures and fire creatures in the battle zone has \"slayer.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticCivilizationKeyword(card, line, CardKeyword.Slayer,
                    Civilization.Water, Civilization.Fire);

            if (text.StartsWith("Each of your water creatures and nature creatures in the battle zone has \"blocker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticCivilizationKeyword(card, line, CardKeyword.Blocker,
                    Civilization.Water, Civilization.Nature);

            if (text.Equals("Each player chooses one of his creatures in the battle zone. Tap the rest of the creatures in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.TapAllExceptOnePerPlayer) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Each player discards a card at random from his hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.Discard, RuleController.EachPlayer,
                    RuleZone.Hand, RuleSelectionMode.Random, 1, 1,
                    new CardFilterDefinition());

            if (text.StartsWith("Each player puts all the cards from his mana zone into his hand and, at the same time, puts all the cards from his hand into his mana zone tapped.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.SwapHandAndMana)
                {
                    RulesText = line.Text,
                    Selector = new RuleSelectorDefinition { Controller = RuleController.EachPlayer }
                });
                return true;
            }

            if (text.Equals("Put a card from your mana zone into your hand. Then put a card from your hand into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());
                var second = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.Move, RuleController.Source, RuleZone.Hand,
                    RuleSelectionMode.Choose, 1, 1, new CardFilterDefinition(), false);
                second.Destination = RuleZone.ManaZone;
                // "Then" fixes the written order but is not an "if you do"
                // dependency. Resolve this instruction even when there was no
                // mana card to move (for example, when Siren Concerto is cast
                // as a shield trigger with an empty mana zone).
                AppendRule(card, second);
                return true;
            }

            if (text.Equals("This creature breaks 2 of your opponent's shields.",
                    StringComparison.OrdinalIgnoreCase) ||
                text.Equals("This creature breaks one of your opponent's shields.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var amount = text.IndexOf(" 2 ", StringComparison.Ordinal) >= 0 ? 2 : 1;
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.BreakShield, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    amount, amount, new CardFilterDefinition());
            }

            if (text.Equals("This creature gets +1000 power for each of your other untapped creatures in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicPower(card, line, 1000,
                    BattleZoneSelector(RuleController.Source,
                        new CardFilterDefinition
                        { CreaturesOnly = true, OtherThanSource = true, UntappedOnly = true }));

            if (text.Equals("This creature gets +2000 power for each of your other untapped creatures in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicPower(card, line, 2000,
                    BattleZoneSelector(RuleController.Source,
                        new CardFilterDefinition
                        { CreaturesOnly = true, OtherThanSource = true, UntappedOnly = true }));

            if (text.Equals("This creature gets +1000 power for each tapped card in your opponent's mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicPower(card, line, 1000,
                    ManaSelector(RuleController.Opponent,
                        new CardFilterDefinition { TappedOnly = true }));

            if (text.Equals("This creature gets +2000 power for each tapped card in your opponent's mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicPower(card, line, 2000,
                    ManaSelector(RuleController.Opponent,
                        new CardFilterDefinition { TappedOnly = true }));

            if (text.Equals("This creature gets +1000 power for each water card in your opponent's mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicPower(card, line, 1000,
                    ManaSelector(RuleController.Opponent,
                        new CardFilterDefinition
                        { Civilizations = new[] { Civilization.Water } }));

            if (text.Equals("This creature gets +2000 power for each shield you and your opponent have.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicPower(card, line, 2000,
                    new RuleSelectorDefinition
                    {
                        Controller = RuleController.Either,
                        Zone = RuleZone.ShieldZone,
                        Mode = RuleSelectionMode.All
                    });

            if (text.Equals("This creature gets +3000 power for each civilization in your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = Dm10DynamicPowerAbility(line,
                    ManaSelector(RuleController.Source, new CardFilterDefinition()));
                ability.ValuePerDistinctCivilization = 3000;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("This creature gets +5000 power for each of your other creatures in the battle zone that has Dragon in its race.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicPower(card, line, 5000,
                    BattleZoneSelector(RuleController.Source,
                        new CardFilterDefinition
                        { CreaturesOnly = true, OtherThanSource = true, RaceContains = "Dragon" }));

            if (text.Equals("When this creature would be destroyed, shuffle it into your deck instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var replacement = new RuleAbilityDefinition(line.Id,
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    RulesText = line.Text,
                    Destination = RuleZone.Deck,
                    ShuffleAfter = true
                };
                AppendRule(card, replacement);
                return true;
            }

            if (text.Equals("When this creature would be discarded from your hand during your opponent's turn, you may put it into the battle zone instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.ReplacementDiscard, AbilityOperation.Move)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Destination = RuleZone.BattleZone,
                    Condition = new AbilityConditionDefinition { DuringOpponentsTurn = true }
                });
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, add the top card of your deck to your shields face down. Then look at one of your opponent's shields.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.ShieldZone, RuleSelectionMode.Top,
                    1, 1, new CardFilterDefinition());
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());
            }

            if (text.Equals("When you put this creature into the battle zone, choose one of your opponent's shields and put it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.StartsWith("When you put this creature into the battle zone, choose one of your shields and put it into your hand. (You can use the \"shield trigger\" ability",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.MoveShieldToHandAllowTrigger, RuleController.Source,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.StartsWith("When you put this creature into the battle zone, destroy all other creatures that have 6000 power. Take an extra turn after this one.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    { CreaturesOnly = true, OtherThanSource = true, PowerExactly = 6000 });
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.ExtraTurn) { RulesText = line.Text });
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.SetForcedLossAfterExtraTurn) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, destroy one of your creatures and put a card from your mana zone into your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());
                var destroyOpponent = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                destroyOpponent.Chooser = RuleController.Opponent;
                AppendRule(card, destroyOpponent);
                var manaOpponent = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.ManaZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                manaOpponent.Chooser = RuleController.Opponent;
                manaOpponent.Destination = RuleZone.Graveyard;
                AppendRule(card, manaOpponent);
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, destroy one of your opponent's creatures that has \"blocker.\" Then put the top card of your deck into your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true });
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.ManaZone, RuleSelectionMode.Top,
                    1, 1, new CardFilterDefinition());
            }

            if (text.StartsWith("When you put this creature into the battle zone, destroy one of your opponent's creatures that has \"blocker.\" Then your opponent discards a card at random",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true });
                return AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Discard,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.Random,
                    1, 1, new CardFilterDefinition());
            }

            if (text.Equals("When you put this creature into the battle zone, if you have 5 or more shields, add the top card of your deck to your shields face down.",
                    StringComparison.OrdinalIgnoreCase))
                return AddConditionalSummonMove(card, line, RuleController.Source, 5,
                    RuleCountComparison.GreaterThanOrEqual, RuleController.Source,
                    RuleZone.Deck, RuleZone.ShieldZone, RuleSelectionMode.Top,
                    new CardFilterDefinition());

            if (text.Equals("When you put this creature into the battle zone, if you have 5 or more shields, untap all the cards in your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Untap, RuleController.Source, RuleZone.ManaZone,
                    RuleSelectionMode.All, 0, int.MaxValue, new CardFilterDefinition(), false);
                ability.Condition = ZoneCountCondition(RuleController.Source,
                    RuleZone.ShieldZone, 5, false);
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, if your opponent has 2 or fewer shields, you may destroy one of your opponent's creatures that has power 3000 or less.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 3000 }, true);
                ability.Condition = ZoneCountCondition(RuleController.Opponent,
                    RuleZone.ShieldZone, 2, true);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, if your opponent has 3 or fewer shields, he discards a card at random from his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.Random, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Condition = ZoneCountCondition(RuleController.Opponent,
                    RuleZone.ShieldZone, 3, true);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, look at one of your opponent's shields. Then your opponent discards a card at random from his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition());
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.Random, 1, 1,
                    new CardFilterDefinition());
            }

            if (text.Equals("When you put this creature into the battle zone, look at up to 5 cards from the top of your deck and put them back in any order.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Source,
                    RuleZone.Deck, RuleSelectionMode.Top, 0, 5,
                    new CardFilterDefinition(), true);
                ability.Amount = 5;
                ability.SelectionMaximumUsesAmount = true;
                ability.ChooseSelectionAmount = true;
                ability.ReorderSelectedDeckCards = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, look at your opponent's hand. You may choose a card in it and put it into his mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition());
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Opponent,
                    RuleZone.Hand, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition(), true);
                var returnMana = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.ManaZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                returnMana.Destination = RuleZone.Hand;
                returnMana.RequiresPreviousSuccess = true;
                AppendRule(card, returnMana);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, put all the cards from your hand into your mana zone. Then put that many cards from your mana zone into your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Hand, RuleZone.ManaZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition());
                var returnCards = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.Move, RuleController.Source, RuleZone.ManaZone,
                    RuleSelectionMode.Choose, 0, int.MaxValue,
                    new CardFilterDefinition(), false);
                returnCards.Destination = RuleZone.Hand;
                returnCards.RequiresPreviousSuccess = true;
                returnCards.UsePreviousSelectionCountAsAmount = true;
                returnCards.SelectionCountUsesAmount = true;
                AppendRule(card, returnCards);
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, put all the cards from your mana zone into your hand and, at the same time, put all the cards from your hand into your mana zone tapped.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.SwapHandAndMana)
                {
                    RulesText = line.Text,
                    Selector = new RuleSelectorDefinition { Controller = RuleController.Source }
                });
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, put all your shields into your hand. (You can use the \"shield trigger\" abilities",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.MoveShieldToHandAllowTrigger, RuleController.Source,
                    RuleZone.ShieldZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition());

            if (text.Equals("When you put this creature into the battle zone, put the top card of your deck into your mana zone. Then add the top card of your deck to your shields face down.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.ManaZone, RuleSelectionMode.Top,
                    1, 1, new CardFilterDefinition());
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.ShieldZone, RuleSelectionMode.Top,
                    1, 1, new CardFilterDefinition());
            }

            if (text.Equals("When you put this creature into the battle zone, return all tapped cards from your mana zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition { TappedOnly = true });

            if (text.Equals("When you put this creature into the battle zone, return all the cards from your mana zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition());

            if (text.Equals("Whenever an opponent's creature attacks, this creature blocks if able.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.BlockIfAble);

            if (text.Equals("Whenever one of your creatures attacks, your opponent discards a card at random from their hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAnyCreatureAttack,
                    AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.Random, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Condition = new AbilityConditionDefinition { EventControlledByOwner = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever one of your creatures is attacked, you may discard a card from your hand. If you do, that creature gets +3000 power",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = new AbilityConditionDefinition { EventControlledByOwner = true };
                var discard = NewSelectionAbility(line, AbilityTiming.OnAnyCreatureAttacked,
                    AbilityOperation.Discard, RuleController.Source,
                    RuleZone.Hand, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition(), true);
                discard.Condition = condition;
                AppendRule(card, discard);
                var power = NewSelectionAbility(line, AbilityTiming.OnAnyCreatureAttacked,
                    AbilityOperation.ModifyPower, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.EventCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                power.Value = 3000;
                power.Condition = condition;
                power.RequiresPreviousSuccess = true;
                AppendRule(card, power);
                return true;
            }

            if (text.Equals("Whenever one of your creatures that has \"silent skill\" would be destroyed, put it into your hand instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.ReplacementDestroy,
                    AbilityOperation.Move, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    { CreaturesOnly = true, HasSilentSkill = true }, false);
                ability.Destination = RuleZone.Hand;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever one of your shields would be broken, you may discard 2 cards from your hand instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.ReplacementShieldBreak,
                    AbilityOperation.Discard, RuleController.Source,
                    RuleZone.Hand, RuleSelectionMode.Choose, 2, 2,
                    new CardFilterDefinition(), true);
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.Hand,
                    CountAtLeast = 2
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever this creature attacks a creature that has power 6000 or more, this creature gets +9000 power until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileBattling, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Value = 9000;
                ability.RelatedFilter = new CardFilterDefinition
                { CreaturesOnly = true, PowerAtLeast = 6000 };
                ability.Condition = new AbilityConditionDefinition { SourceIsBattleAttacker = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever this creature blocks, choose one of your creatures in the battle zone. Untap it after the battle.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnBlock,
                    AbilityOperation.ScheduleSelectedUntapAfterBattle,
                    RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.StartsWith("Whenever this creature would break one of your opponent's shields, your opponent shows that shield to you instead of putting it into his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.ReplacementShieldBreak,
                    AbilityOperation.AttackerCastsShieldTrigger) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("Whenever you put a Cyber Virus into the battle zone, you may discard up to 2 cards from your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var condition = AlliedRaceEventCondition("Cyber Virus");
                var discard = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.Discard, RuleController.Source,
                    RuleZone.Hand, RuleSelectionMode.Choose, 0, 2,
                    new CardFilterDefinition(), true);
                discard.Condition = condition;
                AppendRule(card, discard);
                var bounce = NewSelectionAbility(line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.Move, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 2,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                bounce.Destination = RuleZone.Hand;
                bounce.Condition = condition;
                bounce.RequiresPreviousSuccess = true;
                bounce.UsePreviousSelectionCountAsAmount = true;
                bounce.SelectionCountUsesAmount = true;
                AppendRule(card, bounce);
                return true;
            }

            if (text.Equals("While attacking, this creature gets +1000 power for each of your other tapped creatures in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicAttackPower(card, line, 1000,
                    BattleZoneSelector(RuleController.Source,
                        new CardFilterDefinition
                        { CreaturesOnly = true, OtherThanSource = true, TappedOnly = true }));

            if (text.Equals("While attacking, this creature gets +2000 power for each of your other tapped creatures in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicAttackPower(card, line, 2000,
                    BattleZoneSelector(RuleController.Source,
                        new CardFilterDefinition
                        { CreaturesOnly = true, OtherThanSource = true, TappedOnly = true }));

            if (text.Equals("While attacking, this creature gets +1000 power for each tapped card in your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicAttackPower(card, line, 1000,
                    ManaSelector(RuleController.Source,
                        new CardFilterDefinition { TappedOnly = true }));

            if (text.Equals("While attacking, this creature gets +2000 power for each tapped card in your mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddDm10DynamicAttackPower(card, line, 2000,
                    ManaSelector(RuleController.Source,
                        new CardFilterDefinition { TappedOnly = true }));

            if (text.Equals("While battling a fire creature, this creature gets +4000 power.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileBattling, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Value = 4000;
                ability.RelatedFilter = new CardFilterDefinition
                { CreaturesOnly = true, Civilizations = new[] { Civilization.Fire } };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("While this creature is tapped during your opponent's turn, each of his creatures attacks if able.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Keyword = CardKeyword.MustAttackIfAble;
                ability.Condition = TappedDuringOpponentTurnCondition();
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("While this creature is tapped during your opponent's turn, if it hasn't been attacked that turn, your opponent's creatures must attack it if able.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreferAttackSource)
                {
                    RulesText = line.Text,
                    Condition = new AbilityConditionDefinition
                    {
                        SourceTapped = true,
                        DuringOpponentsTurn = true,
                        SourceNotAttackedThisTurn = true
                    }
                });
                return true;
            }

            if (text.StartsWith("While this creature is tapped, each of your other creatures has \"power attacker +1500.\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true }, false);
                ability.Keyword = CardKeyword.PowerAttacker;
                ability.Value = 1500;
                ability.Condition = new AbilityConditionDefinition { SourceTapped = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("You can't untap the cards in your mana zone at the start of each of your turns.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreventManaUntap) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("You may add a card from your hand to your shields face down. If you do, choose one of your shields and put it into your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var timing = DefaultActionTiming(card);
                AddMoveRule(card, line, timing, RuleController.Source,
                    RuleZone.Hand, RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition(), true);
                var swap = NewSelectionAbility(line, timing,
                    AbilityOperation.Move, RuleController.Source,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                swap.Destination = RuleZone.Hand;
                swap.RequiresPreviousSuccess = true;
                AppendRule(card, swap);
                return true;
            }

            if (text.StartsWith("You may choose a creature in the battle zone and put it into its owner's mana zone. If you do, choose a non-evolution creature in that player's mana zone",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Either,
                    RuleZone.BattleZone, RuleZone.ManaZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition { CreaturesOnly = true }, true);
                var recruit = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.Move, RuleController.CardOwner,
                    RuleZone.ManaZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        NonEvolutionOnly = true,
                        CostAtMostPreviousSelectionOwnerManaCount = true
                    }, false);
                recruit.Destination = RuleZone.BattleZone;
                recruit.RequiresPreviousSuccess = true;
                AppendRule(card, recruit);
                return true;
            }

            if (text.StartsWith("You may destroy a creature. If you do, its owner reveals cards from the top of his deck until he reveals a non-evolution creature.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.DestroyAndRecruitFromDeck, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);

            if (text.Equals("You may destroy one of your creatures. If you do, choose a creature in your hand that costs the same as or less than the number of cards in your mana zone and put it into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.Destroy, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                var summon = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.Move, RuleController.Source,
                    RuleZone.Hand, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition
                    { CreaturesOnly = true, CostAtMostManaCount = true }, false);
                summon.Destination = RuleZone.BattleZone;
                summon.RequiresPreviousSuccess = true;
                AppendRule(card, summon);
                return true;
            }

            if (text.Equals("Your creatures that have \"silent skill\" are put into the battle zone tapped.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ForceEnterTapped, RuleController.Source,
                    RuleZone.None, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    { CreaturesOnly = true, HasSilentSkill = true }, false));
                return true;
            }

            if (text.Equals("Your opponent can't tap this creature.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreventOpponentTap) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Your opponent chooses one of his creatures and destroys it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, DefaultActionTiming(card),
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Chooser = RuleController.Opponent;
                AppendRule(card, ability);
                return true;
            }

            return false;
        }

        private static bool AddStaticCivilizationKeyword(CardDefinition card, PrintedRuleLine line,
            CardKeyword keyword, Civilization first, Civilization second, int value = 0)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.GrantKeyword, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                new CardFilterDefinition
                { CreaturesOnly = true, Civilizations = new[] { first, second } }, false);
            ability.Keyword = keyword;
            ability.Value = value;
            AppendRule(card, ability);
            return true;
        }

        private static RuleSelectorDefinition BattleZoneSelector(RuleController controller,
            CardFilterDefinition filter) => new RuleSelectorDefinition
        {
            Controller = controller,
            Zone = RuleZone.BattleZone,
            Mode = RuleSelectionMode.All,
            Filter = filter
        };

        private static RuleSelectorDefinition ManaSelector(RuleController controller,
            CardFilterDefinition filter) => new RuleSelectorDefinition
        {
            Controller = controller,
            Zone = RuleZone.ManaZone,
            Mode = RuleSelectionMode.All,
            Filter = filter
        };

        private static RuleAbilityDefinition Dm10DynamicPowerAbility(PrintedRuleLine line,
            RuleSelectorDefinition selector)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPower, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition(), false);
            ability.ValueSelector = selector;
            return ability;
        }

        private static bool AddDm10DynamicPower(CardDefinition card, PrintedRuleLine line,
            int value, RuleSelectorDefinition selector)
        {
            var ability = Dm10DynamicPowerAbility(line, selector);
            ability.ValuePerMatchingCard = value;
            AppendRule(card, ability);
            return true;
        }

        private static bool AddDm10DynamicAttackPower(CardDefinition card, PrintedRuleLine line,
            int value, RuleSelectorDefinition selector)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition(), false);
            ability.ValuePerMatchingCard = value;
            ability.ValueSelector = selector;
            AppendRule(card, ability);
            return true;
        }

        private static AbilityConditionDefinition ZoneCountCondition(RuleController controller,
            RuleZone zone, int count, bool atMost) => new AbilityConditionDefinition
        {
            Controller = controller,
            Zone = zone,
            CountAtLeast = atMost ? 0 : count,
            CountAtMost = count,
            HasCountAtMost = atMost
        };

        private static bool AddConditionalSummonMove(CardDefinition card, PrintedRuleLine line,
            RuleController conditionController, int count, RuleCountComparison comparison,
            RuleController moveController, RuleZone source, RuleZone destination,
            RuleSelectionMode mode, CardFilterDefinition filter)
        {
            var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                AbilityOperation.Move, moveController, source, mode,
                1, 1, filter, false);
            ability.Destination = destination;
            ability.Condition = ZoneCountCondition(conditionController,
                RuleZone.ShieldZone, count,
                comparison == RuleCountComparison.LessThanOrEqual);
            AppendRule(card, ability);
            return true;
        }

        private static AbilityConditionDefinition TappedDuringOpponentTurnCondition() =>
            new AbilityConditionDefinition { SourceTapped = true, DuringOpponentsTurn = true };
    }
}
