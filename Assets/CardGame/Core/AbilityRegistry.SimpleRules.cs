using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CardGame.Core
{
    public static partial class AbilityRegistry
    {
        private static readonly Regex SimpleMove = new Regex(
            @"^(?:Put|Return) (?:(up to) )?(?:(\d+) |a |one of your |one of )?(card|cards|creature|creatures|spell) (?:from )?(your )?(opponent's )?(battle zone|hand|mana zone|graveyard) (?:into|to) (?:your |its owner's |their owners' |his )?(hand|mana zone|graveyard|battle zone)\.$",
            RegexOptions.IgnoreCase);

        private static bool TryApplySimpleRule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (TryCompoundOrConditionalRule(card, line, text)) return true;
            var timing = DefaultActionTiming(card);
            text = StripTimingPrefix(text, ref timing);
            if (TryReplacement(card, line, text)) return true;
            if (TryEndStepRule(card, line, text, timing)) return true;
            if (TryAddShieldMove(card, line, text, timing)) return true;
            if (TrySimpleMove(card, line, text, timing)) return true;
            if (TrySimpleDestroy(card, line, text, timing)) return true;
            if (TrySimpleTap(card, line, text, timing)) return true;
            if (TrySimpleDiscard(card, line, text, timing)) return true;
            if (TrySimpleSearch(card, line, text, timing)) return true;
            if (TryTemporaryModifier(card, line, text, timing)) return true;
            if (TryStaticModifier(card, line, text)) return true;
            return false;
        }

        private static bool TryCompoundOrConditionalRule(CardDefinition card, PrintedRuleLine line, string text)
        {
            if (text.Equals("When you put this creature into the battle zone, destroy one of your creatures. Then your opponent chooses one of his creatures and destroys it.", StringComparison.OrdinalIgnoreCase))
            {
                AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });
                AddSelectionRule(card, line, AbilityTiming.OnSummon, AbilityOperation.Destroy,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true }, false,
                    RuleController.Opponent);
                return true;
            }

            var conditionalDraw = Regex.Match(text,
                @"^When you put this creature into the battle zone, if you have (\d+|a) (.+?) in the battle zone, draw up to (\d+) cards\.$",
                RegexOptions.IgnoreCase);
            if (conditionalDraw.Success)
            {
                var ability = new RuleAbilityDefinition(line.Id, AbilityTiming.OnSummon, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Amount = int.Parse(conditionalDraw.Groups[3].Value),
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Race = SingularRace(conditionalDraw.Groups[2].Value)
                        },
                        CountAtLeast = conditionalDraw.Groups[1].Value.Equals("a", StringComparison.OrdinalIgnoreCase)
                            ? 1 : int.Parse(conditionalDraw.Groups[1].Value)
                    }
                };
                AppendRule(card, ability);
                return true;
            }

            if (text.StartsWith("Whenever any of your creatures becomes blocked this turn, it gets \"slayer\"", StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.GrantBlockedCreatureSlayerThisTurn) { RulesText = line.Text });
                return true;
            }
            return false;
        }

        private static string StripTimingPrefix(string text, ref AbilityTiming timing)
        {
            var prefixes = new[]
            {
                new { Text = "When you put this creature into the battle zone, ", Timing = AbilityTiming.OnSummon },
                new { Text = "Whenever this creature attacks, ", Timing = AbilityTiming.OnAttack },
                new { Text = "When this creature is destroyed, ", Timing = AbilityTiming.OnDestroyed },
                new { Text = "When this creature wins a battle, ", Timing = AbilityTiming.OnBattleWon },
                new { Text = "Whenever this creature battles, ", Timing = AbilityTiming.OnBattle },
                new { Text = "Whenever this creature becomes blocked, ", Timing = AbilityTiming.OnBlocked },
                new { Text = "Whenever this creature blocks, ", Timing = AbilityTiming.OnBlock },
                new { Text = "At the end of each of your turns, ", Timing = AbilityTiming.EndOfOwnerTurn },
                new { Text = "At the end of the turn, ", Timing = AbilityTiming.EndOfOwnerTurn }
            };
            foreach (var prefix in prefixes)
            {
                if (!text.StartsWith(prefix.Text, StringComparison.OrdinalIgnoreCase)) continue;
                timing = prefix.Timing;
                return text.Substring(prefix.Text.Length);
            }
            return text;
        }

        private static bool TryReplacement(CardDefinition card, PrintedRuleLine line, string text)
        {
            var match = Regex.Match(text,
                @"^When this creature would be destroyed, (?:put|return) it (?:to|into) your (hand|mana zone) instead\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
            {
                RulesText = line.Text,
                Destination = ParseZone(match.Groups[1].Value)
            });
            return true;
        }

        private static bool TryEndStepRule(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var match = Regex.Match(text,
                @"^(?:you may )?untap (all|any number of) your(?: (light))? creatures(?: in the battle zone)?\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success || timing != AbilityTiming.EndOfOwnerTurn) return false;
            var filter = new CardFilterDefinition { CreaturesOnly = true };
            if (match.Groups[2].Success) filter.Civilizations = new[] { Civilization.Light };
            return AddSelectionRule(card, line, timing, AbilityOperation.Untap, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue, filter,
                text.StartsWith("you may", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryAddShieldMove(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var normalized = text;
            var explicitlyOptional = normalized.StartsWith("you may ", StringComparison.OrdinalIgnoreCase);
            if (explicitlyOptional) normalized = normalized.Substring("you may ".Length);
            var match = Regex.Match(normalized,
                @"^Add (?:(up to) )?(?:(\d+) )?(?:a card|one of your creatures|the top card of your deck|cards from the top of your deck)(?: from (your hand|your mana zone|the battle zone|the top of your deck))? to your shields face down\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var source = RuleZone.Deck;
            var controller = RuleController.Source;
            var mode = RuleSelectionMode.Top;
            var filter = new CardFilterDefinition();
            var phrase = match.Groups[3].Value.ToLowerInvariant();
            if (normalized.IndexOf("one of your creatures", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                source = RuleZone.BattleZone;
                mode = RuleSelectionMode.Choose;
                filter.CreaturesOnly = true;
            }
            else if (phrase.Contains("hand")) { source = RuleZone.Hand; mode = RuleSelectionMode.Choose; }
            else if (phrase.Contains("mana")) { source = RuleZone.ManaZone; mode = RuleSelectionMode.Choose; }
            var amount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            return AddMoveRule(card, line, timing, controller, source, RuleZone.ShieldZone, mode,
                match.Groups[1].Success || explicitlyOptional ? 0 : amount, amount, filter,
                match.Groups[1].Success || explicitlyOptional);
        }

        private static bool TrySimpleMove(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var normalized = text.Replace("1 of your", "one of your");
            var explicitlyOptional = normalized.StartsWith("you may ", StringComparison.OrdinalIgnoreCase);
            if (explicitlyOptional) normalized = normalized.Substring("you may ".Length);
            var opponentChooses = normalized.StartsWith("your opponent chooses ", StringComparison.OrdinalIgnoreCase);
            if (opponentChooses) normalized = "Choose " + normalized.Substring("your opponent chooses ".Length);

            var moveAll = Regex.Match(normalized,
                @"^Return all (your opponent's |your )?creatures in the battle zone that (?:have power (\d+) or less|have ""blocker\.?"") to (?:their owners'|its owner's|his|your) hand\.$",
                RegexOptions.IgnoreCase);
            if (moveAll.Success)
            {
                var allFilter = new CardFilterDefinition
                {
                    CreaturesOnly = true,
                    PowerAtMost = moveAll.Groups[2].Success ? int.Parse(moveAll.Groups[2].Value) : 0,
                    HasBlocker = normalized.IndexOf("blocker", StringComparison.OrdinalIgnoreCase) >= 0
                };
                var allController = moveAll.Groups[1].Value.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0
                    ? RuleController.Opponent
                    : moveAll.Groups[1].Value.Trim().Equals("your", StringComparison.OrdinalIgnoreCase)
                        ? RuleController.Source : RuleController.Either;
                return AddMoveRule(card, line, timing, allController, RuleZone.BattleZone,
                    RuleZone.Hand, RuleSelectionMode.All, 0, int.MaxValue, allFilter);
            }
            var match = Regex.Match(normalized,
                @"^(?:Put|Return) (?:(up to) )?(?:(\d+)|one|a)? ?(?:of )?(your opponent's|your)? ?(cards?|creatures?|spells?) (?:from )?(?:the |your )?(battle zone|hand|mana zone|graveyard) (?:into|to) (?:your |his |their owners' |its owner's )?(hand|mana zone|graveyard|battle zone)\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(normalized,
                    @"^(?:Choose )(?:(up to) )?(?:(\d+)|one|a)? ?(?:of )?(your opponent's|your|his)? ?(cards?|creatures?) (?:in|from) (?:the |your |his )?(battle zone|hand|mana zone|graveyard) and (?:put|puts|return|returns) (?:it|them|those cards) (?:into|to) (?:your |his |their owners' |its owner's )?(hand|hands|mana zone|graveyard|battle zone)\.$",
                    RegexOptions.IgnoreCase);
            }
            if (!match.Success)
            {
                // Keep compatibility with the short canonical wording handled by the
                // original parser while the broader forms above cover numbered choices.
                var legacy = SimpleMove.Match(normalized);
                if (!legacy.Success) return false;
                var legacyAmount = legacy.Groups[2].Success ? int.Parse(legacy.Groups[2].Value) : 1;
                var legacyNoun = legacy.Groups[3].Value.ToLowerInvariant();
                return AddMoveRule(card, line, timing,
                    legacy.Groups[5].Success ? RuleController.Opponent : RuleController.Source,
                    ParseZone(legacy.Groups[6].Value), ParseZone(legacy.Groups[7].Value),
                    RuleSelectionMode.Choose, legacy.Groups[1].Success ? 0 : legacyAmount, legacyAmount,
                    FilterForNoun(legacyNoun), legacy.Groups[1].Success);
            }

            var amount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            var optional = match.Groups[1].Success || explicitlyOptional;
            var noun = match.Groups[4].Value.ToLowerInvariant();
            var controller = match.Groups[3].Value.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             match.Groups[3].Value.Equals("his", StringComparison.OrdinalIgnoreCase)
                ? RuleController.Opponent
                : !match.Groups[3].Success && ParseZone(match.Groups[5].Value) == RuleZone.BattleZone
                    ? RuleController.Either
                    : RuleController.Source;
            var chooser = opponentChooses ? RuleController.Opponent : RuleController.Source;
            var ability = NewSelectionAbility(line, timing, AbilityOperation.Move, controller,
                ParseZone(match.Groups[5].Value), RuleSelectionMode.Choose, optional ? 0 : amount,
                amount, FilterForNoun(noun), optional);
            ability.Destination = ParseZone(match.Groups[6].Value);
            ability.Chooser = chooser;
            AppendRule(card, ability);
            return true;
        }

        private static CardFilterDefinition FilterForNoun(string noun) => new CardFilterDefinition
        {
            CreaturesOnly = noun.StartsWith("creature", StringComparison.OrdinalIgnoreCase),
            SpellsOnly = noun.StartsWith("spell", StringComparison.OrdinalIgnoreCase)
        };

        private static bool TrySimpleDestroy(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var normalized = text;
            var explicitlyOptional = normalized.StartsWith("you may ", StringComparison.OrdinalIgnoreCase);
            if (explicitlyOptional) normalized = normalized.Substring("you may ".Length);
            var opponentChooses = normalized.StartsWith("your opponent chooses and destroys ", StringComparison.OrdinalIgnoreCase) ||
                                  normalized.StartsWith("your opponent chooses ", StringComparison.OrdinalIgnoreCase);
            if (normalized.StartsWith("your opponent chooses and destroys ", StringComparison.OrdinalIgnoreCase))
                normalized = "Destroy " + normalized.Substring("your opponent chooses and destroys ".Length);
            else if (normalized.StartsWith("your opponent chooses ", StringComparison.OrdinalIgnoreCase))
                normalized = "Destroy " + normalized.Substring("your opponent chooses ".Length).Replace(" and destroys it", string.Empty);

            if (normalized.Equals("destroy it.", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("destroy this creature.", StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, timing, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });

            var sacrifice = Regex.Match(normalized,
                @"^Destroy (\d+) of your creatures or destroy this creature\.$", RegexOptions.IgnoreCase);
            if (sacrifice.Success)
            {
                var count = int.Parse(sacrifice.Groups[1].Value);
                var ability = NewSelectionAbility(line, timing, AbilityOperation.DestroyOrSource,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    count, count, new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true }, false);
                ability.Amount = count;
                AppendRule(card, ability);
                return true;
            }

            var match = Regex.Match(normalized,
                @"^Destroy (?:(up to) )?(?:(\d+) (?:of )?|a |one of )?(all |any number of )?(your opponent's |your |his )?(other )?creatures?(?: in the battle zone)?(?: that (?:has|have) (?:""blocker\.?""|“blocker\.?”))?(?: and power| that (?:has|have) power)?(?: (\d+)( or less)?)?\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var filter = new CardFilterDefinition
            {
                CreaturesOnly = true,
                OtherThanSource = match.Groups[5].Success,
                HasBlocker = normalized.IndexOf("blocker", StringComparison.OrdinalIgnoreCase) >= 0,
                PowerAtMost = match.Groups[6].Success && match.Groups[7].Success ? int.Parse(match.Groups[6].Value) : 0,
                PowerExactly = match.Groups[6].Success && !match.Groups[7].Success ? int.Parse(match.Groups[6].Value) : 0
            };
            var controller = normalized.IndexOf("your opponent", StringComparison.OrdinalIgnoreCase) >= 0 || opponentChooses
                ? RuleController.Opponent
                : normalized.IndexOf("your creatures", StringComparison.OrdinalIgnoreCase) >= 0
                    ? RuleController.Source
                    : RuleController.Either;
            var all = match.Groups[3].Success && match.Groups[3].Value.StartsWith("all", StringComparison.OrdinalIgnoreCase);
            var any = match.Groups[3].Success && match.Groups[3].Value.StartsWith("any", StringComparison.OrdinalIgnoreCase);
            var amount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            return AddSelectionRule(card, line, timing, AbilityOperation.Destroy, controller, RuleZone.BattleZone,
                all ? RuleSelectionMode.All : RuleSelectionMode.Choose,
                any || match.Groups[1].Success || explicitlyOptional ? 0 : amount,
                all || any ? int.MaxValue : amount, filter,
                any || match.Groups[1].Success || explicitlyOptional,
                opponentChooses ? RuleController.Opponent : RuleController.Source);
        }

        private static bool TrySimpleTap(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var normalized = text;
            var explicitlyOptional = normalized.StartsWith("you may ", StringComparison.OrdinalIgnoreCase);
            if (explicitlyOptional) normalized = normalized.Substring("you may ".Length);
            var match = Regex.Match(normalized,
                @"^(?:Choose )?(?:(up to) )?(?:(\d+) (?:of )?|one of )?(all )?(your opponent's |your )?(?:untapped )?creatures? in the battle zone(?: and)? tap (?:it|them)\.$|^Tap all (your opponent's )?creatures in the battle zone\.$",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var controller = normalized.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0
                ? RuleController.Opponent : RuleController.Source;
                var all = normalized.StartsWith("Tap all", StringComparison.OrdinalIgnoreCase) || match.Groups[3].Success;
                var amount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
                return AddSelectionRule(card, line, timing, AbilityOperation.Tap, controller, RuleZone.BattleZone,
                    all ? RuleSelectionMode.All : RuleSelectionMode.Choose,
                    match.Groups[1].Success || explicitlyOptional ? 0 : amount,
                    all ? int.MaxValue : amount,
                    new CardFilterDefinition { CreaturesOnly = true }, match.Groups[1].Success || explicitlyOptional);
            }

            var untap = Regex.Match(normalized,
                @"^Untap (all|up to (\d+) of|one of) your(?: (light))? creatures(?: in the battle zone)?\.$",
                RegexOptions.IgnoreCase);
            if (!untap.Success) return false;
            var untapAll = untap.Groups[1].Value.Equals("all", StringComparison.OrdinalIgnoreCase);
            var untapCount = untap.Groups[2].Success ? int.Parse(untap.Groups[2].Value) : 1;
            var filter = new CardFilterDefinition { CreaturesOnly = true };
            if (untap.Groups[3].Success) filter.Civilizations = new[] { Civilization.Light };
            return AddSelectionRule(card, line, timing, AbilityOperation.Untap, RuleController.Source,
                RuleZone.BattleZone, untapAll ? RuleSelectionMode.All : RuleSelectionMode.Choose,
                explicitlyOptional ? 0 : untapCount, untapAll ? int.MaxValue : untapCount,
                filter, explicitlyOptional);
        }

        private static bool TrySimpleDiscard(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var random = Regex.Match(text,
                @"^Your opponent discards (?:(\d+) cards|a card) at random from (?:his|their) hand\.$",
                RegexOptions.IgnoreCase);
            if (random.Success)
            {
                var amount = random.Groups[1].Success ? int.Parse(random.Groups[1].Value) : 1;
                return AddSelectionRule(card, line, timing, AbilityOperation.Discard, RuleController.Opponent,
                    RuleZone.Hand, RuleSelectionMode.Random, amount, amount, null);
            }
            var chosen = Regex.Match(text,
                @"^Your opponent chooses and discards (?:(\d+) cards|a card) from his hand\.$",
                RegexOptions.IgnoreCase);
            if (!chosen.Success) return false;
            var count = chosen.Groups[1].Success ? int.Parse(chosen.Groups[1].Value) : 1;
            return AddSelectionRule(card, line, timing, AbilityOperation.Discard, RuleController.Opponent,
                RuleZone.Hand, RuleSelectionMode.Choose, count, count, null, false, RuleController.Opponent);
        }

        private static bool TrySimpleSearch(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var normalized = text;
            var optionalSearch = normalized.StartsWith("you may ", StringComparison.OrdinalIgnoreCase);
            if (optionalSearch) normalized = normalized.Substring("you may ".Length);
            var match = Regex.Match(normalized,
                @"^Search your deck\. You may take (a|one|up to \d+|any number of) (card|creature|spell|multicolored creature|non-nature card|light card|water card|darkness card|fire card|nature card) from your deck(?:, show (?:that card|that creature|that spell|those cards) to your opponent,?)? and put (?:it|them) into your (hand|mana zone)\. Then shuffle your deck\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var noun = match.Groups[2].Value.ToLowerInvariant();
            var filter = new CardFilterDefinition
            {
                CreaturesOnly = noun.Contains("creature"),
                SpellsOnly = noun == "spell"
            };
            if (noun == "multicolored creature")
            {
                filter.CreaturesOnly = true;
                filter.MulticoloredOnly = true;
            }
            if (noun == "non-nature card") filter.ExcludedCivilizations = new[] { Civilization.Nature };
            foreach (Civilization civilization in Enum.GetValues(typeof(Civilization)))
                if (noun.Equals(civilization.ToString() + " card", StringComparison.OrdinalIgnoreCase))
                    filter.Civilizations = new[] { civilization };
            var maximum = match.Groups[1].Value.Equals("any number of", StringComparison.OrdinalIgnoreCase)
                ? int.MaxValue
                : match.Groups[1].Value.StartsWith("up to ", StringComparison.OrdinalIgnoreCase)
                    ? int.Parse(match.Groups[1].Value.Substring("up to ".Length))
                    : 1;
            return AddMoveRule(card, line, timing, RuleController.Source, RuleZone.Deck,
                ParseZone(match.Groups[3].Value), RuleSelectionMode.Choose, 0, maximum, filter, true, true);
        }

        private static bool TryTemporaryModifier(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var normalized = text;
            var explicitlyOptional = normalized.StartsWith("you may ", StringComparison.OrdinalIgnoreCase);
            if (explicitlyOptional) normalized = normalized.Substring("you may ".Length);

            var treatedAsTapped = Regex.Match(normalized,
                @"^Choose (?:one|1) of your opponent's untapped creatures in the battle zone\. Your creatures can attack it this turn as though it were tapped\.$",
                RegexOptions.IgnoreCase);
            if (treatedAsTapped.Success)
                return AddKeywordRule(card, line, timing, RuleSelectionMode.Choose,
                    CardKeyword.AttackableWhileUntapped,
                    new CardFilterDefinition { CreaturesOnly = true, UntappedOnly = true },
                    RuleController.Opponent);

            var unblockableChoices = Regex.Match(normalized,
                @"^(?:Choose )?(?:(up to) (\d+) of |one of )your creatures(?: in the battle zone)?\. (?:It|They) can't be blocked this turn\.$",
                RegexOptions.IgnoreCase);
            if (unblockableChoices.Success)
            {
                var maximum = unblockableChoices.Groups[2].Success ? int.Parse(unblockableChoices.Groups[2].Value) : 1;
                return AddKeywordRule(card, line, timing, RuleSelectionMode.Choose, CardKeyword.Unblockable,
                    new CardFilterDefinition { CreaturesOnly = true }, RuleController.Source,
                    unblockableChoices.Groups[1].Success || explicitlyOptional ? 0 : maximum, maximum,
                    unblockableChoices.Groups[1].Success || explicitlyOptional);
            }

            var power = Regex.Match(normalized,
                @"^(One of|Each of) your (fire |water |darkness |light |nature )?creatures(?: in the battle zone)? gets \+(\d+) power until the end of the turn\.$",
                RegexOptions.IgnoreCase);
            if (power.Success)
            {
                var all = power.Groups[1].Value.StartsWith("Each", StringComparison.OrdinalIgnoreCase);
                var filter = new CardFilterDefinition { CreaturesOnly = true };
                if (power.Groups[2].Success) filter.Civilizations = new[] { ParseCivilization(power.Groups[2].Value) };
                return AddValueRule(card, line, timing, AbilityOperation.ModifyPower,
                    all ? RuleSelectionMode.All : RuleSelectionMode.Choose, int.Parse(power.Groups[3].Value),
                    filter);
            }

            var powerAttacker = Regex.Match(normalized,
                @"^(One of|Each of) your (fire |water |darkness |light |nature )?creatures?(?: in the battle zone)? gets ""power attacker \+(\d+)""(?: and ""(double breaker|triple breaker)"")? until the end of the turn",
                RegexOptions.IgnoreCase);
            if (powerAttacker.Success)
            {
                var all = powerAttacker.Groups[1].Value.StartsWith("Each", StringComparison.OrdinalIgnoreCase);
                var filter = new CardFilterDefinition { CreaturesOnly = true };
                if (powerAttacker.Groups[2].Success)
                    filter.Civilizations = new[] { ParseCivilization(powerAttacker.Groups[2].Value) };
                AddValueRule(card, line, timing, AbilityOperation.ModifyPowerWhileAttacking,
                    all ? RuleSelectionMode.All : RuleSelectionMode.Choose,
                    int.Parse(powerAttacker.Groups[3].Value), filter);
                if (powerAttacker.Groups[4].Success)
                    AddKeywordRule(card, line, timing,
                        all ? RuleSelectionMode.All : RuleSelectionMode.Choose,
                        ParseGrantedKeyword(powerAttacker.Groups[4].Value), filter);
                return true;
            }

            var keyword = Regex.Match(normalized,
                @"^(One of|Each of) your (fire |water |darkness |light |nature )?creatures(?: in the battle zone)? gets ""?(?:This creature )?(can't be blocked|speed attacker|slayer|double breaker|triple breaker)""? until the end of the turn",
                RegexOptions.IgnoreCase);
            if (!keyword.Success) return false;
            var allTargets = keyword.Groups[1].Value.StartsWith("Each", StringComparison.OrdinalIgnoreCase);
            var cardKeyword = ParseGrantedKeyword(keyword.Groups[3].Value);
            var keywordFilter = new CardFilterDefinition { CreaturesOnly = true };
            if (keyword.Groups[2].Success)
                keywordFilter.Civilizations = new[] { ParseCivilization(keyword.Groups[2].Value) };
            return AddKeywordRule(card, line, timing, allTargets ? RuleSelectionMode.All : RuleSelectionMode.Choose,
                cardKeyword, keywordFilter);
        }

        private static bool TryStaticModifier(CardDefinition card, PrintedRuleLine line, string text)
        {
            var blockLimit = Regex.Match(text,
                @"^This creature can't be blocked by any creature that has power (\d+) or less\.$",
                RegexOptions.IgnoreCase);
            if (blockLimit.Success)
                return AddKeyword(card, CardKeyword.UnblockableByPowerAtMost,
                    int.Parse(blockLimit.Groups[1].Value));

            if (text.Equals("When this creature wins a battle, destroy it.", StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnBattleWon, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });

            var conditionalUnblockable = Regex.Match(text,
                @"^This creature can't be blocked while you have at least (\d+) other creatures in the battle zone\.$",
                RegexOptions.IgnoreCase);
            if (conditionalUnblockable.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.GrantKeyword,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                ability.Keyword = CardKeyword.Unblockable;
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Filter = new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true },
                    CountAtLeast = int.Parse(conditionalUnblockable.Groups[1].Value)
                };
                AppendRule(card, ability);
                return true;
            }

            var racePower = Regex.Match(text,
                @"^While you have at least (\d+) (.+?) in the battle zone, this creature gets \+(\d+) power(?: during its attacks)?\.$",
                RegexOptions.IgnoreCase);
            if (racePower.Success)
            {
                var attackingOnly = text.IndexOf("during its attacks", StringComparison.OrdinalIgnoreCase) >= 0;
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    attackingOnly ? AbilityOperation.ModifyPowerWhileAttacking : AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition(), false);
                ability.Value = int.Parse(racePower.Groups[3].Value);
                ability.Condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Filter = new CardFilterDefinition { CreaturesOnly = true, Race = SingularRace(racePower.Groups[2].Value) },
                    CountAtLeast = int.Parse(racePower.Groups[1].Value)
                };
                AppendRule(card, ability);
                return true;
            }

            var graveyardPower = Regex.Match(text,
                @"^While attacking, this creature gets \+(\d+) power for each (light|water|darkness|fire|nature) card in your graveyard\.$",
                RegexOptions.IgnoreCase);
            if (!graveyardPower.Success) return false;
            var dynamicAbility = NewSelectionAbility(line, AbilityTiming.Static,
                AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                new CardFilterDefinition(), false);
            dynamicAbility.ValuePerMatchingCard = int.Parse(graveyardPower.Groups[1].Value);
            dynamicAbility.ValueSelector = new RuleSelectorDefinition
            {
                Controller = RuleController.Source,
                Zone = RuleZone.Graveyard,
                Mode = RuleSelectionMode.All,
                Filter = new CardFilterDefinition
                {
                    Civilizations = new[] { ParseCivilization(graveyardPower.Groups[2].Value) }
                }
            };
            AppendRule(card, dynamicAbility);
            return true;
        }

        private static bool AddMoveRule(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            RuleController controller, RuleZone source, RuleZone destination, RuleSelectionMode mode,
            int minimum, int maximum, CardFilterDefinition filter, bool optional = false, bool shuffle = false)
        {
            var ability = NewSelectionAbility(line, timing, AbilityOperation.Move, controller, source, mode,
                minimum, maximum, filter, optional);
            ability.Destination = destination;
            ability.ShuffleAfter = shuffle;
            AppendRule(card, ability);
            return true;
        }

        private static bool AddSelectionRule(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            AbilityOperation operation, RuleController controller, RuleZone zone, RuleSelectionMode mode,
            int minimum, int maximum, CardFilterDefinition filter, bool optional = false,
            RuleController chooser = RuleController.Source)
        {
            var ability = NewSelectionAbility(line, timing, operation, controller, zone, mode,
                minimum, maximum, filter, optional);
            ability.Chooser = chooser;
            AppendRule(card, ability);
            return true;
        }

        private static bool AddValueRule(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            AbilityOperation operation, RuleSelectionMode mode, int value, CardFilterDefinition filter)
        {
            var ability = NewSelectionAbility(line, timing, operation, RuleController.Source,
                RuleZone.BattleZone, mode, mode == RuleSelectionMode.All ? 0 : 1,
                mode == RuleSelectionMode.All ? int.MaxValue : 1, filter, false);
            ability.Value = value;
            ability.Duration = AbilityDuration.UntilEndOfTurn;
            AppendRule(card, ability);
            return true;
        }

        private static bool AddKeywordRule(CardDefinition card, PrintedRuleLine line, AbilityTiming timing,
            RuleSelectionMode mode, CardKeyword keyword, CardFilterDefinition filter,
            RuleController controller = RuleController.Source, int minimum = -1, int maximum = -1,
            bool optional = false)
        {
            if (minimum < 0) minimum = mode == RuleSelectionMode.All ? 0 : 1;
            if (maximum < 0) maximum = mode == RuleSelectionMode.All ? int.MaxValue : 1;
            var ability = NewSelectionAbility(line, timing, AbilityOperation.GrantKeyword, controller,
                RuleZone.BattleZone, mode, minimum, maximum, filter, optional);
            ability.Keyword = keyword;
            ability.Duration = AbilityDuration.UntilEndOfTurn;
            AppendRule(card, ability);
            return true;
        }

        private static RuleAbilityDefinition NewSelectionAbility(PrintedRuleLine line, AbilityTiming timing,
            AbilityOperation operation, RuleController controller, RuleZone zone, RuleSelectionMode mode,
            int minimum, int maximum, CardFilterDefinition filter, bool optional)
        {
            return new RuleAbilityDefinition(line.Id, timing, operation)
            {
                RulesText = line.Text,
                Optional = optional || timing == AbilityTiming.SilentSkill,
                Selector = new RuleSelectorDefinition
                {
                    Controller = controller,
                    Zone = zone,
                    Mode = mode,
                    Minimum = minimum,
                    Maximum = maximum,
                    Filter = filter ?? new CardFilterDefinition()
                }
            };
        }

        private static void AppendRule(CardDefinition card, RuleAbilityDefinition ability) =>
            card.RuleAbilities = card.RuleAbilities.Concat(new[] { ability }).ToArray();

        private static RuleZone ParseZone(string value)
        {
            value = value.Trim().ToLowerInvariant();
            if (value.Contains("battle")) return RuleZone.BattleZone;
            if (value.Contains("hand")) return RuleZone.Hand;
            if (value.Contains("mana")) return RuleZone.ManaZone;
            if (value.Contains("grave")) return RuleZone.Graveyard;
            if (value.Contains("shield")) return RuleZone.ShieldZone;
            return RuleZone.Deck;
        }

        private static CardKeyword ParseGrantedKeyword(string value)
        {
            value = value.ToLowerInvariant();
            if (value.Contains("speed")) return CardKeyword.SpeedAttacker;
            if (value.Contains("slayer")) return CardKeyword.Slayer;
            if (value.Contains("double")) return CardKeyword.DoubleBreaker;
            if (value.Contains("triple")) return CardKeyword.TripleBreaker;
            return CardKeyword.Unblockable;
        }

        private static Civilization ParseCivilization(string value)
        {
            value = value.Trim();
            if (value.Equals("light", StringComparison.OrdinalIgnoreCase)) return Civilization.Light;
            if (value.Equals("water", StringComparison.OrdinalIgnoreCase)) return Civilization.Water;
            if (value.Equals("darkness", StringComparison.OrdinalIgnoreCase)) return Civilization.Darkness;
            if (value.Equals("fire", StringComparison.OrdinalIgnoreCase)) return Civilization.Fire;
            return Civilization.Nature;
        }
    }
}
