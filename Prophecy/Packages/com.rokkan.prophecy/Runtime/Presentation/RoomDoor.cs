using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Marks a trigger collider as a DOORWAY between two rooms — the only place a room change
    /// can happen (Matt: rooms are identities, not shapes; predetermined doors connect them,
    /// and a door that teleports instead is simply a <see cref="Rokkan.Prophecy.World.Portal"/>).
    /// <see cref="CollisionBaker"/> reads these into the sim's door list the way it reads
    /// ladders and water: a region the body passes through, meaningful only to the sim's room
    /// tracking.
    ///
    /// <para><b>Author it thin across the passage and TALL enough to cover every crossing</b> —
    /// a jump arcing over the volume would change rooms in the fiction and not in the sim.
    /// The thin axis is the crossing axis; the room on its MIN side and MAX side are named
    /// here, which is the entire room graph: there is no room list anywhere, only doors that
    /// know their neighbours.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomDoor : MonoBehaviour
    {
        [SerializeField, Tooltip("Room on the thin axis' MIN side (left, or below).")]
        private int _roomMinSide = 1;

        [SerializeField, Tooltip("Room on the thin axis' MAX side (right, or above).")]
        private int _roomMaxSide = 2;

        public int RoomMinSide => _roomMinSide;
        public int RoomMaxSide => _roomMaxSide;

        public bool IsProperlyAuthored(out string problem)
        {
            problem = null;

            var collider = GetComponent<Collider>();
            if (collider == null)
            {
                problem = "no Collider — nothing to walk through";
                return false;
            }

            if (!collider.isTrigger)
            {
                problem = "the Collider is not a trigger; a solid doorway is a wall";
                return false;
            }

            if (_roomMinSide == _roomMaxSide)
            {
                problem = "both sides name the same room, so crossing it changes nothing";
                return false;
            }

            return true;
        }

        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            Gizmos.color = IsProperlyAuthored(out _)
                ? new Color(0.85f, 0.65f, 0.2f, 0.35f)
                : new Color(1f, 0.2f, 0.2f, 0.45f);

            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
        }
    }
}
