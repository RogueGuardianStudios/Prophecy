using NUnit.Framework;
using Rokkan.Prophecy.Presentation;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The render-rate to tick-rate bridge, pinned.
    ///
    /// <para>Every failure mode here is silent — dropped input feels like the game ignoring you,
    /// and duplicated input feels like a bug somewhere else entirely. Neither shows up in a
    /// compiler warning, and both are frame-rate dependent, so they reproduce on one machine and
    /// not another. That is exactly the kind of thing worth nailing down in a test that runs in
    /// microseconds with no device attached.</para>
    /// </summary>
    public class InputLatchTests
    {
        [Test]
        public void APressIsReportedOnce_NotOnEveryFrameTheButtonIsHeld()
        {
            var latch = new ButtonLatch();

            latch.Sample(true);
            var first = latch.Consume();

            latch.Sample(true);
            var second = latch.Consume();

            Assert.IsTrue(first.Pressed, "the frame the button went down");
            Assert.IsTrue(first.Held);

            Assert.IsFalse(second.Pressed, "holding is not pressing again");
            Assert.IsTrue(second.Held, "but it is still held");
        }

        [Test]
        public void APressAndReleaseBetweenTwoTicks_BothSurvive()
        {
            // The high-frame-rate case: several render frames fit inside one tick, and a fast tap
            // is fully contained by them. Without latching, the sim would never see it at all.
            var latch = new ButtonLatch();

            latch.Sample(true);
            latch.Sample(false);

            var state = latch.Consume();

            Assert.IsTrue(state.Pressed, "the tap happened");
            Assert.IsTrue(state.Released, "and it ended");
            Assert.IsFalse(state.Held, "but the button is up now");
        }

        [Test]
        public void AnUnconsumedPress_SurvivesUntilATickTakesIt()
        {
            // The low-frame-rate case: a frame can retire zero ticks. The press must wait.
            var latch = new ButtonLatch();

            latch.Sample(true);
            latch.Sample(true);
            latch.Sample(true);

            Assert.IsTrue(latch.Consume().Pressed, "three frames with no tick must not lose the press");
        }

        [Test]
        public void WhenOneFrameRetiresTwoTicks_OnlyTheFirstSeesThePress()
        {
            // The 30 fps case: one frame, two ticks. A jump press must buy one jump, not two.
            var latch = new ButtonLatch();

            latch.Sample(true);

            var firstTick = latch.Consume();
            var secondTick = latch.Consume();

            Assert.IsTrue(firstTick.Pressed);
            Assert.IsFalse(secondTick.Pressed, "the same press must not be delivered twice");
            Assert.IsTrue(secondTick.Held, "though the button is genuinely still down");
        }

        [Test]
        public void ReleaseIsReportedOnce_ThenTheButtonIsSimplyUp()
        {
            var latch = new ButtonLatch();

            latch.Sample(true);
            latch.Consume();

            latch.Sample(false);
            var releaseTick = latch.Consume();
            var afterTick = latch.Consume();

            Assert.IsTrue(releaseTick.Released);
            Assert.IsFalse(releaseTick.Held);

            Assert.IsFalse(afterTick.Released, "released is an edge, not a state");
            Assert.IsFalse(afterTick.Held);
        }

        [Test]
        public void Clear_DropsTheHeldLevelToo()
        {
            // Focus loss: the button may have come up while nothing was sampling. Keeping the held
            // level would leave the character running into a wall until it was pressed again.
            var latch = new ButtonLatch();

            latch.Sample(true);
            latch.Clear();

            var state = latch.Consume();

            Assert.IsFalse(state.Held);
            Assert.IsFalse(state.Pressed);
            Assert.IsFalse(state.Released);
        }

        [Test]
        public void ANeutralLatchReportsNothing()
        {
            var latch = new ButtonLatch();
            var state = latch.Consume();

            Assert.IsFalse(state.Held);
            Assert.IsFalse(state.Pressed);
            Assert.IsFalse(state.Released);
        }
    }
}
