using NUnit.Framework;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The combat timing spine, stepped one tick at a time with no scene, no animation and no
    /// frame rate.
    ///
    /// <para>Everything combat does hangs off these windows being right, and every one of them is
    /// invisible in play — a parry that opens a tick late is indistinguishable from a player who
    /// pressed a tick late. So the edges get pinned here rather than discovered in a playtest.</para>
    /// </summary>
    public class AttackTimelineTests
    {
        private static AttackDefinition Slash()
        {
            return new AttackDefinition
            {
                Id = "slash",
                StartupTicks = 8,
                ActiveTicks = 6,
                RecoveryTicks = 10,
                HitBoxes = new[]
                {
                    new AttackHitBox
                    {
                        Window = new TickRange(9, 12),
                        Offset = new Vector2(0.8f, 1f),
                        HalfExtents = new Vector2(0.6f, 0.4f),
                        Damage = 10,
                    },
                },
            };
        }

        /// <summary>Step the timeline to a given elapsed tick.</summary>
        private static void AdvanceTo(AttackTimeline timeline, int tick)
        {
            while (timeline.ElapsedTicks < tick) timeline.Advance();
        }

        // ---------------------------------------------------------------- phases

        [Test]
        public void PhasesTileTheActionExactly()
        {
            var definition = Slash();

            Assert.AreEqual(24, definition.TotalTicks);
            Assert.AreEqual(new TickRange(0, 8), definition.StartupRange);
            Assert.AreEqual(new TickRange(8, 14), definition.ActiveRange);
            Assert.AreEqual(new TickRange(14, 24), definition.RecoveryRange);

            // No gap and no overlap: each phase ends exactly where the next begins.
            Assert.AreEqual(definition.StartupRange.End, definition.ActiveRange.Start);
            Assert.AreEqual(definition.ActiveRange.End, definition.RecoveryRange.Start);
        }

        [Test]
        public void TheTimelineWalksStartupThenActiveThenRecovery()
        {
            var timeline = new AttackTimeline();
            timeline.Arm(Slash());

            Assert.AreEqual(AttackTimeline.Phase.Idle, timeline.CurrentPhase, "armed but not yet ticked");

            AdvanceTo(timeline, 0);
            Assert.AreEqual(AttackTimeline.Phase.Startup, timeline.CurrentPhase);

            AdvanceTo(timeline, 7);
            Assert.AreEqual(AttackTimeline.Phase.Startup, timeline.CurrentPhase, "last startup tick");

            AdvanceTo(timeline, 8);
            Assert.AreEqual(AttackTimeline.Phase.Active, timeline.CurrentPhase, "first active tick");

            AdvanceTo(timeline, 13);
            Assert.AreEqual(AttackTimeline.Phase.Active, timeline.CurrentPhase, "last active tick");

            AdvanceTo(timeline, 14);
            Assert.AreEqual(AttackTimeline.Phase.Recovery, timeline.CurrentPhase);

            AdvanceTo(timeline, 23);
            Assert.AreEqual(AttackTimeline.Phase.Recovery, timeline.CurrentPhase, "last tick of the move");
            Assert.IsFalse(timeline.IsComplete);

            AdvanceTo(timeline, 24);
            Assert.IsTrue(timeline.IsComplete, "the action has run its length");
        }

        // ---------------------------------------------------------------- hits

        [Test]
        public void AHitBoxIsLiveOnlyInsideItsWindow()
        {
            var timeline = new AttackTimeline();
            timeline.Arm(Slash());

            AdvanceTo(timeline, 8);
            Assert.IsFalse(timeline.IsHitBoxLive(0), "active phase has begun but the box opens at 9");

            AdvanceTo(timeline, 9);
            Assert.IsTrue(timeline.IsHitBoxLive(0));

            AdvanceTo(timeline, 11);
            Assert.IsTrue(timeline.IsHitBoxLive(0), "last live tick");

            AdvanceTo(timeline, 12);
            Assert.IsFalse(timeline.IsHitBoxLive(0), "half-open: the closing tick is outside");
        }

        [Test]
        public void TheOpeningTickIsReportedExactlyOnce()
        {
            // One-shot effects hang off this — a swing sound that fired every live tick would
            // machine-gun.
            var timeline = new AttackTimeline();
            timeline.Arm(Slash());

            int opens = 0;
            for (int i = 0; i < 24; i++)
            {
                timeline.Advance();
                if (timeline.DidHitBoxOpenThisTick(0)) opens++;
            }

            Assert.AreEqual(1, opens);
        }

        [Test]
        public void MultiHitMovesReportEachBoxIndependently()
        {
            var whirl = new AttackDefinition
            {
                Id = "whirl",
                StartupTicks = 6,
                ActiveTicks = 18,
                RecoveryTicks = 12,
                HitBoxes = new[]
                {
                    new AttackHitBox { Window = new TickRange(6, 9), HalfExtents = Vector2.one * 0.5f, Damage = 5 },
                    new AttackHitBox { Window = new TickRange(12, 15), HalfExtents = Vector2.one * 0.5f, Damage = 5 },
                    new AttackHitBox { Window = new TickRange(18, 21), HalfExtents = Vector2.one * 0.5f, Damage = 5 },
                },
            };

            var timeline = new AttackTimeline();
            timeline.Arm(whirl);

            AdvanceTo(timeline, 7);
            Assert.IsTrue(timeline.IsHitBoxLive(0));
            Assert.IsFalse(timeline.IsHitBoxLive(1));

            AdvanceTo(timeline, 13);
            Assert.IsFalse(timeline.IsHitBoxLive(0));
            Assert.IsTrue(timeline.IsHitBoxLive(1));

            AdvanceTo(timeline, 19);
            Assert.IsTrue(timeline.IsHitBoxLive(2));
        }

        // ---------------------------------------------------------------- facing

        [Test]
        public void HitVolumesMirrorWithFacing()
        {
            var box = new AttackHitBox
            {
                Window = new TickRange(0, 2),
                Offset = new Vector2(0.8f, 1f),
                HalfExtents = new Vector2(0.5f, 0.3f),
                RotationDegrees = 20f,
            };

            var feet = new Vector2(10f, 4f);

            Assert.AreEqual(10.8f, box.ResolveCentre(feet, 1).x, 0.001f, "forward is +X facing right");
            Assert.AreEqual(9.2f, box.ResolveCentre(feet, -1).x, 0.001f, "and -X facing left");
            Assert.AreEqual(5f, box.ResolveCentre(feet, -1).y, 0.001f, "height is unaffected by facing");

            Assert.AreEqual(20f, box.ResolveRotation(1), 0.001f);
            Assert.AreEqual(-20f, box.ResolveRotation(-1), 0.001f,
                "a diagonal slash must not point the wrong way when the character turns");
        }

        // ---------------------------------------------------------------- windows and stats

        [Test]
        public void GearWidensTheParryWithoutMovingItsOpeningTick()
        {
            var definition = Slash();
            definition.ParryWindow = new ScalableWindow(baseStart: 4, baseDuration: 4, maxTicks: 20);

            var plain = new AttackTimeline();
            plain.Arm(definition, AttackModifiers.None);

            var geared = new AttackTimeline();
            geared.Arm(definition, new AttackModifiers { IFrameScale = 1f, ParryScale = 2f, CancelScale = 1f });

            Assert.AreEqual(new TickRange(4, 8), plain.ParryRange);
            Assert.AreEqual(new TickRange(4, 12), geared.ParryRange, "twice as long, same opening tick");
        }

        [Test]
        public void AWindowStretchedPastTheActionIsClampedToIt()
        {
            // Otherwise a generous stat leaves the character invulnerable into the next action.
            var definition = Slash();
            definition.IFrames = new ScalableWindow(baseStart: 20, baseDuration: 4, maxTicks: 100);

            var timeline = new AttackTimeline();
            timeline.Arm(definition, new AttackModifiers { IFrameScale = 5f, ParryScale = 1f, CancelScale = 1f });

            Assert.AreEqual(24, timeline.IFrameRange.End, "clamped to the move's own length");
        }

        [Test]
        public void InvulnerabilityAppliesOnlyInsideItsWindow()
        {
            var definition = Slash();
            definition.IFrames = new ScalableWindow(baseStart: 2, baseDuration: 6, maxTicks: 20);

            var timeline = new AttackTimeline();
            timeline.Arm(definition);

            AdvanceTo(timeline, 1);
            Assert.IsFalse(timeline.IsInvulnerable);

            AdvanceTo(timeline, 4);
            Assert.IsTrue(timeline.IsInvulnerable);

            AdvanceTo(timeline, 8);
            Assert.IsFalse(timeline.IsInvulnerable, "and it ends");
        }

        [Test]
        public void WindowsAreFixedAtArmTime_SoABuffExpiringMidSwingChangesNothing()
        {
            // The player committed to a timing; the game owes them that timing for the whole swing.
            var definition = Slash();
            definition.ParryWindow = new ScalableWindow(baseStart: 2, baseDuration: 4, maxTicks: 20);

            var timeline = new AttackTimeline();
            timeline.Arm(definition, new AttackModifiers { IFrameScale = 1f, ParryScale = 2f, CancelScale = 1f });

            var atArm = timeline.ParryRange;
            AdvanceTo(timeline, 5);

            Assert.AreEqual(atArm, timeline.ParryRange, "the resolved window does not move mid-action");
        }

        // ---------------------------------------------------------------- chaining

        [Test]
        public void TheCancelWindowOpensInRecovery_WhichIsWhatMakesAChainFeelResponsive()
        {
            var definition = Slash();
            definition.CancelWindow = new ScalableWindow(baseStart: 16, baseDuration: 6, maxTicks: 20);
            definition.ChainsInto = "slash2";

            var timeline = new AttackTimeline();
            timeline.Arm(definition);

            AdvanceTo(timeline, 10);
            Assert.IsFalse(timeline.IsCancellable, "committed during the active frames");

            AdvanceTo(timeline, 15);
            Assert.IsFalse(timeline.IsCancellable, "still committed at the start of recovery");

            AdvanceTo(timeline, 16);
            Assert.IsTrue(timeline.IsCancellable, "the chain opens partway through recovery");

            AdvanceTo(timeline, 22);
            Assert.IsFalse(timeline.IsCancellable, "and closes before the move ends");
        }

        // ---------------------------------------------------------------- validation

        [Test]
        public void AWellFormedAttackValidates()
        {
            Assert.IsNull(Slash().Validate());
        }

        [Test]
        public void AHitWindowPastTheEndOfTheActionIsRejected()
        {
            // A silent dud otherwise: the box never opens and nothing says why.
            var definition = Slash();
            definition.HitBoxes[0].Window = new TickRange(20, 30);

            StringAssert.Contains("runs past the end", definition.Validate());
        }

        [Test]
        public void ChainingWithNoCancelWindowIsRejected()
        {
            var definition = Slash();
            definition.ChainsInto = "slash2";

            StringAssert.Contains("no cancel window", definition.Validate(),
                "a chain that can never fire is worse than no chain");
        }

        [Test]
        public void AZeroLengthActionIsRejected()
        {
            var definition = new AttackDefinition { StartupTicks = 0, ActiveTicks = 0, RecoveryTicks = 0 };

            StringAssert.Contains("no duration", definition.Validate());
        }

        // ---------------------------------------------------------------- lifecycle

        [Test]
        public void DisarmingClearsEverything()
        {
            var timeline = new AttackTimeline();
            timeline.Arm(Slash());
            AdvanceTo(timeline, 10);

            timeline.Disarm();

            Assert.IsFalse(timeline.IsArmed);
            Assert.AreEqual(AttackTimeline.Phase.Idle, timeline.CurrentPhase);
            Assert.IsFalse(timeline.IsHitBoxLive(0));
            Assert.IsFalse(timeline.IsInvulnerable);
        }

        [Test]
        public void ReArmingRestartsFromZero()
        {
            var timeline = new AttackTimeline();
            timeline.Arm(Slash());
            AdvanceTo(timeline, 15);

            timeline.Arm(Slash());

            Assert.AreEqual(-1, timeline.ElapsedTicks);
            timeline.Advance();
            Assert.AreEqual(AttackTimeline.Phase.Startup, timeline.CurrentPhase);
        }

        [Test]
        public void SteppingAnUnarmedTimelineIsHarmless()
        {
            var timeline = new AttackTimeline();

            for (int i = 0; i < 5; i++) timeline.Advance();

            Assert.IsFalse(timeline.IsArmed);
            Assert.AreEqual(AttackTimeline.Phase.Idle, timeline.CurrentPhase);
        }
    }
}
