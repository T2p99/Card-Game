using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm03CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm03PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-03").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void PsychicShaper_PartitionsTopFourCardsByCivilization()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm03-20-55");
            state.Players[0].Hand.Add(spell);
            state.Players[0].Deck.Add(Instance(21, Creature("fire-one", Civilization.Fire)));
            state.Players[0].Deck.Add(Instance(22, Creature("water-one", Civilization.Water)));
            state.Players[0].Deck.Add(Instance(23, Creature("fire-two", Civilization.Fire)));
            state.Players[0].Deck.Add(Instance(24, Creature("water-two", Civilization.Water)));
            AddMana(state, spell.Definition.Cost, Civilization.Water);

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Hand.Select(card => card.Definition.Name),
                Is.EquivalentTo(new[] { "water-one", "water-two" }));
            Assert.That(state.Players[0].Graveyard.Select(card => card.Definition.Name),
                Does.Contain("fire-one").And.Contain("fire-two"));
        }

        [Test]
        public void JackViper_ReplacesAnotherDarknessCreaturesDestructionWithReturnToHand()
        {
            var state = EmptyState();
            var jackViper = Instance(10, "dm03-29-55");
            var ally = Instance(11, Creature("darkness-ally", Civilization.Darkness));
            state.Players[0].BattleZone.Add(jackViper);
            state.Players[0].BattleZone.Add(ally);

            new AbilityResolver().DestroyCard(state, ally);

            Assert.That(state.Players[0].Hand.Contains(ally), Is.True);
            Assert.That(state.Players[0].Graveyard.Contains(ally), Is.False);
        }

        [Test]
        public void Emeral_SwapsAHandCardWithAShield()
        {
            var state = EmptyState();
            var emeral = Instance(10, "dm03-14-55");
            var handCard = Instance(50, Creature("hand-card", Civilization.Water));
            var oldShield = Instance(20, Creature("old-shield", Civilization.Light));
            state.Players[0].BattleZone.Add(emeral);
            state.Players[0].Hand.Add(handCard);
            state.Players[0].Shields.Add(oldShield);

            new AbilityResolver().Resolve(state, emeral, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Shields, Does.Contain(handCard));
            Assert.That(state.Players[0].Hand, Does.Contain(oldShield));
        }

        [Test]
        public void PouchShell_RemovesOnlyEvolutionTopCard()
        {
            var state = EmptyState();
            var pouchShell = Instance(10, "dm03-50-55");
            var evolution = Instance(20, "dm03-29-55");
            var baseCreature = Instance(21, Creature("ghost-base", Civilization.Darkness, "Ghost"));
            evolution.EvolutionMaterials.Add(baseCreature);
            state.Players[0].BattleZone.Add(pouchShell);
            state.Players[1].BattleZone.Add(evolution);

            new AbilityResolver().Resolve(state, pouchShell, AbilityTiming.OnSummon);

            Assert.That(state.Players[1].Graveyard, Does.Contain(evolution));
            Assert.That(state.Players[1].BattleZone, Does.Contain(baseCreature));
        }

        [Test]
        public void SnipStriker_CannotAttackWhileBehindOnCreatures()
        {
            var state = EmptyState();
            var attacker = Instance(10, "dm03-42-55");
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(Instance(20, Creature("opponent-one")));
            state.Players[1].BattleZone.Add(Instance(21, Creature("opponent-two")));

            var result = new RulesEngine().DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("cannot attack"));
        }

        [Test]
        public void RagingDashHorn_GainsPowerAndDoubleBreakerWithMonoNatureMana()
        {
            var state = EmptyState();
            var creature = Instance(10, "dm03-52-55");
            state.Players[0].BattleZone.Add(creature);
            AddMana(state, 2, Civilization.Nature);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentPower(state, creature), Is.EqualTo(7000));
            Assert.That(resolver.HasKeyword(state, creature, CardKeyword.DoubleBreaker), Is.True);

            state.Players[0].Mana.Add(Instance(999, Creature("off-color", Civilization.Fire)));
            Assert.That(resolver.CurrentPower(state, creature), Is.EqualTo(4000));
            Assert.That(resolver.HasKeyword(state, creature, CardKeyword.DoubleBreaker), Is.False);
        }

        [Test]
        public void BlazeCannon_CannotBeCastWithOffCivilizationMana()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm03-36-55");
            state.Players[0].Hand.Add(spell);
            AddMana(state, spell.Definition.Cost - 1, Civilization.Fire);
            state.Players[0].Mana.Add(Instance(999, Creature("nature-mana", Civilization.Nature)));

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("cast condition"));
        }

        private static GameState EmptyState() => new GameState
        {
            Players = new[] { new PlayerState { PlayerId = 0 }, new PlayerState { PlayerId = 1 } },
            ActivePlayerId = 0,
            StartingPlayerId = 1,
            TurnNumber = 2,
            Phase = GamePhase.Main,
            RandomSeed = 42
        };

        private static CardDefinition Creature(string id,
            Civilization civilization = Civilization.Nature, string race = "Test Race") => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 1,
            Type = CardType.Creature,
            Races = new[] { race },
            Power = 1000,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static void AddMana(GameState state, int count, Civilization civilization)
        {
            for (var i = 0; i < count; i++)
                state.Players[0].Mana.Add(Instance(100 + i,
                    Creature("mana-" + i, civilization)));
        }

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
