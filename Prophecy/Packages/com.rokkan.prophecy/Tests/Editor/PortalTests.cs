using NUnit.Framework;
using Rokkan.Prophecy.World;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The portal's arming rule, exercised as the plain struct it is — no GameObject, no scene.
    ///
    /// <para>The rule exists for one failure: a portal pair points at each other's spawns, and an
    /// arrival spawn inside a portal volume would otherwise bounce the player back the frame they
    /// landed — a ping-pong between two loading screens with no input ever read. The rule is
    /// "you must be seen outside before you can enter", and these tests are that sentence.</para>
    /// </summary>
    public sealed class PortalTests
    {
        private PortalArming _arming;

        [SetUp]
        public void FreshPortal() => _arming = default;

        [Test]
        public void ArrivingInsideNeverFires()
        {
            // The ping-pong case: the player materialises inside the volume. However long they
            // stand there, nothing happens until they have genuinely left and come back.
            for (int frame = 0; frame < 10; frame++)
                Assert.IsFalse(_arming.Evaluate(inside: true, transitioning: false),
                               $"Fired on frame {frame} against a player who was never outside.");
        }

        [Test]
        public void WalkingInFiresExactlyOnce()
        {
            Assert.IsFalse(_arming.Evaluate(inside: false, transitioning: false), "Fired while outside.");
            Assert.IsTrue(_arming.Evaluate(inside: true, transitioning: false), "Walking in did not fire.");

            // Standing in the volume is one entry, not an entry per frame.
            for (int frame = 0; frame < 10; frame++)
                Assert.IsFalse(_arming.Evaluate(inside: true, transitioning: false),
                               $"Refired on frame {frame} without the player ever leaving.");
        }

        [Test]
        public void LeavingRearmsIt()
        {
            _arming.Evaluate(inside: false, transitioning: false);
            Assert.IsTrue(_arming.Evaluate(inside: true, transitioning: false));

            Assert.IsFalse(_arming.Evaluate(inside: false, transitioning: false), "Fired while outside.");
            Assert.IsTrue(_arming.Evaluate(inside: true, transitioning: false),
                          "A portal used once should work again after stepping away.");
        }

        [Test]
        public void NothingArmsOrFiresMidTransition()
        {
            // Wherever the player briefly is while a load is in flight, it is not a position they
            // chose — being "outside" during the swap must not bank an arming for the arrival.
            Assert.IsFalse(_arming.Evaluate(inside: false, transitioning: true));
            Assert.IsFalse(_arming.Evaluate(inside: true, transitioning: false),
                           "An arming banked during a transition let the arrival fire instantly.");
        }

        [Test]
        public void AnArmedPortalHoldsItsFireDuringATransition()
        {
            _arming.Evaluate(inside: false, transitioning: false);   // genuinely armed

            Assert.IsFalse(_arming.Evaluate(inside: true, transitioning: true),
                           "Fired while another transition was already in flight.");

            // The arming itself survives — the player did earn it — so once the world settles,
            // standing in the volume counts.
            Assert.IsTrue(_arming.Evaluate(inside: true, transitioning: false));
        }
    }
}
