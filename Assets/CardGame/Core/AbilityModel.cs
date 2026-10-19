using System;

namespace CardGame.Core
{
    public enum AbilityTiming
    {
        PlayRestriction,
        Static,
        OnSummon,
        OnCast,
        OnAttack,
        OnAttacked,
        OnAttackPlayerUnblocked,
        OnBlock,
        OnBlocked,
        OnBattle,
        OnBattleWon,
        OnBattleLost,
        AfterBattle,
        AfterAttack,
        AfterAnyCreatureAttack,
        OnDestroyed,
        OnLeaveBattleZone,
        OnOtherCreatureSummoned,
        OnOtherCreatureDestroyed,
        OnAnyCreatureAttack,
        OnAnyCreatureAttackPlayerUnblocked,
        OnAnyCreatureAttacked,
        OnAnyCreatureBlock,
        OnAnyCreatureSummonedByPlay,
        OnOwnManaMovedToGraveyardByOpponent,
        OnOpponentShieldTriggerUsed,
        OnShieldTriggerCastByOpponent,
        OnAnyCreatureSummoned,
        OnAnyCreatureEvolved,
        OnSpellCast,
        OnOpponentCardPlayed,
        OnShieldBroken,
        StartOfOwnerTurn,
        AfterStartTurnDraw,
        StartOfOpponentTurn,
        EndOfOwnerTurn,
        EndOfAnyTurn,
        TapAbility,
        SilentSkill,
        ReplacementDestroy,
        ReplacementDiscard,
        StateDefinedLeaveBattleZone,
        ReplacementShieldBreak
    }

    public enum AbilityOperation
    {
        None,
        RequireCondition,
        Draw,
        Discard,
        Destroy,
        DestroyOrSource,
        Tap,
        Untap,
        Move,
        Search,
        Shuffle,
        ModifyCost,
        ModifyPower,
        ModifyPowerWhileAttacking,
        GrantKeyword,
        RemoveKeyword,
        BreakShield,
        AddShield,
        LookAtCards,
        InspectOpponentHandAndTopDeck,
        RevealCards,
        RevealAndPartition,
        MoveEvolutionTop,
        GrantAbility,
        ScheduleAbility,
        RefreshHand,
        DestroySourceInstead,
        ForceOpponentCreaturesAttackNextTurn,
        AllowBlockAgainst,
        ShareTapAbility,
        ForceEvolutionEnterTapped,
        DefendingPlayerChoosesShield,
        RequireAttackTarget,
        PreferAttackRace,
        RefreshOwnHand,
        PreventPlay,
        PreventShieldTrigger,
        AllowAttackUntapped,
        PreventAttackBy,
        PreventBlockBy,
        ForceAttack,
        PreventBattle,
        ExtraTurn,
        LoseGame,
        SetRace,
        SetCivilization,
        SetCost,
        SetBreakerCount,
        CopyAbility,
        GrantBlockedCreatureSlayerThisTurn,
        IgnoreAttackRestrictionsThisTurn,
        ReplaceOwnDestructionWithManaThisTurn,
        BlockedAttackerBreaksShieldThisTurn,
        TapOneCivilizationGroup,
        ReturnShieldTriggerSpellToHand,
        PreventAttack,
        DestroyLowestPower,
        ModifyPowerWhileBattling,
        ChooseRace,
        ReplaceDestroyedRaceWithHandThisTurn,
        TakeOneTopRestBottom,
        PreventDestruction,
        RequireChosenBlocker,
        TurnFaceUp,
        PreventOpponentChoice,
        SwapHandAndMana,
        MoveShieldToHandAllowTrigger,
        SetForcedLossAfterExtraTurn,
        ScheduleSelectedUntapAfterBattle,
        ScheduleSelectedUntapAtEndOfTurn,
        ScheduleSourceDestructionAfterBattle,
        AttackerCastsShieldTrigger,
        PreferAttackSource,
        ForceEnterTapped,
        PreventManaUntap,
        DestroyAndRecruitFromDeck,
        PreventOpponentTap,
        TapAllExceptOnePerPlayer,
        SplitPairOpponentChoice,
        PreventCivilizationAttackPlayer,
        EachPlayerDiscardChosenCost,
        DestroyIfCombinedPowerBelowSource,
        PreventCreatureUntap,
        TapManaPairsToUntapCreatures,
        DestroyAndRecruitSameCost,
        PreventTapAbilities,
        EachPlayerDrawPerMatchingCard,
        ReduceOpponentShieldsToOwnCount,
        DestroyAllChosenPower,
        EachPlayerMayDraw,
        SeparateEvolutionTop,
        OpponentDraw,
        InspectShieldAndCastSpell,
        ModifyBreakerCount,
        PreventBattleDestruction,
        SearchPerOpponentCreatureLead,
        SearchSameNameAsEventToBattle,
        SearchPlayerDeckToGraveyard,
        ShieldOwnerChooses
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
        Bottom,
        EventCard,
        PowerBudget,
        PreviousSelection,
        BlockingCreature
    }

