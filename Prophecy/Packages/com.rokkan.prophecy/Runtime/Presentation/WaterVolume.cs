using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Marks a trigger collider as water. <see cref="CollisionBaker"/> reads these into the
    /// sim's water list the way it reads <see cref="LadderVolume"/>s into the climbable list —
    /// a region the body passes through, meaningful only to the abilities that ask about it
    /// (Swim's sink and breath, Buoyancy's float and surge).
    ///
    /// <para>Deliberately a <b>trigger</b>, exactly like a ladder: water you collide with is a
    /// wall wearing a costume. The top face of the box IS the surface — submersion, the
    /// waterline, and the launch's depth all measure from it — so author the box with its top
    /// at the intended waterline, not loosely around the wet part.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterVolume : MonoBehaviour
    {
        /// <summary>True when the collider will actually bake as water rather than as a wall.</summary>
        public bool IsProperlyAuthored(out string problem)
        {
            problem = null;

            var collider = GetComponent<Collider>();
            if (collider == null)
            {
                problem = "no Collider — nothing to swim in";
                return false;
            }

            if (!collider.isTrigger)
            {
                problem = "the Collider is not a trigger. The baker takes the marker at its " +
                          "word and bakes it as water anyway, but the convention is a trigger, " +
                          "like ladders — a solid pool reads as a mistake waiting to happen";
                return false;
            }

            return true;
        }

        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            bool ok = IsProperlyAuthored(out _);

            Gizmos.color = ok
                ? new Color(0.25f, 0.55f, 0.9f, 0.35f)
                : new Color(1f, 0.2f, 0.2f, 0.45f);

            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);

            // The surface is the load-bearing face; draw it so a misplaced waterline is
            // visible at a glance rather than discovered by swimming.
            var bounds = collider.bounds;
            Gizmos.color = ok ? new Color(0.4f, 0.8f, 1f, 0.9f) : Color.red;
            Gizmos.DrawWireCube(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
                                new Vector3(bounds.size.x, 0.02f, bounds.size.z));
        }
    }
}
