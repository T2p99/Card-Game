using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private Type ControllerType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("CardGame.Presentation.DuelPrototypeController"))
            .First(t => t != null);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            foreach (var instance in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(c => c != null && c.GetType() == ControllerType)) Object.Destroy(instance.gameObject);
            yield return null;
            _controller = (MonoBehaviour)new GameObject("Portrait UI test").AddComponent(ControllerType);
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
        public IEnumerator DeckSetup_SelectionsKeepScrollPositionAndStartButtonFixed()
        {
            Set("_showDeckSetup", true); Set("_portraitDirty", true);
            yield return null;
            var scroll = SetupScroll();
            var start = Find("START DUEL"); var startPosition = Center(start);
            Assert.That(start.parent.name, Is.EqualTo("Deck setup footer"));
            foreach (var name in new[] { "DM-01", "Fire", "ALL SETS" })
            {
                scroll = SetupScroll();
                var property = scroll.GetType().GetProperty("verticalNormalizedPosition");
                property.SetValue(scroll, .15f);
                yield return null;
                var button = scroll.GetComponentsInChildren<Component>().First(c => c.GetType().Name == "Button" &&
                    c.name.Replace("✓ ", "") == name);
                var click = button.GetType().GetProperty("onClick").GetValue(button);
                click.GetType().GetMethod("Invoke").Invoke(click, null);
                yield return null;
                yield return null;
                Assert.That((float)property.GetValue(SetupScroll()), Is.EqualTo(.15f).Within(.01f), name);
                Assert.That(Vector2.Distance(Center(Find("START DUEL")), startPosition), Is.LessThan(1f), name);
            }
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
        private Component SetupScroll() => _controller.GetComponentsInChildren<Component>().Single(c => c.GetType().Name == "ScrollRect");
        private object Get(string field) => ControllerType.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_controller);
        private void Set(string field, object value) => ControllerType.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_controller, value);
        private RectTransform Find(string name) => _controller.GetComponentsInChildren<RectTransform>().First(r => r.name == name);
        private static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
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
