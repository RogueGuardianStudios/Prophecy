using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Horizontal locomotion and facing, on the ground and in the air.
    ///
    /// <para>Accelerates toward a target speed rather than assigning it. Instant velocity reads as
    /// slippery-free but weightless, and every downstream feel decision — how far a run carries
    /// across a gap, whether a turn costs anything — is a property of these rates.</para>
    ///
    /// <para><b>Run is a toggle by default</b> (open knob #1 in the plan). Zelda II had no analog
    /// stick, and a toggle makes top speed a decision the player commits to rather than a thumb
    /// position they hold. <see cref="MovementTuningData.AnalogSpeedBlend"/> flips to the modern
    /// stick-magnitude model so the two can be A/B'd on the same build — the comparison is the
    /// point, and it must not cost a code change.</para>
    ///
    /// <para>While <see cref="LockFlags.Move"/> is held by someone else, input reads as neutral
    /// rather than being ignored outright: a character locked mid-attack slides to a stop under
    /// friction instead of freezing in place, which is what sells the commitment.</para>
    /// </summary>
    public sealed class GroundMove : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private bool _running;

        public GroundMove(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.GroundMove;
        public override int Order => ModuleOrder.GroundMove;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>Current run-toggle state. Read by the debug overlay; never by another module.</summary>
        public bool IsRunning => _running;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            UpdateRunLatch(in input);

            float axis = input.Move.x;
            if (Mathf.Abs(axis) < _tuning.MoveDeadzone) axis = 0f;
            if (!sim.Can(LockFlags.Move)) axis = 0f;

            float target = axis == 0f ? 0f : Mathf.Sign(axis) * TopSpeed(state, Mathf.Abs(axis));

            bool pushing = axis != 0f;
            float rate = state.Grounded
                ? (pushing ? _tuning.GroundAcceleration : _tuning.GroundFriction)
                : (pushing ? _tuning.AirAcceleration : _tuning.AirFriction);

            state.Velocity.x = Mathf.MoveTowards(state.Velocity.x, target, rate * info.DeltaSeconds);

            if (axis != 0f && sim.Can(LockFlags.Turn))
                state.Facing = axis < 0f ? -1 : 1;
        }

        public override void Reset()
        {
            _running = false;
        }

        private void UpdateRunLatch(in InputFrame input)
        {
            switch (_tuning.RunMode)
            {
                case RunMode.Toggle:
                    if (input.RunToggle.Pressed) _running = !_running;
                    break;

                case RunMode.Hold:
                    _running = input.RunToggle.Held;
                    break;

                case RunMode.AnalogBlend:
                    // Stick magnitude decides speed; a button on top would be two controls for one axis.
                    _running = false;
                    break;
            }
        }

        private float TopSpeed(CharacterState state, float axisMagnitude)
        {
            float speed;

            if (_tuning.RunMode == RunMode.AnalogBlend)
            {
                // Remap past the deadzone so the slowest usable input is a walk, not a crawl.
                float t = Mathf.InverseLerp(_tuning.MoveDeadzone, 1f, axisMagnitude);
                speed = Mathf.Lerp(_tuning.WalkSpeed, _tuning.RunSpeed, t);
            }
            else
            {
                // Toggle and Hold differ only in how _running is latched, not in what it means.
                speed = _running ? _tuning.RunSpeed : _tuning.WalkSpeed;
            }

            if (state.Stance == Stance.Crouch)
                speed *= _tuning.CrouchSpeedMultiplier;

            return speed;
        }
    }
}
