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

        private PortalArming _arming;

        public string TargetScene => _targetScene;

        public string TargetSpawnId => _targetSpawnId;

        private void Update()
        {
            var director = SceneDirector.Instance;
            if (director == null) return;

            // The locator, not the director: where the player is and who swaps scenes are
            // different questions, and a portal being tested needs only the first answered.
            var player = Presentation.PlayerLocator.Current;
            if (player == null) return;

            // The sim's position mapped to world, not the transform: the transform is written by
            // CharacterView, which is a component that may legitimately not exist. Layered feet,
            // not the flat rail depth: a portal on a bridge deck must feel the body that is ON
            // the deck, and a cave-floor portal must not fire for one crossing the roof above.
            var feet = player.FeetWorldPosition;

            if (Evaluate(feet, director.IsTransitioning))
                director.GoTo(_targetScene, _targetSpawnId);
        }

        /// <summary>
        /// One frame of the portal's decision: this component's only contributions are the box
        /// test and the scene names — the rule itself lives in <see cref="PortalArming"/>.
        /// </summary>
        internal bool Evaluate(Vector3 feetWorld, bool transitioning) =>
            _arming.Evaluate(Contains(feetWorld), transitioning);

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

    /// <summary>
    /// The portal's arming rule: arm when the player is seen outside, fire when an armed portal
    /// sees them inside, and hold everything while a load is in flight.
    ///
    /// <para>A plain struct, deliberately: the rule is two booleans of state machine and none of
    /// it needs a transform, so it can be exercised in a test with no GameObject and no scene
    /// load — which is exactly how the ping-pong case is kept impossible rather than merely
    /// avoided.</para>
    /// </summary>
    public struct PortalArming
    {
        private bool _armed;

        /// <summary>One frame of the decision. True means fire.</summary>
        public bool Evaluate(bool inside, bool transitioning)
        {
            // While a load is in flight nothing arms and nothing fires — the player is mid-move,
            // and whatever position they briefly hold is not a decision they made.
            if (transitioning) return false;

            if (!inside)
            {
                _armed = true;
                return false;
            }

            if (!_armed) return false;

            _armed = false;
            return true;
        }
    }
}
