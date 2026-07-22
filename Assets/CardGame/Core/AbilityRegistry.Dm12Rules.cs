using System;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryDm12Rule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("At the end of each of your turns, you may untap any number of your Wild Veggies and Rainbow Phantoms in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.EndOfOwnerTurn,
                    AbilityOperation.Untap, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Races = new[] { "Wild Veggie", "Rainbow Phantom" }
                    }, true);

            if (text.Equals("At the end of each turn, if your opponent drew 2 or more cards that turn, he discards his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.EndOfAnyTurn,
                    AbilityOperation.Discard, RuleController.Opponent, RuleZone.Hand,
                    RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition(), false);
                ability.Condition = new AbilityConditionDefinition { OpponentDrewCardsAtLeast = 2 };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Choose a card at random in your opponent's hand. Then, for each Cloned Nightmare in each graveyard, you may choose another card at random",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.Discard, RuleController.Opponent, RuleZone.Hand,
                    RuleSelectionMode.Random, 1, int.MaxValue,
                    new CardFilterDefinition(), false);
                ConfigureCloneScaling(ability, "Cloned Nightmare");
                ability.ChooseSelectionAmount = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Choose a creature in the battle zone. Then, for each Cloned Spiral in each graveyard, you may choose another creature",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.Move, RuleController.Either, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 1, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Destination = RuleZone.Hand;
                ConfigureCloneScaling(ability, "Cloned Spiral");
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Choose a number less than or equal to 6000. Destroy all creatures that have that power.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.DestroyAllChosenPower) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("Choose one of your opponent's creatures in the battle zone that has power 3000 or less. Then, for each Cloned Blade in each graveyard",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.Destroy, RuleController.Opponent, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 1, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 3000 }, false);
                ConfigureCloneScaling(ability, "Cloned Blade");
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Choose one of your opponent's creatures in the battle zone. Then, for each Cloned Deflector in each graveyard",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.Tap, RuleController.Opponent, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 1, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ConfigureCloneScaling(ability, "Cloned Deflector");
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Discard any number of cards from your hand. Then draw that many cards.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Discard,
                    RuleController.Source, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, int.MaxValue, new CardFilterDefinition(), true);
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 0,
                    UsePreviousSelectionCountAsAmount = true
                });
                return true;
            }

            if (text.StartsWith("Each of your Chimeras and Armorloids in the battle zone has \"double breaker.\"",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticKeywordAura(card, line, CardKeyword.DoubleBreaker,
                    RuleController.Source, new CardFilterDefinition
                    { CreaturesOnly = true, Races = new[] { "Chimera", "Armorloid" } });

            if (text.StartsWith("Each of your creatures in the battle zone gets +5000 power and has \"blocker\" and \"double breaker\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddDm11StaticPower(card, line, 5000, RuleController.Source,
                    new CardFilterDefinition { CreaturesOnly = true });
                AddStaticKeywordAura(card, line, CardKeyword.Blocker,
                    RuleController.Source, new CardFilterDefinition { CreaturesOnly = true });
                return AddStaticKeywordAura(card, line, CardKeyword.DoubleBreaker,
                    RuleController.Source, new CardFilterDefinition { CreaturesOnly = true });
            }

            if (text.Equals("This creature gets +2000 power for each card in your opponent's hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = Dm10DynamicPowerAbility(line, new RuleSelectorDefinition
                {
                    Controller = RuleController.Opponent,
                    Zone = RuleZone.Hand,
                    Mode = RuleSelectionMode.All
                });
                ability.ValuePerMatchingCard = 2000;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("This creature gets +3000 power for each Cloned Spike-Horn in each graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = Dm10DynamicPowerAbility(line, CloneGraveyardSelector("Cloned Spike-Horn"));
                ability.ValuePerMatchingCard = 3000;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When a player evolves another creature, destroy this creature.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnAnyCreatureEvolved,
                    AbilityOperation.Destroy, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("When this creature blocks, destroy it after it battles.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnBlock,
                    AbilityOperation.ScheduleSourceDestructionAfterBattle)
                {
                    RulesText = line.Text
                });
                return true;
            }

            if (text.Equals("When this creature leaves the battle zone, destroy all creatures.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnLeaveBattleZone,
                    AbilityOperation.Destroy, RuleController.Either, RuleZone.BattleZone,
                    RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true });

            if (text.Equals("When this creature leaves the battle zone, your opponent discards his hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnLeaveBattleZone,
                    AbilityOperation.Discard, RuleController.Opponent, RuleZone.Hand,
                    RuleSelectionMode.All, 0, int.MaxValue, new CardFilterDefinition());

            if (text.StartsWith("When this creature would leave the battle zone, only the top card leaves the battle zone instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.StateDefinedLeaveBattleZone,
                    AbilityOperation.SeparateEvolutionTop) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, destroy the creature that has the lowest power in the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.DestroyLowestPower, RuleController.Either,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, draw up to 2 cards. Then discard 2 cards from your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 2,
                    ChooseAmount = true
                });
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.Discard, RuleController.Source, RuleZone.Hand,
                    RuleSelectionMode.Choose, 2, 2, new CardFilterDefinition());
            }

            if (text.Equals("When you put this creature into the battle zone, each player may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.EachPlayerMayDraw) { RulesText = line.Text });
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, for each creature you have in the battle zone, you may choose a shield and turn it face up.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnSummon,
                    AbilityOperation.TurnFaceUp, RuleController.Either,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose, 0, int.MaxValue,
                    new CardFilterDefinition(), true);
                ability.Amount = 0;
                ability.AmountPerMatchingCard = 1;
                ability.AmountSelector = BattleZoneSelector(RuleController.Source,
                    new CardFilterDefinition { CreaturesOnly = true });
                ability.SelectionMaximumUsesAmount = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When you put this creature into the battle zone, put the top card of your deck into your mana zone. Then put a creature from your mana zone into your hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Deck, RuleZone.ManaZone, RuleSelectionMode.Top,
                    1, 1, new CardFilterDefinition());
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.ManaZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });
            }

            if (text.Equals("When you put this creature into the battle zone, return all your other creatures from the battle zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.BattleZone, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition
                    { CreaturesOnly = true, OtherThanSource = true });

            if (text.Equals("When you put this creature into the battle zone, return one of your creatures that costs 4 or less from the battle zone to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.BattleZone, RuleZone.Hand, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition
                    { CreaturesOnly = true, CostAtMost = 4 });

            if (text.Equals("When you put this creature into the battle zone, you may choose a darkness or fire creature in the battle zone and tap it.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.Tap, RuleController.Either, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Darkness, Civilization.Fire }
                    }, true);

            if (text.Equals("When you put this creature into the battle zone, your may return a water or fire creature from your graveyard to your hand.",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon, RuleController.Source,
                    RuleZone.Graveyard, RuleZone.Hand, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Water, Civilization.Fire }
                    }, true);

            if (text.Equals("When you put this creature into the battle zone, your opponent draws a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.OpponentDraw) { RulesText = line.Text, Amount = 1 });
                return true;
            }

            if (text.Equals("When you put this creature is put into battle zone, destroy one of your opponent's creatures that has power 5000 or less.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnSummon,
                    AbilityOperation.Destroy, RuleController.Opponent, RuleZone.BattleZone,
                    RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = 5000 });

            if (text.Equals("Your creatures and your opponent's creatures each cost up to 1 less to summon. They can't cost less than 1.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyCost, RuleController.Either, RuleZone.None,
                    RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Value = -1;
                ability.MinimumValue = 1;
                ability.OptionalCostReduction = true;
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Your opponent chooses one of your shields. Look at it. If it's a spell, you may cast it immediately for no cost.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnCast,
                    AbilityOperation.InspectShieldAndCastSpell, RuleController.Source,
                    RuleZone.ShieldZone, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                ability.Chooser = RuleController.Opponent;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever another creature is put into the battle zone, tap this creature.",
                    StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.Tap, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Whenever any of your XenoParts or Giant Insects is attacking your opponent and isn't blocked, your opponent chooses a card in his mana zone and puts it into his graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line,
                    AbilityTiming.OnAnyCreatureAttackPlayerUnblocked,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.ManaZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition(), false);
                ability.Chooser = RuleController.Opponent;
                ability.Destination = RuleZone.Graveyard;
                ability.Condition = new AbilityConditionDefinition
                {
                    EventControlledByOwner = true,
                    EventCardMatchesFilter = true,
                    Filter = new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Races = new[] { "Xenoparts", "Giant Insect" }
                    }
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever one of your Gladiators or Earth Eaters blocks, you may draw a card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.OnAnyCreatureBlock, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true,
                    Condition = new AbilityConditionDefinition
                    {
                        EventControlledByOwner = true,
                        EventCardMatchesFilter = true,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Races = new[] { "Gladiator", "Earth Eater" }
                        }
                    }
                });
                return true;
            }

            if (text.Equals("Whenever this creature attacks or leaves the battle zone, add the top card of your deck to your shields face down.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMoveRule(card, line, AbilityTiming.OnAttack, RuleController.Source,
                    RuleZone.Deck, RuleZone.ShieldZone, RuleSelectionMode.Top,
                    1, 1, new CardFilterDefinition());
                AddMoveRule(card, line, AbilityTiming.OnLeaveBattleZone,
                    RuleController.Source, RuleZone.Deck, RuleZone.ShieldZone,
                    RuleSelectionMode.Top, 1, 1, new CardFilterDefinition());
                return true;
            }

            if (text.StartsWith("Whenever this creature attacks or leaves the battle zone, reveal the top card of your deck. If it's a non-evolution creature, put it into the battle zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddDm12RevealCreatureOrHand(card, line, AbilityTiming.OnAttack);
                AddDm12RevealCreatureOrHand(card, line, AbilityTiming.OnLeaveBattleZone);
                return true;
            }

            if (text.Equals("Whenever this creature blocks, your opponent chooses one of his creatures in the battle zone and returns it to his hand.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnBlock,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Chooser = RuleController.Opponent;
                ability.Destination = RuleZone.Hand;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever you draw the card at the start of your turn, if one of your creatures in the battle zone has higher power then each of your opponent's creatures in the battle zone, you may draw an extra card.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id,
                    AbilityTiming.AfterStartTurnDraw, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = 1,
                    Optional = true,
                    Condition = new AbilityConditionDefinition
                    { OwnCreatureHigherThanEveryOpponentCreature = true }
                });
                return true;
            }

            if (text.StartsWith("Whenever your opponent uses the \"shield trigger\" ability of one of his shields, search your deck. You may take a creature from your deck",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line,
                    AbilityTiming.OnShieldTriggerCastByOpponent,
                    AbilityOperation.Move, RuleController.Source, RuleZone.Deck,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                ability.Destination = RuleZone.Hand;
                ability.ShuffleAfter = true;
                ability.Condition = new AbilityConditionDefinition
                    { EventControlledByOpponent = true };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever your opponent would choose one of your shields, you choose instead.",
                    StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Whenever you would choose one of your opponent's shields, your opponent chooses instead.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.ShieldOwnerChooses) { RulesText = line.Text });
                return true;
            }

            return false;
        }

        private static void AddDm12RevealCreatureOrHand(CardDefinition card,
            PrintedRuleLine line, AbilityTiming timing)
        {
            var ability = NewSelectionAbility(line, timing,
                AbilityOperation.RevealAndPartition, RuleController.Source,
                RuleZone.Deck, RuleSelectionMode.Top, 1, 1,
                new CardFilterDefinition(), false);
            ability.PartitionFilter = new CardFilterDefinition
                { CreaturesOnly = true, NonEvolutionOnly = true };
            ability.Destination = RuleZone.BattleZone;
            ability.SecondaryDestination = RuleZone.Hand;
            AppendRule(card, ability);
        }

        private static void ConfigureCloneScaling(RuleAbilityDefinition ability, string cloneName)
        {
            ability.Amount = 1;
            ability.AmountPerMatchingCard = 1;
            ability.AmountSelector = CloneGraveyardSelector(cloneName);
            ability.SelectionMaximumUsesAmount = true;
        }

        private static RuleSelectorDefinition CloneGraveyardSelector(string cloneName) =>
            new RuleSelectorDefinition
            {
                Controller = RuleController.Either,
                Zone = RuleZone.Graveyard,
                Mode = RuleSelectionMode.All,
                Filter = new CardFilterDefinition { Name = cloneName }
            };
    }
}
