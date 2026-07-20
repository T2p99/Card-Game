using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class SpellRulesTests
    {
        [Test]
        public void CrimsonHammer_DestroysLegalTargetAndMovesSpellToGraveyard()
        {
            var state = EmptyState();
            var spell = Instance(10, StarterDeckCatalog.Cards["dm01-073"]);
            var target = Instance(20, Creature("small", 2000));
            state.Players[0].Hand.Add(spell);
            state.Players[1].BattleZone.Add(target);
            state.Players[0].Mana.Add(Instance(30, Creature("mana-a", 1000)));
            state.Players[0].Mana.Add(Instance(31, Creature("mana-b", 1000)));

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId, new long[] { 30, 31 }, target.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].BattleZone, Is.Empty);
            Assert.That(state.Players[1].Graveyard, Has.Member(target));
            Assert.That(state.Players[0].Graveyard, Has.Member(spell));
            Assert.That(state.Players[0].Mana.TrueForAll(card => card.Tapped), Is.True);
        }

        [Test]
        public void DeathSmoke_RejectsTappedTargetWithoutSpendingMana()
        {
            var state = EmptyState();
            var spell = Instance(10, StarterDeckCatalog.Cards["dm01-053"]);
            var target = Instance(20, Creature("tapped", 1000));
            target.Tapped = true;
            state.Players[0].Hand.Add(spell);
            state.Players[1].BattleZone.Add(target);
            for (var i = 0; i < 4; i++) state.Players[0].Mana.Add(Instance(30 + i, Creature($"mana-{i}", 1000)));

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId, new long[] { 30, 31, 32, 33 }, target.InstanceId);

            Assert.That(result.Success, Is.False);
            Assert.That(state.Players[0].Mana.TrueForAll(card => !card.Tapped), Is.True);
            Assert.That(state.Players[0].Hand, Has.Member(spell));
        }

        [Test]
        public void BurningPower_ChangesBattleForTurnThenExpires()
        {
            var state = EmptyState();
            var engine = new RulesEngine();
            var spell = Instance(10, StarterDeckCatalog.Cards["dm01-071"]);
            var attacker = Instance(20, Creature("attacker", 1000));
            attacker.EnteredBattleZoneOnTurn = 0;
            var defender = Instance(21, Creature("defender", 2000));
            defender.Tapped = true;
            state.Players[0].Hand.Add(spell);
            state.Players[0].Mana.Add(Instance(30, Creature("mana", 1000)));
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(defender);

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId, new long[] { 30 }, attacker.InstanceId).Success, Is.True);
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].BattleZone, Has.Member(attacker));
            Assert.That(state.Players[1].BattleZone, Is.Empty);
            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(attacker.TemporaryPowerAttacker, Is.Zero);
        }

        [Test]
        public void TerrorPit_CastsFromShieldTriggerWithoutMana()
        {
            var state = EmptyState();
            var spell = Instance(10, StarterDeckCatalog.Cards["dm01-063"]);
            var target = Instance(20, Creature("target", 9000));
            state.Players[1].Hand.Add(spell);
            state.PendingShieldTriggers.Add(spell);
            state.Players[0].BattleZone.Add(target);

            var result = new RulesEngine().CastShieldTrigger(state, 1, spell.InstanceId, target.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.PendingShieldTriggers, Is.Empty);
            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[1].Graveyard, Has.Member(spell));
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

        private static CardDefinition Creature(string id, int power)
        {
            return new CardDefinition
            {
                Id = id,
                Name = id,
                Civilizations = new[] { Civilization.Fire },
                Cost = 1,
                Type = CardType.Creature,
                Power = power,
                ImplementationStatus = CardImplementationStatus.Complete
            };
        }

        private static CardInstance Instance(long id, CardDefinition definition)
        {
            return new CardInstance { InstanceId = id, Definition = definition };
        }
    }
}
