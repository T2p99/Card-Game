using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core
{
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
        private readonly IAbilityDecisionProvider _decisions;

        public AbilityResolver(IAbilityDecisionProvider decisions = null)
        {
            _decisions = decisions ?? new DeterministicAbilityDecisionProvider();
        }

        public void Resolve(GameState state, CardInstance source, AbilityTiming timing)
        {
            if (state == null || source == null || source.Definition == null) return;
            foreach (var ability in source.Definition.RuleAbilities.Where(rule => rule.Timing == timing))
            {
                if (!ConditionMatches(state, source, ability.Condition)) continue;
                var ownerId = FindOwnerId(state, source);
                if (ownerId < 0) continue;
                if (ability.Optional && !_decisions.ChooseOptional(state, ownerId, source, ability)) continue;
                ResolveOne(state, source, ownerId, ability);
            }
        }

        public bool ResolveOptionalTiming(GameState state, CardInstance source, AbilityTiming timing)
        {
            if (state == null || source == null) return false;
            var ownerId = FindOwnerId(state, source);
            if (ownerId < 0) return false;
            var resolved = false;
            foreach (var ability in source.Definition.RuleAbilities.Where(rule => rule.Timing == timing))
            {
                if (!ConditionMatches(state, source, ability.Condition)) continue;
                if (!_decisions.ChooseOptional(state, ownerId, source, ability)) continue;
                ResolveOne(state, source, ownerId, ability);
                resolved = true;
            }
            return resolved;
        }

        public int CurrentPower(GameState state, CardInstance card, bool attacking = false)
        {
            if (card == null) return 0;
            var power = card.Definition.Power + card.TemporaryPower;
            if (attacking)
                power += card.Definition.KeywordValue(CardKeyword.PowerAttacker) + card.TemporaryPowerAttacker;
            foreach (var source in AllBattleZoneCards(state))
            {
                foreach (var ability in source.Definition.RuleAbilities)
                {
                    if (ability.Timing != AbilityTiming.Static ||
                        (ability.Operation != AbilityOperation.ModifyPower &&
                         (!attacking || ability.Operation != AbilityOperation.ModifyPowerWhileAttacking))) continue;
                    if (!ConditionMatches(state, source, ability.Condition)) continue;
                    if (MatchesStaticTarget(state, source, card, ability.Selector))
                    {
                        power += ability.Value;
                        if (ability.ValuePerMatchingCard != 0 && ability.ValueSelector != null)
                            power += Candidates(state, source, ability.ValueSelector).Count() * ability.ValuePerMatchingCard;
                    }
                }
            }
            return power;
        }

        public int KeywordValue(GameState state, CardInstance card, CardKeyword keyword)
        {
            if (card == null) return 0;
            var value = card.Definition.KeywordValue(keyword);
            foreach (var source in AllBattleZoneCards(state))
            {
                foreach (var ability in source.Definition.RuleAbilities)
                {
                    if (ability.Timing != AbilityTiming.Static || ability.Operation != AbilityOperation.GrantKeyword ||
                        ability.Keyword != keyword) continue;
                    if (!ConditionMatches(state, source, ability.Condition)) continue;
                    if (MatchesStaticTarget(state, source, card, ability.Selector)) value = Math.Max(value, ability.Value);
                }
            }
            return value;
        }

        public bool TryReplaceDestruction(GameState state, CardInstance source, out RuleZone destination)
        {
            destination = RuleZone.None;
            if (state == null || source == null) return false;
            var ownerId = FindOwnerId(state, source);
            if (ownerId < 0) return false;
            foreach (var ability in source.Definition.RuleAbilities.Where(rule =>
                         rule.Timing == AbilityTiming.ReplacementDestroy &&
                         ConditionMatches(state, source, rule.Condition)))
            {
                if (ability.Optional && !_decisions.ChooseOptional(state, ownerId, source, ability)) continue;
                destination = ability.Destination;
                return destination != RuleZone.None;
            }
            return false;
        }

        public bool HasKeyword(GameState state, CardInstance card, CardKeyword keyword)
        {
            if (card == null) return false;
            if (card.HasKeyword(keyword)) return true;
            foreach (var source in AllBattleZoneCards(state))
            {
                foreach (var ability in source.Definition.RuleAbilities)
                {
                    if (ability.Timing != AbilityTiming.Static || ability.Operation != AbilityOperation.GrantKeyword ||
                        ability.Keyword != keyword) continue;
                    if (!ConditionMatches(state, source, ability.Condition)) continue;
                    if (MatchesStaticTarget(state, source, card, ability.Selector)) return true;
                }
            }
            return false;
        }

        private void ResolveOne(GameState state, CardInstance source, int ownerId, RuleAbilityDefinition ability)
        {
            var owner = state.Players[ownerId];
            var opponent = state.Players[1 - ownerId];
            switch (ability.Operation)
            {
                case AbilityOperation.Draw:
                    Draw(state, owner, opponent.PlayerId, ability.Amount);
                    return;
                case AbilityOperation.Shuffle:
                    Shuffle(owner.Deck, state, ownerId);
                    return;
                case AbilityOperation.ExtraTurn:
                    state.ExtraTurnsQueuedForPlayer = ownerId;
                    return;
                case AbilityOperation.LoseGame:
                    state.WinnerPlayerId = opponent.PlayerId;
                    state.WinReason = source.NameOrFallback() + " effect";
                    state.Phase = GamePhase.Finished;
                    return;
                case AbilityOperation.GrantBlockedCreatureSlayerThisTurn:
                    state.BlockedCreatureGetsSlayerThisTurn[ownerId] = true;
                    return;
            }

            var candidates = Candidates(state, source, ability.Selector).ToList();
            var chooserId = ability.Chooser == RuleController.Opponent ? 1 - ownerId : ownerId;
            if (ability.Operation == AbilityOperation.DestroyOrSource && candidates.Count < ability.Amount)
            {
                DestroyCard(state, source);
                return;
            }
            var selected = Select(state, source, chooserId, ability, candidates);
            switch (ability.Operation)
            {
                case AbilityOperation.Discard:
                    foreach (var card in selected) MoveCard(state, card, RuleZone.Graveyard, false);
                    break;
                case AbilityOperation.Destroy:
                case AbilityOperation.DestroyOrSource:
                    foreach (var card in selected) DestroyCard(state, card);
                    break;
                case AbilityOperation.Tap:
                    foreach (var card in selected) card.Tapped = true;
                    break;
                case AbilityOperation.Untap:
                    foreach (var card in selected) card.Tapped = false;
                    break;
                case AbilityOperation.Move:
                case AbilityOperation.Search:
                case AbilityOperation.AddShield:
                    foreach (var card in selected) MoveCard(state, card, ability.Destination, ability.DestinationTapped);
                    if (ability.ShuffleAfter) Shuffle(owner.Deck, state, ownerId);
                    break;
                case AbilityOperation.ModifyPower:
                    foreach (var card in selected) card.TemporaryPower += ability.Value;
                    break;
                case AbilityOperation.ModifyPowerWhileAttacking:
                    foreach (var card in selected) card.TemporaryPowerAttacker += ability.Value;
                    break;
                case AbilityOperation.GrantKeyword:
                    foreach (var card in selected)
                        if (!card.TemporaryKeywords.Contains(ability.Keyword)) card.TemporaryKeywords.Add(ability.Keyword);
                    break;
                case AbilityOperation.RemoveKeyword:
                    foreach (var card in selected) card.TemporaryKeywords.Remove(ability.Keyword);
                    break;
                case AbilityOperation.SetBreakerCount:
                    foreach (var card in selected) card.TemporaryBreakerCount = ability.Value;
                    break;
                case AbilityOperation.BreakShield:
                    foreach (var card in selected) MoveCard(state, card, RuleZone.Hand, false);
                    state.ShieldsBrokenThisTurn[ownerId] += selected.Count;
                    break;
            }
        }

        private IReadOnlyList<CardInstance> Select(
            GameState state,
            CardInstance source,
            int ownerId,
            RuleAbilityDefinition ability,
            IReadOnlyList<CardInstance> candidates)
        {
            var selector = ability.Selector ?? new RuleSelectorDefinition();
            var maximum = selector.Maximum <= 0 ? candidates.Count : selector.Maximum;
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
                default:
                    return _decisions.ChooseCards(state, ownerId, source, ability, candidates,
                        Math.Min(selector.Minimum, candidates.Count), maximum);
            }
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
                default:
                    players = new[] { state.Players[ownerId] };
                    break;
            }
            return players.SelectMany(player => Zone(player, selector.Zone))
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
            if (condition.ShieldsWereBrokenThisTurn) result &= state.ShieldsBrokenThisTurn[ownerId] > 0;
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
                if (condition.CountAtMost > 0) result &= count <= condition.CountAtMost;
            }
            return condition.Invert ? !result : result;
        }

        private bool MatchesStaticTarget(GameState state, CardInstance source, CardInstance target, RuleSelectorDefinition selector)
        {
            if (selector == null) return target == source;
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
            if (filter.OtherThanSource && card == source) return false;
            if (filter.HasBlocker && !HasKeyword(state, card, CardKeyword.Blocker)) return false;
            if (filter.HasSilentSkill && !HasKeyword(state, card, CardKeyword.SilentSkill)) return false;
            if (filter.PowerExactly > 0 || filter.PowerAtMost > 0 || filter.PowerAtLeast > 0)
            {
                // Selection limits use current power. Static power effects whose own target
                // filters depend on power are intentionally evaluated from printed + temporary
                // power to avoid a self-referential calculation.
                var currentPower = definition.Power + card.TemporaryPower;
                if (filter.PowerExactly > 0 && currentPower != filter.PowerExactly) return false;
                if (filter.PowerAtMost > 0 && currentPower > filter.PowerAtMost) return false;
                if (filter.PowerAtLeast > 0 && currentPower < filter.PowerAtLeast) return false;
            }
            if (filter.CostAtMost > 0 && definition.Cost > filter.CostAtMost) return false;
            if (filter.CostAtLeast > 0 && definition.Cost < filter.CostAtLeast) return false;
            if (!string.IsNullOrEmpty(filter.Name) && !definition.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(filter.Race) && !definition.Races.Any(race => race.Equals(filter.Race, StringComparison.OrdinalIgnoreCase))) return false;
            if (!string.IsNullOrEmpty(filter.RaceContains) && !definition.Races.Any(race => race.IndexOf(filter.RaceContains, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            if (filter.Civilizations != null && filter.Civilizations.Length > 0 && !definition.Civilizations.Any(filter.Civilizations.Contains)) return false;
            if (filter.ExcludedCivilizations != null && filter.ExcludedCivilizations.Length > 0 &&
                definition.Civilizations.Any(filter.ExcludedCivilizations.Contains)) return false;
            return true;
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

        public void DestroyCard(GameState state, CardInstance card)
        {
            if (state == null || card == null || FindOwnerId(state, card) < 0) return;
            if (TryReplaceDestruction(state, card, out var replacement))
            {
                MoveCard(state, card, replacement, replacement == RuleZone.ManaZone, false);
                return;
            }
            Resolve(state, card, AbilityTiming.OnDestroyed);
            Resolve(state, card, AbilityTiming.OnLeaveBattleZone);
            MoveCard(state, card, RuleZone.Graveyard, false, false);
        }

        private void MoveCard(GameState state, CardInstance card, RuleZone destination, bool tapped,
            bool resolveLeaveTrigger = true)
        {
            var ownerId = FindOwnerId(state, card);
            if (ownerId < 0) return;
            var player = state.Players[ownerId];
            var leavingBattleZone = player.BattleZone.Contains(card) && destination != RuleZone.BattleZone;
            if (leavingBattleZone && resolveLeaveTrigger)
                Resolve(state, card, AbilityTiming.OnLeaveBattleZone);
            player.BattleZone.Remove(card);
            player.Hand.Remove(card);
            player.Deck.Remove(card);
            player.Mana.Remove(card);
            player.Shields.Remove(card);
            player.Graveyard.Remove(card);
            card.Tapped = tapped;
            var materials = card.EvolutionMaterials.ToArray();
            card.EvolutionMaterials.Clear();
            foreach (var material in materials)
            {
                material.Tapped = tapped;
                AddToZone(player, material, destination);
            }
            AddToZone(player, card, destination);
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
            }
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

        private static int FindOwnerId(GameState state, CardInstance card)
        {
            for (var i = 0; i < state.Players.Length; i++)
            {
                var player = state.Players[i];
                if (player.Deck.Contains(card) || player.Hand.Contains(card) || player.Shields.Contains(card) ||
                    player.Mana.Contains(card) || player.BattleZone.Contains(card) || player.Graveyard.Contains(card)) return i;
            }
            return -1;
        }

        private static IEnumerable<CardInstance> AllBattleZoneCards(GameState state) =>
            state.Players.SelectMany(player => player.BattleZone);
    }

    internal static class CardInstanceDisplayExtensions
    {
        public static string NameOrFallback(this CardInstance card) =>
            card?.Definition == null || string.IsNullOrEmpty(card.Definition.Name) ? "Card" : card.Definition.Name;
    }
}
