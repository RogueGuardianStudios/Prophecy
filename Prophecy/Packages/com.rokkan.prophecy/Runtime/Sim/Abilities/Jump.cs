using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Jumping, with the three forgiveness mechanics that separate a jump that feels responsive
    /// from one that feels broken: coyote time, input buffering, and variable height.
    ///
    /// <para><b>Coyote time counts ticks, not seconds.</b> <see cref="CharacterState.LastGroundedTick"/>
    /// is a tick stamp, so the window is exactly the same length at 30, 60 and 144 fps. An
    /// accumulated float would make the forgiveness quietly generous on slow machines.</para>
    ///
    /// <para><b>One jump per contact with the ground.</b> The coyote window is still open on the
    /// ticks just after launch — the character was grounded moments ago, which is the whole point
    /// of the window — so without an explicit guard a second press would buy a free extra jump.
    /// The guard is "the last grounded tick must be more recent than the last jump", which
    /// naturally re-arms on landing and never on the way up.</para>
    ///
    /// <para><b>Height is authored in metres.</b> Launch velocity is derived as sqrt(2gh), so
    /// level geometry can be built against a number that means something — a 2.4 m jump clears a
    /// 2 m ledge — instead of a velocity whose reach can only be found by trial.</para>
    ///
    /// <para>Nothing here knows gravity exists. It writes a velocity; <see cref="GravityModule"/>
    /// takes it away again. That is the architecture rule holding on the one pair of abilities
    /// most tempted to merge.</para>
    /// </summary>
    public sealed class Jump : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private long _bufferedPressTick = long.MinValue;
        private long _lastJumpTick = long.MinValue;
        private bool _rising;

        public Jump(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.Jump;
        public override int Order => ModuleOrder.Jump;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>True while this module still owns an un-cut ascent. Debug overlay only.</summary>
        public bool IsRising => _rising;

        /// <summary>Ticks left on a remembered jump press, for the debug overlay. Zero when none.</summary>
        public int BufferedTicksRemaining(long currentTick)
        {
            if (_bufferedPressTick == long.MinValue) return 0;
            long left = _tuning.JumpBufferTicks - (currentTick - _bufferedPressTick);
            return left > 0 ? (int)left : 0;
        }

        /// <summary>Ticks of coyote credit left, for the debug overlay. Zero when spent.</summary>
        public int CoyoteTicksRemaining(CharacterState state, long currentTick)
        {
            if (state.Grounded) return _tuning.CoyoteTicks;
            long left = _tuning.CoyoteTicks - state.TicksSinceGrounded(currentTick);
            return left > 0 ? (int)left : 0;
        }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (input.Jump.Pressed)
                _bufferedPressTick = info.Tick;

            TryStart(sim, in info);

            // Variable height. Evaluated after the start, so a press and release that the capture
            // layer latched into the same tick produces the shortest possible hop rather than a
            // full-height jump — a tap must read as a tap even when it lands inside one tick.
            if (_rising && input.Jump.Released && state.Velocity.y > 0f)
            {
                state.Velocity.y *= _tuning.JumpCutMultiplier;
                _rising = false;
            }

            if (state.Velocity.y <= 0f)
                _rising = false;
        }

        public override void Reset()
        {
            _bufferedPressTick = long.MinValue;
            _lastJumpTick = long.MinValue;
            _rising = false;
        }

        private void TryStart(CharacterSim sim, in SimTickInfo info)
        {
            if (_bufferedPressTick == long.MinValue) return;

            if (info.Tick - _bufferedPressTick > _tuning.JumpBufferTicks)
            {
                _bufferedPressTick = long.MinValue;
                return;
            }

            // A lock must not eat the buffered press — the whole point of buffering is that the
            // input survives the moment it could not be acted on.
            if (!sim.Can(LockFlags.Jump)) return;

            var state = sim.State;

            if (!state.Grounded && state.TicksSinceGrounded(info.Tick) > _tuning.CoyoteTicks)
                return;

            if (_lastJumpTick != long.MinValue && state.LastGroundedTick <= _lastJumpTick)
                return;

            if (state.Stance == Stance.Crouch && !sim.HasHeadroomToStand())
                return;

            state.Velocity.y = _tuning.JumpVelocity;
            state.Stance = Stance.Air;

            _bufferedPressTick = long.MinValue;
            _lastJumpTick = info.Tick;
            _rising = true;
        }
    }
}
