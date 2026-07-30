using System.Collections.Generic;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The scene's answer to "who else is in the fight". Collects every live
    /// <see cref="Combatant"/> into the hurtbox list attackers resolve against, and routes the hits
    /// that come back.
    ///
    /// <para><b>The list is rebuilt once per tick, not once per attack.</b> It ticks ahead of every
    /// character — <see cref="DefaultExecutionOrder"/> puts its registration first — so every
    /// attacker resolving on the same tick sees the same world. Rebuilding per attack would let two
    /// simultaneous swings disagree about where a target was, which is the sort of thing that shows
    /// up once in fifty fights and is never reproducible.</para>
    ///
    /// <para><b>It is the seam, not the rules.</b> <see cref="ICombatWorld.OnHit"/> hands the hit to
    /// the target and the target decides what it means. Damage numbers, i-frames and blocking are
    /// the receiving end's business; putting them here would rebuild the tangle the interface
    /// exists to avoid.</para>
    ///
    /// <para>Found rather than referenced. A prefab cannot hold a reference to a scene object, and
    /// the player is a prefab — so this publishes itself statically in <c>Awake</c> and the host
    /// picks it up. Same problem, same shape of answer, as <c>SimClockDriver.RegisterWithScene</c>.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class CombatDirector : MonoBehaviour, ICombatWorld, ISimSystem
    {
        /// <summary>How many recent hits to keep for the overlay. A demo aid, not a game system.</summary>
        public const int HitLogLength = 12;

        [SerializeField, Tooltip("Leave empty to find one in the scene.")]
        private SimClockDriver _clockDriver;

        [SerializeField, Tooltip("Which plane combatants are placed on. Side-scroll maps to world XY.")]
        private MovementSpace _space = MovementSpace.SideScroll;

        /// <summary>A hit and what the defender made of it. The overlay wants both — "10 damage"
        /// and "10 damage, blocked" are different stories and only the pair tells either.</summary>
        public readonly struct LoggedHit
        {
            public readonly HitEvent Hit;
            public readonly HitResult Result;

            public LoggedHit(in HitEvent hit, in HitResult result)
            {
                Hit = hit;
                Result = result;
            }
        }

        private readonly List<Combatant> _combatants = new List<Combatant>();
        private readonly List<Hurtbox> _hurtboxes = new List<Hurtbox>();
        private readonly List<LoggedHit> _hitLog = new List<LoggedHit>();

        private readonly ProjectileSystem _projectiles = new ProjectileSystem();
        private SimClockDriver _registeredWith;

        /// <summary>The director for the loaded scene, or null. Set in <c>Awake</c>.</summary>
        public static CombatDirector Instance { get; private set; }

        public MovementSpace Space => _space;

        public IReadOnlyList<Hurtbox> Hurtboxes => _hurtboxes;

        /// <summary>Everyone currently registered. Read by the overlay.</summary>
        public IReadOnlyList<Combatant> Combatants => _combatants;

        /// <summary>Most recent hits and their outcomes, newest last. Demo diagnostics.</summary>
        public IReadOnlyList<LoggedHit> HitLog => _hitLog;

        /// <summary>Everything currently in the air. Read by the overlay.</summary>
        public IReadOnlyList<LiveProjectile> Projectiles => _projectiles.Live;

        /// <summary>
        /// Level geometry projectiles are stopped by. Taken from the player's baked world rather
        /// than baked twice — there is one definition of solid in this project and a shot has to
        /// agree with it.
        /// </summary>
        [SerializeField, Tooltip("Leave empty to find the player host in the scene.")]
        private PlayerCharacterHost _geometrySource;

        private void Awake()
        {
            // Last one in wins rather than first: a director in an additively loaded arena should
            // take over from one left behind by a previous scene, not be silently ignored by it.
            Instance = this;
        }

        private void Start()
        {
            _registeredWith = SimClockDriver.RegisterWithScene(_clockDriver, this, this);
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_registeredWith != null && _registeredWith.Clock != null)
                _registeredWith.Clock.Unregister(this);

            if (Instance == this) Instance = null;
            _projectiles.Clear();
        }

        // ---------------------------------------------------------------- registry

        public void Register(Combatant combatant)
        {
            if (combatant == null || _combatants.Contains(combatant)) return;

            _combatants.Add(combatant);
            Rebuild();
        }

        public void Unregister(Combatant combatant)
        {
            if (_combatants.Remove(combatant)) Rebuild();
        }

        // ---------------------------------------------------------------- tick

        public void Tick(in SimTickInfo info)
        {
            Rebuild();

            // After the rebuild, so a shot resolves against the same snapshot every character does,
            // and before nothing else — projectiles are the one attacker with no tick order of
            // their own.
            if (_geometrySource == null) _geometrySource = FindAnyObjectByType<PlayerCharacterHost>();

            _projectiles.Tick(this, _geometrySource != null ? _geometrySource.World : null,
                              info.Tick, info.DeltaSeconds);
        }

        public void Spawn(ProjectileDefinition definition, in Attacker owner)
        {
            _projectiles.Spawn(definition, in owner);
        }

        /// <summary>
        /// Snapshot every combatant's hurtbox for this tick.
        ///
        /// <para>A dead combatant contributes nothing rather than being removed, so a training
        /// dummy that revives does not have to re-register and the indices stay stable within the
        /// tick.</para>
        /// </summary>
        private void Rebuild()
        {
            _hurtboxes.Clear();

            for (int i = 0; i < _combatants.Count; i++)
            {
                var combatant = _combatants[i];
                if (combatant == null || !combatant.isActiveAndEnabled || !combatant.IsAlive) continue;

                _hurtboxes.Add(combatant.BuildHurtbox(_space));
            }
        }

        // ---------------------------------------------------------------- hits

        public HitResult OnHit(in HitEvent hit)
        {
            for (int i = 0; i < _combatants.Count; i++)
            {
                var combatant = _combatants[i];
                if (combatant == null || combatant.CombatId != hit.TargetId) continue;

                var result = combatant.ReceiveHit(in hit);
                Record(in hit, in result);
                return result;
            }

            // The target vanished between the hurtbox snapshot and the hit — died, was unloaded.
            // Not an error, and the attacker still needs an answer.
            return HitResult.Ignored;
        }

        private void Record(in HitEvent hit, in HitResult result)
        {
            _hitLog.Add(new LoggedHit(hit, result));
            if (_hitLog.Count > HitLogLength) _hitLog.RemoveAt(0);
        }
    }
}
