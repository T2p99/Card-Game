using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Presentation
{
    public sealed partial class DuelPrototypeController
    {
        private const float TutorialCoachHeight = 188f;
        private TutorialBattle _tutorial;

        private void StartTutorial()
        {
            ReturnToDeckSetup();
            try
            {
                _actionSession = null;
                _inspectionReview = null;
                _resolvingComputerTriggers = false;
                _actionDepth = 0;
                _selectedManaIds.Clear();
                _engine = new RulesEngine();
                _tutorial = new TutorialBattle(_engine);
                _state = _tutorial.InitialState;
                _matchSeed = TutorialBattle.Seed;
                _humanDeckLabel = "Tutorial Fire deck";
                _computerDeckLabel = "Scripted tutorial Water deck";
                _humanCivilizationLabel = "Fire";
                _computerCivilizationLabel = "Water";
                _message = "Welcome! Follow the tutorial coach above the table.";
                _showDeckSetup = false;
                _selectingManaPayment = false;
                _portraitHandOffset = .5f;
                _portraitRowOffset = _portraitOpponentRowOffset = 0;
                _portraitManaRowOffset = _portraitOpponentManaRowOffset = 0;
                BeginDuelLog();
                _artRequests.Clear();
                StartCoroutine(PreloadDeckArt(_state.Players.SelectMany(p =>
                    p.Hand.Concat(p.Shields).Concat(p.Deck)).Select(c => c.Definition).ToArray()));
                _portraitDirty = true;
            }
            catch (Exception exception) when (exception is ArgumentException ||
                exception is InvalidOperationException || exception is KeyNotFoundException)
            {
                _tutorial = null;
                _state = null;
                _showDeckSetup = true;
                _setupMessage = "Could not start tutorial: " + exception.Message;
                Debug.LogError(_setupMessage);
            }
        }

        private bool TutorialAllows(TutorialAction action, long cardId = 0, long targetId = 0) =>
            _tutorial == null || _tutorial.Allows(action, cardId, targetId);

        private bool TutorialGuard(TutorialAction action, long cardId = 0, long targetId = 0)
        {
            if (TutorialAllows(action, cardId, targetId)) return true;
            ShowResult(RuleResult.Fail("Tutorial: " + _tutorial.Guidance(_state)));
            _portraitDirty = true;
            return false;
        }

        private bool TutorialAllowsAttack(CardInstance card, AttackTargetKind kind, long targetId = 0) =>
            TutorialAllows(kind == AttackTargetKind.Player ? TutorialAction.AttackPlayer :
                TutorialAction.AttackCreature, card.InstanceId, targetId);

        private void ContinueTutorial()
        {
            if (_tutorial == null || _busy || _actionDepth > 0) return;
            var result = _tutorial.Continue();
            if (!result.Success) { ShowResult(result); return; }
            PClosePreview();
            CloseHandCardViewer();
            _message = _tutorial.Guidance(_state);
            _portraitDirty = true;
        }

        private void TutorialInspectDeck(int playerId)
        {
            _tutorial?.InspectDeck(playerId);
            _message = (playerId == HumanId ? "Your" : "Opponent") +
                " deck: " + _state.Players[playerId].Deck.Count + " cards.";
            _portraitDirty = true;
        }

        private void TutorialOpenFocus()
        {
            if (_tutorial == null) return;
            var id = _tutorial.FocusCardId;
            var human = _state.Players[HumanId];
            var zone = human.Hand.Any(c => c.InstanceId == id) ? human.Hand :
                human.BattleZone.Any(c => c.InstanceId == id) ? human.BattleZone : null;
            if (zone == null)
            {
                ShowResult(RuleResult.Fail("Close this viewer and follow the current tutorial step."));
                return;
            }
            var index = zone.FindIndex(c => c.InstanceId == id);
            if (_portraitCanvas != null)
                POpenPreview(zone, index, ReferenceEquals(zone, human.Hand) ? "Your hand" : "Your battle zone");
            else
            {
                if (ReferenceEquals(zone, human.Hand)) OpenHandCardViewer(zone[index]);
                else OpenFaceUpCardViewer(zone[index], zone, "Your battle zone");
            }
        }

        private IEnumerator RunTutorialOpponentTurn()
        {
            _busy = true;
            while (_tutorial != null && !_state.IsFinished && _state.ActivePlayerId == ComputerId)
            {
                yield return new WaitForSeconds(.35f);
                RuleResult outcome = null;
                yield return RunAction((engine, state) => _tutorial.PlayOpponentAction(engine, state),
                    result => outcome = result);
                if (!outcome.Success)
                {
                    _message = "Tutorial opponent stopped: " + outcome.Error + " Use the menu to restart.";
                    Debug.LogError(_message);
                    break;
                }
            }
            _busy = false;
        }

        private void PTutorialHighlight(RectTransform rect, bool highlight)
        {
            if (_tutorial == null || !highlight) return;
            var width = rect.rect.width;
            var height = rect.rect.height;
            PBox(rect, "Tutorial highlight", new Rect(0, 0, width, 3), PGold).raycastTarget = false;
            PBox(rect, "Tutorial highlight", new Rect(0, height - 3, width, 3), PGold).raycastTarget = false;
            PBox(rect, "Tutorial highlight", new Rect(0, 0, 3, height), PGold).raycastTarget = false;
            PBox(rect, "Tutorial highlight", new Rect(width - 3, 0, 3, height), PGold).raycastTarget = false;
        }

        private void PDrawTutorialCoach()
        {
            if (_tutorial == null || _state.IsFinished) return;
            var width = _pw - 24;
            var panel = PBox(_portraitOverlay, "Tutorial coach",
                new Rect(12, 64, width, TutorialCoachHeight - 8), PPanel).rectTransform;
            PLabel(panel, new Rect(10, 4, width - 94, 30),
                _tutorial.LessonNumber + "/" + _tutorial.LessonCount + "  " + _tutorial.Title, 22, PGold);
            PButton(panel, new Rect(width - 78, 4, 68, 34), "EXIT", ReturnToDeckSetup)
                .GetComponentInChildren<Text>().fontSize = 17;
            var text = _tutorial.Guidance(_state);
            var textHeight = Mathf.Max(92, PTextHeight(text, 22, width - 40) + 8);
            var scroll = PScroll(panel, new Rect(10, 38, width - 20, 92), false, textHeight);
            PLabel(scroll.content, new Rect(8, 0, width - 36, textHeight), text, 22, Color.white);
            var canInteract = !_busy && _actionDepth == 0;
            var canView = _tutorial.FocusCardId != 0;
            if (_tutorial.IsExplanation)
            {
                PButton(panel, new Rect(10, 132, canView ? (width - 30) * .5f : width - 20, 44),
                    "NEXT", ContinueTutorial, canInteract && _tutorial.CanContinue, PGold)
                    .GetComponentInChildren<Text>().fontSize = 20;
            }
            if (canView)
            {
                var x = _tutorial.IsExplanation ? (width + 10) * .5f : 10;
                PButton(panel, new Rect(x, 132,
                    _tutorial.IsExplanation ? (width - 30) * .5f : width - 20, 44),
                    "VIEW CARD", TutorialOpenFocus, canInteract)
                    .GetComponentInChildren<Text>().fontSize = 20;
            }
            else if (!_tutorial.IsExplanation)
                PLabel(panel, new Rect(10, 136, width - 20, 40),
                    _tutorial.Step == TutorialStep.OpponentTurn ? "Watch the opponent" :
                    _tutorial.Step == TutorialStep.Practice ? "All normal controls are unlocked" :
                    "Use the highlighted control on the table", 18, PCyan);
        }

        private void PTutorialWinner()
        {
            var won = _state.WinnerPlayerId == HumanId;
            var panel = PModal(won ? "TUTORIAL COMPLETE" : "TRY AGAIN", .27f, .94f, false);
            PLabel(panel, new Rect(20, 78, _pw - 68, panel.rect.height - 330),
                won ? "You won! " + _state.WinReason + "\n\n" +
                    "You learned mana, turns, summoning sickness, shields, creature battles, spells and Speed Attacker." :
                    _state.WinReason + "\n\nReplay the fixed lessons for another try.", 25, Color.white);
            var y = panel.rect.height - 230;
            PButton(panel, new Rect(20, y, _pw - 68, 62), "START NEW GAME",
                () => { PClosePreview(); StartSelectedMatch(); }, _savedDecks != null, PGold);
            PButton(panel, new Rect(20, y + 74, _pw - 68, 62), "REPLAY TUTORIAL", StartTutorial);
            PButton(panel, new Rect(20, y + 148, _pw - 68, 62), "MAIN MENU", ReturnToDeckSetup);
        }

        private void DrawTutorialCoach()
        {
            if (_tutorial == null || _state.IsFinished) return;
            var area = new Rect(18, 54, 1248, 146);
            GUI.backgroundColor = new Color(.07f, .13f, .17f);
            GUI.Box(area, GUIContent.none);
            var body = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            GUI.Label(new Rect(area.x + 12, area.y + 8, area.width - 130, 30),
                "TUTORIAL " + _tutorial.LessonNumber + "/" + _tutorial.LessonCount + " - " + _tutorial.Title, body);
            GUI.Label(new Rect(area.x + 12, area.y + 40, area.width - 24, 58), _tutorial.Guidance(_state), body);
            if (GUI.Button(new Rect(area.xMax - 100, area.y + 8, 88, 30), "EXIT")) ReturnToDeckSetup();
            GUI.enabled = !_busy && _actionDepth == 0;
            if (_tutorial != null && _tutorial.FocusCardId != 0 &&
                GUI.Button(new Rect(area.x + 12, area.y + 104, 190, 32), "VIEW CARD")) TutorialOpenFocus();
            if (_tutorial != null && _tutorial.Step == TutorialStep.Deck &&
                GUI.Button(new Rect(area.x + 12, area.y + 104, 190, 32), "SHOW DECK COUNT")) TutorialInspectDeck(HumanId);
            GUI.enabled = _tutorial != null && !_busy && _actionDepth == 0 && _tutorial.CanContinue;
            if (GUI.Button(new Rect(area.xMax - 202, area.y + 104, 190, 32), "NEXT")) ContinueTutorial();
            GUI.enabled = true;
        }
    }
}
