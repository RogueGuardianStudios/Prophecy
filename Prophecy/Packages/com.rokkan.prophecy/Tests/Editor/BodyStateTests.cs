using NUnit.Framework;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
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

        // ---------------------------------------------------------------- speed matching

        /// <summary>A set with one locomotion entry, for exercising the playback maths.</summary>
        private static BodyAnimationSet.Entry Walking() =>
            new BodyAnimationSet.Entry { State = BodyState.Walk, Loop = true, ReferenceSpeed = 2.595f };

        [Test]
        public void PlaybackTracksActualSpeedSoASlowNeedsNoSpecialHandling()
        {
            // The whole reason a speed debuff does not need its own animation path: the multiplier
            // is computed from real velocity, so halving movement halves the stride with it.
            var set = ScriptableObject.CreateInstance<BodyAnimationSet>();

            try
            {
                var entry = Walking();

                float full = set.PlaybackSpeedFor(in entry, 4f);
                float halved = set.PlaybackSpeedFor(in entry, 2f);

                Assert.AreEqual(full * 0.5f, halved, 0.0001f,
                    "a 50% slow must halve the playback, or the feet drift by the difference");
            }
            finally
            {
                Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void NothingInThePlausibleSpeedRangeIsClamped()
        {
            // Clamping IS foot drift — by exactly the ratio clamped away. The old 0.25 floor made
            // a creeping character step four times too fast, and a strong slow parked you there.
            //
            // Each clip is checked against the band IT covers, which is the correction this test
            // needed: 15 m/s against the walk clip is not a case, because above the run threshold
            // the resolver has already switched to the sprint clip. Testing a clip outside its own
            // band measures nothing.
            var set = ScriptableObject.CreateInstance<BodyAnimationSet>();

            try
            {
                var walk = new BodyAnimationSet.Entry { ReferenceSpeed = 2.595f };
                var run = new BodyAnimationSet.Entry { ReferenceSpeed = 7.271f };

                // Walk covers the move threshold up to the run threshold, and every debuffed speed
                // in between.
                AssertUnclamped(set, walk, BodyStateResolver.DefaultMoveThreshold);
                AssertUnclamped(set, walk, 0.4f);
                AssertUnclamped(set, walk, 2f);
                AssertUnclamped(set, walk, BodyStateResolver.DefaultRunThreshold);

                // Run covers the run threshold upward, including a doubled run.
                AssertUnclamped(set, run, BodyStateResolver.DefaultRunThreshold);
                AssertUnclamped(set, run, 7.5f);
                AssertUnclamped(set, run, 15f);
            }
            finally
            {
                Object.DestroyImmediate(set);
            }
        }

        private static void AssertUnclamped(BodyAnimationSet set, BodyAnimationSet.Entry entry,
                                            float speed)
        {
            float honest = speed / entry.ReferenceSpeed;
            float actual = set.PlaybackSpeedFor(in entry, speed);

            Assert.AreEqual(honest, actual, 0.0001f,
                $"{speed} m/s on a {entry.ReferenceSpeed} m/s clip was clamped from {honest:0.###} " +
                $"to {actual:0.###} — the feet slide by that ratio");
        }

        [Test]
        public void AClipWithNoReferenceSpeedIsNeverScaled()
        {
            // Scaling a sword swing by how fast you happened to be walking is nonsense.
            var set = ScriptableObject.CreateInstance<BodyAnimationSet>();

            try
            {
                var attack = new BodyAnimationSet.Entry { State = BodyState.AttackStandA, ReferenceSpeed = 0f };

                Assert.AreEqual(1f, set.PlaybackSpeedFor(in attack, 0f), 0.0001f);
                Assert.AreEqual(1f, set.PlaybackSpeedFor(in attack, 9f), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(set);
            }
        }

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
        public void LandingAtAStandstillBeatsTheIdleItResolvesInto()
        {
            var input = Standing();
            input.LandedThisTick = true;

            Assert.AreEqual(BodyState.Land, Resolve(input));

            input.LandedThisTick = false;
            Assert.AreEqual(BodyState.Idle, Resolve(input), "and it ends when the landing does");
        }

        [Test]
        public void LandingWhileStillMovingGoesStraightToTheGait()
        {
            // The clips are Land_Idle* — landings into a standstill, with no running equivalent in
            // the pack. Held over a body still travelling they settle the feet while the character
            // keeps moving, which is a skate. Landing out of a run must show the run.
            var input = Standing();
            input.LandedThisTick = true;

            input.Velocity = new Vector2(1.5f, 0f);
            Assert.AreEqual(BodyState.Walk, Resolve(input), "a landing must not stop a walk");

            input.Velocity = new Vector2(input.RunThreshold + 1f, 0f);
            Assert.AreEqual(BodyState.Run, Resolve(input), "nor a run");
        }

        [Test]
        public void ALandingInterruptedByMovingEndsImmediately()
        {
            // The landing pose is held for a moment after touchdown, so pressing a direction
            // during it has to cut it short rather than wait the hold out.
            var input = Standing();
            input.LandedThisTick = true;

            Assert.AreEqual(BodyState.Land, Resolve(input));

            input.Velocity = new Vector2(2f, 0f);
            Assert.AreEqual(BodyState.Walk, Resolve(input));
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
            // code. The expectations are read off the authored moveset itself, so renaming an
            // attack or growing the roster is content work rather than a test edit — the rule
            // being pinned is about the SHAPE of the mapping, not yesterday's ids.
            var combat = new CombatTuningData();
            var input = Standing();

            BodyState StateOf(string id)
            {
                input.Stance = combat.Moveset[id].RequiredStance;
                input.AttackId = id;
                return Resolve(input);
            }

            int chains = 0;
            foreach (var attack in combat.Attacks)
            {
                if (string.IsNullOrEmpty(attack.ChainsInto)) continue;

                chains++;
                Assert.AreNotEqual(StateOf(attack.Id), StateOf(attack.ChainsInto),
                    $"'{attack.ChainsInto}' replays '{attack.Id}''s clip — the chain link must " +
                    "not reuse the opener's");
            }

            Assert.Greater(chains, 0, "the moveset must author a combo for this to pin anything");

            Assert.AreNotEqual(StateOf(combat.StandingAttackId), StateOf(combat.CrouchingAttackId),
                "the low stab must not share the standing slash's pose");
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
