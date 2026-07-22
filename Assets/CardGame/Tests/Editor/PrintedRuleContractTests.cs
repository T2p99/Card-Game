using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    /// <summary>
    /// One independently addressable test case for every distinct printed rule
    /// line in the original English TCG pool. Set-specific behavior tests verify
    /// state changes; these cases are the exhaustive parser/registration gate.
    /// </summary>
    public sealed class PrintedRuleContractTests
    {
        public static IEnumerable<TestCaseData> PrintedRuleCases()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in TcgCatalog.Printings)
            {
                var lines = PrintedLines(card).ToArray();
                for (var index = 0; index < card.RuleLineIds.Length; index++)
                {
                    var abilityId = card.RuleLineIds[index];
                    if (!seen.Add(abilityId)) continue;

                    yield return new TestCaseData(card.Id, index, abilityId)
                        .SetName($"PrintedRule_{abilityId.Replace('.', '_')}_{card.Id}");
                }
            }
        }

        public static IEnumerable<TestCaseData> ActivatedParentCases()
        {
            foreach (var card in TcgCatalog.Printings)
            {
                var lines = PrintedLines(card).ToArray();
                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];
                    AbilityTiming timing;
                    if (line.StartsWith("Silent skill", StringComparison.OrdinalIgnoreCase))
                        timing = AbilityTiming.SilentSkill;
                    else if (line.StartsWith("Instead of having this creature attack",
                                 StringComparison.OrdinalIgnoreCase) ||
                             line.IndexOf("may tap instead of attacking to use this creature's tap ability",
                                 StringComparison.OrdinalIgnoreCase) >= 0)
                        timing = AbilityTiming.TapAbility;
                    else
                        continue;

                    yield return new TestCaseData(card.Id, index, timing)
                        .SetName($"ActivatedParent_{timing}_{card.Id}");
                }
            }
        }

        [TestCaseSource(nameof(PrintedRuleCases))]
        public void PrintedRule_IsIndividuallyRecognized(
            string cardId, int lineIndex, string abilityId)
        {
            var source = TcgCatalog.ById[cardId];
            var text = PrintedLines(source).ElementAt(lineIndex);
            var isolated = BasicCopy(source);

            var mapped = AbilityRegistry.Apply(
                isolated, new PrintedRuleLine(abilityId, text));

            Assert.That(mapped, Is.True,
                $"{source.SetCode} {source.CollectorNumber} {source.Name}: {text}");
            Assert.That(HasExecutableRepresentation(isolated, text), Is.True,
                $"{source.SetCode} {source.CollectorNumber} {source.Name}: " +
                $"the registry accepted the line but produced no executable data: {text}");
        }

        [Test]
        public void PrintedRuleCases_CoverEveryDistinctStableAbilityId()
        {
            var expected = TcgCatalog.Printings
                .SelectMany(card => card.RuleLineIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            Assert.That(PrintedRuleCases().Count(), Is.EqualTo(expected));
            Assert.That(expected, Is.EqualTo(723));
        }

        [TestCaseSource(nameof(ActivatedParentCases))]
        public void ActivatedKeyword_SubordinateRulesUseParentTiming(
            string cardId, int keywordLineIndex, AbilityTiming expectedTiming)
        {
            var card = TcgCatalog.ById[cardId];
            var subordinateIds = new HashSet<string>(
                card.RuleLineIds.Skip(keywordLineIndex + 1),
                StringComparer.OrdinalIgnoreCase);
            var rules = card.RuleAbilities.Where(rule =>
                subordinateIds.Contains(rule.Id)).ToArray();

            Assert.That(subordinateIds, Is.Not.Empty,
                $"{card.Name} has no printed ability after its activation keyword.");
            Assert.That(rules, Is.Not.Empty,
                $"{card.Name} has no executable subordinate RuleAbilityDefinition.");
            Assert.That(rules.All(rule => rule.Timing == expectedTiming), Is.True,
                $"{card.Name}: expected every subordinate rule to use {expectedTiming}; " +
                string.Join(", ", rules.Select(rule => $"{rule.Operation}:{rule.Timing}")));
        }

        private static IEnumerable<string> PrintedLines(CardDefinition card) =>
            card.RulesText.Split('\n')
                .Select(line => line.Trim().TrimStart('■').Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

        private static CardDefinition BasicCopy(CardDefinition source) =>
            new CardDefinition
            {
                Id = source.Id,
                Name = source.Name,
                Civilizations = source.Civilizations.ToArray(),
                Cost = source.Cost,
                Type = source.Type,
                Races = source.Races.ToArray(),
                Power = source.Power,
                SetCode = source.SetCode,
                CollectorNumber = source.CollectorNumber,
                DataSource = source.DataSource,
                DatabaseMediaId = source.DatabaseMediaId,
                HasDatabaseVideo = source.HasDatabaseVideo
            };

        private static bool HasExecutableRepresentation(CardDefinition card, string text)
        {
            if (card.Abilities.Length > 0) return true;
            if (card.RuleAbilities.Any(ability =>
                    ability != null &&
                    !string.IsNullOrWhiteSpace(ability.Id) &&
                    ability.Operation != AbilityOperation.None)) return true;
            if (card.EvolutionRequirements.Length > 0 ||
                card.EvolutionRaces.Length > 0 ||
                !string.IsNullOrWhiteSpace(card.EvolutionRace) ||
                card.IsVortexEvolution || card.CanBeAnyEvolutionBase ||
                card.HasStealthCivilization ||
                !string.IsNullOrWhiteSpace(card.CrewBreakerRace) ||
                !string.IsNullOrWhiteSpace(card.CrewBreakerRaceContains)) return true;

            // Multicolored mana placement is a core rule derived directly from
            // the card's civilizations, so the reminder line needs no separate
            // keyword or RuleAbilityDefinition object.
            return text.StartsWith("(This creature is put into your mana zone tapped",
                       StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("(This spell is put into your mana zone tapped",
                       StringComparison.OrdinalIgnoreCase)
                ? card.Civilizations.Length > 1
                : false;
        }
    }
}
