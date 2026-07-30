using System.Collections.Generic;
using RGS.Core;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// Answers "who does this hit box touch this tick", inside the tick, with no scene and no
    /// engine.
    ///
    /// <para><b>The overlap maths is ours.</b> This used to call
    /// <c>UnityEngine.LowLevelPhysics.ImmediatePhysics</c>, chosen over hand-rolled AABBs for one
    /// reason: rotation, which an axis-aligned box cannot express. That reason turned out to be
    /// answerable in about forty lines — two rotated rectangles are separated if and only if one of
    /// their four face normals separates them, which is four projections. Owning it is better on
    /// every axis that mattered:</para>
    ///
    /// <list type="bullet">
    ///   <item>no six <c>Allocator.Temp</c> arrays per call, which at fifty projectiles was ~300
    ///         allocations a tick</item>
    ///   <item>no native interop, so the sim stays genuinely headless</item>
    ///   <item>it runs everywhere Unity does, rather than being validated on Windows and hoped for
    ///         on console</item>
    ///   <item>bit-identical across same-architecture targets, because the only transcendentals are
    ///         <see cref="DeterministicMath"/>'s — free here, and expensive to retrofit if the
    ///         co-op-only netcode decision is ever revisited</item>
    /// </list>
    ///
    /// <para><b>Cover is a separate question.</b> Whether a wall is in the way is
    /// <see cref="CollisionWorld.IsOccluded"/>, asked of the same baked geometry movement collides
    /// with. There is deliberately no second copy of the level in here, and it is only asked for
    /// attacks that care, only after overlap — an occlusion query per non-touching target would be
    /// work spent proving nothing.</para>
    /// </summary>
    public static class HitResolver
    {
        /// <summary>
        /// Kept for the callers that pass it. It used to be a hard requirement — contact generation
        /// rejected a distance of zero outright — and is now simply the default inflation, which is
        /// none worth speaking of. An attack wanting forgiveness inflates deliberately instead.
        /// </summary>
        public const float NoSkin = 0f;

        private const float DegreesToRadians = DeterministicMath.PI / 180f;

        /// <summary>Below this a rotation is treated as none, taking the axis-aligned fast path.</summary>
        private const float FlatEpsilon = 0.0001f;

        /// <summary>
        /// The same test against a plain list, checking every entry.
        ///
        /// <para><b>Named this way on purpose, and internal on purpose.</b> Nothing shipping calls
        /// it — <see cref="Resolve"/> is what the sim uses, and it asks the broadphase first. This
        /// exists because a test that means "these two boxes, does it connect" should not have to
        /// build a spatial index to ask. If it were called <c>Resolve</c> too, the next call site
        /// would pick it out of an autocomplete list and quietly reintroduce the O(n) sweep this
        /// whole file was rewritten to remove.</para>
        ///
        /// <para>Targets on the attacker's own team are skipped, as is the attacker itself. Team 0
        /// is neutral and hit by anyone.</para>
        /// </summary>
        /// <returns>How many targets were hit.</returns>
        internal static int ResolveWithoutBroadphase(
            in AttackHitBox box,
            in Attacker attacker,
            IReadOnlyList<Hurtbox> targets,
            CollisionWorld world,
            List<int> hits,
            float skin = NoSkin)
        {
            hits.Clear();
            if (targets == null || targets.Count == 0) return 0;
            if (!Prepare(box, attacker, skin, out var centre, out var rotation,
                         out var half, out var bound)) return 0;

            for (int i = 0; i < targets.Count; i++)
                Consider(i, targets[i], box, attacker, world, hits,
                         centre, rotation, half, bound);

            return hits.Count;
        }

        /// <summary>
        /// Fill <paramref name="hits"/> with the indices of every target the box connects with,
        /// asking the broadphase which targets are even worth looking at.
        ///
        /// <para>This is the resolve. There is deliberately no public one that does not narrow
        /// first — see <see cref="ResolveWithoutBroadphase"/>.</para>
        ///
        /// <para>Targets on the attacker's own team are skipped, as is the attacker itself. Team 0
        /// is neutral and hit by anyone.</para>
        /// </summary>
        /// <returns>How many targets were hit.</returns>
        public static int Resolve(
            in AttackHitBox box,
            in Attacker attacker,
            HurtboxSet targets,
            CollisionWorld world,
            List<int> hits,
            List<int> candidates,
            float skin = NoSkin)
        {
            hits.Clear();
            if (targets == null || targets.Count == 0) return 0;
            if (!Prepare(box, attacker, skin, out var centre, out var rotation,
                         out var half, out var bound)) return 0;

            int found = targets.Query(centre.x - bound.x, centre.x + bound.x, candidates);

            for (int c = 0; c < found; c++)
            {
                int index = candidates[c];
                Consider(index, targets[index], box, attacker, world, hits,
                         centre, rotation, half, bound);
            }

            return hits.Count;
        }

        private static bool Prepare(in AttackHitBox box, in Attacker attacker, float skin,
                                    out Vector2 centre, out float rotation,
                                    out Vector2 half, out Vector2 bound)
        {
            centre = default;
            rotation = 0f;
            half = default;
            bound = default;

            if (box.HalfExtents.x <= 0f || box.HalfExtents.y <= 0f) return false;

            centre = box.ResolveCentre(attacker.Feet, attacker.Facing);
            rotation = box.ResolveRotation(attacker.Facing);
            half = box.HalfExtents + new Vector2(skin, skin);

            // The rotated box's axis-aligned bound, computed once. It is both the broadphase query
            // span and the cheap reject, and rotation makes it genuinely non-obvious: a 0.5
            // half-extent reaches 0.5 unrotated and 0.707 at 45 degrees.
            bound = BoundingHalfExtents(half, rotation);
            return true;
        }

        private static void Consider(int index, in Hurtbox target, in AttackHitBox box,
                                     in Attacker attacker, CollisionWorld world, List<int> hits,
                                     Vector2 centre, float rotation, Vector2 half, Vector2 bound)
        {
            if (!target.IsValid) return;
            if (target.OwnerId == attacker.Id) return;
            if (target.Team != 0 && target.Team == attacker.Team) return;

            if (!Overlaps(centre, half, rotation,
                          target.Centre, target.HalfExtents, target.RotationDegrees, bound)) return;

            // Traced from the ATTACKER, not the hit box. A reaching attack's volume is often already
            // past the wall that ought to have stopped it, so casting from the box would report
            // clear exactly when cover matters most.
            if (box.StoppedByGeometry && world != null &&
                world.IsOccluded(attacker.Origin, target.Centre))
            {
                return;
            }

            hits.Add(index);
        }

        /// <summary>
        /// True if two rotated rectangles overlap. Public because it is the one piece of geometry
        /// the whole combat system rests on, and it deserves to be testable on its own.
        /// </summary>
        public static bool Overlaps(Vector2 centreA, Vector2 halfA, float rotationA,
                                    Vector2 centreB, Vector2 halfB, float rotationB)
        {
            return Overlaps(centreA, halfA, rotationA, centreB, halfB, rotationB,
                            BoundingHalfExtents(halfA, rotationA));
        }

        private static bool Overlaps(Vector2 centreA, Vector2 halfA, float rotationA,
                                     Vector2 centreB, Vector2 halfB, float rotationB,
                                     Vector2 boundA)
        {
            var delta = centreB - centreA;

            // Cheap axis-aligned reject using A's precomputed bound. Almost every pair fails here,
            // and the ones that do not are worth the four projections below.
            if (Mathf.Abs(delta.x) > boundA.x + Mathf.Abs(halfB.x) + Mathf.Abs(halfB.y)) return false;
            if (Mathf.Abs(delta.y) > boundA.y + Mathf.Abs(halfB.x) + Mathf.Abs(halfB.y)) return false;

            bool flatA = Mathf.Abs(rotationA) < FlatEpsilon;
            bool flatB = Mathf.Abs(rotationB) < FlatEpsilon;

            // Both unrotated is the overwhelmingly common case — a stationary hurtbox and an
            // unrotated swing — and it is a plain interval test on each axis.
            if (flatA && flatB)
            {
                return Mathf.Abs(delta.x) < halfA.x + halfB.x &&
                       Mathf.Abs(delta.y) < halfA.y + halfB.y;
            }

            Axes(rotationA, out var ax, out var ay);
            Axes(rotationB, out var bx, out var by);

            // Separating axis: two convex shapes miss if and only if some axis separates them, and
            // for rectangles only the four face normals can. Any one of them separating is enough,
            // so this returns on the first.
            return !Separated(delta, ax, halfA, ax, ay, halfB, bx, by)
                && !Separated(delta, ay, halfA, ax, ay, halfB, bx, by)
                && !Separated(delta, bx, halfA, ax, ay, halfB, bx, by)
                && !Separated(delta, by, halfA, ax, ay, halfB, bx, by);
        }

        /// <summary>True if <paramref name="axis"/> separates the two boxes.</summary>
        private static bool Separated(Vector2 delta, Vector2 axis,
                                      Vector2 halfA, Vector2 axisAx, Vector2 axisAy,
                                      Vector2 halfB, Vector2 axisBx, Vector2 axisBy)
        {
            float distance = Mathf.Abs(delta.x * axis.x + delta.y * axis.y);

            return distance > ProjectionRadius(halfA, axis, axisAx, axisAy) +
                              ProjectionRadius(halfB, axis, axisBx, axisBy);
        }

        /// <summary>How far a box reaches along <paramref name="axis"/>, given its own two axes.</summary>
        private static float ProjectionRadius(Vector2 half, Vector2 axis, Vector2 boxX, Vector2 boxY)
        {
            return half.x * Mathf.Abs(boxX.x * axis.x + boxX.y * axis.y) +
                   half.y * Mathf.Abs(boxY.x * axis.x + boxY.y * axis.y);
        }

        /// <summary>The two unit axes of a box rotated by <paramref name="degrees"/>.</summary>
        private static void Axes(float degrees, out Vector2 x, out Vector2 y)
        {
            if (Mathf.Abs(degrees) < FlatEpsilon)
            {
                x = new Vector2(1f, 0f);
                y = new Vector2(0f, 1f);
                return;
            }

            float radians = degrees * DegreesToRadians;
            float cos = DeterministicMath.Cos(radians);
            float sin = DeterministicMath.Sin(radians);

            x = new Vector2(cos, sin);
            y = new Vector2(-sin, cos);
        }

        /// <summary>
        /// The axis-aligned half-extents of a rotated box. A 0.5 half-extent reaches 0.5 along X
        /// unrotated and 0.707 at 45°, which is exactly the intuition that is unreliable enough to
        /// be worth computing rather than eyeballing.
        /// </summary>
        public static Vector2 BoundingHalfExtents(Vector2 half, float degrees)
        {
            if (Mathf.Abs(degrees) < FlatEpsilon) return half;

            Axes(degrees, out var x, out var y);

            return new Vector2(
                half.x * Mathf.Abs(x.x) + half.y * Mathf.Abs(y.x),
                half.x * Mathf.Abs(x.y) + half.y * Mathf.Abs(y.y));
        }
    }
}
