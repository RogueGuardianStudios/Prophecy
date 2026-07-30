using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The down-thrust's damage half, and the bounce it drives.
    ///
    /// <para>The interesting thing here is architectural, not mechanical. The bounce sat
    /// unreferenced for two milestones because every obvious caller was one module reaching into
    /// another — and the way out was that there was never a second module: a down-thrust is one
    /// action with a movement half and a damage half, so it swings its own blade and bounces
    /// itself. These tests pin that: the dive connects, pops, and can descend a column, with
    /// nothing but <c>ICombatWorld</c> between it and the targets.</para>
    /// </summary>
    public class DownThrustCombatTests
    {
        private const float Dt = 1f / 60f;

        private const int PlayerId = 1;
        private const int PlayerTeam = 1;

        /// <summary>Records what it was asked and answers however the test wants.</summary>
        private sealed class RecordingWorld : ICombatWorld
        {
            public readonly List<Hurtbox> Targets = new List<Hurtbox>();
            public readonly List<HitEvent> Hits = new List<HitEvent>();

            public HitResult Answer = new HitResult(HitOutcome.Landed, 16);

            public IReadOnlyList<Hurtbox> Hurtboxes => Targets;

            public HitResult OnHit(in HitEvent hit)
            {
                Hits.Add(hit);
                return Answer;
            }
        }

        // ---------------------------------------------------------------- harness

        private static CollisionWorld Ground(float top = 0f)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, top - 2f), new Vector2(500f, top)));
            return world;
        }

        private static CharacterSim Player(MovementTuningData tuning, CombatTuningData combat,
                                           ICombatWorld world, Vector2 at)
        {
            var sim = PlayerCharacterFactory.Create(
                Ground(), tuning, MovementSpace.SideScroll, null, combat, world);

            sim.State.CombatId = PlayerId;
            sim.State.Team = PlayerTeam;
            sim.Teleport(at, facing: 1);
            return sim;
        }

        private static void Step(CharacterSim sim, InputFrame input, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.SetInput(input);
                sim.Tick(new SimTickInfo(sim.CurrentTick + 1, Dt));
            }
        }

        private static InputFrame Hold(float x = 0f, float y = 0f) =>
            new InputFrame(new Vector2(x, y));

        private static InputFrame DiveInput() =>
            new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press);

        /// <summary>A target floating at <paramref name="y"/>, directly below the diver.</summary>
        private static Hurtbox TargetAt(float x, float y, int id = 2, int team = 2) =>
            new Hurtbox(id, new Vector2(x, y), new Vector2(0.45f, 0.45f), 0f, team);

        /// <summary>Drop from height with down held, pressing attack once airborne.</summary>
        private static DownThrust Dive(CharacterSim sim)
        {
            var thrust = sim.Get<DownThrust>();

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, Hold(y: -1f), 3);
            Step(sim, DiveInput());

            Assert.IsTrue(thrust.IsActive, "the harness must actually be diving");
            return thrust;
        }

        // ---------------------------------------------------------------- connecting

        [Test]
        public void ADiveThatConnectsBounces()
        {
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld();

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            var thrust = Dive(sim);

            // Put a target under the feet only once the dive is running, so the bounce cannot be
            // confused with something that was true before the move started.
            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f));

            Assert.IsFalse(thrust.IsActive, "connecting ends the dive");
            Assert.AreEqual(tuning.DownThrustBounceSpeed, sim.State.Velocity.y, 0.001f);
            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(combat.DownThrustAttackId, world.Hits[0].AttackId);
            Assert.AreEqual(combat.DownThrustBox.Damage, world.Hits[0].Damage);
        }

        [Test]
        public void ADiveThatHitsNothingJustLands()
        {
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld();

            var sim = Player(tuning, combat, world, new Vector2(0f, 4f));
            var thrust = Dive(sim);

            Step(sim, Hold(y: -1f), 120);

            Assert.IsFalse(thrust.IsActive);
            Assert.IsTrue(sim.State.Grounded);
            Assert.IsEmpty(world.Hits);
            Assert.LessOrEqual(sim.State.Velocity.y, 0f, "landing is not a bounce");
        }

        [Test]
        public void TheBounceReleasesTheCommitmentLock()
        {
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld();

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            Dive(sim);

            Assert.IsFalse(sim.Can(LockFlags.Move), "the dive commits");

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f));

            Assert.IsTrue(sim.Can(LockFlags.Move), "the pop hands control straight back");
        }

        [Test]
        public void OneDiveHitsOneTargetOnce()
        {
            // The box is live for every tick of the dive, so without dedup a target the diver is
            // still overlapping would be hit again on the tick after the bounce.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld();

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            Dive(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f), 10);

            Assert.AreEqual(1, world.Hits.Count);
        }

        [Test]
        public void ASecondDiveCanHitTheSameTargetAgain()
        {
            // Descending a column means bouncing off the same thing twice when it survives — a new
            // dive is a new action, so the dedup set starts empty.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld();

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            Dive(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f));
            Assert.AreEqual(1, world.Hits.Count);

            // Still airborne on the way up from the bounce: dive again into the same target.
            world.Targets.Clear();
            Step(sim, Hold(y: -1f), 2);
            Step(sim, DiveInput());

            var thrust = sim.Get<DownThrust>();
            Assert.IsTrue(thrust.IsActive, "the bounce must leave the character able to dive again");

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f));

            Assert.AreEqual(2, world.Hits.Count);
        }

        // ---------------------------------------------------------------- what does not bounce

        [Test]
        public void PhasingThroughInvulnerabilityDoesNotBounce()
        {
            // Otherwise a target with i-frames is a free extra jump, and the down-thrust becomes a
            // way to hover over anything that has just been hit.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld { Answer = new HitResult(HitOutcome.Invulnerable) };

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            var thrust = Dive(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f));

            Assert.IsTrue(thrust.IsActive, "nothing was struck, so nothing to push off");
            Assert.Less(sim.State.Velocity.y, 0f, "still falling");
        }

        [Test]
        public void ABlockedDiveStillBounces()
        {
            // Something solid was struck. Whether the target was hurt by it is the target's
            // business, not the diver's — a shield is still a thing to push off.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld { Answer = new HitResult(HitOutcome.Blocked, 4) };

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            var thrust = Dive(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f));

            Assert.IsFalse(thrust.IsActive);
            Assert.Greater(sim.State.Velocity.y, 0f);
        }

        [Test]
        public void AParriedDiveIsPunishedInsteadOfBouncing()
        {
            // The reward for reading a dive is that the diver keeps falling with nothing left to do
            // about it. Popping them back up would make a correct read the diver's escape route.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld
            {
                Answer = new HitResult(HitOutcome.Parried, 0, combat.ParryStunTicks),
            };

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            var thrust = Dive(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f));
            Step(sim, Hold(y: -1f));

            Assert.IsFalse(thrust.IsActive, "the dive is over");
            Assert.Less(sim.State.Velocity.y, 0f, "no pop off a parried blade");

            Step(sim, Hold());
            Assert.IsTrue(sim.Get<HitReact>().IsReacting, "and the diver pays for it");
        }

        [Test]
        public void ATargetOnTheDiversOwnTeamIsNotStruck()
        {
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld();

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            var thrust = Dive(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y - 0.2f, team: PlayerTeam));
            Step(sim, Hold(y: -1f));

            Assert.IsTrue(thrust.IsActive);
            Assert.IsEmpty(world.Hits);
        }

        [Test]
        public void ADiveWithNoCombatWorldStillDivesAndLands()
        {
            // A traversal scene has nothing to hit. The move must remain a movement move.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();

            var sim = Player(tuning, combat, null, new Vector2(0f, 4f));
            var thrust = Dive(sim);

            Step(sim, Hold(y: -1f), 120);

            Assert.IsFalse(thrust.IsActive);
            Assert.IsTrue(sim.State.Grounded);
        }

        // ---------------------------------------------------------------- frame rate

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void TheBounceIsIdenticalAtAnyFrameRate(int fps)
        {
            Assert.AreEqual(RunAtFrameRate(60), RunAtFrameRate(fps));
        }

        /// <summary>
        /// Dive onto a fixed target and record the tick it connected and the velocity it left
        /// with. Both are integers-in, floats-out of the same fixed step, so any frame-rate
        /// dependence in the dive would show up in one or the other.
        /// </summary>
        private static string RunAtFrameRate(int fps)
        {
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingWorld();

            var sim = Player(tuning, combat, world, new Vector2(0f, 6f));
            world.Targets.Add(TargetAt(0f, 3f));

            double fixedDelta = 1.0 / SimConstants.TicksPerSecond;
            double frameDelta = 1.0 / fps;
            double accumulator = 0.0;
            long tick = 0;

            const int Ticks = 90;
            int pressTick = 6;

            while (tick < Ticks)
            {
                accumulator += frameDelta;

                while (accumulator >= fixedDelta && tick < Ticks)
                {
                    accumulator -= fixedDelta;
                    tick++;

                    // Jump on the first tick, dive on a fixed tick after it. Scripted by tick
                    // rather than by frame so the two runs are asking the same question.
                    InputFrame input;
                    if (tick == 1) input = new InputFrame(Vector2.zero, jump: ButtonState.Press);
                    else if (tick == pressTick) input = DiveInput();
                    else input = Hold(y: -1f);

                    sim.SetInput(input);
                    sim.Tick(new SimTickInfo(tick, SimConstants.FixedDeltaSeconds));
                }
            }

            var thrust = sim.Get<DownThrust>();
            return string.Format("hits={0} bounce={1} y={2:F4}",
                world.Hits.Count,
                thrust.LastBounceTick,
                sim.State.Position.y);
        }
    }
}
