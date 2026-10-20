using System;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class PortraitInteractionRulesTests
    {
        [Test]
        public void AutomaticMana_BacktracksInsteadOfSpendingTheOnlyProviderOfAnotherCivilization()
        {
            var state = State(); var engine = new RulesEngine();
            var summon = Card(1, "summon", Civilization.Fire, Civilization.Water);
            summon.Definition.Cost = 2;
            var flexible = Card(2, "flexible", Civilization.Fire, Civilization.Water);
            var fire = Card(3, "fire", Civilization.Fire);
            state.Players[0].Mana.AddRange(new[] { flexible, fire });
            var ids = engine.FindManaPayment(state, 0, summon);
            Assert.That(ids, Is.EquivalentTo(new long[] { 2, 3 }));
            Assert.That(engine.ValidatePayment(state, 0, summon, ids).Success, Is.True);
            Assert.That(state.Players[0].Mana.All(c => !c.Tapped), Is.True);
        }

        [Test]
        public void AutomaticMana_DoesNotCountOneMulticolorCardAsTwoMana()
        {
            var state = State(); var engine = new RulesEngine();
            var summon = Card(1, "summon", Civilization.Fire, Civilization.Water); summon.Definition.Cost = 2;
            state.Players[0].Mana.AddRange(new[] { Card(2, "flexible", Civilization.Fire, Civilization.Water), Card(3, "nature", Civilization.Nature) });
            Assert.That(engine.FindManaPayment(state, 0, summon), Is.Empty);
        }

        [Test]
        public void AutomaticMana_ExcludesTappedCardsAndUsesLiveCost()
        {
            var state = State(); var engine = new RulesEngine();
            var summon = Card(1, "summon", Civilization.Fire); summon.Definition.Cost = 3;
            state.Players[0].Mana.AddRange(new[] { Card(2, "fire", Civilization.Fire), Card(3, "fire", Civilization.Fire), Card(4, "fire", Civilization.Fire) });
            state.Players[0].Mana[0].Tapped = true;
            Assert.That(engine.FindManaPayment(state, 0, summon), Is.Empty);
            summon.Definition.Cost = 2;
            Assert.That(engine.FindManaPayment(state, 0, summon), Is.EquivalentTo(new long[] { 3, 4 }));
        }

        [Test]
        public void DraggedShield_IsTheShieldThatBreaks()
        {
            var state = AttackState(); var engine = new RulesEngine();
            Assert.That(engine.DeclareAttack(state, 0, 1, AttackTargetKind.Player, 0, 12).Success, Is.True);
            Assert.That(state.Players[1].Shields.Count, Is.EqualTo(3), "Attack declaration must not break a shield before blocking.");
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);
            Assert.That(state.Players[1].Hand.Select(c => c.InstanceId), Is.EqualTo(new long[] { 12 }));
            Assert.That(state.Players[1].Shields.Select(c => c.InstanceId), Is.EquivalentTo(new long[] { 10, 11 }));
        }

        [Test]
        public void DoubleBreaker_SelectsTheRemainingShieldThroughTheNormalChoiceFlow()
        {
            var state = AttackState(); var engine = new RulesEngine();
            state.Players[0].BattleZone[0].Definition.Abilities = new[] { new KeywordAbility(CardKeyword.DoubleBreaker) };
            engine.DeclareAttack(state, 0, 1, AttackTargetKind.Player, 0, 12);
            var session = new AbilityActionSession(state, 0, (e, s) => e.DeclineBlockAndResolve(s, 1));
            Assert.That(session.IsComplete, Is.False);
            Assert.That(session.PendingChoice.Cards.Select(c => c.InstanceId), Is.EquivalentTo(new long[] { 10, 11 }));
            Assert.That(session.PendingChoice.Minimum, Is.EqualTo(1));
            Assert.That(session.PendingChoice.CardLabels.All(label => label.Contains("Face-down")), Is.True);
            session.AnswerCards(new long[] { 11 });
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.State.Players[1].Hand.Select(c => c.InstanceId), Is.EquivalentTo(new long[] { 11, 12 }));
        }

        [TestCase(AbilityOperation.DefendingPlayerChoosesShield, 1)]
        [TestCase(AbilityOperation.ShieldOwnerChooses, 0)]
        [TestCase(AbilityOperation.ShieldOwnerChooses, 1)]
        public void DefenderChoiceEffect_OverridesTheDraggedShield(AbilityOperation operation, int owner)
        {
            var state = AttackState(); var engine = new RulesEngine();
            var source = Card(20, "shield chooser", Civilization.Light);
            source.Definition.RuleAbilities = new[] { new RuleAbilityDefinition("chooser", AbilityTiming.Static, operation) };
            state.Players[owner].BattleZone.Add(source);
            Assert.That(engine.AttackerChoosesShields(state, state.Players[0].BattleZone[0], 1), Is.False);
            engine.DeclareAttack(state, 0, 1, AttackTargetKind.Player, 0, 12);
            engine.DeclineBlockAndResolve(state, 1);
            Assert.That(state.Players[1].Hand.Single().InstanceId, Is.EqualTo(10));
        }

        [Test]
        public void InvalidShieldDrop_DoesNotTapAttackerOrStartAnAttack()
        {
            var state = AttackState(); var engine = new RulesEngine();
            Assert.That(engine.DeclareAttack(state, 0, 1, AttackTargetKind.Player, 0, 99).Success, Is.False);
            Assert.That(state.PendingAttack, Is.Null);
            Assert.That(state.Players[0].BattleZone[0].Tapped, Is.False);
        }

        [Test]
        public void ShieldMovedAndReturnedDuringAttack_DoesNotRetainTheOldPreference()
        {
            var state = AttackState(); var engine = new RulesEngine();
            engine.DeclareAttack(state, 0, 1, AttackTargetKind.Player, 0, 12);
            // Model a shield's new zone incarnation through public fixture
            // state. The engine's reset helper is internal to the Core assembly.
            state.Players[1].Shields.Single(c => c.InstanceId == 12).ZoneChangeVersion++;
            var session = new AbilityActionSession(state, 0, (e, s) => e.DeclineBlockAndResolve(s, 1));
            Assert.That(session.IsComplete, Is.False, "The attacker must choose again after the preferred shield changes zones.");
            Assert.That(session.PendingChoice.Cards.Count, Is.EqualTo(3));
        }

        private static GameState State() => new GameState { Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } }, ActivePlayerId = 0, Phase = GamePhase.Main, TurnNumber = 5 };
        private static GameState AttackState()
        {
            var state = State();
            state.Players[0].BattleZone.Add(Card(1, "attacker", Civilization.Fire));
            foreach (var id in new long[] { 10, 11, 12 }) state.Players[1].Shields.Add(Card(id, "shield " + id, Civilization.Nature));
            return state;
        }
        private static CardInstance Card(long id, string name, params Civilization[] civilizations) => new CardInstance
        {
            InstanceId = id, EnteredBattleZoneOnTurn = -1,
            Definition = new CardDefinition { Id = name, Name = name, Civilizations = civilizations, Type = CardType.Creature,
                Cost = 1, Power = 2000, ImplementationStatus = CardImplementationStatus.Complete }
        };
    }
}
