using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Pulls the character down, with a different constant for rising and falling and a softened
    /// band around the apex.
    ///
    /// <para><b>Why gravity is its own module and not part of Jump.</b> Falling has to work when
    /// jumping is disabled — walking off a ledge before the jump ability is granted must still
    /// drop you. Folding gravity into Jump would make one ability's <c>Enabled</c> flag silently
    /// govern another's physics, and unlocking abilities is meant to be a flag flip with no
    /// side effects. It also keeps the pair honest against the architecture rule: Jump reads and
    /// writes velocity, gravity reads and writes velocity, and neither knows the other exists.</para>
    ///
    /// <para>Asymmetric gravity (heavier falling than rising) plus an apex scale is the standard
    /// platformer trick, and it is worth the two extra numbers: it removes the floaty descent
    /// without shortening the rise, and the hang at the top is what makes an airborne down-thrust
    /// aimable at all.</para>
    /// </summary>
    public sealed class GravityModule : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        public GravityModule(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override int Order => ModuleOrder.Gravity;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            // Resting on ground: stop accumulating downward speed, or a long stand would build a
            // huge phantom velocity that fires the moment the character steps off a ledge.
            if (state.Grounded && state.Velocity.y <= 0f)
            {
                state.Velocity.y = 0f;
                return;
            }

            float gravity = state.Velocity.y > 0f ? _tuning.RiseGravity : _tuning.FallGravity;

            if (Mathf.Abs(state.Velocity.y) < _tuning.ApexVelocityThreshold)
                gravity *= _tuning.ApexGravityScale;

            state.Velocity.y -= gravity * info.DeltaSeconds;

            if (state.Velocity.y < -_tuning.MaxFallSpeed)
                state.Velocity.y = -_tuning.MaxFallSpeed;
        }
    }
}
