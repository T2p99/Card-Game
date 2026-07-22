using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm09CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm09PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-09").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void VenomWorm_UsesTapAbilityTimingAndGrantsSlayerToChosenRace()
        {
            var state = EmptyState();
            var venomWorm = Instance(10, "dm09-34-55");
            var alliedBeast = Instance(20, Creature("allied-beast", Civilization.Nature, "Beast Folk"));
            var opposingBeast = Instance(30, Creature("opposing-beast", Civilization.Nature, "Beast Folk"));
            var guardian = Instance(31, Creature("guardian", Civilization.Light, "Guardian"));
            state.Players[0].BattleZone.Add(venomWorm);
            state.Players[0].BattleZone.Add(alliedBeast);
            state.Players[1].BattleZone.Add(opposingBeast);
            state.Players[1].BattleZone.Add(guardian);
            var decisions = new FixedRaceDecisionProvider("Beast Folk");

            Assert.That(new RulesEngine(decisions).UseTapAbility(state, 0,
                venomWorm.InstanceId).Success, Is.True);
            var resolver = new AbilityResolver(decisions);
            Assert.That(resolver.HasKeyword(state, alliedBeast, CardKeyword.Slayer), Is.True);
            Assert.That(resolver.HasKeyword(state, opposingBeast, CardKeyword.Slayer), Is.True);
            Assert.That(resolver.HasKeyword(state, guardian, CardKeyword.Slayer), Is.False);
        }

        [Test]
        public void Hokira_ProtectsChosenRaceCreaturesThatEnterLaterThisTurn()
        {
            var state = EmptyState();
            var hokira = Instance(10, "dm09-18-55");
            var laterBeast = Instance(20, Creature("later-beast", Civilization.Nature, "Beast Folk"));
            state.Players[0].BattleZone.Add(hokira);
            var decisions = new FixedRaceDecisionProvider("Beast Folk");
            var resolver = new AbilityResolver(decisions);

            Assert.That(new RulesEngine(decisions).UseTapAbility(state, 0,
                hokira.InstanceId).Success, Is.True);
            state.Players[0].BattleZone.Add(laterBeast);
            resolver.DestroyCard(state, laterBeast);

            Assert.That(state.Players[0].Hand, Does.Contain(laterBeast));
            Assert.That(state.Players[0].Graveyard, Has.No.Member(laterBeast));
        }

        [Test]
        public void TwoHokiraActivations_StackAndBothTapAbilitiesCanBeUsed()
        {
            var state = EmptyState();
            var firstHokira = Instance(10, "dm09-18-55");
            var secondHokira = Instance(11, "dm09-18-55");
            var beast = Instance(20,
                Creature("beast", Civilization.Nature, "Beast Folk"));
            var dragon = Instance(21,
                Creature("dragon", Civilization.Fire, "Armored Dragon"));
            state.Players[0].BattleZone.Add(firstHokira);
            state.Players[0].BattleZone.Add(secondHokira);

            Assert.That(new RulesEngine(new FixedRaceDecisionProvider("Beast Folk"))
                .UseTapAbility(state, 0, firstHokira.InstanceId).Success, Is.True);
            Assert.That(new RulesEngine(new FixedRaceDecisionProvider("Armored Dragon"))
                .UseTapAbility(state, 0, secondHokira.InstanceId).Success, Is.True,
                "Tap abilities remain usable after the attack step has begun.");

            state.Players[0].BattleZone.Add(beast);
            state.Players[0].BattleZone.Add(dragon);
            var resolver = new AbilityResolver();
            resolver.DestroyCard(state, beast);
            resolver.DestroyCard(state, dragon);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { beast, dragon }));
            Assert.That(state.Players[0].Graveyard, Is.Empty);
        }

        [Test]
        public void Mihail_PreventsOtherCreaturesFromBeingDestroyedButNotItself()
        {
            var state = EmptyState();
            var mihail = Instance(10, "dm09-12-55");
            var other = Instance(20, Creature("other"));
            state.Players[0].BattleZone.Add(mihail);
            state.Players[1].BattleZone.Add(other);
            var resolver = new AbilityResolver();

            resolver.DestroyCard(state, other);
            Assert.That(state.Players[1].BattleZone, Does.Contain(other));

            resolver.DestroyCard(state, mihail);
            Assert.That(state.Players[0].Graveyard, Does.Contain(mihail));
        }

        [Test]
        public void StormWrangler_OnlyAllowsItsChosenBlockerAndRequiresItIfAble()
        {
            var state = EmptyState();
            var wrangler = Instance(10, "dm09-51-55");
            var chosen = Instance(20, Creature("chosen", Civilization.Light, "Guardian", 1000));
            var other = Instance(21, Creature("other", Civilization.Light, "Guardian", 1000));
            chosen.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            other.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            wrangler.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(wrangler);
            state.Players[1].BattleZone.Add(chosen);
            state.Players[1].BattleZone.Add(other);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, wrangler.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(state.RequiredBlockerInstanceId, Is.EqualTo(chosen.InstanceId));
            Assert.That(engine.Block(state, 1, other.InstanceId).Success, Is.False);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.False);
            Assert.That(engine.Block(state, 1, chosen.InstanceId).Success, Is.True);
        }

        [Test]
        public void KelpCandle_BlockAbilityTakesOneOfTopFourAndBottomsTheRest()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("attacker", Civilization.Fire, "Armorloid", 1000));
            var kelpCandle = Instance(20, "dm09-20-55");
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(kelpCandle);
            for (var i = 0; i < 4; i++)
                state.Players[1].Deck.Add(Instance(30 + i, Creature("deck-" + i)));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, kelpCandle.InstanceId).Success, Is.True);

            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(1));
            Assert.That(state.Players[1].Deck, Has.Count.EqualTo(3));
        }

        [Test]
        public void AquaMaster_TurnsASelectedShieldFaceUpBeforeBreakingShields()
        {
            var state = EmptyState();
            var aquaMaster = Instance(10, "dm09-s2-s5");
            aquaMaster.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(aquaMaster);
            for (var i = 0; i < 3; i++)
                state.Players[1].Shields.Add(Instance(20 + i, Creature("shield-" + i)));
            var engine = new RulesEngine(
                new FaceUpThenBreakDifferentShieldDecisions());

            Assert.That(engine.DeclareAttack(state, 0, aquaMaster.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Shields.Any(card => card.FaceUp), Is.True);
        }

        private sealed class FaceUpThenBreakDifferentShieldDecisions :
            IAbilityDecisionProvider
        {
            public bool ChooseOptional(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state,
                int playerId, CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum)
            {
                var ordered = ability.Operation == AbilityOperation.TurnFaceUp
                    ? candidates.OrderBy(card => card.InstanceId)
                    : candidates.OrderByDescending(card => card.InstanceId);
                return ordered.Take(Math.Min(maximum, candidates.Count)).ToArray();
            }
        }

        [Test]
        public void Petrova_CannotBeChosenByOpponentAndBuffsChosenRace()
        {
            var state = EmptyState();
            var petrova = Instance(10, "dm09-s1-s5");
            var beast = Instance(20, Creature("beast", Civilization.Nature, "Beast Folk", 2000));
            var victim = Instance(21, Creature("victim", Civilization.Light, "Guardian", 1000));
            state.Players[0].BattleZone.Add(petrova);
            state.Players[0].BattleZone.Add(beast);
            state.Players[0].BattleZone.Add(victim);
            var decisions = new FixedRaceDecisionProvider("Beast Folk");
            var resolver = new AbilityResolver(decisions);
            resolver.Resolve(state, petrova, AbilityTiming.OnSummon);

            Assert.That(resolver.CurrentPower(state, beast), Is.EqualTo(6000));

            var opposingEffect = Instance(30, Spell("opposing-effect", Civilization.Darkness));
            opposingEffect.Definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.destroy", AbilityTiming.OnCast, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            state.Players[1].Resolving.Add(opposingEffect);
            resolver.Resolve(state, opposingEffect, AbilityTiming.OnCast);

            Assert.That(state.Players[0].BattleZone, Does.Contain(petrova));
            Assert.That(state.Players[0].Graveyard, Does.Contain(beast));
        }

        [Test]
        public void OgristVhal_GainsDoubleAndTripleBreakerAtPowerThresholds()
        {
            var state = EmptyState();
            var ogrist = Instance(10, "dm09-s4-s5");
            state.Players[0].BattleZone.Add(ogrist);
            state.Players[0].Hand.Add(Instance(20, Creature("hand-one")));
            state.Players[0].Hand.Add(Instance(21, Creature("hand-two")));
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentPower(state, ogrist), Is.EqualTo(6000));
            Assert.That(resolver.HasKeyword(state, ogrist, CardKeyword.DoubleBreaker), Is.True);
            Assert.That(resolver.HasKeyword(state, ogrist, CardKeyword.TripleBreaker), Is.False);

            state.Players[0].Hand.Add(Instance(22, Creature("hand-three")));
            state.Players[0].Hand.Add(Instance(23, Creature("hand-four")));
            state.Players[0].Hand.Add(Instance(24, Creature("hand-five")));
            Assert.That(resolver.CurrentPower(state, ogrist), Is.EqualTo(15000));
            Assert.That(resolver.HasKeyword(state, ogrist, CardKeyword.TripleBreaker), Is.True);
        }

        [Test]
        public void BaleskBaj_QueuesAnExtraTurnAfterUnblockedPlayerAttack()
        {
            var state = EmptyState();
            var balesk = Instance(10, "dm09-4-55");
            balesk.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(balesk);
            state.Players[0].Deck.Add(Instance(20, Creature("draw")));
            state.Players[0].Deck.Add(Instance(21, Creature("deck-filler")));
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, balesk.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);
            Assert.That(engine.EndTurn(state, 0).Success, Is.True);

            Assert.That(state.ActivePlayerId, Is.EqualTo(0));
        }

        [Test]
        public void SlashCharger_ShufflesTheChosenDeckWhenNoCardIsTaken()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm09-32-55");
            state.Players[0].Resolving.Add(spell);
            var ownDeck = Enumerable.Range(0, 5)
                .Select(index => Instance(20 + index,
                    Creature("own-" + index))).ToArray();
            var opponentDeck = Enumerable.Range(0, 5)
                .Select(index => Instance(30 + index,
                    Creature("opponent-" + index))).ToArray();
            state.Players[0].Deck.AddRange(ownDeck);
            state.Players[1].Deck.AddRange(opponentDeck);

            new AbilityResolver(new ChooseOpponentButNoCardDecisions())
                .Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Deck, Is.EqualTo(ownDeck));
            Assert.That(state.Players[1].Deck, Is.Not.EqualTo(opponentDeck));
            Assert.That(state.Players[1].Graveyard, Is.Empty);
        }

        [Test]
        public void SimianWarriorGrash_TriggersWhenItselfIsDestroyed()
        {
            var state = EmptyState();
            var grash = Instance(10, "dm09-43-55");
            var opponentMana = Instance(20, Creature("opponent-mana"));
            state.Players[0].BattleZone.Add(grash);
            state.Players[1].Mana.Add(opponentMana);

            new AbilityResolver().DestroyCard(state, grash);

            Assert.That(state.Players[1].Graveyard,
                Does.Contain(opponentMana));
        }

        private sealed class ChooseOpponentButNoCardDecisions :
            IAbilityDecisionProvider, IAbilityPlayerDecisionProvider
        {
            public bool ChooseOptional(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state,
                int playerId, CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                Array.Empty<CardInstance>();

            public int ChoosePlayer(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<int> availablePlayerIds) => 1;
        }

        private sealed class FixedRaceDecisionProvider : IAbilityDecisionProvider, IAbilityRaceDecisionProvider
        {
            private readonly string _race;
            private readonly DeterministicAbilityDecisionProvider _cards =
                new DeterministicAbilityDecisionProvider();

            public FixedRaceDecisionProvider(string race) => _race = race;

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                _cards.ChooseCards(state, playerId, source, ability, candidates, minimum, maximum);

            public string ChooseRace(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability, IReadOnlyList<string> availableRaces) =>
                availableRaces.Any(race => race.Equals(_race, StringComparison.OrdinalIgnoreCase))
                    ? _race
                    : availableRaces.FirstOrDefault();
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

        private static CardDefinition Creature(string id,
            Civilization civilization = Civilization.Nature, string race = "Test Race", int power = 1000) =>
            new CardDefinition
            {
                Id = id,
                Name = id,
                Civilizations = new[] { civilization },
                Cost = 1,
                Type = CardType.Creature,
                Races = new[] { race },
                Power = power,
                ImplementationStatus = CardImplementationStatus.Complete
            };

        private static CardDefinition Spell(string id, Civilization civilization) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 0,
            Type = CardType.Spell,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
