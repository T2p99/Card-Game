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
