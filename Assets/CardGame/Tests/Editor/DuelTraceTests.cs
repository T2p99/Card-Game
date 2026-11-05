using System;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class DuelTraceTests
    {
        [TestCase(0)]
        [TestCase(2)]
        public void PeppiAttacksTappedPropeller_RecordsWinningBattleAndHandOnlyDiscard(int handCopies)
        {
            var state = State();
            var peppi = Card(101, "dm12-50-55");
            var mutant = Card(201, "dm07-31-55");
            mutant.Tapped = true;
            state.Players[0].BattleZone.Add(peppi);
            state.Players[1].BattleZone.Add(mutant);
            for (var i = 0; i < handCopies; i++)
                state.Players[0].Hand.Add(Card(301 + i, "dm12-50-55"));
            var declaration = new AbilityActionSession(state, 0,
                (engine, trial) => engine.DeclareAttack(trial, 0, 101, AttackTargetKind.Creature, 201));

            Assert.That(declaration.Result.Success, Is.True);
            Assert.That(declaration.Trace.Single(entry => entry.Kind == "AttackDeclared")
                .IntendedTargetInstanceId, Is.EqualTo(201));
            var resolution = new AbilityActionSession(declaration.State, 0,
                (engine, trial) => engine.DeclineBlockAndResolve(trial, 1));
            Assert.That(resolution.Result.Success, Is.True);
            var battle = resolution.Trace.Single(entry => entry.Kind == "Battle");
            Assert.That(battle.SourcePower, Is.EqualTo(5000));
            Assert.That(battle.TargetPower, Is.EqualTo(1000));
            Assert.That(battle.SourceLost, Is.False);
            Assert.That(battle.TargetLost, Is.True);
            Assert.That(resolution.State.Players[0].BattleZone.Select(card => card.InstanceId),
                Is.EqualTo(new long[] { 101 }));
            Assert.That(resolution.State.Players[1].Graveyard.Single().InstanceId, Is.EqualTo(201));
            var discards = resolution.Trace.Where(entry => entry.Kind == "Move" &&
                entry.FromZone == "Hand" && entry.ToZone == "Graveyard").ToArray();
            Assert.That(discards.Length, Is.EqualTo(handCopies == 0 ? 0 : 1));
            Assert.That(discards.All(entry => entry.SourceInstanceId != peppi.InstanceId), Is.True);
        }

        [Test]
        public void GigaslugInterception_RecordsActualBlockerAndSlayerDestruction()
        {
            var state = State();
            state.Players[0].BattleZone.Add(Card(101, "dm12-50-55"));
            var mutant = Card(201, "dm07-31-55");
            mutant.Tapped = true;
            state.Players[1].BattleZone.Add(mutant);
            state.Players[1].BattleZone.Add(Card(202, "dm12-47-55"));
            var declaration = new AbilityActionSession(state, 0,
                (engine, trial) => engine.DeclareAttack(trial, 0, 101, AttackTargetKind.Creature, 201));
            var resolution = new AbilityActionSession(declaration.State, 0,
                (engine, trial) => engine.Block(trial, 1, 202));

            Assert.That(resolution.Result.Success, Is.True);
            var block = resolution.Trace.Single(entry => entry.Kind == "BlockDeclared");
            Assert.That(block.SourceName, Is.EqualTo("Gigaslug"));
            Assert.That(block.TargetInstanceId, Is.EqualTo(101));
            Assert.That(block.IntendedTargetInstanceId, Is.EqualTo(201));
            var battle = resolution.Trace.Single(entry => entry.Kind == "Battle");
            Assert.That(battle.TargetInstanceId, Is.EqualTo(202));
            Assert.That(battle.TargetKeywords, Does.Contain("Slayer"));
            Assert.That(battle.SourceLost, Is.False, "Peppi wins on power before Slayer resolves.");
            Assert.That(resolution.Trace.Any(entry => entry.Kind == "Ability" &&
                entry.AbilityId == "rules.slayer.202"), Is.True);
            Assert.That(resolution.Trace.Any(entry => entry.Kind == "Destruction" &&
                entry.SourceInstanceId == 101 && entry.Detail == "Destroyed"), Is.True);
            Assert.That(resolution.State.Players[0].BattleZone, Is.Empty);
            Assert.That(resolution.State.Players[0].Graveyard.Single().InstanceId, Is.EqualTo(101));
            Assert.That(resolution.State.Players[1].BattleZone.Single().InstanceId, Is.EqualTo(201));
        }

        [Test]
        public void AwaitedHumanChoice_PublishesOnlyTheCompletedReplayTrace()
        {
            var state = State();
            state.Players[0].Hand.Add(DestroySpell(101));
            state.Players[0].Mana.Add(Card(301, "dm07-31-55"));
            state.Players[1].BattleZone.Add(Target(201));
            state.Players[1].BattleZone.Add(Target(202));
            var session = new AbilityActionSession(state, 0,
                (engine, trial) => engine.CastSpell(trial, 0, 101, new long[] { 301 }));

            Assert.That(session.IsComplete, Is.False, session.Result?.Error);
            Assert.That(session.Trace, Is.Empty);
            Assert.That(state.Players[1].Graveyard, Is.Empty);
            Assert.That(session.AnswerCards(new long[] { 202 }), Is.True);
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.Result.Success, Is.True);
            var choice = session.Trace.Single(entry => entry.Kind == "Choice" && entry.Detail == "Cards");
            Assert.That(choice.Automatic, Is.False);
            Assert.That(choice.SelectedInstanceIds, Is.EqualTo(new long[] { 202 }));
            Assert.That(session.Trace.Count(entry => entry.Kind == "Move" &&
                entry.SourceInstanceId == 202), Is.EqualTo(1));
            Assert.That(session.State.Players[1].BattleZone.Single().InstanceId, Is.EqualTo(201));
        }

        [Test]
        public void ComputerChoices_AreRecordedWithoutIntroducingHumanPrompts()
        {
            var state = State();
            state.ActivePlayerId = 1;
            var spell = DestroySpell(101);
            spell.Definition.RuleAbilities[0].Optional = true;
            state.Players[1].Hand.Add(spell);
            state.Players[1].Mana.Add(Card(301, "dm07-31-55"));
            state.Players[0].BattleZone.Add(Target(201));
            state.Players[0].BattleZone.Add(Target(202));
            var session = new AbilityActionSession(state, 0,
                (engine, trial) => engine.CastSpell(trial, 1, 101, new long[] { 301 }));

            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.Result.Success, Is.True, session.Result.Error);
            var choices = session.Trace.Where(entry => entry.Kind == "Choice").ToArray();
            Assert.That(choices.Any(entry => entry.Detail == "Options" &&
                entry.Options.Contains("Use ability") && entry.ChoiceValue == 0), Is.True);
            Assert.That(choices.Any(entry => entry.Detail == "Cards" &&
                entry.SelectedInstanceIds.SequenceEqual(new long[] { 201 })), Is.True);
            Assert.That(choices.All(entry => entry.Automatic && entry.PlayerId == 1), Is.True);
        }

        private static GameState State() => new GameState
        {
            Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
            ActivePlayerId = 0,
            StartingPlayerId = 1,
            Phase = GamePhase.Main,
            TurnNumber = 5,
            RandomSeed = 42
        };

        private static CardInstance Card(long id, string cardId) => new CardInstance
        {
            InstanceId = id,
            Definition = TcgCatalog.ById[cardId]
        };

        private static CardInstance Target(long id) => new CardInstance
        {
            InstanceId = id,
            Definition = new CardDefinition
            {
                Id = "target-" + id,
                Name = "Target " + id,
                Type = CardType.Creature,
                Power = 1000,
                ImplementationStatus = CardImplementationStatus.Complete
            }
        };

        private static CardInstance DestroySpell(long id) => new CardInstance
        {
            InstanceId = id,
            Definition = new CardDefinition
            {
                Id = "test-destroy",
                Name = "Test destroy",
                Type = CardType.Spell,
                Cost = 1,
                Civilizations = new[] { Civilization.Darkness },
                ImplementationStatus = CardImplementationStatus.Complete,
                RuleAbilities = new[]
                {
                    new RuleAbilityDefinition("test.destroy", AbilityTiming.OnCast, AbilityOperation.Destroy)
                    {
                        Selector = new RuleSelectorDefinition
                        {
                            Controller = RuleController.Opponent,
                            Zone = RuleZone.BattleZone,
                            Mode = RuleSelectionMode.Choose,
                            Minimum = 1,
                            Maximum = 1
                        }
                    }
                }
            }
        };
    }
}
