using System;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class TcgCatalogCoverageTests
    {
        [Test]
        public void Catalog_ContainsEveryOriginalEnglishTcgPrinting()
        {
            Assert.That(TcgCatalog.Printings.Count, Is.EqualTo(916));
            Assert.That(TcgCatalog.Printings.Select(card => card.Id).Distinct().Count(), Is.EqualTo(916));
            Assert.That(TcgCatalog.ForSet("DM-01").Count(), Is.EqualTo(120));
            Assert.That(TcgCatalog.ForSet("DM-12").Count(), Is.EqualTo(60));
            Assert.That(TcgCatalog.ForSet("PROMO").Count(), Is.EqualTo(16));
        }

        [Test]
        public void Catalog_PreservesBothCivilizationsForMulticoloredCards()
        {
            var bombazar = TcgCatalog.ByName("Bombazar, Dragon of Destiny").Single();

            Assert.That(bombazar.Civilizations, Is.EquivalentTo(new[] { Civilization.Fire, Civilization.Nature }));
            Assert.That(bombazar.Races, Is.EquivalentTo(new[] { "Armored Dragon" }));
        }

        [Test]
        public void Catalog_EveryPrintedRulesLineHasAStableId()
        {
            foreach (var card in TcgCatalog.Printings)
            {
                var printedLineCount = card.RulesText.Split('\n').Count(line =>
                    !string.IsNullOrWhiteSpace(line.Trim().TrimStart('■').Trim()));
                Assert.That(card.RuleLineIds.Length, Is.EqualTo(printedLineCount), card.Name);
                Assert.That(card.RuleLineIds.All(id => id.StartsWith("ability.")), Is.True, card.Name);
            }
        }

        [Test]
        public void AbilityRegistry_HasNoDuplicateHandlerIds()
        {
            Assert.That(AbilityRegistry.Handlers.Select(handler => handler.Id).Distinct().Count(),
                Is.EqualTo(AbilityRegistry.Handlers.Count));
        }

        // Final zero-gap release gate. This must run in every normal test pass so
        // a newly imported or accidentally unmapped rules line cannot ship.
        [Test]
        public void Catalog_AllCardRulesAreComplete()
        {
            Assert.That(TcgCatalog.Unfinished, Is.Empty);
        }

        [Test]
        public void RandomDeckFromOneSet_IsLegalAndUsesOnlyThatSet()
        {
            var deck = TcgCatalog.BuildRandomDeckFromSets(new[] { "DM-01" }, seed: 2004);

            Assert.That(deck.Count, Is.EqualTo(40));
            Assert.That(deck.All(card => card.SetCode == "DM-01"), Is.True);
            Assert.That(deck.GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .Max(group => group.Count()), Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void RandomDeckFromMultipleSets_IsDeterministicAndStaysInSelectedPool()
        {
            var selectedSets = new[] { "DM-01", "DM-02", "PROMO" };
            var first = TcgCatalog.BuildRandomDeckFromSets(selectedSets, seed: 8675309);
            var repeated = TcgCatalog.BuildRandomDeckFromSets(selectedSets.Reverse(), seed: 8675309);
            var different = TcgCatalog.BuildRandomDeckFromSets(selectedSets, seed: 8675310);

            Assert.That(first.Select(card => card.Id), Is.EqualTo(repeated.Select(card => card.Id)));
            Assert.That(first.All(card => selectedSets.Contains(card.SetCode)), Is.True);
            Assert.That(first.Select(card => card.Id), Is.Not.EqualTo(different.Select(card => card.Id)));
        }

        [Test]
        public void RandomDeckFromSetsAndCivilizations_UsesOnlyTheSelectedCivilizations()
        {
            var deck = TcgCatalog.BuildRandomDeckFromSets(
                new[] { "DM-01" },
                new[] { Civilization.Fire },
                seed: 2004);

            Assert.That(deck.Count, Is.EqualTo(40));
            Assert.That(deck.All(card => card.Civilizations.Length > 0 &&
                                             card.Civilizations.All(civilization =>
                                                 civilization == Civilization.Fire)), Is.True);
            Assert.That(deck.GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .Max(group => group.Count()), Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void RandomDeckFromSetsAndCivilizations_RejectsAnEmptyCivilizationSelection()
        {
            Assert.That(() => TcgCatalog.BuildRandomDeckFromSets(
                    new[] { "DM-01" }, Array.Empty<Civilization>(), seed: 1),
                Throws.ArgumentException.With.Message.Contains("at least one civilization"));
        }

        [Test]
        public void RandomDeckFromSets_RejectsMissingOrUnknownSelection()
        {
            Assert.That(() => TcgCatalog.BuildRandomDeckFromSets(Array.Empty<string>(), seed: 1),
                Throws.ArgumentException.With.Message.Contains("at least one"));
            Assert.That(() => TcgCatalog.BuildRandomDeckFromSets(new[] { "DM-99" }, seed: 1),
                Throws.ArgumentException.With.Message.Contains("DM-99"));
        }
    }
}
