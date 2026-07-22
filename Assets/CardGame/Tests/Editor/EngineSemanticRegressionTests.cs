using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CardGame.Core.Tests
{
    public sealed class EngineSemanticRegressionTests
    {
        [Test]
        public void PutIntoBattleZoneByEffect_IsNotTreatedAsSummonedByPlay()
        {
            var state = EmptyState();
            var sourceDefinition = Creature("recruiter");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.recruit", AbilityTiming.OnSummon,
                    AbilityOperation.Move)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true
                        }
                    },
                    Destination = RuleZone.BattleZone
                }
            };
            var source = Instance(10, sourceDefinition);
            var recruit = Instance(11, Creature("recruit"));
            var aquaRider = Instance(20, TcgCatalog.ByName("Aqua Rider").Single());
            state.Players[0].Hand.Add(source);
            state.Players[0].Hand.Add(recruit);
            state.Players[0].Mana.Add(Instance(30, Creature("mana")));
            state.Players[1].BattleZone.Add(aquaRider);

            var result = new RulesEngine().SummonCreature(
                state, 0, source.InstanceId, new long[] { 30 });

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].BattleZone, Has.Member(recruit));
            Assert.That(aquaRider.TimedKeywords.Count(effect =>
                effect.Keyword == CardKeyword.Blocker), Is.EqualTo(1),
                "Aqua Rider should trigger for the played recruiter, not the recruited creature.");
        }

        [Test]
        public void EndTurn_ExpiresTemporaryChangesOnOpponentsCards()
        {
            var state = EmptyState();
            var sourceDefinition = Spell("temporary-debuff");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.debuff", AbilityTiming.OnCast,
                    AbilityOperation.ModifyPower)
                {
                    Value = -2000,
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
            var source = Instance(10, sourceDefinition);
            var opposingDefinition = Creature("opposing");
            opposingDefinition.Power = 3000;
            var opposingCreature = Instance(20, opposingDefinition);
            state.Players[0].Resolving.Add(source);
            state.Players[1].BattleZone.Add(opposingCreature);
            state.Players[1].Deck.Add(Instance(30, Creature("draw")));

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnCast);
            Assert.That(opposingCreature.TemporaryPower, Is.EqualTo(-2000));

            Assert.That(new RulesEngine().EndTurn(state, 0).Success, Is.True);
            Assert.That(opposingCreature.TemporaryPower, Is.Zero);
        }

        [Test]
        public void RemovingEvolutionTop_PreservesBaseSummoningSickness()
        {
            var state = EmptyState();
            var baseCreature = Instance(10, Creature("new-base"));
            baseCreature.EnteredBattleZoneOnTurn = state.TurnNumber;
            var evolutionDefinition = Creature("evolution");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            var evolution = Instance(11, evolutionDefinition);
            evolution.EnteredBattleZoneOnTurn = -1;
            evolution.EvolutionMaterials.Add(baseCreature);
            state.Players[0].BattleZone.Add(evolution);

            var sourceDefinition = Spell("de-evolution");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.de-evolve", AbilityTiming.OnCast,
                    AbilityOperation.MoveEvolutionTop)
                {
                    Destination = RuleZone.Graveyard,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            EvolutionOnly = true
                        }
                    }
                }
            };
            var source = Instance(20, sourceDefinition);
            state.Players[0].Resolving.Add(source);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[0].BattleZone, Does.Contain(baseCreature));
            Assert.That(baseCreature.EnteredBattleZoneOnTurn,
                Is.EqualTo(state.TurnNumber));
            var attack = new RulesEngine().DeclareAttack(
                state, 0, baseCreature.InstanceId, AttackTargetKind.Player);
            Assert.That(attack.Success, Is.False);
            Assert.That(attack.Error, Does.Contain("summoning sickness"));
        }

        [Test]
        public void RemovingVortexEvolutionTop_RebuildsRemainingCardsAsOnePile()
        {
            var state = EmptyState();
            var firstBase = Instance(10, Creature("vortex-top-base"));
            var secondBase = Instance(11, Creature("vortex-bottom-base"));
            firstBase.Tapped = false;
            secondBase.Tapped = false;
            var evolutionDefinition = Creature("vortex-evolution");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            var evolution = Instance(12, evolutionDefinition);
            evolution.Tapped = true;
            evolution.EvolutionMaterials.Add(firstBase);
            evolution.EvolutionMaterials.Add(secondBase);
            state.Players[0].BattleZone.Add(evolution);

            var sourceDefinition = Spell("de-evolve-vortex");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.de-evolve-vortex",
                    AbilityTiming.OnCast, AbilityOperation.MoveEvolutionTop)
                {
                    Destination = RuleZone.Graveyard,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            EvolutionOnly = true
                        }
                    }
                }
            };
            var source = Instance(20, sourceDefinition);
            state.Players[0].Resolving.Add(source);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Graveyard, Does.Contain(evolution));
            Assert.That(state.Players[0].BattleZone,
                Is.EquivalentTo(new[] { firstBase }));
            Assert.That(firstBase.EvolutionMaterials,
                Is.EquivalentTo(new[] { secondBase }));
            Assert.That(firstBase.Tapped, Is.True,
                "The rebuilt creature inherits the evolution pile's tapped state.");
        }

        [Test]
        public void MulticoloredCard_RequiresManaOfEveryPrintedCivilization()
        {
            var state = EmptyState();
            var multicolored = Creature("fire-nature");
            multicolored.Cost = 2;
            multicolored.Civilizations = new[]
            {
                Civilization.Fire,
                Civilization.Nature
            };
            var card = Instance(10, multicolored);
            var firstFire = Instance(20, Creature("first-fire"));
            var secondFire = Instance(21, Creature("second-fire"));
            var natureDefinition = Creature("nature");
            natureDefinition.Civilizations = new[] { Civilization.Nature };
            var nature = Instance(22, natureDefinition);
            state.Players[0].Hand.Add(card);
            state.Players[0].Mana.Add(firstFire);
            state.Players[0].Mana.Add(secondFire);
            state.Players[0].Mana.Add(nature);

            var invalid = new RulesEngine().SummonCreature(
                state, 0, card.InstanceId,
                new[] { firstFire.InstanceId, secondFire.InstanceId });

            Assert.That(invalid.Success, Is.False);
            Assert.That(invalid.Error, Does.Contain("Nature"));
            Assert.That(firstFire.Tapped, Is.False);
            Assert.That(secondFire.Tapped, Is.False);
            Assert.That(state.Players[0].Hand, Does.Contain(card));

            var valid = new RulesEngine().SummonCreature(
                state, 0, card.InstanceId,
                new[] { firstFire.InstanceId, nature.InstanceId });

            Assert.That(valid.Success, Is.True);
            Assert.That(firstFire.Tapped, Is.True);
            Assert.That(nature.Tapped, Is.True);
            Assert.That(state.Players[0].BattleZone, Does.Contain(card));
        }

        [Test]
        public void MulticoloredManaCard_CanPayForOnlyOneCivilizationAtATime()
        {
            var state = EmptyState();
            var multicolored = Creature("discounted-fire-nature");
            multicolored.Cost = 2;
            multicolored.Civilizations = new[]
            {
                Civilization.Fire,
                Civilization.Nature
            };
            var card = Instance(10, multicolored);
            var dualManaDefinition = Creature("dual-mana");
            dualManaDefinition.Civilizations = new[]
            {
                Civilization.Fire,
                Civilization.Nature
            };
            var dualMana = Instance(20, dualManaDefinition);
            var unrelatedMana = Instance(21, Creature("unrelated-mana", Civilization.Light));
            state.Players[0].Hand.Add(card);
            state.Players[0].Mana.Add(dualMana);
            state.Players[0].Mana.Add(unrelatedMana);

            var result = new RulesEngine().SummonCreature(
                state, 0, card.InstanceId,
                new[] { dualMana.InstanceId, unrelatedMana.InstanceId });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("different selected mana card"));
            Assert.That(state.Players[0].Hand, Does.Contain(card));
        }

        [Test]
        public void CivilizationRequirement_CanExceedReducedPayableCost()
        {
            var state = EmptyState();
            var multicolored = Creature("reduced-multicolored");
            multicolored.Cost = 1;
            multicolored.Civilizations = new[]
            {
                Civilization.Fire,
                Civilization.Nature
            };
            var card = Instance(10, multicolored);
            var fireMana = Instance(20, Creature("fire-mana", Civilization.Fire));
            var natureMana = Instance(21, Creature("nature-mana", Civilization.Nature));
            state.Players[0].Hand.Add(card);
            state.Players[0].Mana.Add(fireMana);
            state.Players[0].Mana.Add(natureMana);

            var result = new RulesEngine().SummonCreature(
                state, 0, card.InstanceId,
                new[] { fireMana.InstanceId, natureMana.InstanceId });

            Assert.That(result.Success, Is.True);
            Assert.That(fireMana.Tapped, Is.True);
            Assert.That(natureMana.Tapped, Is.True);
        }

        [Test]
        public void MovingLastDeckCard_LosesImmediatelyAndInterruptsAbilityResolution()
        {
            var state = EmptyState();
            var sourceDefinition = Creature("deck-mover");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.move-last-card", AbilityTiming.OnSummon,
                    AbilityOperation.Move)
                {
                    Destination = RuleZone.ManaZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Deck,
                        Mode = RuleSelectionMode.Top,
                        Minimum = 1,
                        Maximum = 1
                    }
                },
                new RuleAbilityDefinition("test.must-not-resolve", AbilityTiming.OnSummon,
                    AbilityOperation.ModifyPower)
                {
                    Value = 5000,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1
                    }
                }
            };
            var source = Instance(10, sourceDefinition);
            var lastDefinition = Creature("last-card");
            lastDefinition.Civilizations = new[]
            {
                Civilization.Fire,
                Civilization.Nature
            };
            var lastCard = Instance(20, lastDefinition);
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Deck.Add(lastCard);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnSummon);

            Assert.That(state.WinnerPlayerId, Is.EqualTo(1));
            Assert.That(state.WinReason, Is.EqualTo("Deck out"));
            Assert.That(state.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(state.Players[0].Mana, Does.Contain(lastCard));
            Assert.That(lastCard.Tapped, Is.True,
                "A multicolored card still enters the mana zone tapped before the game ends.");
            Assert.That(source.TemporaryPower, Is.Zero,
                "Deck-out is immediate and interrupts the rest of the resolving ability.");
        }

        [Test]
        public void DestroyedTrigger_ResolvesAfterSourceLeavesBattleZone()
        {
            var state = EmptyState();
            var stallob = Instance(10, TcgCatalog.ByName("Stallob, the Lifequasher").Single());
            var ally = Instance(11, Creature("ally"));
            var enemy = Instance(20, Creature("enemy"));
            state.Players[0].BattleZone.Add(stallob);
            state.Players[0].BattleZone.Add(ally);
            state.Players[1].BattleZone.Add(enemy);

            new AbilityResolver().DestroyCard(state, stallob);

            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[1].BattleZone, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Is.EquivalentTo(new[] { stallob, ally }));
            Assert.That(state.Players[1].Graveyard, Does.Contain(enemy));
        }

        [Test]
        public void LeavingBattleZone_ClearsTransientStateAfterLeaveTriggersResolve()
        {
            var state = EmptyState();
            var sourceDefinition = Creature("bounce-source");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.bounce", AbilityTiming.OnSummon,
                    AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand,
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
            var source = Instance(10, sourceDefinition);
            var target = Instance(20, Creature("temporarily-modified-target"));
            target.Tapped = true;
            target.EnteredBattleZoneOnTurn = state.TurnNumber;
            target.TemporaryPowerAttacker = 2000;
            target.TemporaryPower = 3000;
            target.TemporaryDoubleBreaker = true;
            target.TemporaryBreakerCount = 3;
            target.TemporaryKeywords.Add(CardKeyword.Blocker);
            target.TimedKeywords.Add(new TimedKeywordEffect
            {
                Keyword = CardKeyword.Slayer,
                Value = 1,
                Duration = AbilityDuration.UntilEndOfTurn,
                AppliedByPlayerId = 0
            });
            target.TimedRuleAbilities.Add(new TimedRuleEffect
            {
                Ability = new RuleAbilityDefinition("test.granted-leave-draw",
                    AbilityTiming.OnLeaveBattleZone, AbilityOperation.Draw)
                {
                    Amount = 1
                },
                Duration = AbilityDuration.UntilEndOfTurn,
                AppliedByPlayerId = 0
            });
            target.AttackedThisTurn = true;
            target.BrokeShieldThisTurn = true;
            target.ShieldsBrokenInLastAttack = 2;
            target.BrokenByAttackerInstanceId = 999;
            target.TemporaryRaces.Add("Dragon");
            target.ChosenRace = "Dragon";
            target.FaceUp = true;
            target.WasAttackedThisTurn = true;
            target.ChosenCivilization = Civilization.Nature;
            target.HasChosenCivilization = true;
            state.Players[0].BattleZone.Add(source);
            state.Players[1].BattleZone.Add(target);
            state.Players[1].Deck.Add(Instance(30, Creature("draw-buffer")));
            var drawn = Instance(31, Creature("drawn-by-leave-trigger"));
            state.Players[1].Deck.Add(drawn);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnSummon);

            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { target, drawn }),
                "The granted leave trigger must resolve before its grant is cleared.");
            Assert.That(target.Tapped, Is.False);
            Assert.That(target.EnteredBattleZoneOnTurn, Is.EqualTo(-1));
            Assert.That(target.TemporaryPowerAttacker, Is.Zero);
            Assert.That(target.TemporaryPower, Is.Zero);
            Assert.That(target.TemporaryDoubleBreaker, Is.False);
            Assert.That(target.TemporaryBreakerCount, Is.Zero);
            Assert.That(target.TemporaryKeywords, Is.Empty);
            Assert.That(target.TimedKeywords, Is.Empty);
            Assert.That(target.TimedRuleAbilities, Is.Empty);
            Assert.That(target.AttackedThisTurn, Is.False);
            Assert.That(target.BrokeShieldThisTurn, Is.False);
            Assert.That(target.ShieldsBrokenInLastAttack, Is.Zero);
            Assert.That(target.BrokenByAttackerInstanceId, Is.Zero);
            Assert.That(target.TemporaryRaces, Is.Empty);
            Assert.That(target.ChosenRace, Is.Empty);
            Assert.That(target.FaceUp, Is.False);
            Assert.That(target.WasAttackedThisTurn, Is.False);
            Assert.That(target.HasChosenCivilization, Is.False);
        }

        [Test]
        public void DestroyedEvent_UsesLastKnownTemporaryRaceBeforeZoneReset()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var watcherDefinition = Creature("dragon-destruction-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.watch-destroyed-dragon",
                    AbilityTiming.OnOtherCreatureDestroyed, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Condition = new AbilityConditionDefinition
                    {
                        EventControlledByOpponent = true,
                        EventCardMatchesFilter = true,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Race = "Dragon"
                        }
                    }
                }
            };
            var watcher = Instance(10, watcherDefinition);
            var targetDefinition = Creature("temporary-dragon");
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.reanimate-before-opponent-trigger",
                    AbilityTiming.OnDestroyed, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var target = Instance(20, targetDefinition);
            target.TemporaryRaces.Add("Dragon");
            var drawn = Instance(30, Creature("drawn-for-dragon"));
            state.Players[0].BattleZone.Add(watcher);
            state.Players[1].BattleZone.Add(target);
            state.Players[0].Deck.Add(Instance(31, Creature("draw-buffer")));
            state.Players[0].Deck.Add(drawn);

            Assert.That(new AbilityResolver().DestroyCard(state, target), Is.True);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { drawn }));
            Assert.That(state.Players[1].BattleZone, Does.Contain(target),
                "The active player's destroyed trigger resolves first and reanimates the card.");
            Assert.That(target.TemporaryRaces, Is.Empty,
                "Last-known characteristics must expire after the destruction trigger window.");
        }

        [Test]
        public void DestroyThenReanimate_ClearsOldObjectStateBeforeBattleZoneEntry()
        {
            var state = EmptyState();
            var targetDefinition = Creature("self-reanimator");
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.self-reanimate",
                    AbilityTiming.OnDestroyed, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var target = Instance(20, targetDefinition);
            target.TemporaryPower = 5000;
            target.TemporaryKeywords.Add(CardKeyword.SpeedAttacker);
            target.AttackedThisTurn = true;
            target.TimedRuleAbilities.Add(new TimedRuleEffect
            {
                Ability = new RuleAbilityDefinition("test.stale-entry-draw",
                    AbilityTiming.OnSummon, AbilityOperation.Draw)
                {
                    Amount = 1
                },
                Duration = AbilityDuration.UntilEndOfTurn,
                AppliedByPlayerId = 0
            });
            state.Players[0].BattleZone.Add(target);
            state.Players[0].Deck.Add(Instance(30, Creature("must-not-be-drawn")));
            state.Players[0].Deck.Add(Instance(31, Creature("draw-buffer")));

            Assert.That(new AbilityResolver().DestroyCard(state, target), Is.True);

            Assert.That(state.Players[0].BattleZone, Does.Contain(target));
            Assert.That(state.Players[0].Graveyard, Has.No.Member(target));
            Assert.That(state.Players[0].Hand, Is.Empty,
                "A granted ability from the previous object must not trigger after re-entry.");
            Assert.That(target.TemporaryPower, Is.Zero);
            Assert.That(target.TemporaryKeywords, Is.Empty);
            Assert.That(target.TimedRuleAbilities, Is.Empty);
            Assert.That(target.AttackedThisTurn, Is.False);
            Assert.That(target.EnteredBattleZoneOnTurn, Is.EqualTo(state.TurnNumber));
        }

        [Test]
        public void PowerLimitedEffect_UsesCurrentPowerIncludingStaticAuras()
        {
            var state = EmptyState();
            var auraDefinition = Creature("aura");
            auraDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.aura", AbilityTiming.Static,
                    AbilityOperation.ModifyPower)
                {
                    Value = 2000,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true
                        }
                    }
                }
            };
            var aura = Instance(20, auraDefinition);
            aura.Definition.Power = 5000;
            var printedTwoThousand = Instance(21, Creature("buffed-target"));
            printedTwoThousand.Definition.Power = 2000;
            state.Players[1].BattleZone.Add(aura);
            state.Players[1].BattleZone.Add(printedTwoThousand);

            var sourceDefinition = Spell("power-destroy");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.power-destroy", AbilityTiming.OnCast,
                    AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 0,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            PowerAtMost = 2000
                        }
                    }
                }
            };
            var source = Instance(10, sourceDefinition);
            state.Players[0].Resolving.Add(source);

            var resolver = new AbilityResolver();
            Assert.That(resolver.CurrentPower(state, printedTwoThousand), Is.EqualTo(4000));
            resolver.Resolve(state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[1].BattleZone, Does.Contain(printedTwoThousand));
            Assert.That(state.Players[1].Graveyard, Is.Empty);
        }

        [Test]
        public void DestroyedSurvivor_DoesNotBorrowAbilitiesAfterLeavingBattleZone()
        {
            var state = EmptyState();
            var victimDefinition = Creature("survivor-victim");
            victimDefinition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.Survivor)
            };
            var sharingDefinition = Creature("survivor-sharing-draw");
            sharingDefinition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.Survivor)
            };
            sharingDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.survivor-destroyed-draw",
                    AbilityTiming.OnDestroyed, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var victim = Instance(10, victimDefinition);
            var sharingSurvivor = Instance(11, sharingDefinition);
            state.Players[0].BattleZone.Add(victim);
            state.Players[0].BattleZone.Add(sharingSurvivor);
            state.Players[0].Deck.Add(Instance(20, Creature("draw-one")));
            state.Players[0].Deck.Add(Instance(21, Creature("draw-two")));

            new AbilityResolver().DestroyCard(state, victim);

            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(victim));
            Assert.That(state.Players[0].BattleZone, Does.Contain(sharingSurvivor));
        }

        [Test]
        public void LeaveBattleZoneTrigger_ResolvesAfterSourceChangesZones()
        {
            var state = EmptyState();
            var spellDefinition = Spell("bounce-naga");
            spellDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.bounce-naga", AbilityTiming.OnCast,
                    AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            Name = "Cruel Naga, Avatar of Fate"
                        }
                    }
                }
            };
            var spell = Instance(10, spellDefinition);
            var ally = Instance(11, Creature("ally"));
            var naga = Instance(20, TcgCatalog.ByName("Cruel Naga, Avatar of Fate").Single());
            var nagaAlly = Instance(21, Creature("naga-ally"));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(ally);
            state.Players[1].BattleZone.Add(naga);
            state.Players[1].BattleZone.Add(nagaAlly);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Hand, Does.Contain(naga));
            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[1].BattleZone, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(ally));
            Assert.That(state.Players[1].Graveyard, Does.Contain(nagaAlly));
        }

        [Test]
        public void EvolutionPile_PreservesNestedSourcesUntilThePileLeavesBattleZone()
        {
            var state = EmptyState();
            var bottom = Instance(10, Creature("bottom"));
            var middleDefinition = Creature("middle-evolution");
            middleDefinition.Type = CardType.EvolutionCreature;
            var middle = Instance(11, middleDefinition);
            middle.EvolutionMaterials.Add(bottom);
            var topDefinition = Creature("top-evolution");
            topDefinition.Type = CardType.EvolutionCreature;
            var top = Instance(12, topDefinition);
            top.EvolutionMaterials.Add(middle);
            state.Players[0].BattleZone.Add(top);

            Assert.That(top.EvolutionMaterials, Is.EquivalentTo(new[] { middle }));
            Assert.That(middle.EvolutionMaterials, Is.EquivalentTo(new[] { bottom }));

            new AbilityResolver().DestroyCard(state, top);

            Assert.That(state.Players[0].Graveyard,
                Is.EquivalentTo(new[] { top, middle, bottom }));
            Assert.That(top.EvolutionMaterials, Is.Empty);
            Assert.That(middle.EvolutionMaterials, Is.Empty);
        }

        [Test]
        public void EvolutionPileMovedToDeck_UsesOwnersChosenTopToBottomOrder()
        {
            var state = EmptyState();
            var bottom = Instance(10, Creature("bottom-source"));
            var middleDefinition = Creature("middle-evolution");
            middleDefinition.Type = CardType.EvolutionCreature;
            var middle = Instance(20, middleDefinition);
            middle.EvolutionMaterials.Add(bottom);
            var topDefinition = Creature("top-evolution");
            topDefinition.Type = CardType.EvolutionCreature;
            var top = Instance(30, topDefinition);
            top.EvolutionMaterials.Add(middle);
            var spellDefinition = Spell("pile-to-deck");
            spellDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.pile-to-deck",
                    AbilityTiming.OnCast, AbilityOperation.Move)
                {
                    Destination = RuleZone.Deck,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = top.Definition.Name
                        }
                    }
                }
            };
            var spell = Instance(40, spellDefinition);
            state.Players[0].BattleZone.Add(top);
            state.Players[0].Resolving.Add(spell);
            state.Players[0].Deck.Add(Instance(90, Creature("untouched-bottom")));

            new AbilityResolver(new AscendingOrderDecisionProvider()).Resolve(
                state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Deck.Select(card => card.InstanceId),
                Is.EqualTo(new long[] { 90, 30, 20, 10 }));
        }

        [Test]
        public void EffectPuttingEvolutionIntoBattleZone_RequiresAndAttachesValidSource()
        {
            var state = EmptyState();
            var evolutionDefinition = Creature("effect-evolution");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            evolutionDefinition.EvolutionRace = "Test Base";
            evolutionDefinition.EvolutionRaces = new[] { "Test Base" };
            var evolution = Instance(20, evolutionDefinition);
            var baseDefinition = Creature("valid-base");
            baseDefinition.Races = new[] { "Test Base" };
            var evolutionBase = Instance(21, baseDefinition);
            evolutionBase.EnteredBattleZoneOnTurn = state.TurnNumber;
            evolutionBase.Tapped = true;
            state.Players[0].Hand.Add(evolution);
            state.Players[0].BattleZone.Add(evolutionBase);
            var spell = Instance(10, PutNamedHandCreatureIntoBattle("put-evolution",
                evolutionDefinition.Name));
            state.Players[0].Resolving.Add(spell);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Has.No.Member(evolution));
            Assert.That(state.Players[0].BattleZone, Does.Contain(evolution));
            Assert.That(state.Players[0].BattleZone, Has.No.Member(evolutionBase));
            Assert.That(evolution.EvolutionMaterials, Is.EquivalentTo(new[] { evolutionBase }));
            Assert.That(evolution.Tapped, Is.True);
            Assert.That(evolution.EnteredBattleZoneOnTurn, Is.EqualTo(-1));
        }

        [Test]
        public void EffectPuttingEvolutionIntoBattleZone_LeavesItInPlaceWithoutValidSource()
        {
            var state = EmptyState();
            var evolutionDefinition = Creature("effect-evolution");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            evolutionDefinition.EvolutionRace = "Missing Race";
            evolutionDefinition.EvolutionRaces = new[] { "Missing Race" };
            var evolution = Instance(20, evolutionDefinition);
            state.Players[0].Hand.Add(evolution);
            var spell = Instance(10, PutNamedHandCreatureIntoBattle("put-evolution",
                evolutionDefinition.Name));
            state.Players[0].Resolving.Add(spell);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Does.Contain(evolution));
            Assert.That(state.Players[0].BattleZone, Has.No.Member(evolution));
        }

        [Test]
        public void EffectPuttingEvolutionIntoBattleZone_UsesOwnersChosenValidSource()
        {
            var state = EmptyState();
            var evolutionDefinition = Creature("chosen-effect-evolution");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            evolutionDefinition.EvolutionRace = "Test Base";
            evolutionDefinition.EvolutionRaces = new[] { "Test Base" };
            var evolution = Instance(20, evolutionDefinition);
            var baseDefinition = Creature("valid-base");
            baseDefinition.Races = new[] { "Test Base" };
            var firstBase = Instance(21, baseDefinition);
            var chosenBase = Instance(22, baseDefinition);
            state.Players[0].Hand.Add(evolution);
            state.Players[0].BattleZone.Add(firstBase);
            state.Players[0].BattleZone.Add(chosenBase);
            var spell = Instance(10, PutNamedHandCreatureIntoBattle(
                "put-chosen-evolution", evolutionDefinition.Name));
            state.Players[0].Resolving.Add(spell);

            new AbilityResolver(new HighestInstanceDecisionProvider()).Resolve(
                state, spell, AbilityTiming.OnCast);

            Assert.That(evolution.EvolutionMaterials,
                Is.EquivalentTo(new[] { chosenBase }));
            Assert.That(state.Players[0].BattleZone, Does.Contain(firstBase));
        }

        [Test]
        public void VortexEvolution_MatchesOverlappingBaseRacesWithoutGreedyFailure()
        {
            var state = EmptyState();
            var evolutionDefinition = Creature("overlapping-vortex");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            evolutionDefinition.Cost = 0;
            evolutionDefinition.IsVortexEvolution = true;
            evolutionDefinition.EvolutionRequirements = new[]
            {
                new EvolutionBaseRequirement { Races = new[] { "Race A" } },
                new EvolutionBaseRequirement { Races = new[] { "Race B" } }
            };
            var evolution = Instance(10, evolutionDefinition);
            var flexibleDefinition = Creature("flexible-base");
            flexibleDefinition.Races = new[] { "Race A", "Race B" };
            var raceAOnlyDefinition = Creature("race-a-only-base");
            raceAOnlyDefinition.Races = new[] { "Race A" };
            var flexible = Instance(20, flexibleDefinition);
            var raceAOnly = Instance(21, raceAOnlyDefinition);
            state.Players[0].Hand.Add(evolution);
            state.Players[0].BattleZone.Add(flexible);
            state.Players[0].BattleZone.Add(raceAOnly);
            var mana = Instance(30, Creature("evolution-mana"));
            state.Players[0].Mana.Add(mana);

            var result = new RulesEngine().SummonCreatureWithEvolutionBases(
                state, 0, evolution.InstanceId, new[] { mana.InstanceId },
                new[] { flexible.InstanceId, raceAOnly.InstanceId });

            Assert.That(result.Success, Is.True);
            Assert.That(evolution.EvolutionMaterials,
                Is.EquivalentTo(new[] { flexible, raceAOnly }));
        }

        [Test]
        public void IfYouDestroyFollowup_DoesNotResolveWhenDestructionIsPrevented()
        {
            var state = EmptyState();
            var sourceDefinition = Spell("conditional-destruction");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.conditional-destruction", AbilityTiming.OnCast,
                    AbilityOperation.Destroy)
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
                },
                new RuleAbilityDefinition("test.conditional-destruction", AbilityTiming.OnCast,
                    AbilityOperation.Draw)
                {
                    Amount = 1,
                    RequiresPreviousSuccess = true
                }
            };
            var protectedDefinition = Creature("protected");
            protectedDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.prevent-destruction", AbilityTiming.Static,
                    AbilityOperation.PreventDestruction)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var source = Instance(10, sourceDefinition);
            var protectedCreature = Instance(20, protectedDefinition);
            state.Players[0].Resolving.Add(source);
            state.Players[0].Deck.Add(Instance(30, Creature("draw")));
            state.Players[0].Deck.Add(Instance(31, Creature("deck-filler")));
            state.Players[1].BattleZone.Add(protectedCreature);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[1].BattleZone, Does.Contain(protectedCreature));
            Assert.That(state.Players[1].Graveyard, Is.Empty);
            Assert.That(state.Players[0].Hand, Is.Empty,
                "The 'if you do' draw must not happen when destruction was prevented.");
        }

        [Test]
        public void IfYouDestroyFollowup_ResolvesWhenDestructionCostIsReplaced()
        {
            var state = EmptyState();
            var sourceDefinition = Spell("conditional-replaced-destruction");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.conditional-replaced-destruction", AbilityTiming.OnCast,
                    AbilityOperation.Destroy)
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
                },
                new RuleAbilityDefinition("test.conditional-replaced-destruction", AbilityTiming.OnCast,
                    AbilityOperation.Draw)
                {
                    Amount = 1,
                    RequiresPreviousSuccess = true
                }
            };
            var victimDefinition = Creature("returns-instead-of-destruction");
            victimDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.destroy-to-hand",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand
                }
            };
            var source = Instance(10, sourceDefinition);
            var victim = Instance(20, victimDefinition);
            var drawn = Instance(30, Creature("drawn-after-replacement"));
            state.Players[0].Resolving.Add(source);
            state.Players[0].Deck.Add(Instance(31, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].BattleZone.Add(victim);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Hand, Does.Contain(victim));
            Assert.That(state.Players[0].Hand, Does.Contain(drawn),
                "Replacing the destruction still pays an 'if you do' destruction cost.");
        }

        [Test]
        public void ZeroPowerCreature_IsRecheckedAfterItsProtectionSourceLeaves()
        {
            var state = EmptyState();
            var protectedCreature = Instance(10, Creature("zero-power-protected"));
            protectedCreature.TemporaryPower = -protectedCreature.Definition.Power;
            var mihail = Instance(20,
                TcgCatalog.ByName("Mihail, Celestial Elemental").Single());
            mihail.TemporaryPower = -mihail.Definition.Power;
            state.Players[0].BattleZone.Add(protectedCreature);
            state.Players[0].BattleZone.Add(mihail);

            new AbilityResolver().ResolveStateBasedActions(state);

            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[0].Graveyard,
                Is.EquivalentTo(new[] { mihail, protectedCreature }));
        }

        [Test]
        public void BattleDestruction_DoesNotDestroyOrTriggerTheSameCreatureTwice()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("chain-destroyer");
            attackerDefinition.Power = 2000;
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.destroy-opponent-on-destroyed",
                    AbilityTiming.OnDestroyed, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var defenderDefinition = Creature("draw-when-destroyed");
            defenderDefinition.Power = 2000;
            defenderDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-on-destroyed",
                    AbilityTiming.OnDestroyed, AbilityOperation.Draw) { Amount = 1 }
            };
            var attacker = Instance(10, attackerDefinition);
            var defender = Instance(20, defenderDefinition);
            attacker.EnteredBattleZoneOnTurn = 0;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(defender);
            state.Players[1].Deck.Add(Instance(30, Creature("draw-one")));
            state.Players[1].Deck.Add(Instance(31, Creature("deck-filler-one")));
            state.Players[1].Deck.Add(Instance(32, Creature("deck-filler-two")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Graveyard.Count(card => card == defender),
                Is.EqualTo(1));
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(1),
                "The defender's destroyed trigger must resolve exactly once.");
        }

        [Test]
        public void MultipleExtraTurnEffects_QueueEveryGrantedTurnInOrder()
        {
            var state = EmptyState();
            var balesk = Instance(10,
                TcgCatalog.ByName("Balesk Baj, the Timeburner").Single());
            state.Players[0].BattleZone.Add(balesk);
            for (var index = 0; index < 4; index++)
                state.Players[0].Deck.Add(Instance(20 + index,
                    Creature("own-deck-" + index)));
            state.Players[1].Deck.Add(Instance(30, Creature("opponent-deck-one")));
            state.Players[1].Deck.Add(Instance(31, Creature("opponent-deck-two")));
            var resolver = new AbilityResolver();
            resolver.Resolve(state, balesk, AbilityTiming.OnAttackPlayerUnblocked);
            resolver.Resolve(state, balesk, AbilityTiming.OnAttackPlayerUnblocked);
            var engine = new RulesEngine();

            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(0));
            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(0));
            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(1));
            Assert.That(state.IsFinished, Is.False);
        }

        [Test]
        public void ExtraTurnForNextPlayer_DoesNotConsumeThatPlayersNormalTurn()
        {
            var state = EmptyState();
            var balesk = Instance(10,
                TcgCatalog.ByName("Balesk Baj, the Timeburner").Single());
            state.Players[1].BattleZone.Add(balesk);
            for (var index = 0; index < 4; index++)
                state.Players[1].Deck.Add(Instance(20 + index,
                    Creature("opponent-deck-" + index)));
            state.Players[0].Deck.Add(Instance(30, Creature("own-deck-one")));
            state.Players[0].Deck.Add(Instance(31, Creature("own-deck-two")));
            new AbilityResolver().Resolve(state, balesk,
                AbilityTiming.OnAttackPlayerUnblocked);
            var engine = new RulesEngine();

            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(1), "Granted extra turn");
            Assert.That(engine.EndTurn(state, 1).Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(1), "Normal scheduled turn");
            Assert.That(engine.EndTurn(state, 1).Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(0));
            Assert.That(state.IsFinished, Is.False);
        }

        [Test]
        public void ZeroPowerCreature_IsDestroyedWhenItsSupportingPowerDisappears()
        {
            var state = EmptyState();
            var anrist = Instance(10,
                TcgCatalog.ByName("Terradragon Anrist Vhal").Single());
            var supportDefinition = Creature("nature-support");
            supportDefinition.Civilizations = new[] { Civilization.Nature };
            var support = Instance(11, supportDefinition);
            state.Players[0].BattleZone.Add(anrist);
            state.Players[0].BattleZone.Add(support);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentPower(state, anrist), Is.EqualTo(2000));
            Assert.That(resolver.DestroyCard(state, support), Is.True);

            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[0].Graveyard,
                Is.EquivalentTo(new[] { support, anrist }));
        }

        [Test]
        public void ZeroPowerCreature_IsDestroyedImmediatelyAfterEnteringBattleZone()
        {
            var state = EmptyState();
            var ogrist = Instance(10,
                TcgCatalog.ByName("Magmadragon Ogrist Vhal").Single());
            state.Players[0].BattleZone.Add(ogrist);

            new AbilityResolver().Resolve(state, ogrist, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].BattleZone, Has.No.Member(ogrist));
            Assert.That(state.Players[0].Graveyard, Does.Contain(ogrist));
        }

        [Test]
        public void ShieldTriggerThisTurnEffect_ExpiresAtTheCurrentTurnsEnd()
        {
            var state = EmptyState();
            var fruit = Instance(10, TcgCatalog.ByName("Fruit of Eternity").Single());
            var firstVictim = Instance(20, Creature("first-victim"));
            state.Players[1].Hand.Add(fruit);
            state.Players[1].BattleZone.Add(firstVictim);
            state.PendingShieldTriggers.Add(fruit);
            state.Players[1].Deck.Add(Instance(30, Creature("draw")));
            state.Players[1].Deck.Add(Instance(31, Creature("deck-filler")));
            var engine = new RulesEngine();

            Assert.That(engine.CastShieldTrigger(state, 1, fruit.InstanceId).Success,
                Is.True);
            Assert.That(state.SpellCastThisTurn[1], Is.True);
            Assert.That(new AbilityResolver().DestroyCard(state, firstVictim), Is.False,
                "The replacement moved the creature to mana instead of destroying it.");
            Assert.That(state.Players[1].Mana, Does.Contain(firstVictim));

            Assert.That(engine.EndTurn(state, 0).Success, Is.True);
            Assert.That(state.SpellCastThisTurn[1], Is.False);
            Assert.That(state.DestroyedCreaturesGoToManaThisTurn[1], Is.False);

            var secondVictim = Instance(21, Creature("second-victim"));
            state.Players[1].BattleZone.Add(secondVictim);
            Assert.That(new AbilityResolver().DestroyCard(state, secondVictim), Is.True);
            Assert.That(state.Players[1].Graveyard, Does.Contain(secondVictim));
        }

        [Test]
        public void CharacteristicBlocker_IsAValidBlockerFilterTargetWithoutBlockingEverything()
        {
            var state = EmptyState();
            var amnis = Instance(10,
                TcgCatalog.ByName("Amnis, Holy Elemental").Single());
            var darknessAttacker = Instance(20,
                Creature("darkness-attacker", Civilization.Darkness));
            var fireAttacker = Instance(21,
                Creature("fire-attacker", Civilization.Fire));
            var destroyerDefinition = Spell("blocker-destroyer");
            destroyerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.destroy-blocker", AbilityTiming.OnCast,
                    AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            HasBlocker = true
                        }
                    }
                }
            };
            var destroyer = Instance(30, destroyerDefinition);
            state.Players[0].Resolving.Add(destroyer);
            state.Players[1].BattleZone.Add(amnis);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CanUseAsBlocker(state, amnis, darknessAttacker), Is.True);
            Assert.That(resolver.CanUseAsBlocker(state, amnis, fireAttacker), Is.False,
                "Darkness blocker must not become a general blocker.");

            resolver.Resolve(state, destroyer, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Graveyard, Does.Contain(amnis),
                "Effects referring to a creature that has blocker include characteristic blockers.");
        }

        [Test]
        public void MandatoryInternalChoice_UsesLegalFallbackWhenDecisionProviderReturnsNull()
        {
            var state = EmptyState();
            var sourceDefinition = Spell("shield-reducer");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.reduce-shields", AbilityTiming.OnCast,
                    AbilityOperation.ReduceOpponentShieldsToOwnCount)
            };
            var source = Instance(10, sourceDefinition);
            var ownShield = Instance(20, Creature("own-shield"));
            var opposingShields = Enumerable.Range(0, 3)
                .Select(index => Instance(30 + index, Creature("opposing-shield-" + index)))
                .ToArray();
            state.Players[0].Resolving.Add(source);
            state.Players[0].Shields.Add(ownShield);
            state.Players[1].Shields.AddRange(opposingShields);

            new AbilityResolver(new NullDecisionProvider()).Resolve(
                state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Shields, Has.Count.EqualTo(1));
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(2));
            Assert.That(state.Players[1].Shields.Single().InstanceId,
                Is.EqualTo(opposingShields.Min(card => card.InstanceId)));
        }

        [Test]
        public void CardAddedAsShieldFaceDown_DoesNotRetainAnEarlierFaceUpFlag()
        {
            var state = EmptyState();
            var sourceDefinition = Spell("add-face-down-shield");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.add-face-down-shield", AbilityTiming.OnCast,
                    AbilityOperation.Move)
                {
                    Destination = RuleZone.ShieldZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1
                    }
                }
            };
            var source = Instance(10, sourceDefinition);
            var formerFaceUpShield = Instance(20, Creature("reused-shield"));
            formerFaceUpShield.FaceUp = true;
            state.Players[0].Resolving.Add(source);
            state.Players[0].Hand.Add(formerFaceUpShield);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Shields, Does.Contain(formerFaceUpShield));
            Assert.That(formerFaceUpShield.FaceUp, Is.False);
        }

        [Test]
        public void MustAttackRequirement_IsSatisfiedAfterCreatureAttackedOnceThisTurn()
        {
            var state = EmptyState();
            var definition = Creature("must-attack-once");
            definition.Abilities = new[] { new KeywordAbility(CardKeyword.MustAttackIfAble) };
            var creature = Instance(10, definition);
            creature.AttackedThisTurn = true;
            creature.Tapped = false;
            state.Players[0].BattleZone.Add(creature);
            state.Players[1].Shields.Add(Instance(20, Creature("shield")));
            state.Players[1].Deck.Add(Instance(21, Creature("next-draw")));
            state.Players[1].Deck.Add(Instance(22, Creature("deck-filler")));

            var result = new RulesEngine().EndTurn(state, 0);

            Assert.That(result.Success, Is.True);
            Assert.That(state.ActivePlayerId, Is.EqualTo(1));
        }

        [Test]
        public void MustAttackRequirement_RecognizesUntappedCreatureMadeAttackable()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("must-attack-untapped-target");
            attackerDefinition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.MustAttackIfAble),
                new KeywordAbility(CardKeyword.CannotAttackPlayers)
            };
            var attacker = Instance(10, attackerDefinition);
            var target = Instance(20, Creature("attackable-while-untapped"));
            target.TemporaryKeywords.Add(CardKeyword.AttackableWhileUntapped);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(target);

            var result = new RulesEngine().EndTurn(state, 0);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("must attack"));
        }

        [Test]
        public void UnattackablePreferredTarget_DoesNotPreventPlayerAttack()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("ordinary-attacker"));
            var targetDefinition = Creature("unattackable-taunt");
            targetDefinition.Abilities = new[]
                { new KeywordAbility(CardKeyword.CannotBeAttacked) };
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.prefer-unattackable", AbilityTiming.Static,
                    AbilityOperation.PreferAttackSource)
            };
            var target = Instance(20, targetDefinition);
            target.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(target);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));

            var result = new RulesEngine().DeclareAttack(
                state, 0, attacker.InstanceId, AttackTargetKind.Player);

            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void MandatoryBlocker_CanDeclineWhenAttackerPowerLimitMakesBlockIllegal()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("blocker-power-limit-attacker");
            attackerDefinition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.UnblockableByPowerAtMost, 3000)
            };
            var blockerDefinition = Creature("mandatory-small-blocker");
            blockerDefinition.Power = 2000;
            blockerDefinition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.BlockIfAble)
            };
            var attacker = Instance(10, attackerDefinition);
            var blocker = Instance(20, blockerDefinition);
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(blocker);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            var result = engine.DeclineBlockAndResolve(state, 1);

            Assert.That(result.Success, Is.True);
            Assert.That(state.PendingAttack, Is.Null);
        }

        [Test]
        public void UntilEndOfTurnKeywordFromNonActivePlayer_ExpiresAtCurrentTurnEnd()
        {
            var state = EmptyState();
            var sourceDefinition = Spell("defending-shield-trigger");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.defender-grants-slayer", AbilityTiming.OnCast,
                    AbilityOperation.GrantKeyword)
                {
                    Keyword = CardKeyword.Slayer,
                    Duration = AbilityDuration.UntilEndOfTurn,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var source = Instance(10, sourceDefinition);
            var target = Instance(20, Creature("defending-creature"));
            state.Players[1].Resolving.Add(source);
            state.Players[1].BattleZone.Add(target);
            state.Players[1].Deck.Add(Instance(30, Creature("next-draw")));
            var resolver = new AbilityResolver();

            resolver.Resolve(state, source, AbilityTiming.OnCast);
            Assert.That(resolver.HasKeyword(state, target, CardKeyword.Slayer), Is.True);

            Assert.That(new RulesEngine().EndTurn(state, 0).Success, Is.True);
            Assert.That(resolver.HasKeyword(state, target, CardKeyword.Slayer), Is.False);
        }

        [Test]
        public void SacrificeReplacement_DoesNotChainTheSaviorsOwnReplacement()
        {
            var state = EmptyState();
            var saviorDefinition = Creature("protected-savior");
            saviorDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.sacrifice-instead",
                    AbilityTiming.ReplacementDestroy,
                    AbilityOperation.DestroySourceInstead)
                {
                    Optional = true,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true,
                            Race = "Armored Dragon"
                        }
                    }
                },
                new RuleAbilityDefinition("test.prevent-savior-destruction",
                    AbilityTiming.Static,
                    AbilityOperation.PreventDestruction)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var dragonDefinition = Creature("armored-dragon");
            dragonDefinition.Races = new[] { "Armored Dragon" };
            var savior = Instance(10, saviorDefinition);
            var dragon = Instance(20, dragonDefinition);
            state.Players[0].BattleZone.Add(savior);
            state.Players[0].BattleZone.Add(dragon);

            Assert.That(new AbilityResolver().DestroyCard(state, dragon), Is.False,
                "The dragon's destruction is replaced by destroying the saver.");
            Assert.That(state.Players[0].Graveyard, Does.Contain(savior));
            Assert.That(state.Players[0].BattleZone, Does.Contain(dragon));
        }

        [Test]
        public void DestructionReplacementThatMovesCard_StillTriggersLeaveBattleZoneAbility()
        {
            var state = EmptyState();
            var naga = Instance(10,
                TcgCatalog.ByName("Cruel Naga, Avatar of Fate").Single());
            var ally = Instance(20, Creature("ally"));
            var opponent = Instance(30, Creature("opponent"));
            naga.TimedRuleAbilities.Add(new TimedRuleEffect
            {
                Ability = new RuleAbilityDefinition("test.naga-destruction-to-mana",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.ManaZone
                },
                Duration = AbilityDuration.Permanent,
                AppliedByPlayerId = 0
            });
            state.Players[0].BattleZone.Add(naga);
            state.Players[0].BattleZone.Add(ally);
            state.Players[1].BattleZone.Add(opponent);

            Assert.That(new AbilityResolver().DestroyCard(state, naga), Is.False,
                "The destruction itself was replaced by a move to mana.");

            Assert.That(state.Players[0].Mana, Does.Contain(naga));
            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[1].BattleZone, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(ally));
            Assert.That(state.Players[1].Graveyard, Does.Contain(opponent));
        }

        [Test]
        public void SimultaneousBattleDestruction_EvaluatesMihailBeforeEitherCreatureLeaves()
        {
            var state = EmptyState();
            var mihail = Instance(10,
                TcgCatalog.ByName("Mihail, Celestial Elemental").Single());
            var opponentDefinition = Creature("equal-power-opponent");
            opponentDefinition.Power = 4000;
            var opponent = Instance(20, opponentDefinition);
            mihail.EnteredBattleZoneOnTurn = -1;
            opponent.Tapped = true;
            state.Players[0].BattleZone.Add(mihail);
            state.Players[1].BattleZone.Add(opponent);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, mihail.InstanceId,
                AttackTargetKind.Creature, opponent.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Graveyard, Does.Contain(mihail));
            Assert.That(state.Players[1].BattleZone, Does.Contain(opponent),
                "Mihail protected the other creature during the shared battle-destruction event.");
        }

        [Test]
        public void SimultaneousMassDestruction_SnapshotsAuraReplacementsBeforeAuraLeaves()
        {
            var state = EmptyState();
            var jackViper = Instance(10,
                TcgCatalog.ByName("Jack Viper, Shadow of Doom").Single());
            var darknessDefinition = Creature("darkness-ally", Civilization.Darkness);
            var darknessAlly = Instance(11, darknessDefinition);
            var spellDefinition = Spell("destroy-all-opponents");
            spellDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.mass-destroy", AbilityTiming.OnCast,
                    AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var spell = Instance(20, spellDefinition);
            state.Players[0].Resolving.Add(spell);
            state.Players[1].BattleZone.Add(jackViper);
            state.Players[1].BattleZone.Add(darknessAlly);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[1].Graveyard, Does.Contain(jackViper));
            Assert.That(state.Players[1].Hand, Does.Contain(darknessAlly),
                "Jack Viper's replacement existed when the shared destruction event began.");
        }

        [Test]
        public void Garatyano_ReordersInspectedTopCardsUsingDecisionOrder()
        {
            var state = EmptyState();
            var garatyano = Instance(10, TcgCatalog.ByName("Garatyano").Single());
            state.Players[0].BattleZone.Add(garatyano);
            state.Players[0].Deck.AddRange(new[]
            {
                Instance(90, Creature("untouched-bottom")),
                Instance(1, Creature("first")),
                Instance(2, Creature("second")),
                Instance(3, Creature("third"))
            });

            new AbilityResolver(new AscendingOrderDecisionProvider()).Resolve(
                state, garatyano, AbilityTiming.TapAbility);

            Assert.That(state.Players[0].Deck.Select(card => card.InstanceId),
                Is.EqualTo(new long[] { 90, 3, 2, 1 }));
        }

        [Test]
        public void SubmarineProject_PutsUnchosenCardsOnBottomInChosenOrder()
        {
            var state = EmptyState();
            var project = Instance(10,
                TcgCatalog.ByName("Submarine Project").Single());
            state.Players[0].Resolving.Add(project);
            state.Players[0].Deck.AddRange(new[]
            {
                Instance(90, Creature("untouched-card")),
                Instance(1, Creature("first")),
                Instance(2, Creature("second")),
                Instance(3, Creature("third")),
                Instance(4, Creature("fourth"))
            });

            new AbilityResolver(new AscendingOrderDecisionProvider()).Resolve(
                state, project, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand.Single().InstanceId, Is.EqualTo(1));
            Assert.That(state.Players[0].Deck.Select(card => card.InstanceId),
                Is.EqualTo(new long[] { 4, 3, 2, 90 }));
        }

        [Test]
        public void CostModifiers_CombineBeforeApplyingPrintedMinimum()
        {
            var state = EmptyState();
            var targetDefinition = Creature("two-cost-dragon");
            targetDefinition.Cost = 2;
            targetDefinition.Races = new[] { "Armored Dragon" };
            var target = Instance(10, targetDefinition);
            var reducerDefinition = Creature("reducer");
            reducerDefinition.RuleAbilities = new[]
            {
                CostModifier("test.reduce", -2, 2)
            };
            var taxDefinition = Creature("tax");
            taxDefinition.RuleAbilities = new[]
            {
                CostModifier("test.tax", 2, 0)
            };
            var reducer = Instance(20, reducerDefinition);
            var tax = Instance(21, taxDefinition);
            state.Players[0].Hand.Add(target);
            state.Players[0].BattleZone.Add(reducer);
            state.Players[0].BattleZone.Add(tax);
            var resolver = new AbilityResolver();

            Assert.That(resolver.CurrentCost(state, target), Is.EqualTo(2));

            state.Players[0].BattleZone.Clear();
            state.Players[0].BattleZone.Add(tax);
            state.Players[0].BattleZone.Add(reducer);
            Assert.That(resolver.CurrentCost(state, target), Is.EqualTo(2),
                "Continuous cost modifiers must not depend on battle-zone list order.");
        }

        [Test]
        public void DestructionReplacement_OwnedByActivePlayerTakesPriority()
        {
            var state = EmptyState();
            var targetDefinition = Creature("active-self-replacer");
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.self-to-hand",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand
                }
            };
            var preventionDefinition = Creature("nonactive-prevention");
            preventionDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.prevent-another",
                    AbilityTiming.Static, AbilityOperation.PreventDestruction)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Either,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true
                        }
                    }
                }
            };
            var target = Instance(10, targetDefinition);
            var prevention = Instance(20, preventionDefinition);
            state.ActivePlayerId = 0;
            state.Players[0].BattleZone.Add(target);
            state.Players[1].BattleZone.Add(prevention);

            Assert.That(new AbilityResolver().DestroyCard(state, target), Is.False);
            Assert.That(state.Players[0].Hand, Does.Contain(target));
            Assert.That(state.Players[0].BattleZone, Has.No.Member(target));
        }

        [Test]
        public void DestructionReplacement_ActivePlayersPreventionBeatsNonactiveSelfReplacement()
        {
            var state = EmptyState();
            var targetDefinition = Creature("nonactive-self-replacer");
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.self-to-hand",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand
                }
            };
            var preventionDefinition = Creature("active-prevention");
            preventionDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.prevent-another",
                    AbilityTiming.Static, AbilityOperation.PreventDestruction)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Either,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true
                        }
                    }
                }
            };
            var target = Instance(10, targetDefinition);
            var prevention = Instance(20, preventionDefinition);
            state.ActivePlayerId = 1;
            state.Players[0].BattleZone.Add(target);
            state.Players[1].BattleZone.Add(prevention);

            Assert.That(new AbilityResolver().DestroyCard(state, target), Is.False);
            Assert.That(state.Players[0].BattleZone, Does.Contain(target));
            Assert.That(state.Players[0].Hand, Has.No.Member(target));
        }

        [Test]
        public void SamePlayerCanChooseBetweenDestructionReplacements()
        {
            var state = EmptyState();
            var targetDefinition = Creature("replacement-choice-target");
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.self-to-mana",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.ManaZone
                }
            };
            var auraDefinition = Creature("replacement-choice-aura");
            auraDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.aura-to-hand",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true
                        }
                    }
                }
            };
            var target = Instance(10, targetDefinition);
            var aura = Instance(20, auraDefinition);
            state.Players[0].BattleZone.Add(target);
            state.Players[0].BattleZone.Add(aura);

            var resolver = new AbilityResolver(
                new PreferredReplacementDecisionProvider("test.aura-to-hand"));
            Assert.That(resolver.DestroyCard(state, target), Is.False);

            Assert.That(state.Players[0].Hand, Does.Contain(target));
            Assert.That(state.Players[0].Mana, Has.No.Member(target));
        }

        [Test]
        public void LeaveZoneStateEffect_DoesNotPreventDestroyedAbilities()
        {
            var state = EmptyState();
            var evolutionDefinition = Creature("leave-zone-replacer");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            evolutionDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.separate-evolution",
                    AbilityTiming.StateDefinedLeaveBattleZone,
                    AbilityOperation.SeparateEvolutionTop),
                new RuleAbilityDefinition("test.destroyed-draw",
                    AbilityTiming.OnDestroyed, AbilityOperation.Draw)
            };
            var evolution = Instance(10, evolutionDefinition);
            var firstBase = Instance(11, Creature("base-one"));
            var secondBase = Instance(12, Creature("base-two"));
            evolution.EvolutionMaterials.Add(firstBase);
            evolution.EvolutionMaterials.Add(secondBase);
            state.Players[0].BattleZone.Add(evolution);
            state.Players[0].Deck.Add(Instance(20, Creature("must-not-be-drawn")));

            Assert.That(new AbilityResolver().DestroyCard(state, evolution), Is.True);

            Assert.That(state.Players[0].Hand, Has.Count.EqualTo(1));
            Assert.That(state.Players[0].Deck, Is.Empty);
            Assert.That(state.Players[0].BattleZone,
                Is.EquivalentTo(new[] { firstBase, secondBase }));
            Assert.That(firstBase.EvolutionMaterials, Is.Empty);
            Assert.That(secondBase.EvolutionMaterials, Is.Empty);
        }

        [Test]
        public void DestructionPrevention_StopsLeaveZoneStateEffectFromApplying()
        {
            var state = EmptyState();
            var evolutionDefinition = Creature("protected-leave-state-creature");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            evolutionDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.separate-evolution",
                    AbilityTiming.StateDefinedLeaveBattleZone,
                    AbilityOperation.SeparateEvolutionTop),
                new RuleAbilityDefinition("test.prevent-own-destruction",
                    AbilityTiming.Static, AbilityOperation.PreventDestruction)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var evolution = Instance(10, evolutionDefinition);
            var firstBase = Instance(11, Creature("base-one"));
            var secondBase = Instance(12, Creature("base-two"));
            evolution.EvolutionMaterials.Add(firstBase);
            evolution.EvolutionMaterials.Add(secondBase);
            state.Players[0].BattleZone.Add(evolution);

            Assert.That(new AbilityResolver().DestroyCard(state, evolution), Is.False);

            Assert.That(state.Players[0].BattleZone,
                Is.EquivalentTo(new[] { evolution }));
            Assert.That(evolution.EvolutionMaterials,
                Is.EqualTo(new[] { firstBase, secondBase }));
            Assert.That(state.Players[0].Graveyard, Is.Empty);
        }

        [Test]
        public void DestructionReplacement_IsAppliedBeforeLeaveZoneStateEffect()
        {
            var state = EmptyState();
            var evolutionDefinition = Creature("replacement-chain-evolution");
            evolutionDefinition.Type = CardType.EvolutionCreature;
            evolutionDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.separate-evolution",
                    AbilityTiming.StateDefinedLeaveBattleZone,
                    AbilityOperation.SeparateEvolutionTop),
                new RuleAbilityDefinition("test.whole-pile-to-hand",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand
                }
            };
            var evolution = Instance(10, evolutionDefinition);
            var firstBase = Instance(11, Creature("base-one"));
            var secondBase = Instance(12, Creature("base-two"));
            evolution.EvolutionMaterials.Add(firstBase);
            evolution.EvolutionMaterials.Add(secondBase);
            state.Players[0].BattleZone.Add(evolution);

            var resolver = new AbilityResolver(
                new PreferredReplacementDecisionProvider("test.whole-pile-to-hand"));
            Assert.That(resolver.DestroyCard(state, evolution), Is.False);

            Assert.That(state.Players[0].BattleZone,
                Is.EquivalentTo(new[] { firstBase, secondBase }));
            Assert.That(state.Players[0].Hand,
                Is.EquivalentTo(new[] { evolution }));
            Assert.That(state.Players[0].Graveyard, Is.Empty);
        }

        [Test]
        public void UnselectedDestructionReplacement_DoesNotRunItsLinkedFollowup()
        {
            var state = EmptyState();
            var targetDefinition = Creature("compound-self-replacer");
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.compound-replacement",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.ManaZone
                },
                new RuleAbilityDefinition("test.compound-replacement",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand,
                    RequiresPreviousSuccess = true,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.ManaZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            RaceContains = "Dragon"
                        }
                    }
                }
            };
            var auraDefinition = Creature("global-replacer");
            auraDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.global-to-hand",
                    AbilityTiming.ReplacementDestroy, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true
                        }
                    }
                }
            };
            var dragonDefinition = Creature("mana-dragon");
            dragonDefinition.Races = new[] { "Armored Dragon" };
            var target = Instance(10, targetDefinition);
            var aura = Instance(20, auraDefinition);
            var dragon = Instance(30, dragonDefinition);
            state.Players[0].BattleZone.Add(target);
            state.Players[0].BattleZone.Add(aura);
            state.Players[0].Mana.Add(dragon);

            new AbilityResolver(new PreferredReplacementDecisionProvider(
                    "test.global-to-hand"))
                .DestroyCard(state, target);

            Assert.That(state.Players[0].Hand, Does.Contain(target));
            Assert.That(state.Players[0].Mana, Does.Contain(dragon),
                "Only the followup linked to the selected replacement may resolve.");
        }

        [Test]
        public void OneSaver_ReplacesOnlyOneDestructionInASimultaneousEvent()
        {
            var state = EmptyState();
            var saverDefinition = Creature("one-use-saver");
            saverDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.save-dragon",
                    AbilityTiming.ReplacementDestroy,
                    AbilityOperation.DestroySourceInstead)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            OtherThanSource = true,
                            Race = "Armored Dragon"
                        }
                    }
                }
            };
            var dragonDefinition = Creature("mass-destruction-dragon");
            dragonDefinition.Races = new[] { "Armored Dragon" };
            var saver = Instance(10, saverDefinition);
            var firstDragon = Instance(20, dragonDefinition);
            var secondDragon = Instance(30, dragonDefinition);
            state.Players[0].BattleZone.AddRange(new[]
                { saver, firstDragon, secondDragon });

            var destroyed = new AbilityResolver().DestroyCards(state,
                new[] { saver, firstDragon, secondDragon });

            Assert.That(destroyed, Is.EqualTo(2));
            Assert.That(state.Players[0].Graveyard, Does.Contain(saver));
            Assert.That(state.Players[0].BattleZone, Does.Contain(firstDragon));
            Assert.That(state.Players[0].Graveyard, Does.Contain(secondDragon));
        }

        [Test]
        public void ActiveAttackerShieldBurn_PrecedesDefendersShieldSaver()
        {
            var state = EmptyState();
            var attacker = Instance(10,
                TcgCatalog.ByName("Bolmeteus Steel Dragon").Single());
            var saver = Instance(20,
                TcgCatalog.ByName("Pulsar Tree").Single());
            var shield = Instance(30, Spell("shield"));
            attacker.EnteredBattleZoneOnTurn = -1;
            state.ActivePlayerId = 0;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(saver);
            state.Players[1].Shields.Add(shield);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Graveyard, Does.Contain(shield));
            Assert.That(state.Players[1].BattleZone, Does.Contain(saver));
        }

        [Test]
        public void IndependentCreatureTriggers_CanResolveInTheirControllersChosenOrder()
        {
            var state = EmptyState();
            var definition = Creature("two-trigger-creature");
            definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-trigger",
                    AbilityTiming.OnSummon, AbilityOperation.Draw) { Amount = 1 },
                new RuleAbilityDefinition("test.discard-trigger",
                    AbilityTiming.OnSummon, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var source = Instance(10, definition);
            var drawn = Instance(20, Creature("drawn-card"));
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Deck.Add(Instance(21, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);

            new AbilityResolver(new PreferredTriggerOrderDecisionProvider(
                    "test.discard-trigger"))
                .Resolve(state, source, AbilityTiming.OnSummon);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { drawn }));
        }

        [Test]
        public void SpellAbilities_StayInPrintedOrderEvenWithTriggerOrderProvider()
        {
            var state = EmptyState();
            var definition = Spell("ordered-spell");
            definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-first",
                    AbilityTiming.OnCast, AbilityOperation.Draw) { Amount = 1 },
                new RuleAbilityDefinition("test.discard-second",
                    AbilityTiming.OnCast, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var source = Instance(10, definition);
            state.Players[0].Resolving.Add(source);
            state.Players[0].Deck.Add(Instance(20, Creature("deck-filler")));
            state.Players[0].Deck.Add(Instance(21, Creature("drawn-card")));

            new AbilityResolver(new PreferredTriggerOrderDecisionProvider(
                    "test.discard-second"))
                .Resolve(state, source, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Is.Empty,
                "Spell instructions resolve from top to bottom.");
        }

        [Test]
        public void SimultaneousGlobalTriggers_ResolveActivePlayersSourcesFirst()
        {
            var state = EmptyState();
            state.ActivePlayerId = 1;
            var nonactiveDefinition = Creature("nonactive-watcher");
            nonactiveDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.conditional-draw",
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Filter = new CardFilterDefinition { CreaturesOnly = true },
                        CountAtLeast = 1
                    }
                }
            };
            var activeDefinition = Creature("active-watcher");
            activeDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.bounce-nonactive-watcher",
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand,
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
            var nonactiveWatcher = Instance(10, nonactiveDefinition);
            var activeWatcher = Instance(20, activeDefinition);
            var eventCard = Instance(30, Creature("summoned-event"));
            var draw = Instance(40, Creature("would-be-drawn"));
            state.Players[0].BattleZone.Add(nonactiveWatcher);
            state.Players[0].Deck.Add(Instance(41, Creature("deck-filler")));
            state.Players[0].Deck.Add(draw);
            state.Players[1].BattleZone.Add(activeWatcher);
            state.Players[1].BattleZone.Add(eventCard);

            new AbilityResolver().ResolveGlobalEvent(state, eventCard,
                AbilityTiming.OnOtherCreatureSummoned);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(
                new[] { nonactiveWatcher }));
            Assert.That(state.Players[0].Deck, Does.Contain(draw),
                "The nonactive trigger's condition is checked after active-player triggers.");
        }

        [Test]
        public void EndStep_ActivePlayersRegularTriggerPrecedesTheirDelayedTrigger()
        {
            var state = EmptyState();
            var regularDefinition = Creature("end-step-regular-source");
            regularDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.end-step-discard",
                    AbilityTiming.EndOfAnyTurn, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var regularSource = Instance(10, regularDefinition);
            var delayedSource = Instance(11, Spell("end-step-delayed-source"));
            var delayedDraw = Instance(20, Creature("delayed-draw"));
            state.Players[0].BattleZone.Add(regularSource);
            state.Players[0].Graveyard.Add(delayedSource);
            state.Players[0].Deck.Add(Instance(19, Creature("deck-filler")));
            state.Players[0].Deck.Add(delayedDraw);
            state.Players[1].Deck.Add(Instance(29, Creature("next-turn-filler")));
            state.Players[1].Deck.Add(Instance(30, Creature("next-turn-draw")));
            state.DelayedAbilities.Add(new DelayedAbilityEffect
            {
                Source = delayedSource,
                OwnerPlayerId = 0,
                Duration = AbilityDuration.UntilEndOfTurn,
                Ability = new RuleAbilityDefinition("test.delayed-end-step-draw",
                    AbilityTiming.EndOfOwnerTurn, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            });

            Assert.That(new RulesEngine().EndTurn(state, 0).Success, Is.True);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { delayedDraw }),
                "The active player's regular trigger may resolve before its delayed trigger.");
        }

        [Test]
        public void EndStep_CapturesAllTimingFamiliesBeforeAnyTriggerResolves()
        {
            var state = EmptyState();
            var removerDefinition = Creature("end-step-remover");
            removerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.end-any-remove-source",
                    AbilityTiming.EndOfAnyTurn, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "end-owner-drawer"
                        }
                    }
                }
            };
            var drawerDefinition = Creature("end-owner-drawer");
            drawerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.end-owner-draw",
                    AbilityTiming.EndOfOwnerTurn, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var remover = Instance(10, removerDefinition);
            var drawer = Instance(20, drawerDefinition);
            var drawn = Instance(30, Creature("drawn-after-end-source-leaves"));
            state.Players[0].BattleZone.AddRange(new[] { remover, drawer });
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);

            new AbilityResolver().ResolveEndStep(state, 0);

            Assert.That(state.Players[0].Graveyard, Does.Contain(drawer));
            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { drawn }),
                "The end-of-owner-turn trigger was already waiting when the end-of-any-turn trigger removed its source.");
        }

        [Test]
        public void EndStep_AllowsSameSourceToOrderDifferentTimingLabels()
        {
            var state = EmptyState();
            var definition = Creature("two-end-triggers");
            definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.end-any-discard",
                    AbilityTiming.EndOfAnyTurn, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                },
                new RuleAbilityDefinition("test.end-owner-draw-first",
                    AbilityTiming.EndOfOwnerTurn, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var source = Instance(10, definition);
            var drawn = Instance(20, Creature("ordered-end-draw"));
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Deck.Add(Instance(19, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);

            new AbilityResolver(new PreferredTriggerOrderDecisionProvider(
                    "test.end-owner-draw-first"))
                .ResolveEndStep(state, 0);

            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(drawn));
        }

        [Test]
        public void StartStep_CapturesNonactiveTriggerBeforeItsSourceLeaves()
        {
            var state = EmptyState();
            var activeDefinition = Creature("start-step-remover");
            activeDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.start-owner-remove",
                    AbilityTiming.StartOfOwnerTurn, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "opponent-start-drawer"
                        }
                    }
                }
            };
            var nonactiveDefinition = Creature("opponent-start-drawer");
            nonactiveDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.start-opponent-draw",
                    AbilityTiming.StartOfOpponentTurn,
                    AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var activeSource = Instance(10, activeDefinition);
            var nonactiveSource = Instance(20, nonactiveDefinition);
            var drawn = Instance(30, Creature("drawn-after-start-source-leaves"));
            state.Players[0].BattleZone.Add(activeSource);
            state.Players[1].BattleZone.Add(nonactiveSource);
            state.Players[1].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[1].Deck.Add(drawn);

            new AbilityResolver().ResolveStartStep(state, 0);

            Assert.That(state.Players[1].Graveyard,
                Does.Contain(nonactiveSource));
            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { drawn }));
        }

        [Test]
        public void TriggerCreatedDuringSpell_WaitsUntilTheSpellFinishes()
        {
            var state = EmptyState();
            var watcherDefinition = Creature("summon-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.watcher-draw",
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var spellDefinition = Spell("summon-then-discard");
            spellDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.spell-sequence",
                    AbilityTiming.OnCast, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "summoned-creature"
                        }
                    }
                },
                new RuleAbilityDefinition("test.spell-sequence",
                    AbilityTiming.OnCast, AbilityOperation.Discard)
                {
                    RequiresPreviousSuccess = true,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var spell = Instance(10, spellDefinition);
            var watcher = Instance(20, watcherDefinition);
            var summoned = Instance(30, Creature("summoned-creature"));
            var drawn = Instance(40, Creature("trigger-draw"));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(watcher);
            state.Players[0].Hand.Add(summoned);
            state.Players[0].Deck.Add(Instance(41, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { drawn }),
                "The spell's discard instruction must happen before the pending summon trigger draws.");
        }

        [Test]
        public void PendingTrigger_RemainsPendingWhenItsSourceLeavesBattleZone()
        {
            var state = EmptyState();
            var watcherDefinition = Creature("departing-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.departing-watcher-draw",
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var spellDefinition = Spell("summon-then-bounce-watcher");
            spellDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.departing-sequence",
                    AbilityTiming.OnCast, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "summoned-event"
                        }
                    }
                },
                new RuleAbilityDefinition("test.departing-sequence",
                    AbilityTiming.OnCast, AbilityOperation.Move)
                {
                    RequiresPreviousSuccess = true,
                    Destination = RuleZone.Hand,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "departing-watcher"
                        }
                    }
                }
            };
            var spell = Instance(10, spellDefinition);
            var watcher = Instance(20, watcherDefinition);
            var summoned = Instance(30, Creature("summoned-event"));
            var drawn = Instance(40, Creature("pending-trigger-draw"));
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.Add(watcher);
            state.Players[0].Hand.Add(summoned);
            state.Players[0].Deck.Add(Instance(41, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);

            new AbilityResolver().Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { watcher, drawn }));
        }

        [Test]
        public void TriggerSnapshot_KeepsSharedSurvivorAbilityAfterDonorLeaves()
        {
            var state = EmptyState();
            var donorDefinition = Creature("survivor-trigger-donor");
            donorDefinition.Abilities = new[]
                { new KeywordAbility(CardKeyword.Survivor) };
            donorDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.shared-survivor-draw",
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Draw)
                {
                    Amount = 1,
                    IsSurvivorAbility = true
                }
            };
            var recipientDefinition = Creature("survivor-trigger-recipient");
            recipientDefinition.Abilities = new[]
                { new KeywordAbility(CardKeyword.Survivor) };
            var removerDefinition = Creature("active-trigger-remover");
            removerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.remove-survivor-donor",
                    AbilityTiming.OnOtherCreatureSummoned, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "survivor-trigger-donor"
                        }
                    }
                }
            };
            var remover = Instance(10, removerDefinition);
            var donor = Instance(20, donorDefinition);
            var recipient = Instance(21, recipientDefinition);
            var eventCard = Instance(22, Creature("summoned-event"));
            state.ActivePlayerId = 0;
            state.Players[0].BattleZone.Add(remover);
            state.Players[1].BattleZone.Add(donor);
            state.Players[1].BattleZone.Add(recipient);
            state.Players[1].BattleZone.Add(eventCard);
            state.Players[1].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[1].Deck.Add(Instance(30, Creature("first-draw")));
            state.Players[1].Deck.Add(Instance(31, Creature("second-draw")));

            new AbilityResolver().ResolveGlobalEvent(state, eventCard,
                AbilityTiming.OnOtherCreatureSummoned);

            Assert.That(state.Players[1].Graveyard, Does.Contain(donor));
            Assert.That(state.Players[1].Hand, Has.Count.EqualTo(2),
                "Both Survivor triggers existed when the event occurred.");
        }

        [Test]
        public void SimultaneousDestruction_MovesEveryCreatureBeforeDeathTriggersResolve()
        {
            var state = EmptyState();
            var firstDefinition = Creature("first-destroyed");
            firstDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-if-an-ally-remains",
                    AbilityTiming.OnDestroyed, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        CountAtLeast = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var first = Instance(10, firstDefinition);
            var second = Instance(20, Creature("second-destroyed"));
            var wouldBeDrawn = Instance(30, Creature("would-be-drawn"));
            state.Players[0].BattleZone.AddRange(new[] { first, second });
            state.Players[0].Deck.Add(Instance(31, Creature("deck-filler")));
            state.Players[0].Deck.Add(wouldBeDrawn);

            new AbilityResolver().DestroyCards(state, new[] { first, second });

            Assert.That(state.Players[0].BattleZone, Is.Empty);
            Assert.That(state.Players[0].Hand, Is.Empty,
                "Both cards leave in the same event before either death trigger resolves.");
            Assert.That(state.Players[0].Deck, Does.Contain(wouldBeDrawn));
        }

        [Test]
        public void SimultaneousDestruction_ResolvesAllActiveTriggersBeforeNonactiveTriggers()
        {
            var state = EmptyState();
            var activeWatcherDefinition = Creature("active-destruction-watcher");
            activeWatcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-while-hand-empty",
                    AbilityTiming.OnOtherCreatureDestroyed, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        HasCountAtMost = true,
                        CountAtMost = 0,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var nonactiveWatcherDefinition = Creature("nonactive-destruction-watcher");
            nonactiveWatcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.discard-active-hand",
                    AbilityTiming.OnOtherCreatureDestroyed, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var activeWatcher = Instance(10, activeWatcherDefinition);
            var nonactiveWatcher = Instance(20, nonactiveWatcherDefinition);
            var firstVictim = Instance(30, Creature("first-victim"));
            var secondVictim = Instance(31, Creature("second-victim"));
            var firstDraw = Instance(40, Creature("first-trigger-draw"));
            var secondDraw = Instance(41, Creature("second-trigger-draw"));
            state.ActivePlayerId = 0;
            state.Players[0].BattleZone.Add(activeWatcher);
            state.Players[0].BattleZone.Add(firstVictim);
            state.Players[0].BattleZone.Add(secondVictim);
            state.Players[0].Deck.Add(Instance(39, Creature("deck-filler")));
            state.Players[0].Deck.Add(secondDraw);
            state.Players[0].Deck.Add(firstDraw);
            state.Players[1].BattleZone.Add(nonactiveWatcher);

            new AbilityResolver().DestroyCards(state,
                new[] { firstVictim, secondVictim });

            Assert.That(state.Players[0].Graveyard, Does.Contain(firstDraw));
            Assert.That(state.Players[0].Deck, Does.Contain(secondDraw),
                "Both active-player triggers resolve before either opposing discard trigger.");
        }

        [Test]
        public void SimultaneouslyDestroyedWatchers_TriggerForEachOther()
        {
            var state = EmptyState();
            var watcherDefinition = Creature("destruction-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-for-other-destruction",
                    AbilityTiming.OnOtherCreatureDestroyed, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var first = Instance(10, watcherDefinition);
            var second = Instance(20, watcherDefinition);
            state.Players[0].BattleZone.AddRange(new[] { first, second });
            state.Players[0].Deck.Add(Instance(30, Creature("deck-bottom")));
            state.Players[0].Deck.Add(Instance(31, Creature("first-draw")));
            state.Players[0].Deck.Add(Instance(32, Creature("second-draw")));

            new AbilityResolver().DestroyCards(state, new[] { first, second });

            Assert.That(state.Players[0].Hand, Has.Count.EqualTo(2));
            Assert.That(state.Players[0].Graveyard,
                Is.EquivalentTo(new[] { first, second }));
        }

        [Test]
        public void ChooseCivilizationGroup_DoesNotInferTheChoiceFromAMulticoloredCard()
        {
            var state = EmptyState();
            var spellDefinition = Spell("choose-civilization-group");
            spellDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.choose-civilization-group",
                    AbilityTiming.OnCast, AbilityOperation.TapOneCivilizationGroup)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Either,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Civilizations = new[]
                            {
                                Civilization.Darkness,
                                Civilization.Fire
                            }
                        }
                    }
                }
            };
            var darknessDefinition = Creature("darkness-only");
            darknessDefinition.Civilizations = new[] { Civilization.Darkness };
            var multicoloredDefinition = Creature("darkness-fire");
            multicoloredDefinition.Civilizations = new[]
                { Civilization.Darkness, Civilization.Fire };
            var spell = Instance(10, spellDefinition);
            var darkness = Instance(20, darknessDefinition);
            var multicolored = Instance(30, multicoloredDefinition);
            state.Players[0].Resolving.Add(spell);
            state.Players[0].BattleZone.AddRange(new[] { darkness, multicolored });

            new AbilityResolver(new PreferredCivilizationDecisionProvider(
                    Civilization.Fire))
                .Resolve(state, spell, AbilityTiming.OnCast);

            Assert.That(darkness.Tapped, Is.False);
            Assert.That(multicolored.Tapped, Is.True);
        }

        [Test]
        public void SilentSkill_IsOneActivationChoiceForTheWholePrintedAbility()
        {
            var state = EmptyState();
            var definition = Creature("compound-silent-skill");
            definition.Abilities = new[]
                { new KeywordAbility(CardKeyword.SilentSkill) };
            definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.compound-silent-skill",
                    AbilityTiming.SilentSkill, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Optional = true
                },
                new RuleAbilityDefinition("test.compound-silent-skill",
                    AbilityTiming.SilentSkill, AbilityOperation.Discard)
                {
                    Optional = true,
                    RequiresPreviousSuccess = true,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var source = Instance(10, definition);
            source.Tapped = true;
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Deck.Add(Instance(20, Creature("deck-filler")));
            state.Players[0].Deck.Add(Instance(21, Creature("draw-then-discard")));
            var decisions = new OnlyFirstOptionalDecisionProvider();

            var activated = new AbilityResolver(decisions)
                .ResolveOptionalTiming(state, source, AbilityTiming.SilentSkill);

            Assert.That(activated, Is.True);
            Assert.That(decisions.OptionalCallCount, Is.EqualTo(1));
            Assert.That(state.Players[0].Hand, Is.Empty,
                "After activation, every instruction in that printed silent-skill ability resolves.");
        }

        [Test]
        public void OpponentControlledOptionalEffect_AsksTheOpponent()
        {
            var state = EmptyState();
            var definition = Creature("opponent-optional-source");
            definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.opponent-optional",
                    AbilityTiming.StartOfOpponentTurn, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Optional = true,
                    Chooser = RuleController.Opponent
                }
            };
            var source = Instance(10, definition);
            var draw = Instance(20, Creature("opponent-approved-draw"));
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Deck.Add(Instance(19, Creature("deck-filler")));
            state.Players[0].Deck.Add(draw);

            var decisions = new OpponentOnlyOptionalDecisionProvider();
            new AbilityResolver(decisions).Resolve(state, source,
                AbilityTiming.StartOfOpponentTurn);

            Assert.That(decisions.LastOptionalPlayerId, Is.EqualTo(1));
            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { draw }));
        }

        [Test]
        public void EachPlayerManaMove_IsCausedByEffectController()
        {
            var state = EmptyState();
            var sourceDefinition = Creature("each-player-mana-source");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.each-player-mana-to-grave",
                    AbilityTiming.OnSummon, AbilityOperation.Move)
                {
                    Destination = RuleZone.Graveyard,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.EachPlayer,
                        Zone = RuleZone.ManaZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var watcherDefinition = Creature("opponent-mana-burn-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.return-opponent-burned-mana",
                    AbilityTiming.OnOwnManaMovedToGraveyardByOpponent,
                    AbilityOperation.Move)
                {
                    Destination = RuleZone.ManaZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.EventCard,
                        Minimum = 0,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var source = Instance(10, sourceDefinition);
            var watcher = Instance(20, watcherDefinition);
            var ownMana = Instance(30, Creature("active-mana"));
            var opposingMana = Instance(40, Creature("nonactive-mana"));
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Mana.Add(ownMana);
            state.Players[1].BattleZone.Add(watcher);
            state.Players[1].Mana.Add(opposingMana);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnSummon);

            Assert.That(state.Players[1].Mana, Does.Contain(opposingMana));
            Assert.That(state.Players[1].Graveyard, Has.No.Member(opposingMana),
                "The active player's effect caused the move even though the nonactive player chose their card.");
        }

        [Test]
        public void DiscardReplacement_AppliesDuringOpponentTurnEvenToOwnDiscard()
        {
            var state = EmptyState();
            var sourceDefinition = Creature("each-player-discard-source");
            sourceDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.each-player-random-discard",
                    AbilityTiming.OnSummon, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.EachPlayer,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Random,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var replacementDefinition = Creature("opponent-turn-discard-replacer");
            replacementDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.discard-to-battle",
                    AbilityTiming.ReplacementDiscard, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Condition = new AbilityConditionDefinition
                    {
                        DuringOpponentsTurn = true
                    }
                }
            };
            var source = Instance(10, sourceDefinition);
            var activeHand = Instance(20, Creature("active-hand-card"));
            var replacement = Instance(30, replacementDefinition);
            state.Players[0].BattleZone.Add(source);
            state.Players[0].Hand.Add(activeHand);
            state.Players[1].Hand.Add(replacement);

            new AbilityResolver().Resolve(state, source, AbilityTiming.OnSummon);

            Assert.That(state.Players[1].BattleZone, Does.Contain(replacement));
            Assert.That(state.Players[1].Graveyard, Has.No.Member(replacement));
        }

        [Test]
        public void OneTapCost_UsesOnlyOneAvailableTapAbility()
        {
            var state = EmptyState();
            var targetDefinition = Creature("tap-ability-user");
            targetDefinition.Civilizations = new[] { Civilization.Fire };
            var drawSourceDefinition = SharedTapAbilitySource(
                "draw-source", new RuleAbilityDefinition("test.shared-draw",
                    AbilityTiming.TapAbility, AbilityOperation.Draw) { Amount = 1 });
            var discardSourceDefinition = SharedTapAbilitySource(
                "discard-source", new RuleAbilityDefinition("test.shared-discard",
                    AbilityTiming.TapAbility, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Random,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition()
                    }
                });
            var target = Instance(20, targetDefinition);
            var drawSource = Instance(10, drawSourceDefinition);
            var discardSource = Instance(30, discardSourceDefinition);
            var wouldBeDrawn = Instance(40, Creature("would-be-drawn"));
            var discarded = Instance(50, Creature("opponent-hand-card"));
            state.Players[0].BattleZone.AddRange(new[]
                { target, drawSource, discardSource });
            state.Players[0].Deck.Add(Instance(41, Creature("deck-filler")));
            state.Players[0].Deck.Add(wouldBeDrawn);
            state.Players[1].Hand.Add(discarded);

            var result = new RulesEngine(new HighestInstanceDecisionProvider())
                .UseTapAbility(state, 0, target.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(target.Tapped, Is.True);
            Assert.That(state.Players[1].Graveyard, Does.Contain(discarded));
            Assert.That(state.Players[0].Deck, Does.Contain(wouldBeDrawn),
                "Tapping once pays for the chosen ability, not every shared tap ability.");
        }

        [Test]
        public void AttackDeclaration_ResolvesActivePlayersTriggerBeforeDefendersTrigger()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("attack-discard");
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.attack-discard-hand",
                    AbilityTiming.OnAttack, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var defenderDefinition = Creature("attacked-draw");
            defenderDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.attacked-draw",
                    AbilityTiming.OnAttacked, AbilityOperation.Draw) { Amount = 1 }
            };
            var attacker = Instance(10, attackerDefinition);
            var defender = Instance(20, defenderDefinition);
            var existingHand = Instance(30, Creature("existing-hand"));
            var drawn = Instance(40, Creature("drawn-after-discard"));
            attacker.EnteredBattleZoneOnTurn = -1;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(defender);
            state.Players[1].Hand.Add(existingHand);
            state.Players[1].Deck.Add(Instance(41, Creature("deck-filler")));
            state.Players[1].Deck.Add(drawn);

            var result = new RulesEngine().DeclareAttack(state, 0,
                attacker.InstanceId, AttackTargetKind.Creature,
                defender.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].Graveyard, Does.Contain(existingHand));
            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { drawn }));
        }

        [Test]
        public void AttackDeclaration_EndsWhenAttackerLeavesAndReenters()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("reentering-attacker");
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.attack-destroy-self",
                    AbilityTiming.OnAttack, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                },
                new RuleAbilityDefinition("test.attack-self-reanimate",
                    AbilityTiming.OnDestroyed, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                },
                new RuleAbilityDefinition("test.old-attacker-after-attack",
                    AbilityTiming.AfterAttack, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var attacker = Instance(10, attackerDefinition);
            attacker.EnteredBattleZoneOnTurn = -1;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].Shields.Add(Instance(20, Creature("unbroken-shield")));

            var result = new RulesEngine().DeclareAttack(state, 0,
                attacker.InstanceId, AttackTargetKind.Player);

            Assert.That(result.Success, Is.True);
            Assert.That(state.PendingAttack, Is.Null,
                "Leaving the battle zone ends the original attack immediately.");
            Assert.That(state.Players[0].BattleZone, Does.Contain(attacker));
            Assert.That(state.Players[0].Graveyard, Has.No.Member(attacker));
            Assert.That(state.Players[1].Shields, Has.Count.EqualTo(1));
            Assert.That(attacker.EnteredBattleZoneOnTurn, Is.EqualTo(state.TurnNumber));
            Assert.That(attacker.Tapped, Is.False,
                "The re-entered card is a new creature, not the tapped attacker.");
        }

        [Test]
        public void AttackDeclaration_EndsWhenCreatureTargetLeavesAndReenters()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("plain-attacker"));
            attacker.EnteredBattleZoneOnTurn = -1;
            var targetDefinition = Creature("reentering-target");
            targetDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.attacked-destroy-self",
                    AbilityTiming.OnAttacked, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                },
                new RuleAbilityDefinition("test.target-self-reanimate",
                    AbilityTiming.OnDestroyed, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var target = Instance(20, targetDefinition);
            target.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(target);

            var result = new RulesEngine().DeclareAttack(state, 0,
                attacker.InstanceId, AttackTargetKind.Creature, target.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.PendingAttack, Is.Null);
            Assert.That(state.Players[1].BattleZone, Does.Contain(target));
            Assert.That(state.Players[1].Graveyard, Has.No.Member(target));
            Assert.That(target.Tapped, Is.False);
        }

        [Test]
        public void Slayer_DoesNotDestroyCreatureThatReenteredAfterBattleLoss()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("winning-slayer");
            attackerDefinition.Power = 5000;
            attackerDefinition.Abilities = new[]
                { new KeywordAbility(CardKeyword.Slayer) };
            var defenderDefinition = Creature("battle-self-reanimator");
            defenderDefinition.Power = 1000;
            defenderDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.battle-self-reanimate",
                    AbilityTiming.OnDestroyed, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var attacker = Instance(10, attackerDefinition);
            var defender = Instance(20, defenderDefinition);
            attacker.EnteredBattleZoneOnTurn = -1;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(defender);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].BattleZone, Does.Contain(defender));
            Assert.That(state.Players[1].Graveyard, Has.No.Member(defender),
                "Slayer targets the old creature that battled, not its new incarnation.");
        }

        [Test]
        public void ScheduledAfterBattleUntap_DoesNotAffectReenteredBlocker()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("strong-attacker");
            attackerDefinition.Power = 5000;
            var blockerDefinition = Creature("reentering-blocker");
            blockerDefinition.Power = 1000;
            blockerDefinition.Abilities = new[]
                { new KeywordAbility(CardKeyword.Blocker) };
            blockerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.schedule-old-blocker-untap",
                    AbilityTiming.OnBlock,
                    AbilityOperation.ScheduleSelectedUntapAfterBattle)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                },
                new RuleAbilityDefinition("test.blocker-self-reanimate",
                    AbilityTiming.OnDestroyed, AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                },
                new RuleAbilityDefinition("test.reentered-blocker-is-tapped",
                    AbilityTiming.Static, AbilityOperation.ForceEnterTapped)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.SourceCard,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition { CreaturesOnly = true }
                    }
                }
            };
            var attacker = Instance(10, attackerDefinition);
            var blocker = Instance(20, blockerDefinition);
            attacker.EnteredBattleZoneOnTurn = -1;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(blocker);
            state.Players[1].Shields.Add(Instance(30, Creature("shield")));
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, blocker.InstanceId).Success, Is.True);

            Assert.That(state.Players[1].BattleZone, Does.Contain(blocker));
            Assert.That(blocker.Tapped, Is.True,
                "The scheduled untap belongs to the blocker that left, not the new creature.");
        }

        [Test]
        public void AttackDeclaration_BatchesWatcherAndAttackedTriggersByTurnPlayer()
        {
            var state = EmptyState();
            var attacker = Instance(10, Creature("plain-attacker"));
            attacker.EnteredBattleZoneOnTurn = -1;
            var watcherDefinition = Creature("active-attacked-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.active-watcher-draw",
                    AbilityTiming.OnAnyCreatureAttacked, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var attackedDefinition = Creature("nonactive-attacked-source");
            attackedDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.nonactive-attacked-discard",
                    AbilityTiming.OnAttacked, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var watcher = Instance(11, watcherDefinition);
            var attacked = Instance(20, attackedDefinition);
            var drawn = Instance(30, Creature("drawn-before-defender-trigger"));
            attacked.Tapped = true;
            state.Players[0].BattleZone.AddRange(new[] { attacker, watcher });
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].BattleZone.Add(attacked);

            var result = new RulesEngine().DeclareAttack(state, 0,
                attacker.InstanceId, AttackTargetKind.Creature,
                attacked.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(drawn),
                "All active-player attack-declaration triggers resolve before nonactive triggers, regardless of trigger family.");
        }

        [Test]
        public void UnblockedAttack_AllowsActivePlayerToOrderAttackerAndWatcherTriggers()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("unblocked-attacker");
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.unblocked-discard-own-hand",
                    AbilityTiming.OnAttackPlayerUnblocked,
                    AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var watcherDefinition = Creature("unblocked-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.unblocked-watcher-draw",
                    AbilityTiming.OnAnyCreatureAttackPlayerUnblocked,
                    AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var attacker = Instance(10, attackerDefinition);
            var watcher = Instance(5, watcherDefinition);
            var drawn = Instance(30, Creature("drawn-before-unblocked-discard"));
            attacker.EnteredBattleZoneOnTurn = -1;
            state.Players[0].BattleZone.AddRange(new[] { attacker, watcher });
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].Shields.Add(Instance(40, Creature("shield")));
            var engine = new RulesEngine(new AscendingOrderDecisionProvider());

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(drawn),
                "The active player chose the watcher before the attacker's separate trigger.");
        }

        [Test]
        public void FinishedAttack_AllowsOrderingSourceAndDelayedTriggersTogether()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("after-attack-source");
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.after-attack-discard",
                    AbilityTiming.AfterAttack, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var delayedSource = Instance(5, Spell("delayed-attack-draw"));
            var attacker = Instance(10, attackerDefinition);
            var drawn = Instance(30, Creature("drawn-before-after-attack-discard"));
            attacker.EnteredBattleZoneOnTurn = -1;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[0].Graveyard.Add(delayedSource);
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].Shields.Add(Instance(40, Creature("shield")));
            state.DelayedAbilities.Add(new DelayedAbilityEffect
            {
                Source = delayedSource,
                OwnerPlayerId = 0,
                Duration = AbilityDuration.UntilEndOfTurn,
                Ability = new RuleAbilityDefinition("test.delayed-attack-draw",
                    AbilityTiming.AfterAnyCreatureAttack, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            });
            var engine = new RulesEngine(new AscendingOrderDecisionProvider());

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(drawn),
                "The delayed trigger and the creature's own finish trigger share one ordering window.");
        }

        [Test]
        public void BlockDeclaration_BatchesWatcherAndBlockerTriggersByTurnPlayer()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("blockable-attacker");
            attackerDefinition.Power = 3000;
            var attacker = Instance(10, attackerDefinition);
            attacker.EnteredBattleZoneOnTurn = -1;
            var watcherDefinition = Creature("active-block-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.active-block-watcher-draw",
                    AbilityTiming.OnAnyCreatureBlock, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var blockerDefinition = Creature("nonactive-blocker");
            blockerDefinition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.Blocker)
            };
            blockerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.nonactive-block-discard",
                    AbilityTiming.OnBlock, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var watcher = Instance(11, watcherDefinition);
            var blocker = Instance(20, blockerDefinition);
            var drawn = Instance(30, Creature("drawn-before-blocker-trigger"));
            state.Players[0].BattleZone.AddRange(new[] { attacker, watcher });
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].BattleZone.Add(blocker);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Player).Success, Is.True);
            Assert.That(engine.Block(state, 1, blocker.InstanceId).Success, Is.True);

            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(drawn),
                "All active-player block-declaration triggers resolve before nonactive triggers, regardless of trigger family.");
        }

        [Test]
        public void CreaturePlay_BatchesSummonTriggerFamiliesByTurnPlayer()
        {
            var state = EmptyState();
            var played = Instance(10, Creature("played-creature"));
            var activeWatcherDefinition = Creature("active-summon-watcher");
            activeWatcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.active-summon-watcher-draw",
                    AbilityTiming.OnAnyCreatureSummonedByPlay,
                    AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var nonactiveWatcherDefinition = Creature("nonactive-summon-watcher");
            nonactiveWatcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.nonactive-summon-watcher-discard",
                    AbilityTiming.OnOtherCreatureSummoned,
                    AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var activeWatcher = Instance(11, activeWatcherDefinition);
            var nonactiveWatcher = Instance(20, nonactiveWatcherDefinition);
            var mana = Instance(21, Creature("mana"));
            var drawn = Instance(30, Creature("drawn-before-summon-response"));
            state.Players[0].Hand.Add(played);
            state.Players[0].Mana.Add(mana);
            state.Players[0].BattleZone.Add(activeWatcher);
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].BattleZone.Add(nonactiveWatcher);

            var result = new RulesEngine().SummonCreature(state, 0,
                played.InstanceId, new[] { mana.InstanceId });

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(drawn),
                "All active-player creature-entry triggers resolve before nonactive triggers, regardless of trigger family.");
        }

        [Test]
        public void CreatureEntry_AppliesZeroPowerDestructionBeforeSummonTrigger()
        {
            var state = EmptyState();
            var enteringDefinition = Creature("zero-power-entering-creature");
            enteringDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-after-entry-destruction",
                    AbilityTiming.OnSummon, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        CountAtLeast = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "zero-power-entering-creature"
                        }
                    }
                }
            };
            var auraDefinition = Creature("opponent-power-reduction-aura");
            auraDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.static-minus-one-thousand",
                    AbilityTiming.Static, AbilityOperation.ModifyPower)
                {
                    Value = -1000,
                    Duration = AbilityDuration.WhileSourceInBattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true
                        }
                    }
                }
            };
            var entering = Instance(10, enteringDefinition);
            var aura = Instance(20, auraDefinition);
            var drawn = Instance(30, Creature("drawn-after-zero-power-entry"));
            state.Players[0].BattleZone.Add(entering);
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].BattleZone.Add(aura);

            new AbilityResolver().ResolveCreatureEntry(state, entering,
                true, true);

            Assert.That(state.Players[0].Graveyard, Does.Contain(entering));
            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { drawn }),
                "The entry trigger remains pending after state-based destruction of its source.");
        }

        [Test]
        public void SpellCast_CapturesWatcherBeforeTheSpellRemovesIt()
        {
            var state = EmptyState();
            var spellDefinition = Spell("remove-spell-watcher");
            spellDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.remove-spell-watcher",
                    AbilityTiming.OnCast, AbilityOperation.Destroy)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "spell-watcher"
                        }
                    }
                }
            };
            var watcherDefinition = Creature("spell-watcher");
            watcherDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.removed-watcher-draw",
                    AbilityTiming.OnSpellCast, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var spell = Instance(10, spellDefinition);
            var manaDefinition = Creature("water-mana");
            manaDefinition.Civilizations = new[] { Civilization.Water };
            var mana = Instance(11, manaDefinition);
            var watcher = Instance(20, watcherDefinition);
            var drawn = Instance(30, Creature("drawn-by-removed-watcher"));
            state.Players[0].Hand.Add(spell);
            state.Players[0].Mana.Add(mana);
            state.Players[1].BattleZone.Add(watcher);
            state.Players[1].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[1].Deck.Add(drawn);

            var result = new RulesEngine().CastSpell(state, 0,
                spell.InstanceId, new[] { mana.InstanceId });

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[1].Graveyard, Does.Contain(watcher));
            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { drawn }),
                "A cast trigger is already waiting even if the spell removes its source.");
        }

        [Test]
        public void ShieldTriggerCreature_BatchesPlayAndShieldUseTriggersByTurnPlayer()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("shield-breaking-attacker");
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.attacker-shield-use-draw",
                    AbilityTiming.OnOpponentShieldTriggerUsed,
                    AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var triggerDefinition = Creature("discarding-shield-trigger");
            triggerDefinition.Abilities = new[]
            {
                new KeywordAbility(CardKeyword.ShieldTrigger)
            };
            triggerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.shield-creature-discard",
                    AbilityTiming.OnSummon, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var attacker = Instance(10, attackerDefinition);
            var trigger = Instance(20, triggerDefinition);
            var drawn = Instance(30, Creature("drawn-before-trigger-discard"));
            attacker.Tapped = true;
            trigger.BrokenByAttackerInstanceId = attacker.InstanceId;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(drawn);
            state.Players[1].Hand.Add(trigger);
            state.PendingShieldTriggers.Add(trigger);

            var result = new RulesEngine().CastShieldTrigger(state, 1,
                trigger.InstanceId);

            Assert.That(result.Success, Is.True);
            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Graveyard, Does.Contain(drawn),
                "The active player's shield-use trigger resolves before the nonactive creature's play trigger.");
        }

        [Test]
        public void BattleResult_ResolvesActivePlayersLossTriggerBeforeDefendersWinTrigger()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("losing-attacker");
            attackerDefinition.Power = 1000;
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.loss-discard-hand",
                    AbilityTiming.OnBattleLost, AbilityOperation.Discard)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition()
                    }
                }
            };
            var defenderDefinition = Creature("winning-defender");
            defenderDefinition.Power = 2000;
            defenderDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.win-draw",
                    AbilityTiming.OnBattleWon, AbilityOperation.Draw) { Amount = 1 }
            };
            var attacker = Instance(10, attackerDefinition);
            var defender = Instance(20, defenderDefinition);
            var existingHand = Instance(30, Creature("existing-hand"));
            var drawn = Instance(40, Creature("drawn-after-loss-trigger"));
            attacker.EnteredBattleZoneOnTurn = -1;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[1].BattleZone.Add(defender);
            state.Players[1].Hand.Add(existingHand);
            state.Players[1].Deck.Add(Instance(41, Creature("deck-filler")));
            state.Players[1].Deck.Add(drawn);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Graveyard, Does.Contain(existingHand));
            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { drawn }));
        }

        [Test]
        public void BattleLossTrigger_ResolvesAfterTheLoserLeavesBattleZone()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("graveyard-aware-loser");
            attackerDefinition.Power = 1000;
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.draw-after-battle-loss",
                    AbilityTiming.OnBattleLost, AbilityOperation.Draw)
                {
                    Amount = 1,
                    Condition = new AbilityConditionDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Graveyard,
                        CountAtLeast = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "graveyard-aware-loser"
                        }
                    }
                }
            };
            var defenderDefinition = Creature("stronger-defender");
            defenderDefinition.Power = 2000;
            var attacker = Instance(10, attackerDefinition);
            var defender = Instance(20, defenderDefinition);
            var draw = Instance(30, Creature("post-loss-draw"));
            attacker.EnteredBattleZoneOnTurn = -1;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(draw);
            state.Players[1].BattleZone.Add(defender);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Graveyard, Does.Contain(attacker));
            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { draw }));
        }

        [Test]
        public void TiedBattle_TriggersBothCreaturesBattleLossAbilities()
        {
            var state = EmptyState();
            CardDefinition LosingDrawer(string name)
            {
                var definition = Creature(name);
                definition.Power = 1000;
                definition.RuleAbilities = new[]
                {
                    new RuleAbilityDefinition("test.tie-loss-draw." + name,
                        AbilityTiming.OnBattleLost, AbilityOperation.Draw)
                    {
                        Amount = 1
                    }
                };
                return definition;
            }
            var attacker = Instance(10, LosingDrawer("tied-attacker"));
            var defender = Instance(20, LosingDrawer("tied-defender"));
            var attackerDraw = Instance(30, Creature("attacker-draw"));
            var defenderDraw = Instance(40, Creature("defender-draw"));
            attacker.EnteredBattleZoneOnTurn = -1;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[0].Deck.Add(Instance(29, Creature("attacker-filler")));
            state.Players[0].Deck.Add(attackerDraw);
            state.Players[1].BattleZone.Add(defender);
            state.Players[1].Deck.Add(Instance(39, Creature("defender-filler")));
            state.Players[1].Deck.Add(defenderDraw);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[0].Hand, Is.EquivalentTo(new[] { attackerDraw }));
            Assert.That(state.Players[1].Hand, Is.EquivalentTo(new[] { defenderDraw }));
            Assert.That(state.Players[0].Graveyard, Does.Contain(attacker));
            Assert.That(state.Players[1].Graveyard, Does.Contain(defender));
        }

        [Test]
        public void CreatureRemovedByBattleStartAbility_DoesNotCreateAWinResult()
        {
            var state = EmptyState();
            var attackerDefinition = Creature("battle-start-remover");
            attackerDefinition.Power = 2000;
            attackerDefinition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.remove-before-comparison",
                    AbilityTiming.OnBattle, AbilityOperation.Move)
                {
                    Destination = RuleZone.Hand,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Opponent,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = "battle-start-target"
                        }
                    }
                },
                new RuleAbilityDefinition("test.must-not-win",
                    AbilityTiming.OnBattleWon, AbilityOperation.Draw)
                {
                    Amount = 1
                }
            };
            var defenderDefinition = Creature("battle-start-target");
            defenderDefinition.Power = 1000;
            var attacker = Instance(10, attackerDefinition);
            var defender = Instance(20, defenderDefinition);
            var wouldBeDrawn = Instance(30, Creature("must-not-be-drawn"));
            attacker.EnteredBattleZoneOnTurn = -1;
            defender.Tapped = true;
            state.Players[0].BattleZone.Add(attacker);
            state.Players[0].Deck.Add(Instance(29, Creature("deck-filler")));
            state.Players[0].Deck.Add(wouldBeDrawn);
            state.Players[1].BattleZone.Add(defender);
            var engine = new RulesEngine();

            Assert.That(engine.DeclareAttack(state, 0, attacker.InstanceId,
                AttackTargetKind.Creature, defender.InstanceId).Success, Is.True);
            Assert.That(engine.DeclineBlockAndResolve(state, 1).Success, Is.True);

            Assert.That(state.Players[1].Hand, Does.Contain(defender));
            Assert.That(state.Players[0].Hand, Is.Empty);
            Assert.That(state.Players[0].Deck, Does.Contain(wouldBeDrawn));
        }

        private static CardDefinition PutNamedHandCreatureIntoBattle(string id, string name)
        {
            var spell = Spell(id);
            spell.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.put-evolution", AbilityTiming.OnCast,
                    AbilityOperation.Move)
                {
                    Destination = RuleZone.BattleZone,
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.Hand,
                        Mode = RuleSelectionMode.Choose,
                        Minimum = 1,
                        Maximum = 1,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Name = name
                        }
                    }
                }
            };
            return spell;
        }

        private static CardDefinition SharedTapAbilitySource(string id,
            RuleAbilityDefinition tapAbility)
        {
            var definition = Creature(id);
            definition.Abilities = new[] { new KeywordAbility(CardKeyword.TapAbility) };
            definition.RuleAbilities = new[]
            {
                new RuleAbilityDefinition("test.share-tap." + id,
                    AbilityTiming.Static, AbilityOperation.ShareTapAbility)
                {
                    Selector = new RuleSelectorDefinition
                    {
                        Controller = RuleController.Source,
                        Zone = RuleZone.BattleZone,
                        Mode = RuleSelectionMode.All,
                        Minimum = 0,
                        Maximum = int.MaxValue,
                        Filter = new CardFilterDefinition
                        {
                            CreaturesOnly = true,
                            Civilizations = new[] { Civilization.Fire }
                        }
                    }
                },
                tapAbility
            };
            return definition;
        }

        private static GameState EmptyState() => new GameState
        {
            Players = new[]
            {
                new PlayerState { PlayerId = 0 },
                new PlayerState { PlayerId = 1 }
            },
            ActivePlayerId = 0,
            StartingPlayerId = 0,
            TurnNumber = 2,
            Phase = GamePhase.Main,
            RandomSeed = 42
        };

        private static CardDefinition Creature(string id,
            Civilization civilization = Civilization.Fire) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { civilization },
            Cost = 1,
            Type = CardType.Creature,
            Races = new[] { "Test" },
            Power = 1000,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardDefinition Spell(string id) => new CardDefinition
        {
            Id = id,
            Name = id,
            Civilizations = new[] { Civilization.Water },
            Cost = 1,
            Type = CardType.Spell,
            ImplementationStatus = CardImplementationStatus.Complete
        };

        private static CardInstance Instance(long id, CardDefinition definition) =>
            new CardInstance { InstanceId = id, Definition = definition };

        private static RuleAbilityDefinition CostModifier(string id, int value, int minimum) =>
            new RuleAbilityDefinition(id, AbilityTiming.Static, AbilityOperation.ModifyCost)
            {
                Value = value,
                MinimumValue = minimum,
                Selector = new RuleSelectorDefinition
                {
                    Controller = RuleController.Source,
                    Zone = RuleZone.None,
                    Mode = RuleSelectionMode.All,
                    Minimum = 0,
                    Maximum = int.MaxValue,
                    Filter = new CardFilterDefinition { CreaturesOnly = true }
                }
            };

        private sealed class NullDecisionProvider : IAbilityDecisionProvider
        {
            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) => null;
        }

        private sealed class AscendingOrderDecisionProvider : IAbilityDecisionProvider
        {
            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.OrderBy(card => card.InstanceId).Take(maximum).ToArray();
        }

        private sealed class HighestInstanceDecisionProvider : IAbilityDecisionProvider
        {
            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.OrderByDescending(card => card.InstanceId)
                    .Take(maximum).ToArray();
        }

        private sealed class PreferredReplacementDecisionProvider :
            IAbilityDecisionProvider, IAbilityReplacementDecisionProvider
        {
            private readonly string _preferredAbilityId;

            public PreferredReplacementDecisionProvider(string preferredAbilityId)
            {
                _preferredAbilityId = preferredAbilityId;
            }

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.OrderBy(card => card.InstanceId).Take(maximum).ToArray();

            public int ChooseReplacement(GameState state, int playerId,
                CardInstance affectedCard, IReadOnlyList<CardInstance> sources,
                IReadOnlyList<RuleAbilityDefinition> abilities)
            {
                var index = abilities.ToList().FindIndex(ability =>
                    ability.Id == _preferredAbilityId);
                return index < 0 ? 0 : index;
            }
        }

        private sealed class PreferredTriggerOrderDecisionProvider :
            IAbilityDecisionProvider, IAbilityTriggerOrderDecisionProvider
        {
            private readonly string _preferredAbilityId;

            public PreferredTriggerOrderDecisionProvider(string preferredAbilityId)
            {
                _preferredAbilityId = preferredAbilityId;
            }

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.OrderBy(card => card.InstanceId).Take(maximum).ToArray();

            public int ChooseNextTriggeredAbility(GameState state, int playerId,
                CardInstance source, AbilityTiming timing,
                IReadOnlyList<string> abilityIds)
            {
                var index = abilityIds.ToList().FindIndex(id =>
                    id == _preferredAbilityId);
                return index < 0 ? 0 : index;
            }
        }

        private sealed class PreferredCivilizationDecisionProvider :
            IAbilityDecisionProvider, IAbilityCivilizationDecisionProvider
        {
            private readonly Civilization _civilization;

            public PreferredCivilizationDecisionProvider(Civilization civilization)
            {
                _civilization = civilization;
            }

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => true;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.OrderBy(card => card.InstanceId).Take(maximum).ToArray();

            public Civilization ChooseCivilization(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<Civilization> availableCivilizations) => _civilization;
        }

        private sealed class OpponentOnlyOptionalDecisionProvider :
            IAbilityDecisionProvider
        {
            public int LastOptionalPlayerId { get; private set; } = -1;

            public bool ChooseOptional(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability)
            {
                LastOptionalPlayerId = playerId;
                return playerId == 1;
            }

            public IReadOnlyList<CardInstance> ChooseCards(GameState state,
                int playerId, CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.OrderBy(card => card.InstanceId).Take(maximum).ToArray();
        }

        private sealed class OnlyFirstOptionalDecisionProvider : IAbilityDecisionProvider
        {
            public int OptionalCallCount { get; private set; }

            public bool ChooseOptional(GameState state, int playerId, CardInstance source,
                RuleAbilityDefinition ability) => ++OptionalCallCount == 1;

            public IReadOnlyList<CardInstance> ChooseCards(GameState state, int playerId,
                CardInstance source, RuleAbilityDefinition ability,
                IReadOnlyList<CardInstance> candidates, int minimum, int maximum) =>
                candidates.OrderBy(card => card.InstanceId).Take(maximum).ToArray();
        }
    }
}
