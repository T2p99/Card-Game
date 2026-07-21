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

            var player = state.Players[playerId];
            var card = Find(player.Hand, cardInstanceId);
            if (card == null) return RuleResult.Fail("The selected card is not in the player's hand.");

            player.Hand.Remove(card);
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
                var unmatched = new List<CardInstance>(evolutionBases);
                foreach (var requirement in requirements)
                {
                    var matchingBase = unmatched.FirstOrDefault(candidate => candidate.Definition.Races.Any(candidateRace =>
                        requirement.Races.Any(race => candidateRace.Equals(race, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(requirement.RaceContains) &&
                         candidateRace.IndexOf(requirement.RaceContains, StringComparison.OrdinalIgnoreCase) >= 0)));
                    if (matchingBase == null)
                        return RuleResult.Fail($"{creature.Definition.Name} does not have the required evolution base.");
                    unmatched.Remove(matchingBase);
                }
            }

            var payment = ValidateManaPayment(player, creature.Definition, manaInstanceIds);
            if (!payment.Success) return payment;

            TapMana(player, manaInstanceIds);
            player.Hand.Remove(creature);
            if (evolutionBases.Count > 0)
            {
                creature.Tapped = evolutionBases.Any(card => card.Tapped);
                foreach (var evolutionBase in evolutionBases)
                {
                    player.BattleZone.Remove(evolutionBase);
                    creature.EvolutionMaterials.AddRange(evolutionBase.EvolutionMaterials);
                    evolutionBase.EvolutionMaterials.Clear();
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
            ResolveSummonAbilities(state, playerId, creature);
            state.CreatureSummonedThisTurn[playerId] = true;
            _abilities.Resolve(state, creature, AbilityTiming.OnSummon);
            return RuleResult.Ok();
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

            var targetCheck = ValidateSpellTarget(state, playerId, spell.Definition, targetInstanceId);
            if (!targetCheck.Success) return targetCheck;
            var payment = ValidateManaPayment(player, spell.Definition, manaInstanceIds);
            if (!payment.Success) return payment;

            TapMana(player, manaInstanceIds);
            state.SpellCastThisTurn[playerId] = true;
            _abilities.Resolve(state, spell, AbilityTiming.OnCast);
            player.Hand.Remove(spell);
            ResolveSpellEffects(state, playerId, spell.Definition, targetInstanceId);
            PlaceResolvedSpell(player, spell);
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
            if (defendingPlayerId == state.ActivePlayerId)
                return RuleResult.Fail("Only the defending player can cast a pending shield trigger.");

            var player = state.Players[defendingPlayerId];
            var spell = Find(state.PendingShieldTriggers, cardInstanceId);
            if (spell == null || !player.Hand.Contains(spell))
                return RuleResult.Fail("The selected card is not a pending shield trigger.");
            if (spell.Definition.Type != CardType.Spell)
                return RuleResult.Fail("Creature shield triggers are not supported yet.");
            if (spell.Definition.ImplementationStatus == CardImplementationStatus.DataOnly)
                return RuleResult.Fail("This card's full rules are not implemented yet.");

            var targetCheck = ValidateSpellTarget(state, defendingPlayerId, spell.Definition, targetInstanceId);
            if (!targetCheck.Success) return targetCheck;

            state.PendingShieldTriggers.Remove(spell);
            state.SpellCastThisTurn[defendingPlayerId] = true;
            _abilities.Resolve(state, spell, AbilityTiming.OnCast);
            player.Hand.Remove(spell);
            ResolveSpellEffects(state, defendingPlayerId, spell.Definition, targetInstanceId);
            PlaceResolvedSpell(player, spell);
            return RuleResult.Ok();
        }

        private static void PlaceResolvedSpell(PlayerState player, CardInstance spell)
        {
            if (spell.Definition.HasKeyword(CardKeyword.Charger))
            {
                spell.Tapped = spell.Definition.Civilizations.Length > 1;
                player.Mana.Add(spell);
            }
            else
            {
                spell.Tapped = false;
                player.Graveyard.Add(spell);
            }
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
            if (_abilities.HasKeyword(state, attacker, CardKeyword.CannotAttack))
                return RuleResult.Fail("This creature cannot attack.");
            if (targetKind == AttackTargetKind.Player &&
                _abilities.HasKeyword(state, attacker, CardKeyword.CannotAttackPlayers))
                return RuleResult.Fail("This creature cannot attack players.");
            if (targetKind == AttackTargetKind.Creature &&
                _abilities.HasKeyword(state, attacker, CardKeyword.CannotAttackCreatures))
                return RuleResult.Fail("This creature cannot attack creatures.");
            if (attacker.EnteredBattleZoneOnTurn == state.TurnNumber &&
                !_abilities.HasKeyword(state, attacker, CardKeyword.SpeedAttacker))
                return RuleResult.Fail("The creature has summoning sickness.");

            if (targetKind == AttackTargetKind.Creature)
            {
                var target = Find(state.Opponent.BattleZone, targetCreatureInstanceId);
                if (target == null) return RuleResult.Fail("The target creature is not in the opponent's battle zone.");
                if (_abilities.HasKeyword(state, target, CardKeyword.CannotBeAttacked))
                    return RuleResult.Fail("That creature cannot be attacked.");
                if (!target.Tapped && !_abilities.HasKeyword(state, target, CardKeyword.AttackableWhileUntapped) &&
                    !_abilities.HasKeyword(state, attacker, CardKeyword.AttackUntappedCreature))
                    return RuleResult.Fail("This creature cannot attack an untapped creature.");
            }

            state.Phase = GamePhase.Attack;
            attacker.Tapped = true;
            state.PendingAttack = new PendingAttack
            {
                AttackerInstanceId = attackerInstanceId,
                TargetKind = targetKind,
                TargetCreatureInstanceId = targetCreatureInstanceId,
                DefendingPlayerId = state.Opponent.PlayerId
            };
            attacker.AttackedThisTurn = true;
            ResolveAttackAbilities(state, playerId, attacker);
            _abilities.Resolve(state, attacker, AbilityTiming.OnAttack);
            return RuleResult.Ok();
        }

        public RuleResult UseTapAbility(GameState state, int playerId, long creatureInstanceId)
        {
            var turnCheck = ValidateMainAction(state, playerId);
            if (!turnCheck.Success) return turnCheck;
            var creature = Find(state.Players[playerId].BattleZone, creatureInstanceId);
            if (creature == null) return RuleResult.Fail("Choose one of your creatures in the battle zone.");
            if (creature.Tapped) return RuleResult.Fail("A tapped creature cannot use a tap ability.");
            if (!_abilities.HasKeyword(state, creature, CardKeyword.TapAbility))
                return RuleResult.Fail("This creature does not have a tap ability.");
            creature.Tapped = true;
            _abilities.Resolve(state, creature, AbilityTiming.TapAbility);
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
            if (blocker.Tapped) return RuleResult.Fail("A tapped creature cannot block.");
            if (!_abilities.HasKeyword(state, blocker, CardKeyword.Blocker))
                return RuleResult.Fail("The selected creature does not have Blocker.");
            var attackingPlayer = state.Players[1 - defendingPlayerId];
            var attacker = Find(attackingPlayer.BattleZone, attack.AttackerInstanceId);
            if (attacker != null && state.BlockedCreatureGetsSlayerThisTurn[attackingPlayer.PlayerId] &&
                !attacker.TemporaryKeywords.Contains(CardKeyword.Slayer))
                attacker.TemporaryKeywords.Add(CardKeyword.Slayer);
            if (attacker != null && _abilities.HasKeyword(state, attacker, CardKeyword.Unblockable))
                return RuleResult.Fail("This attacker cannot be blocked.");
            var blockerPowerLimit = attacker == null ? 0 :
                _abilities.KeywordValue(state, attacker, CardKeyword.UnblockableByPowerAtMost);
            if (blockerPowerLimit > 0 && _abilities.CurrentPower(state, blocker) <= blockerPowerLimit)
                return RuleResult.Fail($"This attacker cannot be blocked by a creature with power {blockerPowerLimit} or less.");

            blocker.Tapped = true;
            if (attacker != null) _abilities.Resolve(state, attacker, AbilityTiming.OnBlocked);
            _abilities.Resolve(state, blocker, AbilityTiming.OnBlock);
            ResolveCreatureBattle(state, attack.AttackerInstanceId, blocker.InstanceId);
            ResolveAfterAttackAbilities(state, attack);
            state.PendingAttack = null;
            return RuleResult.Ok();
        }

        public RuleResult DeclineBlockAndResolve(GameState state, int defendingPlayerId)
        {
            var attack = state.PendingAttack;
            if (attack == null) return RuleResult.Fail("There is no pending attack.");
            if (attack.DefendingPlayerId != defendingPlayerId)
                return RuleResult.Fail("This player is not defending the pending attack.");

            if (attack.TargetKind == AttackTargetKind.Creature)
            {
                ResolveCreatureBattle(state, attack.AttackerInstanceId, attack.TargetCreatureInstanceId);
            }
            else
            {
                var attacker = Find(state.ActivePlayer.BattleZone, attack.AttackerInstanceId);
                if (attacker != null) _abilities.Resolve(state, attacker, AbilityTiming.OnAttackPlayerUnblocked);
                ResolvePlayerAttack(state, attack.AttackerInstanceId, defendingPlayerId);
            }

            ResolveAfterAttackAbilities(state, attack);
            state.PendingAttack = null;
            return RuleResult.Ok();
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
                var timingAllowsAttack = !creature.Tapped &&
                    (creature.EnteredBattleZoneOnTurn != state.TurnNumber ||
                     _abilities.HasKeyword(state, creature, CardKeyword.SpeedAttacker));
                var canAttackPlayer = !_abilities.HasKeyword(state, creature, CardKeyword.CannotAttack) &&
                    !_abilities.HasKeyword(state, creature, CardKeyword.CannotAttackPlayers);
                var canAttackCreature = !_abilities.HasKeyword(state, creature, CardKeyword.CannotAttack) &&
                    !_abilities.HasKeyword(state, creature, CardKeyword.CannotAttackCreatures) &&
                    state.Opponent.BattleZone.Any(target => target.Tapped ||
                        _abilities.HasKeyword(state, creature, CardKeyword.AttackUntappedCreature));
                if (timingAllowsAttack && (canAttackPlayer || canAttackCreature) &&
                    _abilities.HasKeyword(state, creature, CardKeyword.MustAttackIfAble))
                    return RuleResult.Fail($"{creature.Definition.Name} must attack this turn if able.");
            }

            var endingPlayerId = state.ActivePlayerId;
            foreach (var creature in state.ActivePlayer.BattleZone.ToArray())
            {
                _abilities.Resolve(state, creature, AbilityTiming.EndOfOwnerTurn);
                creature.TemporaryPowerAttacker = 0;
                creature.TemporaryPower = 0;
                creature.TemporaryDoubleBreaker = false;
                creature.TemporaryBreakerCount = 0;
                creature.TemporaryKeywords.Clear();
                creature.AttackedThisTurn = false;
                creature.BrokeShieldThisTurn = false;
                if (_abilities.HasKeyword(state, creature, CardKeyword.UntapAtEndOfTurn))
                    creature.Tapped = false;
            }

            state.ActivePlayerId = state.ExtraTurnsQueuedForPlayer == endingPlayerId
                ? endingPlayerId
                : 1 - endingPlayerId;
            state.ExtraTurnsQueuedForPlayer = -1;
            state.TurnNumber++;
            state.ManaChargedThisTurn = false;
            state.SpellCastThisTurn[endingPlayerId] = false;
            state.CreatureSummonedThisTurn[endingPlayerId] = false;
            state.ShieldsBrokenThisTurn[endingPlayerId] = 0;
            state.BlockedCreatureGetsSlayerThisTurn[endingPlayerId] = false;
            state.Phase = GamePhase.Main;

            foreach (var card in state.ActivePlayer.Mana) card.Tapped = false;
            foreach (var card in state.ActivePlayer.BattleZone.ToArray())
            {
                var usedSilentSkill = card.Tapped && _abilities.HasKeyword(state, card, CardKeyword.SilentSkill) &&
                    _abilities.ResolveOptionalTiming(state, card, AbilityTiming.SilentSkill);
                if (!usedSilentSkill) card.Tapped = false;
            }
            foreach (var creature in state.ActivePlayer.BattleZone.ToArray())
                _abilities.Resolve(state, creature, AbilityTiming.StartOfOwnerTurn);

            return DrawForTurn(state);
        }

        public RuleResult ResolvePendingShieldTriggers(GameState state, int defendingPlayerId)
        {
            if (defendingPlayerId < 0 || defendingPlayerId >= state.Players.Length)
                return RuleResult.Fail("Unknown player.");
            if (defendingPlayerId == state.ActivePlayerId)
                return RuleResult.Fail("Only the defending player can resolve these shield triggers.");

            // Card-specific effects are resolved by the effect layer. Retaining candidates
            // here prevents presentation from silently skipping the shield-trigger window.
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

            state.ActivePlayer.Hand.Add(DrawTop(state.ActivePlayer.Deck));
            if (state.ActivePlayer.Deck.Count == 0)
                Finish(state, state.Opponent.PlayerId, "Deck out");

            return RuleResult.Ok();
        }

        private static RuleResult ValidateMainAction(GameState state, int playerId)
        {
            if (state.IsFinished) return RuleResult.Fail("The game is already finished.");
            if (playerId != state.ActivePlayerId) return RuleResult.Fail("It is not this player's turn.");
            if (state.Phase != GamePhase.Main)
                return RuleResult.Fail("Cards cannot be played after the attack step has started.");
            return RuleResult.Ok();
        }

        private static RuleResult ValidateManaPayment(
            PlayerState player,
            CardDefinition card,
            IReadOnlyCollection<long> selectedManaIds)
        {
            if (selectedManaIds == null || selectedManaIds.Count != card.Cost)
                return RuleResult.Fail($"Exactly {card.Cost} mana cards must be selected.");

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

            if (!selected.Any(mana => mana.Definition.SharesCivilizationWith(card)))
                return RuleResult.Fail("At least one mana card must match the played card's civilization.");

            return RuleResult.Ok();
        }

        private static void TapMana(PlayerState player, IReadOnlyCollection<long> ids)
        {
            foreach (var id in ids) Find(player.Mana, id).Tapped = true;
        }

        private static RuleResult ValidateSpellTarget(
            GameState state,
            int playerId,
            CardDefinition spell,
            long targetInstanceId)
        {
            foreach (var effect in spell.SpellEffects)
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
                            player.Hand.Add(target);
                        }
                        break;
                    case SpellEffectKind.RandomOpponentDiscard:
                        if (opponent.Hand.Count > 0)
                        {
                            var random = new Random(unchecked(state.RandomSeed * 397 ^ state.RandomActionIndex++));
                            var discarded = opponent.Hand[random.Next(opponent.Hand.Count)];
                            opponent.Hand.Remove(discarded);
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
                        PutTopDeckIntoMana(player, effect.Value);
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
                switch (effect.Kind)
                {
                    case SummonEffectKind.DrawCards:
                        DrawCards(state, player, opponent, effect.Value);
                        break;
                    case SummonEffectKind.PutTopDeckIntoMana:
                        PutTopDeckIntoMana(player, effect.Value);
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
                player.Hand.Add(DrawTop(player.Deck));
            }
        }

        private static void PutTopDeckIntoMana(PlayerState player, int count)
        {
            for (var i = 0; i < count && player.Deck.Count > 0; i++)
            {
                var card = DrawTop(player.Deck);
                card.Tapped = false;
                player.Mana.Add(card);
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
                        player.Hand.Add(target);
                    }
                    break;
                case "dm02-027": // General Dark Fiend
                    if (player.Shields.Count > 0)
                    {
                        var shield = player.Shields[player.Shields.Count - 1];
                        player.Shields.RemoveAt(player.Shields.Count - 1);
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
            if (attack.TargetKind != AttackTargetKind.Player) return;
            var owner = FindOwner(state, attack.AttackerInstanceId);
            var attacker = owner == null ? null : Find(owner.BattleZone, attack.AttackerInstanceId);
            if (attacker != null && attacker.Definition.RuleAbilities.Length == 0 &&
                attacker.Definition.Id == "dm02-032")
                Destroy(state, owner, attacker);
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
            if (attacker.TemporaryBreakerCount > 0) return attacker.TemporaryBreakerCount;
            if (_abilities.HasKeyword(state, attacker, CardKeyword.TripleBreaker)) return 3;
            if (_abilities.HasKeyword(state, attacker, CardKeyword.DoubleBreaker)) return 2;
            return 1;
        }

        private static void DiscardRandom(GameState state, PlayerState player)
        {
            if (player.Hand.Count == 0) return;
            var random = new Random(unchecked(state.RandomSeed * 397 ^ state.RandomActionIndex++));
            var discarded = player.Hand[random.Next(player.Hand.Count)];
            player.Hand.Remove(discarded);
            player.Graveyard.Add(discarded);
        }

        private void ResolveCreatureBattle(GameState state, long attackerId, long defenderId)
        {
            var attackerOwner = FindOwner(state, attackerId);
            var defenderOwner = FindOwner(state, defenderId);
            if (attackerOwner == null || defenderOwner == null) return;

            var attacker = Find(attackerOwner.BattleZone, attackerId);
            var defender = Find(defenderOwner.BattleZone, defenderId);
            if (attacker == null || defender == null) return;

            _abilities.Resolve(state, attacker, AbilityTiming.OnBattle);
            _abilities.Resolve(state, defender, AbilityTiming.OnBattle);
            var attackerPower = AttackPower(state, attackerOwner, attacker);
            var defenderPower = _abilities.CurrentPower(state, defender);
            var attackerSlayer = _abilities.HasKeyword(state, attacker, CardKeyword.Slayer);
            var defenderSlayer = _abilities.HasKeyword(state, defender, CardKeyword.Slayer);
            if (attackerPower > defenderPower)
            {
                _abilities.Resolve(state, attacker, AbilityTiming.OnBattleWon);
                _abilities.Resolve(state, defender, AbilityTiming.OnBattleLost);
            }
            else if (defenderPower > attackerPower)
            {
                _abilities.Resolve(state, defender, AbilityTiming.OnBattleWon);
                _abilities.Resolve(state, attacker, AbilityTiming.OnBattleLost);
            }

            var destroyAttacker = attackerPower <= defenderPower || defenderSlayer ||
                _abilities.HasKeyword(state, attacker, CardKeyword.DestroyAfterBattle);
            var destroyDefender = defenderPower <= attackerPower || attackerSlayer ||
                _abilities.HasKeyword(state, defender, CardKeyword.DestroyAfterBattle);

            if (destroyAttacker) Destroy(state, attackerOwner, attacker);
            if (destroyDefender) Destroy(state, defenderOwner, defender);
        }

        private void ResolvePlayerAttack(GameState state, long attackerId, int defendingPlayerId)
        {
            var attacker = Find(state.ActivePlayer.BattleZone, attackerId);
            var defender = state.Players[defendingPlayerId];
            if (attacker == null) return;

            if (defender.Shields.Count == 0)
            {
                Finish(state, state.ActivePlayerId, "Direct attack");
                return;
            }

            var breakerCount = BreakerCount(state, state.ActivePlayer, attacker);
            if (attacker.TemporaryDoubleBreaker) breakerCount = Math.Max(2, breakerCount);
            var shieldsToBreak = Math.Min(breakerCount, defender.Shields.Count);
            for (var i = 0; i < shieldsToBreak; i++)
            {
                var shield = defender.Shields[defender.Shields.Count - 1];
                defender.Shields.RemoveAt(defender.Shields.Count - 1);
                defender.Hand.Add(shield);
                attacker.BrokeShieldThisTurn = true;
                state.ShieldsBrokenThisTurn[state.ActivePlayerId]++;
                if (_abilities.HasKeyword(state, shield, CardKeyword.ShieldTrigger))
                    state.PendingShieldTriggers.Add(shield);
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
