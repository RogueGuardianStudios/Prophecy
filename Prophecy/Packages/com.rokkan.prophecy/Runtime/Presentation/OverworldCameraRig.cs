using Rokkan.Prophecy.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The overworld's camera: a fixed three-quarter view that follows the player across the map.
    /// Think Tunic — pitched well down, yawed off the grid, a long lens flattening the perspective,
    /// and a rotation that never, ever changes.
    ///
    /// <para><b>Per-space and scene-owned, not a mode on <see cref="LaneCameraRig"/>.</b> Every
    /// number on the lane rig is side-scroll vocabulary — lanes, jump-height dead zones, fall
    /// look — and a top-down frame shares none of it. So each world scene carries the camera that
    /// knows how to shoot it: this component lives in the overworld scene at a higher Cinemachine
    /// priority than the Bootstrap lane rig, wins while its scene is loaded, and takes the answer
    /// with it when the scene unloads. Nothing hands cameras over; existence is the handover.</para>
    ///
    /// <para><b>Follow only, no Aim — same law as the lane rig.</b> The rotation is authored here
    /// and applied as a constant. An Aim component would swing the view as the player crosses the
    /// frame, and a world map read at an angle that drifts is a map you cannot build a mental
    /// picture of. Tunic's stillness is the point being borrowed.</para>
    ///
    /// <para>Framing follows the project rule: the authored number is the character's share of the
    /// viewport, and the camera distance is derived. The share is smaller than side-scroll's 0.2
    /// because an overworld character is a token on a map, not the subject of a shot.</para>
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class OverworldCameraRig : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField, Tooltip("Supplies the stand height the framing is derived from.")]
        private MovementTuning _tuning;

        [SerializeField, Tooltip("Leave empty to find the persistent player.")]
        private PlayerCharacterHost _host;

        [SerializeField, Tooltip("The transform Cinemachine follows. Driven by this component.")]
        private Transform _followTarget;

        [Header("Angle — fixed, forever")]
        [SerializeField, Range(20f, 85f), Tooltip("Downward tilt. Higher reads more like a map, " +
                                                  "lower shows more of the world's faces.")]
        private float _pitch = 50f;

        [SerializeField, Range(-180f, 180f), Tooltip("Rotation off the world grid. 45 is the " +
                                                     "classic three-quarter diagonal.")]
        private float _yaw = 45f;

        [Header("Framing")]
        [SerializeField, Range(0.03f, 0.4f), Tooltip("How much of the viewport's height the " +
                                                     "standing character fills. The framing " +
                                                     "decision — distance is derived from it.")]
        private float _characterViewportShare = 0.13f;

        [SerializeField, Range(10f, 70f), Tooltip("Long lens, flattened perspective. Longer than " +
                                                  "the side-scroll lens because the flattening IS " +
                                                  "the look.")]
        private float _fieldOfView = 22f;

        [Header("Follow")]
        [SerializeField, Tooltip("Cinemachine priority. Above the Bootstrap lane rig's, which is " +
                                 "the entire handover: this camera wins while its scene is loaded " +
                                 "and stops existing when it unloads.")]
        private int _priority = 50;

        [SerializeField, Tooltip("Seconds to close a gap. Gentle but present — 0.7 read as the " +
                                 "camera towing behind once the walk speed came up to match the " +
                                 "side-scroll run.")]
        private float _damping = 0.45f;

        [SerializeField, Tooltip("Dead zone as a fraction of the screen, per axis. Small: the " +
                                 "character stays near centre so the map scrolls evenly all ways.")]
        private Vector2 _deadZone = new Vector2(0.03f, 0.04f);

        private CinemachineCamera _camera;
        private CinemachinePositionComposer _composer;

        /// <summary>Standing body height in metres, from tuning.</summary>
        public float StandHeight => _tuning != null ? _tuning.Data.StandHeight : 1.8f;

        /// <summary>World height the viewport spans at the character, derived from the share.</summary>
        public float VisibleHeight =>
            _characterViewportShare <= 0.0001f ? 14f : StandHeight / _characterViewportShare;

        /// <summary>Distance needed to frame <see cref="VisibleHeight"/> at the authored lens.</summary>
        public float CameraDistance =>
            VisibleHeight / (2f * Mathf.Tan(_fieldOfView * 0.5f * Mathf.Deg2Rad));

        private void Awake()
        {
            _camera = GetComponent<CinemachineCamera>();

            // Self-assembling, so the scene generator can add this one component and know nothing
            // about Cinemachine — the same reason EnemyBuilder attaches GOAP by reflection. The
            // editor assembly stays free of dependencies that only presentation actually has.
            _composer = GetComponent<CinemachinePositionComposer>();
            if (_composer == null) _composer = gameObject.AddComponent<CinemachinePositionComposer>();

            _camera.Priority = _priority;

            if (_host == null) _host = FindAnyObjectByType<PlayerCharacterHost>();

            if (_followTarget == null)
            {
                var created = new GameObject("OverworldCameraTarget");
                created.transform.SetParent(transform.parent, false);
                _followTarget = created.transform;
            }

            _camera.Follow = _followTarget;
            _camera.LookAt = null;   // Follow only. See the class note, and LaneCameraRig's.

            ApplyFraming();
        }

        private void Start()
        {
            // On its mark before the first rendered frame. This camera activates by out-ranking
            // the lane rig on a scene load, which is exactly when a slide in from wherever the old
            // shot was would read as the world lurching.
            SnapToTarget();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && _camera != null) ApplyFraming();
        }

        private void Update()
        {
            if (_followTarget == null) return;

            ApplyFraming();
            _followTarget.position = ResolveTargetPosition();
        }

        /// <summary>Push the derived numbers onto Cinemachine every frame, so retuning the share
        /// or the angle in play mode reframes immediately — same live surface as the lane rig.</summary>
        private void ApplyFraming()
        {
            // The whole Tunic contract in one line: the rotation is a constant, applied rather
            // than trusted, so nothing — not a bump, not an animation, not a well-meaning script —
            // can leave the map at a new angle.
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            var lens = _camera.Lens;
            lens.FieldOfView = _fieldOfView;
            _camera.Lens = lens;

            if (_composer == null) return;

            _composer.CameraDistance = CameraDistance;

            var composition = _composer.Composition;
            composition.ScreenPosition = Vector2.zero;
            composition.DeadZone.Enabled = true;
            composition.DeadZone.Size = _deadZone;
            _composer.Composition = composition;

            _composer.Damping = new Vector3(_damping, _damping, _damping);
        }

        private Vector3 ResolveTargetPosition()
        {
            if (_host == null) return _followTarget.position;

            return SpaceMapping.ToWorld(_host.CurrentPosition, _host.Space, _host.RailDepth);
        }

        /// <summary>Put the camera on its mark immediately — arrivals, where damping would show
        /// the shot sliding across the whole map to find the player.</summary>
        public void SnapToTarget()
        {
            if (_followTarget == null) return;

            ApplyFraming();
            _followTarget.position = ResolveTargetPosition();

            if (_camera != null) _camera.PreviousStateIsValid = false;
        }
    }
}
