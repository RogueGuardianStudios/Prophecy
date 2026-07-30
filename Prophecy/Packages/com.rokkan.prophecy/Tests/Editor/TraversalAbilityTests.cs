using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The traversal moveset: double jump, wall slide and wall jump, ledge hang and pull-up,
    /// ladder climbing — plus the loadout that switches any of them off.
    ///
    /// <para>These are the abilities most likely to interfere with each other, because they all
    /// answer the same button. The interesting assertions here are not "does the move work" but
    /// "does one press do exactly one thing" — a jump next to a wall must not also spend an air
    /// jump, and a jump on a ladder must not do all three.</para>
    /// </summary>
    public class TraversalAbilityTests
    {
        private const float Dt = 1f / 60f;

        private static MovementTuningData Tuning() => new MovementTuningData();

        private static CollisionWorld Ground(float top = 0f)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(-500f, top - 2f), new Vector2(500f, top)));
            return world;
        }

        private static CharacterSim Player(MovementTuningData tuning, CollisionWorld world,
                                           Vector2 at = default, AbilityLoadoutData loadout = null)
        {
            var sim = PlayerCharacterFactory.Create(world, tuning, MovementSpace.SideScroll, loadout);
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

        // ---------------------------------------------------------------- double jump

        [Test]
        public void DoubleJump_GivesASecondJumpInMidAir()
        {
            var tuning = Tuning();
            var sim = Player(tuning, Ground());

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), tuning.AirJumpArmTicks + 2);

            float beforeSecond = sim.State.Velocity.y;
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            Assert.Greater(sim.State.Velocity.y, beforeSecond, "the second press must relaunch");
            Assert.AreEqual(tuning.AirJumpVelocity, sim.State.Velocity.y, 0.001f);
        }

        [Test]
        public void DoubleJump_IsNotSpentByTheSamePressAsTheGroundJump()
        {
            // Both modules see the same press on the same tick, and the character is airborne one
            // tick later. Without the arming delay a single tap burns both jumps.
            var tuning = Tuning();
            var sim = Player(tuning, Ground());
            var doubleJump = sim.Get<DoubleJump>();

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            Assert.AreEqual(tuning.AirJumps, doubleJump.Remaining, "one tap, one jump");
        }

        [Test]
        public void DoubleJump_DoesNotStackBeyondItsAllowance()
        {
            var tuning = Tuning();
            var sim = Player(tuning, Ground());

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            for (int i = 0; i < tuning.AirJumps + 2; i++)
            {
                Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), tuning.AirJumpArmTicks + 2);
                Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            }

            Assert.LessOrEqual(sim.Get<DoubleJump>().Remaining, 0, "the allowance is the resource");
        }

        [Test]
        public void DoubleJump_RearmsOnLanding()
        {
            var tuning = Tuning();
            var sim = Player(tuning, Ground());
            var doubleJump = sim.Get<DoubleJump>();

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), tuning.AirJumpArmTicks + 2);
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Assert.Less(doubleJump.Remaining, tuning.AirJumps);

            for (int i = 0; i < 400 && !sim.State.Grounded; i++) Step(sim);
            Step(sim);

            Assert.AreEqual(tuning.AirJumps, doubleJump.Remaining, "ground is what refills it");
        }

        // ---------------------------------------------------------------- walls

        /// <summary>Ground, plus a tall wall the character can be pressed against.</summary>
        private static CollisionWorld WalledShaft(float wallX = 2f)
        {
            var world = Ground();
            world.Add(new Aabb(new Vector2(wallX, 0f), new Vector2(wallX + 2f, 20f)));
            return world;
        }

        private static CharacterSim AirborneAgainstWall(MovementTuningData tuning, out CollisionWorld world)
        {
            world = WalledShaft();
            var sim = Player(tuning, world, new Vector2(1.5f, 6f));

            // Fall while pressing into the wall until the slide engages.
            Step(sim, Hold(1f), 30);
            return sim;
        }

        [Test]
        public void WallSlide_ClampsTheFallWhilePressedIntoAWall()
        {
            var tuning = Tuning();
            var sim = AirborneAgainstWall(tuning, out _);

            Assert.IsTrue(sim.Get<WallSlide>().IsSliding, "should be scraping the wall");
            Assert.GreaterOrEqual(sim.State.Velocity.y, -tuning.WallSlideSpeed - 0.001f,
                "a slide is much slower than a fall");
        }

        [Test]
        public void WallSlide_NeedsInputTowardTheWall()
        {
            var tuning = Tuning();
            var world = WalledShaft();
            var sim = Player(tuning, world, new Vector2(1.5f, 6f));

            Step(sim, Hold(0f), 20);   // falling past the wall, not pressing into it

            Assert.IsFalse(sim.Get<WallSlide>().IsSliding);
            Assert.Less(sim.State.Velocity.y, -tuning.WallSlideSpeed,
                "brushing a wall must not halve your fall speed");
        }

        [Test]
        public void WallJump_LaunchesUpAndAwayAndFlipsFacing()
        {
            var tuning = Tuning();
            var sim = AirborneAgainstWall(tuning, out _);

            Step(sim, new InputFrame(new Vector2(1f, 0f), jump: ButtonState.Press));

            Assert.AreEqual(tuning.WallJumpVelocity, sim.State.Velocity.y, 0.001f);
            Assert.Less(sim.State.Velocity.x, 0f, "away from the wall");
            Assert.AreEqual(-1, sim.State.Facing, "and looking where you are going");
        }

        [Test]
        public void WallJump_SuppressesSteeringSoTheJumpActuallyLeaves()
        {
            // The player is holding toward the wall — that is how they got there. Without the
            // control lock their own input drags them straight back and they climb forever.
            var tuning = Tuning();
            var sim = AirborneAgainstWall(tuning, out _);

            Step(sim, new InputFrame(new Vector2(1f, 0f), jump: ButtonState.Press));
            Assert.IsFalse(sim.Can(LockFlags.Move), "steering is locked out immediately after the kick");

            Step(sim, Hold(1f), 3);
            Assert.Less(sim.State.Velocity.x, 0f, "still travelling away despite holding into the wall");
        }

        [Test]
        public void AJumpAtAWall_IsAWallJumpAndNotAnAirJump()
        {
            var tuning = Tuning();
            var sim = AirborneAgainstWall(tuning, out _);
            var doubleJump = sim.Get<DoubleJump>();

            Step(sim, new InputFrame(new Vector2(1f, 0f), jump: ButtonState.Press));

            Assert.AreEqual(tuning.AirJumps, doubleJump.Remaining,
                "one press must not spend two abilities");
        }

        // ---------------------------------------------------------------- ledges

        /// <summary>A platform whose left face presents a grabbable ledge.</summary>
        private static CollisionWorld LedgeAt(float faceX, float topY)
        {
            var world = Ground(-30f);
            world.Add(new Aabb(new Vector2(faceX, topY - 10f), new Vector2(faceX + 8f, topY)));
            return world;
        }

        private static CharacterSim HangingOnLedge(MovementTuningData tuning)
        {
            const float faceX = 2f;
            const float topY = 6f;

            var world = LedgeAt(faceX, topY);

            // Start level with the surface, just clear of the face, falling and facing it.
            var start = new Vector2(faceX - tuning.BodyWidth * 0.5f - 0.1f, topY - 0.2f);
            var sim = Player(tuning, world, start);
            sim.State.Facing = 1;

            for (int i = 0; i < 120 && sim.State.Attachment != AttachmentKind.Ledge; i++)
                Step(sim, Hold(1f));

            return sim;
        }

        [Test]
        public void LedgeHang_CatchesAnEdgeAndHoldsStill()
        {
            var tuning = Tuning();
            var sim = HangingOnLedge(tuning);

            Assert.AreEqual(AttachmentKind.Ledge, sim.State.Attachment, "should have caught the ledge");

            var held = sim.State.Position;
            Step(sim, Hold(1f), 30);

            Assert.AreEqual(held, sim.State.Position, "a hang must not drift");
            Assert.AreEqual(Vector2.zero, sim.State.Velocity);
        }

        [Test]
        public void LedgeHang_ReleasesOnDownAndDoesNotImmediatelyRegrab()
        {
            var tuning = Tuning();
            var sim = HangingOnLedge(tuning);
            float hangY = sim.State.Position.y;

            Step(sim, Hold(0f, -1f), 20);

            Assert.AreEqual(AttachmentKind.None, sim.State.Attachment);
            Assert.Less(sim.State.Position.y, hangY, "letting go means going down");
        }

        [Test]
        public void LedgePullUp_ClimbsOntoTheSurface()
        {
            var tuning = Tuning();
            var sim = HangingOnLedge(tuning);
            float hangY = sim.State.Position.y;

            Step(sim, Hold(0f, 1f), tuning.LedgePullUpTicks + 6);

            Assert.AreEqual(AttachmentKind.None, sim.State.Attachment);
            Assert.Greater(sim.State.Position.y, hangY + 1f, "should end up above where it hung");
            Assert.IsTrue(sim.Can(LockFlags.Move), "and hand control back");
        }

        [Test]
        public void LedgeHang_StillWorksWithPullUpSwitchedOff()
        {
            // The point of them being separate abilities: hanging without climbing is a real,
            // usable moveset rather than a broken one.
            var tuning = Tuning();
            var loadout = new AbilityLoadoutData();
            loadout.Set(AbilityId.LedgePullUp, false);

            const float faceX = 2f;
            const float topY = 6f;
            var world = LedgeAt(faceX, topY);
            var start = new Vector2(faceX - tuning.BodyWidth * 0.5f - 0.1f, topY - 0.2f);

            var sim = Player(tuning, world, start, loadout);
            sim.State.Facing = 1;

            for (int i = 0; i < 120 && sim.State.Attachment != AttachmentKind.Ledge; i++)
                Step(sim, Hold(1f));

            Assert.AreEqual(AttachmentKind.Ledge, sim.State.Attachment, "hanging still works");

            float hangY = sim.State.Position.y;
            Step(sim, Hold(0f, 1f), tuning.LedgePullUpTicks + 10);

            Assert.AreEqual(AttachmentKind.Ledge, sim.State.Attachment, "but there is no climbing up");
            Assert.AreEqual(hangY, sim.State.Position.y, 0.0001f);
        }

        // ---------------------------------------------------------------- ladders

        private static CharacterSim OnLadder(MovementTuningData tuning, out CollisionWorld world)
        {
            world = Ground();
            world.AddClimbable(new Aabb(new Vector2(-0.6f, 0f), new Vector2(0.6f, 12f)));

            var sim = Player(tuning, world);
            Step(sim, Hold(0f, 1f), 2);
            return sim;
        }

        [Test]
        public void LadderClimb_MountsAndClimbs()
        {
            var tuning = Tuning();
            var sim = OnLadder(tuning, out _);

            Assert.AreEqual(AttachmentKind.Ladder, sim.State.Attachment);

            float startY = sim.State.Position.y;
            Step(sim, Hold(0f, 1f), 30);

            Assert.Greater(sim.State.Position.y, startY + 1f, "up is up");
            Assert.AreEqual(tuning.ClimbSpeed, sim.State.Velocity.y, 0.001f);
        }

        [Test]
        public void LadderClimb_HoldsPositionWithNoInput()
        {
            // Gravity is never switched off — this module just writes the velocity it wants,
            // later in the tick. If that were not true, the character would sag.
            var tuning = Tuning();
            var sim = OnLadder(tuning, out _);

            Step(sim, Hold(0f, 1f), 30);
            float restY = sim.State.Position.y;

            Step(sim, Hold(0f, 0f), 30);

            Assert.AreEqual(restY, sim.State.Position.y, 0.001f, "a ladder holds you where you stopped");
        }

        [Test]
        public void LadderClimb_JumpsOffRatherThanDoubleJumping()
        {
            var tuning = Tuning();
            var sim = OnLadder(tuning, out _);
            Step(sim, Hold(0f, 1f), 20);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            Assert.AreEqual(AttachmentKind.None, sim.State.Attachment, "jumping leaves the ladder");
            Assert.AreEqual(tuning.ClimbDismountJumpVelocity, sim.State.Velocity.y, 0.001f,
                "a dismount hop, not a full jump and not an air jump");
        }

        [Test]
        public void LadderClimb_DoesNotBlockWalkingPast()
        {
            var tuning = Tuning();
            var world = Ground();
            world.AddClimbable(new Aabb(new Vector2(-0.6f, 0f), new Vector2(0.6f, 12f)));

            var sim = Player(tuning, world, new Vector2(-4f, 0f));
            Step(sim, Hold(1f), 120);

            // Clear of the ladder's far edge (0.6) plus half a body, so this is genuinely "walked
            // past" rather than "stopped somewhere beyond the middle".
            Assert.Greater(sim.State.Position.x, 1.5f,
                "a climbable is not a solid — walking through one must just work");
            Assert.AreEqual(AttachmentKind.None, sim.State.Attachment);
        }

        // ---------------------------------------------------------------- platforms

        /// <summary>Ground far below, with a platform overhead at <paramref name="platformY"/>.</summary>
        private static CollisionWorld PlatformOver(float platformY, bool droppable)
        {
            var world = Ground(-10f);
            world.Add(new Aabb(new Vector2(-8f, platformY), new Vector2(8f, platformY + 0.3f)),
                      SolidKind.OneWay, droppable);
            return world;
        }

        private static CharacterSim StandingOnPlatform(MovementTuningData tuning, bool droppable)
        {
            const float platformY = 4f;
            var world = PlatformOver(platformY, droppable);

            var sim = Player(tuning, world, new Vector2(0f, platformY + 0.3f));
            Step(sim, 5);
            return sim;
        }

        [Test]
        public void HoldingDown_DropsThroughAPassThroughPlatform()
        {
            var tuning = Tuning();
            var sim = StandingOnPlatform(tuning, droppable: true);

            Assert.IsTrue(sim.State.Grounded, "should start on the platform");
            float startY = sim.State.Position.y;

            Step(sim, Hold(0f, -1f), tuning.DropThroughHoldTicks + 20);

            Assert.Less(sim.State.Position.y, startY - 1f, "should have fallen through it");
        }

        [Test]
        public void ATapOfDown_CrouchesRatherThanDropping()
        {
            // Down is also crouch, and crouching on a platform is much the more common intent.
            // A drop on the press would make crouching near an edge feel random.
            var tuning = Tuning();
            var sim = StandingOnPlatform(tuning, droppable: true);
            float startY = sim.State.Position.y;

            Step(sim, Hold(0f, -1f), tuning.DropThroughHoldTicks - 2);

            Assert.AreEqual(Stance.Crouch, sim.State.Stance);
            Assert.AreEqual(startY, sim.State.Position.y, 0.01f, "still standing on it");
        }

        [Test]
        public void AStrictOneWayPlatform_CannotBeDroppedThrough()
        {
            var tuning = Tuning();
            var sim = StandingOnPlatform(tuning, droppable: false);
            float startY = sim.State.Position.y;

            Step(sim, Hold(0f, -1f), tuning.DropThroughHoldTicks + 30);

            Assert.AreEqual(startY, sim.State.Position.y, 0.01f,
                "the sealed floor of an area must not be escapable by crouching");
            Assert.IsTrue(sim.State.Grounded);
        }

        [Test]
        public void APassThroughPlatform_IsStillEnteredFromBelow()
        {
            var tuning = Tuning();

            // Platform within jump reach of the floor, or this tests nothing but gravity.
            var world = Ground();
            world.Add(new Aabb(new Vector2(-8f, 2f), new Vector2(8f, 2.3f)), SolidKind.OneWay, true);

            var sim = Player(tuning, world);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), 120);

            Assert.IsTrue(sim.State.Grounded);
            Assert.GreaterOrEqual(sim.State.Position.y, 2.3f - 0.01f,
                "jumped up through it and landed on top");
        }

        // ---------------------------------------------------------------- ropes

        [Test]
        public void ARope_CanBeMountedDownwardFromThePlatformItHangsFrom()
        {
            // The rope's defining behaviour: it hangs *through* its platform, so holding down on
            // the platform puts you on the rope rather than dropping you off it.
            var tuning = Tuning();
            const float platformY = 6f;

            var world = Ground(-20f);
            world.Add(new Aabb(new Vector2(-8f, platformY), new Vector2(8f, platformY + 0.3f)),
                      SolidKind.OneWay, true);
            world.AddClimbable(new Aabb(new Vector2(-0.6f, 1f), new Vector2(0.6f, platformY + 0.3f)),
                               ClimbableKind.Rope);

            var sim = Player(tuning, world, new Vector2(0f, platformY + 0.3f));
            Step(sim, 5);
            Assert.IsTrue(sim.State.Grounded, "starts on the platform");

            Step(sim, Hold(0f, -1f), 3);

            Assert.AreEqual(AttachmentKind.Ladder, sim.State.Attachment, "should be on the rope");

            float mountY = sim.State.Position.y;
            Step(sim, Hold(0f, -1f), 30);
            Assert.Less(sim.State.Position.y, mountY - 0.5f, "and descending through the platform");
        }

        [Test]
        public void AWallLadder_IsNotMountedByCrouchingAtItsFoot()
        {
            // The mirror case: at the base of a ladder there is nothing below to descend onto, so
            // holding down must just crouch.
            var tuning = Tuning();
            var world = Ground();
            world.AddClimbable(new Aabb(new Vector2(-0.6f, 0f), new Vector2(0.6f, 10f)),
                               ClimbableKind.Ladder);

            var sim = Player(tuning, world);
            Step(sim, Hold(0f, -1f), 20);

            Assert.AreEqual(AttachmentKind.None, sim.State.Attachment);
            Assert.AreEqual(Stance.Crouch, sim.State.Stance);
        }

        [Test]
        public void SteppingOffTheTopOfARope_DoesNotFallBackThroughThePlatform()
        {
            var tuning = Tuning();
            const float platformY = 6f;

            var world = Ground(-20f);
            world.Add(new Aabb(new Vector2(-8f, platformY), new Vector2(8f, platformY + 0.3f)),
                      SolidKind.OneWay, true);
            world.AddClimbable(new Aabb(new Vector2(-0.6f, 1f), new Vector2(0.6f, platformY + 0.9f)),
                               ClimbableKind.Rope);

            var sim = Player(tuning, world, new Vector2(0f, 1.5f));
            Step(sim, Hold(0f, 1f), 200);

            Assert.AreEqual(AttachmentKind.None, sim.State.Attachment, "climbed off the top");
            Assert.IsFalse(sim.State.DropThrough, "and the pass-through permission was handed back");
            Assert.Greater(sim.State.Position.y, platformY, "standing on the platform, not under it");
        }

        // ---------------------------------------------------------------- loadout

        [Test]
        public void Loadout_SwitchesAnAbilityOff()
        {
            var tuning = Tuning();
            var loadout = new AbilityLoadoutData();
            loadout.Set(AbilityId.DoubleJump, false);

            var sim = Player(tuning, Ground(), Vector2.zero, loadout);

            Assert.IsFalse(sim.Get<DoubleJump>().Enabled);

            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Holding), tuning.AirJumpArmTicks + 2);

            float before = sim.State.Velocity.y;
            Step(sim, new InputFrame(Vector2.zero, jump: ButtonState.Press));

            Assert.Less(sim.State.Velocity.y, before, "a disabled ability does nothing at all");
        }

        [Test]
        public void Loadout_LeavesAbilitiesItHasNoOpinionAboutAlone()
        {
            // An empty loadout must not read as "everything off", or every ability added after an
            // asset was authored would silently vanish.
            var sim = Player(Tuning(), Ground(), Vector2.zero, new AbilityLoadoutData());

            Assert.IsTrue(sim.Get<Jump>().Enabled);
            Assert.IsTrue(sim.Get<GroundMove>().Enabled);
            Assert.IsTrue(sim.Get<DodgeStep>().Enabled, "including ones added since it was authored");
            Assert.IsFalse(sim.Get<Crawl>().Enabled, "and unbuilt ones stay off");
        }

        [Test]
        public void Loadout_CanBeReappliedToToggleAnAbilityMidPlay()
        {
            var loadout = new AbilityLoadoutData();
            var sim = Player(Tuning(), Ground(), Vector2.zero, loadout);

            loadout.Set(AbilityId.Jump, false);
            loadout.Apply(sim);
            Assert.IsFalse(sim.Get<Jump>().Enabled);

            loadout.Set(AbilityId.Jump, true);
            loadout.Apply(sim);
            Assert.IsTrue(sim.Get<Jump>().Enabled);
        }

        [Test]
        public void EveryShippedAbilityDeclaresAUniqueId()
        {
            // Ids are what a save file stores. A duplicate or a forgotten override would make one
            // ability's unlock silently control another's.
            var sim = Player(Tuning(), Ground());
            var seen = new System.Collections.Generic.HashSet<AbilityId>();

            foreach (var module in sim.Modules)
            {
                Assert.AreNotEqual(AbilityId.Custom, module.Id,
                    $"{module.Name} never declared an AbilityId");
                Assert.IsTrue(seen.Add(module.Id), $"{module.Id} is claimed by more than one module");
            }
        }

        // ---------------------------------------------------------------- run modes

        [Test]
        public void RunMode_Hold_RunsOnlyWhileTheButtonIsDown()
        {
            var tuning = Tuning();
            tuning.RunMode = RunMode.Hold;
            var sim = Player(tuning, Ground());

            Step(sim, new InputFrame(new Vector2(1f, 0f), runToggle: ButtonState.Holding), 40);
            Assert.AreEqual(tuning.RunSpeed, sim.State.Velocity.x, 0.01f);

            Step(sim, Hold(1f), 40);
            Assert.AreEqual(tuning.WalkSpeed, sim.State.Velocity.x, 0.01f,
                "releasing drops straight back to a walk");
        }
    }
}
