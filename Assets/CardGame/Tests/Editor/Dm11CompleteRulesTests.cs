using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm11CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm11PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-11").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void WaveStrikerAbilities_RequireTwoOtherWaveStrikers()
        {
            var state = EmptyState();
            var asra = Instance(10, "dm11-6-55");
            state.Players[0].BattleZone.Add(asra);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentPower(state, asra), Is.EqualTo(2000));
            Assert.That(resolver.HasKeyword(state, asra, CardKeyword.Blocker), Is.False);

            state.Players[0].BattleZone.Add(Instance(20, WaveStriker("wave-one")));
            state.Players[1].BattleZone.Add(Instance(21, WaveStriker("wave-two")));

            Assert.That(resolver.CurrentPower(state, asra), Is.EqualTo(6000));
            Assert.That(resolver.HasKeyword(state, asra, CardKeyword.Blocker), Is.True);
        }

        [Test]
        public void MiraculousPlague_OpponentSplitsEachChosenPair()
        {
            var state = EmptyState();
            var plague = Instance(100, "dm11-s3-s5");
            var firstCreature = Instance(10, Creature("first-creature"));
            var secondCreature = Instance(11, Creature("second-creature"));
            var firstMana = Instance(20, Creature("first-mana"));
            var secondMana = Instance(21, Creature("second-mana"));
            state.Players[0].Resolving.Add(plague);
            state.Players[1].BattleZone.Add(firstCreature);
            state.Players[1].BattleZone.Add(secondCreature);
            state.Players[1].Mana.Add(firstMana);
            state.Players[1].Mana.Add(secondMana);

            new AbilityResolver().Resolve(state, plague, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Hand, Does.Contain(firstCreature));
            Assert.That(state.Players[1].Graveyard, Does.Contain(secondCreature));
            Assert.That(state.Players[1].Hand, Does.Contain(firstMana));
            Assert.That(state.Players[1].Graveyard, Does.Contain(secondMana));
        }

        [Test]
        public void MiraculousTruce_StopsChosenCivilizationIncludingLaterCreatures()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var truce = Instance(100, "dm11-5-55");
            state.Players[0].Resolving.Add(truce);
            var decisions = new FixedDecisions(Civilization.Fire);
            new AbilityResolver(decisions).Resolve(state, truce, AbilityTiming.OnCast);
            var fireAttacker = Instance(10, Creature("future-fire", Civilization.Fire));
            var waterAttacker = Instance(11, Creature("future-water", Civilization.Water));
            fireAttacker.EnteredBattleZoneOnTurn = 0;
            waterAttacker.EnteredBattleZoneOnTurn = 0;
            state.Players[1].BattleZone.Add(fireAttacker);
            state.Players[1].BattleZone.Add(waterAttacker);
            state.Players[0].Shields.Add(Instance(20, Creature("shield")));
            var engine = new RulesEngine(decisions);

            Assert.That(engine.DeclareAttack(state, 1, fireAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.False);
            Assert.That(engine.DeclareAttack(state, 1, waterAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
        }

        [Test]
        public void MultipleMiraculousTruceEffects_ProtectAgainstEveryChosenCivilization()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var firstTruce = Instance(100, "dm11-5-55");
            var secondTruce = Instance(101, "dm11-5-55");
            state.Players[0].Resolving.Add(firstTruce);
            state.Players[0].Resolving.Add(secondTruce);
            new AbilityResolver(new FixedDecisions(Civilization.Fire))
                .Resolve(state, firstTruce, AbilityTiming.OnCast);
            new AbilityResolver(new FixedDecisions(Civilization.Water))
                .Resolve(state, secondTruce, AbilityTiming.OnCast);

            var fireAttacker = Instance(10,
                Creature("fire-attacker", Civilization.Fire));
            var waterAttacker = Instance(11,
                Creature("water-attacker", Civilization.Water));
            var natureAttacker = Instance(12,
                Creature("nature-attacker", Civilization.Nature));
            fireAttacker.EnteredBattleZoneOnTurn = 0;
            waterAttacker.EnteredBattleZoneOnTurn = 0;
            natureAttacker.EnteredBattleZoneOnTurn = 0;
            state.Players[1].BattleZone.Add(fireAttacker);
            state.Players[1].BattleZone.Add(waterAttacker);
            state.Players[1].BattleZone.Add(natureAttacker);
            state.Players[0].Shields.Add(Instance(20, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 1, fireAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.False);
            Assert.That(engine.DeclareAttack(state, 1, waterAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.False);
            Assert.That(engine.DeclareAttack(state, 1, natureAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
        }

        [Test]
        public void WarpedLunatron_PreventsNormalUntapButTwoManaCanUntapOneCreature()
        {
            var locked = EmptyState();
            var firstLunatron = Instance(10, "dm11-23-55");
            var firstCreature = Instance(20, Creature("locked-creature"));
            firstCreature.Tapped = true;
            locked.Players[0].BattleZone.Add(firstLunatron);
            locked.Players[1].BattleZone.Add(firstCreature);
            locked.Players[1].Deck.Add(Instance(30, Creature("draw")));
            locked.Players[1].Deck.Add(Instance(31, Creature("deck-filler")));

            Assert.That(new RulesEngine().EndTurn(locked, 0).Success, Is.True);
            Assert.That(firstCreature.Tapped, Is.True);

            var paid = EmptyState();
            var secondLunatron = Instance(40, "dm11-23-55");
            var secondCreature = Instance(50, Creature("paid-creature"));
            secondCreature.Tapped = true;
            paid.Players[0].BattleZone.Add(secondLunatron);
            paid.Players[1].BattleZone.Add(secondCreature);
            paid.Players[1].Mana.Add(Instance(60, Creature("mana-one")));
            paid.Players[1].Mana.Add(Instance(61, Creature("mana-two")));
            paid.Players[1].Deck.Add(Instance(62, Creature("draw")));
            paid.Players[1].Deck.Add(Instance(63, Creature("deck-filler")));

            Assert.That(new RulesEngine().EndTurn(paid, 0).Success, Is.True);
            Assert.That(secondCreature.Tapped, Is.False);
            Assert.That(paid.Players[1].Mana.All(card => card.Tapped), Is.True);
        }

        [Test]
        public void EmergencyTyphoon_CanDrawOneThenMustDiscardOne()
        {
            var state = EmptyState();
            var typhoon = Instance(100, "dm11-16-55");
            state.Players[0].Resolving.Add(typhoon);
            state.Players[0].Hand.Add(Instance(10, Creature("existing")));
            state.Players[0].Deck.Add(Instance(20, Creature("draw-one")));
            state.Players[0].Deck.Add(Instance(21, Creature("draw-two")));

            new AbilityResolver(new FixedDecisions(number: 1))
                .Resolve(state, typhoon, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Deck, Has.Count.EqualTo(1));
            Assert.That(state.Players[0].Hand, Has.Count.EqualTo(1));
            Assert.That(state.Players[0].Graveyard, Has.Count.EqualTo(1));
        }

        [Test]
        public void RiseAndShine_TakesOnlyABlockerFromTheTopFour()
        {
            var state = EmptyState();
            var spell = Instance(100, "dm11-51-55");
            var blocker = Instance(20, Creature("blocker"));
            blocker.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            state.Players[0].Resolving.Add(spell);
            state.Players[0].Deck.Add(Instance(10, Creature("bottom")));
            state.Players[0].Deck.Add(Instance(21, Creature("top-one")));
            state.Players[0].Deck.Add(blocker);
            state.Players[0].Deck.Add(Instance(22, Creature("top-three")));
            state.Players[0].Deck.Add(Instance(23, Creature("top-four")));

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { blocker }));
            Assert.That(state.Players[0].Deck, Has.Count.EqualTo(4));
        }

        [Test]
        public void RoyalDurian_RemovesTheTopCardOfEveryEvolution()
        {
            var state = EmptyState();
            var durian = Instance(100, "dm11-49-55");
            var ownBase = Instance(10, Creature("own-base"));
            var opposingBase = Instance(20, Creature("opposing-base"));
            var ownEvolution = Instance(11, Evolution("own-evolution"));
            var opposingEvolution = Instance(21, Evolution("opposing-evolution"));
            ownEvolution.EvolutionMaterials.Add(ownBase);
            opposingEvolution.EvolutionMaterials.Add(opposingBase);
            state.Players[0].BattleZone.Add(durian);
            state.Players[0].BattleZone.Add(ownEvolution);
            state.Players[1].BattleZone.Add(opposingEvolution);

            new AbilityResolver().Resolve(state, durian, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Mana, Does.Contain(ownEvolution));
            Assert.That(state.Players[1].Mana, Does.Contain(opposingEvolution));
            Assert.That(state.Players[0].BattleZone, Does.Contain(ownBase));
            Assert.That(state.Players[1].BattleZone, Does.Contain(opposingBase));
        }

        [Test]
        public void RollickingTotem_CanPutAnEvolutionDragonFromManaOnAValidBase()
        {
            var state = EmptyState();
            var totem = Instance(100, "dm11-48-55");
            totem.Tapped = true;
            state.Players[0].BattleZone.Add(totem);

            var baseDefinition = Creature("fire-bird-base", Civilization.Fire,
                "Fire Bird");
            var evolutionBase = Instance(10, baseDefinition);
            state.Players[0].BattleZone.Add(evolutionBase);

            var evolutionDefinition = new CardDefinition
            {
                Id = "evolution-dragon",
                Name = "Evolution Dragon",
                Civilizations = new[] { Civilization.Fire },
                Cost = 6,
                Type = CardType.EvolutionCreature,
                Races = new[] { "Armored Dragon" },
                Power = 8000,
                EvolutionRace = "Fire Bird",
                EvolutionRaces = new[] { "Fire Bird" },
                EvolutionRequirements = new[]
                {
                    new EvolutionBaseRequirement { Races = new[] { "Fire Bird" } }
                },
                ImplementationStatus = CardImplementationStatus.Complete
            };
            var evolution = Instance(20, evolutionDefinition);
            state.Players[0].Mana.Add(evolution);

            var used = new AbilityResolver().ResolveOptionalTiming(
                state, totem, AbilityTiming.SilentSkill);

            Assert.That(used, Is.True);
            Assert.That(state.Players[0].Mana, Has.No.Member(evolution));
            Assert.That(state.Players[0].BattleZone, Does.Contain(evolution));
            Assert.That(state.Players[0].BattleZone, Has.No.Member(evolutionBase));
            Assert.That(evolution.EvolutionMaterials,
                Is.EquivalentTo(new[] { evolutionBase }));
        }

        [Test]
        public void MiraculousMeltdown_LeavesOneOpponentShieldPerOwnShieldAndOpensTriggers()
        {
            var state = EmptyState();
            var meltdown = Instance(100, "dm11-s4-s5");
            state.Players[0].Resolving.Add(meltdown);
            state.Players[0].Shields.Add(Instance(10, Creature("own-one")));
            state.Players[0].Shields.Add(Instance(11, Creature("own-two")));
            state.Players[1].Shields.Add(Instance(20, Creature("keep-one")));
            state.Players[1].Shields.Add(Instance(21, Creature("keep-two")));
            state.Players[1].Shields.Add(Instance(22, Creature("move-one")));
            state.Players[1].Shields.Add(Instance(23, Creature("move-two")));
            var trigger = Instance(24, Spell("trigger", Civilization.Light));
            trigger.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.ShieldTrigger) };
            state.Players[1].Shields.Add(trigger);

            new AbilityResolver().Resolve(state, meltdown, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Shields, Has.Count.EqualTo(2));
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(3));
            Assert.That(state.PendingShieldTriggers, Does.Contain(trigger));
        }

        [Test]
        public void MiraculousRebirth_RecruitsSameCostCreatureAfterDestruction()
        {
            var state = EmptyState();
            var rebirth = Instance(100, "dm11-s5-s5");
            var victim = Instance(10, Creature("victim", cost: 3, power: 4000));
            var recruit = Instance(20, Creature("recruit", cost: 3, power: 2000));
            var wrongCost = Instance(21, Creature("wrong-cost", cost: 4, power: 2000));
            state.Players[0].Resolving.Add(rebirth);
            state.Players[1].BattleZone.Add(victim);
            state.Players[0].Deck.Add(wrongCost);
            state.Players[0].Deck.Add(recruit);

            new AbilityResolver().Resolve(state, rebirth, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Graveyard, Does.Contain(victim));
            Assert.That(state.Players[0].BattleZone, Does.Contain(recruit));
            Assert.That(state.Players[0].Deck, Does.Contain(wrongCost));
            Assert.That(state.Players[1].BattleZone, Has.No.Member(recruit));
        }

        [Test]
        public void RouletteOfRuin_DiscardsChosenCostFromBothHands()
        {
            var state = EmptyState();
            var roulette = Instance(100, "dm11-30-55");
            state.Players[0].Resolving.Add(roulette);
            state.Players[0].Hand.Add(Instance(10, Creature("own-three", cost: 3)));
            state.Players[0].Hand.Add(Instance(11, Creature("own-four", cost: 4)));
            state.Players[1].Hand.Add(Instance(20, Creature("opposing-three", cost: 3)));

            new AbilityResolver(new FixedDecisions(number: 3))
                .Resolve(state, roulette, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand.Select(card => card.Definition.Cost),
                Is.EquivalentTo(new[] { 4 }));
            Assert.That(state.Players[1].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Has.Count.EqualTo(1));
            Assert.That(state.Players[1].Graveyard, Has.Count.EqualTo(1));
            Assert.That(state.InspectionPlayerId, Is.EqualTo(0));
            Assert.That(state.InspectedCards.Select(card => card.InstanceId),
                Is.EquivalentTo(new long[] { 10, 11, 20 }));
        }

        [Test]
        public void GazerEyes_UsesItsSilentSkillTimingToInspectAndDiscard()
        {
            var state = EmptyState();
            var gazer = Instance(10, "dm11-26-55");
            var first = Instance(20, Creature("first"));
            var second = Instance(21, Creature("second"));
            gazer.Tapped = true;
            state.Players[0].BattleZone.Add(gazer);
            state.Players[1].Hand.Add(first);
            state.Players[1].Hand.Add(second);

            var used = new AbilityResolver().ResolveOptionalTiming(
                state, gazer, AbilityTiming.SilentSkill);

            Assert.That(used, Is.True);
            Assert.That(state.InspectedCards, Is.EquivalentTo(new[] { first, second }));
            Assert.That(state.Players[1].Graveyard, Has.Count.EqualTo(1));
            Assert.That(gazer.Definition.RuleAbilities.Where(rule =>
                    rule.RulesText.StartsWith("Look at your opponent's hand"))
                .All(rule => rule.Timing == AbilityTiming.SilentSkill), Is.True);
        }

        private sealed class FixedDecisions : IAbilityDecisionProvider,
            IAbilityCivilizationDecisionProvider, IAbilityNumberDecisionProvider
        {
            private readonly Civilization _civilization;
            private readonly int? _number;
            private readonly DeterministicAbilityDecisionProvider _cards =
                new DeterministicAbilityDecisionProvider();

            public FixedDecisions(Civilization civilization = Civilization.Light,
                int? number = null)
            {
                _civilization = civilization;
                _number = number;
            }

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                _cards.ChooseCards(state, playerId, source, ability, candidates, minimum, maximum);

            public Civilization ChooseCivilization(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<Civilization> availableCivilizations) => _civilization;

            public int ChooseNumber(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability, int minimum, int maximum, int suggested) =>
                _number ?? suggested;
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

        private static CardDefinition WaveStriker(string id)
        {
            var card = Creature(id);
            card.Abilities = new[] { new KeywordAbility(CardKeyword.WaveStriker) };
            return card;
        }

        private static CardDefinition Creature(string id,
            Civilization civilization = Civilization.Nature,
            string race = "Test Race", int cost = 1, int power = 1000) =>
            new CardDefinition
            {
                Id = id,
                Name = id,
                Civilizations = new[] { civilization },
                Cost = cost,
                Type = CardType.Creature,
                Races = new[] { race },
                Power = power,
                ImplementationStatus = CardImplementationStatus.Complete
            };

        private static CardDefinition Evolution(string id) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { Civilization.Nature },
            Cost = 3,
            Type = CardType.EvolutionCreature,
            Races = new[] { "Test Race" },
            Power = 5000,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardDefinition Spell(string id, Civilization civilization) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 1,
            Type = CardType.Spell,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
