using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm12CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm12PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-12").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card =>
                    card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void Jagraveen_DestroysAfterActualBlockBattleButNotPreventedBattle()
        {
            var state = EmptyState();
            var noBattleAttacker = Instance(10, "dm06-74-110");
            var normalAttacker = Instance(11, Creature("normal-attacker", power: 1000));
            var jagraveen = Instance(20, "dm12-3-55");
            noBattleAttacker.EnteredBattleZoneOnTurn = 0;
            normalAttacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(noBattleAttacker);
            state.Players[0].BattleZone.Add(normalAttacker);
            state.Players[1].BattleZone.Add(jagraveen);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, noBattleAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, jagraveen.InstanceId).Success, Is.True);
            Assert.That(state.Players[1].BattleZone, Does.Contain(jagraveen),
                "No battle occurred, so 'after it battles' must not destroy Jagraveen.");

            jagraveen.Tapped = false;
            Assert.That(engine.DeclareAttack(state, 0, normalAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, jagraveen.InstanceId).Success, Is.True);

            Assert.That(state.Players[1].Graveyard, Does.Contain(jagraveen));
        }

        [Test]
        public void Gigavrand_DiscardsOpponentsHandAfterTwoDrawsThatTurn()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var gigavrand = Instance(10, "dm12-10-55");
            state.Players[0].BattleZone.Add(gigavrand);
            state.Players[0].Deck.Add(Instance(20, Creature("next-draw")));
            state.Players[1].Hand.Add(Instance(30, Creature("first-card")));
            state.Players[1].Hand.Add(Instance(31, Creature("second-card")));
            state.CardsDrawnThisTurn[1] = 2;

            Assert.That(new RulesEngine().EndTurn(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Hand, Is.Empty);
            Assert.That(state.Players[1].Graveyard, Has.Count.EqualTo(2));
        }

        [Test]
        public void ClonedBlade_DestroysOnePlusEachCopyInBothGraveyards()
        {
            var state = EmptyState();
            var spell = Instance(100, "dm12-28-55");
            state.Players[0].Resolving.Add(spell);
            state.Players[0].Graveyard.Add(Instance(101, "dm12-28-55"));
            state.Players[1].Graveyard.Add(Instance(102, "dm12-28-55"));
            for (var index = 0; index < 4; index++)
                state.Players[1].BattleZone.Add(Instance(10 + index,
                    Creature("target-" + index, 3000)));

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[1].BattleZone, Has.Count.EqualTo(1));
            Assert.That(state.Players[1].Graveyard.Count(card =>
                card.Definition.Name.StartsWith("target-")), Is.EqualTo(3));
        }

        [Test]
        public void SoulPhoenix_SeparatesItsTwoEvolutionBasesIntoTwoCreatures()
        {
            var state = EmptyState();
            var soulPhoenix = Instance(100, "dm12-5-55");
            var fireBird = Instance(10, Creature("fire-bird", race: "Fire Bird"));
            var earthDragon = Instance(11, Creature("earth-dragon", race: "Earth Dragon"));
            soulPhoenix.EvolutionMaterials.Add(fireBird);
            soulPhoenix.EvolutionMaterials.Add(earthDragon);
            state.Players[0].BattleZone.Add(soulPhoenix);

            var destroyed = new AbilityResolver().DestroyCard(state, soulPhoenix);

            Assert.That(destroyed, Is.True,
                "The state-defined separation does not replace destruction.");
            Assert.That(state.Players[0].Graveyard, Does.Contain(soulPhoenix));
            Assert.That(state.Players[0].BattleZone,
                Is.EquivalentTo(new[] { fireBird, earthDragon }));
            Assert.That(fireBird.EvolutionMaterials, Is.Empty);
            Assert.That(earthDragon.EvolutionMaterials, Is.Empty);
        }

        [Test]
        public void CruelNaga_LeavingBattleZoneDestroysEveryOtherCreature()
        {
            var state = EmptyState();
            var naga = Instance(100, "dm12-s3-s5");
            state.Players[0].BattleZone.Add(naga);
            state.Players[0].BattleZone.Add(Instance(10, Creature("own-creature")));
            state.Players[1].BattleZone.Add(Instance(20, Creature("opposing-creature")));

            new AbilityResolver().DestroyCard(state, naga);

            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[1].BattleZone, Is.Empty);
            Assert.That(state.Players.SelectMany(player => player.Graveyard).Count(),
                Is.EqualTo(3));
        }

        [Test]
        public void FeverNuts_AllowsEitherReducedOrPrintedCreatureCost()
        {
            var state = EmptyState();
            var feverNuts = Instance(10, "dm12-54-55");
            var ownCreature = Instance(20, Creature("own-five", cost: 5));
            var opposingCreature = Instance(21, Creature("opposing-five", cost: 5));
            state.Players[0].BattleZone.Add(feverNuts);
            state.Players[0].Hand.Add(ownCreature);
            state.Players[1].Hand.Add(opposingCreature);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentCost(state, ownCreature), Is.EqualTo(4));
            Assert.That(resolver.MaximumPayableCost(state, ownCreature), Is.EqualTo(5));
            Assert.That(resolver.CurrentCost(state, opposingCreature), Is.EqualTo(4));
            Assert.That(resolver.MaximumPayableCost(state, opposingCreature), Is.EqualTo(5));
        }

        [Test]
        public void CosmicDarts_CastsTheOpponentChosenShieldSpellForFree()
        {
            var state = EmptyState();
            var darts = Instance(100, "dm12-7-55");
            var shieldSpell = Instance(10, Spell("shield-spell"));
            shieldSpell.Definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw", AbilityTiming.OnCast,
                    AbilityOperation.Draw) { Amount = 1 }
            };
            var drawn = Instance(20, Creature("drawn"));
            state.Players[0].Resolving.Add(darts);
            state.Players[0].Shields.Add(shieldSpell);
            state.Players[0].Deck.Add(drawn);

            new AbilityResolver().Resolve(state, darts, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Shields, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(shieldSpell));
            Assert.That(state.Players[0].Hand, Does.Contain(drawn));
            Assert.That(state.SpellCastThisTurn[0], Is.True);
        }

        [Test]
        public void Kilstine_GrantsItsWaveStrikerPowerBlockerAndDoubleBreaker()
        {
            var state = EmptyState();
            var kilstine = Instance(10, "dm12-1-55");
            var ally = Instance(11, Creature("ally", 2000));
            state.Players[0].BattleZone.Add(kilstine);
            state.Players[0].BattleZone.Add(ally);
            state.Players[0].BattleZone.Add(Instance(12, WaveStriker("wave-one")));
            state.Players[1].BattleZone.Add(Instance(13, WaveStriker("wave-two")));
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentPower(state, ally), Is.EqualTo(7000));
            Assert.That(resolver.HasKeyword(state, ally, CardKeyword.Blocker), Is.True);
            Assert.That(resolver.HasKeyword(state, ally, CardKeyword.DoubleBreaker), Is.True);
        }

        [Test]
        public void CopperLocust_IsDestroyedWhenAnyPlayerEvolvesAnotherCreature()
        {
            var state = EmptyState();
            var locust = Instance(10, "dm12-52-55");
            var evolution = Instance(20, Evolution("evolution"));
            state.Players[0].BattleZone.Add(locust);
            state.Players[1].BattleZone.Add(evolution);

            new AbilityResolver().ResolveGlobalEvent(state, evolution,
                AbilityTiming.OnAnyCreatureEvolved, true);

            Assert.That(state.Players[0].BattleZone, Has.No.Member(locust));
            Assert.That(state.Players[0].Graveyard, Does.Contain(locust));
        }

        [Test]
        public void Ularus_TurnsUpToOneShieldFaceUpPerOwnCreature()
        {
            var state = EmptyState();
            var ularus = Instance(10, "dm12-6-55");
            state.Players[0].BattleZone.Add(ularus);
            state.Players[0].BattleZone.Add(Instance(11, Creature("ally")));
            state.Players[0].Shields.Add(Instance(20, Creature("shield-one")));
            state.Players[0].Shields.Add(Instance(21, Creature("shield-two")));
            state.Players[1].Shields.Add(Instance(22, Creature("shield-three")));

            new AbilityResolver().Resolve(state, ularus, AbilityTiming.OnSummon);

            Assert.That(state.Players.SelectMany(player => player.Shields)
                .Count(card => card.FaceUp), Is.EqualTo(2));
        }

        [Test]
        public void MechadragonsBreath_DestroysEveryCreatureAtTheChosenPower()
        {
            var state = EmptyState();
            var spell = Instance(100, "dm12-13-55");
            var ownTarget = Instance(10, Creature("own-3000", 3000));
            var opposingTarget = Instance(20, Creature("opposing-3000", 3000));
            var survivor = Instance(21, Creature("opposing-4000", 4000));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(ownTarget);
            state.Players[1].BattleZone.Add(opposingTarget);
            state.Players[1].BattleZone.Add(survivor);

            new AbilityResolver(new FixedDecisions(3000))
                .Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Graveyard, Does.Contain(ownTarget));
            Assert.That(state.Players[1].Graveyard, Does.Contain(opposingTarget));
            Assert.That(state.Players[1].BattleZone, Is.EquivalentTo(new[] { survivor }));
        }

        [Test]
        public void WiseStarnoid_AddsATopDeckShieldWhenItAttacks()
        {
            var state = EmptyState();
            var starnoid = Instance(10, "dm12-s2-s5");
            var topCard = Instance(20, Creature("top-card"));
            state.Players[0].BattleZone.Add(starnoid);
            state.Players[0].Deck.Add(topCard);

            new AbilityResolver().Resolve(state, starnoid, AbilityTiming.OnAttack);

            Assert.That(state.Players[0].Shields, Is.EquivalentTo(new[] { topCard }));
        }

        [Test]
        public void AuraPegasus_RevealsNonEvolutionCreatureIntoBattleZone()
        {
            var state = EmptyState();
            var pegasus = Instance(10, "dm12-s5-s5");
            var topCreature = Instance(20, Creature("top-creature"));
            state.Players[0].BattleZone.Add(pegasus);
            state.Players[0].Deck.Add(topCreature);

            new AbilityResolver().Resolve(state, pegasus, AbilityTiming.OnAttack);

            Assert.That(state.Players[0].BattleZone, Does.Contain(topCreature));
            Assert.That(state.Players[0].Hand, Has.No.Member(topCreature));
        }

        [Test]
        public void WingeyeMoth_DrawsOnlyWhenAnOwnCreatureExceedsEveryOpponent()
        {
            var state = EmptyState();
            var moth = Instance(10, "dm12-29-55");
            state.Players[0].BattleZone.Add(moth);
            state.Players[0].BattleZone.Add(Instance(11, Creature("strong", 5000)));
            state.Players[1].BattleZone.Add(Instance(20, Creature("weak", 4000)));
            var bonus = Instance(30, Creature("bonus"));
            state.Players[0].Deck.Add(bonus);

            new AbilityResolver().Resolve(state, moth, AbilityTiming.AfterStartTurnDraw);

            Assert.That(state.Players[0].Hand, Does.Contain(bonus));
        }

        [Test]
        public void Nemonex_BurnsManaWhenAnAlliedMatchingRaceIsUnblocked()
        {
            var state = EmptyState();
            var nemonex = Instance(10, "dm12-19-55");
            var attacker = Instance(11, Creature("xenoparts-attacker", race: "Xenoparts"));
            var mana = Instance(20, Creature("opposing-mana"));
            state.Players[0].BattleZone.Add(nemonex);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].Mana.Add(mana);

            new AbilityResolver().ResolveGlobalEvent(state, attacker,
                AbilityTiming.OnAnyCreatureAttackPlayerUnblocked, true);

            Assert.That(state.Players[1].Mana, Is.Empty);
            Assert.That(state.Players[1].Graveyard, Does.Contain(mana));
        }

        [Test]
        public void WhirlingWarriorMalian_TapsWhenAnotherCreatureEnters()
        {
            var state = EmptyState();
            var malian = Instance(10, "dm12-12-55");
            var arrivingCreature = Instance(20, Creature("arriving"));
            state.Players[0].BattleZone.Add(malian);
            state.Players[1].BattleZone.Add(arrivingCreature);

            new AbilityResolver().ResolveGlobalEvent(state, arrivingCreature,
                AbilityTiming.OnOtherCreatureSummoned);

            Assert.That(malian.Tapped, Is.True);
        }

        [Test]
        public void Agira_DrawsWhenAnAlliedGladiatorBlocks()
        {
            var state = EmptyState();
            var agira = Instance(10, "dm12-16-55");
            var blocker = Instance(11, Creature("blocker", race: "Gladiator"));
            var draw = Instance(20, Creature("draw"));
            state.Players[0].BattleZone.Add(agira);
            state.Players[0].BattleZone.Add(blocker);
            state.Players[0].Deck.Add(draw);

            new AbilityResolver().ResolveGlobalEvent(state, blocker,
                AbilityTiming.OnAnyCreatureBlock, true);

            Assert.That(state.Players[0].Hand, Does.Contain(draw));
        }

        [Test]
        public void TropicCrawler_ReturnsAnOpponentChosenCreatureWhenItBlocks()
        {
            var state = EmptyState();
            var crawler = Instance(10, "dm12-40-55");
            var first = Instance(20, Creature("first"));
            var second = Instance(21, Creature("second"));
            state.Players[0].BattleZone.Add(crawler);
            state.Players[1].BattleZone.Add(first);
            state.Players[1].BattleZone.Add(second);

            new AbilityResolver().Resolve(state, crawler, AbilityTiming.OnBlock);

            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { first }));
            Assert.That(state.Players[1].BattleZone, Is.EquivalentTo(new[] { second }));
        }

        [Test]
        public void TurtleHorn_SearchesCreatureAfterOpponentShieldTrigger()
        {
            var state = EmptyState();
            var turtle = Instance(10, "dm12-53-55");
            var eventSpell = Instance(20, Spell("opposing-trigger"));
            var creature = Instance(30, Creature("search-target"));
            var spell = Instance(31, Spell("not-a-creature"));
            state.Players[0].BattleZone.Add(turtle);
            state.Players[1].Resolving.Add(eventSpell);
            state.Players[0].Deck.Add(spell);
            state.Players[0].Deck.Add(creature);

            new AbilityResolver().ResolveGlobalEvent(state, eventSpell,
                AbilityTiming.OnShieldTriggerCastByOpponent, true);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { creature }));
            Assert.That(state.Players[0].Deck, Is.EquivalentTo(new[] { spell }));
            Assert.That(state.InspectionPlayerId, Is.EqualTo(1));
            Assert.That(state.InspectedCards, Is.EquivalentTo(new[] { creature }));
        }

        private sealed class FixedDecisions : IAbilityDecisionProvider,
            IAbilityNumberDecisionProvider
        {
            private readonly int _number;
            private readonly DeterministicAbilityDecisionProvider _cards =
                new DeterministicAbilityDecisionProvider();

            public FixedDecisions(int number) => _number = number;

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                _cards.ChooseCards(state, playerId, source, ability,
                    candidates, minimum, maximum);

            public int ChooseNumber(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability, int minimum, int maximum, int suggested) => _number;
        }

        private static GameState EmptyState() => new GameState
        {
            Players = new[]
            {
                new PlayerState { PlayerId = 0 },
                new PlayerState { PlayerId = 1 }
            },
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

        private static CardDefinition Creature(string id, int power = 1000,
            int cost = 1, string race = "Test Race") => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { Civilization.Nature },
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

        private static CardDefinition Spell(string id) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { Civilization.Light },
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
