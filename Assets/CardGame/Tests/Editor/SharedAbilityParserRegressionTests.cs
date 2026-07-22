using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class SharedAbilityParserRegressionTests
    {
        [Test]
        public void LegendaryBynor_GrantsUnblockableOnlyToOtherAlliedWaterCreatures()
        {
            var state = EmptyState();
            var bynor = Instance(10, "dm03-18-55");
            var alliedWater = Instance(11, Creature("allied-water", Civilization.Water));
            var alliedFire = Instance(12, Creature("allied-fire", Civilization.Fire));
            var opposingWater = Instance(20, Creature("opposing-water", Civilization.Water));
            state.Players[0].BattleZone.Add(bynor);
            state.Players[0].BattleZone.Add(alliedWater);
            state.Players[0].BattleZone.Add(alliedFire);
            state.Players[1].BattleZone.Add(opposingWater);
            var resolver = new AbilityResolver();

            Assert.That(resolver.HasKeyword(state, alliedWater, CardKeyword.Unblockable), Is.True);
            Assert.That(resolver.HasKeyword(state, bynor, CardKeyword.Unblockable), Is.False);
            Assert.That(resolver.HasKeyword(state, alliedFire, CardKeyword.Unblockable), Is.False);
            Assert.That(resolver.HasKeyword(state, opposingWater, CardKeyword.Unblockable), Is.False);
        }

        [Test]
        public void FluorogillManta_GrantsUnblockableToAlliedLightAndDarknessCreatures()
        {
            var state = EmptyState();
            var manta = Instance(10, "dm10-34-110");
            var light = Instance(11, Creature("light", Civilization.Light));
            var darkness = Instance(12, Creature("darkness", Civilization.Darkness));
            var nature = Instance(13, Creature("nature", Civilization.Nature));
            var opposingLight = Instance(20, Creature("opposing-light", Civilization.Light));
            state.Players[0].BattleZone.Add(manta);
            state.Players[0].BattleZone.Add(light);
            state.Players[0].BattleZone.Add(darkness);
            state.Players[0].BattleZone.Add(nature);
            state.Players[1].BattleZone.Add(opposingLight);
            var resolver = new AbilityResolver();

            Assert.That(resolver.HasKeyword(state, light, CardKeyword.Unblockable), Is.True);
            Assert.That(resolver.HasKeyword(state, darkness, CardKeyword.Unblockable), Is.True);
            Assert.That(resolver.HasKeyword(state, nature, CardKeyword.Unblockable), Is.False);
            Assert.That(resolver.HasKeyword(state, opposingLight, CardKeyword.Unblockable), Is.False);
        }

        [Test]
        public void Hydrooze_GrantsUnblockableToBothPrintedAlliedRaces()
        {
            var state = EmptyState();
            var hydrooze = Instance(10, "dm12-17-55");
            var cyberLord = Instance(11,
                Creature("cyber-lord", Civilization.Water, "Cyber Lord"));
            var hedrian = Instance(12,
                Creature("hedrian", Civilization.Darkness, "Hedrian"));
            var other = Instance(13, Creature("other", Civilization.Nature));
            var opposingCyberLord = Instance(20,
                Creature("opposing-cyber-lord", Civilization.Water, "Cyber Lord"));
            state.Players[0].BattleZone.Add(hydrooze);
            state.Players[0].BattleZone.Add(cyberLord);
            state.Players[0].BattleZone.Add(hedrian);
            state.Players[0].BattleZone.Add(other);
            state.Players[1].BattleZone.Add(opposingCyberLord);
            var resolver = new AbilityResolver();

            Assert.That(resolver.HasKeyword(state, hydrooze, CardKeyword.Unblockable), Is.True);
            Assert.That(resolver.HasKeyword(state, cyberLord, CardKeyword.Unblockable), Is.True);
            Assert.That(resolver.HasKeyword(state, hedrian, CardKeyword.Unblockable), Is.True);
            Assert.That(resolver.HasKeyword(state, other, CardKeyword.Unblockable), Is.False);
            Assert.That(resolver.HasKeyword(state, opposingCyberLord, CardKeyword.Unblockable), Is.False);
        }

        [Test]
        public void AerodactylKooza_IsUnblockableOnlyWhileAttackingACreature()
        {
            var kooza = TcgCatalog.ById["dm09-36-55"];

            Assert.That(kooza.HasKeyword(CardKeyword.UnblockableWhenAttackingCreature), Is.True);
            Assert.That(kooza.HasKeyword(CardKeyword.Unblockable), Is.False);
        }

        [Test]
        public void ArmoredGroblav_CountsOtherFireCreaturesOnBothSides()
        {
            var state = EmptyState();
            var groblav = Instance(10, "promo-l1-y1");
            var alliedFire = Instance(11, Creature("allied-fire", Civilization.Fire));
            var opposingFire = Instance(20, Creature("opposing-fire", Civilization.Fire));
            var opposingNature = Instance(21, Creature("opposing-nature", Civilization.Nature));
            state.Players[0].BattleZone.Add(groblav);
            state.Players[0].BattleZone.Add(alliedFire);
            state.Players[1].BattleZone.Add(opposingFire);
            state.Players[1].BattleZone.Add(opposingNature);

            Assert.That(new AbilityResolver().CurrentPower(state, groblav, true), Is.EqualTo(8000));
        }

        [Test]
        public void Hydrooze_PowerAuraRecognizesBothPrintedRaces()
        {
            var state = EmptyState();
            var hydrooze = Instance(10, "dm12-17-55");
            var cyberLord = Instance(11,
                Creature("cyber-lord", Civilization.Water, "Cyber Lord", 1000));
            var hedrian = Instance(12,
                Creature("hedrian", Civilization.Darkness, "Hedrian", 1000));
            state.Players[0].BattleZone.Add(hydrooze);
            state.Players[0].BattleZone.Add(cyberLord);
            state.Players[0].BattleZone.Add(hedrian);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentPower(state, cyberLord), Is.EqualTo(3000));
            Assert.That(resolver.CurrentPower(state, hedrian), Is.EqualTo(3000));
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

        private static CardDefinition Creature(string id, Civilization civilization,
            string race = "Test Race", int power = 1000) => new CardDefinition
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

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
