using System.Collections.Generic;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.AI
{
    /// <summary>What the senses found this sweep.</summary>
    public struct Percept
    {
        /// <summary>Combat id of the nearest hostile, or 0 for none.</summary>
        public int TargetId;

        /// <summary>Where it is. Meaningless when <see cref="HasTarget"/> is false.</summary>
        public Vector2 TargetCentre;

        /// <summary>Horizontal distance. The one that matters in a lane.</summary>
        public float DistanceX;

        /// <summary>Straight-line distance, for range checks that care about height.</summary>
        public float Distance;

        /// <summary>-1 or +1: which way the target lies.</summary>
        public int DirectionToTarget;

        /// <summary>Nothing solid between the eyes and the target.</summary>
        public bool HasLineOfSight;

        public bool HasTarget => TargetId != 0;
    }

    /// <summary>
    /// Reads the simulation on a brain's behalf.
    ///
    /// <para><b>It queries the sim rather than the engine, and that is the whole design.</b>
    /// HopeFell's sensors raycast a live <c>PhysicsScene</c> and walk a NavMesh — reasonable there,
    /// impossible here, because Prophecy's simulation owns its own collision precisely so it can
    /// run headless. A sensor that needed PhysX would drag the AI outside the thing it is meant to
    /// be reasoning about, and no enemy could be tested without a scene.</para>
    ///
    /// <para>So sight is <c>CollisionWorld.IsOccluded</c> — the same call an attack uses to decide
    /// cover, which means an enemy cannot see through a wall it also cannot shoot through. Target
    /// finding is the combat broadphase, already bucketed and already the authority on who exists.
    /// One source of truth, queried two ways.</para>
    ///
    /// <para>Plain static methods over explicit arguments: no component, no allocation per sweep,
    /// and every one testable with a hand-built world.</para>
    /// </summary>
    public static class EnemyPerception
    {
        /// <summary>
        /// Nearest hostile within <paramref name="range"/>, and whether it can be seen.
        ///
        /// <para>Hostility is the combat rule, not an AI rule: same team is not a target, and team
        /// 0 is neutral and hostile to everyone. Reusing it means an enemy cannot come to a
        /// different conclusion about who its enemies are than the hit resolver does.</para>
        /// </summary>
        public static Percept Sense(CombatState fight, CollisionWorld level, List<int> scratch,
                                    int selfId, int selfTeam, Vector2 eyes, float range)
        {
            var percept = default(Percept);
            if (fight == null || scratch == null || range <= 0f) return percept;

            var boxes = fight.Hurtboxes;
            int found = boxes.Query(eyes.x - range, eyes.x + range, scratch);

            float best = float.MaxValue;

            for (int i = 0; i < found; i++)
            {
                var box = boxes[scratch[i]];

                if (box.OwnerId == selfId) continue;
                if (box.Team != 0 && box.Team == selfTeam) continue;

                float distance = Vector2.Distance(eyes, box.Centre);
                if (distance > range || distance >= best) continue;

                best = distance;
                percept.TargetId = box.OwnerId;
                percept.TargetCentre = box.Centre;
            }

            if (!percept.HasTarget) return percept;

            float dx = percept.TargetCentre.x - eyes.x;

            percept.DistanceX = Mathf.Abs(dx);
            percept.Distance = best;
            percept.DirectionToTarget = dx < 0f ? -1 : 1;

            // Sight last, and only for the one target that won: occlusion is the expensive query
            // and asking it about candidates that lost would be paying for answers nobody reads.
            percept.HasLineOfSight = level == null || !level.IsOccluded(eyes, percept.TargetCentre);

            return percept;
        }

        /// <summary>
        /// Whether there is floor to walk onto in <paramref name="direction"/>. What stops a patrol
        /// strolling off its own ledge.
        ///
        /// <para>Probes a small box at foot height, one step ahead and one step down. A patrol that
        /// asked "am I grounded" instead would only find out after it had already left.</para>
        /// </summary>
        public static bool GroundAhead(CollisionWorld level, Vector2 feet, Vector2 bodySize,
                                       int direction, float lookAhead = 0.6f, float probeDepth = 0.5f)
        {
            if (level == null) return true;   // nothing baked: do not invent a cliff

            float x = feet.x + direction * lookAhead;
            float half = Mathf.Max(0.05f, bodySize.x * 0.25f);

            var probe = new Aabb(new Vector2(x - half, feet.y - probeDepth),
                                 new Vector2(x + half, feet.y - 0.02f));

            return level.OverlapsAnySolid(probe);
        }

        /// <summary>Whether a wall blocks travel in <paramref name="direction"/>.</summary>
        public static bool WallAhead(CollisionWorld level, Vector2 feet, Vector2 bodySize,
                                     int direction, float probeDistance = 0.12f)
        {
            if (level == null) return false;

            var body = new Aabb(new Vector2(feet.x - bodySize.x * 0.5f, feet.y),
                                new Vector2(feet.x + bodySize.x * 0.5f, feet.y + bodySize.y));

            return level.HasWall(body, direction, probeDistance);
        }

        /// <summary>
        /// Whether a patrol should turn around: a wall in front, or no floor to step onto.
        /// </summary>
        public static bool ShouldTurnBack(CollisionWorld level, Vector2 feet, Vector2 bodySize,
                                          int direction) =>
            WallAhead(level, feet, bodySize, direction) ||
            !GroundAhead(level, feet, bodySize, direction);
    }
}
