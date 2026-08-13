using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using Rokkan.Prophecy.Presentation;
using UnityEngine;
using static Rokkan.Prophecy.Tests.SimTestHarness;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The attack module: the join between the combat timing spine and the rest of the sim.
    ///
    /// <para>The timeline, the combo runner and the hit resolver are each already tested on their
    /// own. What is only testable here is the wiring — that a press commits the character, that
    /// control comes back on the right tick, that a box live for four ticks deals its damage once,
    /// and that the cancel window reaches the arbiter so a takeover behaves.</para>
    ///
    /// <para>Every test is headless. No scene, no colliders, no animation — combat outcomes are
    /// the thing this project most needs to reproduce identically at any frame rate, and the
    /// claim is only worth making if the whole loop can be stepped a tick at a time.</para>
    /// </summary>
    public class AttackModuleTests
    {
        private const int PlayerId = 1;
        private const int PlayerTeam = 1;
        private const int DummyId = 2;
        private const int DummyTeam = 2;

        // ---------------------------------------------------------------- harness

        /// <summary>A body-sized target standing at <paramref name="x"/>.</summary>
        private static Hurtbox Dummy(float x, int team = DummyTeam, int id = DummyId) =>
            Hurtbox.ForBody(id, new Vector2(x, 0f), new Vector2(0.9f, 1.8f), team);

        private static InputFrame Attack(float x = 0f, float y = 0f) =>
            new InputFrame(new Vector2(x, y), attack: ButtonState.Press);

        private static InputFrame Hold(float x = 0f, float y = 0f) =>
            new InputFrame(new Vector2(x, y));

        private static AttackModule ModuleOf(CharacterSim sim) => sim.Get<AttackModule>();

        // ---------------------------------------------------------------- starting

        [Test]
        public void APress_StartsTheStandingAttack()
        {
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            Step(sim, Attack());

            var module = ModuleOf(sim);
            Assert.IsTrue(module.IsAttacking);
            Assert.AreEqual(combat.StandingAttackId, module.Current.Id);
        }

        [Test]
        public void Crouching_ThrowsTheLowAttackInstead()
        {
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            Step(sim, Hold(y: -1f), 2);
            Assert.AreEqual(Stance.Crouch, sim.State.Stance, "the harness must actually be crouched");

            Step(sim, Attack(y: -1f));

            Assert.AreEqual(combat.CrouchingAttackId, ModuleOf(sim).Current.Id);
        }

        [Test]
        public void DownAndAttackOnTheSameTickStillThrowsTheLowAttack()
        {
            // Stance chooses the attack, so the module has to read the stance this tick's input
            // produced rather than last tick's. Getting this wrong is invisible in code and
            // completely visible in play: a chest-height slash over a crouching enemy.
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            Step(sim, Attack(y: -1f));

            Assert.AreEqual(combat.CrouchingAttackId, ModuleOf(sim).Current.Id);
        }

        [Test]
        public void ACrouchingAttackStaysCrouchedForItsWholeSwing()
        {
            // The swing suppresses Move, which is indistinguishable from releasing down. If the
            // crouch module read it that way, the character would stand up on tick two of their
            // own low thrust — and stance is what chose the attack in the first place.
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            Step(sim, Hold(y: -1f), 2);
            Step(sim, Attack(y: -1f));

            var definition = combat.Moveset[combat.CrouchingAttackId];
            for (int i = 1; i < definition.TotalTicks; i++)
            {
                Step(sim, Hold(y: -1f));
                Assert.AreEqual(Stance.Crouch, sim.State.Stance,
                    $"stood up on tick {i} of a crouching attack");
            }
        }

        [Test]
        public void AirbornePressDoesNothingByDefault()
        {
            // The air entry point is empty on purpose: the down-thrust is its own module and owns
            // the dive. An air press must therefore not throw the standing attack.
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, Hold(), 4);
            Assert.AreEqual(Stance.Air, sim.State.Stance);

            Step(sim, Attack());

            Assert.IsFalse(ModuleOf(sim).IsAttacking);
        }

        [Test]
        public void AnAttackCannotStartWhileSomethingOutranksIt()
        {
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            var cutscene = new object();
            Assert.IsTrue(sim.TryLock(cutscene, LockFlags.All, LockPriority.Absolute));

            Step(sim, Attack());

            Assert.IsFalse(ModuleOf(sim).IsAttacking);
        }

        // ---------------------------------------------------------------- commitment

        [Test]
        public void TheSwingSuppressesMovement()
        {
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            Step(sim, Attack(x: 1f));
            Step(sim, Hold(x: 1f), 4);

            Assert.AreEqual(0f, sim.State.Velocity.x, 0.0001f,
                "an attack must commit the character — walking out of it defeats the point");
        }

        [Test]
        public void ControlReturnsOnTheTickAfterTheActionEnds()
        {
            // The exact tick matters. An attack that hands movement back one tick late reads as
            // sticky, and at 60 Hz that is precisely the resolution combat is judged at.
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            int total = combat.Moveset[combat.StandingAttackId].TotalTicks;

            Step(sim, Attack(x: 1f));
            int lockedTicks = 1;

            for (int i = 0; i < total * 2; i++)
            {
                Step(sim, Hold(x: 1f));
                if (!sim.Can(LockFlags.Move)) lockedTicks++;
                else break;
            }

            Assert.AreEqual(total, lockedTicks,
                "the character is committed for exactly the authored length of the action");
            Assert.Greater(sim.State.Velocity.x, 0f, "movement resumes the moment the lock drops");
        }

        [Test]
        public void TheCancelWindowReachesTheArbiter()
        {
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            var definition = combat.Moveset[combat.StandingAttackId];
            var window = definition.CancelWindow.BaseRange;

            Step(sim, Attack());

            for (int elapsed = 1; elapsed < definition.TotalTicks; elapsed++)
            {
                Step(sim, Hold());
                Assert.AreEqual(window.Contains(elapsed), sim.CurrentLock.CancelWindowOpen,
                    $"the arbiter disagreed with the timeline on tick {elapsed}");
            }
        }

        [Test]
        public void AHigherPriorityTakeoverInTheWindowEndsTheAttack()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f));

            var sim = Player(Ground(), combat, world);
            var definition = combat.Moveset[combat.StandingAttackId];

            Step(sim, Attack());

            // Forward to the cancel window. The opener's own hit has already landed by now, which
            // is the point — what must not happen is any FURTHER hit after the takeover.
            int toWindow = definition.CancelWindow.BaseStart;
            Step(sim, Hold(), toWindow);
            Assert.IsTrue(sim.CurrentLock.CancelWindowOpen);

            var hitReact = new object();
            Assert.IsTrue(sim.TryLock(hitReact, LockFlags.All, LockPriority.HitReact),
                "hit-react outranks an attack and the window is open");

            int before = world.Hits.Count;
            Step(sim, Hold(), 12);

            Assert.IsFalse(ModuleOf(sim).IsAttacking, "an interrupted swing must not keep running");
            Assert.AreEqual(before, world.Hits.Count, "and must not keep landing hits");
        }

        // ---------------------------------------------------------------- hits

        [Test]
        public void AHitLandsOnceThoughTheBoxIsLiveForSeveralTicks()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f));

            var sim = Player(Ground(), combat, world);
            var definition = combat.Moveset[combat.StandingAttackId];

            Assert.Greater(definition.HitBoxes[0].Window.Duration, 1,
                "this test is meaningless unless the box is live for more than one tick");

            Step(sim, Attack());
            Step(sim, Hold(), definition.TotalTicks);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(DummyId, world.Hits[0].TargetId);
            Assert.AreEqual(PlayerId, world.Hits[0].AttackerId);
            Assert.AreEqual(definition.HitBoxes[0].Damage, world.Hits[0].Damage);
        }

        [Test]
        public void TheHitLandsOnTheTickTheWindowOpens()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f));

            var sim = Player(Ground(), combat, world);
            var definition = combat.Moveset[combat.StandingAttackId];

            long startTick = sim.CurrentTick + 1;
            Step(sim, Attack());
            Step(sim, Hold(), definition.TotalTicks);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(definition.HitBoxes[0].Window.Start, world.Hits[0].Tick - startTick,
                "the hit must land on the authored tick, counted from the start of the action");
        }

        [Test]
        public void ATargetBehindTheAttackerIsNotHit()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(-1.2f));

            var sim = Player(Ground(), combat, world);

            Step(sim, Attack());
            Step(sim, Hold(), combat.Moveset[combat.StandingAttackId].TotalTicks);

            Assert.IsEmpty(world.Hits);
        }

        [Test]
        public void ATargetOnTheAttackersOwnTeamIsNotHit()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f, team: PlayerTeam));

            var sim = Player(Ground(), combat, world);

            Step(sim, Attack());
            Step(sim, Hold(), combat.Moveset[combat.StandingAttackId].TotalTicks);

            Assert.IsEmpty(world.Hits);
        }

        [Test]
        public void AWallBetweenAttackerAndTargetStopsTheHit()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f));

            var level = Ground();
            level.Add(new Aabb(new Vector2(0.85f, 0f), new Vector2(0.95f, 3f)));

            var sim = Player(level, combat, world);

            Assert.IsTrue(combat.Moveset[combat.StandingAttackId].HitBoxes[0].StoppedByGeometry,
                "this test asserts the flag is honoured, so the flag has to be set");

            Step(sim, Attack());
            Step(sim, Hold(), combat.Moveset[combat.StandingAttackId].TotalTicks);

            Assert.IsEmpty(world.Hits);
        }

        [Test]
        public void ACrouchingAttackReachesLow()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.1f));

            var sim = Player(Ground(), combat, world);

            Step(sim, Hold(y: -1f), 2);
            Step(sim, Attack(y: -1f));
            Step(sim, Hold(y: -1f), combat.Moveset[combat.CrouchingAttackId].TotalTicks);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(combat.CrouchingAttackId, world.Hits[0].AttackId);
        }

        // ---------------------------------------------------------------- chaining

        [Test]
        public void APressBeforeTheWindowIsBufferedAndChainsWhenItOpens()
        {
            var combat = new CombatTuningData();
            var sim = Player(Ground(), combat, null);

            var definition = combat.Moveset[combat.StandingAttackId];
            int windowOpens = definition.CancelWindow.BaseStart;

            Step(sim, Attack());

            // Press early — inside the buffer, before the window. The chain must not fire yet.
            Step(sim, Hold(), windowOpens - 4);
            Step(sim, Attack());
            Assert.AreEqual(definition.Id, ModuleOf(sim).Current.Id, "chained before the window opened");

            Step(sim, Hold(), 4);

            Assert.AreEqual(definition.ChainsInto, ModuleOf(sim).Current.Id,
                "the remembered press must spend itself the moment the window opens");
        }

        [Test]
        public void TheChainLinkCanHitTheSameTargetAgain()
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f));

            var sim = Player(Ground(), combat, world);
            var definition = combat.Moveset[combat.StandingAttackId];
            var next = combat.Moveset[definition.ChainsInto];

            Step(sim, Attack());
            Step(sim, Hold(), definition.CancelWindow.BaseStart - 1);
            Step(sim, Attack());
            Step(sim, Hold(), definition.TotalTicks + next.TotalTicks);

            Assert.AreEqual(2, world.Hits.Count, "the follow-up must connect with the same target");
            Assert.AreEqual(definition.Id, world.Hits[0].AttackId);
            Assert.AreEqual(next.Id, world.Hits[1].AttackId);
            Assert.AreEqual(next.HitBoxes[0].Damage, world.Hits[1].Damage);
        }

        // ---------------------------------------------------------------- data

        [Test]
        public void TheDefaultMovesetValidates()
        {
            Assert.IsNull(new CombatTuningData().Validate());
        }

        [Test]
        public void TheMovesetSurvivesUnitysSerializer()
        {
            // This caught a real one. Hit windows were a TickRange field, and Unity does not
            // serialize readonly fields — so every window round-tripped as [0..0), which reads as
            // "no window". Nothing was logged; the asset simply loaded inert on the next fresh
            // Editor launch and no attack in the game would ever have connected.
            //
            // Asserting through the actual serializer is the only way to see it: in memory the
            // field initializers make everything look fine.
            var authored = ScriptableObject.CreateInstance<Rokkan.Prophecy.Core.CombatTuning>();
            var reloaded = ScriptableObject.CreateInstance<Rokkan.Prophecy.Core.CombatTuning>();

            try
            {
                UnityEditor.EditorJsonUtility.FromJsonOverwrite(
                    UnityEditor.EditorJsonUtility.ToJson(authored), reloaded);

                Assert.IsNull(reloaded.Data.Validate(),
                    "the moveset does not survive a round trip through Unity's serializer");

                for (int a = 0; a < authored.Data.Attacks.Length; a++)
                {
                    var before = authored.Data.Attacks[a];
                    var after = reloaded.Data.Attacks[a];

                    Assert.AreEqual(before.Id, after.Id);

                    for (int b = 0; b < before.HitBoxes.Length; b++)
                    {
                        Assert.AreEqual(before.HitBoxes[b].Window, after.HitBoxes[b].Window,
                            $"{before.Id} hit box {b} lost its window");
                        Assert.AreEqual(before.HitBoxes[b].Damage, after.HitBoxes[b].Damage);
                    }

                    Assert.AreEqual(before.CancelWindow.BaseRange, after.CancelWindow.BaseRange);
                }

                Assert.AreEqual(authored.Data.ParryWindow.BaseRange,
                                reloaded.Data.ParryWindow.BaseRange,
                                "the parry window lost its shape");
            }
            finally
            {
                Object.DestroyImmediate(authored);
                Object.DestroyImmediate(reloaded);
            }
        }

        [Test]
        public void AnEntryPointRequiringADifferentStanceIsReported()
        {
            // Silent in play — the press simply does nothing — so it has to be loud here.
            var combat = new CombatTuningData { CrouchingAttackId = "slash_high" };

            var problems = combat.Validate();

            Assert.IsNotNull(problems);
            StringAssert.Contains("can never start", problems);
        }

        [Test]
        public void AChainNamingAMissingAttackIsReported()
        {
            var combat = new CombatTuningData();
            combat.Attacks[0].ChainsInto = "nonexistent";

            StringAssert.Contains("does not exist", combat.Validate());
        }

        [Test]
        public void AddingAnAttackNeedsNoCodeChange()
        {
            // The architecture test, in its data form. A new move is an entry in an asset — no new
            // module, and no edit to an existing one.
            var combat = new CombatTuningData();
            var existing = combat.Attacks;

            var uppercut = new AttackDefinition
            {
                Id = "uppercut",
                RequiredStance = Stance.Crouch,
                StartupTicks = 4,
                ActiveTicks = 3,
                RecoveryTicks = 10,
                HitBoxes = new[]
                {
                    new AttackHitBox
                    {
                        OpenTick = 4, CloseTick = 7,
                        Offset = new Vector2(0.7f, 1.2f),
                        HalfExtents = new Vector2(0.5f, 0.7f),
                        Damage = 21,
                    },
                },
            };

            var grown = new AttackDefinition[existing.Length + 1];
            existing.CopyTo(grown, 0);
            grown[existing.Length] = uppercut;

            combat.Attacks = grown;
            combat.CrouchingAttackId = "uppercut";

            Assert.IsNull(combat.Validate());

            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f));
            var sim = Player(Ground(), combat, world);

            Step(sim, Hold(y: -1f), 2);
            Step(sim, Attack(y: -1f));
            Step(sim, Hold(y: -1f), uppercut.TotalTicks);

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual("uppercut", world.Hits[0].AttackId);
            Assert.AreEqual(21, world.Hits[0].Damage);
        }

        // ---------------------------------------------------------------- frame rate

        /// <summary>
        /// The acceptance test: identical combat at 30, 60 and 144 fps.
        ///
        /// <para>Driven the way the game is — the button is sampled once per rendered frame and
        /// latched, the accumulator releases whole fixed ticks, and each tick consumes the latch.
        /// That is what makes the claim meaningful rather than circular: at 144 fps most frames
        /// produce no tick at all and at 30 fps most produce two, so a press only survives both
        /// because the latch holds the edge until a tick takes it.</para>
        /// </summary>
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void CombatIsIdenticalAtAnyFrameRate(int fps)
        {
            var expected = RunAtFrameRate(60);
            var actual = RunAtFrameRate(fps);

            Assert.AreEqual(expected.Count, actual.Count, "a different number of hits landed");

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].Tick, actual[i].Tick, $"hit {i} landed on a different tick");
                Assert.AreEqual(expected[i].TargetId, actual[i].TargetId);
                Assert.AreEqual(expected[i].Damage, actual[i].Damage);
                Assert.AreEqual(expected[i].AttackId, actual[i].AttackId);
            }
        }

        private static List<HitEvent> RunAtFrameRate(int fps)
        {
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld();
            world.Targets.Add(Dummy(1.2f));

            var sim = Player(Ground(), combat, world);

            var latch = new ButtonLatch();

            // The accumulator is the REAL one — SimClock, the loop the game ships — rather than
            // a re-derivation of its arithmetic. A hand-rolled copy once mixed a double
            // accumulator with the float tick constant, and the 60 fps baseline quietly retired
            // 89 ticks instead of 90; driving the actual clock makes that class of drift
            // impossible, because there is nothing here to drift.
            var clock = new SimClock();
            clock.Register(new TickHook(_ =>
                sim.SetInput(new InputFrame(Vector2.zero, attack: latch.Consume()))));
            clock.Register(sim);

            int frames = Mathf.CeilToInt(fps * 1.5f);

            for (int frame = 0; frame < frames; frame++)
            {
                // One tap, on the first rendered frame. At 144 fps that frame produces no tick at
                // all, so the edge has to survive in the latch to be seen.
                latch.Sample(frame == 0);

                clock.Advance(1.0 / fps);
            }

            // 1.5 s of real time is 90 ticks at any rate, give or take the accumulator's tail
            // residue — if the clamp had eaten a backlog the comparison above would be between
            // runs of different lengths and mean nothing.
            Assert.That(clock.CurrentTick, Is.EqualTo(90).Within(1),
                "the clock must retire the same ticks however the time is sliced");

            return world.Hits;
        }
    }
}
