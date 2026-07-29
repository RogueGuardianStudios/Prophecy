using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The down-thrust: jump, hold down, attack, and stab through the floor beneath you.
    /// Design bible §6.1 calls it non-negotiable — it is <i>the</i> Zelda II move.
    ///
    /// <para><b>This is the movement half only.</b> The dive, the commitment lock and the bounce
    /// live here because they are locomotion; the blade, the hitbox and the damage arrive with
    /// combat in M4/M5 and will drive <see cref="Bounce"/> from the hit response. Building the
    /// motion now is what lets the gray box test whether falling on things is fun before any
    /// enemy exists to fall on.</para>
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

        private bool _active;
        private long _startTick;

        public DownThrust(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.DownThrust;
        public override int Order => ModuleOrder.DownThrust;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>True while the dive is in progress. Debug overlay and, later, the hitbox driver.</summary>
        public bool IsActive => _active;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_active)
            {
                Continue(sim, in info);
                return;
            }

            TryStart(sim, in input, in info);
        }

        public override void Reset()
        {
            _active = false;
            _startTick = 0;
        }

        /// <summary>
        /// End the dive with an upward pop. Called by the combat layer when the thrust connects —
        /// the bounce is what makes the move chainable down a column of enemies, and it is the
        /// reason connecting feels different from simply landing.
        /// </summary>
        public void Bounce(CharacterSim sim)
        {
            if (!_active) return;

            sim.State.Velocity.y = _tuning.DownThrustBounceSpeed;
            Stop(sim);
        }

        private void TryStart(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (state.Grounded) return;
            if (!input.Attack.Pressed) return;
            if (input.Move.y > -_tuning.CrouchInputThreshold) return;
            if (!sim.Can(LockFlags.Attack)) return;

            if (!sim.TryLock(this, DiveLock, LockPriority.Attack)) return;

            _active = true;
            _startTick = info.Tick;
            state.Velocity.y = -_tuning.DownThrustSpeed;
        }

        private void Continue(CharacterSim sim, in SimTickInfo info)
        {
            var state = sim.State;

            // Landing ends it. LandedThisTick is published after resolution, so a module cannot
            // read it — grounded state refreshed at the top of the tick is the signal available here.
            if (state.Grounded)
            {
                Stop(sim);
                return;
            }

            state.Velocity.y = -_tuning.DownThrustSpeed;

            sim.SetCancelWindow(this, info.Tick - _startTick >= _tuning.DownThrustMinTicks);
        }

        private void Stop(CharacterSim sim)
        {
            _active = false;
            sim.ReleaseLock(this);
        }
    }
}
