using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Combo chaining: whether the next link starts, and when.
    ///
    /// <para>The tests worth having here are about input that arrives at awkward moments, because
    /// that is all a player ever produces. A press during the committed frames, a press a hair too
    /// early, a press that should have expired — each has a right answer, and getting them wrong
    /// produces a combo system that feels like it is ignoring you.</para>
    /// </summary>
    public class ComboRunnerTests
    {
        private static AttackDefinition Link(string id, string chainsInto, int cancelStart = 16)
        {
            return new AttackDefinition
            {
                Id = id,
                StartupTicks = 6,
                ActiveTicks = 6,
                RecoveryTicks = 12,
                ChainsInto = chainsInto,
                CancelWindow = chainsInto == ""
                    ? ScalableWindow.None
                    : new ScalableWindow(baseStart: cancelStart, baseDuration: 6, maxTicks: 30),
            };
        }

        private static Dictionary<string, AttackDefinition> Moveset()
        {
            return new Dictionary<string, AttackDefinition>
            {
                { "slash1", Link("slash1", "slash2") },
                { "slash2", Link("slash2", "slash3") },
                { "slash3", Link("slash3", "") },
            };
        }

        /// <summary>Run the chain forward, feeding a press on the given ticks.</summary>
        private static ComboRunner RunTo(long lastTick, params long[] pressOn)
        {
            var runner = new ComboRunner(Moveset());
            var presses = new HashSet<long>(pressOn);

            runner.Begin("slash1");

            for (long tick = 0; tick <= lastTick; tick++)
            {
                if (presses.Contains(tick)) runner.QueueInput(tick);
                runner.Advance(tick);
            }

            return runner;
        }

        // ---------------------------------------------------------------- starting

        [Test]
        public void BeginStartsTheFirstLink()
        {
            var runner = new ComboRunner(Moveset());

            Assert.IsTrue(runner.Begin("slash1"));
            Assert.AreEqual(1, runner.ChainDepth);
            Assert.AreEqual("slash1", runner.Current.Id);
        }

        [Test]
        public void AnUnknownAttackDoesNotStart()
        {
            var runner = new ComboRunner(Moveset());

            Assert.IsFalse(runner.Begin("nonexistent"));
            Assert.IsFalse(runner.IsAttacking);
        }

        // ---------------------------------------------------------------- chaining

        [Test]
        public void APressDuringTheCancelWindowChains()
        {
            var runner = RunTo(20, pressOn: 17);

            Assert.AreEqual("slash2", runner.Current.Id);
            Assert.AreEqual(2, runner.ChainDepth);
        }

        [Test]
        public void APressDuringTheCommittedFramesIsBufferedRatherThanDropped()
        {
            // The case that decides whether a combo feels responsive. Players press while the
            // current swing is still landing; an unbuffered chain throws that away.
            var runner = RunTo(20, pressOn: 10);

            Assert.AreEqual("slash2", runner.Current.Id,
                "the early press was remembered and spent when the window opened");
        }

        [Test]
        public void ChainingDoesNotHappenBeforeTheWindowOpens()
        {
            // Committed frames are the point of an attack being a decision.
            var runner = new ComboRunner(Moveset());
            runner.Begin("slash1");

            runner.QueueInput(0);
            for (long tick = 0; tick < 16; tick++) runner.Advance(tick);

            Assert.AreEqual("slash1", runner.Current.Id, "still committed at tick 15");
        }

        [Test]
        public void AStalePressExpiresRatherThanFiringLater()
        {
            // Buffering is forgiveness, not a queued command that goes off whenever it likes.
            var runner = new ComboRunner(Moveset(), bufferTicks: 4);
            runner.Begin("slash1");

            runner.QueueInput(0);
            for (long tick = 0; tick <= 20; tick++) runner.Advance(tick);

            Assert.AreNotEqual("slash2", runner.Current?.Id ?? "none",
                "a press twenty ticks stale must not chain");
        }

        [Test]
        public void TheChainRunsToItsEnd()
        {
            // Each link is 24 ticks and chains at its own tick 16, so the third starts around
            // tick 33. Stopping at 45 catches it mid-swing — run much past that and slash3
            // finishes and the runner correctly returns to neutral.
            var runner = new ComboRunner(Moveset());
            runner.Begin("slash1");

            for (long tick = 0; tick <= 45; tick++)
            {
                runner.QueueInput(tick);
                runner.Advance(tick);
            }

            Assert.AreEqual("slash3", runner.Current?.Id ?? "none");
            Assert.AreEqual(3, runner.ChainDepth);
        }

        [Test]
        public void TheFinalLinkDoesNotChainOnward()
        {
            var runner = new ComboRunner(Moveset());
            runner.Begin("slash3");

            for (long tick = 0; tick <= 40; tick++)
            {
                runner.QueueInput(tick);
                runner.Advance(tick);
            }

            Assert.IsFalse(runner.IsAttacking, "slash3 chains nowhere, so the chain ends");
            Assert.AreEqual(0, runner.ChainDepth);
        }

        // ---------------------------------------------------------------- ending

        [Test]
        public void AnUnchainedAttackReturnsToNeutral()
        {
            var runner = RunTo(30);

            Assert.IsFalse(runner.IsAttacking);
            Assert.AreEqual(0, runner.ChainDepth);
        }

        [Test]
        public void ChainingRestartsTheTimelineFromZero()
        {
            var runner = RunTo(17, pressOn: 16);

            Assert.AreEqual("slash2", runner.Current.Id);
            Assert.AreEqual(0, runner.Timeline.ElapsedTicks,
                "the new link begins at its own tick zero, not where the last one stopped");
        }

        [Test]
        public void EndAbandonsTheChain()
        {
            // A hit-react, a scene change, death.
            var runner = new ComboRunner(Moveset());
            runner.Begin("slash1");
            for (long tick = 0; tick < 8; tick++) runner.Advance(tick);

            runner.End();

            Assert.IsFalse(runner.IsAttacking);
            Assert.AreEqual(0, runner.ChainDepth);
            Assert.IsNull(runner.Current);
        }

        [Test]
        public void ConsumingAPressClearsTheBuffer()
        {
            // Otherwise one press would chain every remaining link at once.
            var runner = new ComboRunner(Moveset());
            runner.Begin("slash1");

            // Pressed inside the buffer's reach of the window at tick 16 — a press at tick 0 would
            // correctly have expired long before, which is a different test.
            runner.QueueInput(12);
            for (long tick = 0; tick <= 17; tick++) runner.Advance(tick);

            Assert.AreEqual("slash2", runner.Current.Id);
            Assert.AreEqual(0, runner.BufferedTicksRemaining(17), "the press was spent, not reused");
        }

        [Test]
        public void AdvancingWhenIdleIsHarmless()
        {
            var runner = new ComboRunner(Moveset());

            for (long tick = 0; tick < 5; tick++)
                Assert.IsFalse(runner.Advance(tick));

            Assert.IsFalse(runner.IsAttacking);
        }

        // ---------------------------------------------------------------- stats

        [Test]
        public void AWiderCancelWindowMakesChainingEasier()
        {
            // Gear that lengthens the cancel window should widen the chain opportunity, not move it.
            var moveset = Moveset();
            var generous = new AttackModifiers { IFrameScale = 1f, ParryScale = 1f, CancelScale = 3f };

            var runner = new ComboRunner(moveset);
            runner.Begin("slash1", generous);

            Assert.AreEqual(16, runner.Timeline.CancelRange.Start, "the opening tick is unmoved");
            Assert.Greater(runner.Timeline.CancelRange.Duration, 6, "but it stays open longer");
        }
    }
}
