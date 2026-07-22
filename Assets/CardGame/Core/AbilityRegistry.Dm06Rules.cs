using System;
using System.Text.RegularExpressions;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm06Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("At the end of each of your turns, if this is your only creature in the battle zone, destroy it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.EndOfOwnerTurn,
                    AbilityOperation.Destroy, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Filter = new CardFilterDefinition { CreaturesOnly = true },
                    CountAtMost = 1,
                    HasCountAtMost = true
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("At the end of your turn, return this creature to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.EndOfOwnerTurn,
                    RuleController.Source, RuleZone.BattleZone, RuleZone.Hand,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Choose a shield and look at it. Then put it back where it was.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.LookAtCards, RuleController.Either,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.Equals("Choose one of your opponent’s evolution creatures in the battle zone and put the top card of that creature into your opponent’s mana zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.MoveEvolutionTop, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true, EvolutionOnly = true })
                    && SetLastDestination(card, RuleZone.ManaZone);

            if (text.Equals("Choose up to 3 of your opponent's shields and put them into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Opponent,
                    RuleZone.ShieldZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    0, 3, new CardFilterDefinition(), true);

            if (text.StartsWith("Crew breaker—", StringComparison.OrdinalIgnoreCase))
                return true; // Parsed by the core keyword registry before this method is reached.

            if (text.Equals("Destroy any number of your creatures. Then draw that many cards.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, true);
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    RequiresPreviousSuccess = true,
                    UsePreviousSelectionCountAsAmount = true
                });
                return true;
            }

            if (text.Equals("Destroy one of your opponent’s creatures that has “blocker.”",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true });

            if (text.Equals("During your opponent's turn, if this creature would be discarded from your hand, put it into the battle zone instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.ReplacementDiscard,
                    AbilityOperation.Move)
                {
                    RulesText = line.Text,
                    Destination = RuleZone.BattleZone,
                    Condition = new AbilityConditionDefinition { DuringOpponentsTurn = true }
                });
                return true;
            }

            if (text.StartsWith("Each of your creatures in the battle zone gets +8000 power and \"triple breaker\" until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddValueRule(card, line, AbilityTiming.OnCast, AbilityOperation.ModifyPower,
                    RuleSelectionMode.All, 8000, new CardFilterDefinition { CreaturesOnly = true });
                return AddKeywordRule(card, line, AbilityTiming.OnCast, RuleSelectionMode.All,
                    CardKeyword.TripleBreaker, new CardFilterDefinition { CreaturesOnly = true });
            }

            var shareTapAbility = Regex.Match(text,
                @"^Each of your (light|water|darkness|fire|nature) creatures may tap instead of attacking to use this creature's tap ability\.$",
                RegexOptions.IgnoreCase);
            if (shareTapAbility.Success)
            {
                AddKeyword(card, CardKeyword.TapAbility);
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ShareTapAbility, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { ParseCivilization(shareTapAbility.Groups[1].Value) }
                    }, false);
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Each of your Ghosts in the battle zone has \"slayer.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticKeywordAura(card, line, CardKeyword.Slayer,
                    RuleController.Source, new CardFilterDefinition
                    { CreaturesOnly = true, Race = "Ghost" });

            if (text.Equals("Evolution creatures are put into the battle zone tapped.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ForceEvolutionEnterTapped, RuleController.Either,
                    RuleZone.None, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, EvolutionOnly = true }, false);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("If your opponent has more shields than you do, add the top card of your deck to your shields face down.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Source, RuleZone.Deck, RuleSelectionMode.Top,
                    0, 1, new CardFilterDefinition(), false);
                ability.Destination = RuleZone.ShieldZone;
                ability.Condition = CompareZoneCounts(RuleZone.ShieldZone, RuleCountComparison.LessThan);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Look at your opponent's hand. He discards all darkness spells from it.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.LookAtCards, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition());
                return AddSelectionRule(card, line, AbilityTiming.OnCast,
                    AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        SpellsOnly = true,
                        Civilizations = new[] { Civilization.Darkness }
                    });
            }

            if (text.Equals("One of your creatures in the battle zone that has \"blocker\" gets +4000 power until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
                return AddValueRule(card, line, AbilityTiming.OnCast, AbilityOperation.ModifyPower,
                    RuleSelectionMode.Choose, 4000,
                    new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true });

            if (text.Equals("Return a darkness creature from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, DefaultActionTiming(card), RuleController.Source,
                    RuleZone.Graveyard, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition
                    { CreaturesOnly = true, Civilizations = new[] { Civilization.Darkness } });

            if (text.Equals("When you put this creature into the battle zone, you may return a Survivor from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon,
                    RuleController.Source, RuleZone.Graveyard, RuleZone.Hand,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        HasRequiredKeyword = true,
                        RequiredKeyword = CardKeyword.Survivor
                    }, true);

            if (text.Equals("Return any number of your creatures from the battle zone to your hand. Then you may choose that many of your opponent's creatures in the battle zone and return them to your opponent's hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Source,
                    RuleZone.BattleZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, true);
                var followup = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true }, true);
                followup.Destination = RuleZone.Hand;
                followup.RequiresPreviousSuccess = true;
                followup.UsePreviousSelectionCountAsAmount = true;
                followup.SelectionCountUsesAmount = true;
                AppendRule(card, followup);
                return true;
            }

            if (text.Equals("Search your deck. You may take any number of cards from your deck, show those cards to your opponent, and put them into your hand. Then shuffle your deck.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Source,
                    RuleZone.Deck, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition(), true, true);

            if (text.Equals("Search your opponent's deck. Take up to 2 cards from his deck and put them into his graveyard. Then your opponent shuffles his deck.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnCast, RuleController.Opponent,
                    RuleZone.Deck, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    0, 2, new CardFilterDefinition(), true, true);

            if (text.Equals("Tap all creatures in the battle zone that don't have \"blocker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Tap,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        HasForbiddenKeyword = true,
                        ForbiddenKeyword = CardKeyword.Blocker
                    });

            if (text.Equals("This creature can attack only creatures that have \"blocker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddRelationshipRule(card, line, AbilityOperation.RequireAttackTarget,
                    new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true });

            if (text.Equals("When this creature battles, destroy it after the battle.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.DestroyAfterBattle);

            if (text.Equals("When this creature is destroyed, each player discards his hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnDestroyed,
                    AbilityOperation.Discard, RuleController.EachPlayer, RuleZone.Hand,
                    RuleSelectionMode.All, 0, int.MaxValue, new CardFilterDefinition());

            if (text.Equals("When you put this creature into the battle zone, choose a card in your opponent's mana zone and return it to his hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Opponent,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition());

            if (text.Equals("When you put this creature into the battle zone, count the cards in your hand, shuffle those cards into your deck, then draw that many cards.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.RefreshOwnHand) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, search your deck. You may take a Survivor from your deck, show that Survivor to your opponent, and put it into your hand. Then shuffle your deck.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    { CreaturesOnly = true, HasRequiredKeyword = true, RequiredKeyword = CardKeyword.Survivor },
                    true, true);

            if (text.Equals("When you put this creature into the battle zone, you may draw a card for each Survivor in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Amount = 0,
                    AmountPerMatchingCard = 1,
                    AmountSelector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Either,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition
                        { CreaturesOnly = true, HasRequiredKeyword = true, RequiredKeyword = CardKeyword.Survivor }
                    }
                });
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, you may look at your shields. Then put them back where they were.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.LookAtCards, RuleController.Source,
                    RuleZone.ShieldZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition(), true);

            if (text.Equals("Whenever an opponent's creature would break a shield, you choose the shield instead of your opponent.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.DefendingPlayerChoosesShield) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Whenever this creature becomes blocked, no battle happens. (Both creatures stay tapped.)",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnBlocked,
                    AbilityOperation.PreventBattle) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Whenever this creature blocks, no battle happens. (Both creatures stay tapped.)",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnBlock,
                    AbilityOperation.PreventBattle) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("Whenever this creature would break a shield, your opponent puts that shield into his graveyard instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.ReplacementShieldBreak, AbilityOperation.Move)
                {
                    RulesText = line.Text,
                    Destination = RuleZone.Graveyard
                });
                return true;
            }

            if (text.StartsWith("Whenever your opponent summons a creature or casts a spell, this creature gets \"blocker\" until the end of the turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnOpponentCardPlayed,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Keyword = CardKeyword.Blocker;
                ability.Duration = AbilityDuration.UntilEndOfTurn;
                ability.Condition = new AbilityConditionDefinition { EventControlledByOpponent = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("While you have any other untapped creatures in the battle zone, this creature can't attack.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = SelfStaticKeyword(line, CardKeyword.CannotAttack);
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Filter = new CardFilterDefinition
                    { CreaturesOnly = true, OtherThanSource = true, UntappedOnly = true },
                    CountAtLeast = 1
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("While you have at least one other fire creature in the battle zone, this creature has \"Power attacker +3000",
                    StringComparison.OrdinalIgnoreCase))
                return AddConditionalPowerAttacker(card, line, Civilization.Fire, 3000, false);

            if (text.StartsWith("While you have no other fire creatures in the battle zone, this creature has \"power attacker +3000\" and \"double breaker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddConditionalPowerAttacker(card, line, Civilization.Fire, 3000, true);

            if (text.Equals("You can summon this creature only if you have cast a spell this turn.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.PlayRestriction,
                    AbilityOperation.RequireCondition)
                {
                    RulesText = line.Text,
                    Condition = new AbilityConditionDefinition { SpellWasCastThisTurn = true }
                });
                return true;
            }

            if (text.StartsWith("Your creatures that have Dragon in their race each cost 2 less to summon.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyCost, RuleController.Source,
                    RuleZone.None, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        RaceContains = "Dragon",
                        ExcludedRaces = new[] { "Dragonoid" }
                    }, false);
                ability.Value = -2;
                ability.MinimumValue = 2;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Your opponent chooses one of his creatures in the battle zone and destroys it.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.Destroy, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true }, false,
                    RuleController.Opponent);

            if (text.Equals("Your opponent chooses one of his creatures in the battle zone or a card in his mana zone and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast, AbilityOperation.Move,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition(), false);
                ability.Selector.Zones = new[] { RuleZone.BattleZone, RuleZone.ManaZone };
                ability.Chooser = RuleController.Opponent;
                ability.Destination = RuleZone.Graveyard;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Your opponent's attacking creatures attack Mystery Totems if able.",
                    StringComparison.OrdinalIgnoreCase))
                return AddRelationshipRule(card, line, AbilityOperation.PreferAttackRace,
                    new CardFilterDefinition { CreaturesOnly = true, Race = "Mystery Totem" });

            return false;
        }

        private static bool AddConditionalPowerAttacker(CardDefinition card, PrintedRuleLine line,
            Civilization civilization, int value, bool requiresNoOthers)
        {
            var condition = new AbilityConditionDefinition
            {
                Controller = RuleController.Source,
                Zone = RuleZone.BattleZone,
                Filter = new CardFilterDefinition
                {
                    CreaturesOnly = true,
                    OtherThanSource = true,
                    Civilizations = new[] { civilization }
                },
                CountAtLeast = requiresNoOthers ? 0 : 1,
                CountAtMost = 0,
                HasCountAtMost = requiresNoOthers
            };
            var power = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                1, 1, new CardFilterDefinition(), false);
            power.Value = value;
            power.Condition = condition;
            AppendRule(card, power);
            if (requiresNoOthers)
            {
                var breaker = SelfStaticKeyword(line, CardKeyword.DoubleBreaker);
                breaker.Condition = condition;
                AppendRule(card, breaker);
            }
            return true;
        }
    }
}
