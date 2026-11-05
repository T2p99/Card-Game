using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CardGame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CardGame.Presentation.Tests
{
    // The default Assembly-CSharp cannot be referenced by an asmdef. Resolve
    // the presentation component at runtime; these tests run in Unity Play Mode.
    public sealed class PortraitGestureTests
    {
        private MonoBehaviour _controller;
        private GameState _initial;
        private string _deckSavePath;
        private string _duelLogDirectory;
        private Type ControllerType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("CardGame.Presentation.DuelPrototypeController"))
            .First(t => t != null);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            foreach (var instance in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(c => c != null && c.GetType() == ControllerType)) Object.Destroy(instance.gameObject);
            yield return null;
            _deckSavePath = Path.Combine(Application.temporaryCachePath, "deck-ui-test-" +
                Guid.NewGuid().ToString("N") + ".json");
            _duelLogDirectory = Path.Combine(Application.temporaryCachePath, "duel-log-ui-test-" +
                Guid.NewGuid().ToString("N"));
            CreateController();
            _initial = new GameState { ActivePlayerId = 0, Phase = GamePhase.Main, TurnNumber = 5,
                Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } } };
            _initial.Players[0].BattleZone.Add(Card(1, "attacker"));
            var target = Card(2, "target"); target.Tapped = true; _initial.Players[1].BattleZone.Add(target);
            for (var i = 0; i < 3; i++)
            {
                _initial.Players[0].Hand.Add(Card(10 + i, "hand " + i));
                _initial.Players[1].Shields.Add(Card(20 + i, "secret shield " + i));
            }
            Set("_state", _initial); Set("_engine", new RulesEngine()); Set("_showDeckSetup", false);
            Set("_animationsEnabled", false);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_controller != null) Object.Destroy(_controller.gameObject);
            yield return null;
            if (File.Exists(_deckSavePath)) File.Delete(_deckSavePath);
            if (File.Exists(_deckSavePath + ".tmp")) File.Delete(_deckSavePath + ".tmp");
            if (Directory.Exists(_duelLogDirectory))
            {
                foreach (var path in Directory.GetFiles(_duelLogDirectory)) File.Delete(path);
                Directory.Delete(_duelLogDirectory);
            }
        }

        [UnityTest]
        public IEnumerator PortraitBuild_HasCanvasSafeAreaAndNoHiddenShieldNames()
        {
            Assert.That(Screen.orientation, Is.EqualTo(ScreenOrientation.Portrait));
            Assert.That(_controller.GetComponentInChildren<Canvas>(), Is.Not.Null);
            Assert.That(_controller.GetComponentsInChildren<Component>().Where(c => c.GetType().Name == "Text")
                .Any(c => ((string)c.GetType().GetProperty("text").GetValue(c)).Contains("secret shield")), Is.False);
            Assert.That(_controller.GetComponentsInChildren<RectTransform>().Any(t => t.name == "Safe area"), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EndTurn_IsCenteredOnRightOverlayAndRespectsInteractionGuards()
        {
            var frame = (RectTransform)Get("_portraitFrame");
            var turn = Find("END TURN");
            Assert.That(turn.parent, Is.SameAs(Get("_portraitOverlay")));
            Assert.That(Center(turn).y, Is.EqualTo(Center(frame).y).Within(1f));
            Assert.That(frame.InverseTransformPoint(turn.TransformPoint(turn.rect.center)).x,
                Is.GreaterThan(frame.rect.width * .35f));
            Assert.That(Find("End turn icon").IsChildOf(turn), Is.True);
            Assert.That(Find("End turn icon").GetComponent<CanvasRenderer>(), Is.Not.Null);
            var deck = _controller.GetComponentsInChildren<RectTransform>()
                .Where(r => r.name == "DECK pile").OrderBy(r => Center(r).y).First();
            Assert.That(ScreenRect(turn).Overlaps(ScreenRect(deck)), Is.False);
            var battleViewport = (RectTransform)Find("attacker").parent.parent.parent;
            Assert.That(ScreenRect(turn).Overlaps(ScreenRect(battleViewport)), Is.False);
            var button = turn.GetComponents<Component>().Single(c => c.GetType().Name == "Button");
            Assert.That((bool)button.GetType().GetProperty("interactable").GetValue(button), Is.True);

            Set("_busy", true); Set("_portraitDirty", true);
            yield return null;
            button = Find("END TURN").GetComponents<Component>().Single(c => c.GetType().Name == "Button");
            Assert.That((bool)button.GetType().GetProperty("interactable").GetValue(button), Is.False);
            Set("_busy", false); State.ActivePlayerId = 1; Set("_portraitDirty", true);
            yield return null;
            button = Find("END TURN").GetComponents<Component>().Single(c => c.GetType().Name == "Button");
            Assert.That((bool)button.GetType().GetProperty("interactable").GetValue(button), Is.False);

            State.ActivePlayerId = 0;
            ControllerType.GetMethod("POpenPreview", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_controller, new object[] { State.Players[0].Hand, 0, "Your hand" });
            yield return null;
            Assert.That(_controller.GetComponentsInChildren<RectTransform>().Any(r => r.name == "END TURN"), Is.False);
        }

        [UnityTest]
        public IEnumerator Hands_AreCenteredForSingleSmallAndCrowdedHands()
        {
            var frame = (RectTransform)Get("_portraitFrame");
            foreach (var count in new[] { 1, 3, 9, 15 })
            {
                for (var owner = 0; owner < 2; owner++)
                {
                    State.Players[owner].Hand.Clear();
                    for (var i = 0; i < count; i++)
                        State.Players[owner].Hand.Add(Card(100 + owner * 20 + i, "hand " + owner + " " + i));
                }
                Set("_portraitDirty", true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                var hand = ((Dictionary<long, RectTransform>)Get("_portraitHandCards")).Values.ToArray();
                var opponentHand = _controller.GetComponentsInChildren<RectTransform>()
                    .Where(r => r.name == "Hidden card" && r.parent == (Transform)Get("_portraitBoard")).ToArray();
                foreach (var cards in new[] { hand, opponentHand })
                {
                    var bounds = cards.Select(ScreenRect).ToArray();
                    Assert.That((bounds.Min(r => r.xMin) + bounds.Max(r => r.xMax)) * .5f,
                        Is.EqualTo(Center(frame).x).Within(1f), "Hand size " + count);
                }
            }
        }

        [UnityTest]
        public IEnumerator Labels_AreCenteredAcrossBoardSetupPreviewAndHelp()
        {
            AssertCenteredLabels();
            Set("_showDeckSetup", true); Set("_portraitDirty", true);
            yield return null;
            AssertCenteredLabels();
            Set("_showDeckSetup", false);
            ControllerType.GetMethod("POpenPreview", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_controller, new object[] { State.Players[0].Hand, 0, "Your hand" });
            yield return null;
            AssertCenteredLabels();
            Set("_portraitRules", true); Set("_portraitDirty", true);
            yield return null;
            AssertCenteredLabels();
            ControllerType.GetMethod("PClosePreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_controller, null);
            Set("_portraitModal", "help");
            yield return null;
            AssertCenteredLabels();
        }

        [UnityTest]
        public IEnumerator BattlePower_AndAttackPowerHaveVisibleUprightText()
        {
            State.Players[0].BattleZone[0].TemporaryPowerAttacker = 2000;
            foreach (var tapped in new[] { false, true })
            {
                State.Players[0].BattleZone[0].Tapped = tapped;
                Set("_portraitDirty", true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var slot = Find("attacker").parent;
                foreach (var value in new[] { "2000", "ATK 4000" })
                {
                    var label = slot.GetComponentsInChildren<Component>().Single(c => c.GetType().Name == "Text" &&
                        (string)c.GetType().GetProperty("text").GetValue(c) == value);
                    var generator = (TextGenerator)label.GetType().GetProperty("cachedTextGenerator").GetValue(label);
                    Assert.That(generator.vertexCount, Is.GreaterThanOrEqualTo(value.Count(c => !char.IsWhiteSpace(c)) * 4), value);
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(label.transform.eulerAngles.z, 0)), Is.LessThan(.1f), value);
                    var settings = (TextGenerationSettings)label.GetType().GetMethod("GetGenerationSettings")
                        .Invoke(label, new object[] { ((RectTransform)label.transform).rect.size });
                    foreach (var scale in new[] { 1f, 1.5f, 2f, 3f })
                    {
                        settings.scaleFactor = scale;
                        using (var scaled = new TextGenerator())
                        {
                            scaled.Populate(value, settings);
                            Assert.That(scaled.vertexCount, Is.GreaterThanOrEqualTo(value.Count(c => !char.IsWhiteSpace(c)) * 4),
                                value + " at canvas scale " + scale);
                        }
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator CardArt_RetainsUpscaledDimensionsAtRuntime()
        {
            var art = Resources.Load<Texture2D>("CardGame/CardArt/0001");
            Assert.That(art, Is.Not.Null);
            Assert.That(art.width, Is.EqualTo(982));
            Assert.That(art.height, Is.EqualTo(1364));
            Assert.That(art.width / (float)art.height, Is.EqualTo(491f / 682f).Within(.0001f));
            Assert.That(art.mipmapCount, Is.EqualTo(1));
#if UNITY_EDITOR
            var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(
                "Assets/Resources/CardGame/CardArt/0001.png");
            Assert.That(importer.mipmapEnabled, Is.False);
            var android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(UnityEditor.TextureImporterFormat.ASTC_4x4));
            Assert.That(android.textureCompression,
                Is.EqualTo(UnityEditor.TextureImporterCompression.CompressedHQ));
            Assert.That(android.maxTextureSize, Is.GreaterThanOrEqualTo(Mathf.Max(art.width, art.height)));
#endif
            yield return null;
        }

        [UnityTest]
        public IEnumerator CustomGraphics_HaveRenderersAndCanBeClipped()
        {
            var graphics = _controller.GetComponentsInChildren<Component>(true)
                .Where(c => c.GetType().Name == "PortraitShieldGraphic" || c.GetType().Name == "PortraitAttackArrow").ToArray();
            Assert.That(graphics.Length, Is.GreaterThan(1));
            foreach (var graphic in graphics)
                Assert.That(graphic.GetComponent<CanvasRenderer>(), Is.Not.Null, graphic.name);
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DeckEditor_FiltersKeepScrollPositionAndSaveButtonFixed()
        {
            Call("OpenDeckLibrary");
            Call("BeginDeckEdit", ((SavedDeckLibrary)Get("_savedDecks")).SelectedDeck);
            yield return null;
            var scroll = SetupScroll();
            var save = Find("SAVE DECK"); var savePosition = Center(save);
            Assert.That(save.parent.name, Is.EqualTo("Deck editor footer"));
            foreach (var name in new[] { "Z TO A", "MANA COST" })
            {
                scroll = SetupScroll();
                var property = scroll.GetType().GetProperty("verticalNormalizedPosition");
                property.SetValue(scroll, .15f);
                yield return null;
                Click(name);
                yield return null;
                yield return null;
                Assert.That((float)property.GetValue(SetupScroll()), Is.EqualTo(.15f).Within(.01f), name);
                Assert.That(Vector2.Distance(Center(Find("SAVE DECK")), savePosition), Is.LessThan(1f), name);
            }
            Click("FILTERS");
            yield return null;
            Click("Civilization");
            yield return null;
            Click("Fire"); Click("APPLY FILTERS");
            yield return null;
            yield return null;
            Assert.That((float)SetupScroll().GetType().GetProperty("verticalNormalizedPosition")
                .GetValue(SetupScroll()), Is.EqualTo(.15f).Within(.01f));
            Assert.That(Vector2.Distance(Center(Find("SAVE DECK")), savePosition), Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator DeckFilters_ExactCostsAndMultiselectCategoriesUseTheFullPage()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            yield return null;
            Click("FILTERS");
            yield return null;
            var rows = Find("Filter values scroll").GetComponentsInChildren<RectTransform>()
                .Where(rect => rect.name.StartsWith("Mana ", StringComparison.Ordinal)).ToArray();
            Assert.That(rows.Select(rect => rect.name), Is.EquivalentTo(
                Enumerable.Range(1, DeckCatalogue.MaximumManaCost).Select(cost => "Mana " + cost)));
            var values = Find("Mana 1");
            foreach (var heading in new[] { "Mana cost", "Card type", "Civilization", "Shields", "Special abilities" })
                Assert.That(ScreenRect(Find(heading)).xMax, Is.LessThan(ScreenRect(values).xMin), heading);
            Assert.That(Find("Filter values scroll").parent, Is.SameAs(Get("_portraitBoard")));
            Click("Mana 1"); Click("Mana 3");
            yield return null;
            Click("Special abilities");
            yield return null;
            var scroll = SetupScroll();
            scroll.GetType().GetProperty("verticalNormalizedPosition").SetValue(scroll, .25f);
            yield return null;
            Click("Civilization");
            yield return null;
            Click("Fire"); Click("Multi-civilization");
            yield return null;
            Click("Card type");
            yield return null;
            Click("Creature"); Click("Spell");
            yield return null;
            var draft = (DeckCatalogueFilters)Get("_deckFilterDraft");
            Assert.That(draft.ManaCosts, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(draft.Civilizations, Is.EquivalentTo(new[] { Civilization.Fire }));
            Assert.That(draft.MultiCivilization, Is.True);
            Assert.That(draft.CardTypes, Is.EquivalentTo(new[] { CardType.Creature, CardType.Spell }));
            Assert.That(((DeckCatalogueFilters)Get("_deckFilters")).SelectionCount, Is.Zero);
            Click("Special abilities");
            yield return null;
            yield return null;
            Assert.That((float)SetupScroll().GetType().GetProperty("verticalNormalizedPosition")
                .GetValue(SetupScroll()), Is.EqualTo(.25f).Within(.01f));
            Click("APPLY FILTERS");
            yield return null;
            var filters = (DeckCatalogueFilters)Get("_deckFilters");
            Assert.That(filters.ManaCosts, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(filters.MultiCivilization, Is.True);
            Assert.That(Get("_deckFilterDraft"), Is.Null);
            var expected = DeckCatalogue.AvailableCards((SavedDeck)Get("_deckDraft"),
                DeckCardSort.NameAscending, filters).Take(24).Select(card => "Catalogue card " + card.Id);
            Assert.That(_controller.GetComponentsInChildren<RectTransform>()
                .Where(rect => rect.name.StartsWith("Catalogue card ", StringComparison.Ordinal))
                .Select(rect => rect.name), Is.EquivalentTo(expected));
        }

        [UnityTest]
        public IEnumerator DeckFilters_CancelAndClearKeepAppliedFiltersAndDeckEditsSeparate()
        {
            var originalFile = File.ReadAllText(_deckSavePath);
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            yield return null;
            Click("FILTERS");
            yield return null;
            Click("Mana 1"); Click("Mana 3"); Click("APPLY FILTERS");
            yield return null;
            Click("FILTERS");
            yield return null;
            Click("Mana 1"); Click("CANCEL FILTERS");
            yield return null;
            Assert.That(((DeckCatalogueFilters)Get("_deckFilters")).ManaCosts,
                Is.EquivalentTo(new[] { 1, 3 }));
            Click("FILTERS");
            yield return null;
            Click("CLEAR ALL");
            yield return null;
            Assert.That(((DeckCatalogueFilters)Get("_deckFilterDraft")).SelectionCount, Is.Zero);
            Assert.That(((DeckCatalogueFilters)Get("_deckFilters")).ManaCosts,
                Is.EquivalentTo(new[] { 1, 3 }));
            Click("CANCEL FILTERS");
            yield return null;
            Assert.That(((DeckCatalogueFilters)Get("_deckFilters")).ManaCosts,
                Is.EquivalentTo(new[] { 1, 3 }));
            Click("FILTERS");
            yield return null;
            Click("CLEAR ALL"); Click("APPLY FILTERS");
            yield return null;
            Assert.That(((DeckCatalogueFilters)Get("_deckFilters")).SelectionCount, Is.Zero);
            Assert.That(((SavedDeck)Get("_deckDraft")).CardCount, Is.Zero);
            Assert.That((bool)Get("_deckChanged"), Is.False);
            Assert.That(File.ReadAllText(_deckSavePath), Is.EqualTo(originalFile));
        }

        [UnityTest]
        public IEnumerator DeckFilters_ShieldsTypesAndAnySelectedAbilitiesCombine()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            yield return null;
            Click("FILTERS");
            yield return null;
            Click("Shields");
            yield return null;
            Click("Normal"); Click("Double Breaker");
            yield return null;
            Assert.That(((DeckCatalogueFilters)Get("_deckFilterDraft")).ShieldBreakers,
                Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(Find("Triple Breaker"), Is.Not.Null);
            Click("Card type");
            yield return null;
            Click("Spell");
            yield return null;
            Click("Special abilities");
            yield return null;
            foreach (var ability in new[] { "Speed Attacker", "Blocker", "Turbo Rush", "Shield Trigger", "Water Stealth" })
                Assert.That(Find(ability), Is.Not.Null, ability);
            Click("Shield Trigger"); Click("Blocker"); Click("APPLY FILTERS");
            yield return null;
            var cards = DeckCatalogue.AvailableCards((SavedDeck)Get("_deckDraft"),
                DeckCardSort.NameAscending, (DeckCatalogueFilters)Get("_deckFilters"));
            Assert.That(cards, Is.Not.Empty);
            Assert.That(cards.All(card => card.Type == CardType.Spell &&
                card.HasKeyword(CardKeyword.ShieldTrigger)), Is.True);
            Assert.That(((DeckCatalogueFilters)Get("_deckFilters")).SpecialAbilities.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator MainMenu_ShowsThreeActionsAndCustomizationShowsFiveStarters()
        {
            Call("ReturnToDeckSetup");
            yield return null;
            Assert.That(State, Is.Null);
            Assert.That(_controller.GetComponentsInChildren<Component>().Where(c => c.GetType().Name == "Button")
                .Select(c => c.name), Is.EquivalentTo(new[] { "NEW GAME", "CUSTOMIZE DECK", "TUTORIAL" }));
            Assert.That(Center(Find("TUTORIAL")).y, Is.LessThan(Center(Find("CUSTOMIZE DECK")).y));
            Click("CUSTOMIZE DECK");
            yield return null;
            Assert.That(((SavedDeckLibrary)Get("_savedDecks")).Decks.Count, Is.EqualTo(5));
            Assert.That(_controller.GetComponentsInChildren<Component>()
                .Count(c => c.GetType().Name == "Button" && c.name == "EDIT"), Is.EqualTo(5));
            Assert.That(_controller.GetComponentsInChildren<RectTransform>().Any(r => r.name == "DM-01"), Is.False);
        }

        [UnityTest]
        public IEnumerator Tutorial_ReplaysFixedCardsWithoutChangingSavedDecks()
        {
            var library = (SavedDeckLibrary)Get("_savedDecks");
            var selection = library.SelectedDeckId;
            var saved = File.ReadAllText(_deckSavePath);
            Call("ReturnToDeckSetup");
            yield return null;
            yield return TutorialClick("TUTORIAL");
            yield return null;
            var opening = State.Players[0].Hand.Select(c => c.InstanceId + ":" + c.Definition.Id).ToArray();
            Assert.That(State.Players[0].Deck.Count, Is.EqualTo(30));
            Assert.That(Find("Tutorial coach"), Is.Not.Null);
            yield return TutorialClick("NEXT");
            yield return null;
            yield return TutorialClick("☰");
            yield return null;
            yield return TutorialClick("RESTART TUTORIAL");
            yield return null;
            Assert.That(Tutorial.Step, Is.EqualTo(TutorialStep.Welcome));
            Assert.That(State.Players[0].Hand.Select(c => c.InstanceId + ":" + c.Definition.Id), Is.EqualTo(opening));
            yield return TutorialClick("EXIT");
            yield return null;
            Assert.That(Get("_tutorial"), Is.Null);
            Assert.That(State, Is.Null);
            Assert.That(library.SelectedDeckId, Is.EqualTo(selection));
            Assert.That(File.ReadAllText(_deckSavePath), Is.EqualTo(saved));
        }

        [UnityTest]
        public IEnumerator Tutorial_GuardsActionsKeepsCoachVisibleAndExitsDuringOpponentTurn()
        {
            Call("StartTutorial");
            yield return null;
            yield return TutorialClick("NEXT");
            yield return null;
            Assert.That(IsButtonEnabled("NEXT"), Is.False);
            yield return TutorialClick("VIEW CARD");
            yield return null;
            Assert.That(IsButtonEnabled("NEXT"), Is.True);
            Assert.That(IsButtonEnabled("SUMMON"), Is.False);
            Assert.That(IsButtonEnabled("CHARGE MANA"), Is.False);
            Assert.That(ScreenRect(Find("Tutorial coach")).Overlaps(ScreenRect(Find("SUMMON"))), Is.False);
            Assert.That(ScreenRect(Find("Tutorial coach")).Overlaps(ScreenRect(Find("X"))), Is.False);
            yield return TutorialClick("NEXT");
            yield return null;
            Assert.That(IsButtonEnabled("NEXT"), Is.False);
            TapTutorialDeck();
            yield return null;
            yield return TutorialClick("NEXT");
            yield return null;
            yield return TutorialClick("NEXT");
            yield return null;
            Assert.That(Tutorial.Step, Is.EqualTo(TutorialStep.ChargeFirstMana));
            Assert.That(IsButtonEnabled("END TURN"), Is.False);
            var wrong = State.Players[0].Hand.First(c => c.InstanceId != Tutorial.FocusCardId);
            Set("_selectedHand", wrong);
            Call("ChargeSelected");
            Call("EndHumanTurn");
            Assert.That(State.Players[0].Mana, Is.Empty);
            Assert.That(State.ActivePlayerId, Is.Zero);
            Assert.That((string)Get("_message"), Does.StartWith("Tutorial:"));
            yield return TutorialClick("VIEW CARD");
            yield return null;
            Assert.That(IsButtonEnabled("CHARGE MANA"), Is.True);
            Assert.That(IsButtonEnabled("SUMMON"), Is.False);
            yield return TutorialClick("CHARGE MANA");
            yield return null;
            yield return TutorialClick("NEXT");
            yield return null;
            yield return TutorialClick("END TURN");
            yield return null;
            Assert.That((bool)Get("_busy"), Is.True);
            yield return TutorialClick("EXIT");
            yield return null;
            Assert.That(Get("_tutorial"), Is.Null);
            Assert.That((int)Get("_actionDepth"), Is.Zero);
            yield return TutorialClick("NEW GAME");
            yield return new WaitForSeconds(.8f);
            Assert.That(Get("_tutorial"), Is.Null);
            Assert.That(State.TurnNumber, Is.EqualTo(1));
            Assert.That(IsButtonEnabled("END TURN"), Is.True);
            Assert.That(_controller.GetComponentsInChildren<RectTransform>().Any(r => r.name == "Tutorial coach"), Is.False);
        }

        [UnityTest]
        public IEnumerator Tutorial_RemainsPlayableWhenSavedDecksAreUnavailable()
        {
            Call("ReturnToDeckSetup"); Set("_savedDecks", null); Set("_portraitDirty", true);
            yield return null;
            Assert.That(IsButtonEnabled("NEW GAME"), Is.False);
            Assert.That(IsButtonEnabled("TUTORIAL"), Is.True);
            yield return TutorialClick("TUTORIAL");
            yield return null;
            Assert.That(Tutorial.Step, Is.EqualTo(TutorialStep.Welcome));
            Assert.That(State.Players[0].Hand.Count, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator Tutorial_FullTouchBattleUnlocksFreeTurnsAndFinishesWithARealWin() =>
            RunTutorialTouchBattle(1280);

        [UnityTest]
        public IEnumerator Tutorial_TallPortraitTouchBattleFinishesWithARealWin() =>
            RunTutorialTouchBattle(1600);

        private IEnumerator RunTutorialTouchBattle(int referenceHeight)
        {
            var scaler = _controller.GetComponentInChildren<Canvas>().GetComponents<Component>()
                .Single(c => c.GetType().Name == "CanvasScaler");
            scaler.GetType().GetProperty("referenceResolution").SetValue(scaler, new Vector2(720, referenceHeight));
            yield return null;
            yield return null;
            Assert.That((float)Get("_pw"), Is.EqualTo(720).Within(1));
            Assert.That((float)Get("_ph"), Is.EqualTo(referenceHeight).Within(1));
            Call("StartTutorial");
            Debug.Log("Tutorial viewport: " + Screen.width + "x" + Screen.height +
                "; table: " + Get("_pw") + "x" + Get("_ph"));
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var pile in _controller.GetComponentsInChildren<RectTransform>().Where(r => r.name == "DECK pile"))
                Assert.That(ScreenRect(Find("END TURN")).Overlaps(ScreenRect(pile)), Is.False);
            var deadline = Time.realtimeSinceStartup + 60;
            var sawSickness = false;
            while (Tutorial.Step != TutorialStep.Practice && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var choice = ((AbilityActionSession)Get("_actionSession"))?.PendingChoice;
                if (choice != null)
                {
                    if (!ReferenceEquals(Get("_portraitChoice"), choice)) continue;
                    if (choice.Kind == AbilityChoiceKind.Cards)
                    {
                        yield return TutorialClick(choice.CardLabels[0]);
                        yield return null;
                        yield return TutorialClick("CONFIRM 1 SELECTED");
                    }
                    else yield return TutorialClick(choice.Options[choice.Minimum]);
                    continue;
                }
                if ((bool)Get("_busy") || (int)Get("_actionDepth") > 0)
                { yield return new WaitForSeconds(.02f); continue; }
                if (Tutorial.IsExplanation)
                {
                    if (Tutorial.Step == TutorialStep.Hand || Tutorial.Step == TutorialStep.SummoningSickness)
                    {
                        yield return TutorialClick("VIEW CARD");
                        yield return null;
                        if (Tutorial.Step == TutorialStep.SummoningSickness)
                        {
                            sawSickness = true;
                            Assert.That(_controller.GetComponentsInChildren<Component>().Any(c =>
                                c.GetType().Name == "Text" && ((string)c.GetType().GetProperty("text").GetValue(c))
                                    .StartsWith("SUMMONING SICKNESS:")), Is.True);
                        }
                    }
                    if (Tutorial.Step == TutorialStep.Deck) { TapTutorialDeck(); yield return null; }
                    yield return TutorialClick("NEXT");
                }
                else if (Tutorial.Allows(TutorialAction.EndTurn)) yield return TutorialClick("END TURN");
                else if (Tutorial.Allows(TutorialAction.ChargeMana, Tutorial.FocusCardId) ||
                    Tutorial.Allows(TutorialAction.PlayCard, Tutorial.FocusCardId))
                {
                    var charge = Tutorial.Allows(TutorialAction.ChargeMana, Tutorial.FocusCardId);
                    var card = State.Players[0].Hand.Single(c => c.InstanceId == Tutorial.FocusCardId);
                    yield return TutorialClick("VIEW CARD");
                    yield return null;
                    yield return TutorialClick(charge ? "CHARGE MANA" : card.Definition.Type == CardType.Spell ? "CAST SPELL" : "SUMMON");
                }
                else
                {
                    var card = State.Players[0].BattleZone.Single(c => c.InstanceId == Tutorial.FocusCardId);
                    DragTutorialAttack(card, Tutorial.Step == TutorialStep.CreatureBattle ? "creature" : "shield",
                        Tutorial.Step == TutorialStep.CreatureBattle ? Tutorial.TargetCardId : 0);
                }
            }
            Assert.That(Tutorial.Step, Is.EqualTo(TutorialStep.Practice));
            Assert.That(sawSickness, Is.True);
            while ((bool)Get("_busy") || (int)Get("_actionDepth") > 0) yield return null;
            yield return null;
            Assert.That(IsButtonEnabled("END TURN"), Is.True);
            for (var turn = 0; turn < 2; turn++)
            {
                yield return TutorialClick("END TURN");
                while ((bool)Get("_busy") || (int)Get("_actionDepth") > 0)
                    yield return new WaitForSeconds(.02f);
                yield return null;
                Assert.That(Tutorial.Step, Is.EqualTo(TutorialStep.Practice));
                if (turn == 0)
                {
                    var charge = State.Players[0].Hand.First(c => c.Definition.Id == "dm01-71-110");
                    TapTutorialHandCard(charge.InstanceId);
                    yield return null;
                    yield return TutorialClick("CHARGE MANA");
                    yield return null;
                    Assert.That(State.Players[0].Mana.Count, Is.EqualTo(4));
                    var extra = State.Players[0].Hand.First(c => c.Definition.Id == "dm06-84-110");
                    TapTutorialHandCard(extra.InstanceId);
                    yield return null;
                    yield return TutorialClick("CHOOSE MANA MANUALLY  ·  2 AUTO-SELECTED");
                    yield return null;
                    yield return TutorialClick("CONFIRM PAYMENT & PLAY");
                    yield return null;
                    Assert.That(State.Players[0].BattleZone.Any(c => c.InstanceId == extra.InstanceId), Is.True);
                    Assert.That(Tutorial.Step, Is.EqualTo(TutorialStep.Practice));
                }
            }
            var sawLastShield = false;
            while (!State.IsFinished && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if ((bool)Get("_busy") || (int)Get("_actionDepth") > 0)
                { yield return new WaitForSeconds(.02f); continue; }
                if (State.Players[1].Shields.Count == 0) sawLastShield = true;
                var attacker = State.Players[0].BattleZone.FirstOrDefault(c =>
                    new RulesEngine().ValidateAttack(State, 0, c.InstanceId, AttackTargetKind.Player).Success);
                if (attacker == null) yield return TutorialClick("END TURN");
                else DragTutorialAttack(attacker, State.Players[1].Shields.Count > 0 ? "shield" : "player");
            }
            Assert.That(sawLastShield, Is.True);
            Assert.That(State.WinnerPlayerId, Is.EqualTo(0));
            yield return null;
            Assert.That(Find("TUTORIAL COMPLETE"), Is.Not.Null);
            Assert.That(IsButtonEnabled("START NEW GAME"), Is.True);
            yield return TutorialClick("START NEW GAME");
            yield return null;
            Assert.That(Get("_tutorial"), Is.Null);
            Assert.That(State.TurnNumber, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DeckEditor_TapAndCountControlsMoveCardsBetweenDeckAndCatalogue()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            yield return null;
            var card = DeckCatalogue.AvailableCards((SavedDeck)Get("_deckDraft"), DeckCardSort.NameAscending)[0];
            Click("Catalogue card " + card.Id);
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(card.Id));
            Click("+");
            yield return null;
            Assert.That(((SavedDeck)Get("_deckDraft")).CountOf(card.Id), Is.EqualTo(1));
            Assert.That(Find("Deck card " + card.Id), Is.Not.Null);
            Click("+");
            yield return null;
            var badge = Find("Deck copies " + card.Id);
            Assert.That((string)badge.GetComponent("Text").GetType().GetProperty("text")
                .GetValue(badge.GetComponent("Text")), Is.EqualTo("2"));
            Assert.That(((RectTransform)badge.parent).anchoredPosition.x,
                Is.GreaterThan(Find("Deck card " + card.Id).rect.width * .5f));
            Click("-");
            yield return null;
            Click("-");
            yield return null;
            Assert.That(((SavedDeck)Get("_deckDraft")).CountOf(card.Id), Is.Zero);
            Assert.That(_controller.GetComponentsInChildren<RectTransform>()
                .Any(r => r.name == "Deck card " + card.Id), Is.False);
            Assert.That(Find("Catalogue card " + card.Id), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DeckPreview_ArrowsBrowseDeckCardsAndWrapAtTheEnds()
        {
            Call("OpenDeckLibrary");
            Call("BeginDeckEdit", ((SavedDeckLibrary)Get("_savedDecks")).SelectedDeck);
            var cards = ((SavedDeck)Get("_deckDraft")).Entries.Select(entry => TcgCatalog.ById[entry.CardId])
                .OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            yield return null;
            Click("Deck card " + cards[0].Id);
            yield return null;
            var image = ScreenRect(Find("Deck preview card"));
            foreach (var arrow in new[] { "‹", "›" })
            {
                Assert.That(image.Contains(Center(Find(arrow))), Is.True, arrow);
                Assert.That(ScreenRect(Find(arrow)).Overlaps(ScreenRect(Find("+"))), Is.False, arrow);
                Assert.That(ScreenRect(Find(arrow)).Overlaps(ScreenRect(Find("-"))), Is.False, arrow);
            }
            Click("›");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[1].Id));
            Click("‹");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[0].Id));
            Click("‹");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards.Last().Id));
            Click("›");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[0].Id));
        }

        [UnityTest]
        public IEnumerator DeckPreview_FillsAvailableSpaceAndKeepsCountControlsBelowTheImage()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            var card = DeckCatalogue.AvailableCards((SavedDeck)Get("_deckDraft"), DeckCardSort.NameAscending)[0];
            yield return null;
            Click("Catalogue card " + card.Id);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var image = Find("Deck preview card");
            var panel = (RectTransform)image.parent;
            Assert.That(image.rect.width / image.rect.height, Is.EqualTo(491f / 682f).Within(.001f));
            Assert.That(image.rect.width >= panel.rect.width * .94f ||
                image.rect.height >= panel.rect.height - 290, Is.True, "The card should use the available width or height.");
            foreach (var control in new[] { "+", "-", "Preview deck total" })
            {
                var bounds = ScreenRect(Find(control));
                Assert.That(bounds.yMax, Is.LessThan(ScreenRect(image).yMin), control);
                Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(ScreenRect(panel).yMin), control);
            }
            Assert.That(ScreenRect(Find("CLOSE CARD")).yMin, Is.GreaterThan(ScreenRect(image).yMax));
            Assert.That(image.GetComponentsInChildren<Component>().Any(component => component.GetType().Name == "Text" &&
                (string)component.GetType().GetProperty("text").GetValue(component) == card.Cost.ToString()), Is.False);
            Click("+");
            yield return null;
            Assert.That(((SavedDeck)Get("_deckDraft")).CountOf(card.Id), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CataloguePreview_RespectsFiltersAndSortAcrossPagesWhileAddingCards()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            yield return null;
            Click("Z TO A"); Click("FILTERS");
            yield return null;
            for (var cost = 1; cost <= 5; cost++) Click("Mana " + cost);
            Click("Civilization");
            yield return null;
            Click("Fire"); Click("APPLY FILTERS");
            yield return null;
            var draft = (SavedDeck)Get("_deckDraft");
            var cards = DeckCatalogue.AvailableCards(draft, DeckCardSort.NameDescending,
                (DeckCatalogueFilters)Get("_deckFilters"));
            Assert.That(cards.Count, Is.GreaterThan(25));
            Click("Catalogue card " + cards[23].Id);
            yield return null;
            Click("›");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[24].Id));
            Click("+");
            yield return null;
            Assert.That(draft.CountOf(cards[24].Id), Is.EqualTo(1));
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[24].Id));
            Click("›");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[25].Id));
            Click("‹");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[24].Id));
            Click("-");
            yield return null;
            Assert.That(draft.CountOf(cards[24].Id), Is.Zero);
            Click("‹");
            yield return null;
            Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[23].Id));
        }

        [UnityTest]
        public IEnumerator DeckPreview_TouchSwipesNavigateOnceAndIgnoreVerticalOrCancelledDrags()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            var cards = DeckCatalogue.AvailableCards((SavedDeck)Get("_deckDraft"), DeckCardSort.NameAscending);
            yield return null;
            Click("Catalogue card " + cards[0].Id);
            yield return null;
            var gestures = new[]
            {
                (Movement: new Vector2(-90, 0), ExpectedIndex: 1, Cancel: false),
                (Movement: new Vector2(90, 0), ExpectedIndex: 0, Cancel: false),
                (Movement: new Vector2(-60, 150), ExpectedIndex: 0, Cancel: false),
                (Movement: new Vector2(20, 0), ExpectedIndex: 0, Cancel: false),
                (Movement: new Vector2(-90, 0), ExpectedIndex: 0, Cancel: true)
            };
            foreach (var gesture in gestures)
            {
                var art = Find("Deck preview card");
                var data = Pointer(Center(art));
                data.GetType().GetProperty("pointerId").SetValue(data, 7);
                Dispatch(art.gameObject, "OnPointerDown", data);
                SetPosition(data, Center(art) + gesture.Movement * _controller.GetComponentInChildren<Canvas>().scaleFactor);
                Dispatch(art.gameObject, "OnDrag", data);
                if (gesture.Cancel) Dispatch(art.gameObject, "OnCancel", data);
                Dispatch(art.gameObject, "OnPointerUp", data);
                Dispatch(art.gameObject, "OnEndDrag", data);
                yield return null;
                Assert.That(((CardDefinition)Get("_deckPreview")).Id, Is.EqualTo(cards[gesture.ExpectedIndex].Id));
            }
        }

        [UnityTest]
        public IEnumerator DeckPreview_SingleCardDisablesArrowsAndClosingClearsTheBrowseList()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            var card = TcgCatalog.Printings[0];
            Call("ChangeEditedCardCount", card, 1);
            yield return null;
            Click("Deck card " + card.Id);
            yield return null;
            Assert.That(IsButtonEnabled("‹"), Is.False);
            Assert.That(IsButtonEnabled("›"), Is.False);
            Click("CLOSE CARD");
            yield return null;
            Assert.That(Get("_deckPreview"), Is.Null);
            Assert.That(((IReadOnlyList<CardDefinition>)Get("_deckPreviewCards")), Is.Empty);
        }

        [UnityTest]
        public IEnumerator DeckEditor_InvalidSizeIsRedAndCannotReplaceTheSavedFile()
        {
            var library = (SavedDeckLibrary)Get("_savedDecks");
            var original = File.ReadAllText(_deckSavePath);
            Call("OpenDeckLibrary"); Call("BeginDeckEdit", library.SelectedDeck);
            var draft = (SavedDeck)Get("_deckDraft");
            var addition = TcgCatalog.Printings.First(card => draft.CountOfName(card.Id) == 0);
            Call("OpenDeckPreview", addition, false);
            Call("ChangeEditedCardCount", addition, 1);
            yield return null;
            Assert.That(draft.CardCount, Is.EqualTo(41));
            var total = Find("Deck total").GetComponent("Text");
            var color = (Color)total.GetType().GetProperty("color").GetValue(total);
            Assert.That(color.r, Is.GreaterThan(color.g * 2));
            var previewTotal = Find("Preview deck total").GetComponent("Text");
            var previewColor = (Color)previewTotal.GetType().GetProperty("color").GetValue(previewTotal);
            Assert.That(previewColor.r, Is.GreaterThan(previewColor.g * 2));
            Assert.That(IsButtonEnabled("SAVE DECK"), Is.False);
            Call("SaveEditedDeck");
            Assert.That(File.ReadAllText(_deckSavePath), Is.EqualTo(original));
            Call("ChangeEditedCardCount", addition, -1);
            yield return null;
            Assert.That(IsButtonEnabled("SAVE DECK"), Is.True);
            var existing = TcgCatalog.ById[draft.Entries[0].CardId];
            Call("ChangeEditedCardCount", existing, -1);
            yield return null;
            Assert.That(draft.CardCount, Is.EqualTo(39));
            Assert.That(IsButtonEnabled("SAVE DECK"), Is.False);
        }

        [UnityTest]
        public IEnumerator SavedEditsAndSelection_SurviveRestartAndSupplyTheHumanDuelDeck()
        {
            var original = ((SavedDeckLibrary)Get("_savedDecks")).SelectedDeck.Copy();
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            var draft = (SavedDeck)Get("_deckDraft");
            draft.Entries = original.Entries;
            draft.Name = "Persisted custom deck";
            var deckId = draft.Id;
            Call("SaveEditedDeck"); Call("SelectDuelDeck", deckId);
            var savedFile = File.ReadAllText(_deckSavePath);
            Object.Destroy(_controller.gameObject);
            yield return null;
            CreateController();
            yield return null;
            var loaded = (SavedDeckLibrary)Get("_savedDecks");
            Assert.That(loaded.SelectedDeckId, Is.EqualTo(deckId));
            Assert.That(loaded.SelectedDeck.Name, Is.EqualTo("Persisted custom deck"));
            Assert.That(loaded.SelectedDeck.Entries.Select(entry => (entry.CardId, entry.Count)),
                Is.EqualTo(original.Entries.Select(entry => (entry.CardId, entry.Count))));
            for (var duel = 0; duel < 2; duel++)
            {
                Call("StartSelectedMatch");
                yield return null;
                var human = State.Players[0];
                var computer = State.Players[1];
                var humanCards = human.Deck.Concat(human.Hand).Concat(human.Shields).ToArray();
                var computerCards = computer.Deck.Concat(computer.Hand).Concat(computer.Shields).ToArray();
                Assert.That(humanCards.GroupBy(card => card.Definition.Id)
                        .Select(group => (group.Key, group.Count())).OrderBy(item => item.Key),
                    Is.EqualTo(original.Entries.Select(entry => (entry.CardId, entry.Count))
                        .OrderBy(item => item.CardId)));
                Assert.That(computerCards.Length, Is.EqualTo(40));
                Assert.That(computerCards.All(card => card.Definition.Civilizations.Length == 1), Is.True);
                Assert.That(computerCards.SelectMany(card => card.Definition.Civilizations).Distinct().Count(),
                    Is.EqualTo(1));
                Assert.That(computerCards.GroupBy(card => card.Definition.Name)
                    .All(group => group.Count() <= 4), Is.True);
                Assert.That(File.ReadAllText(_deckSavePath), Is.EqualTo(savedFile));
            }
        }

        [UnityTest]
        public IEnumerator UnsavedDeckChanges_RequireConfirmationBeforeDiscard()
        {
            Call("OpenDeckLibrary"); Call("CreateCustomDeck");
            var draft = (SavedDeck)Get("_deckDraft");
            Call("ChangeEditedCardCount", TcgCatalog.Printings[0], 1);
            Call("CloseDeckEditor");
            yield return null;
            Assert.That((string)Get("_deckConfirmation"), Does.Contain("Discard"));
            Click("CANCEL");
            yield return null;
            Assert.That(Get("_deckDraft"), Is.SameAs(draft));
            Call("CloseDeckEditor");
            yield return null;
            Click("CONFIRM");
            yield return null;
            Assert.That(Get("_deckDraft"), Is.Null);
            Assert.That(((SavedDeckLibrary)Get("_savedDecks")).CustomDeckCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InvalidSavedData_IsReportedAndNotOverwritten()
        {
            Call("ReturnToDeckSetup");
            const string invalid = "{\"Version\":999}";
            File.WriteAllText(_deckSavePath, invalid);
            LogAssert.Expect(LogType.Error, new Regex("Could not load saved decks:"));
            Call("LoadSavedDecks");
            yield return null;
            Assert.That(Get("_savedDecks"), Is.Null);
            Assert.That(File.ReadAllText(_deckSavePath), Is.EqualTo(invalid));
            Assert.That(IsButtonEnabled("NEW GAME"), Is.False);
        }

        [UnityTest]
        public IEnumerator FailedSave_RetainsTheDraftAndLastSavedLibrary()
        {
            var library = (SavedDeckLibrary)Get("_savedDecks");
            var original = File.ReadAllText(_deckSavePath);
            Call("OpenDeckLibrary"); Call("BeginDeckEdit", library.SelectedDeck);
            var draft = (SavedDeck)Get("_deckDraft");
            draft.Name = "Unsaved name";
            Set("_deckSavePath", Path.Combine(_deckSavePath, "blocked.json"));
            LogAssert.Expect(LogType.Error, new Regex("Could not save decks:"));
            Call("SaveEditedDeck");
            yield return null;
            Assert.That(Get("_savedDecks"), Is.SameAs(library));
            Assert.That(Get("_deckDraft"), Is.SameAs(draft));
            Assert.That(File.ReadAllText(_deckSavePath), Is.EqualTo(original));
            Assert.That((string)Get("_deckNotice"), Does.Contain("Could not save"));
        }

        [UnityTest]
        public IEnumerator TappedBattleCard_TurnsSidewaysWithUprightPower()
        {
            var card = Find("target");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(card.localEulerAngles.z, -90)), Is.LessThan(.1f));
            var power = card.parent.GetComponentsInChildren<Component>().Single(c => c.GetType().Name == "Text" &&
                (string)c.GetType().GetProperty("text").GetValue(c) == "2000");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(power.transform.eulerAngles.z, 0)), Is.LessThan(.1f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManaCards_AreFaceUpInvertedAndTurnWhenTapped()
        {
            var ready = Card(50, "ready mana"); var tapped = Card(51, "tapped mana"); tapped.Tapped = true;
            State.Players[0].Mana.Add(ready); State.Players[0].Mana.Add(tapped);
            Set("_portraitDirty", true);
            yield return null;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(Find("ready mana").localEulerAngles.z, 180)), Is.LessThan(.1f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(Find("tapped mana").localEulerAngles.z, 90)), Is.LessThan(.1f));
            Assert.That(Find("Your mana row").GetComponentsInChildren<RectTransform>()
                .Any(r => r.name == "Hidden card"), Is.False);
        }

        [UnityTest]
        public IEnumerator Graveyard_ShowsScatteredFaceUpCardsAndFullLabel()
        {
            for (var i = 0; i < 4; i++) State.Players[0].Graveyard.Add(Card(60 + i, "grave " + i));
            Set("_portraitDirty", true);
            yield return null;
            var cards = Enumerable.Range(0, 4).Select(i => Find("grave " + i)).ToArray();
            Assert.That(cards.All(c => c.parent.name == "Graveyard pile"), Is.True);
            Assert.That(cards.Select(c => c.localEulerAngles.z).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(cards[0].parent.GetComponentsInChildren<RectTransform>().Any(r => r.name == "Hidden card"), Is.False);
            Assert.That(_controller.GetComponentsInChildren<Component>().Count(c => c.GetType().Name == "Text" &&
                (string)c.GetType().GetProperty("text").GetValue(c) == "Graveyard"), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator AttackVideo_IsCenteredAndKeepsItsAspectRatio()
        {
            Set("_showVideo", true); Set("_portraitDirty", true);
            yield return null;
            var video = Find("Attack video"); var frame = (RectTransform)Get("_portraitFrame");
            Assert.That(Vector2.Distance(Center(video), Center(frame)), Is.LessThan(1f));
            Assert.That(video.rect.width / video.rect.height, Is.EqualTo(16f / 9f).Within(.001f));
            Assert.That(Find("Attack video backdrop").rect.height, Is.EqualTo(frame.rect.height).Within(1));
            Set("_showVideo", false);
        }

        [UnityTest]
        public IEnumerator NoLegalBlocker_ResolvesWithoutOpeningBlockerPrompt()
        {
            // No blockers, a tapped blocker, and an unblockable attacker must
            // all skip the prompt. The engine remains responsible for damage.
            for (var scenario = 0; scenario < 3; scenario++)
            {
                var state = BlockTestState(scenario > 0);
                if (scenario == 1) state.Players[0].BattleZone.Last().Tapped = true;
                if (scenario == 2) state.Players[1].BattleZone[0].TemporaryKeywords.Add(CardKeyword.Unblockable);
                var engine = new RulesEngine();
                Assert.That(engine.DeclareAttack(state, 1, 201, AttackTargetKind.Creature, 101).Success, Is.True);
                Assert.That(engine.GetBlockerCandidates(state, 0).Count, Is.Zero);
                Set("_state", state); Set("_awaitingHumanBlock", false);
                var routine = (IEnumerator)ControllerType.GetMethod("WaitForHumanBlockOrResolve", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(_controller, null);
                while (routine.MoveNext())
                {
                    Assert.That((bool)Get("_awaitingHumanBlock"), Is.False);
                    yield return routine.Current;
                }
                Assert.That(State.PendingAttack, Is.Null);
                Assert.That((bool)Get("_awaitingHumanBlock"), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator LegalBlocker_StillRequiresPlayerDecision()
        {
            var state = BlockTestState(true); var engine = new RulesEngine();
            Assert.That(engine.DeclareAttack(state, 1, 201, AttackTargetKind.Creature, 101).Success, Is.True);
            Assert.That(engine.GetBlockerCandidates(state, 0).Count, Is.EqualTo(1));
            Set("_state", state);
            var routine = (IEnumerator)ControllerType.GetMethod("WaitForHumanBlockOrResolve", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_controller, null);
            Assert.That(routine.MoveNext(), Is.True);
            Assert.That((bool)Get("_awaitingHumanBlock"), Is.True);
            Assert.That(State.PendingAttack, Is.Not.Null);
            Set("_awaitingHumanBlock", false);
            Assert.That(routine.MoveNext(), Is.False);
            yield return null;
        }

        private static GameState BlockTestState(bool withBlocker)
        {
            var state = new GameState { ActivePlayerId = 1, Phase = GamePhase.Main, TurnNumber = 5,
                Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } } };
            var defender = Card(101, "defender"); defender.Tapped = true;
            state.Players[0].BattleZone.Add(defender);
            state.Players[1].BattleZone.Add(Card(201, "opponent attacker"));
            if (withBlocker)
            {
                var blocker = Card(102, "blocker"); blocker.TemporaryKeywords.Add(CardKeyword.Blocker);
                state.Players[0].BattleZone.Add(blocker);
            }
            return state;
        }

        [UnityTest]
        public IEnumerator AttackDrag_ReturnToSourceCancelsWithoutTapping()
        {
            var source = Find("attacker"); var target = Find("target");
            var data = Pointer(Center(source));
            Dispatch(source.gameObject, "OnPointerDown", data);
            SetPosition(data, Center(target));
            Dispatch(source.gameObject, "OnDrag", data);
            Assert.That(_controller.GetComponentsInChildren<Component>().Any(g => g.GetType().Name == "PortraitAttackArrow"), Is.True);
            SetPosition(data, Center(source));
            Dispatch(source.gameObject, "OnDrag", data);
            Dispatch(source.gameObject, "OnPointerUp", data);
            yield return null;
            Assert.That(State.PendingAttack, Is.Null);
            Assert.That(State.Players[0].BattleZone.Single().Tapped, Is.False);
            Assert.That(State.Players[1].BattleZone.Single().Definition.Name, Is.EqualTo("target"));
        }

        [UnityTest]
        public IEnumerator AttackDrag_InvalidDropCancelsWithoutTapping()
        {
            var source = Find("attacker"); var data = Pointer(Center(source));
            Dispatch(source.gameObject, "OnPointerDown", data);
            SetPosition(data, (Vector2)data.GetType().GetProperty("position").GetValue(data) + new Vector2(-300, 100));
            Dispatch(source.gameObject, "OnDrag", data);
            Dispatch(source.gameObject, "OnPointerUp", data);
            yield return null;
            Assert.That(State.PendingAttack, Is.Null);
            Assert.That(State.Players[0].BattleZone.Single().Tapped, Is.False);
        }

        [UnityTest]
        public IEnumerator AttackDrag_LegalCreatureCommitsOnce()
        {
            var source = Find("attacker"); var data = Pointer(Center(source));
            Dispatch(source.gameObject, "OnPointerDown", data);
            SetPosition(data, Center(Find("target")));
            Dispatch(source.gameObject, "OnDrag", data);
            Dispatch(source.gameObject, "OnPointerUp", data);
            Dispatch(source.gameObject, "OnEndDrag", data);
            for (var i = 0; i < 5; i++) yield return null;
            Assert.That(State.Players[1].Graveyard.Count(c => c.InstanceId == 2), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator HandHold_ScrubsToAnotherCardThenOpensItOnRelease()
        {
            var source = Find("hand 0"); var data = Pointer(Center(source));
            Dispatch(source.gameObject, "OnPointerDown", data);
            yield return new WaitForSecondsRealtime(.4f);
            SetPosition(data, Center(Find("hand 2")));
            Dispatch(source.gameObject, "OnDrag", data);
            Dispatch(source.gameObject, "OnPointerUp", data);
            yield return null;
            var preview = (List<CardInstance>)Get("_portraitPreview");
            Assert.That(preview[(int)Get("_portraitPreviewIndex")].InstanceId, Is.EqualTo(12));
            Assert.That(State.Players[0].Hand.Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator CancelledPointer_DoesNotBecomeAnAttackOrTap()
        {
            var source = Find("attacker"); var data = Pointer(Center(source));
            Dispatch(source.gameObject, "OnPointerDown", data);
            SetPosition(data, Center(Find("target")));
            Dispatch(source.gameObject, "OnDrag", data);
            Dispatch(source.gameObject, "OnCancel", data);
            Dispatch(source.gameObject, "OnPointerUp", data);
            yield return null;
            Assert.That(State.PendingAttack, Is.Null);
            Assert.That(State.Players[0].BattleZone.Single().Tapped, Is.False);
        }

        private GameState State => (GameState)Get("_state");
        [Serializable]
        private sealed class LoggedDuel
        {
            public string DuelId;
            public bool Closed;
            public LoggedEvent[] Events;
        }
        [Serializable]
        private sealed class LoggedEvent
        {
            public string Kind;
            public bool Success;
            public string Error;
            public RuleTraceEntry[] Trace;
            public LoggedMovement[] Movements;
            public LoggedState State;
        }
        [Serializable]
        private sealed class LoggedMovement
        {
            public long InstanceId;
            public string From;
            public string To;
        }
        [Serializable]
        private sealed class LoggedState
        {
            public LoggedPlayer[] Players;
        }
        [Serializable]
        private sealed class LoggedPlayer
        {
            public LoggedCard[] BattleZone;
            public LoggedCard[] Graveyard;
        }
        [Serializable]
        private sealed class LoggedCard
        {
            public long InstanceId;
            public string Name;
        }

        [UnityTest]
        public IEnumerator DuelLogs_RecordActualBlockerPowersSlayerAndDistinctCardMovements()
        {
            var peppi = new CardInstance { InstanceId = 101, Definition = TcgCatalog.ById["dm12-50-55"] };
            var mutant = new CardInstance { InstanceId = 201, Definition = TcgCatalog.ById["dm07-31-55"], Tapped = true };
            var blocker = new CardInstance { InstanceId = 202, Definition = TcgCatalog.ById["dm12-47-55"] };
            State.Players[0].BattleZone.Clear();
            State.Players[1].BattleZone.Clear();
            State.Players[1].Shields.Clear();
            State.Players[0].BattleZone.Add(peppi);
            State.Players[1].BattleZone.AddRange(new[] { mutant, blocker });
            Set("_selectedCreature", peppi);
            Call("StartHumanAttack", AttackTargetKind.Creature, mutant.InstanceId);
            for (var i = 0; i < 30 && ((bool)Get("_busy") || (int)Get("_actionDepth") > 0); i++)
                yield return null;
            Assert.That((bool)Get("_busy"), Is.False);
            var log = JsonUtility.FromJson<LoggedDuel>(File.ReadAllText((string)Get("_duelLogPath")));
            var trace = log.Events.SelectMany(entry => entry.Trace).ToArray();
            var block = trace.Single(entry => entry.Kind == "BlockDeclared");
            Assert.That(block.SourceName, Is.EqualTo("Gigaslug"));
            Assert.That(block.IntendedTargetInstanceId, Is.EqualTo(mutant.InstanceId));
            var battle = trace.Single(entry => entry.Kind == "Battle");
            Assert.That(battle.SourcePower, Is.EqualTo(5000));
            Assert.That(battle.TargetPower, Is.EqualTo(1000));
            Assert.That(battle.TargetKeywords, Does.Contain("Slayer"));
            var resolution = log.Events.Last();
            Assert.That(resolution.Movements.Any(move => move.InstanceId == peppi.InstanceId &&
                move.From == "0/BattleZone" && move.To == "0/Graveyard"), Is.True);
            Assert.That(resolution.State.Players[1].BattleZone.Single().InstanceId, Is.EqualTo(mutant.InstanceId));
            Assert.That(resolution.State.Players[0].Graveyard.Any(card => card.InstanceId == peppi.InstanceId), Is.True);
        }

        [UnityTest]
        public IEnumerator DuelLogs_RetainThreeDuelsAcrossControllerRestartsAndPreserveOtherFiles()
        {
            var created = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                Call("StartSelectedMatch");
                created.Add((string)Get("_duelLogPath"));
                yield return null;
                if (i == 0) File.WriteAllText(Path.Combine(_duelLogDirectory, "unrelated.txt"), "Keep this file");
            }
            Assert.That(Directory.GetFiles(_duelLogDirectory, "duel-*.json"),
                Is.EquivalentTo(created.Skip(1)));
            Assert.That(File.ReadAllText(Path.Combine(_duelLogDirectory, "unrelated.txt")), Is.EqualTo("Keep this file"));
            var older = File.ReadAllText(created[1]);
            Object.Destroy(_controller.gameObject);
            yield return null;
            CreateController();
            yield return null;
            Assert.That(Directory.GetFiles(_duelLogDirectory, "duel-*.json").Length, Is.EqualTo(3));
            Assert.That(File.ReadAllText(created[1]), Is.EqualTo(older));
            Assert.That(JsonUtility.FromJson<LoggedDuel>(File.ReadAllText(created.Last())).Closed, Is.True);
            Call("StartSelectedMatch");
            yield return null;
            Assert.That(Directory.GetFiles(_duelLogDirectory, "duel-*.json").Length, Is.EqualTo(3));
            Assert.That(File.Exists(created[1]), Is.False);
        }

        [UnityTest]
        public IEnumerator DuelLogs_StorageFailureIsReportedAndRecoversWithoutLosingActions()
        {
            File.WriteAllText(_duelLogDirectory, "Block directory creation");
            LogAssert.Expect(LogType.Error, new Regex("Duel logging error:"));
            Call("BeginDuelLog");
            Assert.That((string)Get("_duelLogError"), Is.Not.Empty);
            Assert.That(File.Exists((string)Get("_duelLogPath")), Is.False);
            File.Delete(_duelLogDirectory);
            var cardId = State.Players[0].Hand[0].InstanceId;
            Func<RulesEngine, GameState, RuleResult> charge =
                (engine, state) => engine.ChargeMana(state, 0, cardId);
            var routine = (IEnumerator)ControllerType.GetMethod("RunAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_controller, new object[] { charge, null });
            while (routine.MoveNext()) yield return routine.Current;
            Assert.That((string)Get("_duelLogError"), Is.Empty);
            Assert.That(State.Players[0].Mana.Any(card => card.InstanceId == cardId), Is.True);
            var log = JsonUtility.FromJson<LoggedDuel>(File.ReadAllText((string)Get("_duelLogPath")));
            Assert.That(log.Events.Select(entry => entry.Kind), Is.EqualTo(new[] { "DuelStarted", "Command" }));
            Assert.That(log.Events.Last().Movements.Any(move => move.InstanceId == cardId &&
                move.From == "0/Hand" && move.To == "0/Mana"), Is.True);
        }

        [UnityTest]
        public IEnumerator DuelLogs_RecordFailedCommandsWithoutChangingTheBoard()
        {
            var handId = State.Players[0].Hand[0].InstanceId;
            Func<RulesEngine, GameState, RuleResult> illegalCharge =
                (engine, state) => engine.ChargeMana(state, 1, handId);
            var routine = (IEnumerator)ControllerType.GetMethod("RunAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_controller, new object[] { illegalCharge, null });
            while (routine.MoveNext()) yield return routine.Current;
            var log = JsonUtility.FromJson<LoggedDuel>(File.ReadAllText((string)Get("_duelLogPath")));
            Assert.That(log.Events.Last().Success, Is.False);
            Assert.That(log.Events.Last().Error, Does.Contain("turn"));
            Assert.That(log.Events.Last().Movements, Is.Empty);
            Assert.That(State.Players[0].Hand.Any(card => card.InstanceId == handId), Is.True);
        }

        private TutorialBattle Tutorial => (TutorialBattle)Get("_tutorial");

        private void TapTutorialHandCard(long id)
        {
            var card = ((Dictionary<long, RectTransform>)Get("_portraitHandCards"))[id];
            var data = Pointer(Center(card));
            Dispatch(card.gameObject, "OnPointerDown", data);
            Dispatch(card.gameObject, "OnPointerUp", data);
        }

        private void TapTutorialDeck()
        {
            var pile = _controller.GetComponentsInChildren<RectTransform>()
                .Where(r => r.name == "DECK pile").OrderBy(r => Center(r).y).First();
            var data = Pointer(Center(pile));
            Dispatch(pile.gameObject, "OnPointerDown", data);
            Dispatch(pile.gameObject, "OnPointerUp", data);
        }

        private void DragTutorialAttack(CardInstance card, string kind, long targetId = 0)
        {
            var source = _controller.GetComponentsInChildren<RectTransform>().First(r =>
                r.name == card.Definition.Name && r.parent.name == "Battle card slot");
            var target = ((IEnumerable)Get("_portraitTargets")).Cast<object>().First(item =>
                (string)item.GetType().GetField("Kind").GetValue(item) == kind &&
                (targetId == 0 || (long)item.GetType().GetField("Id").GetValue(item) == targetId));
            var targetRect = (RectTransform)target.GetType().GetField("Rect").GetValue(target);
            Assert.That(ScreenRect(source).Overlaps(ScreenRect(Find("Tutorial coach"))), Is.False);
            Assert.That(ScreenRect(targetRect).Overlaps(ScreenRect(Find("Tutorial coach"))), Is.False);
            var data = Pointer(Center(source));
            Dispatch(source.gameObject, "OnPointerDown", data);
            SetPosition(data, Center(targetRect));
            Dispatch(source.gameObject, "OnDrag", data);
            Assert.That((bool)Get("_portraitDragging"), Is.True, card.Definition.Name + " -> " + kind);
            Dispatch(source.gameObject, "OnPointerUp", data);
            Dispatch(source.gameObject, "OnEndDrag", data);
        }

        private void CreateController()
        {
            var host = new GameObject("Portrait UI test");
            host.SetActive(false);
            _controller = (MonoBehaviour)host.AddComponent(ControllerType);
            Set("_deckSavePath", _deckSavePath);
            Set("_duelLogDirectory", _duelLogDirectory);
            host.SetActive(true);
        }
        private void Call(string method, params object[] args) =>
            ControllerType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_controller, args);
        private void Click(string name)
        {
            var button = Find(name).GetComponents<Component>().Single(c => c.GetType().Name == "Button");
            Assert.That((bool)button.GetType().GetProperty("interactable").GetValue(button), Is.True, name);
            var click = button.GetType().GetProperty("onClick").GetValue(button);
            click.GetType().GetMethod("Invoke").Invoke(click, null);
        }
        private IEnumerator TutorialClick(string name)
        {
            yield return null;
            var rect = Find(name);
            var graphic = rect.GetComponent("Image");
            // Rebuilt graphics need a native canvas pass before they can be raycast.
            for (var frame = 0; frame < 5; frame++)
            {
                Canvas.ForceUpdateCanvases();
                if ((int)graphic.GetType().GetProperty("depth").GetValue(graphic) >= 0) break;
                yield return null;
            }
            var system = UiType("EventSystem").GetProperty("current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var resultType = UiType("RaycastResult");
            var hits = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(resultType));
            UiType("EventSystem").GetMethod("RaycastAll").Invoke(system,
                new object[] { Pointer(Center(rect)), hits });
            Assert.That(hits.Count, Is.GreaterThan(0), name + " must be touchable");
            var hit = (GameObject)resultType.GetProperty("gameObject").GetValue(hits[0]);
            Assert.That(hit.transform == rect || hit.transform.IsChildOf(rect), Is.True,
                name + " is covered by " + hit.name);
            Click(name);
        }
        private bool IsButtonEnabled(string name)
        {
            var button = Find(name).GetComponents<Component>().Single(c => c.GetType().Name == "Button");
            return (bool)button.GetType().GetProperty("interactable").GetValue(button);
        }
        private Component SetupScroll() => _controller.GetComponentsInChildren<Component>().Single(c => c.GetType().Name == "ScrollRect");
        private object Get(string field) => ControllerType.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_controller);
        private void Set(string field, object value) => ControllerType.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_controller, value);
        private RectTransform Find(string name) => _controller.GetComponentsInChildren<RectTransform>().First(r => r.name == name);
        private static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(c => c.x), corners.Min(c => c.y), corners.Max(c => c.x), corners.Max(c => c.y));
        }
        private void AssertCenteredLabels()
        {
            foreach (var label in _controller.GetComponentsInChildren<Component>().Where(c => c.GetType().Name == "Text"))
                Assert.That((TextAnchor)label.GetType().GetProperty("alignment").GetValue(label),
                    Is.EqualTo(TextAnchor.MiddleCenter), (string)label.GetType().GetProperty("text").GetValue(label));
        }
        private static Type UiType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEngine.EventSystems." + name)).First(t => t != null);
        private static object Pointer(Vector2 p)
        {
            var system = UiType("EventSystem").GetProperty("current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var data = Activator.CreateInstance(UiType("PointerEventData"), new[] { system });
            data.GetType().GetProperty("pointerId").SetValue(data, -1);
            SetPosition(data, p);
            return data;
        }
        private static void SetPosition(object data, Vector2 position) => data.GetType().GetProperty("position").SetValue(data, position);
        private static void Dispatch(GameObject target, string method, object data)
        {
            var pointer = target.GetComponents<Component>().Single(c => c.GetType().Name == "PortraitPointer");
            pointer.GetType().GetMethod(method).Invoke(pointer, new[] { data });
        }
        private static CardInstance Card(long id, string name) => new CardInstance { InstanceId = id, EnteredBattleZoneOnTurn = -1,
            Definition = new CardDefinition { Id = name, Name = name, Cost = 1, Power = 2000, Civilizations = new[] { Civilization.Fire },
                Type = CardType.Creature, ImplementationStatus = CardImplementationStatus.Complete } };
    }
}
