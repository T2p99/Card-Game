using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace CardGame.Presentation
{
    public sealed partial class DuelPrototypeController
    {
        private static readonly Color PInk = new Color(.035f, .075f, .10f);
        private static readonly Color PPanel = new Color(.065f, .13f, .17f);
        private static readonly Color PGold = new Color(.91f, .73f, .38f);
        private static readonly Color PCyan = new Color(.30f, .83f, 1f);
        private Canvas _portraitCanvas;
        private RectTransform _portraitSafe, _portraitFrame, _portraitBoard, _portraitOverlay;
        private PortraitAttackArrow _portraitArrow;
        private Font _portraitFont;
        private Sprite _portraitRound, _portraitCircle;
        private Texture2D _portraitRoundTexture, _portraitCircleTexture;
        private GameObject _portraitEventSystem;
        private float _pw = 720f, _ph = 1280f;
        private string _portraitSignature;
        private bool _portraitDirty = true, _portraitGestureActive, _portraitScrubbing, _portraitDragging, _portraitScrolling;
        private Vector2 _portraitPress, _portraitLast;
        private CardInstance _portraitPressedCard;
        private ScrollRect _portraitGestureScroll;
        private readonly Dictionary<long, RectTransform> _portraitHandCards = new Dictionary<long, RectTransform>();
        private readonly List<PortraitTarget> _portraitTargets = new List<PortraitTarget>();
        private long _portraitScrubId;
        private RectTransform _portraitSource;
        private List<CardInstance> _portraitPreview;
        private int _portraitPreviewIndex;
        private string _portraitPreviewZone;
        private string _portraitModal;
        private bool _portraitManualMana, _portraitRules;
        private float _portraitRowOffset, _portraitOpponentRowOffset, _portraitHandOffset;
        private float _portraitManaRowOffset, _portraitOpponentManaRowOffset;
        private long _portraitPaymentCard;
        private readonly HashSet<long> _portraitManaIds = new HashSet<long>();
        private AbilityChoice _portraitChoice;
        private readonly List<long> _portraitChoiceIds = new List<long>();
        private int _portraitNumber;
        private float _portraitChoiceOffset = 1f, _portraitManaOffset = 1f, _portraitSetupOffset = 1f;
        private Text _portraitStatus;
        private readonly List<PortraitArt> _portraitArt = new List<PortraitArt>();
        private Rect _portraitLastSafe;
        private int _portraitScreenWidth, _portraitScreenHeight;
        private GameState _portraitLastState;

        private sealed class PortraitTarget
        {
            public RectTransform Rect;
            public Image Border;
            public long Id;
            public string Kind;
        }
        private sealed class PortraitArt { public RawImage Image; public string Id; public GameObject Fallback; }

        private void CreatePortraitInterface()
        {
            _portraitFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _portraitRound = PortraitSprite(false, out _portraitRoundTexture);
            _portraitCircle = PortraitSprite(true, out _portraitCircleTexture);
            var canvasObject = new GameObject("Portrait Duel UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _portraitCanvas = canvasObject.GetComponent<Canvas>();
            _portraitCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _portraitCanvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var backdrop = PBox(canvasObject.transform, "Backdrop", new Rect(0, 0, 720, 1280), PInk);
            PStretch(backdrop.rectTransform);
            _portraitSafe = PRect(canvasObject.transform, "Safe area", new Rect());
            _portraitFrame = PRect(_portraitSafe, "Portrait table", new Rect());
            _portraitFrame.anchorMin = new Vector2(.5f, 0);
            _portraitFrame.anchorMax = new Vector2(.5f, 1);
            _portraitFrame.pivot = new Vector2(.5f, .5f);
            _portraitFrame.anchoredPosition = Vector2.zero;
            _portraitFrame.sizeDelta = new Vector2(720, 0);
            _portraitBoard = PRect(_portraitFrame, "Board", new Rect()); PStretch(_portraitBoard);
            _portraitOverlay = PRect(_portraitFrame, "Overlays", new Rect()); PStretch(_portraitOverlay);
            var arrowObject = new GameObject("Attack arrow", typeof(RectTransform), typeof(PortraitAttackArrow));
            arrowObject.transform.SetParent(_portraitFrame, false);
            _portraitArrow = arrowObject.GetComponent<PortraitAttackArrow>();
            PStretch(_portraitArrow.rectTransform);
            _portraitArrow.color = new Color(1f, .20f, .22f, .96f);
            _portraitArrow.raycastTarget = false;
            arrowObject.SetActive(false);
            // Use the project's selected input backend. This also allows the
            // existing Old-input Android test build to run without a restart.
            if (EventSystem.current == null)
            {
                _portraitEventSystem = new GameObject("Duel EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                _portraitEventSystem.AddComponent<InputSystemUIInputModule>();
#else
                _portraitEventSystem.AddComponent<StandaloneInputModule>();
#endif
            }
            else
            {
#if ENABLE_INPUT_SYSTEM
                var oldModule = EventSystem.current.GetComponent<StandaloneInputModule>();
                if (oldModule != null) oldModule.enabled = false;
                if (EventSystem.current.GetComponent<InputSystemUIInputModule>() == null)
                    EventSystem.current.gameObject.AddComponent<InputSystemUIInputModule>();
#endif
            }
        }

        private void Update()
        {
            if (_portraitCanvas == null) return;
            var safe = Screen.safeArea;
            if (_portraitScreenWidth != Screen.width || _portraitScreenHeight != Screen.height || !_portraitLastSafe.Equals(safe))
            {
                _portraitScreenWidth = Screen.width; _portraitScreenHeight = Screen.height; _portraitLastSafe = safe;
                _portraitSafe.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
                _portraitSafe.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
                _portraitSafe.offsetMin = _portraitSafe.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                _portraitDirty = true;
            }
            _portraitFrame.sizeDelta = new Vector2(Mathf.Min(720, _portraitSafe.rect.width), 0);
            var width = _portraitFrame.rect.width;
            var height = _portraitFrame.rect.height;
            if (Mathf.Abs(width - _pw) > 1 || Mathf.Abs(height - _ph) > 1)
            {
                PCancelGesture(); _pw = width; _ph = height; _portraitDirty = true;
            }
            foreach (var art in _portraitArt)
                if (art.Image != null && art.Image.texture == null && _cardArt.TryGetValue(art.Id, out var texture))
                { art.Image.texture = texture; if (art.Fallback != null) art.Fallback.SetActive(false); }
            var signature = (_state?.GetHashCode().ToString() ?? "setup") + ":" + _showDeckSetup + ":" + _busy + ":" +
                _actionDepth + ":" + _actionSession?.PendingChoice?.GetHashCode() + ":" + _inspectionReview?.GetHashCode() +
                ":" + _awaitingHumanBlock + ":" + _showVideo + ":" + _state?.PendingShieldTriggers.Count;
            if (signature != _portraitSignature) { _portraitSignature = signature; _portraitDirty = true; }
            if (_portraitLastState != _state)
            {
                _portraitLastState = _state;
                PRefreshPreview();
            }
            if (_portraitStatus != null) _portraitStatus.text = _message;
            if (_portraitScrubbing) PHighlightHand(_portraitLast);
            if (_portraitGestureActive && (_busy || _actionDepth > 0 || _showDeckSetup)) PCancelGesture();
            if (_portraitDirty && !_portraitGestureActive)
            {
                _portraitDirty = false;
                RenderPortrait();
            }
        }

        private void RenderPortrait()
        {
            PClear(_portraitBoard); PClear(_portraitOverlay);
            _portraitTargets.Clear(); _portraitHandCards.Clear(); _portraitArt.Clear();
            _portraitStatus = null;
            if (_showDeckSetup || _state == null) { PSetup(); return; }
            PDrawTable();
            if (_showVideo) { PVideo(); return; }
            if (_inspectionReview != null || _actionDepth > 0) { PChoice(); return; }
            if (_state.IsFinished) { PWinner(); return; }
            if (_awaitingHumanBlock) { PBlockChoice(); return; }
            var shieldTriggers = _state.PendingShieldTriggers.Where(c => _state.Players[HumanId].Hand.Contains(c)).ToList();
            if (shieldTriggers.Count > 0)
            {
                if (_portraitPreview == null || !_portraitPreview.Any(c => shieldTriggers.Contains(c)))
                    POpenPreview(shieldTriggers, 0, "Shield Trigger");
            }
            if (_portraitModal != null) PMenu();
            else if (_portraitPreview != null) PPreview();
        }

        private void PDrawTable()
        {
            var field = _ph - 132f;
            var split = 64f + field * .40f;
            PBox(_portraitBoard, "Opponent table", new Rect(6, 68, _pw - 12, split - 68), new Color(.055f, .115f, .155f));
            PBox(_portraitBoard, "Your table", new Rect(6, split + 6, _pw - 12, _ph - split - 76), new Color(.075f, .16f, .175f));
            PBox(_portraitBoard, "Battle line", new Rect(26, split, _pw - 52, 2), new Color(.65f, .52f, .28f, .7f));
            PLabel(_portraitBoard, new Rect(24, 8, 190, 44), "DUEL MASTERS", 24, PGold, TextAnchor.MiddleLeft);
            PLabel(_portraitBoard, new Rect(220, 8, _pw - 484, 44),
                "TURN " + _state.TurnNumber + (_state.ActivePlayerId == HumanId ? "  ·  YOUR TURN" : "  ·  OPPONENT"),
                19, Color.white);
            PButton(_portraitBoard, new Rect(_pw - 94, 10, 74, 44), "☰", () => { _portraitModal = "menu"; _portraitDirty = true; });
            PDrawSide(_state.Players[ComputerId], 64, field * .40f, true);
            PDrawSide(_state.Players[HumanId], split + 6, field * .60f - 6, false);
            var canEnd = PCanAct && _state.PendingShieldTriggers.Count == 0;
            PButton(_portraitBoard, new Rect(_pw - 256, 8, 152, 44), "END TURN", () => EndHumanTurn(), canEnd, PGold);
            _portraitStatus = PLabel(_portraitBoard, new Rect(20, _ph - 64, _pw - 40, 56), _message, 21,
                new Color(.75f, .85f, .88f));
        }

        private void PDrawSide(PlayerState player, float y, float height, bool opponent)
        {
            var battleY = opponent ? y + height - 166 : y + 24;
            var manaY = opponent ? y + height * .42f : y + height * .31f;
            var shieldsY = opponent ? y + height * .25f : y + height * .52f;
            var handY = opponent ? y + 12 : y + height * .74f;
            PAvatar(player, new Rect(opponent ? 18 : _pw - 104, handY + (opponent ? 2 : 44), 86, 86), opponent);
            if (opponent)
            {
                var count = Math.Min(player.Hand.Count, 9);
                for (var i = 0; i < count; i++)
                    PCard(_portraitBoard, null, new Rect(_pw * .5f - count * 14 + i * 28, handY, 52, 72), true);
                PLabel(_portraitBoard, new Rect(122, handY + 74, _pw - 244, 28), "OPPONENT HAND · " + player.Hand.Count, 20, new Color(.62f, .73f, .78f));
            }
            else PHand(player.Hand, new Rect(12, handY, _pw - 126, Mathf.Max(130, height * .25f)));
            PShieldRow(player, new Rect(120, shieldsY, _pw - 240, opponent ? 64 : 92), opponent);
            PManaRow(player, new Rect(110, manaY, _pw - 220, opponent ? 94 : 122), opponent);
            PZoneStack(player.Graveyard, new Rect(14, battleY + 28, 76, 104), "Graveyard", false,
                () => POpenZone(player.Graveyard, opponent ? "Opponent graveyard" : "Your graveyard"));
            PZoneStack(player.Deck, new Rect(_pw - 90, battleY + 28, 76, 104), "DECK", true,
                () => { _message = (opponent ? "Opponent" : "Your") + " deck: " + player.Deck.Count + " cards."; });
            PBattleRow(player, new Rect(104, battleY, _pw - 208, 156), opponent);
        }

        private void PAvatar(PlayerState player, Rect area, bool opponent)
        {
            var image = PBox(_portraitBoard, "Avatar", area, opponent ? new Color(.38f, .17f, .19f) : new Color(.12f, .37f, .39f));
            image.sprite = _portraitCircle; image.type = Image.Type.Simple;
            var rim = image.gameObject.AddComponent<Outline>(); rim.effectColor = opponent ? new Color(.8f, .39f, .34f) : PGold;
            rim.effectDistance = new Vector2(2, 2);
            PLabel(image.transform, new Rect(4, 8, area.width - 8, area.height - 16), opponent ? "RIVAL" : "YOU", 21, Color.white);
            if (opponent) _portraitTargets.Add(new PortraitTarget { Rect = image.rectTransform, Border = image, Kind = "player" });
        }

        private void PZoneStack(List<CardInstance> cards, Rect area, string label, bool hidden, Action tap)
        {
            var pile = PRect(_portraitBoard, label + " pile", area);
            if (cards.Count == 0)
            {
                PBox(pile, "Empty " + label, new Rect(0, 0, area.width, area.height), new Color(.08f, .15f, .19f));
                PLabel(pile, new Rect(0, 20, area.width, 40), "EMPTY", 18, new Color(.52f, .65f, .68f));
            }
            else
            {
                var layers = hidden ? 1 : Math.Min(4, cards.Count);
                for (var i = 0; i < layers; i++)
                {
                    var rect = PCard(pile, cards[cards.Count - layers + i], new Rect(0, 0, area.width, area.height), hidden);
                    var angle = hidden ? 0 : new[] { -7f, 6f, -4f, 3f }[i];
                    POrientCard(rect, new Vector2(area.width * .5f + (i % 2 == 0 ? -2 : 2), -area.height * .5f - i), angle);
                }
            }
            PLabel(pile, new Rect(0, area.height - 30, area.width, 30), cards.Count.ToString(), 24, Color.white, TextAnchor.MiddleCenter, true);
            PLabel(_portraitBoard, new Rect(Mathf.Clamp(area.x - 20, 2, _pw - area.width - 42), area.y + area.height + 8, area.width + 40, 28), label, 20, new Color(.76f, .86f, .88f));
            pile.gameObject.AddComponent<PortraitPointer>().Tap = _ => { tap(); _portraitDirty = true; };
        }

        private void PManaRow(PlayerState player, Rect area, bool opponent)
        {
            var panel = PBox(_portraitBoard, opponent ? "Opponent mana row" : "Your mana row", area, new Color(.075f, .18f, .20f)).rectTransform;
            PLabel(panel, new Rect(8, 0, area.width - 16, 26),
                "MANA · " + player.Mana.Count(c => !c.Tapped) + " / " + player.Mana.Count + " READY", 21, new Color(.76f, .88f, .88f));
            var cardHeight = opponent ? 60f : 86f;
            var cardWidth = cardHeight * 491f / 682f;
            var stride = cardHeight + 14;
            var viewport = new Rect(6, 28, area.width - 12, area.height - 30);
            var contentWidth = Mathf.Max(viewport.width, player.Mana.Count * stride);
            var scroll = PScroll(panel, viewport, true, contentWidth);
            scroll.horizontalNormalizedPosition = opponent ? _portraitOpponentManaRowOffset : _portraitManaRowOffset;
            scroll.onValueChanged.AddListener(v => { if (opponent) _portraitOpponentManaRowOffset = v.x; else _portraitManaRowOffset = v.x; });
            var start = Mathf.Max(0, (viewport.width - player.Mana.Count * stride) * .5f);
            for (var i = 0; i < player.Mana.Count; i++)
            {
                var card = player.Mana[i];
                var rect = PCard(scroll.content, card, new Rect(0, 0, cardWidth, cardHeight));
                // Mana is face up and inverted. Tapping adds a quarter turn;
                // only the card face rotates, so the header stays readable.
                POrientCard(rect, new Vector2(start + (i + .5f) * stride, -viewport.height * .5f), card.Tapped ? 90 : 180);
                PAttachCardInput(rect, card, player.Mana, opponent ? "Opponent mana" : "Your mana", false, false, scroll);
            }
            if (player.Mana.Count == 0)
                PLabel(scroll.content, new Rect(0, 0, viewport.width, viewport.height), "NO MANA", 20, new Color(.49f, .65f, .67f));
        }

        private static void POrientCard(RectTransform rect, Vector2 center, float degrees)
        {
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = center;
            rect.localRotation = Quaternion.Euler(0, 0, degrees);
        }

        private void PShieldRow(PlayerState player, Rect area, bool opponent)
        {
            var n = player.Shields.Count;
            if (n == 0) { PLabel(_portraitBoard, area, "NO SHIELDS", 18, new Color(.48f, .64f, .70f)); return; }
            var width = Mathf.Min(58, (area.width - 8 * (Math.Min(n, 5) - 1)) / Math.Min(n, 5));
            var scroll = PScroll(_portraitBoard, area, true, Math.Max(area.width, n * (width + 9)));
            var start = n <= 5 ? (area.width - n * (width + 9) + 9) * .5f : 0;
            for (var i = 0; i < n; i++)
            {
                var card = player.Shields[i];
                var shield = PBox(scroll.content, "Shield " + (i + 1), new Rect(start + i * (width + 9), 8, width, area.height - 12), new Color(.15f, .58f, .85f, .14f));
                var graphicRect = PRect(shield.transform, "Luminous shield", new Rect(0, 0, width, area.height - 12));
                var graphic = graphicRect.gameObject.AddComponent<PortraitShieldGraphic>();
                graphic.color = new Color(.24f, .80f, 1f, .95f); graphic.raycastTarget = false;
                PLabel(shield.transform, new Rect(0, 5, width, area.height - 26), card.FaceUp ? "◈" : "", 30, new Color(.72f, .96f, 1f));
                if (card.FaceUp)
                {
                    var index = player.Shields.Where(c => c.FaceUp).ToList().IndexOf(card);
                    shield.gameObject.AddComponent<PortraitPointer>().Tap = _ => POpenPreview(player.Shields.Where(c => c.FaceUp).ToList(), index, "Face-up shields");
                }
                if (opponent) _portraitTargets.Add(new PortraitTarget { Rect = shield.rectTransform, Border = shield, Id = card.InstanceId, Kind = "shield" });
            }
            if (opponent) _portraitTargets.Add(new PortraitTarget { Rect = scroll.viewport, Border = scroll.GetComponent<Image>(), Kind = "shieldzone" });
        }

        private void PBattleRow(PlayerState player, Rect area, bool opponent)
        {
            if (player.BattleZone.Count == 0) { PLabel(_portraitBoard, area, "BATTLE ZONE", 19, new Color(.31f, .46f, .49f)); return; }
            const float stride = 142f;
            const float slotWidth = 132f;
            var contentWidth = Mathf.Max(area.width, player.BattleZone.Count * stride - 10);
            var scroll = PScroll(_portraitBoard, area, true, contentWidth);
            scroll.horizontalNormalizedPosition = opponent ? _portraitOpponentRowOffset : _portraitRowOffset;
            scroll.onValueChanged.AddListener(v => { if (opponent) _portraitOpponentRowOffset = v.x; else _portraitRowOffset = v.x; });
            var start = Math.Max(0, (area.width - player.BattleZone.Count * stride + 10) * .5f);
            for (var i = 0; i < player.BattleZone.Count; i++)
            {
                var card = player.BattleZone[i];
                var slot = PRect(scroll.content, "Battle card slot", new Rect(start + i * stride, 0, slotWidth, area.height));
                var rect = PCard(slot, card, new Rect(0, 0, 96, 126));
                POrientCard(rect, new Vector2(slotWidth * .5f, -91), card.Tapped ? -90 : 0);
                PLabel(slot, new Rect(0, 0, slotWidth, 28), _engine.CurrentPower(_state, card).ToString(),
                    26, PGold, TextAnchor.MiddleCenter, true);
                if (_engine.CurrentPower(_state, card, true) != _engine.CurrentPower(_state, card))
                    PLabel(slot, new Rect(0, 132, slotWidth, 24), "ATK " + _engine.CurrentPower(_state, card, true), 18, PGold, TextAnchor.MiddleCenter, true);
                if (opponent) _portraitTargets.Add(new PortraitTarget { Rect = rect, Border = rect.GetComponent<Image>(), Id = card.InstanceId, Kind = "creature" });
                PAttachCardInput(rect, card, player.BattleZone, opponent ? "Opponent battle zone" : "Your battle zone", false, !opponent, scroll);
            }
        }

        private void PHand(List<CardInstance> cards, Rect area)
        {
            var stride = cards.Count <= 1 ? 0 : Mathf.Clamp((area.width - 114) / (cards.Count - 1), 36, 80);
            var width = Mathf.Max(area.width, (cards.Count - 1) * stride + 114);
            var scroll = PScroll(_portraitBoard, area, true, width);
            scroll.horizontalNormalizedPosition = _portraitHandOffset;
            scroll.onValueChanged.AddListener(v => _portraitHandOffset = v.x);
            var start = Mathf.Max(0, (area.width - (cards.Count - 1) * stride - 114) * .5f);
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var t = cards.Count <= 1 ? 0 : (i / (float)(cards.Count - 1) - .5f);
                var rect = PCard(scroll.content, card, new Rect(start + i * stride, 17 + 16 * Mathf.Abs(t), 114, 158));
                rect.localRotation = Quaternion.Euler(0, 0, -t * 13);
                _portraitHandCards[card.InstanceId] = rect;
                PAttachCardInput(rect, card, cards, "Your hand", true, false, scroll);
            }
            PLabel(_portraitBoard, new Rect(area.x, area.y - 12, area.width, 28), "HAND · " + cards.Count + "   TAP TO VIEW · HOLD TO BROWSE", 18, new Color(.59f, .75f, .78f));
        }

        private RectTransform PCard(Transform parent, CardInstance card, Rect area, bool hidden = false)
        {
            var frame = PBox(parent, hidden || card == null ? "Hidden card" : card.Definition.Name, area,
                card != null && !hidden ? CivilizationColor(card.Definition) : new Color(.12f, .23f, .30f));
            frame.raycastTarget = true;
            var inset = new Rect(3, 3, area.width - 6, area.height - 6);
            var artRect = PRect(frame.transform, "Card art", inset);
            var art = artRect.gameObject.AddComponent<RawImage>(); art.raycastTarget = false;
            if (hidden || card == null)
            {
                art.texture = _cardBack;
                if (_cardBack == null) PLabel(frame.transform, inset, "◇\nDUEL", 16, PCyan);
            }
            else
            {
                if (_cardArt.TryGetValue(card.Definition.Id, out var texture)) art.texture = texture;
                GameObject fallback = null;
                if (art.texture == null)
                    fallback = PLabel(frame.transform, new Rect(7, 14, area.width - 14, area.height - 28), card.Definition.Name, area.width > 200 ? 24 : 14, Color.white).gameObject;
                _portraitArt.Add(new PortraitArt { Image = art, Id = card.Definition.Id, Fallback = fallback });
                PLabel(frame.transform, new Rect(3, 3, Mathf.Max(24, area.width * .22f), Mathf.Max(24, area.width * .22f)),
                    (_state != null && _engine != null ? _engine.MinimumManaCost(_state, card) : card.Definition.Cost).ToString(),
                    area.width > 200 ? 28 : 17, Color.white, TextAnchor.MiddleCenter, true);
                if (card.EvolutionMaterials.Count > 0)
                    PLabel(frame.transform, new Rect(0, area.height - 24, area.width, 22), "+" + PCountMaterials(card) + " UNDER", 13, PCyan, TextAnchor.MiddleCenter, true);
            }
            return frame.rectTransform;
        }

        private static int PCountMaterials(CardInstance card) => card.EvolutionMaterials.Sum(c => 1 + PCountMaterials(c));
        private bool PCanAct => _state != null && !_state.IsFinished && !_busy && _actionDepth == 0 &&
            _state.ActivePlayerId == HumanId && !_awaitingHumanBlock;

        private void PAttachCardInput(RectTransform rect, CardInstance card, List<CardInstance> zone,
            string title, bool hand, bool battle, ScrollRect scroll)
        {
            var input = rect.gameObject.AddComponent<PortraitPointer>();
            input.LogicalScale = _portraitCanvas.scaleFactor;
            input.Press = pos =>
            {
                _portraitGestureActive = true; _portraitPress = _portraitLast = pos;
                _portraitPressedCard = card; _portraitSource = rect; _portraitGestureScroll = scroll;
                _portraitScrubbing = _portraitDragging = _portraitScrolling = false;
                _portraitScrubId = card.InstanceId;
                if (scroll != null) scroll.StopMovement();
            };
            input.Hold = pos => { if (hand) { _portraitScrubbing = true; PHighlightHand(pos); } };
            input.Move = pos =>
            {
                var delta = (pos - _portraitPress) / _portraitCanvas.scaleFactor;
                if (hand && _portraitScrubbing) PHighlightHand(pos);
                else if (battle && !_portraitScrolling && PCanAct &&
                    (_engine.ValidateAttack(_state, HumanId, card.InstanceId, AttackTargetKind.Player).Success ||
                     _engine.GetAttackTargetCandidates(_state, HumanId, card.InstanceId).Count > 0) &&
                    Mathf.Abs(delta.y) > 14)
                { _portraitDragging = true; PDrawAttackArrow(pos); }
                else if (!_portraitDragging && Mathf.Abs(delta.x) > 10 && scroll != null)
                {
                    _portraitScrolling = true;
                    var p = scroll.content.anchoredPosition;
                    p.x += (pos.x - _portraitLast.x) / _portraitCanvas.scaleFactor;
                    p.x = Mathf.Clamp(p.x, -Mathf.Max(0, scroll.content.rect.width - scroll.viewport.rect.width), 0);
                    scroll.content.anchoredPosition = p;
                }
                if (_portraitDragging) PDrawAttackArrow(pos);
                _portraitLast = pos;
            };
            input.Tap = pos => { PCancelGesture(); POpenPreview(zone, zone.IndexOf(card), title); };
            input.Release = pos =>
            {
                var target = _portraitDragging ? PAttackTarget(pos) : null;
                var scrubbed = _portraitScrubbing ? zone.FindIndex(c => c.InstanceId == _portraitScrubId) : -1;
                PCancelGesture();
                if (target != null) PCommitAttack(card, target);
                else if (scrubbed >= 0) POpenPreview(zone, scrubbed, title);
            };
            input.Cancel = PCancelGesture;
        }

        private void PHighlightHand(Vector2 screen)
        {
            var nearest = _portraitHandCards.OrderBy(pair => Mathf.Abs(RectTransformUtility.WorldToScreenPoint(null,
                pair.Value.TransformPoint(pair.Value.rect.center)).x - screen.x)).FirstOrDefault();
            if (nearest.Value == null) return;
            _portraitScrubId = nearest.Key;
            foreach (var pair in _portraitHandCards)
            {
                pair.Value.localScale = pair.Key == _portraitScrubId ? Vector3.one * 1.12f : Vector3.one;
                var image = pair.Value.GetComponent<Image>();
                image.color = pair.Key == _portraitScrubId ? PGold : CivilizationColor(_state.Players[HumanId].Hand.First(c => c.InstanceId == pair.Key).Definition);
            }
            nearest.Value.SetAsLastSibling();
            if (_portraitGestureScroll != null && _portraitGestureScroll.content.rect.width > _portraitGestureScroll.viewport.rect.width)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_portraitGestureScroll.viewport, screen, null, out var local);
                var edge = _portraitGestureScroll.viewport.rect;
                var velocity = local.x < edge.xMin + 34 ? 320 : local.x > edge.xMax - 34 ? -320 : 0;
                var p = _portraitGestureScroll.content.anchoredPosition;
                p.x = Mathf.Clamp(p.x + velocity * Time.unscaledDeltaTime, -(_portraitGestureScroll.content.rect.width - edge.width), 0);
                _portraitGestureScroll.content.anchoredPosition = p;
            }
        }

        private PortraitTarget PAttackTarget(Vector2 screen)
        {
            if (_portraitPressedCard == null || !PCanAct || _portraitSource == null ||
                RectTransformUtility.RectangleContainsScreenPoint(_portraitSource, screen)) return null;
            foreach (var target in _portraitTargets)
            {
                var canChooseShield = _engine.AttackerChoosesShields(_state, _portraitPressedCard, ComputerId);
                if (target.Kind == "shield" && !canChooseShield || target.Kind == "shieldzone" && canChooseShield) continue;
                // Reject clipped cards: a horizontally scrolled creature may
                // geometrically overlap the deck outside its viewport.
                var scroll = target.Rect.GetComponentInParent<ScrollRect>();
                if (scroll != null && !RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, screen)) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(target.Rect, screen)) continue;
                var check = _engine.ValidateAttack(_state, HumanId, _portraitPressedCard.InstanceId,
                    target.Kind == "creature" ? AttackTargetKind.Creature : AttackTargetKind.Player,
                    target.Kind == "creature" ? target.Id : 0);
                if (check.Success) return target;
            }
            return null;
        }

        private void PDrawAttackArrow(Vector2 screen)
        {
            _portraitArrow.gameObject.SetActive(true);
            var target = PAttackTarget(screen);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_portraitArrow.rectTransform,
                RectTransformUtility.WorldToScreenPoint(null, _portraitSource.TransformPoint(_portraitSource.rect.center)), null, out var from);
            var end = target == null ? screen : RectTransformUtility.WorldToScreenPoint(null, target.Rect.TransformPoint(target.Rect.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_portraitArrow.rectTransform, end, null, out var to);
            _portraitArrow.SetPoints(from, to);
            foreach (var item in _portraitTargets)
            {
                var outline = item.Border.GetComponent<Outline>();
                if (outline == null) outline = item.Border.gameObject.AddComponent<Outline>();
                outline.effectColor = item == target ? new Color(1, .25f, .23f) : new Color(.15f, .65f, .8f, .45f);
                outline.effectDistance = item == target ? new Vector2(3, 3) : new Vector2(1, 1);
                var canChoose = _engine.AttackerChoosesShields(_state, _portraitPressedCard, ComputerId);
                var eligible = !(item.Kind == "shield" && !canChoose || item.Kind == "shieldzone" && canChoose) &&
                    _engine.ValidateAttack(_state, HumanId, _portraitPressedCard.InstanceId,
                        item.Kind == "creature" ? AttackTargetKind.Creature : AttackTargetKind.Player,
                        item.Kind == "creature" ? item.Id : 0).Success;
                outline.enabled = eligible;
            }
        }

        private void PCancelGesture()
        {
            _portraitGestureActive = _portraitScrubbing = _portraitDragging = _portraitScrolling = false;
            _portraitPressedCard = null;
            if (_portraitArrow != null) _portraitArrow.gameObject.SetActive(false);
            _portraitDirty = true;
        }

        private void PCommitAttack(CardInstance card, PortraitTarget target)
        {
            var id = card.InstanceId;
            var kind = target.Kind == "creature" ? AttackTargetKind.Creature : AttackTargetKind.Player;
            QueueAction((engine, state) => engine.DeclareAttack(state, HumanId, id, kind,
                target.Kind == "creature" ? target.Id : 0, target.Kind == "shield" ? target.Id : 0), result =>
            {
                ShowResult(result);
                if (!result.Success) return;
                _busy = true;
                StartCoroutine(ResolveHumanAttack(card));
            });
        }

        private void POpenZone(List<CardInstance> cards, string title)
        {
            if (cards.Count == 0) { _message = title + " is empty."; return; }
            POpenPreview(cards, cards.Count - 1, title);
        }

        private void POpenPreview(List<CardInstance> cards, int index, string title)
        {
            if (cards.Count == 0) return;
            _portraitPreview = cards.ToList(); _portraitPreviewIndex = Mathf.Clamp(index, 0, cards.Count - 1);
            _portraitPreviewZone = title; _portraitManualMana = _portraitRules = false;
            _portraitManaOffset = 1f;
            _portraitManaIds.Clear(); _portraitDirty = true;
        }

        private void PClosePreview()
        {
            _portraitPreview = null; _portraitModal = null;
            _portraitManualMana = false; _portraitDirty = true;
        }

        private void PRefreshPreview()
        {
            if (_portraitPreview == null || _state == null) return;
            if (_portraitPreviewZone == "Your hand") _portraitPreview = _state.Players[HumanId].Hand.ToList();
            else
            {
                var visible = _state.Players.SelectMany(p => p.BattleZone.SelectMany(PEvolutionPile)
                    .Concat(p.Mana).Concat(p.Graveyard).Concat(p.Shields.Where(c => c.FaceUp)))
                    .Concat(_state.Players[HumanId].Hand).ToList();
                var selectedId = _portraitPreview.ElementAtOrDefault(_portraitPreviewIndex)?.InstanceId;
                _portraitPreview = _portraitPreview.Select(old => visible.FirstOrDefault(c => c.InstanceId == old.InstanceId))
                    .Where(c => c != null).ToList();
                var index = _portraitPreview.FindIndex(c => c.InstanceId == selectedId);
                if (index >= 0) _portraitPreviewIndex = index;
            }
            if (_portraitPreview.Count == 0) PClosePreview();
        }

        private RectTransform PModal(string title, float top = .08f, float bottom = .92f, bool closable = true,
            float dim = .88f, bool solid = true)
        {
            PBox(_portraitOverlay, "Dim board", new Rect(0, 0, _pw, _ph), new Color(.01f, .025f, .04f, dim));
            var panel = PBox(_portraitOverlay, title, new Rect(14, _ph * top, _pw - 28, _ph * (bottom - top)),
                solid ? PPanel : new Color(0, 0, 0, 0)).rectTransform;
            PLabel(panel, new Rect(20, 14, _pw - 130, 46), title, 23, PGold, TextAnchor.MiddleLeft);
            if (closable) PButton(panel, new Rect(_pw - 118, 8, 76, 64), "X", PClosePreview);
            return panel;
        }

        private void PPreview()
        {
            if (_portraitPreview.Count == 0) { PClosePreview(); return; }
            _portraitPreviewIndex = Mathf.Clamp(_portraitPreviewIndex, 0, _portraitPreview.Count - 1);
            var card = _portraitPreview[_portraitPreviewIndex];
            var panel = PModal(_portraitPreviewZone + "  ·  " + (_portraitPreviewIndex + 1) + " / " + _portraitPreview.Count,
                .02f, .98f, true, .76f, false);
            var w = _pw - 28;
            if (_portraitRules)
            {
                var ruleText = LiveAbilityText(card) + "\n\nPrinted text\n" + card.Definition.RulesText;
                var available = panel.rect.height - 190;
                var scroll = PScroll(panel, new Rect(18, 82, w - 36, available), false,
                    Mathf.Max(available, PTextHeight(ruleText, 25, w - 74) + 24));
                PLabel(scroll.content, new Rect(10, 4, w - 74, scroll.content.sizeDelta.y - 8), ruleText, 25, Color.white, TextAnchor.UpperLeft);
                PButton(panel, new Rect(20, panel.rect.height - 92, w - 40, 76), "BACK TO CARD", () => { _portraitRules = false; _portraitDirty = true; });
                return;
            }
            if (_portraitManualMana) { PManualPayment(panel, card, 82); return; }
            var height = _ph * .50f;
            var cardTop = _ph * .23f;
            var cardWidth = Mathf.Min(w - 176, height * .70f);
            var art = PCard(panel, card, new Rect((w - cardWidth) / 2, cardTop, cardWidth, height));
            var pointer = art.gameObject.AddComponent<PortraitPointer>();
            pointer.LogicalScale = _portraitCanvas.scaleFactor;
            pointer.Press = pos => { _portraitPress = pos; _portraitGestureActive = true; };
            pointer.Tap = _ => PCancelGesture();
            pointer.Release = pos =>
            {
                var dx = (pos.x - _portraitPress.x) / _portraitCanvas.scaleFactor;
                PCancelGesture();
                if (Mathf.Abs(dx) > 42) PNavigatePreview(dx < 0 ? 1 : -1);
            };
            pointer.Cancel = PCancelGesture;
            PButton(panel, new Rect(4, cardTop + height * .45f, 64, 88), "‹", () => PNavigatePreview(-1));
            PButton(panel, new Rect(w - 68, cardTop + height * .45f, 64, 88), "›", () => PNavigatePreview(1));
            PLabel(panel, new Rect(20, cardTop - 104, w - 40, 62), card.Definition.Name, 27, Color.white);
            PLabel(panel, new Rect(20, cardTop - 36, w - 40, 28), "MANA " + ManaCostText(card) +
                (card.Definition.IsCreature ? "    POWER " + _engine.CurrentPower(_state, card) : "    SPELL"), 23, PGold);
            // All contextual actions scroll on short devices and with long
            // ability menus. The card itself remains centred at half-screen height.
            var actionY = cardTop + height + 10;
            var actions = PScroll(panel, new Rect(0, actionY, w, panel.rect.height - actionY - 10), false, 470);
            var actionsHeight = PPreviewActions(actions.content, card, 0);
            actions.content.sizeDelta = new Vector2(w, Mathf.Max(actions.viewport.rect.height, actionsHeight + 8));
        }

        private void PNavigatePreview(int delta)
        {
            _portraitPreviewIndex = (_portraitPreviewIndex + delta + _portraitPreview.Count) % _portraitPreview.Count;
            _portraitManualMana = _portraitRules = false; _portraitDirty = true;
        }

        private float PPreviewActions(RectTransform panel, CardInstance card, float y)
        {
            var width = _pw - 68;
            var hand = _state.Players[HumanId].Hand.Contains(card);
            var ownBattle = _state.Players[HumanId].BattleZone.Contains(card);
            var trigger = hand && _state.PendingShieldTriggers.Contains(card);
            if (trigger)
            {
                PAction(panel, ref y, "SHIELD TRIGGER · PLAY FREE", () => { PClosePreview(); CastHumanShieldTrigger(card); }, !_busy && _actionDepth == 0, PCyan);
                PAction(panel, ref y, "KEEP IN HAND", () => { PClosePreview(); QueueAction((e, s) => e.DeclineShieldTrigger(s, HumanId, card.InstanceId), r => ShowResult(r)); });
            }
            else if (hand)
            {
                var ids = _engine.FindManaPayment(_state, HumanId, card);
                var enough = _engine.ValidatePayment(_state, HumanId, card, ids).Success;
                var legalBase = card.Definition.Type != CardType.EvolutionCreature || _engine.GetEvolutionBaseCandidates(_state, HumanId, card.InstanceId).Count > 0;
                PButton(panel, new Rect(20, y, (width - 12) * .5f, 84), card.Definition.Type == CardType.Spell ? "CAST SPELL" : "SUMMON",
                    () => PPlayCard(card, ids), PCanAct && enough && legalBase, PGold);
                PButton(panel, new Rect(26 + width * .5f, y, (width - 12) * .5f, 84), "CHARGE MANA", () =>
                {
                    PClosePreview(); QueueAction((e, s) => e.ChargeMana(s, HumanId, card.InstanceId), r => ShowResult(r, r.Success ? "Charged " + card.Definition.Name + "." : null));
                }, PCanAct && !_state.ManaChargedThisTurn && !_state.MainActionTakenThisTurn);
                y += 94;
                PAction(panel, ref y, "CHOOSE MANA MANUALLY  ·  " + ids.Count + " AUTO-SELECTED", () =>
                {
                    _portraitManaIds.Clear(); foreach (var id in ids) _portraitManaIds.Add(id);
                    _portraitPaymentCard = card.InstanceId; _portraitManualMana = true; _portraitDirty = true;
                }, PCanAct);
                if (!enough || !legalBase)
                {
                    PLabel(panel, new Rect(20, y, width, 38), !legalBase ? "No legal evolution base is available." : "Not enough matching, untapped mana.", 17, new Color(.95f, .65f, .43f)); y += 42;
                }
            }
            else if (ownBattle)
            {
                if (_engine.HasTapAbility(_state, card))
                    PAction(panel, ref y, "USE TAP ABILITY", () =>
                    {
                        PClosePreview(); QueueAction((e, s) => e.UseTapAbility(s, HumanId, card.InstanceId), r => ShowResult(r));
                    }, PCanAct && _engine.ValidateTapAbility(_state, HumanId, card.InstanceId).Success, PCyan);
                PLabel(panel, new Rect(20, y, width, 44), "Close this card, then drag it onto a legal target to attack.", 17, new Color(.70f, .82f, .85f)); y += 48;
            }
            if (card.EvolutionMaterials.Count > 0)
                PAction(panel, ref y, "VIEW " + PCountMaterials(card) + " CARDS UNDER THIS EVOLUTION", () =>
                    POpenPreview(PEvolutionPile(card).Skip(1).ToList(), 0, "Evolution materials"));
            PAction(panel, ref y, "READ LIVE ABILITIES & RULES", () => { _portraitRules = true; _portraitDirty = true; });
            return y;
        }

        private static IEnumerable<CardInstance> PEvolutionPile(CardInstance card)
        {
            yield return card;
            foreach (var material in card.EvolutionMaterials)
                foreach (var nested in PEvolutionPile(material)) yield return nested;
        }

        private void PPlayCard(CardInstance card, IReadOnlyCollection<long> mana)
        {
            var id = card.InstanceId; var spell = card.Definition.Type == CardType.Spell;
            PClosePreview();
            QueueAction((e, s) => spell ? e.CastSpell(s, HumanId, id, mana) : e.SummonCreatureWithChoices(s, HumanId, id, mana),
                r => ShowResult(r, r.Success ? "Played " + card.Definition.Name + "." : null));
        }

        private void PManualPayment(RectTransform panel, CardInstance card, float y)
        {
            var valid = _engine.ValidatePayment(_state, HumanId, card, _portraitManaIds.ToArray());
            PLabel(panel, new Rect(20, y, _pw - 68, 38), "SELECTED " + _portraitManaIds.Count + " / " + ManaCostText(card), 20, PGold);
            y += 44;
            var viewportHeight = Mathf.Max(80, panel.rect.height - y - 112);
            var scroll = PScroll(panel, new Rect(20, y, _pw - 68, viewportHeight), false, _state.Players[HumanId].Mana.Count * 64);
            scroll.verticalNormalizedPosition = _portraitManaOffset;
            scroll.onValueChanged.AddListener(v => _portraitManaOffset = v.y);
            var n = 0;
            foreach (var mana in _state.Players[HumanId].Mana)
            {
                var copy = mana;
                PButton(scroll.content, new Rect(0, n++ * 64, _pw - 92, 56),
                    (_portraitManaIds.Contains(mana.InstanceId) ? "✓ " : "") + mana.Definition.Name + (mana.Tapped ? " · TAPPED" : ""),
                    () => { if (!_portraitManaIds.Remove(copy.InstanceId)) _portraitManaIds.Add(copy.InstanceId); _portraitDirty = true; }, !mana.Tapped,
                    _portraitManaIds.Contains(mana.InstanceId) ? PGold : PPanel);
            }
            PButton(panel, new Rect(20, panel.rect.height - 100, _pw - 68, 44), "CONFIRM PAYMENT & PLAY",
                () => PPlayCard(card, _portraitManaIds.ToArray()), valid.Success, PGold);
            PButton(panel, new Rect(20, panel.rect.height - 50, _pw - 68, 40), "BACK", () => { _portraitManualMana = false; _portraitDirty = true; });
        }

        private void PAction(Transform parent, ref float y, string label, Action action, bool enabled = true, Color? tint = null)
        {
            PButton(parent, new Rect(20, y, _pw - 68, 76), label, action, enabled, tint); y += 86;
        }

        private void PChoice()
        {
            var choice = _actionSession?.PendingChoice;
            if (_portraitChoice != choice)
            {
                _portraitChoice = choice; _portraitChoiceIds.Clear(); _portraitNumber = choice?.Suggested ?? 0;
                _portraitChoiceOffset = 1f;
            }
            var panel = PModal(_inspectionReview != null ? "REVEALED CARDS" : choice?.Source?.Definition.Name ?? "RESOLVING", .08f, .94f, false);
            if (choice == null && _inspectionReview == null)
            { PLabel(panel, new Rect(20, 100, _pw - 68, 80), "Resolving effects…", 24, Color.white); return; }
            var revealed = _inspectionReview ?? choice.RevealedCards;
            var prompt = choice?.Prompt ?? "Review these cards before continuing.";
            if (choice?.MaximumCombinedPower > 0)
                prompt += "\nPower selected: " + _portraitChoiceIds.Sum(id => choice.CardPowers[choice.Cards.ToList().FindIndex(c => c.InstanceId == id)]) + " / " + choice.MaximumCombinedPower;
            var promptHeight = PTextHeight(prompt, 20, _pw - 94) + 16;
            var heights = choice == null ? Array.Empty<float>() : choice.Kind == AbilityChoiceKind.Cards
                ? choice.CardLabels.Select(label => Mathf.Max(112, PTextHeight(label, 23, _pw - 198) + 24)).ToArray()
                : choice.Options.Select(label => Mathf.Max(80, PTextHeight(label, 24, _pw - 114) + 24)).ToArray();
            var revealHeight = revealed.Count * 118;
            var availableHeight = panel.rect.height - 184;
            var scroll = PScroll(panel, new Rect(18, 76, _pw - 64, availableHeight - 24), false,
                Math.Max(availableHeight, promptHeight + revealHeight + heights.Sum() + heights.Length * 10 + 90));
            scroll.verticalNormalizedPosition = _portraitChoiceOffset;
            scroll.onValueChanged.AddListener(v => _portraitChoiceOffset = v.y);
            var content = scroll.content;
            PLabel(content, new Rect(10, 0, _pw - 100, promptHeight), prompt, 20, Color.white, TextAnchor.UpperLeft);
            var y = promptHeight;
            foreach (var card in revealed)
            {
                PCard(content, card, new Rect(10, y, 72, 100));
                PLabel(content, new Rect(96, y, _pw - 190, 108), card.Definition.Name + "\n" + card.Definition.RulesText, 17, Color.white, TextAnchor.UpperLeft); y += 118;
            }
            for (var i = 0; i < heights.Length; i++)
            {
                var index = i;
                var cards = choice.Kind == AbilityChoiceKind.Cards;
                var selectedIndex = cards ? _portraitChoiceIds.IndexOf(choice.Cards[i].InstanceId) : -1;
                var label = cards ? choice.CardLabels[i] : choice.Options[i];
                var button = PButton(content, new Rect(6, y, _pw - 90, heights[i]),
                    (selectedIndex >= 0 ? (selectedIndex + 1) + ". ✓  " : "") + label,
                    () =>
                    {
                        if (!cards) { _actionSession.AnswerValue(index); _portraitDirty = true; return; }
                        var id = choice.Cards[index].InstanceId;
                        if (!_portraitChoiceIds.Remove(id) && _portraitChoiceIds.Count < choice.Maximum)
                        {
                            var power = _portraitChoiceIds.Sum(c => choice.CardPowers[choice.Cards.ToList().FindIndex(item => item.InstanceId == c)]);
                            if (choice.MaximumCombinedPower <= 0 || power + choice.CardPowers[index] <= choice.MaximumCombinedPower) _portraitChoiceIds.Add(id);
                        }
                        _portraitDirty = true;
                    }, true, selectedIndex >= 0 ? PGold : new Color(.13f, .25f, .31f));
                var text = button.GetComponentInChildren<Text>(); text.alignment = TextAnchor.MiddleLeft; text.fontSize = 23;
                if (cards)
                {
                    // CardLabels already encode hidden-zone permissions. Never
                    // use a face-down candidate's definition to draw its art.
                    var hidden = label.Contains("Face-down");
                    PCard(button.transform, choice.Cards[i], new Rect(5, 6, 54, 76), hidden);
                    text.rectTransform.anchoredPosition = new Vector2(70, -8);
                    text.rectTransform.sizeDelta = new Vector2(_pw - 170, heights[i] - 16);
                }
                y += heights[i] + 10;
            }
            if (choice?.Kind == AbilityChoiceKind.Number)
            {
                PButton(content, new Rect(10, y, 84, 58), "−", () => { _portraitNumber = Math.Max(choice.Minimum, _portraitNumber - 1); _portraitDirty = true; });
                PLabel(content, new Rect(108, y, _pw - 312, 58), _portraitNumber.ToString(), 32, PGold);
                PButton(content, new Rect(_pw - 194, y, 84, 58), "+", () => { _portraitNumber = Math.Min(choice.Maximum, _portraitNumber + 1); _portraitDirty = true; });
            }
            var footer = new Rect(20, panel.rect.height - 92, _pw - 68, 76);
            if (_inspectionReview != null)
                PButton(panel, footer, "CONTINUE", () => { _inspectionReview = null; _portraitDirty = true; }, true, PGold);
            else if (choice.Kind == AbilityChoiceKind.Cards)
                PButton(panel, footer, "CONFIRM " + _portraitChoiceIds.Count + " SELECTED",
                    () => { _actionSession.AnswerCards(_portraitChoiceIds.ToArray()); _portraitDirty = true; },
                    _portraitChoiceIds.Count >= choice.Minimum && _portraitChoiceIds.Count <= choice.Maximum, PGold);
            else if (choice.Kind == AbilityChoiceKind.Number)
                PButton(panel, footer, "CONFIRM " + _portraitNumber, () => { _actionSession.AnswerValue(_portraitNumber); _portraitDirty = true; }, true, PGold);
        }

        private void PBlockChoice()
        {
            var panel = PModal("OPPONENT ATTACKS · CHOOSE A BLOCKER", .16f, .86f, false);
            var blockers = EligibleBlockers(_state.Players[HumanId]).ToList();
            var viewport = PScroll(panel, new Rect(20, 76, _pw - 68, panel.rect.height - 232), false, blockers.Count * 100);
            for (var i = 0; i < blockers.Count; i++)
            {
                var card = blockers[i];
                PButton(viewport.content, new Rect(0, i * 100, _pw - 92, 90), card.Definition.Name + "\nPOWER " + _engine.CurrentPower(_state, card), () => ResolveHumanBlock(card.InstanceId));
            }
            var required = blockers.Any(card => card.InstanceId == _state.RequiredBlockerInstanceId ||
                _engine.GetEffectiveKeywords(_state, card).Contains(CardKeyword.BlockIfAble));
            PLabel(panel, new Rect(20, panel.rect.height - 154, _pw - 68, 54), required ? "A card effect requires a block." : _message, 20, Color.white);
            PButton(panel, new Rect(20, panel.rect.height - 92, _pw - 68, 76), required ? "BLOCKING REQUIRED" : "DO NOT BLOCK",
                () => ResolveHumanBlock(0), !required, PGold);
        }

        private void PSetup()
        {
            PLabel(_portraitBoard, new Rect(20, 30, _pw - 40, 58), "DUEL MASTERS", 38, PGold);
            PLabel(_portraitBoard, new Rect(28, 92, _pw - 56, 60), "Choose card pools for this duel. Each deck contains 40 cards.", 21, Color.white);
            var scroll = PScroll(_portraitBoard, new Rect(20, 166, _pw - 40, _ph - 346), false, 1360f);
            var y = 0f;
            foreach (var owner in new[] { HumanId, ComputerId })
            {
                var sets = owner == HumanId ? _humanSelectedSets : _computerSelectedSets;
                var civs = owner == HumanId ? _humanSelectedCivilizations : _computerSelectedCivilizations;
                PLabel(scroll.content, new Rect(8, y, _pw - 80, 40), owner == HumanId ? "YOUR DECK" : "OPPONENT DECK", 25, PGold, TextAnchor.MiddleLeft); y += 50;
                for (var i = 0; i < SelectableSetCodes.Length; i++)
                {
                    var set = SelectableSetCodes[i]; var column = i % 4; var row = i / 4;
                    PButton(scroll.content, new Rect(8 + column * ((_pw - 100) / 4 + 5), y + row * 78, (_pw - 100) / 4, 70),
                        (sets.Contains(set) ? "✓ " : "") + set,
                        () => { if (!sets.Remove(set)) sets.Add(set); _portraitDirty = true; }, true, sets.Contains(set) ? new Color(.30f, .43f, .37f) : PPanel);
                }
                y += 324;
                PButton(scroll.content, new Rect(8, y, _pw - 84, 66), "ALL SETS", () => { foreach (var set in SelectableSetCodes) sets.Add(set); _portraitDirty = true; }); y += 78;
                for (var i = 0; i < SelectableCivilizations.Length; i++)
                {
                    var civ = SelectableCivilizations[i];
                    PButton(scroll.content, new Rect(8 + i % 3 * ((_pw - 100) / 3 + 5), y + i / 3 * 76, (_pw - 100) / 3, 68),
                        (civs.Contains(civ) ? "✓ " : "") + civ,
                        () => { if (!civs.Remove(civ)) civs.Add(civ); _portraitDirty = true; }, true, civs.Contains(civ) ? new Color(.30f, .43f, .37f) : PPanel);
                }
                y += 174;
            }
            scroll.content.sizeDelta = new Vector2(scroll.content.sizeDelta.x, Mathf.Max(scroll.viewport.rect.height, y + 12));
            // Selections rebuild this view. Restore its position after sizing the
            // content, and retain subsequent scrolling for the next rebuild.
            scroll.verticalNormalizedPosition = _portraitSetupOffset;
            scroll.onValueChanged.AddListener(v => _portraitSetupOffset = v.y);

            // Keep the action outside the scrolling content and inside the safe area.
            var footer = PBox(_portraitBoard, "Deck setup footer", new Rect(8, _ph - 164, _pw - 16, 156), PPanel).rectTransform;
            PLabel(footer, new Rect(20, 8, _pw - 56, 62), _setupMessage, 18, new Color(.72f, .83f, .86f));
            PButton(footer, new Rect(20, 82, _pw - 56, 60), "START DUEL", () => { PClosePreview(); StartSelectedMatch(); _portraitDirty = true; }, true, PGold);
        }

        private void PMenu()
        {
            var panel = PModal("DUEL MENU", .22f, .78f);
            var y = 86f;
            PAction(panel, ref y, "RETURN TO DUEL", PClosePreview);
            PAction(panel, ref y, "ATTACK VIDEOS · " + (_animationsEnabled ? "ON" : "OFF"), () => { _animationsEnabled = !_animationsEnabled; _portraitDirty = true; });
            PAction(panel, ref y, "HOW TO PLAY", () => { _portraitModal = "help"; _portraitDirty = true; });
            if (_portraitModal == "help")
                PLabel(panel, new Rect(20, y, _pw - 68, 180), "Tap a hand card to inspect it. Hold and slide across your hand to choose a card.\n\nDrag a ready creature to a legal target. Release elsewhere to cancel. Tap mana or graveyard to inspect it.", 20, Color.white, TextAnchor.UpperLeft);
            else PAction(panel, ref y, "NEW DUEL / CHANGE CARD POOLS", () => { PClosePreview(); ReturnToDeckSetup(); _portraitDirty = true; });
        }

        private void PVideo()
        {
            PBox(_portraitOverlay, "Attack video backdrop", new Rect(0, 0, _pw, _ph), Color.black);
            var availableHeight = Mathf.Max(1, _ph - 144);
            var videoWidth = Mathf.Min(_pw, availableHeight * (16f / 9f));
            var videoHeight = videoWidth * (9f / 16f);
            // Fit explicitly: AspectRatioFitter on a top-left-pivoted rectangle
            // used to shrink the video towards the top instead of centering it.
            var rect = PRect(_portraitOverlay, "Attack video", new Rect((_pw - videoWidth) * .5f,
                (_ph - videoHeight) * .5f, videoWidth, videoHeight));
            var image = rect.gameObject.AddComponent<RawImage>(); image.texture = _videoTexture;
            image.raycastTarget = false;
            PLabel(_portraitOverlay, new Rect(24, 20, _pw - 48, 48), "ATTACK", 24, PGold);
            PButton(_portraitOverlay, new Rect(24, _ph - 84, _pw - 48, 60), "SKIP ANIMATION",
                () => { EndVideo(); _portraitDirty = true; }, true, PGold);
        }

        private void PWinner()
        {
            var panel = PModal(_state.WinnerPlayerId == HumanId ? "VICTORY" : "DEFEAT", .28f, .72f, false);
            PLabel(panel, new Rect(20, 100, _pw - 68, 100), _state.WinReason, 25, Color.white);
            PButton(panel, new Rect(20, panel.rect.height - 82, _pw - 68, 58), "NEW DUEL", () => { PClosePreview(); ReturnToDeckSetup(); _portraitDirty = true; }, true, PGold);
        }

        private RectTransform PRect(Transform parent, string name, Rect area)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y); rect.sizeDelta = new Vector2(area.width, area.height);
            return rect;
        }
        private static void PStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; rect.pivot = new Vector2(.5f, .5f);
        }
        private Image PBox(Transform parent, string name, Rect area, Color color)
        {
            var rect = PRect(parent, name, area); var image = rect.gameObject.AddComponent<Image>();
            image.sprite = _portraitRound; image.type = Image.Type.Sliced; image.color = color;
            return image;
        }
        private Text PLabel(Transform parent, Rect area, string text, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter, bool backing = false)
        {
            if (backing)
            {
                var background = PBox(parent, "Label backing", area, new Color(.015f, .035f, .045f, .88f));
                background.raycastTarget = false;
                parent = background.transform; area = new Rect(0, 0, area.width, area.height);
            }
            var rect = PRect(parent, "Label", area);
            var label = rect.gameObject.AddComponent<Text>(); label.font = _portraitFont;
            label.fontSize = size; label.text = text; label.color = color; label.alignment = alignment;
            label.supportRichText = false; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        private Button PButton(Transform parent, Rect area, string text, Action action, bool enabled = true, Color? tint = null)
        {
            var image = PBox(parent, text, area, tint ?? new Color(.13f, .25f, .30f));
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.interactable = enabled;
            var colors = button.colors; colors.disabledColor = new Color(.34f, .38f, .40f, .55f);
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1); colors.pressedColor = new Color(.72f, .8f, .83f, 1); button.colors = colors;
            var label = PLabel(image.transform, new Rect(10, 4, area.width - 20, area.height - 8), text, 24,
                tint.HasValue && tint.Value == PGold ? PInk : Color.white);
            button.onClick.AddListener(() => { action(); _portraitDirty = true; });
            return button;
        }
        private ScrollRect PScroll(Transform parent, Rect area, bool horizontal, float contentSize)
        {
            var viewport = PBox(parent, "Scrollable zone", area, new Color(0, 0, 0, .015f)).rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = PRect(viewport, "Content", new Rect(0, 0, horizontal ? contentSize : area.width, horizontal ? area.height : Mathf.Max(area.height, contentSize)));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = horizontal; scroll.vertical = !horizontal; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24; scroll.inertia = true; scroll.decelerationRate = .1f;
            return scroll;
        }
        private float PTextHeight(string text, int size, float width)
        {
            var settings = new TextGenerationSettings { font = _portraitFont, fontSize = size, lineSpacing = 1f,
                generationExtents = new Vector2(width, 0), textAnchor = TextAnchor.UpperLeft,
                horizontalOverflow = HorizontalWrapMode.Wrap, verticalOverflow = VerticalWrapMode.Overflow,
                scaleFactor = 1f, color = Color.white, fontStyle = FontStyle.Normal, richText = false };
            return new TextGenerator().GetPreferredHeight(text ?? "", settings);
        }
        private static void PClear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            { var obj = parent.GetChild(i).gameObject; obj.SetActive(false); Destroy(obj); }
        }
        private static Sprite PortraitSprite(bool circle, out Texture2D texture)
        {
            const int n = 256; texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[n * n];
            var center = (n - 1) * .5f;
            for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
            {
                var dx = circle ? x - center : Mathf.Max(0, Mathf.Abs(x - center) - 88);
                var dy = circle ? y - center : Mathf.Max(0, Mathf.Abs(y - center) - 88);
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01((circle ? 127 : 36) - distance));
            }
            texture.SetPixels(pixels); texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 400, 0,
                SpriteMeshType.FullRect, new Vector4(48, 48, 48, 48));
        }
        private void DisposePortraitInterface()
        {
            if (_portraitEventSystem != null) Destroy(_portraitEventSystem);
            if (_portraitRound != null) Destroy(_portraitRound);
            if (_portraitCircle != null) Destroy(_portraitCircle);
            if (_portraitRoundTexture != null) Destroy(_portraitRoundTexture);
            if (_portraitCircleTexture != null) Destroy(_portraitCircleTexture);
        }
    }
}
