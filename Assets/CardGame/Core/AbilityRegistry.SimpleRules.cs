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
            if (TryDm03Rule(card, line, text)) return true;
            if (TryDm04Rule(card, line, text)) return true;
            if (TryDm05Rule(card, line, text)) return true;
            if (TryDm06Rule(card, line, text)) return true;
            if (TryDm07Rule(card, line, text)) return true;
            if (TryDm08Rule(card, line, text)) return true;
            if (TryDm09Rule(card, line, text)) return true;
            if (TryDm10Rule(card, line, text)) return true;
            if (TryDm11Rule(card, line, text)) return true;
            if (TryDm12Rule(card, line, text)) return true;
            if (TryPromoRule(card, line, text)) return true;
            if (TryCompoundOrConditionalRule(card, line, text)) return true;
            var timing = DefaultActionTiming(card);
            text = StripTimingPrefix(text, ref timing);
            if (TryReplacement(card, line, text)) return true;
            if (TryEndStepRule(card, line, text, timing)) return true;
            if (TrySimpleDraw(card, line, text, timing)) return true;
            if (TryInformationEffect(card, line, text, timing)) return true;
            if (TryAddShieldMove(card, line, text, timing)) return true;
            if (TrySimpleMove(card, line, text, timing)) return true;
            if (TrySimpleDestroy(card, line, text, timing)) return true;
            if (TrySimpleTap(card, line, text, timing)) return true;
            if (TrySimpleDiscard(card, line, text, timing)) return true;
            if (TrySimpleSearch(card, line, text, timing)) return true;
            if (TryTemporaryModifier(card, line, text, timing)) return true;
            if (TryStaticModifier(card, line, text)) return true;
            if (TryCostModifier(card, line, text)) return true;
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
                    ChooseAmount = true,
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

            if (text.StartsWith("This turn, ignore any effects that would prevent your creatures from attacking your opponent.", StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast,
                    AbilityOperation.IgnoreAttackRestrictionsThisTurn) { RulesText = line.Text });
                return true;
            }

            if (text.Equals("When this creature would be put into your graveyard from the battle zone, you may return it to your hand instead. If you do, discard a card from your hand.", StringComparison.OrdinalIgnoreCase))
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.ReplacementDestroy,
                    AbilityOperation.Move)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Destination = RuleZone.Hand
                });
                var discard = NewSelectionAbility(line,
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Discard,
                    RuleController.Source, RuleZone.Hand,
                    RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition(), false);
                // This is the second sentence of the same replacement, not a
                // competing replacement option. It happens only after the
                // optional return-to-hand replacement was actually used.
                discard.RequiresPreviousSuccess = true;
                AppendRule(card, discard);
                return true;
            }

            var castConditionalDraw = Regex.Match(text,
                @"^When you cast this spell, if your opponent has (\d+) or more creatures in the battle zone, draw (\d+) cards\.$",
                RegexOptions.IgnoreCase);
            if (castConditionalDraw.Success)
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnCast, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Amount = int.Parse(castConditionalDraw.Groups[2].Value),
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Filter = new CardFilterDefinition { CreaturesOnly = true },
                        CountAtLeast = int.Parse(castConditionalDraw.Groups[1].Value)
                    }
                });
                return true;
            }

            var eachPlayerMana = Regex.Match(text,
                @"^When this creature is destroyed, each player chooses (?:(\d+) cards|a card) in his mana zone and puts (?:them|it) into his graveyard\.$",
                RegexOptions.IgnoreCase);
            if (eachPlayerMana.Success)
            {
                var amount = eachPlayerMana.Groups[1].Success ? int.Parse(eachPlayerMana.Groups[1].Value) : 1;
                return AddMoveRule(card, line, AbilityTiming.OnDestroyed, RuleController.EachPlayer,
                    RuleZone.ManaZone, RuleZone.Graveyard, RuleSelectionMode.Choose,
                    amount, amount, new CardFilterDefinition());
            }

            var dynamicDraw = Regex.Match(text,
                @"^Whenever this creature attacks, you may draw a number of cards equal to the number of creatures your opponent has in the battle zone\.$",
                RegexOptions.IgnoreCase);
            if (dynamicDraw.Success)
            {
                AppendRule(card, new RuleAbilityDefinition(line.Id, AbilityTiming.OnAttack, AbilityOperation.Draw)
                {
                    RulesText = line.Text,
                    Optional = true,
                    Amount = 0,
                    AmountPerMatchingCard = 1,
                    AmountSelector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                });
                return true;
            }

            if (text.Equals("When this creature attacks a player, destroy it after the attack.", StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.AfterAttack, AbilityOperation.Destroy,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Whenever this creature blocks, untap it after it battles.", StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.AfterBattle, AbilityOperation.Untap,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.SourceCard,
                    1, 1, new CardFilterDefinition { CreaturesOnly = true });

            if (text.Equals("Each of your creatures in the battle zone that can attack creatures can attack untapped creatures this turn.", StringComparison.OrdinalIgnoreCase))
                return AddKeywordRule(card, line, AbilityTiming.OnCast, RuleSelectionMode.All,
                    CardKeyword.AttackUntappedCreature,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        HasForbiddenKeyword = true,
                        ForbiddenKeyword = CardKeyword.CannotAttackCreatures
                    });

            if (text.Equals("Your opponent discards all cards from his hand.", StringComparison.OrdinalIgnoreCase))
                return AddSelectionRule(card, line, AbilityTiming.OnCast, AbilityOperation.Discard,
                    RuleController.Opponent, RuleZone.Hand, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition());
            return false;
        }

        private static string StripTimingPrefix(string text, ref AbilityTiming timing)
        {
            var prefixes = new[]
            {
                new { Text = "When you put this creature into the battle zone, ", Timing = AbilityTiming.OnSummon },
                new { Text = "Whenever this creature attacks, ", Timing = AbilityTiming.OnAttack },
                new { Text = "When you cast this spell, ", Timing = AbilityTiming.OnCast },
                new { Text = "When this creature attacks a player, ", Timing = AbilityTiming.AfterAttack },
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
            var delayed = NewSelectionAbility(line, timing, AbilityOperation.Untap,
                RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                0, int.MaxValue, filter,
                text.StartsWith("you may", StringComparison.OrdinalIgnoreCase));

            // A creature is still present when its end-step ability is found.
            // A spell is not: it leaves the resolving zone immediately after
            // OnCast. Preserve the spell's delayed instruction explicitly so
            // it can fire after the source has moved to the graveyard.
            var parentTiming = DefaultActionTiming(card);
            var requiresActivation = card.Type == CardType.Spell ||
                parentTiming == AbilityTiming.TapAbility ||
                parentTiming == AbilityTiming.SilentSkill;
            if (!requiresActivation)
            {
                AppendRule(card, delayed);
                return true;
            }

            AppendRule(card, new RuleAbilityDefinition(line.Id, parentTiming,
                AbilityOperation.ScheduleAbility)
            {
                RulesText = line.Text,
                GrantedAbility = delayed,
                Duration = AbilityDuration.UntilEndOfTurn
            });
            return true;
        }

        private static bool TrySimpleDraw(CardDefinition card, PrintedRuleLine line, string text, AbilityTiming timing)
        {
            var match = Regex.Match(text, @"^(?:you may )?draw (?:(?:up to )?(\d+) cards?|a card)\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var amount = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 1;
            AppendRule(card, new RuleAbilityDefinition(line.Id, timing, AbilityOperation.Draw)
            {
                RulesText = line.Text,
                Amount = amount,
                Optional = text.StartsWith("you may", StringComparison.OrdinalIgnoreCase) ||
                           text.IndexOf("up to", StringComparison.OrdinalIgnoreCase) >= 0,
                ChooseAmount = text.IndexOf("up to", StringComparison.OrdinalIgnoreCase) >= 0
            });
            return true;
        }

        private static bool TryInformationEffect(CardDefinition card, PrintedRuleLine line, string text,
            AbilityTiming timing)
        {
            var match = Regex.Match(text,
                @"^(?:you may )?look at (?:(up to) )?(?:(\d+) (?:of )?|one of )?your opponent's shields?\. Then put (?:it|them) back where (?:it was|they were)\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var amount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            var optional = match.Groups[1].Success ||
                text.StartsWith("you may", StringComparison.OrdinalIgnoreCase);
            return AddSelectionRule(card, line, timing, AbilityOperation.LookAtCards,
                RuleController.Opponent, RuleZone.ShieldZone, RuleSelectionMode.Choose,
                optional ? 0 : amount, amount, new CardFilterDefinition(), optional);
        }

        private static bool TryCostModifier(CardDefinition card, PrintedRuleLine line, string text)
        {
            var match = Regex.Match(text,
                @"^Your (creatures|spells) each cost (\d+) less to (?:summon|cast)\. They can't cost less than (\d+)\.$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var filter = new CardFilterDefinition
            {
                CreaturesOnly = match.Groups[1].Value.StartsWith("creature", StringComparison.OrdinalIgnoreCase),
                SpellsOnly = match.Groups[1].Value.StartsWith("spell", StringComparison.OrdinalIgnoreCase)
            };
            var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyCost,
                RuleController.Source, RuleZone.None, RuleSelectionMode.All, 0, int.MaxValue, filter, false);
            ability.Value = -int.Parse(match.Groups[2].Value);
            ability.MinimumValue = int.Parse(match.Groups[3].Value);
            AppendRule(card, ability);
            return true;
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

            if (normalized.Equals("put the top card of your deck into your mana zone.", StringComparison.OrdinalIgnoreCase))
                return AddMoveRule(card, line, timing, RuleController.Source, RuleZone.Deck,
                    RuleZone.ManaZone, RuleSelectionMode.Top, 1, 1, new CardFilterDefinition(), explicitlyOptional);

            var opponentMana = Regex.Match(normalized,
                @"^Choose (?:(up to) )?(?:(\d+) cards?|a card) in your opponent's mana zone and put (?:it|them) into his graveyard\.$",
                RegexOptions.IgnoreCase);
            if (opponentMana.Success)
            {
                var count = opponentMana.Groups[2].Success ? int.Parse(opponentMana.Groups[2].Value) : 1;
                return AddMoveRule(card, line, timing, RuleController.Opponent, RuleZone.ManaZone,
                    RuleZone.Graveyard, RuleSelectionMode.Choose,
                    opponentMana.Groups[1].Success ? 0 : count, count, new CardFilterDefinition(),
                    opponentMana.Groups[1].Success);
            }

            var ownShield = Regex.Match(normalized,
                @"^Choose one of your shields(?: without looking)? and put it into your graveyard\.(?: You can't use the ""shield trigger"" ability of that shield\.)?$",
                RegexOptions.IgnoreCase);
            if (ownShield.Success)
                return AddMoveRule(card, line, timing, RuleController.Source, RuleZone.ShieldZone,
                    RuleZone.Graveyard, RuleSelectionMode.Choose, 1, 1, new CardFilterDefinition());

            var topOfDeck = Regex.Match(normalized,
                @"^Choose one of your opponent's creatures in the battle zone and put it on top of his deck\.$",
                RegexOptions.IgnoreCase);
            if (topOfDeck.Success)
                return AddMoveRule(card, line, timing, RuleController.Opponent, RuleZone.BattleZone,
                    RuleZone.Deck, RuleSelectionMode.Choose, 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true });

            var civilizationBounce = Regex.Match(normalized,
                @"^Choose one of your opponent's (light|water|darkness|fire|nature) or (light|water|darkness|fire|nature) creatures in the battle zone and return it to its owner's hand\.$",
                RegexOptions.IgnoreCase);
            if (civilizationBounce.Success)
                return AddMoveRule(card, line, timing, RuleController.Opponent, RuleZone.BattleZone,
                    RuleZone.Hand, RuleSelectionMode.Choose, explicitlyOptional ? 0 : 1, 1,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[]
                        {
                            ParseCivilization(civilizationBounce.Groups[1].Value),
                            ParseCivilization(civilizationBounce.Groups[2].Value)
                        }
                    }, explicitlyOptional);

            var moveAll = Regex.Match(normalized,
                @"^Return all (your opponent's |your )?creatures in the battle zone that (?:have power (\d+) or less|have ""blocker\.?"") to (?:their owners'|its owner's|his|your) hands?\.$",
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

            var chooseThenDestroy = Regex.Match(normalized,
                @"^choose a creature in the battle zone that has ""blocker\.?"" and destroy it\.$",
                RegexOptions.IgnoreCase);
            if (chooseThenDestroy.Success)
                return AddSelectionRule(card, line, timing, AbilityOperation.Destroy,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.Choose,
                    explicitlyOptional ? 0 : 1, 1,
                    new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true }, explicitlyOptional);

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
            if (Regex.IsMatch(normalized,
                    @"^Tap all your opponent's creatures in the battle zone that have ""blocker\.?""\.$",
                    RegexOptions.IgnoreCase))
                return AddSelectionRule(card, line, timing, AbilityOperation.Tap,
                    RuleController.Opponent, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue, new CardFilterDefinition { CreaturesOnly = true, HasBlocker = true });
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
            if (text.Equals("While attacking a creature, this creature can't be blocked.",
                    StringComparison.OrdinalIgnoreCase))
                return AddKeyword(card, CardKeyword.UnblockableWhenAttackingCreature);

            var civilizationUnblockable = Regex.Match(text,
                @"^Your (other )?(light|water|darkness|fire|nature) creatures(?: in the battle zone)? can't be blocked\.$",
                RegexOptions.IgnoreCase);
            if (civilizationUnblockable.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = civilizationUnblockable.Groups[1].Success,
                        Civilizations = new[]
                        {
                            ParseCivilization(civilizationUnblockable.Groups[2].Value)
                        }
                    }, false);
                ability.Keyword = CardKeyword.Unblockable;
                AppendRule(card, ability);
                return true;
            }

            var twoCivilizationsUnblockable = Regex.Match(text,
                @"^Your (light|water|darkness|fire|nature) creatures and (light|water|darkness|fire|nature) creatures can't be blocked\.$",
                RegexOptions.IgnoreCase);
            if (twoCivilizationsUnblockable.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Civilizations = new[]
                        {
                            ParseCivilization(twoCivilizationsUnblockable.Groups[1].Value),
                            ParseCivilization(twoCivilizationsUnblockable.Groups[2].Value)
                        }
                    }, false);
                ability.Keyword = CardKeyword.Unblockable;
                AppendRule(card, ability);
                return true;
            }

            var twoRacesUnblockable = Regex.Match(text,
                @"^Your (.+?) and (.+?) can't be blocked\.$",
                RegexOptions.IgnoreCase);
            if (twoRacesUnblockable.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Races = new[]
                        {
                            SingularRace(twoRacesUnblockable.Groups[1].Value),
                            SingularRace(twoRacesUnblockable.Groups[2].Value)
                        }
                    }, false);
                ability.Keyword = CardKeyword.Unblockable;
                AppendRule(card, ability);
                return true;
            }

            var twoRaceAura = Regex.Match(text,
                @"^Each of your (other )?(.+?) and (.+?) in the battle zone gets \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (twoRaceAura.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPower, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.All, 0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = twoRaceAura.Groups[1].Success,
                        Races = new[]
                        {
                            SingularRace(twoRaceAura.Groups[2].Value),
                            SingularRace(twoRaceAura.Groups[3].Value)
                        }
                    }, false);
                ability.Value = int.Parse(twoRaceAura.Groups[4].Value);
                AppendRule(card, ability);
                return true;
            }

            var raceAura = Regex.Match(text,
                @"^Each of your (other )?(.+?) in the battle zone gets \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (raceAura.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = raceAura.Groups[1].Success,
                        Race = SingularRace(raceAura.Groups[2].Value)
                    }, false);
                ability.Value = int.Parse(raceAura.Groups[3].Value);
                AppendRule(card, ability);
                return true;
            }

            var raceUnblockable = Regex.Match(text, @"^(.+?) can't be blocked\.$", RegexOptions.IgnoreCase);
            if (raceUnblockable.Success && !raceUnblockable.Groups[1].Value.Equals("This creature", StringComparison.OrdinalIgnoreCase))
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.GrantKeyword,
                    RuleController.Either, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        Race = SingularRace(raceUnblockable.Groups[1].Value)
                    }, false);
                ability.Keyword = CardKeyword.Unblockable;
                AppendRule(card, ability);
                return true;
            }

            var dynamicAttack = Regex.Match(text,
                @"^While attacking, this creature gets \+(\d+) power for each other (creature you have|tapped creature you have|.+?) in the battle zone\.$",
                RegexOptions.IgnoreCase);
            if (dynamicAttack.Success)
            {
                var descriptor = dynamicAttack.Groups[2].Value;
                var countFilter = new CardFilterDefinition
                {
                    CreaturesOnly = true,
                    OtherThanSource = true,
                    TappedOnly = descriptor.StartsWith("tapped creature", StringComparison.OrdinalIgnoreCase)
                };
                var civilizationCreature = Regex.Match(descriptor,
                    @"^(light|water|darkness|fire|nature) creature$", RegexOptions.IgnoreCase);
                if (civilizationCreature.Success)
                    countFilter.Civilizations = new[]
                    {
                        ParseCivilization(civilizationCreature.Groups[1].Value)
                    };
                else if (!descriptor.EndsWith("creature you have", StringComparison.OrdinalIgnoreCase))
                    countFilter.Race = SingularRace(descriptor);
                var ability = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                ability.ValuePerMatchingCard = int.Parse(dynamicAttack.Groups[1].Value);
                ability.ValueSelector = new RuleSelectorDefinition
                {
                    Controller = descriptor.EndsWith("you have", StringComparison.OrdinalIgnoreCase)
                        ? RuleController.Source
                        : RuleController.Either,
                    Zone = RuleZone.BattleZone,
                    Mode = RuleSelectionMode.All,
                    Filter = countFilter
                };
                AppendRule(card, ability);
                return true;
            }

            var tappedAura = Regex.Match(text,
                @"^While this creature is tapped, each of your other (.+?) in the battle zone gets \+(\d+) power\.$",
                RegexOptions.IgnoreCase);
            if (tappedAura.Success)
            {
                var ability = NewSelectionAbility(line, AbilityTiming.Static, AbilityOperation.ModifyPower,
                    RuleController.Source, RuleZone.BattleZone, RuleSelectionMode.All,
                    0, int.MaxValue,
                    new CardFilterDefinition
                    {
                        CreaturesOnly = true,
                        OtherThanSource = true,
                        Race = SingularRace(tappedAura.Groups[1].Value)
                    }, false);
                ability.Value = int.Parse(tappedAura.Groups[2].Value);
                ability.Condition = new AbilityConditionDefinition { SourceTapped = true };
                AppendRule(card, ability);
                return true;
            }

            var lonePower = Regex.Match(text,
                @"^While you have no other creatures in the battle zone, this creature gets ""power attacker \+(\d+)"" and ""(double breaker|triple breaker)""",
                RegexOptions.IgnoreCase);
            if (lonePower.Success)
            {
                var condition = new AbilityConditionDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.BattleZone,
                    Filter = new CardFilterDefinition { CreaturesOnly = true, OtherThanSource = true },
                    CountAtMost = 0,
                    HasCountAtMost = true
                };
                var powerAbility = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.ModifyPowerWhileAttacking, RuleController.Source,
                    RuleZone.BattleZone, RuleSelectionMode.SourceCard, 1, 1,
                    new CardFilterDefinition(), false);
                powerAbility.Value = int.Parse(lonePower.Groups[1].Value);
                powerAbility.Condition = condition;
                AppendRule(card, powerAbility);
                var breakerAbility = NewSelectionAbility(line, AbilityTiming.Static,
                    AbilityOperation.GrantKeyword, RuleController.Source, RuleZone.BattleZone,
                    RuleSelectionMode.SourceCard, 1, 1, new CardFilterDefinition(), false);
                breakerAbility.Keyword = ParseGrantedKeyword(lonePower.Groups[2].Value);
                breakerAbility.Condition = condition;
                AppendRule(card, breakerAbility);
                return true;
            }

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
