using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Watches falls and reports landings — impact speed, airborne duration, and an optional
    /// hard-landing recovery lock.
    ///
    /// <para>Impact speed cannot be read after the fact: the vertical sweep zeroes velocity the
    /// instant it hits the ground, so by the time anything notices a landing the number that
    /// would have described it is gone. This module keeps the running peak, which is why landing
    /// squash, dust and (later) fall damage all have something to scale against.</para>
    ///
    /// <para><b>The landing is reported one tick after it resolves.</b> Modules run before
    /// integration, so the earliest a module can observe a landing is the following tick. That is
    /// structural, not an oversight — presentation wanting the exact frame should read
    /// <see cref="CharacterState.LandedThisTick"/>, which the sim publishes after resolution.</para>
    ///
    /// <para>Hard-landing lag ships at zero ticks on purpose. It is a real tool for weight, and
    /// also the fastest way to make a game feel unresponsive, so it starts off and gets turned on
    /// deliberately during tuning rather than arriving by default.</para>
    /// </summary>
    public sealed class FallLand : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private bool _wasGrounded = true;
        private float _peakFallSpeed;
        private long _lockUntilTick = long.MinValue;

        public FallLand(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.FallLand;
        public override int Order => ModuleOrder.FallLand;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>Downward speed at the most recent landing, in m/s.</summary>
        public float LastImpactSpeed { get; private set; }

        /// <summary>Tick on which the most recent landing was reported.</summary>
        public long LastLandingTick { get; private set; } = long.MinValue;

        /// <summary>Whether the most recent landing exceeded <see cref="MovementTuningData.HardLandingSpeed"/>.</summary>
        public bool LastLandingWasHard { get; private set; }

        /// <summary>Consecutive ticks spent airborne. Zero while grounded.</summary>
        public int AirborneTicks { get; private set; }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (!state.Grounded)
            {
                AirborneTicks++;

                float fallSpeed = -state.Velocity.y;
                if (fallSpeed > _peakFallSpeed) _peakFallSpeed = fallSpeed;
            }
            else
            {
                if (!_wasGrounded) ReportLanding(sim, in info);

                AirborneTicks = 0;
                _peakFallSpeed = 0f;
            }

            if (_lockUntilTick != long.MinValue && info.Tick >= _lockUntilTick)
            {
                sim.ReleaseLock(this);
                _lockUntilTick = long.MinValue;
            }

            _wasGrounded = state.Grounded;
        }

        public override void Reset()
        {
            _wasGrounded = true;
            _peakFallSpeed = 0f;
            _lockUntilTick = long.MinValue;
            AirborneTicks = 0;
            LastImpactSpeed = 0f;
            LastLandingTick = long.MinValue;
            LastLandingWasHard = false;
        }

        private void ReportLanding(CharacterSim sim, in SimTickInfo info)
        {
            LastImpactSpeed = _peakFallSpeed;
            LastLandingTick = info.Tick;
            LastLandingWasHard = _peakFallSpeed >= _tuning.HardLandingSpeed;

            if (!LastLandingWasHard || _tuning.HardLandingTicks <= 0) return;

            if (sim.TryLock(this, LockFlags.Move | LockFlags.Jump, LockPriority.Movement))
                _lockUntilTick = info.Tick + _tuning.HardLandingTicks;
        }
    }
}
