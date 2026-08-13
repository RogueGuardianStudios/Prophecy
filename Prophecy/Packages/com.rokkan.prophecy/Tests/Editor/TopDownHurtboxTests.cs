using NUnit.Framework;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// A hurtbox is shaped for the space it is played in, and the two shapes disagree about
    /// everything but the owner.
    ///
    /// <para>The bug these pin down: <c>ForBody</c> used for a top-down character puts a box the
    /// body's HEIGHT long on the plane — 1.8 m of hittable wake behind a 0.7 m body — with its
    /// centre half a height north of where the character actually stands, because plane-Y means
    /// "up" in side-scroll and "north" from above. It looked like everyone's hit box was simply
    /// too large; it was actually the wrong silhouette entirely.</para>
    /// </summary>
    public sealed class TopDownHurtboxTests
    {
        private static readonly Vector2 Feet = new Vector2(3f, 5f);
        private static readonly Vector2 Body = new Vector2(0.7f, 1.8f);

        [Test]
        public void TheFootprintIsTheBodysWidthBothWaysAndSitsOnTheFeet()
        {
            var box = Hurtbox.ForFootprint(7, Feet, Body, team: 2);

            Assert.AreEqual(Feet, box.Centre,
                "Seen from above, a body is exactly where its feet are — no half-height offset.");
            Assert.AreEqual(new Vector2(0.35f, 0.35f), box.HalfExtents,
                "The footprint is the width both ways; height belongs to the railed axis up here.");
            Assert.AreEqual(7, box.OwnerId);
            Assert.AreEqual(2, box.Team);
        }

        [Test]
        public void TheStandingShapeIsStillTheStandingShape()
        {
            // The contrast, so a refactor cannot quietly make one shape serve both spaces again.
            var box = Hurtbox.ForBody(7, Feet, Body, team: 2);

            Assert.AreEqual(new Vector2(3f, 5.9f), box.Centre,
                "Side-scroll raises the centre by half the height to stand the box on the feet.");
            Assert.AreEqual(new Vector2(0.35f, 0.9f), box.HalfExtents);
        }
    }
}
