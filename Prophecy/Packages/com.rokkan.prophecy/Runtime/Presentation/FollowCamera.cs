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

        [Header("Framing")]
        [SerializeField]
        private Vector3 _sideScrollOffset = new Vector3(0f, 1.6f, -12f);

        [SerializeField]
        private Vector3 _topDownOffset = new Vector3(0f, 13f, -9f);

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

            if (offset.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
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

            if (offset.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        }
    }
}
