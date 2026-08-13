using UnityEngine;

namespace Rokkan.Prophecy.Sim.Collision
{
    /// <summary>
    /// The facts about one scene collider that decide what it becomes in the sim's world —
    /// already flattened to plain numbers, with the marker components reduced to their data.
    /// The bake gathers these; nothing here can touch a scene.
    /// </summary>
    public readonly struct ColliderFacts
    {
        public readonly Vector2 Min;
        public readonly Vector2 Max;
        public readonly bool IsTrigger;

        public readonly bool IsDoor;
        public readonly int DoorRoomMinSide;
        public readonly int DoorRoomMaxSide;

        public readonly bool IsWater;

        public readonly bool IsClimbable;
        public readonly ClimbableKind ClimbableKind;

        public readonly bool IsOneWay;
        public readonly bool OneWayAllowsDropThrough;

        public ColliderFacts(Vector2 min, Vector2 max, bool isTrigger,
                             bool isDoor = false, int doorRoomMinSide = 0, int doorRoomMaxSide = 0,
                             bool isWater = false,
                             bool isClimbable = false, ClimbableKind climbableKind = ClimbableKind.Ladder,
                             bool isOneWay = false, bool oneWayAllowsDropThrough = true)
        {
            Min = min;
            Max = max;
            IsTrigger = isTrigger;
            IsDoor = isDoor;
            DoorRoomMinSide = doorRoomMinSide;
            DoorRoomMaxSide = doorRoomMaxSide;
            IsWater = isWater;
            IsClimbable = isClimbable;
            ClimbableKind = climbableKind;
            IsOneWay = isOneWay;
            OneWayAllowsDropThrough = oneWayAllowsDropThrough;
        }
    }

    /// <summary>
    /// Turns one collider's facts into entries in a <see cref="CollisionWorld"/> — the
    /// classification half of the bake, split from the scene-walk so it can be pinned by a
    /// plain test. Every sim test used to hand-build its world while this logic ran only in a
    /// MonoBehaviour: the suite proved the sim correct against worlds the bake might never
    /// produce, and a misclassified one-way or a door missing its room sides broke the game
    /// with every test green.
    /// </summary>
    public static class CollisionClassifier
    {
        /// <summary>
        /// Apply the facts. Returns true when a countable solid was added — the bake's
        /// "how many solids" tally, which deliberately counts walls and platforms but not
        /// doors-as-passages, water, or climbables.
        /// </summary>
        /// <param name="doorsAreWalls">For a body that cannot use doors (an enemy): a doorway
        /// bakes as a plain solid. For a door-capable body it bakes as its trigger PLUS a
        /// <see cref="SolidKind.DoorBarrier"/> — free to walk through, opaque to attacks and
        /// fatal to projectiles. Zelda II's law, enforced in the bake.</summary>
        public static bool Apply(CollisionWorld world, in ColliderFacts facts, bool doorsAreWalls)
        {
            if (world == null) return false;

            // A zero-thickness volume cannot be swept against meaningfully — the strict-overlap
            // test would never fire on it. Skip rather than add something that silently is not there.
            if (facts.Max.x - facts.Min.x <= 0f || facts.Max.y - facts.Min.y <= 0f) return false;

            var box = new Aabb(facts.Min, facts.Max);

            if (facts.IsDoor)
            {
                if (doorsAreWalls)
                {
                    world.Add(box, SolidKind.Solid);
                    return true;
                }

                world.AddDoor(box, facts.DoorRoomMinSide, facts.DoorRoomMaxSide);
                world.Add(box, SolidKind.DoorBarrier);
                return false;
            }

            if (facts.IsWater)
            {
                world.AddWater(box);
                return false;
            }

            if (facts.IsClimbable)
            {
                world.AddClimbable(box, facts.ClimbableKind);
                return false;
            }

            // A plain trigger marks nothing the sim cares about — only the marker components
            // above give a trigger a meaning.
            if (facts.IsTrigger) return false;

            world.Add(box, facts.IsOneWay ? SolidKind.OneWay : SolidKind.Solid,
                      facts.OneWayAllowsDropThrough);
            return true;
        }
    }
}
