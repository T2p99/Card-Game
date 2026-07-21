using System;

namespace CardGame.Core
{
    public enum AbilityTiming
    {
        Static,
        OnSummon,
        OnCast,
        OnAttack,
        OnAttackPlayerUnblocked,
        OnBlock,
        OnBlocked,
        OnBattle,
        OnBattleWon,
        OnBattleLost,
        OnDestroyed,
        OnLeaveBattleZone,
        OnOtherCreatureSummoned,
        OnAnyCreatureSummoned,
        OnSpellCast,
        OnShieldBroken,
        StartOfOwnerTurn,
        StartOfOpponentTurn,
        EndOfOwnerTurn,
        EndOfAnyTurn,
        TapAbility,
        SilentSkill,
        ReplacementDestroy,
        ReplacementDiscard,
        ReplacementLeaveBattleZone,
        ReplacementShieldBreak
    }

    public enum AbilityOperation
    {
        None,
        Draw,
        Discard,
        Destroy,
        DestroyOrSource,
        Tap,
        Untap,
        Move,
        Search,
        Shuffle,
        ModifyPower,
        ModifyPowerWhileAttacking,
        GrantKeyword,
        RemoveKeyword,
        BreakShield,
        AddShield,
        LookAtCards,
        RevealCards,
        ForceAttack,
        PreventBattle,
        ExtraTurn,
        LoseGame,
        SetRace,
        SetCivilization,
        SetCost,
        SetBreakerCount,
        CopyAbility,
        GrantBlockedCreatureSlayerThisTurn
    }

    public enum RuleZone
    {
        None,
        BattleZone,
        Hand,
        Deck,
        ManaZone,
        ShieldZone,
        Graveyard
    }

    public enum RuleController
    {
        Source,
        Opponent,
        Either,
        EachPlayer,
        CardOwner
    }

    public enum RuleSelectionMode
    {
        SourceCard,
        Choose,
        Random,
        All,
        Top,
        Bottom
    }

    public enum AbilityDuration
    {
        Immediate,
        WhileSourceInBattleZone,
        UntilEndOfTurn,
        UntilStartOfOwnersNextTurn,
        Permanent
    }

    [Serializable]
    public sealed class CardFilterDefinition
    {
        public bool CreaturesOnly;
        public bool SpellsOnly;
        public bool EvolutionOnly;
        public bool NonEvolutionOnly;
        public bool MulticoloredOnly;
        public bool TappedOnly;
        public bool UntappedOnly;
        public bool OtherThanSource;
        public bool HasBlocker;
        public bool HasSilentSkill;
        public int PowerExactly;
        public int PowerAtMost;
        public int PowerAtLeast;
        public int CostAtMost;
        public int CostAtLeast;
        public string Name = string.Empty;
        public string Race = string.Empty;
        public string RaceContains = string.Empty;
        public Civilization[] Civilizations = Array.Empty<Civilization>();
        public Civilization[] ExcludedCivilizations = Array.Empty<Civilization>();
    }

    [Serializable]
    public sealed class RuleSelectorDefinition
    {
        public RuleController Controller = RuleController.Source;
        public RuleZone Zone = RuleZone.BattleZone;
        public RuleSelectionMode Mode = RuleSelectionMode.Choose;
        public int Minimum = 1;
        public int Maximum = 1;
        public CardFilterDefinition Filter = new CardFilterDefinition();
    }

    [Serializable]
    public sealed class AbilityConditionDefinition
    {
        public RuleController Controller = RuleController.Source;
        public RuleZone Zone = RuleZone.None;
        public CardFilterDefinition Filter = new CardFilterDefinition();
        public int CountAtLeast;
        public int CountAtMost;
        public bool DuringOwnersTurn;
        public bool DuringOpponentsTurn;
        public bool SourceTapped;
        public bool SourceUntapped;
        public bool ShieldsWereBrokenThisTurn;
        public bool SpellWasCastThisTurn;
        public bool Invert;
    }

    [Serializable]
    public sealed class RuleAbilityDefinition
    {
        public string Id = string.Empty;
        public string RulesText = string.Empty;
        public AbilityTiming Timing;
        public AbilityOperation Operation;
        public RuleSelectorDefinition Selector = new RuleSelectorDefinition();
        public RuleController Chooser = RuleController.Source;
        public RuleZone Destination;
        public AbilityDuration Duration = AbilityDuration.Immediate;
        public AbilityConditionDefinition Condition;
        public CardKeyword Keyword;
        public int Amount = 1;
        public int Value;
        public int ValuePerMatchingCard;
        public RuleSelectorDefinition ValueSelector;
        public bool Optional;
        public bool UseShieldTrigger;
        public bool DestinationTapped;
        public bool ShuffleAfter;

        public RuleAbilityDefinition(string id, AbilityTiming timing, AbilityOperation operation)
        {
            Id = id;
            Timing = timing;
            Operation = operation;
        }
    }
}
