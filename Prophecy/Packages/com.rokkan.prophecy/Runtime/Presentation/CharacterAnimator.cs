using System.Collections.Generic;
using Rokkan.Animation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Reads the simulation, decides what the body should be seen doing, and shows it.
    ///
    /// <para><b>The whole class is one-directional.</b> It reads <c>CharacterSim</c> and writes to
    /// the animator. Nothing here is read back by the sim, nothing here changes an outcome, and if
    /// this component is deleted the game still plays identically — it just has nothing to look at.
    /// That is the contract (<c>Plans/Animation-Contract.md</c>) and it is worth being able to
    /// state that plainly about a file.</para>
    ///
    /// <para><b>Speed matching is the point of it.</b> Without root motion the clips have no idea
    /// how fast the character is travelling, so a walk cycle authored at 1.4 m/s played on a body
    /// moving at 3 m/s skates its feet across the floor. Playback is scaled by the ratio, which is
    /// most of the difference between "placeholder" and "deliberate".</para>
    ///
    /// <para>Driven from <c>Update</c>, not the sim tick, on purpose: this is presentation, it
    /// should run at display rate, and the sim state it reads is whatever the last completed tick
    /// produced.</para>
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class CharacterAnimator : MonoBehaviour
    {
        [SerializeField, Tooltip("Leave empty to find one on this object or a parent.")]
        private PlayerCharacterHost _host;

        [SerializeField, Tooltip("The clip for each BodyState, and the speed each was authored at.")]
        private BodyAnimationSet _set;

        [SerializeField, Tooltip("The animation system on the rendered body. Leave empty to find " +
                                 "one in the children.")]
        private AnimationSystem _animation;

        // The thresholds and the land hold live on the SET, beside the clips they describe — a
        // per-component copy is how the run threshold silently disagrees with the walk/run
        // handover on exactly one prefab. The constants are only the no-set fallback.
        private float MoveThreshold =>
            _set != null ? _set.MoveThreshold : BodyStateResolver.DefaultMoveThreshold;

        private float RunThreshold =>
            _set != null ? _set.RunThreshold : BodyStateResolver.DefaultRunThreshold;

        private float LandHoldSeconds => _set != null ? _set.LandHoldSeconds : 0.18f;

        private BodyState _current = BodyState.Idle;
        private bool _started;
        private float _landHold;

        /// <summary>The state showing right now. For the overlay and for tests.</summary>
        public BodyState Current => _current;

        /// <summary>The clip set this body animates from. The pack portrait's double borrows
        /// it to stand in idle.</summary>
        public BodyAnimationSet Set => _set;

        /// <summary>
        /// The last few states, newest first, with how long each lasted in seconds.
        ///
        /// <para>Here because a one-or-two-frame wrong pose is invisible to every other kind of
        /// check: it does not throw, no test fails, and it is gone before you can pause. Seeing the
        /// sequence written down is the difference between diagnosing it and guessing at it.</para>
        /// </summary>
        public IReadOnlyList<(BodyState State, float Seconds)> History => _history;

        private readonly List<(BodyState State, float Seconds)> _history =
            new List<(BodyState, float)>();

        private const int HistoryLength = 8;

        private float _currentSeconds;

        private void Awake()
        {
            if (_host == null) _host = GetComponentInParent<PlayerCharacterHost>();
            if (_animation == null) _animation = GetComponentInChildren<AnimationSystem>();
        }

        private void Update()
        {
            if (_host == null || _host.Sim == null || _set == null || _animation == null) return;

            // Latch the landing here rather than in the resolver, which stays a pure function of
            // its inputs. How long a pose lingers is a presentation question anyway.
            if (_host.Sim.State.LandedThisTick) _landHold = LandHoldSeconds;
            else _landHold = Mathf.Max(0f, _landHold - Time.deltaTime);

            var inputs = Gather(_host.Sim);
            var next = BodyStateResolver.Resolve(in inputs, _set);

            if (!_set.TryGet(next, out var entry) || entry.Clip == null) return;

            float speed = _set.PlaybackSpeedFor(in entry, Mathf.Abs(_host.Sim.State.Velocity.x));

            // Snap into the first state rather than blending up from nothing, which would otherwise
            // show the character fading in from a T-pose on the first frame.
            float blend = _started ? _set.BlendSeconds : 0f;
            _started = true;

#if UNITY_EDITOR
            _lastSpeedMultiplier = speed;
#endif
            _animation.PlayState(entry.Clip, entry.Loop, speed, blend);

            if (next != _current)
            {
                _history.Insert(0, (_current, _currentSeconds));
                if (_history.Count > HistoryLength) _history.RemoveAt(_history.Count - 1);

#if UNITY_EDITOR
                RecordTransition(_current, _currentSeconds, next, in inputs);
#endif
                _currentSeconds = 0f;
                _current = next;
            }
            else
            {
                _currentSeconds += Time.deltaTime;
            }

#if UNITY_EDITOR
            // Remembered AFTER the transition is recorded, so the next one is judged against the
            // frame its state actually lived in.
            _wasGrounded = inputs.Grounded;
#endif
        }


#if UNITY_EDITOR
        // ---------------------------------------------------------------- flicker log
        //
        // Editor-only by construction rather than by a flag someone has to remember to turn off,
        // so there is nothing here to leave switched on in a build.

        [Header("Diagnostics (editor only)")]
        [SerializeField, Tooltip("Record every state change to Logs/body-state-log.txt. The file " +
                                 "rewrites whenever a state lasts less than two frames, so a flicker " +
                                 "is captured the moment it happens rather than needing to be caught.")]
        private bool _logStateChanges = true;

        [SerializeField, Tooltip("A state shorter than this is treated as a flicker. ~2 frames at 60 fps.")]
        private float _flickerSeconds = 0.034f;

        private readonly List<string> _log = new List<string>();
        private int _flickerCount;
        private int _impossibleCount;
        private int _interruptedCount;

        // The previous frame's context. The first version of this log compared the OUTGOING state
        // against the INCOMING frame's inputs and duly reported six impossible transitions, all of
        // which were just "we were falling, we have now landed". Judging a state needs the frame it
        // was actually in.
        private bool _wasGrounded;
        private float _lastSpeedMultiplier = 1f;

        private static string LogPath => System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath)!.FullName, "Logs", "body-state-log.txt");

        private void RecordTransition(BodyState from, float seconds, BodyState to,
                                      in BodyStateInputs inputs)
        {
            if (!_logStateChanges) return;

            bool flicker = seconds < _flickerSeconds && _log.Count > 0;
            if (flicker) _flickerCount++;

            // The question the whole log exists to answer. While grounded the resolver cannot
            // return Fall, so if this ever counts above zero the fault is in the simulation's
            // Grounded flag and not in the animation at all.
            bool impossible = from == BodyState.Fall && _wasGrounded;
            if (impossible) _impossibleCount++;

            // blend tells us what the graph had to do. "Replaced" is the only one that destroys a
            // clip that was still visible, so it is the one that can pop.
            var blend = _animation.LastAction;
            bool interrupted = blend == AnimationSystem.BlendAction.Replaced;
            if (interrupted) _interruptedCount++;

            _log.Add(string.Format(
                "{0,6} {1,-14} {2,7:F1}ms -> {3,-14} grounded={4,-5} vel=({5,6:F2},{6,6:F2}) " +
                "facing={7,2} stance={8,-6} speed={9,5:F2} blend={10,-9} w={11,4:F2}{12}{13}{14}",
                Time.frameCount, from, seconds * 1000f, to, inputs.Grounded,
                inputs.Velocity.x, inputs.Velocity.y,
                _host.Sim.State.Facing, inputs.Stance,
                _lastSpeedMultiplier, blend, _animation.StateWeight,
                flicker ? "  <-- FLICKER" : "",
                impossible ? "  <-- FALL WHILE GROUNDED" : "",
                interrupted ? "  <-- INTERRUPTED BLEND" : ""));

            if (flicker || impossible || interrupted) Flush();
        }

        private void OnDisable()
        {
            if (_logStateChanges && _log.Count > 0) Flush();
        }

        private void Flush()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("Prophecy body-state log");
            text.AppendLine($"transitions : {_log.Count}");
            text.AppendLine($"flickers    : {_flickerCount}  (state shorter than {_flickerSeconds * 1000f:F0}ms)");
            text.AppendLine($"impossible  : {_impossibleCount}  (Fall held across a grounded frame — " +
                            "non-zero means the sim's Grounded is flickering, not the animation)");
            text.AppendLine($"interrupted : {_interruptedCount}  (a blend was cut short and a visible " +
                            "clip had to be destroyed — this is what can pop)");
            text.AppendLine();
            text.AppendLine(" frame state              held    next           context");
            text.AppendLine(new string('-', 118));

            // Newest last, so the tail of the file is the most recent thing that happened.
            int start = Mathf.Max(0, _log.Count - 400);
            for (int i = start; i < _log.Count; i++) text.AppendLine(_log[i]);

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
                System.IO.File.WriteAllText(LogPath, text.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Prophecy] Could not write the body-state log: {e.Message}", this);
            }
        }
