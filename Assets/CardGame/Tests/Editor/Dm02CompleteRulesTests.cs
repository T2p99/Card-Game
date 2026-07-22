using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm02CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm02PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-02").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void CrystalPaladin_EvolvesAndReturnsEveryBlocker()
        {
            var state = EmptyState();
            var paladin = Instance(10, "dm02-s2-s5");
            var baseCreature = Instance(20, Creature("liquid-base", 1000, Civilization.Water, "Liquid People"));
            var firstBlocker = Instance(30, Blocker("first-blocker", 2000));
            var secondBlocker = Instance(31, Blocker("second-blocker", 3000));
            state.Players[0].Hand.Add(paladin);
            state.Players[0].BattleZone.Add(baseCreature);
            state.Players[1].BattleZone.Add(firstBlocker);
            state.Players[1].BattleZone.Add(secondBlocker);
            AddMana(state, paladin.Definition.Cost, Civilization.Water);

            var result = new RulesEngine().SummonCreature(state, 0, paladin.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray(), baseCreature.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(paladin.EvolutionMaterials.Contains(baseCreature), Is.True);
            Assert.That(state.Players[1].BattleZone, Is.Empty);
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(2));
            Assert.That(paladin.EnteredBattleZoneOnTurn, Is.EqualTo(-1));
        }

        [Test]
        public void ElfX_ReducesCreatureSummonCostButNotBelowOne()
        {
            var state = EmptyState();
            var elfX = Instance(10, "dm02-46-55");
            var creature = Instance(20, Creature("three-cost", 2000, Civilization.Nature));
            creature.Definition.Cost = 3;
            state.Players[0].BattleZone.Add(elfX);
            state.Players[0].Hand.Add(creature);
            AddMana(state, 2, Civilization.Nature);

            var result = new RulesEngine().SummonCreature(state, 0, creature.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].BattleZone.Contains(creature), Is.True);
        }

        [Test]
        public void DiamondCutter_AllowsSummoningSickRestrictedCreatureToAttackPlayer()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm02-1-55");
            var definition = Creature("restricted", 3000, Civilization.Light);
            definition.Abilities = new[] { new KeywordAbility(CardKeyword.CannotAttackPlayers) };
            var attacker = Instance(20, definition);
            attacker.EnteredBattleZoneOnTurn = state.TurnNumber;
            state.Players[0].Hand.Add(spell);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].Shields.Add(Instance(30, Creature("shield", 1000)));
            AddMana(state, spell.Definition.Cost, Civilization.Light);
            var engine = new RulesEngine();

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray()).Success, Is.True);
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player).Success, Is.True);
        }

        [Test]
        public void Gigastand_ReplacesDestructionThenDiscards()
        {
            var state = EmptyState();
            var gigastand = Instance(10, "dm02-28-55");
            var filler = Instance(5, Creature("filler", 1000));
            var opponent = Instance(20, Creature("opponent", 9000));
            gigastand.EnteredBattleZoneOnTurn = 0;
            opponent.Tapped = true;
            state.Players[0].BattleZone.Add(gigastand);
            state.Players[0].Hand.Add(filler);
            state.Players[1].BattleZone.Add(opponent);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, gigastand.InstanceId,
                AttackTargetKind.Creature, opponent.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Hand.Contains(gigastand), Is.True);
            Assert.That(state.Players[0].Graveyard.Contains(filler), Is.True);
        }

        [Test]
        public void Bombersaur_OnDestroyedBurnsManaForEachPlayer()
        {
            var state = EmptyState();
            var bombersaur = Instance(10, "dm02-36-55");
            var opponent = Instance(20, Creature("opponent", 9000));
            bombersaur.EnteredBattleZoneOnTurn = 0;
            opponent.Tapped = true;
            state.Players[0].BattleZone.Add(bombersaur);
            state.Players[1].BattleZone.Add(opponent);
            AddMana(state, 3, Civilization.Fire);
            AddMana(state, 1, 3, Civilization.Nature);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, bombersaur.InstanceId,
                AttackTargetKind.Creature, opponent.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Mana.Count, Is.EqualTo(1));
            Assert.That(state.Players[1].Mana.Count, Is.EqualTo(1));
            Assert.That(state.Players[0].Graveyard.Count(card => card.InstanceId >= 100), Is.EqualTo(2));
            Assert.That(state.Players[1].Graveyard.Count(card => card.InstanceId >= 200), Is.EqualTo(2));
        }

        [Test]
        public void SpiralGrass_UntapsAfterItBlocksAndSurvives()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("attacker", 1000, Civilization.Fire));
            var spiralGrass = Instance(20, "dm02-10-55");
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(spiralGrass);
            state.Players[1].Shields.Add(Instance(30, Creature("shield", 1000)));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, spiralGrass.InstanceId).Success, Is.True);

            Assert.That(state.Players[1].BattleZone.Contains(spiralGrass), Is.True);
            Assert.That(spiralGrass.Tapped, Is.False);
        }

        [Test]
        public void ReconOperation_StoresThreeInspectedOpponentShields()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm02-19-55");
            state.Players[0].Hand.Add(spell);
            for (var i = 0; i < 4; i++)
                state.Players[1].Shields.Add(Instance(30 + i, Creature("shield-" + i, 1000)));
            AddMana(state, spell.Definition.Cost, Civilization.Water);

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            Assert.That(state.InspectionPlayerId, Is.EqualTo(0));
            Assert.That(state.InspectedCards, Has.Count.EqualTo(3));
            Assert.That(state.Players[1].Shields, Has.Count.EqualTo(4));
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

        private static CardDefinition Creature(string id, int power,
            Civilization civilization = Civilization.Nature, string race = "Test Race") => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 3,
            Type = CardType.Creature,
            Races = new[] { race },
            Power = power,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardDefinition Blocker(string id, int power)
        {
            var definition = Creature(id, power, Civilization.Light, "Guardian");
            definition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            return definition;
        }

        private static void AddMana(GameState state, int count, Civilization civilization) =>
            AddMana(state, 0, count, civilization);

        private static void AddMana(GameState state, int playerId, int count, Civilization civilization)
        {
            var offset = playerId == 0 ? 100 : 200;
            for (var i = 0; i < count; i++)
                state.Players[playerId].Mana.Add(Instance(offset + i,
                    Creature("mana-" + playerId + "-" + i, 1000, civilization)));
        }

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
