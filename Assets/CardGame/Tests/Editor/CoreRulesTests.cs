using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class CoreRulesTests
    {
        [Test]
        public void StartGame_DealsFiveShieldsAndFiveCards()
        {
            var state = NewGame();

            Assert.That(state.Players[0].Shields.Count, Is.EqualTo(5));
            Assert.That(state.Players[0].Hand.Count, Is.EqualTo(5));
            Assert.That(state.Players[0].Deck.Count, Is.EqualTo(30));
            Assert.That(state.ActivePlayerId, Is.EqualTo(0));
        }

        [Test]
        public void ChargeMana_IsLimitedToOncePerTurn()
        {
            var engine = new RulesEngine();
            var state = NewGame(engine);
            var first = state.ActivePlayer.Hand[0];
            var second = state.ActivePlayer.Hand[1];

            Assert.That(engine.ChargeMana(state, 0, first.InstanceId).Success, Is.True);
            var result = engine.ChargeMana(state, 0, second.InstanceId);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("one mana charge"));
        }

        [Test]
        public void SummonCreature_RequiresCivilizationMatch()
        {
            var engine = new RulesEngine();
            var state = NewGame(engine);
            var fireCreature = Instance(900, Creature("fire-test", "Fire Test", Civilization.Fire, 2, 2000));
            state.ActivePlayer.Hand.Add(fireCreature);
            state.ActivePlayer.Mana.Add(Instance(901, Creature("water-mana-1", "Water Mana 1", Civilization.Water, 1, 1000)));
            state.ActivePlayer.Mana.Add(Instance(902, Creature("water-mana-2", "Water Mana 2", Civilization.Water, 1, 1000)));

            var result = engine.SummonCreature(state, 0, fireCreature.InstanceId, new long[] { 901, 902 });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("civilization"));
        }

        [Test]
        public void CreatureCannotAttackOnSummonTurnWithoutSpeedAttacker()
        {
            var engine = new RulesEngine();
            var state = NewGame(engine);
            var attacker = Instance(900, Creature("attacker", "Attacker", Civilization.Fire, 2, 2000));
            attacker.EnteredBattleZoneOnTurn = state.TurnNumber;
            state.ActivePlayer.BattleZone.Add(attacker);

            var result = engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("summoning sickness"));
        }

        [Test]
        public void EqualPowerBattle_DestroysBothCreatures()
        {
            var engine = new RulesEngine();
            var state = NewGame(engine);
            var attacker = Instance(900, Creature("attacker", "Attacker", Civilization.Fire, 2, 3000));
            var defender = Instance(901, Creature("defender", "Defender", Civilization.Nature, 2, 3000));
            attacker.EnteredBattleZoneOnTurn = 0;
            defender.EnteredBattleZoneOnTurn = 0;
            defender.Tapped = true;
            state.ActivePlayer.BattleZone.Add(attacker);
            state.Opponent.BattleZone.Add(defender);

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.ActivePlayer.BattleZone, Is.Empty);
            Assert.That(state.Opponent.BattleZone, Is.Empty);
            Assert.That(state.Players[0].Graveyard.Single().InstanceId, Is.EqualTo(attacker.InstanceId));
            Assert.That(state.Players[1].Graveyard.Single().InstanceId, Is.EqualTo(defender.InstanceId));
        }

        [Test]
        public void UnblockedAttackWithNoShields_WinsGame()
        {
            var engine = new RulesEngine();
            var state = NewGame(engine);
            var attacker = Instance(900, Creature("attacker", "Attacker", Civilization.Fire, 2, 2000));
            attacker.EnteredBattleZoneOnTurn = 0;
            state.ActivePlayer.BattleZone.Add(attacker);
            state.Opponent.Shields.Clear();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.WinnerPlayerId, Is.EqualTo(0));
            Assert.That(state.WinReason, Is.EqualTo("Direct attack"));
        }

        private static GameState NewGame(RulesEngine engine = null)
        {
            engine ??= new RulesEngine();
            return engine.StartGame(Deck("P1"), Deck("P2"), seed: 42, startingPlayerId: 0);
        }

        private static List<CardDefinition> Deck(string prefix)
        {
            var deck = new List<CardDefinition>();
            for (var i = 0; i < 40; i++)
                deck.Add(Creature($"{prefix}-{i}", $"{prefix} Card {i}", Civilization.Fire, 1, 1000));
            return deck;
        }

        private static CardDefinition Creature(
            string id,
            string name,
            Civilization civilization,
            int cost,
            int power)
        {
            return new CardDefinition
            {
                Id = id,
                Name = name,
                Civilizations = new[] { civilization },
                Cost = cost,
                Type = CardType.Creature,
                Power = power
            };
        }

        private static CardInstance Instance(long id, CardDefinition definition)
        {
            return new CardInstance { InstanceId = id, Definition = definition };
        }
    }
}
