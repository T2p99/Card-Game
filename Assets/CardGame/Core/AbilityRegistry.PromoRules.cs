using System;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static bool TryPromoRule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("Each of your creatures in the battle zone is a Survivor in addition to its other races.",
                    StringComparison.OrdinalIgnoreCase))
                return AddStaticKeywordAura(card, line, CardKeyword.Survivor,
                    RuleController.Source,
                    new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Each of your other creatures in the battle zone that has power 5000 or more breaks one more shield.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyBreakerCount, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        PowerAtLeast = 5000,
                        UseCurrentPower = true
                    }, false);
                ability.Value = 1;
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("When this creature loses a battle against a darkness creature, this creature isn't destroyed.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreventBattleDestruction)
                {
                    RulesText = line.Text,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    },
                    RelatedFilter = new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Darkness }
                    }
                });
                return true;
            }

            if (text.Equals("Whenever this creature loses a battle against a light creature, this creature isn't destroyed.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                    AbilityOperation.PreventBattleDestruction)
                {
                    RulesText = line.Text,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    },
                    RelatedFilter = new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[] { Civilization.Light }
                    }
                });
                return true;
            }

            if (text.Equals("Whenever one of your creatures is destroyed, you may untap this creature.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line,
                    AbilityTiming.OnOtherCreatureDestroyed, AbilityOperation.Untap,
                    RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true }, true);
                ability.Condition = new AbilityConditionDefinition
                {
                    EventControlledByOwner = true,
                    EventCardMatchesFilter = true,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.Equals("Whenever this creature attacks, reveal the top card of your deck. If it's a Survivor, put it into your hand. Otherwise, put it into your graveyard.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAttack,
                    AbilityOperation.RevealAndPartition, RuleController.Source,
                    RuleZone.Deck, RuleSelectionMode.Top, 1, 1,
                    new CardFilterDefinition(), false);
                ability.PartitionFilter = new CardFilterDefinition
                {
                    HasRequiredKeyword = true,
                    RequiredKeyword = CardKeyword.Survivor
                };
                ability.Destination = RuleZone.Hand;
                ability.SecondaryDestination = RuleZone.Graveyard;
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever one of your other creatures that has Dragon in its race attacks, you may choose one of your opponent's creatures in the battle zone that has power 5000 or less and put it into his mana zone.",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.OnAnyCreatureAttack,
                    AbilityOperation.Move, RuleController.Opponent,
                    RuleZone.BattleZone, RuleSelectionMode.Choose,
                    0, 1, new CardFilterDefinition
                    { CreaturesOnly = true, PowerAtMost = 5000 }, true);
                ability.Destination = RuleZone.ManaZone;
                ability.Condition = new AbilityConditionDefinition
                {
                    EventControlledByOwner = true,
                    EventCardMatchesFilter = true,
                    Filter = new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        RaceContains = "Dragon"
                    }
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, if your opponent has more creatures in the battle zone than you do, search your deck. For each extra creature he has",
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon,
                    AbilityOperation.SearchPerOpponentCreatureLead)
                {
                    RulesText = line.Text,
                    Optional = true
                });
                return true;
            }

            if (text.StartsWith("When you put this creature into the battle zone, search your deck. You may take an Armored Dragon from your deck",
                    StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, AbilityTiming.OnSummon,
                    RuleController.Source, RuleZone.Deck, RuleZone.Hand,
                    RuleSelectionMode.Choose, 0, 1,
                    new CardFilterDefinition
                    { CreaturesOnly = true, Race = "Armored Dragon" }, true, true);

            return false;
        }
    }
}
