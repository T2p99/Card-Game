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
    public sealed class TimedKeywordEffect
    {
        public CardKeyword Keyword;
        public int Value;
        public AbilityDuration Duration;
        public int AppliedByPlayerId;
    }

    [Serializable]
    public sealed class TimedRuleEffect
    {
        public RuleAbilityDefinition Ability;
        public AbilityDuration Duration;
        public int AppliedByPlayerId;
    }

    [Serializable]
    public sealed class DelayedAbilityEffect
    {
        public CardInstance Source;
        public RuleAbilityDefinition Ability;
        public int OwnerPlayerId;
        public AbilityDuration Duration;
        public List<CardInstance> FixedTargets = new List<CardInstance>();
    }

    [Serializable]
    public sealed class PlayerRaceEffect
    {
        public int PlayerId;
        public string Race = string.Empty;
    }

    [Serializable]
    public sealed class PlayerCivilizationEffect
    {
        public int PlayerId;
        public Civilization Civilization;
    }

    [Serializable]
    public sealed class CardInstance
    {
        public long InstanceId;
        public CardDefinition Definition = new CardDefinition();
        public bool Tapped;
        public int EnteredBattleZoneOnTurn = -1;
        public int TemporaryPowerAttacker;
        public int TemporaryPower;
        public bool TemporaryDoubleBreaker;
        public int TemporaryBreakerCount;
        public List<CardKeyword> TemporaryKeywords = new List<CardKeyword>();
        public List<TimedKeywordEffect> TimedKeywords = new List<TimedKeywordEffect>();
        public List<TimedRuleEffect> TimedRuleAbilities = new List<TimedRuleEffect>();
        public bool AttackedThisTurn;
        public bool BrokeShieldThisTurn;
        public int ShieldsBrokenInLastAttack;
        public long BrokenByAttackerInstanceId;
        // Direct evolution sources in physical top-to-bottom order. A source
        // can itself retain a nested evolution pile.
        public List<CardInstance> EvolutionMaterials = new List<CardInstance>();
        public List<string> TemporaryRaces = new List<string>();
        public string ChosenRace = string.Empty;
        public bool FaceUp;
        public bool WasAttackedThisTurn;
        public Civilization ChosenCivilization;
        public bool HasChosenCivilization;

        public bool HasKeyword(CardKeyword keyword) =>
            Definition.HasKeyword(keyword) || TemporaryKeywords.Contains(keyword);
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
        public List<CardInstance> Resolving = new List<CardInstance>();
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
        public bool MainActionTakenThisTurn;
        public int? WinnerPlayerId;
        public string WinReason = string.Empty;
        public PendingAttack PendingAttack;
        public List<CardInstance> PendingShieldTriggers = new List<CardInstance>();
        public int RandomSeed;
        public int RandomActionIndex;
        public bool[] SpellCastThisTurn = new bool[2];
        public bool[] CreatureSummonedThisTurn = new bool[2];
        public int[] ShieldsBrokenThisTurn = new int[2];
        public int[] CardsDrawnThisTurn = new int[2];
        public bool[] BlockedCreatureGetsSlayerThisTurn = new bool[2];
        public bool[] IgnoreAttackRestrictionsThisTurn = new bool[2];
        public int InspectionPlayerId = -1;
        public List<CardInstance> InspectedCards = new List<CardInstance>();
        public List<int> ExtraTurnQueue = new List<int>();
        public int ResumePlayerAfterExtraTurns = -1;
        public CardInstance CurrentEventCard;
        public bool[] ForceAllCreaturesAttackNextTurn = new bool[2];
        public List<DelayedAbilityEffect> DelayedAbilities = new List<DelayedAbilityEffect>();
        public bool PreventPendingBattle;
        public bool[] DestroyedCreaturesGoToManaThisTurn = new bool[2];
        public Civilization?[] BlockedAttackerBreaksShieldCivilizationThisTurn = new Civilization?[2];
        public List<CardInstance> LastRuleSelection = new List<CardInstance>();
        public long CurrentBattleAttackerInstanceId;
        public long CurrentBattleDefenderInstanceId;
        public List<PlayerRaceEffect> DestroyedRacesReturnToHandThisTurn =
            new List<PlayerRaceEffect>();
        public long CurrentBlockerInstanceId;
        public long RequiredBlockerInstanceId;
        public int[] ForcedLossOwnTurnEndsRemaining = new int[2];
        public List<CardInstance> UntapAfterCurrentBattle = new List<CardInstance>();
        public List<CardInstance> DestroyAfterCurrentBattle = new List<CardInstance>();
        public List<PlayerCivilizationEffect> PreventOpponentAttackCivilizationsUntilNextTurn =
            new List<PlayerCivilizationEffect>();

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
