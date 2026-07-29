using NUnit.Framework;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Cover queries: "is there a wall between me and you".
    ///
    /// <para>This is the half of hit resolution that stays on the project's own geometry. Hit
    /// volumes are resolved with <c>ImmediatePhysics</c>, which knows nothing about the level, so
    /// without this an attack would happily land through a metre of stone. Keeping the query on
    /// <see cref="CollisionWorld"/> means the level has exactly one definition of solid, asked two
    /// different questions.</para>
    ///
    /// <para>The length-bounded behaviour is the subtle part and gets its own tests: a wall beyond
    /// the target must not shield the target.</para>
    /// </summary>
    public class OcclusionTests
    {
        private static CollisionWorld WithWall(float minX, float maxX, float minY = -5f, float maxY = 5f)
        {
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(minX, minY), new Vector2(maxX, maxY)));
            return world;
        }

        [Test]
        public void AWallBetweenTwoPoints_Occludes()
        {
            var world = WithWall(1f, 2f);

            Assert.IsTrue(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)),
                "a wall squarely in the path blocks the line");
        }

        [Test]
        public void ClearAir_DoesNotOcclude()
        {
            var world = new CollisionWorld();

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)));
        }

        [Test]
        public void AWallBeyondTheTarget_DoesNotOcclude()
        {
            // The reason the query is length-bounded. An infinite ray would report this as blocked
            // and an enemy standing in front of a wall would be immune to everything.
            var world = WithWall(5f, 6f);

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)),
                "geometry past the target is irrelevant");
        }

        [Test]
        public void AWallBehindTheAttacker_DoesNotOcclude()
        {
            var world = WithWall(-6f, -5f);

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)));
        }

        [Test]
        public void AWallOffToOneSide_DoesNotOcclude()
        {
            var world = WithWall(1f, 2f, minY: 3f, maxY: 8f);

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)),
                "the wall spans the right X but the wrong Y");
        }

        [Test]
        public void DiagonalLinesAreHandled()
        {
            var world = WithWall(1f, 2f, minY: -1f, maxY: 1f);

            Assert.IsTrue(world.IsOccluded(new Vector2(0f, -2f), new Vector2(3f, 2f)),
                "a diagonal that crosses the slab is blocked");

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 2f), new Vector2(3f, 4f)),
                "a diagonal that passes above it is not");
        }

        [Test]
        public void VerticalLinesAreHandled()
        {
            // The axis-parallel branch: delta.x is zero, so the X slab cannot be divided through.
            var world = WithWall(-1f, 1f, minY: 1f, maxY: 2f);

            Assert.IsTrue(world.IsOccluded(new Vector2(0f, 0f), new Vector2(0f, 4f)),
                "straight up through a ceiling slab is blocked");

            Assert.IsFalse(world.IsOccluded(new Vector2(5f, 0f), new Vector2(5f, 4f)),
                "the same line displaced sideways is clear");
        }

        [Test]
        public void GrazingAFace_IsNotBlocked()
        {
            // Matches Aabb.Overlaps' strictness. Touching is not intersecting — otherwise a
            // character standing flush against a wall could not attack along it.
            var world = WithWall(1f, 2f, minY: 0f, maxY: 5f);

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)),
                "a line running exactly along the wall's bottom edge grazes rather than crosses");
        }

        [Test]
        public void PassThroughPlatformsDoNotProvideCover()
        {
            // A thin slab underfoot must not shield anyone from a sword swung across it, or every
            // platform in the game becomes an inexplicable safe spot.
            var world = new CollisionWorld();
            world.Add(new Aabb(new Vector2(1f, -1f), new Vector2(2f, 1f)), SolidKind.OneWay);

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)),
                "one-way platforms are floors, not cover");
        }

        [Test]
        public void ClimbablesDoNotProvideCover()
        {
            var world = new CollisionWorld();
            world.AddClimbable(new Aabb(new Vector2(1f, -2f), new Vector2(2f, 6f)));

            Assert.IsFalse(world.IsOccluded(new Vector2(0f, 0f), new Vector2(4f, 0f)),
                "you can be hit through a ladder");
        }

        [Test]
        public void ZeroLengthQuery_InsideAWall_IsBlocked()
        {
            // Degenerate but reachable: attacker and target at the same point. Both slabs take the
            // parallel branch, so this exercises it twice.
            var world = WithWall(-1f, 1f);

            Assert.IsTrue(world.IsOccluded(Vector2.zero, Vector2.zero),
                "a point inside solid rock is occluded");

            Assert.IsFalse(new CollisionWorld().IsOccluded(Vector2.zero, Vector2.zero),
                "and a point in empty space is not");
        }
    }
}
