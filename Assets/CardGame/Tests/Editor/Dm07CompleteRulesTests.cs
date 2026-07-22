using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm07CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm07PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-07").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void SelfDestructionReplacement_PreservesOptionalAndMandatoryWording()
        {
            var aquaAgent = TcgCatalog.ById["dm07-16-55"].RuleAbilities.Single(rule =>
                rule.Timing == AbilityTiming.ReplacementDestroy &&
                rule.Operation == AbilityOperation.Move);
            var dreamPirate = TcgCatalog.ById["dm07-26-55"].RuleAbilities.Single(rule =>
                rule.Timing == AbilityTiming.ReplacementDestroy &&
                rule.Operation == AbilityOperation.Move);
            var aless = TcgCatalog.ById["dm03-2-55"].RuleAbilities.Single(rule =>
                rule.Timing == AbilityTiming.ReplacementDestroy &&
                rule.Operation == AbilityOperation.Move);

            Assert.That(aquaAgent.Optional, Is.True, "Aqua Agent says 'you may'.");
            Assert.That(dreamPirate.Optional, Is.True, "Dream Pirate says 'you may'.");
            Assert.That(aless.Optional, Is.False, "Aless has a mandatory replacement.");
        }

        [Test]
        public void ApocalypseVise_DestroysAChosenGroupWithinTheTotalPowerBudget()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm07-36-55");
            var threeThousand = Instance(20, Creature("three-thousand", power: 3000));
            var fiveThousand = Instance(21, Creature("five-thousand", power: 5000));
            var fourThousand = Instance(22, Creature("four-thousand", power: 4000));
            state.Players[0].Resolving.Add(spell);
            state.Players[1].BattleZone.Add(threeThousand);
            state.Players[1].BattleZone.Add(fiveThousand);
            state.Players[1].BattleZone.Add(fourThousand);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Graveyard, Is.EquivalentTo(new[] { threeThousand, fiveThousand }));
            Assert.That(state.Players[1].BattleZone, Does.Contain(fourThousand));
        }

        [Test]
        public void MiraclePortal_LetsChosenCreatureAttackImmediatelyAndPreventsBlocking()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm07-11-55");
            var attacker = Instance(20, Creature("attacker", Civilization.Light));
            var blocker = Instance(30, Creature("blocker", Civilization.Light));
            attacker.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.CannotAttackPlayers) };
            blocker.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            attacker.EnteredBattleZoneOnTurn = state.TurnNumber;
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(blocker);
            state.Players[1].Shields.Add(Instance(40, Creature("shield")));
            var engine = new RulesEngine();

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, blocker.InstanceId).Success, Is.False);
        }

        [Test]
        public void PulsarTree_ReplacesOnlyOneBreakFromADoubleBreaker()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("double-breaker", Civilization.Fire));
            attacker.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.DoubleBreaker) };
            attacker.EnteredBattleZoneOnTurn = 0;
            var pulsarTree = Instance(20, "dm07-12-55");
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(pulsarTree);
            state.Players[1].Shields.Add(Instance(30, Creature("first-shield")));
            state.Players[1].Shields.Add(Instance(31, Creature("second-shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Graveyard, Does.Contain(pulsarTree));
            Assert.That(state.Players[1].Shields, Has.Count.EqualTo(1));
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(1));
        }

        [Test]
        public void FruitOfEternity_AlsoProtectsCreaturesThatEnterLaterInTheTurn()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm07-49-55");
            var laterCreature = Instance(20, Creature("later-creature"));
            state.Players[0].Resolving.Add(spell);

            var resolver = new AbilityResolver();
            resolver.Resolve(state, spell, AbilityTiming.OnCast);
            state.Players[0].BattleZone.Add(laterCreature);
            resolver.DestroyCard(state, laterCreature);

            Assert.That(state.Players[0].Mana, Does.Contain(laterCreature));
            Assert.That(state.Players[0].Graveyard, Does.Not.Contain(laterCreature));
        }

        [Test]
        public void ScalpelSpider_GainsSlayerWhenItIsAttacked()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("large-attacker", Civilization.Fire, power: 9000));
            var spider = Instance(20, "dm07-32-55");
            attacker.EnteredBattleZoneOnTurn = 0;
            spider.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(spider);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Creature, spider.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Graveyard, Does.Contain(attacker));
        }

        [Test]
        public void CosmicNebula_DrawsOneAdditionalCardAfterTheNormalTurnDraw()
        {
            var state = EmptyState();
            var nebula = Instance(20, "dm07-s2-s5");
            state.Players[1].BattleZone.Add(nebula);
            state.Players[1].Deck.Add(Instance(30, Creature("normal-draw")));
            state.Players[1].Deck.Add(Instance(31, Creature("extra-draw")));

            Assert.That(new RulesEngine().EndTurn(state, 0).Success, Is.True);

            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(2));
        }

        [Test]
        public void CrypticTotem_DisablesOpposingShieldTriggersOnlyWhileTapped()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("attacker", Civilization.Fire));
            var crypticTotem = Instance(20, "dm07-47-55");
            var trigger = Instance(30, Spell("trigger", Civilization.Light));
            trigger.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.ShieldTrigger) };
            attacker.EnteredBattleZoneOnTurn = 0;
            crypticTotem.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(crypticTotem);
            state.Players[1].Shields.Add(trigger);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.PendingShieldTriggers, Is.Empty);
            Assert.That(state.Players[1].Hand, Does.Contain(trigger));
        }

        [Test]
        public void SpinningTotem_MakesBlockedNatureAttackerBreakOneShield()
        {
            var state = EmptyState();
            var spinningTotem = Instance(10, "dm07-5-55");
            var attacker = Instance(20, Creature("nature-attacker", Civilization.Nature, power: 5000));
            var blocker = Instance(30, Creature("blocker", Civilization.Light, power: 1000));
            blocker.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.Blocker) };
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(spinningTotem);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(blocker);
            state.Players[1].Shields.Add(Instance(40, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.UseTapAbility(state, 0,
                spinningTotem.InstanceId).Success, Is.True);
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, blocker.InstanceId).Success, Is.True);

            Assert.That(state.Players[1].Shields, Is.Empty);
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(1));
        }

        [TestCase("dm07-s4-s5", "Each player puts a card")]
        [TestCase("dm07-3-55", "Until the end of the turn")]
        [TestCase("dm07-4-55", "Each of your fire creatures")]
        [TestCase("dm07-5-55", "This turn, whenever any of your nature creatures")]
        public void PrintedTapAbilityEffect_UsesTapAbilityTiming(
            string cardId, string rulesPrefix)
        {
            var card = TcgCatalog.ById[cardId];
            var matching = card.RuleAbilities.Where(rule =>
                rule.RulesText.StartsWith(rulesPrefix)).ToArray();

            Assert.That(matching, Is.Not.Empty);
            Assert.That(matching.All(rule =>
                rule.Timing == AbilityTiming.TapAbility), Is.True,
                card.Name);
        }

        [Test]
        public void SkyCrusher_TapAbilityMakesEachPlayerLoseOneManaCard()
        {
            var state = EmptyState();
            var skyCrusher = Instance(10, "dm07-s4-s5");
            var ownMana = Instance(20, Creature("own-mana"));
            var opposingMana = Instance(30, Creature("opposing-mana"));
            state.Players[0].BattleZone.Add(skyCrusher);
            state.Players[0].Mana.Add(ownMana);
            state.Players[1].Mana.Add(opposingMana);
            state.Players[1].Shields.Add(Instance(40, Creature("shield")));

            var result = new RulesEngine().UseTapAbility(state, 0,
                skyCrusher.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Graveyard, Does.Contain(ownMana));
            Assert.That(state.Players[1].Graveyard, Does.Contain(opposingMana));
        }

        [Test]
        public void JusticeJamming_TapsExactlyOneChosenCivilizationGroup()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm07-8-55");
            var firstDarkness = Instance(20, Creature("dark-one", Civilization.Darkness));
            var secondDarkness = Instance(21, Creature("dark-two", Civilization.Darkness));
            var fire = Instance(30, Creature("fire", Civilization.Fire));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(firstDarkness);
            state.Players[1].BattleZone.Add(secondDarkness);
            state.Players[1].BattleZone.Add(fire);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(firstDarkness.Tapped, Is.True);
            Assert.That(secondDarkness.Tapped, Is.True);
            Assert.That(fire.Tapped, Is.False);
        }

        [Test]
        public void Siri_HasConditionalBlockerOnlyWhileItsOwnerHasNoShields()
        {
            var state = EmptyState();
            var siri = Instance(10, "dm07-s1-s5");
            state.Players[0].BattleZone.Add(siri);
            var resolver = new AbilityResolver();

            Assert.That(resolver.HasKeyword(state, siri, CardKeyword.Blocker), Is.True);
            state.Players[0].Shields.Add(Instance(20, Creature("shield")));
            Assert.That(resolver.HasKeyword(state, siri, CardKeyword.Blocker), Is.False);
        }

        [Test]
        public void Gandar_EndStepUntapRequiresTapAbilityAndSurvivesSourceLeaving()
        {
            var state = EmptyState();
            var gandar = Instance(10, "dm07-1-55");
            var ally = Instance(20, Creature("light-ally", Civilization.Light));
            ally.Tapped = true;
            state.Players[0].BattleZone.Add(gandar);
            state.Players[0].BattleZone.Add(ally);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            var resolver = new AbilityResolver();

            resolver.ResolveEndStep(state, 0);
            Assert.That(ally.Tapped, Is.True,
                "The delayed untap must not exist before Gandar uses its tap ability.");

            Assert.That(new RulesEngine().UseTapAbility(state, 0,
                gandar.InstanceId).Success, Is.True);
            resolver.DestroyCard(state, gandar);
            Assert.That(state.Players[0].Graveyard, Does.Contain(gandar));

            resolver.ResolveEndStep(state, 0);

            Assert.That(ally.Tapped, Is.False,
                "An already-created delayed effect must survive its source leaving play.");
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
            Civilization civilization = Civilization.Nature, string race = "Test Race", int power = 1000) =>
            new CardDefinition
            {
                Id = id,
                Name = id,
                Civilizations = new[] { civilization },
                Cost = 1,
                Type = CardType.Creature,
                Races = new[] { race },
                Power = power,
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

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
