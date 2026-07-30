using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Something that can be hit. Publishes a hurtbox to the <see cref="CombatDirector"/> and
    /// decides what an incoming hit means.
    ///
    /// <para><b>Presentation, deliberately.</b> A training dummy has no simulation — it does not
    /// move, fall or decide anything, so giving it a <see cref="CharacterSim"/> would be ceremony.
    /// What it needs is a hurtbox in the right place and a visible reaction, which is exactly what
    /// a MonoBehaviour is for. When enemies arrive they will own a sim <i>and</i> one of these; the
    /// hurtbox half does not change.</para>
    ///
    /// <para><b>The id is assigned, not authored.</b> Hit dedup and hit routing both key on it, so
    /// it has to be unique and non-zero; leaving that to whoever drags the component in is a bug
    /// waiting for a duplicate. Stable within a session is all that is required, because nothing
    /// persists it yet.</para>
    ///
    /// <para>Reacting is a flash and a shove, and neither touches the sim. Knockback that moved a
    /// simulated character would have to go through its velocity on the tick, not through the
    /// transform — this is a dummy, and the shove is honest about being a visual.</para>
    /// </summary>
    public sealed class Combatant : MonoBehaviour
    {
        private static int _nextId = 1;

        [Header("Identity")]
        [SerializeField, Tooltip("Faction. Attacks skip their own team; 0 is neutral and hit by everyone.")]
        private int _team = 2;

        [Header("Hurtbox")]
        [SerializeField, Tooltip("Size of the hittable volume, in metres.")]
        private Vector2 _size = new Vector2(0.9f, 1.8f);

        [SerializeField, Tooltip("Offset from this transform to the box centre. The default puts a " +
                                 "body-sized box on top of a transform sitting at the feet.")]
        private Vector2 _offset = new Vector2(0f, 0.9f);

        [Header("Health")]
        [SerializeField] private int _maxHealth = 60;

        [SerializeField, Tooltip("Seconds before a dead dummy comes back. Zero leaves it down — " +
                                 "which is what a real enemy will want.")]
        private float _reviveSeconds = 2f;

        [Header("Reaction")]
        [SerializeField] private Color _flashColour = new Color(1f, 0.35f, 0.25f);
        [SerializeField] private float _flashSeconds = 0.12f;
        [SerializeField, Tooltip("How far a hit shoves this, in metres. Visual only — nothing is simulated.")]
        private float _knockback = 0.25f;
        [SerializeField] private float _knockbackRecoverySeconds = 0.35f;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private Vector3 _restPosition;

        private float _flashRemaining;
        private float _shove;
        private int _shoveDirection = 1;
        private float _reviveRemaining;

        /// <summary>Unique within the session. What hit routing and hit dedup key on.</summary>
        public int CombatId { get; private set; }

        public int Team => _team;
        public int Health { get; private set; }
        public int MaxHealth => _maxHealth;
        public bool IsAlive => Health > 0;

        /// <summary>Sim tick of the most recent hit taken, for the overlay. Long.MinValue if never.</summary>
        public long LastHitTick { get; private set; } = long.MinValue;

        /// <summary>Total damage taken since the last revive. The number a tuning pass reads.</summary>
        public int DamageTaken { get; private set; }

        private void Awake()
        {
            CombatId = _nextId++;
            Health = _maxHealth;
            _restPosition = transform.position;

            _renderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (CombatDirector.Instance != null) CombatDirector.Instance.Register(this);
        }

        private void OnDisable()
        {
            if (CombatDirector.Instance != null) CombatDirector.Instance.Unregister(this);
        }

        /// <summary>The volume attacks resolve against, in the sim's plane.</summary>
        public Hurtbox BuildHurtbox(MovementSpace space)
        {
            var plane = SpaceMapping.ToPlane(_restPosition, space);
            return new Hurtbox(CombatId, plane + _offset, _size * 0.5f, 0f, _team);
        }

        /// <summary>
        /// Take a hit. Called by the director on the tick it lands, so the damage is applied inside
        /// the fixed step even though everything visual about it is not.
        /// </summary>
        public void ReceiveHit(in HitEvent hit)
        {
            if (!IsAlive) return;

            Health = Mathf.Max(0, Health - hit.Damage);
            DamageTaken += hit.Damage;
            LastHitTick = hit.Tick;

            _flashRemaining = _flashSeconds;
            _shove = _knockback;
            _shoveDirection = hit.Facing < 0 ? -1 : 1;

            if (!IsAlive) _reviveRemaining = _reviveSeconds;
        }

        public void Revive()
        {
            Health = _maxHealth;
            DamageTaken = 0;
            _reviveRemaining = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (!IsAlive && _reviveSeconds > 0f)
            {
                _reviveRemaining -= dt;
                if (_reviveRemaining <= 0f) Revive();
            }

            if (_shove > 0f)
            {
                _shove = Mathf.Max(0f, _shove - _knockback * dt / Mathf.Max(0.0001f, _knockbackRecoverySeconds));
                transform.position = _restPosition + new Vector3(_shove * _shoveDirection, 0f, 0f);
            }
            else if (transform.position != _restPosition)
            {
                transform.position = _restPosition;
            }

            if (_flashRemaining > 0f) _flashRemaining -= dt;

            ApplyTint();
        }

        /// <summary>
        /// Tint through a property block rather than the material. Touching <c>renderer.material</c>
        /// instantiates a copy per object and leaks it, which in a scene full of dummies is a pile
        /// of materials nobody asked for.
        /// </summary>
        private void ApplyTint()
        {
            if (_renderers == null) return;

            Color tint = !IsAlive
                ? new Color(0.25f, 0.25f, 0.28f)
                : _flashRemaining > 0f
                    ? _flashColour
                    : Color.Lerp(Color.white, new Color(0.9f, 0.35f, 0.35f),
                                 1f - Health / (float)Mathf.Max(1, _maxHealth));

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;

                _renderers[i].GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", tint);
                _block.SetColor("_Color", tint);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        private void OnDrawGizmos()
        {
            var centre = Application.isPlaying ? _restPosition : transform.position;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(centre + new Vector3(_offset.x, _offset.y, 0f),
                                new Vector3(_size.x, _size.y, 0.2f));
        }
    }
}
