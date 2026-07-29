using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Renders the simulated character: interpolates its position between ticks, faces it, and
    /// squashes it when it crouches.
    ///
    /// <para><b>Why interpolate at all.</b> The sim runs at a fixed 60 Hz and the renderer does
    /// not. Drawing the raw sim position means the character advances in 60 discrete jumps a
    /// second regardless of frame rate — imperceptible at 60 fps, visibly stepped at 144, and
    /// juddery whenever a frame boundary falls just after a tick rather than just before. Lerping
    /// by <c>InterpolationAlpha</c> — how far the clock's accumulator has travelled toward the next
    /// tick — puts the character where it would be if the sim were continuous.</para>
    ///
    /// <para>The cost is that the visual is up to one tick behind the simulation. That is the
    /// correct trade and the standard one: 16 ms of latency nobody can see, in exchange for motion
    /// that is smooth at every frame rate. The alternative — extrapolating ahead — guesses, and
    /// then visibly corrects itself the moment the character hits a wall.</para>
    ///
    /// <para>Strictly one-directional. This reads sim state and writes Transforms; it never writes
    /// back. If this component were deleted the character would still be simulated correctly, and
    /// nothing about the game's outcome would change — which is the test of whether a presentation
    /// component is doing only its job.</para>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CharacterView : MonoBehaviour
    {
        [SerializeField]
        private PlayerCharacterHost _host;

        [SerializeField, Tooltip("Visual root to rotate and squash. Defaults to this transform.")]
        private Transform _body;

        [Header("Facing")]
        [SerializeField, Tooltip("Degrees per second the body turns to face its direction. 0 snaps.")]
        private float _turnSpeed = 900f;

        [Header("Stance")]
        [SerializeField, Tooltip("Gray-box only: resize the proxy mesh to match the collision height. " +
                                 "Turn off once a real crouch animation exists — an animated character " +
                                 "should change pose, not scale.")]
        private bool _resizeProxyOnCrouch = true;

        [SerializeField, Tooltip("How quickly the resize follows a stance change, in seconds.")]
        private float _resizeSmoothing = 0.06f;

        private Vector3 _baseScale = Vector3.one;
        private Vector3 _baseLocalPosition;
        private bool _canResize;
        private float _stanceScale = 1f;
        private float _stanceScaleVelocity;

        private void Awake()
        {
            if (_body == null) _body = transform;
            if (_host == null) _host = GetComponentInParent<PlayerCharacterHost>();

            _baseScale = _body.localScale;
            _baseLocalPosition = _body.localPosition;

            // Resizing needs a child to act on. If the body IS the root, its local position is
            // owned by ApplyPosition and cannot also be used to keep the feet planted — so the
            // resize would have to scale about the centre, which is the bug this replaced.
            _canResize = _body != transform;

            if (_resizeProxyOnCrouch && !_canResize)
                Debug.LogWarning($"{name}: crouch resize needs a child body transform. " +
                                 "Assign one, or turn the resize off.", this);
        }

        private void Update()
        {
            if (_host == null || _host.Sim == null) return;

            ApplyPosition();
            ApplyFacing();
            ApplyStance();
        }

        private void ApplyPosition()
        {
            // With no clock the accumulator is unknowable; showing the latest tick is the honest
            // fallback, and it is what a paused or single-stepped sim should look like anyway.
            float alpha = _host.Clock != null ? Mathf.Clamp01(_host.Clock.InterpolationAlpha) : 1f;

            var plane = Vector2.Lerp(_host.PreviousPosition, _host.CurrentPosition, alpha);
            transform.position = SpaceMapping.ToWorld(plane, _host.Space, _host.RailDepth);
        }

        private void ApplyFacing()
        {
            var state = _host.Sim.State;

            // Side-scroll turns on the spot; the overworld turns toward travel, falling back to
            // the sim's facing when standing still so the character does not snap to a default.
            Vector3 forward;
            if (_host.Space == MovementSpace.TopDown)
            {
                var velocity = state.Velocity;
                forward = velocity.sqrMagnitude > 0.0001f
                    ? SpaceMapping.ToWorld(velocity.normalized, _host.Space)
                    : new Vector3(state.Facing, 0f, 0f);
            }
            else
            {
                forward = new Vector3(state.Facing, 0f, 0f);
            }

            if (forward.sqrMagnitude <= 0.0001f) return;

            var target = Quaternion.LookRotation(forward, Vector3.up);

            _body.rotation = _turnSpeed <= 0f
                ? target
                : Quaternion.RotateTowards(_body.rotation, target, _turnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Resize the proxy mesh to the sim's current body height, <b>anchored at the feet</b>.
        ///
        /// <para>Scaling alone shrinks a mesh about its own origin, and the capsule proxy's origin
        /// is its centre — so a crouch pulled the feet up off the floor by half the height lost
        /// while the collision box stayed put. The character appeared to hover, then sink back on
        /// standing. Moving the body down by the same proportion keeps its base exactly where the
        /// sim says the feet are, because the base sits at <c>localPosition.y</c> above the root
        /// and both scale together.</para>
        ///
        /// <para>Only Y is touched. Scaling X and Z would fatten the character as it crouches,
        /// which no crouch does.</para>
        ///
        /// <para>This is scenery for a capsule. A real character crouches by playing a crouch, and
        /// this whole method turns off the day one exists.</para>
        /// </summary>
        private void ApplyStance()
        {
            if (!_resizeProxyOnCrouch || !_canResize) return;

            var state = _host.Sim.State;
            float target = state.StandSize.y > 0f ? state.BodySize.y / state.StandSize.y : 1f;

            _stanceScale = Mathf.SmoothDamp(_stanceScale, target, ref _stanceScaleVelocity,
                                            _resizeSmoothing);

            var scale = _baseScale;
            scale.y = _baseScale.y * _stanceScale;
            _body.localScale = scale;

            var localPosition = _baseLocalPosition;
            localPosition.y = _baseLocalPosition.y * _stanceScale;
            _body.localPosition = localPosition;
        }
    }
}
