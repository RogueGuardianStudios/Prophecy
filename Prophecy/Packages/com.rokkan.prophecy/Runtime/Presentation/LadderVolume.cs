using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Marks a trigger collider as climbable — a ladder, a rope, a vine.
    ///
    /// <para>Deliberately a <b>trigger</b>, not a solid. A ladder you collide with is a wall you
    /// cannot walk past, and the moment one is placed against a corridor the player is stuck on
    /// it. <see cref="CollisionBaker"/> already skips triggers when collecting solids, so marking
    /// one climbable adds it to a separate list without it ever blocking anything.</para>
    ///
    /// <para>Ropes and ladders are the same volume as far as the sim is concerned; what differs is
    /// the art and, later, whether lateral movement on it swings. Splitting them now would be two
    /// components with identical behaviour.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LadderVolume : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            Gizmos.color = new Color(0.9f, 0.75f, 0.25f, 0.35f);
            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);

            if (!collider.isTrigger)
            {
                // Loud on purpose: a solid ladder bakes as a wall and silently blocks the corridor.
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size * 1.05f);
            }
        }
    }
}
