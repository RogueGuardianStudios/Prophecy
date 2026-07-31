using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Sim;
using Unity.Cinemachine;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Frames the camera in <b>lanes</b> rather than metres, and drives the target Cinemachine
    /// follows.
    ///
    /// <para>A lane is the floor-to-floor module a level is built from — see
    /// <see cref="MovementTuningData.LaneHeight"/>. Framing the camera in the same unit the level
    /// is composed in is what makes the shot repeatable: every room shows the same amount of
    /// world, a drop of one floor moves the frame by a known amount, and "how much can the player
    /// see" stops being an emergent property of a distance and a field of view that were each
    /// picked for other reasons.</para>
    ///
    /// <para><b>The half-lane offsets are the point.</b> Four lanes fit the viewport, arranged as
    /// half / whole / whole / whole / half, so a player on the ground floor stands half a lane
    /// above the bottom edge. A whole lane of dead space beneath the floor — which is what a
    /// naively centred camera gives — is a quarter of the screen showing nothing, and reads as
    /// the character having sunk.</para>
    ///
    /// <para><b>Cinemachine does the work; this decides the numbers.</b> Position Composer handles
    /// dead zone, damping and screen placement, and — importantly — it only ever <i>positions</i>
    /// the camera. No Aim component is used, because Rotation Composer and Hard Look At tilt the
    /// camera to track the target, which is exactly the convergence bug this project already
    /// removed once. Side-scroll rotation stays authored and level.</para>
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class LaneCameraRig : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField, Tooltip("Supplies the lane height, so the grid follows the hero's size.")]
        private MovementTuning _tuning;

        [SerializeField]
        private PlayerCharacterHost _host;

        [SerializeField, Tooltip("The transform Cinemachine follows. Driven by this component.")]
        private Transform _followTarget;

        [Header("Framing")]
        [SerializeField, Tooltip("How much of the viewport's height the standing character fills. " +
                                 "THIS is the framing decision — everything else about the frame is " +
                                 "derived from it, so the character reads the same size however the " +
                                 "body or the lane height is retuned.")]
        [Range(0.05f, 0.5f)]
        private float _characterViewportShare = 0.2f;

        [SerializeField, Tooltip("Where the feet sit vertically. 0 = bottom edge, 1 = top. Below " +
                                 "centre on purpose: in a platformer the space that matters is above " +
                                 "you, because that is where you are jumping to.")]
        [Range(0f, 1f)]
        private float _feetViewportY = 0.375f;

        [SerializeField, Tooltip("Long lens. Distance is derived from this and the visible height.")]
        [Range(10f, 70f)]
        private float _fieldOfView = 28f;

        [Header("Look-ahead")]
        [SerializeField, Tooltip("Lanes the camera leads by at full running speed. Scales down with " +
                                 "actual speed, so standing still leads by nothing.")]
        private float _lookAheadLanesAtRun = 1f;

        [SerializeField, Tooltip("Seconds for look-ahead to swing across after a turn.")]
        private float _lookAheadSmoothTime = 0.45f;

        [Header("Falling")]
        [SerializeField, Tooltip("Lanes the camera pans down by during a long fall, so you can see " +
                                 "what you are landing on.")]
        private float _fallLookLanes = 1f;

        [SerializeField, Tooltip("Seconds of falling before the pan begins. Long enough that ordinary " +
                                 "hops and drops do not twitch the frame.")]
        private float _fallLookDelay = 0.33f;

        [SerializeField, Tooltip("Seconds from the pan starting to reaching full extent.")]
        private float _fallLookRamp = 0.4f;

        [Header("Dead zone & damping")]
        [SerializeField, Tooltip("Lanes the player may move vertically before the camera follows. " +
                                 "Above jump apex (0.70 lane) means a jump moves the frame not at all, " +
                                 "while a sustained climb still pushes past it.")]
        private float _verticalDeadZoneLanes = 0.8f;

        [SerializeField, Tooltip("Horizontal dead zone as a fraction of screen WIDTH. In screen units " +
                                 "rather than lanes because the visible width changes with aspect ratio, " +
                                 "so a lane-based value would mean something different on every monitor.")]
        [Range(0f, 0.5f)]
        private float _horizontalDeadZone = 0.06f;

        [SerializeField] private float _dampingHorizontal = 0.35f;
        [SerializeField] private float _dampingVertical = 0.6f;

        [SerializeField, Tooltip("Quantise vertical framing to whole lanes, so the frame steps " +
                                 "floor by floor instead of drifting. A distinct look — try both.")]
        private bool _snapToLanes;

        private CinemachineCamera _camera;
        private CinemachinePositionComposer _composer;

        private float _lookAheadValue;
        private float _lookAheadVelocity;

        private float _fallingSeconds;
        private float _fallLookValue;
        private float _fallLookVelocity;

        private bool _hasBounds;
        private float _boundsFloorY;
        private float _boundsCeilingY;

        /// <summary>
        /// Constrain the camera's vertical travel to the level's extent. Set by
        /// <c>SceneDirector</c> from the arriving scene's descriptor.
        ///
        /// <para>This is what makes the framing rule work at the edges of a level. The camera
        /// <i>wants</i> the player in the centre lane, but showing a lane of void beneath the
        /// ground floor to achieve that is worse than letting the player ride low in frame. So the
        /// bound wins, and the player rises up the screen as they approach the bottom of the world
        /// — which is also a legible signal that there is nothing below them.</para>
        /// </summary>
        public void SetVerticalBounds(float floorY, float ceilingY)
        {
            _hasBounds = ceilingY > floorY;
            _boundsFloorY = floorY;
            _boundsCeilingY = ceilingY;
        }

        public void ClearVerticalBounds() => _hasBounds = false;

        /// <summary>Floor-to-floor lane height in metres, from tuning.</summary>
        public float LaneHeight => _tuning != null ? _tuning.Data.LaneHeight : 3.6f;

        /// <summary>Standing body height in metres, from tuning. What the frame is sized against.</summary>
        public float StandHeight => _tuning != null ? _tuning.Data.StandHeight : 1.8f;

        /// <summary>
        /// World height the viewport spans, derived from how big the character should read.
        ///
        /// <para><b>This used to be authored as a lane count, and that was the wrong way round.</b>
        /// "Four lanes" is a number you have to do arithmetic on before you know the thing you
        /// actually care about — a 1.8 m body in 14.4 m of view is 12.5% of the screen, which
        /// nobody could see from the field. Worse, it was only true for one set of tuning: retune
        /// <c>StandHeight</c> or <c>LaneHeightMultiplier</c> and the character silently changes
        /// size on screen while the camera settings look untouched.</para>
        ///
        /// <para>Stating the share and deriving the height inverts that. The character reads the
        /// same however the body and the lane are retuned, and the number in the inspector is the
        /// one a designer would actually ask for.</para>
        /// </summary>
        public float VisibleHeight =>
            _characterViewportShare <= 0.0001f ? LaneHeight * 4f : StandHeight / _characterViewportShare;

        /// <summary>How many lanes that works out to. Derived — for the gizmo and for diagnostics.</summary>
        public float VisibleLanes => LaneHeight <= 0f ? 0f : VisibleHeight / LaneHeight;

        /// <summary>
        /// Distance needed to frame <see cref="VisibleHeight"/> at <see cref="_fieldOfView"/>.
        /// Derived rather than authored: framing is the pair, and letting someone set the distance
        /// alone silently changes how big the character reads.
        /// </summary>
        public float CameraDistance =>
            VisibleHeight / (2f * Mathf.Tan(_fieldOfView * 0.5f * Mathf.Deg2Rad));

        /// <summary>Where the player's feet sit vertically, 0 = bottom edge, 1 = top.</summary>
        public float FeetViewportY => Mathf.Clamp01(_feetViewportY);

        /// <summary>
        /// How far above the feet the followed point sits, in metres.
        ///
        /// <para>The framing is done by <b>raising the target</b> rather than by offsetting the
        /// composer's screen position. Cinemachine's screen-position sign convention is easy to get
        /// backwards — and getting it backwards pins the player to the top of the frame instead of
        /// near the bottom, which is exactly the wrong half. This is plain arithmetic in world
        /// units: put the followed point <c>n</c> metres above the feet, the composer centres it,
        /// and the feet land <c>n</c> metres below centre. Nothing to invert.</para>
        /// </summary>
        public float FocusOffsetY => (0.5f - FeetViewportY) * VisibleHeight;

        private void Awake()
        {
            _camera = GetComponent<CinemachineCamera>();
            _composer = GetComponent<CinemachinePositionComposer>();

            if (_host == null) _host = FindAnyObjectByType<PlayerCharacterHost>();

            if (_followTarget == null)
            {
                var created = new GameObject("CameraTarget");
                created.transform.SetParent(transform.parent, false);
                _followTarget = created.transform;
            }

            _camera.Follow = _followTarget;

            // No LookAt on purpose. An Aim component would pitch the camera to track the target
            // and reintroduce the convergence this project has already fixed once.
            _camera.LookAt = null;

            ApplyFraming();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) ApplyFraming();
        }

        private void Update()
        {
            if (_followTarget == null) return;

            TrackFalling();
            ApplyFraming();
            _followTarget.position = ResolveTargetPosition();
        }

        /// <summary>Push the lane-derived numbers onto Cinemachine. Cheap, and done every frame so
        /// retuning a lane in play mode reframes immediately.</summary>
        private void ApplyFraming()
        {
            if (_camera == null) return;

            var lens = _camera.Lens;
            lens.FieldOfView = _fieldOfView;
            _camera.Lens = lens;

            if (_composer == null) return;

            _composer.CameraDistance = CameraDistance;

            var composition = _composer.Composition;

            // Centred on purpose — the vertical framing is carried by FocusOffsetY instead.
            composition.ScreenPosition = new Vector2(composition.ScreenPosition.x, 0f);

            // The dead zone is what makes a jump not move the camera. Cinemachine's Size is the
            // FULL height of the band, so a half-extent above the jump apex is twice that in Size.
            composition.DeadZone.Enabled = true;
            composition.DeadZone.Size = new Vector2(
                _horizontalDeadZone,
                VisibleHeight <= 0f ? 0.2f : Mathf.Clamp01(2f * _verticalDeadZoneLanes * LaneHeight / VisibleHeight));

            _composer.Composition = composition;
            _composer.Damping = new Vector3(_dampingHorizontal, _dampingVertical, _dampingHorizontal);
        }

        private Vector3 ResolveTargetPosition()
        {
            var space = _host != null ? _host.Space : MovementSpace.SideScroll;
            var feet = _host != null
                ? SpaceMapping.ToWorld(_host.CurrentPosition, space, _host.RailDepth)
                : _followTarget.position;

            _lookAheadValue = Mathf.SmoothDamp(
                _lookAheadValue, ResolveLookAhead(), ref _lookAheadVelocity, _lookAheadSmoothTime);

            _fallLookValue = Mathf.SmoothDamp(
                _fallLookValue, ResolveFallLook(), ref _fallLookVelocity, _fallLookRamp * 0.5f);

            float y = feet.y;

            // Quantising to the grid the level is built on keeps whole lanes in frame as the
            // player changes floor, rather than parking the shot halfway between two of them.
            if (_snapToLanes && LaneHeight > 0f)
                y = Mathf.Floor(y / LaneHeight + 0.001f) * LaneHeight;

            return new Vector3(
                feet.x + _lookAheadValue,
                ClampToBounds(y + FocusOffsetY + _fallLookValue),
                feet.z);
        }

        /// <summary>
        /// How far ahead to lead, scaled by how fast the character is actually travelling.
        ///
        /// <para>A fixed lead is wrong at both ends. Sized for running it wanders off ahead of a
        /// player who is standing still; sized for walking it leaves a sprint effectively blind,
        /// since at full speed the character crosses the whole frame in under three seconds.
        /// Scaling by speed also means turning on the spot does not swing the frame — there is
        /// nothing to lead when you are not going anywhere.</para>
        /// </summary>
        private float ResolveLookAhead()
        {
            if (_host == null || _host.Sim == null) return 0f;

            var state = _host.Sim.State;
            float runSpeed = _tuning != null ? _tuning.Data.RunSpeed : 7.5f;
            if (runSpeed <= 0f) return 0f;

            float speedFraction = Mathf.Clamp01(Mathf.Abs(state.Velocity.x) / runSpeed);

            return state.Facing * _lookAheadLanesAtRun * LaneHeight * speedFraction;
        }

        /// <summary>
        /// Downward offset during a sustained fall, so a long drop shows what is at the bottom.
        ///
        /// <para>Delayed rather than immediate: every hop and kerb-step is a brief descent, and
        /// panning on all of them would leave the frame twitching constantly. Waiting a third of a
        /// second means only real falls — the ones where you genuinely cannot see the landing —
        /// move the camera.</para>
        ///
        /// <para>Timed in seconds rather than ticks on purpose. This is presentation, deliberately
        /// outside the fixed-tick determinism contract; nothing about where the camera looks may
        /// affect the simulation.</para>
        /// </summary>
        private float ResolveFallLook()
        {
            if (_fallLookLanes <= 0f) return 0f;

            float ramp = Mathf.Max(0.01f, _fallLookRamp);
            float progress = Mathf.Clamp01((_fallingSeconds - _fallLookDelay) / ramp);

            return -_fallLookLanes * LaneHeight * progress;
        }

        /// <summary>Accumulate how long the character has been genuinely falling.</summary>
        private void TrackFalling()
        {
            if (_host == null || _host.Sim == null)
            {
                _fallingSeconds = 0f;
                return;
            }

            var state = _host.Sim.State;

            bool falling = !state.Grounded
                           && state.Velocity.y < 0f
                           && state.Attachment == AttachmentKind.None;

            _fallingSeconds = falling ? _fallingSeconds + Time.deltaTime : 0f;
        }

        /// <summary>
        /// Keep the framed centre inside the level, so the camera never shows past its edges.
        ///
        /// <para>When the level is shorter than the frame there is nothing useful to clamp to —
        /// every position shows past one edge or the other — so the whole extent is centred and the
        /// framing rule is abandoned rather than fought over.</para>
        /// </summary>
        private float ClampToBounds(float centreY)
        {
            if (!_hasBounds) return centreY;

            float half = VisibleHeight * 0.5f;
            float lowest = _boundsFloorY + half;
            float highest = _boundsCeilingY - half;

            if (lowest > highest) return (_boundsFloorY + _boundsCeilingY) * 0.5f;

            return Mathf.Clamp(centreY, lowest, highest);
        }

        /// <summary>Put the camera on its mark immediately — spawns and scene transitions, where
        /// damping would show a long slide in from wherever the shot used to be.</summary>
        public void SnapToTarget()
        {
            if (_followTarget == null) return;

            _lookAheadValue = 0f;
            _lookAheadVelocity = 0f;
            _fallingSeconds = 0f;
            _fallLookValue = 0f;
            _fallLookVelocity = 0f;

            ApplyFraming();
            _followTarget.position = ResolveTargetPosition();

            if (_camera != null) _camera.PreviousStateIsValid = false;
        }

        private void OnDrawGizmosSelected()
        {
            if (_host == null || _tuning == null) return;

            // Draw the lane bands the shot is composed from — the whole point is that this is a
            // grid you can see, not a set of numbers in an inspector.
            float lane = LaneHeight;
            var centre = _followTarget != null ? _followTarget.position : transform.position;
            float bottom = centre.y - FeetViewportY * VisibleHeight;

            for (int i = 0; i <= Mathf.CeilToInt(VisibleLanes); i++)
            {
                Gizmos.color = i == 0 ? new Color(1f, 1f, 1f, 0.5f) : new Color(0.4f, 0.8f, 1f, 0.35f);
                var y = bottom + i * lane;
                Gizmos.DrawLine(new Vector3(centre.x - 20f, y, 0f), new Vector3(centre.x + 20f, y, 0f));
            }
        }
    }
}
