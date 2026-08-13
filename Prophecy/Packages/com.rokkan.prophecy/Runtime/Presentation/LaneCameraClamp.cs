using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The camera's room clamp and door pan — the state machine behind "you cannot see past a
    /// door you have not committed to". The frame's limits slide between rooms, and a committed
    /// crossing pans the camera on the walk's own progress.
    ///
    /// <para><b>Plain C# on purpose.</b> This logic has already shipped two regressions — the
    /// mid-walk lurch (Matt's jar) and the still-sliding respawn — and both were invisible until
    /// someone walked the exact door that showed them. Welded to the rig they could only be
    /// re-tested by walking doors; as values in, values out, every case is pinned by
    /// <c>LaneCameraClampTests</c> with no scene, no Cinemachine and no play mode.
    /// <see cref="LaneCameraRig"/> gathers a <see cref="Frame"/> each update and reads the
    /// effective clamp rect and the pan override back.</para>
    /// </summary>
    public sealed class LaneCameraClamp
    {
        /// <summary>Stands in for "no horizontal limit" — far enough that no authored level
        /// reaches it, finite so the clamp arithmetic stays ordinary.</summary>
        public const float Unbounded = 100000f;

        /// <summary>
        /// Everything one update needs, as plain values. The rig fills this from the sim's
        /// <c>DoorTransit</c>, the scene's <c>RoomBounds</c>, and its own framing numbers;
        /// a test fills it from literals.
        /// </summary>
        public struct Frame
        {
            /// <summary>The room the sim says the body is in.</summary>
            public int Room;

            /// <summary>Mid-crossing. While true, the transit fields below are meaningful.</summary>
            public bool Transiting;

            public int TargetRoom;

            /// <summary>How far through the crossing the body is, 0..1. THE SYNC SIGNAL
            /// (Matt: the transition must move with the walk, not on its own clock).</summary>
            public float TransitProgress;

            /// <summary>True when the crossing runs along X; false for a vertical one.</summary>
            public bool TransitAxisX;

            /// <summary>The axis value the body will be delivered at — known from the step-in,
            /// which is what lets the pan aim at a fixed destination.</summary>
            public float DeliveryAxis;

            /// <summary>The current room's clamp rect (the baseline where none is authored).</summary>
            public (float floor, float ceiling, float minX, float maxX) RoomRect;

            /// <summary>The destination room's clamp rect. Read only while transiting.</summary>
            public (float floor, float ceiling, float minX, float maxX) TargetRect;

            /// <summary>Where the camera's follow target actually stands — the spot a pan
            /// departs from, so a pinned camera leaves from where it is pinned.</summary>
            public float CameraX;
            public float CameraY;

            public float HalfVisibleWidth;
            public float HalfVisibleHeight;
            public float FocusOffsetY;

            /// <summary>Seconds the ordinary (non-transit) slide takes.</summary>
            public float SlideSeconds;

            /// <summary>Presentation delta time. Only the ordinary slide consumes it — the
            /// transit path rides the walk's progress instead of any clock.</summary>
            public float DeltaSeconds;
        }

        private float _floorY;
        private float _ceilingY;
        private float _minX = -Unbounded;
        private float _maxX = Unbounded;

        private float _targetFloorY;
        private float _targetCeilingY;
        private float _targetMinX = -Unbounded;
        private float _targetMaxX = Unbounded;

        private float _floorVelocity;
        private float _ceilingVelocity;
        private float _minXVelocity;
        private float _maxXVelocity;

        private int _lastRoom = int.MinValue;

        private bool _panActive;
        private bool _panAxisX;
        private float _panFrom;
        private float _panTo;
        private float _panBlend;

        /// <summary>The effective clamp: the camera never frames below this.</summary>
        public float FloorY => _floorY;

        /// <summary>The effective clamp: the camera never frames above this.</summary>
        public float CeilingY => _ceilingY;

        /// <summary>The effective clamp: the frame's centre keeps the visible width east of this.</summary>
        public float MinX => _minX;

        /// <summary>The effective clamp: the frame's centre keeps the visible width west of this.</summary>
        public float MaxX => _maxX;

        /// <summary>Mid-crossing. While true the pan owns its axis outright and the rig must
        /// ride <see cref="PanPosition"/> instead of following the body on that axis.</summary>
        public bool PanActive => _panActive;

        /// <summary>Which axis the pan owns. The other keeps following normally.</summary>
        public bool PanAxisX => _panAxisX;

        /// <summary>Where the pan's axis sits right now: a fixed glide from where the camera
        /// stood at the step-in to the destination frame, ridden on the walk's progress.</summary>
        public float PanPosition => Mathf.Lerp(_panFrom, _panTo, _panBlend);

        /// <summary>Where the pan departed from — frozen at the step-in. For the tests.</summary>
        public float PanFrom => _panFrom;

        /// <summary>Where the pan is delivering the frame — aimed once. For the tests.</summary>
        public float PanTo => _panTo;

        /// <summary>
        /// A new scene's descriptor globals — the BASELINE every room without its own bounds
        /// falls back to. Clamps and targets land on it outright and the horizontal limits
        /// open fully; the rig snaps a <see cref="Drive"/> straight after, because a load is
        /// not a place to watch a slide.
        /// </summary>
        public void SetBaseline(float floorY, float ceilingY)
        {
            _floorY = floorY;
            _ceilingY = ceilingY;
            _targetFloorY = floorY;
            _targetCeilingY = ceilingY;
            _minX = _targetMinX = -Unbounded;
            _maxX = _targetMaxX = Unbounded;
            _lastRoom = int.MinValue;
            _panActive = false;
        }

        /// <summary>
        /// A scene with no camera bounds also has no rooms to clamp by — the horizontal
        /// clamps and the room memory must reset, or a roomed scene's stale X-clamps would
        /// pin the camera in the next scene.
        /// </summary>
        public void Clear()
        {
            _minX = _targetMinX = -Unbounded;
            _maxX = _targetMaxX = Unbounded;
            _lastRoom = int.MinValue;
            _panActive = false;
        }

        /// <summary>
        /// One update: slide the clamp to the current room's bounds, and mid-crossing run the
        /// pan. <paramref name="snap"/> lands everything outright — spawns and scene
        /// transitions, where a slide would show the camera settling.
        /// </summary>
        public void Drive(in Frame frame, bool snap = false)
        {
            // Mid-crossing, the slide is SYNCED to the walk (Matt): the clamps blend by the
            // transit's own progress, so the frame arrives exactly when the feet do — no
            // separate clock to drift against. On completion the room change lands with the
            // clamps already at the destination, and the ordinary path below has nothing
            // left to move.
            if (!snap && frame.Transiting)
            {
                var from = frame.RoomRect;
                var to = frame.TargetRect;
                float t = Mathf.SmoothStep(0f, 1f, frame.TransitProgress);

                // The PAN, aimed once at the step-in: from wherever the camera actually is
                // to exactly where the delivery point sits under the destination room's
                // clamps. Blending only the RECTS released the pinned camera mid-walk and it
                // lurched after the player — Matt's jar. The pan owns the crossing axis
                // outright; the rig rides it by the walk's own progress.
                if (!_panActive)
                {
                    _panActive = true;
                    _panAxisX = frame.TransitAxisX;

                    if (_panAxisX)
                    {
                        float half = frame.HalfVisibleWidth;
                        _panFrom = frame.CameraX;
                        _panTo = to.minX + half > to.maxX - half
                            ? (to.minX + to.maxX) * 0.5f
                            : Mathf.Clamp(frame.DeliveryAxis, to.minX + half, to.maxX - half);
                    }
                    else
                    {
                        float half = frame.HalfVisibleHeight;
                        _panFrom = frame.CameraY;
                        _panTo = to.floor + half > to.ceiling - half
                            ? (to.floor + to.ceiling) * 0.5f
                            : Mathf.Clamp(frame.DeliveryAxis + frame.FocusOffsetY,
                                          to.floor + half, to.ceiling - half);
                    }
                }

                _panBlend = t;

                _floorY = Mathf.Lerp(from.floor, to.floor, t);
                _ceilingY = Mathf.Lerp(from.ceiling, to.ceiling, t);
                _minX = Mathf.Lerp(from.minX, to.minX, t);
                _maxX = Mathf.Lerp(from.maxX, to.maxX, t);

                _targetFloorY = to.floor;
                _targetCeilingY = to.ceiling;
                _targetMinX = to.minX;
                _targetMaxX = to.maxX;
                _floorVelocity = _ceilingVelocity = _minXVelocity = _maxXVelocity = 0f;
                _lastRoom = frame.TargetRoom;
                return;
            }

            _panActive = false;

            if (frame.Room != _lastRoom)
            {
                _lastRoom = frame.Room;

                _targetFloorY = frame.RoomRect.floor;
                _targetCeilingY = frame.RoomRect.ceiling;
                _targetMinX = frame.RoomRect.minX;
                _targetMaxX = frame.RoomRect.maxX;
            }

            // A snap lands the clamps outright even when the ROOM did not change — a body can
            // be re-placed while the previous room slide is still in flight, and a snap that
            // only finished on room changes would reveal a camera still settling.
            if (snap)
            {
                _floorY = _targetFloorY;
                _ceilingY = _targetCeilingY;
                _minX = _targetMinX;
                _maxX = _targetMaxX;
                _floorVelocity = _ceilingVelocity = _minXVelocity = _maxXVelocity = 0f;
                return;
            }

            _floorY = Mathf.SmoothDamp(_floorY, _targetFloorY, ref _floorVelocity,
                                       frame.SlideSeconds, Mathf.Infinity, frame.DeltaSeconds);
            _ceilingY = Mathf.SmoothDamp(_ceilingY, _targetCeilingY, ref _ceilingVelocity,
                                         frame.SlideSeconds, Mathf.Infinity, frame.DeltaSeconds);
            _minX = Mathf.SmoothDamp(_minX, _targetMinX, ref _minXVelocity,
                                     frame.SlideSeconds, Mathf.Infinity, frame.DeltaSeconds);
            _maxX = Mathf.SmoothDamp(_maxX, _targetMaxX, ref _maxXVelocity,
                                     frame.SlideSeconds, Mathf.Infinity, frame.DeltaSeconds);
        }
    }
}
