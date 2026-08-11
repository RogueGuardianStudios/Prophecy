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
    /// <para><b>The frame is sized by the character, not by a lane count.</b> It was four lanes
    /// once, which was a number you had to do arithmetic on before you knew the thing that
    /// actually matters — and it only held for one set of tuning, since retuning the body or the
    /// lane silently resized the character on screen. Now the share is authored and the height
    /// derived, so the character reads the same through any retune. The feet still sit below
    /// centre: a naively centred camera spends a quarter of the screen on void beneath the floor,
    /// which reads as the character having sunk.</para>
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
        [SerializeField, Tooltip("Vertical dead zone, as a multiple of JUMP HEIGHT. Anything above " +
                                 "1.0 means an ordinary jump moves the frame not at all, while a " +
                                 "sustained climb still pushes past it. Derived from the jump rather " +
                                 "than authored in lanes or in screen fractions, because clearing the " +
                                 "apex is the property this exists for — and it is the only one of " +
                                 "the three that stays true when either the jump or the framing is " +
                                 "retuned.")]
        [Range(1f, 2f)]
        private float _verticalDeadZoneJumps = 1.1f;

        [SerializeField, Tooltip("Horizontal dead zone as a fraction of screen WIDTH. In screen units " +
                                 "rather than lanes because the visible width changes with aspect ratio, " +
                                 "so a lane-based value would mean something different on every monitor.")]
        [Range(0f, 0.5f)]
        private float _horizontalDeadZone = 0.06f;

        [SerializeField, Tooltip("Seconds for the camera to close a horizontal gap.")]
        private float _dampingHorizontal = 0.35f;

        [SerializeField, Tooltip("Seconds for the camera to close a vertical gap. Cut from 0.6 when " +
                                 "framing moved to a 20% character share: damping is a TIME, so it " +
                                 "did not change, but the frame shrank 14.4 m -> 9 m and the same " +
                                 "lag in metres became 1.6x larger a share of the screen. Scaled by " +
                                 "the same 1.6 to hold the apparent responsiveness.")]
        private float _dampingVertical = 0.375f;

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

        [SerializeField, Tooltip("Seconds the clamp takes to SLIDE to a new room's bounds — " +
                                 "the Metroid pan, as smooth reframing rather than a cut.")]
        private float _roomSlideSeconds = 0.5f;

        private float _baseFloorY;
        private float _baseCeilingY;
        private int _lastRoom = int.MinValue;
        private float _floorVelocity;
        private float _ceilingVelocity;
        private RoomBounds[] _roomBounds = System.Array.Empty<RoomBounds>();

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

            // The descriptor's globals are the BASELINE every room without its own bounds
            // falls back to. An arrival re-learns the scene's rooms and snaps to whichever
            // one the player was seeded into — a load is not a place to watch a slide.
            _baseFloorY = floorY;
            _baseCeilingY = ceilingY;
            _targetFloorY = floorY;
            _targetCeilingY = ceilingY;
            _boundsMinX = _targetMinX = -100000f;
            _boundsMaxX = _targetMaxX = 100000f;
            _roomBounds = FindObjectsByType<RoomBounds>(FindObjectsSortMode.None);
            _lastRoom = int.MinValue;
            _panActive = false;
            UpdateRoomBounds(snap: true);
        }

        /// <summary>Scenes without camera bounds also have no rooms to clamp by — the room
        /// cache and the horizontal clamps must reset here too, or a roomed scene's stale
        /// X-clamps would pin the camera in the next scene.</summary>
        public void ClearVerticalBounds()
        {
            _hasBounds = false;
            _boundsMinX = _targetMinX = -100000f;
            _boundsMaxX = _targetMaxX = 100000f;
            _roomBounds = System.Array.Empty<RoomBounds>();
            _lastRoom = int.MinValue;
            _panActive = false;
        }

        /// <summary>
        /// Slide the clamp to the current room's bounds. Rooms are the sim's fact (graph
        /// state, changed only at doors); this is its presentation — the frame's limits
        /// reframe smoothly as the body walks through, which IS the Metroid room slide in a
        /// camera that otherwise just follows.
        /// </summary>
        private void UpdateRoomBounds(bool snap = false)
        {
            if (_host == null || _host.Sim == null) return;

            // Mid-crossing, the slide is SYNCED to the walk (Matt): the clamps blend by the
            // transit's own progress, so the frame arrives exactly when the feet do — no
            // separate clock to drift against. On completion the room change lands with the
            // clamps already at the destination, and the ordinary path below has nothing
            // left to move.
            var transit = _host.Sim.Get<Rokkan.Prophecy.Sim.Abilities.DoorTransit>();
            if (!snap && transit != null && transit.IsTransiting)
            {
                var from = BoundsFor(_host.Sim.State.Room);
                var to = BoundsFor(transit.TargetRoom);
                float t = Mathf.SmoothStep(0f, 1f, transit.Progress);

                // The PAN, aimed once at the step-in: from wherever the camera actually is
                // to exactly where the delivery point sits under the destination room's
                // clamps. Blending only the RECTS released the pinned camera mid-walk and it
                // lurched after the player — Matt's jar. The pan owns the crossing axis
                // outright; ResolveTargetPosition rides it by the walk's own progress.
                if (!_panActive)
                {
                    _panActive = true;
                    _panAxisX = transit.TransitAxisX;

                    if (_panAxisX)
                    {
                        float half = HalfVisibleWidth();
                        _panFrom = _followTarget != null ? _followTarget.position.x : 0f;
                        _panTo = to.minX + half > to.maxX - half
                            ? (to.minX + to.maxX) * 0.5f
                            : Mathf.Clamp(transit.DeliveryAxis, to.minX + half, to.maxX - half);
                    }
                    else
                    {
                        float half = VisibleHeight * 0.5f;
                        _panFrom = _followTarget != null ? _followTarget.position.y : 0f;
                        _panTo = to.floor + half > to.ceiling - half
                            ? (to.floor + to.ceiling) * 0.5f
                            : Mathf.Clamp(transit.DeliveryAxis + FocusOffsetY,
                                          to.floor + half, to.ceiling - half);
                    }
                }

                _panBlend = t;

                _boundsFloorY = Mathf.Lerp(from.floor, to.floor, t);
                _boundsCeilingY = Mathf.Lerp(from.ceiling, to.ceiling, t);
                _boundsMinX = Mathf.Lerp(from.minX, to.minX, t);
                _boundsMaxX = Mathf.Lerp(from.maxX, to.maxX, t);

                _targetFloorY = to.floor;
                _targetCeilingY = to.ceiling;
                _targetMinX = to.minX;
                _targetMaxX = to.maxX;
                _floorVelocity = _ceilingVelocity = _minXVelocity = _maxXVelocity = 0f;
                _lastRoom = transit.TargetRoom;
                return;
            }

            _panActive = false;

            int room = _host.Sim.State.Room;
            if (room != _lastRoom)
            {
                _lastRoom = room;

                _targetFloorY = _baseFloorY;
                _targetCeilingY = _baseCeilingY;
                _targetMinX = -100000f;
                _targetMaxX = 100000f;

                for (int i = 0; i < _roomBounds.Length; i++)
                {
                    if (_roomBounds[i] == null || _roomBounds[i].Room != room) continue;
                    _targetFloorY = _roomBounds[i].FloorY;
                    _targetCeilingY = _roomBounds[i].CeilingY;
                    _targetMinX = _roomBounds[i].MinX;
                    _targetMaxX = _roomBounds[i].MaxX;
                    break;
                }

                if (snap)
                {
                    _boundsFloorY = _targetFloorY;
                    _boundsCeilingY = _targetCeilingY;
                    _boundsMinX = _targetMinX;
                    _boundsMaxX = _targetMaxX;
                    _floorVelocity = _ceilingVelocity = _minXVelocity = _maxXVelocity = 0f;
                }
            }

            if (snap) return;

            _boundsFloorY = Mathf.SmoothDamp(_boundsFloorY, _targetFloorY,
                                             ref _floorVelocity, _roomSlideSeconds);
            _boundsCeilingY = Mathf.SmoothDamp(_boundsCeilingY, _targetCeilingY,
                                               ref _ceilingVelocity, _roomSlideSeconds);
            _boundsMinX = Mathf.SmoothDamp(_boundsMinX, _targetMinX,
                                           ref _minXVelocity, _roomSlideSeconds);
            _boundsMaxX = Mathf.SmoothDamp(_boundsMaxX, _targetMaxX,
                                           ref _maxXVelocity, _roomSlideSeconds);
        }

        /// <summary>One room's clamp rect, with the descriptor baseline for rooms that never
        /// authored bounds of their own.</summary>
        private (float floor, float ceiling, float minX, float maxX) BoundsFor(int room)
        {
            for (int i = 0; i < _roomBounds.Length; i++)
            {
                if (_roomBounds[i] == null || _roomBounds[i].Room != room) continue;
                return (_roomBounds[i].FloorY, _roomBounds[i].CeilingY,
                        _roomBounds[i].MinX, _roomBounds[i].MaxX);
            }

            return (_baseFloorY, _baseCeilingY, -100000f, 100000f);
        }

        private float _targetFloorY;
        private float _targetCeilingY;
        private float _boundsMinX = -100000f;
        private float _boundsMaxX = 100000f;
        private float _targetMinX = -100000f;
        private float _targetMaxX = 100000f;
        private float _minXVelocity;
        private float _maxXVelocity;

        private bool _panActive;
        private bool _panAxisX;
        private float _panFrom;
        private float _panTo;
        private float _panBlend;

        private float HalfVisibleWidth() =>
            VisibleHeight * 0.5f *
            (Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f);

        /// <summary>
        /// The horizontal half of "you cannot see past a door you have not committed to":
        /// the frame's centre stays where the visible width fits inside the current room.
        /// A room narrower than the frame centres, the same surrender as the vertical rule.
        /// </summary>
        private float ClampXToBounds(float centreX)
        {
            float halfWidth = HalfVisibleWidth();

            float lowest = _boundsMinX + halfWidth;
            float highest = _boundsMaxX - halfWidth;

            if (lowest > highest) return (_boundsMinX + _boundsMaxX) * 0.5f;

            return Mathf.Clamp(centreX, lowest, highest);
        }

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

        /// <summary>Jump apex height in metres, from tuning.</summary>
        public float JumpHeight => _tuning != null ? _tuning.Data.JumpHeight : 2.4f;

        /// <summary>
        /// Vertical dead zone in metres — how far the body may drift before the frame follows.
        ///
        /// <para><b>Measured in jumps, and that is the whole point.</b> It was authored in lanes,
        /// which was fine only while the viewport was also authored in lanes. Once framing became
        /// a share of the character, the two units drifted: the same 2.88 m dead zone went from a
        /// fifth of the frame to a third, and the obvious correction — restore the old fifth —
        /// would have put it at 1.8 m, below the 2.4 m apex, so every hop would have started
        /// shoving the camera.</para>
        ///
        /// <para>Clearing the apex is the property the number was chosen for. Deriving it from the
        /// jump keeps that true through any retune of either the jump or the frame.</para>
        /// </summary>
        public float VerticalDeadZone => JumpHeight * _verticalDeadZoneJumps;

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
            UpdateRoomBounds();
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
                VisibleHeight <= 0f ? 0.2f : Mathf.Clamp01(2f * VerticalDeadZone / VisibleHeight));

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

            float targetX = ClampXToBounds(feet.x + _lookAheadValue);
            float targetY = ClampToBounds(y + FocusOffsetY + _fallLookValue);

            // Mid-crossing the pan owns its axis: a fixed glide from where the camera stood
            // at the step-in to the destination frame, ridden on the walk's own progress —
            // the other axis keeps following normally.
            if (_panActive)
            {
                if (_panAxisX) targetX = Mathf.Lerp(_panFrom, _panTo, _panBlend);
                else targetY = Mathf.Lerp(_panFrom, _panTo, _panBlend);
            }

            return new Vector3(targetX, targetY, feet.z);
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
