using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardGame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Presentation
{
    public sealed partial class DuelPrototypeController
    {
        private enum DeckScreen { Home, Library, Editor, Filters }
        private enum DeckFilterCategory { ManaCost, CardType, Civilization, Shields, SpecialAbilities }
        private const int CataloguePageSize = 24;
        private SavedDeckLibrary _savedDecks;
        private SavedDeck _deckDraft;
        private DeckScreen _deckScreen;
        private string _deckSavePath;
        private bool _deckChanged;
        private string _deckNotice = string.Empty;
        private string _deckConfirmation;
        private Action _deckConfirmedAction;
        private CardDefinition _deckPreview;
        private IReadOnlyList<CardDefinition> _deckPreviewCards = Array.Empty<CardDefinition>();
        private int _deckPreviewIndex;
        private DeckCardSort _deckSort;
        private DeckCatalogueFilters _deckFilters = new DeckCatalogueFilters();
        private DeckCatalogueFilters _deckFilterDraft;
        private DeckFilterCategory _deckFilterCategory;
        private readonly float[] _deckFilterOffsets = { 1f, 1f, 1f, 1f, 1f };
        private int _deckCataloguePage;
        private float _deckLibraryOffset = 1f, _deckEditorOffset = 1f;
        private readonly HashSet<string> _deckArtIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void LoadSavedDecks()
        {
            try
            {
                if (string.IsNullOrEmpty(_deckSavePath))
                    _deckSavePath = Path.Combine(Application.persistentDataPath, "decks-v1.json");
                SavedDeckLibrary candidate;
                if (File.Exists(_deckSavePath))
                {
                    candidate = JsonUtility.FromJson<SavedDeckLibrary>(File.ReadAllText(_deckSavePath));
                    if (candidate == null) throw new InvalidOperationException("The saved deck file is empty.");
                    candidate.Validate();
                }
                else
                {
                    candidate = SavedDeckLibrary.CreateDefault();
                    WriteSavedDecks(candidate);
                }
                _savedDecks = candidate;
                _setupMessage = "Your saved duel deck: " + candidate.SelectedDeck.Name;
                _portraitDirty = true;
            }
            catch (Exception exception) when (IsDeckStorageError(exception))
            {
                _savedDecks = null;
                _setupMessage = "Could not load saved decks: " + exception.Message +
                    "\nExisting saved decks have not been reset.";
                Debug.LogError(_setupMessage);
                _portraitDirty = true;
            }
        }

        private static bool IsDeckStorageError(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException ||
            exception is ArgumentException || exception is InvalidOperationException ||
            exception is NotSupportedException;

        private void WriteSavedDecks(SavedDeckLibrary candidate)
        {
            candidate.Validate();
            WritePersistentJson(_deckSavePath, JsonUtility.ToJson(candidate, true));
        }

        private static void WritePersistentJson(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            // Replace only a complete file, leaving the last saved file intact on a failed write.
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }

        private bool PersistDeckChange(Func<SavedDeckLibrary> change, string message)
        {
            try
            {
                var candidate = change();
                WriteSavedDecks(candidate);
                _savedDecks = candidate;
                _setupMessage = "Your saved duel deck: " + candidate.SelectedDeck.Name;
                _deckNotice = message;
                _portraitDirty = true;
                return true;
            }
            catch (Exception exception) when (IsDeckStorageError(exception))
            {
                _deckNotice = "Could not save decks: " + exception.Message;
                Debug.LogError(_deckNotice);
                _portraitDirty = true;
                return false;
            }
        }

        private void OpenDeckLibrary()
        {
            ReturnToDeckSetup();
            _deckScreen = DeckScreen.Library;
            _portraitDirty = true;
        }

        private void ResetDeckScreen()
        {
            _deckScreen = DeckScreen.Home;
            _deckDraft = null;
            CloseDeckPreview();
            _deckConfirmation = null;
            _deckConfirmedAction = null;
            _deckChanged = false;
            _deckNotice = string.Empty;
            _deckFilterDraft = null;
            ReleaseDeckArt(Array.Empty<string>());
        }

        private void BeginDeckEdit(SavedDeck deck)
        {
            _deckDraft = deck.Copy();
            _deckScreen = DeckScreen.Editor;
            _deckChanged = false;
            _deckNotice = string.Empty;
            CloseDeckPreview();
            _deckSort = DeckCardSort.NameAscending;
            _deckFilters = new DeckCatalogueFilters();
            _deckFilterDraft = null;
            _deckFilterCategory = DeckFilterCategory.ManaCost;
            for (var i = 0; i < _deckFilterOffsets.Length; i++) _deckFilterOffsets[i] = 1f;
            _deckCataloguePage = 0;
            _deckEditorOffset = 1f;
            _portraitDirty = true;
        }

        private void CreateCustomDeck()
        {
            if (_savedDecks.CustomDeckCount >= SavedDeckLibrary.MaximumCustomDecks)
            {
                _deckNotice = "All five additional deck slots are in use.";
                _portraitDirty = true;
                return;
            }
            BeginDeckEdit(new SavedDeck
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Custom Deck " + (_savedDecks.CustomDeckCount + 1)
            });
        }

        private void SaveEditedDeck()
        {
            var error = _deckDraft.ValidationError();
            if (error != null)
            {
                _deckNotice = error;
                _portraitDirty = true;
                return;
            }
            var name = _deckDraft.Name.Trim();
            if (!PersistDeckChange(() => _savedDecks.WithSavedDeck(_deckDraft), "Saved " + name + ".")) return;
            _deckChanged = false;
            _deckDraft = null;
            CloseDeckPreview();
            _deckScreen = DeckScreen.Library;
            ReleaseDeckArt(Array.Empty<string>());
        }

        private void SelectDuelDeck(string deckId) =>
            PersistDeckChange(() => _savedDecks.WithSelectedDeck(deckId), "Selected deck saved for duels.");

        private void CloseDeckEditor()
        {
            if (!_deckChanged) { OpenDeckLibrary(); return; }
            ConfirmDeckAction("Discard unsaved changes?", OpenDeckLibrary);
        }

        private void ConfirmDeckAction(string message, Action confirmed)
        {
            _deckConfirmation = message;
            _deckConfirmedAction = confirmed;
            _portraitDirty = true;
        }

        private void DeleteEditedDeck()
        {
            var deckId = _deckDraft.Id;
            var name = _deckDraft.Name;
            ConfirmDeckAction("Delete " + name + "?", () =>
            {
                if (!PersistDeckChange(() => _savedDecks.WithoutDeck(deckId), "Deleted " + name + ".")) return;
                _deckDraft = null;
                CloseDeckPreview();
                _deckChanged = false;
                _deckScreen = DeckScreen.Library;
                ReleaseDeckArt(Array.Empty<string>());
            });
        }

        private void ChangeEditedCardCount(CardDefinition card, int difference)
        {
            try
            {
                _deckDraft.SetCount(card.Id, _deckDraft.CountOf(card.Id) + difference);
                _deckChanged = true;
                _deckNotice = string.Empty;
                _portraitDirty = true;
            }
            catch (ArgumentException exception)
            {
                _deckNotice = exception.Message;
                _portraitDirty = true;
            }
        }

        private void OpenDeckFilters()
        {
            CloseDeckPreview();
            _deckFilterDraft = _deckFilters.Copy();
            _deckScreen = DeckScreen.Filters;
            ReleaseDeckArt(Array.Empty<string>());
            _portraitDirty = true;
        }

        private void CloseDeckFilters(bool apply)
        {
            if (_deckFilterDraft == null) return;
            if (apply)
            {
                _deckFilters = _deckFilterDraft.Copy();
                _deckCataloguePage = 0;
            }
            _deckFilterDraft = null;
            _deckScreen = DeckScreen.Editor;
            _portraitDirty = true;
        }

        private void OpenDeckPreview(CardDefinition card, bool inDeck)
        {
            // Retain the opening order while count edits move cards between the grids.
            var cards = inDeck
                ? _deckDraft.Entries.Select(entry => TcgCatalog.ById[entry.CardId])
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()
                : DeckCatalogue.AvailableCards(_deckDraft, _deckSort, _deckFilters).ToArray();
            var index = Array.FindIndex(cards, item =>
                string.Equals(item.Id, card.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                _deckNotice = "This card is no longer in the selected card list. Open it from its current grid.";
                _portraitDirty = true;
                return;
            }
            _deckPreviewCards = cards;
            _deckPreviewIndex = index;
            _deckPreview = cards[index];
            _portraitDirty = true;
        }

        private void NavigateDeckPreview(int direction)
        {
            if (_deckPreviewCards.Count < 2) return;
            _deckPreviewIndex = (_deckPreviewIndex + direction + _deckPreviewCards.Count) % _deckPreviewCards.Count;
            _deckPreview = _deckPreviewCards[_deckPreviewIndex];
            _portraitDirty = true;
        }

        private void CloseDeckPreview()
        {
            _deckPreview = null;
            _deckPreviewCards = Array.Empty<CardDefinition>();
            _deckPreviewIndex = 0;
            _portraitDirty = true;
        }

        private void PDeckScreen()
        {
            if (_savedDecks == null) { PDeckHome(); return; }
            switch (_deckScreen)
            {
                case DeckScreen.Library:
                    PDeckLibrary();
                    break;
                case DeckScreen.Filters:
                    PDeckFilters();
                    break;
                case DeckScreen.Editor:
                    var available = DeckCatalogue.AvailableCards(_deckDraft, _deckSort, _deckFilters);
                    var pageCount = Math.Max(1, (available.Count + CataloguePageSize - 1) / CataloguePageSize);
                    _deckCataloguePage = Mathf.Clamp(_deckCataloguePage, 0, pageCount - 1);
                    var page = available.Skip(_deckCataloguePage * CataloguePageSize).Take(CataloguePageSize).ToArray();
                    var included = _deckDraft.Entries.Select(entry => TcgCatalog.ById[entry.CardId])
                        .OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                    LoadDeckArt(included.Concat(page).Concat(_deckPreview == null
                        ? Array.Empty<CardDefinition>() : new[] { _deckPreview }));
                    PDeckEditor(included, page, available.Count, pageCount);
                    if (_deckPreview != null) PDeckCardPreview();
                    break;
                default:
                    PDeckHome();
                    break;
            }
            if (_deckConfirmation != null) PDeckConfirmation();
        }

        private void PDeckHome()
        {
            PLabel(_portraitBoard, new Rect(20, _ph * .16f, _pw - 40, 72), "DUEL MASTERS", 38, PGold);
            PLabel(_portraitBoard, new Rect(30, _ph * .27f, _pw - 60, 72),
                _savedDecks == null ? "Saved decks unavailable" : "Selected deck: " + _savedDecks.SelectedDeck.Name,
                24, Color.white);
            PButton(_portraitBoard, new Rect(40, _ph * .41f, _pw - 80, 80), "NEW GAME",
                () => { PClosePreview(); StartSelectedMatch(); }, _savedDecks != null, PGold);
            PButton(_portraitBoard, new Rect(40, _ph * .41f + 100, _pw - 80, 80), "CUSTOMIZE DECK",
                OpenDeckLibrary, _savedDecks != null);
            PButton(_portraitBoard, new Rect(40, _ph * .41f + 200, _pw - 80, 80), "TUTORIAL", StartTutorial);
            PLabel(_portraitBoard, new Rect(30, _ph * .41f + 300, _pw - 60, 112),
                _setupMessage, 20, _savedDecks == null ? new Color(1f, .40f, .40f) : new Color(.72f, .83f, .86f));
            if (_savedDecks == null)
                PButton(_portraitBoard, new Rect(40, _ph - 90, _pw - 80, 62), "RETRY LOADING DECKS", LoadSavedDecks);
        }

        private void PDeckLibrary()
        {
            PButton(_portraitBoard, new Rect(20, 16, 96, 50), "BACK", ReturnToDeckSetup);
            PLabel(_portraitBoard, new Rect(126, 16, _pw - 146, 50), "CUSTOMIZE DECK", 30, PGold);
            PLabel(_portraitBoard, new Rect(20, 78, _pw - 40, 76),
                "Five editable starters + five additional decks\nDuel deck: " + _savedDecks.SelectedDeck.Name,
                21, Color.white);
            var scroll = PScroll(_portraitBoard, new Rect(20, 164, _pw - 40, _ph - 332),
                false, _savedDecks.Decks.Count * 142f + 10);
            for (var i = 0; i < _savedDecks.Decks.Count; i++)
            {
                var deck = _savedDecks.Decks[i];
                var row = PBox(scroll.content, deck.Name + " deck", new Rect(0, i * 142, _pw - 40, 130), PPanel);
                var civilizations = deck.Build().SelectMany(card => card.Civilizations).Distinct()
                    .OrderBy(civilization => civilization).ToArray();
                PLabel(row.transform, new Rect(12, 6, _pw - 64, 44), deck.Name, 25, PGold);
                PLabel(row.transform, new Rect(12, 50, _pw - 64, 30),
                    (deck.IsStarter ? "Starter" : "Custom") + " · 40 cards · " +
                    string.Join(", ", civilizations), 17, new Color(.72f, .83f, .86f));
                var selected = _savedDecks.SelectedDeckId == deck.Id;
                PButton(row.transform, new Rect(12, 86, (_pw - 76) * .55f, 36),
                    selected ? "SELECTED FOR DUEL" : "USE FOR DUEL", () => SelectDuelDeck(deck.Id),
                    !selected, selected ? new Color(.18f, .38f, .31f) : (Color?)null)
                    .GetComponentInChildren<Text>().fontSize = 18;
                PButton(row.transform, new Rect(24 + (_pw - 76) * .55f, 86, (_pw - 76) * .45f, 36),
                    "EDIT", () => BeginDeckEdit(deck)).GetComponentInChildren<Text>().fontSize = 18;
            }
            scroll.verticalNormalizedPosition = _deckLibraryOffset;
            scroll.onValueChanged.AddListener(value => _deckLibraryOffset = value.y);
            var footer = PBox(_portraitBoard, "Deck library footer", new Rect(8, _ph - 156, _pw - 16, 148), PPanel);
            PLabel(footer.transform, new Rect(12, 6, _pw - 40, 58), _deckNotice, 18, Color.white);
            PButton(footer.transform, new Rect(12, 76, _pw - 40, 58),
                "CREATE DECK · " + _savedDecks.CustomDeckCount + "/5",
                CreateCustomDeck, _savedDecks.CustomDeckCount < SavedDeckLibrary.MaximumCustomDecks, PGold);
        }

        private void PDeckEditor(IReadOnlyList<CardDefinition> included, IReadOnlyList<CardDefinition> page,
            int availableCount, int pageCount)
        {
            PButton(_portraitBoard, new Rect(20, 16, 96, 48), "BACK", CloseDeckEditor);
            PLabel(_portraitBoard, new Rect(124, 16, _pw - 300, 48), "EDIT DECK", 28, PGold);
            var count = _deckDraft.CardCount;
            var countLabel = PLabel(_portraitBoard, new Rect(_pw - 166, 16, 146, 48), count + " / 40", 30,
                count == SavedDeckLibrary.DeckSize ? PGold : new Color(1f, .26f, .28f));
            countLabel.gameObject.name = "Deck total";
            PDeckInput(_portraitBoard, "Deck name", new Rect(20, 76, _pw - 40, 48),
                _deckDraft.Name, "Deck name", 50,
                text => { _deckDraft.Name = text; _deckChanged = true; }, _ => _portraitDirty = true);

            var sortWidth = (_pw - 40) / 4;
            var sorts = new[] { DeckCardSort.NameAscending, DeckCardSort.NameDescending, DeckCardSort.ManaCost };
            var sortNames = new[] { "A TO Z", "Z TO A", "MANA COST" };
            for (var i = 0; i < sorts.Length; i++)
            {
                var sort = sorts[i];
                PButton(_portraitBoard, new Rect(20 + i * sortWidth, 136, sortWidth - 6, 44),
                    sortNames[i], () => { _deckSort = sort; _deckCataloguePage = 0; }, true,
                    _deckSort == sort ? new Color(.18f, .38f, .31f) : PPanel)
                    .GetComponentInChildren<Text>().fontSize = 17;
            }
            var filters = PButton(_portraitBoard, new Rect(20 + 3 * sortWidth, 136, sortWidth - 6, 44),
                "FILTERS" + (_deckFilters.SelectionCount > 0 ? " (" + _deckFilters.SelectionCount + ")" : ""),
                OpenDeckFilters, true, _deckFilters.SelectionCount > 0 ? new Color(.18f, .38f, .31f) : PPanel);
            filters.gameObject.name = "FILTERS";
            filters.GetComponentInChildren<Text>().fontSize = 17;
            PLabel(_portraitBoard, new Rect(20, 186, _pw - 40, 28),
                _deckFilters.SelectionCount == 0 ? "Catalogue: all costs, types, civilizations and abilities" :
                    "Catalogue: " + _deckFilters.SelectionCount + " filter values selected",
                16, new Color(.72f, .83f, .86f));

            var scroll = PScroll(_portraitBoard, new Rect(20, 220, _pw - 40, _ph - 388), false, 1);
            scroll.gameObject.name = "Deck editor scroll";
            var width = _pw - 40;
            var y = 0f;
            PLabel(scroll.content, new Rect(0, y, width, 38), "CARDS IN DECK · " + count, 23, PGold);
            y += 48;
            if (included.Count == 0)
            {
                PLabel(scroll.content, new Rect(0, y, width, 72),
                    "Your deck is empty. Tap catalogue cards below, then use + to add copies.", 21, Color.white);
                y += 84;
            }
            else y = PDeckGrid(scroll.content, included, y, true);
            PLabel(scroll.content, new Rect(0, y, width, 54),
                "CATALOGUE · " + availableCount + " cards not in deck\nFilters and sorting apply to this section.",
                20, PCyan);
            y += 64;
            PButton(scroll.content, new Rect(0, y, 112, 44), "PREVIOUS",
                () => _deckCataloguePage--, _deckCataloguePage > 0).GetComponentInChildren<Text>().fontSize = 16;
            PLabel(scroll.content, new Rect(122, y, width - 244, 44),
                "PAGE " + (_deckCataloguePage + 1) + " / " + pageCount, 19, Color.white);
            PButton(scroll.content, new Rect(width - 112, y, 112, 44), "NEXT",
                () => _deckCataloguePage++, _deckCataloguePage + 1 < pageCount)
                .GetComponentInChildren<Text>().fontSize = 16;
            y += 56;
            if (page.Count == 0)
            {
                PLabel(scroll.content, new Rect(0, y, width, 90),
                    "No catalogue cards match these filters.", 21, Color.white);
                y += 100;
            }
            else y = PDeckGrid(scroll.content, page, y, false);
            scroll.content.sizeDelta = new Vector2(width, Mathf.Max(scroll.viewport.rect.height, y + 12));
            scroll.verticalNormalizedPosition = _deckEditorOffset;
            scroll.onValueChanged.AddListener(value => _deckEditorOffset = value.y);

            var footer = PBox(_portraitBoard, "Deck editor footer", new Rect(8, _ph - 156, _pw - 16, 148), PPanel);
            var validation = _deckDraft.ValidationError();
            PLabel(footer.transform, new Rect(12, 6, _pw - 40, 62),
                _deckNotice.Length > 0 ? _deckNotice : validation ?? "40 cards ready to save. Maximum four copies per name.",
                18, validation == null ? Color.white : new Color(1f, .42f, .42f));
            var canDelete = !_deckDraft.IsStarter && _savedDecks.Decks.Any(deck => deck.Id == _deckDraft.Id);
            var saveWidth = canDelete ? (_pw - 52) * .68f : _pw - 40;
            PButton(footer.transform, new Rect(12, 78, saveWidth, 56), "SAVE DECK",
                SaveEditedDeck, validation == null, PGold);
            if (canDelete)
                PButton(footer.transform, new Rect(24 + saveWidth, 78, (_pw - 52) * .32f, 56),
                    "DELETE", DeleteEditedDeck).GetComponentInChildren<Text>().fontSize = 20;
        }

        private int DeckFilterSelectionCount(DeckFilterCategory category)
        {
            switch (category)
            {
                case DeckFilterCategory.ManaCost: return _deckFilterDraft.ManaCosts.Count;
                case DeckFilterCategory.CardType: return _deckFilterDraft.CardTypes.Count;
                case DeckFilterCategory.Civilization:
                    return _deckFilterDraft.Civilizations.Count + (_deckFilterDraft.MultiCivilization ? 1 : 0);
                case DeckFilterCategory.Shields: return _deckFilterDraft.ShieldBreakers.Count;
                default: return _deckFilterDraft.SpecialAbilities.Count;
            }
        }

        private static void ToggleDeckFilter<T>(HashSet<T> selection, T value)
        {
            if (!selection.Remove(value)) selection.Add(value);
        }

        private void PDeckFilters()
        {
            var back = PButton(_portraitBoard, new Rect(20, 16, 96, 50), "BACK",
                () => CloseDeckFilters(false));
            back.gameObject.name = "CANCEL FILTERS";
            PLabel(_portraitBoard, new Rect(126, 16, _pw - 146, 50), "FILTER CATALOGUE", 28, PGold);
            var matches = DeckCatalogue.AvailableCards(_deckDraft, _deckSort, _deckFilterDraft).Count;
            PLabel(_portraitBoard, new Rect(20, 78, _pw - 40, 48),
                matches + " catalogue cards match · " + _deckFilterDraft.SelectionCount + " selections",
                22, Color.white);
            var categoryWidth = Mathf.Min(208, (_pw - 40) * .31f);
            var titles = new[] { "Mana cost", "Card type", "Civilization", "Shields", "Special abilities" };
            for (var i = 0; i < titles.Length; i++)
            {
                var category = (DeckFilterCategory)i;
                var button = PButton(_portraitBoard, new Rect(20, 142 + i * 90, categoryWidth, 78),
                    titles[i] + "\n" + DeckFilterSelectionCount(category) + " selected",
                    () => _deckFilterCategory = category, true,
                    category == _deckFilterCategory ? new Color(.18f, .38f, .31f) : PPanel);
                button.gameObject.name = titles[i];
                button.GetComponentInChildren<Text>().fontSize = 21;
            }

            var valuesWidth = _pw - categoryWidth - 56;
            var values = PScroll(_portraitBoard, new Rect(categoryWidth + 36, 142, valuesWidth, _ph - 310),
                false, 1);
            values.gameObject.name = "Filter values scroll";
            var y = 0f;
            switch (_deckFilterCategory)
            {
                case DeckFilterCategory.ManaCost:
                    for (var cost = 1; cost <= DeckCatalogue.MaximumManaCost; cost++)
                    {
                        var exactCost = cost;
                        PDeckFilterValue(values.content, valuesWidth, ref y, "Mana " + cost, cost + " mana",
                            _deckFilterDraft.ManaCosts.Contains(cost),
                            () => ToggleDeckFilter(_deckFilterDraft.ManaCosts, exactCost));
                    }
                    break;
                case DeckFilterCategory.CardType:
                    foreach (var type in new[] { CardType.Creature, CardType.Spell })
                        PDeckFilterValue(values.content, valuesWidth, ref y, type.ToString(), type.ToString(),
                            _deckFilterDraft.CardTypes.Contains(type),
                            () => ToggleDeckFilter(_deckFilterDraft.CardTypes, type));
                    break;
                case DeckFilterCategory.Civilization:
                    foreach (var civilization in SelectableCivilizations)
                        PDeckFilterValue(values.content, valuesWidth, ref y, civilization.ToString(),
                            civilization.ToString(), _deckFilterDraft.Civilizations.Contains(civilization),
                            () => ToggleDeckFilter(_deckFilterDraft.Civilizations, civilization));
                    PDeckFilterValue(values.content, valuesWidth, ref y, "Multi-civilization", "Multi-civilization",
                        _deckFilterDraft.MultiCivilization,
                        () => _deckFilterDraft.MultiCivilization = !_deckFilterDraft.MultiCivilization);
                    break;
                case DeckFilterCategory.Shields:
                    var breakers = new[] { "Normal", "Double Breaker", "Triple Breaker" };
                    for (var i = 0; i < breakers.Length; i++)
                    {
                        var count = i + 1;
                        PDeckFilterValue(values.content, valuesWidth, ref y, breakers[i],
                            i == 0 ? "Normal (includes spells)" : breakers[i],
                            _deckFilterDraft.ShieldBreakers.Contains(count),
                            () => ToggleDeckFilter(_deckFilterDraft.ShieldBreakers, count));
                    }
                    break;
                default:
                    foreach (var ability in DeckCatalogue.AbilityOptions)
                        PDeckFilterValue(values.content, valuesWidth, ref y, ability.Name, ability.Name,
                            _deckFilterDraft.SpecialAbilities.Contains(ability),
                            () => ToggleDeckFilter(_deckFilterDraft.SpecialAbilities, ability));
                    break;
            }
            values.content.sizeDelta = new Vector2(valuesWidth, Mathf.Max(values.viewport.rect.height, y));
            var categoryIndex = (int)_deckFilterCategory;
            values.verticalNormalizedPosition = _deckFilterOffsets[categoryIndex];
            values.onValueChanged.AddListener(value => _deckFilterOffsets[categoryIndex] = value.y);

            var footer = PBox(_portraitBoard, "Filter footer", new Rect(8, _ph - 156, _pw - 16, 148), PPanel);
            PLabel(footer.transform, new Rect(12, 6, _pw - 40, 58),
                "Match any selected value within a category.\nAll active categories must match.",
                18, Color.white);
            var clearWidth = (_pw - 52) * .35f;
            PButton(footer.transform, new Rect(12, 76, clearWidth, 58), "CLEAR ALL",
                () => _deckFilterDraft = new DeckCatalogueFilters()).GetComponentInChildren<Text>().fontSize = 20;
            PButton(footer.transform, new Rect(24 + clearWidth, 76, (_pw - 52) * .65f, 58), "APPLY FILTERS",
                () => CloseDeckFilters(true), true, PGold);
        }

        private void PDeckFilterValue(Transform parent, float width, ref float y, string name, string label,
            bool selected, Action toggle)
        {
            var button = PButton(parent, new Rect(0, y, width, 64), (selected ? "[x] " : "[ ] ") + label,
                toggle, true, selected ? new Color(.18f, .38f, .31f) : PPanel);
            button.gameObject.name = name;
            button.GetComponentInChildren<Text>().fontSize = 22;
            y += 74;
        }

        private float PDeckGrid(Transform parent, IReadOnlyList<CardDefinition> cards, float y, bool inDeck)
        {
            var width = _pw - 40;
            var columns = _pw >= 600 ? 4 : 3;
            var cardWidth = (width - (columns - 1) * 12) / columns;
            var cardHeight = cardWidth * (682f / 491f);
            var rowHeight = cardHeight + 58;
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var x = i % columns * (cardWidth + 12);
                var top = y + i / columns * rowHeight;
                var rect = PCard(parent, new CardInstance { Definition = card },
                    new Rect(x, top, cardWidth, cardHeight));
                rect.gameObject.name = (inDeck ? "Deck card " : "Catalogue card ") + card.Id;
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = rect.GetComponent<Image>();
                button.onClick.AddListener(() => OpenDeckPreview(card, inDeck));
                var copies = _deckDraft.CountOf(card.Id);
                if (copies > 1)
                {
                    var badge = PLabel(rect, new Rect(cardWidth - 38, 3, 35, 32),
                        copies.ToString(), 20, Color.white, true);
                    badge.gameObject.name = "Deck copies " + card.Id;
                }
                PLabel(parent, new Rect(x, top + cardHeight + 4, cardWidth, 44),
                    card.Name + "\n" + card.SetCode, 14, Color.white);
            }
            return y + ((cards.Count + columns - 1) / columns) * rowHeight;
        }

        private void PDeckCardPreview()
        {
            var card = _deckPreview;
            var panel = PModal(card.Name, .01f, .99f, false);
            var arrowWidth = Mathf.Clamp(panel.rect.width * .10f, 44, 64);
            var availableWidth = panel.rect.width - 20;
            var cardHeight = Mathf.Max(80, Mathf.Min(panel.rect.height - 272,
                availableWidth * (682f / 491f)));
            var cardWidth = cardHeight * (491f / 682f);
            var cardX = (panel.rect.width - cardWidth) * .5f;
            var art = PCard(panel, new CardInstance { Definition = card },
                new Rect(cardX, 90, cardWidth, cardHeight),
                showManaCost: false);
            art.gameObject.name = "Deck preview card";
            var pointer = art.gameObject.AddComponent<PortraitPointer>();
            pointer.LogicalScale = _portraitCanvas.scaleFactor;
            var press = Vector2.zero;
            pointer.Press = position => press = position;
            pointer.Release = position =>
            {
                var movement = (position - press) / _portraitCanvas.scaleFactor;
                if (Mathf.Abs(movement.x) > 42 && Mathf.Abs(movement.x) > Mathf.Abs(movement.y))
                    NavigateDeckPreview(movement.x < 0 ? 1 : -1);
            };
            var arrowY = 90 + (cardHeight - 72) * .5f;
            PButton(panel, new Rect(cardX + 2, arrowY, arrowWidth, 72), "‹",
                () => NavigateDeckPreview(-1), _deckPreviewCards.Count > 1, new Color(.055f, .115f, .155f, .68f))
                .GetComponentInChildren<Text>().fontSize = 42;
            PButton(panel, new Rect(cardX + cardWidth - arrowWidth - 2, arrowY, arrowWidth, 72), "›",
                () => NavigateDeckPreview(1), _deckPreviewCards.Count > 1, new Color(.055f, .115f, .155f, .68f))
                .GetComponentInChildren<Text>().fontSize = 42;
            PLabel(panel, new Rect(20, 64, panel.rect.width - 40, 22),
                "CARD " + (_deckPreviewIndex + 1) + " / " + _deckPreviewCards.Count, 17, PCyan);
            var y = 98 + cardHeight;
            PLabel(panel, new Rect(20, y, panel.rect.width - 40, 28),
                "MANA " + card.Cost + " · " + string.Join(", ", card.Civilizations) + " · " + card.Type,
                18, Color.white);
            y += 34;
            var copies = _deckDraft.CountOf(card.Id);
            var copiesByName = _deckDraft.CountOfName(card.Id);
            PButton(panel, new Rect(28, y, 96, 62), "-", () => ChangeEditedCardCount(card, -1), copies > 0);
            PLabel(panel, new Rect(136, y, panel.rect.width - 272, 62),
                copies + " IN DECK", 27, PGold);
            PButton(panel, new Rect(panel.rect.width - 124, y, 96, 62), "+",
                () => ChangeEditedCardCount(card, 1), copiesByName < SavedDeckLibrary.MaximumCopies);
            PLabel(panel, new Rect(20, y + 68, panel.rect.width - 40, 24),
                copiesByName == SavedDeckLibrary.MaximumCopies
                    ? "Four-copy limit reached across this card's printings."
                    : "Maximum four copies per card name.", 18, Color.white);
            var total = PLabel(panel, new Rect(20, y + 98, panel.rect.width - 40, 34),
                "Deck total: " + _deckDraft.CardCount + " / 40", 21,
                _deckDraft.CardCount == SavedDeckLibrary.DeckSize ? PGold : new Color(1f, .26f, .28f));
            total.gameObject.name = "Preview deck total";
            var close = PButton(panel, new Rect(panel.rect.width - 68, 8, 56, 48),
                "X", CloseDeckPreview);
            close.gameObject.name = "CLOSE CARD";
        }

        private void PDeckConfirmation()
        {
            var panel = PModal(_deckConfirmation, .30f, .70f, false);
            var width = (panel.rect.width - 60) * .5f;
            PButton(panel, new Rect(20, panel.rect.height - 88, width, 62), "CANCEL",
                () => { _deckConfirmation = null; _deckConfirmedAction = null; });
            PButton(panel, new Rect(40 + width, panel.rect.height - 88, width, 62), "CONFIRM", () =>
            {
                var action = _deckConfirmedAction;
                _deckConfirmation = null;
                _deckConfirmedAction = null;
                action();
            }, true, PGold);
        }

        private void PDeckInput(Transform parent, string name, Rect area, string value, string hint,
            int characterLimit, Action<string> changed, Action<string> committed)
        {
            var box = PBox(parent, name, area, new Color(.10f, .21f, .26f));
            var field = box.gameObject.AddComponent<InputField>();
            field.targetGraphic = box;
            field.textComponent = PLabel(box.transform, new Rect(8, 2, area.width - 16, area.height - 4),
                value, 24, Color.white);
            field.placeholder = PLabel(box.transform, new Rect(8, 2, area.width - 16, area.height - 4),
                hint, 24, new Color(.55f, .65f, .70f));
            field.characterLimit = characterLimit;
            field.contentType = InputField.ContentType.Standard;
            field.lineType = InputField.LineType.SingleLine;
            field.caretColor = PGold;
            field.text = value;
            if (changed != null) field.onValueChanged.AddListener(text => changed(text));
            if (committed != null) field.onEndEdit.AddListener(text => committed(text));
        }

        private void LoadDeckArt(IEnumerable<CardDefinition> cards)
        {
            var needed = cards.GroupBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToArray();
            ReleaseDeckArt(needed.Select(card => card.Id));
            foreach (var card in needed)
            {
                if (_cardArt.ContainsKey(card.Id)) continue;
                var art = Resources.Load<Texture2D>($"CardGame/CardArt/{card.DatabaseMediaId:0000}");
                if (art == null) continue;
                _cardArt[card.Id] = art;
                _deckArtIds.Add(card.Id);
            }
        }

        private void ReleaseDeckArt(IEnumerable<string> retainedIds)
        {
            var retained = new HashSet<string>(retainedIds, StringComparer.OrdinalIgnoreCase);
            foreach (var id in _deckArtIds.Where(id => !retained.Contains(id)).ToArray())
            {
                if (_cardArt.TryGetValue(id, out var texture))
                {
                    _cardArt.Remove(id);
                    if (texture != null && !_cardArt.Values.Contains(texture)) Resources.UnloadAsset(texture);
                }
                _deckArtIds.Remove(id);
            }
        }
    }
}
