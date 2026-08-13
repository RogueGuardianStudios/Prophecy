using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;
using static Rokkan.Prophecy.Tests.SimTestHarness;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The up-thrust: the rising mirror of the dive. What these pin is the pair's grammar —
    /// the thrust exists only on the way up (the apex sheathes it, falling is the dive's
    /// domain), it rides the jump arc rather than driving one (a connect changes nothing about
    /// the movement — no bounce), and like the dive it swings its own blade through the shared
    /// sweep, so one thrust strikes one target once and a fresh jump is a fresh blade.
    /// </summary>
    public class UpThrustCombatTests
    {
        private const int PlayerId = 1;
        private const int PlayerTeam = 1;

        // ---------------------------------------------------------------- harness

        /// <summary>The shared recording world, answering the clean 16-damage landing every
        /// thrust test here assumes unless it says otherwise.</summary>
        private static RecordingCombatWorld World() =>
            new RecordingCombatWorld { Answer = new HitResult(HitOutcome.Landed, 16) };

        private static CharacterSim Player(MovementTuningData tuning, CombatTuningData combat,
                                           ICombatWorld world, Vector2 at) =>
            SimTestHarness.Player(Ground(), combat, world, tuning, at, PlayerId, PlayerTeam);

        private static InputFrame Hold(float x = 0f, float y = 0f) =>
            new InputFrame(new Vector2(x, y));

        private static InputFrame ThrustInput() =>
            new InputFrame(new Vector2(0f, 1f), attack: ButtonState.Press);

        /// <summary>A target floating at <paramref name="y"/>, directly over the thruster.</summary>
        private static Hurtbox TargetAt(float x, float y, int id = 2, int team = 2) =>
            new Hurtbox(id, new Vector2(x, y), new Vector2(0.45f, 0.45f), 0f, team);

        /// <summary>Jump off the ground, then press attack with up held while still rising.</summary>
        private static UpThrust Rise(CharacterSim sim)
        {
            var thrust = sim.Get<UpThrust>();

            Step(sim, Hold(), 3);
            Assert.IsTrue(sim.State.Grounded, "the harness starts from the ground");

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, Hold(), 1);
            Assert.Greater(sim.State.Velocity.y, 0f, "the harness must actually be rising");

            Step(sim, ThrustInput());

            Assert.IsTrue(thrust.IsActive, "the harness must actually be thrusting");
            return thrust;
        }

        // ---------------------------------------------------------------- starting

        [Test]
        public void RisingWithUpHeldStartsTheThrust()
        {
            var sim = Player(new MovementTuningData(), new CombatTuningData(),
                             World(), new Vector2(0f, 0f));

            Rise(sim);
        }

        [Test]
        public void ASameTickJumpStabBelongsToTheSlash()
        {
            // Jump and attack on the EXACT same tick: the attack module runs before the jump
            // does, the body still reads grounded, and the swing's lock includes Jump — so the
            // press buys a standing slash and the jump is refused. An attack is a commitment
            // (the one-action-lock rule, older than this move). The thrust's input is jump,
            // THEN attack while rising; human presses spread over several frames and land
            // there. Pinned so the seam is a decision, not an accident someone rediscovers.
            var sim = Player(new MovementTuningData(), new CombatTuningData(),
                             World(), new Vector2(0f, 0f));

            Step(sim, Hold(), 3);
            Assert.IsTrue(sim.State.Grounded);

            Step(sim, new InputFrame(new Vector2(0f, 1f),
                                     jump: ButtonState.Press, attack: ButtonState.Press));

            Assert.IsFalse(sim.Get<UpThrust>().IsActive, "the slash took the lock first");
            Assert.IsTrue(sim.Get<AttackModule>().IsAttacking, "the press was not eaten");
            Assert.AreEqual(0f, sim.State.Velocity.y, 0.001f,
                            "the slash committed the body; the jump was refused by its lock");
        }

        [Test]
        public void TheGroundRefusesIt()
        {
            var sim = Player(new MovementTuningData(), new CombatTuningData(),
                             World(), new Vector2(0f, 0f));

            Step(sim, Hold(), 3);
            Step(sim, ThrustInput());

            Assert.IsFalse(sim.Get<UpThrust>().IsActive);
        }

        [Test]
        public void AFallRefusesIt()
        {
            // Falling is the dive's domain. An up-thrust that fires on the descent would blur
            // the pair into one air slash and there would be nothing left to teach in Threnhold.
            var sim = Player(new MovementTuningData(), new CombatTuningData(),
                             World(), new Vector2(0f, 6f));

            Step(sim, Hold(), 3);
            Assert.IsFalse(sim.State.Grounded);
            Assert.Less(sim.State.Velocity.y, 0f);

            Step(sim, ThrustInput());

            Assert.IsFalse(sim.Get<UpThrust>().IsActive);
        }

        [Test]
        public void ANeutralAirPressIsNotAThrust()
        {
            var sim = Player(new MovementTuningData(), new CombatTuningData(),
                             World(), new Vector2(0f, 0f));

            Step(sim, Hold(), 3);
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, Hold(), 1);
            Step(sim, new InputFrame(Vector2.zero, attack: ButtonState.Press));

            Assert.IsFalse(sim.Get<UpThrust>().IsActive);
        }

        // ---------------------------------------------------------------- connecting

        [Test]
        public void AThrustThatConnectsKeepsItsArc()
        {
            // No bounce, no hang: the jump was the commitment and it continues. The dive's pop
            // is the reward for placing the hardest attack in the game; this one was placed by
            // the jump that carries it, so its reward is simply the hit.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = World();

            var sim = Player(tuning, combat, world, new Vector2(0f, 0f));
            var thrust = Rise(sim);

            float velocityBefore = sim.State.Velocity.y;

            world.Targets.Add(TargetAt(0f, sim.State.Position.y + combat.UpThrustBox.Offset.y));
            Step(sim, Hold(y: 1f));

            Assert.AreEqual(1, world.Hits.Count);
            Assert.AreEqual(combat.UpThrustAttackId, world.Hits[0].AttackId);
            Assert.AreEqual(combat.UpThrustBox.Damage, world.Hits[0].Damage);

            Assert.IsTrue(thrust.IsActive, "a connect does not end the rise");
            Assert.Less(sim.State.Velocity.y, velocityBefore, "gravity owns the arc; nothing pops it");
        }

        [Test]
        public void OneThrustHitsOneTargetOnce()
        {
            // The box is live for every tick of the rise, so without dedup a target the blade
            // rises past would be milled on every tick of the overlap.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = World();

            var sim = Player(tuning, combat, world, new Vector2(0f, 0f));
            Rise(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y + combat.UpThrustBox.Offset.y));
            Step(sim, Hold(y: 1f), 6);

            Assert.AreEqual(1, world.Hits.Count);
        }

        [Test]
        public void ASecondThrustCanHitTheSameTargetAgain()
        {
            // A fresh jump is a fresh blade — the dedup set starts empty per thrust, exactly
            // like the dive's does per dive.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = World();

            var sim = Player(tuning, combat, world, new Vector2(0f, 0f));
            Rise(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y + combat.UpThrustBox.Offset.y));
            Step(sim, Hold(y: 1f));
            Assert.AreEqual(1, world.Hits.Count);

            // Come down, land, and go again into the same target.
            world.Targets.Clear();
            Step(sim, Hold(), 120);
            Assert.IsTrue(sim.State.Grounded);

            Rise(sim);
            world.Targets.Add(TargetAt(0f, sim.State.Position.y + combat.UpThrustBox.Offset.y));
            Step(sim, Hold(y: 1f));

            Assert.AreEqual(2, world.Hits.Count);
        }

        [Test]
        public void ATargetOnTheThrustersOwnTeamIsNotStruck()
        {
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = World();

            var sim = Player(tuning, combat, world, new Vector2(0f, 0f));
            var thrust = Rise(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y + combat.UpThrustBox.Offset.y,
                                       team: PlayerTeam));
            Step(sim, Hold(y: 1f));

            Assert.IsTrue(thrust.IsActive);
            Assert.IsEmpty(world.Hits);
        }

        // ---------------------------------------------------------------- ending

        [Test]
        public void TheApexSheathesTheBlade()
        {
            var sim = Player(new MovementTuningData(), new CombatTuningData(),
                             World(), new Vector2(0f, 0f));
            var thrust = Rise(sim);

            Assert.IsFalse(sim.Can(LockFlags.Move), "the thrust commits");

            int guard = 0;
            while (sim.State.Velocity.y > 0f && guard++ < 240) Step(sim, Hold(y: 1f));

            Assert.IsFalse(thrust.IsActive, "the rise is its whole lifetime");
            Assert.IsFalse(sim.State.Grounded, "it ended in the air, not by landing");
            Assert.IsTrue(sim.Can(LockFlags.Move), "the apex hands control straight back");
        }

        [Test]
        public void TheApexLeavesRoomForTheDive()
        {
            // The Zelda II air sandwich: stab up on the way up, stab down on the way down. The
            // pair partitions the air, and the seam between them is one tick wide.
            var sim = Player(new MovementTuningData(), new CombatTuningData(),
                             World(), new Vector2(0f, 0f));
            var thrust = Rise(sim);

            int guard = 0;
            while (sim.State.Velocity.y > 0f && guard++ < 240) Step(sim, Hold(y: 1f));
            Assert.IsFalse(thrust.IsActive);
            Assert.IsFalse(sim.State.Grounded);

            Step(sim, new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press));

            Assert.IsTrue(sim.Get<DownThrust>().IsActive, "falling is the dive's to claim");
        }

        // ---------------------------------------------------------------- being answered

        [Test]
        public void AParriedThrustIsPunished()
        {
            // Read at the top of a jump, the thruster falls with nothing left to say — the
            // dive's rule, mirrored.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld
            {
                Answer = new HitResult(HitOutcome.Parried, 0, combat.ParryStunTicks),
            };

            var sim = Player(tuning, combat, world, new Vector2(0f, 0f));
            var thrust = Rise(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y + combat.UpThrustBox.Offset.y));
            Step(sim, Hold(y: 1f));

            Assert.IsFalse(thrust.IsActive, "the thrust is over");

            Step(sim, Hold());
            Assert.IsTrue(sim.Get<HitReact>().IsReacting, "and the thruster pays for it");
        }

        [Test]
        public void ABlockedThrustJustKeepsRising()
        {
            // Blocking answers the damage; it does not answer the jump. Nothing about the arc
            // is the defender's to change.
            var tuning = new MovementTuningData();
            var combat = new CombatTuningData();
            var world = new RecordingCombatWorld { Answer = new HitResult(HitOutcome.Blocked, 4) };

            var sim = Player(tuning, combat, world, new Vector2(0f, 0f));
            var thrust = Rise(sim);

            world.Targets.Add(TargetAt(0f, sim.State.Position.y + combat.UpThrustBox.Offset.y));
            Step(sim, Hold(y: 1f));

            Assert.IsTrue(thrust.IsActive);
            Assert.AreEqual(1, world.Hits.Count);
        }
    }
}
