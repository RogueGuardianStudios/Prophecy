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
        private string _driverLabel;

        public EnemyBrainHost() => _builtIn = new PatrolPursueAttack(_tuning);

        /// <summary>What the senses last found. Read by GOAP sensors and by the overlay.</summary>
        public Percept Percept => _percept;

        /// <summary>A wall or a ledge in the way. What turns a patrol round.</summary>
        public bool BlockedAhead => _blockedAhead;

        /// <summary>What the body will be told to do. GOAP action strategies write this.</summary>
        public EnemyIntent Intent => _intent;

        /// <summary>
        /// Working state for whichever action is running on THIS enemy.
        ///
        /// <para><b>Because the strategies are shared assets.</b> A <c>GoapActionStrategy</c> is a
        /// ScriptableObject, so there is exactly one instance of it for the whole game — every
        /// enemy running "Swing" is running the same object. A field on the strategy is therefore
        /// a global variable wearing a member's clothing: the grunt set its swing timestamp and
        /// the caster, reading the same field, believed it had already fired and skipped the shot
        /// entirely. Nothing errored; the caster simply never attacked.</para>
        ///
        /// <para>One scratch rather than one per action because an agent runs a single action at a
        /// time — the executor guarantees it — so the fields cannot be contended within an agent,
        /// only between them, which is exactly what this separates.</para>
        /// </summary>
        public sealed class ActionScratch
        {
            /// <summary>Tick the current action began doing whatever it times. MinValue = not yet.</summary>
            public long StartedTick = long.MinValue;

            /// <summary>Which way it is going: a patrol's heading, or a spring's committed aim.</summary>
            public int Direction = 1;
        }

        public ActionScratch Scratch { get; } = new ActionScratch();

        /// <summary>The built-in loop, for comparison against a planner and for the overlay.</summary>
        public PatrolPursueAttack BuiltIn => _builtIn;

        public EnemyBrainTuning Tuning => _tuning;

        /// <summary>
        /// Tell this host that something else is writing the intent, so the built-in loop stands
        /// down. A GOAP agent calls this when it takes over.
        ///
        /// <para><paramref name="label"/> is for the trace only — the name of whatever is deciding,
        /// so a log can say "Swing" rather than reporting the built-in loop's last thought. It
        /// carries no behaviour and nothing reads it to make a decision.</para>
        /// </summary>
        public void DriveExternally(bool driven, string label = null)
        {
            _drivenExternally = driven;
            if (driven) _driverLabel = label;
        }

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

#if UNITY_EDITOR
            Trace(sim);
#endif
            return _intent.Consume();
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------- trace
        //
        // Editor-only by construction. Enemy behaviour is invisible from outside — a capsule that
        // is planning and a capsule that is running the fallback look identical — and the console
        // bridge does not reach play mode reliably, so this writes a file instead.

        [Header("Diagnostics (editor only)")]
        [SerializeField, Tooltip("Write a line to Logs/enemy-trace.txt every few ticks. The column " +
                                 "that matters is 'driver': GOAP means a planner action is writing " +
                                 "the intent, builtin means it never took over.")]
        private bool _trace = true;

        [SerializeField, Tooltip("Ticks between trace lines. 30 is twice a second.")]
        private int _traceEveryTicks = 30;

        private static readonly System.Collections.Generic.List<string> TraceLines =
            new System.Collections.Generic.List<string>();

        private static string TracePath => System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath)!.FullName, "Logs", "enemy-trace.txt");

        private void Trace(CharacterSim sim)
        {
            if (!_trace || _traceEveryTicks < 1) return;
            if (sim.CurrentTick % _traceEveryTicks != 0) return;

            // The built-in loop does not tick while a planner drives, so its Behaviour is frozen at
            // whatever it last decided — it read "Patrol" through an entire pursuit. Report whoever
            // is actually deciding.
            string behaviour = _drivenExternally
                ? (_driverLabel ?? "<external>")
                : _builtIn.Behaviour.ToString();

            TraceLines.Add(string.Format(
                "{0,6} {1,-14} driver={2,-7} x={3,7:F2} vx={4,6:F2} target={5,-5} see={6,-5} " +
                "dist={7,6:F2} blocked={8,-5} move={9,5:F2}",
                sim.CurrentTick, behaviour,
                _drivenExternally ? "GOAP" : "builtin",
                sim.State.Position.x, sim.State.Velocity.x,
                _percept.HasTarget, _percept.HasLineOfSight,
                _percept.HasTarget ? _percept.DistanceX : -1f,
                _blockedAhead, _intent.MoveX));

            if (TraceLines.Count % 20 == 0) FlushTrace();
        }

        private void OnDisable()
        {
            if (_trace) FlushTrace();
        }

        private static void FlushTrace()
        {
            if (TraceLines.Count == 0) return;

            var text = new System.Text.StringBuilder();
            text.AppendLine("Prophecy enemy trace");
            text.AppendLine("driver=GOAP means a planner action wrote the intent; builtin means it never took over.");
            text.AppendLine();
            text.AppendLine("  tick behaviour      driver        x     vx target see    dist blocked  move");
            text.AppendLine(new string('-', 108));

            int start = Mathf.Max(0, TraceLines.Count - 300);
            for (int i = start; i < TraceLines.Count; i++) text.AppendLine(TraceLines[i]);

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(TracePath)!);
                System.IO.File.WriteAllText(TracePath, text.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Prophecy] could not write the enemy trace: {e.Message}");
            }
        }
#endif

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
