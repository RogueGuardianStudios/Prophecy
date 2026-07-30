using System.Collections.Generic;
using Rokkan.Prophecy.Sim.Collision;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevelPhysics;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// Answers "who does this hit box touch this tick", inside the tick, with no scene.
    ///
    /// <para><b>Two questions, two systems, one truth.</b> Overlap between a rotated hit box and a
    /// hurtbox is <see cref="ImmediatePhysics"/> — Unity's own geometry maths, which is why hit
    /// volumes can rotate at all. Whether a wall is in the way is
    /// <see cref="CollisionWorld.IsOccluded"/>, asked of the same baked geometry movement collides
    /// with. There is deliberately no second copy of the level in here.</para>
    ///
    /// <para><b>Cover is per attack, not per world.</b> A spear thrust through a grate stops; an
    /// expanding shockwave does not. <see cref="AttackHitBox.StoppedByGeometry"/> decides, and the
    /// world only ever answers the geometric question.</para>
    ///
    /// <para>Allocations are <c>Allocator.Temp</c> and disposed on the way out, so there is no
    /// lifetime to manage and nothing to leak between ticks. Every target is submitted in a single
    /// batched call rather than one call per pair.</para>
    /// </summary>
    public static class HitResolver
    {
        /// <summary>
        /// Contact generation rejects a distance of zero outright, so "no inflation" has to be an
        /// epsilon. Attacks that want forgiveness inflate deliberately instead.
        /// </summary>
        public const float NoSkin = 0.0001f;

        /// <summary>
        /// Depth half-extent given to every volume. The sim is a 2D plane inside a 3D query, so
        /// the depth axis must always overlap or nothing would ever connect.
        /// </summary>
        private const float PlaneDepth = 1f;

        private const int MaxContactsPerPair = 4;

        /// <summary>
        /// Fill <paramref name="hits"/> with the indices of every target the box connects with.
        ///
        /// <para>Targets on the attacker's own team are skipped, as is the attacker itself. Team 0
        /// is neutral and hit by anyone.</para>
        /// </summary>
        /// <returns>How many targets were hit.</returns>
        public static int Resolve(
            in AttackHitBox box,
            in Attacker attacker,
            IReadOnlyList<Hurtbox> targets,
            CollisionWorld world,
            List<int> hits,
            float skin = NoSkin)
        {
            hits.Clear();

            if (targets == null || targets.Count == 0) return 0;
            if (box.HalfExtents.x <= 0f || box.HalfExtents.y <= 0f) return 0;

            var centre = box.ResolveCentre(attacker.Feet, attacker.Facing);
            float rotation = box.ResolveRotation(attacker.Facing);

            // Cull first so the batch only contains plausible pairs. Cheap rejections here keep
            // the contact call proportional to what is actually nearby.
            var candidates = new List<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (!target.IsValid) continue;
                if (target.OwnerId == attacker.Id) continue;
                if (target.Team != 0 && target.Team == attacker.Team) continue;

                candidates.Add(i);
            }

            if (candidates.Count == 0) return 0;

            int pairs = candidates.Count;

            var geomA = new NativeArray<GeometryHolder>(pairs, Allocator.Temp);
            var geomB = new NativeArray<GeometryHolder>(pairs, Allocator.Temp);
            var xformA = new NativeArray<ImmediateTransform>(pairs, Allocator.Temp);
            var xformB = new NativeArray<ImmediateTransform>(pairs, Allocator.Temp);
            var contacts = new NativeArray<ImmediateContact>(pairs * MaxContactsPerPair, Allocator.Temp);
            var counts = new NativeArray<int>(pairs, Allocator.Temp);

            try
            {
                var attackGeometry = GeometryHolder.Create(
                    new BoxGeometry(new Vector3(box.HalfExtents.x, box.HalfExtents.y, PlaneDepth)));

                var attackTransform = new ImmediateTransform
                {
                    Position = new Vector3(centre.x, centre.y, 0f),
                    Rotation = Quaternion.Euler(0f, 0f, rotation),
                };

                for (int p = 0; p < pairs; p++)
                {
                    var target = targets[candidates[p]];

                    geomA[p] = attackGeometry;
                    xformA[p] = attackTransform;

                    geomB[p] = GeometryHolder.Create(
                        new BoxGeometry(new Vector3(target.HalfExtents.x, target.HalfExtents.y, PlaneDepth)));

                    xformB[p] = new ImmediateTransform
                    {
                        Position = new Vector3(target.Centre.x, target.Centre.y, 0f),
                        Rotation = Quaternion.Euler(0f, 0f, target.RotationDegrees),
                    };
                }

                ImmediatePhysics.GenerateContacts(
                    geomA.AsReadOnly(), geomB.AsReadOnly(),
                    xformA.AsReadOnly(), xformB.AsReadOnly(),
                    pairs, contacts, counts,
                    Mathf.Max(NoSkin, skin));

                for (int p = 0; p < pairs; p++)
                {
                    if (counts[p] <= 0) continue;

                    int targetIndex = candidates[p];

                    // Traced from the ATTACKER, not the hit box. A reaching attack's volume is
                    // often already past the wall that ought to have stopped it, so casting from
                    // the box would report clear exactly when cover matters most.
                    //
                    // Checked only for attacks that care, and only after overlap — an occlusion
                    // query per non-touching target would be wasted work.
                    if (box.StoppedByGeometry && world != null &&
                        world.IsOccluded(attacker.Origin, targets[targetIndex].Centre))
                    {
                        continue;
                    }

                    hits.Add(targetIndex);
                }
            }
            finally
            {
                geomA.Dispose();
                geomB.Dispose();
                xformA.Dispose();
                xformB.Dispose();
                contacts.Dispose();
                counts.Dispose();
            }

            return hits.Count;
        }
    }
}
