using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// A damped follow camera with side-scroll and top-down modes.
    ///
    /// <para>Hand-rolled because Cinemachine is not installed, and for the gray box that is the
    /// right call: this is about fifty lines whose every number is visible and tunable, versus a
    /// dependency whose behaviour has to be learned before it can be trusted. If the camera turns
    /// out to need real work — confiners, blends, shake — Cinemachine is the answer then.</para>
    ///
    /// <para><b>The vertical dead zone is the part that matters.</b> A camera that tracks Y
    /// faithfully bobs with every jump, and the whole screen moving each time the player hops is
    /// exhausting to look at within about a minute. So vertical focus only moves once the
    /// character leaves a band, and re-centres gently while grounded — the camera follows where
    /// you <i>are</i>, not every arc you trace getting there.</para>
    ///
    /// <para>Look-ahead leads the facing direction, so running right shows more of what is to the
    /// right. It is smoothed separately and slowly: snapping it on every turn is worse than not
    /// having it.</para>
    ///
    /// <para>Rotation is derived from the offset rather than aimed at the target. Aiming would tilt
    /// the camera as it lags behind, which reads as the world rocking.</para>
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class FollowCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField]
        private Transform _target;

        [SerializeField, Tooltip("Optional. Supplies facing and grounded state for look-ahead and re-centring.")]
        private PlayerCharacterHost _host;

        [Header("Framing — side-scroll")]
        [SerializeField, Tooltip("Y lifts the frame so the player sits below centre; Z is the lens distance.")]
        private Vector3 _sideScrollOffset = new Vector3(0f, 1.9f, -24f);

        [SerializeField, Tooltip("Long lens. Narrow FOV far back keeps vertical surfaces near-parallel " +
                                 "so the gameplay plane reads flat, while still parallaxing.")]
        [Range(10f, 70f)]
        private float _sideScrollFov = 28f;

        [SerializeField, Tooltip("Degrees below level. Should be 0 for a side-scroller — see ApplyOrientation.")]
        [Range(-20f, 20f)]
        private float _sideScrollTilt;

        [Header("Framing — top-down")]
        [SerializeField]
        private Vector3 _topDownOffset = new Vector3(0f, 13f, -9f);

        [SerializeField, Range(10f, 70f)]
        private float _topDownFov = 45f;

        [SerializeField, Tooltip("Seconds to close most of the distance. Higher is looser.")]
        private float _smoothTime = 0.18f;

        [Header("Side-scroll feel")]
        [SerializeField, Tooltip("Metres the character may rise or fall before the camera follows.")]
        private float _verticalDeadZone = 1.4f;

        [SerializeField, Tooltip("How fast vertical focus re-centres while grounded.")]
        private float _groundedRecentreSpeed = 6f;

        [SerializeField, Tooltip("Metres the camera leads the facing direction.")]
        private float _lookAhead = 1.8f;

        [SerializeField, Tooltip("Seconds for look-ahead to swing across after a turn.")]
        private float _lookAheadSmoothTime = 0.45f;

        private Vector3 _followVelocity;
        private float _lookAheadValue;
        private float _lookAheadVelocity;
        private float _focusY;
        private bool _focusInitialised;

        private void LateUpdate()
        {
            if (_target == null) return;

            var space = _host != null ? _host.Space : MovementSpace.SideScroll;
            var offset = space == MovementSpace.TopDown ? _topDownOffset : _sideScrollOffset;

            var focus = _target.position;

            if (space == MovementSpace.SideScroll)
                focus.y = ResolveVerticalFocus(focus.y);
            else
                _focusInitialised = false;

            focus.x += ResolveLookAhead();

            transform.position = Vector3.SmoothDamp(
                transform.position, focus + offset, ref _followVelocity, _smoothTime);

            ApplyOrientation(space, offset);
        }

        /// <summary>
        /// Point the camera, and set the lens.
        ///
        /// <para><b>Side-scroll is held dead level, not aimed at the target.</b> Deriving the
        /// pitch from the offset — which is what this used to do — meant any vertical offset at all
        /// tilted the camera, and a tilted camera makes every vertical surface converge: walls lean
        /// inward, the tops of boxes come into view, and the axis-constrained plane stops reading
        /// as flat. It is subtle enough that the result just looks wrong without being nameable.
        /// The frame is raised by moving the camera up, never by pitching it down.</para>
        ///
        /// <para>Top-down genuinely wants the tilt, so there the look direction still comes from
        /// the offset.</para>
        ///
        /// <para>Field of view lives here rather than on the Camera because framing is the pair of
        /// FOV and distance, not either alone. Split them and someone moves the camera back for
        /// breathing room and silently halves how big the character reads.</para>
        /// </summary>
        private void ApplyOrientation(MovementSpace space, Vector3 offset)
        {
            var camera = GetComponent<Camera>();

            if (space == MovementSpace.TopDown)
            {
                if (offset.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);

                if (camera != null) camera.fieldOfView = _topDownFov;
                return;
            }

            transform.rotation = Quaternion.Euler(_sideScrollTilt, 0f, 0f);
            if (camera != null) camera.fieldOfView = _sideScrollFov;
        }

        /// <summary>Dead-banded vertical tracking, re-centring while the character is on the ground.</summary>
        private float ResolveVerticalFocus(float targetY)
        {
            if (!_focusInitialised)
            {
                _focusY = targetY;
                _focusInitialised = true;
                return _focusY;
            }

            float delta = targetY - _focusY;
            if (Mathf.Abs(delta) > _verticalDeadZone)
                _focusY += delta - Mathf.Sign(delta) * _verticalDeadZone;

            // Landing should settle the frame rather than leave it wherever the jump dragged it.
            // Exponential rather than linear so it is frame-rate independent.
            if (_host != null && _host.Sim != null && _host.Sim.State.Grounded)
                _focusY = Mathf.Lerp(_focusY, targetY,
                                     1f - Mathf.Exp(-_groundedRecentreSpeed * Time.deltaTime));

            return _focusY;
        }

        private float ResolveLookAhead()
        {
            int facing = _host != null && _host.Sim != null ? _host.Sim.State.Facing : 1;

            _lookAheadValue = Mathf.SmoothDamp(
                _lookAheadValue, facing * _lookAhead, ref _lookAheadVelocity, _lookAheadSmoothTime);

            return _lookAheadValue;
        }

        /// <summary>Jump the camera straight to its mark. For spawns and scene transitions, where
        /// smoothing would show the player a long slide in from wherever the camera used to be.</summary>
        public void SnapToTarget()
        {
            if (_target == null) return;

            var space = _host != null ? _host.Space : MovementSpace.SideScroll;
            var offset = space == MovementSpace.TopDown ? _topDownOffset : _sideScrollOffset;

            _focusInitialised = false;
            _followVelocity = Vector3.zero;
            _lookAheadValue = 0f;
            _lookAheadVelocity = 0f;

            var focus = _target.position;
            if (space == MovementSpace.SideScroll) focus.y = ResolveVerticalFocus(focus.y);

            transform.position = focus + offset;

            ApplyOrientation(space, offset);
        }
    }
}
