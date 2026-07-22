using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core
{
    public enum ShieldBreakReplacementResult
    {
        Normal,
        Prevented,
        MoveToGraveyard,
        AttackerCastsShieldTrigger
    }

    public interface IAbilityDecisionProvider
    {
        bool ChooseOptional(GameState state, int playerId, CardInstance source, RuleAbilityDefinition ability);
        IReadOnlyList<CardInstance> ChooseCards(
            GameState state,
            int playerId,
            CardInstance source,
            RuleAbilityDefinition ability,
            IReadOnlyList<CardInstance> candidates,
            int minimum,
            int maximum);
    }

    public interface IAbilityRaceDecisionProvider
    {
        string ChooseRace(GameState state, int playerId, CardInstance source,
            RuleAbilityDefinition ability, IReadOnlyList<string> availableRaces);
    }

    public interface IAbilityCivilizationDecisionProvider
    {
        Civilization ChooseCivilization(GameState state, int playerId, CardInstance source,
            RuleAbilityDefinition ability, IReadOnlyList<Civilization> availableCivilizations);
    }

    public interface IAbilityNumberDecisionProvider
    {
        int ChooseNumber(GameState state, int playerId, CardInstance source,
            RuleAbilityDefinition ability, int minimum, int maximum, int suggested);
    }

    public interface IAbilityPlayerDecisionProvider
    {
        int ChoosePlayer(GameState state, int playerId, CardInstance source,
            RuleAbilityDefinition ability, IReadOnlyList<int> availablePlayerIds);
    }

    /// <summary>
    /// Optional extension for choosing between replacement effects owned by the
    /// same player. The two lists are parallel and the returned value is an
    /// index into both. Providers that do not implement this interface use the
    /// deterministic first legal option.
    /// </summary>
    public interface IAbilityReplacementDecisionProvider
    {
        int ChooseReplacement(GameState state, int playerId, CardInstance affectedCard,
            IReadOnlyList<CardInstance> sources,
            IReadOnlyList<RuleAbilityDefinition> abilities);
    }

    /// <summary>
    /// Optional extension for ordering independent triggered abilities on the
    /// same card, including abilities with different timing labels that entered
    /// one simultaneous resolution window. Definitions sharing an ID are one
    /// printed ability and always resolve together in their printed order.
    /// </summary>
    public interface IAbilityTriggerOrderDecisionProvider
    {
        int ChooseNextTriggeredAbility(GameState state, int playerId,
            CardInstance source, AbilityTiming timing,
            IReadOnlyList<string> abilityIds);
    }

    public sealed class DeterministicAbilityDecisionProvider : IAbilityDecisionProvider
    {
        public bool ChooseOptional(GameState state, int playerId, CardInstance source, RuleAbilityDefinition ability) => true;

        public IReadOnlyList<CardInstance> ChooseCards(
            GameState state,
            int playerId,
            CardInstance source,
            RuleAbilityDefinition ability,
            IReadOnlyList<CardInstance> candidates,
            int minimum,
            int maximum)
        {
            var count = Math.Min(maximum, candidates.Count);
            if (count < minimum) return Array.Empty<CardInstance>();
            return candidates.OrderBy(card => card.InstanceId).Take(count).ToArray();
        }
    }

    public sealed class AbilityResolver
    {
        private enum DestructionOutcome
        {
            Invalid,
            Prevented,
            Replaced,
            Destroyed
        }

        private sealed class DestructionPlan
        {
            public CardInstance Card;
            public int OwnerId;
            public bool Prevented;
            public RuleZone ReplacementDestination;
            public bool ReplacementDestinationTapped;
            public RuleAbilityDefinition Replacement;
            public CardInstance ReplacementSource;
            public CardInstance[] DestructionTriggerSources = Array.Empty<CardInstance>();
            public DelayedAbilityEffect[] DestructionDelayedEffects =
                Array.Empty<DelayedAbilityEffect>();
        }

        private sealed class ReplacementOption
        {
            public CardInstance Source;
            public int ControllerId;
            public RuleAbilityDefinition Ability;
            public bool Prevented;
            public RuleZone Destination;
            public bool DestinationTapped;
        }

        private enum PendingTriggerKind
        {
            Source,
            Delayed
        }

        private sealed class PendingTriggerInvocation
        {
            public PendingTriggerKind Kind;
            public GameState State;
            public CardInstance Source;
            public CardInstance EventCard;
            public AbilityTiming Timing;
            public int ControllerId = -1;
            public DelayedAbilityEffect DelayedEffect;
            public RuleAbilityDefinition[] TriggeredAbilities =
                Array.Empty<RuleAbilityDefinition>();
        }

        private readonly IAbilityDecisionProvider _decisions;
        private readonly HashSet<CardInstance> _powerEvaluation = new HashSet<CardInstance>();
        private readonly List<PendingTriggerInvocation> _pendingTriggers =
            new List<PendingTriggerInvocation>();
        private bool _resolvingStateBasedActions;
        private bool _drainingPendingTriggers;
        private int _abilityResolutionDepth;

        public AbilityResolver(IAbilityDecisionProvider decisions = null)
        {
            _decisions = decisions ?? new DeterministicAbilityDecisionProvider();
        }

        public void Resolve(GameState state, CardInstance source, AbilityTiming timing)
        {
            if (state == null || state.IsFinished || source == null || source.Definition == null) return;
            if (_abilityResolutionDepth > 0 && CanChooseTriggeredAbilityOrder(timing))
            {
                var controllerId = FindOwnerId(state, source);
                if (controllerId < 0) return;
                var triggeredAbilities = RulesOf(state, source)
                    .Where(rule => rule.Timing == timing).ToArray();
                if (triggeredAbilities.Length == 0) return;
                _pendingTriggers.Add(new PendingTriggerInvocation
                {
                    Kind = PendingTriggerKind.Source,
                    State = state,
                    Source = source,
                    EventCard = source,
                    Timing = timing,
                    ControllerId = controllerId,
                    TriggeredAbilities = triggeredAbilities
                });
                return;
            }
            ResolveImmediate(state, source, timing);
            DrainPendingTriggers();
        }

        private void ResolveImmediate(GameState state, CardInstance source,
            AbilityTiming timing, bool forceOptional = false,
            IReadOnlyList<RuleAbilityDefinition> triggeredAbilities = null)
        {
            if (state == null || state.IsFinished || source == null || source.Definition == null) return;
            var ownerId = FindOwnerId(state, source);
            if (ownerId < 0) return;
            _abilityResolutionDepth++;
            try
            {
                var applicableRules = triggeredAbilities != null
                    ? triggeredAbilities.AsEnumerable()
                    : RulesOf(state, source);
                var groups = applicableRules
                    .Where(rule => rule.Timing == timing)
                    .GroupBy(rule => rule.Id)
                    .Select(group => group.ToList())
                    .ToList();
                while (groups.Count > 0)
                {
                    var selectedIndex = 0;
                    if (CanChooseTriggeredAbilityOrder(timing) && groups.Count > 1)
                    {
                        var ids = groups.Select(group => group[0].Id).ToArray();
                        selectedIndex = (_decisions as IAbilityTriggerOrderDecisionProvider)?
                            .ChooseNextTriggeredAbility(state, ownerId, source, timing, ids) ?? 0;
                        if (selectedIndex < 0 || selectedIndex >= groups.Count) selectedIndex = 0;
                    }
                    var selected = groups[selectedIndex];
                    groups.RemoveAt(selectedIndex);
                    var previousCount = 1;
                    foreach (var ability in selected)
                    {
                        if (state.IsFinished) return;
                        if (ability.RequiresPreviousSuccess && previousCount <= 0)
                        {
                            previousCount = 0;
                            continue;
                        }
                        if (!ConditionMatches(state, source, ability.Condition))
                        {
                            previousCount = 0;
                            continue;
                        }
                        var optionalPlayerId = ability.Chooser == RuleController.Opponent
                            ? 1 - ownerId
                            : ownerId;
                        if (ability.Optional && !forceOptional &&
                            !_decisions.ChooseOptional(state, optionalPlayerId,
                                source, ability))
                        {
                            previousCount = 0;
                            continue;
                        }
                        previousCount = ResolveOne(state, source, ownerId, ability,
                            previousCount);
                        if (state.IsFinished) return;
                    }
                }
                ResolveStateBasedActions(state);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
        }

        private void DrainPendingTriggers()
        {
            if (_drainingPendingTriggers || _abilityResolutionDepth > 0) return;
            _drainingPendingTriggers = true;
            try
            {
                while (_pendingTriggers.Count > 0)
                {
                    // Every trigger waiting after the current ability finishes
                    // belongs to one simultaneous resolution window. The active
                    // player resolves all of theirs before the nonactive player,
                    // even when the triggers came from different event cards in
                    // a mass move or destruction.
                    var activeIndex = _pendingTriggers.FindIndex(candidate =>
                        candidate.State != null &&
                        candidate.ControllerId == candidate.State.ActivePlayerId);
                    var preferredControllerId = activeIndex >= 0
                        ? _pendingTriggers[activeIndex].ControllerId
                        : _pendingTriggers[0].ControllerId;
                    var eligibleIndices = Enumerable.Range(0, _pendingTriggers.Count)
                        .Where(index => _pendingTriggers[index].ControllerId ==
                            preferredControllerId).ToArray();
                    var selectedIndex = eligibleIndices[0];
                    var candidateSources = eligibleIndices
                        .Select(index => _pendingTriggers[index].Source)
                        .Where(source => source != null).Distinct().ToArray();
                    if (candidateSources.Length > 1)
                    {
                        var firstPending = _pendingTriggers[selectedIndex];
                        var orderAbility = new RuleAbilityDefinition(
                            $"rules.order_pending_trigger_sources.{firstPending.Timing}",
                            firstPending.Timing, AbilityOperation.None);
                        var chosenSource = ChooseCardsSafely(firstPending.State,
                                preferredControllerId,
                                firstPending.EventCard ?? firstPending.Source,
                                orderAbility, candidateSources, 1, 1)
                            .FirstOrDefault();
                        if (chosenSource != null)
                            selectedIndex = eligibleIndices.First(index =>
                                _pendingTriggers[index].Source == chosenSource);
                    }
                    var selectedSource = _pendingTriggers[selectedIndex].Source;
                    var sameSourceIndices = eligibleIndices.Where(index =>
                        _pendingTriggers[index].Source == selectedSource).ToArray();
                    if (sameSourceIndices.Length > 1 &&
                        _decisions is IAbilityTriggerOrderDecisionProvider orderProvider)
                    {
                        var firstPending = _pendingTriggers[sameSourceIndices[0]];
                        var ids = sameSourceIndices.Select(index =>
                            PendingTriggerId(_pendingTriggers[index])).ToArray();
                        var choice = orderProvider.ChooseNextTriggeredAbility(
                            firstPending.State, preferredControllerId,
                            selectedSource, firstPending.Timing, ids);
                        if (choice >= 0 && choice < sameSourceIndices.Length)
                            selectedIndex = sameSourceIndices[choice];
                    }
                    var pending = _pendingTriggers[selectedIndex];
                    _pendingTriggers.RemoveAt(selectedIndex);
                    if (pending.State == null || pending.State.IsFinished) continue;
                    var previousEvent = pending.State.CurrentEventCard;
                    pending.State.CurrentEventCard = pending.EventCard;
                    try
                    {
                        if (pending.Kind == PendingTriggerKind.Delayed)
                            ResolveDelayedInvocationImmediate(pending);
                        else
                            ResolveImmediate(pending.State, pending.Source,
                                pending.Timing, false,
                                pending.TriggeredAbilities);
                    }
                    finally
                    {
                        pending.State.CurrentEventCard = previousEvent;
                    }
                }
            }
            finally
            {
                _drainingPendingTriggers = false;
            }
        }

        private static string PendingTriggerId(PendingTriggerInvocation pending)
        {
            if (pending?.Kind == PendingTriggerKind.Delayed)
                return pending.DelayedEffect?.Ability?.Id ?? string.Empty;
            return pending?.TriggeredAbilities?.FirstOrDefault()?.Id ?? string.Empty;
        }

        private static bool CanChooseTriggeredAbilityOrder(AbilityTiming timing)
        {
            switch (timing)
            {
                case AbilityTiming.PlayRestriction:
                case AbilityTiming.Static:
                case AbilityTiming.OnCast:
                case AbilityTiming.TapAbility:
                case AbilityTiming.SilentSkill:
                case AbilityTiming.ReplacementDestroy:
                case AbilityTiming.ReplacementDiscard:
                case AbilityTiming.StateDefinedLeaveBattleZone:
                case AbilityTiming.ReplacementShieldBreak:
                    return false;
                default:
                    return true;
            }
        }

        public void ResolveStateBasedActions(GameState state)
        {
            if (state == null || state.IsFinished || _resolvingStateBasedActions) return;
            _resolvingStateBasedActions = true;
            try
            {
                var attempted = new HashSet<CardInstance>();
                while (!state.IsFinished)
                {
                    var zeroPower = AllBattleZoneCards(state).FirstOrDefault(card =>
                        !attempted.Contains(card) && CurrentPower(state, card) <= 0);
                    if (zeroPower == null) break;
                    var before = new HashSet<CardInstance>(AllBattleZoneCards(state));
                    DestroyCard(state, zeroPower);
                    var after = new HashSet<CardInstance>(AllBattleZoneCards(state));
                    if (!before.SetEquals(after))
                    {
                        // A consumed replacement or removed protection source can
                        // make a previously protected zero-power creature eligible
                        // again, so restart the state check against the new board.
                        attempted.Clear();
                    }
                    else if (after.Contains(zeroPower))
                    {
                        // Stable prevention (for example Mihail) must not spin the
                        // state-based action loop forever.
                        attempted.Add(zeroPower);
                    }
                }
            }
            finally
            {
                _resolvingStateBasedActions = false;
            }
        }

        public int DestroyCards(GameState state, IEnumerable<CardInstance> cards)
        {
            _abilityResolutionDepth++;
            int destroyed;
            try
            {
                destroyed = ApplyDestructionEvent(state, cards)
                    .Count(outcome => outcome == DestructionOutcome.Destroyed);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
            return destroyed;
        }

        public void ResolveBattleStart(GameState state, CardInstance attacker,
            CardInstance defender)
        {
            if (state == null || state.IsFinished || attacker == null || defender == null)
                return;
            var attackerAbilities = RulesOf(state, attacker)
                .Where(rule => rule.Timing == AbilityTiming.OnBattle).ToArray();
            var defenderAbilities = RulesOf(state, defender)
                .Where(rule => rule.Timing == AbilityTiming.OnBattle).ToArray();
            _abilityResolutionDepth++;
            try
            {
                QueueSourceTriggerSnapshot(state, attacker, AbilityTiming.OnBattle,
                    attacker, attackerAbilities);
                QueueSourceTriggerSnapshot(state, defender, AbilityTiming.OnBattle,
                    defender, defenderAbilities);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveAttackDeclaration(GameState state, CardInstance attacker,
            CardInstance attackedCreature = null)
        {
            if (state == null || state.IsFinished || attacker == null) return;
            var attackerAbilities = RulesOf(state, attacker)
                .Where(rule => rule.Timing == AbilityTiming.OnAttack).ToArray();
            var attackWatchers = CaptureGlobalTriggerSources(state, attacker,
                AbilityTiming.OnAnyCreatureAttack, true);
            var attackDelayed = CaptureDelayedTriggers(state,
                AbilityTiming.OnAnyCreatureAttack);
            var attackedAbilities = attackedCreature == null
                ? Array.Empty<RuleAbilityDefinition>()
                : RulesOf(state, attackedCreature)
                    .Where(rule => rule.Timing == AbilityTiming.OnAttacked).ToArray();
            var attackedWatchers = attackedCreature == null
                ? Array.Empty<CardInstance>()
                : CaptureGlobalTriggerSources(state, attackedCreature,
                    AbilityTiming.OnAnyCreatureAttacked, true);
            var attackedDelayed = attackedCreature == null
                ? Array.Empty<DelayedAbilityEffect>()
                : CaptureDelayedTriggers(state,
                    AbilityTiming.OnAnyCreatureAttacked);

            _abilityResolutionDepth++;
            try
            {
                QueueSourceTriggerSnapshot(state, attacker, AbilityTiming.OnAttack,
                    attacker, attackerAbilities);
                ScheduleGlobalEvent(state, attacker, AbilityTiming.OnAnyCreatureAttack,
                    true, attackWatchers, attackDelayed);
                if (attackedCreature != null)
                {
                    QueueSourceTriggerSnapshot(state, attackedCreature,
                        AbilityTiming.OnAttacked, attackedCreature, attackedAbilities);
                    ScheduleGlobalEvent(state, attackedCreature,
                        AbilityTiming.OnAnyCreatureAttacked, true,
                        attackedWatchers, attackedDelayed);
                }
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveBlockDeclaration(GameState state, CardInstance attacker,
            CardInstance blocker)
        {
            if (state == null || state.IsFinished || attacker == null || blocker == null)
                return;
            var blockedAbilities = RulesOf(state, attacker)
                .Where(rule => rule.Timing == AbilityTiming.OnBlocked).ToArray();
            var blockAbilities = RulesOf(state, blocker)
                .Where(rule => rule.Timing == AbilityTiming.OnBlock).ToArray();
            var blockWatchers = CaptureGlobalTriggerSources(state, blocker,
                AbilityTiming.OnAnyCreatureBlock, true);
            var blockDelayed = CaptureDelayedTriggers(state,
                AbilityTiming.OnAnyCreatureBlock);
            _abilityResolutionDepth++;
            try
            {
                QueueSourceTriggerSnapshot(state, attacker, AbilityTiming.OnBlocked,
                    attacker, blockedAbilities);
                QueueSourceTriggerSnapshot(state, blocker, AbilityTiming.OnBlock,
                    blocker, blockAbilities);
                ScheduleGlobalEvent(state, blocker, AbilityTiming.OnAnyCreatureBlock,
                    true, blockWatchers, blockDelayed);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveUnblockedPlayerAttack(GameState state,
            CardInstance attacker)
        {
            if (state == null || state.IsFinished || attacker == null) return;
            var attackerAbilities = RulesOf(state, attacker)
                .Where(rule => rule.Timing ==
                    AbilityTiming.OnAttackPlayerUnblocked).ToArray();
            var watchers = CaptureGlobalTriggerSources(state, attacker,
                AbilityTiming.OnAnyCreatureAttackPlayerUnblocked, true);
            var delayed = CaptureDelayedTriggers(state,
                AbilityTiming.OnAnyCreatureAttackPlayerUnblocked);
            _abilityResolutionDepth++;
            try
            {
                QueueSourceTriggerSnapshot(state, attacker,
                    AbilityTiming.OnAttackPlayerUnblocked, attacker,
                    attackerAbilities);
                ScheduleGlobalEvent(state, attacker,
                    AbilityTiming.OnAnyCreatureAttackPlayerUnblocked, true,
                    watchers, delayed);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveAttackFinished(GameState state, CardInstance attacker,
            bool attackedPlayer, int attackingPlayerId)
        {
            if (state == null || state.IsFinished || attacker == null ||
                attackingPlayerId < 0 || attackingPlayerId >= state.Players.Length)
                return;
            var afterAttackAbilities = attackedPlayer &&
                    state.Players[attackingPlayerId].BattleZone.Contains(attacker)
                ? RulesOf(state, attacker).Where(rule =>
                    rule.Timing == AbilityTiming.AfterAttack).ToArray()
                : Array.Empty<RuleAbilityDefinition>();
            var delayed = CaptureDelayedTriggers(state,
                    AbilityTiming.AfterAnyCreatureAttack)
                .Where(effect => effect.OwnerPlayerId == attackingPlayerId)
                .ToArray();
            _abilityResolutionDepth++;
            try
            {
                if (afterAttackAbilities.Length > 0)
                    QueueSourceTriggerSnapshot(state, attacker,
                        AbilityTiming.AfterAttack, attacker,
                        afterAttackAbilities);
                foreach (var effect in delayed)
                {
                    if (effect?.Source == null || effect.Ability == null) continue;
                    _pendingTriggers.Add(new PendingTriggerInvocation
                    {
                        Kind = PendingTriggerKind.Delayed,
                        State = state,
                        Source = effect.Source,
                        EventCard = attacker,
                        Timing = AbilityTiming.AfterAnyCreatureAttack,
                        ControllerId = effect.OwnerPlayerId,
                        DelayedEffect = effect
                    });
                }
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveCreatureEntry(GameState state, CardInstance creature,
            bool summonedByPlay, bool notifyOpponentPlayed,
            bool castByShieldTrigger = false,
            CardInstance triggeringAttacker = null)
        {
            if (state == null || state.IsFinished || creature == null) return;
            var summonAbilities = RulesOf(state, creature)
                .Where(rule => rule.Timing == AbilityTiming.OnSummon).ToArray();
            var otherWatchers = CaptureGlobalTriggerSources(state, creature,
                AbilityTiming.OnOtherCreatureSummoned, false);
            var otherDelayed = CaptureDelayedTriggers(state,
                AbilityTiming.OnOtherCreatureSummoned);
            var playWatchers = summonedByPlay
                ? CaptureGlobalTriggerSources(state, creature,
                    AbilityTiming.OnAnyCreatureSummonedByPlay, true)
                : Array.Empty<CardInstance>();
            var playDelayed = summonedByPlay
                ? CaptureDelayedTriggers(state,
                    AbilityTiming.OnAnyCreatureSummonedByPlay)
                : Array.Empty<DelayedAbilityEffect>();
            var evolveWatchers = creature.Definition.Type == CardType.EvolutionCreature
                ? CaptureGlobalTriggerSources(state, creature,
                    AbilityTiming.OnAnyCreatureEvolved, false)
                : Array.Empty<CardInstance>();
            var evolveDelayed = creature.Definition.Type == CardType.EvolutionCreature
                ? CaptureDelayedTriggers(state, AbilityTiming.OnAnyCreatureEvolved)
                : Array.Empty<DelayedAbilityEffect>();
            var playedWatchers = notifyOpponentPlayed
                ? CaptureGlobalTriggerSources(state, creature,
                    AbilityTiming.OnOpponentCardPlayed, false)
                : Array.Empty<CardInstance>();
            var playedDelayed = notifyOpponentPlayed
                ? CaptureDelayedTriggers(state, AbilityTiming.OnOpponentCardPlayed)
                : Array.Empty<DelayedAbilityEffect>();
            var shieldWatchers = castByShieldTrigger
                ? CaptureGlobalTriggerSources(state, creature,
                    AbilityTiming.OnShieldTriggerCastByOpponent, true)
                : Array.Empty<CardInstance>();
            var shieldDelayed = castByShieldTrigger
                ? CaptureDelayedTriggers(state,
                    AbilityTiming.OnShieldTriggerCastByOpponent)
                : Array.Empty<DelayedAbilityEffect>();
            var attackerWatchers = castByShieldTrigger && triggeringAttacker != null
                ? CaptureGlobalTriggerSources(state, triggeringAttacker,
                    AbilityTiming.OnOpponentShieldTriggerUsed, true)
                : Array.Empty<CardInstance>();
            var attackerDelayed = castByShieldTrigger && triggeringAttacker != null
                ? CaptureDelayedTriggers(state,
                    AbilityTiming.OnOpponentShieldTriggerUsed)
                : Array.Empty<DelayedAbilityEffect>();

            _abilityResolutionDepth++;
            try
            {
                QueueSourceTriggerSnapshot(state, creature, AbilityTiming.OnSummon,
                    creature, summonAbilities);
                ScheduleGlobalEvent(state, creature,
                    AbilityTiming.OnOtherCreatureSummoned, false,
                    otherWatchers, otherDelayed);
                if (summonedByPlay)
                    ScheduleGlobalEvent(state, creature,
                        AbilityTiming.OnAnyCreatureSummonedByPlay, true,
                        playWatchers, playDelayed);
                if (creature.Definition.Type == CardType.EvolutionCreature)
                    ScheduleGlobalEvent(state, creature,
                        AbilityTiming.OnAnyCreatureEvolved, false,
                        evolveWatchers, evolveDelayed);
                if (notifyOpponentPlayed)
                    ScheduleGlobalEvent(state, creature,
                        AbilityTiming.OnOpponentCardPlayed, false,
                        playedWatchers, playedDelayed);
                if (castByShieldTrigger)
                    ScheduleGlobalEvent(state, creature,
                        AbilityTiming.OnShieldTriggerCastByOpponent, true,
                        shieldWatchers, shieldDelayed);
                if (castByShieldTrigger && triggeringAttacker != null)
                    ScheduleGlobalEvent(state, triggeringAttacker,
                        AbilityTiming.OnOpponentShieldTriggerUsed, true,
                        attackerWatchers, attackerDelayed);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            // Static effects apply as soon as the creature enters. Resolve all
            // resulting state-based destruction before any captured entry
            // trigger, while keeping those triggers on standby.
            _abilityResolutionDepth++;
            try
            {
                ResolveStateBasedActions(state);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveSpellCast(GameState state, CardInstance spell,
            Action resolveLegacyEffects = null, bool castByShieldTrigger = false,
            CardInstance triggeringAttacker = null)
        {
            if (state == null || state.IsFinished || spell == null) return;
            // Cast triggers are captured before the spell starts resolving. A
            // spell can remove a watcher, but that watcher's trigger is already
            // waiting and must still resolve after the spell finishes.
            var playedWatchers = CaptureGlobalTriggerSources(state, spell,
                AbilityTiming.OnOpponentCardPlayed, false);
            var playedDelayed = CaptureDelayedTriggers(state,
                AbilityTiming.OnOpponentCardPlayed);
            var spellWatchers = CaptureGlobalTriggerSources(state, spell,
                AbilityTiming.OnSpellCast, true);
            var spellDelayed = CaptureDelayedTriggers(state,
                AbilityTiming.OnSpellCast);
            var shieldWatchers = castByShieldTrigger
                ? CaptureGlobalTriggerSources(state, spell,
                    AbilityTiming.OnShieldTriggerCastByOpponent, true)
                : Array.Empty<CardInstance>();
            var shieldDelayed = castByShieldTrigger
                ? CaptureDelayedTriggers(state,
                    AbilityTiming.OnShieldTriggerCastByOpponent)
                : Array.Empty<DelayedAbilityEffect>();
            var attackerWatchers = castByShieldTrigger && triggeringAttacker != null
                ? CaptureGlobalTriggerSources(state, triggeringAttacker,
                    AbilityTiming.OnOpponentShieldTriggerUsed, true)
                : Array.Empty<CardInstance>();
            var attackerDelayed = castByShieldTrigger && triggeringAttacker != null
                ? CaptureDelayedTriggers(state,
                    AbilityTiming.OnOpponentShieldTriggerUsed)
                : Array.Empty<DelayedAbilityEffect>();

            _abilityResolutionDepth++;
            try
            {
                ScheduleGlobalEvent(state, spell,
                    AbilityTiming.OnOpponentCardPlayed, false,
                    playedWatchers, playedDelayed);
                ScheduleGlobalEvent(state, spell, AbilityTiming.OnSpellCast,
                    true, spellWatchers, spellDelayed);
                if (castByShieldTrigger)
                    ScheduleGlobalEvent(state, spell,
                        AbilityTiming.OnShieldTriggerCastByOpponent, true,
                        shieldWatchers, shieldDelayed);
                if (castByShieldTrigger && triggeringAttacker != null)
                    ScheduleGlobalEvent(state, triggeringAttacker,
                        AbilityTiming.OnOpponentShieldTriggerUsed, true,
                        attackerWatchers, attackerDelayed);

                Resolve(state, spell, AbilityTiming.OnCast);
                if (!state.IsFinished) resolveLegacyEffects?.Invoke();
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveBattleOutcome(GameState state, CardInstance attacker,
            CardInstance defender, bool attackerLost, bool defenderLost)
        {
            if (state == null || state.IsFinished || attacker == null || defender == null)
                return;
            var attackerTiming = attackerLost
                ? AbilityTiming.OnBattleLost
                : AbilityTiming.OnBattleWon;
            var defenderTiming = defenderLost
                ? AbilityTiming.OnBattleLost
                : AbilityTiming.OnBattleWon;
            var attackerAbilities = RulesOf(state, attacker)
                .Where(rule => rule.Timing == attackerTiming).ToArray();
            var defenderAbilities = RulesOf(state, defender)
                .Where(rule => rule.Timing == defenderTiming).ToArray();
            var destroyed = new List<CardInstance>();
            if (attackerLost && !PreventsBattleDestruction(state, attacker, defender))
                destroyed.Add(attacker);
            if (defenderLost && !PreventsBattleDestruction(state, defender, attacker))
                destroyed.Add(defender);

            _abilityResolutionDepth++;
            try
            {
                ApplyDestructionEvent(state, destroyed);
                // Win/loss and destruction triggers wait in the same post-battle
                // window. Their controllers may order them, with the active
                // player's complete set resolving first.
                QueueSourceTriggerSnapshot(state, attacker, attackerTiming,
                    attacker, attackerAbilities);
                QueueSourceTriggerSnapshot(state, defender, defenderTiming,
                    defender, defenderAbilities);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveBattleAftermath(GameState state, CardInstance attacker,
            CardInstance defender, bool attackerSlayer, bool defenderSlayer,
            bool destroyAttackerAfterBattle, bool destroyDefenderAfterBattle)
        {
            if (state == null || state.IsFinished || attacker == null ||
                defender == null) return;
            var attackerAbilities = RulesOf(state, attacker).Where(rule =>
                    rule.Timing == AbilityTiming.AfterBattle).ToList();
            var defenderAbilities = RulesOf(state, defender).Where(rule =>
                    rule.Timing == AbilityTiming.AfterBattle).ToList();
            if (attackerSlayer)
                attackerAbilities.Add(BattleDestructionAbility(
                    $"rules.slayer.{attacker.InstanceId}", false));
            if (destroyAttackerAfterBattle)
                attackerAbilities.Add(BattleDestructionAbility(
                    $"rules.destroy_after_battle.{attacker.InstanceId}", true));
            if (defenderSlayer)
                defenderAbilities.Add(BattleDestructionAbility(
                    $"rules.slayer.{defender.InstanceId}", false));
            if (destroyDefenderAfterBattle)
                defenderAbilities.Add(BattleDestructionAbility(
                    $"rules.destroy_after_battle.{defender.InstanceId}", true));
            _abilityResolutionDepth++;
            try
            {
                QueueSourceTriggerSnapshot(state, attacker,
                    AbilityTiming.AfterBattle, defender, attackerAbilities);
                QueueSourceTriggerSnapshot(state, defender,
                    AbilityTiming.AfterBattle, attacker, defenderAbilities);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        private static RuleAbilityDefinition BattleDestructionAbility(string id,
            bool destroySource) => new RuleAbilityDefinition(id,
            AbilityTiming.AfterBattle, AbilityOperation.Destroy)
        {
            Selector = new RuleSelectorDefinition
            {
                Controller = destroySource
                    ? RuleController.Source
                    : RuleController.Opponent,
                Zone = RuleZone.BattleZone,
                Mode = destroySource
                    ? RuleSelectionMode.SourceCard
                    : RuleSelectionMode.EventCard,
                Minimum = 0,
                Maximum = 1,
                Filter = new CardFilterDefinition { CreaturesOnly = true }
            }
        };

        private void QueueSourceTriggerSnapshot(GameState state,
            CardInstance source, AbilityTiming timing, CardInstance eventCard,
            IReadOnlyList<RuleAbilityDefinition> triggeredAbilities)
        {
            if (state == null || source == null || triggeredAbilities == null ||
                triggeredAbilities.Count == 0) return;
            var controllerId = FindOwnerId(state, source);
            if (controllerId < 0) return;
            _pendingTriggers.Add(new PendingTriggerInvocation
            {
                Kind = PendingTriggerKind.Source,
                State = state,
                Source = source,
                EventCard = eventCard ?? source,
                Timing = timing,
                ControllerId = controllerId,
                TriggeredAbilities = triggeredAbilities.ToArray()
            });
        }

        private IReadOnlyList<DestructionOutcome> ApplyDestructionEvent(
            GameState state, IEnumerable<CardInstance> cards)
        {
            if (state == null || cards == null || state.IsFinished)
                return Array.Empty<DestructionOutcome>();
            var candidates = cards.Where(card => card != null)
                .Distinct()
                .Where(card =>
                {
                    var ownerId = FindOwnerId(state, card);
                    return ownerId >= 0 && state.Players[ownerId].BattleZone.Contains(card);
                })
                .ToArray();
            // Prevention and replacement effects are evaluated for the complete
            // destruction event before any member leaves the battle zone. Aura
            // replacements therefore cannot change merely because of list order.
            var destructionTriggerSources = CaptureGlobalTriggerSources(state, null,
                AbilityTiming.OnOtherCreatureDestroyed, true);
            var destructionDelayedEffects = CaptureDelayedTriggers(state,
                AbilityTiming.OnOtherCreatureDestroyed);
            var reservedReplacementSources = new HashSet<CardInstance>();
            var plans = candidates.Select(card => BuildDestructionPlan(state, card,
                reservedReplacementSources)).ToArray();
            foreach (var plan in plans)
            {
                plan.DestructionTriggerSources = destructionTriggerSources;
                plan.DestructionDelayedEffects = destructionDelayedEffects;
            }
            var outcomes = new List<DestructionOutcome>();
            // A saver chosen for a simultaneous event is destroyed as part of
            // that replacement before its own original destruction plan is
            // applied. This preserves the official mass-destruction ruling and
            // also ensures one saver can replace only one event.
            var destroyedAsReplacement = new HashSet<CardInstance>();
            foreach (var plan in plans.OrderBy(plan =>
                         plan.Replacement?.Operation == AbilityOperation.DestroySourceInstead
                             ? 0 : 1))
            {
                if (state.IsFinished) break;
                outcomes.Add(ApplyDestructionPlan(state, plan, destroyedAsReplacement));
            }
            return outcomes;
        }

        public bool CanPlay(GameState state, CardInstance card)
        {
            if (state == null || card == null) return false;
            if (!RulesOf(state, card)
                .Where(rule => rule.Timing == AbilityTiming.PlayRestriction)
                .All(rule => ConditionMatches(state, card, rule.Condition))) return false;
            foreach (var source in AllBattleZoneCards(state))
            {
                foreach (var ability in RulesOf(state, source).Where(rule =>
                             rule.Timing == AbilityTiming.Static &&
                             rule.Operation == AbilityOperation.PreventPlay))
                {
                    if (!ConditionMatches(state, source, ability.Condition)) continue;
                    if (ControllerAndFilterMatch(state, source, card, ability.Selector)) return false;
                }
            }
            return true;
        }

        public bool CanUseShieldTrigger(GameState state, CardInstance card)
        {
            if (state == null || card == null) return false;
            foreach (var source in AllBattleZoneCards(state))
            foreach (var ability in RulesOf(state, source).Where(rule =>
                         rule.Timing == AbilityTiming.Static &&
                         rule.Operation == AbilityOperation.PreventShieldTrigger))
                if (ConditionMatches(state, source, ability.Condition) &&
                    ControllerAndFilterMatch(state, source, card, ability.Selector)) return false;
            return true;
        }

        public bool CanAttack(GameState state, CardInstance attacker)
        {
            if (state == null || attacker == null) return false;
            foreach (var source in AllBattleZoneCards(state))
            foreach (var ability in RulesOf(state, source).Where(rule =>
                         rule.Timing == AbilityTiming.Static &&
                         rule.Operation == AbilityOperation.PreventAttack))
                if (ConditionMatches(state, source, ability.Condition) &&
                    ControllerAndFilterMatch(state, source, attacker, ability.Selector)) return false;
            return true;
        }

        /// <summary>
        /// True when the active player's creature can legally declare at least
        /// one attack in the current board state. This is used by printed
        /// effects that require choosing a creature "that can attack," rather
        /// than merely an untapped creature.
        /// </summary>
        public bool CanDeclareAnyAttack(GameState state, CardInstance attacker)
        {
            if (state == null || attacker == null ||
                !attacker.Definition.IsCreature || attacker.Tapped) return false;
            var ownerId = FindOwnerId(state, attacker);
            if (ownerId < 0 || ownerId != state.ActivePlayerId) return false;
            var ignoresPlayerRestrictions =
                state.IgnoreAttackRestrictionsThisTurn[ownerId] ||
                HasKeyword(state, attacker,
                    CardKeyword.IgnoreAttackRestrictionsAgainstPlayer);
            var hasSummoningSickness =
                attacker.EnteredBattleZoneOnTurn == state.TurnNumber &&
                !HasKeyword(state, attacker, CardKeyword.SpeedAttacker);
            var generallyPrevented = HasKeyword(state, attacker,
                    CardKeyword.CannotAttack) || !CanAttack(state, attacker);

            var canAttackPlayer = ignoresPlayerRestrictions ||
                (!hasSummoningSickness && !generallyPrevented &&
                 !HasKeyword(state, attacker,
                     CardKeyword.CannotAttackPlayers) &&
                 CanAttackPlayerByCivilization(state, attacker, 1 - ownerId) &&
                 MeetsAttackTargetRequirement(state, attacker, null));

            var canAttackCreature = !hasSummoningSickness &&
                !generallyPrevented &&
                !HasKeyword(state, attacker, CardKeyword.CannotAttackCreatures) &&
                state.Players[1 - ownerId].BattleZone.Any(target =>
                    !HasKeyword(state, target, CardKeyword.CannotBeAttacked) &&
                    CanAttackTarget(state, attacker, target) &&
                    MeetsAttackTargetRequirement(state, attacker, target) &&
                    (target.Tapped ||
                     HasKeyword(state, target,
                         CardKeyword.AttackableWhileUntapped) ||
                     CanAttackUntapped(state, attacker, target)));
            return canAttackPlayer || canAttackCreature;
        }

        public bool CanAttackUntapped(GameState state, CardInstance attacker, CardInstance target)
        {
            if (HasKeyword(state, attacker, CardKeyword.AttackUntappedCreature)) return true;
            return RulesOf(state, attacker).Any(ability => ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.AllowAttackUntapped &&
                MatchesFilter(state, target, attacker, ability.RelatedFilter) &&
                ConditionMatches(state, attacker, ability.Condition));
        }

        public bool CanAttackTarget(GameState state, CardInstance attacker, CardInstance target) =>
            !RulesOf(state, target).Any(ability => ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.PreventAttackBy &&
                MatchesFilter(state, attacker, target, ability.RelatedFilter) &&
                ConditionMatches(state, target, ability.Condition));

        public bool CanBlock(GameState state, CardInstance attacker, CardInstance blocker) =>
            !RulesOf(state, attacker).Any(ability => ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.PreventBlockBy &&
                MatchesFilter(state, blocker, attacker, ability.RelatedFilter) &&
                ConditionMatches(state, attacker, ability.Condition));

        public bool CanUseAsBlocker(GameState state, CardInstance blocker, CardInstance attacker)
        {
            if (HasKeyword(state, blocker, CardKeyword.Blocker) ||
                HasKeyword(state, blocker, CardKeyword.BlockIfAble)) return true;
            return RulesOf(state, blocker).Any(ability => ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.AllowBlockAgainst &&
                MatchesFilter(state, attacker, blocker, ability.RelatedFilter) &&
                ConditionMatches(state, blocker, ability.Condition));
        }

        public bool CanUseTapAbility(GameState state, CardInstance creature)
        {
            if (AllBattleZoneCards(state).Any(source => RulesOf(state, source).Any(ability =>
                    ability.Timing == AbilityTiming.Static &&
                    ability.Operation == AbilityOperation.PreventTapAbilities &&
                    ConditionMatches(state, source, ability.Condition)))) return false;
            if (HasKeyword(state, creature, CardKeyword.TapAbility)) return true;
            return AllBattleZoneCards(state).Any(source => FindOwnerId(state, source) == FindOwnerId(state, creature) &&
                RulesOf(state, source).Any(ability => ability.Timing == AbilityTiming.Static &&
                    ability.Operation == AbilityOperation.ShareTapAbility &&
                    MatchesStaticTarget(state, source, creature, ability.Selector)));
        }

        public bool CanUntapCreaturesAtStart(GameState state, int playerId)
        {
            if (state == null || playerId < 0 || playerId >= state.Players.Length) return false;
            return !AllBattleZoneCards(state).Any(source => RulesOf(state, source).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.PreventCreatureUntap &&
                ConditionMatches(state, source, ability.Condition)));
        }

        public bool CanAttackPlayerByCivilization(GameState state, CardInstance attacker,
            int defendingPlayerId)
        {
            if (state == null || attacker == null || defendingPlayerId < 0 ||
                defendingPlayerId >= state.Players.Length) return false;
            return !state.PreventOpponentAttackCivilizationsUntilNextTurn.Any(effect =>
                effect != null && effect.PlayerId == defendingPlayerId &&
                attacker.Definition.Civilizations.Contains(effect.Civilization));
        }

        public int AdditionalBreakerCount(GameState state, CardInstance attacker)
        {
            if (state == null || attacker == null) return 0;
            var amount = 0;
            foreach (var source in AllBattleZoneCards(state))
            foreach (var ability in RulesOf(state, source).Where(rule =>
                         rule.Timing == AbilityTiming.Static &&
                         rule.Operation == AbilityOperation.ModifyBreakerCount))
                if (ConditionMatches(state, source, ability.Condition) &&
                    MatchesStaticTarget(state, source, attacker, ability.Selector))
                    amount += ability.Value;
            return amount;
        }

        public bool PreventsBattleDestruction(GameState state, CardInstance target,
            CardInstance opposingCreature)
        {
            if (state == null || target == null || opposingCreature == null) return false;
            foreach (var source in AllBattleZoneCards(state))
            foreach (var ability in RulesOf(state, source).Where(rule =>
                         rule.Timing == AbilityTiming.Static &&
                         rule.Operation == AbilityOperation.PreventBattleDestruction))
                if (ConditionMatches(state, source, ability.Condition) &&
                    MatchesStaticTarget(state, source, target, ability.Selector) &&
                    MatchesFilter(state, opposingCreature, source, ability.RelatedFilter))
                    return true;
            return false;
        }

        public void ResolveTapAbility(GameState state, CardInstance creature)
        {
            if (state == null || creature == null) return;
            _abilityResolutionDepth++;
            try
            {
                var ownerId = FindOwnerId(state, creature);
                var abilitySources = new List<CardInstance>();
                if (RulesOf(state, creature).Any(ability =>
                        ability.Timing == AbilityTiming.TapAbility))
                    abilitySources.Add(creature);
                abilitySources.AddRange(AllBattleZoneCards(state).Where(source =>
                    source != creature && FindOwnerId(state, source) == ownerId &&
                    RulesOf(state, source).Any(ability => ability.Timing == AbilityTiming.Static &&
                            ability.Operation == AbilityOperation.ShareTapAbility &&
                            MatchesStaticTarget(state, source, creature,
                                ability.Selector))));
                var distinctSources = abilitySources.Distinct().ToArray();
                if (distinctSources.Length > 0)
                {
                    var choice = new RuleAbilityDefinition(
                        "rules.choose_tap_ability", AbilityTiming.TapAbility,
                        AbilityOperation.None);
                    var selectedSource = ChooseCardsSafely(state, ownerId, creature,
                        choice, distinctSources, 1, 1).FirstOrDefault() ??
                        distinctSources[0];
                    ResolveImmediate(state, selectedSource,
                        AbilityTiming.TapAbility);
                }
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public bool ShouldEnterBattleZoneTapped(GameState state, CardInstance card)
        {
            return AllBattleZoneCards(state).Any(source => RulesOf(state, source).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                (ability.Operation == AbilityOperation.ForceEvolutionEnterTapped ||
                 ability.Operation == AbilityOperation.ForceEnterTapped) &&
                ConditionMatches(state, source, ability.Condition) &&
                ControllerAndFilterMatch(state, source, card, ability.Selector)));
        }

        public bool MeetsAttackTargetRequirement(GameState state, CardInstance attacker,
            CardInstance target)
        {
            return RulesOf(state, attacker).Where(ability =>
                    ability.Timing == AbilityTiming.Static &&
                    ability.Operation == AbilityOperation.RequireAttackTarget &&
                    ConditionMatches(state, attacker, ability.Condition))
                .All(ability => target != null &&
                    MatchesFilter(state, target, attacker, ability.RelatedFilter));
        }

        public IReadOnlyList<CardInstance> PreferredAttackTargets(GameState state, CardInstance attacker)
        {
            var ownerId = FindOwnerId(state, attacker);
            if (ownerId < 0) return Array.Empty<CardInstance>();
            var opponent = state.Players[1 - ownerId];
            var filters = opponent.BattleZone.SelectMany(source => RulesOf(state, source)
                .Where(ability => ability.Timing == AbilityTiming.Static &&
                                  ability.Operation == AbilityOperation.PreferAttackRace)
                .Select(ability => ability.RelatedFilter)).ToArray();
            var raceTargets = opponent.BattleZone.Where(target => filters.Any(filter =>
                MatchesFilter(state, target, attacker, filter)));
            var sourceTargets = opponent.BattleZone.Where(source => RulesOf(state, source).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.PreferAttackSource &&
                ConditionMatches(state, source, ability.Condition)));
            return raceTargets.Concat(sourceTargets).Distinct().ToArray();
        }

        public ShieldBreakReplacementResult ResolveShieldBreakReplacement(
            GameState state, CardInstance attacker, CardInstance shield,
            int defendingPlayerId)
        {
            if (state == null || attacker == null || shield == null ||
                defendingPlayerId < 0 || defendingPlayerId >= state.Players.Length)
                return ShieldBreakReplacementResult.Normal;
            var attackerId = FindOwnerId(state, attacker);
            if (attackerId < 0) return ShieldBreakReplacementResult.Normal;
            var options = new List<ReplacementOption>();

            foreach (var ability in RulesOf(state, attacker).Where(rule =>
                         rule.Timing == AbilityTiming.ReplacementShieldBreak &&
                         ConditionMatches(state, attacker, rule.Condition) &&
                         ((rule.Operation == AbilityOperation.Move &&
                           rule.Destination == RuleZone.Graveyard) ||
                          rule.Operation == AbilityOperation.AttackerCastsShieldTrigger)))
                options.Add(new ReplacementOption
                {
                    Source = attacker,
                    ControllerId = attackerId,
                    Ability = ability,
                    Destination = ability.Destination
                });

            foreach (var source in state.Players[defendingPlayerId].BattleZone.ToArray())
            foreach (var ability in RulesOf(state, source).Where(rule =>
                         rule.Timing == AbilityTiming.ReplacementShieldBreak &&
                         (rule.Operation == AbilityOperation.DestroySourceInstead ||
                          rule.Operation == AbilityOperation.Discard) &&
                         ConditionMatches(state, source, rule.Condition)))
                options.Add(new ReplacementOption
                {
                    Source = source,
                    ControllerId = defendingPlayerId,
                    Ability = ability
                });

            while (options.Count > 0)
            {
                var selected = ChooseReplacementOption(state, shield, options);
                if (selected == null) break;
                options.Remove(selected);
                switch (selected.Ability.Operation)
                {
                    case AbilityOperation.Move:
                        return ShieldBreakReplacementResult.MoveToGraveyard;
                    case AbilityOperation.AttackerCastsShieldTrigger:
                        return ShieldBreakReplacementResult.AttackerCastsShieldTrigger;
                    case AbilityOperation.DestroySourceInstead:
                        if (DestroyCardWithoutReplacement(state, selected.Source))
                            return ShieldBreakReplacementResult.Prevented;
                        break;
                    case AbilityOperation.Discard:
                    {
                        var candidates = Candidates(state, selected.Source,
                            selected.Ability.Selector).ToArray();
                        var discarded = Select(state, selected.Source,
                            selected.ControllerId, selected.Ability, candidates);
                        var required = selected.Ability.Selector?.Minimum ?? 0;
                        if (discarded.Count < required) break;
                        foreach (var card in discarded)
                            MoveCard(state, card, RuleZone.Graveyard, false, true,
                                selected.ControllerId);
                        state.LastRuleSelection.Clear();
                        state.LastRuleSelection.AddRange(discarded);
                        return ShieldBreakReplacementResult.Prevented;
                    }
                }
            }
            return ShieldBreakReplacementResult.Normal;
        }

        private void ResolveStolenShield(GameState state, CardInstance attacker,
            CardInstance shield, int defendingPlayerId)
        {
            var attackerId = FindOwnerId(state, attacker);
            if (attackerId < 0 || defendingPlayerId < 0 ||
                defendingPlayerId >= state.Players.Length) return;
            var defender = state.Players[defendingPlayerId];
            state.InspectionPlayerId = attackerId;
            state.InspectedCards.Clear();
            state.InspectedCards.Add(shield);
            if (shield.Definition.Type == CardType.Spell &&
                HasKeyword(state, shield, CardKeyword.ShieldTrigger))
            {
                defender.Shields.Remove(shield);
                state.Players[attackerId].Resolving.Add(shield);
                state.SpellCastThisTurn[attackerId] = true;
                ResolveSpellCast(state, shield);
                state.Players[attackerId].Resolving.Remove(shield);
                if (HasKeyword(state, shield, CardKeyword.Charger))
                {
                    shield.Tapped = shield.Definition.Civilizations.Length > 1;
                    defender.Mana.Add(shield);
                }
                else
                {
                    shield.Tapped = false;
                    defender.Graveyard.Add(shield);
                }
                return;
            }

            MoveCard(state, shield, RuleZone.Hand, false, true, attackerId);
            if (HasKeyword(state, shield, CardKeyword.ShieldTrigger) &&
                CanUseShieldTrigger(state, shield) &&
                !state.PendingShieldTriggers.Contains(shield))
                state.PendingShieldTriggers.Add(shield);
        }

        public IReadOnlyList<CardInstance> ChooseShieldsToBreak(GameState state,
            CardInstance attacker, int defendingPlayerId, int count)
        {
            if (state == null || attacker == null || defendingPlayerId < 0 ||
                defendingPlayerId >= state.Players.Length || count <= 0)
                return Array.Empty<CardInstance>();
            var attackerId = FindOwnerId(state, attacker);
            if (attackerId < 0) return Array.Empty<CardInstance>();
            var defender = state.Players[defendingPlayerId];
            var candidates = defender.Shields.ToArray();
            var requested = Math.Min(count, candidates.Length);
            var chooserId = attackerId;
            if (defender.BattleZone.Any(source => RulesOf(state, source).Any(ability =>
                    ability.Timing == AbilityTiming.Static &&
                    ability.Operation == AbilityOperation.DefendingPlayerChoosesShield &&
                    ConditionMatches(state, source, ability.Condition))) ||
                ShieldOwnerChooses(state, chooserId, defendingPlayerId))
                chooserId = defendingPlayerId;
            var choiceAbility = new RuleAbilityDefinition("rules.choose_shields_to_break",
                AbilityTiming.Static, AbilityOperation.BreakShield)
            {
                Selector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Opponent,
                    Zone = RuleZone.ShieldZone,
                    Mode = RuleSelectionMode.Choose,
                    Minimum = requested,
                    Maximum = requested
                }
            };
            return ChooseCardsSafely(state, chooserId, attacker, choiceAbility,
                candidates, requested, requested);
        }

        private bool ShieldOwnerChooses(GameState state, int chooserId, int shieldOwnerId)
        {
            if (chooserId == shieldOwnerId) return false;
            return AllBattleZoneCards(state).Any(source =>
            {
                var sourceOwnerId = FindOwnerId(state, source);
                return (sourceOwnerId == chooserId || sourceOwnerId == shieldOwnerId) &&
                    RulesOf(state, source).Any(ability =>
                        ability.Timing == AbilityTiming.Static &&
                        ability.Operation == AbilityOperation.ShieldOwnerChooses &&
                        ConditionMatches(state, source, ability.Condition));
            });
        }

        public bool BlockedAttackerBreaksShield(GameState state, int attackerPlayerId, CardInstance attacker)
        {
            if (state == null || attacker == null || attackerPlayerId < 0 ||
                attackerPlayerId >= state.Players.Length) return false;
            var civilization = state.BlockedAttackerBreaksShieldCivilizationThisTurn[attackerPlayerId];
            return civilization.HasValue && attacker.Definition.Civilizations.Contains(civilization.Value);
        }

        public bool ShieldTriggerSpellReturnsToHand(GameState state, int playerId)
        {
            if (state == null || playerId < 0 || playerId >= state.Players.Length) return false;
            return state.Players[playerId].BattleZone.Any(source => RulesOf(state, source).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.ReturnShieldTriggerSpellToHand &&
                ConditionMatches(state, source, ability.Condition)));
        }

        public bool CanUntapManaAtStart(GameState state, int playerId)
        {
            if (state == null || playerId < 0 || playerId >= state.Players.Length) return false;
            return !AllBattleZoneCards(state).Any(source => RulesOf(state, source).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.PreventManaUntap &&
                FindOwnerId(state, source) == playerId &&
                ConditionMatches(state, source, ability.Condition)));
        }

        public bool HasRace(CardInstance card, string race)
        {
            if (card == null || string.IsNullOrWhiteSpace(race)) return false;
            return EffectiveRaces(card).Any(candidate =>
                candidate.Equals(race, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasRace(GameState state, CardInstance card, string race) =>
            HasRace(card, race) || (race != null &&
                race.Equals("Survivor", StringComparison.OrdinalIgnoreCase) &&
                IsSurvivor(state, card));

        public bool RaceContains(CardInstance card, string value)
        {
            if (card == null || string.IsNullOrWhiteSpace(value)) return false;
            return EffectiveRaces(card).Any(candidate =>
                candidate.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public bool ResolveOptionalTiming(GameState state, CardInstance source, AbilityTiming timing)
        {
            if (state == null || state.IsFinished || source == null) return false;
            var ownerId = FindOwnerId(state, source);
            if (ownerId < 0) return false;
            var activation = RulesOf(state, source).FirstOrDefault(rule =>
                rule.Timing == timing &&
                ConditionMatches(state, source, rule.Condition));
            if (activation == null ||
                !_decisions.ChooseOptional(state, ownerId, source, activation)) return false;
            ResolveImmediate(state, source, timing, true);
            DrainPendingTriggers();
            return true;
        }

        public void ResolveGlobalEvent(GameState state, CardInstance eventCard, AbilityTiming timing,
            bool includeEventCard = false)
        {
            if (state == null || eventCard == null) return;
            ScheduleGlobalEvent(state, eventCard, timing, includeEventCard,
                CaptureGlobalTriggerSources(state, eventCard, timing, includeEventCard),
                CaptureDelayedTriggers(state, timing));
        }

        private void ScheduleGlobalEvent(GameState state, CardInstance eventCard,
            AbilityTiming timing, bool includeEventCard,
            IReadOnlyList<CardInstance> capturedSources,
            IReadOnlyList<DelayedAbilityEffect> capturedDelayed)
        {
            if (state == null || eventCard == null) return;
            foreach (var source in (capturedSources ??
                         Array.Empty<CardInstance>()).Where(card =>
                         includeEventCard || card != eventCard))
            {
                var controllerId = FindOwnerId(state, source);
                if (controllerId < 0) continue;
                var triggeredAbilities = RulesOf(state, source)
                    .Where(rule => rule.Timing == timing).ToArray();
                if (triggeredAbilities.Length == 0) continue;
                _pendingTriggers.Add(new PendingTriggerInvocation
                {
                    Kind = PendingTriggerKind.Source,
                    State = state,
                    Source = source,
                    EventCard = eventCard,
                    Timing = timing,
                    ControllerId = controllerId,
                    TriggeredAbilities = triggeredAbilities
                });
            }
            foreach (var delayed in capturedDelayed ??
                         Array.Empty<DelayedAbilityEffect>())
            {
                if (delayed == null || delayed.Source == null ||
                    delayed.Ability == null || delayed.Ability.Timing != timing)
                    continue;
                _pendingTriggers.Add(new PendingTriggerInvocation
                {
                    Kind = PendingTriggerKind.Delayed,
                    State = state,
                    Source = delayed.Source,
                    EventCard = eventCard,
                    Timing = timing,
                    ControllerId = delayed.OwnerPlayerId,
                    DelayedEffect = delayed
                });
            }
            DrainPendingTriggers();
        }

        private void ResolveDelayedInvocationImmediate(
            PendingTriggerInvocation pending)
        {
            var state = pending?.State;
            var delayed = pending?.DelayedEffect;
            if (state == null || delayed == null || delayed.Source == null ||
                delayed.Ability == null || state.IsFinished) return;
            _abilityResolutionDepth++;
            try
            {
                if (ConditionMatches(state, delayed.Source,
                        delayed.Ability.Condition))
                {
                    if (delayed.FixedTargets != null &&
                        delayed.FixedTargets.Count > 0 &&
                        delayed.Ability.Operation == AbilityOperation.Untap)
                    {
                        if (!delayed.Ability.Optional ||
                            _decisions.ChooseOptional(state,
                                delayed.OwnerPlayerId, delayed.Source,
                                delayed.Ability))
                        {
                            foreach (var target in delayed.FixedTargets)
                            {
                                var targetOwnerId = FindOwnerId(state, target);
                                if (targetOwnerId >= 0 &&
                                    state.Players[targetOwnerId].BattleZone.Contains(target))
                                    target.Tapped = false;
                            }
                        }
                    }
                    else
                    {
                        ResolveOne(state, delayed.Source,
                            delayed.OwnerPlayerId, delayed.Ability, 1);
                    }
                }
                ResolveStateBasedActions(state);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
        }

        private CardInstance[] CaptureGlobalTriggerSources(GameState state,
            CardInstance eventCard, AbilityTiming timing, bool includeEventCard) =>
            AllBattleZoneCards(state)
                .Where(card => (includeEventCard || card != eventCard) &&
                    RulesOf(state, card).Any(rule => rule.Timing == timing))
                .ToArray();

        private static DelayedAbilityEffect[] CaptureDelayedTriggers(
            GameState state, AbilityTiming timing) =>
            state.DelayedAbilities.Where(effect => effect != null &&
                    effect.Ability != null && effect.Ability.Timing == timing)
                .ToArray();

        public void ResolveEndStep(GameState state, int endingPlayerId)
        {
            if (state == null || state.IsFinished || endingPlayerId < 0 ||
                endingPlayerId >= state.Players.Length) return;
            var activeSnapshots = state.Players[endingPlayerId].BattleZone
                .Select(card => new
                {
                    Card = card,
                    AnyTurn = RulesOf(state, card).Where(rule =>
                        rule.Timing == AbilityTiming.EndOfAnyTurn).ToArray(),
                    OwnerTurn = RulesOf(state, card).Where(rule =>
                        rule.Timing == AbilityTiming.EndOfOwnerTurn).ToArray()
                }).ToArray();
            var nonactiveSnapshots = state.Players[1 - endingPlayerId].BattleZone
                .Select(card => new
                {
                    Card = card,
                    AnyTurn = RulesOf(state, card).Where(rule =>
                        rule.Timing == AbilityTiming.EndOfAnyTurn).ToArray()
                }).ToArray();
            var delayed = CaptureDelayedTriggers(state,
                    AbilityTiming.EndOfOwnerTurn)
                .Where(effect => effect.OwnerPlayerId == endingPlayerId)
                .ToArray();

            _abilityResolutionDepth++;
            try
            {
                foreach (var snapshot in activeSnapshots)
                {
                    QueueSourceTriggerSnapshot(state, snapshot.Card,
                        AbilityTiming.EndOfAnyTurn, snapshot.Card,
                        snapshot.AnyTurn);
                    QueueSourceTriggerSnapshot(state, snapshot.Card,
                        AbilityTiming.EndOfOwnerTurn, snapshot.Card,
                        snapshot.OwnerTurn);
                }
                foreach (var effect in delayed)
                {
                    if (effect?.Source == null || effect.Ability == null) continue;
                    _pendingTriggers.Add(new PendingTriggerInvocation
                    {
                        Kind = PendingTriggerKind.Delayed,
                        State = state,
                        Source = effect.Source,
                        EventCard = effect.Source,
                        Timing = AbilityTiming.EndOfOwnerTurn,
                        ControllerId = effect.OwnerPlayerId,
                        DelayedEffect = effect
                    });
                }
                foreach (var snapshot in nonactiveSnapshots)
                    QueueSourceTriggerSnapshot(state, snapshot.Card,
                        AbilityTiming.EndOfAnyTurn, snapshot.Card,
                        snapshot.AnyTurn);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveStartStep(GameState state, int activePlayerId)
        {
            if (state == null || state.IsFinished || activePlayerId < 0 ||
                activePlayerId >= state.Players.Length) return;
            var activeSnapshots = state.Players[activePlayerId].BattleZone
                .Select(card => new
                {
                    Card = card,
                    Abilities = RulesOf(state, card).Where(rule =>
                        rule.Timing == AbilityTiming.StartOfOwnerTurn).ToArray()
                }).ToArray();
            var nonactiveSnapshots = state.Players[1 - activePlayerId].BattleZone
                .Select(card => new
                {
                    Card = card,
                    Abilities = RulesOf(state, card).Where(rule =>
                        rule.Timing == AbilityTiming.StartOfOpponentTurn).ToArray()
                }).ToArray();
            _abilityResolutionDepth++;
            try
            {
                foreach (var snapshot in activeSnapshots)
                    QueueSourceTriggerSnapshot(state, snapshot.Card,
                        AbilityTiming.StartOfOwnerTurn, snapshot.Card,
                        snapshot.Abilities);
                foreach (var snapshot in nonactiveSnapshots)
                    QueueSourceTriggerSnapshot(state, snapshot.Card,
                        AbilityTiming.StartOfOpponentTurn, snapshot.Card,
                        snapshot.Abilities);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolvePlayerSources(GameState state, int playerId,
            AbilityTiming timing)
        {
            if (state == null || state.IsFinished || playerId < 0 ||
                playerId >= state.Players.Length) return;
            _abilityResolutionDepth++;
            try
            {
                var snapshots = state.Players[playerId].BattleZone.Select(card => new
                    {
                        Card = card,
                        Abilities = RulesOf(state, card)
                            .Where(rule => rule.Timing == timing).ToArray()
                    })
                    .Where(item => item.Abilities.Length > 0).ToArray();
                var pending = snapshots.Select(item => item.Card).ToList();
                while (pending.Count > 0 && !state.IsFinished)
                {
                    var orderAbility = new RuleAbilityDefinition(
                        $"rules.order_player_sources.{timing}", timing,
                        AbilityOperation.None);
                    var chosen = ChooseCardsSafely(state, playerId, pending[0],
                        orderAbility, pending, 1, 1).FirstOrDefault() ?? pending[0];
                    pending.Remove(chosen);
                    var snapshot = snapshots.First(item => item.Card == chosen)
                        .Abilities;
                    ResolveImmediate(state, chosen, timing, false, snapshot);
                }
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
        }

        public void ResolveDelayed(GameState state, AbilityTiming timing, int ownerPlayerId,
            CardInstance eventCard = null)
        {
            ResolveDelayedImmediate(state, timing, ownerPlayerId, eventCard);
            DrainPendingTriggers();
        }

        private void ResolveDelayedImmediate(GameState state, AbilityTiming timing,
            int ownerPlayerId, CardInstance eventCard = null)
        {
            if (state == null) return;
            ResolveDelayedSnapshotImmediate(state, timing, ownerPlayerId, eventCard,
                CaptureDelayedTriggers(state, timing));
        }

        private void ResolveDelayedSnapshotImmediate(GameState state,
            AbilityTiming timing, int ownerPlayerId, CardInstance eventCard,
            IReadOnlyList<DelayedAbilityEffect> delayedEffects)
        {
            if (state == null) return;
            var previousEvent = state.CurrentEventCard;
            state.CurrentEventCard = eventCard;
            _abilityResolutionDepth++;
            try
            {
                foreach (var delayed in (delayedEffects ?? Array.Empty<DelayedAbilityEffect>())
                             .Where(effect => effect != null && effect.Ability != null &&
                                 effect.Ability.Timing == timing &&
                                 effect.OwnerPlayerId == ownerPlayerId))
                {
                    if (state.IsFinished) break;
                    if (delayed.Source == null || !ConditionMatches(state, delayed.Source,
                            delayed.Ability.Condition)) continue;
                    ResolveOne(state, delayed.Source, delayed.OwnerPlayerId,
                        delayed.Ability, 1);
                }
                ResolveStateBasedActions(state);
            }
            finally
            {
                _abilityResolutionDepth--;
                state.CurrentEventCard = previousEvent;
            }
        }

        public void ExpireDelayedEffects(GameState state)
        {
            state?.DelayedAbilities.RemoveAll(effect => effect != null &&
                effect.Duration == AbilityDuration.UntilEndOfTurn);
        }

        public int CurrentPower(GameState state, CardInstance card, bool attacking = false)
        {
            if (card == null) return 0;
            var fallback = card.Definition.Power + card.TemporaryPower;
            if (attacking)
                fallback += card.Definition.KeywordValue(CardKeyword.PowerAttacker) +
                    card.TemporaryPowerAttacker;
            if (!_powerEvaluation.Add(card)) return fallback;
            try
            {
                var power = card.Definition.Power + card.TemporaryPower;
                if (attacking)
                    power += KeywordValue(state, card, CardKeyword.PowerAttacker) +
                        card.TemporaryPowerAttacker;
                foreach (var source in AllBattleZoneCards(state))
                {
                    foreach (var ability in RulesOf(state, source))
                    {
                        if (ability.Timing != AbilityTiming.Static) continue;
                        var modifiesPower = ability.Operation == AbilityOperation.ModifyPower ||
                            (attacking && ability.Operation == AbilityOperation.ModifyPowerWhileAttacking) ||
                            (ability.Operation == AbilityOperation.ModifyPowerWhileBattling &&
                             BattleOpponentMatches(state, card, ability.RelatedFilter));
                        if (!modifiesPower) continue;
                        if (!ConditionMatches(state, source, ability.Condition)) continue;
                        if (MatchesStaticTarget(state, source, card, ability.Selector))
                        {
                            power += ability.Value;
                            if (ability.ValuePerMatchingCard != 0 && ability.ValueSelector != null)
                                power += Candidates(state, source, ability.ValueSelector).Count() *
                                    ability.ValuePerMatchingCard;
                            if (ability.ValuePerDistinctCivilization != 0 && ability.ValueSelector != null)
                                power += Candidates(state, source, ability.ValueSelector)
                                    .SelectMany(candidate => candidate.Definition.Civilizations)
                                    .Distinct().Count() * ability.ValuePerDistinctCivilization;
                        }
                    }
                }
                return power;
            }
            finally
            {
                _powerEvaluation.Remove(card);
            }
        }

        public int KeywordValue(GameState state, CardInstance card, CardKeyword keyword)
        {
            if (card == null) return 0;
            var value = card.Definition.KeywordValue(keyword);
            foreach (var effect in card.TimedKeywords.Where(effect => effect.Keyword == keyword))
                value = Math.Max(value, effect.Value);
            if (IsSurvivor(state, card))
            {
                var ownerId = FindOwnerId(state, card);
                if (ownerId >= 0 && state.Players[ownerId].BattleZone.Contains(card))
                    foreach (var survivor in state.Players[ownerId].BattleZone.Where(other =>
                                 other != card && IsSurvivor(state, other)))
                        value = Math.Max(value, survivor.Definition.Abilities
                            .Where(ability => ability.IsSurvivorAbility &&
                                ability.Keyword == keyword)
                            .Select(ability => ability.Value).DefaultIfEmpty(0).Max());
            }
            foreach (var source in AllBattleZoneCards(state))
            {
                foreach (var ability in RulesOf(state, source))
                {
                    if (ability.Timing != AbilityTiming.Static || ability.Operation != AbilityOperation.GrantKeyword ||
                        ability.Keyword != keyword) continue;
                    if (!ConditionMatches(state, source, ability.Condition)) continue;
                    if (ability.RelatedFilter != null &&
                        !BattleOpponentMatches(state, card, ability.RelatedFilter)) continue;
                    if (MatchesStaticTarget(state, source, card, ability.Selector)) value = Math.Max(value, ability.Value);
                }
            }
            return value;
        }

        public int CurrentCost(GameState state, CardInstance card) =>
            CalculateCost(state, card, true);

        public int MaximumPayableCost(GameState state, CardInstance card) =>
            CalculateCost(state, card, false);

        private int CalculateCost(GameState state, CardInstance card, bool includeOptionalReductions)
        {
            if (card == null) return 0;
            var cost = card.Definition.Cost;
            var minimumCost = 0;
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0) return cost;
            foreach (var source in AllBattleZoneCards(state))
            {
                foreach (var ability in RulesOf(state, source))
                {
                    if (ability.Timing != AbilityTiming.Static || ability.Operation != AbilityOperation.ModifyCost)
                        continue;
                    if (ability.OptionalCostReduction && !includeOptionalReductions) continue;
                    if (!ConditionMatches(state, source, ability.Condition) ||
                        !ControllerAndFilterMatch(state, source, card, ability.Selector) ||
                        !MatchesFilter(state, card, source, ability.Selector?.Filter)) continue;
                    cost += ability.Value;
                    minimumCost = Math.Max(minimumCost, ability.MinimumValue);
                }
            }
            return Math.Max(minimumCost, Math.Max(0, cost));
        }

        private ReplacementOption ChooseDestructionReplacement(GameState state,
            CardInstance affectedCard, ISet<CardInstance> unavailableReplacementSources)
        {
            if (state == null || affectedCard == null) return null;
            var ownerId = FindOwnerId(state, affectedCard);
            if (ownerId < 0) return null;
            var options = new List<ReplacementOption>();

            var returnedByRaceEffect = state.DestroyedRacesReturnToHandThisTurn.Any(effect =>
                effect != null && effect.PlayerId == ownerId &&
                !string.IsNullOrWhiteSpace(effect.Race) && HasRace(affectedCard, effect.Race));
            if (returnedByRaceEffect)
            {
                options.Add(new ReplacementOption
                {
                    Source = affectedCard,
                    ControllerId = ownerId,
                    Destination = RuleZone.Hand,
                    Ability = new RuleAbilityDefinition(
                        "rules.replace_destroyed_race_to_hand",
                        AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                    {
                        Destination = RuleZone.Hand
                    }
                });
            }
            if (state.DestroyedCreaturesGoToManaThisTurn[ownerId])
            {
                options.Add(new ReplacementOption
                {
                    Source = affectedCard,
                    ControllerId = ownerId,
                    Destination = RuleZone.ManaZone,
                    DestinationTapped = true,
                    Ability = new RuleAbilityDefinition(
                        "rules.replace_destroyed_creature_to_mana",
                        AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                    {
                        Destination = RuleZone.ManaZone
                    }
                });
            }

            foreach (var candidateSource in AllBattleZoneCards(state))
            {
                var replacementOwnerId = FindOwnerId(state, candidateSource);
                if (replacementOwnerId < 0) continue;
                foreach (var ability in RulesOf(state, candidateSource))
                {
                    if (!ConditionMatches(state, candidateSource, ability.Condition) ||
                        !MatchesStaticTarget(state, candidateSource, affectedCard,
                            ability.Selector)) continue;
                    if (ability.Timing == AbilityTiming.Static &&
                        ability.Operation == AbilityOperation.PreventDestruction)
                    {
                        options.Add(new ReplacementOption
                        {
                            Source = candidateSource,
                            ControllerId = replacementOwnerId,
                            Ability = ability,
                            Prevented = true
                        });
                        continue;
                    }
                    if (ability.Timing != AbilityTiming.ReplacementDestroy ||
                        ability.RequiresPreviousSuccess) continue;
                    if (ability.Operation == AbilityOperation.DestroySourceInstead)
                    {
                        if (unavailableReplacementSources != null &&
                            unavailableReplacementSources.Contains(candidateSource)) continue;
                        options.Add(new ReplacementOption
                        {
                            Source = candidateSource,
                            ControllerId = replacementOwnerId,
                            Ability = ability
                        });
                        continue;
                    }
                    if (ability.Operation != AbilityOperation.Move ||
                        ability.Destination == RuleZone.None) continue;
                    options.Add(new ReplacementOption
                    {
                        Source = candidateSource,
                        ControllerId = replacementOwnerId,
                        Ability = ability,
                        Destination = ability.Destination,
                        DestinationTapped = ability.Destination == RuleZone.ManaZone
                    });
                }
            }

            var chosen = ChooseReplacementOption(state, affectedCard, options);
            if (chosen?.Ability.Operation == AbilityOperation.DestroySourceInstead)
                unavailableReplacementSources?.Add(chosen.Source);
            return chosen;
        }

        private ReplacementOption ChooseReplacementOption(GameState state,
            CardInstance affectedCard, IReadOnlyList<ReplacementOption> options)
        {
            if (options == null || options.Count == 0) return null;
            var activeOptions = options.Where(option =>
                option.ControllerId == state.ActivePlayerId).ToList();
            var chosen = ChooseReplacementOptionForPlayer(state, affectedCard,
                state.ActivePlayerId, activeOptions);
            if (chosen != null) return chosen;

            var nonActiveId = 1 - state.ActivePlayerId;
            return ChooseReplacementOptionForPlayer(state, affectedCard, nonActiveId,
                options.Where(option => option.ControllerId == nonActiveId).ToList());
        }

        private ReplacementOption ChooseReplacementOptionForPlayer(GameState state,
            CardInstance affectedCard, int playerId, List<ReplacementOption> options)
        {
            while (options.Count > 0)
            {
                var sources = options.Select(option => option.Source).ToArray();
                var abilities = options.Select(option => option.Ability).ToArray();
                var selectedIndex = (_decisions as IAbilityReplacementDecisionProvider)?
                    .ChooseReplacement(state, playerId, affectedCard, sources, abilities) ?? 0;
                if (selectedIndex < 0 || selectedIndex >= options.Count) selectedIndex = 0;
                var selected = options[selectedIndex];
                if (selected.Ability.Optional && !_decisions.ChooseOptional(state, playerId,
                        selected.Source, selected.Ability))
                {
                    options.RemoveAt(selectedIndex);
                    continue;
                }
                return selected;
            }
            return null;
        }

        public bool HasKeyword(GameState state, CardInstance card, CardKeyword keyword)
        {
            if (card == null) return false;
            if (keyword == CardKeyword.Unblockable &&
                (card.HasKeyword(CardKeyword.IgnoreAttackRestrictionsAgainstPlayer) ||
                 card.TimedKeywords.Any(effect =>
                     effect.Keyword == CardKeyword.IgnoreAttackRestrictionsAgainstPlayer))) return true;
            if (keyword == CardKeyword.Unblockable && card.Definition.HasStealthCivilization &&
                HasKeyword(state, card, CardKeyword.Stealth))
            {
                var ownerId = FindOwnerId(state, card);
                if (ownerId >= 0 && state.Players[1 - ownerId].Mana.Any(mana =>
                        mana.Definition.Civilizations.Contains(card.Definition.StealthCivilization)))
                    return true;
            }
            if (card.HasKeyword(keyword)) return true;
            if (card.TimedKeywords.Any(effect => effect.Keyword == keyword)) return true;
            if (keyword == CardKeyword.Survivor && IsSurvivor(state, card)) return true;
            if (keyword != CardKeyword.Survivor && IsSurvivor(state, card))
            {
                var ownerId = FindOwnerId(state, card);
                if (ownerId >= 0 && state.Players[ownerId].BattleZone.Contains(card) &&
                    state.Players[ownerId].BattleZone.Any(other =>
                        other != card && IsSurvivor(state, other) &&
                        other.Definition.Abilities.Any(ability =>
                            ability.IsSurvivorAbility &&
                            ability.Keyword == keyword))) return true;
            }
            foreach (var source in AllBattleZoneCards(state))
            {
                foreach (var ability in RulesOf(state, source))
                {
                    if (ability.Timing != AbilityTiming.Static || ability.Operation != AbilityOperation.GrantKeyword ||
                        ability.Keyword != keyword) continue;
                    if (!ConditionMatches(state, source, ability.Condition)) continue;
                    if (ability.RelatedFilter != null &&
                        !BattleOpponentMatches(state, card, ability.RelatedFilter)) continue;
                    if (MatchesStaticTarget(state, source, card, ability.Selector)) return true;
                }
            }
            return false;
        }

        private int ResolveOne(GameState state, CardInstance source, int ownerId,
            RuleAbilityDefinition ability, int previousCount)
        {
            var owner = state.Players[ownerId];
            var opponent = state.Players[1 - ownerId];
            var amount = ability.UsePreviousSelectionCountAsAmount ? previousCount : ability.Amount;
            if (ability.AmountPerMatchingCard != 0 && ability.AmountSelector != null)
                amount += Candidates(state, source, ability.AmountSelector).Count() * ability.AmountPerMatchingCard;
            if (ability.AmountPerShieldBrokenByEvent != 0 && state.CurrentEventCard != null)
                amount += state.CurrentEventCard.ShieldsBrokenInLastAttack * ability.AmountPerShieldBrokenByEvent;
            if (ability.AmountPerShieldBrokenThisTurn != 0)
                amount += state.ShieldsBrokenThisTurn[ownerId] * ability.AmountPerShieldBrokenThisTurn;
            switch (ability.Operation)
            {
                case AbilityOperation.Draw:
                {
                    var drawAmount = ability.ChooseAmount
                        ? ChooseNumber(state, ownerId, source, ability, 0,
                            Math.Max(0, amount), Math.Max(0, amount))
                        : amount;
                    Draw(state, owner, opponent.PlayerId, drawAmount);
                    return drawAmount;
                }
                case AbilityOperation.Shuffle:
                    Shuffle(owner.Deck, state, ownerId);
                    return 1;
                case AbilityOperation.ExtraTurn:
                    state.ExtraTurnQueue.Add(ownerId);
                    return 1;
                case AbilityOperation.LoseGame:
                    state.WinnerPlayerId = opponent.PlayerId;
                    state.WinReason = source.NameOrFallback() + " effect";
                    state.Phase = GamePhase.Finished;
                    return 1;
                case AbilityOperation.GrantBlockedCreatureSlayerThisTurn:
                    state.BlockedCreatureGetsSlayerThisTurn[ownerId] = true;
                    return 1;
                case AbilityOperation.IgnoreAttackRestrictionsThisTurn:
                    state.IgnoreAttackRestrictionsThisTurn[ownerId] = true;
                    return 1;
                case AbilityOperation.ReplaceOwnDestructionWithManaThisTurn:
                    state.DestroyedCreaturesGoToManaThisTurn[ownerId] = true;
                    return 1;
                case AbilityOperation.BlockedAttackerBreaksShieldThisTurn:
                    if (ability.Selector?.Filter?.Civilizations == null ||
                        ability.Selector.Filter.Civilizations.Length == 0) return 0;
                    state.BlockedAttackerBreaksShieldCivilizationThisTurn[ownerId] =
                        ability.Selector.Filter.Civilizations[0];
                    return 1;
                case AbilityOperation.TapOneCivilizationGroup:
                {
                    var groupCandidates = Candidates(state, source, ability.Selector).ToArray();
                    var allowed = ability.Selector.Filter.Civilizations ?? Array.Empty<Civilization>();
                    if (allowed.Length == 0) return 0;
                    var chosenCivilization = (_decisions as IAbilityCivilizationDecisionProvider)?
                        .ChooseCivilization(state, ownerId, source, ability, allowed) ?? allowed[0];
                    if (!allowed.Contains(chosenCivilization)) chosenCivilization = allowed[0];
                    var tapped = 0;
                    foreach (var card in groupCandidates.Where(card =>
                                 card.Definition.Civilizations.Contains(chosenCivilization)))
                    {
                        card.Tapped = true;
                        tapped++;
                    }
                    return Math.Max(1, tapped);
                }
                case AbilityOperation.DestroyLowestPower:
                {
                    var lowestCandidates = Candidates(state, source, ability.Selector).ToArray();
                    if (lowestCandidates.Length == 0) return 0;
                    var minimumPower = lowestCandidates.Min(card => CurrentPower(state, card));
                    var tied = lowestCandidates.Where(card => CurrentPower(state, card) == minimumPower)
                        .ToArray();
                    var chosen = ChooseCardsSafely(state, ownerId, source, ability, tied, 1, 1)
                        .FirstOrDefault();
                    if (chosen == null) return 0;
                    state.LastRuleSelection.Clear();
                    state.LastRuleSelection.Add(chosen);
                    return DestroyCard(state, chosen) ? 1 : 0;
                }
                case AbilityOperation.ChooseRace:
                {
                    var exclusions = ability.ChoiceExclusions ?? Array.Empty<string>();
                    var races = state.Players.SelectMany(AllOwnedCards)
                        .SelectMany(EffectiveRaces)
                        .Concat(TcgCatalog.Printings.SelectMany(definition => definition.Races))
                        .Where(race => !string.IsNullOrWhiteSpace(race) &&
                            !exclusions.Any(excluded => race.Equals(
                                excluded, StringComparison.OrdinalIgnoreCase)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(race => race, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var choice = (_decisions as IAbilityRaceDecisionProvider)?.ChooseRace(
                        state, ownerId, source, ability, races) ?? races.FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(choice) || !races.Any(race =>
                            race.Equals(choice, StringComparison.OrdinalIgnoreCase))) return 0;
                    source.ChosenRace = races.First(race =>
                        race.Equals(choice, StringComparison.OrdinalIgnoreCase));
                    return 1;
                }
                case AbilityOperation.PreventCivilizationAttackPlayer:
                {
                    var civilizations = ((Civilization[])Enum.GetValues(typeof(Civilization)))
                        .OrderBy(value => value).ToArray();
                    var choice = (_decisions as IAbilityCivilizationDecisionProvider)?
                        .ChooseCivilization(state, ownerId, source, ability, civilizations)
                        ?? civilizations[0];
                    if (!civilizations.Contains(choice)) return 0;
                    source.ChosenCivilization = choice;
                    source.HasChosenCivilization = true;
                    if (!state.PreventOpponentAttackCivilizationsUntilNextTurn.Any(effect =>
                            effect != null && effect.PlayerId == ownerId &&
                            effect.Civilization == choice))
                        state.PreventOpponentAttackCivilizationsUntilNextTurn.Add(
                            new PlayerCivilizationEffect
                            {
                                PlayerId = ownerId,
                                Civilization = choice
                            });
                    return 1;
                }
                case AbilityOperation.EachPlayerDiscardChosenCost:
                {
                    var allHandCards = state.Players.SelectMany(player => player.Hand).ToArray();
                    state.InspectionPlayerId = ownerId;
                    state.InspectedCards.Clear();
                    state.InspectedCards.AddRange(allHandCards);
                    var maximum = Math.Max(20, allHandCards.Select(card => card.Definition.Cost)
                        .DefaultIfEmpty(0).Max());
                    var suggested = Enumerable.Range(0, maximum + 1)
                        .OrderByDescending(cost => opponent.Hand.Count(card => card.Definition.Cost == cost))
                        .ThenBy(cost => owner.Hand.Count(card => card.Definition.Cost == cost))
                        .ThenBy(cost => cost)
                        .First();
                    var chosenCost = ChooseNumber(state, ownerId, source, ability,
                        0, maximum, suggested);
                    var discarded = 0;
                    foreach (var card in allHandCards.Where(card =>
                                 card.Definition.Cost == chosenCost))
                    {
                        DiscardCard(state, card, ownerId);
                        discarded++;
                    }
                    return Math.Max(1, discarded);
                }
                case AbilityOperation.TapManaPairsToUntapCreatures:
                {
                    var targetPlayerId = ability.Selector?.Controller == RuleController.Opponent
                        ? 1 - ownerId : ownerId;
                    var targetPlayer = state.Players[targetPlayerId];
                    var availableMana = targetPlayer.Mana.Where(card => !card.Tapped).ToArray();
                    var mana = ChooseCardsSafely(state, targetPlayerId, source, ability,
                        availableMana, 0, availableMana.Length);
                    foreach (var card in mana) card.Tapped = true;
                    var maximumCreatures = mana.Length / 2;
                    var tappedCreatures = targetPlayer.BattleZone.Where(card =>
                        card.Definition.IsCreature && card.Tapped).ToArray();
                    var creatures = ChooseCardsSafely(state, targetPlayerId, source, ability,
                        tappedCreatures, 0, Math.Min(maximumCreatures, tappedCreatures.Length));
                    foreach (var creature in creatures) creature.Tapped = false;
                    return mana.Length + creatures.Length;
                }
                case AbilityOperation.EachPlayerDrawPerMatchingCard:
                {
                    var drawn = 0;
                    foreach (var player in PlayersInActiveOrder(state))
                    {
                        if (state.IsFinished) break;
                        var selector = ability.AmountSelector ?? new RuleSelectorDefinition
                        {
                            Zone = RuleZone.BattleZone,
                            Filter = new CardFilterDefinition { CreaturesOnly = true }
                        };
                        var count = Zone(player, selector.Zone).Count(card =>
                            MatchesFilter(state, card, source, selector.Filter));
                        Draw(state, player, 1 - player.PlayerId, count);
                        drawn += count;
                    }
                    return Math.Max(1, drawn);
                }
                case AbilityOperation.EachPlayerMayDraw:
                {
                    var drawn = 0;
                    foreach (var player in PlayersInActiveOrder(state))
                    {
                        if (state.IsFinished) break;
                        if (!_decisions.ChooseOptional(state, player.PlayerId, source, ability))
                            continue;
                        Draw(state, player, 1 - player.PlayerId, 1);
                        drawn++;
                    }
                    return Math.Max(1, drawn);
                }
                case AbilityOperation.OpponentDraw:
                    Draw(state, opponent, ownerId, amount);
                    return amount;
                case AbilityOperation.InspectOpponentHandAndTopDeck:
                {
                    state.InspectionPlayerId = ownerId;
                    state.InspectedCards.Clear();
                    state.InspectedCards.AddRange(opponent.Hand);
                    if (opponent.Deck.Count > 0)
                        state.InspectedCards.Add(opponent.Deck[opponent.Deck.Count - 1]);
                    return 1;
                }
                case AbilityOperation.DestroyAllChosenPower:
                {
                    var creatures = AllBattleZoneCards(state).ToArray();
                    var suggested = creatures.Select(card => CurrentPower(state, card))
                        .Where(power => power <= 6000)
                        .GroupBy(power => power)
                        .OrderByDescending(group => group.Count())
                        .ThenBy(group => group.Key)
                        .Select(group => group.Key)
                        .FirstOrDefault();
                    var chosenPower = ChooseNumber(state, ownerId, source, ability,
                        0, 6000, suggested);
                    var destroyed = creatures.Where(card =>
                        CurrentPower(state, card) == chosenPower).ToArray();
                    var destroyedCount = DestroyCards(state, destroyed);
                    return Math.Max(1, destroyedCount);
                }
                case AbilityOperation.SearchPerOpponentCreatureLead:
                {
                    var lead = Math.Max(0, opponent.BattleZone.Count(card => card.Definition.IsCreature) -
                        owner.BattleZone.Count(card => card.Definition.IsCreature));
                    var candidates = owner.Deck.Where(card => card.Definition.IsCreature).ToArray();
                    var chosen = ChooseCardsSafely(state, ownerId, source, ability,
                        candidates, 0, Math.Min(lead, candidates.Length));
                    state.InspectionPlayerId = opponent.PlayerId;
                    state.InspectedCards.Clear();
                    state.InspectedCards.AddRange(chosen);
                    foreach (var selectedCard in chosen)
                    {
                        MoveCard(state, selectedCard, RuleZone.Hand, false, true, ownerId);
                        if (state.IsFinished) break;
                    }
                    if (state.IsFinished) return Math.Max(1, chosen.Length);
                    Shuffle(owner.Deck, state, ownerId);
                    return Math.Max(1, chosen.Length);
                }
                case AbilityOperation.SearchSameNameAsEventToBattle:
                {
                    var eventCard = state.CurrentEventCard;
                    if (eventCard == null || !eventCard.Definition.IsCreature) return 0;
                    var candidates = owner.Deck.Where(card => card.Definition.IsCreature &&
                        card.Definition.Name.Equals(eventCard.Definition.Name,
                            StringComparison.OrdinalIgnoreCase)).ToArray();
                    var chosen = ChooseCardsSafely(state, ownerId, source, ability,
                        candidates, 0, Math.Min(1, candidates.Length));
                    foreach (var selectedCard in chosen)
                        MoveCard(state, selectedCard, RuleZone.BattleZone, false, true, ownerId);
                    if (state.IsFinished) return Math.Max(1, chosen.Length);
                    Shuffle(owner.Deck, state, ownerId);
                    return Math.Max(1, chosen.Length);
                }
                case AbilityOperation.SearchPlayerDeckToGraveyard:
                {
                    var availablePlayers = Enumerable.Range(0,
                        state.Players.Length).ToArray();
                    var selectedPlayerId = (_decisions as
                        IAbilityPlayerDecisionProvider)?.ChoosePlayer(state,
                        ownerId, source, ability, availablePlayers) ?? ownerId;
                    if (!availablePlayers.Contains(selectedPlayerId))
                        selectedPlayerId = ownerId;
                    var selectedPlayer = state.Players[selectedPlayerId];
                    var chosen = ChooseCardsSafely(state, ownerId, source,
                        ability, selectedPlayer.Deck.ToArray(), 0,
                        Math.Min(1, selectedPlayer.Deck.Count));
                    foreach (var selectedCard in chosen)
                        MoveCard(state, selectedCard, RuleZone.Graveyard,
                            false, true, ownerId);
                    if (!state.IsFinished)
                        Shuffle(selectedPlayer.Deck, state, selectedPlayerId);
                    return Math.Max(1, chosen.Count);
                }
                case AbilityOperation.ReduceOpponentShieldsToOwnCount:
                {
                    var keepCount = Math.Min(owner.Shields.Count, opponent.Shields.Count);
                    var shields = opponent.Shields.ToArray();
                    var kept = ChooseCardsSafely(state, opponent.PlayerId, source, ability,
                        shields, keepCount, keepCount);
                    var moved = 0;
                    foreach (var shield in shields.Where(card => !kept.Contains(card)))
                    {
                        MoveCard(state, shield, RuleZone.Hand, false, true, ownerId);
                        if (HasKeyword(state, shield, CardKeyword.ShieldTrigger) &&
                            CanUseShieldTrigger(state, shield) &&
                            !state.PendingShieldTriggers.Contains(shield))
                            state.PendingShieldTriggers.Add(shield);
                        moved++;
                    }
                    return Math.Max(1, moved);
                }
                case AbilityOperation.ReplaceDestroyedRaceWithHandThisTurn:
                    if (string.IsNullOrWhiteSpace(source.ChosenRace)) return 0;
                    if (!state.DestroyedRacesReturnToHandThisTurn.Any(effect =>
                            effect != null && effect.PlayerId == ownerId &&
                            effect.Race.Equals(source.ChosenRace,
                                StringComparison.OrdinalIgnoreCase)))
                        state.DestroyedRacesReturnToHandThisTurn.Add(new PlayerRaceEffect
                        {
                            PlayerId = ownerId,
                            Race = source.ChosenRace
                        });
                    return 1;
                case AbilityOperation.TakeOneTopRestBottom:
                {
                    var maximum = ability.Selector?.Maximum > 0 ? ability.Selector.Maximum : 1;
                    var topCards = Candidates(state, source, ability.Selector)
                        .TakeLast(maximum).Reverse().ToArray();
                    if (topCards.Length == 0) return 0;
                    state.InspectionPlayerId = ownerId;
                    state.InspectedCards.Clear();
                    state.InspectedCards.AddRange(topCards);
                    var eligible = ability.PartitionFilter == null
                        ? topCards
                        : topCards.Where(card => MatchesFilter(state, card, source,
                            ability.PartitionFilter)).ToArray();
                    var chosen = ChooseCardsSafely(state, ownerId, source, ability,
                        eligible, eligible.Length == 0 ? 0 : 1, 1).FirstOrDefault();
                    foreach (var revealed in topCards) owner.Deck.Remove(revealed);
                    if (chosen != null) owner.Hand.Add(chosen);
                    var remaining = topCards.Where(card => card != chosen).ToArray();
                    var ordered = ChooseCardsSafely(state, ownerId, source, ability,
                        remaining, remaining.Length, remaining.Length);
                    owner.Deck.InsertRange(0, ordered.Reverse());
                    if (owner.Deck.Count == 0)
                        FinishDeckOut(state, ownerId);
                    state.LastRuleSelection.Clear();
                    if (chosen != null) state.LastRuleSelection.Add(chosen);
                    return Math.Max(1, chosen == null ? 0 : 1);
                }
                case AbilityOperation.SwapHandAndMana:
                {
                    var players = ability.Selector?.Controller == RuleController.EachPlayer
                        ? PlayersInActiveOrder(state)
                        : new[] { owner };
                    foreach (var player in players)
                    {
                        var oldHand = player.Hand.ToArray();
                        var oldMana = player.Mana.ToArray();
                        player.Hand.Clear();
                        player.Mana.Clear();
                        foreach (var card in oldMana)
                        {
                            card.Tapped = false;
                            player.Hand.Add(card);
                        }
                        foreach (var card in oldHand)
                        {
                            card.Tapped = true;
                            player.Mana.Add(card);
                        }
                    }
                    return 1;
                }
                case AbilityOperation.SetForcedLossAfterExtraTurn:
                    state.ForcedLossOwnTurnEndsRemaining[ownerId] = 2;
                    return 1;
                case AbilityOperation.TapAllExceptOnePerPlayer:
                {
                    var kept = 0;
                    foreach (var player in PlayersInActiveOrder(state))
                    {
                        if (state.IsFinished) break;
                        var creatures = player.BattleZone.Where(card => card.Definition.IsCreature)
                            .ToArray();
                        var chosen = ChooseCardsSafely(state, player.PlayerId, source,
                            ability, creatures, creatures.Length == 0 ? 0 : 1, 1).FirstOrDefault();
                        foreach (var creature in creatures.Where(card => card != chosen))
                            creature.Tapped = true;
                        if (chosen != null) kept++;
                    }
                    return kept;
                }
                case AbilityOperation.RequireCondition:
                    return 1;
                case AbilityOperation.PreventBattle:
                    state.PreventPendingBattle = true;
                    return 1;
                case AbilityOperation.ScheduleAbility:
                    if (ability.GrantedAbility == null) return 0;
                    state.DelayedAbilities.Add(new DelayedAbilityEffect
                    {
                        Source = source,
                        Ability = ability.GrantedAbility,
                        OwnerPlayerId = ownerId,
                        Duration = ability.Duration
                    });
                    return 1;
                case AbilityOperation.ForceOpponentCreaturesAttackNextTurn:
                    state.ForceAllCreaturesAttackNextTurn[opponent.PlayerId] = true;
                    return 1;
                case AbilityOperation.RefreshHand:
                    foreach (var player in PlayersInActiveOrder(state))
                    {
                        var handCount = player.Hand.Count;
                        player.Deck.AddRange(player.Hand);
                        player.Hand.Clear();
                        Shuffle(player.Deck, state, player.PlayerId);
                        Draw(state, player, 1 - player.PlayerId, handCount);
                    }
                    return 1;
                case AbilityOperation.RefreshOwnHand:
                {
                    var ownHandCount = owner.Hand.Count;
                    owner.Deck.AddRange(owner.Hand);
                    owner.Hand.Clear();
                    Shuffle(owner.Deck, state, ownerId);
                    Draw(state, owner, opponent.PlayerId, ownHandCount);
                    return 1;
                }
            }

            if (ability.Selector != null && ability.Selector.Controller == RuleController.EachPlayer)
            {
                var total = 0;
                foreach (var player in PlayersInActiveOrder(state))
                {
                    if (state.IsFinished) break;
                    var perPlayerCandidates = Zone(player, ability.Selector.Zone)
                        .Where(card => MatchesFilter(state, card, source, ability.Selector.Filter)).ToList();
                    var perPlayerSelection = Select(state, source, player.PlayerId, ability,
                        perPlayerCandidates, ability.SelectionCountUsesAmount ? amount : (int?)null,
                        ability.SelectionMaximumUsesAmount ? amount : (int?)null);
                    // The controller of the resolving spell or ability remains
                    // the cause even when "each player" makes their own choice
                    // and physically moves the chosen card. This distinction is
                    // used by effects such as Snork La's opponent-caused mana
                    // movement replacement.
                    total += ApplySelected(state, source, ownerId, ability,
                        perPlayerSelection, player);
                }
                return total;
            }

            var candidates = Candidates(state, source, ability.Selector).ToList();
            if (ability.Operation == AbilityOperation.Tap)
                candidates = candidates.Where(card => CanBeTappedBy(state, card, ownerId)).ToList();
            var chooserId = ability.Chooser == RuleController.Opponent ? 1 - ownerId : ownerId;
            var selectionMaximum = ability.SelectionMaximumUsesAmount ? amount : (int?)null;
            if (ability.ChooseSelectionAmount && selectionMaximum.HasValue)
                selectionMaximum = ChooseNumber(state, chooserId, source, ability,
                    Math.Min(ability.Selector?.Minimum ?? 0, selectionMaximum.Value),
                    selectionMaximum.Value, selectionMaximum.Value);
            if (ability.Operation == AbilityOperation.DestroyOrSource && candidates.Count < ability.Amount)
            {
                return DestroyCard(state, source) ? 1 : 0;
            }
            var selected = Select(state, source, chooserId, ability, candidates,
                ability.SelectionCountUsesAmount ? amount : (int?)null,
                selectionMaximum);
            var selectsShields = ability.Selector != null &&
                (ability.Selector.Zone == RuleZone.ShieldZone ||
                 (ability.Selector.Zones != null &&
                  ability.Selector.Zones.Contains(RuleZone.ShieldZone)));
            if (selectsShields && selected.Count > 0)
            {
                var shieldOwnerId = FindOwnerId(state, selected[0]);
                if (shieldOwnerId >= 0 && ShieldOwnerChooses(state, chooserId, shieldOwnerId))
                {
                    var ownerCandidates = candidates.Where(card =>
                        FindOwnerId(state, card) == shieldOwnerId).ToList();
                    selected = Select(state, source, shieldOwnerId, ability, ownerCandidates,
                        ability.SelectionCountUsesAmount ? amount : (int?)null,
                        selectionMaximum);
                }
            }
            var applied = ApplySelected(state, source, ownerId, ability, selected, owner);
            return applied > 0 ? applied : ability.Selector != null && ability.Selector.Minimum == 0 && !ability.Optional
                ? 1 : 0;
        }

        private int ApplySelected(GameState state, CardInstance source, int ownerId,
            RuleAbilityDefinition ability, IReadOnlyList<CardInstance> selected, PlayerState shuffleOwner)
        {
            state.LastRuleSelection.Clear();
            state.LastRuleSelection.AddRange(selected);
            var appliedCount = selected.Count;
            var value = ability.Value;
            if (ability.ValuePerMatchingCard != 0 && ability.ValueSelector != null)
                value += Candidates(state, source, ability.ValueSelector).Count() * ability.ValuePerMatchingCard;
            if (ability.ValuePerDistinctCivilization != 0 && ability.ValueSelector != null)
                value += Candidates(state, source, ability.ValueSelector)
                    .SelectMany(candidate => candidate.Definition.Civilizations)
                    .Distinct().Count() * ability.ValuePerDistinctCivilization;
            switch (ability.Operation)
            {
                case AbilityOperation.Discard:
                    foreach (var card in selected) DiscardCard(state, card, ownerId);
                    break;
                case AbilityOperation.Destroy:
                case AbilityOperation.DestroyOrSource:
                    appliedCount = ApplyDestructionEvent(state, selected).Count(outcome =>
                        outcome == DestructionOutcome.Destroyed);
                    break;
                case AbilityOperation.DestroyIfCombinedPowerBelowSource:
                    appliedCount = 0;
                    if (selected.Sum(card => CurrentPower(state, card)) < CurrentPower(state, source))
                        appliedCount = ApplyDestructionEvent(state, selected).Count(outcome =>
                            outcome == DestructionOutcome.Destroyed);
                    break;
                case AbilityOperation.SplitPairOpponentChoice:
                {
                    var opponentId = 1 - ownerId;
                    var primary = ChooseCardsSafely(state, opponentId, source, ability,
                        selected, selected.Count == 0 ? 0 : 1, 1);
                    foreach (var card in primary)
                        MoveCard(state, card, ability.Destination, false, true, ownerId);
                    foreach (var card in selected.Where(card => !primary.Contains(card)).ToArray())
                    {
                        if (ability.SecondaryIsDestruction) DestroyCard(state, card);
                        else MoveCard(state, card, ability.SecondaryDestination,
                            ability.SecondaryDestination == RuleZone.ManaZone, true, ownerId);
                    }
                    break;
                }
                case AbilityOperation.Tap:
                    foreach (var card in selected) card.Tapped = true;
                    break;
                case AbilityOperation.Untap:
                    foreach (var card in selected) card.Tapped = false;
                    break;
                case AbilityOperation.Move:
                case AbilityOperation.Search:
                case AbilityOperation.AddShield:
                    appliedCount = 0;
                    if (!string.IsNullOrWhiteSpace(ability.RulesText) &&
                        ability.RulesText.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        ability.RulesText.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        state.InspectionPlayerId = 1 - ownerId;
                        state.InspectedCards.Clear();
                        state.InspectedCards.AddRange(selected);
                    }
                    foreach (var card in selected)
                    {
                        if (MoveCard(state, card, ability.Destination,
                                ability.DestinationTapped, true, ownerId))
                            appliedCount++;
                        if (state.IsFinished) break;
                    }
                    if (state.IsFinished) break;
                    if (ability.ShuffleAfter)
                    {
                        var deckOwnerId = selected.Count > 0 ? FindOwnerId(state, selected[0]) :
                            ability.Selector != null && ability.Selector.Controller == RuleController.Opponent
                                ? 1 - ownerId : shuffleOwner.PlayerId;
                        if (deckOwnerId >= 0)
                            Shuffle(state.Players[deckOwnerId].Deck, state, deckOwnerId);
                    }
                    break;
                case AbilityOperation.ModifyPower:
                    foreach (var card in selected) card.TemporaryPower += value;
                    break;
                case AbilityOperation.ModifyPowerWhileAttacking:
                    foreach (var card in selected) card.TemporaryPowerAttacker += value;
                    break;
                case AbilityOperation.GrantKeyword:
                    foreach (var card in selected)
                    {
                        if (ability.Duration == AbilityDuration.UntilStartOfOwnersNextTurn ||
                            ability.Duration == AbilityDuration.UntilEndOfTurn)
                        {
                            card.TimedKeywords.Add(new TimedKeywordEffect
                            {
                                Keyword = ability.Keyword,
                                Value = ability.Value,
                                Duration = ability.Duration,
                                AppliedByPlayerId = ability.Duration == AbilityDuration.UntilEndOfTurn
                                    ? state.ActivePlayerId : ownerId
                            });
                        }
                        else if (!card.TemporaryKeywords.Contains(ability.Keyword))
                        {
                            card.TemporaryKeywords.Add(ability.Keyword);
                        }
                    }
                    break;
                case AbilityOperation.GrantAbility:
                    foreach (var card in selected)
                        if (ability.GrantedAbility != null)
                            card.TimedRuleAbilities.Add(new TimedRuleEffect
                            {
                                Ability = ability.GrantedAbility,
                                Duration = ability.Duration,
                                AppliedByPlayerId = ability.Duration == AbilityDuration.UntilEndOfTurn
                                    ? state.ActivePlayerId : ownerId
                            });
                    break;
                case AbilityOperation.RemoveKeyword:
                    foreach (var card in selected) card.TemporaryKeywords.Remove(ability.Keyword);
                    break;
                case AbilityOperation.SetBreakerCount:
                    foreach (var card in selected) card.TemporaryBreakerCount = ability.Value;
                    break;
                case AbilityOperation.BreakShield:
                {
                    var brokenShields = 0;
                    foreach (var card in selected)
                    {
                        var defendingPlayerId = FindOwnerId(state, card);
                        if (defendingPlayerId < 0) continue;
                        var replacement = ResolveShieldBreakReplacement(state, source,
                            card, defendingPlayerId);
                        if (replacement == ShieldBreakReplacementResult.Prevented) continue;
                        if (replacement ==
                            ShieldBreakReplacementResult.AttackerCastsShieldTrigger)
                        {
                            ResolveStolenShield(state, source, card, defendingPlayerId);
                        }
                        else
                        {
                            var toGraveyard = replacement ==
                                ShieldBreakReplacementResult.MoveToGraveyard;
                            MoveCard(state, card,
                                toGraveyard ? RuleZone.Graveyard : RuleZone.Hand,
                                false, true, ownerId);
                            if (!toGraveyard && HasKeyword(state, card,
                                    CardKeyword.ShieldTrigger) && CanUseShieldTrigger(state, card))
                                state.PendingShieldTriggers.Add(card);
                        }
                        card.BrokenByAttackerInstanceId = source.InstanceId;
                        brokenShields++;
                    }
                    state.ShieldsBrokenThisTurn[ownerId] += brokenShields;
                    source.BrokeShieldThisTurn |= brokenShields > 0;
                    source.ShieldsBrokenInLastAttack += brokenShields;
                    break;
                }
                case AbilityOperation.MoveShieldToHandAllowTrigger:
                    foreach (var card in selected)
                    {
                        MoveCard(state, card, RuleZone.Hand, false, true, ownerId);
                        if (HasKeyword(state, card, CardKeyword.ShieldTrigger) &&
                            CanUseShieldTrigger(state, card) &&
                            !state.PendingShieldTriggers.Contains(card))
                            state.PendingShieldTriggers.Add(card);
                    }
                    break;
                case AbilityOperation.LookAtCards:
                case AbilityOperation.RevealCards:
                    state.InspectionPlayerId = ownerId;
                    foreach (var card in selected)
                        if (!state.InspectedCards.Contains(card)) state.InspectedCards.Add(card);
                    if (ability.Operation == AbilityOperation.LookAtCards &&
                        ability.ReorderSelectedDeckCards && selected.Count > 1)
                    {
                        var deckOwnerId = FindOwnerId(state, selected[0]);
                        if (deckOwnerId >= 0)
                        {
                            var deck = state.Players[deckOwnerId].Deck;
                            var ordered = ChooseCardsSafely(state, ownerId, source, ability,
                                selected, selected.Count, selected.Count);
                            foreach (var card in selected) deck.Remove(card);
                            deck.AddRange(ordered.Reverse());
                        }
                    }
                    break;
                case AbilityOperation.RevealAndPartition:
                    state.InspectionPlayerId = ownerId;
                    foreach (var card in selected)
                    {
                        if (!state.InspectedCards.Contains(card)) state.InspectedCards.Add(card);
                        var destination = MatchesFilter(state, card, source, ability.PartitionFilter)
                            ? ability.Destination
                            : ability.SecondaryDestination;
                        MoveCard(state, card, destination, false);
                        if (state.IsFinished) break;
                    }
                    break;
                case AbilityOperation.MoveEvolutionTop:
                    foreach (var card in selected) MoveEvolutionTop(state, card, ability.Destination);
                    break;
                case AbilityOperation.SetRace:
                    foreach (var card in selected)
                        if (!string.IsNullOrWhiteSpace(ability.TextValue) &&
                            !card.TemporaryRaces.Any(race => race.Equals(
                                ability.TextValue, StringComparison.OrdinalIgnoreCase)))
                            card.TemporaryRaces.Add(ability.TextValue);
                    break;
                case AbilityOperation.RequireChosenBlocker:
                    state.RequiredBlockerInstanceId = selected.Count > 0
                        ? selected[0].InstanceId : 0;
                    break;
                case AbilityOperation.TurnFaceUp:
                    foreach (var card in selected) card.FaceUp = true;
                    break;
                case AbilityOperation.InspectShieldAndCastSpell:
                    foreach (var shield in selected.Take(1))
                    {
                        state.InspectionPlayerId = ownerId;
                        state.InspectedCards.Clear();
                        state.InspectedCards.Add(shield);
                        if (shield.Definition.Type != CardType.Spell ||
                            !_decisions.ChooseOptional(state, ownerId, source, ability)) break;
                        owner.Shields.Remove(shield);
                        owner.Resolving.Add(shield);
                        state.SpellCastThisTurn[ownerId] = true;
                        ResolveSpellCast(state, shield);
                        owner.Resolving.Remove(shield);
                        shield.Tapped = shield.Definition.Civilizations.Length > 1;
                        if (HasKeyword(state, shield, CardKeyword.Charger)) owner.Mana.Add(shield);
                        else owner.Graveyard.Add(shield);
                    }
                    break;
                case AbilityOperation.ScheduleSelectedUntapAfterBattle:
                    foreach (var card in selected)
                        if (!state.UntapAfterCurrentBattle.Contains(card))
                            state.UntapAfterCurrentBattle.Add(card);
                    break;
                case AbilityOperation.ScheduleSelectedUntapAtEndOfTurn:
                    if (selected.Count > 0)
                    {
                        state.DelayedAbilities.Add(new DelayedAbilityEffect
                        {
                            Source = source,
                            OwnerPlayerId = ownerId,
                            Duration = AbilityDuration.UntilEndOfTurn,
                            FixedTargets = selected.ToList(),
                            Ability = new RuleAbilityDefinition(ability.Id,
                                AbilityTiming.EndOfOwnerTurn,
                                AbilityOperation.Untap)
                            {
                                RulesText = ability.RulesText,
                                Optional = true
                            }
                        });
                    }
                    break;
                case AbilityOperation.ScheduleSourceDestructionAfterBattle:
                    if (!state.DestroyAfterCurrentBattle.Contains(source))
                        state.DestroyAfterCurrentBattle.Add(source);
                    break;
                case AbilityOperation.DestroyAndRecruitFromDeck:
                    foreach (var target in selected)
                    {
                        var targetOwnerId = FindOwnerId(state, target);
                        if (targetOwnerId < 0) continue;
                        var targetOwner = state.Players[targetOwnerId];
                        var destruction = ApplyDestructionEvent(state, new[] { target })
                            .FirstOrDefault();
                        if (destruction != DestructionOutcome.Destroyed) continue;
                        while (targetOwner.Deck.Count > 0)
                        {
                            var revealed = targetOwner.Deck[targetOwner.Deck.Count - 1];
                            if (revealed.Definition.IsCreature &&
                                revealed.Definition.Type != CardType.EvolutionCreature)
                            {
                                MoveCard(state, revealed, RuleZone.BattleZone, false, true, ownerId);
                                break;
                            }
                            MoveCard(state, revealed, RuleZone.Graveyard, false, false, ownerId);
                            if (state.IsFinished) break;
                        }
                        if (state.IsFinished) break;
                    }
                    break;
                case AbilityOperation.DestroyAndRecruitSameCost:
                    foreach (var target in selected.ToArray())
                    {
                        var targetOwnerId = FindOwnerId(state, target);
                        if (targetOwnerId < 0) continue;
                        var targetOwner = state.Players[targetOwnerId];
                        var cost = target.Definition.Cost;
                        DestroyCard(state, target);
                        if (!targetOwner.Graveyard.Contains(target)) continue;
                        var candidates = owner.Deck.Where(card =>
                            card.Definition.IsCreature &&
                            card.Definition.Type != CardType.EvolutionCreature &&
                            card.Definition.Cost == cost).ToArray();
                        var recruit = ChooseCardsSafely(state, ownerId, source, ability,
                            candidates, 0, Math.Min(1, candidates.Length)).FirstOrDefault();
                        if (recruit != null)
                            MoveCard(state, recruit, RuleZone.BattleZone, false, true, ownerId);
                        if (state.IsFinished) break;
                        Shuffle(owner.Deck, state, ownerId);
                    }
                    break;
            }
            state.LastRuleSelection.Clear();
            state.LastRuleSelection.AddRange(selected);
            return appliedCount;
        }

        private IReadOnlyList<CardInstance> Select(
            GameState state,
            CardInstance source,
            int ownerId,
            RuleAbilityDefinition ability,
            IReadOnlyList<CardInstance> candidates,
            int? exactCount = null,
            int? maximumCount = null)
        {
            var selector = ability.Selector ?? new RuleSelectorDefinition();
            if (selector.Mode == RuleSelectionMode.Choose ||
                selector.Mode == RuleSelectionMode.PowerBudget)
                candidates = candidates.Where(card => CanBeChosenBy(state, card, ownerId)).ToArray();
            var maximum = exactCount ?? maximumCount ??
                (selector.Maximum <= 0 ? candidates.Count : selector.Maximum);
            var minimum = exactCount ?? selector.Minimum;
            maximum = Math.Min(maximum, candidates.Count);
            minimum = Math.Min(minimum, candidates.Count);
            switch (selector.Mode)
            {
                case RuleSelectionMode.SourceCard:
                    return candidates.Contains(source) ? new[] { source } : Array.Empty<CardInstance>();
                case RuleSelectionMode.All:
                    return candidates;
                case RuleSelectionMode.Top:
                    return candidates.TakeLast(Math.Min(maximum, candidates.Count)).Reverse().ToArray();
                case RuleSelectionMode.Bottom:
                    return candidates.Take(Math.Min(maximum, candidates.Count)).ToArray();
                case RuleSelectionMode.Random:
                    return candidates.OrderBy(_ => NextRandom(state)).Take(Math.Min(maximum, candidates.Count)).ToArray();
                case RuleSelectionMode.EventCard:
                    return state.CurrentEventCard != null && candidates.Contains(state.CurrentEventCard)
                        ? new[] { state.CurrentEventCard }
                        : Array.Empty<CardInstance>();
                case RuleSelectionMode.PreviousSelection:
                    return state.LastRuleSelection.Where(candidates.Contains).ToArray();
                case RuleSelectionMode.BlockingCreature:
                    return candidates.Where(card =>
                        card.InstanceId == state.CurrentBlockerInstanceId).Take(1).ToArray();
                case RuleSelectionMode.PowerBudget:
                {
                    var requested = ChooseCardsSafely(state, ownerId, source, ability,
                        candidates, 0, Math.Min(maximum, candidates.Count));
                    var selected = new List<CardInstance>();
                    var totalPower = 0;
                    foreach (var card in requested.Where(candidates.Contains).Distinct())
                    {
                        if (selected.Count >= maximum) break;
                        var power = CurrentPower(state, card);
                        if (selector.MaximumCombinedPower > 0 && totalPower + power > selector.MaximumCombinedPower)
                            continue;
                        selected.Add(card);
                        totalPower += power;
                    }
                    return selected;
                }
                default:
                {
                    return ChooseCardsSafely(state, ownerId, source, ability,
                        candidates, minimum, maximum);
                }
            }
        }

        private IReadOnlyList<CardInstance> ChooseCardsSafely(
            GameState state,
            int playerId,
            CardInstance source,
            RuleAbilityDefinition ability,
            IReadOnlyList<CardInstance> candidates,
            int minimum,
            int maximum)
        {
            candidates ??= Array.Empty<CardInstance>();
            maximum = Math.Max(0, Math.Min(maximum, candidates.Count));
            minimum = Math.Max(0, Math.Min(minimum, maximum));
            var requested = _decisions.ChooseCards(state, playerId, source, ability,
                    candidates, minimum, maximum) ?? Array.Empty<CardInstance>();
            var selected = requested.Where(card => card != null && candidates.Contains(card))
                .Distinct().Take(maximum).ToList();
            foreach (var fallback in candidates.Where(card => card != null)
                         .OrderBy(card => card.InstanceId))
            {
                if (selected.Count >= minimum) break;
                if (!selected.Contains(fallback)) selected.Add(fallback);
            }
            return selected;
        }

        private IEnumerable<CardInstance> Candidates(GameState state, CardInstance source, RuleSelectorDefinition selector)
        {
            selector ??= new RuleSelectorDefinition();
            var ownerId = FindOwnerId(state, source);
            if (ownerId < 0) return Array.Empty<CardInstance>();
            IEnumerable<PlayerState> players;
            switch (selector.Controller)
            {
                case RuleController.Opponent:
                    players = new[] { state.Players[1 - ownerId] };
                    break;
                case RuleController.Either:
                case RuleController.EachPlayer:
                    players = state.Players;
                    break;
                case RuleController.CardOwner:
                    var selectedOwnerId = state.LastRuleSelection.Count == 0
                        ? ownerId : FindOwnerId(state, state.LastRuleSelection[0]);
                    players = selectedOwnerId >= 0
                        ? new[] { state.Players[selectedOwnerId] }
                        : Array.Empty<PlayerState>();
                    break;
                default:
                    players = new[] { state.Players[ownerId] };
                    break;
            }
            var zones = selector.Zones != null && selector.Zones.Length > 0
                ? selector.Zones
                : new[] { selector.Zone };
            return players.SelectMany(player => zones.SelectMany(zone => Zone(player, zone)))
                .Where(card => MatchesFilter(state, card, source, selector.Filter));
        }

        private bool ConditionMatches(GameState state, CardInstance source, AbilityConditionDefinition condition)
        {
            if (condition == null) return true;
            var ownerId = FindOwnerId(state, source);
            if (ownerId < 0) return false;
            var result = true;
            if (condition.DuringOwnersTurn) result &= state.ActivePlayerId == ownerId;
            if (condition.DuringOpponentsTurn) result &= state.ActivePlayerId != ownerId;
            if (condition.SourceTapped) result &= source.Tapped;
            if (condition.SourceUntapped) result &= !source.Tapped;
            if (condition.EventCardIsSource) result &= state.CurrentEventCard == source;
            if (condition.EventControlledByOpponent)
                result &= state.CurrentEventCard != null &&
                    FindOwnerId(state, state.CurrentEventCard) != ownerId;
            if (condition.EventControlledByOwner)
                result &= state.CurrentEventCard != null &&
                    FindOwnerId(state, state.CurrentEventCard) == ownerId;
            if (condition.EventCardMatchesFilter)
                result &= state.CurrentEventCard != null &&
                    MatchesFilter(state, state.CurrentEventCard, source, condition.Filter);
            if (condition.PreviousSelectionMatchesFilter)
                result &= state.LastRuleSelection.Any(card =>
                    MatchesFilter(state, card, source, condition.Filter));
            if (condition.PendingAttackTargetsPlayer)
                result &= state.PendingAttack != null &&
                    state.PendingAttack.TargetKind == AttackTargetKind.Player;
            if (condition.AnotherCreatureBrokeShieldThisTurn)
                result &= AllOwnedCards(state.Players[ownerId]).Any(card =>
                    card != source && card.BrokeShieldThisTurn);
            if (condition.SourcePowerAtLeast > 0)
                result &= CurrentPower(state, source) >= condition.SourcePowerAtLeast;
            if (condition.SourceIsBattleAttacker)
                result &= state.CurrentBattleAttackerInstanceId == source.InstanceId;
            if (condition.SourceNotAttackedThisTurn)
                result &= !source.WasAttackedThisTurn;
            if (condition.OwnCreatureHigherThanEveryOpponentCreature)
            {
                var ownCreatures = state.Players[ownerId].BattleZone
                    .Where(card => card.Definition.IsCreature).ToArray();
                var opposingCreatures = state.Players[1 - ownerId].BattleZone
                    .Where(card => card.Definition.IsCreature).ToArray();
                result &= ownCreatures.Length > 0 &&
                    (opposingCreatures.Length == 0 || ownCreatures.Any(own =>
                        opposingCreatures.All(opposing =>
                            CurrentPower(state, own) > CurrentPower(state, opposing))));
            }
            if (condition.RequiresWaveStriker)
                result &= AllBattleZoneCards(state).Count(card => card != source &&
                    card.HasKeyword(CardKeyword.WaveStriker)) >= 2;
            if (condition.ShieldsWereBrokenThisTurn) result &= state.ShieldsBrokenThisTurn[ownerId] > 0;
            if (condition.SourceBrokeShieldThisTurn) result &= source.BrokeShieldThisTurn;
            if (condition.OpponentDrewCardsAtLeast > 0)
                result &= state.CardsDrawnThisTurn[1 - ownerId] >=
                    condition.OpponentDrewCardsAtLeast;
            if (condition.SpellWasCastThisTurn) result &= state.SpellCastThisTurn[ownerId];
            if (condition.Zone != RuleZone.None)
            {
                var selector = new RuleSelectorDefinition
                {
                    Controller = condition.Controller,
                    Zone = condition.Zone,
                    Mode = RuleSelectionMode.All,
                    Filter = condition.Filter
                };
                var count = Candidates(state, source, selector).Count();
                if (condition.CountAtLeast > 0) result &= count >= condition.CountAtLeast;
                if (condition.HasCountAtMost || condition.CountAtMost > 0) result &= count <= condition.CountAtMost;
                if (condition.AllCardsMustMatchFilter)
                {
                    var player = condition.Controller == RuleController.Opponent
                        ? state.Players[1 - ownerId]
                        : state.Players[ownerId];
                    var allCards = Zone(player, condition.Zone).ToArray();
                    result &= allCards.Length > 0 && allCards.All(card =>
                        MatchesFilter(state, card, source, condition.Filter));
                }
            }
            if (condition.CountComparison != RuleCountComparison.None &&
                condition.ComparisonLeft != null && condition.ComparisonRight != null)
            {
                var left = Candidates(state, source, condition.ComparisonLeft).Count();
                var right = Candidates(state, source, condition.ComparisonRight).Count();
                switch (condition.CountComparison)
                {
                    case RuleCountComparison.LessThan: result &= left < right; break;
                    case RuleCountComparison.LessThanOrEqual: result &= left <= right; break;
                    case RuleCountComparison.Equal: result &= left == right; break;
                    case RuleCountComparison.GreaterThanOrEqual: result &= left >= right; break;
                    case RuleCountComparison.GreaterThan: result &= left > right; break;
                }
            }
            return condition.Invert ? !result : result;
        }

        private bool MatchesStaticTarget(GameState state, CardInstance source, CardInstance target, RuleSelectorDefinition selector)
        {
            if (selector == null) return target == source;
            if (selector.Mode == RuleSelectionMode.SourceCard) return target == source;
            var ownerId = FindOwnerId(state, source);
            var targetOwnerId = FindOwnerId(state, target);
            if (ownerId < 0 || targetOwnerId < 0) return false;
            if (selector.Controller == RuleController.Source && ownerId != targetOwnerId) return false;
            if (selector.Controller == RuleController.Opponent && ownerId == targetOwnerId) return false;
            return Zone(state.Players[targetOwnerId], selector.Zone).Contains(target) &&
                   MatchesFilter(state, target, source, selector.Filter);
        }

        private bool MatchesFilter(GameState state, CardInstance card, CardInstance source, CardFilterDefinition filter)
        {
            if (filter == null) return true;
            var definition = card.Definition;
            if (filter.CreaturesOnly && !definition.IsCreature) return false;
            if (filter.SpellsOnly && definition.Type != CardType.Spell) return false;
            if (filter.EvolutionOnly && definition.Type != CardType.EvolutionCreature) return false;
            if (filter.NonEvolutionOnly && definition.Type == CardType.EvolutionCreature) return false;
            if (filter.MulticoloredOnly && definition.Civilizations.Length < 2) return false;
            if (filter.TappedOnly && !card.Tapped) return false;
            if (filter.UntappedOnly && card.Tapped) return false;
            if (filter.CanAttackOnly && !CanDeclareAnyAttack(state, card)) return false;
            if (filter.OtherThanSource && card == source) return false;
            // Civilization/Dragon blockers are printed blocker abilities even though
            // their blocking permission is conditional on the current attacker. They
            // must therefore match effects that search for or destroy a creature that
            // "has blocker" without becoming unrestricted blockers in CanUseAsBlocker.
            if (filter.HasBlocker && !HasBlockerAbility(state, card)) return false;
            if (filter.HasSilentSkill && !HasKeyword(state, card, CardKeyword.SilentSkill)) return false;
            if (filter.HasForbiddenKeyword &&
                (filter.ForbiddenKeyword == CardKeyword.Blocker
                    ? HasBlockerAbility(state, card)
                    : HasKeyword(state, card, filter.ForbiddenKeyword)))
                return false;
            if (filter.HasRequiredKeyword && !HasKeyword(state, card, filter.RequiredKeyword))
                return false;
            if (filter.PowerExactly > 0 || filter.PowerAtMost > 0 || filter.PowerAtLeast > 0)
            {
                // Power thresholds use the creature's current power. CurrentPower has a
                // recursion guard for continuous effects that depend on another live value.
                var currentPower = CurrentPower(state, card);
                if (filter.PowerExactly > 0 && currentPower != filter.PowerExactly) return false;
                if (filter.PowerAtMost > 0 && currentPower > filter.PowerAtMost) return false;
                if (filter.PowerAtLeast > 0 && currentPower < filter.PowerAtLeast) return false;
            }
            if (filter.CostAtMost > 0 && definition.Cost > filter.CostAtMost) return false;
            if (filter.CostAtLeast > 0 && definition.Cost < filter.CostAtLeast) return false;
            if (!string.IsNullOrEmpty(filter.Name) && !definition.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase)) return false;
            var effectiveRaces = EffectiveRaces(card)
                .Concat(IsSurvivor(state, card) ? new[] { "Survivor" } : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (filter.UseSourceChosenRace &&
                (string.IsNullOrWhiteSpace(source?.ChosenRace) || !effectiveRaces.Any(race =>
                    race.Equals(source.ChosenRace, StringComparison.OrdinalIgnoreCase)))) return false;
            if (filter.UsePreviousSelectionName &&
                (state.LastRuleSelection.Count == 0 || !definition.Name.Equals(
                    state.LastRuleSelection[0].Definition.Name,
                    StringComparison.OrdinalIgnoreCase))) return false;
            if (!string.IsNullOrEmpty(filter.Race) && !effectiveRaces.Any(race => race.Equals(filter.Race, StringComparison.OrdinalIgnoreCase))) return false;
            if (filter.Races != null && filter.Races.Length > 0 &&
                !(filter.AnyRaceCriterion && !string.IsNullOrEmpty(filter.RaceContains)) &&
                !effectiveRaces.Any(cardRace => filter.Races.Any(race =>
                    cardRace.Equals(race, StringComparison.OrdinalIgnoreCase)))) return false;
            if (filter.ExcludedRaces != null && filter.ExcludedRaces.Length > 0 &&
                effectiveRaces.Any(cardRace => filter.ExcludedRaces.Any(race =>
                    cardRace.Equals(race, StringComparison.OrdinalIgnoreCase)))) return false;
            if (!string.IsNullOrEmpty(filter.RaceContains) &&
                !(filter.AnyRaceCriterion && filter.Races != null && filter.Races.Length > 0 &&
                  effectiveRaces.Any(cardRace => filter.Races.Any(race =>
                      cardRace.Equals(race, StringComparison.OrdinalIgnoreCase)))) &&
                !effectiveRaces.Any(race => race.IndexOf(filter.RaceContains, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            if (filter.ExcludedNames != null && filter.ExcludedNames.Any(name =>
                    definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return false;
            if (filter.Civilizations != null && filter.Civilizations.Length > 0 && !definition.Civilizations.Any(filter.Civilizations.Contains)) return false;
            if (filter.ExcludedCivilizations != null && filter.ExcludedCivilizations.Length > 0 &&
                definition.Civilizations.Any(filter.ExcludedCivilizations.Contains)) return false;
            if (filter.CostAtMostManaCount)
            {
                var sourceOwnerId = FindOwnerId(state, source);
                if (sourceOwnerId < 0 || definition.Cost > state.Players[sourceOwnerId].Mana.Count)
                    return false;
            }
            if (filter.CostAtMostPreviousSelectionOwnerManaCount)
            {
                var selectedOwnerId = state.LastRuleSelection.Count == 0
                    ? -1 : FindOwnerId(state, state.LastRuleSelection[0]);
                if (selectedOwnerId < 0 || definition.Cost > state.Players[selectedOwnerId].Mana.Count)
                    return false;
            }
            return true;
        }

        private bool HasBlockerAbility(GameState state, CardInstance card)
        {
            if (HasKeyword(state, card, CardKeyword.Blocker) ||
                HasKeyword(state, card, CardKeyword.BlockIfAble)) return true;
            return RulesOf(state, card).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.AllowBlockAgainst &&
                ConditionMatches(state, card, ability.Condition));
        }

        private static IEnumerable<CardInstance> Zone(PlayerState player, RuleZone zone)
        {
            switch (zone)
            {
                case RuleZone.BattleZone: return player.BattleZone;
                case RuleZone.Hand: return player.Hand;
                case RuleZone.Deck: return player.Deck;
                case RuleZone.ManaZone: return player.Mana;
                case RuleZone.ShieldZone: return player.Shields;
                case RuleZone.Graveyard: return player.Graveyard;
                default: return Array.Empty<CardInstance>();
            }
        }

        public bool DestroyCard(GameState state, CardInstance card)
        {
            _abilityResolutionDepth++;
            bool destroyed;
            try
            {
                var plan = BuildDestructionPlan(state, card,
                    new HashSet<CardInstance>());
                plan.DestructionTriggerSources = CaptureGlobalTriggerSources(state,
                    card, AbilityTiming.OnOtherCreatureDestroyed, false);
                plan.DestructionDelayedEffects = CaptureDelayedTriggers(state,
                    AbilityTiming.OnOtherCreatureDestroyed);
                destroyed = ApplyDestructionPlan(state, plan,
                    new HashSet<CardInstance>()) == DestructionOutcome.Destroyed;
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
            return destroyed;
        }

        private DestructionPlan BuildDestructionPlan(GameState state, CardInstance card,
            ISet<CardInstance> unavailableReplacementSources)
        {
            var plan = new DestructionPlan { Card = card, OwnerId = -1 };
            if (state == null || card == null) return plan;
            var ownerId = FindOwnerId(state, card);
            plan.OwnerId = ownerId;
            if (ownerId < 0 || !state.Players[ownerId].BattleZone.Contains(card)) return plan;
            var replacement = ChooseDestructionReplacement(state, card,
                unavailableReplacementSources);
            if (replacement != null)
            {
                plan.Prevented = replacement.Prevented;
                plan.Replacement = replacement.Ability;
                plan.ReplacementSource = replacement.Source;
                plan.ReplacementDestination = replacement.Destination;
                plan.ReplacementDestinationTapped = replacement.DestinationTapped;
            }
            return plan;
        }

        private DestructionOutcome ApplyDestructionPlan(GameState state, DestructionPlan plan,
            ISet<CardInstance> destroyedAsReplacement)
        {
            if (state == null || plan == null || plan.Card == null || plan.OwnerId < 0)
                return DestructionOutcome.Invalid;
            if (!state.Players[plan.OwnerId].BattleZone.Contains(plan.Card))
                return destroyedAsReplacement != null &&
                    destroyedAsReplacement.Contains(plan.Card)
                        ? DestructionOutcome.Destroyed
                        : DestructionOutcome.Invalid;
            if (plan.Prevented) return DestructionOutcome.Prevented;
            var card = plan.Card;
            var ownerId = plan.OwnerId;
            if (plan.ReplacementDestination != RuleZone.None)
            {
                var replacementApplied = MoveCard(state, card, plan.ReplacementDestination,
                    plan.ReplacementDestinationTapped, true);
                if (replacementApplied && plan.Replacement != null &&
                    plan.Replacement.ShuffleAfter)
                {
                    var replacementOwnerId = FindOwnerId(state, card);
                    if (replacementOwnerId >= 0)
                        Shuffle(state.Players[replacementOwnerId].Deck,
                            state, replacementOwnerId);
                }
                if (replacementApplied && plan.Replacement != null)
                    ResolveDestructionReplacementFollowups(state,
                        plan.ReplacementSource ?? card, plan.Replacement);
                return replacementApplied
                    ? DestructionOutcome.Replaced
                    : DestructionOutcome.Invalid;
            }
            if (plan.Replacement != null &&
                plan.Replacement.Operation == AbilityOperation.DestroySourceInstead)
            {
                // A replacement event cannot itself be replaced. Destroy the
                // saver directly, ignoring any second destruction/leave-zone
                // replacement that would otherwise chain onto this event.
                var replacementApplied = plan.ReplacementSource != card &&
                    DestroyCardWithoutReplacement(state, plan.ReplacementSource);
                if (replacementApplied)
                {
                    destroyedAsReplacement?.Add(plan.ReplacementSource);
                    ResolveDestructionReplacementFollowups(state,
                        plan.ReplacementSource, plan.Replacement);
                    return DestructionOutcome.Replaced;
                }
            }
            MoveCard(state, card, RuleZone.Graveyard, false, false);
            Resolve(state, card, AbilityTiming.OnDestroyed);
            if (state.IsFinished) return DestructionOutcome.Destroyed;
            Resolve(state, card, AbilityTiming.OnLeaveBattleZone);
            if (state.IsFinished) return DestructionOutcome.Destroyed;
            ScheduleGlobalEvent(state, card, AbilityTiming.OnOtherCreatureDestroyed,
                false, plan.DestructionTriggerSources,
                plan.DestructionDelayedEffects);
            return DestructionOutcome.Destroyed;
        }

        private bool DestroyCardWithoutReplacement(GameState state, CardInstance card)
        {
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0 || !state.Players[ownerId].BattleZone.Contains(card)) return false;
            _abilityResolutionDepth++;
            try
            {
                var watchers = CaptureGlobalTriggerSources(state, card,
                    AbilityTiming.OnOtherCreatureDestroyed, false);
                var delayed = CaptureDelayedTriggers(state,
                    AbilityTiming.OnOtherCreatureDestroyed);
                MoveCard(state, card, RuleZone.Graveyard, false, false, -1,
                    false);
                Resolve(state, card, AbilityTiming.OnDestroyed);
                Resolve(state, card, AbilityTiming.OnLeaveBattleZone);
                ScheduleGlobalEvent(state, card,
                    AbilityTiming.OnOtherCreatureDestroyed, false,
                    watchers, delayed);
            }
            finally
            {
                _abilityResolutionDepth--;
            }
            DrainPendingTriggers();
            return true;
        }

        private void ResolveDestructionReplacementFollowups(GameState state,
            CardInstance replacementSource, RuleAbilityDefinition replacement)
        {
            if (replacementSource == null || replacement == null) return;
            var ownerId = FindOwnerId(state, replacementSource);
            if (ownerId < 0) return;
            foreach (var followup in RulesOf(state, replacementSource).Where(rule =>
                         rule.Timing == AbilityTiming.ReplacementDestroy && rule != replacement &&
                         rule.Id == replacement.Id && rule.RequiresPreviousSuccess &&
                         ConditionMatches(state, replacementSource, rule.Condition)))
                ResolveOne(state, replacementSource, ownerId, followup, 1);
        }

        private void DiscardCard(GameState state, CardInstance card, int causedByPlayerId)
        {
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0) return;
            foreach (var replacement in RulesOf(state, card).Where(ability =>
                         ability.Timing == AbilityTiming.ReplacementDiscard &&
                         ability.Operation == AbilityOperation.Move &&
                         ability.Destination != RuleZone.None &&
                         ConditionMatches(state, card, ability.Condition)))
            {
                if (replacement.Optional && !_decisions.ChooseOptional(state,
                        ownerId, card, replacement)) continue;
                MoveCard(state, card, replacement.Destination,
                    replacement.Destination == RuleZone.ManaZone, true,
                    causedByPlayerId);
                return;
            }
            MoveCard(state, card, RuleZone.Graveyard, false, true, causedByPlayerId);
        }

        private void MoveEvolutionTop(GameState state, CardInstance evolution, RuleZone destination)
        {
            var ownerId = FindOwnerId(state, evolution);
            if (ownerId < 0 || evolution.Definition.Type != CardType.EvolutionCreature) return;
            var owner = state.Players[ownerId];
            if (!owner.BattleZone.Contains(evolution)) return;

            owner.BattleZone.Remove(evolution);
            RebuildEvolutionPile(owner, evolution);
            evolution.Tapped = destination == RuleZone.ManaZone &&
                evolution.Definition.Civilizations.Length > 1;
            AddToZone(owner, evolution, destination);
            Resolve(state, evolution, AbilityTiming.OnLeaveBattleZone);
        }

        /// <summary>
        /// Rebuilds the cards below a removed evolution top as one creature.
        /// EvolutionMaterials are stored top-to-bottom: the first remaining
        /// card becomes the visible creature and every later card stays under
        /// that card. Rebuilding is not a battle-zone entry event.
        /// </summary>
        private static CardInstance RebuildEvolutionPile(PlayerState owner,
            CardInstance removedTop)
        {
            if (owner == null || removedTop == null ||
                removedTop.EvolutionMaterials.Count == 0) return null;
            var remaining = removedTop.EvolutionMaterials.ToArray();
            removedTop.EvolutionMaterials.Clear();
            var rebuiltTop = remaining[0];
            for (var index = 1; index < remaining.Length; index++)
                rebuiltTop.EvolutionMaterials.Add(remaining[index]);
            rebuiltTop.Tapped = removedTop.Tapped;
            owner.BattleZone.Add(rebuiltTop);
            return rebuiltTop;
        }

        private bool MoveCard(GameState state, CardInstance card, RuleZone destination, bool tapped,
            bool resolveLeaveTrigger = true, int causedByPlayerId = -1,
            bool allowLeaveBattleZoneReplacement = true)
        {
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0) return false;
            var player = state.Players[ownerId];
            var leavingBattleZone = player.BattleZone.Contains(card) && destination != RuleZone.BattleZone;
            var leavingManaZone = player.Mana.Contains(card) && destination == RuleZone.Graveyard;
            var leavingDeck = player.Deck.Contains(card) && destination != RuleZone.Deck;
            var entersEvolutionPile = destination == RuleZone.BattleZone &&
                !player.BattleZone.Contains(card) &&
                card.Definition.Type == CardType.EvolutionCreature;
            if (entersEvolutionPile && !TryAttachEvolutionSourcesForEffect(state, player, card))
                return false;
            if (allowLeaveBattleZoneReplacement && leavingBattleZone &&
                TryApplyLeaveBattleZoneStateEffect(state, card, destination,
                    tapped, resolveLeaveTrigger)) return true;
            var materials = FlattenEvolutionMaterials(card).ToArray();
            IReadOnlyList<CardInstance> orderedDeckPile = null;
            if (destination == RuleZone.Deck && materials.Length > 0)
            {
                var pile = new[] { card }.Concat(materials).ToArray();
                var orderAbility = new RuleAbilityDefinition(
                    "rules.order_evolution_pile_on_deck",
                    AbilityTiming.OnLeaveBattleZone, AbilityOperation.Move)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Deck,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = pile.Length,
                        Maximum = pile.Length
                    }
                };
                // Ordering providers return cards top-first; the deck stores
                // its top card at the end of the list.
                orderedDeckPile = ChooseCardsSafely(state, ownerId, card,
                    orderAbility, pile, pile.Length, pile.Length);
            }
            player.BattleZone.Remove(card);
            player.Hand.Remove(card);
            player.Deck.Remove(card);
            player.Mana.Remove(card);
            player.Shields.Remove(card);
            player.Graveyard.Remove(card);
            player.Resolving.Remove(card);
            // Face-up is a shield-zone state, not a permanent card property.
            // Every move creates a new object in its destination zone; cards
            // added to the shields by the English TCG effects enter face down.
            card.FaceUp = false;
            card.Tapped = entersEvolutionPile ? card.Tapped :
                tapped || destination == RuleZone.ManaZone &&
                card.Definition.Civilizations.Length > 1;
            card.EvolutionMaterials.Clear();
            foreach (var material in materials)
            {
                material.EvolutionMaterials.Clear();
                material.FaceUp = false;
                material.Tapped = tapped || destination == RuleZone.ManaZone &&
                    material.Definition.Civilizations.Length > 1;
            }
            if (orderedDeckPile != null)
            {
                foreach (var pileCard in orderedDeckPile.Reverse())
                    AddToZone(player, pileCard, destination);
            }
            else
            {
                foreach (var material in materials)
                    AddToZone(player, material, destination);
                AddToZone(player, card, destination);
            }
            if (leavingDeck && player.Deck.Count == 0)
            {
                FinishDeckOut(state, ownerId);
                return true;
            }
            if (leavingBattleZone && resolveLeaveTrigger)
            {
                Resolve(state, card, AbilityTiming.OnLeaveBattleZone);
                if (state.IsFinished) return true;
            }
            if (destination == RuleZone.BattleZone && card.Definition.IsCreature)
            {
                card.EnteredBattleZoneOnTurn = card.Definition.Type == CardType.EvolutionCreature
                    ? -1
                    : state.TurnNumber;
                if (ShouldEnterBattleZoneTapped(state, card)) card.Tapped = true;
                ResolveCreatureEntry(state, card, false, false);
            }
            if (leavingManaZone && causedByPlayerId >= 0 && causedByPlayerId != ownerId)
                ResolveGlobalEvent(state, card, AbilityTiming.OnOwnManaMovedToGraveyardByOpponent);
            return true;
        }

        private bool TryAttachEvolutionSourcesForEffect(
            GameState state,
            PlayerState player,
            CardInstance evolution)
        {
            var requirements = evolution.Definition.EvolutionRequirements.Length > 0
                ? evolution.Definition.EvolutionRequirements
                : new[]
                {
                    new EvolutionBaseRequirement
                    {
                        Races = evolution.Definition.EvolutionRaces.Length > 0
                            ? evolution.Definition.EvolutionRaces
                            : new[] { evolution.Definition.EvolutionRace }
                    }
                };
            var available = player.BattleZone.Where(card => card.Definition.IsCreature)
                .OrderBy(card => card.InstanceId).ToArray();
            var chosen = new List<CardInstance>();
            var choiceAbility = new RuleAbilityDefinition(
                "rules.choose_effect_evolution_bases", AbilityTiming.OnSummon,
                AbilityOperation.Move)
            {
                Selector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Mode = RuleSelectionMode.Choose,
                    Minimum = requirements.Length,
                    Maximum = requirements.Length,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                }
            };
            var requested = ChooseCardsSafely(state, player.PlayerId, evolution,
                choiceAbility, available, requirements.Length, requirements.Length);
            if (requested.Count == requirements.Length &&
                CanMatchChosenEvolutionSources(state, requirements, requested,
                    new HashSet<CardInstance>(), 0))
            {
                chosen.AddRange(requested);
            }
            else if (!TryChooseEvolutionSources(state, requirements, available,
                         chosen, 0))
            {
                return false;
            }

            evolution.Tapped = chosen.Any(card => card.Tapped);
            evolution.EnteredBattleZoneOnTurn = -1;
            foreach (var source in chosen)
            {
                player.BattleZone.Remove(source);
                evolution.EvolutionMaterials.Add(source);
            }
            return true;
        }

        private bool CanMatchChosenEvolutionSources(GameState state,
            IReadOnlyList<EvolutionBaseRequirement> requirements,
            IReadOnlyList<CardInstance> chosen, ISet<CardInstance> used,
            int requirementIndex)
        {
            if (requirementIndex >= requirements.Count) return true;
            var requirement = requirements[requirementIndex];
            foreach (var candidate in chosen)
            {
                if (used.Contains(candidate)) continue;
                var matches = candidate.Definition.CanBeAnyEvolutionBase ||
                    requirement.Races.Any(race => HasRace(state, candidate, race)) ||
                    (!string.IsNullOrEmpty(requirement.RaceContains) &&
                     RaceContains(candidate, requirement.RaceContains));
                if (!matches) continue;
                used.Add(candidate);
                if (CanMatchChosenEvolutionSources(state, requirements, chosen, used,
                        requirementIndex + 1)) return true;
                used.Remove(candidate);
            }
            return false;
        }

        private bool TryChooseEvolutionSources(
            GameState state,
            IReadOnlyList<EvolutionBaseRequirement> requirements,
            IReadOnlyList<CardInstance> available,
            List<CardInstance> chosen,
            int requirementIndex)
        {
            if (requirementIndex >= requirements.Count) return true;
            var requirement = requirements[requirementIndex];
            foreach (var candidate in available)
            {
                if (chosen.Contains(candidate)) continue;
                var matches = candidate.Definition.CanBeAnyEvolutionBase ||
                    requirement.Races.Any(race => HasRace(state, candidate, race)) ||
                    (!string.IsNullOrEmpty(requirement.RaceContains) &&
                     RaceContains(candidate, requirement.RaceContains));
                if (!matches) continue;
                chosen.Add(candidate);
                if (TryChooseEvolutionSources(state, requirements, available, chosen,
                        requirementIndex + 1)) return true;
                chosen.RemoveAt(chosen.Count - 1);
            }
            return false;
        }

        private bool TryApplyLeaveBattleZoneStateEffect(GameState state,
            CardInstance card, RuleZone destination, bool tapped,
            bool resolveLeaveTrigger)
        {
            if (card == null || card.EvolutionMaterials.Count == 0) return false;
            var replacement = RulesOf(state, card).FirstOrDefault(ability =>
                ability.Timing == AbilityTiming.StateDefinedLeaveBattleZone &&
                ability.Operation == AbilityOperation.SeparateEvolutionTop &&
                ConditionMatches(state, card, ability.Condition));
            if (replacement == null) return false;
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0) return false;
            if (replacement.Optional && !_decisions.ChooseOptional(state, ownerId,
                    card, replacement)) return false;

            var owner = state.Players[ownerId];
            owner.BattleZone.Remove(card);
            SeparateEvolutionSources(owner, card);
            card.Tapped = tapped || destination == RuleZone.ManaZone &&
                card.Definition.Civilizations.Length > 1;
            AddToZone(owner, card, destination);
            if (resolveLeaveTrigger) Resolve(state, card, AbilityTiming.OnLeaveBattleZone);
            return true;
        }

        /// <summary>
        /// Soul Phoenix explicitly separates its two Vortex evolution sources
        /// into two creatures. Each source retains any cards already below it,
        /// and both creatures inherit the departing pile's tapped state. This
        /// is a state-defined leave effect, not a battle-zone entry event.
        /// </summary>
        private static void SeparateEvolutionSources(PlayerState owner,
            CardInstance removedTop)
        {
            if (owner == null || removedTop == null) return;
            var sources = removedTop.EvolutionMaterials.ToArray();
            removedTop.EvolutionMaterials.Clear();
            foreach (var source in sources)
            {
                source.Tapped = removedTop.Tapped;
                owner.BattleZone.Add(source);
            }
        }

        private static void AddToZone(PlayerState player, CardInstance card, RuleZone destination)
        {
            switch (destination)
            {
                case RuleZone.BattleZone: player.BattleZone.Add(card); break;
                case RuleZone.Hand: player.Hand.Add(card); break;
                case RuleZone.Deck: player.Deck.Add(card); break;
                case RuleZone.ManaZone: player.Mana.Add(card); break;
                case RuleZone.ShieldZone: player.Shields.Add(card); break;
                case RuleZone.Graveyard: player.Graveyard.Add(card); break;
            }
        }

        private static IEnumerable<CardInstance> FlattenEvolutionMaterials(CardInstance card)
        {
            foreach (var material in card.EvolutionMaterials)
            {
                yield return material;
                foreach (var nested in FlattenEvolutionMaterials(material))
                    yield return nested;
            }
        }

        private static void Draw(GameState state, PlayerState player, int opponentId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (player.Deck.Count == 0)
                {
                    state.WinnerPlayerId = opponentId;
                    state.WinReason = "Deck out";
                    state.Phase = GamePhase.Finished;
                    return;
                }
                var card = player.Deck[player.Deck.Count - 1];
                player.Deck.RemoveAt(player.Deck.Count - 1);
                player.Hand.Add(card);
                state.CardsDrawnThisTurn[player.PlayerId]++;
                if (player.Deck.Count == 0)
                {
                    state.WinnerPlayerId = opponentId;
                    state.WinReason = "Deck out";
                    state.Phase = GamePhase.Finished;
                    return;
                }
            }
        }

        private static void FinishDeckOut(GameState state, int losingPlayerId)
        {
            if (state.IsFinished) return;
            state.WinnerPlayerId = 1 - losingPlayerId;
            state.WinReason = "Deck out";
            state.Phase = GamePhase.Finished;
        }

        private static void Shuffle(List<CardInstance> cards, GameState state, int salt)
        {
            var random = new Random(unchecked(state.RandomSeed * 397 ^ state.RandomActionIndex++ ^ salt));
            for (var i = cards.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }

        private static double NextRandom(GameState state)
        {
            var random = new Random(unchecked(state.RandomSeed * 397 ^ state.RandomActionIndex++));
            return random.NextDouble();
        }

        private int ChooseNumber(GameState state, int playerId, CardInstance source,
            RuleAbilityDefinition ability, int minimum, int maximum, int suggested)
        {
            if (maximum < minimum) maximum = minimum;
            suggested = Math.Max(minimum, Math.Min(maximum, suggested));
            var choice = (_decisions as IAbilityNumberDecisionProvider)?.ChooseNumber(
                state, playerId, source, ability, minimum, maximum, suggested) ?? suggested;
            return Math.Max(minimum, Math.Min(maximum, choice));
        }

        private static int FindOwnerId(GameState state, CardInstance card)
        {
            for (var i = 0; i < state.Players.Length; i++)
            {
                var player = state.Players[i];
                if (player.Deck.Contains(card) || player.Hand.Contains(card) || player.Shields.Contains(card) ||
                    player.Mana.Contains(card) || player.BattleZone.Contains(card) || player.Graveyard.Contains(card) ||
                    player.Resolving.Contains(card) || player.BattleZone.Any(top =>
                        EvolutionPileContains(top, card))) return i;
            }
            return -1;
        }

        private static bool EvolutionPileContains(CardInstance top, CardInstance card)
        {
            if (top == null || card == null) return false;
            foreach (var material in top.EvolutionMaterials)
                if (material == card || EvolutionPileContains(material, card)) return true;
            return false;
        }

        private bool ControllerAndFilterMatch(GameState state, CardInstance source,
            CardInstance target, RuleSelectorDefinition selector)
        {
            selector ??= new RuleSelectorDefinition();
            var sourceOwnerId = FindOwnerId(state, source);
            var targetOwnerId = FindOwnerId(state, target);
            if (sourceOwnerId < 0 || targetOwnerId < 0) return false;
            if (selector.Controller == RuleController.Source && sourceOwnerId != targetOwnerId) return false;
            if (selector.Controller == RuleController.Opponent && sourceOwnerId == targetOwnerId) return false;
            return MatchesFilter(state, target, source, selector.Filter);
        }

        private bool CanBeChosenBy(GameState state, CardInstance target, int chooserId)
        {
            var ownerId = FindOwnerId(state, target);
            if (ownerId < 0 || ownerId == chooserId) return true;
            return !RulesOf(state, target).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.PreventOpponentChoice &&
                ConditionMatches(state, target, ability.Condition));
        }

        private bool CanBeTappedBy(GameState state, CardInstance target, int effectControllerId)
        {
            var ownerId = FindOwnerId(state, target);
            if (ownerId < 0 || ownerId == effectControllerId) return true;
            return !RulesOf(state, target).Any(ability =>
                ability.Timing == AbilityTiming.Static &&
                ability.Operation == AbilityOperation.PreventOpponentTap &&
                ConditionMatches(state, target, ability.Condition));
        }

        private IEnumerable<RuleAbilityDefinition> RulesOf(GameState state, CardInstance card)
        {
            var rules = card.Definition.RuleAbilities.Concat(card.TimedRuleAbilities
                .Where(effect => effect?.Ability != null)
                .Select(effect => effect.Ability));
            if (!IsSurvivor(state, card)) return rules;
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0 || !state.Players[ownerId].BattleZone.Contains(card)) return rules;
            return rules.Concat(state.Players[ownerId].BattleZone
                .Where(other => other != card && IsSurvivor(state, other))
                .SelectMany(other => other.Definition.RuleAbilities
                    .Where(ability => ability.IsSurvivorAbility)));
        }

        private bool IsSurvivor(GameState state, CardInstance card)
        {
            if (card == null) return false;
            if (card.Definition.HasKeyword(CardKeyword.Survivor) ||
                card.TemporaryKeywords.Contains(CardKeyword.Survivor) ||
                card.TimedKeywords.Any(effect => effect.Keyword == CardKeyword.Survivor))
                return true;
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0) return false;
            foreach (var source in state.Players[ownerId].BattleZone)
            {
                var directRules = source.Definition.RuleAbilities.Concat(source.TimedRuleAbilities
                    .Where(effect => effect?.Ability != null)
                    .Select(effect => effect.Ability));
                foreach (var ability in directRules.Where(rule =>
                             rule.Timing == AbilityTiming.Static &&
                             rule.Operation == AbilityOperation.GrantKeyword &&
                             rule.Keyword == CardKeyword.Survivor))
                    if (ConditionMatches(state, source, ability.Condition) &&
                        MatchesStaticTarget(state, source, card, ability.Selector)) return true;
            }
            return false;
        }

        private bool BattleOpponentMatches(GameState state, CardInstance card,
            CardFilterDefinition relatedFilter)
        {
            if (state == null || card == null || state.CurrentBattleAttackerInstanceId == 0 ||
                state.CurrentBattleDefenderInstanceId == 0) return false;
            var opponentId = card.InstanceId == state.CurrentBattleAttackerInstanceId
                ? state.CurrentBattleDefenderInstanceId
                : card.InstanceId == state.CurrentBattleDefenderInstanceId
                    ? state.CurrentBattleAttackerInstanceId
                    : 0;
            if (opponentId == 0) return false;
            var opponent = AllBattleZoneCards(state).FirstOrDefault(candidate =>
                candidate.InstanceId == opponentId);
            return opponent != null && MatchesFilter(state, opponent, card, relatedFilter);
        }

        private static IEnumerable<string> EffectiveRaces(CardInstance card) =>
            card.Definition.Races.Concat(card.TemporaryRaces ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase);

        private static IEnumerable<CardInstance> AllOwnedCards(PlayerState player) =>
            player.Deck.Concat(player.Hand).Concat(player.Shields).Concat(player.Mana)
                .Concat(player.BattleZone).Concat(player.Graveyard).Concat(player.Resolving);

        private static PlayerState[] PlayersInActiveOrder(GameState state) =>
            new[]
            {
                state.Players[state.ActivePlayerId],
                state.Players[1 - state.ActivePlayerId]
            };

        private static IEnumerable<CardInstance> AllBattleZoneCards(GameState state) =>
            state.Players.SelectMany(player => player.BattleZone);
    }

    internal static class CardInstanceDisplayExtensions
    {
        public static string NameOrFallback(this CardInstance card) =>
            card?.Definition == null || string.IsNullOrEmpty(card.Definition.Name) ? "Card" : card.Definition.Name;
    }
}
