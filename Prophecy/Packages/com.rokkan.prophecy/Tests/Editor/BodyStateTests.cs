using NUnit.Framework;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// What the body is seen doing, given what the simulation says is true.
    ///
    /// <para>The assertions that matter here are the <i>precedence</i> ones. Any single mapping is
    /// obvious and would be caught the first time anyone looked at the character; the ordering
    /// bugs are the ones that survive, because they only appear when two things are true at once —
    /// hit during a swing, guarding on a ladder — and then the picture is quietly wrong rather
    /// than broken.</para>
    /// </summary>
    public class BodyStateTests
    {
        private static BodyStateInputs Standing()
        {
            return new BodyStateInputs
            {
                Alive = true,
                Grounded = true,
                Stance = Stance.Stand,
                Velocity = Vector2.zero,
                Attachment = AttachmentKind.None,
                RunThreshold = BodyStateResolver.DefaultRunThreshold,
                MoveThreshold = BodyStateResolver.DefaultMoveThreshold,
            };
        }

        private static BodyState Resolve(BodyStateInputs input) =>
            BodyStateResolver.Resolve(in input);

        // ---------------------------------------------------------------- ground

        [Test]
        public void StandingStillIsIdle()
        {
            Assert.AreEqual(BodyState.Idle, Resolve(Standing()));
        }

        [Test]
        public void WalkBecomesRunAtTheThreshold()
        {
            var input = Standing();

            input.Velocity = new Vector2(input.RunThreshold - 0.1f, 0f);
            Assert.AreEqual(BodyState.Walk, Resolve(input));

            input.Velocity = new Vector2(input.RunThreshold, 0f);
            Assert.AreEqual(BodyState.Run, Resolve(input), "the threshold itself should run");
        }

        [Test]
        public void SpeedIsReadWithoutRegardToDirection()
        {
            // Facing is a separate concern — the body is mirrored, not given a second clip. A
            // signed comparison here would leave everything walking backwards look idle.
            var input = Standing();
            input.Velocity = new Vector2(-(input.RunThreshold + 1f), 0f);

            Assert.AreEqual(BodyState.Run, Resolve(input));
        }

        [Test]
        public void CrouchHasItsOwnIdleAndWalk()
        {
            var input = Standing();
            input.Stance = Stance.Crouch;

            Assert.AreEqual(BodyState.CrouchIdle, Resolve(input));

            input.Velocity = new Vector2(1f, 0f);
            Assert.AreEqual(BodyState.CrouchWalk, Resolve(input));
        }

        // ---------------------------------------------------------------- air

        [Test]
        public void TheApexSplitsRiseFromFall()
        {
            var input = Standing();
            input.Grounded = false;

            input.Velocity = new Vector2(0f, 5f);
            Assert.AreEqual(BodyState.JumpRise, Resolve(input));

            input.Velocity = new Vector2(0f, -5f);
            Assert.AreEqual(BodyState.Fall, Resolve(input));
        }

        [Test]
        public void LandingIsEdgeTriggeredAndBeatsTheIdleItResolvesInto()
        {
            var input = Standing();
            input.LandedThisTick = true;

            Assert.AreEqual(BodyState.Land, Resolve(input));

            input.LandedThisTick = false;
            Assert.AreEqual(BodyState.Idle, Resolve(input), "and it lasts exactly one tick");
        }

        [Test]
        public void AWallSlideBeatsAnOrdinaryFall()
        {
            var input = Standing();
            input.Grounded = false;
            input.Velocity = new Vector2(0f, -2f);
            input.WallSliding = true;

            Assert.AreEqual(BodyState.WallSlide, Resolve(input));
        }

        // ---------------------------------------------------------------- attachments

        [Test]
        public void ALadderIsIdleUntilItIsClimbed()
        {
            var input = Standing();
            input.Grounded = false;
            input.Attachment = AttachmentKind.Ladder;

            Assert.AreEqual(BodyState.LadderIdle, Resolve(input));

            input.Velocity = new Vector2(0f, 1.5f);
            Assert.AreEqual(BodyState.LadderClimb, Resolve(input));
        }

        [Test]
        public void TheClimbOutlivesTheAttachmentItStartedFrom()
        {
            // LedgePullUp clears Attachment on its first tick and then lerps for eleven more. Key
            // only off the attachment and the character drops into a fall pose halfway up a wall
            // they are demonstrably still climbing.
            var input = Standing();
            input.Grounded = false;
            input.Attachment = AttachmentKind.None;
            input.ClimbingLedge = true;
            input.Velocity = new Vector2(0f, -3f);

            Assert.AreEqual(BodyState.LedgeClimb, Resolve(input));
        }

        // ---------------------------------------------------------------- precedence

        [Test]
        public void BeingHitBeatsEverythingVoluntary()
        {
            var input = Standing();
            input.InHitReact = true;
            input.AttackId = "slash_high";
            input.Dodging = true;
            input.Guarding = true;

            Assert.AreEqual(BodyState.HitReact, Resolve(input),
                "the sim force-locks a hit-react through all of these, so the picture must agree");
        }

        [Test]
        public void DeathBeatsBeingHit()
        {
            var input = Standing();
            input.Alive = false;
            input.InHitReact = true;

            Assert.AreEqual(BodyState.Death, Resolve(input));
        }

        [Test]
        public void AnAttackBeatsTheMovementUnderneathIt()
        {
            var input = Standing();
            input.Velocity = new Vector2(6f, 0f);
            input.AttackId = "slash_high";

            Assert.AreEqual(BodyState.AttackStandA, Resolve(input),
                "sliding along in a run while swinging is the tell that this ordering is wrong");
        }

        [Test]
        public void TheDiveBeatsTheAttackItIs()
        {
            var input = Standing();
            input.Grounded = false;
            input.DownThrusting = true;
            input.AttackId = "down_thrust";

            Assert.AreEqual(BodyState.DownThrust, Resolve(input));
        }

        [Test]
        public void GuardBecomesParryOnlyWhileTheWindowIsOpen()
        {
            // One button, one module, and the pose is picked the same way the damage gate is.
            var input = Standing();
            input.Guarding = true;

            Assert.AreEqual(BodyState.Block, Resolve(input));

            input.ParryWindowOpen = true;
            Assert.AreEqual(BodyState.Parry, Resolve(input));
        }

        [Test]
        public void ADodgeBeatsAGuardHeldThroughIt()
        {
            var input = Standing();
            input.Guarding = true;
            input.Dodging = true;

            Assert.AreEqual(BodyState.Dodge, Resolve(input),
                "the dodge outranks the guard in the sim, so it must here too");
        }

        // ---------------------------------------------------------------- moveset coverage

        [Test]
        public void EveryAuthoredAttackIdMapsToItsOwnState()
        {
            // The combo is the case worth pinning: a follow-up that replays the opener's clip
            // reads as a stutter rather than a chain, and nothing about it looks like a bug in
            // code.
            var input = Standing();

            input.AttackId = "slash_high";
            Assert.AreEqual(BodyState.AttackStandA, Resolve(input));

            input.AttackId = "slash_high_2";
            Assert.AreEqual(BodyState.AttackStandB, Resolve(input));
            Assert.AreNotEqual(Resolve(input), BodyState.AttackStandA,
                "the chain link must not reuse the opener's clip");

            input.AttackId = "thrust_low";
            Assert.AreEqual(BodyState.AttackCrouch, Resolve(input));
        }

        [Test]
        public void AnUnmappedAttackStillSwingsSomething()
        {
            // Adding an attack to CombatTuning without touching the resolver should degrade to a
            // wrong-but-present swing, never to an idle pose while an attack is plainly running.
            var input = Standing();
            input.AttackId = "some_new_move_nobody_mapped";

            Assert.AreEqual(BodyState.AttackStandA, Resolve(input));

            input.Stance = Stance.Crouch;
            Assert.AreEqual(BodyState.AttackCrouch, Resolve(input));
        }
    }
}
