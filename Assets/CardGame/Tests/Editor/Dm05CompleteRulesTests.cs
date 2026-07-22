using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class Dm05CompleteRulesTests
    {
        [Test]
        public void Catalog_AllDm05PrintingsHaveExecutableRules()
        {
            var cards = TcgCatalog.ForSet("DM-05").ToArray();

            Assert.That(cards.Length, Is.EqualTo(60));
            Assert.That(cards.Where(card => card.ImplementationStatus != CardImplementationStatus.Complete)
                .Select(card => card.Name), Is.Empty);
        }

        [Test]
        public void KipChippotto_IsDestroyedInsteadOfAnAlliedArmoredDragon()
        {
            var state = EmptyState();
            var kip = Instance(10, "dm05-42-55");
            var dragon = Instance(11, Creature("dragon", Civilization.Fire, "Armored Dragon"));
            state.Players[0].BattleZone.Add(kip);
            state.Players[0].BattleZone.Add(dragon);

            new AbilityResolver().DestroyCard(state, dragon);

            Assert.That(state.Players[0].BattleZone, Does.Contain(dragon));
            Assert.That(state.Players[0].Graveyard, Does.Contain(kip));
        }

        [Test]
        public void LurkingEel_CanBlockFireButNotWaterAttackers()
        {
            var fireState = EmptyState();
            var fireAttacker = Instance(10, Creature("fire-attacker", Civilization.Fire));
            var firstEel = Instance(20, "dm05-18-55");
            fireAttacker.EnteredBattleZoneOnTurn = 0;
            fireState.Players[0].BattleZone.Add(fireAttacker);
            fireState.Players[1].BattleZone.Add(firstEel);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(fireState, 0, fireAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(fireState, 1, firstEel.InstanceId).Success, Is.True);

            var waterState = EmptyState();
            var waterAttacker = Instance(30, Creature("water-attacker", Civilization.Water));
            var secondEel = Instance(40, "dm05-18-55");
            waterAttacker.EnteredBattleZoneOnTurn = 0;
            waterState.Players[0].BattleZone.Add(waterAttacker);
            waterState.Players[1].BattleZone.Add(secondEel);

            Assert.That(engine.DeclareAttack(waterState, 0, waterAttacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(waterState, 1, secondEel.InstanceId).Success, Is.False);
        }

        [Test]
        public void MiracleQuest_DrawsTwiceForEachShieldBrokenByAttack()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm05-19-55");
            var attacker = Instance(20, Creature("attacker", Civilization.Water));
            attacker.EnteredBattleZoneOnTurn = 0;
            state.Players[0].Hand.Add(spell);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            state.Players[0].Deck.Add(Instance(40, Creature("draw-one")));
            state.Players[0].Deck.Add(Instance(41, Creature("draw-two")));
            AddMana(state, 0, spell.Definition.Cost, Civilization.Water);
            var engine = new RulesEngine();

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray()).Success, Is.True);
            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Hand, Has.Count.EqualTo(2));
        }

        [Test]
        public void SnorkLa_ReturnsManaBurnedByOpponent()
        {
            var state = EmptyState();
            var snorkLa = Instance(10, "dm05-13-55");
            var manaCard = Instance(11, Creature("mana-card", Civilization.Light));
            var enemySource = Instance(20, Creature("enemy-source", Civilization.Nature));
            enemySource.Definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.mana-burn", AbilityTiming.OnSummon, AbilityOperation.Move)
                {
                    Destination = RuleZone.Graveyard,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.ManaZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1
                    }
                }
            };
            state.Players[0].BattleZone.Add(snorkLa);
            state.Players[0].Mana.Add(manaCard);
            state.Players[1].BattleZone.Add(enemySource);

            new AbilityResolver().Resolve(state, enemySource, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Mana, Does.Contain(manaCard));
            Assert.That(state.Players[0].Graveyard, Does.Not.Contain(manaCard));
        }

        [Test]
        public void ThunderNet_AllowsFewerTargetsThanWaterCreatureCount()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm05-15-55");
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(Instance(20,
                Creature("water-one", Civilization.Water)));
            state.Players[0].BattleZone.Add(Instance(21,
                Creature("water-two", Civilization.Water)));
            var first = Instance(30, Creature("first"));
            var second = Instance(31, Creature("second"));
            var third = Instance(32, Creature("third"));
            state.Players[1].BattleZone.Add(first);
            state.Players[1].BattleZone.Add(second);
            state.Players[1].BattleZone.Add(third);

            new AbilityResolver(new ChooseOneDecisionProvider()).Resolve(
                state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[1].BattleZone.Count(card => card.Tapped),
                Is.EqualTo(1));
        }

        [Test]
        public void SchemingHands_ExposesTheWholeHandBeforeDiscardingOneCard()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm05-31-55");
            var first = Instance(20, Creature("first"));
            var second = Instance(21, Creature("second"));
            state.Players[0].Resolving.Add(spell);
            state.Players[1].Hand.Add(first);
            state.Players[1].Hand.Add(second);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.InspectionPlayerId, Is.EqualTo(0));
            Assert.That(state.InspectedCards, Is.EquivalentTo(new[] { first, second }));
            Assert.That(state.Players[1].Graveyard, Has.Count.EqualTo(1));
        }

        [Test]
        public void Pokolul_UntapsWhenItsBrokenShieldTriggerIsUsed()
        {
            var state = EmptyState();
            var pokolul = Instance(10, "dm05-20-55");
            var trigger = Instance(20, Spell("trigger", Civilization.Light));
            trigger.Definition.Abilities = new[] { new KeywordAbility(CardKeyword.ShieldTrigger) };
            pokolul.EnteredBattleZoneOnTurn = 0;
            state.Players[0].BattleZone.Add(pokolul);
            state.Players[1].Shields.Add(trigger);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, pokolul.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);
            Assert.That(pokolul.Tapped, Is.True);
            Assert.That(engine.CastShieldTrigger(state, 1, trigger.InstanceId).Success, Is.True);
            Assert.That(pokolul.Tapped, Is.False);
        }

        [Test]
        public void SlimeVeil_RequiresOpponentCreaturesToAttackIfAble()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm05-33-55");
            var opposingCreature = Instance(20, Creature("opposing", Civilization.Nature));
            opposingCreature.EnteredBattleZoneOnTurn = 0;
            state.Players[0].Hand.Add(spell);
            state.Players[1].BattleZone.Add(opposingCreature);
            state.Players[0].Deck.Add(Instance(30, Creature("draw-zero")));
            state.Players[1].Deck.Add(Instance(31, Creature("draw-one")));
            AddMana(state, 0, spell.Definition.Cost, Civilization.Darkness);
            var engine = new RulesEngine();

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray()).Success, Is.True);
            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(engine.EndTurn(state, 1).Success, Is.False);
        }

        [Test]
        public void BrutalCharge_SearchesAtEndOfTurnPerShieldBroken()
        {
            var state = EmptyState();
            var spell = Instance(10, "dm05-49-55");
            state.Players[0].Hand.Add(spell);
            AddMana(state, 0, spell.Definition.Cost, Civilization.Nature);
            state.Players[0].Deck.Add(Instance(20, Creature("search-one")));
            state.Players[0].Deck.Add(Instance(21, Creature("search-two")));
            state.Players[0].Deck.Add(Instance(22, Creature("search-three")));
            state.Players[1].Deck.Add(Instance(30, Creature("opponent-draw")));
            state.ShieldsBrokenThisTurn[0] = 2;
            var engine = new RulesEngine();

            Assert.That(engine.CastSpell(state, 0, spell.InstanceId,
                state.Players[0].Mana.Select(card => card.InstanceId).ToArray()).Success, Is.True);
            Assert.That(engine.EndTurn(state, 0).Success, Is.True);

            Assert.That(state.Players[0].Hand, Has.Count.EqualTo(2));
            Assert.That(state.Players[0].Deck, Has.Count.EqualTo(1));
        }

        [Test]
        public void Gigakail_SlayerAppliesOnlyAgainstNatureOrLightCreature()
        {
            var state = EmptyState();
            var gigakail = Instance(10, "dm05-26-55");
            var nature = Instance(20, Creature("nature", Civilization.Nature));
            var fire = Instance(21, Creature("fire", Civilization.Fire));
            state.Players[0].BattleZone.Add(gigakail);
            state.Players[1].BattleZone.Add(nature);
            state.Players[1].BattleZone.Add(fire);
            var resolver = new AbilityResolver();

            state.CurrentBattleAttackerInstanceId = gigakail.InstanceId;
            state.CurrentBattleDefenderInstanceId = nature.InstanceId;
            Assert.That(resolver.HasKeyword(state, gigakail, CardKeyword.Slayer), Is.True);

            state.CurrentBattleDefenderInstanceId = fire.InstanceId;
            Assert.That(resolver.HasKeyword(state, gigakail, CardKeyword.Slayer), Is.False);
        }

        [Test]
        public void NativeSurvivorKeywordAbility_IsSharedWithOtherSurvivors()
        {
            var state = EmptyState();
            var bladerush = Instance(10, "dm05-4-55");
            var ballus = Instance(20, "dm05-6-55");
            state.Players[0].BattleZone.AddRange(new[] { bladerush, ballus });

            Assert.That(bladerush.Definition.Abilities.Single(ability =>
                ability.Keyword == CardKeyword.DoubleBreaker).IsSurvivorAbility,
                Is.True);
            Assert.That(new AbilityResolver().HasKeyword(state, ballus,
                CardKeyword.DoubleBreaker), Is.True);
        }

        [Test]
        public void GalliaZohl_BlockerIsItsSharedSurvivorAbility()
        {
            var state = EmptyState();
            var gallia = Instance(10, "dm05-8-55");
            var ballus = Instance(20, "dm05-6-55");
            state.Players[0].BattleZone.AddRange(new[] { gallia, ballus });

            Assert.That(gallia.Definition.Abilities.Single(ability =>
                ability.Keyword == CardKeyword.Blocker).IsSurvivorAbility,
                Is.True);
            Assert.That(new AbilityResolver().HasKeyword(state, ballus,
                CardKeyword.Blocker), Is.True);
        }

        [Test]
        public void Omnistrain_DoesNotTurnOrdinaryAbilitiesIntoSurvivorAbilities()
        {
            var state = EmptyState();
            var ordinaryWithAbility = Creature("ordinary-with-attack-draw");
            ordinaryWithAbility.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.ordinary-attack-draw",
                    AbilityTiming.OnAttack, AbilityOperation.Draw) { Amount = 1 }
            };
            var first = Instance(10, ordinaryWithAbility);
            var second = Instance(20, Creature("ordinary-without-ability"));
            var omnistrain = Instance(30, "promo-l2-y2");
            var wouldBeDrawn = Instance(40, Creature("would-be-drawn"));
            state.Players[0].BattleZone.AddRange(new[] { first, second, omnistrain });
            state.Players[0].Deck.Add(Instance(41, Creature("deck-filler")));
            state.Players[0].Deck.Add(wouldBeDrawn);

            var resolver = new AbilityResolver();
            Assert.That(resolver.HasKeyword(state, first, CardKeyword.Survivor), Is.True);
            Assert.That(resolver.HasKeyword(state, second, CardKeyword.Survivor), Is.True);

            resolver.Resolve(state, second, AbilityTiming.OnAttack);

            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Deck, Does.Contain(wouldBeDrawn));
        }

        private sealed class ChooseOneDecisionProvider : IAbilityDecisionProvider
        {
            public bool ChooseOptional(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state,
                int playerId, CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.Take(Math.Min(1, maximum)).ToArray();
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
