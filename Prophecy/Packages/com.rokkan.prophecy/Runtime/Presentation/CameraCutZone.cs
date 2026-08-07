using Unity.Cinemachine;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Cutaway style 3 of 3 (Matt): walking into a place changes the camera to display
    /// something. A volume, resolved like a portal — the player's FEET inside the box, no
    /// colliders, presentation-side — and a posed <see cref="CinemachineCamera"/> that
    /// out-prioritises the follow rig while they stand there. Cinemachine does the actual
    /// cut or blend; this component only decides WHEN and how FAST.
    ///
    /// <para><b>The shot is authored as a camera, not as numbers</b>: the zone's shot vcam
    /// is a child (or any referenced vcam) whose transform and lens ARE the framing — place
    /// it in the scene looking at the thing the zone exists to show. Blend seconds 0 is a
    /// hard cut; anything more eases. The brain's default blend is set per transition and
    /// restored on teardown, so a zone cannot permanently re-tempo the game's cameras.</para>
    ///
    /// <para><b>Interaction-driven cuts wait for an interaction system.</b> When one
    /// exists, it can call <see cref="SetHeld"/> to hold the shot for the length of a
    /// conversation or a lever pull — the zone machinery is already the whole mechanism.</para>
    /// </summary>
    public sealed class CameraCutZone : MonoBehaviour
    {
        [SerializeField, Tooltip("The posed shot this zone activates. Its transform and " +
                                 "lens are the framing — aim it at what the zone shows.")]
        private CinemachineCamera _shot;

        [SerializeField, Tooltip("Half-extents of the trigger box, in local space — the " +
                                 "zone's transform can rotate and move it like any volume.")]
        private Vector3 _halfExtents = new Vector3(3f, 2f, 3f);

        [SerializeField, Tooltip("Seconds the transition takes, both ways. Zero is a hard " +
                                 "cut — reveals want cuts, area framing wants a short ease.")]
        private float _blendSeconds = 0.6f;

        [SerializeField, Tooltip("Added to the follow rig's priority while active. Big on " +
                                 "purpose: a zone that fires must WIN.")]
        private int _priorityBoost = 100;

        [Header("Generated shot — used only when no shot vcam is wired or childed")]
        [SerializeField, Tooltip("Shot position in the zone's local space.")]
        private Vector3 _shotLocalPosition = new Vector3(0f, 4f, -8f);

        [SerializeField, Tooltip("Shot rotation, local euler angles.")]
        private Vector3 _shotEulerAngles = new Vector3(30f, 0f, 0f);

        [SerializeField, Tooltip("Shot lens.")]
        private float _shotFieldOfView = 40f;

        private PlayerCharacterHost _host;
        private bool _active;
        private bool _held;
        private CinemachineBlendDefinition _originalBlend;
        private bool _blendStored;

        /// <summary>Future interaction hook: hold the shot regardless of the volume.</summary>
        public void SetHeld(bool held) => _held = held;

        /// <summary>Feet-in-box, in the zone's local space — pure, for the tests.</summary>
        public static bool Contains(Vector3 halfExtents, Vector3 localPoint) =>
            Mathf.Abs(localPoint.x) <= halfExtents.x &&
            Mathf.Abs(localPoint.y) <= halfExtents.y &&
            Mathf.Abs(localPoint.z) <= halfExtents.z;

        private void Awake()
        {
            // Self-assembling, like the camera rigs: a scene generator adds this ONE
            // component with pose numbers and knows nothing about Cinemachine — the editor
            // assembly stays free of dependencies only presentation actually has.
            if (_shot == null) _shot = GetComponentInChildren<CinemachineCamera>();
            if (_shot == null)
            {
                var shotObject = new GameObject("Shot");
                shotObject.transform.SetParent(transform, false);
                shotObject.transform.localPosition = _shotLocalPosition;
                shotObject.transform.localEulerAngles = _shotEulerAngles;
                _shot = shotObject.AddComponent<CinemachineCamera>();
                var lens = _shot.Lens;
                lens.FieldOfView = _shotFieldOfView;
                _shot.Lens = lens;
            }
            _shot.Priority = 0;
        }

        private void Update()
        {
            if (_shot == null) return;

            // Re-resolve until found — the persistent player arrives after this scene's
            // Awake when play starts from the overworld (the camera rig's own lesson).
            if (_host == null)
            {
                _host = FindAnyObjectByType<PlayerCharacterHost>();
                if (_host == null) return;
            }

            bool inside = _held || Contains(
                _halfExtents, transform.InverseTransformPoint(_host.FeetWorldPosition));
            if (inside == _active) return;
            _active = inside;

            var brain = CinemachineBrain.GetActiveBrain(0);
            if (brain != null)
            {
                if (!_blendStored)
                {
                    _originalBlend = brain.DefaultBlend;
                    _blendStored = true;
                }
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    _blendSeconds <= 0.001f
                        ? CinemachineBlendDefinition.Styles.Cut
                        : CinemachineBlendDefinition.Styles.EaseInOut,
                    _blendSeconds);
            }

            _shot.Priority = _active ? 50 + _priorityBoost : 0;
        }

        private void OnDisable()
        {
            if (_shot != null) _shot.Priority = 0;
            if (_blendStored)
            {
                var brain = CinemachineBrain.GetActiveBrain(0);
                if (brain != null) brain.DefaultBlend = _originalBlend;
                _blendStored = false;
            }
            _active = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, _halfExtents * 2f);
        }
    }
}
