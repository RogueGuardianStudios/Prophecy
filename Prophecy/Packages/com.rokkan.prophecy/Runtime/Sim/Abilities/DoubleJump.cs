using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Extra jumps in mid-air.
    ///
    /// <para><b>The arming delay is the whole design.</b> Both this and the ground jump see the
    /// same press on the same tick, and the character is airborne within one tick of launching —
    /// so without a delay, a single tap spends the ground jump and the air jump together and the
    /// player never gets a second one. Requiring a few ticks of genuine airtime also stops the
    /// coyote window being cashed in as a free double jump, which is the other way this goes
    /// wrong.</para>
    ///
    /// <para>It re-arms on contact with the ground, never in the air — the count is the resource.</para>
    ///
    /// <para>The wall check is the one place this module looks outward. Next to a wall, a press
    /// belongs to the wall jump, and spending an air jump instead would feel like the input was
    /// stolen. It asks the collision world rather than asking a wall-jump module, so the rule
    /// still holds: modules read the world and the arbiter, never each other.</para>
    /// </summary>
    public sealed class DoubleJump : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private int _used;
        private long _airborneSinceTick = long.MinValue;

        public DoubleJump(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.DoubleJump;
        public override int Order => ModuleOrder.DoubleJump;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>Air jumps still available. Debug overlay only.</summary>
        public int Remaining => _tuning.AirJumps - _used;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (state.Grounded || state.Attachment != AttachmentKind.None)
            {
                _used = 0;
                _airborneSinceTick = long.MinValue;
                return;
            }

            if (_airborneSinceTick == long.MinValue)
                _airborneSinceTick = info.Tick;

            if (!input.Jump.Pressed) return;
            if (_used >= _tuning.AirJumps) return;
            if (info.Tick - _airborneSinceTick < _tuning.AirJumpArmTicks) return;
            if (!sim.Can(LockFlags.Jump)) return;

            if (TouchingAnyWall(sim)) return;

            state.Velocity.y = _tuning.AirJumpVelocity;
            state.Stance = Stance.Air;
            _used++;
        }

        public override void Reset()
        {
            _used = 0;
            _airborneSinceTick = long.MinValue;
        }

        private bool TouchingAnyWall(CharacterSim sim)
        {
            var body = sim.State.Body;
            return sim.World.HasWall(body, 1, _tuning.WallProbeDistance) ||
                   sim.World.HasWall(body, -1, _tuning.WallProbeDistance);
        }
    }
}
