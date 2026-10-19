using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CardGame.Core
{
    public sealed class AbilityHandlerMetadata
    {
        public string Id { get; }
        public string Category { get; }
        public string Description { get; }

        public AbilityHandlerMetadata(string id, string category, string description)
        {
            Id = id;
            Category = category;
            Description = description;
        }
    }

    public sealed class PrintedRuleLine
    {
        public string Id { get; }
        public string Text { get; }
        public string Handler { get; }

        public PrintedRuleLine(string id, string text, string handler = "")
        {
            Id = id;
            Text = text;
            Handler = handler;
        }
    }

    /// <summary>
    /// Canonical stock of executable TCG abilities. New card catalogs contain
    /// printed rules only; this registry is the single mapping from printed
    /// language to engine behavior.
    /// </summary>
    public static partial class AbilityRegistry
    {
        private static readonly Regex PowerAttacker = new Regex(@"^Power attacker \+(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex SingleEvolution = new Regex(@"^Evolution—Put on one of your (.+)\.$", RegexOptions.IgnoreCase);
        private static readonly Regex VortexEvolution = new Regex(@"^Vortex evolution—Put on one of your (.+?) and one of your (.+?)\.$", RegexOptions.IgnoreCase);
        private static readonly Regex CrewBreaker = new Regex(@"^Crew breaker—(.+?) \(", RegexOptions.IgnoreCase);
        private static readonly Regex DestroyPower = new Regex(@"^Destroy (?:one|1) of your opponent's creatures that has power (\d+) or less\.$", RegexOptions.IgnoreCase);
        private static readonly Regex SummonDestroyPower = new Regex(@"^When you put this creature into the battle zone, (?:you may )?destroy one of your opponent's creatures that has power (\d+) or less\.$", RegexOptions.IgnoreCase);
        private static readonly Regex DrawCards = new Regex(@"^Draw (?:up to )?(\d+) cards?\.$", RegexOptions.IgnoreCase);
        private static readonly Regex SummonDrawCards = new Regex(@"^When you put this creature into the battle zone, (?:you may )?draw (?:up to )?(?:(\d+) cards?|a card)\.$", RegexOptions.IgnoreCase);
        private static readonly Regex TopDeckToMana = new Regex(@"^Put the top (?:(\d+) cards?|card) of your deck into your mana zone\.$", RegexOptions.IgnoreCase);
        private static readonly Regex SummonTopDeckToMana = new Regex(@"^When you put this creature into the battle zone, (?:you may )?put the top (?:(\d+) cards?|card) of your deck into your mana zone\.$", RegexOptions.IgnoreCase);

        public static readonly IReadOnlyList<AbilityHandlerMetadata> Handlers =
            CreateHandlerStock().AsReadOnly();

        public static bool Configure(CardDefinition card, IReadOnlyList<PrintedRuleLine> lines)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            lines ??= Array.Empty<PrintedRuleLine>();
            card.RuleLineIds = lines.Select(line => line.Id).ToArray();
            var complete = true;
            foreach (var line in lines)
            {
                var isSurvivorAbility = card.HasKeyword(CardKeyword.Survivor) &&
                    !Starts(line.Text.Trim().TrimStart('■').Trim(), "Survivor");
                var isWaveStrikerAbility = card.HasKeyword(CardKeyword.WaveStriker) &&
                    !Starts(line.Text.Trim().TrimStart('■').Trim(), "Wave striker");
                var isTurboRushAbility = card.HasKeyword(CardKeyword.TurboRush) &&
                    !Starts(line.Text.Trim().TrimStart('■').Trim(), "Turbo rush");
                var keywordCount = card.Abilities.Length;
                var ruleCount = card.RuleAbilities.Length;
                if (!Apply(card, line))
                {
                    complete = false;
                    continue;
                }
                if (isSurvivorAbility)
                {
                    foreach (var keyword in card.Abilities.Skip(keywordCount))
                        keyword.IsSurvivorAbility = true;
                    foreach (var ability in card.RuleAbilities.Skip(ruleCount))
                        ability.IsSurvivorAbility = true;
                }
                if (isWaveStrikerAbility)
                    ApplyWaveStrikerCondition(card, line, keywordCount, ruleCount);
                if (isTurboRushAbility)
                    ApplyTurboRushCondition(card, line, keywordCount, ruleCount);
            }
            card.ImplementationStatus = complete ? CardImplementationStatus.Complete :
                card.Abilities.Length > 0 || card.RuleAbilities.Length > 0
                    ? CardImplementationStatus.CoreKeywords
                    : CardImplementationStatus.DataOnly;
            return complete;
        }

        private static void ApplyWaveStrikerCondition(CardDefinition card, PrintedRuleLine line,
            int keywordCount, int ruleCount)
        {
            var conditionalKeywords = card.Abilities.Skip(keywordCount).ToArray();
            if (conditionalKeywords.Length > 0)
            {
                card.Abilities = card.Abilities.Take(keywordCount).ToArray();
                foreach (var keyword in conditionalKeywords)
                {
                    var granted = new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                        AbilityOperation.GrantKeyword)
                    {
                        RulesText = line.Text,
                        Keyword = keyword.Keyword,
                        Value = keyword.Value,
                        Selector = new RuleSelectorDefinition
                        {
                            Controller = RuleController.Source,
                            Zone = RuleZone.BattleZone,
                            Mode = RuleSelectionMode.SourceCard,
                            Minimum = 1,
                            Maximum = 1,
                            Filter = new CardFilterDefinition { CreaturesOnly = true }
                        },
                        Condition = new AbilityConditionDefinition { RequiresWaveStriker = true }
                    };
                    card.RuleAbilities = card.RuleAbilities.Concat(new[] { granted }).ToArray();
                }
            }

            foreach (var ability in card.RuleAbilities.Skip(ruleCount))
            {
                ability.Condition ??= new AbilityConditionDefinition();
                ability.Condition.RequiresWaveStriker = true;
            }
        }

        private static void ApplyTurboRushCondition(CardDefinition card, PrintedRuleLine line,
            int keywordCount, int ruleCount)
        {
            var conditionalKeywords = card.Abilities.Skip(keywordCount).ToArray();
            if (conditionalKeywords.Length > 0)
            {
                card.Abilities = card.Abilities.Take(keywordCount).ToArray();
                foreach (var keyword in conditionalKeywords)
                {
                    var granted = new RuleAbilityDefinition(line.Id, AbilityTiming.Static,
                        AbilityOperation.GrantKeyword)
                    {
                        RulesText = line.Text,
                        Keyword = keyword.Keyword,
                        Value = keyword.Value,
                        Selector = new RuleSelectorDefinition
                        {
                            Controller = RuleController.Source,
                            Zone = RuleZone.BattleZone,
                            Mode = RuleSelectionMode.SourceCard,
                            Minimum = 1,
                            Maximum = 1,
                            Filter = new CardFilterDefinition { CreaturesOnly = true }
                        },
                        Condition = new AbilityConditionDefinition
                            { AnotherCreatureBrokeShieldThisTurn = true }
                    };
                    card.RuleAbilities = card.RuleAbilities.Concat(new[] { granted }).ToArray();
                }
            }

            foreach (var ability in card.RuleAbilities.Skip(ruleCount))
            {
                ability.Condition ??= new AbilityConditionDefinition();
                ability.Condition.AnotherCreatureBrokeShieldThisTurn = true;
            }
        }

        public static bool Apply(CardDefinition card, PrintedRuleLine line)
        {
            var text = line.Text.Trim().TrimStart('■').Trim();
            if (text.StartsWith("(This creature is put into your mana zone tapped", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("(This spell is put into your mana zone tapped", StringComparison.OrdinalIgnoreCase))
                return true;
            if (Starts(text, "Double breaker")) return AddKeyword(card, CardKeyword.DoubleBreaker);
            if (Starts(text, "Triple breaker")) return AddKeyword(card, CardKeyword.TripleBreaker);
            if (Starts(text, "Blocker")) return AddKeyword(card, CardKeyword.Blocker);
            if (Starts(text, "Shield trigger")) return AddKeyword(card, CardKeyword.ShieldTrigger);
            if (Starts(text, "Speed attacker")) return AddKeyword(card, CardKeyword.SpeedAttacker);
            if (Starts(text, "Charger")) return AddKeyword(card, CardKeyword.Charger);
            if (Starts(text, "Survivor")) return AddKeyword(card, CardKeyword.Survivor);
            if (Starts(text, "Wave striker")) return AddKeyword(card, CardKeyword.WaveStriker);
            if (Starts(text, "Silent skill")) return AddKeyword(card, CardKeyword.SilentSkill);
            if (Starts(text, "Turbo rush")) return AddKeyword(card, CardKeyword.TurboRush);
            var crewBreaker = CrewBreaker.Match(text);
            if (crewBreaker.Success)
            {
                var race = crewBreaker.Groups[1].Value.Trim();
                card.CrewBreakerRace = race;
                if (race.Equals("Dragon", StringComparison.OrdinalIgnoreCase))
                {
                    card.CrewBreakerRace = string.Empty;
                    card.CrewBreakerRaceContains = "Dragon";
                }
                return AddKeyword(card, CardKeyword.CrewBreaker);
            }
            if (text.StartsWith("Instead of having this creature attack", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.TapAbility);
            var stealth = Regex.Match(text, @"^(Light|Water|Darkness|Fire|Nature) stealth", RegexOptions.IgnoreCase);
            if (stealth.Success)
            {
                card.StealthCivilization = ParseCivilization(stealth.Groups[1].Value);
                card.HasStealthCivilization = true;
                return AddKeyword(card, CardKeyword.Stealth);
            }
            var slayer = Regex.Match(text,
                @"^(?:(Light|Water|Darkness|Fire|Nature)(?: and (Light|Water|Darkness|Fire|Nature))? )?Slayer(?:\s|$)",
                RegexOptions.IgnoreCase);
            if (slayer.Success)
            {
                if (!slayer.Groups[1].Success) return AddKeyword(card, CardKeyword.Slayer);
                var civilizations = slayer.Groups[2].Success
                    ? new[]
                    {
                        ParseCivilization(slayer.Groups[1].Value),
                        ParseCivilization(slayer.Groups[2].Value)
                    }
                    : new[] { ParseCivilization(slayer.Groups[1].Value) };
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true }, false);
                ability.Keyword = CardKeyword.Slayer;
                ability.RelatedFilter = new CardFilterDefinition
                {
                    CreaturesOnly = true,
                    Civilizations = civilizations
                };
                AppendRule(card, ability);
                return true;
            }
            if (text.Equals("This creature can attack untapped creatures.", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.AttackUntappedCreature);
            if (text.Equals("This creature attacks each turn if able.", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.MustAttackIfAble);
            if (text.Equals("This creature can't attack.", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.CannotAttack);
            if (text.Equals("This creature can't attack players.", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.CannotAttackPlayers);
            if (text.Equals("This creature can't attack creatures.", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.CannotAttackCreatures);
            if (text.Equals("This creature can't be blocked.", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.Unblockable);
            if (text.Equals("This creature can't be attacked.", StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.CannotBeAttacked);
            if (text.Equals("You can put an evolution creature of any race on this creature.",
                    StringComparison.OrdinalIgnoreCase))
            {
                card.CanBeAnyEvolutionBase = true;
                return true;
            }

            var match = PowerAttacker.Match(text);
            if (match.Success) return AddKeyword(card, CardKeyword.PowerAttacker, int.Parse(match.Groups[1].Value));
            match = VortexEvolution.Match(text);
            if (match.Success)
            {
                card.IsVortexEvolution = true;
                card.EvolutionRaces = new[] { SingularRace(match.Groups[1].Value), SingularRace(match.Groups[2].Value) };
                card.EvolutionRequirements = new[]
                {
                    EvolutionRequirement(match.Groups[1].Value),
                    EvolutionRequirement(match.Groups[2].Value)
                };
                return true;
            }
            match = SingleEvolution.Match(text);
            if (match.Success)
            {
                var requirement = EvolutionRequirement(match.Groups[1].Value);
                card.EvolutionRequirements = new[] { requirement };
                card.EvolutionRaces = requirement.Races.ToArray();
                card.EvolutionRace = card.EvolutionRaces.FirstOrDefault() ??
                    string.Empty;
                return true;
            }
            if (text.Equals("At the end of each of your turns, you may untap this creature.", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("At the end of each of your turns, untap this creature.", StringComparison.OrdinalIgnoreCase))
                return AddEndTurnSelfUntap(card, line, text.IndexOf("you may", StringComparison.OrdinalIgnoreCase) >= 0);

            match = SummonDrawCards.Match(text);
            if (match.Success)
                return AddRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Draw,
                    match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1,
                    text.IndexOf("may", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("up to", StringComparison.OrdinalIgnoreCase) >= 0);
            match = DrawCards.Match(text);
            if (match.Success)
                return AddRule(card, line, DefaultActionTiming(card), AbilityOperation.Draw,
                    int.Parse(match.Groups[1].Value), text.IndexOf("up to", StringComparison.OrdinalIgnoreCase) >= 0);
            if (text.Equals("Draw a card.", StringComparison.OrdinalIgnoreCase))
                return AddRule(card, line, DefaultActionTiming(card), AbilityOperation.Draw, 1);

            match = SummonTopDeckToMana.Match(text);
            if (match.Success)
                return AddTopDeckMove(card, line, AbilityTiming.OnSummon, RuleZone.ManaZone,
                    match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1,
                    text.IndexOf("may", StringComparison.OrdinalIgnoreCase) >= 0);
            match = TopDeckToMana.Match(text);
            if (match.Success)
                return AddTopDeckMove(card, line, DefaultActionTiming(card), RuleZone.ManaZone,
                    match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1);

            if (text.Equals("Tap all your opponent's creatures in the battle zone.", StringComparison.OrdinalIgnoreCase))
                return AddSelectedRule(card, line, AbilityTiming.OnCast, AbilityOperation.Tap,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.All);
            if (Regex.IsMatch(text, @"^Choose (?:one|1) of your opponent's creatures in the battle zone and tap it\.$", RegexOptions.IgnoreCase))
                return AddSelectedRule(card, line, DefaultActionTiming(card),
                    AbilityOperation.Tap, RuleController.Opponent, RuleZone.BattleZone);
            if (text.Equals("Destroy one of your opponent's creatures.", StringComparison.OrdinalIgnoreCase))
                return AddSelectedRule(card, line, DefaultActionTiming(card), AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone);
            if (text.Equals("Destroy one of your opponent's untapped creatures.", StringComparison.OrdinalIgnoreCase))
                return AddSelectedRule(card, line, DefaultActionTiming(card), AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    new CardFilterDefinition { UntappedOnly = true, CreaturesOnly = true });
            match = DestroyPower.Match(text);
            if (match.Success)
                return AddSelectedRule(card, line, DefaultActionTiming(card), AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = int.Parse(match.Groups[1].Value) });
            match = SummonDestroyPower.Match(text);
            if (match.Success)
                return AddSelectedRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    new CardFilterDefinition { CreaturesOnly = true, PowerAtMost = int.Parse(match.Groups[1].Value) },
                    text.IndexOf("may", StringComparison.OrdinalIgnoreCase) >= 0);
            if (text.Equals("Choose a creature in the battle zone and return it to its owner's hand.", StringComparison.OrdinalIgnoreCase))
                return AddSelectedMove(card, line, DefaultActionTiming(card), RuleController.Either, RuleZone.BattleZone, RuleZone.Hand);
            return TryApplySimpleRule(card, line, text);
        }

        private static bool AddEndTurnSelfUntap(CardDefinition card, PrintedRuleLine line,
            bool optional, AbilityConditionDefinition condition = null)
        {
            var ability = new RuleAbilityDefinition(line.Id, AbilityTiming.EndOfOwnerTurn, AbilityOperation.Untap)
            {
                RulesText = line.Text, Optional = optional, Condition = condition,
                Selector = new RuleSelectorDefinition { Mode = RuleSelectionMode.SourceCard }
            };
            card.RuleAbilities = card.RuleAbilities.Concat(new[] { ability }).ToArray();
            return true;
        }

        private static bool AddKeyword(CardDefinition card, CardKeyword keyword, int value = 0)
        {
            if (card.Abilities.Any(ability => ability.Keyword == keyword && ability.Value == value)) return true;
            card.Abilities = card.Abilities.Concat(new[] { new KeywordAbility(keyword, value) }).ToArray();
            return true;
        }

        private static bool AddRule(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            AbilityOperation operation, int amount = 1, bool optional = false)
        {
            var ability = new RuleAbilityDefinition(line.Id, timing, operation)
            {
                RulesText = line.Text,
                Amount = amount,
                Optional = optional || timing == AbilityTiming.SilentSkill,
                ChooseAmount = operation == AbilityOperation.Draw &&
                    line.Text.IndexOf("up to", StringComparison.OrdinalIgnoreCase) >= 0
            };
            card.RuleAbilities = card.RuleAbilities.Concat(new[] { ability }).ToArray();
            return true;
        }

        private static bool AddTopDeckMove(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            RuleZone destination, int amount, bool optional = false)
        {
            var ability = new RuleAbilityDefinition(line.Id, timing, AbilityOperation.Move)
            {
                RulesText = line.Text,
                Amount = amount,
                Optional = optional,
                Destination = destination,
                Selector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.Deck,
                    Mode = RuleSelectionMode.Top,
                    Minimum = amount,
                    Maximum = amount
                }
            };
            card.RuleAbilities = card.RuleAbilities.Concat(new[] { ability }).ToArray();
            return true;
        }

        private static bool AddSelectedRule(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            AbilityOperation operation, RuleController controller, RuleZone zone,
            RuleSelectionMode mode = RuleSelectionMode.Choose, CardFilterDefinition filter = null, bool optional = false)
        {
            var ability = new RuleAbilityDefinition(line.Id, timing, operation)
            {
                RulesText = line.Text,
                Optional = optional,
                Selector = new RuleSelectorDefinition
                {
                    Controller = controller,
                    Zone = zone,
                    Mode = mode,
                    Minimum = mode == RuleSelectionMode.All ? 0 : 1,
                    Maximum = mode == RuleSelectionMode.All ? 0 : 1,
                    Filter = filter ?? new CardFilterDefinition { CreaturesOnly = zone == RuleZone.BattleZone }
                }
            };
            card.RuleAbilities = card.RuleAbilities.Concat(new[] { ability }).ToArray();
            return true;
        }

        private static bool AddSelectedMove(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            RuleController controller, RuleZone source, RuleZone destination)
        {
            AddSelectedRule(card, line, timing, AbilityOperation.Move, controller, source);
            card.RuleAbilities[card.RuleAbilities.Length - 1].Destination = destination;
            return true;
        }

        private static string SingularRace(string value)
        {
            value = value.Trim().TrimEnd('.');
            if (value.EndsWith("Viruses", StringComparison.OrdinalIgnoreCase))
                return value.Substring(0, value.Length - "Viruses".Length) + "Virus";
            if (value.EndsWith("XenoParts", StringComparison.OrdinalIgnoreCase))
                return value.Substring(0, value.Length - "XenoParts".Length) + "Xenoparts";
            if (value.EndsWith("People", StringComparison.OrdinalIgnoreCase)) return value;
            if (value.EndsWith("Folk", StringComparison.OrdinalIgnoreCase)) return value;
            if (value.EndsWith("Merfolk", StringComparison.OrdinalIgnoreCase)) return value;
            if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase)) return value.Substring(0, value.Length - 3) + "y";
            if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return value.Substring(0, value.Length - 1);
            return value;
        }

        private static EvolutionBaseRequirement EvolutionRequirement(string printedRace)
        {
            printedRace = printedRace.Trim().TrimEnd('.');
            if (printedRace.IndexOf("has Dragon in its race", StringComparison.OrdinalIgnoreCase) >= 0)
                return new EvolutionBaseRequirement { RaceContains = "Dragon" };
            return new EvolutionBaseRequirement
            {
                Races = printedRace.Split(new[] { " or " }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(SingularRace)
                    .ToArray()
            };
        }

        private static bool Starts(string value, string prefix) =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static AbilityTiming DefaultActionTiming(CardDefinition card)
        {
            if (card.Type == CardType.Spell) return AbilityTiming.OnCast;
            if (card.HasKeyword(CardKeyword.SilentSkill)) return AbilityTiming.SilentSkill;
            if (card.HasKeyword(CardKeyword.TapAbility)) return AbilityTiming.TapAbility;
            return AbilityTiming.OnSummon;
        }

        private static AbilityHandlerMetadata H(string id, string category, string description) =>
            new AbilityHandlerMetadata(id, category, description);
    }
}
