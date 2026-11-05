using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CardGame.Core
{
    public readonly struct DeckAbilityFilter : IEquatable<DeckAbilityFilter>
    {
        public CardKeyword Keyword { get; }
        public Civilization? StealthCivilization { get; }
        public string Name => StealthCivilization.HasValue
            ? StealthCivilization.Value + " Stealth"
            : Regex.Replace(Keyword.ToString(), "([a-z])([A-Z])", "$1 $2");

        public DeckAbilityFilter(CardKeyword keyword, Civilization? stealthCivilization = null)
        {
            if (!Enum.IsDefined(typeof(CardKeyword), keyword))
                throw new ArgumentOutOfRangeException(nameof(keyword));
            if (stealthCivilization.HasValue &&
                (keyword != CardKeyword.Stealth ||
                 !Enum.IsDefined(typeof(Civilization), stealthCivilization.Value)))
                throw new ArgumentException("A civilization qualifier is valid only for stealth.",
                    nameof(stealthCivilization));
            Keyword = keyword;
            StealthCivilization = stealthCivilization;
        }

        public bool Equals(DeckAbilityFilter other) =>
            Keyword == other.Keyword && StealthCivilization == other.StealthCivilization;
        public override bool Equals(object obj) => obj is DeckAbilityFilter other && Equals(other);
        public override int GetHashCode() =>
            unchecked((int)Keyword * 397 ^ (StealthCivilization.HasValue ? (int)StealthCivilization.Value + 1 : 0));
    }

    public sealed class DeckCatalogueFilters
    {
        public HashSet<int> ManaCosts { get; } = new HashSet<int>();
        public HashSet<CardType> CardTypes { get; } = new HashSet<CardType>();
        public HashSet<Civilization> Civilizations { get; } = new HashSet<Civilization>();
        public bool MultiCivilization;
        public HashSet<int> ShieldBreakers { get; } = new HashSet<int>();
        public HashSet<DeckAbilityFilter> SpecialAbilities { get; } = new HashSet<DeckAbilityFilter>();
        public int SelectionCount => ManaCosts.Count + CardTypes.Count + Civilizations.Count +
            (MultiCivilization ? 1 : 0) + ShieldBreakers.Count + SpecialAbilities.Count;

        public DeckCatalogueFilters Copy()
        {
            var copy = new DeckCatalogueFilters { MultiCivilization = MultiCivilization };
            copy.ManaCosts.UnionWith(ManaCosts);
            copy.CardTypes.UnionWith(CardTypes);
            copy.Civilizations.UnionWith(Civilizations);
            copy.ShieldBreakers.UnionWith(ShieldBreakers);
            copy.SpecialAbilities.UnionWith(SpecialAbilities);
            return copy;
        }

        public void Validate()
        {
            if (ManaCosts.Any(cost => cost < 1 || cost > DeckCatalogue.MaximumManaCost))
                throw new ArgumentOutOfRangeException(nameof(ManaCosts),
                    "Choose exact mana costs from 1 to the catalogue maximum.");
            if (CardTypes.Any(type => type != CardType.Creature && type != CardType.Spell))
                throw new ArgumentException("Card type filters support Creature and Spell.", nameof(CardTypes));
            if (Civilizations.Any(civilization => !Enum.IsDefined(typeof(Civilization), civilization)))
                throw new ArgumentOutOfRangeException(nameof(Civilizations));
            if (ShieldBreakers.Any(count => count < 1 || count > 3))
                throw new ArgumentOutOfRangeException(nameof(ShieldBreakers));
        }

        public bool Matches(CardDefinition card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (ManaCosts.Count > 0 && !ManaCosts.Contains(card.Cost)) return false;
            if (CardTypes.Count > 0 &&
                !(CardTypes.Contains(CardType.Creature) && card.IsCreature) &&
                !(CardTypes.Contains(CardType.Spell) && card.Type == CardType.Spell)) return false;
            if ((Civilizations.Count > 0 || MultiCivilization) &&
                !card.Civilizations.Any(Civilizations.Contains) &&
                !(MultiCivilization && card.Civilizations.Length > 1)) return false;
            if (ShieldBreakers.Count == 0 && SpecialAbilities.Count == 0) return true;

            var abilities = DeckCatalogue.PrintedAbilities(card).ToArray();
            if (ShieldBreakers.Count > 0)
            {
                var breaker = card.Type == CardType.Spell ? 1 : !card.IsCreature ? 0 :
                    abilities.Any(ability => ability.Keyword == CardKeyword.TripleBreaker) ? 3 :
                    abilities.Any(ability => ability.Keyword == CardKeyword.DoubleBreaker) ? 2 : 1;
                if (!ShieldBreakers.Contains(breaker)) return false;
            }
            return SpecialAbilities.Count == 0 || abilities.Any(SpecialAbilities.Contains);
        }
    }

    public static partial class DeckCatalogue
    {
        public static readonly int MaximumManaCost = TcgCatalog.Printings.Max(card => card.Cost);
        public static readonly IReadOnlyList<DeckAbilityFilter> AbilityOptions = TcgCatalog.Printings
            .SelectMany(PrintedAbilities)
            .Where(ability => ability.Keyword != CardKeyword.DoubleBreaker &&
                              ability.Keyword != CardKeyword.TripleBreaker)
            .Distinct().OrderBy(ability => ability.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        public static IEnumerable<DeckAbilityFilter> PrintedAbilities(CardDefinition card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            var keywords = card.Abilities.Select(ability => ability.Keyword)
                .Concat(card.RuleAbilities.Where(ability => ability.Operation == AbilityOperation.GrantKeyword &&
                    ability.Selector?.Mode == RuleSelectionMode.SourceCard).Select(ability => ability.Keyword));
            foreach (var keyword in keywords.Distinct())
                yield return new DeckAbilityFilter(keyword,
                    keyword == CardKeyword.Stealth && card.HasStealthCivilization
                        ? card.StealthCivilization : (Civilization?)null);
        }

        public static IReadOnlyList<CardDefinition> AvailableCards(SavedDeck deck, DeckCardSort sort,
            DeckCatalogueFilters filters)
        {
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            filters.Validate();
            return QueryAvailableCards(deck, sort, filters.Matches);
        }
    }
}
