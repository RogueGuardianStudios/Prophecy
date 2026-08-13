using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Overworld locomotion: eight-way movement with no gravity, on the same character, the same
    /// state and the same tick as the side-scroll motor.
    ///
    /// <para>Zelda II's dual structure is a single character moving through two presentations, not
    /// two controllers wearing one name. Declaring <see cref="ValidIn"/> as
    /// <see cref="MovementSpace.TopDown"/> is the whole of the switch — the sim skips this module
    /// in side-scroll and skips <see cref="GroundMove"/> here, and there is exactly one save
    /// shape because there is exactly one character.</para>
    ///
    /// <para>Speed is a single number rather than a walk/run pair: the overworld is a map to
    /// cross, and a second speed there would be a second thing to tune for no gain in feel.</para>
    ///
    /// <para><b>Known gap:</b> <c>CharacterSim.Integrate</c> currently applies top-down motion
    /// without sweeping it against the world, so the overworld has no walls yet. That belongs
    /// with the overworld scene and its own tests, not smuggled in here.</para>
    /// </summary>
    public sealed class TopDownMove : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        public TopDownMove(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.TopDownMove;
        public override int Order => ModuleOrder.TopDownMove;
        public override MovementSpace ValidIn => MovementSpace.TopDown;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            var axis = input.Move;
            if (axis.magnitude < _tuning.MoveDeadzone) axis = Vector2.zero;
            if (!sim.Can(LockFlags.Move)) axis = Vector2.zero;

            // Clamp rather than normalise: a partly-deflected stick should move you slowly, but a
            // diagonal must not outrun a cardinal.
            if (axis.sqrMagnitude > 1f) axis = axis.normalized;

            var target = axis * _tuning.TopDownSpeed;

            bool pushing = axis != Vector2.zero;
            float rate = (pushing ? _tuning.TopDownAcceleration : _tuning.TopDownFriction)
                         * info.DeltaSeconds;

            state.Velocity = Vector2.MoveTowards(state.Velocity, target, rate);

            if (axis.x != 0f && sim.Can(LockFlags.Turn))
                state.Facing = axis.x < 0f ? -1 : 1;
        }
    }
}
