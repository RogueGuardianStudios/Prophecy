using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Kicks off a wall: upward, and away from it.
    ///
    /// <para><b>The control lock is not optional.</b> A wall jump that leaves horizontal steering
    /// free is no wall jump at all — the player is holding toward the wall (that is how they got
    /// there), so the instant they launch, their own input drags them straight back into it. The
    /// result is a character that climbs a single wall forever without the player meaning to.
    /// Suppressing <see cref="LockFlags.Move"/> for a handful of ticks makes the jump actually
    /// commit to leaving.</para>
    ///
    /// <para>The lock goes through the arbiter rather than a private flag, so wall-sliding also
    /// stops re-engaging during it — the slide asks whether movement is permitted and gets told
    /// no, without either module knowing the other exists.</para>
    ///
    /// <para>Facing flips to the launch direction. Kicking away while still looking at the wall
    /// reads as sliding backwards off it.</para>
    /// </summary>
    public sealed class WallJump : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private long _lockUntilTick = long.MinValue;

        public WallJump(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.WallJump;
        public override int Order => ModuleOrder.WallJump;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            ExpireControlLock(sim, in info);

            var state = sim.State;

            if (state.Grounded) return;
            if (state.Attachment != AttachmentKind.None) return;
            if (!input.Jump.Pressed) return;
            if (!sim.Can(LockFlags.Jump)) return;

            int wall = WallDirection(sim);
            if (wall == 0) return;

            state.Velocity.y = _tuning.WallJumpVelocity;
            state.Velocity.x = -wall * _tuning.WallJumpPushSpeed;
            state.Facing = -wall;
            state.Stance = Stance.Air;

            if (_tuning.WallJumpControlLockTicks > 0 &&
                sim.TryLock(this, LockFlags.Move, LockPriority.Movement))
            {
                _lockUntilTick = info.Tick + _tuning.WallJumpControlLockTicks;
            }
        }

        public override void Reset()
        {
            _lockUntilTick = long.MinValue;
        }

        private void ExpireControlLock(CharacterSim sim, in SimTickInfo info)
        {
            if (_lockUntilTick == long.MinValue) return;

            // Landing ends it early: once there is ground underfoot, taking away steering is just
            // an unresponsive moment the player cannot explain.
            if (info.Tick < _lockUntilTick && !sim.State.Grounded) return;

            sim.ReleaseLock(this);
            _lockUntilTick = long.MinValue;
        }

        private int WallDirection(CharacterSim sim)
        {
            var body = sim.State.Body;

            if (sim.World.HasWall(body, 1, _tuning.WallProbeDistance)) return 1;
            if (sim.World.HasWall(body, -1, _tuning.WallProbeDistance)) return -1;
            return 0;
        }
    }
}
