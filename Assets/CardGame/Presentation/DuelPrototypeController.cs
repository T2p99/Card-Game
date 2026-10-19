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
        private static readonly string[] SelectableSetCodes =
        {
            "DM-01", "DM-02", "DM-03", "DM-04", "DM-05", "DM-06",
            "DM-07", "DM-08", "DM-09", "DM-10", "DM-11", "DM-12", "PROMO"
        };
        private static readonly Civilization[] SelectableCivilizations =
        {
            Civilization.Light,
            Civilization.Water,
            Civilization.Darkness,
            Civilization.Fire,
            Civilization.Nature
        };

        private readonly Dictionary<string, Texture2D> _cardArt = new Dictionary<string, Texture2D>();
        private readonly Dictionary<Texture, RenderTexture> _turnedCardArt =
            new Dictionary<Texture, RenderTexture>();
        private readonly HashSet<string> _artRequests = new HashSet<string>();
        private readonly HashSet<long> _selectedManaIds = new HashSet<long>();
        private readonly HashSet<string> _humanSelectedSets =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DM-01" };
        private readonly HashSet<string> _computerSelectedSets =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DM-01" };
        private readonly HashSet<Civilization> _humanSelectedCivilizations =
            new HashSet<Civilization>(SelectableCivilizations);
        private readonly HashSet<Civilization> _computerSelectedCivilizations =
            new HashSet<Civilization>(SelectableCivilizations);
        private long _selectedEvolutionBaseId;
        private long _selectedSpellTargetId;
        private long _selectedSummonChoiceId;
        private long _selectedTapTargetId;
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
        private Vector2 _humanManaScroll;
        private Vector2 _computerManaScroll;
        private Vector2 _manaSelectionScroll;
        private Vector2 _spellTargetScroll;
        private Vector2 _summonChoiceScroll;
        private Vector2 _battleActionScroll;
        private Vector2 _controlPanelScroll;
        private string _message = "Choose a card, charge mana, and summon a creature.";
        private string _viewerMessage = string.Empty;
        private bool _busy;
        private bool _awaitingHumanBlock;
        private bool _showVideo;
        private bool _animationsEnabled = true;
        private bool _showHandCardViewer;
        private bool _selectingManaPayment;
        private bool _showDeckSetup = true;
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTexture;
        private Material _turnCardMaterial;
        private float _uiWidth;
        private float _uiHeight;
        private int _matchSeed;
        private string _setupMessage = "Choose one or more sets and civilizations for each deck pool.";
        private string _humanDeckLabel = "DM-01";
        private string _computerDeckLabel = "DM-01";
        private string _humanCivilizationLabel = "All civilizations";
        private string _computerCivilizationLabel = "All civilizations";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<DuelPrototypeController>() != null) return;
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
            var turnCardShader = Resources.Load<Shader>("CardGame/UI/RotateCard90");
            if (turnCardShader != null) _turnCardMaterial = new Material(turnCardShader);
            CreateVideoPlayer();
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

        private IEnumerator PreloadDeckArt(IEnumerable<CardDefinition> deckCards)
        {
            foreach (var card in deckCards.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
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
            const float referenceWidth = 1600f;
            const float referenceHeight = 900f;
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            GUI.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);

            var uiScale = Mathf.Max(0.1f,
                Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight));
            var offsetX = (Screen.width - referenceWidth * uiScale) * 0.5f;
            var offsetY = (Screen.height - referenceHeight * uiScale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(offsetX, offsetY, 0f),
                Quaternion.identity,
                new Vector3(uiScale, uiScale, 1f));
            _uiWidth = referenceWidth;
            _uiHeight = referenceHeight;

            GUI.skin.label.fontSize = 15;
            GUI.skin.button.fontSize = 15;
            GUI.skin.toggle.fontSize = 14;

            GUI.backgroundColor = new Color(0.075f, 0.095f, 0.125f);
            GUI.Box(new Rect(0, 0, _uiWidth, _uiHeight), GUIContent.none);
            if (_showDeckSetup || _state == null)
            {
                DrawDeckSetup();
                GUI.matrix = previousMatrix;
                return;
            }

            DrawHeader();
            var modalWasOpen = _showHandCardViewer || _showVideo;
            if (!modalWasOpen)
            {
                DrawBoard();
                DrawControlPanel();
            }

            if (_showHandCardViewer) DrawCardViewer();
            if (_showVideo) DrawVideoOverlay();
            if (_state.IsFinished) DrawWinnerOverlay();
            GUI.matrix = previousMatrix;
        }

        private void DrawDeckSetup()
        {
            var panelWidth = Mathf.Min(1320f, _uiWidth - 70f);
            var panelHeight = Mathf.Min(780f, _uiHeight - 60f);
            var panel = new Rect((_uiWidth - panelWidth) * 0.5f, (_uiHeight - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            GUI.backgroundColor = new Color(0.16f, 0.19f, 0.24f);
            GUI.Box(panel, GUIContent.none);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 32,
                fontStyle = FontStyle.Bold
            };
            var subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                wordWrap = true
            };
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 30f, panel.y + 20f, panel.width - 60f, 48f),
                "CHOOSE DECK POOLS", titleStyle);
            GUI.Label(new Rect(panel.x + 70f, panel.y + 68f, panel.width - 140f, 50f),
                "Choose card sets and civilizations independently for each side. Multicolored cards are included only when all of their civilizations are selected. Each legal 40-card deck is freshly built and shuffled.",
                subtitleStyle);

            var gap = 30f;
            var selectorWidth = (panel.width - 90f - gap) * 0.5f;
            var selectorHeight = panel.height - 260f;
            DrawSetPoolSelector(new Rect(panel.x + 45f, panel.y + 126f, selectorWidth, selectorHeight),
                "YOUR DECK POOL", _humanSelectedSets, _humanSelectedCivilizations);
            DrawSetPoolSelector(new Rect(panel.x + 45f + selectorWidth + gap, panel.y + 126f,
                    selectorWidth, selectorHeight),
                "COMPUTER DECK POOL", _computerSelectedSets, _computerSelectedCivilizations);

            var humanCardNames = CountPlayableCardNames(_humanSelectedSets, _humanSelectedCivilizations);
            var computerCardNames = CountPlayableCardNames(_computerSelectedSets, _computerSelectedCivilizations);
            var humanReady = humanCardNames >= 10;
            var computerReady = computerCardNames >= 10;
            var messageStyle = new GUIStyle(subtitleStyle)
            {
                alignment = TextAnchor.MiddleLeft
            };
            messageStyle.normal.textColor = humanReady && computerReady
                ? new Color(0.70f, 0.90f, 0.72f)
                : new Color(1f, 0.70f, 0.30f);
            var readinessMessage = humanReady && computerReady
                ? _setupMessage
                : $"A legal deck needs at least 10 different card names. Your pool has {humanCardNames}; the computer pool has {computerCardNames}.";
            GUI.Label(new Rect(panel.x + 48f, panel.yMax - 116f, panel.width - 430f, 64f),
                readinessMessage, messageStyle);

            GUI.enabled = humanReady && computerReady;
            if (GUI.Button(new Rect(panel.xMax - 350f, panel.yMax - 108f, 300f, 62f), "BUILD DECKS & START DUEL"))
                StartSelectedMatch();
            GUI.enabled = true;
        }

        private static void DrawSetPoolSelector(
            Rect area,
            string title,
            HashSet<string> selectedSets,
            HashSet<Civilization> selectedCivilizations)
        {
            GUI.backgroundColor = new Color(0.10f, 0.13f, 0.17f);
            GUI.Box(area, GUIContent.none);
            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 21,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(area.x + 10f, area.y + 10f, area.width - 20f, 34f), title, headerStyle);

            const int columns = 3;
            const float rowHeight = 48f;
            var cellWidth = (area.width - 36f) / columns;
            for (var i = 0; i < SelectableSetCodes.Length; i++)
            {
                var setCode = SelectableSetCodes[i];
                var column = i % columns;
                var row = i / columns;
                var wasSelected = selectedSets.Contains(setCode);
                var cardCount = TcgCatalog.ForSet(setCode)
                    .Count(card => card.ImplementationStatus == CardImplementationStatus.Complete);
                var isSelected = DrawDeckPoolChoice(
                    new Rect(area.x + 14f + column * cellWidth,
                        area.y + 56f + row * rowHeight, cellWidth - 8f, 38f),
                    wasSelected, $"{setCode}  ({cardCount})",
                    new Color(0.12f, 0.64f, 0.96f));
                if (isSelected != wasSelected)
                {
                    if (isSelected) selectedSets.Add(setCode);
                    else selectedSets.Remove(setCode);
                }
            }

            var civilizationY = area.y + 300f;
            var sectionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(area.x + 14f, civilizationY, area.width - 28f, 22f),
                "CIVILIZATIONS", sectionStyle);
            var civilizationCellWidth = (area.width - 28f) / SelectableCivilizations.Length;
            for (var i = 0; i < SelectableCivilizations.Length; i++)
            {
                var civilization = SelectableCivilizations[i];
                var wasSelected = selectedCivilizations.Contains(civilization);
                var isSelected = DrawDeckPoolChoice(
                    new Rect(area.x + 14f + i * civilizationCellWidth, civilizationY + 24f,
                        civilizationCellWidth - 5f, 36f),
                    wasSelected, civilization.ToString(), CivilizationColor(civilization));
                if (isSelected != wasSelected)
                {
                    if (isSelected) selectedCivilizations.Add(civilization);
                    else selectedCivilizations.Remove(civilization);
                }
            }

            var selectedCards = TcgCatalog.Printings.Count(card =>
                CardMatchesDeckPool(card, selectedSets, selectedCivilizations));
            var uniqueNames = TcgCatalog.Printings.Where(card =>
                    CardMatchesDeckPool(card, selectedSets, selectedCivilizations))
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            GUI.backgroundColor = new Color(0.14f, 0.17f, 0.22f);
            GUI.Label(new Rect(area.x + 14f, civilizationY + 66f, area.width - 28f, 46f),
                $"{selectedSets.Count} set(s) • {selectedCivilizations.Count} civilization(s)\n" +
                $"{selectedCards} printings • {uniqueNames} card names");

            var controlsY = area.yMax - 52f;
            var buttonWidth = (area.width - 58f) * 0.25f;
            if (GUI.Button(new Rect(area.x + 14f, controlsY, buttonWidth, 38f), "All Sets"))
                foreach (var setCode in SelectableSetCodes) selectedSets.Add(setCode);
            if (GUI.Button(new Rect(area.x + 20f + buttonWidth, controlsY, buttonWidth, 38f), "Clear Sets"))
                selectedSets.Clear();
            if (GUI.Button(new Rect(area.x + 26f + buttonWidth * 2f, controlsY, buttonWidth, 38f), "All Civs"))
                foreach (var civilization in SelectableCivilizations)
                    selectedCivilizations.Add(civilization);
            if (GUI.Button(new Rect(area.x + 32f + buttonWidth * 3f, controlsY, buttonWidth, 38f), "Clear Civs"))
                selectedCivilizations.Clear();
        }

        private static bool DrawDeckPoolChoice(
            Rect rect,
            bool selected,
            string label,
            Color accent)
        {
            var previousColor = GUI.color;
            var previousBackground = GUI.backgroundColor;
            var previousContent = GUI.contentColor;
            var borderColor = selected
                ? Color.Lerp(accent, Color.white, 0.34f)
                : new Color(0.25f, 0.30f, 0.38f);
            GUI.color = borderColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            var buttonRect = new Rect(rect.x + 3f, rect.y + 3f,
                rect.width - 6f, rect.height - 6f);
            GUI.color = Color.white;
            GUI.backgroundColor = selected
                ? Color.Lerp(accent, new Color(0.04f, 0.06f, 0.09f), 0.42f)
                : new Color(0.14f, 0.17f, 0.22f);
            GUI.contentColor = Color.white;
            var style = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = selected ? 14 : 13,
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal
            };
            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = Color.white;
            var clicked = GUI.Button(buttonRect,
                selected ? $"✓  {label}" : label, style);

            GUI.color = previousColor;
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
            return clicked ? !selected : selected;
        }

        private static int CountPlayableCardNames(
            HashSet<string> selectedSets,
            HashSet<Civilization> selectedCivilizations)
        {
            if (selectedSets.Count == 0 || selectedCivilizations.Count == 0) return 0;
            return TcgCatalog.Printings.Where(card =>
                    CardMatchesDeckPool(card, selectedSets, selectedCivilizations))
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private static bool CardMatchesDeckPool(
            CardDefinition card,
            HashSet<string> selectedSets,
            HashSet<Civilization> selectedCivilizations) =>
            selectedSets.Contains(card.SetCode) &&
            card.ImplementationStatus == CardImplementationStatus.Complete &&
            card.Civilizations.Length > 0 &&
            card.Civilizations.All(selectedCivilizations.Contains);

        private void StartSelectedMatch()
        {
            try
            {
                StopAllCoroutines();
                EndVideo();
                _matchSeed = unchecked(Environment.TickCount * 397 ^ Guid.NewGuid().GetHashCode());
                var humanSets = OrderedSelectedSets(_humanSelectedSets);
                var computerSets = OrderedSelectedSets(_computerSelectedSets);
                var humanCivilizations = OrderedSelectedCivilizations(_humanSelectedCivilizations);
                var computerCivilizations = OrderedSelectedCivilizations(_computerSelectedCivilizations);
                var humanDeck = TcgCatalog.BuildRandomDeckFromSets(
                    humanSets, humanCivilizations, unchecked(_matchSeed ^ 0x5f3759df));
                var computerDeck = TcgCatalog.BuildRandomDeckFromSets(
                    computerSets, computerCivilizations, unchecked(_matchSeed ^ (int)0x9e3779b9));

                _engine = new RulesEngine();
                _state = _engine.StartGame(humanDeck, computerDeck, _matchSeed, HumanId);
                _humanDeckLabel = FormatSetSelection(humanSets);
                _computerDeckLabel = FormatSetSelection(computerSets);
                _humanCivilizationLabel = FormatCivilizationSelection(humanCivilizations);
                _computerCivilizationLabel = FormatCivilizationSelection(computerCivilizations);
                _message = "Choose a card, charge mana, and play a card.";
                _setupMessage = "Choose one or more sets and civilizations for each deck pool.";
                _busy = false;
                _awaitingHumanBlock = false;
                _showDeckSetup = false;
                _selectedHand = null;
                _selectedCreature = null;
                _selectedOpponentCreature = null;
                _viewerCards = null;
                _showHandCardViewer = false;
                _selectingManaPayment = false;
                _selectedManaIds.Clear();
                _selectedEvolutionBaseId = 0;
                _selectedSpellTargetId = 0;
                _selectedSummonChoiceId = 0;
                _handScroll = Vector2.zero;
                _humanBattleScroll = Vector2.zero;
                _computerBattleScroll = Vector2.zero;
                _humanManaScroll = Vector2.zero;
                _computerManaScroll = Vector2.zero;
                _manaSelectionScroll = Vector2.zero;
                _spellTargetScroll = Vector2.zero;
                _summonChoiceScroll = Vector2.zero;
                _battleActionScroll = Vector2.zero;
                _controlPanelScroll = Vector2.zero;
                _artRequests.Clear();
                StartCoroutine(PreloadDeckArt(humanDeck.Concat(computerDeck).ToArray()));
            }
            catch (Exception exception)
            {
                _state = null;
                _showDeckSetup = true;
                _setupMessage = exception.Message;
            }
        }

        private static string[] OrderedSelectedSets(HashSet<string> selectedSets) =>
            SelectableSetCodes.Where(selectedSets.Contains).ToArray();

        private static Civilization[] OrderedSelectedCivilizations(
            HashSet<Civilization> selectedCivilizations) =>
            SelectableCivilizations.Where(selectedCivilizations.Contains).ToArray();

        private static string FormatSetSelection(IReadOnlyCollection<string> sets)
        {
            if (sets.Count == SelectableSetCodes.Length) return "All TCG sets";
            return sets.Count <= 4 ? string.Join(", ", sets) : $"{sets.Count} selected sets";
        }

        private static string FormatCivilizationSelection(IReadOnlyCollection<Civilization> civilizations)
        {
            if (civilizations.Count == SelectableCivilizations.Length) return "All civilizations";
            return civilizations.Count <= 3
                ? string.Join(", ", civilizations)
                : $"{civilizations.Count} civilizations";
        }

        private void ReturnToDeckSetup()
        {
            StopAllCoroutines();
            EndVideo();
            _state = null;
            _busy = false;
            _awaitingHumanBlock = false;
            _showHandCardViewer = false;
            _viewerCards = null;
            _selectedHand = null;
            _selectedCreature = null;
            _selectedOpponentCreature = null;
            _selectedSpellTargetId = 0;
            _selectedSummonChoiceId = 0;
            _showDeckSetup = true;
            _setupMessage = "Change either set/civilization pool, then build two newly randomized decks.";
        }

        private void DrawHeader()
        {
            GUI.backgroundColor = new Color(0.12f, 0.16f, 0.22f);
            GUI.Box(new Rect(0, 0, _uiWidth, 48f), GUIContent.none);
            GUI.color = Color.white;
            var turnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            var messageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            GUI.Label(new Rect(16f, 5f, 430f, 38f),
                $"TURN {_state.TurnNumber}  •  {(_state.ActivePlayerId == HumanId ? "YOUR TURN" : "COMPUTER TURN")}  •  {_state.Phase}",
                turnStyle);
            GUI.Label(new Rect(448f, 5f, 830f, 38f), _message, messageStyle);
            _animationsEnabled = GUI.Toggle(
                new Rect(_uiWidth - 292f, 10f, 274f, 28f), _animationsEnabled, " Play attack animations");
        }

        private void DrawBoard()
        {
            var board = new Rect(16f, 56f, 1268f, 832f);
            var innerX = board.x + 8f;
            var innerWidth = board.width - 16f;

            GUI.backgroundColor = new Color(0.10f, 0.15f, 0.19f);
            GUI.Box(board, GUIContent.none);
            GUI.backgroundColor = new Color(0.13f, 0.20f, 0.25f);
            GUI.Box(new Rect(board.x + 4f, board.y + 4f, board.width - 8f, 384f), GUIContent.none);
            GUI.backgroundColor = new Color(0.21f, 0.18f, 0.16f);
            GUI.Box(new Rect(board.x + 4f, board.y + 444f, board.width - 8f, 384f), GUIContent.none);

            DrawPlayerRail(_state.Players[ComputerId],
                new Rect(innerX, 64f, innerWidth, 120f), true);
            DrawManaRow(_state.Players[ComputerId],
                new Rect(innerX, 190f, innerWidth, 66f), "OPPONENT MANA • FACE UP", true,
                ref _computerManaScroll);
            DrawCardRow(_state.Players[ComputerId].BattleZone,
                new Rect(innerX, 262f, innerWidth, 142f),
                ref _computerBattleScroll, card =>
                {
                    _selectedOpponentCreature = card;
                    OpenFaceUpCardViewer(card, _state.Players[ComputerId].BattleZone, "Opponent Battle Zone");
                }, _selectedOpponentCreature, false, "OPPONENT BATTLE ZONE");

            GUI.backgroundColor = _state.ActivePlayerId == HumanId
                ? new Color(0.34f, 0.43f, 0.30f)
                : new Color(0.27f, 0.34f, 0.43f);
            GUI.Box(new Rect(innerX, 410f, innerWidth, 34f), GUIContent.none);
            var battleLineStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(innerX, 410f, innerWidth, 34f),
                _state.ActivePlayerId == HumanId ? "YOUR ACTION • BATTLE LINE" : "COMPUTER ACTION • BATTLE LINE",
                battleLineStyle);

            DrawCardRow(_state.Players[HumanId].BattleZone,
                new Rect(innerX, 450f, innerWidth, 142f),
                ref _humanBattleScroll, card =>
                {
                    if (!_busy && _state.ActivePlayerId == HumanId)
                    {
                        _selectedCreature = card;
                        _selectedHand = null;
                    }
                    OpenFaceUpCardViewer(card, _state.Players[HumanId].BattleZone, "Your Battle Zone");
                }, _selectedCreature, false, "YOUR BATTLE ZONE");

            DrawManaRow(_state.Players[HumanId],
                new Rect(innerX, 598f, innerWidth, 66f), "YOUR MANA • FACE UP & REVERSED", true,
                ref _humanManaScroll);
            DrawPlayerRail(_state.Players[HumanId],
                new Rect(innerX, 670f, innerWidth, 210f), false);
        }

        private void DrawPlayerRail(PlayerState player, Rect area, bool opponent)
        {
            GUI.backgroundColor = opponent
                ? new Color(0.11f, 0.15f, 0.21f)
                : new Color(0.16f, 0.13f, 0.12f);
            GUI.Box(area, GUIContent.none);
            var poolLabel = opponent
                ? $"COMPUTER  •  {_computerDeckLabel}  •  {_computerCivilizationLabel}"
                : $"YOU  •  {_humanDeckLabel}  •  {_humanCivilizationLabel}";
            DrawPlayerSummary(player, new Rect(area.x + 12f, area.y + 3f, area.width - 24f, 22f), poolLabel);

            var contentTop = area.y + 45f;
            var utilityCardHeight = opponent ? 70f : 94f;
            var utilityCardWidth = utilityCardHeight * 0.67f;
            var deckX = area.x + 12f;
            var shieldsX = area.x + 92f;
            const float shieldLaneWidth = 244f;
            var graveyardX = shieldsX + shieldLaneWidth + 20f;
            var handX = area.x + 452f;

            DrawZoneCaption(new Rect(deckX, area.y + 25f, 76f, 20f), "DECK");
            DrawDeckStack(new Rect(deckX, contentTop, utilityCardWidth, utilityCardHeight), player.Deck.Count);

            DrawZoneCaption(new Rect(shieldsX, area.y + 25f, shieldLaneWidth, 20f),
                $"SHIELDS ({player.Shields.Count})");
            DrawShieldRow(player.Shields.Count,
                new Rect(shieldsX, contentTop, shieldLaneWidth, utilityCardHeight), utilityCardWidth);

            DrawZoneCaption(new Rect(graveyardX, area.y + 25f, 92f, 20f),
                $"GRAVE ({player.Graveyard.Count})");
            DrawGraveyard(player,
                new Rect(graveyardX, contentTop, utilityCardWidth, utilityCardHeight));

            var handArea = new Rect(handX, contentTop, area.xMax - handX - 10f,
                area.yMax - contentTop - 6f);
            DrawZoneCaption(new Rect(handX, area.y + 25f, handArea.width, 20f),
                opponent ? $"OPPONENT HAND ({player.Hand.Count})" : $"YOUR HAND ({player.Hand.Count})");
            if (opponent)
                DrawHiddenHand(player.Hand.Count, handArea);
            else
                DrawVisibleHand(player.Hand, handArea);
        }

        private static void DrawZoneCaption(Rect rect, string text)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            GUI.color = new Color(0.83f, 0.88f, 0.93f);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        private void DrawDeckStack(Rect rect, int count)
        {
            DrawCardBack(new Rect(rect.x + 5f, rect.y - 5f, rect.width, rect.height), string.Empty);
            DrawCardBack(new Rect(rect.x + 2.5f, rect.y - 2.5f, rect.width, rect.height), string.Empty);
            DrawCardBack(rect, count.ToString());
        }

        private void DrawShieldRow(int shieldCount, Rect lane, float cardWidth)
        {
            if (shieldCount == 0)
            {
                GUI.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
                GUI.Box(new Rect(lane.x, lane.y, cardWidth, lane.height), "EMPTY");
                return;
            }

            var stride = shieldCount == 1
                ? 0f
                : Mathf.Min(cardWidth + 6f, (lane.width - cardWidth) / (shieldCount - 1));
            for (var i = 0; i < shieldCount; i++)
                DrawCardBack(new Rect(lane.x + i * stride, lane.y, cardWidth, lane.height), string.Empty);
        }

        private void DrawGraveyard(PlayerState player, Rect rect)
        {
            if (player.Graveyard.Count == 0)
            {
                GUI.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
                GUI.Box(rect, "EMPTY");
                return;
            }

            GUI.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            GUI.Box(new Rect(rect.x + 5f, rect.y - 5f, rect.width, rect.height), GUIContent.none);
            GUI.Box(new Rect(rect.x + 2.5f, rect.y - 2.5f, rect.width, rect.height), GUIContent.none);
            var topCard = player.Graveyard[player.Graveyard.Count - 1];
            DrawCardButton(topCard, rect, false, false,
                () => OpenFaceUpCardViewer(topCard, player.Graveyard,
                    player.PlayerId == HumanId ? "Your Graveyard" : "Opponent Graveyard"));
        }

        private void DrawHiddenHand(int cardCount, Rect area)
        {
            if (cardCount == 0) return;
            var cardHeight = Mathf.Min(76f, area.height);
            var cardWidth = cardHeight * 0.67f;
            var shownCards = Mathf.Min(cardCount, 10);
            var stride = cardWidth * 0.48f;
            var totalWidth = cardWidth + (shownCards - 1) * stride;
            var x = area.x + Mathf.Max(0f, (area.width - totalWidth) * 0.5f);
            for (var i = 0; i < shownCards; i++)
                DrawCardBack(new Rect(x + i * stride, area.y, cardWidth, cardHeight), string.Empty);
        }

        private void DrawVisibleHand(IReadOnlyList<CardInstance> cards, Rect area)
        {
            var cardHeight = Mathf.Min(150f, area.height - 16f);
            var cardWidth = cardHeight * 0.67f;
            var stride = Mathf.Max(72f, cardWidth * 0.78f);
            var totalWidth = cards.Count == 0 ? 0f : cardWidth + (cards.Count - 1) * stride;
            var contentWidth = Mathf.Max(area.width - 18f, totalWidth);
            var content = new Rect(0f, 0f, contentWidth, cardHeight);
            var startX = Mathf.Max(0f, (contentWidth - totalWidth) * 0.5f);
            _handScroll = GUI.BeginScrollView(area, _handScroll, content);
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var rect = new Rect(startX + i * stride, 0f, cardWidth, cardHeight);
                var pendingShieldTrigger = _state.PendingShieldTriggers.Contains(card);
                DrawCardButton(card, rect,
                    _selectedHand == card || pendingShieldTrigger, false, () =>
                {
                    if (pendingShieldTrigger ||
                        (!_busy && _state.ActivePlayerId == HumanId))
                        OpenHandCardViewer(card);
                });
                if (pendingShieldTrigger)
                {
                    GUI.color = new Color(1f, 0.78f, 0.12f);
                    GUI.Box(new Rect(rect.x + 3f, rect.yMax - 27f,
                        rect.width - 6f, 24f), "TRIGGER • 0 MANA");
                    GUI.color = Color.white;
                }
            }
            GUI.EndScrollView();
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
                $"{label}     HAND {player.Hand.Count}   •   SHIELDS {player.Shields.Count}   •   MANA {player.Mana.Count}   •   DECK {player.Deck.Count}   •   GRAVE {player.Graveyard.Count}");
        }

        private void DrawManaRow(
            PlayerState player,
            Rect rect,
            string label,
            bool reverseArtwork,
            ref Vector2 scroll)
        {
            GUI.backgroundColor = new Color(0.10f, 0.12f, 0.14f);
            GUI.Box(rect, GUIContent.none);
            DrawZoneCaption(new Rect(rect.x + 10f, rect.y + 4f, 186f, rect.height - 8f),
                $"{label} ({player.Mana.Count})");
            var inner = new Rect(rect.x + 196f, rect.y + 5f, rect.width - 204f, rect.height - 10f);
            var cardHeight = Mathf.Max(42f, inner.height - 16f);
            var cardWidth = cardHeight * 0.67f;
            var totalWidth = player.Mana.Count == 0 ? 0f : player.Mana.Count * (cardWidth + 7f) - 7f;
            var contentWidth = Mathf.Max(inner.width - 18f, totalWidth);
            var content = new Rect(0f, 0f, contentWidth, cardHeight);
            var startX = Mathf.Max(0f, (contentWidth - totalWidth) * 0.5f);
            scroll = GUI.BeginScrollView(inner, scroll, content);
            for (var i = 0; i < player.Mana.Count; i++)
            {
                var mana = player.Mana[i];
                var cardRect = new Rect(startX + i * (cardWidth + 7f), 0f, cardWidth, cardHeight);
                GUI.backgroundColor = CivilizationColor(mana.Definition);
                if (!_showHandCardViewer && !_showVideo)
                {
                    if (GUI.Button(cardRect, GUIContent.none))
                        OpenFaceUpCardViewer(mana, player.Mana, label);
                }
                else
                {
                    GUI.Box(cardRect, GUIContent.none);
                }

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
            GUI.EndScrollView();
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
            GUI.backgroundColor = label.StartsWith("OPPONENT", StringComparison.Ordinal)
                ? new Color(0.12f, 0.18f, 0.22f)
                : new Color(0.22f, 0.18f, 0.15f);
            GUI.Box(area, GUIContent.none);
            DrawZoneCaption(new Rect(area.x + 9f, area.y + 3f, 230f, 22f),
                $"{label} ({cards.Count})");
            var inner = new Rect(area.x + 5f, area.y + 25f, area.width - 10f, area.height - 30f);
            var visibleCardHeight = Mathf.Min(122f, inner.height - 7f);
            var readyCardWidth = Mathf.Clamp(visibleCardHeight * 0.67f, 58f, 98f);
            var slotWidth = visibleCardHeight;
            var totalWidth = cards.Count == 0 ? 0f : cards.Count * (slotWidth + 10f) - 10f;
            var contentWidth = Mathf.Max(inner.width - 18f, totalWidth);
            var content = new Rect(0f, 0f, contentWidth, visibleCardHeight);
            var startX = Mathf.Max(0f, (contentWidth - totalWidth) * 0.5f);
            scroll = GUI.BeginScrollView(inner, scroll, content);
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var slot = new Rect(startX + i * (slotWidth + 10f), 0f, slotWidth, content.height);
                var rect = card.Tapped
                    ? new Rect(slot.x, slot.center.y - readyCardWidth * 0.5f, slot.width, readyCardWidth)
                    : new Rect(slot.center.x - readyCardWidth * 0.5f, slot.y, readyCardWidth, slot.height);
                DrawCardButton(card, rect, selected == card, faceDown, () => onClick(card));
            }
            GUI.EndScrollView();
        }

        private void DrawCardButton(
            CardInstance card,
            Rect rect,
            bool selected,
            bool faceDown,
            Action clicked,
            bool allowWhileViewerOpen = false)
        {
            GUI.backgroundColor = selected ? new Color(1f, 0.75f, 0.15f) : CivilizationColor(card.Definition);
            var acceptsInput = !_showVideo && (allowWhileViewerOpen || !_showHandCardViewer);
            if (acceptsInput)
            {
                if (GUI.Button(rect, GUIContent.none)) clicked();
            }
            else
            {
                // A disabled button can still claim the IMGUI mouse event before the modal viewer is drawn.
                // Render a passive frame so the viewer's Mana Zone/Battle Zone controls receive the click.
                GUI.Box(rect, GUIContent.none);
            }

            var inset = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8);
            Texture cardTexture = null;
            if (faceDown && _cardBack != null) cardTexture = _cardBack;
            else if (!faceDown && _cardArt.TryGetValue(card.Definition.Id, out var texture))
                cardTexture = texture;

            if (cardTexture != null)
            {
                var displayTexture = card.Tapped ? GetTurnedCardArt(cardTexture) : cardTexture;
                GUI.DrawTexture(inset, displayTexture, ScaleMode.ScaleToFit);
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

            if (card.Tapped)
            {
                GUI.color = new Color(0, 0, 0, 0.72f);
                GUI.Box(new Rect(rect.x + 7f, rect.center.y - 13f, rect.width - 14f, 26f), "TURNED");
            }
            GUI.color = Color.white;
        }

        private Texture GetTurnedCardArt(Texture source)
        {
            if (source == null || _turnCardMaterial == null) return source;
            if (_turnedCardArt.TryGetValue(source, out var cached)) return cached;
            if (Event.current.type != EventType.Repaint) return source;

            const int previewWidth = 256;
            var previewHeight = Mathf.Max(1,
                Mathf.RoundToInt(previewWidth * (float)source.width / Mathf.Max(1, source.height)));
            var turned = new RenderTexture(previewWidth, previewHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = $"Turned {source.name}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            turned.Create();
            Graphics.Blit(source, turned, _turnCardMaterial);
            _turnedCardArt[source] = turned;
            return turned;
        }

        private void DrawControlPanel()
        {
            var rect = new Rect(1296f, 56f, 288f, 832f);
            GUI.backgroundColor = new Color(0.13f, 0.16f, 0.21f);
            GUI.Box(rect, GUIContent.none);

            var viewport = new Rect(rect.x + 10f, rect.y + 10f, rect.width - 20f, rect.height - 20f);
            var contentWidth = viewport.width - 18f;
            var wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            var panelHeadingStyle = new GUIStyle(wrapStyle)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold
            };

            var selected = _selectedHand ?? _selectedCreature ?? _selectedOpponentCreature;
            var selectedHand = _selectedHand;
            var selectedCreature = _selectedCreature;
            var modalOpen = _showHandCardViewer || _showVideo;
            var legalCreatureTargets = selectedCreature != null && !selectedCreature.Tapped
                ? _state.Players[ComputerId].BattleZone
                    .Where(card => card.Tapped ||
                                   selectedCreature.Definition.HasKeyword(CardKeyword.AttackUntappedCreature))
                    .ToArray()
                : Array.Empty<CardInstance>();
            var readyBlockers = selectedCreature != null && !selectedCreature.Tapped
                ? EligibleBlockers(_state.Players[ComputerId]).Count()
                : 0;
            var humanBlockers = _awaitingHumanBlock
                ? EligibleBlockers(_state.Players[HumanId]).ToArray()
                : Array.Empty<CardInstance>();
            var pendingTriggers = _state.PendingShieldTriggers.Count > 0 &&
                                  _state.ActivePlayerId == ComputerId
                ? _state.PendingShieldTriggers.ToArray()
                : Array.Empty<CardInstance>();

            var estimatedHeight = 38f +
                                  PanelTextHeight($"Your pool: {_humanDeckLabel} • {_humanCivilizationLabel}",
                                      wrapStyle, contentWidth) +
                                  PanelTextHeight($"Computer: {_computerDeckLabel} • {_computerCivilizationLabel}",
                                      wrapStyle, contentWidth) + 18f;
            if (selected != null)
            {
                estimatedHeight += PanelTextHeight(selected.Definition.Name, wrapStyle, contentWidth) +
                                   PanelTextHeight(
                                       $"Cost {selected.Definition.Cost}  •  Power {selected.Definition.Power}",
                                       wrapStyle, contentWidth) +
                                   PanelTextHeight(selected.Definition.RulesText, wrapStyle, contentWidth) + 18f;
            }

            if (modalOpen)
            {
                estimatedHeight += PanelTextHeight(
                    "Close the card viewer before choosing a board action.",
                    wrapStyle, contentWidth) + 18f;
            }
            else
            {
                if (selectedHand != null) estimatedHeight += 44f;
                if (selectedCreature != null)
                {
                    if (selectedCreature.Tapped)
                    {
                        estimatedHeight += PanelTextHeight(
                            "TURNED — this creature cannot attack again this turn.",
                            wrapStyle, contentWidth) + 8f;
                    }
                    else
                    {
                        estimatedHeight += 44f + legalCreatureTargets.Length * 46f;
                        if (readyBlockers > 0)
                            estimatedHeight += PanelTextHeight(
                                $"Opponent has {readyBlockers} ready Blocker(s). Attack the player or a legal turned creature; the opponent chooses which ready Blocker intercepts.",
                                wrapStyle, contentWidth) + 8f;
                        if (legalCreatureTargets.Length == 0 &&
                            _state.Players[ComputerId].BattleZone.Count > 0)
                            estimatedHeight += PanelTextHeight(
                                "No opposing creature is currently a legal attack target. Normally, only turned creatures can be attacked.",
                                wrapStyle, contentWidth) + 8f;
                    }
                }

                estimatedHeight += 44f;
                if (_awaitingHumanBlock)
                {
                    estimatedHeight += 18f +
                                       PanelTextHeight("The computer is attacking. Choose a blocker:",
                                           wrapStyle, contentWidth) +
                                       humanBlockers.Length * 40f + 40f;
                }

                if (pendingTriggers.Length > 0)
                {
                    estimatedHeight += 18f +
                                       PanelTextHeight("SHIELD TRIGGER — play now for 0 mana.",
                                           wrapStyle, contentWidth) +
                                       pendingTriggers.Length * 42f + 42f;
                }
            }

            const string footer =
                "Prototype: hand cards use manual mana selection. Single-card spell targets are chosen in the card viewer.";
            estimatedHeight += PanelTextHeight(footer, wrapStyle, contentWidth) + 28f;
            var contentHeight = Mathf.Max(viewport.height - 4f, estimatedHeight);
            _controlPanelScroll = GUI.BeginScrollView(
                viewport,
                _controlPanelScroll,
                new Rect(0f, 0f, contentWidth, contentHeight));

            var y = 0f;
            DrawPanelLabel(ref y, contentWidth, "DUEL CONTROLS", panelHeadingStyle, 28f, 2f);
            DrawPanelLabel(ref y, contentWidth,
                $"Your pool: {_humanDeckLabel} • {_humanCivilizationLabel}", wrapStyle, 22f, 2f);
            DrawPanelLabel(ref y, contentWidth,
                $"Computer: {_computerDeckLabel} • {_computerCivilizationLabel}", wrapStyle, 22f, 8f);

            if (selected != null)
            {
                DrawPanelLabel(ref y, contentWidth, selected.Definition.Name, wrapStyle, 22f, 2f);
                DrawPanelLabel(ref y, contentWidth,
                    $"Cost {selected.Definition.Cost}  •  Power {selected.Definition.Power}",
                    wrapStyle, 22f, 2f);
                DrawPanelLabel(ref y, contentWidth, selected.Definition.RulesText, wrapStyle, 22f, 10f);
            }

            if (modalOpen)
            {
                DrawPanelLabel(ref y, contentWidth,
                    "Close the card viewer before choosing a board action.",
                    wrapStyle, 22f, 12f);
            }
            else
            {
                GUI.enabled = !_busy && !_state.IsFinished && _state.ActivePlayerId == HumanId;
                if (selectedHand != null &&
                    DrawPanelButton(ref y, contentWidth, "Open Selected Card", 38f, 6f))
                    _showHandCardViewer = true;

                if (selectedCreature != null)
                {
                    if (selectedCreature.Tapped)
                    {
                        DrawPanelLabel(ref y, contentWidth,
                            "TURNED — this creature cannot attack again this turn.",
                            wrapStyle, 22f, 8f);
                    }
                    else
                    {
                        if (DrawPanelButton(ref y, contentWidth, "Attack Player", 38f, 6f))
                            StartHumanAttack(AttackTargetKind.Player, 0);

                        foreach (var target in legalCreatureTargets)
                            if (DrawPanelButton(ref y, contentWidth,
                                    $"Attack {target.Definition.Name} — {target.Definition.Power}", 40f, 6f))
                                StartHumanAttack(AttackTargetKind.Creature, target.InstanceId);

                        if (readyBlockers > 0)
                            DrawPanelLabel(ref y, contentWidth,
                                $"Opponent has {readyBlockers} ready Blocker(s). Attack the player or a legal turned creature; the opponent chooses which ready Blocker intercepts.",
                                wrapStyle, 22f, 8f);
                        if (legalCreatureTargets.Length == 0 &&
                            _state.Players[ComputerId].BattleZone.Count > 0)
                            DrawPanelLabel(ref y, contentWidth,
                                "No opposing creature is currently a legal attack target. Normally, only turned creatures can be attacked.",
                                wrapStyle, 22f, 8f);
                    }
                }

                if (DrawPanelButton(ref y, contentWidth, "End Turn", 38f, 6f)) EndHumanTurn();
                GUI.enabled = true;

                if (_awaitingHumanBlock)
                {
                    y += 12f;
                    DrawPanelLabel(ref y, contentWidth,
                        "The computer is attacking. Choose a blocker:", wrapStyle, 22f, 4f);
                    foreach (var blocker in humanBlockers)
                        if (DrawPanelButton(ref y, contentWidth,
                                $"Block with {blocker.Definition.Name}", 34f, 6f))
                            ResolveHumanBlock(blocker.InstanceId);
                    if (DrawPanelButton(ref y, contentWidth, "Do Not Block", 34f, 6f))
                        ResolveHumanBlock(0);
                }

                if (pendingTriggers.Length > 0)
                {
                    y += 12f;
                    DrawPanelLabel(ref y, contentWidth,
                        "SHIELD TRIGGER — play now for 0 mana.", wrapStyle, 22f, 4f);
                    foreach (var trigger in pendingTriggers)
                        if (DrawPanelButton(ref y, contentWidth,
                                $"PLAY {trigger.Definition.Name} — 0 MANA", 36f, 6f))
                            CastHumanShieldTrigger(trigger);
                    if (DrawPanelButton(ref y, contentWidth, "Skip Remaining Triggers", 36f, 6f))
                        ShowResult(_engine.ResolvePendingShieldTriggers(_state, HumanId));
                }
            }

            y += 12f;
            DrawPanelLabel(ref y, contentWidth, footer, wrapStyle, 22f, 4f);
            GUI.EndScrollView();
            GUI.enabled = true;
        }

        private static float PanelTextHeight(string text, GUIStyle style, float width) =>
            Mathf.Max(22f, style.CalcHeight(new GUIContent(text ?? string.Empty), width));

        private static void DrawPanelLabel(
            ref float y,
            float width,
            string text,
            GUIStyle style,
            float minimumHeight,
            float spacingAfter)
        {
            var height = Mathf.Max(minimumHeight,
                style.CalcHeight(new GUIContent(text ?? string.Empty), width));
            GUI.Label(new Rect(0f, y, width, height), text, style);
            y += height + spacingAfter;
        }

        private static bool DrawPanelButton(
            ref float y,
            float width,
            string text,
            float height,
            float spacingAfter)
        {
            var pressed = GUI.Button(new Rect(0f, y, width, height), text);
            y += height + spacingAfter;
            return pressed;
        }

        private void OpenHandCardViewer(CardInstance card)
        {
            _selectedHand = card;
            _selectedCreature = null;
            _showHandCardViewer = true;
            _viewerCards = _state.Players[HumanId].Hand;
            _viewerZoneName = "Your Hand";
            _viewerAllowsHandActions = true;
            _selectingManaPayment = false;
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _selectedSpellTargetId = 0;
            _selectedSummonChoiceId = 0;
            _summonChoiceScroll = Vector2.zero;
            var pendingShieldTrigger = _state.PendingShieldTriggers.Contains(card);
            if (pendingShieldTrigger && card.Definition.Type == CardType.Spell &&
                _engine.SpellUsesSingleCardTarget(card.Definition))
            {
                var candidates = _engine.GetSpellTargetCandidates(_state, HumanId,
                    card.InstanceId);
                if (_selectedOpponentCreature != null &&
                    candidates.Contains(_selectedOpponentCreature))
                    _selectedSpellTargetId = _selectedOpponentCreature.InstanceId;
                else if (candidates.Count == 1)
                    _selectedSpellTargetId = candidates[0].InstanceId;
            }
            _viewerMessage = pendingShieldTrigger
                ? "Shield trigger ready — play this card immediately for 0 mana."
                : string.Empty;
        }

        private void OpenFaceUpCardViewer(CardInstance card, IReadOnlyList<CardInstance> cards, string zoneName)
        {
            _selectedTapTargetId = 0;
            _selectedHand = card;
            _viewerCards = cards;
            _viewerZoneName = zoneName;
            _viewerAllowsHandActions = false;
            _showHandCardViewer = true;
            _selectingManaPayment = false;
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _selectedSpellTargetId = 0;
            _selectedSummonChoiceId = 0;
            _summonChoiceScroll = Vector2.zero;
            _viewerMessage = card.Tapped ? "This card is currently turned." : string.Empty;
        }

        private void CloseHandCardViewer()
        {
            _selectedTapTargetId = 0;
            var wasHandViewer = _viewerAllowsHandActions;
            _showHandCardViewer = false;
            _viewerCards = null;
            _viewerZoneName = string.Empty;
            _viewerAllowsHandActions = false;
            _selectingManaPayment = false;
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _selectedSpellTargetId = 0;
            _selectedSummonChoiceId = 0;
            _summonChoiceScroll = Vector2.zero;
            _viewerMessage = string.Empty;
            if (!wasHandViewer) _selectedHand = null;
        }

        private void NavigateViewer(int direction)
        {
            _selectedTapTargetId = 0;
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
            _selectedSpellTargetId = 0;
            _selectedSummonChoiceId = 0;
            _summonChoiceScroll = Vector2.zero;
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

            var pendingShieldTrigger = _viewerAllowsHandActions &&
                                       _state.PendingShieldTriggers.Contains(
                                           _selectedHand);
            if (pendingShieldTrigger)
            {
                DrawShieldTriggerActions(new Rect(infoRect.x,
                    infoRect.y + 314f, infoRect.width,
                    infoRect.height - 314f), bodyStyle);
            }
            else if (_selectingManaPayment && _viewerAllowsHandActions)
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
                    DrawEnlargedBattleActions(new Rect(infoRect.x, infoRect.y + 314f,
                        infoRect.width, infoRect.height - 314f), bodyStyle);
                else
                    GUI.Label(new Rect(infoRect.x, infoRect.yMax - 66f, infoRect.width, 44f),
                        "Face-up zone viewer — use the arrows to inspect the other cards in this zone.", bodyStyle);
            }
        }

        private void DrawShieldTriggerActions(Rect area, GUIStyle bodyStyle)
        {
            var trigger = _selectedHand;
            GUI.Box(area, GUIContent.none);
            var headingStyle = new GUIStyle(bodyStyle)
            {
                fontStyle = FontStyle.Bold
            };
            headingStyle.normal.textColor = new Color(1f, 0.82f, 0.22f);
            GUI.Label(new Rect(area.x + 10f, area.y + 8f,
                area.width - 20f, 28f),
                "SHIELD TRIGGER — NO MANA PAYMENT", headingStyle);
            GUI.Label(new Rect(area.x + 10f, area.y + 38f,
                area.width - 20f, 44f),
                "Play it now for 0 mana, or decline and keep the broken shield in your hand.",
                bodyStyle);

            var usesTarget = trigger.Definition.Type == CardType.Spell &&
                             _engine.SpellUsesSingleCardTarget(
                                 trigger.Definition);
            var requiresTarget = usesTarget &&
                                 _engine.SpellRequiresSingleCardTarget(
                                     trigger.Definition);
            IReadOnlyList<CardInstance> targets = Array.Empty<CardInstance>();
            var actionTop = area.y + 88f;
            if (usesTarget)
            {
                targets = _engine.GetSpellTargetCandidates(_state, HumanId,
                    trigger.InstanceId);
                if (_selectedSpellTargetId != 0 && !targets.Any(card =>
                        card.InstanceId == _selectedSpellTargetId))
                    _selectedSpellTargetId = 0;
                if (_selectedSpellTargetId == 0 && targets.Count == 1)
                    _selectedSpellTargetId = targets[0].InstanceId;

                var targetMessage = targets.Count == 0
                    ? "No legal target is currently available."
                    : _selectedSpellTargetId == 0
                        ? "Choose the card affected by this shield trigger:"
                        : $"Target: {targets.First(card => card.InstanceId == _selectedSpellTargetId).Definition.Name}";
                GUI.Label(new Rect(area.x + 10f, actionTop,
                    area.width - 20f, 28f), targetMessage, bodyStyle);

                var viewport = new Rect(area.x + 8f, actionTop + 30f,
                    area.width - 16f, 100f);
                GUI.Box(viewport, GUIContent.none);
                const float slotWidth = 108f;
                const float slotHeight = 80f;
                var content = new Rect(0f, 0f,
                    Mathf.Max(viewport.width - 18f,
                        targets.Count * slotWidth), slotHeight);
                _spellTargetScroll = GUI.BeginScrollView(viewport,
                    _spellTargetScroll, content);
                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    var slot = new Rect(i * slotWidth, 0f,
                        slotWidth - 8f, slotHeight);
                    var uprightWidth = slotHeight * 0.67f;
                    var targetRect = target.Tapped
                        ? new Rect(slot.x + 4f,
                            slot.center.y - uprightWidth * 0.5f,
                            slotHeight, uprightWidth)
                        : new Rect(slot.center.x - uprightWidth * 0.5f,
                            slot.y, uprightWidth, slotHeight);
                    DrawCardButton(target, targetRect,
                        _selectedSpellTargetId == target.InstanceId, false,
                        () =>
                        {
                            _selectedSpellTargetId = target.InstanceId;
                            if (_state.Players[ComputerId].BattleZone.Contains(target))
                                _selectedOpponentCreature = target;
                            _viewerMessage = $"Selected {target.Definition.Name} as the shield-trigger target.";
                        }, true);
                }
                GUI.EndScrollView();
            }

            var playRect = new Rect(area.x + 8f, area.yMax - 58f,
                area.width * 0.62f, 50f);
            var declineRect = new Rect(area.x + area.width * 0.66f,
                area.yMax - 58f, area.width * 0.34f - 8f, 50f);
            GUI.enabled = !requiresTarget || _selectedSpellTargetId != 0;
            if (GUI.Button(playRect, "PLAY FOR 0 MANA"))
            {
                var result = CastHumanShieldTrigger(trigger,
                    _selectedSpellTargetId);
                if (result.Success)
                {
                    _selectedHand = null;
                    CloseHandCardViewer();
                }
                else
                {
                    _viewerMessage = result.Error;
                }
            }
            GUI.enabled = true;
            if (GUI.Button(declineRect, "DECLINE"))
            {
                var result = _engine.DeclineShieldTrigger(_state, HumanId,
                    trigger.InstanceId);
                ShowResult(result, result.Success
                    ? $"Kept {trigger.Definition.Name} in your hand."
                    : null);
                if (result.Success)
                {
                    _selectedHand = null;
                    CloseHandCardViewer();
                }
                else
                {
                    _viewerMessage = result.Error;
                }
            }
        }

        private void DrawEnlargedBattleActions(Rect area, GUIStyle bodyStyle)
        {
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 10f, area.y + 7f, area.width - 20f, 26f), "AVAILABLE ACTIONS", bodyStyle);

            var attacker = _selectedHand;
            var canUseControls = !_busy && !_state.IsFinished && _state.ActivePlayerId == HumanId;
            var hasSummoningSickness = _engine.HasSummoningSickness(_state, attacker);
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

            var noteTop = area.y + 108f;
            if (_engine.HasTapAbility(_state, attacker))
            {
                var tapTargets = _engine.GetTapAbilityTargetCandidates(
                    _state, HumanId, attacker.InstanceId);
                if (!tapTargets.Any(card => card.InstanceId == _selectedTapTargetId))
                    _selectedTapTargetId = tapTargets.Count == 1
                        ? tapTargets[0].InstanceId : 0;
                if (tapTargets.Count > 0)
                {
                    GUI.Label(new Rect(area.x + 10f, noteTop, area.width - 20f, 28f),
                        "Choose the card affected by the tap ability:", bodyStyle);
                    var targetViewport = new Rect(area.x + 8f, noteTop + 30f,
                        area.width - 16f, 66f);
                    var targetContent = new Rect(0f, 0f,
                        Mathf.Max(targetViewport.width - 18f,
                            tapTargets.Count * (buttonWidth + 10f)), buttonHeight);
                    _summonChoiceScroll = GUI.BeginScrollView(targetViewport,
                        _summonChoiceScroll, targetContent);
                    GUI.enabled = canUseControls;
                    for (var i = 0; i < tapTargets.Count; i++)
                    {
                        var target = tapTargets[i];
                        GUI.backgroundColor = _selectedTapTargetId == target.InstanceId
                            ? new Color(1f, 0.75f, 0.15f)
                            : new Color(0.20f, 0.28f, 0.36f);
                        var label = (_selectedTapTargetId == target.InstanceId ? "✓ " : "") +
                                    target.Definition.Name;
                        if (GUI.Button(new Rect(i * (buttonWidth + 10f), 0f,
                                buttonWidth, buttonHeight), label))
                            _selectedTapTargetId = target.InstanceId;
                    }
                    GUI.EndScrollView();
                    noteTop += 102f;
                }
                GUI.enabled = canUseControls &&
                              (tapTargets.Count == 0 || _selectedTapTargetId != 0);
                GUI.backgroundColor = new Color(0.30f, 0.58f, 0.36f);
                if (GUI.Button(new Rect(area.x + 10f, noteTop,
                        area.width - 20f, 46f), "Use Tap Ability — Instead of Attacking"))
                {
                    var result = _engine.UseTapAbility(_state, HumanId,
                        attacker.InstanceId, _selectedTapTargetId);
                    ShowResult(result, result.Success
                        ? $"Used {attacker.Definition.Name}'s tap ability." : null);
                    if (result.Success)
                    {
                        CloseHandCardViewer();
                        return;
                    }
                    _viewerMessage = result.Error;
                }
                GUI.enabled = true;
                noteTop += 54f;
            }

            var readyBlockers = EligibleBlockers(_state.Players[ComputerId]).Count();
            var note = readyBlockers > 0
                ? $"Opponent has {readyBlockers} ready Blocker(s); the opponent chooses which one intercepts."
                : legalTargets.Count == 0 && !canAttackPlayer
                    ? "This creature currently has no legal attack."
                    : "Turned opposing creatures appear here as direct attack targets.";
            GUI.Label(new Rect(area.x + 10f, noteTop, area.width - 20f, 44f), note, bodyStyle);
        }

        private void BeginManaPayment()
        {
            _selectedManaIds.Clear();
            _selectedEvolutionBaseId = 0;
            _selectedSpellTargetId = 0;
            _selectedSummonChoiceId = 0;
            _spellTargetScroll = Vector2.zero;
            _summonChoiceScroll = Vector2.zero;
            if (_selectedHand.Definition.Type == CardType.Spell &&
                _engine.SpellUsesSingleCardTarget(_selectedHand.Definition))
            {
                var candidates = _engine.GetSpellTargetCandidates(_state, HumanId,
                    _selectedHand.InstanceId);
                if (_selectedOpponentCreature != null &&
                    candidates.Contains(_selectedOpponentCreature))
                    _selectedSpellTargetId = _selectedOpponentCreature.InstanceId;
                else if (candidates.Count == 1)
                    _selectedSpellTargetId = candidates[0].InstanceId;
            }
            if (_engine.CreatureRequiresSingleCardSummonChoice(
                    _selectedHand.Definition))
            {
                var candidates = _engine.GetCreatureSummonChoiceCandidates(
                    _state, HumanId, _selectedHand.InstanceId);
                if (candidates.Count == 1)
                    _selectedSummonChoiceId = candidates[0].InstanceId;
            }
            _selectingManaPayment = true;
            var paymentMessage = _selectedHand.Definition.Cost == 0
                ? "This card costs no mana. Confirm to play it."
                : $"Select exactly {_selectedHand.Definition.Cost} untapped mana cards to turn.";
            _viewerMessage = _engine.CreatureRequiresSingleCardSummonChoice(
                _selectedHand.Definition)
                ? paymentMessage + " Also choose the card required by its summon ability."
                : paymentMessage;
        }

        private void DrawManaPaymentSelector(Rect area)
        {
            var player = _state.Players[HumanId];
            var cost = _selectedHand.Definition.Cost;
            var isEvolution = _selectedHand.Definition.Type == CardType.EvolutionCreature;
            var manaTop = area.y;
            var spellUsesTarget = _selectedHand.Definition.Type == CardType.Spell &&
                                  _engine.SpellUsesSingleCardTarget(
                                      _selectedHand.Definition);
            var spellRequiresTarget = spellUsesTarget &&
                                      _engine.SpellRequiresSingleCardTarget(
                                          _selectedHand.Definition);
            var requiresSummonChoice =
                _engine.CreatureRequiresSingleCardSummonChoice(
                    _selectedHand.Definition);
            IReadOnlyList<CardInstance> spellTargets = Array.Empty<CardInstance>();
            IReadOnlyList<CardInstance> summonChoiceCandidates =
                Array.Empty<CardInstance>();

            if (spellUsesTarget)
            {
                spellTargets = _engine.GetSpellTargetCandidates(_state, HumanId,
                    _selectedHand.InstanceId);
                if (_selectedSpellTargetId != 0 && !spellTargets.Any(card =>
                        card.InstanceId == _selectedSpellTargetId))
                    _selectedSpellTargetId = 0;

                var targetText = spellTargets.Count == 0
                    ? "No legal target is currently available."
                    : _selectedSpellTargetId == 0
                        ? "Choose the card this spell will affect. Turned creatures are valid unless the printed rule says otherwise."
                        : $"Target: {spellTargets.First(card => card.InstanceId == _selectedSpellTargetId).Definition.Name}";
                GUI.Label(new Rect(area.x, manaTop, area.width, 42f), targetText);

                var targetViewport = new Rect(area.x, manaTop + 44f, area.width, 104f);
                GUI.Box(targetViewport, GUIContent.none);
                const float targetSlotWidth = 108f;
                const float targetSlotHeight = 84f;
                var targetContent = new Rect(0f, 0f,
                    Mathf.Max(targetViewport.width - 18f,
                        spellTargets.Count * targetSlotWidth), targetSlotHeight);
                _spellTargetScroll = GUI.BeginScrollView(targetViewport,
                    _spellTargetScroll, targetContent);
                for (var i = 0; i < spellTargets.Count; i++)
                {
                    var target = spellTargets[i];
                    var slot = new Rect(i * targetSlotWidth, 0f,
                        targetSlotWidth - 8f, targetSlotHeight);
                    var uprightWidth = targetSlotHeight * 0.67f;
                    var targetRect = target.Tapped
                        ? new Rect(slot.x + 4f,
                            slot.center.y - uprightWidth * 0.5f,
                            targetSlotHeight, uprightWidth)
                        : new Rect(slot.center.x - uprightWidth * 0.5f,
                            slot.y, uprightWidth, targetSlotHeight);
                    DrawCardButton(target, targetRect,
                        _selectedSpellTargetId == target.InstanceId, false, () =>
                        {
                            _selectedSpellTargetId = target.InstanceId;
                            if (_state.Players[ComputerId].BattleZone.Contains(target))
                                _selectedOpponentCreature = target;
                            _viewerMessage = $"Selected {target.Definition.Name} as the spell target.";
                        }, true);
                }
                GUI.EndScrollView();
                manaTop += 158f;
            }

            if (requiresSummonChoice)
            {
                summonChoiceCandidates =
                    _engine.GetCreatureSummonChoiceCandidates(_state, HumanId,
                        _selectedHand.InstanceId);
                if (_selectedSummonChoiceId != 0 &&
                    !summonChoiceCandidates.Any(card =>
                        card.InstanceId == _selectedSummonChoiceId))
                    _selectedSummonChoiceId = 0;
                if (_selectedSummonChoiceId == 0 &&
                    summonChoiceCandidates.Count == 1)
                    _selectedSummonChoiceId =
                        summonChoiceCandidates[0].InstanceId;

                var choiceStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    fontStyle = FontStyle.Bold
                };
                choiceStyle.normal.textColor = new Color(1f, 0.78f, 0.18f);
                var choiceText = summonChoiceCandidates.Count == 0
                    ? "SUMMON ABILITY — no legal card is currently available."
                    : "SUMMON ABILITY — choose the card this effect will use:\n" +
                      _engine.CreatureSingleCardSummonChoiceText(
                          _selectedHand.Definition);
                GUI.Label(new Rect(area.x, manaTop, area.width, 50f),
                    choiceText, choiceStyle);

                var choiceViewport = new Rect(area.x, manaTop + 52f,
                    area.width, 104f);
                GUI.Box(choiceViewport, GUIContent.none);
                const float choiceSlotWidth = 108f;
                const float choiceCardHeight = 84f;
                var choiceContent = new Rect(0f, 0f,
                    Mathf.Max(choiceViewport.width - 18f,
                        summonChoiceCandidates.Count * choiceSlotWidth),
                    choiceCardHeight);
                _summonChoiceScroll = GUI.BeginScrollView(choiceViewport,
                    _summonChoiceScroll, choiceContent);
                for (var i = 0; i < summonChoiceCandidates.Count; i++)
                {
                    var candidate = summonChoiceCandidates[i];
                    var choiceCardWidth = choiceCardHeight * 0.67f;
                    var cardRect = new Rect(
                        i * choiceSlotWidth +
                        (choiceSlotWidth - choiceCardWidth) * 0.5f,
                        0f, choiceCardWidth, choiceCardHeight);
                    var selectedForAbility =
                        _selectedSummonChoiceId == candidate.InstanceId;
                    DrawCardButton(candidate, cardRect, selectedForAbility,
                        false, () =>
                        {
                            _selectedSummonChoiceId = candidate.InstanceId;
                            _viewerMessage =
                                $"Selected {candidate.Definition.Name} for the summon ability.";
                        }, true);
                    if (selectedForAbility)
                    {
                        GUI.color = new Color(1f, 0.78f, 0.12f);
                        GUI.Box(new Rect(i * choiceSlotWidth + 4f,
                            cardRect.yMax - 25f, choiceSlotWidth - 8f, 23f),
                            "GRAVEYARD");
                        GUI.color = Color.white;
                    }
                }
                GUI.EndScrollView();
                manaTop += 164f;
            }

            if (isEvolution)
            {
                GUI.Label(new Rect(area.x, manaTop, area.width, 24f),
                    $"Choose a {_selectedHand.Definition.EvolutionRace} in your battle zone to evolve:");
                var bases = player.BattleZone.Where(card => card.Definition.Races.Any(race =>
                    race.Equals(_selectedHand.Definition.EvolutionRace, StringComparison.OrdinalIgnoreCase))).ToList();
                var baseCardHeight = 90f;
                var baseCardWidth = baseCardHeight * 0.66f;
                for (var i = 0; i < bases.Count; i++)
                {
                    var baseCard = bases[i];
                    var baseRect = new Rect(area.x + i * (baseCardWidth + 10f),
                        manaTop + 26f, baseCardWidth, baseCardHeight);
                    DrawCardButton(baseCard, baseRect, _selectedEvolutionBaseId == baseCard.InstanceId, false,
                        () => _selectedEvolutionBaseId = baseCard.InstanceId, true);
                }
                if (bases.Count == 0)
                    GUI.Label(new Rect(area.x, manaTop + 32f, area.width, 48f),
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
                }, true);
                GUI.enabled = true;
                if (selected)
                {
                    GUI.color = new Color(1f, 0.76f, 0.12f);
                    GUI.Box(new Rect(rect.x + 4f, rect.y + rect.height - 28f, rect.width - 8f, 24f), "TURN");
                    GUI.color = Color.white;
                }
            }
            GUI.EndScrollView();

            var confirmEnabled = _selectedManaIds.Count == cost &&
                                 (!isEvolution || _selectedEvolutionBaseId != 0) &&
                                 (!spellRequiresTarget ||
                                  _selectedSpellTargetId != 0) &&
                                 (!requiresSummonChoice ||
                                  _selectedSummonChoiceId != 0);
            GUI.enabled = confirmEnabled;
            if (GUI.Button(new Rect(area.x, area.yMax - 56f, area.width * 0.62f, 48f), $"Confirm — Turn {cost} Mana"))
                ConfirmManualPlay();
            GUI.enabled = true;
            if (GUI.Button(new Rect(area.x + area.width * 0.66f, area.yMax - 56f, area.width * 0.34f, 48f), "Back"))
            {
                _selectingManaPayment = false;
                _selectedManaIds.Clear();
                _selectedEvolutionBaseId = 0;
                _selectedSpellTargetId = 0;
                _selectedSummonChoiceId = 0;
                _viewerMessage = string.Empty;
            }
        }

        private void ConfirmManualPlay()
        {
            var card = _selectedHand;
            var payment = _selectedManaIds.ToArray();
            RuleResult result;
            if (card.Definition.Type == CardType.Spell)
                result = TryCastSpell(HumanId, card, payment,
                    _selectedSpellTargetId);
            else
                result = _engine.SummonCreature(_state, HumanId,
                    card.InstanceId, payment, _selectedEvolutionBaseId,
                    _selectedSummonChoiceId);

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
            var candidates = new List<long>();
            if (preferredTarget != 0) candidates.Add(preferredTarget);
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

            if (_state.PendingAttack == null)
            {
                ShowResult(result, $"{attacker.Definition.Name}'s attack ended during its attack abilities.");
                ResolveComputerShieldTriggers();
                _selectedCreature = null;
                _selectedOpponentCreature = null;
                return true;
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

                if (_state.PendingAttack == null)
                {
                    _message = $"{attacker.Definition.Name}'s attack ended during its attack abilities.";
                    while (_state.PendingShieldTriggers.Count > 0) yield return null;
                    continue;
                }

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

        private RuleResult CastHumanShieldTrigger(CardInstance trigger,
            long preferredTarget = 0)
        {
            var candidates = new List<long>();
            if (preferredTarget != 0) candidates.Add(preferredTarget);
            if (trigger.Definition.Type == CardType.Spell)
                candidates.AddRange(_engine.GetSpellTargetCandidates(_state,
                    HumanId, trigger.InstanceId).Select(card => card.InstanceId));
            candidates.AddRange(_state.Players[ComputerId].BattleZone
                .Select(card => card.InstanceId));
            candidates.AddRange(_state.Players[HumanId].BattleZone
                .Select(card => card.InstanceId));
            candidates.AddRange(_state.Players[HumanId].Graveyard
                .Where(card => card.Definition.IsCreature)
                .Select(card => card.InstanceId));
            candidates.Add(0);
            RuleResult last = null;
            foreach (var target in candidates.Distinct())
            {
                last = _engine.CastShieldTrigger(_state, HumanId, trigger.InstanceId, target);
                if (last.Success) break;
            }
            last ??= RuleResult.Fail("No legal shield-trigger target.");
            ShowResult(last, last.Success
                ? $"Played {trigger.Definition.Name} for 0 mana."
                : null);
            return last;
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
            if (!_animationsEnabled) yield break;
            var localVideo = Resources.Load<VideoClip>($"CardGame/Videos/{attacker.Definition.DatabaseMediaId:0000}");
            if (localVideo != null)
            {
                _showVideo = true;
                _videoPlayer.source = VideoSource.VideoClip;
                _videoPlayer.clip = localVideo;
            }
            else
            {
                if (!attacker.Definition.HasDatabaseVideo) yield break;
                _showVideo = true;
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
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 34,
                fontStyle = FontStyle.Bold
            };
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18
            };
            GUI.Label(new Rect(_uiWidth / 2f - 220f, _uiHeight / 2f - 90f, 440f, 52f), text, titleStyle);
            GUI.Label(new Rect(_uiWidth / 2f - 220f, _uiHeight / 2f - 32f, 440f, 40f),
                _state.WinReason, bodyStyle);
            if (GUI.Button(new Rect(_uiWidth / 2f - 155f, _uiHeight / 2f + 32f, 310f, 58f),
                    "NEW DUEL / CHANGE SETS"))
                ReturnToDeckSetup();
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

        private static Color CivilizationColor(Civilization civilization)
        {
            switch (civilization)
            {
                case Civilization.Fire:
                    return new Color(0.56f, 0.18f, 0.12f);
                case Civilization.Darkness:
                    return new Color(0.30f, 0.18f, 0.38f);
                case Civilization.Water:
                    return new Color(0.12f, 0.38f, 0.62f);
                case Civilization.Nature:
                    return new Color(0.18f, 0.52f, 0.25f);
                default:
                    return new Color(0.72f, 0.62f, 0.22f);
            }
        }

        private void OnDestroy()
        {
            if (_videoTexture != null) _videoTexture.Release();
            foreach (var turned in _turnedCardArt.Values)
                if (turned != null) turned.Release();
            _turnedCardArt.Clear();
            if (_turnCardMaterial != null) Destroy(_turnCardMaterial);
        }
    }
}
