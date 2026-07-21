using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm01CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm01PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-01").ToArray();

            Assert.That(cards.Length, Is.EqualTo(120));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void DestroyReplacement_MovesChiliasToHand()
        {
            var state = EmptyState();
            var chilias = Instance(10, "dm01-1-110");
            var opponent = Instance(20, Creature("strong", 5000));
            chilias.EnteredBattleZoneOnTurn = 0;
            opponent.EnteredBattleZoneOnTurn = 0;
            opponent.Tapped = true;
            state.Players[0].BattleZone.Add(chilias);
            state.Players[1].BattleZone.Add(opponent);

            var engine = new RulesEngine();
            Assert.That(engine.DeclareAttack(state, 0, chilias.InstanceId,
                AttackTargetKind.Creature, opponent.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Hand.Contains(chilias), Is.True);
            Assert.That(state.Players[0].Graveyard.Contains(chilias), Is.False);
        }

        [Test]
        public void Teleportation_ReturnsTwoSelectedCreatures()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm01-41-110");
            var first = Instance(20, Creature("first", 1000));
            var second = Instance(21, Creature("second", 1000));
            var third = Instance(22, Creature("third", 1000));
            state.Players[0].Hand.Add(spell);
            state.Players[0].BattleZone.Add(first);
            state.Players[1].BattleZone.Add(second);
            state.Players[1].BattleZone.Add(third);
            AddMana(state, spell.Definition.Cost, Civilization.Water);

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Hand.Contains(first), Is.True);
            Assert.That(state.Players[1].Hand.Contains(second), Is.True);
            Assert.That(state.Players[1].BattleZone.Contains(third), Is.True);
        }

        [Test]
        public void BolshackDragon_CountsFireCardsInGraveyardWhileAttacking()
        {
            var state = EmptyState();
            var bolshack = Instance(10, "dm01-69-110");
            var defender = Instance(20, Creature("defender", 7000));
            bolshack.EnteredBattleZoneOnTurn = 0;
            defender.EnteredBattleZoneOnTurn = 0;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(bolshack);
            state.Players[0].Graveyard.Add(Instance(30, Creature("fire-one", 1000, Civilization.Fire)));
            state.Players[0].Graveyard.Add(Instance(31, Creature("fire-two", 1000, Civilization.Fire)));
            state.Players[1].BattleZone.Add(defender);

            var engine = new RulesEngine();
            Assert.That(engine.DeclareAttack(state, 0, bolshack.InstanceId,
                AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].BattleZone.Contains(bolshack), Is.True);
            Assert.That(state.Players[1].Graveyard.Contains(defender), Is.True);
        }

        [Test]
        public void CreepingPlague_GivesBlockedAttackerSlayerForTurn()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm01-49-110");
            var attacker = Instance(20, Creature("attacker", 1000, Civilization.Darkness));
            var blockerDefinition = Creature("blocker", 5000, Civilization.Light);
            blockerDefinition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            var blocker = new CardInstance { InstanceId = 30, Definition = blockerDefinition };
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].Hand.Add(spell);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(blocker);
            state.Players[1].Shields.Add(Instance(40, Creature("shield", 1000)));
            AddMana(state, spell.Definition.Cost, Civilization.Darkness);
            var engine = new RulesEngine();

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray()).Success, Is.True);
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, blocker.InstanceId).Success, Is.True);

            Assert.That(state.Players[0].Graveyard.Contains(attacker), Is.True);
            Assert.That(state.Players[1].Graveyard.Contains(blocker), Is.True);
        }

        [Test]
        public void MagmaGazer_GrantsPowerAttackerAndDoubleBreaker()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm01-81-110");
            var attacker = Instance(20, Creature("attacker", 2000, Civilization.Fire));
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].Hand.Add(spell);
            state.Players[0].BattleZone.Add(attacker);
            for (var i = 0; i < 3; i++)
                state.Players[1].Shields.Add(Instance(40 + i, Creature("shield-" + i, 1000)));
            AddMana(state, spell.Definition.Cost, Civilization.Fire);
            var engine = new RulesEngine();

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray()).Success, Is.True);
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(attacker.TemporaryPowerAttacker, Is.EqualTo(4000));
            Assert.That(state.Players[1].Shields.Count, Is.EqualTo(1));
        }

        private static GameState EmptyState() => new GameState
        {
            Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
            ActivePlayerId = 0,
            StartingPlayerId = 1,
            TurnNumber = 2,
            Phase = GamePhase.Main,
            RandomSeed = 42
        };

        private static void AddMana(GameState state, int count, Civilization civilization)
        {
            for (var i = 0; i < count; i++)
                state.Players[0].Mana.Add(new CardInstance
                {
                    InstanceId = 100 + i,
                    Definition = Creature("mana-" + i, 1000, civilization)
                });
        }

        private static CardDefinition Creature(string id, int power,
            Civilization civilization = Civilization.Nature) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 1,
            Type = CardType.Creature,
            Races = new[] { "Test Race" },
            Power = power,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
