using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace CardGame.Presentation
{
    public sealed class DuelPrototypeController : MonoBehaviour
    {
        private const int HumanId = 0;
        private const int ComputerId = 1;
        private readonly Dictionary<string, Texture2D> _cardArt = new Dictionary<string, Texture2D>();
        private readonly HashSet<string> _artRequests = new HashSet<string>();
        private readonly HashSet<long> _selectedManaIds = new HashSet<long>();
        private long _selectedEvolutionBaseId;
        private Texture2D _cardBack;

        private RulesEngine _engine;
        private GameState _state;
        private CardInstance _selectedHand;
        private CardInstance _selectedCreature;
        private CardInstance _selectedOpponentCreature;
        private IReadOnlyList<CardInstance> _viewerCards;
        private string _viewerZoneName = string.Empty;
        private bool _viewerAllowsHandActions;
        private Vector2 _handScroll;
        private Vector2 _humanBattleScroll;
        private Vector2 _computerBattleScroll;
        private Vector2 _manaSelectionScroll;
        private Vector2 _battleActionScroll;
        private string _message = "Choose a card, charge mana, and summon a creature.";
        private string _viewerMessage = string.Empty;
        private bool _busy;
        private bool _awaitingHumanBlock;
        private bool _showVideo;
        private bool _animationsEnabled = true;
        private bool _showHandCardViewer;
        private bool _selectingManaPayment;
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTexture;
        private float _uiWidth;
        private float _uiHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<DuelPrototypeController>() != null) return;
            var host = new GameObject("Duel Prototype");
            DontDestroyOnLoad(host);
            host.AddComponent<DuelPrototypeController>();
        }

        private void Awake()
        {
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            _cardBack = Resources.Load<Texture2D>("CardGame/UI/CardBack");
            if (_cardBack == null)
            {
                var encodedCardBack = Resources.Load<TextAsset>("CardGame/UI/CardBack");
                if (encodedCardBack != null)
                {
                    _cardBack = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    _cardBack.LoadImage(Convert.FromBase64String(encodedCardBack.text));
                }
            }
            _engine = new RulesEngine();
            _state = _engine.StartGame(
                StarterDeckCatalog.ShobusFireDeck.Build(StarterDeckCatalog.Cards),
                StarterDeckCatalog.KokujosDarknessDeck.Build(StarterDeckCatalog.Cards),
                seed: 2004,
                startingPlayerId: HumanId);

            CreateVideoPlayer();
            StartCoroutine(PreloadStarterArt());
        }

        private void CreateVideoPlayer()
        {
            _videoTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoTexture;
            _videoPlayer.loopPointReached += _ => EndVideo();
            _videoPlayer.errorReceived += (_, error) =>
            {
                _message = $"Animation unavailable: {error}";
                EndVideo();
            };
        }

        private IEnumerator PreloadStarterArt()
        {
            foreach (var card in StarterDeckCatalog.Cards.Values)
            {
                var localArt = Resources.Load<Texture2D>($"CardGame/CardArt/{card.DatabaseMediaId:0000}");
                if (localArt != null)
                {
                    _cardArt[card.Id] = localArt;
                    continue;
                }

                if (string.IsNullOrEmpty(card.DatabaseImageUrl) || !_artRequests.Add(card.DatabaseImageUrl))
                    continue;

                using (var request = UnityWebRequestTexture.GetTexture(card.DatabaseImageUrl))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                        _cardArt[card.Id] = DownloadHandlerTexture.GetContent(request);
                }
            }
        }

        private void OnGUI()
        {
            if (_state == null) return;

            const float referenceWidth = 1600f;
            const float referenceHeight = 900f;
            var uiScale = Mathf.Max(0.5f, Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight));
            _uiWidth = Screen.width / uiScale;
            _uiHeight = Screen.height / uiScale;
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            GUI.skin.label.fontSize = 15;
            GUI.skin.button.fontSize = 15;
            GUI.skin.toggle.fontSize = 14;

            GUI.backgroundColor = new Color(0.10f, 0.13f, 0.17f);
            GUI.Box(new Rect(0, 0, _uiWidth, _uiHeight), GUIContent.none);
            DrawHeader();
            DrawBoard();
            DrawControlPanel();

            if (_showHandCardViewer) DrawCardViewer();
            if (_showVideo) DrawVideoOverlay();
            if (_state.IsFinished) DrawWinnerOverlay();
            GUI.matrix = previousMatrix;
        }

        private void DrawHeader()
        {
            GUI.backgroundColor = new Color(0.18f, 0.21f, 0.27f);
            GUI.Box(new Rect(0, 0, _uiWidth, 42), GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(new Rect(16, 8, 500, 26),
                $"Turn {_state.TurnNumber}  •  {(_state.ActivePlayerId == HumanId ? "Your turn" : "Computer turn")}  •  {_state.Phase}");
            GUI.Label(new Rect(520, 8, _uiWidth - 820, 26), _message);
            _animationsEnabled = GUI.Toggle(
                new Rect(_uiWidth - 270, 8, 250, 26), _animationsEnabled, " Play attack animations");
        }

        private void DrawBoard()
        {
            var panelWidth = 280f;
            var boardWidth = _uiWidth - panelWidth;
            var handHeight = Mathf.Clamp(_uiHeight * 0.24f, 170f, 230f);
            var hiddenZoneHeight = Mathf.Clamp(_uiHeight * 0.105f, 82f, 100f);
            var manaZoneHeight = Mathf.Clamp(_uiHeight * 0.085f, 72f, 88f);
            var fixedHeight = 50f + 36f + hiddenZoneHeight * 2f + manaZoneHeight * 2f + handHeight + 52f;
            var battleHeight = Mathf.Max(88f, (_uiHeight - fixedHeight) / 2f);
            var y = 50f;

            DrawPlayerSummary(_state.Players[ComputerId], new Rect(12, y, boardWidth - 24, 32), "COMPUTER — Kokujo's Darkness Deck");
            y += 36f;
            DrawDeckShieldsAndHiddenHand(_state.Players[ComputerId], new Rect(12, y, boardWidth - 24, hiddenZoneHeight), true);
            y += hiddenZoneHeight + 6f;
            DrawManaRow(_state.Players[ComputerId], new Rect(12, y, boardWidth - 24, manaZoneHeight), "Opponent Mana", true);
            y += manaZoneHeight + 6f;
            DrawCardRow(_state.Players[ComputerId].BattleZone, new Rect(12, y, boardWidth - 24, battleHeight),
                ref _computerBattleScroll, card =>
                {
                    _selectedOpponentCreature = card;
                    OpenFaceUpCardViewer(card, _state.Players[ComputerId].BattleZone, "Opponent Battle Zone");
                }, _selectedOpponentCreature, false, "Computer Battle Zone");

            y += battleHeight + 6f;
            DrawCardRow(_state.Players[HumanId].BattleZone, new Rect(12, y, boardWidth - 24, battleHeight),
                ref _humanBattleScroll, card =>
                {
                    if (!_busy && _state.ActivePlayerId == HumanId)
                    {
                        _selectedCreature = card;
                        _selectedHand = null;
                    }
                    OpenFaceUpCardViewer(card, _state.Players[HumanId].BattleZone, "Your Battle Zone");
                }, _selectedCreature, false, "Your Battle Zone");

            y += battleHeight + 6f;
            DrawDeckShieldsAndHiddenHand(_state.Players[HumanId], new Rect(12, y, boardWidth - 24, hiddenZoneHeight), false);
            y += hiddenZoneHeight + 6f;
            DrawManaRow(_state.Players[HumanId], new Rect(12, y, boardWidth - 24, manaZoneHeight), "Your Mana — face up, reversed", true);
            y += manaZoneHeight + 6f;
            DrawCardRow(_state.Players[HumanId].Hand, new Rect(12, y, boardWidth - 24,
                    Mathf.Min(handHeight, _uiHeight - y - 10f)),
                ref _handScroll, card =>
                {
                    if (!_busy && _state.ActivePlayerId == HumanId)
                    {
                        OpenHandCardViewer(card);
                    }
                }, _selectedHand, false, "Your Hand");
        }

        private void DrawDeckShieldsAndHiddenHand(PlayerState player, Rect area, bool hideHand)
        {
            GUI.backgroundColor = new Color(0.18f, 0.21f, 0.27f);
            GUI.Box(area, GUIContent.none);
            var cardHeight = Mathf.Max(52f, area.height - 28f);
            var cardWidth = cardHeight * 0.67f;
            var top = area.y + 23f;
            var x = area.x + 10f;

            GUI.color = Color.white;
            GUI.Label(new Rect(x, area.y + 3f, 120f, 20f), hideHand ? "Opponent Deck" : "Your Deck");
            DrawCardBack(new Rect(x + 4f, top - 4f, cardWidth, cardHeight), string.Empty);
            DrawCardBack(new Rect(x + 2f, top - 2f, cardWidth, cardHeight), string.Empty);
            DrawCardBack(new Rect(x, top, cardWidth, cardHeight), player.Deck.Count.ToString());

            x += cardWidth + 28f;
            GUI.Label(new Rect(x, area.y + 3f, 180f, 20f), $"Shields ({player.Shields.Count})");
            for (var i = 0; i < player.Shields.Count; i++)
            {
                DrawCardBack(new Rect(x + i * (cardWidth + 7f), top, cardWidth, cardHeight), string.Empty);
            }

            x += Mathf.Max(5, player.Shields.Count) * (cardWidth + 7f) + 30f;
            GUI.Label(new Rect(x, area.y + 3f, 180f, 20f), $"Graveyard ({player.Graveyard.Count})");
            var graveyardRect = new Rect(x, top, cardWidth, cardHeight);
            if (player.Graveyard.Count > 0)
            {
                GUI.backgroundColor = new Color(0.09f, 0.09f, 0.11f);
                GUI.Box(new Rect(graveyardRect.x + 4f, graveyardRect.y - 4f, graveyardRect.width, graveyardRect.height), GUIContent.none);
                GUI.Box(new Rect(graveyardRect.x + 2f, graveyardRect.y - 2f, graveyardRect.width, graveyardRect.height), GUIContent.none);
                var topCard = player.Graveyard[player.Graveyard.Count - 1];
                DrawCardButton(topCard, graveyardRect, false, false,
                    () => OpenFaceUpCardViewer(topCard, player.Graveyard,
                        player.PlayerId == HumanId ? "Your Graveyard" : "Opponent Graveyard"));
            }
            else
            {
                GUI.backgroundColor = new Color(0.09f, 0.09f, 0.11f);
                GUI.Box(graveyardRect, "EMPTY");
            }

            if (!hideHand) return;
            x += cardWidth + 30f;
            GUI.Label(new Rect(x, area.y + 3f, 180f, 20f), $"Opponent Hand ({player.Hand.Count})");
            var shownCards = Mathf.Min(player.Hand.Count, 8);
            for (var i = 0; i < shownCards; i++)
            {
                DrawCardBack(new Rect(x + i * (cardWidth * 0.48f), top, cardWidth, cardHeight), string.Empty);
            }
        }

        private void DrawCardBack(Rect rect, string count)
        {
            GUI.backgroundColor = new Color(0.03f, 0.04f, 0.08f);
            GUI.Box(rect, GUIContent.none);
            var inset = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            if (_cardBack != null)
                GUI.DrawTexture(inset, _cardBack, ScaleMode.ScaleToFit);
            else
                GUI.Box(inset, "DUEL MASTERS");
            if (!string.IsNullOrEmpty(count))
            {
                GUI.color = new Color(0f, 0f, 0f, 0.78f);
                GUI.Box(new Rect(rect.x + 5f, rect.yMax - 25f, rect.width - 10f, 21f), count);
            }
            GUI.color = Color.white;
        }

        private static void DrawPlayerSummary(PlayerState player, Rect rect, string label)
        {
            GUI.color = Color.white;
            GUI.Label(rect,
                $"{label}     Hand {player.Hand.Count}   Shields {player.Shields.Count}   Mana {player.Mana.Count}   Deck {player.Deck.Count}   Graveyard {player.Graveyard.Count}");
        }

        private void DrawManaRow(PlayerState player, Rect rect, string label, bool reverseArtwork)
        {
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, 260f, 22f), $"{label} ({player.Mana.Count})");
            var inner = new Rect(rect.x + 6f, rect.y + 25f, rect.width - 12f, rect.height - 29f);
            var cardHeight = Mathf.Max(42f, inner.height);
            var cardWidth = cardHeight * 0.67f;
            for (var i = 0; i < player.Mana.Count; i++)
            {
                var mana = player.Mana[i];
                var cardRect = new Rect(inner.x + i * (cardWidth + 7f), inner.y, cardWidth, cardHeight);
                GUI.backgroundColor = CivilizationColor(mana.Definition);
                if (GUI.Button(cardRect, GUIContent.none) && !_showHandCardViewer && !_showVideo)
                    OpenFaceUpCardViewer(mana, player.Mana, label);

                var artRect = new Rect(cardRect.x + 3f, cardRect.y + 3f, cardRect.width - 6f, cardRect.height - 6f);
                if (_cardArt.TryGetValue(mana.Definition.Id, out var texture))
                {
                    if (reverseArtwork)
                        GUI.DrawTextureWithTexCoords(artRect, texture, new Rect(1f, 1f, -1f, -1f));
                    else
                        GUI.DrawTexture(artRect, texture, ScaleMode.ScaleToFit);
                }
                if (mana.Tapped)
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.67f);
                    GUI.Box(artRect, "TURNED");
                    GUI.color = Color.white;
                }
            }
            GUI.color = Color.white;
        }

        private void DrawCardRow(
            IReadOnlyList<CardInstance> cards,
            Rect area,
            ref Vector2 scroll,
            Action<CardInstance> onClick,
            CardInstance selected,
            bool faceDown,
            string label)
        {
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 7, area.y + 3, 180, 22), label);
            var inner = new Rect(area.x + 4, area.y + 24, area.width - 8, area.height - 28);
            var visibleCardHeight = Mathf.Min(158f, inner.height - 18f);
            var cardWidth = Mathf.Clamp(visibleCardHeight * 0.66f, 72f, 104f);
            var content = new Rect(0, 0, Mathf.Max(inner.width - 18, cards.Count * (cardWidth + 8)), visibleCardHeight);
            scroll = GUI.BeginScrollView(inner, scroll, content);
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var rect = new Rect(i * (cardWidth + 8), 0, cardWidth, content.height);
                DrawCardButton(card, rect, selected == card, faceDown, () => onClick(card));
            }
            GUI.EndScrollView();
        }

        private void DrawCardButton(CardInstance card, Rect rect, bool selected, bool faceDown, Action clicked)
        {
            GUI.backgroundColor = selected ? new Color(1f, 0.75f, 0.15f) : CivilizationColor(card.Definition);
            if (GUI.Button(rect, GUIContent.none) && !_showHandCardViewer && !_showVideo) clicked();

            var previousMatrix = GUI.matrix;
            var inset = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8);
            if (card.Tapped)
            {
                var turnedHeight = Mathf.Max(20f, rect.width - 8f);
                var turnedWidth = turnedHeight * 0.66f;
                inset = new Rect(rect.center.x - turnedWidth * 0.5f, rect.center.y - turnedHeight * 0.5f,
                    turnedWidth, turnedHeight);
                GUIUtility.RotateAroundPivot(90f, rect.center);
            }

            if (faceDown && _cardBack != null)
            {
                GUI.DrawTexture(inset, _cardBack, ScaleMode.ScaleToFit);
            }
            else if (!faceDown && _cardArt.TryGetValue(card.Definition.Id, out var texture))
            {
                GUI.DrawTexture(inset, texture, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.color = faceDown ? new Color(0.25f, 0.12f, 0.08f) : Color.white;
                GUI.Box(inset, faceDown ? "DUEL\nMASTERS" : string.Empty);
                if (!faceDown)
                {
                    GUI.Label(new Rect(inset.x + 4, inset.y + 4, inset.width - 8, 36), card.Definition.Name);
                    GUI.Label(new Rect(inset.x + 4, inset.yMax - 42, inset.width - 8, 38),
                        $"Cost {card.Definition.Cost}\n{(card.Definition.IsCreature ? $"Power {card.Definition.Power}" : "Spell")}");
                }
            }

            GUI.matrix = previousMatrix;
            if (card.Tapped)
            {
                GUI.color = new Color(0, 0, 0, 0.72f);
                GUI.Box(new Rect(rect.x + 7f, rect.center.y - 13f, rect.width - 14f, 26f), "TURNED");
            }
            GUI.color = Color.white;
        }

        private void DrawControlPanel()
        {
            var rect = new Rect(_uiWidth - 270, 52, 258, _uiHeight - 64);
            GUI.backgroundColor = new Color(0.20f, 0.22f, 0.27f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20));
            GUILayout.Label("DUEL CONTROLS");
            GUILayout.Space(6);

            var selected = _selectedHand ?? _selectedCreature ?? _selectedOpponentCreature;
            if (selected != null)
            {
                GUILayout.Label(selected.Definition.Name);
                GUILayout.Label($"Cost {selected.Definition.Cost}  •  Power {selected.Definition.Power}");
                GUILayout.Label(selected.Definition.RulesText);
                GUILayout.Space(8);
            }

            if (_showHandCardViewer || _showVideo)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Close the card viewer before choosing a board action.");
                GUILayout.EndArea();
                return;
            }

            GUI.enabled = !_busy && !_state.IsFinished && _state.ActivePlayerId == HumanId;
            if (_selectedHand != null)
            {
                if (GUILayout.Button("Open Selected Card", GUILayout.Height(38))) _showHandCardViewer = true;
            }
            if (_selectedCreature != null)
            {
                if (_selectedCreature.Tapped)
                {
                    GUILayout.Label("TURNED — this creature cannot attack again this turn.");
                }
                else
                {
                    if (GUILayout.Button("Attack Player", GUILayout.Height(38)))
                        StartHumanAttack(AttackTargetKind.Player, 0);

                    var canAttackUntapped = _selectedCreature.Definition.HasKeyword(CardKeyword.AttackUntappedCreature);
                    var legalCreatureTargets = _state.Players[ComputerId].BattleZone
                        .Where(card => card.Tapped || canAttackUntapped).ToList();
                    foreach (var target in legalCreatureTargets)
                    {
                        if (GUILayout.Button($"Attack {target.Definition.Name} — {target.Definition.Power}", GUILayout.Height(40)))
                            StartHumanAttack(AttackTargetKind.Creature, target.InstanceId);
                    }

                    var readyBlockers = EligibleBlockers(_state.Players[ComputerId]).Count();
                    if (readyBlockers > 0)
                        GUILayout.Label($"Opponent has {readyBlockers} ready Blocker(s). Attack the player or a legal turned creature; the opponent chooses which ready Blocker intercepts.");
                    if (legalCreatureTargets.Count == 0 && _state.Players[ComputerId].BattleZone.Count > 0)
                        GUILayout.Label("No opposing creature is currently a legal attack target. Normally, only turned creatures can be attacked.");
                }
            }
            if (GUILayout.Button("End Turn", GUILayout.Height(38))) EndHumanTurn();
            GUI.enabled = true;

            if (_awaitingHumanBlock)
            {
                GUILayout.Space(12);
                GUILayout.Label("The computer is attacking. Choose a blocker:");
                foreach (var blocker in EligibleBlockers(_state.Players[HumanId]))
                    if (GUILayout.Button($"Block with {blocker.Definition.Name}")) ResolveHumanBlock(blocker.InstanceId);
                if (GUILayout.Button("Do Not Block")) ResolveHumanBlock(0);
            }

            if (_state.PendingShieldTriggers.Count > 0 && _state.ActivePlayerId == ComputerId)
            {
                GUILayout.Space(12);
                GUILayout.Label("Shield Trigger!");
                foreach (var trigger in _state.PendingShieldTriggers.ToArray())
                    if (GUILayout.Button($"Cast {trigger.Definition.Name}")) CastHumanShieldTrigger(trigger);
                if (GUILayout.Button("Skip Remaining Triggers"))
                    ShowResult(_engine.ResolvePendingShieldTriggers(_state, HumanId));
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Prototype: hand cards use manual mana selection. Spell targeting is currently automatic.");
            GUILayout.EndArea();
        }

        private void OpenHandCardViewer(CardInstance card)
        {
            _selectedHand = card;
            _selectedCreature = null;
            _selectedOpponentCreature = null;
            _showHandCardViewer = true;
            _viewerCards = _state.Players[HumanId].Hand;
            _viewerZoneName = "Your Hand";
            _viewerAllowsHandActions = true;
            _selectingManaPayment = false;
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _viewerMessage = string.Empty;
        }

        private void OpenFaceUpCardViewer(CardInstance card, IReadOnlyList<CardInstance> cards, string zoneName)
        {
            _selectedHand = card;
            _viewerCards = cards;
            _viewerZoneName = zoneName;
            _viewerAllowsHandActions = false;
            _showHandCardViewer = true;
            _selectingManaPayment = false;
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _viewerMessage = card.Tapped ? "This card is currently turned." : string.Empty;
        }

        private void CloseHandCardViewer()
        {
            var wasHandViewer = _viewerAllowsHandActions;
            _showHandCardViewer = false;
            _viewerCards = null;
            _viewerZoneName = string.Empty;
            _viewerAllowsHandActions = false;
            _selectingManaPayment = false;
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _viewerMessage = string.Empty;
            if (!wasHandViewer) _selectedHand = null;
        }

        private void NavigateViewer(int direction)
        {
            var cards = _viewerCards;
            if (cards == null || cards.Count == 0)
            {
                CloseHandCardViewer();
                return;
            }

            var currentIndex = -1;
            for (var i = 0; i < cards.Count; i++)
                if (cards[i] == _selectedHand) currentIndex = i;
            if (currentIndex < 0) currentIndex = 0;
            var nextIndex = (currentIndex + direction + cards.Count) % cards.Count;
            _selectedHand = cards[nextIndex];
            _selectingManaPayment = false;
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _viewerMessage = string.Empty;
        }

        private void DrawCardViewer()
        {
            var cards = _viewerCards;
            if (_selectedHand == null || cards == null || !cards.Contains(_selectedHand))
            {
                CloseHandCardViewer();
                return;
            }

            GUI.color = new Color(0f, 0f, 0f, 0.86f);
            GUI.Box(new Rect(0, 0, _uiWidth, _uiHeight), GUIContent.none);
            GUI.color = Color.white;

            var panel = new Rect(55f, 38f, _uiWidth - 110f, _uiHeight - 76f);
            GUI.backgroundColor = new Color(0.12f, 0.15f, 0.20f);
            GUI.Box(panel, GUIContent.none);

            if (GUI.Button(new Rect(panel.xMax - 52f, panel.y + 12f, 38f, 38f), "X"))
            {
                CloseHandCardViewer();
                return;
            }

            var imageHeight = panel.height - 120f;
            var imageWidth = Mathf.Min(imageHeight * 0.715f, panel.width * 0.42f);
            var imageRect = new Rect(panel.x + 78f, panel.y + 66f, imageWidth, imageHeight);
            GUI.backgroundColor = CivilizationColor(_selectedHand.Definition);
            GUI.Box(new Rect(imageRect.x - 6f, imageRect.y - 6f, imageRect.width + 12f, imageRect.height + 12f), GUIContent.none);
            if (_cardArt.TryGetValue(_selectedHand.Definition.Id, out var texture))
                GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit);
            else
                GUI.Box(imageRect, _selectedHand.Definition.Name);

            var previousFontSize = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 34;
            if (GUI.Button(new Rect(panel.x + 14f, panel.center.y - 38f, 52f, 76f), "◀")) NavigateViewer(-1);
            if (GUI.Button(new Rect(imageRect.xMax + 12f, panel.center.y - 38f, 52f, 76f), "▶")) NavigateViewer(1);
            GUI.skin.button.fontSize = previousFontSize;

            var index = 0;
            for (var i = 0; i < cards.Count; i++)
                if (cards[i] == _selectedHand) index = i + 1;
            GUI.Label(new Rect(imageRect.x, panel.y + 18f, imageRect.width, 30f), $"{_viewerZoneName} — card {index} of {cards.Count}");

            var infoX = imageRect.xMax + 82f;
            var infoRect = new Rect(infoX, panel.y + 62f, panel.xMax - infoX - 24f, panel.height - 82f);
            var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = true };
            var bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            GUI.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 72f), _selectedHand.Definition.Name, titleStyle);
            GUI.Label(new Rect(infoRect.x, infoRect.y + 78f, infoRect.width, 58f),
                $"Cost: {_selectedHand.Definition.Cost}     Type: {_selectedHand.Definition.Type}\n" +
                $"Civilization: {string.Join(", ", _selectedHand.Definition.Civilizations)}" +
                (_selectedHand.Definition.IsCreature ? $"     Power: {_selectedHand.Definition.Power}" : string.Empty), bodyStyle);
            GUI.Label(new Rect(infoRect.x, infoRect.y + 142f, infoRect.width, 118f),
                string.IsNullOrWhiteSpace(_selectedHand.Definition.RulesText)
                    ? "This card has no printed ability."
                    : _selectedHand.Definition.RulesText, bodyStyle);

            if (!string.IsNullOrEmpty(_viewerMessage))
            {
                var messageStyle = new GUIStyle(bodyStyle);
                messageStyle.normal.textColor = new Color(1f, 0.72f, 0.25f);
                GUI.Label(new Rect(infoRect.x, infoRect.y + 258f, infoRect.width, 54f), _viewerMessage, messageStyle);
            }

            if (_selectingManaPayment && _viewerAllowsHandActions)
            {
                DrawManaPaymentSelector(new Rect(infoRect.x, infoRect.y + 314f, infoRect.width, infoRect.height - 314f));
            }
            else if (_viewerAllowsHandActions)
            {
                var canAct = !_busy && !_state.IsFinished && _state.ActivePlayerId == HumanId;
                GUI.enabled = canAct;
                if (GUI.Button(new Rect(infoRect.x, infoRect.yMax - 74f, infoRect.width * 0.47f, 58f), "Mana Zone"))
                    ChargeSelected();
                if (GUI.Button(new Rect(infoRect.x + infoRect.width * 0.53f, infoRect.yMax - 74f, infoRect.width * 0.47f, 58f), "Battle Zone"))
                    BeginManaPayment();
                GUI.enabled = true;
            }
            else
            {
                var viewingOwnBattleZone = ReferenceEquals(_viewerCards, _state.Players[HumanId].BattleZone);
                if (viewingOwnBattleZone)
                    DrawEnlargedBattleActions(new Rect(infoRect.x, infoRect.yMax - 178f, infoRect.width, 162f), bodyStyle);
                else
                    GUI.Label(new Rect(infoRect.x, infoRect.yMax - 66f, infoRect.width, 44f),
                        "Face-up zone viewer — use the arrows to inspect the other cards in this zone.", bodyStyle);
            }
        }

        private void DrawEnlargedBattleActions(Rect area, GUIStyle bodyStyle)
        {
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 10f, area.y + 7f, area.width - 20f, 26f), "AVAILABLE ACTIONS", bodyStyle);

            var attacker = _selectedHand;
            var canUseControls = !_busy && !_state.IsFinished && _state.ActivePlayerId == HumanId;
            var hasSummoningSickness = attacker.EnteredBattleZoneOnTurn == _state.TurnNumber &&
                !attacker.Definition.HasKeyword(CardKeyword.SpeedAttacker);
            if (attacker.Tapped || hasSummoningSickness)
            {
                var reason = attacker.Tapped
                    ? "TURNED — this creature cannot attack again this turn."
                    : "This creature has summoning sickness and cannot attack this turn.";
                GUI.Label(new Rect(area.x + 10f, area.y + 40f, area.width - 20f, 48f), reason, bodyStyle);
                return;
            }

            var canAttackPlayer = !attacker.Definition.HasKeyword(CardKeyword.CannotAttack) &&
                !attacker.Definition.HasKeyword(CardKeyword.CannotAttackPlayers);
            var canAttackCreatures = !attacker.Definition.HasKeyword(CardKeyword.CannotAttack);
            var canAttackUntapped = attacker.Definition.HasKeyword(CardKeyword.AttackUntappedCreature);
            var legalTargets = canAttackCreatures
                ? _state.Players[ComputerId].BattleZone.Where(card => card.Tapped || canAttackUntapped).ToList()
                : new List<CardInstance>();

            var buttonHeight = 46f;
            var buttonWidth = Mathf.Clamp(area.width * 0.31f, 150f, 230f);
            var actionCount = (canAttackPlayer ? 1 : 0) + legalTargets.Count;
            var viewport = new Rect(area.x + 8f, area.y + 36f, area.width - 16f, 66f);
            var content = new Rect(0f, 0f, Mathf.Max(viewport.width - 18f,
                actionCount * (buttonWidth + 10f)), buttonHeight);
            _battleActionScroll = GUI.BeginScrollView(viewport, _battleActionScroll, content);
            var x = 0f;
            GUI.enabled = canUseControls && canAttackPlayer;
            if (canAttackPlayer && GUI.Button(new Rect(x, 0f, buttonWidth, buttonHeight), "Attack Player"))
            {
                _selectedCreature = attacker;
                if (StartHumanAttack(AttackTargetKind.Player, 0)) CloseHandCardViewer();
            }

            if (canAttackPlayer) x += buttonWidth + 10f;
            GUI.enabled = canUseControls;
            foreach (var target in legalTargets)
            {
                if (GUI.Button(new Rect(x, 0f, buttonWidth, buttonHeight),
                        $"Attack {target.Definition.Name}"))
                {
                    _selectedCreature = attacker;
                    if (StartHumanAttack(AttackTargetKind.Creature, target.InstanceId)) CloseHandCardViewer();
                }
                x += buttonWidth + 10f;
            }
            GUI.EndScrollView();
            GUI.enabled = true;

            var readyBlockers = EligibleBlockers(_state.Players[ComputerId]).Count();
            var note = readyBlockers > 0
                ? $"Opponent has {readyBlockers} ready Blocker(s); the opponent chooses which one intercepts."
                : legalTargets.Count == 0 && !canAttackPlayer
                    ? "This creature currently has no legal attack."
                    : "Turned opposing creatures appear here as direct attack targets.";
            GUI.Label(new Rect(area.x + 10f, area.y + 108f, area.width - 20f, 44f), note, bodyStyle);
        }

        private void BeginManaPayment()
        {
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _selectingManaPayment = true;
            _viewerMessage = _selectedHand.Definition.Cost == 0
                ? "This card costs no mana. Confirm to play it."
                : $"Select exactly {_selectedHand.Definition.Cost} untapped mana cards to turn.";
        }

        private void DrawManaPaymentSelector(Rect area)
        {
            var player = _state.Players[HumanId];
            var cost = _selectedHand.Definition.Cost;
            var isEvolution = _selectedHand.Definition.Type == CardType.EvolutionCreature;
            var manaTop = area.y;

            if (isEvolution)
            {
                GUI.Label(new Rect(area.x, area.y, area.width, 24f),
                    $"Choose a {_selectedHand.Definition.EvolutionRace} in your battle zone to evolve:");
                var bases = player.BattleZone.Where(card => card.Definition.Races.Any(race =>
                    race.Equals(_selectedHand.Definition.EvolutionRace, StringComparison.OrdinalIgnoreCase))).ToList();
                var baseCardHeight = 90f;
                var baseCardWidth = baseCardHeight * 0.66f;
                for (var i = 0; i < bases.Count; i++)
                {
                    var baseCard = bases[i];
                    var baseRect = new Rect(area.x + i * (baseCardWidth + 10f), area.y + 26f, baseCardWidth, baseCardHeight);
                    DrawCardButton(baseCard, baseRect, _selectedEvolutionBaseId == baseCard.InstanceId, false,
                        () => _selectedEvolutionBaseId = baseCard.InstanceId);
                }
                if (bases.Count == 0)
                    GUI.Label(new Rect(area.x, area.y + 32f, area.width, 48f),
                        $"You do not currently control a {_selectedHand.Definition.EvolutionRace}.");
                manaTop += 122f;
            }

            GUI.Label(new Rect(area.x, manaTop, area.width, 28f),
                $"Mana selected: {_selectedManaIds.Count} / {cost}. At least one must match the card's civilization.");

            var viewport = new Rect(area.x, manaTop + 32f, area.width, Mathf.Max(70f, area.yMax - manaTop - 100f));
            GUI.Box(viewport, GUIContent.none);
            var cardHeight = Mathf.Min(132f, viewport.height - 22f);
            var cardWidth = cardHeight * 0.66f;
            var content = new Rect(0, 0, Mathf.Max(viewport.width - 18f, player.Mana.Count * (cardWidth + 10f)), cardHeight + 4f);
            _manaSelectionScroll = GUI.BeginScrollView(viewport, _manaSelectionScroll, content);
            for (var i = 0; i < player.Mana.Count; i++)
            {
                var mana = player.Mana[i];
                var selected = _selectedManaIds.Contains(mana.InstanceId);
                var rect = new Rect(i * (cardWidth + 10f), 0, cardWidth, cardHeight);
                GUI.enabled = !mana.Tapped;
                DrawCardButton(mana, rect, selected, false, () =>
                {
                    if (!_selectedManaIds.Add(mana.InstanceId)) _selectedManaIds.Remove(mana.InstanceId);
                    _viewerMessage = $"Selected {_selectedManaIds.Count} of {cost} mana cards.";
                });
                GUI.enabled = true;
                if (selected)
                {
                    GUI.color = new Color(1f, 0.76f, 0.12f);
                    GUI.Box(new Rect(rect.x + 4f, rect.y + rect.height - 28f, rect.width - 8f, 24f), "TURN");
                    GUI.color = Color.white;
                }
            }
            GUI.EndScrollView();

            var confirmEnabled = _selectedManaIds.Count == cost && (!isEvolution || _selectedEvolutionBaseId != 0);
            GUI.enabled = confirmEnabled;
            if (GUI.Button(new Rect(area.x, area.yMax - 56f, area.width * 0.62f, 48f), $"Confirm — Turn {cost} Mana"))
                ConfirmManualPlay();
            GUI.enabled = true;
            if (GUI.Button(new Rect(area.x + area.width * 0.66f, area.yMax - 56f, area.width * 0.34f, 48f), "Back"))
            {
                _selectingManaPayment = false;
                _selectedManaIds.Clear();
                _selectedEvolutionBaseId = 0;
                _viewerMessage = string.Empty;
            }
        }

        private void ConfirmManualPlay()
        {
            var card = _selectedHand;
            var payment = _selectedManaIds.ToArray();
            RuleResult result;
            if (card.Definition.Type == CardType.Spell)
                result = TryCastSpell(HumanId, card, payment, _selectedOpponentCreature?.InstanceId ?? 0);
            else
                result = _engine.SummonCreature(_state, HumanId, card.InstanceId, payment, _selectedEvolutionBaseId);

            ShowResult(result, result.Success ? $"Played {card.Definition.Name}." : null);
            if (!result.Success)
            {
                _viewerMessage = result.Error;
                return;
            }

            _selectedHand = null;
            CloseHandCardViewer();
        }

        private void ChargeSelected()
        {
            var card = _selectedHand;
            var result = _engine.ChargeMana(_state, HumanId, card.InstanceId);
            ShowResult(result, result.Success ? $"Charged {card.Definition.Name} as mana." : null);
            if (!result.Success)
            {
                _viewerMessage = result.Error;
                return;
            }

            _selectedHand = null;
            CloseHandCardViewer();
        }

        private RuleResult TryCastSpell(int playerId, CardInstance spell, IReadOnlyCollection<long> payment, long preferredTarget)
        {
            var candidates = new List<long> { preferredTarget };
            candidates.AddRange(_state.Players[1 - playerId].BattleZone.Select(card => card.InstanceId));
            candidates.AddRange(_state.Players[playerId].BattleZone.Select(card => card.InstanceId));
            candidates.AddRange(_state.Players[playerId].Graveyard.Select(card => card.InstanceId));
            candidates.Add(0);

            RuleResult last = null;
            foreach (var target in candidates.Distinct())
            {
                last = _engine.CastSpell(_state, playerId, spell.InstanceId, payment, target);
                if (last.Success) return last;
            }
            return last ?? RuleResult.Fail("No legal spell target is available.");
        }

        private bool StartHumanAttack(AttackTargetKind kind, long targetId)
        {
            var attacker = _selectedCreature;
            if (attacker == null)
            {
                ShowResult(RuleResult.Fail("Choose one of your creatures to attack."));
                return false;
            }
            var result = _engine.DeclareAttack(_state, HumanId, attacker.InstanceId, kind, targetId);
            if (!result.Success)
            {
                ShowResult(result);
                _viewerMessage = result.Error;
                return false;
            }

            _busy = true;
            StartCoroutine(ResolveHumanAttack(attacker));
            return true;
        }

        private IEnumerator ResolveHumanAttack(CardInstance attacker)
        {
            yield return PlayAttackVideo(attacker);
            var blockers = EligibleBlockers(_state.Players[ComputerId]).ToList();
            var result = blockers.Count > 0
                ? _engine.Block(_state, ComputerId, blockers.OrderBy(card => card.Definition.Power).First().InstanceId)
                : _engine.DeclineBlockAndResolve(_state, ComputerId);
            ShowResult(result, result.Success ? $"{attacker.Definition.Name} attacked." : null);
            ResolveComputerShieldTriggers();
            _selectedCreature = null;
            _selectedOpponentCreature = null;
            _busy = false;
        }

        private void EndHumanTurn()
        {
            CloseHandCardViewer();
            var result = _engine.EndTurn(_state, HumanId);
            ShowResult(result, result.Success ? "Computer is thinking…" : null);
            if (!result.Success) return;
            _selectedHand = null;
            _selectedCreature = null;
            _selectedOpponentCreature = null;
            StartCoroutine(RunComputerTurn());
        }

        private IEnumerator RunComputerTurn()
        {
            _busy = true;
            yield return new WaitForSeconds(0.45f);
            var computer = _state.Players[ComputerId];

            if (computer.Hand.Any())
            {
                var manaCard = computer.Hand
                    .OrderByDescending(card => card.Definition.ImplementationStatus == CardImplementationStatus.DataOnly)
                    .ThenByDescending(card => card.Definition.Cost)
                    .First();
                _engine.ChargeMana(_state, ComputerId, manaCard.InstanceId);
            }

            while (TryComputerPlayCreature()) yield return new WaitForSeconds(0.25f);

            foreach (var attacker in computer.BattleZone.ToArray())
            {
                if (_state.IsFinished) break;
                var attack = _engine.DeclareAttack(_state, ComputerId, attacker.InstanceId, AttackTargetKind.Player);
                if (!attack.Success) continue;

                _message = $"{attacker.Definition.Name} attacks you!";
                yield return PlayAttackVideo(attacker);
                _awaitingHumanBlock = true;
                while (_awaitingHumanBlock) yield return null;
                while (_state.PendingShieldTriggers.Count > 0) yield return null;
                yield return new WaitForSeconds(0.2f);
            }

            if (!_state.IsFinished)
            {
                var end = _engine.EndTurn(_state, ComputerId);
                ShowResult(end, end.Success ? "Your turn." : null);
            }
            _busy = false;
        }

        private bool TryComputerPlayCreature()
        {
            var computer = _state.Players[ComputerId];
            foreach (var card in computer.Hand
                         .Where(card => card.Definition.Type == CardType.Creature &&
                                       card.Definition.ImplementationStatus != CardImplementationStatus.DataOnly)
                         .OrderByDescending(card => card.Definition.Cost))
            {
                var payment = SelectPayment(computer, card.Definition);
                if (payment.Count != card.Definition.Cost) continue;
                if (_engine.SummonCreature(_state, ComputerId, card.InstanceId, payment).Success)
                {
                    _message = $"Computer summoned {card.Definition.Name}.";
                    return true;
                }
            }
            return false;
        }

        private void ResolveHumanBlock(long blockerId)
        {
            var result = blockerId == 0
                ? _engine.DeclineBlockAndResolve(_state, HumanId)
                : _engine.Block(_state, HumanId, blockerId);
            ShowResult(result);
            if (result.Success) _awaitingHumanBlock = false;
        }

        private void CastHumanShieldTrigger(CardInstance trigger)
        {
            var candidates = _state.Players[ComputerId].BattleZone.Select(card => card.InstanceId)
                .Concat(_state.Players[HumanId].Graveyard.Where(card => card.Definition.IsCreature).Select(card => card.InstanceId))
                .Concat(new long[] { 0 });
            RuleResult last = null;
            foreach (var target in candidates)
            {
                last = _engine.CastShieldTrigger(_state, HumanId, trigger.InstanceId, target);
                if (last.Success) break;
            }
            ShowResult(last ?? RuleResult.Fail("No legal shield-trigger target."));
        }

        private void ResolveComputerShieldTriggers()
        {
            foreach (var trigger in _state.PendingShieldTriggers.ToArray())
            {
                var targets = _state.Players[HumanId].BattleZone.Select(card => card.InstanceId).Concat(new long[] { 0 });
                var resolved = false;
                foreach (var target in targets)
                {
                    var result = _engine.CastShieldTrigger(_state, ComputerId, trigger.InstanceId, target);
                    if (!result.Success) continue;
                    resolved = true;
                    break;
                }
                if (!resolved) _state.PendingShieldTriggers.Remove(trigger);
            }
        }

        private IEnumerator PlayAttackVideo(CardInstance attacker)
        {
            if (!_animationsEnabled || !attacker.Definition.HasDatabaseVideo) yield break;
            _showVideo = true;
            var localVideo = Resources.Load<VideoClip>($"CardGame/Videos/{attacker.Definition.DatabaseMediaId:0000}");
            if (localVideo != null)
            {
                _videoPlayer.source = VideoSource.VideoClip;
                _videoPlayer.clip = localVideo;
            }
            else
            {
                _videoPlayer.source = VideoSource.Url;
                _videoPlayer.url = attacker.Definition.DatabaseVideoUrl;
            }
            _videoPlayer.Play();
            while (_showVideo) yield return null;
        }

        private void DrawVideoOverlay()
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, _uiWidth, _uiHeight), _videoTexture, ScaleMode.ScaleToFit);
            GUI.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            if (GUI.Button(new Rect(_uiWidth - 150, 18, 132, 44), "Skip Animation")) EndVideo();
        }

        private void EndVideo()
        {
            if (_videoPlayer != null) _videoPlayer.Stop();
            _showVideo = false;
        }

        private void DrawWinnerOverlay()
        {
            GUI.color = new Color(0, 0, 0, 0.82f);
            GUI.Box(new Rect(0, 0, _uiWidth, _uiHeight), GUIContent.none);
            GUI.color = Color.white;
            var text = _state.WinnerPlayerId == HumanId ? "YOU WIN" : "COMPUTER WINS";
            GUI.Label(new Rect(_uiWidth / 2f - 150, _uiHeight / 2f - 45, 300, 40), text);
            GUI.Label(new Rect(_uiWidth / 2f - 150, _uiHeight / 2f, 300, 30), _state.WinReason);
        }

        private static IReadOnlyCollection<long> SelectPayment(PlayerState player, CardDefinition definition)
        {
            if (definition.Cost == 0) return Array.Empty<long>();
            var untapped = player.Mana.Where(card => !card.Tapped).ToList();
            var matching = untapped.FirstOrDefault(card => card.Definition.SharesCivilizationWith(definition));
            if (matching == null || untapped.Count < definition.Cost) return Array.Empty<long>();
            var selected = new List<long> { matching.InstanceId };
            selected.AddRange(untapped.Where(card => card != matching).Take(definition.Cost - 1).Select(card => card.InstanceId));
            return selected;
        }

        private static IEnumerable<CardInstance> EligibleBlockers(PlayerState player) =>
            player.BattleZone.Where(card => !card.Tapped && card.Definition.HasKeyword(CardKeyword.Blocker));

        private void ShowResult(RuleResult result, string successMessage = null)
        {
            _message = result.Success ? successMessage ?? "Action resolved." : result.Error;
        }

        private static Color CivilizationColor(CardDefinition card)
        {
            if (card.Civilizations.Contains(Civilization.Fire)) return new Color(0.56f, 0.18f, 0.12f);
            if (card.Civilizations.Contains(Civilization.Darkness)) return new Color(0.30f, 0.18f, 0.38f);
            if (card.Civilizations.Contains(Civilization.Water)) return new Color(0.12f, 0.38f, 0.62f);
            if (card.Civilizations.Contains(Civilization.Nature)) return new Color(0.18f, 0.52f, 0.25f);
            return new Color(0.72f, 0.62f, 0.22f);
        }

        private void OnDestroy()
        {
            if (_videoTexture != null) _videoTexture.Release();
        }
    }
}