    public enum RuleCountComparison
    {
        None,
        LessThan,
        LessThanOrEqual,
        Equal,
        GreaterThanOrEqual,
        GreaterThan
    }

    public enum AbilityDuration
    {
        Immediate,
        WhileSourceInBattleZone,
        UntilEndOfTurn,
        UntilStartOfOwnersNextTurn,
        Permanent
    }

    public sealed class AbilityPreview
    {
        public RuleAbilityDefinition Ability;
        public CardInstance Source;
        public bool ConditionMetNow;
        public bool ChecksAtEvent;
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
        public bool CanAttackOnly;
        public bool OtherThanSource;
        public bool HasBlocker;
        public bool HasSilentSkill;
        public bool HasForbiddenKeyword;
        public CardKeyword ForbiddenKeyword;
        public bool HasRequiredKeyword;
        public CardKeyword RequiredKeyword;
        public int PowerExactly;
        public int PowerAtMost;
        public int PowerAtLeast;
        public int CostAtMost;
        public int CostAtLeast;
        public string Name = string.Empty;
        public string Race = string.Empty;
        public string[] Races = Array.Empty<string>();
        public string[] ExcludedRaces = Array.Empty<string>();
        public string RaceContains = string.Empty;
        public bool AnyRaceCriterion;
        public bool UseSourceChosenRace;
        public bool UsePreviousSelectionName;
        public bool CostAtMostManaCount;
        public bool CostAtMostPreviousSelectionOwnerManaCount;
        public bool UseCurrentPower;
        public string[] ExcludedNames = Array.Empty<string>();
        public Civilization[] Civilizations = Array.Empty<Civilization>();
        public Civilization[] ExcludedCivilizations = Array.Empty<Civilization>();
    }

    [Serializable]
    public sealed class RuleSelectorDefinition
    {
        public RuleController Controller = RuleController.Source;
        public RuleZone Zone = RuleZone.BattleZone;
        public RuleZone[] Zones = Array.Empty<RuleZone>();
        public RuleSelectionMode Mode = RuleSelectionMode.Choose;
        public int Minimum = 1;
        public int Maximum = 1;
        public int MaximumCombinedPower;
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
        public bool HasCountAtMost;
        public bool DuringOwnersTurn;
        public bool DuringOpponentsTurn;
        public bool SourceTapped;
        public bool SourceUntapped;
        public bool ShieldsWereBrokenThisTurn;
        public bool SourceBrokeShieldThisTurn;
        public bool SpellWasCastThisTurn;
        public bool EventCardIsSource;
        public bool EventControlledByOpponent;
        public bool EventControlledByOwner;
        public bool EventCardMatchesFilter;
        public bool PreviousSelectionMatchesFilter;
        public bool PendingAttackTargetsPlayer;
        public bool AnotherCreatureBrokeShieldThisTurn;
        public int SourcePowerAtLeast;
        public bool SourceIsBattleAttacker;
        public bool SourceNotAttackedThisTurn;
        public bool OwnCreatureHigherThanEveryOpponentCreature;
        public bool RequiresWaveStriker;
        public int OpponentDrewCardsAtLeast;
        public bool AllCardsMustMatchFilter;
        public RuleSelectorDefinition ComparisonLeft;
        public RuleSelectorDefinition ComparisonRight;
        public RuleCountComparison CountComparison;
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
        public RuleZone SecondaryDestination;
        public CardFilterDefinition PartitionFilter;
        public CardFilterDefinition RelatedFilter;
        public RuleAbilityDefinition GrantedAbility;
        public AbilityDuration Duration = AbilityDuration.Immediate;
        public AbilityConditionDefinition Condition;
        public CardKeyword Keyword;
        public int Amount = 1;
        public int AmountPerMatchingCard;
        public int AmountPerShieldBrokenByEvent;
        public int AmountPerShieldBrokenThisTurn;
        public RuleSelectorDefinition AmountSelector;
        public int Value;
        public int ValuePerMatchingCard;
        public int ValuePerDistinctCivilization;
        public RuleSelectorDefinition ValueSelector;
        public bool Optional;
        public bool UseShieldTrigger;
        public bool DestinationTapped;
        public bool ShuffleAfter;
        public int MinimumValue;
        public string TextValue = string.Empty;
        public string[] ChoiceExclusions = Array.Empty<string>();
        public bool RequiresPreviousSuccess;
        public bool SelectionCountUsesAmount;
        public bool UsePreviousSelectionCountAsAmount;
        public bool ChooseAmount;
        public bool SecondaryIsDestruction;
        public bool SelectionMaximumUsesAmount;
        public bool ChooseSelectionAmount;
        public bool OptionalCostReduction;
        public bool ReorderSelectedDeckCards;
        public bool IsSurvivorAbility;

        public RuleAbilityDefinition(string id, AbilityTiming timing, AbilityOperation operation)
        {
            Id = id;
            Timing = timing;
            Operation = operation;
        }
    }
}
