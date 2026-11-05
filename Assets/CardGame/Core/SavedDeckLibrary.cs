using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core
{
    [Serializable]
    public sealed class SavedDeckCard
    {
        public string CardId;
        public int Count;
    }

    [Serializable]
    public sealed class SavedDeck
    {
        public string Id;
        public string Name;
        public bool IsStarter;
        public List<SavedDeckCard> Entries = new List<SavedDeckCard>();

        public int CardCount => Entries.Sum(entry => entry.Count);

        public int CountOf(string cardId) => Entries
            .Where(entry => string.Equals(entry.CardId, cardId, StringComparison.OrdinalIgnoreCase))
            .Sum(entry => entry.Count);

        public int CountOfName(string cardId)
        {
            var name = TcgCatalog.ById[cardId].Name;
            return Entries.Where(entry => string.Equals(TcgCatalog.ById[entry.CardId].Name,
                    name, StringComparison.OrdinalIgnoreCase))
                .Sum(entry => entry.Count);
        }

        public void SetCount(string cardId, int count)
        {
            if (!TcgCatalog.ById.ContainsKey(cardId))
                throw new ArgumentException($"Unknown catalogue card: {cardId}.", nameof(cardId));
            if (count < 0 || count > SavedDeckLibrary.MaximumCopies)
                throw new ArgumentOutOfRangeException(nameof(count), "A card count must be between 0 and 4.");
            if (CountOfName(cardId) - CountOf(cardId) + count > SavedDeckLibrary.MaximumCopies)
                throw new ArgumentException("Only four copies of a card name are allowed, including other printings.");

            var entry = Entries.FirstOrDefault(item =>
                string.Equals(item.CardId, cardId, StringComparison.OrdinalIgnoreCase));
            if (count == 0)
            {
                if (entry != null) Entries.Remove(entry);
            }
            else if (entry != null) entry.Count = count;
            else Entries.Add(new SavedDeckCard { CardId = cardId, Count = count });
        }

        public SavedDeck Copy() => new SavedDeck
        {
            Id = Id,
            Name = Name,
            IsStarter = IsStarter,
            Entries = Entries.Select(entry => new SavedDeckCard
                { CardId = entry.CardId, Count = entry.Count }).ToList()
        };

        public string ValidationError()
        {
            if (string.IsNullOrWhiteSpace(Id)) return "The deck has no saved identity.";
            if (string.IsNullOrWhiteSpace(Name)) return "Enter a deck name.";
            if (Name.Trim().Length > 50) return "Deck names must be 50 characters or fewer.";
            if (Entries == null) return "The deck's card list is missing.";

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var copiesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var total = 0;
            foreach (var entry in Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.CardId) ||
                    !TcgCatalog.ById.TryGetValue(entry.CardId, out var card))
                    return "The deck contains an unknown catalogue card.";
                if (!ids.Add(entry.CardId)) return "A printing must use one entry with a copy count.";
                if (entry.Count < 1 || entry.Count > SavedDeckLibrary.MaximumCopies)
                    return $"The count for {card.Name} must be between 1 and 4.";
                copiesByName.TryGetValue(card.Name, out var previous);
                copiesByName[card.Name] = previous + entry.Count;
                if (copiesByName[card.Name] > SavedDeckLibrary.MaximumCopies)
                    return $"Only four copies of {card.Name} are allowed, including other printings.";
                total += entry.Count;
            }

            return total == SavedDeckLibrary.DeckSize
                ? null
                : $"Save requires exactly {SavedDeckLibrary.DeckSize} cards; this deck has {total}.";
        }

        public IReadOnlyList<CardDefinition> Build()
        {
            var error = ValidationError();
            if (error != null) throw new InvalidOperationException(error);
            return new DeckRecipe(Id, Name, Entries.Select(entry =>
                    new DeckEntry(entry.CardId, entry.Count)).ToArray())
                .Build(TcgCatalog.ById).AsReadOnly();
        }
    }

    [Serializable]
    public sealed class SavedDeckLibrary
    {
        public const int CurrentVersion = 1;
        public const int DeckSize = 40;
        public const int MaximumCopies = 4;
        public const int MaximumCustomDecks = 5;
        public int Version;
        public string SelectedDeckId;
        public List<SavedDeck> Decks;

        public SavedDeck SelectedDeck => Decks.Single(deck => deck.Id == SelectedDeckId);
        public int CustomDeckCount => Decks.Count(deck => !deck.IsStarter);

        public static SavedDeckLibrary CreateDefault()
        {
            var library = new SavedDeckLibrary
            {
                Version = CurrentVersion,
                Decks = Enum.GetValues(typeof(Civilization)).Cast<Civilization>()
                    .Select(civilization => CreateStarterDeck(civilization, TcgCatalog.Printings)).ToList(),
                SelectedDeckId = StarterId(Civilization.Fire)
            };
            library.Validate();
            return library;
        }

        public static SavedDeck CreateStarterDeck(Civilization civilization, IEnumerable<CardDefinition> catalogue)
        {
            if (!Enum.IsDefined(typeof(Civilization), civilization))
                throw new ArgumentOutOfRangeException(nameof(civilization));
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            var candidates = catalogue.Where(card => card.Civilizations.Length == 1 &&
                    card.Civilizations[0] == civilization &&
                    (card.Type == CardType.Creature || card.Type == CardType.Spell) &&
                    card.ImplementationStatus == CardImplementationStatus.Complete)
                .GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(card => card.Id, StringComparer.OrdinalIgnoreCase).First())
                .ToArray();
            if (candidates.Length * MaximumCopies < DeckSize)
                throw new ArgumentException($"The {civilization} pool cannot form a legal 40-card starter.",
                    nameof(catalogue));

            var low = candidates.Where(card => card.Cost <= 5).ToArray();
            var high = candidates.Where(card => card.Cost > 5).ToArray();
            var lowCount = Math.Min(low.Length * MaximumCopies,
                Math.Max(24, DeckSize - high.Length * MaximumCopies));
            var deck = new SavedDeck
            {
                Id = StarterId(civilization),
                Name = civilization + " Starter",
                IsStarter = true
            };
            var random = new Random(unchecked(0x44534b + (int)civilization * 7919));
            AddStarterCards(deck, low, lowCount, random);
            AddStarterCards(deck, high, DeckSize - lowCount, random);
            return deck;
        }

        private static void AddStarterCards(SavedDeck deck, IEnumerable<CardDefinition> cards,
            int count, Random random)
        {
            var creatures = cards.Where(card => card.Type == CardType.Creature).ToList();
            var spells = cards.Where(card => card.Type == CardType.Spell).ToList();
            var addedNames = 0;
            while (count > 0)
            {
                var preferred = addedNames % 3 == 2 ? spells : creatures;
                var pool = preferred.Count > 0 ? preferred : preferred == spells ? creatures : spells;
                var index = random.Next(pool.Count);
                var card = pool[index];
                pool.RemoveAt(index);
                var copies = Math.Min(MaximumCopies, count);
                deck.Entries.Add(new SavedDeckCard { CardId = card.Id, Count = copies });
                count -= copies;
                addedNames++;
            }
        }

        private static string StarterId(Civilization civilization) =>
            "starter-" + civilization.ToString().ToLowerInvariant();

        public void Validate()
        {
            if (Version != CurrentVersion)
                throw new InvalidOperationException($"Unsupported saved deck version: {Version}.");
            if (Decks == null || Decks.Any(deck => deck == null || string.IsNullOrWhiteSpace(deck.Id)))
                throw new InvalidOperationException("The saved deck library is incomplete.");
            if (Decks.Select(deck => deck.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Decks.Count)
                throw new InvalidOperationException("Saved deck identities must be unique.");
            var expectedStarters = Enum.GetValues(typeof(Civilization)).Cast<Civilization>()
                .Select(StarterId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (!Decks.Where(deck => deck.IsStarter).Select(deck => deck.Id)
                    .OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(expectedStarters))
                throw new InvalidOperationException("The library must retain all five starter decks.");
            if (CustomDeckCount > MaximumCustomDecks)
                throw new InvalidOperationException("Only five additional decks can be saved.");
            if (!Decks.Any(deck => deck.Id == SelectedDeckId))
                throw new InvalidOperationException("The selected duel deck is missing.");
            foreach (var deck in Decks)
            {
                var error = deck.ValidationError();
                if (error != null) throw new InvalidOperationException($"{deck.Name}: {error}");
            }
        }

        public SavedDeckLibrary Copy() => new SavedDeckLibrary
        {
            Version = Version,
            SelectedDeckId = SelectedDeckId,
            Decks = Decks.Select(deck => deck.Copy()).ToList()
        };

        public SavedDeckLibrary WithSavedDeck(SavedDeck draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var error = draft.ValidationError();
            if (error != null) throw new ArgumentException(error, nameof(draft));
            var candidate = Copy();
            var index = candidate.Decks.FindIndex(deck => deck.Id == draft.Id);
            if (index < 0)
            {
                if (draft.IsStarter) throw new ArgumentException("New decks cannot replace starter identities.");
                if (candidate.CustomDeckCount >= MaximumCustomDecks)
                    throw new InvalidOperationException("All five additional deck slots are in use.");
                candidate.Decks.Add(draft.Copy());
            }
            else
            {
                if (candidate.Decks[index].IsStarter != draft.IsStarter)
                    throw new ArgumentException("A deck's starter identity cannot be changed.");
                candidate.Decks[index] = draft.Copy();
            }
            candidate.Decks.Single(deck => deck.Id == draft.Id).Name = draft.Name.Trim();
            candidate.Validate();
            return candidate;
        }

        public SavedDeckLibrary WithSelectedDeck(string deckId)
        {
            if (!Decks.Any(deck => deck.Id == deckId))
                throw new ArgumentException("Choose a saved deck for the duel.", nameof(deckId));
            var candidate = Copy();
            candidate.SelectedDeckId = deckId;
            candidate.Validate();
            return candidate;
        }

        public SavedDeckLibrary WithoutDeck(string deckId)
        {
            var deck = Decks.SingleOrDefault(item => item.Id == deckId);
            if (deck == null) throw new ArgumentException("The saved deck was not found.", nameof(deckId));
            if (deck.IsStarter) throw new InvalidOperationException("Starter decks can be edited but not deleted.");
            var candidate = Copy();
            candidate.Decks.RemoveAll(item => item.Id == deckId);
            if (candidate.SelectedDeckId == deckId)
                candidate.SelectedDeckId = StarterId(Civilization.Fire);
            candidate.Validate();
            return candidate;
        }
    }

    public enum DeckCardSort
    {
        NameAscending,
        NameDescending,
        ManaCost
    }

    public static partial class DeckCatalogue
    {
        public static IReadOnlyList<CardDefinition> AvailableCards(SavedDeck deck, DeckCardSort sort,
            int? maximumMana = null, Civilization? civilization = null)
        {
            if (deck == null) throw new ArgumentNullException(nameof(deck));
            if (!Enum.IsDefined(typeof(DeckCardSort), sort)) throw new ArgumentOutOfRangeException(nameof(sort));
            if (maximumMana < 0) throw new ArgumentOutOfRangeException(nameof(maximumMana));
            if (civilization.HasValue && !Enum.IsDefined(typeof(Civilization), civilization.Value))
                throw new ArgumentOutOfRangeException(nameof(civilization));
            return QueryAvailableCards(deck, sort, card =>
                (!maximumMana.HasValue || card.Cost <= maximumMana.Value) &&
                (!civilization.HasValue || card.Civilizations.Contains(civilization.Value)));
        }

        private static IReadOnlyList<CardDefinition> QueryAvailableCards(SavedDeck deck, DeckCardSort sort,
            Func<CardDefinition, bool> matches)
        {
            if (deck == null) throw new ArgumentNullException(nameof(deck));
            if (!Enum.IsDefined(typeof(DeckCardSort), sort)) throw new ArgumentOutOfRangeException(nameof(sort));
            var included = new HashSet<string>(deck.Entries.Select(entry => entry.CardId),
                StringComparer.OrdinalIgnoreCase);
            var cards = TcgCatalog.Printings.Where(card => !included.Contains(card.Id) && matches(card));
            IOrderedEnumerable<CardDefinition> ordered;
            switch (sort)
            {
                case DeckCardSort.NameDescending:
                    ordered = cards.OrderByDescending(card => card.Name, StringComparer.OrdinalIgnoreCase);
                    break;
                case DeckCardSort.ManaCost:
                    ordered = cards.OrderBy(card => card.Cost)
                        .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    ordered = cards.OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase);
                    break;
            }
            return ordered.ThenBy(card => card.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
