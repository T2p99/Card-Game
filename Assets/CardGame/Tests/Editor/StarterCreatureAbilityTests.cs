using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class StarterCreatureAbilityTests
    {
        [Test]
        public void ArmoredCannonBalbaro_EvolvesFromHumanAndCanAttackImmediately()
        {
            var state = EmptyState();
            var engine = new RulesEngine();
            var evolution = Instance(10, StarterDeckCatalog.Cards["dm02-034"]);
            var human = Instance(20, StarterDeckCatalog.Cards["dm01-080"]);
            state.Players[0].Hand.Add(evolution);
            state.Players[0].BattleZone.Add(human);
            AddMana(state, Civilization.Fire, 3);

            var result = engine.SummonCreature(state, 0, evolution.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray(), human.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].BattleZone, Is.EquivalentTo(new[] { evolution }));
            Assert.That(evolution.EvolutionMaterials, Is.EquivalentTo(new[] { human }));
            Assert.That(engine.DeclareAttack(state, 0, evolution.InstanceId, AttackTargetKind.Player).Success, Is.True);
        }

        [Test]
        public void ChaosWorm_RejectsNonParasiteWormEvolutionBase()
        {
            var state = EmptyState();
            var evolution = Instance(10, StarterDeckCatalog.Cards["dm02-024"]);
            var ghost = Instance(20, StarterDeckCatalog.Cards["dm01-051"]);
            state.Players[0].Hand.Add(evolution);
            state.Players[0].BattleZone.Add(ghost);
            AddMana(state, Civilization.Darkness, 5);

            var result = new RulesEngine().SummonCreature(state, 0, evolution.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray(), ghost.InstanceId);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("Parasite Worm"));
        }

        [Test]
        public void Meteosaur_DestroysOpponentCreatureWithPower2000OrLessOnSummon()
        {
            var state = EmptyState();
            var meteosaur = Instance(10, StarterDeckCatalog.Cards["dm01-082"]);
            var small = Instance(20, Creature("small", Civilization.Darkness, 2000));
            var large = Instance(21, Creature("large", Civilization.Darkness, 3000));
            state.Players[0].Hand.Add(meteosaur);
            state.Players[1].BattleZone.Add(small);
            state.Players[1].BattleZone.Add(large);
            AddMana(state, Civilization.Fire, 5);

            var result = new RulesEngine().SummonCreature(state, 0, meteosaur.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].BattleZone, Is.EquivalentTo(new[] { large }));
            Assert.That(state.Players[1].Graveyard, Has.Member(small));
        }

        [Test]
        public void SuperExplosiveVolcanodon_UsesPrintedPowerAttacker4000()
        {
            var state = EmptyState();
            var engine = new RulesEngine();
            var attacker = Instance(10, StarterDeckCatalog.Cards["dm01-087"]);
            var defender = Instance(20, Creature("defender", Civilization.Darkness, 5000));
            attacker.EnteredBattleZoneOnTurn = 0;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(defender);

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].BattleZone, Has.Member(attacker));
            Assert.That(state.Players[1].BattleZone, Is.Empty);
        }

        [Test]
        public void HorridWorm_DiscardsRandomOpponentCardWhenItAttacks()
        {
            var state = EmptyState();
            var attacker = Instance(10, StarterDeckCatalog.Cards["dm02-030"]);
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].Hand.Add(Instance(20, Creature("hand-a", Civilization.Fire, 1000)));
            state.Players[1].Hand.Add(Instance(21, Creature("hand-b", Civilization.Fire, 1000)));

            var result = new RulesEngine().DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].Hand.Count, Is.EqualTo(1));
            Assert.That(state.Players[1].Graveyard.Count, Is.EqualTo(1));
        }

        private static GameState EmptyState()
        {
            return new GameState
            {
                Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
                ActivePlayerId = 0,
                StartingPlayerId = 0,
                TurnNumber = 2,
                Phase = GamePhase.Main,
                RandomSeed = 42
            };
        }

        private static void AddMana(GameState state, Civilization civilization, int count)
        {
            for (var i = 0; i < count; i++)
                state.Players[0].Mana.Add(Instance(100 + i, Creature($"mana-{i}", civilization, 1000)));
        }

        private static CardDefinition Creature(string id, Civilization civilization, int power)
        {
            return new CardDefinition
            {
                Id = id,
                Name = id,
                Civilizations = new[] { civilization },
                Cost = 1,
                Type = CardType.Creature,
                Races = new[] { "Test" },
                Power = power,
                ImplementationStatus = CardImplementationStatus.Complete
            };
        }

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
