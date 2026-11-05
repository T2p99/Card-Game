using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Presentation
{
    public sealed partial class DuelPrototypeController
    {
        private const int RetainedDuelLogs = 3;
        private string _duelLogDirectory;
        private string _duelLogPath;
        private string _duelLogError = string.Empty;
        private DuelLogDocument _duelLog;
        private bool _duelLogWritten;

        [Serializable]
        private sealed class DuelLogDocument
        {
            public int FormatVersion = 1;
            public string DuelId;
            public string StartedUtc;
            public string ApplicationVersion;
            public string Platform;
            public string HumanDeck;
            public string ComputerDeck;
            public bool Closed;
            public List<DuelLogEvent> Events = new List<DuelLogEvent>();
        }

        [Serializable]
        private sealed class DuelLogEvent
        {
            public int Sequence;
            public string Utc;
            public string Kind;
            public bool Success;
            public string Error;
            public string Detail;
            public DuelLogState State;
            public RuleTraceEntry[] Trace = Array.Empty<RuleTraceEntry>();
            public DuelLogMovement[] Movements = Array.Empty<DuelLogMovement>();
        }

        [Serializable]
        private sealed class DuelLogState
        {
            public int Turn;
            public int ActivePlayerId;
            public int StartingPlayerId;
            public string Phase;
            public int RandomSeed;
            public int RandomActionIndex;
            public bool Finished;
            public int WinnerPlayerId;
            public string WinReason;
            public PendingAttack PendingAttack;
            public bool ManaChargedThisTurn;
            public bool MainActionTakenThisTurn;
            public bool[] SpellCastThisTurn;
            public bool[] CreatureSummonedThisTurn;
            public int[] ShieldsBrokenThisTurn;
            public int[] CardsDrawnThisTurn;
            public bool[] BlockedCreatureGetsSlayerThisTurn;
            public bool[] IgnoreAttackRestrictionsThisTurn;
            public bool[] DestroyedCreaturesGoToManaThisTurn;
            public bool PreventPendingBattle;
            public long RequiredBlockerInstanceId;
            public long[] PendingShieldTriggers;
            public string[] DelayedAbilities;
            public DuelLogPlayer[] Players;
        }

        [Serializable]
        private sealed class DuelLogPlayer
        {
            public int PlayerId;
            public DuelLogCard[] Deck;
            public DuelLogCard[] Hand;
            public DuelLogCard[] Shields;
            public DuelLogCard[] Mana;
            public DuelLogCard[] BattleZone;
            public DuelLogCard[] Graveyard;
            public DuelLogCard[] Resolving;
        }

        [Serializable]
        private sealed class DuelLogCard
        {
            public long InstanceId;
            public int ZoneChangeVersion;
            public string CardId;
            public string Name;
            public string Type;
            public bool Tapped;
            public bool FaceUp;
            public int EnteredBattleZoneOnTurn;
            public int BasePower;
            public int Power;
            public int AttackingPower;
            public int TemporaryPower;
            public int TemporaryPowerAttacker;
            public bool AttackedThisTurn;
            public bool WasAttackedThisTurn;
            public bool BrokeShieldThisTurn;
            public string ChosenRace;
            public string ChosenCivilization;
            public string[] Keywords;
            public string[] TemporaryRaces;
            public string[] TimedAbilities;
            public string[] PrintedRuleIds;
            public DuelLogCard[] EvolutionMaterials;
        }

        [Serializable]
        private sealed class DuelLogMovement
        {
            public long InstanceId;
            public string CardId;
            public string Name;
            public string From;
            public string To;
            public int FromVersion;
            public int ToVersion;
        }

        private void BeginDuelLog()
        {
            if (string.IsNullOrEmpty(_duelLogDirectory))
                _duelLogDirectory = Path.Combine(Application.persistentDataPath, "battle-logs");
            var now = DateTime.UtcNow;
            var id = now.ToString("yyyyMMddTHHmmssfffffff'Z'", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N");
            _duelLogPath = Path.Combine(_duelLogDirectory, "duel-" + id + ".json");
            _duelLogError = string.Empty;
            _duelLogWritten = false;
            _duelLog = new DuelLogDocument
            {
                DuelId = id,
                StartedUtc = now.ToString("O", CultureInfo.InvariantCulture),
                ApplicationVersion = Application.version,
                Platform = Application.platform.ToString(),
                HumanDeck = _humanDeckLabel,
                ComputerDeck = _computerDeckLabel
            };
            AppendDuelLogEvent(new DuelLogEvent
            {
                Kind = "DuelStarted",
                Success = true,
                State = CaptureDuelLogState(_state)
            });
        }

        private void RecordDuelAction(GameState before, AbilityActionSession session)
        {
            var state = CaptureDuelLogState(session.State);
            AppendDuelLogEvent(new DuelLogEvent
            {
                Kind = "Command",
                Success = session.Result.Success,
                Error = session.Result.Error,
                State = state,
                Trace = session.Trace.ToArray(),
                Movements = DuelLogMovements(CaptureDuelLogState(before), state)
            });
            if (session.State.IsFinished) EndDuelLog("Game finished");
        }

        private void EndDuelLog(string reason)
        {
            if (_duelLog == null || _duelLog.Closed) return;
            _duelLog.Closed = true;
            AppendDuelLogEvent(new DuelLogEvent
            {
                Kind = "DuelEnded",
                Success = true,
                Detail = reason,
                State = CaptureDuelLogState(_state)
            });
        }

        private void OnApplicationQuit() => EndDuelLog("Application quit");

        private void AppendDuelLogEvent(DuelLogEvent entry)
        {
            entry.Sequence = _duelLog.Events.Count + 1;
            entry.Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _duelLog.Events.Add(entry);
            try
            {
                WritePersistentJson(_duelLogPath, JsonUtility.ToJson(_duelLog));
                PruneDuelLogs();
                if (!_duelLogWritten) Debug.Log("Duel log: " + _duelLogPath);
                _duelLogWritten = true;
                if (_duelLogError.Length > 0) _message = "Duel logging resumed.";
                _duelLogError = string.Empty;
            }
            catch (Exception exception) when (IsDeckStorageError(exception))
            {
                var message = "Duel logging error: " + exception.Message;
                if (_duelLogError != message) Debug.LogError(message);
                _duelLogError = message;
                _message = message;
                _portraitDirty = true;
            }
        }

        private void PruneDuelLogs()
        {
            var current = Path.GetFullPath(_duelLogPath);
            var logs = Directory.GetFiles(_duelLogDirectory, "duel-*.json")
                .Where(path => Regex.IsMatch(Path.GetFileName(path),
                    @"^duel-\d{8}T\d{13}Z-[a-f0-9]{32}\.json$"))
                .Where(path => !string.Equals(Path.GetFullPath(path), current,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(RetainedDuelLogs - 1);
            foreach (var path in logs)
            {
                File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }

        private DuelLogState CaptureDuelLogState(GameState state)
        {
            if (state == null) return null;
            return new DuelLogState
            {
                Turn = state.TurnNumber,
                ActivePlayerId = state.ActivePlayerId,
                StartingPlayerId = state.StartingPlayerId,
                Phase = state.Phase.ToString(),
                RandomSeed = state.RandomSeed,
                RandomActionIndex = state.RandomActionIndex,
                Finished = state.IsFinished,
                WinnerPlayerId = state.WinnerPlayerId ?? -1,
                WinReason = state.WinReason,
                PendingAttack = state.PendingAttack == null ? null : new PendingAttack
                {
                    AttackerInstanceId = state.PendingAttack.AttackerInstanceId,
                    AttackerZoneChangeVersion = state.PendingAttack.AttackerZoneChangeVersion,
                    TargetKind = state.PendingAttack.TargetKind,
                    TargetCreatureInstanceId = state.PendingAttack.TargetCreatureInstanceId,
                    TargetCreatureZoneChangeVersion = state.PendingAttack.TargetCreatureZoneChangeVersion,
                    DefendingPlayerId = state.PendingAttack.DefendingPlayerId,
                    PreferredShieldInstanceId = state.PendingAttack.PreferredShieldInstanceId,
                    PreferredShieldZoneChangeVersion = state.PendingAttack.PreferredShieldZoneChangeVersion
                },
                ManaChargedThisTurn = state.ManaChargedThisTurn,
                MainActionTakenThisTurn = state.MainActionTakenThisTurn,
                SpellCastThisTurn = state.SpellCastThisTurn.ToArray(),
                CreatureSummonedThisTurn = state.CreatureSummonedThisTurn.ToArray(),
                ShieldsBrokenThisTurn = state.ShieldsBrokenThisTurn.ToArray(),
                CardsDrawnThisTurn = state.CardsDrawnThisTurn.ToArray(),
                BlockedCreatureGetsSlayerThisTurn = state.BlockedCreatureGetsSlayerThisTurn.ToArray(),
                IgnoreAttackRestrictionsThisTurn = state.IgnoreAttackRestrictionsThisTurn.ToArray(),
                DestroyedCreaturesGoToManaThisTurn = state.DestroyedCreaturesGoToManaThisTurn.ToArray(),
                PreventPendingBattle = state.PreventPendingBattle,
                RequiredBlockerInstanceId = state.RequiredBlockerInstanceId,
                PendingShieldTriggers = state.PendingShieldTriggers.Select(card => card.InstanceId).ToArray(),
                DelayedAbilities = state.DelayedAbilities.Select(effect => effect.OwnerPlayerId + ":" +
                    effect.Source.InstanceId + ":" + effect.Ability.Id + ":" + effect.Duration).ToArray(),
                Players = state.Players.Select(player => new DuelLogPlayer
                {
                    PlayerId = player.PlayerId,
                    Deck = player.Deck.Select(card => CaptureDuelLogCard(state, card, false)).ToArray(),
                    Hand = player.Hand.Select(card => CaptureDuelLogCard(state, card, false)).ToArray(),
                    Shields = player.Shields.Select(card => CaptureDuelLogCard(state, card, false)).ToArray(),
                    Mana = player.Mana.Select(card => CaptureDuelLogCard(state, card, false)).ToArray(),
                    BattleZone = player.BattleZone.Select(card => CaptureDuelLogCard(state, card, true)).ToArray(),
                    Graveyard = player.Graveyard.Select(card => CaptureDuelLogCard(state, card, false)).ToArray(),
                    Resolving = player.Resolving.Select(card => CaptureDuelLogCard(state, card, false)).ToArray()
                }).ToArray()
            };
        }

        private DuelLogCard CaptureDuelLogCard(GameState state, CardInstance card, bool inBattle)
        {
            return new DuelLogCard
            {
                InstanceId = card.InstanceId,
                ZoneChangeVersion = card.ZoneChangeVersion,
                CardId = card.Definition.Id,
                Name = card.Definition.Name,
                Type = card.Definition.Type.ToString(),
                Tapped = card.Tapped,
                FaceUp = card.FaceUp,
                EnteredBattleZoneOnTurn = card.EnteredBattleZoneOnTurn,
                BasePower = card.Definition.Power,
                Power = inBattle ? _engine.CurrentPower(state, card) : card.Definition.Power,
                AttackingPower = inBattle ? _engine.CurrentPower(state, card, true) : card.Definition.Power,
                TemporaryPower = card.TemporaryPower,
                TemporaryPowerAttacker = card.TemporaryPowerAttacker,
                AttackedThisTurn = card.AttackedThisTurn,
                WasAttackedThisTurn = card.WasAttackedThisTurn,
                BrokeShieldThisTurn = card.BrokeShieldThisTurn,
                ChosenRace = card.ChosenRace,
                ChosenCivilization = card.HasChosenCivilization ? card.ChosenCivilization.ToString() : null,
                Keywords = (inBattle ? _engine.GetEffectiveKeywords(state, card) :
                    card.Definition.Abilities.Select(ability => ability.Keyword))
                    .Select(keyword => keyword.ToString()).ToArray(),
                TemporaryRaces = card.TemporaryRaces.ToArray(),
                TimedAbilities = card.TimedKeywords.Select(effect => effect.Keyword + ":" +
                    effect.Value + ":" + effect.Duration + ":" + effect.AppliedByPlayerId)
                    .Concat(card.TimedRuleAbilities.Select(effect => effect.Ability.Id + ":" +
                        effect.Duration + ":" + effect.AppliedByPlayerId)).ToArray(),
                PrintedRuleIds = card.Definition.RuleLineIds.ToArray(),
                EvolutionMaterials = card.EvolutionMaterials
                    .Select(material => CaptureDuelLogCard(state, material, false)).ToArray()
            };
        }

        private static IEnumerable<(DuelLogCard Card, string Location)> LocatedDuelCards(DuelLogState state)
        {
            foreach (var player in state.Players)
            {
                var zones = new[] { player.Deck, player.Hand, player.Shields, player.Mana,
                    player.BattleZone, player.Graveyard, player.Resolving };
                var names = new[] { "Deck", "Hand", "Shields", "Mana", "BattleZone", "Graveyard", "Resolving" };
                for (var i = 0; i < zones.Length; i++)
                foreach (var card in zones[i])
                {
                    yield return (card, player.PlayerId + "/" + names[i]);
                    foreach (var material in LocatedEvolutionMaterials(card, player.PlayerId))
                        yield return material;
                }
            }
        }

        private static IEnumerable<(DuelLogCard Card, string Location)> LocatedEvolutionMaterials(
            DuelLogCard card, int playerId)
        {
            foreach (var material in card.EvolutionMaterials)
            {
                yield return (material, playerId + "/EvolutionMaterials/" + card.InstanceId);
                foreach (var nested in LocatedEvolutionMaterials(material, playerId))
                    yield return nested;
            }
        }

        private static DuelLogMovement[] DuelLogMovements(DuelLogState before, DuelLogState after)
        {
            var previous = LocatedDuelCards(before).ToDictionary(item => item.Card.InstanceId);
            var current = LocatedDuelCards(after).ToDictionary(item => item.Card.InstanceId);
            var moves = new List<DuelLogMovement>();
            foreach (var id in previous.Keys.Union(current.Keys).OrderBy(id => id))
            {
                var hadCard = previous.TryGetValue(id, out var old);
                var hasCard = current.TryGetValue(id, out var next);
                if (hadCard && hasCard && old.Location == next.Location &&
                    old.Card.ZoneChangeVersion == next.Card.ZoneChangeVersion) continue;
                var card = hasCard ? next.Card : old.Card;
                moves.Add(new DuelLogMovement
                {
                    InstanceId = id,
                    CardId = card.CardId,
                    Name = card.Name,
                    From = hadCard ? old.Location : null,
                    To = hasCard ? next.Location : null,
                    FromVersion = hadCard ? old.Card.ZoneChangeVersion : -1,
                    ToVersion = hasCard ? next.Card.ZoneChangeVersion : -1
                });
            }
            return moves.ToArray();
        }
    }
}
