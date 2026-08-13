using NUnit.Framework;
using Rokkan.Prophecy.Sim.AI;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The wanderer's walk, pinned down. RoamSteering is a pure function precisely so these can
    /// exist: the strategy that calls it is a shared ScriptableObject, and per-agent randomness
    /// through hashed (who, when) is what keeps two wanderers from sharing one brain's dice.
    /// </summary>
    public sealed class RoamSteeringTests
    {
        private const int Repick = 90;

        [Test]
        public void TheSameAgentAtTheSameTickAlwaysWantsTheSameHeading()
        {
            var first = RoamSteering.Heading(7, 1234, Repick, Vector2.zero, 0.5f);
            var second = RoamSteering.Heading(7, 1234, Repick, Vector2.zero, 0.5f);

            Assert.AreEqual(first, second, "Statelessness is the contract — same inputs, same walk.");
        }

        [Test]
        public void AHeadingHoldsForItsWholeWindow()
        {
            var early = RoamSteering.Heading(7, 900, Repick, Vector2.zero, 0.5f);
            var late = RoamSteering.Heading(7, 989, Repick, Vector2.zero, 0.5f);

            Assert.AreEqual(early, late,
                "Ticks 900 and 989 share a 90-tick window; re-rolling inside one is jitter, not wandering.");
        }

        [Test]
        public void TheNextWindowRollsANewHeading()
        {
            var before = RoamSteering.Heading(7, 989, Repick, Vector2.zero, 0.5f);
            var after = RoamSteering.Heading(7, 990, Repick, Vector2.zero, 0.5f);

            Assert.AreNotEqual(before, after,
                "Window 10 to window 11 should re-roll — a heading that never changes is a bee-line.");
        }

        [Test]
        public void TwoAgentsInTheSameWindowWalkDifferently()
        {
            var seven = RoamSteering.Heading(7, 1234, Repick, Vector2.zero, 0.5f);
            var eight = RoamSteering.Heading(8, 1234, Repick, Vector2.zero, 0.5f);

            Assert.AreNotEqual(seven, eight,
                "Identical walks would read as a formation — the shared-strategy trap by another route.");
        }

        [Test]
        public void EveryHeadingIsUnitLength([Values(0f, 0.5f, 1f)] float bias)
        {
            for (int agent = 1; agent < 20; agent++)
            {
                var heading = RoamSteering.Heading(agent, agent * 137, Repick,
                                                   new Vector2(3f, -4f), bias);

                Assert.AreEqual(1f, heading.magnitude, 0.001f,
                    $"Agent {agent} at bias {bias} produced a non-unit heading — that scales speed.");
            }
        }

        [Test]
        public void FullBiasIsAHomingMissile()
        {
            var toTarget = new Vector2(3f, -4f);
            var heading = RoamSteering.Heading(7, 1234, Repick, toTarget, bias: 1f);

            Assert.AreEqual(0f, Vector2.Angle(toTarget, heading), 0.5f,
                "Bias one should point dead at the target; anything else means the roll leaks through.");
        }

        [Test]
        public void ZeroBiasIgnoresTheTargetEntirely()
        {
            var blind = RoamSteering.Heading(7, 1234, Repick, Vector2.zero, 0f);
            var sighted = RoamSteering.Heading(7, 1234, Repick, new Vector2(5f, 0f), 0f);

            Assert.AreEqual(blind, sighted, "Bias zero is a pure drunkard's walk, target or no target.");
        }

        [Test]
        public void MidBiasBendsTowardTheTargetOnAverage()
        {
            // Any single window may wander away; the walk as a whole must not. Average many
            // windows and the pull should show — this is the property that makes a wanderer a
            // threat rather than a screensaver.
            var toTarget = new Vector2(1f, 0f);
            var sum = Vector2.zero;

            for (int window = 0; window < 200; window++)
                sum += RoamSteering.Heading(7, window * Repick, Repick, toTarget, 0.55f);

            Assert.Greater(sum.x, 0f, "Two hundred windows averaged away from the target — no menace.");
        }
    }

    /// <summary>
    /// The intent's second axis. Side-scroll brains never touch <c>MoveY</c>, and the one rule
    /// worth a test is the collision between the two meanings of "down".
    /// </summary>
    public sealed class WandererIntentTests
    {
        [Test]
        public void MoveYReachesTheFrame()
        {
            var intent = new EnemyIntent { MoveX = 0.5f, MoveY = -0.8f };

            var frame = intent.Consume();

            Assert.AreEqual(0.5f, frame.Move.x, 0.001f);
            Assert.AreEqual(-0.8f, frame.Move.y, 0.001f);
        }

        [Test]
        public void CrouchBeatsMoveY()
        {
            // Ducking is a decision; drifting south is not. A side-scroll brain that crouches
            // while some stale MoveY lingers must still read as crouching, full stop.
            var intent = new EnemyIntent { MoveY = 1f, Crouch = true };

            Assert.AreEqual(-1f, intent.Consume().Move.y, 0.001f);
        }

        [Test]
        public void ClearForgetsTheSecondAxisToo()
        {
            var intent = new EnemyIntent { MoveY = 1f };
            intent.Clear();

            Assert.AreEqual(0f, intent.Consume().Move.y, 0.001f);
        }
    }
}
