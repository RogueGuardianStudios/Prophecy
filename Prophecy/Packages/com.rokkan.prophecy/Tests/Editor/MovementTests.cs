using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The movement numbers, pinned. No scene, no prefab, no Animator — the character is built by
    /// <see cref="PlayerCharacterFactory"/>, exactly as the game builds it, and driven by hand.
    ///
    /// <para>This is what the headless contract was for. Level geometry is about to be generated
    /// from these numbers, so the numbers have to be assertable before any of it exists: a jump
    /// that clears 2.4 m is only a fact if something checks it every build.</para>
    /// </summary>
    public class MovementTests
    {
        private const float Dt = 1f / 60f;

        // ---------------------------------------------------------------- helpers

        private static MovementTuningData Tuning() => new MovementTuningData();

        /// <summary>Tuning with the apex softening off, so a jump arc is pure ballistics and the
        /// authored height can be checked against closed-form physics rather than a fudge factor.</summary>
        private static MovementTuningData PureArcTuning()
        {
            var t = new MovementTuningData();
            t.ApexGravityScale = 1f;
            t.FallGravity = t.RiseGravity;
            return t;
        }

        private static CollisionWorld Ground(float top = 0f)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, top - 2f), new Vector2(500f, top)));
            return world;
        }

        private static CharacterSim Player(MovementTuningData tuning, CollisionWorld world = null,
                                           Vector2 at = default)
        {
            var sim = PlayerCharacterFactory.Create(world ?? Ground(), tuning);
            sim.Teleport(at);
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

        private static void Step(CharacterSim sim, int ticks = 1) => Step(sim, InputFrame.Empty, ticks);

        private static InputFrame Hold(float x = 0f, float y = 0f) => new InputFrame(new Vector2(x, y));

        // ---------------------------------------------------------------- jump arc

        [Test]
        public void Jump_ReachesTheAuthoredHeight()
        {
            var tuning = PureArcTuning();
            var sim = Player(tuning);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            float apex = sim.State.Position.y;
            for (int i = 0; i < 200 && sim.State.Velocity.y > 0f; i++)
            {
                Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding));
                apex = Mathf.Max(apex, sim.State.Position.y);
            }

            // A fixed tick samples the arc rather than following it, so the discrete apex sits
            // half a tick of launch velocity above the continuous one. That offset is predictable
            // and small — but it is real, and pretending otherwise would make this test a lie.
            float expected = tuning.JumpHeight + tuning.JumpVelocity * Dt * 0.5f;

            Assert.AreEqual(expected, apex, 0.02f,
                $"a {tuning.JumpHeight} m jump must clear a {tuning.JumpHeight} m ledge");
            Assert.GreaterOrEqual(apex, tuning.JumpHeight, "never undershoot the authored height");
        }

        [Test]
        public void ApexHang_KeepsTheCharacterAirborneLonger()
        {
            int AirtimeWith(float apexScale)
            {
                var tuning = Tuning();
                tuning.ApexGravityScale = apexScale;
                var sim = Player(tuning);

                Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

                int ticks = 1;
                while (!sim.State.Grounded && ticks < 600)
                {
                    Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding));
                    ticks++;
                }
                return ticks;
            }

            Assert.Greater(AirtimeWith(0.5f), AirtimeWith(1f),
                "softening gravity near the apex is what buys the hang time an aimed down-thrust needs");
        }

        [Test]
        public void ReleasingJumpEarly_ShortensTheArc()
        {
            float ApexAfterHolding(int ticksHeld)
            {
                var tuning = PureArcTuning();
                var sim = Player(tuning);

                Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
                Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), ticksHeld);
                Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Release));

                float apex = sim.State.Position.y;
                for (int i = 0; i < 200 && sim.State.Velocity.y > 0f; i++)
                {
                    Step(sim);
                    apex = Mathf.Max(apex, sim.State.Position.y);
                }
                return apex;
            }

            float shortHop = ApexAfterHolding(2);
            float longHop = ApexAfterHolding(12);

            Assert.Less(shortHop, longHop, "jump height must answer to how long the button is held");
        }

        [Test]
        public void APressAndReleaseInsideOneTick_IsAShortHop()
        {
            var tuning = PureArcTuning();
            var sim = Player(tuning);

            // The capture layer latches edges between ticks, so a fast tap arrives as a single
            // frame carrying both edges and no hold. It must still read as a tap.
            Step(sim, new InputFrame(Vector2.zero, jump: new ButtonState(false, true, true)));

            float apex = sim.State.Position.y;
            for (int i = 0; i < 200 && sim.State.Velocity.y > 0f; i++)
            {
                Step(sim);
                apex = Mathf.Max(apex, sim.State.Position.y);
            }

            Assert.Less(apex, tuning.JumpHeight * 0.6f,
                "a tap that lands inside one tick must not buy a full-height jump");
            Assert.Greater(apex, 0f, "but it must still leave the ground");
        }

        // ---------------------------------------------------------------- coyote time

        [Test]
        public void CoyoteTime_AllowsAJumpJustAfterWalkingOffAnEdge()
        {
            var tuning = Tuning();
            var sim = Player(tuning);

            Step(sim, 3);
            sim.World.Clear();                  // the ground goes away: exactly like stepping off it
            Step(sim, tuning.CoyoteTicks - 1);  // fall almost the whole window...

            Assert.IsFalse(sim.State.Grounded);

            // ...so that the press itself lands on the last tick the window still covers.
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            Assert.Greater(sim.State.Velocity.y, 0f,
                $"a jump {tuning.CoyoteTicks} ticks after leaving the ground must still count");
        }

        [Test]
        public void CoyoteTime_ExpiresAfterItsWindow()
        {
            var tuning = Tuning();
            var sim = Player(tuning);

            Step(sim, 3);
            sim.World.Clear();
            Step(sim, tuning.CoyoteTicks);   // one tick more than the test above

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            Assert.Less(sim.State.Velocity.y, 0f, "one tick past the window is past the window");
        }

        [Test]
        public void CoyoteTime_DoesNotHandOutASecondJump()
        {
            // The subtle one. Right after launching, the character *was* grounded a tick ago, so
            // the coyote window is wide open and a second press would be granted a free jump
            // unless something re-arms only on landing.
            var tuning = Tuning();
            var sim = Player(tuning);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            float afterFirst = sim.State.Velocity.y;

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), 2);
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            Assert.Less(sim.State.Velocity.y, afterFirst,
                "the arc must keep decaying — a second press mid-rise is not a second jump");
        }

        // ---------------------------------------------------------------- jump buffer

        [Test]
        public void JumpBuffer_FiresAPressMadeJustBeforeLanding()
        {
            var tuning = Tuning();

            int landingTick = TicksUntilLanding(tuning);

            var sim = Player(tuning, Ground(), new Vector2(0f, 3f));
            Step(sim, landingTick - 3);
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), 5);

            Assert.Greater(sim.State.Velocity.y, 0f,
                "a press made three ticks early must survive until the feet touch down");
        }

        [Test]
        public void JumpBuffer_ForgetsAPressMadeTooEarly()
        {
            var tuning = Tuning();

            int landingTick = TicksUntilLanding(tuning);
            Assert.Greater(landingTick, tuning.JumpBufferTicks + 4, "test needs a fall longer than the buffer");

            var sim = Player(tuning, Ground(), new Vector2(0f, 3f));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, landingTick + 5);

            Assert.LessOrEqual(sim.State.Velocity.y, 0f,
                "buffering is forgiveness, not a queued command that fires whenever it likes");
        }

        private static int TicksUntilLanding(MovementTuningData tuning)
        {
            var sim = Player(tuning, Ground(), new Vector2(0f, 3f));
            int ticks = 0;
            while (!sim.State.Grounded && ticks < 600)
            {
                Step(sim);
                ticks++;
            }
            return ticks;
        }

        // ---------------------------------------------------------------- landing

        [Test]
        public void Landing_ReportsTheImpactSpeedThatTheSweepDestroys()
        {
            var tuning = Tuning();
            var sim = Player(tuning, Ground(), new Vector2(0f, 6f));
            var fallLand = sim.Get<FallLand>();

            for (int i = 0; i < 600 && !sim.State.Grounded; i++) Step(sim);
            Assert.IsTrue(sim.State.Grounded, "the character should have landed");

            Step(sim);   // modules run before resolution, so the report lands one tick later

            Assert.Greater(fallLand.LastImpactSpeed, 5f,
                "velocity is zeroed by the sweep — if nothing kept the peak, nothing could scale a landing");
            Assert.AreEqual(0, fallLand.AirborneTicks);
        }

        // ---------------------------------------------------------------- ground movement

        [Test]
        public void GroundMove_AcceleratesToWalkSpeed()
        {
            var tuning = Tuning();
            var sim = Player(tuning);

            Step(sim, Hold(1f), 30);

            Assert.AreEqual(tuning.WalkSpeed, sim.State.Velocity.x, 0.01f);
            Assert.AreEqual(1, sim.State.Facing);
        }

        [Test]
        public void GroundMove_StopsUnderFriction()
        {
            var tuning = Tuning();
            var sim = Player(tuning);

            Step(sim, Hold(1f), 30);
            Step(sim, Hold(0f), 30);

            Assert.AreEqual(0f, sim.State.Velocity.x, 0.001f);
        }

        [Test]
        public void RunToggle_LatchesTopSpeedRatherThanHoldingIt()
        {
            var tuning = Tuning();
            var sim = Player(tuning);
            Assert.AreEqual(RunMode.Toggle, tuning.RunMode, "open knob #1 ships as a toggle");

            Step(sim, new InputFrame(new Vector2(1f, 0f), runToggle: ButtonState.Press));
            Step(sim, Hold(1f), 40);   // toggle released — running must persist

            Assert.AreEqual(tuning.RunSpeed, sim.State.Velocity.x, 0.01f);

            Step(sim, new InputFrame(new Vector2(1f, 0f), runToggle: ButtonState.Press));
            Step(sim, Hold(1f), 40);

            Assert.AreEqual(tuning.WalkSpeed, sim.State.Velocity.x, 0.01f);
        }

        [Test]
        public void AnalogBlend_ReplacesTheToggleRatherThanStackingWithIt()
        {
            var tuning = Tuning();
            tuning.RunMode = RunMode.AnalogBlend;
            var sim = Player(tuning);

            // Full deflection reaches run speed with no toggle pressed at all.
            Step(sim, Hold(1f), 40);
            Assert.AreEqual(tuning.RunSpeed, sim.State.Velocity.x, 0.01f);

            // Half deflection sits between the two, which is the whole point of the alternative.
            Step(sim, Hold(0.6f), 60);
            Assert.Less(sim.State.Velocity.x, tuning.RunSpeed);
            Assert.Greater(sim.State.Velocity.x, tuning.WalkSpeed);
        }

        [Test]
        public void AirControl_IsWeakerThanGroundControl()
        {
            var tuning = Tuning();
            Assert.Less(tuning.AirAcceleration, tuning.GroundAcceleration,
                "a jump should commit you somewhat, or the arc stops meaning anything");
        }

        // ---------------------------------------------------------------- crouch

        [Test]
        public void Crouch_ShrinksTheBodyAndSlowsMovement()
        {
            var tuning = Tuning();
            var sim = Player(tuning);

            Step(sim, Hold(1f, -1f), 60);

            Assert.AreEqual(Stance.Crouch, sim.State.Stance);
            Assert.AreEqual(tuning.CrouchHeight, sim.State.BodySize.y, 0.001f);
            Assert.AreEqual(tuning.WalkSpeed * tuning.CrouchSpeedMultiplier, sim.State.Velocity.x, 0.01f);
        }

        [Test]
        public void Crouch_DoesNotStandUpIntoALowCeiling()
        {
            var tuning = Tuning();
            var world = Ground();
            // A gap tall enough to crouch in, too short to stand in.
            world.Add(new Aabb(new Vector2(-4f, tuning.CrouchHeight + 0.2f), new Vector2(4f, 4f)));

            var sim = Player(tuning, world);

            Step(sim, Hold(0f, -1f), 5);
            Assert.AreEqual(Stance.Crouch, sim.State.Stance);

            Step(sim, Hold(0f, 0f), 5);
            Assert.AreEqual(Stance.Crouch, sim.State.Stance,
                "releasing down under a ceiling must not grow the body into the geometry");
        }

        [Test]
        public void Crouch_StandsUpOnceTheCeilingIsGone()
        {
            var tuning = Tuning();
            var world = Ground();
            world.Add(new Aabb(new Vector2(-4f, tuning.CrouchHeight + 0.2f), new Vector2(4f, 4f)));

            var sim = Player(tuning, world);
            Step(sim, Hold(0f, -1f), 5);

            world.Clear();
            world.Add(new Aabb(new Vector2(-500f, -2f), new Vector2(500f, 0f)));

            Step(sim, Hold(0f, 0f), 2);
            Assert.AreEqual(Stance.Stand, sim.State.Stance);
        }

        // ---------------------------------------------------------------- down-thrust

        [Test]
        public void DownThrust_NeedsAirborneDownAndAttackTogether()
        {
            var tuning = Tuning();
            var sim = Player(tuning);
            var thrust = sim.Get<DownThrust>();

            // Grounded: the bible's trigger is jump *then* down *then* attack.
            Step(sim, new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press));
            Assert.IsFalse(thrust.IsActive, "there is nothing below you to stab when you are standing on it");

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), 5);

            // Airborne but attacking without holding down: a normal air attack, not a thrust.
            Step(sim, new InputFrame(Vector2.zero, attack: ButtonState.Press));
            Assert.IsFalse(thrust.IsActive);

            Step(sim, new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press));
            Assert.IsTrue(thrust.IsActive);
            Assert.AreEqual(-tuning.DownThrustSpeed, sim.State.Velocity.y, 0.001f);
        }

        [Test]
        public void DownThrust_HoldsOneSpeedRegardlessOfHowHighItStarted()
        {
            var tuning = Tuning();
            var sim = Player(tuning, Ground(), new Vector2(0f, 20f));
            var thrust = sim.Get<DownThrust>();

            Step(sim, 30);   // build up a real fall first
            Step(sim, new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press));
            Step(sim, Hold(0f, -1f), 10);

            Assert.IsTrue(thrust.IsActive);
            Assert.AreEqual(-tuning.DownThrustSpeed, sim.State.Velocity.y, 0.001f,
                "gravity runs first in the tick, so the dive must be re-asserted or it drifts to terminal velocity");
        }

        [Test]
        public void DownThrust_EndsOnLandingAndReleasesTheCharacter()
        {
            var tuning = Tuning();
            var sim = Player(tuning, Ground(), new Vector2(0f, 6f));
            var thrust = sim.Get<DownThrust>();

            Step(sim, 5);
            Step(sim, new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press));
            Assert.IsTrue(thrust.IsActive);

            Step(sim, Hold(0f, -1f), 40);

            Assert.IsFalse(thrust.IsActive);
            Assert.IsFalse(sim.HoldsLock(thrust), "a finished action must not keep the lock");
            Assert.IsTrue(sim.Can(LockFlags.Move));
        }

        [Test]
        public void DownThrust_CommitsBeforeItCanBeCancelled()
        {
            var tuning = Tuning();
            var sim = Player(tuning, Ground(), new Vector2(0f, 20f));
            var thrust = sim.Get<DownThrust>();

            Step(sim, 5);
            Step(sim, new InputFrame(new Vector2(0f, -1f), attack: ButtonState.Press));

            Assert.IsFalse(sim.CurrentLock.CancelWindowOpen, "the stab commits first");

            Step(sim, Hold(0f, -1f), tuning.DownThrustMinTicks + 1);
            Assert.IsTrue(sim.CurrentLock.CancelWindowOpen, "then it opens up to a reaction");
        }

        // ---------------------------------------------------------------- top-down

        [Test]
        public void TopDownMove_UsesBothAxesAndNeverOutrunsItselfDiagonally()
        {
            var tuning = Tuning();
            var sim = PlayerCharacterFactory.Create(new CollisionWorld(), tuning, MovementSpace.TopDown);
            sim.Teleport(Vector2.zero);

            Step(sim, Hold(1f, 1f), 60);

            Assert.AreEqual(tuning.TopDownSpeed, sim.State.Velocity.magnitude, 0.01f,
                "a diagonal must not be faster than a cardinal");
            Assert.Greater(sim.State.Position.x, 0f);
            Assert.Greater(sim.State.Position.y, 0f);
        }

        [Test]
        public void SideScrollModules_DoNotTickInTheOverworld()
        {
            var tuning = Tuning();
            var sim = PlayerCharacterFactory.Create(new CollisionWorld(), tuning, MovementSpace.TopDown);
            sim.Teleport(Vector2.zero);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press), 30);

            Assert.AreEqual(Vector2.zero, sim.State.Velocity, "no gravity and no jumping on the map");
            Assert.AreEqual(Stance.Stand, sim.State.Stance);
        }

        // ---------------------------------------------------------------- frame rate

        [Test]
        public void Movement_IsIdenticalAt30_60_And144Fps()
        {
            // The sim advances on a fixed tick; the frame rate only decides how many ticks a
            // frame retires. Drive the same 120 ticks through an accumulator at three very
            // different frame rates and the character must end up in exactly the same place —
            // if it does not, something is reading frame time where it should read the tick.
            (Vector2 position, Vector2 velocity, float elapsed) RunAt(float fps)
            {
                var sim = Player(Tuning());

                // Every quantity here is a float, deliberately. Mixing a double accumulator with
                // the sim's float tick length deadlocks at 60 fps: 1.0/60.0 in double is a hair
                // *below* 1f/60f widened, so the accumulator never reaches a whole tick and no
                // tick ever retires. Staying in float keeps the arithmetic self-consistent — and
                // it is what a real driver does anyway.
                float frameDelta = 1f / fps;
                float accumulator = 0f;
                float elapsed = 0f;
                long tick = 0;

                while (tick < 120)
                {
                    accumulator += frameDelta;
                    elapsed += frameDelta;

                    while (accumulator >= Dt && tick < 120)
                    {
                        accumulator -= Dt;
                        tick++;

                        // A scripted input, keyed off the tick so all three runs see the same thing.
                        var jump = tick == 10 ? ButtonState.Press
                                 : tick == 25 ? ButtonState.Release
                                 : tick > 10 && tick < 25 ? ButtonState.Holding
                                 : ButtonState.None;

                        sim.SetInput(new InputFrame(new Vector2(1f, 0f), jump: jump));
                        sim.Tick(new SimTickInfo(tick, Dt));
                    }
                }

                return (sim.State.Position, sim.State.Velocity, elapsed);
            }

            var at30 = RunAt(30f);
            var at60 = RunAt(60f);
            var at144 = RunAt(144f);

            Assert.AreEqual(at60.position, at30.position, "30 fps must not change where the character is");
            Assert.AreEqual(at60.position, at144.position, "and neither must 144");
            Assert.AreEqual(at60.velocity, at30.velocity);
            Assert.AreEqual(at60.velocity, at144.velocity);

            // And the wall clock agrees: 120 ticks is two seconds however it was sliced.
            Assert.AreEqual(2f, at30.elapsed, 1f / 30f);
            Assert.AreEqual(2f, at60.elapsed, 1f / 60f);
            Assert.AreEqual(2f, at144.elapsed, 1f / 60f);
        }

        // ---------------------------------------------------------------- architecture

        /// <summary>A brand-new ability, defined entirely inside this test file. If registering it
        /// required so much as a line in <see cref="GroundMove"/>, the boundary would be broken.</summary>
        private sealed class Newcomer : AbilityModule
        {
            public int Ticks;
            public bool GrabLock;

            public override int Order => 45;

            public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
            {
                Ticks++;
                if (GrabLock) sim.TryLock(this, LockFlags.Move, LockPriority.Attack);
            }
        }

        [Test]
        public void ANewAbility_DropsInWithoutTouchingAnyExistingModule()
        {
            var sim = Player(Tuning());
            var newcomer = sim.Add(new Newcomer());

            Step(sim, Hold(1f), 20);

            Assert.AreEqual(20, newcomer.Ticks, "it ticks purely by being registered");
            Assert.Greater(sim.State.Velocity.x, 0f, "and the abilities already there are unaffected");
        }

        [Test]
        public void ANewAbility_SuppressesMovementThroughTheArbiterAloneAndReleasesItCleanly()
        {
            var tuning = Tuning();
            var sim = Player(tuning);
            var newcomer = sim.Add(new Newcomer());

            Step(sim, Hold(1f), 20);
            Assert.AreEqual(tuning.WalkSpeed, sim.State.Velocity.x, 0.01f);

            newcomer.GrabLock = true;
            Step(sim, Hold(1f), 20);

            Assert.AreEqual(0f, sim.State.Velocity.x, 0.01f,
                "GroundMove has never heard of this module — the lock flags are the entire contract");

            newcomer.GrabLock = false;
            sim.ReleaseLock(newcomer);
            Step(sim, Hold(1f), 20);

            Assert.AreEqual(tuning.WalkSpeed, sim.State.Velocity.x, 0.01f, "and control comes straight back");
        }

        [Test]
        public void TheWholeMovesetIsRegistered_WithUnbuiltAbilitiesDisabled()
        {
            var sim = Player(Tuning());

            Assert.IsNotNull(sim.Get<GravityModule>());
            Assert.IsNotNull(sim.Get<GroundMove>());
            Assert.IsNotNull(sim.Get<TopDownMove>());
            Assert.IsNotNull(sim.Get<Crouch>());
            Assert.IsNotNull(sim.Get<Jump>());
            Assert.IsNotNull(sim.Get<DownThrust>());
            Assert.IsNotNull(sim.Get<Interact>());
            Assert.IsNotNull(sim.Get<FallLand>());

            foreach (var module in sim.Modules)
            {
                if (module is PlannedAbility)
                    Assert.IsFalse(module.Enabled, $"{module.Name} is declared, not built — it must ship off");
                else
                    Assert.IsTrue(module.Enabled, $"{module.Name} is built and should be on");
            }

            Assert.AreEqual(3, CountPlanned(sim),
                "Crawl, DodgeStep and FlameArt are all that remain unbuilt");
        }

        private static int CountPlanned(CharacterSim sim)
        {
            int n = 0;
            for (int i = 0; i < sim.Modules.Count; i++)
                if (sim.Modules[i] is PlannedAbility) n++;
            return n;
        }

        [Test]
        public void ModulesTickInOrderRegardlessOfHowTheyWereRegistered()
        {
            var sim = Player(Tuning());

            int previous = int.MinValue;
            foreach (var module in sim.Modules)
            {
                Assert.GreaterOrEqual(module.Order, previous, "the registry keeps itself sorted");
                previous = module.Order;
            }

            Assert.AreEqual(ModuleOrder.Gravity, sim.Modules[0].Order,
                "gravity runs first so a launch velocity set later in the tick integrates in full");
        }

        // ---------------------------------------------------------------- tuning asset

        [Test]
        public void JumpVelocity_IsDerivedFromHeightNotAuthoredSeparately()
        {
            var tuning = Tuning();
            float expected = Mathf.Sqrt(2f * tuning.RiseGravity * tuning.JumpHeight);

            Assert.AreEqual(expected, tuning.JumpVelocity, 0.0001f);
        }

        [Test]
        public void MaxFallSpeed_DoesNotClampTheDownThrust()
        {
            var tuning = Tuning();
            Assert.GreaterOrEqual(tuning.MaxFallSpeed, tuning.DownThrustSpeed,
                "terminal velocity below the dive speed would silently shorten the move");
        }

        [Test]
        public void TuningIsHeldByReference_SoInspectorEditsApplyMidPlay()
        {
            var tuning = Tuning();
            var sim = Player(tuning);

            Step(sim, Hold(1f), 30);
            Assert.AreEqual(tuning.WalkSpeed, sim.State.Velocity.x, 0.01f);

            tuning.WalkSpeed = 2f;   // stands in for dragging the slider during play mode
            Step(sim, Hold(1f), 30);

            Assert.AreEqual(2f, sim.State.Velocity.x, 0.01f,
                "modules must hold the reference, not a copy taken at construction");
        }
    }
}
