using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core
{
    public sealed class RulesEngine
    {
        private long _nextInstanceId = 1;
        private readonly AbilityResolver _abilities;

        public RulesEngine(IAbilityDecisionProvider decisions = null)
        {
            _abilities = new AbilityResolver(decisions);
        }

        public GameState StartGame(
            IReadOnlyList<CardDefinition> firstDeck,
            IReadOnlyList<CardDefinition> secondDeck,
            int seed,
            int? startingPlayerId = null)
        {
            if (firstDeck == null || secondDeck == null) throw new ArgumentNullException();
            ValidateDeck(firstDeck);
            ValidateDeck(secondDeck);

            var random = new Random(seed);
            var first = CreatePlayer(0, firstDeck, random);
            var second = CreatePlayer(1, secondDeck, random);
            var starter = startingPlayerId ?? random.Next(0, 2);

            var state = new GameState
            {
                Players = new[] { first, second },
                ActivePlayerId = starter,
                StartingPlayerId = starter,
                TurnNumber = 1,
                Phase = GamePhase.Main,
                RandomSeed = seed
            };

            DealOpeningCards(first);
            DealOpeningCards(second);
            return state;
        }

        public RuleResult ChargeMana(GameState state, int playerId, long cardInstanceId)
        {
            var turnCheck = ValidateMainAction(state, playerId);
            if (!turnCheck.Success) return turnCheck;
            if (state.ManaChargedThisTurn) return RuleResult.Fail("Only one mana charge is allowed each turn.");
            if (state.MainActionTakenThisTurn)
                return RuleResult.Fail("The mana charge step has ended for this turn.");

            var player = state.Players[playerId];
            var card = Find(player.Hand, cardInstanceId);
            if (card == null) return RuleResult.Fail("The selected card is not in the player's hand.");

            player.Hand.Remove(card);
            card.ResetTransientZoneState();
            card.Tapped = card.Definition.Civilizations.Length > 1;
            player.Mana.Add(card);
            state.ManaChargedThisTurn = true;
            return RuleResult.Ok();
        }

        public RuleResult SummonCreature(
            GameState state,
            int playerId,
            long cardInstanceId,
            IReadOnlyCollection<long> manaInstanceIds,
            long evolutionBaseInstanceId = 0)
        {
            return SummonCreatureWithEvolutionBases(state, playerId, cardInstanceId, manaInstanceIds,
                evolutionBaseInstanceId == 0 ? Array.Empty<long>() : new[] { evolutionBaseInstanceId });
        }

        public RuleResult SummonCreatureWithEvolutionBases(
            GameState state,
            int playerId,
            long cardInstanceId,
            IReadOnlyCollection<long> manaInstanceIds,
            IReadOnlyCollection<long> evolutionBaseInstanceIds)
        {
            var turnCheck = ValidateMainAction(state, playerId);
            if (!turnCheck.Success) return turnCheck;

            var player = state.Players[playerId];
            var creature = Find(player.Hand, cardInstanceId);
            if (creature == null) return RuleResult.Fail("The selected card is not in the player's hand.");
            if (creature.Definition.Type != CardType.Creature && creature.Definition.Type != CardType.EvolutionCreature)
                return RuleResult.Fail("The selected card is not a creature.");
            if (creature.Definition.ImplementationStatus == CardImplementationStatus.DataOnly)
                return RuleResult.Fail("This card's full rules are not implemented yet.");
            if (!_abilities.CanPlay(state, creature))
                return RuleResult.Fail("This creature's summon condition is not met.");

            var evolutionBases = new List<CardInstance>();
            if (creature.Definition.Type == CardType.EvolutionCreature)
            {
                var baseIds = evolutionBaseInstanceIds ?? Array.Empty<long>();
                var requirements = creature.Definition.EvolutionRequirements.Length > 0
                    ? creature.Definition.EvolutionRequirements
                    : new[] { new EvolutionBaseRequirement { Races = creature.Definition.EvolutionRaces.Length > 0
                        ? creature.Definition.EvolutionRaces
                        : new[] { creature.Definition.EvolutionRace } } };
                if (baseIds.Count != requirements.Length || baseIds.Distinct().Count() != baseIds.Count)
                    return RuleResult.Fail(creature.Definition.IsVortexEvolution
                        ? "Choose one creature for each Vortex evolution requirement."
                        : "Choose a valid creature in your battle zone to evolve.");

                foreach (var id in baseIds)
                {
                    var evolutionBase = Find(player.BattleZone, id);
                    if (evolutionBase == null) return RuleResult.Fail("Every evolution base must be in your battle zone.");
                    evolutionBases.Add(evolutionBase);
                }
                if (!CanMatchEvolutionBases(state, requirements, evolutionBases,
                        new HashSet<CardInstance>(), 0))
                    return RuleResult.Fail(
                        $"{creature.Definition.Name} requires an evolution base matching " +
                        $"{DescribeEvolutionRequirements(requirements)}.");
            }

            var payment = ValidateManaPayment(state, player, creature, manaInstanceIds);
            if (!payment.Success) return payment;

            TapMana(player, manaInstanceIds);
            state.MainActionTakenThisTurn = true;
            player.Hand.Remove(creature);
            creature.ResetTransientZoneState();
            if (evolutionBases.Count > 0)
            {
                creature.Tapped = evolutionBases.Any(card => card.Tapped);
                foreach (var evolutionBase in evolutionBases)
                {
                    foreach (var pileCard in CardAndEvolutionMaterials(evolutionBase))
                        pileCard.ResetTransientZoneState(
                            preserveBattleZoneEntryTurn: true);
                    player.BattleZone.Remove(evolutionBase);
                    creature.EvolutionMaterials.Add(evolutionBase);
                }
                creature.EnteredBattleZoneOnTurn = -1;
            }
            else
            {
                creature.Tapped = false;
                creature.EnteredBattleZoneOnTurn = state.TurnNumber;
            }
            player.BattleZone.Add(creature);
            if (_abilities.ShouldEnterBattleZoneTapped(state, creature)) creature.Tapped = true;
            ResolveSummonAbilities(state, playerId, creature);
            if (state.IsFinished) return RuleResult.Ok();
            state.CreatureSummonedThisTurn[playerId] = true;
            _abilities.ResolveCreatureEntry(state, creature, true, true);
            return RuleResult.Ok();
        }

        private bool CanMatchEvolutionBases(GameState state,
            IReadOnlyList<EvolutionBaseRequirement> requirements,
            IReadOnlyList<CardInstance> candidates,
            HashSet<CardInstance> used,
            int requirementIndex)
        {
            if (requirementIndex >= requirements.Count) return true;
            var requirement = requirements[requirementIndex];
            foreach (var candidate in candidates)
            {
                if (used.Contains(candidate)) continue;
                var matches = candidate.Definition.CanBeAnyEvolutionBase ||
                    requirement.Races.Any(race => _abilities.HasRace(state, candidate, race)) ||
                    (!string.IsNullOrEmpty(requirement.RaceContains) &&
                     _abilities.RaceContains(candidate, requirement.RaceContains));
                if (!matches) continue;
                used.Add(candidate);
                if (CanMatchEvolutionBases(state, requirements, candidates, used,
                        requirementIndex + 1)) return true;
                used.Remove(candidate);
            }
            return false;
        }

        private static string DescribeEvolutionRequirements(
            IEnumerable<EvolutionBaseRequirement> requirements)
        {
            var descriptions = new List<string>();
            foreach (var requirement in requirements ??
                     Array.Empty<EvolutionBaseRequirement>())
            {
                var alternatives = (requirement.Races ?? Array.Empty<string>())
                    .Where(race => !string.IsNullOrWhiteSpace(race)).ToList();
                if (!string.IsNullOrWhiteSpace(requirement.RaceContains))
                    alternatives.Add("a race containing " + requirement.RaceContains);
                descriptions.Add(alternatives.Count == 0
                    ? "the printed requirement"
                    : string.Join(" or ", alternatives));
            }
            return descriptions.Count == 0
                ? "the printed requirement"
                : string.Join(" and ", descriptions);
        }

        public bool SpellUsesSingleCardTarget(CardDefinition spell)
        {
            if (spell == null || spell.Type != CardType.Spell) return false;
            return spell.SpellEffects.Any(LegacySpellEffectUsesTarget) ||
                   SingleCardTargetRules(spell).Any();
        }

        public bool SpellRequiresSingleCardTarget(CardDefinition spell)
        {
            if (spell == null || spell.Type != CardType.Spell) return false;
            if (spell.SpellEffects.Any(LegacySpellEffectUsesTarget)) return true;
            return SingleCardTargetRules(spell).Any(rule =>
                !rule.Optional && rule.Selector.Minimum > 0);
        }

        public IReadOnlyList<CardInstance> GetSpellTargetCandidates(
            GameState state,
            int playerId,
            long cardInstanceId)
        {
            if (state == null || playerId < 0 || playerId >= state.Players.Length)
                return Array.Empty<CardInstance>();
            var spell = Find(state.Players[playerId].Hand, cardInstanceId) ??
                        Find(state.PendingShieldTriggers, cardInstanceId);
            if (spell == null || spell.Definition.Type != CardType.Spell)
                return Array.Empty<CardInstance>();

            var candidates = new List<CardInstance>();
            foreach (var effect in spell.Definition.SpellEffects)
                AddLegacySpellTargetCandidates(state, playerId, effect, candidates);
            foreach (var rule in SingleCardTargetRules(spell.Definition))
                candidates.AddRange(_abilities.LegalCandidatesFor(state, spell, rule));
            return candidates.Where(card => card != null)
                .GroupBy(card => card.InstanceId)
                .Select(group => group.First())
                .ToArray();
        }

        private static IEnumerable<RuleAbilityDefinition> SingleCardTargetRules(
            CardDefinition spell)
        {
            return (spell?.RuleAbilities ?? Array.Empty<RuleAbilityDefinition>())
                .Where(rule => rule != null &&
                    rule.Timing == AbilityTiming.OnCast &&
                    rule.Selector != null &&
                    rule.Selector.Mode == RuleSelectionMode.Choose &&
                    rule.Selector.Maximum == 1);
        }

        private static bool LegacySpellEffectUsesTarget(
            SpellEffectDefinition effect)
        {
            switch (effect.Kind)
            {
                case SpellEffectKind.DestroyOpponentCreature:
                case SpellEffectKind.DestroyOpponentUntappedCreature:
                case SpellEffectKind.DestroyOpponentCreatureAtMostPower:
                case SpellEffectKind.ReturnOwnCreatureFromGraveyard:
                case SpellEffectKind.GrantTemporaryPowerAttacker:
                case SpellEffectKind.GrantTemporaryDoubleBreaker:
                    return true;
                default:
                    return false;
            }
        }

        private static void AddLegacySpellTargetCandidates(
            GameState state,
            int playerId,
            SpellEffectDefinition effect,
            ICollection<CardInstance> candidates)
        {
            var player = state.Players[playerId];
            var opponent = state.Players[1 - playerId];
            switch (effect.Kind)
            {
                case SpellEffectKind.DestroyOpponentCreature:
                    foreach (var card in opponent.BattleZone) candidates.Add(card);
                    break;
                case SpellEffectKind.DestroyOpponentUntappedCreature:
                    foreach (var card in opponent.BattleZone.Where(card => !card.Tapped))
                        candidates.Add(card);
                    break;
                case SpellEffectKind.DestroyOpponentCreatureAtMostPower:
                    foreach (var card in opponent.BattleZone.Where(card =>
                                 card.Definition.Power <= effect.Value))
                        candidates.Add(card);
                    break;
                case SpellEffectKind.ReturnOwnCreatureFromGraveyard:
                    foreach (var card in player.Graveyard.Where(card =>
                                 card.Definition.IsCreature))
                        candidates.Add(card);
                    break;
                case SpellEffectKind.GrantTemporaryPowerAttacker:
                case SpellEffectKind.GrantTemporaryDoubleBreaker:
                    foreach (var card in player.BattleZone) candidates.Add(card);
                    break;
            }
        }

        public RuleResult CastSpell(
            GameState state,
            int playerId,
            long cardInstanceId,
            IReadOnlyCollection<long> manaInstanceIds,
            long targetInstanceId = 0)
        {
            var turnCheck = ValidateMainAction(state, playerId);
            if (!turnCheck.Success) return turnCheck;

            var player = state.Players[playerId];
            var spell = Find(player.Hand, cardInstanceId);
            if (spell == null) return RuleResult.Fail("The selected card is not in the player's hand.");
            if (spell.Definition.Type != CardType.Spell) return RuleResult.Fail("The selected card is not a spell.");
            if (spell.Definition.ImplementationStatus == CardImplementationStatus.DataOnly)
                return RuleResult.Fail("This card's full rules are not implemented yet.");
            if (!_abilities.CanPlay(state, spell))
                return RuleResult.Fail("This spell's cast condition is not met.");
            var targetCheck = ValidateSpellTarget(state, playerId, spell,
                targetInstanceId);
            if (!targetCheck.Success) return targetCheck;
            var payment = ValidateManaPayment(state, player, spell, manaInstanceIds);
            if (!payment.Success) return payment;

            TapMana(player, manaInstanceIds);
            state.MainActionTakenThisTurn = true;
            state.SpellCastThisTurn[playerId] = true;
            player.Hand.Remove(spell);
            spell.ResetTransientZoneState();
            player.Resolving.Add(spell);
            _abilities.ResolveSpellCast(state, spell,
                () => ResolveSpellEffects(state, playerId, spell.Definition,
                    targetInstanceId),
                preferredTargetInstanceId: targetInstanceId);
            if (state.IsFinished) return RuleResult.Ok();
            player.Resolving.Remove(spell);
            PlaceResolvedSpell(state, player, spell, false);
            return RuleResult.Ok();
        }

        public RuleResult CastShieldTrigger(
            GameState state,
            int defendingPlayerId,
            long cardInstanceId,
            long targetInstanceId = 0)
        {
            if (state.IsFinished) return RuleResult.Fail("The game is already finished.");
            if (defendingPlayerId < 0 || defendingPlayerId >= state.Players.Length)
                return RuleResult.Fail("Unknown player.");
            var player = state.Players[defendingPlayerId];
            var card = Find(state.PendingShieldTriggers, cardInstanceId);
            if (card == null || !player.Hand.Contains(card))
                return RuleResult.Fail("The selected card is not a pending shield trigger.");
            if (card.Definition.ImplementationStatus == CardImplementationStatus.DataOnly)
                return RuleResult.Fail("This card's full rules are not implemented yet.");
            if (!_abilities.CanPlay(state, card))
                return RuleResult.Fail("This card's play condition is not met.");
            if (!_abilities.CanUseShieldTrigger(state, card))
                return RuleResult.Fail("A card effect prevents this shield trigger.");
            var triggeringAttacker = FindAnywhere(state, card.BrokenByAttackerInstanceId);

            if (card.Definition.IsCreature)
            {
                var summonResult = SummonShieldTriggerCreature(state, defendingPlayerId,
                    card, targetInstanceId, triggeringAttacker);
                if (!summonResult.Success) return summonResult;
                return RuleResult.Ok();
            }
            if (card.Definition.Type != CardType.Spell)
                return RuleResult.Fail("The selected shield trigger cannot be played.");

            var targetCheck = ValidateSpellTarget(state, defendingPlayerId, card,
                targetInstanceId);
            if (!targetCheck.Success) return targetCheck;

            state.PendingShieldTriggers.Remove(card);
            state.SpellCastThisTurn[defendingPlayerId] = true;
            player.Hand.Remove(card);
            card.ResetTransientZoneState(false);
            player.Resolving.Add(card);
            _abilities.ResolveSpellCast(state, card,
                () => ResolveSpellEffects(state, defendingPlayerId,
                    card.Definition, targetInstanceId), true,
                triggeringAttacker, targetInstanceId);
            if (state.IsFinished) return RuleResult.Ok();
            player.Resolving.Remove(card);
            PlaceResolvedSpell(state, player, card, true);
            return RuleResult.Ok();
        }

        private RuleResult SummonShieldTriggerCreature(GameState state, int playerId,
            CardInstance creature, long evolutionBaseInstanceId,
            CardInstance triggeringAttacker)
        {
            var player = state.Players[playerId];
            CardInstance evolutionBase = null;
            if (creature.Definition.Type == CardType.EvolutionCreature)
            {
                var requirements = creature.Definition.EvolutionRequirements.Length > 0
                    ? creature.Definition.EvolutionRequirements
                    : new[]
                    {
                        new EvolutionBaseRequirement
                        {
                            Races = creature.Definition.EvolutionRaces.Length > 0
                                ? creature.Definition.EvolutionRaces
                                : new[] { creature.Definition.EvolutionRace }
                        }
                    };
                if (requirements.Length != 1 || evolutionBaseInstanceId == 0)
                    return RuleResult.Fail("Choose a valid creature in your battle zone to evolve.");
                evolutionBase = Find(player.BattleZone, evolutionBaseInstanceId);
                if (evolutionBase == null)
                    return RuleResult.Fail("The evolution base must be in your battle zone.");
                var requirement = requirements[0];
                var matches = evolutionBase.Definition.CanBeAnyEvolutionBase ||
                    requirement.Races.Any(race => _abilities.HasRace(state, evolutionBase, race)) ||
                    (!string.IsNullOrEmpty(requirement.RaceContains) &&
                     _abilities.RaceContains(evolutionBase, requirement.RaceContains));
                if (!matches)
                    return RuleResult.Fail(
                        $"{creature.Definition.Name} requires an evolution base matching " +
                        $"{DescribeEvolutionRequirements(requirements)}.");
            }

            state.PendingShieldTriggers.Remove(creature);
            player.Hand.Remove(creature);
            creature.ResetTransientZoneState();
            if (evolutionBase != null)
            {
                creature.Tapped = evolutionBase.Tapped;
                foreach (var pileCard in CardAndEvolutionMaterials(evolutionBase))
                    pileCard.ResetTransientZoneState(
                        preserveBattleZoneEntryTurn: true);
                player.BattleZone.Remove(evolutionBase);
                creature.EvolutionMaterials.Add(evolutionBase);
                creature.EnteredBattleZoneOnTurn = -1;
            }
            else
            {
                creature.Tapped = false;
                creature.EnteredBattleZoneOnTurn = state.TurnNumber;
            }
            player.BattleZone.Add(creature);
            if (_abilities.ShouldEnterBattleZoneTapped(state, creature)) creature.Tapped = true;
            ResolveSummonAbilities(state, playerId, creature);
            if (state.IsFinished) return RuleResult.Ok();
            state.CreatureSummonedThisTurn[playerId] = true;
            _abilities.ResolveCreatureEntry(state, creature, true, true, true,
                triggeringAttacker);
            return RuleResult.Ok();
        }

        private void PlaceResolvedSpell(GameState state, PlayerState player, CardInstance spell,
            bool castByShieldTrigger)
        {
            spell.ResetTransientZoneState();
            // Charger is the spell's own movement replacement, so it has
            // priority over battlefield replacements such as Bailas Gale.
            if (spell.Definition.HasKeyword(CardKeyword.Charger))
            {
                spell.Tapped = spell.Definition.Civilizations.Length > 1;
                player.Mana.Add(spell);
                return;
            }
            if (castByShieldTrigger && _abilities.ShieldTriggerSpellReturnsToHand(state, player.PlayerId))
            {
                spell.Tapped = false;
                player.Hand.Add(spell);
                return;
            }
            spell.Tapped = false;
            player.Graveyard.Add(spell);
        }

        public RuleResult DeclareAttack(
            GameState state,
            int playerId,
            long attackerInstanceId,
            AttackTargetKind targetKind,
            long targetCreatureInstanceId = 0)
        {
            if (state.IsFinished) return RuleResult.Fail("The game is already finished.");
            if (playerId != state.ActivePlayerId) return RuleResult.Fail("It is not this player's turn.");
            if (state.PendingAttack != null) return RuleResult.Fail("Another attack is awaiting a block decision.");
            if (state.PendingShieldTriggers.Count > 0)
                return RuleResult.Fail("Resolve pending shield triggers before declaring another attack.");

            var attacker = Find(state.ActivePlayer.BattleZone, attackerInstanceId);
            if (attacker == null) return RuleResult.Fail("The attacker is not in the active player's battle zone.");
            if (attacker.Tapped) return RuleResult.Fail("A tapped creature cannot attack.");
            var ignoresRestrictions = targetKind == AttackTargetKind.Player &&
                (state.IgnoreAttackRestrictionsThisTurn[playerId] ||
                 _abilities.HasKeyword(state, attacker, CardKeyword.IgnoreAttackRestrictionsAgainstPlayer));
            if (!ignoresRestrictions && _abilities.HasKeyword(state, attacker, CardKeyword.CannotAttack))
                return RuleResult.Fail("This creature cannot attack.");
            if (!ignoresRestrictions && !_abilities.CanAttack(state, attacker))
                return RuleResult.Fail("A card effect prevents this creature from attacking.");
            if (!ignoresRestrictions && targetKind == AttackTargetKind.Player &&
                _abilities.HasKeyword(state, attacker, CardKeyword.CannotAttackPlayers))
                return RuleResult.Fail("This creature cannot attack players.");
            if (!ignoresRestrictions && targetKind == AttackTargetKind.Player &&
                !_abilities.CanAttackPlayerByCivilization(state, attacker, state.Opponent.PlayerId))
                return RuleResult.Fail("The chosen civilization cannot attack this player this turn.");
            if (!ignoresRestrictions && targetKind == AttackTargetKind.Player &&
                !_abilities.MeetsAttackTargetRequirement(state, attacker, null))
                return RuleResult.Fail("This creature can attack only a restricted kind of creature.");
            if (targetKind == AttackTargetKind.Creature &&
                _abilities.HasKeyword(state, attacker, CardKeyword.CannotAttackCreatures))
                return RuleResult.Fail("This creature cannot attack creatures.");
            if (!ignoresRestrictions && attacker.EnteredBattleZoneOnTurn == state.TurnNumber &&
                !_abilities.HasKeyword(state, attacker, CardKeyword.SpeedAttacker))
                return RuleResult.Fail("The creature has summoning sickness.");

            if (targetKind == AttackTargetKind.Creature)
            {
                var target = Find(state.Opponent.BattleZone, targetCreatureInstanceId);
                if (target == null) return RuleResult.Fail("The target creature is not in the opponent's battle zone.");
                if (_abilities.HasKeyword(state, target, CardKeyword.CannotBeAttacked))
                    return RuleResult.Fail("That creature cannot be attacked.");
                if (!_abilities.CanAttackTarget(state, attacker, target))
                    return RuleResult.Fail("This creature cannot be attacked by that attacker.");
                if (!_abilities.MeetsAttackTargetRequirement(state, attacker, target))
                    return RuleResult.Fail("This creature can attack only a restricted kind of creature.");
                if (!target.Tapped && !_abilities.HasKeyword(state, target, CardKeyword.AttackableWhileUntapped) &&
                    !_abilities.CanAttackUntapped(state, attacker, target))
                    return RuleResult.Fail("This creature cannot attack an untapped creature.");
            }

            var preferredTargets = _abilities.PreferredAttackTargets(state, attacker)
                .Where(target =>
                    !_abilities.HasKeyword(state, target, CardKeyword.CannotBeAttacked) &&
                    (target.Tapped ||
                     _abilities.HasKeyword(state, target, CardKeyword.AttackableWhileUntapped) ||
                     _abilities.CanAttackUntapped(state, attacker, target)) &&
                    _abilities.CanAttackTarget(state, attacker, target) &&
                    _abilities.MeetsAttackTargetRequirement(state, attacker, target))
                .ToArray();
            if (preferredTargets.Length > 0 && (targetKind != AttackTargetKind.Creature ||
                                               preferredTargets.All(target => target.InstanceId != targetCreatureInstanceId)))
                return RuleResult.Fail("This attacker must attack a required creature if able.");

            state.Phase = GamePhase.Attack;
            state.RequiredBlockerInstanceId = 0;
            attacker.Tapped = true;
            attacker.ShieldsBrokenInLastAttack = 0;
            CardInstance attackedCreature = null;
            if (targetKind == AttackTargetKind.Creature)
            {
                attackedCreature = Find(state.Opponent.BattleZone,
                    targetCreatureInstanceId);
                if (attackedCreature != null) attackedCreature.WasAttackedThisTurn = true;
            }
            state.PendingAttack = new PendingAttack
            {
                AttackerInstanceId = attackerInstanceId,
                AttackerZoneChangeVersion = attacker.ZoneChangeVersion,
                TargetKind = targetKind,
                TargetCreatureInstanceId = targetCreatureInstanceId,
                TargetCreatureZoneChangeVersion = attackedCreature?.ZoneChangeVersion ?? -1,
                DefendingPlayerId = state.Opponent.PlayerId
            };
            attacker.AttackedThisTurn = true;
            ResolveAttackAbilities(state, playerId, attacker);
            if (state.IsFinished) return RuleResult.Ok();
            _abilities.ResolveAttackDeclaration(state, attacker, attackedCreature);
            if (!PendingAttackParticipantsRemain(state, state.PendingAttack))
                ConcludeInterruptedAttack(state, state.PendingAttack);
            return RuleResult.Ok();
        }

        public RuleResult UseTapAbility(GameState state, int playerId, long creatureInstanceId)
        {
            if (state.IsFinished) return RuleResult.Fail("The game is already finished.");
            if (playerId != state.ActivePlayerId) return RuleResult.Fail("It is not this player's turn.");
            if (state.PendingAttack != null) return RuleResult.Fail("Resolve the pending attack first.");
            if (state.PendingShieldTriggers.Count > 0)
                return RuleResult.Fail("Resolve pending shield triggers first.");
            var creature = Find(state.Players[playerId].BattleZone, creatureInstanceId);
            if (creature == null) return RuleResult.Fail("Choose one of your creatures in the battle zone.");
            if (creature.EnteredBattleZoneOnTurn == state.TurnNumber &&
                !_abilities.HasKeyword(state, creature, CardKeyword.SpeedAttacker))
                return RuleResult.Fail("The creature has summoning sickness and cannot use a tap ability.");
            if (!_abilities.CanDeclareAnyAttack(state, creature))
                return RuleResult.Fail("A creature that cannot legally attack cannot use a tap ability.");
            if (!_abilities.CanUseTapAbility(state, creature))
                return RuleResult.Fail("This creature does not have a tap ability.");
            state.Phase = GamePhase.Attack;
            creature.Tapped = true;
            _abilities.ResolveTapAbility(state, creature);
            return RuleResult.Ok();
        }

        public RuleResult Block(GameState state, int defendingPlayerId, long blockerInstanceId)
        {
            var attack = state.PendingAttack;
            if (attack == null) return RuleResult.Fail("There is no pending attack.");
            if (attack.DefendingPlayerId != defendingPlayerId)
                return RuleResult.Fail("This player is not defending the pending attack.");

            var defender = state.Players[defendingPlayerId];
            var blocker = Find(defender.BattleZone, blockerInstanceId);
            if (blocker == null) return RuleResult.Fail("The blocker is not in the defending battle zone.");
            if (state.RequiredBlockerInstanceId != 0 &&
                blocker.InstanceId != state.RequiredBlockerInstanceId)
                return RuleResult.Fail("A card effect requires a different creature to block this attack.");
            if (blocker.Tapped) return RuleResult.Fail("A tapped creature cannot block.");
            var attackingPlayer = state.Players[1 - defendingPlayerId];
            var attacker = Find(attackingPlayer.BattleZone, attack.AttackerInstanceId);
            if (attacker == null ||
                attacker.ZoneChangeVersion != attack.AttackerZoneChangeVersion)
            {
                ConcludeInterruptedAttack(state, attack);
                return RuleResult.Fail("The attacking creature is no longer in this attack.");
            }
            if (!_abilities.CanUseAsBlocker(state, blocker, attacker))
                return RuleResult.Fail("The selected creature does not have Blocker.");
            if (attacker != null && _abilities.HasKeyword(state, attacker, CardKeyword.Unblockable))
                return RuleResult.Fail("This attacker cannot be blocked.");
            if (attacker != null && attack.TargetKind == AttackTargetKind.Creature &&
                _abilities.HasKeyword(state, attacker, CardKeyword.UnblockableWhenAttackingCreature))
                return RuleResult.Fail("This attacker cannot be blocked while attacking a creature.");
            if (attacker != null && !_abilities.CanBlock(state, attacker, blocker))
                return RuleResult.Fail("This creature cannot block that attacker.");
            var blockerPowerLimit = attacker == null ? 0 :
                _abilities.KeywordValue(state, attacker, CardKeyword.UnblockableByPowerAtMost);
            if (blockerPowerLimit > 0 && _abilities.CurrentPower(state, blocker) <= blockerPowerLimit)
                return RuleResult.Fail($"This attacker cannot be blocked by a creature with power {blockerPowerLimit} or less.");

            if (state.BlockedCreatureGetsSlayerThisTurn[attackingPlayer.PlayerId] &&
                !attacker.TemporaryKeywords.Contains(CardKeyword.Slayer))
                attacker.TemporaryKeywords.Add(CardKeyword.Slayer);
            blocker.Tapped = true;
            var blockerZoneChangeVersion = blocker.ZoneChangeVersion;
            state.CurrentBlockerInstanceId = blocker.InstanceId;
            if (attacker != null)
                _abilities.ResolveBlockDeclaration(state, attacker, blocker);
            if (attacker != null &&
                attacker.ZoneChangeVersion == attack.AttackerZoneChangeVersion &&
                attackingPlayer.BattleZone.Contains(attacker) &&
                _abilities.BlockedAttackerBreaksShield(
                    state, attackingPlayer.PlayerId, attacker))
                ResolveShieldBreaks(state, attacker, defender, 1);
            if (state.PreventPendingBattle)
            {
                state.PreventPendingBattle = false;
                state.UntapAfterCurrentBattle.Clear();
                state.DestroyAfterCurrentBattle.Clear();
            }
            else
            {
                ResolveCreatureBattle(state, attack.AttackerInstanceId,
                    blocker.InstanceId, attack.AttackerZoneChangeVersion,
                    blockerZoneChangeVersion);
                ResolveScheduledAfterBattleEffects(state);
            }
            if (state.IsFinished)
            {
                state.PendingAttack = null;
                state.CurrentBlockerInstanceId = 0;
                state.RequiredBlockerInstanceId = 0;
                return RuleResult.Ok();
            }
            ResolveAfterAttackAbilities(state, attack);
            state.PendingAttack = null;
            state.CurrentBlockerInstanceId = 0;
            state.RequiredBlockerInstanceId = 0;
            return RuleResult.Ok();
        }

        public RuleResult DeclineBlockAndResolve(GameState state, int defendingPlayerId)
        {
            var attack = state.PendingAttack;
            if (attack == null) return RuleResult.Fail("There is no pending attack.");
            if (attack.DefendingPlayerId != defendingPlayerId)
                return RuleResult.Fail("This player is not defending the pending attack.");
            if (!PendingAttackParticipantsRemain(state, attack))
            {
                ConcludeInterruptedAttack(state, attack);
                return RuleResult.Ok();
            }

            if (state.RequiredBlockerInstanceId != 0)
            {
                var required = Find(state.Players[defendingPlayerId].BattleZone,
                    state.RequiredBlockerInstanceId);
                var attackingPlayer = state.Players[1 - defendingPlayerId];
                var attacker = Find(attackingPlayer.BattleZone, attack.AttackerInstanceId);
                if (required != null && attacker != null && !required.Tapped &&
                    _abilities.CanUseAsBlocker(state, required, attacker) &&
                    !_abilities.HasKeyword(state, attacker, CardKeyword.Unblockable) &&
                    !(attack.TargetKind == AttackTargetKind.Creature &&
                      _abilities.HasKeyword(state, attacker,
                          CardKeyword.UnblockableWhenAttackingCreature)) &&
                    _abilities.CanBlock(state, attacker, required) &&
                    CanMeetBlockerPowerLimit(state, attacker, required))
                    return RuleResult.Fail($"{required.Definition.Name} must block this attack if able.");
            }

            var attackingOwner = state.Players[1 - defendingPlayerId];
            var pendingAttacker = Find(attackingOwner.BattleZone, attack.AttackerInstanceId);
            if (pendingAttacker != null)
            {
                var mandatoryBlocker = state.Players[defendingPlayerId].BattleZone.FirstOrDefault(blocker =>
                    !blocker.Tapped &&
                    _abilities.HasKeyword(state, blocker, CardKeyword.BlockIfAble) &&
                    _abilities.CanUseAsBlocker(state, blocker, pendingAttacker) &&
                    !_abilities.HasKeyword(state, pendingAttacker, CardKeyword.Unblockable) &&
                    !(attack.TargetKind == AttackTargetKind.Creature &&
                      _abilities.HasKeyword(state, pendingAttacker,
                          CardKeyword.UnblockableWhenAttackingCreature)) &&
                    _abilities.CanBlock(state, pendingAttacker, blocker) &&
                    CanMeetBlockerPowerLimit(state, pendingAttacker, blocker));
                if (mandatoryBlocker != null)
                    return RuleResult.Fail($"{mandatoryBlocker.Definition.Name} must block this attack if able.");
            }

            if (attack.TargetKind == AttackTargetKind.Creature)
            {
                ResolveCreatureBattle(state, attack.AttackerInstanceId,
                    attack.TargetCreatureInstanceId,
                    attack.AttackerZoneChangeVersion,
                    attack.TargetCreatureZoneChangeVersion);
                ResolveScheduledAfterBattleEffects(state);
            }
            else
            {
                var attacker = Find(state.ActivePlayer.BattleZone, attack.AttackerInstanceId);
                if (attacker != null)
                    _abilities.ResolveUnblockedPlayerAttack(state, attacker);
                ResolvePlayerAttack(state, attack.AttackerInstanceId,
                    attack.AttackerZoneChangeVersion, defendingPlayerId);
            }

            if (state.IsFinished)
            {
                state.PendingAttack = null;
                state.CurrentBlockerInstanceId = 0;
                state.RequiredBlockerInstanceId = 0;
                return RuleResult.Ok();
            }
            ResolveAfterAttackAbilities(state, attack);
            state.PendingAttack = null;
            state.CurrentBlockerInstanceId = 0;
            state.RequiredBlockerInstanceId = 0;
            return RuleResult.Ok();
        }

        private bool CanMeetBlockerPowerLimit(GameState state, CardInstance attacker,
            CardInstance blocker)
        {
            var powerLimit = _abilities.KeywordValue(
                state, attacker, CardKeyword.UnblockableByPowerAtMost);
            return powerLimit <= 0 || _abilities.CurrentPower(state, blocker) > powerLimit;
        }

        private static void ResolveScheduledAfterBattleEffects(GameState state)
        {
            foreach (var reference in state.UntapAfterCurrentBattle)
                if (reference?.Card != null && reference.Matches(reference.Card) &&
                    state.Players.Any(player =>
                        player.BattleZone.Contains(reference.Card)))
                    reference.Card.Tapped = false;
            state.UntapAfterCurrentBattle.Clear();
            state.DestroyAfterCurrentBattle.Clear();
        }

        public RuleResult EndTurn(GameState state, int playerId)
        {
            if (state.IsFinished) return RuleResult.Fail("The game is already finished.");
            if (playerId != state.ActivePlayerId) return RuleResult.Fail("It is not this player's turn.");
            if (state.PendingAttack != null) return RuleResult.Fail("Resolve the pending attack first.");
            if (state.PendingShieldTriggers.Count > 0)
                return RuleResult.Fail("Resolve pending shield triggers first.");

            foreach (var creature in state.ActivePlayer.BattleZone)
            {
                // One completed attack satisfies "attacks each turn if able."
                // Untapping later permits another attack but does not renew the
                // same once-per-turn obligation.
                if (creature.AttackedThisTurn) continue;
                if (_abilities.CanDeclareAnyAttack(state, creature) &&
                    (_abilities.HasKeyword(state, creature, CardKeyword.MustAttackIfAble) ||
                     state.ForceAllCreaturesAttackNextTurn[state.ActivePlayerId]))
                    return RuleResult.Fail($"{creature.Definition.Name} must attack this turn if able.");
            }

            var endingPlayerId = state.ActivePlayerId;
            // Simultaneous end-step triggers resolve active player first.
            _abilities.ResolveEndStep(state, endingPlayerId);
            if (state.IsFinished) return RuleResult.Ok();
            foreach (var creature in state.ActivePlayer.BattleZone.ToArray())
                if (_abilities.HasKeyword(state, creature, CardKeyword.UntapAtEndOfTurn))
                    creature.Tapped = false;

            foreach (var card in state.Players.SelectMany(AllCardsIncludingEvolutionMaterials)
                         .Distinct())
            {
                card.TemporaryPowerAttacker = 0;
                card.TemporaryPower = 0;
                card.TemporaryDoubleBreaker = false;
                card.TemporaryBreakerCount = 0;
                card.TemporaryKeywords.Clear();
                card.TemporaryRaces.Clear();
                card.AttackedThisTurn = false;
            }

            foreach (var card in state.Players.SelectMany(AllCardsIncludingEvolutionMaterials)
                         .Distinct())
            {
                card.TimedKeywords.RemoveAll(effect =>
                    effect.Duration == AbilityDuration.UntilEndOfTurn);
                card.TimedRuleAbilities.RemoveAll(effect =>
                    effect.Duration == AbilityDuration.UntilEndOfTurn);
            }
            _abilities.ResolveStateBasedActions(state);
            if (state.IsFinished) return RuleResult.Ok();

            if (state.ForcedLossOwnTurnEndsRemaining[endingPlayerId] > 0)
            {
                state.ForcedLossOwnTurnEndsRemaining[endingPlayerId]--;
                if (state.ForcedLossOwnTurnEndsRemaining[endingPlayerId] == 0)
                {
                    Finish(state, 1 - endingPlayerId, "Delayed card effect");
                    return RuleResult.Ok();
                }
            }

            if (state.ExtraTurnQueue.Count > 0)
            {
                if (state.ResumePlayerAfterExtraTurns < 0)
                    state.ResumePlayerAfterExtraTurns = 1 - endingPlayerId;
                state.ActivePlayerId = state.ExtraTurnQueue[0];
                state.ExtraTurnQueue.RemoveAt(0);
            }
            else if (state.ResumePlayerAfterExtraTurns >= 0)
            {
                state.ActivePlayerId = state.ResumePlayerAfterExtraTurns;
                state.ResumePlayerAfterExtraTurns = -1;
            }
            else
            {
                state.ActivePlayerId = 1 - endingPlayerId;
            }
            state.TurnNumber++;
            state.ManaChargedThisTurn = false;
            state.MainActionTakenThisTurn = false;
            for (var playerIndex = 0; playerIndex < state.Players.Length; playerIndex++)
            {
                state.SpellCastThisTurn[playerIndex] = false;
                state.CreatureSummonedThisTurn[playerIndex] = false;
                state.ShieldsBrokenThisTurn[playerIndex] = 0;
                state.CardsDrawnThisTurn[playerIndex] = 0;
                state.BlockedCreatureGetsSlayerThisTurn[playerIndex] = false;
                state.IgnoreAttackRestrictionsThisTurn[playerIndex] = false;
                state.DestroyedCreaturesGoToManaThisTurn[playerIndex] = false;
                state.BlockedAttackerBreaksShieldCivilizationThisTurn[playerIndex] = null;
            }
            state.DestroyedRacesReturnToHandThisTurn.Clear();
            state.ForceAllCreaturesAttackNextTurn[endingPlayerId] = false;
            state.Phase = GamePhase.Main;
            _abilities.ExpireDelayedEffects(state);
            state.PreventOpponentAttackCivilizationsUntilNextTurn.RemoveAll(effect =>
                effect != null && effect.PlayerId == state.ActivePlayerId);

            foreach (var card in state.Players.SelectMany(AllCardsIncludingEvolutionMaterials)
                         .Distinct())
            {
                card.BrokeShieldThisTurn = false;
                card.WasAttackedThisTurn = false;
            }

            foreach (var card in state.Players.SelectMany(AllCardsIncludingEvolutionMaterials)
                         .Distinct())
            {
                card.TimedKeywords.RemoveAll(effect =>
                    effect.Duration == AbilityDuration.UntilStartOfOwnersNextTurn &&
                    effect.AppliedByPlayerId == state.ActivePlayerId);
                card.TimedRuleAbilities.RemoveAll(effect =>
                    effect.Duration == AbilityDuration.UntilStartOfOwnersNextTurn &&
                    effect.AppliedByPlayerId == state.ActivePlayerId);
            }

            if (_abilities.CanUntapManaAtStart(state, state.ActivePlayerId))
                foreach (var card in state.ActivePlayer.Mana) card.Tapped = false;
            var canUntapCreatures = _abilities.CanUntapCreaturesAtStart(state,
                state.ActivePlayerId);
            foreach (var card in state.ActivePlayer.BattleZone.ToArray())
            {
                var usedSilentSkill = card.Tapped && _abilities.HasKeyword(state, card, CardKeyword.SilentSkill) &&
                    _abilities.ResolveOptionalTiming(state, card, AbilityTiming.SilentSkill);
                if (!usedSilentSkill && canUntapCreatures) card.Tapped = false;
            }
            // All start-of-turn triggers occur after untapping; the new active
            // player's triggers take priority over the nonactive player's.
            _abilities.ResolveStartStep(state, state.ActivePlayerId);
            if (state.IsFinished) return RuleResult.Ok();

            var drawResult = DrawForTurn(state);
            if (!drawResult.Success || state.IsFinished) return drawResult;
            _abilities.ResolvePlayerSources(state, state.ActivePlayerId,
                AbilityTiming.AfterStartTurnDraw);
            return drawResult;
        }

        public RuleResult ResolvePendingShieldTriggers(GameState state, int defendingPlayerId)
        {
            if (defendingPlayerId < 0 || defendingPlayerId >= state.Players.Length)
                return RuleResult.Fail("Unknown player.");
            // Card-specific effects are resolved by the effect layer. Retaining candidates
            // here prevents presentation from silently skipping the shield-trigger window.
            foreach (var card in state.PendingShieldTriggers)
                card.BrokenByAttackerInstanceId = 0;
            state.PendingShieldTriggers.Clear();
            return RuleResult.Ok();
        }

        private static void ValidateDeck(IReadOnlyList<CardDefinition> deck)
        {
            if (deck.Count < 40)
                throw new ArgumentException("Each English TCG deck must contain at least 40 cards.");

            var copyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in deck)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.Name))
                    throw new ArgumentException("Decks cannot contain null or unnamed cards.");

                copyCounts.TryGetValue(card.Name, out var count);
                count++;
                if (count > 4)
                    throw new ArgumentException($"A deck cannot contain more than four copies of {card.Name}.");
                copyCounts[card.Name] = count;
            }
        }

        private PlayerState CreatePlayer(
            int playerId,
            IReadOnlyList<CardDefinition> definitions,
            Random random)
        {
            var player = new PlayerState { PlayerId = playerId };
            foreach (var definition in definitions)
            {
                player.Deck.Add(new CardInstance
                {
                    InstanceId = _nextInstanceId++,
                    Definition = definition
                });
            }

            Shuffle(player.Deck, random);
            return player;
        }

        private static void DealOpeningCards(PlayerState player)
        {
            for (var i = 0; i < 5; i++) player.Shields.Add(DrawTop(player.Deck));
            for (var i = 0; i < 5; i++) player.Hand.Add(DrawTop(player.Deck));
        }

        private static RuleResult DrawForTurn(GameState state)
        {
            if (state.ActivePlayerId == state.StartingPlayerId && state.TurnNumber == 1)
                return RuleResult.Ok();

            if (state.ActivePlayer.Deck.Count == 0)
            {
                Finish(state, state.Opponent.PlayerId, "Deck out");
                return RuleResult.Ok();
            }

            var drawn = DrawTop(state.ActivePlayer.Deck);
            drawn.ResetTransientZoneState();
            state.ActivePlayer.Hand.Add(drawn);
            state.CardsDrawnThisTurn[state.ActivePlayerId]++;
            if (state.ActivePlayer.Deck.Count == 0)
                Finish(state, state.Opponent.PlayerId, "Deck out");
            return RuleResult.Ok();
        }

        private static RuleResult ValidateMainAction(GameState state, int playerId)
        {
            if (state.IsFinished) return RuleResult.Fail("The game is already finished.");
            if (playerId != state.ActivePlayerId) return RuleResult.Fail("It is not this player's turn.");
            if (state.PendingShieldTriggers.Count > 0)
                return RuleResult.Fail("Resolve pending shield triggers before taking another action.");
            if (state.Phase != GamePhase.Main)
                return RuleResult.Fail("Cards cannot be played after the attack step has started.");
            return RuleResult.Ok();
        }

        private RuleResult ValidateManaPayment(
            GameState state,
            PlayerState player,
            CardInstance cardInstance,
            IReadOnlyCollection<long> selectedManaIds)
        {
            var card = cardInstance.Definition;
            var civilizationCount = card.Civilizations.Distinct().Count();
            // Required civilization payment takes precedence when a reduction
            // would make the payable cost smaller than the civilization count.
            var minimumCost = Math.Max(civilizationCount,
                _abilities.CurrentCost(state, cardInstance));
            var maximumCost = Math.Max(civilizationCount,
                _abilities.MaximumPayableCost(state, cardInstance));
            if (selectedManaIds == null || selectedManaIds.Count < minimumCost ||
                selectedManaIds.Count > maximumCost)
                return minimumCost == maximumCost
                    ? RuleResult.Fail($"Exactly {minimumCost} mana cards must be selected.")
                    : RuleResult.Fail($"Select between {minimumCost} and {maximumCost} mana cards.");

            if (selectedManaIds.Distinct().Count() != selectedManaIds.Count)
                return RuleResult.Fail("A mana card cannot be selected more than once.");

            var selected = new List<CardInstance>();
            foreach (var id in selectedManaIds)
            {
                var mana = Find(player.Mana, id);
                if (mana == null) return RuleResult.Fail("A selected mana card is not in the mana zone.");
                if (mana.Tapped) return RuleResult.Fail("Tapped mana cannot be used for payment.");
                selected.Add(mana);
            }

            foreach (var civilization in card.Civilizations.Distinct())
            {
                if (!selected.Any(mana => mana.Definition.Civilizations.Contains(civilization)))
                    return RuleResult.Fail(
                        $"At least one selected mana card must provide {civilization} civilization.");
            }
            var requiredCivilizations = card.Civilizations.Distinct().ToArray();
            if (!CanAssignCivilizationMana(selected, requiredCivilizations, 0,
                    new HashSet<CardInstance>()))
                return RuleResult.Fail(
                    "Each civilization on a multicolored card requires a different selected mana card.");

            return RuleResult.Ok();
        }

        private static bool CanAssignCivilizationMana(
            IReadOnlyList<CardInstance> selectedMana,
            IReadOnlyList<Civilization> civilizations,
            int civilizationIndex,
            HashSet<CardInstance> usedMana)
        {
            if (civilizationIndex >= civilizations.Count) return true;
            var civilization = civilizations[civilizationIndex];
            foreach (var mana in selectedMana)
            {
                if (usedMana.Contains(mana) ||
                    !mana.Definition.Civilizations.Contains(civilization)) continue;
                usedMana.Add(mana);
                if (CanAssignCivilizationMana(selectedMana, civilizations,
                        civilizationIndex + 1, usedMana)) return true;
                usedMana.Remove(mana);
            }
            return false;
        }

        private static void TapMana(PlayerState player, IReadOnlyCollection<long> ids)
        {
            foreach (var id in ids) Find(player.Mana, id).Tapped = true;
        }

        private RuleResult ValidateSpellTarget(
            GameState state,
            int playerId,
            CardInstance spell,
            long targetInstanceId)
        {
            foreach (var effect in spell.Definition.SpellEffects)
            {
                CardInstance target;
                switch (effect.Kind)
                {
                    case SpellEffectKind.DestroyOpponentCreature:
                        target = Find(state.Players[1 - playerId].BattleZone, targetInstanceId);
                        if (target == null) return RuleResult.Fail("Choose an opponent's creature.");
                        break;
                    case SpellEffectKind.DestroyOpponentUntappedCreature:
                        target = Find(state.Players[1 - playerId].BattleZone, targetInstanceId);
                        if (target == null || target.Tapped)
                            return RuleResult.Fail("Choose an opponent's untapped creature.");
                        break;
                    case SpellEffectKind.DestroyOpponentCreatureAtMostPower:
                        target = Find(state.Players[1 - playerId].BattleZone, targetInstanceId);
                        if (target == null || target.Definition.Power > effect.Value)
                            return RuleResult.Fail($"Choose an opponent's creature with power {effect.Value} or less.");
                        break;
                    case SpellEffectKind.ReturnOwnCreatureFromGraveyard:
                        target = Find(state.Players[playerId].Graveyard, targetInstanceId);
                        if (target == null || !target.Definition.IsCreature)
                            return RuleResult.Fail("Choose a creature in your graveyard.");
                        break;
                    case SpellEffectKind.GrantTemporaryPowerAttacker:
                    case SpellEffectKind.GrantTemporaryDoubleBreaker:
                        target = Find(state.Players[playerId].BattleZone, targetInstanceId);
                        if (target == null) return RuleResult.Fail("Choose one of your creatures.");
                        break;
                    case SpellEffectKind.DrawCards:
                    case SpellEffectKind.PutTopDeckIntoMana:
                    case SpellEffectKind.TapAllOpponentCreatures:
                        break;
                }
            }

            var targetRules = SingleCardTargetRules(spell.Definition).ToArray();
            if (targetInstanceId != 0 && targetRules.Length > 0 &&
                !targetRules.SelectMany(rule =>
                        _abilities.LegalCandidatesFor(state, spell, rule))
                    .Any(card => card.InstanceId == targetInstanceId))
                return RuleResult.Fail("Choose a legal target for this spell.");

            return RuleResult.Ok();
        }

        private void ResolveSpellEffects(
            GameState state,
            int playerId,
            CardDefinition spell,
            long targetInstanceId)
        {
            var player = state.Players[playerId];
            var opponent = state.Players[1 - playerId];
            foreach (var effect in spell.SpellEffects)
            {
                CardInstance target;
                switch (effect.Kind)
                {
                    case SpellEffectKind.DestroyOpponentCreature:
                    case SpellEffectKind.DestroyOpponentUntappedCreature:
                    case SpellEffectKind.DestroyOpponentCreatureAtMostPower:
                        target = Find(opponent.BattleZone, targetInstanceId);
                        if (target != null) Destroy(state, opponent, target);
                        break;
                    case SpellEffectKind.ReturnOwnCreatureFromGraveyard:
                        target = Find(player.Graveyard, targetInstanceId);
                        if (target != null)
                        {
                            player.Graveyard.Remove(target);
                            target.ResetTransientZoneState();
                            player.Hand.Add(target);
                        }
                        break;
                    case SpellEffectKind.RandomOpponentDiscard:
                        if (opponent.Hand.Count > 0)
                        {
                            var random = new Random(unchecked(state.RandomSeed * 397 ^ state.RandomActionIndex++));
                            var discarded = opponent.Hand[random.Next(opponent.Hand.Count)];
                            opponent.Hand.Remove(discarded);
                            discarded.ResetTransientZoneState();
                            opponent.Graveyard.Add(discarded);
                        }
                        break;
                    case SpellEffectKind.GrantTemporaryPowerAttacker:
                        target = Find(player.BattleZone, targetInstanceId);
                        if (target != null) target.TemporaryPowerAttacker += effect.Value;
                        break;
                    case SpellEffectKind.GrantTemporaryDoubleBreaker:
                        target = Find(player.BattleZone, targetInstanceId);
                        if (target != null) target.TemporaryDoubleBreaker = true;
                        break;
                    case SpellEffectKind.DrawCards:
                        DrawCards(state, player, opponent, effect.Value);
                        break;
                    case SpellEffectKind.PutTopDeckIntoMana:
                        PutTopDeckIntoMana(state, player, opponent.PlayerId, effect.Value);
                        break;
                    case SpellEffectKind.TapAllOpponentCreatures:
                        foreach (var creature in opponent.BattleZone) creature.Tapped = true;
                        break;
                }
            }
        }

        private void ResolveSummonAbilities(GameState state, int playerId, CardInstance creature)
        {
            var player = state.Players[playerId];
            var opponent = state.Players[1 - playerId];
            foreach (var effect in creature.Definition.SummonEffects)
            {
                if (state.IsFinished) return;
                switch (effect.Kind)
                {
                    case SummonEffectKind.DrawCards:
                        DrawCards(state, player, opponent, effect.Value);
                        break;
                    case SummonEffectKind.PutTopDeckIntoMana:
                        PutTopDeckIntoMana(state, player, opponent.PlayerId, effect.Value);
                        break;
                }
            }
            if (creature.Definition.RuleAbilities.Length > 0) return;
            CardInstance target;
            switch (creature.Definition.Id)
            {
                case "dm01-082": // Meteosaur
                    target = opponent.BattleZone.Where(card => card.Definition.Power <= 2000)
                        .OrderByDescending(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(state, opponent, target);
                    break;
                case "dm01-058": // Masked Horror
                    DiscardRandom(state, opponent);
                    break;
                case "dm01-061": // Stinger Worm
                    target = player.BattleZone.Where(card => card != creature)
                        .OrderBy(card => card.Definition.Power).FirstOrDefault() ?? creature;
                    Destroy(state, player, target);
                    break;
                case "dm01-062": // Swamp Worm
                    target = opponent.BattleZone.OrderBy(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(state, opponent, target);
                    break;
                case "dm01-085": // Rothus, the Traveler
                    target = player.BattleZone.Where(card => card != creature)
                        .OrderBy(card => card.Definition.Power).FirstOrDefault() ?? creature;
                    Destroy(state, player, target);
                    target = opponent.BattleZone.OrderBy(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(state, opponent, target);
                    break;
                case "dm02-033": // Poison Worm
                    target = player.BattleZone.Where(card => card != creature && card.Definition.Power <= 3000)
                        .OrderBy(card => card.Definition.Power).FirstOrDefault();
                    if (target == null && creature.Definition.Power <= 3000) target = creature;
                    if (target != null) Destroy(state, player, target);
                    break;
                case "dm02-024": // Chaos Worm
                    target = opponent.BattleZone.OrderByDescending(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(state, opponent, target);
                    break;
            }
        }

        private static void DrawCards(GameState state, PlayerState player, PlayerState opponent, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (player.Deck.Count == 0)
                {
                    Finish(state, opponent.PlayerId, "Deck out");
                    return;
                }
                var card = DrawTop(player.Deck);
                card.ResetTransientZoneState();
                player.Hand.Add(card);
                if (player.Deck.Count == 0)
                {
                    Finish(state, opponent.PlayerId, "Deck out");
                    return;
                }
            }
        }

        private static void PutTopDeckIntoMana(
            GameState state,
            PlayerState player,
            int opponentPlayerId,
            int count)
        {
            for (var i = 0; i < count && player.Deck.Count > 0; i++)
            {
                var card = DrawTop(player.Deck);
                card.ResetTransientZoneState();
                card.Tapped = card.Definition.Civilizations.Length > 1;
                player.Mana.Add(card);
                if (player.Deck.Count == 0)
                {
                    Finish(state, opponentPlayerId, "Deck out");
                    return;
                }
            }
        }

        private void ResolveAttackAbilities(GameState state, int playerId, CardInstance attacker)
        {
            if (attacker.Definition.RuleAbilities.Length > 0) return;
            var player = state.Players[playerId];
            var opponent = state.Players[1 - playerId];
            CardInstance target;
            switch (attacker.Definition.Id)
            {
                case "dm02-023": // Amber Piercer
                    target = player.Graveyard.FirstOrDefault(card => card.Definition.IsCreature);
                    if (target != null)
                    {
                        player.Graveyard.Remove(target);
                        target.ResetTransientZoneState();
                        player.Hand.Add(target);
                    }
                    break;
                case "dm02-027": // General Dark Fiend
                    if (player.Shields.Count > 0)
                    {
                        var shield = player.Shields[player.Shields.Count - 1];
                        player.Shields.RemoveAt(player.Shields.Count - 1);
                        shield.ResetTransientZoneState();
                        player.Graveyard.Add(shield);
                    }
                    break;
                case "dm02-030": // Horrid Worm
                    DiscardRandom(state, opponent);
                    break;
                case "dm02-042": // Metalwing Skyterror
                    target = opponent.BattleZone.FirstOrDefault(card => card.Definition.HasKeyword(CardKeyword.Blocker)) ??
                        player.BattleZone.FirstOrDefault(card => card != attacker && card.Definition.HasKeyword(CardKeyword.Blocker));
                    if (target != null)
                    {
                        var owner = opponent.BattleZone.Contains(target) ? opponent : player;
                        Destroy(state, owner, target);
                    }
                    break;
            }
        }

        private void ResolveAfterAttackAbilities(GameState state, PendingAttack attack)
        {
            var eventAttacker = FindAnywhere(state, attack.AttackerInstanceId);
            if (eventAttacker != null)
                _abilities.ResolveAttackFinished(state, eventAttacker,
                    attack.TargetKind == AttackTargetKind.Player,
                    1 - attack.DefendingPlayerId,
                    attack.AttackerZoneChangeVersion);
            var owner = FindOwner(state, attack.AttackerInstanceId);
            var attacker = owner == null
                ? null
                : Find(owner.BattleZone, attack.AttackerInstanceId);
            if (attack.TargetKind == AttackTargetKind.Player && attacker != null &&
                attacker.ZoneChangeVersion == attack.AttackerZoneChangeVersion &&
                attacker.Definition.RuleAbilities.Length == 0 &&
                attacker.Definition.Id == "dm02-032")
                Destroy(state, owner, attacker);
        }

        private static bool PendingAttackParticipantsRemain(GameState state,
            PendingAttack attack)
        {
            if (state == null || attack == null || attack.DefendingPlayerId < 0 ||
                attack.DefendingPlayerId >= state.Players.Length) return false;
            var attackingPlayerId = 1 - attack.DefendingPlayerId;
            var attacker = Find(state.Players[attackingPlayerId].BattleZone,
                attack.AttackerInstanceId);
            if (attacker == null ||
                attacker.ZoneChangeVersion != attack.AttackerZoneChangeVersion)
                return false;
            if (attack.TargetKind != AttackTargetKind.Creature) return true;
            var target = Find(state.Players[attack.DefendingPlayerId].BattleZone,
                attack.TargetCreatureInstanceId);
            return target != null &&
                target.ZoneChangeVersion == attack.TargetCreatureZoneChangeVersion;
        }

        private void ConcludeInterruptedAttack(GameState state, PendingAttack attack)
        {
            if (state == null || attack == null) return;
            if (!state.IsFinished) ResolveAfterAttackAbilities(state, attack);
            state.PendingAttack = null;
            state.CurrentBlockerInstanceId = 0;
            state.RequiredBlockerInstanceId = 0;
        }

        private int AttackPower(GameState state, PlayerState owner, CardInstance attacker)
        {
            var bonus = 0;
            if (attacker.Definition.RuleAbilities.Length == 0) switch (attacker.Definition.Id)
            {
                case "dm01-067":
                    if (owner.BattleZone.Any(card => card.Definition.Races.Contains("Human"))) bonus += 2000;
                    break;
                case "dm01-077":
                    if (owner.BattleZone.Any(card => card.Definition.Races.Contains("Armorloid"))) bonus += 2000;
                    break;
                case "dm02-034":
                    bonus += owner.BattleZone.Count(card => card != attacker && card.Definition.Races.Contains("Human")) * 2000;
                    break;
                case "dm02-039":
                    bonus += owner.BattleZone.Count(card => card != attacker && card.Tapped) * 2000;
                    break;
                case "dm02-041":
                    if (owner.BattleZone.Count == 1) bonus += 4000;
                    break;
            }
            return _abilities.CurrentPower(state, attacker, true) + bonus;
        }

        private int BreakerCount(GameState state, PlayerState owner, CardInstance attacker)
        {
            if (attacker.Definition.RuleAbilities.Length == 0 &&
                attacker.Definition.Id == "dm02-041" && owner.BattleZone.Count == 1) return 2;
            var count = 1;
            if (attacker.TemporaryBreakerCount > 0) count = attacker.TemporaryBreakerCount;
            if (_abilities.HasKeyword(state, attacker, CardKeyword.TripleBreaker)) count = Math.Max(count, 3);
            if (_abilities.HasKeyword(state, attacker, CardKeyword.DoubleBreaker)) count = Math.Max(count, 2);
            if (_abilities.HasKeyword(state, attacker, CardKeyword.CrewBreaker))
            {
                var crew = owner.BattleZone.Count(card => card != attacker &&
                    ((!string.IsNullOrEmpty(attacker.Definition.CrewBreakerRace) &&
                      (attacker.Definition.CrewBreakerRace.Equals("Survivor", StringComparison.OrdinalIgnoreCase)
                          ? _abilities.HasKeyword(state, card, CardKeyword.Survivor)
                          : _abilities.HasRace(card, attacker.Definition.CrewBreakerRace))) ||
                     (!string.IsNullOrEmpty(attacker.Definition.CrewBreakerRaceContains) &&
                      _abilities.RaceContains(card, attacker.Definition.CrewBreakerRaceContains))));
                count = Math.Max(count, 1 + crew);
            }
            return count + _abilities.AdditionalBreakerCount(state, attacker);
        }

        private static void DiscardRandom(GameState state, PlayerState player)
        {
            if (player.Hand.Count == 0) return;
            var random = new Random(unchecked(state.RandomSeed * 397 ^ state.RandomActionIndex++));
            var discarded = player.Hand[random.Next(player.Hand.Count)];
            player.Hand.Remove(discarded);
            discarded.ResetTransientZoneState();
            player.Graveyard.Add(discarded);
        }

        private void ResolveCreatureBattle(GameState state, long attackerId,
            long defenderId, int attackerZoneChangeVersion,
            int defenderZoneChangeVersion)
        {
            var attackerOwner = FindOwner(state, attackerId);
            var defenderOwner = FindOwner(state, defenderId);
            if (attackerOwner == null || defenderOwner == null) return;

            var attacker = Find(attackerOwner.BattleZone, attackerId);
            var defender = Find(defenderOwner.BattleZone, defenderId);
            if (attacker == null || defender == null ||
                attacker.ZoneChangeVersion != attackerZoneChangeVersion ||
                defender.ZoneChangeVersion != defenderZoneChangeVersion) return;

            state.CurrentBattleAttackerInstanceId = attackerId;
            state.CurrentBattleDefenderInstanceId = defenderId;
            // "Whenever this creature battles" abilities, including Slayer,
            // trigger before powers are compared. Snapshot the delayed keyword
            // effects now so they still resolve if their source later leaves.
            var attackerSlayer = _abilities.HasKeyword(state, attacker, CardKeyword.Slayer);
            var defenderSlayer = _abilities.HasKeyword(state, defender, CardKeyword.Slayer);
            var destroyAttackerAfterBattle = _abilities.HasKeyword(state, attacker,
                CardKeyword.DestroyAfterBattle) ||
                state.DestroyAfterCurrentBattle.Any(reference =>
                    reference != null && reference.Matches(attacker));
            var destroyDefenderAfterBattle = _abilities.HasKeyword(state, defender,
                CardKeyword.DestroyAfterBattle) ||
                state.DestroyAfterCurrentBattle.Any(reference =>
                    reference != null && reference.Matches(defender));
            _abilities.ResolveBattleStart(state, attacker, defender);

            var battleContinues = attackerOwner.BattleZone.Contains(attacker) &&
                defenderOwner.BattleZone.Contains(defender) &&
                attacker.ZoneChangeVersion == attackerZoneChangeVersion &&
                defender.ZoneChangeVersion == defenderZoneChangeVersion;
            if (battleContinues && !state.IsFinished)
            {
                var attackerPower = AttackPower(state, attackerOwner, attacker);
                var defenderPower = _abilities.CurrentPower(state, defender);
                var attackerLost = attackerPower <= defenderPower;
                var defenderLost = defenderPower <= attackerPower;
                _abilities.ResolveBattleOutcome(state, attacker, defender,
                    attackerLost, defenderLost);
            }

            // Slayer, self-destruction, and printed after-battle abilities are
            // one trigger window. Their sources and abilities were fixed when
            // the battle happened, even if either creature has since left.
            if (!state.IsFinished)
                _abilities.ResolveBattleAftermath(state, attacker, defender,
                    attackerZoneChangeVersion, defenderZoneChangeVersion,
                    attackerSlayer, defenderSlayer,
                    destroyAttackerAfterBattle, destroyDefenderAfterBattle);
            state.CurrentBattleAttackerInstanceId = 0;
            state.CurrentBattleDefenderInstanceId = 0;
        }

        private void ResolvePlayerAttack(GameState state, long attackerId,
            int attackerZoneChangeVersion, int defendingPlayerId)
        {
            var attacker = Find(state.ActivePlayer.BattleZone, attackerId);
            var defender = state.Players[defendingPlayerId];
            if (attacker == null ||
                attacker.ZoneChangeVersion != attackerZoneChangeVersion) return;

            if (defender.Shields.Count == 0)
            {
                Finish(state, state.ActivePlayerId, "Direct attack");
                return;
            }

            var breakerCount = BreakerCount(state, state.ActivePlayer, attacker);
            if (attacker.TemporaryDoubleBreaker) breakerCount = Math.Max(2, breakerCount);
            ResolveShieldBreaks(state, attacker, defender, breakerCount);
        }

        private void ResolveShieldBreaks(GameState state, CardInstance attacker,
            PlayerState defender, int requestedCount)
        {
            var shieldsToBreak = Math.Min(requestedCount, defender.Shields.Count);
            var selectedShields = _abilities.ChooseShieldsToBreak(state, attacker,
                defender.PlayerId, shieldsToBreak);
            foreach (var selectedShield in selectedShields)
            {
                if (state.IsFinished) break;
                if (defender.Shields.Count == 0) break;
                if (!defender.Shields.Contains(selectedShield)) continue;
                var shield = selectedShield;
                var replacement = _abilities.ResolveShieldBreakReplacement(
                    state, attacker, shield, defender.PlayerId);
                if (replacement == ShieldBreakReplacementResult.Prevented) continue;
                defender.Shields.Remove(shield);
                shield.ResetTransientZoneState();
                if (replacement == ShieldBreakReplacementResult.AttackerCastsShieldTrigger)
                {
                    state.InspectionPlayerId = state.ActivePlayerId;
                    state.InspectedCards.Clear();
                    state.InspectedCards.Add(shield);
                    if (shield.Definition.Type == CardType.Spell &&
                        _abilities.HasKeyword(state, shield, CardKeyword.ShieldTrigger))
                    {
                        state.ActivePlayer.Resolving.Add(shield);
                        state.SpellCastThisTurn[state.ActivePlayerId] = true;
                        _abilities.ResolveSpellCast(state, shield,
                            () => ResolveSpellEffects(state,
                                state.ActivePlayerId, shield.Definition, 0));
                        if (state.IsFinished) break;
                        state.ActivePlayer.Resolving.Remove(shield);
                        shield.ResetTransientZoneState();
                        if (_abilities.HasKeyword(state, shield, CardKeyword.Charger))
                        {
                            shield.Tapped = shield.Definition.Civilizations.Length > 1;
                            defender.Mana.Add(shield);
                        }
                        else
                        {
                            shield.Tapped = false;
                            defender.Graveyard.Add(shield);
                        }
                    }
                    else
                    {
                        defender.Hand.Add(shield);
                        if (_abilities.HasKeyword(state, shield,
                                CardKeyword.ShieldTrigger) &&
                            _abilities.CanUseShieldTrigger(state, shield) &&
                            !state.PendingShieldTriggers.Contains(shield))
                        {
                            shield.BrokenByAttackerInstanceId = attacker.InstanceId;
                            state.PendingShieldTriggers.Add(shield);
                        }
                    }
                    attacker.BrokeShieldThisTurn = true;
                    attacker.ShieldsBrokenInLastAttack++;
                    state.ShieldsBrokenThisTurn[state.ActivePlayerId]++;
                    continue;
                }
                var sendToGraveyard = replacement ==
                    ShieldBreakReplacementResult.MoveToGraveyard;
                if (sendToGraveyard) defender.Graveyard.Add(shield);
                else defender.Hand.Add(shield);
                attacker.BrokeShieldThisTurn = true;
                attacker.ShieldsBrokenInLastAttack++;
                state.ShieldsBrokenThisTurn[state.ActivePlayerId]++;
                if (!sendToGraveyard && _abilities.HasKeyword(state, shield, CardKeyword.ShieldTrigger))
                    if (_abilities.CanUseShieldTrigger(state, shield))
                    {
                        shield.BrokenByAttackerInstanceId = attacker.InstanceId;
                        state.PendingShieldTriggers.Add(shield);
                    }
            }
        }

        private void Destroy(GameState state, PlayerState owner, CardInstance card)
        {
            _abilities.DestroyCard(state, card);
        }

        private static PlayerState FindOwner(GameState state, long cardInstanceId)
        {
            foreach (var player in state.Players)
            {
                if (Find(player.BattleZone, cardInstanceId) != null) return player;
            }

            return null;
        }

        private static CardInstance Find(IEnumerable<CardInstance> cards, long id)
        {
            return cards.FirstOrDefault(card => card.InstanceId == id);
        }

        private static CardInstance FindAnywhere(GameState state, long id) =>
            state.Players.SelectMany(AllCards).FirstOrDefault(card => card.InstanceId == id);

        private static IEnumerable<CardInstance> AllCards(PlayerState player) =>
            player.Deck.Concat(player.Hand).Concat(player.Shields).Concat(player.Mana)
                .Concat(player.BattleZone).Concat(player.Graveyard).Concat(player.Resolving);

        private static IEnumerable<CardInstance> AllCardsIncludingEvolutionMaterials(
            PlayerState player) => AllCards(player).SelectMany(CardAndEvolutionMaterials);

        private static IEnumerable<CardInstance> CardAndEvolutionMaterials(CardInstance card)
        {
            yield return card;
            foreach (var material in card.EvolutionMaterials)
                foreach (var nested in CardAndEvolutionMaterials(material))
                    yield return nested;
        }

        private static CardInstance DrawTop(List<CardInstance> deck)
        {
            var index = deck.Count - 1;
            var card = deck[index];
            deck.RemoveAt(index);
            return card;
        }

        private static void Shuffle<T>(IList<T> list, Random random)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        private static void Finish(GameState state, int winnerPlayerId, string reason)
        {
            state.WinnerPlayerId = winnerPlayerId;
            state.WinReason = reason;
            state.Phase = GamePhase.Finished;
        }
    }
}
