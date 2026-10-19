using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm01BatchOneTests
    {
        [Test]
        public void KingDepthcon_CannotBeBlocked()
        {
            var state = EmptyState();
            var attacker = Instance(10, StarterDeckCatalog.Cards["dm01-s04"]);
            attacker.EnteredBattleZoneOnTurn = 0;
            var blockerDefinition = Creature("blocker");
            blockerDefinition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            var blocker = Instance(20, blockerDefinition);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(blocker);

            var engine = new RulesEngine();
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId, AttackTargetKind.Player).Success, Is.True);
            var result = engine.Block(state, 1, blocker.InstanceId);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("cannot be blocked"));
            Assert.That(blocker.Tapped, Is.False);
        }

        [Test]
        public void Urth_UntapsAtEndOfOwnersTurn()
        {
            var state = EmptyState();
            var urth = Instance(10, StarterDeckCatalog.Cards["dm01-s02"]);
            urth.Tapped = true;
            state.Players[0].BattleZone.Add(urth);

            Assert.That(new RulesEngine().EndTurn(state, 0).Success, Is.True);
            Assert.That(urth.Tapped, Is.False);
        }

        [Test]
        public void AquaHulcus_DrawsCardWhenSummoned()
        {
            var state = EmptyState();
            var hulcus = Instance(10, StarterDeckCatalog.Cards["dm01-023"]);
            state.Players[0].Hand.Add(hulcus);
            AddMana(state, 3);
            state.Players[0].Deck.Add(Instance(50, Creature("drawn")));

            var result = new RulesEngine().SummonCreature(state, 0, hulcus.InstanceId, new long[] { 100, 101, 102 });

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Hand.Exists(card => card.InstanceId == 50), Is.True);
        }

        [Test]
        public void BronzeArmTribe_PutsTopDeckCardIntoMana()
        {
            var state = EmptyState();
            var bronzeArm = Instance(10, StarterDeckCatalog.Cards["dm01-090"]);
            state.Players[0].Hand.Add(bronzeArm);
            AddMana(state, 3, Civilization.Nature);
            state.Players[0].Deck.Add(Instance(50, Creature("accelerated")));

            var result = new RulesEngine().SummonCreature(state, 0, bronzeArm.InstanceId, new long[] { 100, 101, 102 });

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Mana.Exists(card => card.InstanceId == 50), Is.True);
            Assert.That(state.Players[0].Mana.Find(card => card.InstanceId == 50).Tapped, Is.False);
        }

        [Test]
        public void OnslaughterTriceps_MovesThePlayersChosenManaToGraveyard()
        {
            var state = EmptyState();
            var engine = new RulesEngine();
            var triceps = Instance(10,
                TcgCatalog.ByName("Onslaughter Triceps").Single());
            state.Players[0].Hand.Add(triceps);
            AddMana(state, 4, Civilization.Fire);
            var chosenMana = state.Players[0].Mana.Single(card =>
                card.InstanceId == 102);

            var candidates = engine.GetCreatureSummonChoiceCandidates(
                state, 0, triceps.InstanceId);
            var result = engine.SummonCreature(state, 0,
                triceps.InstanceId, new long[] { 100, 101, 102 }, 0,
                chosenMana.InstanceId);

            Assert.That(candidates, Is.EquivalentTo(state.Players[0].Mana
                .Concat(new[] { chosenMana })));
            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Graveyard, Does.Contain(chosenMana));
            Assert.That(state.Players[0].Mana.Any(card =>
                card.InstanceId == 100), Is.True,
                "The deterministic first mana card must not replace the player's choice.");
        }

        [Test]
        public void HolyAwe_TapsEveryOpponentCreature()
        {
            var state = EmptyState();
            var holyAwe = Instance(10, StarterDeckCatalog.Cards["dm01-006"]);
            state.Players[0].Hand.Add(holyAwe);
            AddMana(state, 6, Civilization.Light);
            state.Players[1].BattleZone.Add(Instance(20, Creature("first")));
            state.Players[1].BattleZone.Add(Instance(21, Creature("second")));

            var result = new RulesEngine().CastSpell(state, 0, holyAwe.InstanceId,
                new long[] { 100, 101, 102, 103, 104, 105 });

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].BattleZone.TrueForAll(card => card.Tapped), Is.True);
        }

        private static GameState EmptyState()
        {
            return new GameState
            {
                Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
                ActivePlayerId = 0,
                StartingPlayerId = 1,
                TurnNumber = 2,
                Phase = GamePhase.Main,
                RandomSeed = 42
            };
        }

        private static void AddMana(GameState state, int count, Civilization civilization = Civilization.Water)
        {
            for (var i = 0; i < count; i++)
                state.Players[0].Mana.Add(Instance(100 + i, Creature($"mana-{i}", civilization)));
        }

        private static CardDefinition Creature(string id, Civilization civilization = Civilization.Water)
        {
            return new CardDefinition
            {
                Id = id,
                Name = id,
                Civilizations = new[] { civilization },
                Cost = 1,
                Type = CardType.Creature,
                Power = 1000,
                ImplementationStatus = CardImplementationStatus.Complete
            };
        }

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
