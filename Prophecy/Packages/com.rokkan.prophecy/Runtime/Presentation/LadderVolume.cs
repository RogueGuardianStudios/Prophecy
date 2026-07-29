using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Marks a trigger collider as climbable — a ladder fixed to a wall, or a rope hanging from a
    /// platform.
    ///
    /// <para>Deliberately a <b>trigger</b>, not a solid. A ladder you collide with is a wall you
    /// cannot walk past, and the moment one is placed along a corridor the player is stuck on it.
    /// <see cref="CollisionBaker"/> already skips triggers when collecting solids, so marking one
    /// climbable adds it to a separate list without it ever blocking anything.</para>
    ///
    /// <para><b>Both kinds must be anchored to something.</b> A ladder is fixed against a wall; a
    /// rope hangs from a platform that can be passed through, so climbing it actually leads
    /// somewhere. An unanchored climbable still <i>works</i> — that is exactly the problem. It is
    /// a floating column the player can ride into the ceiling, and nothing about it looks wrong
    /// until someone plays it. So the anchor is checked, and complains in the editor rather than
    /// waiting to be discovered.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LadderVolume : MonoBehaviour
    {
        [SerializeField, Tooltip("Ladder: fixed against a wall. Rope: hangs from a pass-through platform.")]
        private ClimbableKind _kind = ClimbableKind.Ladder;

        [SerializeField, Tooltip("How far to look for the wall or platform this should be attached to.")]
        private float _anchorSearchDistance = 0.6f;

        public ClimbableKind Kind => _kind;

        /// <summary>
        /// True if this is attached to the thing its kind requires: a solid behind a ladder, a
        /// pass-through platform above a rope.
        /// </summary>
        public bool IsProperlyAnchored(out string problem)
        {
            problem = null;

            var collider = GetComponent<Collider>();
            if (collider == null)
            {
                problem = "no Collider — nothing to climb";
                return false;
            }

            if (!collider.isTrigger)
            {
                problem = "the Collider is not a trigger, so this bakes as a wall and blocks the corridor";
                return false;
            }

            var bounds = collider.bounds;

            if (_kind == ClimbableKind.Rope)
            {
                // A band around the rope's upper end, not strictly above it. A rope is authored to
                // run a little PAST the platform it is tied to, so that climbing off the top leaves
                // the feet on the surface rather than just beneath it — which means the platform
                // sits inside the rope's span, not above its highest point.
                var centre = new Vector3(
                    bounds.center.x,
                    bounds.max.y - _anchorSearchDistance * 0.5f,
                    bounds.center.z);

                var extents = new Vector3(
                    bounds.extents.x,
                    _anchorSearchDistance * 1.5f,
                    bounds.extents.z);

                foreach (var hit in Physics.OverlapBox(centre, extents, Quaternion.identity))
                    if (hit.GetComponentInParent<OneWayPlatform>() != null) return true;

                problem = "a rope must hang from a pass-through platform, and there is none at its top";
                return false;
            }

            // A ladder wants a solid immediately behind it, on either side.
            foreach (float direction in new[] { -1f, 1f })
            {
                var beside = bounds.center + new Vector3(direction * (bounds.extents.x + _anchorSearchDistance * 0.5f), 0f, 0f);
                var extents = new Vector3(_anchorSearchDistance * 0.5f, bounds.extents.y * 0.9f, bounds.extents.z);

                foreach (var hit in Physics.OverlapBox(beside, extents, Quaternion.identity))
                {
                    if (hit.isTrigger) continue;
                    if (hit.GetComponentInParent<LadderVolume>() != null) continue;
                    return true;
                }
            }

            problem = "a ladder must be fixed against a wall, and there is no solid beside it";
            return false;
        }

        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            bool anchored = IsProperlyAnchored(out _);

            Gizmos.color = anchored
                ? new Color(0.9f, 0.75f, 0.25f, 0.35f)
                : new Color(1f, 0.2f, 0.2f, 0.45f);

            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);

            if (!anchored)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size * 1.05f);
            }
        }
    }
}
