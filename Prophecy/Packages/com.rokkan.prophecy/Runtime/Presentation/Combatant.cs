using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Something that can be hit. Publishes a hurtbox to the fight and routes what comes back.
    ///
    /// <para><b>Two kinds of target, one component.</b> A training dummy has no simulation — it
    /// does not move, fall or decide anything — so its hurtbox is a box on a transform and its
    /// health is a local <see cref="Vitals"/>. A simulated character's hurtbox follows its feet,
    /// shrinks when it crouches, and routes hits into <c>CharacterSim.ReceiveHit</c> so the damage
    /// gates get their say. Assigning <see cref="_simHost"/> switches between them.</para>
    ///
    /// <para>That is why crouching under a high swing needs no rule anywhere: a simulated hurtbox
    /// is built from <c>BodySize</c>, which is already the crouch height when crouched, so the
    /// geometry misses on its own.</para>
    ///
    /// <para><b>The id is authored, not counted.</b> It used to come from a static counter, which
    /// made it depend on instantiation order — fine for one machine and useless for two, since a
    /// host naming "entity 47" to a client needs both to agree which one that is. Scene-placed
    /// combatants carry an authored id; anything spawned takes one from the fight's allocator,
    /// which a host will own. Zero means "allocate me one".</para>
    /// </summary>
    public sealed class Combatant : MonoBehaviour, ICombatant
    {
        [Header("Identity")]
        [SerializeField, Tooltip("Stable across machines, so it must be authored rather than " +
                                 "counted. Leave at 0 for anything spawned at runtime and the " +
                                 "fight allocates one.")]
        private int _combatId;

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
        [SerializeField] private int _maxHealth = 12;   // quarters

        [SerializeField, Tooltip("Seconds before a dead dummy comes back. Zero leaves it down — " +
                                 "which is what a real enemy will want.")]
        private float _reviveSeconds = 2f;

        [Header("Contact")]
        [SerializeField, Tooltip("Damage dealt just by touching this. Zero for anything that is " +
                                 "only dangerous when it swings — a training dummy, a crate.")]
        private int _contactDamage;

        [SerializeField, Tooltip("Ticks between contact hits on the same victim. Long enough that " +
                                 "standing in something is a bleed rather than an execution.")]
        private int _contactIntervalTicks = 45;

        [SerializeField, Tooltip("Which answers contact DEFEATS. Walking into a body is not a " +
                                 "telegraph, so by default there is nothing to time against it and " +
                                 "only moving away or i-frames help.")]
        private DefensiveAnswer _contactDefeats = DefensiveAnswer.Block | DefensiveAnswer.Parry;

        [Header("Reaction")]
        [SerializeField] private Color _flashColour = new Color(1f, 0.35f, 0.25f);
        [SerializeField] private Color _blockColour = new Color(0.55f, 0.8f, 1f);
        [SerializeField] private Color _parryColour = new Color(1f, 0.95f, 0.5f);
        [SerializeField] private float _flashSeconds = 0.12f;
        [SerializeField, Tooltip("How far a hit shoves a dummy, in metres. Simulated characters " +
                                 "are knocked back by the sim instead and ignore this.")]
        private float _knockback = 0.25f;
        [SerializeField] private float _knockbackRecoverySeconds = 0.35f;

        private readonly SimpleDefender _dummy = new SimpleDefender();

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private Vector3 _restPosition;
        private MovementSpace _space = MovementSpace.SideScroll;

        private Color _flashTint;
        private Color _lastWritten = new Color(-1f, -1f, -1f, -1f);
        private float _flashRemaining;
        private float _shove;
        private int _shoveDirection = 1;
        private float _reviveRemaining;

        public int CombatId => _combatId;

        public int Team => _team;

        /// <summary>True when this is a simulated character rather than a dummy.</summary>
        public bool IsSimulated => _simHost != null && _simHost.Sim != null;

        /// <summary>
        /// The simulated body behind this combatant, or null for a dummy.
        ///
        /// <para>Exposed so the debug overlay can draw what a real enemy is swinging. It used to
        /// draw incoming attacks only for scripted <see cref="TrainingAttacker"/>s, which meant a
        /// planning enemy's hit box — the one that actually reaches the player — was the single
        /// volume the volume viewer could not show.</para>
        /// </summary>
        public PlayerCharacterHost SimHost => _simHost;

        private Vitals Vitals => IsSimulated ? _simHost.Sim.Vitals : _dummy.Vitals;

        public int Health => Vitals.Health;

        public int MaxHealth => Vitals.MaxHealth;

        public bool IsAlive => Vitals.IsAlive;

        public int ContactDamage => _contactDamage;

        public int ContactIntervalTicks => _contactIntervalTicks;

        public DefensiveAnswer ContactDefeats => _contactDefeats;

        /// <summary>
        /// Where this body IS, for combat purposes — the anchor its hurtbox is built from.
        ///
        /// <para>A dummy's transform is presentation: the knockback shove moves it on frame time
        /// while the hurtbox stays planted at the rest position. Anything resolving combat from
        /// this body — an attack origin, a projectile spawn, a facing decision — must read this
        /// anchor, never the transform, or the point it attacks from diverges from the point it
        /// is hit at for exactly as long as a shove is decaying, by an amount that depends on
        /// frame rate.</para>
        /// </summary>
        public Vector3 AnchorPosition => IsSimulated ? _simHost.FeetWorldPosition
                                       : Application.isPlaying ? _restPosition
                                       : transform.position;

        /// <summary>Sim tick of the most recent hit taken. <c>long.MinValue</c> if never.</summary>
        public long LastHitTick { get; private set; } = long.MinValue;

        /// <summary>What the last incoming hit turned into.</summary>
        public HitOutcome LastOutcome { get; private set; } = HitOutcome.Ignored;

        /// <summary>Total damage taken since the last revive. The number a tuning pass reads.</summary>
        public int DamageTaken { get; private set; }

        /// <summary>
        /// A tint forced by whatever is driving this body — a wind-up, a charge. Null lets the
        /// health colour show through.
        ///
        /// <para>Set by the attacker rather than owned here, because <i>what</i> a body is doing is
        /// the attacker's business and only the renderers are this component's.</para>
        /// </summary>
        public Color? TelegraphTint { get; set; }

        private void Awake()
        {
            // A host on this same object means a simulated body, always — the dummy path exists for
            // things that have no simulation at all, and those have no host to find. Leaving this
            // to be wired by hand made a moving character publish a hurtbox at its spawn point and
            // say nothing about it, because "a dummy that does not move" is the normal case and
            // nothing downstream can tell the two apart.
            if (_simHost == null) _simHost = GetComponent<PlayerCharacterHost>();

            _dummy.Vitals.MaxHealth = _maxHealth;
            _dummy.Vitals.Reset();

            _restPosition = transform.position;
            _renderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            // May be null, and routinely is. The player lives in Bootstrap, which loads first and
            // never unloads, so this runs long before any arena — and every fight after the first
            // arrives with a scene load too. The director sweeps for us in its Start; see JoinFight.
            JoinFight(CombatDirector.Instance);
        }

        /// <summary>
        /// Join <paramref name="director"/>'s fight, if there is one. Safe to call repeatedly —
        /// both the director and the fight refuse a combatant that is already in.
        ///
        /// <para><b>Why this is not just <c>OnEnable</c>.</b> It used to be, and a null director
        /// meant giving up permanently. That worked in the editor, where you press Play from an
        /// arena and its director wakes before Bootstrap loads on top — and it meant that in a
        /// build, where Bootstrap is the first scene and the world arrives later, the player
        /// enabled with no director in existence and never joined any fight at all. They could
        /// still deal damage, because <c>PlayerCharacterHost</c> re-points its combat world every
        /// tick, so the failure was silent and one-sided: hits went out, nothing came back.</para>
        /// </summary>
        public void JoinFight(CombatDirector director)
        {
            if (director == null) return;

            _space = director.Space;

            // Zero means "not authored", which is correct for anything spawned. Everything placed
            // in a scene should carry one, so the id survives being loaded on another machine.
            if (_combatId == 0) _combatId = director.State.AllocateId();

            director.Register(this);
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
            state.CombatId = _combatId;
            state.Team = _team;
        }

        /// <summary>The volume attacks resolve against, in the sim's plane — shaped for the
        /// space it is played in: a standing silhouette in side-scroll, a footprint from above.</summary>
        public Hurtbox BuildHurtbox()
        {
            if (IsSimulated)
            {
                SyncIdentity();

                var state = _simHost.Sim.State;

                return state.Space == MovementSpace.TopDown
                    ? Hurtbox.ForFootprint(_combatId, state.Position, state.BodySize, _team)
                    : Hurtbox.ForBody(_combatId, state.Position, state.BodySize, _team);
            }

            var plane = SpaceMapping.ToPlane(_restPosition, _space);
            return new Hurtbox(_combatId, plane + _offset, _size * 0.5f, 0f, _team);
        }

        /// <summary>
        /// Take a hit, and say what it turned into.
        ///
        /// <para>Both kinds of target answer through the sim's damage pipeline — the character
        /// through its own gates, the dummy through a <see cref="SimpleDefender"/> walking the
        /// same chain — so what a hit MEANS is decided in exactly one place, and none of it
        /// here. This component keeps only the reactions a camera can see.</para>
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
                result = _dummy.ReceiveHit(in hit);

                if (result.Outcome == HitOutcome.Landed)
                {
                    _shove = _knockback;
                    _shoveDirection = hit.Facing < 0 ? -1 : 1;
                }

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
            Vitals.Reset();
            DamageTaken = 0;
            _reviveRemaining = 0f;
        }

        /// <summary>
        /// The visual half, driven by the director rather than by an <c>Update</c> of its own —
        /// a hundred of those is a hundred managed-to-native transitions a frame for a flash timer.
        /// </summary>
        public void PresentationTick(float deltaSeconds)
        {
            if (!IsAlive && _reviveSeconds > 0f)
            {
                _reviveRemaining -= deltaSeconds;
                if (_reviveRemaining <= 0f) Revive();
            }

            // Dummies are shoved by this component. A simulated character is shoved by its own
            // hit-react module writing velocity, and moving its transform here would fight the
            // interpolation that already owns it.
            if (!IsSimulated)
            {
                if (_shove > 0f)
                {
                    _shove = Mathf.Max(0f, _shove - _knockback * deltaSeconds /
                                             Mathf.Max(0.0001f, _knockbackRecoverySeconds));
                    transform.position = _restPosition + new Vector3(_shove * _shoveDirection, 0f, 0f);
                }
                else if (transform.position != _restPosition)
                {
                    transform.position = _restPosition;
                }
            }

            if (_flashRemaining > 0f) _flashRemaining -= deltaSeconds;

            ApplyTint();
        }

        /// <summary>
        /// Tint through a property block rather than the material, and only when it changes.
        ///
        /// <para>Touching <c>renderer.material</c> instantiates a copy per object and leaks it.
        /// Writing the property block unconditionally is cheaper but still not free, and a body
        /// that is idle at full health has exactly one colour for minutes at a time — so the write
        /// is skipped unless the colour actually moved.</para>
        /// </summary>
        private void ApplyTint()
        {
            if (_renderers == null) return;

            // A hit flash beats a wind-up, which beats the health colour. Being struck is the more
            // urgent thing to read, and a telegraph that swallowed its own hit reaction would make
            // a connecting attack invisible.
            Color tint = !IsAlive
                ? new Color(0.25f, 0.25f, 0.28f)
                : _flashRemaining > 0f
                    ? _flashTint
                    : TelegraphTint ?? Color.Lerp(Color.white, new Color(0.9f, 0.35f, 0.35f),
                                                  1f - Health / (float)Mathf.Max(1, MaxHealth));

            if (tint == _lastWritten) return;
            _lastWritten = tint;

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
