using System.Collections.Generic;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The scene's handle on the fight. Feeds a <see cref="CombatState"/> and drives it, and does
    /// not own anything a host would need to replicate.
    ///
    /// <para><b>Thin on purpose.</b> This used to hold the registry, the projectiles, the contact
    /// cooldowns and the hit routing — all of it authoritative state, all of it on a MonoBehaviour.
    /// That is the wrong side of the line for a host that has to simulate a fight and clients that
    /// have to predict against it, so it all moved into <see cref="CombatState"/>, which is plain
    /// C# and testable with no scene. What is left here is the three things that genuinely are
    /// presentation: finding the scene's geometry, ticking on the clock, and driving the visual
    /// half of every combatant.</para>
    ///
    /// <para>Found rather than referenced. A prefab cannot hold a reference to a scene object, and
    /// the player is a prefab — so this publishes itself statically in <c>Awake</c> and the host
    /// picks it up. Same problem, same shape of answer, as <c>SimClockDriver.RegisterWithScene</c>.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class CombatDirector : MonoBehaviour, ISimSystem
    {
        [SerializeField, Tooltip("Leave empty to find one in the scene.")]
        private SimClockDriver _clockDriver;

        [SerializeField, Tooltip("Which plane combatants are placed on. Side-scroll maps to world XY.")]
        private MovementSpace _space = MovementSpace.SideScroll;

        [SerializeField, Tooltip("Level geometry projectiles are stopped by. Leave empty to find " +
                                 "the player host — there is one definition of solid in this " +
                                 "project and a shot has to agree with it.")]
        private PlayerCharacterHost _geometrySource;

        [SerializeField, Tooltip("The attack director's dials — budgets, beats, gaps. Live: " +
                                 "edits in play mode reach the fight immediately, the same " +
                                 "surface every tuning asset offers.")]
        private AttackDirectorTuning _attackPacing = new AttackDirectorTuning();

        private readonly List<Combatant> _presented = new List<Combatant>();
        private SimClockDriver _registeredWith;

        /// <summary>The director for the loaded scene, or null. Set in <c>Awake</c>.</summary>
        public static CombatDirector Instance { get; private set; }

        /// <summary>The fight itself. This is what characters are pointed at, not the director.</summary>
        public CombatState State { get; } = new CombatState();

        public MovementSpace Space => _space;

        // Convenience for the overlay, which has no business knowing the shape of the split.
        public HurtboxSet Hurtboxes => State.Hurtboxes;
        public IReadOnlyList<LiveProjectile> Projectiles => State.Projectiles;
        public IReadOnlyList<CombatState.LoggedHit> HitLog => State.HitLog;
        public IReadOnlyList<Combatant> Combatants => _presented;

        private void Awake()
        {
            // Last one in wins rather than first: a director in an additively loaded arena should
            // take over from one left behind by a previous scene, not be silently ignored by it.
            Instance = this;
        }

        private void Start()
        {
            // The class REFERENCE, not a copy — inspector edits in play mode land live.
            State.Attacks.Tuning = _attackPacing;

            _registeredWith = SimClockDriver.RegisterWithScene(_clockDriver, this, this);

            AdoptCombatantsAlreadyInTheWorld();
            State.RebuildHurtboxes();
        }

        /// <summary>
        /// Register every combatant that already exists, because most of them do.
        ///
        /// <para><b>Registration cannot be left to <c>OnEnable</c>.</b> Two kinds of combatant
        /// enable when no director exists to hear them. The player is in Bootstrap, which loads
        /// first and never unloads, so it enables before the first arena and stays enabled through
        /// every transition afterwards — its <c>OnEnable</c> fires exactly once, in an empty world.
        /// And a scene's own combatants race this component's <c>Awake</c>, since Unity does not
        /// order objects within a scene load.</para>
        ///
        /// <para>In <c>Start</c> rather than <c>Awake</c>: by then every object in the incoming
        /// scene has woken, so this sees all of them. Once per scene load, not per tick.</para>
        /// </summary>
        private void AdoptCombatantsAlreadyInTheWorld()
        {
            var existing = FindObjectsByType<Combatant>(FindObjectsInactive.Exclude,
                                                        FindObjectsSortMode.None);

            for (int i = 0; i < existing.Length; i++)
                existing[i].JoinFight(this);

            // The same reason PlayerCharacterHost warns about baking zero solids: the failure this
            // method exists to prevent is silent, and a fight with nobody in it looks exactly like
            // a fight nobody has walked into yet.
            if (State.Combatants.Count == 0)
                Debug.LogWarning($"{name}: no combatants joined this fight. Nothing here can be hit " +
                                 "— check that the scene's Combatant components are enabled.", this);
        }

        private void OnDestroy()
        {
            if (_registeredWith != null && _registeredWith.Clock != null)
                _registeredWith.Clock.Unregister(this);

            if (Instance == this) Instance = null;
            State.Clear();
        }

        // ---------------------------------------------------------------- registry

        public void Register(Combatant combatant)
        {
            if (combatant == null || _presented.Contains(combatant)) return;
            if (!State.Register(combatant)) return;

            _presented.Add(combatant);
            State.RebuildHurtboxes();
        }

        public void Unregister(Combatant combatant)
        {
            if (combatant == null) return;

            State.Unregister(combatant);
            if (_presented.Remove(combatant)) State.RebuildHurtboxes();
        }

        // ---------------------------------------------------------------- tick

        public void Tick(in SimTickInfo info)
        {
            if (_geometrySource == null) _geometrySource = FindAnyObjectByType<PlayerCharacterHost>();

            State.Tick(_geometrySource != null ? _geometrySource.World : null,
                       info.Tick, info.DeltaSeconds);
        }

        /// <summary>
        /// Drive every combatant's visual half from here rather than giving each one an
        /// <c>Update</c>.
        ///
        /// <para>A hundred MonoBehaviour <c>Update</c> calls is a hundred managed-to-native
        /// transitions a frame for work that is a flash timer and a colour. One loop costs one.</para>
        /// </summary>
        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < _presented.Count; i++)
            {
                var combatant = _presented[i];
                if (combatant != null) combatant.PresentationTick(dt);
            }
        }
    }
}
