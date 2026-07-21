using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class AbilityResolverTests
    {
        [Test]
        public void Resolve_MoveTopDeckToMana_UsesPrintedAmount()
        {
            var state = EmptyState();
            var source = Instance(1, Creature("source"));
            source.Definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.mana", AbilityTiming.OnSummon, AbilityOperation.Move)
                {
                    Destination = RuleZone.ManaZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Deck,
                        Mode = RuleSelectionMode.Top,
                        Minimum = 2,
                        Maximum = 2
                    }
                }
            };
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Deck.Add(Instance(2, Creature("bottom")));
            state.Players[0].Deck.Add(Instance(3, Creature("top-one")));
            state.Players[0].Deck.Add(Instance(4, Creature("top-two")));

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Mana.Count, Is.EqualTo(2));
            Assert.That(state.Players[0].Mana.Exists(card => card.InstanceId == 4), Is.True);
            Assert.That(state.Players[0].Mana.Exists(card => card.InstanceId == 3), Is.True);
            Assert.That(state.Players[0].Deck.Single().InstanceId, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_StaticPowerModifier_OnlyAffectsMatchingAllies()
        {
            var state = EmptyState();
            var source = Instance(1, Creature("source", Civilization.Light, "Guardian"));
            source.Definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.aura", AbilityTiming.Static, AbilityOperation.ModifyPower)
                {
                    Value = 2000,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition { Race = "Guardian" }
                    }
                }
            };
            var ally = Instance(2, Creature("ally", Civilization.Light, "Guardian"));
            var opponent = Instance(3, Creature("opponent", Civilization.Light, "Guardian"));
            state.Players[0].BattleZone.Add(source);
            state.Players[0].BattleZone.Add(ally);
            state.Players[1].BattleZone.Add(opponent);

            var resolver = new AbilityResolver();
            Assert.That(resolver.CurrentPower(state, ally), Is.EqualTo(3000));
            Assert.That(resolver.CurrentPower(state, opponent), Is.EqualTo(1000));
        }

        private static GameState EmptyState() => new GameState
        {
            Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
            ActivePlayerId = 0,
            StartingPlayerId = 0,
            TurnNumber = 2,
            Phase = GamePhase.Main,
            RandomSeed = 42
        };

        private static CardDefinition Creature(string id, Civilization civilization = Civilization.Nature, string race = "Beast Folk") =>
            new CardDefinition
            {
                Id = id,
                Name = id,
                Civilizations = new[] { civilization },
                Cost = 1,
                Type = CardType.Creature,
                Races = new[] { race },
                Power = 1000,
                ImplementationStatus = CardImplementationStatus.Complete
            };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
