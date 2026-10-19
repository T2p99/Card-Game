using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class PreviewerCoverageTests
    {
        public static IEnumerable<CardDefinition> Catalog => TcgCatalog.Printings;

        [TestCaseSource(nameof(Catalog))]
        public void EveryPrinting_ExposesEveryExecutableRuleWithoutMutatingState(CardDefinition definition)
        {
            var state = State();
            var card = Card(10, definition);
            state.Players[0].BattleZone.Add(card);
            state.Players[1].BattleZone.Add(Card(20, Creature("opponent")));
            var engine = new RulesEngine();
            var before = Fingerprint(state);
            var previews = engine.GetAbilityPreviews(state, card);
            foreach (var rule in definition.RuleAbilities)
                Assert.That(previews.Any(p => ReferenceEquals(p.Ability, rule)), Is.True, definition.Name + " / " + rule.Id);
            engine.GetEffectiveKeywords(state, card);
            engine.CurrentPower(state, card);
            engine.CurrentPower(state, card, true);
            engine.MinimumManaCost(state, card);
            engine.GetAttackTargetCandidates(state, 0, card.InstanceId);
            engine.ValidateTapAbility(state, 0, card.InstanceId);
            Assert.That(Fingerprint(state), Is.EqualTo(before), definition.Name);
        }

        [TestCaseSource(nameof(Catalog))]
        public void EveryPrinting_PlayChoicesReplayToTheSameResultAsAutomaticResolution(CardDefinition definition)
        {
            var state = State();
            state.Players[0].Hand.Add(Card(10, definition));
            state.Players[0].BattleZone.Add(Card(20, Creature("own target")));
            state.Players[0].Graveyard.Add(Card(21, Creature("graveyard target")));
            state.Players[1].BattleZone.Add(Card(22, Creature("opponent target")));
            for (var i = 0; i < 20; i++)
                state.Players[0].Mana.Add(Card(100 + i, Creature("mana", (Civilization)(i % 5))));
            var engine = new RulesEngine();
            var payment = new List<long>();
            foreach (var civilization in definition.Civilizations.Distinct())
                payment.Add(state.Players[0].Mana.First(c => !payment.Contains(c.InstanceId) && c.Definition.Civilizations.Contains(civilization)).InstanceId);
            payment.AddRange(state.Players[0].Mana.Where(c => !payment.Contains(c.InstanceId))
                .Take(Math.Max(0, engine.MinimumManaCost(state, state.Players[0].Hand[0]) - payment.Count)).Select(c => c.InstanceId));
            Func<RulesEngine, GameState, RuleResult> command = (e, s) => definition.IsCreature
                ? e.SummonCreatureWithChoices(s, 0, 10, payment)
                : e.CastSpell(s, 0, 10, payment);
            var automatic = GameStateSnapshot.Copy(state);
            var expected = command(new RulesEngine(), automatic);
            var before = Fingerprint(state);
            var session = new AbilityActionSession(state, 0, command);
            var steps = 0;
            while (!session.IsComplete)
            {
                Assert.That(++steps, Is.LessThan(100), definition.Name);
                var choice = session.PendingChoice;
                if (choice.Kind == AbilityChoiceKind.Cards)
                {
                    var cards = choice.Cards.OrderBy(c => c.InstanceId).Take(choice.Maximum).ToArray();
                    if (choice.MaximumCombinedPower > 0)
                    {
                        var sum = 0;
                        cards = cards.Where(c => { var power = new RulesEngine().CurrentPower(state, c);
                            if (sum + power > choice.MaximumCombinedPower) return false; sum += power; return true; }).ToArray();
                    }
                    Assert.That(session.AnswerCards(cards.Select(c => c.InstanceId)), Is.True, definition.Name);
                }
                else Assert.That(session.AnswerValue(choice.Kind == AbilityChoiceKind.Number ? choice.Suggested : 0), Is.True);
                Assert.That(Fingerprint(state), Is.EqualTo(before), "A pending choice mutated the original match.");
            }
            Assert.That(session.Result.Success, Is.EqualTo(expected.Success), definition.Name);
            Assert.That(Fingerprint(session.State), Is.EqualTo(Fingerprint(expected.Success ? automatic : state)), definition.Name);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SilentSkill_OffersUseOrUntapDuringOpponentEndTurn(bool use)
        {
            var state = State();
            state.ActivePlayerId = 1;
            var definition = Creature("silent");
            definition.Abilities = new[] { new KeywordAbility(CardKeyword.SilentSkill) };
            definition.RuleAbilities = new[] { new RuleAbilityDefinition("silent.draw", AbilityTiming.SilentSkill, AbilityOperation.Draw) };
            var card = Card(10, definition); card.Tapped = true;
            state.Players[0].BattleZone.Add(card);
            var session = new AbilityActionSession(state, 0, (e, s) => e.EndTurn(s, 1));
            Assert.That(session.PendingChoice.Kind, Is.EqualTo(AbilityChoiceKind.Options));
            Assert.That(state.ActivePlayerId, Is.EqualTo(1));
            Assert.That(session.AnswerValue(use ? 0 : 1), Is.True);
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.State.ActivePlayerId, Is.Zero);
            Assert.That(session.State.Players[0].BattleZone[0].Tapped, Is.EqualTo(use));
            Assert.That(session.State.Players[0].Hand.Count, Is.EqualTo(use ? 2 : 1));
            Assert.That(state.Players[0].Hand, Is.Empty);
        }

        [Test]
        public void MultiStepOptionalChoice_CommitsManaAndEffectsExactlyOnceAndRejectsBadAnswers()
        {
            var state = State();
            var definition = Creature("choice spell"); definition.Type = CardType.Spell;
            definition.RuleAbilities = new[] {
                new RuleAbilityDefinition("steps", AbilityTiming.OnCast, AbilityOperation.Draw) { Optional = true },
                new RuleAbilityDefinition("steps", AbilityTiming.OnCast, AbilityOperation.Move) {
                    Selector = new RuleSelectorDefinition { Zone = RuleZone.Graveyard, Minimum = 0, Maximum = 2 }, Destination = RuleZone.Hand }
            };
            state.Players[0].Hand.Add(Card(10, definition));
            state.Players[0].Mana.Add(Card(11, Creature("mana")));
            state.Players[0].Graveyard.AddRange(new[] { Card(12, Creature("A")), Card(13, Creature("B")), Card(14, Creature("C")) });
            var session = new AbilityActionSession(state, 0, (e, s) => e.CastSpell(s, 0, 10, new long[] { 11 }));
            Assert.That(session.AnswerValue(0), Is.True);
            Assert.That(session.PendingChoice.Maximum, Is.EqualTo(2));
            Assert.That(session.AnswerCards(new long[] { 12, 12 }), Is.False);
            Assert.That(session.AnswerCards(new long[] { 999 }), Is.False);
            Assert.That(state.Players[0].Mana[0].Tapped, Is.False);
            Assert.That(session.AnswerCards(new long[] { 14, 12 }), Is.True);
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.State.Players[0].Hand.Select(c => c.InstanceId), Is.EquivalentTo(new long[] { 1009, 14, 12 }));
            Assert.That(session.State.Players[0].Graveyard.Select(c => c.InstanceId), Is.EquivalentTo(new long[] { 13, 10 }));
            Assert.That(session.State.Players[0].Deck.Count, Is.EqualTo(9));
            Assert.That(session.State.Players[0].Mana[0].Tapped, Is.True);
            Assert.That(state.Players[0].Hand.Select(c => c.InstanceId), Is.EqualTo(new long[] { 10 }));
        }

        [Test]
        public void ShieldSelection_DoesNotRevealFaceDownCards()
        {
            var state = State();
            state.Players[0].BattleZone.Add(Card(10, Creature("attacker")));
            state.Players[1].Shields.AddRange(new[] { Card(11, Creature("secret A")), Card(12, Creature("secret B")) });
            Assert.That(new RulesEngine().DeclareAttack(state, 0, 10, AttackTargetKind.Player).Success, Is.True);
            state.InspectionPlayerId = 0;
            state.InspectedCards.Add(state.Players[1].Shields[0]);
            var session = new AbilityActionSession(state, 0, (e, s) => e.DeclineBlockAndResolve(s, 1));
            Assert.That(session.PendingChoice.CardLabels, Is.EqualTo(new[] { "Face-down shield 1", "Face-down shield 2" }));
            Assert.That(session.AnswerCards(new long[] { 12 }), Is.True);
            Assert.That(session.State.Players[1].Hand.Single().InstanceId, Is.EqualTo(12));
            Assert.That(state.Players[1].Shields.Count, Is.EqualTo(2));
        }

        [Test]
        public void Snapshot_PreservesSharedReferencesAndIsolatesNestedMutableState()
        {
            var state = State();
            var top = Card(10, Creature("top")); var material = Card(11, Creature("material"));
            top.EvolutionMaterials.Add(material); state.Players[0].BattleZone.Add(top);
            state.CurrentEventCard = material;
            state.DelayedAbilities.Add(new DelayedAbilityEffect { Source = top, FixedTargets = new List<CardInstance> { material } });
            state.UntapAfterCurrentBattle.Add(new CardIncarnationReference(material));
            var copy = GameStateSnapshot.Copy(state);
            var copiedMaterial = copy.Players[0].BattleZone[0].EvolutionMaterials[0];
            Assert.That(copy.CurrentEventCard, Is.SameAs(copiedMaterial));
            Assert.That(copy.DelayedAbilities[0].FixedTargets[0], Is.SameAs(copiedMaterial));
            Assert.That(copy.UntapAfterCurrentBattle[0].Matches(copiedMaterial), Is.True);
            copiedMaterial.TemporaryRaces.Add("new race"); copy.SpellCastThisTurn[0] = true;
            Assert.That(material.TemporaryRaces, Is.Empty);
            Assert.That(state.SpellCastThisTurn[0], Is.False);
        }

        [Test]
        public void RaceCivilizationNumberAndPlayerChoices_AreAllPresentedInResolutionOrder()
        {
            var state = State();
            var definition = Creature("choices"); definition.Type = CardType.Spell;
            definition.RuleAbilities = new[] {
                new RuleAbilityDefinition("steps", AbilityTiming.OnCast, AbilityOperation.ChooseRace),
                new RuleAbilityDefinition("steps", AbilityTiming.OnCast, AbilityOperation.PreventCivilizationAttackPlayer),
                new RuleAbilityDefinition("steps", AbilityTiming.OnCast, AbilityOperation.Draw) { ChooseAmount = true, Amount = 2 },
                new RuleAbilityDefinition("steps", AbilityTiming.OnCast, AbilityOperation.SearchPlayerDeckToGraveyard)
            };
            state.Players[0].Hand.Add(Card(10, definition));
            state.Players[0].Mana.Add(Card(11, Creature("mana")));
            var session = new AbilityActionSession(state, 0, (e, s) => e.CastSpell(s, 0, 10, new long[] { 11 }));
            var race = session.PendingChoice.Options.ToList().IndexOf("Human");
            Assert.That(race, Is.GreaterThanOrEqualTo(0)); session.AnswerValue(race);
            Assert.That(session.PendingChoice.Options, Does.Contain("Water"));
            session.AnswerValue(session.PendingChoice.Options.ToList().IndexOf("Water"));
            Assert.That(session.PendingChoice.Kind, Is.EqualTo(AbilityChoiceKind.Number));
            Assert.That(session.AnswerValue(3), Is.False); session.AnswerValue(0);
            Assert.That(session.PendingChoice.Options, Is.EqualTo(new[] { "You", "Opponent" }));
            session.AnswerValue(1);
            Assert.That(session.PendingChoice.Cards.All(c => c.InstanceId >= 1100), Is.True);
            session.AnswerCards(new long[] { 1104 });
            Assert.That(session.Result.Success, Is.True);
            Assert.That(session.State.Players[1].Graveyard.Single().InstanceId, Is.EqualTo(1104));
            Assert.That(session.State.PreventOpponentAttackCivilizationsUntilNextTurn.Single().Civilization, Is.EqualTo(Civilization.Water));
        }

        [Test]
        public void PowerBudget_RejectsSelectionTheEngineWouldOtherwiseSilentlyTruncate()
        {
            var state = State();
            var definition = Creature("budget"); definition.Type = CardType.Spell;
            definition.RuleAbilities = new[] { new RuleAbilityDefinition("budget", AbilityTiming.OnCast, AbilityOperation.Destroy) {
                Selector = new RuleSelectorDefinition { Controller = RuleController.Opponent,
                    Mode = RuleSelectionMode.PowerBudget, Minimum = 0, Maximum = 2, MaximumCombinedPower = 3000 } } };
            state.Players[0].Hand.Add(Card(10, definition)); state.Players[0].Mana.Add(Card(11, Creature("mana")));
            state.Players[1].BattleZone.AddRange(new[] { Card(20, Creature("A")), Card(21, Creature("B")) });
            var session = new AbilityActionSession(state, 0, (e, s) => e.CastSpell(s, 0, 10, new long[] { 11 }));
            Assert.That(session.AnswerCards(new long[] { 20, 21 }), Is.False);
            Assert.That(session.AnswerCards(new long[] { 21 }), Is.True);
            Assert.That(session.State.Players[1].Graveyard.Single().InstanceId, Is.EqualTo(21));
        }

        public static IEnumerable<CardDefinition> VortexCards => Catalog.Where(c => c.IsVortexEvolution);
        [TestCaseSource(nameof(VortexCards))]
        public void EveryVortex_OffersOnlyCompletableBasePairs(CardDefinition definition)
        {
            var state = State(); state.Players[0].Hand.Add(Card(10, definition));
            for (var i = 0; i < 2; i++)
            {
                var baseDefinition = Creature("base " + i); baseDefinition.Races = definition.EvolutionRequirements[i].Races;
                state.Players[0].BattleZone.Add(Card(20 + i, baseDefinition));
            }
            state.Players[0].BattleZone.Add(Card(22, Creature("wrong race")));
            var payment = new List<long>();
            for (var i = 0; i < definition.Cost; i++) {
                var mana = Card(100 + i, Creature("mana", definition.Civilizations[i % definition.Civilizations.Length]));
                state.Players[0].Mana.Add(mana); payment.Add(mana.InstanceId); }
            var session = new AbilityActionSession(state, 0, (e, s) => e.SummonCreatureWithChoices(s, 0, 10, payment));
            Assert.That(session.PendingChoice.Cards.Select(c => c.InstanceId), Is.EquivalentTo(new long[] { 20, 21 }));
            session.AnswerCards(new long[] { 21 });
            while (!session.IsComplete) {
                var choice = session.PendingChoice;
                if (choice.Kind == AbilityChoiceKind.Cards) session.AnswerCards(choice.Cards.Take(choice.Minimum).Select(c => c.InstanceId));
                else session.AnswerValue(choice.Kind == AbilityChoiceKind.Number ? choice.Minimum : 0);
            }
            Assert.That(session.Result.Success, Is.True);
            var evolved = session.State.Players[0].BattleZone.Single(c => c.InstanceId == 10);
            Assert.That(evolved.EvolutionMaterials.Select(c => c.InstanceId), Is.EquivalentTo(new long[] { 20, 21 }));
        }

        [Test]
        public void EvolutionPicker_UsesGrantedRacesAndUniversalBases()
        {
            var state = State();
            var evolution = Creature("evolution"); evolution.Type = CardType.EvolutionCreature;
            evolution.EvolutionRaces = new[] { "Angel Command" };
            state.Players[0].Hand.Add(Card(10, evolution));
            var granted = Card(20, Creature("granted")); granted.TemporaryRaces.Add("Angel Command");
            var universal = Creature("universal"); universal.CanBeAnyEvolutionBase = true;
            state.Players[0].BattleZone.AddRange(new[] { granted, Card(21, universal), Card(22, Creature("wrong")) });
            Assert.That(new RulesEngine().GetEvolutionBaseCandidates(state, 0, 10).Select(c => c.InstanceId),
                Is.EquivalentTo(new long[] { 20, 21 }));
        }

        [Test]
        public void SharedTapAbility_IsOfferedOnlyWhileSourceConditionHolds()
        {
            var state = State();
            var shared = Creature("shared"); shared.RuleAbilities = new[] {
                new RuleAbilityDefinition("share", AbilityTiming.Static, AbilityOperation.ShareTapAbility) {
                    Selector = new RuleSelectorDefinition { Mode = RuleSelectionMode.All },
                    Condition = new AbilityConditionDefinition { SpellWasCastThisTurn = true } },
                new RuleAbilityDefinition("draw", AbilityTiming.TapAbility, AbilityOperation.Draw) };
            state.Players[0].BattleZone.Add(Card(10, shared));
            var receiver = Card(11, Creature("receiver")); state.Players[0].BattleZone.Add(receiver);
            var engine = new RulesEngine();
            Assert.That(engine.HasTapAbility(state, receiver), Is.False);
            state.SpellCastThisTurn[0] = true;
            Assert.That(engine.HasTapAbility(state, receiver), Is.True);
            Assert.That(engine.GetAbilityPreviews(state, receiver).Any(p => p.Source.InstanceId == 10 && p.Ability.Timing == AbilityTiming.TapAbility), Is.True);
            var session = new AbilityActionSession(state, 0, (e, s) => e.UseTapAbility(s, 0, 11));
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.State.Players[0].Hand.Count, Is.EqualTo(1));
            Assert.That(state.Players[0].Hand, Is.Empty);
        }

        [Test]
        public void SimultaneousTriggers_AskWhichAbilityResolvesFirst()
        {
            var state = State();
            var definition = Creature("triggers"); definition.RuleAbilities = new[] {
                new RuleAbilityDefinition("first", AbilityTiming.OnSummon, AbilityOperation.Draw) { RulesText = "Draw a card." },
                new RuleAbilityDefinition("second", AbilityTiming.OnSummon, AbilityOperation.Draw) { RulesText = "Draw two cards.", Amount = 2 } };
            state.Players[0].Hand.Add(Card(10, definition)); state.Players[0].Mana.Add(Card(11, Creature("mana")));
            var session = new AbilityActionSession(state, 0, (e, s) => e.SummonCreatureWithChoices(s, 0, 10, new long[] { 11 }));
            Assert.That(session.PendingChoice.Options, Is.EqualTo(new[] { "Draw a card.", "Draw two cards." }));
            session.AnswerValue(1);
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.State.Players[0].Hand.Count, Is.EqualTo(3));
        }

        [Test]
        public void DestructionReplacement_OffersAllLegalReplacements()
        {
            var state = State();
            var targetDefinition = Creature("protected"); targetDefinition.RuleAbilities = new[] {
                new RuleAbilityDefinition("mana", AbilityTiming.ReplacementDestroy, AbilityOperation.Move) {
                    RulesText = "Move to mana instead.", Destination = RuleZone.ManaZone, Selector = new RuleSelectorDefinition { Mode = RuleSelectionMode.SourceCard } },
                new RuleAbilityDefinition("hand", AbilityTiming.ReplacementDestroy, AbilityOperation.Move) {
                    RulesText = "Return to hand instead.", Destination = RuleZone.Hand, Selector = new RuleSelectorDefinition { Mode = RuleSelectionMode.SourceCard } } };
            state.Players[0].BattleZone.Add(Card(20, targetDefinition));
            var spell = Creature("destroy"); spell.Type = CardType.Spell;
            spell.RuleAbilities = new[] { new RuleAbilityDefinition("destroy", AbilityTiming.OnCast, AbilityOperation.Destroy) };
            state.Players[0].Hand.Add(Card(10, spell)); state.Players[0].Mana.Add(Card(11, Creature("mana")));
            var session = new AbilityActionSession(state, 0, (e, s) => e.CastSpell(s, 0, 10, new long[] { 11 }));
            Assert.That(session.PendingChoice.Kind, Is.EqualTo(AbilityChoiceKind.Options));
            Assert.That(session.PendingChoice.Options.Count, Is.EqualTo(2)); session.AnswerValue(1);
            Assert.That(session.State.Players[0].Hand.Single().InstanceId, Is.EqualTo(20));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PrintedOptionalEndTurnUntap_IsAPlayerChoice(bool untap)
        {
            var state = State();
            var urth = Card(10, TcgCatalog.ByName("Urth, Purifying Elemental").First());
            urth.Tapped = true; state.Players[0].BattleZone.Add(urth);
            var session = new AbilityActionSession(state, 0, (e, s) => e.EndTurn(s, 0));
            Assert.That(session.PendingChoice.Kind, Is.EqualTo(AbilityChoiceKind.Options));
            session.AnswerValue(untap ? 0 : 1);
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.State.Players[0].BattleZone[0].Tapped, Is.EqualTo(!untap));
        }

        private static string Fingerprint(GameState state) =>
            state.ActivePlayerId + ":" + state.TurnNumber + ":" + state.Phase + ":" + state.WinnerPlayerId + ":" +
            state.RandomActionIndex + ":" + state.MainActionTakenThisTurn + ":" + string.Join("|", state.Players.SelectMany(p =>
                new[] { p.Deck, p.Hand, p.Mana, p.BattleZone, p.Graveyard, p.Shields, p.Resolving }.Select(zone =>
                    string.Join(",", zone.Select(c => c.InstanceId + ":" + c.Tapped + ":" + c.ZoneChangeVersion + ":" +
                        c.TemporaryPower + ":" + c.ChosenRace + ":" + c.HasChosenCivilization + ":" + c.ChosenCivilization)))));

        private static GameState State()
        {
            var state = new GameState { Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
                TurnNumber = 5, Phase = GamePhase.Main, RandomSeed = 9876 };
            for (var player = 0; player < 2; player++)
                for (var i = 0; i < 10; i++) state.Players[player].Deck.Add(Card(1000 + player * 100 + i, Creature("deck")));
            return state;
        }
        private static CardInstance Card(long id, CardDefinition definition) => new CardInstance { InstanceId = id, Definition = definition, EnteredBattleZoneOnTurn = -1 };
        private static CardDefinition Creature(string name, Civilization civilization = Civilization.Fire) => new CardDefinition {
            Id = name, Name = name, Cost = 1, Power = 2000, Type = CardType.Creature, Civilizations = new[] { civilization },
            Races = new[] { "Human" }, ImplementationStatus = CardImplementationStatus.Complete };
    }
}
