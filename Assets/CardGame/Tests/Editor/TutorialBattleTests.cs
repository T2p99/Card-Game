using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class TutorialBattleTests
    {
        [Test]
        public void NewTutorial_ReproducesLegalDecksHandsShieldsAndDrawOrder()
        {
            var first = new TutorialBattle(new RulesEngine()).InitialState;
            var second = new TutorialBattle(new RulesEngine()).InitialState;
            foreach (var owner in new[] { 0, 1 })
            {
                var player = first.Players[owner];
                var cards = player.Hand.Concat(player.Deck).Concat(player.Shields).ToArray();
                Assert.That(cards.Length, Is.EqualTo(40));
                Assert.That(cards.GroupBy(c => c.Definition.Name).All(group => group.Count() <= 4), Is.True);
                Assert.That(cards.All(c => c.Definition.ImplementationStatus == CardImplementationStatus.Complete), Is.True);
                Assert.That(player.Hand.Count, Is.EqualTo(5));
                Assert.That(player.Shields.Count, Is.EqualTo(5));
                Assert.That(player.Deck.Count, Is.EqualTo(30));
                Assert.That(cards.Select(c => c.InstanceId).Distinct().Count(), Is.EqualTo(40));
                foreach (var zone in new[] { player.Hand, player.Shields, player.Deck })
                {
                    var comparison = ReferenceEquals(zone, player.Hand) ? second.Players[owner].Hand :
                        ReferenceEquals(zone, player.Shields) ? second.Players[owner].Shields : second.Players[owner].Deck;
                    Assert.That(zone.Select(c => c.InstanceId + ":" + c.Definition.Id),
                        Is.EqualTo(comparison.Select(c => c.InstanceId + ":" + c.Definition.Id)));
                }
                Assert.That(player.Shields.All(c => !c.HasKeyword(CardKeyword.ShieldTrigger)), Is.True);
            }
        }

        [Test]
        public void Introductions_RequireRealHandAndDeckInspection()
        {
            var course = new TutorialBattle(new RulesEngine());
            Assert.That(course.Continue().Success, Is.True);
            Assert.That(course.Step, Is.EqualTo(TutorialStep.Hand));
            Assert.That(course.Continue().Success, Is.False);
            course.InspectCard(course.InitialState, course.InitialState.Players[1].Hand[0].InstanceId);
            Assert.That(course.CanContinue, Is.False);
            course.InspectCard(course.InitialState, course.FocusCardId);
            Assert.That(course.Continue().Success, Is.True);
            course.InspectDeck(1);
            Assert.That(course.CanContinue, Is.False);
            course.InspectDeck(0);
            Assert.That(course.Continue().Success, Is.True);
            Assert.That(course.Step, Is.EqualTo(TutorialStep.Shields));
        }

        [Test]
        public void GuidedActions_BlockUnrelatedCardsAndDoNotAdvanceOnFailuresOrNoOps()
        {
            var course = new TutorialBattle(new RulesEngine());
            var state = course.InitialState;
            AdvanceExplanations(course, state);
            Assert.That(course.Step, Is.EqualTo(TutorialStep.ChargeFirstMana));
            Assert.That(course.Allows(TutorialAction.ChargeMana, course.FocusCardId), Is.True);
            Assert.That(course.Allows(TutorialAction.ChargeMana,
                state.Players[0].Hand.First(c => c.InstanceId != course.FocusCardId).InstanceId), Is.False);
            Assert.That(course.Allows(TutorialAction.PlayCard, course.FocusCardId), Is.False);
            Assert.That(course.Allows(TutorialAction.EndTurn), Is.False);
            course.Observe(state, state, RuleResult.Fail("Not this player's turn."));
            course.Observe(state, state, RuleResult.Ok());
            Assert.That(course.Step, Is.EqualTo(TutorialStep.ChargeFirstMana));
            Assert.That(course.Continue().Success, Is.False);
        }

        [Test]
        public void FullGuidedBattle_UsesRealCostsDrawsSicknessShieldMovesCombatAndSpells()
        {
            var course = new TutorialBattle(new RulesEngine());
            var state = course.InitialState;
            var visited = new HashSet<TutorialStep>();
            for (var i = 0; i < 100 && course.Step != TutorialStep.Practice; i++)
            {
                visited.Add(course.Step);
                AssertGuidedState(course, state);
                state = AdvanceOne(course, state);
            }
            Assert.That(course.Step, Is.EqualTo(TutorialStep.Practice));
            Assert.That(visited, Does.Contain(TutorialStep.SummoningSickness));
            Assert.That(visited, Does.Contain(TutorialStep.YourShieldToHand));
            Assert.That(visited, Does.Contain(TutorialStep.Graveyard));
            Assert.That(visited, Does.Contain(TutorialStep.SpellResolved));
            Assert.That(state.Players[1].Shields.Count, Is.EqualTo(3));
            Assert.That(state.Players[0].Shields.Count, Is.EqualTo(4));
            var speed = state.Players[0].BattleZone.Single(c => c.Definition.Id == TutorialBattle.SpeedAttackerId);
            Assert.That(speed.EnteredBattleZoneOnTurn, Is.EqualTo(state.TurnNumber));
            Assert.That(speed.Tapped, Is.True);
            Assert.That(state.IsFinished, Is.False);
            Assert.That(state.Players[0].Mana.Count(c => c.Tapped), Is.EqualTo(3));
            Assert.That(state.Players[1].Graveyard.Select(c => c.InstanceId),
                Is.EquivalentTo(new[] { course.FirstEnemyId, course.SecondEnemyId }));
        }

        [Test]
        public void FreePractice_AllowsExtraTurnsManaAndSummonsButPreservesNormalRules()
        {
            var course = new TutorialBattle(new RulesEngine());
            var state = ReachPractice(course);
            Assert.That(course.Allows(TutorialAction.EndTurn), Is.True);
            var extra = state.Players[0].Hand.First(c => c.Definition.Id == "dm06-84-110");
            Assert.That(course.Allows(TutorialAction.PlayCard, extra.InstanceId), Is.True);
            var insufficient = new AbilityActionSession(state, 0,
                (engine, trial) => engine.SummonCreature(trial, 0, extra.InstanceId, Array.Empty<long>()));
            Assert.That(insufficient.Result.Success, Is.False);
            Assert.That(insufficient.State, Is.SameAs(state));
            for (var turn = 0; turn < 3; turn++)
            {
                state = Apply(course, state, (engine, trial) => engine.EndTurn(trial, 0));
                while (state.ActivePlayerId == 1) state = Apply(course, state, course.PlayOpponentAction);
                Assert.That(course.Step, Is.EqualTo(TutorialStep.Practice));
            }
            var charge = state.Players[0].Hand.First(c => c.InstanceId != extra.InstanceId);
            Assert.That(course.Allows(TutorialAction.ChargeMana, charge.InstanceId), Is.True);
            state = Apply(course, state, (engine, trial) => engine.ChargeMana(trial, 0, charge.InstanceId));
            state = Apply(course, state, (engine, trial) =>
                engine.SummonCreature(trial, 0, extra.InstanceId,
                    engine.FindManaPayment(trial, 0, trial.Players[0].Hand.Single(c => c.InstanceId == extra.InstanceId))));
            var check = new RulesEngine().ValidateAttack(state, 0, extra.InstanceId, AttackTargetKind.Player);
            Assert.That(check.Success, Is.False);
            Assert.That(check.Error, Does.Contain("summoning sickness"));
        }

        [Test]
        public void FinishingPractice_RequiresAnAttackAfterTheFifthShield()
        {
            var course = new TutorialBattle(new RulesEngine());
            var state = ReachPractice(course);
            var engine = new RulesEngine();
            var sawNoShieldsWithoutVictory = false;
            var extraHumanTurns = 0;
            for (var i = 0; i < 40 && !state.IsFinished; i++)
            {
                if (state.ActivePlayerId == 1)
                    state = Apply(course, state, course.PlayOpponentAction);
                else if (state.PendingAttack != null)
                {
                    state = Apply(course, state, (e, trial) => e.DeclineBlockAndResolve(trial, 1));
                    if (state.Players[1].Shields.Count == 0 && !state.IsFinished)
                    {
                        sawNoShieldsWithoutVictory = true;
                        Assert.That(course.Step, Is.EqualTo(TutorialStep.Practice));
                        Assert.That(course.Guidance(state), Does.Contain("NOT won yet"));
                    }
                }
                else
                {
                    var attacker = state.Players[0].BattleZone.FirstOrDefault(c =>
                        engine.ValidateAttack(state, 0, c.InstanceId, AttackTargetKind.Player).Success);
                    if (attacker == null)
                    {
                        extraHumanTurns++;
                        state = Apply(course, state, (e, trial) => e.EndTurn(trial, 0));
                    }
                    else
                        state = Apply(course, state, (e, trial) => e.DeclareAttack(trial, 0,
                            attacker.InstanceId, AttackTargetKind.Player, 0,
                            trial.Players[1].Shields.FirstOrDefault()?.InstanceId ?? 0));
                }
            }
            Assert.That(sawNoShieldsWithoutVictory, Is.True);
            Assert.That(extraHumanTurns, Is.GreaterThanOrEqualTo(2));
            Assert.That(state.WinnerPlayerId, Is.EqualTo(0));
            Assert.That(course.Step, Is.EqualTo(TutorialStep.Finished));
        }

        private static GameState ReachPractice(TutorialBattle course)
        {
            var state = course.InitialState;
            for (var i = 0; i < 100 && course.Step != TutorialStep.Practice; i++)
                state = AdvanceOne(course, state);
            Assert.That(course.Step, Is.EqualTo(TutorialStep.Practice));
            return state;
        }

        private static void AdvanceExplanations(TutorialBattle course, GameState state)
        {
            while (course.IsExplanation)
            {
                course.InspectCard(state, course.FocusCardId);
                course.InspectDeck(0);
                Assert.That(course.Continue().Success, Is.True, course.Title);
            }
        }

        private static GameState AdvanceOne(TutorialBattle course, GameState state)
        {
            if (course.IsExplanation)
            {
                course.InspectCard(state, course.FocusCardId);
                course.InspectDeck(0);
                Assert.That(course.Continue().Success, Is.True, course.Title);
                return state;
            }
            if (course.Step == TutorialStep.OpponentTurn) return Apply(course, state, course.PlayOpponentAction);
            if (state.PendingAttack != null)
            {
                Assert.That(course.CanContinue, Is.False);
                return Apply(course, state, (engine, trial) => engine.DeclineBlockAndResolve(trial, 1));
            }
            var cardId = course.FocusCardId;
            if (course.Allows(TutorialAction.ChargeMana, cardId))
                return Apply(course, state, (engine, trial) => engine.ChargeMana(trial, 0, cardId));
            if (course.Allows(TutorialAction.PlayCard, cardId))
                return Apply(course, state, (engine, trial) =>
                {
                    var card = trial.Players[0].Hand.Single(c => c.InstanceId == cardId);
                    var mana = engine.FindManaPayment(trial, 0, card);
                    return card.Definition.Type == CardType.Spell
                        ? engine.CastSpell(trial, 0, cardId, mana)
                        : engine.SummonCreatureWithChoices(trial, 0, cardId, mana);
                });
            if (course.Allows(TutorialAction.EndTurn))
                return Apply(course, state, (engine, trial) => engine.EndTurn(trial, 0));
            var kind = course.Step == TutorialStep.CreatureBattle ? AttackTargetKind.Creature : AttackTargetKind.Player;
            Assert.That(course.Allows(kind == AttackTargetKind.Creature ? TutorialAction.AttackCreature :
                TutorialAction.AttackPlayer, cardId, course.TargetCardId), Is.True, course.Title);
            return Apply(course, state, (engine, trial) => engine.DeclareAttack(trial, 0, cardId,
                kind, course.TargetCardId, kind == AttackTargetKind.Player ? trial.Players[1].Shields[0].InstanceId : 0));
        }

        private static GameState Apply(TutorialBattle course, GameState state,
            Func<RulesEngine, GameState, RuleResult> command)
        {
            var session = new AbilityActionSession(state, 0, command);
            for (var i = 0; i < 10 && !session.IsComplete; i++)
            {
                var choice = session.PendingChoice;
                if (choice.Kind == AbilityChoiceKind.Cards)
                    Assert.That(session.AnswerCards(choice.Cards.Take(choice.Minimum).Select(c => c.InstanceId)), Is.True);
                else Assert.That(session.AnswerValue(choice.Minimum), Is.True);
            }
            Assert.That(session.IsComplete, Is.True, course.Title);
            Assert.That(session.Result.Success, Is.True, course.Title + ": " + session.Result.Error);
            course.Observe(state, session.State, session.Result);
            return session.State;
        }

        private static void AssertGuidedState(TutorialBattle course, GameState state)
        {
            var engine = new RulesEngine();
            var human = state.Players[0];
            switch (course.Step)
            {
                case TutorialStep.FirstMana:
                    Assert.That(human.Mana.Count, Is.EqualTo(1));
                    Assert.That(human.Hand.All(c => c.Definition.Cost > 1), Is.True);
                    break;
                case TutorialStep.DrawCard:
                    Assert.That(state.CardsDrawnThisTurn[0], Is.EqualTo(1));
                    Assert.That(human.Deck.Count, Is.EqualTo(29));
                    break;
                case TutorialStep.SummoningSickness:
                    Assert.That(human.Mana.Count(c => c.Tapped), Is.EqualTo(2));
                    Assert.That(engine.ValidateAttack(state, 0, course.FocusCardId, AttackTargetKind.Player).Error,
                        Does.Contain("summoning sickness"));
                    Assert.That(course.CanContinue, Is.False);
                    break;
                case TutorialStep.ReadyCreature:
                    Assert.That(human.Mana.All(c => !c.Tapped), Is.True);
                    Assert.That(engine.ValidateAttack(state, 0, course.FocusCardId, AttackTargetKind.Player).Success, Is.True);
                    Assert.That(engine.ValidateAttack(state, 0, course.FocusCardId,
                        AttackTargetKind.Creature, course.FirstEnemyId).Success, Is.False);
                    break;
                case TutorialStep.YourShieldToHand:
                    Assert.That(human.Shields.Count, Is.EqualTo(4));
                    Assert.That(human.Hand.Any(c => c.InstanceId == course.FocusCardId), Is.True);
                    Assert.That(state.Players[1].BattleZone.Single().Tapped, Is.True);
                    break;
                case TutorialStep.Graveyard:
                    Assert.That(human.BattleZone.Single().Definition.Id, Is.EqualTo(TutorialBattle.NormalCreatureId));
                    Assert.That(state.Players[1].Shields.Count, Is.EqualTo(4));
                    break;
                case TutorialStep.SpellResolved:
                    Assert.That(human.Mana.Count(c => !c.Tapped), Is.EqualTo(1));
                    Assert.That(human.Graveyard.Single().Definition.Id, Is.EqualTo(TutorialBattle.SpellId));
                    break;
                case TutorialStep.SpeedAttack:
                    Assert.That(engine.ValidateAttack(state, 0, course.FocusCardId, AttackTargetKind.Player).Success,
                        Is.EqualTo(state.PendingAttack == null));
                    break;
            }
        }
    }
}
