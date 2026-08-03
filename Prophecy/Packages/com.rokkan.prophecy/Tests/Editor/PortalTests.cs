using NUnit.Framework;
using Rokkan.Prophecy.World;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The portal's arming rule, exercised without a scene load.
    ///
    /// <para>The rule exists for one failure: a portal pair points at each other's spawns, and an
    /// arrival spawn inside a portal volume would otherwise bounce the player back the frame they
    /// landed — a ping-pong between two loading screens with no input ever read. The rule is
    /// "you must be seen outside before you can enter", and these tests are that sentence.</para>
    /// </summary>
    public sealed class PortalTests
    {
        private GameObject _gameObject;
        private Portal _portal;

        // Comfortably inside / outside the default half extents (0.9, 1.3, 1.5) of a portal at
        // the origin. Points, not boxes: the portal tests the player's feet.
        private static readonly Vector3 Inside = new Vector3(0.2f, 0f, 0f);
        private static readonly Vector3 Outside = new Vector3(5f, 0f, 0f);

        [SetUp]
        public void CreatePortal()
        {
            _gameObject = new GameObject("Portal_UnderTest");
            _portal = _gameObject.AddComponent<Portal>();
        }

        [TearDown]
        public void DestroyPortal() => Object.DestroyImmediate(_gameObject);

        [Test]
        public void ArrivingInsideNeverFires()
        {
            // The ping-pong case: the player materialises inside the volume. However long they
            // stand there, nothing happens until they have genuinely left and come back.
            for (int frame = 0; frame < 10; frame++)
                Assert.IsFalse(_portal.Evaluate(Inside, transitioning: false),
                               $"Fired on frame {frame} against a player who was never outside.");
        }

        [Test]
        public void WalkingInFiresExactlyOnce()
        {
            Assert.IsFalse(_portal.Evaluate(Outside, transitioning: false), "Fired while outside.");
            Assert.IsTrue(_portal.Evaluate(Inside, transitioning: false), "Walking in did not fire.");

            // Standing in the volume is one entry, not an entry per frame.
            for (int frame = 0; frame < 10; frame++)
                Assert.IsFalse(_portal.Evaluate(Inside, transitioning: false),
                               $"Refired on frame {frame} without the player ever leaving.");
        }

        [Test]
        public void LeavingRearmsIt()
        {
            _portal.Evaluate(Outside, transitioning: false);
            Assert.IsTrue(_portal.Evaluate(Inside, transitioning: false));

            Assert.IsFalse(_portal.Evaluate(Outside, transitioning: false), "Fired while outside.");
            Assert.IsTrue(_portal.Evaluate(Inside, transitioning: false),
                          "A portal used once should work again after stepping away.");
        }

        [Test]
        public void NothingArmsOrFiresMidTransition()
        {
            // Wherever the player briefly is while a load is in flight, it is not a position they
            // chose — being "outside" during the swap must not bank an arming for the arrival.
            Assert.IsFalse(_portal.Evaluate(Outside, transitioning: true));
            Assert.IsFalse(_portal.Evaluate(Inside, transitioning: false),
                           "An arming banked during a transition let the arrival fire instantly.");
        }

        [Test]
        public void AnArmedPortalHoldsItsFireDuringATransition()
        {
            _portal.Evaluate(Outside, transitioning: false);   // genuinely armed

            Assert.IsFalse(_portal.Evaluate(Inside, transitioning: true),
                           "Fired while another transition was already in flight.");

            // The arming itself survives — the player did earn it — so once the world settles,
            // standing in the volume counts.
            Assert.IsTrue(_portal.Evaluate(Inside, transitioning: false));
        }
    }
}
