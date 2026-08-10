using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The down-thrust: jump, hold down, attack, and stab through the floor beneath you.
    /// Design bible §6.1 calls it non-negotiable — it is <i>the</i> Zelda II move.
    ///
    /// <para><b>It swings its own blade, and that is the whole answer to who calls the bounce.</b>
    /// The bounce sat here unreferenced for two milestones because every obvious caller was a
    /// module reaching into another module — the attack module telling the down-thrust it had
    /// connected, or the down-thrust asking the attack module what it hit. Both break the rule the
    /// architecture is built on. The way out is that there was never a second module involved: a
    /// down-thrust is one action with a movement half and a damage half, so it resolves its own hit
    /// through the shared <see cref="HitSweep"/> and bounces itself. Nothing references anything.</para>
    ///
    /// <para><b>The dive is not a timeline.</b> Every other attack is a fixed number of ticks; this
    /// one lasts until it hits something or reaches the ground, so its volume has no window and is
    /// simply live for as long as the dive is. That is why it is an <see cref="AttackHitBox"/>
    /// swung directly rather than an <c>AttackDefinition</c> on a timeline.</para>
    ///
    /// <para>The dive velocity is <b>re-asserted every tick</b> rather than set once. Gravity runs
    /// first in the tick order and would otherwise accelerate the dive past its authored speed
    /// into the terminal-velocity clamp, making the thrust's reach depend on how high it started.
    /// Pinning it keeps the move the same length every time.</para>
    ///
    /// <para>It commits: for <see cref="MovementTuningData.DownThrustMinTicks"/> the lock's cancel
    /// window stays shut, so nothing can steal the character mid-stab. After that the window
    /// opens and a higher-priority reaction — a parry, a hit-react — can take over, which is
    /// exactly the arrangement the single-lock arbiter was built for.</para>
    /// </summary>
    public sealed class DownThrust : AbilityModule
    {
        private const LockFlags DiveLock = LockFlags.Move | LockFlags.Turn | LockFlags.Jump | LockFlags.Attack;

        private readonly MovementTuningData _tuning;
        private readonly CombatTuningData _combat;
        private readonly HitSweep _sweep = new HitSweep();

        private bool _active;
        private bool _rising;
        private long _startTick;

        public DownThrust(MovementTuningData tuning, CombatTuningData combat = null)
        {
            _tuning = tuning;
            _combat = combat;
        }

        /// <summary>Tick of the most recent bounce. <c>long.MinValue</c> if never. Overlay.</summary>
        public long LastBounceTick { get; private set; } = long.MinValue;

        public override AbilityId Id => AbilityId.DownThrust;
        public override int Order => ModuleOrder.DownThrust;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>True while the dive is in progress. Debug overlay and, later, the hitbox driver.</summary>
        public bool IsActive => _active;

        /// <summary>The box this dive swings, for the overlay. Neither thrust runs on the attack
        /// timeline, so without this the overlay would draw every volume except theirs.</summary>
        public AttackHitBox Volume => _combat != null ? _combat.DownThrustBox : default;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_active)
            {
                Continue(sim, in input, in info);
                return;
            }

            TryStart(sim, in input, in info);
        }

        public override void Reset()
        {
            _active = false;
            _rising = false;
            _startTick = 0;
            BounceCount = 0;
        }

        /// <summary>True while riding the pop off a connect rather than driving downward.</summary>
        public bool IsRising => _rising;

        /// <summary>
        /// End the dive with an upward pop. Driven by this module's own hit landing — the bounce is
        /// what makes the move chainable down a column of enemies, and it is the reason connecting
        /// feels different from simply landing.
        ///
        /// <para>Public because a scripted moment may want to grant one, not because anything else
        /// is expected to notice a hit on this module's behalf.</para>
        /// </summary>
        public void Bounce(CharacterSim sim, long tick = long.MinValue, bool keepDiving = false)
        {
            if (!_active) return;

            sim.State.Velocity.y = _tuning.DownThrustBounceVelocity;
            LastBounceTick = tick;
            BounceCount++;

            // Connecting hands the air back. A bounce is a reward for landing the hardest-to-place
            // attack in the game, and taking the follow-up jump away from it would make the safe
            // play never to use it far from the ground.
            sim.State.AirRefreshTick = tick;

            if (!keepDiving)
            {
                Stop(sim);
                return;
            }

            // Still committed, but riding the pop rather than driving down. Everything under the
            // blade becomes hittable again, which is what makes descending a column work at all.
            _rising = true;
            _sweep.Begin();
        }

        /// <summary>Bounces in the current dive. Resets when the dive does. Debug overlay.</summary>
        public int BounceCount { get; private set; }

        private void TryStart(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (state.Grounded) return;
            if (!input.Attack.Pressed) return;
            if (input.Move.y > -_tuning.CrouchInputThreshold) return;
            if (!sim.Can(LockFlags.Attack)) return;

            if (!sim.TryLock(this, DiveLock, LockPriority.Attack)) return;

            _active = true;
            _rising = false;
            _startTick = info.Tick;
            BounceCount = 0;
            state.Velocity.y = -_tuning.DownThrustSpeed;

            // A fresh dive, so everything under it is hittable again — including whatever the last
            // one bounced off, which is exactly the column of enemies the move exists to descend.
            _sweep.Begin();
        }

        /// <summary>
        /// Keep diving. The dive ends on the ground, or when the button is let go — and nowhere
        /// else, which is what lets it be held through a whole descent.
        /// </summary>
        private void Continue(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            // Landing ends it. LandedThisTick is published after resolution, so a module cannot
            // read it — grounded state refreshed at the top of the tick is the signal available here.
            if (state.Grounded)
            {
                Stop(sim);
                return;
            }

            // Letting go ends it, and that is the whole control scheme: hold to keep falling on
            // things, release to take the pop and get your air control back. Only checked while
            // rising, so a tap still buys a full committed dive rather than being cancelled by the
            // release that follows it a frame later.
            if (_rising && !input.Attack.Held)
            {
                Stop(sim);
                return;
            }

            // Riding a bounce: gravity owns the arc until the apex, then the dive resumes. Pinning
            // the dive speed here instead would cancel the pop on the tick it was granted.
            if (_rising)
            {
                if (state.Velocity.y > 0f)
                {
                    sim.SetCancelWindow(this, false);
                    return;
                }

                _rising = false;
            }

            state.Velocity.y = -_tuning.DownThrustSpeed;

            sim.SetCancelWindow(this, info.Tick - _startTick >= _tuning.DownThrustMinTicks);

            Strike(sim, in input, in info);
        }

        /// <summary>
        /// Swing the blade under the feet. Resolved after the dive velocity is re-asserted, so a
        /// bounce set here is the last word on this tick's vertical motion rather than something
        /// the dive immediately overwrites.
        /// </summary>
        private void Strike(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_combat == null || sim.CombatWorld == null) return;

            var box = _combat.DownThrustBox;
            if (box.HalfExtents.x <= 0f || box.HalfExtents.y <= 0f) return;

            var state = sim.State;
            var attacker = Attacker.FromBody(state.CombatId, state.Position, state.BodySize,
                                             state.Facing, state.Team,
                                             sim.Stats.DamageScale);

            // No cover query: the blade is directly underfoot, and a wall between the character and
            // a target they are standing on top of is not a situation the geometry can produce.
            var result = _sweep.Sweep(0, box, attacker, sim.CombatWorld, null,
                                      info.Tick, _combat.DownThrustAttackId);

            if (result.Punished)
            {
                // Parried out of the air. No bounce — the reward for reading a dive is that the
                // diver keeps falling, with nothing left to do about it.
                sim.Stun(result.AttackerStunTicks, -state.Facing, info.Tick, result.LastOutcome);
                Stop(sim);
                return;
            }

            // Blocked counts. Something solid was struck, and the pop off it is what makes the
            // move chainable; whether the target was hurt by it is the target's business.
            //
            // Holding the button keeps the dive alive through the pop, so a held descent carries
            // down a column of enemies. Releasing takes the pop and hands back air control.
            if (result.Connected > 0) Bounce(sim, info.Tick, keepDiving: input.Attack.Held);
        }

        private void Stop(CharacterSim sim)
        {
            _active = false;
            sim.ReleaseLock(this);
        }
    }
}
