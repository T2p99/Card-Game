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
    public sealed class CardIncarnationReference
    {
        public CardInstance Card;
        public int ZoneChangeVersion;

        public CardIncarnationReference()
        {
        }

        public CardIncarnationReference(CardInstance card)
        {
            Card = card;
            ZoneChangeVersion = card?.ZoneChangeVersion ?? -1;
        }

        public bool Matches(CardInstance card) => card != null && Card == card &&
            card.ZoneChangeVersion == ZoneChangeVersion;
    }

    [Serializable]
    public sealed class CardInstance
    {
        public long InstanceId;
        public CardDefinition Definition = new CardDefinition();
        // A physical card can be represented by the same CardInstance for the
        // whole match, but every zone change creates a new game object. Combat
        // references record this value so a card that leaves and re-enters the
        // battle zone cannot be mistaken for its previous incarnation.
        public int ZoneChangeVersion;
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

        /// <summary>
        /// Clears state that belongs to one appearance of this card in a zone.
        /// CardInstance objects are reused by the simulation, but a card that
        /// changes zones must not carry temporary effects or combat history into
        /// its new game object.
        /// </summary>
        internal void ResetTransientZoneState(bool clearShieldBreakContext = true,
            bool preserveBattleZoneEntryTurn = false)
        {
            ZoneChangeVersion = unchecked(ZoneChangeVersion + 1);
            if (!preserveBattleZoneEntryTurn) EnteredBattleZoneOnTurn = -1;
            TemporaryPowerAttacker = 0;
            TemporaryPower = 0;
            TemporaryDoubleBreaker = false;
            TemporaryBreakerCount = 0;
            TemporaryKeywords.Clear();
            TimedKeywords.Clear();
            TimedRuleAbilities.Clear();
            AttackedThisTurn = false;
            BrokeShieldThisTurn = false;
            ShieldsBrokenInLastAttack = 0;
            if (clearShieldBreakContext) BrokenByAttackerInstanceId = 0;
            TemporaryRaces.Clear();
            ChosenRace = string.Empty;
            FaceUp = false;
            WasAttackedThisTurn = false;
            ChosenCivilization = default;
            HasChosenCivilization = false;
        }
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
        public int AttackerZoneChangeVersion;
        public AttackTargetKind TargetKind;
        public long TargetCreatureInstanceId;
        public int TargetCreatureZoneChangeVersion = -1;
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
        public List<CardIncarnationReference> UntapAfterCurrentBattle =
            new List<CardIncarnationReference>();
        public List<CardIncarnationReference> DestroyAfterCurrentBattle =
            new List<CardIncarnationReference>();
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
