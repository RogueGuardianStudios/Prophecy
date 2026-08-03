using System.Collections.Generic;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.AI;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Drives an enemy: senses the world each tick, lets something decide, and hands the result to
    /// the simulation as ordinary input.
    ///
    /// <para><b>This is the whole of the AI's contact with the game.</b> It implements
    /// <see cref="IInputSource"/>, which is the same interface the player's gamepad capture
    /// implements, so the simulation cannot tell the two apart — see that file for why the
    /// distinction matters and how quietly it erodes.</para>
    ///
    /// <para><b>Two ways to decide, one way to act.</b> A GOAP brain writes
    /// <see cref="EnemyIntent"/> through its action strategies; with no brain assigned, the built-in
    /// <see cref="PatrolPursueAttack"/> writes the same intent. Either way the intent is the only
    /// output, so an enemy with no authored behaviour still patrols rather than standing inert —
    /// and a half-authored brain fails visibly rather than silently.</para>
    ///
    /// <para>Perception runs here rather than inside either decider, because both need it and
    /// because it is the part that must agree with the simulation. Sensors read the result.</para>
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class EnemyBrainHost : MonoBehaviour, IInputSource
    {
        [SerializeField, Tooltip("The body this drives. Leave empty to find one on this object.")]
        private PlayerCharacterHost _host;

        [SerializeField, Tooltip("Who this fights alongside. Attacks skip their own team.")]
        private int _team = 2;

        [SerializeField]
        private EnemyBrainTuning _tuning = new EnemyBrainTuning();

        [SerializeField, Tooltip("Let the built-in patrol/pursue/attack loop decide when no GOAP " +
                                 "brain is driving. Turn OFF once a brain is authored, so a broken " +
                                 "brain shows as an enemy standing still rather than as one that " +
                                 "looks fine and is not actually planning.")]
        private bool _fallbackWhenNoBrain = true;

        private readonly EnemyIntent _intent = new EnemyIntent();
        private readonly List<int> _candidates = new List<int>();
        private readonly PatrolPursueAttack _builtIn;

        private Percept _percept;
        private bool _blockedAhead;
        private bool _drivenExternally;

        public EnemyBrainHost() => _builtIn = new PatrolPursueAttack(_tuning);

        /// <summary>What the senses last found. Read by GOAP sensors and by the overlay.</summary>
        public Percept Percept => _percept;

        /// <summary>A wall or a ledge in the way. What turns a patrol round.</summary>
        public bool BlockedAhead => _blockedAhead;

        /// <summary>What the body will be told to do. GOAP action strategies write this.</summary>
        public EnemyIntent Intent => _intent;

        /// <summary>The built-in loop, for comparison against a planner and for the overlay.</summary>
        public PatrolPursueAttack BuiltIn => _builtIn;

        public EnemyBrainTuning Tuning => _tuning;

        /// <summary>
        /// Tell this host that something else is writing the intent, so the built-in loop stands
        /// down. A GOAP agent calls this when it takes over.
        /// </summary>
        public void DriveExternally(bool driven) => _drivenExternally = driven;

        private void Awake()
        {
            if (_host == null) _host = GetComponent<PlayerCharacterHost>();

            if (_host == null)
                Debug.LogError($"{name}: no PlayerCharacterHost to drive.", this);
        }

        private void Start()
        {
            if (_host?.Sim != null) _host.Sim.State.Team = _team;
        }

        /// <summary>
        /// Sense, decide, and hand over one tick of input.
        ///
        /// <para>Called by the host on its tick, so perception is refreshed exactly once per
        /// simulation step — not per rendered frame. Sensing twice in one tick would let two
        /// deciders disagree about where the target is; sensing once per frame would make an
        /// enemy's reactions depend on the display rate.</para>
        /// </summary>
        public InputFrame ConsumeFrame()
        {
            var sim = _host != null ? _host.Sim : null;
            if (sim == null) return InputFrame.Empty;

            Sense(sim);

            // With a planner driving, the intent has already been written by its action strategies
            // and this only spends it.
            if (!_drivenExternally && _fallbackWhenNoBrain)
                _builtIn.Tick(in _percept, _intent, sim.CurrentTick, _blockedAhead);

            return _intent.Consume();
        }

        private void Sense(CharacterSim sim)
        {
            var state = sim.State;
            var fight = CombatDirector.Instance != null ? CombatDirector.Instance.State : null;

            var eyes = new Vector2(state.Position.x, state.Position.y + state.BodySize.y * 0.5f);

            _percept = Sim.AI.EnemyPerception.Sense(fight, _host.World, _candidates,
                                                    state.CombatId, state.Team,
                                                    eyes, _tuning.SightRange);

            // Which way it is trying to go, not which way it is facing — a patrol that has just
            // been turned round should be asked about the direction it is about to walk in.
            int heading = Mathf.Abs(_intent.MoveX) > 0.01f
                ? (_intent.MoveX < 0f ? -1 : 1)
                : state.Facing;

            _blockedAhead = Sim.AI.EnemyPerception.ShouldTurnBack(_host.World, state.Position,
                                                                  state.BodySize, heading);
        }
    }
}
