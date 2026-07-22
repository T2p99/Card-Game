using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class CreatureShieldTriggerTests
    {
        [Test]
        public void AquaSurfer_ShieldTriggerSummonsAndReturnsOpponentCreature()
        {
            var state = EmptyState();
            var surfer = Instance(10, "dm05-16-55");
            var target = Instance(20, Creature("target"));
            state.Players[1].Hand.Add(surfer);
            state.PendingShieldTriggers.Add(surfer);
            state.Players[0].BattleZone.Add(target);

            var result = new RulesEngine(new TargetCardDecisionProvider(target.InstanceId))
                .CastShieldTrigger(state, 1, surfer.InstanceId);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(state.Players[1].BattleZone, Does.Contain(surfer));
            Assert.That(state.Players[0].Hand, Does.Contain(target));
            Assert.That(state.PendingShieldTriggers, Has.No.Member(surfer));
        }

        [Test]
        public void QTronicOmnistrain_ShieldTriggerStillRequiresEvolutionBase()
        {
            var state = EmptyState();
            var omnistrain = Instance(10, "promo-l2-y2");
            var survivor = Instance(20, Creature("survivor"));
            survivor.Definition.Abilities = new[]
                { new KeywordAbility(CardKeyword.Survivor) };
            state.Players[1].Hand.Add(omnistrain);
            state.PendingShieldTriggers.Add(omnistrain);
            state.Players[1].BattleZone.Add(survivor);

            var result = new RulesEngine().CastShieldTrigger(state, 1,
                omnistrain.InstanceId, survivor.InstanceId);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(state.Players[1].BattleZone, Is.EquivalentTo(new[] { omnistrain }));
            Assert.That(omnistrain.EvolutionMaterials, Is.EquivalentTo(new[] { survivor }));
        }

        [Test]
        public void EvolutionCreatureShieldTrigger_RemainsPendingWithoutAValidBase()
        {
            var state = EmptyState();
            var omnistrain = Instance(10, "promo-l2-y2");
            state.Players[1].Hand.Add(omnistrain);
            state.PendingShieldTriggers.Add(omnistrain);

            var result = new RulesEngine().CastShieldTrigger(state, 1,
                omnistrain.InstanceId);

            Assert.That(result.Success, Is.False);
            Assert.That(state.Players[1].Hand, Does.Contain(omnistrain));
            Assert.That(state.PendingShieldTriggers, Does.Contain(omnistrain));
        }

        private static GameState EmptyState() => new GameState
        {
            Players = new[]
            {
                new PlayerState { PlayerId = 0 },
                new PlayerState { PlayerId = 1 }
            },
            ActivePlayerId = 0,
            StartingPlayerId = 0,
            TurnNumber = 2,
            Phase = GamePhase.Attack,
            RandomSeed = 42
        };

        private sealed class TargetCardDecisionProvider : IAbilityDecisionProvider
        {
            private readonly long _targetInstanceId;

            public TargetCardDecisionProvider(long targetInstanceId) =>
                _targetInstanceId = targetInstanceId;

            public bool ChooseOptional(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state,
                int playerId, CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum)
            {
                var target = candidates.FirstOrDefault(card =>
                    card.InstanceId == _targetInstanceId);
                if (target != null && maximum > 0) return new[] { target };
                return candidates.Take(maximum).ToArray();
            }
        }

        private static CardDefinition Creature(string id) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { Civilization.Nature },
            Cost = 1,
            Type = CardType.Creature,
            Races = new[] { "Test Race" },
            Power = 1000,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
