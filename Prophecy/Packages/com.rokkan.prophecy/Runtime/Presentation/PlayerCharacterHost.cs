using RGS.Core.Sim;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Owns the player's <see cref="CharacterSim"/> and is the single thing the clock ticks for it.
    ///
    /// <para><b>Why one registration instead of three.</b> Input delivery, the sim tick, and the
    /// snapshot that presentation interpolates against must happen in that exact order, every tick.
    /// Registering three separate systems would make that order a function of which MonoBehaviour's
    /// <c>Start</c> ran first — a race that would work on this machine, work in the editor, and
    /// then produce one-frame-stale input on someone else's build for no visible reason. So this
    /// registers once and calls the three in sequence, and the ordering becomes a property of five
    /// lines you can read.</para>
    ///
    /// <para>It holds a <c>CharacterSim</c>, which is plain C#, and never reaches back the other
    /// way: nothing in the sim knows this component exists. The sim is constructed by
    /// <see cref="PlayerCharacterFactory"/>, the same call the headless tests use, so the character
    /// being played is the character being tested.</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerCharacterHost : MonoBehaviour, ISimSystem
    {
        [Header("Wiring")]
        [SerializeField, Tooltip("The live tuning asset. Edits apply on the next tick, mid-play.")]
        private MovementTuning _tuning;

        [SerializeField, Tooltip("Which abilities are switched on. Ticking a box applies on the next " +
                                 "tick, mid-play. Leave empty to use each module's own default.")]
        private AbilityLoadout _loadout;

        [SerializeField, Tooltip("The live moveset and frame data. Edits apply on the next tick, " +
                                 "mid-play — which is how combat numbers get authored at all.")]
        private CombatTuning _combatTuning;

        [SerializeField, Tooltip("Leave empty to find one in the scene.")]
        private SimClockDriver _clockDriver;

        [SerializeField]
        private PlayerInputCapture _input;

        [Header("World")]
        [SerializeField]
        private MovementSpace _space = MovementSpace.SideScroll;

        [SerializeField, Tooltip("Layers baked into the sim's collision world.")]
        private LayerMask _collisionMask = ~0;

        [SerializeField, Tooltip("Position on the railed axis: Z in side-scroll, Y in top-down.")]
        private float _railDepth;

        [SerializeField, Tooltip("Bake collision on start-up. Switch OFF when a SceneDirector owns " +
                                 "world loading — the ground arrives after this component does.")]
        private bool _bakeOnStart = true;

        private SimClockDriver _registeredWith;

        /// <summary>The simulated character. Read by presentation; never written to.</summary>
        public CharacterSim Sim { get; private set; }

        /// <summary>The baked geometry the sim collides against.</summary>
        public CollisionWorld World { get; private set; }

        public MovementSpace Space => _space;

        public float RailDepth => _railDepth;

        /// <summary>The clock driving this character, for reading <c>InterpolationAlpha</c>.</summary>
        public SimClock Clock => _registeredWith != null ? _registeredWith.Clock : null;

        /// <summary>Foot position before the most recent tick. The 'from' end of interpolation.</summary>
        public Vector2 PreviousPosition { get; private set; }

        /// <summary>Foot position after the most recent tick. The 'to' end of interpolation.</summary>
        public Vector2 CurrentPosition { get; private set; }

        /// <summary>Solids baked at load. Zero means the character will fall forever.</summary>
        public int BakedSolidCount { get; private set; }

        private void Awake()
        {
            World = new CollisionWorld();

            if (_tuning == null)
                Debug.LogWarning($"{name}: no MovementTuning assigned — falling back to code defaults, " +
                                 "so inspector tuning will do nothing.", this);

            Sim = PlayerCharacterFactory.Create(
                World,
                _tuning != null ? _tuning.Data : null,
                _space,
                _loadout != null ? _loadout.Data : null,
                _combatTuning != null ? _combatTuning.Data : null,
                CombatDirector.Instance);
        }

        private void Start()
        {
            // Bake after every other object's Awake, so geometry spawned at load is included.
            // Once, outside the tick — see CollisionBaker for why that is the whole ballgame.
            //
            // Skipped when a SceneDirector owns world loading: in that setup the player exists in
            // Bootstrap before any ground does, so a start-up bake would find nothing and the only
            // honest thing it could report is a warning about a situation that is entirely normal.
            if (_bakeOnStart)
            {
                BakedSolidCount = CollisionBaker.Bake(World, _space, _collisionMask, transform);

                if (BakedSolidCount == 0)
                    Debug.LogWarning($"{name}: baked 0 solids. Check the collision mask — " +
                                     "the character has nothing to stand on.", this);
            }

            Sim.Teleport(SpaceMapping.ToPlane(transform.position, _space));
            PreviousPosition = CurrentPosition = Sim.State.Position;

            _registeredWith = SimClockDriver.RegisterWithScene(_clockDriver, this, this);
        }

        private void OnDestroy()
        {
            if (_registeredWith != null && _registeredWith.Clock != null)
                _registeredWith.Clock.Unregister(this);
        }

        public void Tick(in SimTickInfo info)
        {
            // Re-applied every tick rather than once at build, so ticking an ability on or off
            // during play takes effect immediately — the same live-editing property the tuning
            // asset has, and what makes A/B-ing a moveset quick. It is deterministic: the same
            // loadout data always produces the same enabled set.
            if (_loadout != null) _loadout.Data.Apply(Sim);

            // Re-pointed rather than fixed at construction: this character is a persistent prefab
            // and the fight it is in arrives with a scene load, then again after every transition.
            // A reference compare per tick is cheaper than the class of bug where the player swings
            // at an arena that loaded after they did. On the character rather than per module, so
            // every module that swings gets it — the down-thrust included.
            if (!ReferenceEquals(Sim.CombatWorld, CombatDirector.Instance))
                Sim.CombatWorld = CombatDirector.Instance;

            if (_input != null)
                Sim.SetInput(_input.ConsumeFrame());

            PreviousPosition = Sim.State.Position;
            Sim.Tick(in info);
            CurrentPosition = Sim.State.Position;
        }

        /// <summary>
        /// Move the character and reset its motion — scene transitions and respawns. Goes through
        /// the sim's own Teleport so no stale velocity or coyote credit survives the move, and
        /// collapses the interpolation window so the view does not smear across the jump.
        /// </summary>
        public void TeleportTo(Vector3 worldPosition, int facing = 0)
        {
            var plane = SpaceMapping.ToPlane(worldPosition, _space);
            Sim.Teleport(plane, facing);
            PreviousPosition = CurrentPosition = plane;
        }

        /// <summary>Re-bake the collision world. For scene loads, not for per-tick use.</summary>
        public int RebakeCollision()
        {
            BakedSolidCount = CollisionBaker.Bake(World, _space, _collisionMask, transform);
            return BakedSolidCount;
        }

        /// <summary>
        /// Switch which movement space this character is played in, and re-bake to match.
        ///
        /// <para>Cheap by construction: every module is already registered and declares the spaces
        /// it is valid in, so the sim skips the wrong ones without anything being added or removed.
        /// The bake is the part that actually has to change — the sim's 2D plane maps to world XY
        /// in side-scroll and XZ in the overworld, so the same colliders project to different
        /// boxes and stale ones would be silently wrong rather than obviously missing.</para>
        /// </summary>
        public void ConfigureSpace(MovementSpace space)
        {
            if (space != MovementSpace.SideScroll && space != MovementSpace.TopDown)
            {
                Debug.LogError($"{name}: '{space}' is not a single play space. Falling back to SideScroll.", this);
                space = MovementSpace.SideScroll;
            }

            _space = space;
            if (Sim != null) Sim.State.Space = space;

            RebakeCollision();
        }
    }
}
