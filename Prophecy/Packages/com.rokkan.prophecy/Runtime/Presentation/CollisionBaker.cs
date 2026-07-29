using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Fills the sim's <see cref="CollisionWorld"/> from the scene's colliders, once, at load.
    ///
    /// <para><b>This is the seam the whole architecture rests on.</b> The sim must run headless, so
    /// it cannot query Unity physics — <c>Physics.BoxCast</c> needs a live <c>PhysicsScene</c> that
    /// a unit test does not have. But level geometry is authored in a scene, by hand and by the
    /// gray box generator, and it would be absurd to author it twice. So the geometry is read out
    /// of the scene exactly once, converted to plain numbers, and handed over. The bake touches
    /// Transforms and Colliders; the tick that follows touches neither.</para>
    ///
    /// <para>Being a one-shot is what keeps that honest. If this ran per-tick it would be a physics
    /// query wearing a disguise, and the sim would stop being testable the moment someone relied
    /// on it. Moving platforms, when they arrive, will need an explicit sim-side representation
    /// rather than a re-bake.</para>
    ///
    /// <para><b>Rotation is flattened.</b> Every collider contributes its world-space bounding box,
    /// so a rotated ramp bakes as the box that encloses it — solid where the ramp is not. The gray
    /// box is axis-aligned by design and the sim is AABB-only, so this costs nothing today; it is
    /// listed here because a tilted collider will otherwise produce collision nobody can see.</para>
    /// </summary>
    public static class CollisionBaker
    {
        /// <summary>
        /// Clear <paramref name="world"/> and refill it from every enabled, non-trigger collider on
        /// <paramref name="mask"/>. Returns the number of solids added.
        /// </summary>
        /// <param name="ignoreRoot">Skips colliders under this object — the player's own body,
        /// which must not be baked as a wall it then stands inside.</param>
        public static int Bake(CollisionWorld world, MovementSpace space, LayerMask mask,
                               Transform ignoreRoot = null)
        {
            if (world == null) return 0;

            world.Clear();

            // Inactive objects are excluded: something switched off in the scene must not be
            // solid. Sort order is unspecified and deliberately not asked for — the sweeps take a
            // min/max across every solid, so the result does not depend on the order they arrive in.
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
            int added = 0;

            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];

                if (!collider.enabled) continue;
                if ((mask.value & (1 << collider.gameObject.layer)) == 0) continue;
                if (ignoreRoot != null && collider.transform.IsChildOf(ignoreRoot)) continue;

                var bounds = collider.bounds;
                var min = SpaceMapping.ToPlane(bounds.min, space);
                var max = SpaceMapping.ToPlane(bounds.max, space);

                // A zero-thickness volume cannot be swept against meaningfully — the strict-overlap
                // test would never fire on it. Skip rather than add something that silently is not there.
                if (max.x - min.x <= 0f || max.y - min.y <= 0f) continue;

                // Climbables are read from triggers, because a ladder must not block walking past.
                var climbable = collider.GetComponentInParent<LadderVolume>();
                if (climbable != null)
                {
                    if (!climbable.IsProperlyAnchored(out string problem))
                        Debug.LogWarning($"{climbable.name}: {problem}. It will still be climbable, " +
                                         "which is why this is worth saying out loud.", climbable);

                    world.AddClimbable(new Aabb(min, max), climbable.Kind);
                    continue;
                }

                if (collider.isTrigger) continue;

                var platform = collider.GetComponentInParent<OneWayPlatform>();

                if (platform != null)
                    world.Add(new Aabb(min, max), SolidKind.OneWay, platform.AllowDropThrough);
                else
                    world.Add(new Aabb(min, max), SolidKind.Solid);

                added++;
            }

            return added;
        }
    }
}
