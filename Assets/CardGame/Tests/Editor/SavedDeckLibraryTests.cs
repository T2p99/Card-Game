using System;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class SavedDeckLibraryTests
    {
        [TestCase(Civilization.Light)]
        [TestCase(Civilization.Water)]
        [TestCase(Civilization.Darkness)]
        [TestCase(Civilization.Fire)]
        [TestCase(Civilization.Nature)]
        public void StarterDeck_IsPlayableMonoCivilizationWithTheRequestedManaSplit(Civilization civilization)
        {
            var starter = SavedDeckLibrary.CreateStarterDeck(civilization, TcgCatalog.Printings);
            var deck = starter.Build();

            Assert.That(deck.Count, Is.EqualTo(40));
            Assert.That(deck.All(card => card.Civilizations.SequenceEqual(new[] { civilization })), Is.True);
            Assert.That(deck.Count(card => card.Cost <= 5), Is.EqualTo(24));
            Assert.That(deck.Count(card => card.Cost > 5), Is.EqualTo(16));
            Assert.That(deck.Any(card => card.Type == CardType.Spell), Is.True);
            Assert.That(deck.Any(card => card.Type == CardType.Creature), Is.True);
            Assert.That(deck.GroupBy(card => card.Name).All(group => group.Count() <= 4), Is.True);
            Assert.That(new RulesEngine().StartGame(deck, deck, 2004).Players[0].Deck.Count, Is.EqualTo(30));
        }

        [Test]
        public void StarterCreation_IsStableEvenIfCatalogueOrderChanges()
        {
            foreach (Civilization civilization in Enum.GetValues(typeof(Civilization)))
            {
                var first = SavedDeckLibrary.CreateStarterDeck(civilization, TcgCatalog.Printings);
                var second = SavedDeckLibrary.CreateStarterDeck(civilization, TcgCatalog.Printings.Reverse());
                Assert.That(second.Entries.Select(entry => (entry.CardId, entry.Count)),
                    Is.EqualTo(first.Entries.Select(entry => (entry.CardId, entry.Count))));
            }
        }

        [TestCase(5, 6, 20)]
        [TestCase(7, 3, 28)]
        [TestCase(10, 0, 40)]
        public void StarterCreation_UsesTheClosestFeasibleManaSplit(int lowNames, int highNames, int expectedLow)
        {
            var cards = Enumerable.Range(0, lowNames + highNames).Select(index => new CardDefinition
            {
                Id = "test-" + index,
                Name = "Test card " + index,
                Civilizations = new[] { Civilization.Nature },
                Cost = index < lowNames ? 3 : 7,
                Type = index % 3 == 0 ? CardType.Spell : CardType.Creature,
                ImplementationStatus = CardImplementationStatus.Complete
            }).ToArray();
            var byId = cards.ToDictionary(card => card.Id);
            var starter = SavedDeckLibrary.CreateStarterDeck(Civilization.Nature, cards);

            Assert.That(starter.CardCount, Is.EqualTo(40));
            Assert.That(starter.Entries.Where(entry => byId[entry.CardId].Cost <= 5)
                .Sum(entry => entry.Count), Is.EqualTo(expectedLow));
            Assert.That(starter.Entries.All(entry => entry.Count <= 4), Is.True);
        }

        [TestCase(39)]
        [TestCase(41)]
        public void Editing_CanLeaveTheSizeLimitButCannotSaveAnInvalidSize(int count)
        {
            var library = SavedDeckLibrary.CreateDefault();
            var draft = library.SelectedDeck.Copy();
            if (count < 40) draft.SetCount(draft.Entries[0].CardId, draft.Entries[0].Count - 1);
            else draft.SetCount(UnusedCard(draft).Id, 1);

            Assert.That(draft.CardCount, Is.EqualTo(count));
            Assert.That(() => library.WithSavedDeck(draft),
                Throws.ArgumentException.With.Message.Contains("exactly 40"));
            Assert.That(library.SelectedDeck.CardCount, Is.EqualTo(40));
        }

        [Test]
        public void SavingEdits_PreservesMixedCivilizationsWithoutEnforcingTheStarterManaRatio()
        {
            var library = SavedDeckLibrary.CreateDefault();
            var draft = library.SelectedDeck.Copy();
            var high = draft.Entries.First(entry => TcgCatalog.ById[entry.CardId].Cost > 5);
            draft.SetCount(high.CardId, high.Count - 1);
            var addition = TcgCatalog.Printings.First(card => card.Cost <= 5 &&
                card.Civilizations.Contains(Civilization.Water) && draft.CountOfName(card.Id) == 0);
            draft.SetCount(addition.Id, 1);
            draft.Name = "My mixed deck";
            var saved = library.WithSavedDeck(draft);
            draft.SetCount(addition.Id, 0);

            Assert.That(saved.SelectedDeck.Name, Is.EqualTo("My mixed deck"));
            Assert.That(saved.SelectedDeck.CountOf(addition.Id), Is.EqualTo(1));
            Assert.That(saved.SelectedDeck.Build().Count(card => card.Cost <= 5), Is.EqualTo(25));
            Assert.That(library.SelectedDeck.Name, Is.Not.EqualTo("My mixed deck"));
            Assert.That(saved.SelectedDeck.Build().SelectMany(card => card.Civilizations),
                Does.Contain(Civilization.Water));
        }

        [Test]
        public void CopyLimit_CountsDifferentPrintingsOfTheSameCardNameTogether()
        {
            var printings = TcgCatalog.Printings.GroupBy(card => card.Name)
                .First(group => group.Count() > 1).Take(2).ToArray();
            var draft = new SavedDeck { Id = "custom", Name = "Reprints" };
            draft.SetCount(printings[0].Id, 3);
            draft.SetCount(printings[1].Id, 1);

            Assert.That(() => draft.SetCount(printings[1].Id, 2), Throws.ArgumentException);
            Assert.That(draft.CountOf(printings[1].Id), Is.EqualTo(1));
            draft.Entries[1].Count = 2;
            Assert.That(draft.ValidationError(), Does.Contain("four copies"));
        }

        [Test]
        public void Library_AllowsFiveAdditionalDecksAndDeletionFreesASlot()
        {
            var library = SavedDeckLibrary.CreateDefault();
            for (var i = 0; i < 5; i++) library = library.WithSavedDeck(CustomDeck(library, "custom-" + i));
            Assert.That(library.Decks.Count, Is.EqualTo(10));
            Assert.That(() => library.WithSavedDeck(CustomDeck(library, "sixth")),
                Throws.InvalidOperationException.With.Message.Contains("five"));

            var selected = library.WithSelectedDeck("custom-4");
            var deleted = selected.WithoutDeck("custom-4");
            var replacement = deleted.WithSavedDeck(CustomDeck(deleted, "replacement"));
            Assert.That(replacement.CustomDeckCount, Is.EqualTo(5));
            Assert.That(deleted.SelectedDeck.IsStarter, Is.True);
            Assert.That(selected.SelectedDeckId, Is.EqualTo("custom-4"));
            Assert.That(() => library.WithoutDeck(library.SelectedDeckId),
                Throws.InvalidOperationException.With.Message.Contains("not deleted"));
        }

        [Test]
        public void LibraryValidation_RejectsMissingSelectionAndStartersInsteadOfResettingThem()
        {
            var library = SavedDeckLibrary.CreateDefault();
            library.SelectedDeckId = "missing";
            Assert.That(() => library.Validate(), Throws.InvalidOperationException);
            library.SelectedDeckId = library.Decks[0].Id;
            library.Decks.RemoveAt(1);
            Assert.That(() => library.Validate(),
                Throws.InvalidOperationException.With.Message.Contains("five starter"));
        }

        [Test]
        public void Catalogue_ContainsTheFullPoolAndMovesCardsBackWhenTheirLastCopyIsRemoved()
        {
            var empty = new SavedDeck { Id = "empty", Name = "Empty" };
            Assert.That(DeckCatalogue.AvailableCards(empty, DeckCardSort.NameAscending).Count,
                Is.EqualTo(TcgCatalog.Printings.Count));
            var draft = SavedDeckLibrary.CreateDefault().SelectedDeck.Copy();
            var cardId = draft.Entries[0].CardId;
            Assert.That(DeckCatalogue.AvailableCards(draft, DeckCardSort.NameAscending)
                .Any(card => card.Id == cardId), Is.False);
            draft.SetCount(cardId, 0);
            Assert.That(DeckCatalogue.AvailableCards(draft, DeckCardSort.NameAscending)
                .Any(card => card.Id == cardId), Is.True);
        }

        [Test]
        public void Catalogue_FiltersByMaximumManaAndIncludesMatchingMulticoloredCards()
        {
            var draft = new SavedDeck { Id = "empty", Name = "Empty" };
            var cards = DeckCatalogue.AvailableCards(draft, DeckCardSort.ManaCost, 7, Civilization.Fire);
            Assert.That(cards.All(card => card.Cost <= 7 && card.Civilizations.Contains(Civilization.Fire)),
                Is.True);
            Assert.That(cards.Any(card => card.Name == "Bombazar, Dragon of Destiny"), Is.True);
            Assert.That(DeckCatalogue.AvailableCards(draft, DeckCardSort.NameAscending, 0), Is.Empty);
            Assert.That(() => DeckCatalogue.AvailableCards(draft, DeckCardSort.NameAscending, -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Catalogue_SupportsBothAlphabeticalDirectionsAndManaOrdering()
        {
            var draft = new SavedDeck { Id = "empty", Name = "Empty" };
            var ascending = DeckCatalogue.AvailableCards(draft, DeckCardSort.NameAscending, 5, Civilization.Light);
            var descending = DeckCatalogue.AvailableCards(draft, DeckCardSort.NameDescending, 5, Civilization.Light);
            var mana = DeckCatalogue.AvailableCards(draft, DeckCardSort.ManaCost, 5, Civilization.Light);

            Assert.That(ascending.Select(card => card.Name), Is.Ordered.Using<string>(StringComparer.OrdinalIgnoreCase));
            Assert.That(descending.Select(card => card.Name),
                Is.Ordered.Descending.Using<string>(StringComparer.OrdinalIgnoreCase));
            Assert.That(mana.Select(card => card.Cost), Is.Ordered);
            Assert.That(descending.Select(card => card.Name).Distinct(),
                Is.EqualTo(ascending.Select(card => card.Name).Distinct().Reverse()));
        }

        [Test]
        public void RichFilters_SelectExactCostsAndCombineIndependentCategories()
        {
            var filters = new DeckCatalogueFilters();
            filters.ManaCosts.UnionWith(new[] { 1, 3 });
            filters.Civilizations.UnionWith(new[] { Civilization.Fire, Civilization.Water });
            filters.CardTypes.Add(CardType.Creature);
            var cards = DeckCatalogue.AvailableCards(new SavedDeck { Id = "empty", Name = "Empty" },
                DeckCardSort.NameAscending, filters);

            Assert.That(cards, Is.Not.Empty);
            Assert.That(cards.Select(card => card.Cost).Distinct(), Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(cards.All(card => card.IsCreature &&
                card.Civilizations.Any(civilization => civilization == Civilization.Fire ||
                    civilization == Civilization.Water)), Is.True);
            Assert.That(cards.Any(card => card.Cost == 2), Is.False);
        }

        [Test]
        public void RichFilters_CivilizationsSupportMultipleValuesAndMulticoloredCards()
        {
            var filters = new DeckCatalogueFilters { MultiCivilization = true };
            var empty = new SavedDeck { Id = "empty", Name = "Empty" };
            var multicolored = DeckCatalogue.AvailableCards(empty, DeckCardSort.NameAscending, filters);
            Assert.That(multicolored, Is.Not.Empty);
            Assert.That(multicolored.All(card => card.Civilizations.Length > 1), Is.True);
            filters.Civilizations.Add(Civilization.Light);
            var expanded = DeckCatalogue.AvailableCards(empty, DeckCardSort.NameAscending, filters);
            Assert.That(expanded.Any(card => card.Civilizations.SequenceEqual(new[] { Civilization.Light })), Is.True);
            Assert.That(expanded.All(card => card.Civilizations.Contains(Civilization.Light) ||
                card.Civilizations.Length > 1), Is.True);
        }

        [Test]
        public void RichFilters_NormalIncludesSpellsAndCreatureTypeIncludesEvolutionCreatures()
        {
            var filters = new DeckCatalogueFilters();
            filters.CardTypes.Add(CardType.Spell);
            filters.ShieldBreakers.Add(1);
            var spells = DeckCatalogue.AvailableCards(new SavedDeck { Id = "empty", Name = "Empty" },
                DeckCardSort.NameAscending, filters);
            Assert.That(spells.Select(card => card.Id),
                Is.EquivalentTo(TcgCatalog.Printings.Where(card => card.Type == CardType.Spell)
                    .Select(card => card.Id)));

            filters.ShieldBreakers.Clear();
            filters.CardTypes.Clear();
            filters.CardTypes.Add(CardType.Creature);
            var creatures = DeckCatalogue.AvailableCards(new SavedDeck { Id = "empty", Name = "Empty" },
                DeckCardSort.NameAscending, filters);
            Assert.That(creatures.All(card => card.IsCreature), Is.True);
            Assert.That(creatures.Any(card => card.Type == CardType.EvolutionCreature), Is.True);
        }

        [Test]
        public void RichFilters_BreakerLevelsAreMultiselectAndExcludeSpellsForDoubleAndTriple()
        {
            var filters = new DeckCatalogueFilters();
            filters.ShieldBreakers.UnionWith(new[] { 2, 3 });
            var cards = DeckCatalogue.AvailableCards(new SavedDeck { Id = "empty", Name = "Empty" },
                DeckCardSort.NameAscending, filters);
            Assert.That(cards.All(card => card.IsCreature), Is.True);
            Assert.That(cards.Any(card => card.HasKeyword(CardKeyword.DoubleBreaker)), Is.True);
            Assert.That(cards.Any(card => card.HasKeyword(CardKeyword.TripleBreaker)), Is.True);
            Assert.That(filters.Matches(new CardDefinition { Type = CardType.Creature }), Is.False);
            Assert.That(filters.Matches(new CardDefinition { Type = CardType.Spell }), Is.False);
        }

        [Test]
        public void RichFilters_SpecialAbilitiesMatchAnyChoiceAndStealthIsCivilizationSpecific()
        {
            var filters = new DeckCatalogueFilters();
            filters.SpecialAbilities.Add(new DeckAbilityFilter(CardKeyword.SpeedAttacker));
            filters.SpecialAbilities.Add(new DeckAbilityFilter(CardKeyword.Blocker));
            var empty = new SavedDeck { Id = "empty", Name = "Empty" };
            var cards = DeckCatalogue.AvailableCards(empty, DeckCardSort.NameAscending, filters);
            Assert.That(cards.Any(card => card.HasKeyword(CardKeyword.SpeedAttacker) &&
                !card.HasKeyword(CardKeyword.Blocker)), Is.True);
            Assert.That(cards.Any(card => card.HasKeyword(CardKeyword.Blocker) &&
                !card.HasKeyword(CardKeyword.SpeedAttacker)), Is.True);
            filters.SpecialAbilities.Clear();
            filters.SpecialAbilities.Add(new DeckAbilityFilter(CardKeyword.Stealth, Civilization.Water));
            var stealth = DeckCatalogue.AvailableCards(empty, DeckCardSort.NameAscending, filters);
            Assert.That(stealth, Is.Not.Empty);
            Assert.That(stealth.All(card => card.HasStealthCivilization &&
                card.StealthCivilization == Civilization.Water), Is.True);
        }

        [Test]
        public void RichFilters_IncludeConditionalSelfKeywordsButNotKeywordsGivenToOtherCards()
        {
            var own = new CardDefinition { Type = CardType.Creature };
            own.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("conditional", AbilityTiming.Static, AbilityOperation.GrantKeyword)
                {
                    Keyword = CardKeyword.DoubleBreaker,
                    Selector = new RuleSelectorDefinition { Mode = RuleSelectionMode.SourceCard },
                    Condition = new AbilityConditionDefinition { RequiresWaveStriker = true }
                }
            };
            var filters = new DeckCatalogueFilters();
            filters.ShieldBreakers.Add(2);
            Assert.That(own.HasKeyword(CardKeyword.DoubleBreaker), Is.False);
            Assert.That(filters.Matches(own), Is.True);

            own.RuleAbilities[0].Keyword = CardKeyword.Blocker;
            filters.ShieldBreakers.Clear();
            filters.SpecialAbilities.Add(new DeckAbilityFilter(CardKeyword.Blocker));
            Assert.That(filters.Matches(own), Is.True);
            own.RuleAbilities[0].Selector.Mode = RuleSelectionMode.Choose;
            Assert.That(filters.Matches(own), Is.False);
        }

        [Test]
        public void RichFilters_CopySelectionsIndependentlyAndRejectOutOfRangeCosts()
        {
            var filters = new DeckCatalogueFilters { MultiCivilization = true };
            filters.ManaCosts.Add(1);
            filters.SpecialAbilities.Add(new DeckAbilityFilter(CardKeyword.Blocker));
            var copy = filters.Copy();
            copy.ManaCosts.Add(3);
            copy.MultiCivilization = false;
            copy.SpecialAbilities.Clear();
            Assert.That(filters.ManaCosts, Is.EquivalentTo(new[] { 1 }));
            Assert.That(filters.MultiCivilization, Is.True);
            Assert.That(filters.SpecialAbilities.Count, Is.EqualTo(1));
            copy.ManaCosts.Add(0);
            Assert.That(() => copy.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
            copy.ManaCosts.Remove(0);
            copy.ManaCosts.Add(DeckCatalogue.MaximumManaCost + 1);
            Assert.That(() => copy.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static CardDefinition UnusedCard(SavedDeck deck) =>
            TcgCatalog.Printings.First(card => deck.CountOfName(card.Id) == 0);

        private static SavedDeck CustomDeck(SavedDeckLibrary library, string id)
        {
            var deck = library.SelectedDeck.Copy();
            deck.Id = id;
            deck.Name = id;
            deck.IsStarter = false;
            return deck;
        }
    }
}
