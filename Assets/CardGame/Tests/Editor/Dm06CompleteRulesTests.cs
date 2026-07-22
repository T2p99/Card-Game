using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm06CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm06PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-06").ToArray();

            Assert.That(cards.Length, Is.EqualTo(120));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void BondsOfJustice_DoesNotTapAConditionalBlocker()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm06-14-110");
            var conditionalBlocker = Instance(20, "dm05-18-55");
            var nonBlocker = Instance(21, Creature("non-blocker"));
            state.Players[0].Resolving.Add(spell);
            state.Players[1].BattleZone.Add(conditionalBlocker);
            state.Players[1].BattleZone.Add(nonBlocker);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(conditionalBlocker.Tapped, Is.False);
            Assert.That(nonBlocker.Tapped, Is.True);
        }

        [Test]
        public void RainOfArrows_ExposesTheHandAndDiscardsOnlyDarknessSpells()
        {
            var state = EmptyState();
            var rain = Instance(10, "dm06-25-110");
            var darknessSpell = Instance(20, Spell("darkness-spell", Civilization.Darkness));
            var waterSpell = Instance(21, Spell("water-spell", Civilization.Water));
            var darknessCreature = Instance(22,
                Creature("darkness-creature", Civilization.Darkness));
            state.Players[0].Resolving.Add(rain);
            state.Players[1].Hand.Add(darknessSpell);
            state.Players[1].Hand.Add(waterSpell);
            state.Players[1].Hand.Add(darknessCreature);

            new AbilityResolver().Resolve(state, rain, AbilityTiming.OnCast);

            Assert.That(state.InspectionPlayerId, Is.EqualTo(0));
            Assert.That(state.InspectedCards,
                Is.EquivalentTo(new[] { darknessSpell, waterSpell, darknessCreature }));
            Assert.That(state.Players[1].Graveyard,
                Is.EquivalentTo(new[] { darknessSpell }));
            Assert.That(state.Players[1].Hand,
                Is.EquivalentTo(new[] { waterSpell, darknessCreature }));
        }

        [Test]
        public void RumblesaurQ_SharesSpeedAttackerWithAlliedSurvivors()
        {
            var state = EmptyState();
            var rumblesaur = Instance(10, "dm06-88-110");
            var survivor = Instance(11, Creature("survivor", Civilization.Nature, "Beast Folk"));
            survivor.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Survivor) };
            state.Players[0].BattleZone.Add(rumblesaur);
            state.Players[0].BattleZone.Add(survivor);

            Assert.That(new AbilityResolver().HasKeyword(state, survivor, CardKeyword.SpeedAttacker), Is.True);
        }

        [Test]
        public void Forbos_SharesItsSummonSearchWithAnotherSurvivor()
        {
            var state = EmptyState();
            var forbos = Instance(10, "dm06-19-110");
            var survivor = Instance(11, Creature("survivor", Civilization.Nature, "Beast Folk"));
            survivor.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Survivor) };
            var spell = Instance(20, Spell("searched-spell", Civilization.Water));
            state.Players[0].BattleZone.Add(forbos);
            state.Players[0].BattleZone.Add(survivor);
            state.Players[0].Deck.Add(spell);

            new AbilityResolver().Resolve(state, survivor, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Hand, Does.Contain(spell));
        }

        [Test]
        public void ArcBine_LetsAnotherLightCreatureUseItsTapAbility()
        {
            var state = EmptyState();
            var arcBine = Instance(10, "dm06-12-110");
            var lightCreature = Instance(11, Creature("light-creature", Civilization.Light));
            var target = Instance(20, Creature("target", Civilization.Fire));
            state.Players[0].BattleZone.Add(arcBine);
            state.Players[0].BattleZone.Add(lightCreature);
            state.Players[1].BattleZone.Add(target);

            var result = new RulesEngine().UseTapAbility(state, 0, lightCreature.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(lightCreature.Tapped, Is.True);
            Assert.That(target.Tapped, Is.True);
            Assert.That(arcBine.Tapped, Is.False);
        }

        [Test]
        public void DavaTorey_EntersBattleZoneInsteadOfBeingDiscardedOnOpponentsTurn()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var davaTorey = Instance(10, "dm06-18-110");
            var discardSource = Instance(20, Creature("discard-source", Civilization.Darkness));
            discardSource.Definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.discard", AbilityTiming.OnSummon, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1
                    }
                }
            };
            state.Players[0].Hand.Add(davaTorey);
            state.Players[1].BattleZone.Add(discardSource);

            new AbilityResolver().Resolve(state, discardSource, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].BattleZone, Does.Contain(davaTorey));
            Assert.That(state.Players[0].Graveyard, Has.No.Member(davaTorey));
        }

        [Test]
        public void LuGila_MakesEvolutionCreatureEnterTapped_AndInnocentHunterIsAnyRaceBase()
        {
            var state = EmptyState();
            var innocentHunter = Instance(10, "dm06-103-110");
            var luGila = Instance(11, "dm06-2-110");
            var crazeValkyrie = Instance(12, "dm06-s1-s10");
            state.Players[0].BattleZone.Add(innocentHunter);
            state.Players[0].BattleZone.Add(luGila);
            state.Players[0].Hand.Add(crazeValkyrie);
            AddMana(state, 0, crazeValkyrie.Definition.Cost, Civilization.Light);

            var result = new RulesEngine().SummonCreature(state, 0,
                crazeValkyrie.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray(),
                innocentHunter.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(crazeValkyrie.Tapped, Is.True);
            Assert.That(crazeValkyrie.EvolutionMaterials,
                Does.Contain(innocentHunter));
        }

        [Test]
        public void GraveWormQ_ReturnsASurvivorFromGraveyardOnSummon()
        {
            var state = EmptyState();
            var graveWorm = Instance(10, "dm06-58-110");
            var survivorDefinition = Creature("grave-survivor",
                Civilization.Nature);
            survivorDefinition.Abilities = new[]
                { new KeywordAbility(CardKeyword.Survivor) };
            var survivor = Instance(20, survivorDefinition);
            var ordinary = Instance(21, Creature("ordinary-grave-creature"));
            state.Players[0].Hand.Add(graveWorm);
            state.Players[0].Graveyard.Add(survivor);
            state.Players[0].Graveyard.Add(ordinary);
            AddMana(state, 0, graveWorm.Definition.Cost,
                Civilization.Darkness);

            var result = new RulesEngine().SummonCreature(state, 0,
                graveWorm.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray());

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(state.Players[0].Hand, Does.Contain(survivor));
            Assert.That(state.Players[0].Graveyard, Does.Contain(ordinary));
        }

        [Test]
        public void Bolmeteus_PutsBrokenShieldsIntoGraveyardWithoutShieldTriggers()
        {
            var state = EmptyState();
            var bolmeteus = Instance(10, "dm06-s7-s10");
            bolmeteus.EnteredBattleZoneOnTurn = 0;
            var firstShield = Instance(20, Spell("trigger-one", Civilization.Light));
            var secondShield = Instance(21, Spell("trigger-two", Civilization.Water));
            firstShield.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.ShieldTrigger) };
            secondShield.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.ShieldTrigger) };
            state.Players[0].BattleZone.Add(bolmeteus);
            state.Players[1].Shields.Add(firstShield);
            state.Players[1].Shields.Add(secondShield);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, bolmeteus.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Graveyard, Is.EquivalentTo(new[] { firstShield, secondShield }));
            Assert.That(state.Players[1].Hand, Is.Empty);
            Assert.That(state.PendingShieldTriggers, Is.Empty);
        }

        [Test]
        public void QTronicGargantua_CrewBreakerCountsOtherSurvivors()
        {
            var state = EmptyState();
            var gargantua = Instance(10, "dm06-86-110");
            gargantua.EnteredBattleZoneOnTurn = 0;
            var firstSurvivor = Instance(11, Creature("first-survivor"));
            var secondSurvivor = Instance(12, Creature("second-survivor"));
            firstSurvivor.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Survivor) };
            secondSurvivor.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Survivor) };
            state.Players[0].BattleZone.Add(gargantua);
            state.Players[0].BattleZone.Add(firstSurvivor);
            state.Players[0].BattleZone.Add(secondSurvivor);
            for (var i = 0; i < 4; i++)
                state.Players[1].Shields.Add(Instance(20 + i, Creature("shield-" + i)));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, gargantua.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Shields, Has.Count.EqualTo(1));
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(3));
        }

        [Test]
        public void CoccoLupia_ReducesDragonCostButNotDragonoidCost()
        {
            var state = EmptyState();
            var cocco = Instance(10, "dm06-77-110");
            var dragon = Instance(11, Creature("dragon", Civilization.Fire, "Armored Dragon"));
            var dragonoid = Instance(12, Creature("dragonoid", Civilization.Fire, "Dragonoid"));
            dragon.Definition.Cost = 5;
            dragonoid.Definition.Cost = 5;
            state.Players[0].BattleZone.Add(cocco);
            state.Players[0].Hand.Add(dragon);
            state.Players[0].Hand.Add(dragonoid);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentCost(state, dragon), Is.EqualTo(3));
            Assert.That(resolver.CurrentCost(state, dragonoid), Is.EqualTo(5));
        }

        [Test]
        public void Chekicul_BlocksWithoutEitherCreatureBattling()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("attacker", Civilization.Fire));
            var chekicul = Instance(20, "dm06-15-110");
            attacker.Definition.Power = 10000;
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(chekicul);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, chekicul.InstanceId).Success, Is.True);

            Assert.That(state.Players[0].BattleZone, Does.Contain(attacker));
            Assert.That(state.Players[1].BattleZone, Does.Contain(chekicul));
            Assert.That(attacker.Tapped, Is.True);
            Assert.That(chekicul.Tapped, Is.True);
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

        private static CardDefinition Spell(string id, Civilization civilization) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 0,
            Type = CardType.Spell,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static void AddMana(GameState state, int playerId, int count, Civilization civilization)
        {
            for (var i = 0; i < count; i++)
                state.Players[playerId].Mana.Add(Instance(100 + playerId * 100 + i,
                    Creature("mana-" + playerId + "-" + i, civilization)));
        }

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
