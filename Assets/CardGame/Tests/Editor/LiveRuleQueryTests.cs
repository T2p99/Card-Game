using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class LiveRuleQueryTests
    {
        [Test]
        public void Ultimo_TurboRushUpdatesUntappedTargetsAndAttackingPowerThenExpires()
        {
            var state = State();
            var engine = new RulesEngine();
            var ultimo = Card(10, TcgCatalog.ById["dm08-40-55"]);
            var ally = Card(11, Creature("ally"));
            var target = Card(20, Creature("target", 4000));
            var otherTarget = Card(21, Creature("other-target", 7000));
            state.Players[0].BattleZone.AddRange(new[] { ultimo, ally });
            state.Players[1].BattleZone.AddRange(new[] { target, otherTarget });
            state.Players[1].Shields.Add(Card(30, Creature("shield")));

            Assert.That(engine.GetAttackTargetCandidates(state, 0, ultimo.InstanceId), Is.Empty);
            Assert.That(engine.CurrentPower(state, ultimo, true), Is.EqualTo(2000));
            Assert.That(engine.DeclareAttack(state, 0, ally.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(engine.GetAttackTargetCandidates(state, 0, ultimo.InstanceId),
                Is.EquivalentTo(new[] { target, otherTarget }));
            Assert.That(engine.ValidateAttack(state, 0, ultimo.InstanceId,
                AttackTargetKind.Creature, target.InstanceId).Success, Is.True);
            Assert.That(engine.CurrentPower(state, ultimo, true), Is.EqualTo(6000));
            Assert.That(ultimo.Tapped, Is.False, "Preview queries must not spend the attack.");
            Assert.That(ultimo.AttackedThisTurn, Is.False);
            Assert.That(state.PendingAttack, Is.Null);
            Assert.That(engine.DeclareAttack(state, 0, ultimo.InstanceId,
                AttackTargetKind.Creature, target.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);
            Assert.That(state.Players[1].Graveyard, Does.Contain(target));

            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(engine.EndTurn(state, 1).Success, Is.True);
            Assert.That(engine.CurrentPower(state, ultimo, true), Is.EqualTo(2000));
            Assert.That(engine.GetAttackTargetCandidates(state, 0, ultimo.InstanceId), Is.Empty,
                "An untouched opponent must stop being a legal target when Turbo Rush expires.");
        }

        [Test]
        public void AttackPreview_RespectsGrantedRestrictionsAndPlayerOnlyOverrides()
        {
            var state = State();
            var engine = new RulesEngine();
            var attacker = Card(10, Creature("attacker"));
            var target = Card(20, Creature("target"));
            target.Tapped = true;
            attacker.TemporaryKeywords.Add(CardKeyword.CannotAttackPlayers);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(target);

            Assert.That(engine.ValidateAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.False);
            Assert.That(engine.GetAttackTargetCandidates(state, 0, attacker.InstanceId),
                Is.EquivalentTo(new[] { target }));

            attacker.EnteredBattleZoneOnTurn = state.TurnNumber;
            state.IgnoreAttackRestrictionsThisTurn[0] = true;
            Assert.That(engine.HasSummoningSickness(state, attacker), Is.True);
            Assert.That(engine.ValidateAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True,
                "An effect such as Diamond Cutter permits this player attack despite sickness.");
            Assert.That(engine.GetAttackTargetCandidates(state, 0, attacker.InstanceId), Is.Empty,
                "The player-only override must not permit a creature attack.");
            Assert.That(attacker.Tapped, Is.False);
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BlockerPreview_UsesGrantedBlockerAndCurrentUnblockability(bool unblockable)
        {
            var state = State();
            var engine = new RulesEngine();
            var attacker = Card(10, Creature("attacker"));
            var blocker = Card(20, Creature("blocker", 3000));
            blocker.TemporaryKeywords.Add(CardKeyword.Blocker);
            if (unblockable) attacker.TemporaryKeywords.Add(CardKeyword.Unblockable);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(blocker);
            state.Players[1].Shields.Add(Card(30, Creature("shield")));
            Assert.That(engine.GetBlockerCandidates(state, 1), Is.Empty);

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            var pending = state.PendingAttack;
            Assert.That(engine.GetBlockerCandidates(state, 1).Contains(blocker),
                Is.EqualTo(!unblockable));
            Assert.That(engine.ValidateBlock(state, 1, blocker.InstanceId).Success,
                Is.EqualTo(!unblockable));
            Assert.That(blocker.Tapped, Is.False);
            Assert.That(state.PendingAttack, Is.SameAs(pending));
            if (unblockable)
            {
                Assert.That(engine.Block(state, 1, blocker.InstanceId).Success, Is.False);
                Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);
            }
            else
                Assert.That(engine.Block(state, 1, blocker.InstanceId).Success, Is.True);
        }

        [Test]
        public void ManaPreview_UpdatesWhenElfXEntersOrLeavesAndMatchesPayment()
        {
            var state = State();
            var engine = new RulesEngine();
            var elfX = Card(10, TcgCatalog.ById["dm02-46-55"]);
            var definition = Creature("three-cost");
            definition.Cost = 3;
            var creature = Card(20, definition);
            state.Players[0].Hand.Add(creature);
            state.Players[0].Mana.Add(Card(30, Creature("mana-one")));
            state.Players[0].Mana.Add(Card(31, Creature("mana-two")));

            Assert.That(engine.MinimumManaCost(state, creature), Is.EqualTo(3));
            state.Players[0].BattleZone.Add(elfX);
            Assert.That(engine.MinimumManaCost(state, creature), Is.EqualTo(2));
            Assert.That(engine.MaximumManaCost(state, creature), Is.EqualTo(2));
            state.Players[0].BattleZone.Remove(elfX);
            Assert.That(engine.MinimumManaCost(state, creature), Is.EqualTo(3));
            state.Players[0].BattleZone.Add(elfX);
            Assert.That(engine.SummonCreature(state, 0, creature.InstanceId,
                new long[] { 30, 31 }).Success, Is.True);
            Assert.That(definition.Cost, Is.EqualTo(3), "Printed card data must remain unchanged.");
        }

        [Test]
        public void CrimsonHammer_TargetPreviewUsesCurrentPower()
        {
            var state = State();
            var engine = new RulesEngine();
            var hammer = Card(10, TcgCatalog.ByName("Crimson Hammer").First());
            var target = Card(20, Creature("target", 3000));
            state.Players[0].Hand.Add(hammer);
            state.Players[1].BattleZone.Add(target);
            for (var index = 0; index < hammer.Definition.Cost; index++)
                state.Players[0].Mana.Add(Card(30 + index,
                    Creature("fire-mana-" + index, civilization: Civilization.Fire)));

            Assert.That(engine.GetSpellTargetCandidates(state, 0, hammer.InstanceId),
                Has.No.Member(target));
            target.TemporaryPower = -1500;
            Assert.That(engine.CurrentPower(state, target), Is.EqualTo(1500));
            Assert.That(engine.GetSpellTargetCandidates(state, 0, hammer.InstanceId),
                Does.Contain(target));
            Assert.That(engine.CastSpell(state, 0, hammer.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray(),
                target.InstanceId).Success, Is.True);
            Assert.That(state.Players[1].Graveyard, Does.Contain(target));
        }

        private static GameState State()
        {
            var state = new GameState
            {
                Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
                ActivePlayerId = 0,
                StartingPlayerId = 1,
                TurnNumber = 2,
                Phase = GamePhase.Main,
                RandomSeed = 42
            };
            foreach (var player in state.Players)
                for (var index = 0; index < 2; index++)
                    player.Deck.Add(Card(9000 + player.PlayerId * 10 + index,
                        Creature("deck-" + player.PlayerId + "-" + index)));
            return state;
        }

        private static CardDefinition Creature(string id, int power = 1000,
            Civilization civilization = Civilization.Nature) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 1,
            Type = CardType.Creature,
            Races = new[] { "Test Race" },
            Power = power,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Card(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition, EnteredBattleZoneOnTurn = 0 };
    }
}
