using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class StarterDeckCatalogTests
    {
        [Test]
        public void ShobusFireDeck_HasExactLegalCardCountAndCopies()
        {
            var deck = StarterDeckCatalog.ShobusFireDeck.Build(StarterDeckCatalog.Cards);

            Assert.That(deck.Count, Is.EqualTo(40));
            Assert.That(deck.Count(card => card.Name == "Deadly Fighter Braid Claw"), Is.EqualTo(4));
            Assert.That(deck.Count(card => card.Name == "Tornado Flame"), Is.EqualTo(2));
            Assert.That(deck.All(card => card.Civilizations.Single() == Civilization.Fire), Is.True);
        }

        [Test]
        public void KokujosDarknessDeck_HasExactLegalCardCountAndCopies()
        {
            var deck = StarterDeckCatalog.KokujosDarknessDeck.Build(StarterDeckCatalog.Cards);

            Assert.That(deck.Count, Is.EqualTo(40));
            Assert.That(deck.Count(card => card.Name == "Horrid Worm"), Is.EqualTo(4));
            Assert.That(deck.Count(card => card.Name == "Terror Pit"), Is.EqualTo(1));
            Assert.That(deck.All(card => card.Civilizations.Single() == Civilization.Darkness), Is.True);
        }

        [Test]
        public void StarterDecks_CanInitializeDeterministicMatch()
        {
            var fire = StarterDeckCatalog.ShobusFireDeck.Build(StarterDeckCatalog.Cards);
            var darkness = StarterDeckCatalog.KokujosDarknessDeck.Build(StarterDeckCatalog.Cards);

            var state = new RulesEngine().StartGame(fire, darkness, seed: 2004, startingPlayerId: 0);

            Assert.That(state.Players[0].Deck.Count, Is.EqualTo(30));
            Assert.That(state.Players[1].Deck.Count, Is.EqualTo(30));
            Assert.That(state.Players[0].Hand.Count, Is.EqualTo(5));
            Assert.That(state.Players[1].Shields.Count, Is.EqualTo(5));
        }

        [Test]
        public void Catalog_TracksProvenanceAndImplementationStatus()
        {
            Assert.That(StarterDeckCatalog.Cards.Count, Is.EqualTo(41));
            Assert.That(StarterDeckCatalog.Cards.Values.All(card =>
                card.DataSource == StarterDeckCatalog.CardDatabaseSource), Is.True);
            Assert.That(StarterDeckCatalog.Cards["dm01-070"].KeywordValue(CardKeyword.PowerAttacker), Is.EqualTo(2000));
            Assert.That(StarterDeckCatalog.Cards["dm01-074"].HasKeyword(CardKeyword.MustAttackIfAble), Is.True);
            Assert.That(StarterDeckCatalog.Cards["dm01-080"].ImplementationStatus, Is.EqualTo(CardImplementationStatus.Complete));
            Assert.That(StarterDeckCatalog.Cards["dm01-047"].HasKeyword(CardKeyword.Slayer), Is.True);
            Assert.That(StarterDeckCatalog.Cards["dm02-029"].HasKeyword(CardKeyword.Blocker), Is.True);
            Assert.That(StarterDeckCatalog.Cards["dm01-073"].ImplementationStatus, Is.EqualTo(CardImplementationStatus.Complete));
        }

        [Test]
        public void Catalog_HasPlayableRulesForEveryStarterDeckCard()
        {
            var incomplete = StarterDeckCatalog.Cards.Values
                .Where(card => card.ImplementationStatus == CardImplementationStatus.DataOnly)
                .Select(card => card.Name)
                .ToArray();

            Assert.That(incomplete, Is.Empty,
                $"Starter cards still marked data-only: {string.Join(", ", incomplete)}");
            Assert.That(StarterDeckCatalog.Cards["dm01-082"].RulesText, Does.Contain("power 2000 or less"));
            Assert.That(StarterDeckCatalog.Cards["dm01-087"].KeywordValue(CardKeyword.PowerAttacker), Is.EqualTo(4000));
            Assert.That(StarterDeckCatalog.Cards["dm02-034"].EvolutionRace, Is.EqualTo("Human"));
            Assert.That(StarterDeckCatalog.Cards["dm02-024"].EvolutionRace, Is.EqualTo("Parasite Worm"));
        }

        [Test]
        public void Catalog_MapsDatabaseImagesAndAttackVideos()
        {
            var brawler = StarterDeckCatalog.Cards["dm01-070"];
            var braidClaw = StarterDeckCatalog.Cards["dm01-074"];

            Assert.That(brawler.DatabaseMediaId, Is.EqualTo(80));
            Assert.That(brawler.DatabaseImageUrl, Is.EqualTo("https://img.duelmasters.us/0080.webp"));
            Assert.That(brawler.HasDatabaseVideo, Is.True);
            Assert.That(brawler.DatabaseVideoUrl, Is.EqualTo("https://media.duelmasters.us/0080.mp4"));
            Assert.That(braidClaw.DatabaseMediaId, Is.EqualTo(84));
            Assert.That(braidClaw.HasDatabaseVideo, Is.False);
        }
    }
}
