using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core
{
    public sealed class RulesEngine
    {
        private long _nextInstanceId = 1;

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
            var turnCheck = ValidateMainAction(state, playerId);
            if (!turnCheck.Success) return turnCheck;

            var player = state.Players[playerId];
            var creature = Find(player.Hand, cardInstanceId);
            if (creature == null) return RuleResult.Fail("The selected card is not in the player's hand.");
            if (creature.Definition.Type != CardType.Creature && creature.Definition.Type != CardType.EvolutionCreature)
                return RuleResult.Fail("The selected card is not a creature.");
            if (creature.Definition.ImplementationStatus == CardImplementationStatus.DataOnly)
                return RuleResult.Fail("This card's full rules are not implemented yet.");

            CardInstance evolutionBase = null;
            if (creature.Definition.Type == CardType.EvolutionCreature)
            {
                evolutionBase = Find(player.BattleZone, evolutionBaseInstanceId);
                if (evolutionBase == null)
                    return RuleResult.Fail($"Choose a {creature.Definition.EvolutionRace} in your battle zone to evolve.");
                if (!evolutionBase.Definition.Races.Any(race =>
                        race.Equals(creature.Definition.EvolutionRace, StringComparison.OrdinalIgnoreCase)))
                    return RuleResult.Fail($"{creature.Definition.Name} must evolve from a {creature.Definition.EvolutionRace}.");
            }

            var payment = ValidateManaPayment(player, creature.Definition, manaInstanceIds);
            if (!payment.Success) return payment;

            TapMana(player, manaInstanceIds);
            player.Hand.Remove(creature);
            if (evolutionBase != null)
            {
                player.BattleZone.Remove(evolutionBase);
                creature.EvolutionMaterials.Add(evolutionBase);
                creature.Tapped = evolutionBase.Tapped;
                creature.EnteredBattleZoneOnTurn = -1;
            }
            else
            {
                creature.Tapped = false;
                creature.EnteredBattleZoneOnTurn = state.TurnNumber;
            }
            player.BattleZone.Add(creature);
            ResolveSummonAbilities(state, playerId, creature);
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
            player.Hand.Remove(spell);
            ResolveSpellEffects(state, playerId, spell.Definition, targetInstanceId);
            player.Graveyard.Add(spell);
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
            player.Hand.Remove(spell);
            ResolveSpellEffects(state, defendingPlayerId, spell.Definition, targetInstanceId);
            player.Graveyard.Add(spell);
            return RuleResult.Ok();
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
            if (attacker.Definition.HasKeyword(CardKeyword.CannotAttack))
                return RuleResult.Fail("This creature cannot attack.");
            if (targetKind == AttackTargetKind.Player &&
                attacker.Definition.HasKeyword(CardKeyword.CannotAttackPlayers))
                return RuleResult.Fail("This creature cannot attack players.");
            if (attacker.EnteredBattleZoneOnTurn == state.TurnNumber &&
                !attacker.Definition.HasKeyword(CardKeyword.SpeedAttacker))
                return RuleResult.Fail("The creature has summoning sickness.");

            if (targetKind == AttackTargetKind.Creature)
            {
                var target = Find(state.Opponent.BattleZone, targetCreatureInstanceId);
                if (target == null) return RuleResult.Fail("The target creature is not in the opponent's battle zone.");
                if (!target.Tapped && !attacker.Definition.HasKeyword(CardKeyword.AttackUntappedCreature))
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
            ResolveAttackAbilities(state, playerId, attacker);
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
            if (!blocker.Definition.HasKeyword(CardKeyword.Blocker))
                return RuleResult.Fail("The selected creature does not have Blocker.");

            blocker.Tapped = true;
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
                     creature.Definition.HasKeyword(CardKeyword.SpeedAttacker));
                var canAttackPlayer = !creature.Definition.HasKeyword(CardKeyword.CannotAttack) &&
                    !creature.Definition.HasKeyword(CardKeyword.CannotAttackPlayers);
                var canAttackCreature = !creature.Definition.HasKeyword(CardKeyword.CannotAttack) &&
                    state.Opponent.BattleZone.Any(target => target.Tapped ||
                        creature.Definition.HasKeyword(CardKeyword.AttackUntappedCreature));
                if (timingAllowsAttack && (canAttackPlayer || canAttackCreature) &&
                    creature.Definition.HasKeyword(CardKeyword.MustAttackIfAble))
                    return RuleResult.Fail($"{creature.Definition.Name} must attack this turn if able.");
            }

            foreach (var creature in state.ActivePlayer.BattleZone)
            {
                creature.TemporaryPowerAttacker = 0;
                creature.TemporaryDoubleBreaker = false;
            }

            state.ActivePlayerId = 1 - state.ActivePlayerId;
            state.TurnNumber++;
            state.ManaChargedThisTurn = false;
            state.Phase = GamePhase.Main;

            foreach (var card in state.ActivePlayer.Mana) card.Tapped = false;
            foreach (var card in state.ActivePlayer.BattleZone) card.Tapped = false;

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
                }
            }

            return RuleResult.Ok();
        }

        private static void ResolveSpellEffects(
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
                        if (target != null) Destroy(opponent, target);
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
                }
            }
        }

        private static void ResolveSummonAbilities(GameState state, int playerId, CardInstance creature)
        {
            var player = state.Players[playerId];
            var opponent = state.Players[1 - playerId];
            CardInstance target;
            switch (creature.Definition.Id)
            {
                case "dm01-082": // Meteosaur
                    target = opponent.BattleZone.Where(card => card.Definition.Power <= 2000)
                        .OrderByDescending(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(opponent, target);
                    break;
                case "dm01-058": // Masked Horror
                    DiscardRandom(state, opponent);
                    break;
                case "dm01-061": // Stinger Worm
                    target = player.BattleZone.Where(card => card != creature)
                        .OrderBy(card => card.Definition.Power).FirstOrDefault() ?? creature;
                    Destroy(player, target);
                    break;
                case "dm01-062": // Swamp Worm
                    target = opponent.BattleZone.OrderBy(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(opponent, target);
                    break;
                case "dm01-085": // Rothus, the Traveler
                    target = player.BattleZone.Where(card => card != creature)
                        .OrderBy(card => card.Definition.Power).FirstOrDefault() ?? creature;
                    Destroy(player, target);
                    target = opponent.BattleZone.OrderBy(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(opponent, target);
                    break;
                case "dm02-033": // Poison Worm
                    target = player.BattleZone.Where(card => card != creature && card.Definition.Power <= 3000)
                        .OrderBy(card => card.Definition.Power).FirstOrDefault();
                    if (target == null && creature.Definition.Power <= 3000) target = creature;
                    if (target != null) Destroy(player, target);
                    break;
                case "dm02-024": // Chaos Worm
                    target = opponent.BattleZone.OrderByDescending(card => card.Definition.Power).FirstOrDefault();
                    if (target != null) Destroy(opponent, target);
                    break;
            }
        }

        private static void ResolveAttackAbilities(GameState state, int playerId, CardInstance attacker)
        {
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
                        Destroy(owner, target);
                    }
                    break;
            }
        }

        private static void ResolveAfterAttackAbilities(GameState state, PendingAttack attack)
        {
            if (attack.TargetKind != AttackTargetKind.Player) return;
            var owner = FindOwner(state, attack.AttackerInstanceId);
            var attacker = owner == null ? null : Find(owner.BattleZone, attack.AttackerInstanceId);
            if (attacker != null && attacker.Definition.Id == "dm02-032")
                Destroy(owner, attacker);
        }

        private static int AttackPower(GameState state, PlayerState owner, CardInstance attacker)
        {
            var bonus = attacker.Definition.KeywordValue(CardKeyword.PowerAttacker) + attacker.TemporaryPowerAttacker;
            switch (attacker.Definition.Id)
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
            return attacker.Definition.Power + bonus;
        }

        private static int BreakerCount(PlayerState owner, CardInstance attacker)
        {
            if (attacker.Definition.Id == "dm02-041" && owner.BattleZone.Count == 1) return 2;
            return attacker.Definition.BreakerCount;
        }

        private static void DiscardRandom(GameState state, PlayerState player)
        {
            if (player.Hand.Count == 0) return;
            var random = new Random(unchecked(state.RandomSeed * 397 ^ state.RandomActionIndex++));
            var discarded = player.Hand[random.Next(player.Hand.Count)];
            player.Hand.Remove(discarded);
            player.Graveyard.Add(discarded);
        }

        private static void ResolveCreatureBattle(GameState state, long attackerId, long defenderId)
        {
            var attackerOwner = FindOwner(state, attackerId);
            var defenderOwner = FindOwner(state, defenderId);
            if (attackerOwner == null || defenderOwner == null) return;

            var attacker = Find(attackerOwner.BattleZone, attackerId);
            var defender = Find(defenderOwner.BattleZone, defenderId);
            if (attacker == null || defender == null) return;

            var attackerPower = AttackPower(state, attackerOwner, attacker);
            var defenderPower = defender.Definition.Power;
            var attackerSlayer = attacker.Definition.HasKeyword(CardKeyword.Slayer);
            var defenderSlayer = defender.Definition.HasKeyword(CardKeyword.Slayer);

            var destroyAttacker = attackerPower <= defenderPower || defenderSlayer ||
                attacker.Definition.HasKeyword(CardKeyword.DestroyAfterBattle);
            var destroyDefender = defenderPower <= attackerPower || attackerSlayer ||
                defender.Definition.HasKeyword(CardKeyword.DestroyAfterBattle);

            if (destroyAttacker) Destroy(attackerOwner, attacker);
            if (destroyDefender) Destroy(defenderOwner, defender);
        }

        private static void ResolvePlayerAttack(GameState state, long attackerId, int defendingPlayerId)
        {
            var attacker = Find(state.ActivePlayer.BattleZone, attackerId);
            var defender = state.Players[defendingPlayerId];
            if (attacker == null) return;

            if (defender.Shields.Count == 0)
            {
                Finish(state, state.ActivePlayerId, "Direct attack");
                return;
            }

            var breakerCount = BreakerCount(state.ActivePlayer, attacker);
            if (attacker.TemporaryDoubleBreaker) breakerCount = Math.Max(2, breakerCount);
            var shieldsToBreak = Math.Min(breakerCount, defender.Shields.Count);
            for (var i = 0; i < shieldsToBreak; i++)
            {
                var shield = defender.Shields[defender.Shields.Count - 1];
                defender.Shields.RemoveAt(defender.Shields.Count - 1);
                defender.Hand.Add(shield);
                if (shield.Definition.HasKeyword(CardKeyword.ShieldTrigger))
                    state.PendingShieldTriggers.Add(shield);
            }
        }

        private static void Destroy(PlayerState owner, CardInstance card)
        {
            owner.BattleZone.Remove(card);
            card.Tapped = false;
            foreach (var material in card.EvolutionMaterials)
            {
                material.Tapped = false;
                owner.Graveyard.Add(material);
            }
            card.EvolutionMaterials.Clear();
            owner.Graveyard.Add(card);
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
