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

        [Header("Framing, in lanes")]
        [SerializeField, Tooltip("Lanes of world height the viewport spans.")]
        [Range(1f, 10f)]
        private float _visibleLanes = 4f;

        [SerializeField, Tooltip("Where the player sits when the camera is free: lanes of space below " +
                                 "their feet. 1.5 = the centre lane (half lane + one full), so the body " +
                                 "straddles screen centre. Level bounds may push them off this.")]
        [Range(0f, 4f)]
        private float _lanesBelowFeet = 1.5f;

        [SerializeField, Tooltip("Long lens. Distance is derived from this and the visible height.")]
        [Range(10f, 70f)]
        private float _fieldOfView = 28f;

        [Header("Feel")]
        [SerializeField, Tooltip("Metres the camera leads the facing direction.")]
        private float _lookAhead = 1.8f;

        [SerializeField, Tooltip("Seconds for look-ahead to swing across after a turn.")]
        private float _lookAheadSmoothTime = 0.45f;

        [SerializeField, Tooltip("Quantise vertical framing to whole lanes, so the frame steps " +
                                 "floor by floor instead of drifting. A distinct look — try both.")]
        private bool _snapToLanes;

        private CinemachineCamera _camera;
        private CinemachinePositionComposer _composer;

        private float _lookAheadValue;
        private float _lookAheadVelocity;

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

        /// <summary>World height the viewport spans.</summary>
        public float VisibleHeight => LaneHeight * _visibleLanes;

        /// <summary>
        /// Distance needed to frame <see cref="VisibleHeight"/> at <see cref="_fieldOfView"/>.
        /// Derived rather than authored: framing is the pair, and letting someone set the distance
        /// alone silently changes how big the character reads.
        /// </summary>
        public float CameraDistance =>
            VisibleHeight / (2f * Mathf.Tan(_fieldOfView * 0.5f * Mathf.Deg2Rad));

        /// <summary>Where the player's feet sit vertically, 0 = bottom edge, 1 = top.</summary>
        public float FeetViewportY => _visibleLanes <= 0f ? 0.5f : _lanesBelowFeet / _visibleLanes;

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

            // Centred on purpose — the vertical framing is carried by FocusOffsetY instead.
            var composition = _composer.Composition;
            composition.ScreenPosition = new Vector2(composition.ScreenPosition.x, 0f);
            _composer.Composition = composition;
        }

        private Vector3 ResolveTargetPosition()
        {
            var space = _host != null ? _host.Space : MovementSpace.SideScroll;
            var feet = _host != null
                ? SpaceMapping.ToWorld(_host.CurrentPosition, space, _host.RailDepth)
                : _followTarget.position;

            int facing = _host != null && _host.Sim != null ? _host.Sim.State.Facing : 1;

            _lookAheadValue = Mathf.SmoothDamp(
                _lookAheadValue, facing * _lookAhead, ref _lookAheadVelocity, _lookAheadSmoothTime);

            float y = feet.y;

            // Quantising to the grid the level is built on keeps whole lanes in frame as the
            // player changes floor, rather than parking the shot halfway between two of them.
            if (_snapToLanes && LaneHeight > 0f)
                y = Mathf.Floor(y / LaneHeight + 0.001f) * LaneHeight;

            return new Vector3(feet.x + _lookAheadValue, ClampToBounds(y + FocusOffsetY), feet.z);
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

            for (int i = 0; i <= Mathf.CeilToInt(_visibleLanes); i++)
            {
                Gizmos.color = i == 0 ? new Color(1f, 1f, 1f, 0.5f) : new Color(0.4f, 0.8f, 1f, 0.35f);
                var y = bottom + i * lane;
                Gizmos.DrawLine(new Vector3(centre.x - 20f, y, 0f), new Vector3(centre.x + 20f, y, 0f));
            }
        }
    }
}
