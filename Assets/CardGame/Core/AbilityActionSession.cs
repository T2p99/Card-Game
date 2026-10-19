using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CardGame.Core
{
    public enum AbilityChoiceKind { Cards, Options, Number }

    public sealed class AbilityChoice
    {
        public AbilityChoiceKind Kind;
        public int PlayerId;
        public CardInstance Source;
        public string Prompt;
        public IReadOnlyList<CardInstance> Cards = Array.Empty<CardInstance>();
        public IReadOnlyList<string> Options = Array.Empty<string>();
        public IReadOnlyList<string> CardLabels = Array.Empty<string>();
        public IReadOnlyList<CardInstance> RevealedCards = Array.Empty<CardInstance>();
        public int Minimum;
        public int Maximum;
        public int Suggested;
        public int MaximumCombinedPower;
        public IReadOnlyList<int> CardPowers = Array.Empty<int>();
        internal string Key;
    }

    /// <summary>
    /// Runs a rules command transactionally. Each answer replays from an isolated
    /// snapshot with the same RNG state and earlier answers. An unanswered choice
    /// discards the trial; mana, triggers, and effects commit exactly once.
    /// Commands must resolve cards by instance ID in the supplied state.
    /// </summary>
    public sealed class AbilityActionSession
    {
        private sealed class Answer
        {
            public string Key;
            public long[] Cards;
            public int Value;
        }
        private sealed class AwaitChoice : Exception
        {
            public readonly AbilityChoice Choice;
            public AwaitChoice(AbilityChoice choice) { Choice = choice; }
        }
        private readonly GameState _baseline;
        private readonly Func<RulesEngine, GameState, RuleResult> _command;
        private readonly int _humanId;
        private readonly List<Answer> _answers = new List<Answer>();
        public GameState State { get; private set; }
        public AbilityChoice PendingChoice { get; private set; }
        public RuleResult Result { get; private set; }
        public bool IsComplete => Result != null;

        public AbilityActionSession(GameState state, int humanId,
            Func<RulesEngine, GameState, RuleResult> command)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _baseline = GameStateSnapshot.Copy(state);
            // Inspection grants belong to one resolution. Old inspected object
            // references must not reveal a card after it changes hidden zones.
            _baseline.InspectionPlayerId = -1;
            _baseline.InspectedCards.Clear();
            State = state;
            _humanId = humanId;
            _command = command ?? throw new ArgumentNullException(nameof(command));
            Advance();
        }

        public bool AnswerCards(IEnumerable<long> cardIds)
        {
            var choice = PendingChoice;
            if (choice == null || choice.Kind != AbilityChoiceKind.Cards) return false;
            var ids = (cardIds ?? Array.Empty<long>()).ToArray();
            if (ids.Distinct().Count() != ids.Length || ids.Length < choice.Minimum ||
                ids.Length > choice.Maximum || ids.Any(id => !choice.Cards.Any(c => c.InstanceId == id)))
                return false;
            if (choice.MaximumCombinedPower > 0 && ids.Sum(id =>
                choice.CardPowers[choice.Cards.ToList().FindIndex(c => c.InstanceId == id)]) > choice.MaximumCombinedPower)
                return false;
            _answers.Add(new Answer { Key = choice.Key, Cards = ids });
            Advance();
            return true;
        }

        public bool AnswerValue(int value)
        {
            var choice = PendingChoice;
            if (choice == null || choice.Kind == AbilityChoiceKind.Cards ||
                value < choice.Minimum || value > choice.Maximum) return false;
            _answers.Add(new Answer { Key = choice.Key, Value = value });
            Advance();
            return true;
        }

        private void Advance()
        {
            PendingChoice = null;
            var trial = GameStateSnapshot.Copy(_baseline);
            var provider = new Decisions(this);
            try
            {
                Result = _command(new RulesEngine(provider), trial);
                if (Result.Success) State = trial;
            }
            catch (AwaitChoice pending)
            {
                PendingChoice = pending.Choice;
            }
        }

        private sealed class Decisions : IAbilityDecisionProvider,
            IAbilityRaceDecisionProvider, IAbilityCivilizationDecisionProvider,
            IAbilityNumberDecisionProvider, IAbilityPlayerDecisionProvider,
            IAbilityReplacementDecisionProvider, IAbilityTriggerOrderDecisionProvider
        {
            private readonly AbilityActionSession _session;
            private readonly DeterministicAbilityDecisionProvider _automatic =
                new DeterministicAbilityDecisionProvider();
            private int _cursor;
            public Decisions(AbilityActionSession session) { _session = session; }

            private Answer Ask(AbilityChoice choice, string abilityId, GameState state = null)
            {
                if (state != null && state.InspectionPlayerId == _session._humanId)
                    choice.RevealedCards = state.InspectedCards.ToArray();
                choice.Key = choice.Kind + ":" + choice.PlayerId + ":" +
                    (choice.Source?.InstanceId ?? 0) + ":" + abilityId + ":" +
                    choice.Minimum + ":" + choice.Maximum + ":" +
                    string.Join(",", choice.Cards.Select(c => c.InstanceId)) + ":" +
                    string.Join("|", choice.Options);
                if (_cursor >= _session._answers.Count) throw new AwaitChoice(choice);
                var answer = _session._answers[_cursor++];
                if (answer.Key != choice.Key)
                    throw new InvalidOperationException("The rules command did not replay deterministically.");
                return answer;
            }

            private static string Prompt(CardInstance source, RuleAbilityDefinition ability, string action)
            {
                var text = !string.IsNullOrWhiteSpace(ability?.RulesText)
                    ? ability.RulesText : source?.Definition.RulesText;
                return action + (string.IsNullOrWhiteSpace(text) ? string.Empty : "\n" + text);
            }

            private int Option(GameState state, int playerId, CardInstance source, string id,
                string prompt, IReadOnlyList<string> options)
            {
                if (options.Count <= 1) return 0;
                return Ask(new AbilityChoice { Kind = AbilityChoiceKind.Options,
                    PlayerId = playerId, Source = source, Prompt = prompt,
                    Options = options, Minimum = 0, Maximum = options.Count - 1 }, id, state).Value;
            }

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => playerId != _session._humanId ||
                Option(state, playerId, source, ability.Id, Prompt(source, ability, "Use this optional ability?"),
                    new[] { "Use ability", "Decline" }) == 0;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum)
            {
                maximum = Math.Min(maximum, candidates.Count);
                minimum = Math.Min(minimum, maximum);
                if (playerId != _session._humanId || maximum == 0 ||
                    (minimum == 1 && maximum == 1 && candidates.Count == 1))
                    return _automatic.ChooseCards(state, playerId, source, ability,
                        candidates, minimum, maximum);
                var answer = Ask(new AbilityChoice { Kind = AbilityChoiceKind.Cards,
                    PlayerId = playerId, Source = source, Cards = candidates,
                    CardLabels = candidates.Select((c, i) => CardLabel(state, playerId, c, i)).ToArray(),
                    Minimum = minimum, Maximum = maximum,
                    MaximumCombinedPower = ability.Selector?.Mode == RuleSelectionMode.PowerBudget
                        ? ability.Selector.MaximumCombinedPower : 0,
                    CardPowers = candidates.Select(c => new AbilityResolver().CurrentPower(state, c)).ToArray(),
                    Prompt = Prompt(source, ability, $"Choose {minimum}–{maximum} cards. Select in the order to resolve.") }, ability.Id, state);
                return answer.Cards.Select(id => candidates.First(c => c.InstanceId == id)).ToArray();
            }

            private static string CardLabel(GameState state, int playerId, CardInstance card, int index)
            {
                var shield = state.Players.Any(p => p.Shields.Contains(card));
                var hiddenHand = state.Players.Any(p => p.PlayerId != playerId && p.Hand.Contains(card));
                var inspected = state.InspectionPlayerId == playerId && state.InspectedCards.Contains(card);
                if (!card.FaceUp && !inspected && (shield || hiddenHand))
                    return (state.Players[playerId].Shields.Contains(card) || state.Players[playerId].Hand.Contains(card)
                        ? "You — " : "Opponent — ") +
                        (shield ? "Face-down shield " : "Face-down hand card ") + (index + 1);
                var location = string.Empty;
                foreach (var player in state.Players)
                {
                    var zones = new[] { player.BattleZone, player.Hand, player.Mana, player.Graveyard, player.Deck, player.Shields, player.Resolving };
                    var names = new[] { "Battle zone", "Hand", "Mana", "Graveyard", "Deck", "Shields", "Resolving" };
                    var zone = Array.FindIndex(zones, cards => cards.Contains(card));
                    if (zone >= 0) { location = (player.PlayerId == playerId ? "You" : "Opponent") + " — " + names[zone] + "\n"; break; }
                }
                return location + card.Definition.Name + (card.Tapped ? " (tapped)" : "") +
                    (card.Definition.IsCreature ? " · Power " + new AbilityResolver().CurrentPower(state, card) : string.Empty) +
                    "\n" + card.Definition.RulesText;
            }

            public string ChooseRace(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability, IReadOnlyList<string> races) =>
                races.Count == 0 ? string.Empty : races[playerId != _session._humanId ? 0 :
                    Option(state, playerId, source, ability.Id, Prompt(source, ability, "Choose a race."), races)];

            public Civilization ChooseCivilization(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability, IReadOnlyList<Civilization> civilizations) =>
                civilizations[playerId != _session._humanId ? 0 : Option(state, playerId, source,
                    ability.Id, Prompt(source, ability, "Choose a civilization."),
                    civilizations.Select(c => c.ToString()).ToArray())];

            public int ChooseNumber(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability, int minimum, int maximum, int suggested) =>
                playerId != _session._humanId || minimum == maximum ? suggested :
                Ask(new AbilityChoice { Kind = AbilityChoiceKind.Number,
                    PlayerId = playerId, Source = source, Minimum = minimum,
                    Maximum = maximum, Suggested = suggested,
                    Prompt = Prompt(source, ability, $"Choose a number from {minimum} to {maximum}.") }, ability.Id, state).Value;

            public int ChoosePlayer(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability, IReadOnlyList<int> players) =>
                playerId != _session._humanId ? playerId : players[Option(state, playerId, source,
                    ability.Id, Prompt(source, ability, "Choose a player."),
                    players.Select(id => id == _session._humanId ? "You" : "Opponent").ToArray())];

            public int ChooseReplacement(GameState state, int playerId, CardInstance affectedCard,
                IReadOnlyList<CardInstance> sources, IReadOnlyList<RuleAbilityDefinition> abilities) =>
                playerId != _session._humanId ? 0 : Option(state, playerId, affectedCard,
                    "replacement", "Choose a replacement effect.", abilities.Select((a, i) =>
                        sources[i].Definition.Name + " — " + (string.IsNullOrWhiteSpace(a.RulesText)
                            ? sources[i].Definition.RulesText : a.RulesText)).ToArray());

            public int ChooseNextTriggeredAbility(GameState state, int playerId,
                CardInstance source, AbilityTiming timing, IReadOnlyList<string> ids) =>
                playerId != _session._humanId ? 0 : Option(state, playerId, source,
                    "trigger_order." + timing, "Choose the next ability to resolve.",
                    ids.Select(id => new AbilityResolver().GetAbilityPreviews(state, source)
                        .Select(preview => preview.Ability)
                        .Concat(state.DelayedAbilities.Where(effect => effect.Source == source).Select(effect => effect.Ability))
                        .Concat(TcgCatalog.Printings.SelectMany(card => card.RuleAbilities))
                        .Where(a => a != null && a.Id == id).Select(a => a.RulesText)
                        .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ??
                        source.Definition.RulesText).ToArray());
        }
    }

    /// <summary>Copies mutable match state while retaining immutable card/rule definitions.</summary>
    public static class GameStateSnapshot
    {
        public static GameState Copy(GameState state) =>
            (GameState)CopyObject(state, new Dictionary<object, object>());

        private static object CopyObject(object value, IDictionary<object, object> seen)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsValueType || value is string || value is CardDefinition ||
                value is RuleAbilityDefinition) return value;
            if (seen.TryGetValue(value, out var existing)) return existing;
            if (value is Array array)
            {
                var copy = Array.CreateInstance(type.GetElementType(), array.Length);
                seen.Add(value, copy);
                for (var i = 0; i < array.Length; i++) copy.SetValue(CopyObject(array.GetValue(i), seen), i);
                return copy;
            }
            var result = Activator.CreateInstance(type);
            seen.Add(value, result);
            if (value is IList list)
            {
                var copy = (IList)result;
                foreach (var item in list) copy.Add(CopyObject(item, seen));
            }
            else
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    field.SetValue(result, CopyObject(field.GetValue(value), seen));
            return result;
        }
    }
}
