using UnityEngine;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// A place the player can arrive at, and which way they face when they do.
    ///
    /// <para>Named rather than positional, because the thing that sends you here is a portal in
    /// another scene, and it has to say where it is sending you without holding a reference across
    /// a scene boundary — Unity will not serialise one. A string both scenes agree on is the
    /// cheapest contract that survives that.</para>
    ///
    /// <para>Facing is authored rather than inferred. Arriving through a door should leave you
    /// looking into the room, not back the way you came, and the arrival point is the only thing
    /// that knows which way that is.</para>
    /// </summary>
    public sealed class SpawnPoint : MonoBehaviour
    {
        [SerializeField, Tooltip("Portals name this. 'default' is used when nothing is specified.")]
        private string _id = "default";

        [SerializeField, Tooltip("+1 faces right, -1 faces left.")]
        private int _facing = 1;

        [SerializeField, Tooltip("Which room the arrival stands in. Rooms are graph state — " +
                                 "changed only at doors — so a spawn must SAY where it is; " +
                                 "0 is 'the whole scene', right for any scene without rooms.")]
        private int _room;

        public string Id => _id;

        public int Room => _room;

        /// <summary>Normalised to +1 or -1; a spawn facing nowhere is not a thing.</summary>
        public int Facing => _facing < 0 ? -1 : 1;

        /// <summary>Where the feet go. Sim position is the feet, so this is the pivot directly.</summary>
        public Vector3 Position => transform.position;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);

            var feet = transform.position;
            Gizmos.DrawWireSphere(feet, 0.18f);
            Gizmos.DrawLine(feet, feet + Vector3.up * 1.8f);
            Gizmos.DrawRay(feet + Vector3.up * 0.9f, new Vector3(Facing, 0f, 0f) * 0.8f);
        }
    }
}
