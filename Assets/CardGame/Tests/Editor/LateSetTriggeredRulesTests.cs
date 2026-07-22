using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class LateSetTriggeredRulesTests
    {
        [Test]
        public void SlashAndBurn_WatchesOpponentDestructionForManaAndShieldBurn()
        {
            var state = EmptyState();
            var spell = Instance(100, "dm11-54-55");
            var victim = Instance(20, Creature("victim"));
            var mana = Instance(21, Creature("mana"));
            var shield = Instance(22, Creature("shield"));
            state.Players[0].Resolving.Add(spell);
            state.Players[1].BattleZone.Add(victim);
            state.Players[1].Mana.Add(mana);
            state.Players[1].Shields.Add(shield);
            var resolver = new AbilityResolver();

            resolver.Resolve(state, spell, AbilityTiming.OnCast);
            resolver.DestroyCard(state, victim);

            Assert.That(state.Players[1].Mana, Is.Empty);
            Assert.That(state.Players[1].Shields, Is.Empty);
            Assert.That(state.Players[1].Graveyard,
                Is.EquivalentTo(new[] { victim, mana, shield }));
        }

        [Test]
        public void Lamiel_DrawsWhenOwnCreatureDiesDuringOpponentsTurn()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var lamiel = Instance(10, "dm11-10-55");
            var victim = Instance(11, Creature("victim"));
            var draw = Instance(12, Creature("draw"));
            state.Players[0].BattleZone.Add(lamiel);
            state.Players[0].BattleZone.Add(victim);
            state.Players[0].Deck.Add(draw);

            new AbilityResolver().DestroyCard(state, victim);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { draw }));
        }

        [Test]
        public void QuillspikeRumbler_GainsPowerOnlyWhenAttackingACreature()
        {
            var creatureAttack = EmptyState();
            var firstRumbler = Instance(10, "dm11-46-55");
            creatureAttack.Players[0].BattleZone.Add(firstRumbler);
            creatureAttack.PendingAttack = new PendingAttack
            {
                AttackerInstanceId = firstRumbler.InstanceId,
                TargetKind = AttackTargetKind.Creature,
                TargetCreatureInstanceId = 20,
                DefendingPlayerId = 1
            };
            var resolver = new AbilityResolver();
            var printedPower = resolver.CurrentPower(creatureAttack, firstRumbler);

            resolver.Resolve(creatureAttack, firstRumbler, AbilityTiming.OnAttack);

            Assert.That(resolver.CurrentPower(creatureAttack, firstRumbler),
                Is.EqualTo(printedPower + 3000));

            var playerAttack = EmptyState();
            var secondRumbler = Instance(30, "dm11-46-55");
            playerAttack.Players[0].BattleZone.Add(secondRumbler);
            playerAttack.PendingAttack = new PendingAttack
            {
                AttackerInstanceId = secondRumbler.InstanceId,
                TargetKind = AttackTargetKind.Player,
                DefendingPlayerId = 1
            };
            resolver.Resolve(playerAttack, secondRumbler, AbilityTiming.OnAttack);

            Assert.That(resolver.CurrentPower(playerAttack, secondRumbler),
                Is.EqualTo(secondRumbler.Definition.Power));
        }

        [Test]
        public void LiveAndBreathe_RecruitsOneSameNameCreatureForEachSummon()
        {
            var state = EmptyState();
            var spell = Instance(100, "dm11-52-55");
            var summoned = Instance(10, Creature("matching-name"));
            var matchingCopy = Instance(11, Creature("matching-name"));
            var different = Instance(12, Creature("different"));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(summoned);
            state.Players[0].Deck.Add(different);
            state.Players[0].Deck.Add(matchingCopy);
            var resolver = new AbilityResolver();

            resolver.Resolve(state, spell, AbilityTiming.OnCast);
            resolver.ResolveGlobalEvent(state, summoned,
                AbilityTiming.OnAnyCreatureSummonedByPlay, true);

            Assert.That(state.Players[0].BattleZone, Does.Contain(matchingCopy));
            Assert.That(state.Players[0].Deck, Is.EquivalentTo(new[] { different }));
            Assert.That(state.DelayedAbilities, Has.Count.EqualTo(1));
        }

        [Test]
        public void StrictCatalogGate_HasAllPrintingsAndNoDataOnlyCards()
        {
            Assert.That(TcgCatalog.Printings, Has.Count.EqualTo(916));
            Assert.That(TcgCatalog.Unfinished.Select(card =>
                card.SetCode + " " + card.CollectorNumber + " " + card.Name), Is.Empty);
            Assert.That(TcgCatalog.Printings.SelectMany(card => card.RuleLineIds)
                .Distinct().Count(), Is.EqualTo(723));
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

        private static CardDefinition Creature(string name) => new CardDefinition
        {
            Id = name,
            Name = name,
            Civilizations = new[] { Civilization.Nature },
            Cost = 1,
            Type = CardType.Creature,
            Races = new[] { "Test Race" },
            Power = 1000,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
