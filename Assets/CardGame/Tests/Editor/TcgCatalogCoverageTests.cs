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

        // This is the final zero-gap release gate. It is intentionally explicit
        // during iterative implementation batches; remove Explicit only when the
        // ability ledger reports zero unmapped rules lines.
        [Test, Explicit("Enable as the final gate after every TCG rules line is mapped.")]
        public void Catalog_AllCardRulesAreComplete()
        {
            Assert.That(TcgCatalog.Unfinished, Is.Empty);
        }
    }
}
