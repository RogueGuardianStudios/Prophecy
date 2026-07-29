using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Marks a collider as a platform you pass up through and land on top of.
    ///
    /// <para>A component rather than a layer or a tag. Layers are a scarce global resource that
    /// gets fought over as a project grows, and a tag is a string nobody can find the definition
    /// of. A component is self-documenting on the object, survives being reparented into a prefab,
    /// and shows up in a search.</para>
    ///
    /// <para><b>Two kinds of platform, one component.</b> By default it is <i>pass-through</i>:
    /// jump up through it, land on it, and hold down to drop back off. Clearing
    /// <see cref="AllowDropThrough"/> makes it strictly one-way — still climbable from beneath,
    /// but the player cannot leave downward.</para>
    ///
    /// <para>Which one is right is a level design decision. A platform in the middle of a room
    /// should be droppable, or the player gets stranded on scenery. A platform forming the floor
    /// of an area they climbed up into should not be, or crouching drops them back out of it.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OneWayPlatform : MonoBehaviour
    {
        [SerializeField, Tooltip("Hold down to drop through. Turn OFF for a platform that must " +
                                 "only ever be passed upward — the sealed floor of an area.")]
        private bool _allowDropThrough = true;

        public bool AllowDropThrough => _allowDropThrough;

        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            // Green passes both ways, amber only upward — readable at a glance in a gray box
            // where every platform is otherwise the same grey slab.
            Gizmos.color = _allowDropThrough
                ? new Color(0.4f, 1f, 0.5f, 0.5f)
                : new Color(1f, 0.7f, 0.2f, 0.5f);

            var bounds = collider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            var top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            Gizmos.DrawRay(top, Vector3.up * 0.6f);

            if (_allowDropThrough) Gizmos.DrawRay(top, Vector3.down * 0.6f);
        }
    }
}
