using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class PromoCompleteRulesTests
    {
        [Test]
        public void Catalog_AllPromoPrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("PROMO").ToArray();

            Assert.That(cards.Length, Is.EqualTo(16));
            Assert.That(cards.Where(card =>
                    card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void QTronicOmnistrain_MakesEveryOwnBattleZoneCreatureASurvivor()
        {
            var state = EmptyState();
            var omnistrain = Instance(10, "promo-l2-y2");
            var ally = Instance(11, Creature("ally"));
            state.Players[0].BattleZone.Add(omnistrain);
            state.Players[0].BattleZone.Add(ally);
            var resolver = new AbilityResolver();

            Assert.That(resolver.HasKeyword(state, ally, CardKeyword.Survivor), Is.True);
            Assert.That(resolver.HasRace(state, ally, "Survivor"), Is.True);
        }

        [Test]
        public void DynoMantis_MakesAnotherFiveThousandPowerCreatureBreakTwoShields()
        {
            var state = EmptyState();
            var mantis = Instance(10, "promo-l10-y2");
            var attacker = Instance(11, Creature("attacker", 5000));
            attacker.EnteredBattleZoneOnTurn = -1;
            state.Players[0].BattleZone.Add(mantis);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].Shields.Add(Instance(20, Creature("shield-one")));
            state.Players[1].Shields.Add(Instance(21, Creature("shield-two")));
            state.Players[1].Shields.Add(Instance(22, Creature("shield-three")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Shields, Has.Count.EqualTo(1));
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(2));
        }

        [Test]
        public void Amnis_SurvivesOnlyWhenLosingAgainstDarkness()
        {
            var darkness = BattleState("promo-l3-y1", Civilization.Darkness);

            ResolveCreatureAttack(darkness, 1, 20, 10);

            Assert.That(darkness.Players[0].BattleZone.Any(card =>
                card.InstanceId == 10), Is.True);

            var fire = BattleState("promo-l3-y1", Civilization.Fire);

            ResolveCreatureAttack(fire, 1, 20, 10);

            Assert.That(fire.Players[0].BattleZone.Any(card =>
                card.InstanceId == 10), Is.False);
            Assert.That(fire.Players[0].Graveyard.Any(card =>
                card.InstanceId == 10), Is.True);
        }

        [Test]
        public void Giliam_SurvivesOnlyWhenLosingAgainstLight()
        {
            var light = BattleState("promo-l5-y1", Civilization.Light);

            ResolveCreatureAttack(light, 1, 20, 10);

            Assert.That(light.Players[0].BattleZone.Any(card =>
                card.InstanceId == 10), Is.True);
        }

        [Test]
        public void Olgate_UntapsWhenAnotherOwnCreatureIsDestroyed()
        {
            var state = EmptyState();
            var olgate = Instance(10, "promo-l14-y1");
            var victim = Instance(11, Creature("victim"));
            olgate.Tapped = true;
            state.Players[0].BattleZone.Add(olgate);
            state.Players[0].BattleZone.Add(victim);

            new AbilityResolver().DestroyCard(state, victim);

            Assert.That(olgate.Tapped, Is.False);
        }

        [Test]
        public void BrigadeShell_RevealsASurvivorToHandAndOtherCardsToGraveyard()
        {
            var survivorState = EmptyState();
            var firstShell = Instance(10, "promo-l18-y1");
            var survivor = Instance(11, Creature("survivor"));
            survivor.Definition.Abilities = new[]
                { new KeywordAbility(CardKeyword.Survivor) };
            survivorState.Players[0].BattleZone.Add(firstShell);
            survivorState.Players[0].Deck.Add(survivor);

            new AbilityResolver().Resolve(survivorState, firstShell, AbilityTiming.OnAttack);

            Assert.That(survivorState.Players[0].Hand, Does.Contain(survivor));

            var otherState = EmptyState();
            var secondShell = Instance(20, "promo-l18-y1");
            var other = Instance(21, Creature("other"));
            otherState.Players[0].BattleZone.Add(secondShell);
            otherState.Players[0].Deck.Add(other);

            new AbilityResolver().Resolve(otherState, secondShell, AbilityTiming.OnAttack);

            Assert.That(otherState.Players[0].Graveyard, Does.Contain(other));
        }

        [Test]
        public void Dolzark_MovesSmallCreatureToManaWhenAnotherDragonAttacks()
        {
            var state = EmptyState();
            var dolzark = Instance(10, "promo-p0-y3");
            var dragon = Instance(11, Creature("dragon", race: "Armored Dragon"));
            var target = Instance(20, Creature("target", 5000));
            state.Players[0].BattleZone.Add(dolzark);
            state.Players[0].BattleZone.Add(dragon);
            state.Players[1].BattleZone.Add(target);

            new AbilityResolver().ResolveGlobalEvent(state, dragon,
                AbilityTiming.OnAnyCreatureAttack, true);

            Assert.That(state.Players[1].BattleZone, Has.No.Member(target));
            Assert.That(state.Players[1].Mana, Does.Contain(target));
        }

        [Test]
        public void Neve_SearchesOneCreaturePerOpposingCreatureLead()
        {
            var state = EmptyState();
            var neve = Instance(10, "promo-p4-y3");
            state.Players[0].BattleZone.Add(neve);
            for (var index = 0; index < 3; index++)
                state.Players[1].BattleZone.Add(Instance(20 + index,
                    Creature("opponent-" + index)));
            for (var index = 0; index < 3; index++)
                state.Players[0].Deck.Add(Instance(30 + index,
                    Creature("deck-" + index)));

            new AbilityResolver().Resolve(state, neve, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Hand, Has.Count.EqualTo(2));
            Assert.That(state.Players[0].Deck, Has.Count.EqualTo(1));
            Assert.That(state.InspectionPlayerId, Is.EqualTo(1));
            Assert.That(state.InspectedCards, Has.Count.EqualTo(2));
        }

        [Test]
        public void Velyrika_SearchesOnlyAnArmoredDragon()
        {
            var state = EmptyState();
            var velyrika = Instance(10, "promo-l14-y2");
            var dragon = Instance(20, Creature("dragon", race: "Armored Dragon"));
            var other = Instance(21, Creature("other", race: "Human"));
            state.Players[0].BattleZone.Add(velyrika);
            state.Players[0].Deck.Add(other);
            state.Players[0].Deck.Add(dragon);

            new AbilityResolver().Resolve(state, velyrika, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { dragon }));
            Assert.That(state.Players[0].Deck, Is.EquivalentTo(new[] { other }));
        }

        [Test]
        public void Meloppe_RedirectsOpponentShieldChoiceToShieldOwner()
        {
            var state = EmptyState();
            var meloppe = Instance(10, "dm12-9-55");
            var darts = Instance(11, "dm12-7-55");
            var creatureShield = Instance(20, Creature("creature-shield"));
            var spellShield = Instance(21, Spell("spell-shield"));
            state.Players[0].BattleZone.Add(meloppe);
            state.Players[0].Resolving.Add(darts);
            state.Players[0].Shields.Add(creatureShield);
            state.Players[0].Shields.Add(spellShield);
            var decisions = new OwnerPrefersHighestDecisionProvider();

            new AbilityResolver(decisions).Resolve(state, darts, AbilityTiming.OnCast);

            Assert.That(decisions.ChooserIds, Does.Contain(1));
            Assert.That(decisions.ChooserIds, Does.Contain(0));
            Assert.That(state.Players[0].Graveyard, Does.Contain(spellShield));
            Assert.That(state.Players[0].Shields, Does.Contain(creatureShield));
        }

        private sealed class OwnerPrefersHighestDecisionProvider : IAbilityDecisionProvider
        {
            public List<int> ChooserIds { get; } = new List<int>();

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum)
            {
                ChooserIds.Add(playerId);
                var ordered = playerId == 0
                    ? candidates.OrderByDescending(card => card.InstanceId)
                    : candidates.OrderBy(card => card.InstanceId);
                return ordered.Take(maximum).ToArray();
            }
        }

        private static GameState BattleState(string defenderCatalogId,
            Civilization attackerCivilization)
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var defender = Instance(10, defenderCatalogId);
            defender.Tapped = true;
            var attacker = Instance(20, Creature("attacker", 6000,
                attackerCivilization));
            attacker.EnteredBattleZoneOnTurn = -1;
            state.Players[0].BattleZone.Add(defender);
            state.Players[1].BattleZone.Add(attacker);
            return state;
        }

        private static void ResolveCreatureAttack(GameState state, int attackerPlayerId,
            long attackerId, long defenderId)
        {
            var engine = new RulesEngine();
            Assert.That(engine.DeclareAttack(state, attackerPlayerId, attackerId,
                AttackTargetKind.Creature, defenderId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1 - attackerPlayerId).Success, Is.True);
        }

        private static GameState EmptyState() => new GameState
        {
            Players = new[]
            {
                new PlayerState { PlayerId = 0 },
                new PlayerState { PlayerId = 1 }
            },
            ActivePlayerId = 0,
            StartingPlayerId = 1,
            TurnNumber = 2,
            Phase = GamePhase.Main,
            RandomSeed = 42
        };

        private static CardDefinition Creature(string id, int power = 1000,
            Civilization civilization = Civilization.Nature,
            string race = "Test Race") => new CardDefinition
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

        private static CardDefinition Spell(string id) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { Civilization.Light },
            Cost = 1,
            Type = CardType.Spell,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Instance(long id, string catalogId) =>
            new CardInstance { InstanceId = id, Definition = TcgCatalog.ById[catalogId] };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };
    }
}
