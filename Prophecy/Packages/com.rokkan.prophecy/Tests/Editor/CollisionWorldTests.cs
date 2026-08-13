using NUnit.Framework;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Pins the sim's collision behaviour. Every test builds a world in code — no scene, no
    /// colliders, no MonoBehaviour — which is the whole reason the sim owns its own collision
    /// representation instead of calling <c>Physics.CapsuleCast</c>.
    /// </summary>
    public class CollisionWorldTests
    {
        /// <summary>A 1x2 body standing with its feet at <paramref name="x"/>,<paramref name="y"/>.</summary>
        private static Aabb Body(float x, float y) =>
            Aabb.FromFootSize(new Vector2(x, y), new Vector2(1f, 2f));

        private static CollisionWorld FlatGround()
        {
            var w = new CollisionWorld();
            // Ground slab with its top surface exactly at y = 0.
            w.Add(new Aabb(new Vector2(-50f, -1f), new Vector2(50f, 0f)));
            return w;
        }

        // ---------- Aabb ----------

        [Test]
        public void Overlap_IsStrict_SoRestingOnGroundIsNotIntersecting()
        {
            // The single most important collision decision: a body whose feet sit exactly on the
            // ground plane must NOT read as overlapping it. If touching counted as overlapping,
            // every grounded frame would trigger depenetration and the character would jitter.
            var ground = new Aabb(new Vector2(-50f, -1f), new Vector2(50f, 0f));
            var resting = Body(0f, 0f);

            Assert.IsFalse(resting.Overlaps(ground), "flush contact must not be an overlap");
            Assert.IsTrue(Body(0f, -0.5f).Overlaps(ground), "genuine penetration must be an overlap");
        }

        [Test]
        public void FromFootSize_AnchorsAtTheFeet()
        {
            var b = Aabb.FromFootSize(new Vector2(3f, 5f), new Vector2(1f, 2f));
            Assert.AreEqual(5f, b.Min.y, 1e-5f, "feet at the given y");
            Assert.AreEqual(7f, b.Max.y, 1e-5f, "head one height above");
            Assert.AreEqual(3f, b.Center.x, 1e-5f, "horizontally centred on the given x");
        }

        // ---------- vertical ----------

        [Test]
        public void FallingOntoGround_StopsAtTheSurface_NotInsideIt()
        {
            var w = FlatGround();
            var body = Body(0f, 5f);

            float allowed = w.SweepVertical(body, -10f, out bool hit);

            Assert.IsTrue(hit);
            Assert.AreEqual(-5f, allowed, 0.001f, "falls exactly the 5 units to the surface");
            Assert.IsFalse(body.Translated(new Vector2(0f, allowed)).Overlaps(
                new Aabb(new Vector2(-50f, -1f), new Vector2(50f, 0f))),
                "landed position must not penetrate the ground");
        }

        [Test]
        public void FallingShortOfGround_IsUnobstructed()
        {
            var w = FlatGround();
            float allowed = w.SweepVertical(Body(0f, 5f), -2f, out bool hit);

            Assert.IsFalse(hit);
            Assert.AreEqual(-2f, allowed, 1e-5f, "nothing in the way, full motion allowed");
        }

        [Test]
        public void JumpingIntoCeiling_StopsBelowIt()
        {
            var w = FlatGround();
            w.Add(new Aabb(new Vector2(-5f, 4f), new Vector2(5f, 5f))); // ceiling underside at y=4

            // Body is 2 tall standing at y=0, so its head is at 2. Room to rise is 2.
            float allowed = w.SweepVertical(Body(0f, 0f), 10f, out bool hit);

            Assert.IsTrue(hit);
            Assert.AreEqual(2f, allowed, 0.001f);
        }

        [Test]
        public void VerticalSweep_IgnoresSolidsNotHorizontallyAligned()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(20f, -1f), new Vector2(30f, 0f))); // far to the right

            float allowed = w.SweepVertical(Body(0f, 5f), -10f, out bool hit);

            Assert.IsFalse(hit);
            Assert.AreEqual(-10f, allowed, 1e-5f);
        }

        // ---------- horizontal ----------

        [Test]
        public void WalkingIntoWall_StopsAgainstIt()
        {
            var w = FlatGround();
            w.Add(new Aabb(new Vector2(5f, 0f), new Vector2(6f, 3f))); // wall face at x=5

            // Body half-width 0.5 standing at x=0, so its right edge is at 0.5. Gap is 4.5.
            float allowed = w.SweepHorizontal(Body(0f, 0f), 10f, out bool hit);

            Assert.IsTrue(hit);
            Assert.AreEqual(4.5f, allowed, 0.001f);
        }

        [Test]
        public void WalkingLeftIntoWall_StopsAgainstIt()
        {
            var w = FlatGround();
            w.Add(new Aabb(new Vector2(-6f, 0f), new Vector2(-5f, 3f))); // wall face at x=-5

            float allowed = w.SweepHorizontal(Body(0f, 0f), -10f, out bool hit);

            Assert.IsTrue(hit);
            Assert.AreEqual(-4.5f, allowed, 0.001f);
            Assert.Less(allowed, 0f, "sign must be preserved");
        }

        [Test]
        public void HorizontalSweep_IgnoresSolidsAboveOrBelow()
        {
            var w = FlatGround(); // ground spans the whole X range but is entirely below the feet

            float allowed = w.SweepHorizontal(Body(0f, 0f), 10f, out bool hit);

            Assert.IsFalse(hit, "the floor must never block horizontal movement");
            Assert.AreEqual(10f, allowed, 1e-5f);
        }

        // ---------- one-way platforms ----------

        [Test]
        public void OneWayPlatform_CatchesAFallFromAbove()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(-5f, 3f), new Vector2(5f, 3.2f)), SolidKind.OneWay);

            float allowed = w.SweepVertical(Body(0f, 6f), -10f, out bool hit);

            Assert.IsTrue(hit);
            Assert.AreEqual(-2.8f, allowed, 0.001f, "lands on the platform top at y=3.2");
        }

        [Test]
        public void OneWayPlatform_IsPassedThroughFromBelow()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(-5f, 3f), new Vector2(5f, 3.2f)), SolidKind.OneWay);

            float allowed = w.SweepVertical(Body(0f, 0f), 10f, out bool hit);

            Assert.IsFalse(hit, "jumping up through a one-way must not be blocked");
            Assert.AreEqual(10f, allowed, 1e-5f);
        }

        [Test]
        public void OneWayPlatform_DoesNotCatchAFallThatStartedBelowIt()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(-5f, 3f), new Vector2(5f, 3.2f)), SolidKind.OneWay);

            // Feet at 1, i.e. below the platform surface — falling here must not snap up onto it.
            float allowed = w.SweepVertical(Body(0f, 1f), -0.5f, out bool hit);

            Assert.IsFalse(hit);
            Assert.AreEqual(-0.5f, allowed, 1e-5f);
        }

        [Test]
        public void OneWayPlatform_DropThrough_DisablesIt()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(-5f, 3f), new Vector2(5f, 3.2f)), SolidKind.OneWay);

            float allowed = w.SweepVertical(Body(0f, 3.2f), -5f, out bool hit, dropThrough: true);

            Assert.IsFalse(hit, "drop-through must ignore one-ways entirely");
            Assert.AreEqual(-5f, allowed, 1e-5f);
        }

        [Test]
        public void OneWayPlatform_NeverBlocksHorizontalMovement()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(5f, 0f), new Vector2(6f, 3f)), SolidKind.OneWay);

            float allowed = w.SweepHorizontal(Body(0f, 0f), 10f, out bool hit);

            Assert.IsFalse(hit);
            Assert.AreEqual(10f, allowed, 1e-5f);
        }

        [Test]
        public void OverlapsAnySolid_IgnoresOneWays()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(-5f, 0f), new Vector2(5f, 4f)), SolidKind.OneWay);

            Assert.IsFalse(w.OverlapsAnySolid(Body(0f, 1f)),
                "you are never 'inside' a one-way platform");
        }

        // ---------- grounded ----------

        [Test]
        public void IsGrounded_TrueWhenRestingFlush()
        {
            // Probing rather than contact-testing is what makes this work: strict overlap is
            // deliberately false at flush rest, so a contact test would report airborne.
            Assert.IsTrue(FlatGround().IsGrounded(Body(0f, 0f)));
        }

        [Test]
        public void IsGrounded_FalseWhenAirborne()
        {
            Assert.IsFalse(FlatGround().IsGrounded(Body(0f, 3f)));
        }

        [Test]
        public void IsGrounded_TrueOnAOneWayPlatform()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector2(-5f, 3f), new Vector2(5f, 3.2f)), SolidKind.OneWay);

            Assert.IsTrue(w.IsGrounded(Body(0f, 3.2f)), "standing on a one-way counts as grounded");
        }

        // ---------- housekeeping ----------

        [Test]
        public void ZeroDelta_MovesNothingAndHitsNothing()
        {
            var w = FlatGround();
            Assert.AreEqual(0f, w.SweepHorizontal(Body(0f, 0f), 0f, out bool hx));
            Assert.AreEqual(0f, w.SweepVertical(Body(0f, 0f), 0f, out bool hy));
            Assert.IsFalse(hx);
            Assert.IsFalse(hy);
        }

        [Test]
        public void EmptyWorld_NeverObstructs()
        {
            var w = new CollisionWorld();
            Assert.AreEqual(0, w.Count);
            Assert.AreEqual(-10f, w.SweepVertical(Body(0f, 0f), -10f, out bool hit), 1e-5f);
            Assert.IsFalse(hit);
            Assert.IsFalse(w.IsGrounded(Body(0f, 0f)));
        }

        [Test]
        public void Clear_EmptiesTheWorld()
        {
            var w = FlatGround();
            Assert.AreEqual(1, w.Count);
            w.Clear();
            Assert.AreEqual(0, w.Count);
            Assert.IsFalse(w.IsGrounded(Body(0f, 0f)));
        }

        [Test]
        public void NearestObstacleWins_WhenSeveralAreInTheWay()
        {
            var w = FlatGround();
            w.Add(new Aabb(new Vector2(8f, 0f), new Vector2(9f, 3f)));
            w.Add(new Aabb(new Vector2(5f, 0f), new Vector2(6f, 3f)));  // nearer
            w.Add(new Aabb(new Vector2(12f, 0f), new Vector2(13f, 3f)));

            float allowed = w.SweepHorizontal(Body(0f, 0f), 20f, out bool hit);

            Assert.IsTrue(hit);
            Assert.AreEqual(4.5f, allowed, 0.001f, "must stop at the nearest, not the last found");
        }
    }
}
