using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core
{
    public static partial class TcgCatalog
    {
        public const string CardDatabaseSource = "https://db.duelmasters.us/search?card_set=tcg_only";

        public static readonly IReadOnlyList<CardDefinition> Printings = CreatePrintings().AsReadOnly();
        public static readonly IReadOnlyDictionary<string, CardDefinition> ById =
            Printings.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<CardDefinition> ByName(string name) =>
            Printings.Where(card => card.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<CardDefinition> ForSet(string setCode) =>
            Printings.Where(card => card.SetCode.Equals(setCode, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<CardDefinition> Unfinished =>
            Printings.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete);

        public static IReadOnlyList<CardDefinition> BuildRandomDeckFromSets(
            IEnumerable<string> setCodes,
            int seed,
            int deckSize = 40)
        {
            if (setCodes == null) throw new ArgumentNullException(nameof(setCodes));
            if (deckSize <= 0) throw new ArgumentOutOfRangeException(nameof(deckSize));

            var selectedSets = new HashSet<string>(
                setCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (selectedSets.Count == 0)
                throw new ArgumentException("Choose at least one card set.", nameof(setCodes));

            var knownSets = new HashSet<string>(
                Printings.Select(card => card.SetCode), StringComparer.OrdinalIgnoreCase);
            var unknownSets = selectedSets.Where(code => !knownSets.Contains(code)).OrderBy(code => code).ToArray();
            if (unknownSets.Length > 0)
                throw new ArgumentException($"Unknown card set: {string.Join(", ", unknownSets)}.", nameof(setCodes));

            var cardsByName = Printings
                .Where(card => selectedSets.Contains(card.SetCode) &&
                               card.ImplementationStatus == CardImplementationStatus.Complete)
                .GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(card => card.Id, StringComparer.OrdinalIgnoreCase).ToArray())
                .ToArray();

            const int maximumCopiesPerCard = 4;
            if (cardsByName.Length * maximumCopiesPerCard < deckSize)
                throw new ArgumentException(
                    $"The selected sets contain only {cardsByName.Length} playable card names; " +
                    $"at least {(deckSize + maximumCopiesPerCard - 1) / maximumCopiesPerCard} are needed for a legal {deckSize}-card deck.",
                    nameof(setCodes));

            var random = new Random(seed);
            var copyCounts = new int[cardsByName.Length];
            var eligibleNames = Enumerable.Range(0, cardsByName.Length).ToList();
            var deck = new List<CardDefinition>(deckSize);

            while (deck.Count < deckSize)
            {
                var eligibleIndex = random.Next(eligibleNames.Count);
                var nameIndex = eligibleNames[eligibleIndex];
                var printings = cardsByName[nameIndex];
                deck.Add(printings[random.Next(printings.Length)]);

                copyCounts[nameIndex]++;
                if (copyCounts[nameIndex] == maximumCopiesPerCard)
                    eligibleNames.RemoveAt(eligibleIndex);
            }

            return deck.AsReadOnly();
        }

        private static CardDefinition C(
            string id,
            string name,
            Civilization[] civilizations,
            int cost,
            CardType type,
            string[] races,
            int power,
            string setCode,
            string collectorNumber,
            int mediaId,
            string rulesText,
            PrintedRuleLine[] printedRules)
        {
            var card = new CardDefinition
            {
                Id = id,
                Name = name,
                Civilizations = civilizations,
                Cost = cost,
                Type = type,
                Races = races,
                Power = power,
                RulesText = rulesText,
                SetCode = setCode,
                CollectorNumber = collectorNumber,
                DataSource = CardDatabaseSource,
                DatabaseMediaId = mediaId,
                HasDatabaseVideo = CardMediaCatalog.HasVideo(mediaId)
            };
            AbilityRegistry.Configure(card, printedRules);
            return card;
        }
    }
}
