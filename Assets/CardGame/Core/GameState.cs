using System;
using System.Collections.Generic;

namespace CardGame.Core
{
    public enum GamePhase
    {
        Main,
        Attack,
        Finished
    }

    public enum AttackTargetKind
    {
        Player,
        Creature
    }

    [Serializable]
    public sealed class CardInstance
    {
        public long InstanceId;
        public CardDefinition Definition = new CardDefinition();
        public bool Tapped;
        public int EnteredBattleZoneOnTurn = -1;
        public int TemporaryPowerAttacker;
        public bool TemporaryDoubleBreaker;
    }

    [Serializable]
    public sealed class PlayerState
    {
        public int PlayerId;
        public List<CardInstance> Deck = new List<CardInstance>();
        public List<CardInstance> Hand = new List<CardInstance>();
        public List<CardInstance> Shields = new List<CardInstance>();
        public List<CardInstance> Mana = new List<CardInstance>();
        public List<CardInstance> BattleZone = new List<CardInstance>();
        public List<CardInstance> Graveyard = new List<CardInstance>();
    }

    [Serializable]
    public sealed class PendingAttack
    {
        public long AttackerInstanceId;
        public AttackTargetKind TargetKind;
        public long TargetCreatureInstanceId;
        public int DefendingPlayerId;
    }

    [Serializable]
    public sealed class GameState
    {
        public PlayerState[] Players = Array.Empty<PlayerState>();
        public int ActivePlayerId;
        public int StartingPlayerId;
        public int TurnNumber;
        public GamePhase Phase;
        public bool ManaChargedThisTurn;
        public int? WinnerPlayerId;
        public string WinReason = string.Empty;
        public PendingAttack PendingAttack;
        public List<CardInstance> PendingShieldTriggers = new List<CardInstance>();
        public int RandomSeed;
        public int RandomActionIndex;

        public PlayerState ActivePlayer => Players[ActivePlayerId];
        public PlayerState Opponent => Players[1 - ActivePlayerId];
        public bool IsFinished => WinnerPlayerId.HasValue;
    }

    public sealed class RuleResult
    {
        public bool Success { get; }
        public string Error { get; }

        private RuleResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public static RuleResult Ok() => new RuleResult(true, string.Empty);
        public static RuleResult Fail(string error) => new RuleResult(false, error);
    }
}
