using Rokkan.Prophecy.Presentation;
using UnityEngine;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// A place that sends the player somewhere else: walk in, and <see cref="SceneDirector"/>
    /// carries you to another world scene, arriving at a named <see cref="SpawnPoint"/>.
    ///
    /// <para><b>Walk-in, not press-to-use.</b> Zelda II's transitions are tiles you step onto, and
    /// that is the model here: crossing the threshold is the input. The <c>Interact</c> ability is
    /// the right seam for doors that should ask first; a portal deliberately does not ask.</para>
    ///
    /// <para><b>It tests the player's feet against its own box, on purpose.</b> The sim owns no
    /// notion of "trigger volumes" — hitboxes are combat's, and level geometry answers exactly two
    /// questions, neither of which is this one. A portal is world furniture, like the kill plane:
    /// presentation-side, frame-rate timed, and incapable of affecting a combat outcome. Putting it
    /// through the sim would buy determinism nothing — a scene load is not replayable state — and
    /// would cost the sim a third kind of geometry.</para>
    ///
    /// <para><b>Armed only after the player has been seen outside it.</b> A portal pair points at
    /// each other's spawn points, and nothing guarantees an arrival spawn sits clear of a portal
    /// volume — an authoring slip there would otherwise bounce the player straight back, forever.
    /// Requiring one clean frame outside before it can fire makes the ping-pong impossible rather
    /// than merely avoided.</para>
    /// </summary>
    public sealed class Portal : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene this portal leads to. Must be in the build settings.")]
        private string _targetScene;

        [SerializeField, Tooltip("SpawnPoint id to arrive at over there.")]
        private string _targetSpawnId = "default";

        [SerializeField, Tooltip("Half extents of the volume, centred on this transform. Sized a " +
                                 "little past the visible geometry, so touching the thing counts " +
                                 "as entering it.")]
        private Vector3 _halfExtents = new Vector3(0.9f, 1.3f, 1.5f);

        private bool _armed;

        public string TargetScene => _targetScene;

        public string TargetSpawnId => _targetSpawnId;

        private void Update()
        {
            var director = SceneDirector.Instance;
            if (director == null) return;

            var player = director.Player;
            if (player == null) return;

            // The sim's position mapped to world, not the transform: the transform is written by
            // CharacterView, which is a component that may legitimately not exist.
            var feet = SpaceMapping.ToWorld(player.CurrentPosition, player.Space, player.RailDepth);

            if (Evaluate(feet, director.IsTransitioning))
                director.GoTo(_targetScene, _targetSpawnId);
        }

        /// <summary>
        /// One frame of the portal's decision: arm when the player is outside, fire when an armed
        /// portal sees them inside. Separated from <c>Update</c> so the arming rule is testable
        /// without a scene load.
        /// </summary>
        internal bool Evaluate(Vector3 feetWorld, bool transitioning)
        {
            // While a load is in flight nothing arms and nothing fires — the player is mid-move,
            // and whatever position they briefly hold is not a decision they made.
            if (transitioning) return false;

            if (!Contains(feetWorld))
            {
                _armed = true;
                return false;
            }

            if (!_armed) return false;

            _armed = false;
            return true;
        }

        private bool Contains(Vector3 point)
        {
            var delta = point - transform.position;

            return Mathf.Abs(delta.x) <= _halfExtents.x &&
                   Mathf.Abs(delta.y) <= _halfExtents.y &&
                   Mathf.Abs(delta.z) <= _halfExtents.z;
        }

        private void OnDrawGizmos()
        {
            // Always drawn, not just when selected: an invisible teleport threshold is the same
            // trap as an invisible kill plane, and this one moves you somewhere confusing.
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position, _halfExtents * 2f);
        }
    }
}