#endif

        /// <summary>
        /// Collect what the resolver needs. Every module is queried through <c>Get&lt;T&gt;</c>
        /// rather than cached, because the loadout can enable and disable modules mid-play and a
        /// cached reference to a disabled module would keep reporting stale state.
        /// </summary>
        private BodyStateInputs Gather(CharacterSim sim)
        {
            var state = sim.State;

            var attack = sim.Get<AttackModule>();
            var block = sim.Get<Block>();
            var dodge = sim.Get<DodgeStep>();
            var hitReact = sim.Get<HitReact>();
            var wallSlide = sim.Get<WallSlide>();
            var pullUp = sim.Get<LedgePullUp>();
            var downThrust = sim.Get<DownThrust>();
            var upThrust = sim.Get<UpThrust>();
            var swim = sim.Get<Swim>();
            var buoyancy = sim.Get<Buoyancy>();

            return new BodyStateInputs
            {
                Alive = sim.Vitals.IsAlive,
                Grounded = state.Grounded,
                Stance = state.Stance,
                Velocity = state.Velocity,
                Attachment = state.Attachment,

                InHitReact = hitReact != null && hitReact.IsReacting,
                Dodging = dodge != null && dodge.IsDodging,
                Guarding = block != null && block.IsGuarding,
                ParryWindowOpen = block != null && block.ParryWindowOpen(sim.CurrentTick),
                WallSliding = wallSlide != null && wallSlide.IsSliding,
                ClimbingLedge = pullUp != null && pullUp.IsClimbing,
                // Active but NOT rising. IsActive stays true through the bounce, and showing a
                // downward dive pose while the body pops upward would be a plain lie — the rise
                // falls through to JumpRise, so a pogo chain reads as dive, up, dive.
                DownThrusting = downThrust != null && downThrust.IsActive && !downThrust.IsRising,
                UpThrusting = upThrust != null && upThrust.IsActive,
                InWater = swim != null && swim.InWater,
                // Floating is the TRANSITION — rising to the water-walk platform. Standing on
                // it is grounded, and grounded on water shows the ordinary walk states: the
                // art's whole point is that the surface behaves like any other floor.
                Floating = buoyancy != null && buoyancy.FloatOn &&
                           swim != null && swim.InWater && !state.Grounded,

                AttackId = attack != null && attack.IsAttacking && attack.Current != null
                    ? attack.Current.Id
                    : null,

                LandedThisTick = _landHold > 0f,
                RunThreshold = RunThreshold,
                MoveThreshold = MoveThreshold,
            };
        }
    }
}
