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

        [SerializeField, Tooltip("Speed below which the character is treated as standing still, " +
                                 "in m/s. Matches BodyStateResolver's default.")]
        private float _moveThreshold = BodyStateResolver.DefaultMoveThreshold;

        [SerializeField, Tooltip("Speed at or above which a walk becomes a run, in m/s.")]
        private float _runThreshold = BodyStateResolver.DefaultRunThreshold;

        [SerializeField, Tooltip("How long the landing pose is held after touchdown, in seconds. " +
                                 "The sim's LandedThisTick is true for exactly one tick — 16 ms — " +
                                 "which is less than a single frame at 60 fps. Passed straight " +
                                 "through, a half-second landing clip got one frame of screen time " +
                                 "and read as a flicker rather than as a landing. Short enough that " +
                                 "running out of a landing does not skate.")]
        private float _landHoldSeconds = 0.18f;

        private BodyState _current = BodyState.Idle;
        private bool _started;
        private float _landHold;

        /// <summary>The state showing right now. For the overlay and for tests.</summary>
        public BodyState Current => _current;

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
            if (_host.Sim.State.LandedThisTick) _landHold = _landHoldSeconds;
            else _landHold = Mathf.Max(0f, _landHold - Time.deltaTime);

            var inputs = Gather(_host.Sim);
            var next = BodyStateResolver.Resolve(in inputs);

            if (!_set.TryGet(next, out var entry) || entry.Clip == null) return;

            float speed = _set.PlaybackSpeedFor(in entry, Mathf.Abs(_host.Sim.State.Velocity.x));

            // Snap into the first state rather than blending up from nothing, which would otherwise
            // show the character fading in from a T-pose on the first frame.
            float blend = _started ? _set.BlendSeconds : 0f;
            _started = true;

            _animation.PlayState(entry.Clip, entry.Loop, speed, blend);

            if (next != _current)
            {
                _history.Insert(0, (_current, _currentSeconds));
                if (_history.Count > HistoryLength) _history.RemoveAt(_history.Count - 1);

                _currentSeconds = 0f;
                _current = next;
            }
            else
            {
                _currentSeconds += Time.deltaTime;
            }
        }

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

                AttackId = attack != null && attack.IsAttacking && attack.Current != null
                    ? attack.Current.Id
                    : null,

                LandedThisTick = _landHold > 0f,
                RunThreshold = _runThreshold,
                MoveThreshold = _moveThreshold,
            };
        }
    }
}
