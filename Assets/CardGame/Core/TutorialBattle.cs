using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core
{
    public enum TutorialStep
    {
        Welcome, Hand, Deck, Shields, ChargeFirstMana, FirstMana, EndFirstTurn,
        DrawCard, ChargeSecondMana, SummonCreature, SummoningSickness, EndSecondTurn,
        ReadyCreature, ChargeThirdMana, AttackShield, OpponentShieldToHand, EndThirdTurn,
        YourShieldToHand, CreatureBattle, Graveyard, EndFourthTurn, CastSpell,
        SpellResolved, EndFifthTurn, SummonSpeedAttacker, SpeedAttack, Practice,
        OpponentTurn, Finished
    }

    public enum TutorialAction { ChargeMana, PlayCard, AttackPlayer, AttackCreature, EndTurn, TapAbility }
    public enum TutorialFocus
    {
        None, Hand, Deck, YourShields, OpponentShields, Mana, Battle,
        OpponentBattle, Graveyard, OpponentGraveyard, EndTurn
    }

    public sealed class TutorialBattle
    {
        public const int Seed = 741203;
        public const string NormalCreatureId = "dm01-80-110";
        public const string SpeedAttackerId = "dm05-43-55";
        public const string SpellId = "dm01-73-110";
        private const string FirstManaId = "dm08-39-55";
        private const string SecondManaId = "dm10-66-110";
        private const string ThirdManaId = "dm03-38-55";
        private const string EnemyCreatureId = "dm01-26-110";

        private static readonly string[] HumanCardIds =
        {
            NormalCreatureId, SpeedAttackerId, SpellId, FirstManaId, SecondManaId,
            ThirdManaId, "dm06-84-110", "dm01-70-110", "dm01-71-110", "dm01-82-110"
        };
        private static readonly string[] ComputerCardIds =
        {
            EnemyCreatureId, "dm01-23-110", "dm01-24-110", "dm01-25-110", "dm01-28-110",
            "dm01-30-110", "dm01-31-110", "dm01-32-110", "dm01-33-110", "dm01-34-110"
        };

        private sealed class Lesson
        {
            public readonly string Title, Text;
            public readonly TutorialFocus Focus;
            public readonly TutorialAction? Action;
            public readonly bool Explanation;

            public Lesson(string title, string text, TutorialFocus focus = TutorialFocus.None,
                TutorialAction? action = null, bool explanation = true)
            {
                Title = title; Text = text; Focus = focus; Action = action;
                Explanation = explanation && !action.HasValue;
            }
        }

        private static readonly Lesson[] Lessons =
        {
            new Lesson("Welcome to your first duel",
                "Learn with fixed cards and a training opponent. Follow the highlights first, then play freely to win. You can inspect cards or exit at any time."),
            new Lesson("This is your hand",
                "Your hand holds cards you can play or turn into mana. Tap the highlighted card to inspect its cost, power and abilities.", TutorialFocus.Hand),
            new Lesson("This is your deck",
                "Your 40-card deck supplies your draws. Five cards started in hand and five as shields, leaving 30 here. Tap your deck to see its count.", TutorialFocus.Deck),
            new Lesson("Five shields protect each player",
                "Shields are face-down cards. Attacking a player usually breaks one. A broken shield goes to its OWNER'S hand, not the attacker's.", TutorialFocus.YourShields),
            new Lesson("Charge your first mana",
                "Inspect Magmadragon Melgars, then tap CHARGE MANA. Any hand card can become mana, but you give up playing that copy.", TutorialFocus.Hand, TutorialAction.ChargeMana),
            new Lesson("One mana is ready",
                "You may charge only once per turn, before playing cards. A 1-cost card could be played now, but this hand needs at least 2 mana.", TutorialFocus.Mana),
            new Lesson("End your first turn",
                "Tap END TURN. Your training opponent will charge mana, then give the turn back to you.", TutorialFocus.EndTurn, TutorialAction.EndTurn),
            new Lesson("Draw at the start of your turn",
                "You automatically drew a card from your deck. Draw once at the start of each turn, except the starting player's very first turn.", TutorialFocus.Hand),
            new Lesson("Grow to two mana",
                "Charge Cragsaur as your second mana card. You can add up to one mana each turn; you do not have to charge every turn.", TutorialFocus.Hand, TutorialAction.ChargeMana),
            new Lesson("Summon a normal creature",
                "Inspect Immortal Baron, Vorg and tap SUMMON. Its cost is 2. Pay with two ready mana, including mana matching its Fire civilization.", TutorialFocus.Hand, TutorialAction.PlayCard),
            new Lesson("Summoning sickness",
                "Tap your new Vorg to inspect it. It cannot attack this turn: normal creatures have summoning sickness. The mana you paid is tapped, not spent forever.", TutorialFocus.Battle),
            new Lesson("Let your creature get ready",
                "End your turn. Your mana untaps when your next turn begins, and Vorg will then be able to attack.", TutorialFocus.EndTurn, TutorialAction.EndTurn),
            new Lesson("Your creature can now attack",
                "Vorg has waited a turn. Aqua Vehicle is upright, so it is not a normal creature-attack target. First, let's attack a shield.", TutorialFocus.Battle),
            new Lesson("Charge before attacking",
                "Charge Explosive Dude Joe to reach 3 mana. Summon and cast BEFORE attacking; once the attack step begins you cannot play more cards that turn.", TutorialFocus.Hand, TutorialAction.ChargeMana),
            new Lesson("Break your first shield",
                "Close the viewer, then drag Vorg onto an opponent shield. The red arrow shows your target. Release there, then choose a shield if prompted.", TutorialFocus.OpponentShields, TutorialAction.AttackPlayer),
            new Lesson("Their shield became their hand card",
                "One shield broke and its card entered the opponent's hand. Vorg turned sideways: it is tapped and cannot attack again until it untaps.", TutorialFocus.OpponentShields),
            new Lesson("See a shield break from your side",
                "End your turn. Aqua Vehicle is now ready and will attack you. Watch your shield count and your hand.", TutorialFocus.EndTurn, TutorialAction.EndTurn),
            new Lesson("Your broken shield is in your hand",
                "You have 4 shields now. The broken shield entered YOUR hand. Aqua Vehicle tapped to attack, leaving it open to a creature battle.", TutorialFocus.YourShields),
            new Lesson("Battle a tapped creature",
                "Drag Vorg onto the sideways Aqua Vehicle. Vorg has 2000 power; Aqua Vehicle has 1000. This attack battles a creature instead of breaking a shield.", TutorialFocus.OpponentBattle, TutorialAction.AttackCreature),
            new Lesson("The weaker creature is destroyed",
                "Vorg survived. Aqua Vehicle went to its owner's graveyard. Higher power wins a normal battle; equal power destroys both creatures.", TutorialFocus.OpponentGraveyard),
            new Lesson("Prepare for a spell",
                "End your turn. The opponent will summon another Aqua Vehicle so you can learn to remove it with a spell.", TutorialFocus.EndTurn, TutorialAction.EndTurn),
            new Lesson("Cast a spell",
                "Inspect Crimson Hammer and tap CAST SPELL. Pay 2 mana to destroy the opponent's 1000-power Aqua Vehicle; this spell can destroy power 2000 or less.", TutorialFocus.Hand, TutorialAction.PlayCard),
            new Lesson("Spells resolve, then go to the graveyard",
                "Crimson Hammer destroyed Aqua Vehicle, then went to YOUR graveyard. Your three mana still exist, but only one is ready after paying two.", TutorialFocus.Graveyard),
            new Lesson("Refresh mana for a special creature",
                "End your turn to untap your mana and draw again. Next, try a creature that does not have summoning sickness.", TutorialFocus.EndTurn, TutorialAction.EndTurn),
            new Lesson("Meet a Speed Attacker",
                "Summon Rikabu, the Dismantler for 3 mana. Its Speed Attacker ability means it can attack on the same turn it enters the battle zone.", TutorialFocus.Hand, TutorialAction.PlayCard),
            new Lesson("Attack immediately",
                "Drag your newly summoned Rikabu onto an opponent shield. Unlike Vorg on its first turn, Rikabu can attack right away.", TutorialFocus.OpponentShields, TutorialAction.AttackPlayer),
            new Lesson("Your turn to play freely", "", TutorialFocus.Battle, explanation: false),
            new Lesson("Opponent's turn",
                "Watch the training opponent's moves. Your lesson continues when your next turn starts.", explanation: false),
            new Lesson("Battle finished", "", explanation: false)
        };

        private readonly long _normal, _speed, _spell, _firstMana, _secondMana, _thirdMana, _brokenShield;
        private TutorialStep _afterOpponent;
        private bool _inspected;
        public GameState InitialState { get; }
        public long FirstEnemyId { get; }
        public long SecondEnemyId { get; }
        public TutorialStep Step { get; private set; } = TutorialStep.Welcome;
        public TutorialFocus Focus => Lessons[(int)Step].Focus;
        public string Title => Lessons[(int)Step].Title;
        public bool IsExplanation => Lessons[(int)Step].Explanation;
        public int LessonCount => (int)TutorialStep.Practice + 1;
        public int LessonNumber => Math.Min(LessonCount,
            (int)(Step == TutorialStep.OpponentTurn ? _afterOpponent : Step) + 1);
        public bool CanContinue => Lessons[(int)Step].Explanation &&
            (Step != TutorialStep.Hand && Step != TutorialStep.Deck &&
             Step != TutorialStep.SummoningSickness || _inspected);

        public long FocusCardId
        {
            get
            {
                switch (Step)
                {
                    case TutorialStep.ChargeFirstMana: return _firstMana;
                    case TutorialStep.ChargeSecondMana: return _secondMana;
                    case TutorialStep.ChargeThirdMana: return _thirdMana;
                    case TutorialStep.CastSpell: return _spell;
                    case TutorialStep.SummonSpeedAttacker:
                    case TutorialStep.SpeedAttack: return _speed;
                    case TutorialStep.YourShieldToHand: return _brokenShield;
                    case TutorialStep.Hand:
                    case TutorialStep.SummonCreature:
                    case TutorialStep.SummoningSickness:
                    case TutorialStep.ReadyCreature:
                    case TutorialStep.AttackShield:
                    case TutorialStep.CreatureBattle: return _normal;
                    default: return 0;
                }
            }
        }

        public long TargetCardId => Step == TutorialStep.CreatureBattle ? FirstEnemyId :
            Step == TutorialStep.CastSpell ? SecondEnemyId : 0;

        public TutorialBattle(RulesEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            InitialState = engine.StartGame(BuildDeck(HumanCardIds), BuildDeck(ComputerCardIds), Seed, 0);
            Arrange(InitialState.Players[0],
                new[] { "dm01-70-110", "dm01-70-110", "dm01-70-110", "dm01-70-110", "dm01-82-110" },
                new[] { FirstManaId, NormalCreatureId, SpeedAttackerId, SpellId, SecondManaId },
                new[] { "dm06-84-110", ThirdManaId, NormalCreatureId, "dm01-71-110", "dm06-84-110" });
            Arrange(InitialState.Players[1],
                new[] { "dm01-28-110", "dm01-28-110", "dm01-28-110", "dm01-28-110", "dm01-24-110" },
                new[] { EnemyCreatureId, EnemyCreatureId, "dm01-23-110", "dm01-24-110", "dm01-25-110" },
                new[] { "dm01-30-110", "dm01-32-110", "dm01-34-110" });
            var hand = InitialState.Players[0].Hand;
            _firstMana = hand[0].InstanceId;
            _normal = hand[1].InstanceId;
            _speed = hand[2].InstanceId;
            _spell = hand[3].InstanceId;
            _secondMana = hand[4].InstanceId;
            _thirdMana = InitialState.Players[0].Deck[InitialState.Players[0].Deck.Count - 2].InstanceId;
            _brokenShield = InitialState.Players[0].Shields[0].InstanceId;
            FirstEnemyId = InitialState.Players[1].Hand[0].InstanceId;
            SecondEnemyId = InitialState.Players[1].Hand[1].InstanceId;
        }

        public string Guidance(GameState state)
        {
            if (Step != TutorialStep.Practice) return Lessons[(int)Step].Text;
            if (state.ActivePlayerId != 0)
                return "The training opponent is taking its turn. Your cards will untap and you will draw when your next turn starts.";
            if (state.Players[1].Shields.Count == 0)
                return "No shields remain, but you have NOT won yet! Drag a ready creature onto RIVAL for the winning attack. If your creatures are tapped, end your turn first.";
            return "Play freely now! Charge up to one mana, summon or cast before attacking, then use ready creatures to break the remaining shields. End turns as needed. After the last shield, attack RIVAL once more to win.";
        }

        public bool Allows(TutorialAction action, long cardId = 0, long targetId = 0)
        {
            if (Step == TutorialStep.Practice) return true;
            if (Lessons[(int)Step].Action != action) return false;
            if (action == TutorialAction.EndTurn) return true;
            return cardId == FocusCardId &&
                (action != TutorialAction.AttackCreature || targetId == FirstEnemyId);
        }

        public void InspectCard(GameState state, long cardId)
        {
            if (Step == TutorialStep.Hand && state.Players[0].Hand.Any(c => c.InstanceId == cardId) ||
                Step == TutorialStep.SummoningSickness && cardId == _normal &&
                state.Players[0].BattleZone.Any(c => c.InstanceId == cardId))
                _inspected = true;
        }

        public void InspectDeck(int playerId)
        {
            if (Step == TutorialStep.Deck && playerId == 0) _inspected = true;
        }

        public RuleResult Continue()
        {
            if (!CanContinue)
                return RuleResult.Fail("Complete the highlighted inspection or action before continuing.");
            SetStep(Step + 1);
            return RuleResult.Ok();
        }

        public void Observe(GameState before, GameState after, RuleResult result)
        {
            if (!result.Success) return;
            if (after.IsFinished) { SetStep(TutorialStep.Finished); return; }
            if (Step == TutorialStep.OpponentTurn)
            {
                if (after.ActivePlayerId == 0) SetStep(_afterOpponent);
                return;
            }
            var human = after.Players[0];
            var enemy = after.Players[1];
            switch (Step)
            {
                case TutorialStep.ChargeFirstMana:
                case TutorialStep.ChargeSecondMana:
                case TutorialStep.ChargeThirdMana:
                    if (before.Players[0].Hand.Any(c => c.InstanceId == FocusCardId) &&
                        human.Mana.Any(c => c.InstanceId == FocusCardId))
                        SetStep(Step + 1);
                    break;
                case TutorialStep.SummonCreature:
                case TutorialStep.SummonSpeedAttacker:
                    if (before.Players[0].Hand.Any(c => c.InstanceId == FocusCardId) &&
                        human.BattleZone.Any(c => c.InstanceId == FocusCardId))
                        SetStep(Step + 1);
                    break;
                case TutorialStep.AttackShield:
                case TutorialStep.SpeedAttack:
                    if (after.PendingAttack == null && before.PendingAttack?.AttackerInstanceId == FocusCardId &&
                        enemy.Shields.Count == before.Players[1].Shields.Count - 1 &&
                        before.Players[1].Shields.Any(c => enemy.Hand.Any(h => h.InstanceId == c.InstanceId)))
                        SetStep(Step + 1);
                    break;
                case TutorialStep.CreatureBattle:
                    if (enemy.Graveyard.Any(c => c.InstanceId == FirstEnemyId) &&
                        human.BattleZone.Any(c => c.InstanceId == _normal))
                        SetStep(TutorialStep.Graveyard);
                    break;
                case TutorialStep.CastSpell:
                    if (human.Graveyard.Any(c => c.InstanceId == _spell) &&
                        enemy.Graveyard.Any(c => c.InstanceId == SecondEnemyId))
                        SetStep(TutorialStep.SpellResolved);
                    break;
                case TutorialStep.EndFirstTurn:
                case TutorialStep.EndSecondTurn:
                case TutorialStep.EndThirdTurn:
                case TutorialStep.EndFourthTurn:
                case TutorialStep.EndFifthTurn:
                    if (before.ActivePlayerId == 0 && after.ActivePlayerId == 1)
                    {
                        _afterOpponent = Step + 1;
                        SetStep(TutorialStep.OpponentTurn);
                    }
                    break;
            }
        }

        // This method changes only the supplied trial state, so choice-session
        // replays cannot advance the lesson or spend a scripted move twice.
        public RuleResult PlayOpponentAction(RulesEngine engine, GameState state)
        {
            if (state.IsFinished || state.ActivePlayerId != 1)
                return RuleResult.Fail("The training opponent can act only on its own turn.");
            if (state.PendingAttack != null) return engine.DeclineBlockAndResolve(state, 0);
            var player = state.Players[1];
            if (!state.ManaChargedThisTurn)
            {
                var mana = player.Hand.FirstOrDefault(c =>
                    c.InstanceId != FirstEnemyId && c.InstanceId != SecondEnemyId);
                if (mana != null) return engine.ChargeMana(state, 1, mana.InstanceId);
            }
            var turn = state.TurnNumber / 2;
            var summonId = turn == 2 ? FirstEnemyId : turn == 4 ? SecondEnemyId : 0;
            var summon = player.Hand.FirstOrDefault(c => c.InstanceId == summonId);
            if (summon != null)
                return engine.SummonCreatureWithChoices(state, 1, summonId,
                    engine.FindManaPayment(state, 1, summon));
            if (turn == 3 && player.BattleZone.Any(c => c.InstanceId == FirstEnemyId && !c.Tapped))
                return engine.DeclareAttack(state, 1, FirstEnemyId, AttackTargetKind.Player,
                    0, state.Players[0].Shields[0].InstanceId);
            return engine.EndTurn(state, 1);
        }

        private void SetStep(TutorialStep step)
        {
            Step = step;
            _inspected = false;
        }

        private static CardDefinition[] BuildDeck(IEnumerable<string> ids) =>
            ids.SelectMany(id => Enumerable.Repeat(TcgCatalog.ById[id], 4)).ToArray();

        private static void Arrange(PlayerState player, string[] shields, string[] hand, string[] draws)
        {
            var pool = player.Deck.Concat(player.Hand).Concat(player.Shields)
                .OrderBy(c => c.InstanceId).ToList();
            player.Deck.Clear(); player.Hand.Clear(); player.Shields.Clear();
            foreach (var id in shields) player.Shields.Add(Take(pool, id));
            foreach (var id in hand) player.Hand.Add(Take(pool, id));
            // The engine draws from the end of the deck list.
            var openingDraws = draws.Select(id => Take(pool, id)).Reverse().ToArray();
            player.Deck.AddRange(pool);
            player.Deck.AddRange(openingDraws);
        }

        private static CardInstance Take(List<CardInstance> pool, string id)
        {
            var card = pool.First(c => c.Definition.Id == id);
            pool.Remove(card);
            return card;
        }
    }
}
