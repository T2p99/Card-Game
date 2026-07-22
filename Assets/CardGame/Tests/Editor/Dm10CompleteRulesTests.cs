using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm10CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm10PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-10").ToArray();

            Assert.That(cards.Length, Is.EqualTo(120));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [TestCase("dm10-37-110", "Choose a creature in the battle zone", AbilityTiming.SilentSkill)]
        [TestCase("dm10-8-110", "Choose a creature in your graveyard", AbilityTiming.TapAbility)]
        [TestCase("dm10-s10-s10", "Destroy all creatures that have power 3000", AbilityTiming.TapAbility)]
        [TestCase("dm10-53-110", "Each player discards a card at random", AbilityTiming.TapAbility)]
        public void PrintedActivatedEffect_UsesItsParentKeywordTiming(
            string cardId, string rulesPrefix, AbilityTiming expectedTiming)
        {
            var card = TcgCatalog.ById[cardId];
            var matching = card.RuleAbilities.Where(rule =>
                rule.RulesText.StartsWith(rulesPrefix)).ToArray();

            Assert.That(matching, Is.Not.Empty);
            Assert.That(matching.All(rule => rule.Timing == expectedTiming), Is.True,
                card.Name);
        }

        [Test]
        public void Bombazar_GrantsOneExtraTurnThenItsControllerLoses()
        {
            var state = EmptyState();
            var bombazar = Instance(10, "dm10-9-110");
            state.Players[0].BattleZone.Add(bombazar);
            state.Players[0].Deck.Add(Instance(20, Creature("first-draw")));
            state.Players[0].Deck.Add(Instance(21, Creature("second-draw")));
            state.Players[0].Deck.Add(Instance(22, Creature("deck-filler")));
            new AbilityResolver().Resolve(state, bombazar, AbilityTiming.OnSummon);
            var engine = new RulesEngine();

            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(0));
            Assert.That(state.IsFinished, Is.False);

            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.IsFinished, Is.True);
            Assert.That(state.WinnerPlayerId, Is.EqualTo(1));
        }

        [Test]
        public void Upheaval_SwapsBothPlayersHandsAndManaSimultaneously()
        {
            var state = EmptyState();
            var upheaval = Instance(10, "dm10-59-110");
            var ownHand = Instance(11, Creature("own-hand"));
            var ownMana = Instance(12, Creature("own-mana"));
            var opposingHand = Instance(13, Creature("opposing-hand"));
            var opposingMana = Instance(14, Creature("opposing-mana"));
            state.Players[0].Resolving.Add(upheaval);
            state.Players[0].Hand.Add(ownHand);
            state.Players[0].Mana.Add(ownMana);
            state.Players[1].Hand.Add(opposingHand);
            state.Players[1].Mana.Add(opposingMana);

            new AbilityResolver().Resolve(state, upheaval, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { ownMana }));
            Assert.That(state.Players[0].Mana, Is.EquivalentTo(new[] { ownHand }));
            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { opposingMana }));
            Assert.That(state.Players[1].Mana, Is.EquivalentTo(new[] { opposingHand }));
            Assert.That(ownHand.Tapped, Is.True);
            Assert.That(opposingHand.Tapped, Is.True);
        }

        [Test]
        public void SirenConcerto_StillResolvesSecondInstructionWithNoManaCard()
        {
            var state = EmptyState();
            var concerto = Instance(10, "dm10-39-110");
            var handCard = Instance(11, Creature("hand-card"));
            state.Players[0].Resolving.Add(concerto);
            state.Players[0].Hand.Add(handCard);

            new AbilityResolver().Resolve(state, concerto, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Mana, Is.EquivalentTo(new[] { handCard }));
            Assert.That(handCard.Tapped, Is.False);
        }

        [Test]
        public void GlaisMejicula_CanDiscardTwoCardsInsteadOfLosingAShield()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("attacker", Civilization.Fire));
            var glais = Instance(20, "dm10-16-110");
            var shield = Instance(21, Creature("shield"));
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(glais);
            state.Players[1].Shields.Add(shield);
            state.Players[1].Hand.Add(Instance(22, Creature("discard-one")));
            state.Players[1].Hand.Add(Instance(23, Creature("discard-two")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Shields, Does.Contain(shield));
            Assert.That(state.Players[1].Hand, Is.Empty);
            Assert.That(state.Players[1].Graveyard, Has.Count.EqualTo(2));
        }

        [Test]
        public void Deklowaz_ExposesTheHandBeforeDiscardingSmallCreatures()
        {
            var state = EmptyState();
            var deklowaz = Instance(10, "dm10-s10-s10");
            var smallCreature = Instance(20,
                Creature("small-creature", Civilization.Nature, null, power: 3000));
            var largeCreature = Instance(21,
                Creature("large-creature", Civilization.Nature, null, power: 4000));
            var spell = Instance(22, Spell("spell", Civilization.Water));
            state.Players[0].BattleZone.Add(deklowaz);
            state.Players[1].Hand.Add(smallCreature);
            state.Players[1].Hand.Add(largeCreature);
            state.Players[1].Hand.Add(spell);

            new AbilityResolver().Resolve(state, deklowaz,
                AbilityTiming.TapAbility);

            Assert.That(state.InspectionPlayerId, Is.EqualTo(0));
            Assert.That(state.InspectedCards,
                Is.EquivalentTo(new[] { smallCreature, largeCreature, spell }));
            Assert.That(state.Players[1].Graveyard, Does.Contain(smallCreature));
            Assert.That(state.Players[1].Hand,
                Is.EquivalentTo(new[] { largeCreature, spell }));
        }

        [Test]
        public void Gigandura_ExposesTheHandBeforeItsOptionalExchange()
        {
            var state = EmptyState();
            var gigandura = Instance(10, "dm10-49-110");
            var firstHandCard = Instance(20, Creature("first-hand-card"));
            var secondHandCard = Instance(21, Creature("second-hand-card"));
            var manaCard = Instance(22, Creature("mana-card"));
            state.Players[0].BattleZone.Add(gigandura);
            state.Players[1].Hand.Add(firstHandCard);
            state.Players[1].Hand.Add(secondHandCard);
            state.Players[1].Mana.Add(manaCard);

            new AbilityResolver().Resolve(state, gigandura,
                AbilityTiming.OnSummon);

            Assert.That(state.InspectionPlayerId, Is.EqualTo(0));
            Assert.That(state.InspectedCards,
                Is.EquivalentTo(new[] { firstHandCard, secondHandCard }));
        }

        [Test]
        public void RapidReincarnation_DoesNotSummonWhenDestructionWasReplaced()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm10-24-110");
            var ryudmila = Instance(20, "dm10-2-110");
            var recruit = Instance(21, Creature("recruit"));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(ryudmila);
            state.Players[0].Hand.Add(recruit);
            state.Players[0].Mana.Add(Instance(30, Creature("mana")));

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Deck, Does.Contain(ryudmila));
            Assert.That(state.Players[0].Hand, Does.Contain(recruit));
            Assert.That(state.Players[0].BattleZone, Has.No.Member(recruit));
        }

        [Test]
        public void Transmogrify_DoesNotRecruitWhenDestructionWasReplaced()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm10-43-110");
            var ryudmila = Instance(20, "dm10-2-110");
            var recruit = Instance(21, Creature("recruit"));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(ryudmila);
            state.Players[0].Deck.Add(recruit);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Deck, Does.Contain(ryudmila));
            Assert.That(state.Players[0].Deck, Does.Contain(recruit));
            Assert.That(state.Players[0].BattleZone, Has.No.Member(recruit));
        }

        [Test]
        public void MessaBahna_MustBlockAnAttackWhenAble()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("attacker", Civilization.Fire));
            var messa = Instance(20, "dm10-21-110");
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(messa);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.False);
            Assert.That(engine.Block(state, 1, messa.InstanceId).Success, Is.True);
        }

        [Test]
        public void MysticMagician_EntersSilentSkillCreaturesTappedAndReturnsThemOnDestruction()
        {
            var state = EmptyState();
            var magician = Instance(10, "dm10-36-110");
            var silent = Instance(20, Creature("silent"));
            silent.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.SilentSkill) };
            state.Players[0].BattleZone.Add(magician);
            state.Players[0].BattleZone.Add(silent);
            var resolver = new AbilityResolver();

            Assert.That(resolver.ShouldEnterBattleZoneTapped(state, silent), Is.True);
            resolver.DestroyCard(state, silent);

            Assert.That(state.Players[0].Hand, Does.Contain(silent));
            Assert.That(state.Players[0].Graveyard, Has.No.Member(silent));
        }

        [Test]
        public void Soulswap_ReplacesChosenCreatureWithEligibleManaCreature()
        {
            var state = EmptyState();
            var soulswap = Instance(30, "dm10-91-110");
            var victim = Instance(20, Creature("victim", Civilization.Fire));
            var recruit = Instance(10, Creature("recruit", Civilization.Nature));
            state.Players[0].Resolving.Add(soulswap);
            state.Players[1].BattleZone.Add(victim);
            state.Players[1].Mana.Add(recruit);

            new AbilityResolver().Resolve(state, soulswap, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Mana, Does.Contain(victim));
            Assert.That(state.Players[1].BattleZone, Does.Contain(recruit));
        }

        [Test]
        public void Ulex_CannotBeTappedByAnOpponentsEffect()
        {
            var state = EmptyState();
            var ulex = Instance(10, "dm10-104-110");
            var opposingSpell = Instance(20, Spell("tap-spell", Civilization.Water));
            opposingSpell.Definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.tap", AbilityTiming.OnCast, AbilityOperation.Tap)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            state.Players[0].BattleZone.Add(ulex);
            state.Players[1].Resolving.Add(opposingSpell);

            new AbilityResolver().Resolve(state, opposingSpell, AbilityTiming.OnCast);

            Assert.That(ulex.Tapped, Is.False);
        }

        [Test]
        public void Elixia_GainsPowerForEachDistinctManaCivilization()
        {
            var state = EmptyState();
            var elixia = Instance(10, "dm10-s1-s10");
            state.Players[0].BattleZone.Add(elixia);
            state.Players[0].Mana.Add(Instance(20,
                Creature("light-water", Civilization.Light, Civilization.Water)));
            state.Players[0].Mana.Add(Instance(21,
                Creature("water-darkness", Civilization.Water, Civilization.Darkness)));
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentPower(state, elixia), Is.EqualTo(10000));
            Assert.That(resolver.HasKeyword(state, elixia, CardKeyword.DoubleBreaker), Is.True);
            Assert.That(resolver.HasKeyword(state, elixia, CardKeyword.TripleBreaker), Is.False);
        }

        [Test]
        public void SpinalParasite_CommandsOneOpponentCreatureAtTurnStart()
        {
            var state = EmptyState();
            var parasite = Instance(10, "dm10-57-110");
            var first = Instance(20, Creature("first", Civilization.Fire));
            var second = Instance(21, Creature("second", Civilization.Fire));
            first.Definition.Abilities = new[]
                { new KeywordAbility(CardKeyword.CannotAttack) };
            state.Players[0].BattleZone.Add(parasite);
            state.Players[1].BattleZone.Add(first);
            state.Players[1].BattleZone.Add(second);
            state.Players[1].Deck.Add(Instance(30, Creature("draw")));
            state.Players[1].Deck.Add(Instance(31, Creature("deck-filler")));

            Assert.That(new RulesEngine().EndTurn(state, 0).Success, Is.True);

            var resolver = new AbilityResolver();
            Assert.That(resolver.HasKeyword(state, first,
                CardKeyword.MustAttackIfAble), Is.False,
                "A creature that cannot attack is not a legal choice.");
            Assert.That(resolver.HasKeyword(state, second,
                CardKeyword.MustAttackIfAble), Is.True);
        }

        [Test]
        public void BluumErkis_CastsBrokenShieldTriggerAsTheAttackingPlayer()
        {
            var state = EmptyState();
            var bluum = Instance(10, "dm10-s9-s10");
            var holyAwe = Instance(20, "dm01-6-110");
            var aquaRider = Instance(21, "dm06-31-110");
            bluum.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(bluum);
            state.Players[1].Shields.Add(holyAwe);
            state.Players[1].BattleZone.Add(aquaRider);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, bluum.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.SpellCastThisTurn[0], Is.True);
            Assert.That(state.Players[1].Graveyard, Does.Contain(holyAwe));
            Assert.That(aquaRider.Tapped, Is.True);
            Assert.That(new AbilityResolver().HasKeyword(
                state, aquaRider, CardKeyword.Blocker), Is.True);
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
            Civilization first = Civilization.Nature,
            Civilization? second = null,
            string race = "Test Race",
            int power = 1000) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = second.HasValue
                ? new[] { first, second.Value }
                : new[] { first },
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
