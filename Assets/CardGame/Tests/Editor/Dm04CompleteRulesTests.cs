using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm04CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm04PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-04").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void Alcadeias_PreventsEitherPlayerFromCastingNonLightSpells()
        {
            var state = EmptyState();
            state.Players[0].BattleZone.Add(Instance(10, "dm04-1-55"));
            var darknessSpell = Instance(20, Spell("dark-spell", Civilization.Darkness));
            state.Players[1].Hand.Add(darknessSpell);
            state.ActivePlayerId = 1;
            AddMana(state, 1, 1, Civilization.Darkness);

            var result = new RulesEngine().CastSpell(state, 1, darknessSpell.InstanceId,
                state.Players[1].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("cast condition"));
        }

        [Test]
        public void DewMushroom_TaxesDarknessCardsForBothPlayers()
        {
            var state = EmptyState();
            var dewMushroom = Instance(10, "dm04-50-55");
            var darknessCreature = Instance(20, Creature("darkness", Civilization.Darkness));
            darknessCreature.Definition.Cost = 4;
            state.Players[0].BattleZone.Add(dewMushroom);
            state.Players[1].Hand.Add(darknessCreature);

            Assert.That(new AbilityResolver().CurrentCost(state, darknessCreature), Is.EqualTo(5));
        }

        [Test]
        public void Ballom_DestroysEveryNonDarknessCreatureOnSummon()
        {
            var state = EmptyState();
            var ballom = Instance(10, "dm04-s3-s5");
            var alliedLight = Instance(11, Creature("allied-light", Civilization.Light));
            var opposingFire = Instance(20, Creature("opposing-fire", Civilization.Fire));
            var opposingDarkness = Instance(21, Creature("opposing-darkness", Civilization.Darkness));
            state.Players[0].BattleZone.Add(ballom);
            state.Players[0].BattleZone.Add(alliedLight);
            state.Players[1].BattleZone.Add(opposingFire);
            state.Players[1].BattleZone.Add(opposingDarkness);

            new AbilityResolver().Resolve(state, ballom, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Graveyard, Does.Contain(alliedLight));
            Assert.That(state.Players[1].Graveyard, Does.Contain(opposingFire));
            Assert.That(state.Players[1].BattleZone, Does.Contain(opposingDarkness));
            Assert.That(state.Players[0].BattleZone, Does.Contain(ballom));
        }

        [Test]
        public void MistRias_DrawsWhenAnotherCreatureIsSummoned()
        {
            var state = EmptyState();
            var mistRias = Instance(10, "dm04-13-55");
            var summoned = Instance(20, Creature("summoned", Civilization.Light));
            state.Players[0].BattleZone.Add(mistRias);
            state.Players[0].Hand.Add(summoned);
            state.Players[0].Deck.Add(Instance(30, Creature("drawn")));
            AddMana(state, 0, summoned.Definition.Cost, Civilization.Light);

            var result = new RulesEngine().SummonCreature(state, 0, summoned.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Hand.Single().Definition.Name, Is.EqualTo("drawn"));
        }

        [Test]
        public void MongrelMan_DrawsWhenAnotherCreatureIsDestroyed()
        {
            var state = EmptyState();
            var mongrelMan = Instance(10, "dm04-33-55");
            var victim = Instance(20, Creature("victim", Civilization.Fire));
            var drawn = Instance(30, Creature("drawn"));
            state.Players[0].BattleZone.Add(mongrelMan);
            state.Players[0].Deck.Add(drawn);
            state.Players[1].BattleZone.Add(victim);

            new AbilityResolver().DestroyCard(state, victim);

            Assert.That(state.Players[0].Hand, Does.Contain(drawn));
        }

        [Test]
        public void Darkpact_DrawsExactlyTheNumberOfManaCardsSacrificed()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm04-28-55");
            state.Players[0].Hand.Add(spell);
            AddMana(state, 0, 3, Civilization.Darkness);
            for (var i = 0; i < 3; i++)
                state.Players[0].Deck.Add(Instance(30 + i, Creature("draw-" + i)));

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Take(spell.Definition.Cost)
                    .Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Mana, Is.Empty);
            Assert.That(state.Players[0].Hand, Has.Count.EqualTo(3));
        }

        [Test]
        public void FullDefensor_BlockerExpiresAtStartOfOwnersNextTurn()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm04-9-55");
            var creature = Instance(20, Creature("creature", Civilization.Light));
            state.Players[0].Hand.Add(spell);
            state.Players[0].BattleZone.Add(creature);
            AddMana(state, 0, spell.Definition.Cost, Civilization.Light);
            state.Players[0].Deck.Add(Instance(30, Creature("draw-zero")));
            state.Players[1].Deck.Add(Instance(31, Creature("draw-one")));
            var engine = new RulesEngine();

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray()).Success, Is.True);
            Assert.That(new AbilityResolver().HasKeyword(state, creature, CardKeyword.Blocker), Is.True);
            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(new AbilityResolver().HasKeyword(state, creature, CardKeyword.Blocker), Is.True);
            Assert.That(engine.EndTurn(state, 1).Success, Is.True);
            Assert.That(new AbilityResolver().HasKeyword(state, creature, CardKeyword.Blocker), Is.False);
        }

        [Test]
        public void WhiskingWhirlwind_UntapsCreaturesAtEndOfTurnAfterSpellLeavesResolving()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm04-18-55");
            var first = Instance(20, Creature("first", Civilization.Light));
            var second = Instance(21, Creature("second", Civilization.Nature));
            first.Tapped = true;
            second.Tapped = true;
            state.Players[0].Hand.Add(spell);
            state.Players[0].BattleZone.Add(first);
            state.Players[0].BattleZone.Add(second);
            AddMana(state, 0, spell.Definition.Cost, Civilization.Light);

            var cast = new RulesEngine().CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(cast.Success, Is.True);
            Assert.That(state.Players[0].Graveyard, Does.Contain(spell));
            Assert.That(first.Tapped, Is.True);
            Assert.That(second.Tapped, Is.True);

            new AbilityResolver().ResolveEndStep(state, 0);

            Assert.That(first.Tapped, Is.False);
            Assert.That(second.Tapped, Is.False);
        }

        [Test]
        public void MegaDetonator_GrantsBreakerToSameNumberAsCardsDiscarded()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm04-44-55");
            var firstHandCard = Instance(11, Creature("hand-one"));
            var secondHandCard = Instance(12, Creature("hand-two"));
            var firstCreature = Instance(20, Creature("battle-one"));
            var secondCreature = Instance(21, Creature("battle-two"));
            state.Players[0].Hand.Add(spell);
            state.Players[0].Hand.Add(firstHandCard);
            state.Players[0].Hand.Add(secondHandCard);
            state.Players[0].BattleZone.Add(firstCreature);
            state.Players[0].BattleZone.Add(secondCreature);
            AddMana(state, 0, spell.Definition.Cost, Civilization.Fire);

            var result = new RulesEngine().CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True);
            var resolver = new AbilityResolver();
            Assert.That(resolver.HasKeyword(state, firstCreature, CardKeyword.DoubleBreaker), Is.True);
            Assert.That(resolver.HasKeyword(state, secondCreature, CardKeyword.DoubleBreaker), Is.True);
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
            Civilization civilization = Civilization.Nature) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 1,
            Type = CardType.Creature,
            Races = new[] { "Test Race" },
            Power = 1000,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardDefinition Spell(string id, Civilization civilization) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 1,
            Type = CardType.Spell,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static void AddMana(GameState state, int playerId, int count, Civilization civilization)
        {
            var offset = playerId == 0 ? 100 : 200;
            for (var i = 0; i < count; i++)
                state.Players[playerId].Mana.Add(Instance(offset + i,
                    Creature("mana-" + playerId + "-" + i, civilization)));
        }

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
