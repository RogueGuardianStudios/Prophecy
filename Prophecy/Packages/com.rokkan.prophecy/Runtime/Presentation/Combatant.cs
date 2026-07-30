using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Something that can be hit. Publishes a hurtbox to the <see cref="CombatDirector"/> and
    /// routes what comes back.
    ///
    /// <para><b>Two kinds of target, one component.</b> A training dummy has no simulation — it
    /// does not move, fall or decide anything — so its hurtbox is a box on a transform and its
    /// health is a local number. A simulated character's hurtbox has to follow its feet, shrink
    /// when it crouches, and route hits into <c>CharacterSim.ReceiveHit</c> so the damage gates get
    /// their say. Assigning <see cref="_simHost"/> switches between them.</para>
    ///
    /// <para>That is why crouching under a high swing needs no rule anywhere: a simulated hurtbox
    /// is built from <c>BodySize</c>, which is already the crouch height when crouched, so the
    /// geometry misses on its own.</para>
    ///
    /// <para><b>The id is assigned, not authored.</b> Hit dedup and hit routing both key on it, so
    /// it has to be unique and non-zero; leaving that to whoever drags the component in is a bug
    /// waiting for a duplicate. When there is a sim, the id is pushed into it so a character's
    /// hurtbox and their attacks cannot disagree about who they belong to.</para>
    /// </summary>
    public sealed class Combatant : MonoBehaviour
    {
        private static int _nextId = 1;

        [Header("Identity")]
        [SerializeField, Tooltip("Faction. Attacks skip their own team; 0 is neutral and hit by everyone.")]
        private int _team = 2;

        [SerializeField, Tooltip("Leave empty for a dummy. Assign a host and the hurtbox follows " +
                                 "the simulated body and hits go through its damage gates.")]
        private PlayerCharacterHost _simHost;

        [Header("Hurtbox (dummies only)")]
        [SerializeField, Tooltip("Size of the hittable volume, in metres.")]
        private Vector2 _size = new Vector2(0.9f, 1.8f);

        [SerializeField, Tooltip("Offset from this transform to the box centre. The default puts a " +
                                 "body-sized box on top of a transform sitting at the feet.")]
        private Vector2 _offset = new Vector2(0f, 0.9f);

        [Header("Health (dummies only)")]
        [SerializeField] private int _maxHealth = 60;

        [SerializeField, Tooltip("Seconds before a dead dummy comes back. Zero leaves it down — " +
                                 "which is what a real enemy will want.")]
        private float _reviveSeconds = 2f;

        [Header("Reaction")]
        [SerializeField] private Color _flashColour = new Color(1f, 0.35f, 0.25f);
        [SerializeField] private Color _blockColour = new Color(0.55f, 0.8f, 1f);
        [SerializeField] private Color _parryColour = new Color(1f, 0.95f, 0.5f);
        [SerializeField] private float _flashSeconds = 0.12f;
        [SerializeField, Tooltip("How far a hit shoves a dummy, in metres. Simulated characters " +
                                 "are knocked back by the sim instead and ignore this.")]
        private float _knockback = 0.25f;
        [SerializeField] private float _knockbackRecoverySeconds = 0.35f;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private Vector3 _restPosition;

        private Color _flashTint;
        private float _flashRemaining;
        private float _shove;
        private int _shoveDirection = 1;
        private float _reviveRemaining;
        private int _dummyHealth;

        /// <summary>Unique within the session. What hit routing and hit dedup key on.</summary>
        public int CombatId { get; private set; }

        public int Team => _team;

        /// <summary>True when this is a simulated character rather than a dummy.</summary>
        public bool IsSimulated => _simHost != null && _simHost.Sim != null;

        public int Health => IsSimulated ? _simHost.Sim.Vitals.Health : _dummyHealth;

        public int MaxHealth => IsSimulated ? _simHost.Sim.Vitals.MaxHealth : _maxHealth;

        public bool IsAlive => Health > 0;

        /// <summary>Sim tick of the most recent hit taken, for the overlay. Long.MinValue if never.</summary>
        public long LastHitTick { get; private set; } = long.MinValue;

        /// <summary>What the last incoming hit turned into.</summary>
        public HitOutcome LastOutcome { get; private set; } = HitOutcome.Ignored;

        /// <summary>Total damage taken since the last revive. The number a tuning pass reads.</summary>
        public int DamageTaken { get; private set; }

        private void Awake()
        {
            CombatId = _nextId++;
            _dummyHealth = _maxHealth;
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

        /// <summary>
        /// Push this combatant's identity into the sim it fronts.
        ///
        /// <para>Done lazily rather than in <c>Awake</c>: the host builds its sim in its own
        /// <c>Awake</c> and nothing orders the two, so the only reliable moment is the first time
        /// the identity is actually needed.</para>
        /// </summary>
        private void SyncIdentity()
        {
            if (!IsSimulated) return;

            var state = _simHost.Sim.State;
            state.CombatId = CombatId;
            state.Team = _team;
        }

        /// <summary>The volume attacks resolve against, in the sim's plane.</summary>
        public Hurtbox BuildHurtbox(MovementSpace space)
        {
            if (IsSimulated)
            {
                SyncIdentity();

                var state = _simHost.Sim.State;
                return Hurtbox.ForBody(CombatId, state.Position, state.BodySize, _team);
            }

            var plane = SpaceMapping.ToPlane(_restPosition, space);
            return new Hurtbox(CombatId, plane + _offset, _size * 0.5f, 0f, _team);
        }

        /// <summary>
        /// Take a hit, and say what it turned into.
        ///
        /// <para>A simulated character's answer comes from its damage gates, which is where every
        /// defensive rule lives. A dummy has no gates and no opinion, so it simply takes it.</para>
        /// </summary>
        public HitResult ReceiveHit(in HitEvent hit)
        {
            if (!IsAlive) return HitResult.Ignored;

            LastHitTick = hit.Tick;

            HitResult result;

            if (IsSimulated)
            {
                SyncIdentity();
                result = _simHost.Sim.ReceiveHit(in hit);
            }
            else
            {
                _dummyHealth = Mathf.Max(0, _dummyHealth - hit.Damage);
                result = new HitResult(HitOutcome.Landed, hit.Damage);

                _shove = _knockback;
                _shoveDirection = hit.Facing < 0 ? -1 : 1;

                if (!IsAlive) _reviveRemaining = _reviveSeconds;
            }

            LastOutcome = result.Outcome;
            DamageTaken += result.DamageApplied;

            _flashRemaining = _flashSeconds;
            _flashTint = result.Outcome == HitOutcome.Parried ? _parryColour
                       : result.Outcome == HitOutcome.Blocked ? _blockColour
                       : _flashColour;

            return result;
        }

        public void Revive()
        {
            if (IsSimulated) _simHost.Sim.Vitals.Reset();
            else _dummyHealth = _maxHealth;

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

            // Dummies are shoved by this component. A simulated character is shoved by its own
            // hit-react module writing velocity, and moving its transform here would fight the
            // interpolation that already owns it.
            if (!IsSimulated)
            {
                if (_shove > 0f)
                {
                    _shove = Mathf.Max(0f, _shove - _knockback * dt / Mathf.Max(0.0001f, _knockbackRecoverySeconds));
                    transform.position = _restPosition + new Vector3(_shove * _shoveDirection, 0f, 0f);
                }
                else if (transform.position != _restPosition)
                {
                    transform.position = _restPosition;
                }
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
                    ? _flashTint
                    : Color.Lerp(Color.white, new Color(0.9f, 0.35f, 0.35f),
                                 1f - Health / (float)Mathf.Max(1, MaxHealth));

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
            if (_simHost != null) return;

            var centre = Application.isPlaying ? _restPosition : transform.position;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(centre + new Vector3(_offset.x, _offset.y, 0f),
                                new Vector3(_size.x, _size.y, 0.2f));
        }
    }
}
