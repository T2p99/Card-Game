using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm08CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm08PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-08").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void BailasGale_ReturnsCastShieldTriggerSpellToHand()
        {
            var state = EmptyState();
            var bailas = Instance(10, "dm08-s5-s5");
            var trigger = Instance(20, Spell("shield-trigger", Civilization.Nature));
            trigger.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.ShieldTrigger) };
            state.Players[1].BattleZone.Add(bailas);
            state.Players[1].Hand.Add(trigger);
            state.PendingShieldTriggers.Add(trigger);

            var result = new RulesEngine().CastShieldTrigger(state, 1, trigger.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].Hand, Does.Contain(trigger));
            Assert.That(state.Players[1].Graveyard, Does.Not.Contain(trigger));
        }

        [Test]
        public void ChargerShieldTrigger_UsesItsOwnManaReplacementBeforeBailasGale()
        {
            var state = EmptyState();
            var bailas = Instance(10, "dm08-s5-s5");
            var trigger = Instance(20, Spell("charger-trigger", Civilization.Nature));
            trigger.Definition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.ShieldTrigger),
                new KeywordAbility(CardKeyword.Charger)
            };
            state.Players[1].BattleZone.Add(bailas);
            state.Players[1].Hand.Add(trigger);
            state.PendingShieldTriggers.Add(trigger);

            var result = new RulesEngine().CastShieldTrigger(state, 1,
                trigger.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].Mana, Does.Contain(trigger));
            Assert.That(state.Players[1].Hand, Does.Not.Contain(trigger));
            Assert.That(state.Players[1].Graveyard, Does.Not.Contain(trigger));
        }

        [Test]
        public void Nariel_StopsThreeThousandPowerButNotLowerPrintedPowerAttacker()
        {
            var highState = EmptyState();
            var nariel = Instance(20, "dm08-11-55");
            var high = Instance(10, Creature("high", Civilization.Fire, "Armorloid", 3000));
            high.EnteredBattleZoneOnTurn = 0;
            highState.Players[0].BattleZone.Add(high);
            highState.Players[1].BattleZone.Add(nariel);

            Assert.That(new RulesEngine().DeclareAttack(highState, 0, high.InstanceId,
                AttackTargetKind.Player).Success, Is.False);

            var lowState = EmptyState();
            var secondNariel = Instance(40, "dm08-11-55");
            var low = Instance(30, Creature("low", Civilization.Fire, "Armorloid", 2000));
            low.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.PowerAttacker, 4000) };
            low.EnteredBattleZoneOnTurn = 0;
            lowState.Players[0].BattleZone.Add(low);
            lowState.Players[1].BattleZone.Add(secondNariel);

            Assert.That(new RulesEngine().DeclareAttack(lowState, 0, low.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
        }

        [Test]
        public void Jagalzor_TurboRushActivatesOnlyAfterAnotherCreatureBreaksAShield()
        {
            var state = EmptyState();
            var jagalzor = Instance(10, "dm08-4-55");
            var ally = Instance(11, Creature("ally", Civilization.Fire));
            var newcomer = Instance(12, Creature("newcomer", Civilization.Nature));
            state.Players[0].BattleZone.Add(jagalzor);
            state.Players[0].BattleZone.Add(ally);
            state.Players[0].BattleZone.Add(newcomer);
            var resolver = new AbilityResolver();

            Assert.That(resolver.HasKeyword(state, newcomer, CardKeyword.SpeedAttacker), Is.False);
            ally.BrokeShieldThisTurn = true;
            Assert.That(resolver.HasKeyword(state, newcomer, CardKeyword.SpeedAttacker), Is.True);
        }

        [Test]
        public void IllusionFish_UnblockableKeywordAlsoRequiresTurboRush()
        {
            var state = EmptyState();
            var fish = Instance(10, "dm08-20-55");
            var ally = Instance(11, Creature("ally"));
            state.Players[0].BattleZone.Add(fish);
            state.Players[0].BattleZone.Add(ally);
            var resolver = new AbilityResolver();

            Assert.That(resolver.HasKeyword(state, fish, CardKeyword.Unblockable), Is.False);

            ally.BrokeShieldThisTurn = true;

            Assert.That(resolver.HasKeyword(state, fish, CardKeyword.Unblockable), Is.True);
        }

        [Test]
        public void Kachua_SummonsDragonWithSpeedAttackerThenDestroysItAtEndOfTurn()
        {
            var state = EmptyState();
            var kachua = Instance(10, "dm08-5-55");
            var dragon = Instance(20, Creature("dragon", Civilization.Fire, "Armored Dragon", 5000));
            state.Players[0].BattleZone.Add(kachua);
            state.Players[0].Deck.Add(dragon);
            state.Players[1].Deck.Add(Instance(30, Creature("opponent-draw")));
            var engine = new RulesEngine();

            Assert.That(engine.UseTapAbility(state, 0, kachua.InstanceId).Success, Is.True);
            Assert.That(state.Players[0].BattleZone, Does.Contain(dragon));
            Assert.That(new AbilityResolver().HasKeyword(state, dragon, CardKeyword.SpeedAttacker), Is.True);

            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.Players[0].Graveyard, Does.Contain(dragon));
        }

        [Test]
        public void FuriousOnslaught_AddsArmoredDragonRacePowerAndDoubleBreakerTemporarily()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm08-37-55");
            var dragonoid = Instance(20, Creature("dragonoid", Civilization.Fire, "Dragonoid", 2000));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(dragonoid);
            var resolver = new AbilityResolver();

            resolver.Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(resolver.HasRace(dragonoid, "Armored Dragon"), Is.True);
            Assert.That(resolver.CurrentPower(state, dragonoid), Is.EqualTo(6000));
            Assert.That(resolver.HasKeyword(state, dragonoid, CardKeyword.DoubleBreaker), Is.True);
        }

        [Test]
        public void SolGalla_GainsPowerAfterEitherPlayerCastsASpell()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var solGalla = Instance(10, "dm08-13-55");
            var spell = Instance(20, Spell("opponent-spell", Civilization.Water));
            state.Players[0].BattleZone.Add(solGalla);
            state.Players[1].Hand.Add(spell);

            Assert.That(new RulesEngine().CastSpell(state, 1, spell.InstanceId,
                new long[0]).Success, Is.True);

            Assert.That(new AbilityResolver().CurrentPower(state, solGalla), Is.EqualTo(4000));
        }

        [Test]
        public void ScreamSlicer_DestroysLowestPowerAfterAlliedDragonSummon()
        {
            var state = EmptyState();
            var slicer = Instance(10, "dm08-34-55");
            var dragon = Instance(20, Creature("dragon", Civilization.Fire, "Armored Dragon", 5000));
            var lowest = Instance(30, Creature("lowest", Civilization.Light, "Guardian", 1000));
            state.Players[0].BattleZone.Add(slicer);
            state.Players[0].BattleZone.Add(dragon);
            state.Players[1].BattleZone.Add(lowest);

            new AbilityResolver().ResolveGlobalEvent(state, dragon,
                AbilityTiming.OnOtherCreatureSummoned);

            Assert.That(state.Players[1].Graveyard, Does.Contain(lowest));
        }

        [Test]
        public void Sasha_GetsBattlePowerAgainstDragon()
        {
            var state = EmptyState();
            var dragon = Instance(10, Creature("dragon", Civilization.Fire, "Armored Dragon", 12000));
            var sasha = Instance(20, "dm08-12-55");
            dragon.EnteredBattleZoneOnTurn = 0;
            sasha.Tapped = true;
            state.Players[0].BattleZone.Add(dragon);
            state.Players[1].BattleZone.Add(sasha);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, dragon.InstanceId,
                AttackTargetKind.Creature, sasha.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Graveyard, Does.Contain(dragon));
            Assert.That(state.Players[1].BattleZone, Does.Contain(sasha));
        }

        [Test]
        public void DracodanceTotem_ReplacesDestructionAndReturnsADragonFromMana()
        {
            var state = EmptyState();
            var totem = Instance(10, "dm08-49-55");
            var dragon = Instance(20, Creature("dragon", Civilization.Fire, "Armored Dragon", 5000));
            state.Players[0].BattleZone.Add(totem);
            state.Players[0].Mana.Add(dragon);

            new AbilityResolver().DestroyCard(state, totem);

            Assert.That(state.Players[0].Mana, Does.Contain(totem));
            Assert.That(state.Players[0].Hand, Does.Contain(dragon));
            Assert.That(state.Players[0].Graveyard, Is.Empty);
        }

        [Test]
        public void LaserWhip_CanGrantUnblockableWhenNoOpponentCreatureCanBeTapped()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm08-7-55");
            var ally = Instance(20, Creature("ally"));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(ally);

            var resolver = new AbilityResolver();
            resolver.Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(resolver.HasKeyword(state, ally,
                CardKeyword.Unblockable), Is.True);
        }

        [Test]
        public void Lalicious_UsesOneOptionalChoiceToInspectHandAndTopDeckTogether()
        {
            var state = EmptyState();
            var lalicious = Instance(10, "dm08-21-55");
            var handCard = Instance(20, Creature("hand-card"));
            var lowerDeckCard = Instance(21, Creature("lower-deck-card"));
            var topDeckCard = Instance(22, Creature("top-deck-card"));
            state.Players[0].BattleZone.Add(lalicious);
            state.Players[1].Hand.Add(handCard);
            state.Players[1].Deck.Add(lowerDeckCard);
            state.Players[1].Deck.Add(topDeckCard);
            var decisions = new CountingOptionalDecisions();

            new AbilityResolver(decisions).Resolve(state, lalicious,
                AbilityTiming.OnAttack);

            Assert.That(decisions.OptionalChoices, Is.EqualTo(1));
            Assert.That(state.InspectionPlayerId, Is.EqualTo(0));
            Assert.That(state.InspectedCards,
                Is.EquivalentTo(new[] { handCard, topDeckCard }));
        }

        [Test]
        public void LunarCharger_AllowsTheEndStepUntapToBeDeclined()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm08-8-55");
            var first = Instance(20, Creature("first"));
            var second = Instance(21, Creature("second"));
            first.Tapped = true;
            second.Tapped = true;
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(first);
            state.Players[0].BattleZone.Add(second);
            var decisions = new DeclineOptionalDecisions();
            var resolver = new AbilityResolver(decisions);

            resolver.Resolve(state, spell, AbilityTiming.OnCast);
            resolver.ResolveEndStep(state, 0);

            Assert.That(first.Tapped, Is.True);
            Assert.That(second.Tapped, Is.True);
            Assert.That(decisions.OptionalChoices, Is.EqualTo(1));
        }

        private sealed class CountingOptionalDecisions : IAbilityDecisionProvider
        {
            private readonly DeterministicAbilityDecisionProvider _cards =
                new DeterministicAbilityDecisionProvider();

            public int OptionalChoices { get; private set; }

            public bool ChooseOptional(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability)
            {
                OptionalChoices++;
                return true;
            }

            public IReadOnlyList<CardInstance> ChooseCards(GameState state,
                int playerId, CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                _cards.ChooseCards(state, playerId, source, ability,
                    candidates, minimum, maximum);
        }

        private sealed class DeclineOptionalDecisions : IAbilityDecisionProvider
        {
            public int OptionalChoices { get; private set; }

            public bool ChooseOptional(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability)
            {
                OptionalChoices++;
                return false;
            }

            public IReadOnlyList<CardInstance> ChooseCards(GameState state,
                int playerId, CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.Take(maximum).ToArray();
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
