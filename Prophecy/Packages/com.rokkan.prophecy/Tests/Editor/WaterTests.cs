using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Arts;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;
using static Rokkan.Prophecy.Tests.SimTestHarness;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Water: the global rule (the player sinks — a struggle, not a swim) and Mirefen's answer
    /// to it. What these pin: buoyancy makes jumps HIGHER underwater while drag makes walking
    /// slower — one force, both feels; the breath is the hazard and it goes straight to vitals
    /// past every defence gate; the art floats at the waterline or launches from depth, deeper
    /// casts launching higher; and a committed dive pierces water at full speed, because the
    /// thrusts' re-asserted velocities outrank the water's opinion by module order.
    /// </summary>
    public class WaterTests
    {
        // ---------------------------------------------------------------- harness

        /// <summary>Dry land everywhere: one long floor with its top at 0.</summary>
        private static CollisionWorld DryGround() => Ground();

        /// <summary>
        /// A deck at 0 for x&lt;0, then a pool: floor at -depth for 0..30, a far wall, and
        /// water filling the basin to a surface at 0.
        /// </summary>
        private static CollisionWorld PoolWorld(float depth)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, -2f), new Vector2(0f, 0f)));            // deck
            world.Add(new Aabb(new Vector2(0f, -depth - 2f), new Vector2(30f, -depth)));  // basin floor
            world.Add(new Aabb(new Vector2(30f, -depth - 2f), new Vector2(32f, 4f)));     // far wall
            world.AddWater(new Aabb(new Vector2(0f, -depth), new Vector2(30f, 0f)));
            return world;
        }

        private static CharacterSim Player(CollisionWorld collision, MovementTuningData tuning,
                                           Vector2 at)
        {
            var sim = SimTestHarness.Player(collision, new CombatTuningData(), null, tuning, at);

            // Ascent gates the double jump now; the water tests that jump out of pools
            // light it as part of the rig.
            sim.ActiveArts.Add(ArtId.Ascent);
            return sim;
        }

        private static InputFrame Hold(float x = 0f, float y = 0f) =>
            new InputFrame(new Vector2(x, y));

        private static InputFrame Cast() =>
            new InputFrame(Vector2.zero, flameArt: ButtonState.Press);

        /// <summary>Highest point the feet reach over the next <paramref name="ticks"/>.</summary>
        private static float ApexOver(CharacterSim sim, InputFrame held, int ticks)
        {
            float apex = sim.State.Position.y;
            for (int i = 0; i < ticks; i++)
            {
                Step(sim, held);
                if (sim.State.Position.y > apex) apex = sim.State.Position.y;
            }
            return apex;
        }

        // ---------------------------------------------------------------- the sink

        [Test]
        public void WaterCapsTheSink()
        {
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(6f), tuning, new Vector2(5f, 4f));

            bool sampledInWater = false;
            for (int i = 0; i < 240 && !sim.State.Grounded; i++)
            {
                Step(sim, Hold());

                // The module reads the pre-integration position, so on the very tick the body
                // crosses the surface it has not acted yet — sample by ITS truth, not ours.
                if (!sim.Get<Swim>().InWater) continue;

                sampledInWater = true;
                Assert.GreaterOrEqual(sim.State.Velocity.y, -tuning.WaterSinkSpeed - 0.001f,
                                      "sinking must never exceed the authored sink speed");
            }

            Assert.IsTrue(sampledInWater, "the harness must actually have sunk");
            Assert.IsTrue(sim.State.Grounded, "the sink ends standing on the pool floor");
        }

        [Test]
        public void JumpsBehaveExactlyAsOnLand()
        {
            // No buoyant boost, no vertical drag (Matt's second drive: the boost overshot
            // every ledge grab). A jump from the pool floor rises to the SAME apex a dry jump
            // does — the water's whole opinion of vertical motion is the sink cap.
            var tuning = new MovementTuningData();

            var dry = Player(DryGround(), tuning, new Vector2(0f, 0f));
            Step(dry, Hold(), 3);
            Step(dry, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            float dryRise = ApexOver(dry, Hold(), 120) - 0f;

            var wet = Player(PoolWorld(8f), tuning, new Vector2(5f, -8f));
            Step(wet, Hold(), 3);
            Assert.IsTrue(wet.State.Grounded, "the wet jumper starts on the pool floor");
            Step(wet, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            float wetRise = ApexOver(wet, Hold(), 240) - (-8f);

            Assert.AreEqual(dryRise, wetRise, 0.05f, "water changes nothing about a jump");
        }

        [Test]
        public void WalkingIsSlowerInWaterByTheAuthoredFraction()
        {
            // The trudge is a Speed debuff — an exact, readable fraction (Matt: ~20%), not an
            // exponential argument with the mover's acceleration that reads as nothing.
            var tuning = new MovementTuningData();

            var dry = Player(DryGround(), tuning, new Vector2(0f, 0f));
            Step(dry, Hold(), 3);
            Step(dry, Hold(x: 1f), 60);
            float dryDistance = dry.State.Position.x;

            var wet = Player(PoolWorld(4f), tuning, new Vector2(2f, -4f));
            Step(wet, Hold(), 3);
            float startX = wet.State.Position.x;
            Step(wet, Hold(x: 1f), 60);
            float wetDistance = wet.State.Position.x - startX;

            float expected = dryDistance * (1f - tuning.WaterSpeedPenalty);
            Assert.AreEqual(expected, wetDistance, dryDistance * 0.06f,
                            "the water takes its authored fraction and no more");
        }

        // ---------------------------------------------------------------- the breath

        [Test]
        public void TheBreathRunsOutAndTheWaterTakesHealth()
        {
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(6f), tuning, new Vector2(5f, -6f));
            Step(sim, Hold(), 3);
            Assert.IsTrue(sim.Get<Swim>().HeadUnder, "the harness must actually be under");

            Step(sim, Hold(), tuning.BreathTicks);
            int healthAtExpiry = sim.Vitals.Health;

            Step(sim, Hold(), tuning.DrownIntervalTicks + 1);
            Assert.Less(sim.Vitals.Health, healthAtExpiry, "expired breath drains health");

            int afterFirstBite = sim.Vitals.Health;
            Step(sim, Hold(), tuning.DrownIntervalTicks);
            Assert.Less(sim.Vitals.Health, afterFirstBite, "and keeps draining on the interval");
        }

        [Test]
        public void SurfacingRefillsTheBreath()
        {
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(6f), tuning, new Vector2(5f, -6f));
            Step(sim, Hold(), 3);

            // Almost drowned, then out, then straight back under for the same stretch: only a
            // refilled breath survives both dips with full health.
            Step(sim, Hold(), tuning.BreathTicks - 10);
            sim.Teleport(new Vector2(-5f, 0f), facing: 1);
            Step(sim, Hold(), 5);

            sim.Teleport(new Vector2(5f, -6f), facing: 1);
            Step(sim, Hold(), tuning.BreathTicks - 10);

            Assert.AreEqual(sim.Vitals.MaxHealth, sim.Vitals.Health,
                            "two near-drownings with a surfacing between must cost nothing");
        }

        [Test]
        public void DrownDamageIgnoresTheGuard()
        {
            // There is no parrying drowning: the damage goes straight to vitals, never through
            // the gate chain a shield or i-frames could answer.
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(6f), tuning, new Vector2(5f, -6f));
            Step(sim, Hold(), 3);

            var guarding = new InputFrame(Vector2.zero, block: ButtonState.Holding);
            Step(sim, guarding, tuning.BreathTicks + tuning.DrownIntervalTicks + 2);

            Assert.Less(sim.Vitals.Health, sim.Vitals.MaxHealth,
                        "the shield is not a snorkel");
        }

        // ---------------------------------------------------------------- the art

        [Test]
        public void ACastWhileWadingPopsOntoTheWater()
        {
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(6f), tuning, new Vector2(5f, 2f));

            // Fall in, and cast during the splash. Any real feet submersion is a LAUNCH now
            // (Matt: the feet decide, no full-submersion requirement) — from under a metre
            // it is a small pop, and the splash-back lands standing on the surface.
            while (sim.State.Position.y > -0.8f)
                Step(sim, Hold());

            Step(sim, Cast());
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn, "the cast must take");

            Step(sim, Hold(), 120);

            Assert.AreEqual(0f, sim.State.Position.y, 0.05f, "feet ON the surface, not wading");
            Assert.IsTrue(sim.State.Grounded, "the water top is a platform to STAND on");
            Assert.IsFalse(sim.Get<Swim>().HeadUnder, "standing on water is breathing");
        }

        [Test]
        public void TheWaterTopActsLikeAnyOtherPlatform()
        {
            // Walking on water at full stride, jumping off it like ground, landing back on it
            // like a floor — and the landing resets the jumps, the normal rules (Matt).
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(6f), tuning, new Vector2(3f, 2f));

            while (sim.State.Position.y > -0.8f) Step(sim, Hold());
            Step(sim, Cast());
            Step(sim, Hold(), 90);
            Assert.IsTrue(sim.State.Grounded);

            // Full stride: the same walk on water as a dry deck manages, no trudge.
            float startX = sim.State.Position.x;
            Step(sim, Hold(x: 1f), 60);
            float onWater = sim.State.Position.x - startX;

            var dry = Player(DryGround(), tuning, new Vector2(0f, 0f));
            Step(dry, Hold(), 3);
            Step(dry, Hold(x: 1f), 60);

            Assert.AreEqual(dry.State.Position.x, onWater, 0.25f,
                            "walking on water is walking, not wading");

            // A ground jump off the surface, a landing back on it, and another jump: the
            // platform loop, closed twice to prove the reset.
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Assert.Greater(sim.State.Velocity.y, 0f, "a jump off the water is a ground jump");

            int guard = 0;
            while (!sim.State.Grounded && guard++ < 300) Step(sim, Hold());
            Assert.IsTrue(sim.State.Grounded, "the fall ends back ON the water");
            Assert.AreEqual(0f, sim.State.Position.y, 0.05f);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Assert.Greater(sim.State.Velocity.y, 0f, "landing on water reset the jump");
        }

        [Test]
        public void WaterGrantsNoAirJumps()
        {
            // Normal jump rules apply underwater: the air jump spends once, and only touching
            // a surface gives it back — sinking through water is not touching anything.
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(8f), tuning, new Vector2(-2f, 0f));
            Step(sim, Hold(), 3);

            Step(sim, new InputFrame(new Vector2(1f, 0f), jump: ButtonState.Press));
            Step(sim, Hold(x: 1f), 10);
            Assert.IsFalse(sim.State.Grounded);

            var doubleJump = sim.Get<DoubleJump>();
            Step(sim, new InputFrame(new Vector2(1f, 0f), jump: ButtonState.Press));
            Assert.AreEqual(0, doubleJump.Remaining, "the air jump is spent");

            // Keep carrying right so the fall actually lands in the pool, then sink a while,
            // well under the surface and well off the floor: pressing jump must buy nothing.
            Step(sim, Hold(x: 1f), 60);
            Assert.IsFalse(sim.State.Grounded);
            Assert.IsTrue(sim.Get<Swim>().InWater);

            float before = sim.State.Velocity.y;
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Assert.AreEqual(0, doubleJump.Remaining, "water grants nothing");
            Assert.LessOrEqual(sim.State.Velocity.y, Mathf.Max(before, 0f) + 0.01f,
                               "no jump fired mid-sink");
        }

        [Test]
        public void TheLaunchKeepsTheAirJump()
        {
            // The surge leaves like a jump leaves (Matt): an unused double jump is still in
            // hand at the top of it.
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(5f), tuning, new Vector2(5f, -5f));
            Step(sim, Hold(), 3);

            Step(sim, Cast());
            int guard = 0;
            while (sim.State.Position.y < 0.5f && guard++ < 300) Step(sim, Hold());
            Assert.Greater(sim.State.Position.y, 0f, "the surge must clear the water");

            // Ride the surge to its apex first: pressing mid-surge would fire the air jump
            // into a body already moving faster than the jump itself.
            guard = 0;
            while (sim.State.Velocity.y > 0f && guard++ < 300) Step(sim, Hold());
            Assert.IsFalse(sim.State.Grounded);

            var doubleJump = sim.Get<DoubleJump>();
            Assert.Greater(doubleJump.Remaining, 0, "the launch spent no air jumps");

            float before = sim.State.Velocity.y;
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Assert.Greater(sim.State.Velocity.y, before, "and the double jump still fires");
            Assert.Greater(sim.State.Velocity.y, 0f, "rising again at the top of the surge");
        }

        [Test]
        public void ACastFromDepthLaunchesAndDeeperLaunchesHigher()
        {
            var tuning = new MovementTuningData();

            float ApexFromDepth(float depth)
            {
                var sim = Player(PoolWorld(depth), tuning, new Vector2(5f, -depth));
                Step(sim, Hold(), 3);
                Assert.IsTrue(sim.State.Grounded);
                Step(sim, Cast());
                return ApexOver(sim, Hold(), 300);
            }

            float fromHalf = ApexFromDepth(0.5f);
            float fromThree = ApexFromDepth(3f);
            float fromFive = ApexFromDepth(5f);

            Assert.Greater(fromHalf, 0f, "even a wading-depth cast pops above the surface");
            Assert.Greater(fromThree, fromHalf, "the feet's depth is the whole scale");
            Assert.Greater(fromFive, fromThree, "deeper casts launch higher — the sluice rule");
        }

        [Test]
        public void TheSplashBackLandsOnTheWater()
        {
            // The launch is also a cast of the water-walk: falling back into the pool LANDS
            // on the surface like a floor, rather than resuming the struggle.
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(5f), tuning, new Vector2(5f, -5f));
            Step(sim, Hold(), 3);

            Step(sim, Cast());
            Step(sim, Hold(), 300);

            Assert.AreEqual(0f, sim.State.Position.y, 0.05f,
                            "the surge ends standing on the water, not sinking");
            Assert.IsTrue(sim.State.Grounded);
        }

        [Test]
        public void TheCastIsAToggleAndDryLandDoesNotEndIt()
        {
            // A shallow shelf world: floor at -1 throughout, water only over its first stretch.
            var tuning = new MovementTuningData();
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, -3f), new Vector2(500f, -1f)));
            world.AddWater(new Aabb(new Vector2(0f, -1f), new Vector2(10f, 0f)));

            var sim = Player(world, tuning, new Vector2(5f, -1f));
            Step(sim, Hold(), 3);

            // Depth 1.0, so the cast is a small LAUNCH under the feet-decide rule — ride it
            // out and land standing on the surface.
            Step(sim, Cast());
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn);
            int settle = 0;
            while ((!sim.State.Grounded || sim.State.Position.y > 0.1f) && settle++ < 300)
                Step(sim, Hold());
            Assert.AreEqual(0f, sim.State.Position.y, 0.05f, "walking on the water first");

            // Walk right, off the water's edge, down onto the dry floor beyond it. The cast
            // SURVIVES (Matt: once on, only recasting turns it off) — it is merely inert.
            Step(sim, Hold(x: 1f), 240);
            Assert.Greater(sim.State.Position.x, 10f, "the harness must actually have left");
            Assert.IsTrue(sim.State.Grounded);
            Assert.AreEqual(-1f, sim.State.Position.y, 0.05f, "on ordinary ground");
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn, "dry land does not end the cast");

            // Walk back until over the pool — by observation, not arithmetic, so the gait's
            // speed is nobody's business: the armed cast lifts the body onto the surface.
            int guard = 0;
            while (sim.State.Position.x > 8f && guard++ < 900) Step(sim, Hold(x: -1f));
            Step(sim, Hold(), 30);
            Assert.Less(sim.State.Position.x, 10f);
            Assert.Greater(sim.State.Position.x, 0f, "still over the pool");
            Assert.AreEqual(0f, sim.State.Position.y, 0.05f, "standing on the water again");

            // And the ONLY off switch: the recast. The body resumes the struggle.
            Step(sim, Cast());
            Assert.IsFalse(sim.Get<Buoyancy>().FloatOn, "recasting is the off switch");
            Step(sim, Hold(), 60);
            Assert.AreEqual(-1f, sim.State.Position.y, 0.1f, "sunk back to the floor");
        }

        [Test]
        public void TheCastArmsOnDryLandAndTheNextPoolHonoursIt()
        {
            // The art is castable anywhere (Matt): armed on the shore, it lifts you the
            // moment you wade in — and a second dry press disarms it again.
            var tuning = new MovementTuningData();
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, -3f), new Vector2(500f, -1f)));
            world.AddWater(new Aabb(new Vector2(0f, -1f), new Vector2(10f, 0f)));

            var sim = Player(world, tuning, new Vector2(15f, -1f));
            Step(sim, Hold(), 3);
            Assert.IsFalse(sim.Get<Swim>().InWater, "the harness starts on dry land");

            Step(sim, Cast());
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn, "armed on the shore");

            Step(sim, Cast());
            Assert.IsFalse(sim.Get<Buoyancy>().FloatOn, "and disarmed the same way");

            Step(sim, Cast());
            int guard = 0;
            while (sim.State.Position.x > 5f && guard++ < 900) Step(sim, Hold(x: -1f));
            Step(sim, Hold(), 30);

            Assert.AreEqual(0f, sim.State.Position.y, 0.05f,
                            "the armed cast stood the body on the water it walked into");
        }

        [Test]
        public void LandingOnTheWaterMeetsAFloorAlreadyInPlace()
        {
            // Matt's double bounce: the floor used to be armed only once the feet were
            // INSIDE the water, so every landing spent one tick plunging under the line
            // (uncapped — Swim had not seen the water either) before the pull yanked it
            // back — a bounce nobody authored, worst on fast arrivals into shallow water.
            // The platform now exists over the water before the crossing: one landing,
            // exactly at the line, and it sticks.
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(1f), tuning, new Vector2(5f, 4f));   // waist-deep

            Step(sim, Cast());   // armed in the air, on the way down

            float minY = float.MaxValue;
            int guard = 0;
            while (!sim.State.Grounded && guard++ < 300)
            {
                Step(sim, Hold());
                if (sim.State.Position.y < minY) minY = sim.State.Position.y;
            }

            Assert.IsTrue(sim.State.Grounded, "the fall must end on the water");
            Assert.AreEqual(0f, sim.State.Position.y, 0.01f, "landed exactly on the line");
            Assert.GreaterOrEqual(minY, -0.01f, "and never dipped under it");

            for (int i = 0; i < 30; i++)
            {
                Step(sim, Hold());
                Assert.IsTrue(sim.State.Grounded, "one landing, no bounce");
            }
        }

        [Test]
        public void CastingWhileHangingLetsGoAndFloats()
        {
            // Matt's edge case: cast while holding a ladder (ledges and ropes detach through
            // the same shared state) and the art must take the body — not leave a hand on the
            // wall pinning it under its own rising water.
            var tuning = new MovementTuningData();
            var world = PoolWorld(5f);
            world.AddClimbable(new Aabb(new Vector2(4.5f, -5f), new Vector2(5.5f, 0f)));

            var sim = Player(world, tuning, new Vector2(5f, 2f));

            int guard = 0;
            while (sim.State.Position.y > -0.8f && guard++ < 300) Step(sim, Hold());
            Step(sim, Hold(y: 1f), 2);
            Assert.AreEqual(AttachmentKind.Ladder, sim.State.Attachment,
                            "the harness must actually be hanging");

            Step(sim, Cast());
            Assert.AreEqual(AttachmentKind.None, sim.State.Attachment, "the cast lets go");
            Assert.IsTrue(sim.Get<Buoyancy>().FloatOn);

            Step(sim, Hold(), 120);
            Assert.AreEqual(0f, sim.State.Position.y, 0.05f, "and the body rides up to the top");
            Assert.IsTrue(sim.State.Grounded, "standing on the water, hands free");
        }

        // ---------------------------------------------------------------- the dive

        [Test]
        public void ACommittedDivePiercesWater()
        {
            // The thrusts run after the water by module order, so a dive's re-asserted speed is
            // the tick's last word: water slows everything except a blade with a body on it.
            var tuning = new MovementTuningData();
            var sim = Player(PoolWorld(8f), tuning, new Vector2(5f, 4f));

            Step(sim, Hold(y: -1f), 3);
            Step(sim, new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press));
            Assert.IsTrue(sim.Get<DownThrust>().IsActive, "the harness must actually be diving");

            bool sampledUnder = false;
            for (int i = 0; i < 120 && !sim.State.Grounded; i++)
            {
                Step(sim, Hold(y: -1f));
                if (sim.State.Grounded) break;        // the landing tick zeroes the velocity
                if (sim.State.Position.y > -1f) continue;

                sampledUnder = true;
                Assert.AreEqual(-tuning.DownThrustSpeed, sim.State.Velocity.y, 0.001f,
                                "the dive keeps its authored speed through water");
            }

            Assert.IsTrue(sampledUnder);
        }
    }
}
