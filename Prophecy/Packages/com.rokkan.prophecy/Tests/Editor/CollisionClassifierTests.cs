using NUnit.Framework;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Pins the classification half of the bake — the one link between authored scenes and the
    /// sim's world. Every sim fixture hand-builds its <see cref="CollisionWorld"/>, so without
    /// these the suite proves the sim correct against worlds the bake might never produce: a
    /// misclassified one-way flag or a door missing its room sides breaks the game while
    /// everything stays green. One test per kind, plus the skip rules.
    /// </summary>
    public class CollisionClassifierTests
    {
        private static ColliderFacts Plain(bool trigger = false) =>
            new ColliderFacts(new Vector2(0f, 0f), new Vector2(2f, 1f), trigger);

        private static readonly Aabb Probe = new Aabb(new Vector2(0.5f, 0.25f), new Vector2(1.5f, 0.75f));

        [Test]
        public void ASolidColliderBakesAsAWall_AndCounts()
        {
            var world = new CollisionWorld();

            bool counted = CollisionClassifier.Apply(world, Plain(), doorsAreWalls: false);

            Assert.IsTrue(counted, "walls are what the bake tallies");
            Assert.IsTrue(world.OverlapsAnySolid(Probe));
        }

        [Test]
        public void APlainTriggerBakesAsNothing()
        {
            var world = new CollisionWorld();

            bool counted = CollisionClassifier.Apply(world, Plain(trigger: true), doorsAreWalls: false);

            Assert.IsFalse(counted);
            Assert.AreEqual(0, world.Count, "an unmarked trigger means nothing to the sim");
        }

        [Test]
        public void AZeroThicknessVolumeIsSkipped()
        {
            // The strict-overlap test can never fire on a degenerate box — adding one would be
            // adding something that silently is not there.
            var world = new CollisionWorld();
            var flat = new ColliderFacts(new Vector2(0f, 0f), new Vector2(2f, 0f), isTrigger: false);

            Assert.IsFalse(CollisionClassifier.Apply(world, in flat, doorsAreWalls: false));
            Assert.AreEqual(0, world.Count);
        }

        [Test]
        public void AOneWayPlatformKeepsItsDropThroughAuthoring()
        {
            var world = new CollisionWorld();
            var deck = new ColliderFacts(new Vector2(0f, 0f), new Vector2(4f, 0.2f), isTrigger: false,
                                         isOneWay: true, oneWayAllowsDropThrough: false);

            CollisionClassifier.Apply(world, in deck, doorsAreWalls: false);

            var feet = new Aabb(new Vector2(1.5f, 0.2f), new Vector2(2.5f, 2f));
            Assert.IsTrue(world.IsGrounded(feet), "a one-way grounds a body standing on it");
            Assert.IsFalse(world.StandingOnDroppablePlatform(feet),
                "AllowDropThrough=false must survive the bake — losing it turns a hard deck " +
                "into one the player can fall out of");
        }

        [Test]
        public void WaterBakesAsWater_NotAsAWall()
        {
            var world = new CollisionWorld();
            var pool = new ColliderFacts(new Vector2(0f, -2f), new Vector2(6f, 0f), isTrigger: true,
                                         isWater: true);

            bool counted = CollisionClassifier.Apply(world, in pool, doorsAreWalls: false);

            Assert.IsFalse(counted);
            Assert.AreEqual(0, world.Count, "water that blocks is a wall — it must not be solid");
            Assert.IsTrue(world.TryGetWater(new Vector2(3f, -1f), out _));
        }

        [Test]
        public void ALadderBakesAsClimbable_AndDoesNotBlockWalkingPast()
        {
            var world = new CollisionWorld();
            var ladder = new ColliderFacts(new Vector2(1f, 0f), new Vector2(1.6f, 3f), isTrigger: true,
                                           isClimbable: true, climbableKind: ClimbableKind.Ladder);

            CollisionClassifier.Apply(world, in ladder, doorsAreWalls: false);

            Assert.AreEqual(1, world.ClimbableCount);
            Assert.AreEqual(0, world.Count);
            Assert.IsTrue(world.TryGetClimbable(new Aabb(new Vector2(1.1f, 1f), new Vector2(1.5f, 2f)), out var found));
            Assert.AreEqual(ClimbableKind.Ladder, found.Kind, "the kind must survive the bake");
        }

        [Test]
        public void ADoorBakesAsPassageAndBarrier_ForADoorCapableBody()
        {
            var world = new CollisionWorld();
            var door = new ColliderFacts(new Vector2(0f, 0f), new Vector2(1f, 3f), isTrigger: true,
                                         isDoor: true, doorRoomMinSide: 1, doorRoomMaxSide: 2);

            bool counted = CollisionClassifier.Apply(world, in door, doorsAreWalls: false);

            Assert.IsFalse(counted, "a passage is not a wall in the tally");
            Assert.AreEqual(1, world.DoorCount, "the crossing trigger must exist");
            Assert.IsFalse(world.OverlapsAnySolid(new Aabb(new Vector2(0.2f, 1f), new Vector2(0.8f, 2f))),
                "a door-capable body walks through freely");
            Assert.IsTrue(world.OverlapsAnySolid(new Aabb(new Vector2(0.2f, 1f), new Vector2(0.8f, 2f)),
                                                 includeDoorBarriers: true),
                "but attacks and projectiles see the barrier — the fight does not cross a door");
        }

        [Test]
        public void ADoorBakesAsAPlainWall_ForABodyThatCannotUseDoors()
        {
            var world = new CollisionWorld();
            var door = new ColliderFacts(new Vector2(0f, 0f), new Vector2(1f, 3f), isTrigger: true,
                                         isDoor: true, doorRoomMinSide: 1, doorRoomMaxSide: 2);

            bool counted = CollisionClassifier.Apply(world, in door, doorsAreWalls: true);

            Assert.IsTrue(counted, "a wall is exactly what it is for the Roc");
            Assert.AreEqual(0, world.DoorCount, "no crossing exists for a body that cannot cross");
            Assert.IsTrue(world.OverlapsAnySolid(new Aabb(new Vector2(0.2f, 1f), new Vector2(0.8f, 2f))));
        }
    }
}
