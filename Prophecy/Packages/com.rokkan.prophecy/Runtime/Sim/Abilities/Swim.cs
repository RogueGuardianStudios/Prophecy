using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Being in water, before any art makes it kind: the global rule is that the player SINKS
    /// (gray-box doc §3, design doc 5.8 — water is a struggle by default; Mirefen's Buoyancy is
    /// the grace that flips it).
    ///
    /// <para><b>Two rules carry the whole feel, and only two.</b> The sink cap: a fall in
    /// water never adds up past a gentle drift — that IS the sink. The trudge: a −20% Speed
    /// debuff through the stat system while submerged (Matt's number; the same lane the
    /// Censer will slow enemies through). Everything else behaves EXACTLY as on land — jumps,
    /// ledge grabs, all of it (Matt's second drive killed the buoyant jump boost: it overshot
    /// every grab, and the breath is the hazard anyway, not escape difficulty).</para>
    ///
    /// <para><b>The breath is the hazard.</b> While the head is under, a timer runs; surfacing
    /// refills it. When it runs out, the water takes health on an interval, straight into
    /// vitals — the defence gates are never consulted, because there is no parrying drowning.
    /// Death hands over to the ordinary death rule, whatever it is that week.</para>
    ///
    /// <para>Runs AFTER the jumps and the wall slide (their launches must exist before water
    /// can lean on them) and BEFORE the thrusts, whose re-asserted velocities deliberately
    /// pierce water at full speed — a committed dive is a committed dive.</para>
    /// </summary>
    public sealed class Swim : AbilityModule
    {
        /// <summary>How far under the waterline still counts as ON it, for the slow gate.</summary>
        private const float SurfaceSkin = 0.05f;

        /// <summary>The water's Speed debuff group — one constant, so every submerged tick
        /// REFRESHES the same modifier instead of stacking a new one.</summary>
        private const int WaterSlowStackKey = 0x5EA5;

        private readonly MovementTuningData _tuning;

        private int _breath;
        private long _nextDrownTick;

        public Swim(MovementTuningData tuning)
        {
            _tuning = tuning;
            _breath = tuning.BreathTicks;
        }

        public override AbilityId Id => AbilityId.Swim;
        public override int Order => ModuleOrder.Swim;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>Feet inside a pool this tick.</summary>
        public bool InWater { get; private set; }

        /// <summary>Surface minus feet — positive below the waterline, zero when dry.</summary>
        public float Depth { get; private set; }

        /// <summary>The whole body is under — the breath is running.</summary>
        public bool HeadUnder { get; private set; }

        /// <summary>Breath left, 1 at the surface, 0 when the drowning starts. Overlay.</summary>
        public float BreathFraction =>
            _tuning.BreathTicks <= 0 ? 1f : Mathf.Clamp01(_breath / (float)_tuning.BreathTicks);

        /// <summary>Out of breath and paying for it.</summary>
        public bool Drowning => InWater && HeadUnder && _breath <= 0;

        public override void Reset()
        {
            InWater = false;
            HeadUnder = false;
            Depth = 0f;
            _breath = _tuning.BreathTicks;
            _nextDrownTick = 0;
        }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            InWater = sim.World.TryGetWater(state.Position, out var water);

            if (!InWater)
            {
                Depth = 0f;
                HeadUnder = false;
                _breath = _tuning.BreathTicks;
                return;
            }

            Depth = water.Max.y - state.Position.y;
            HeadUnder = Depth > state.BodySize.y;

            // Submerged past the surface skin — feet ON the waterline (Buoyancy's water-walk)
            // are not wading, and walking on water at full stride is the art's whole promise.
            if (Depth > SurfaceSkin)
            {
                // The sink cap is the WHOLE vertical story. No buoyant force, deliberately
                // (Matt's second drive: jumps and ledge grabs must behave exactly as on land —
                // the boost overshot every grab). Gravity pulls as it always does; the water
                // just refuses to let a fall add up.
                if (state.Velocity.y < -_tuning.WaterSinkSpeed)
                    state.Velocity.y = -_tuning.WaterSinkSpeed;

                // The trudge: a Speed debuff through the stat system — the same lane the
                // Censer will slow enemies through, and the honest way to say "20% slower"
                // (an exponential drag loses the argument with the mover's acceleration and
                // reads as nothing). Refreshed every submerged tick, it lapses on its own two
                // ticks after the water ends.
                sim.Stats.Apply(new StatModifier
                {
                    Kind = StatKind.Speed,
                    Stage = StatStage.Percent,
                    Value = -_tuning.WaterSpeedPenalty,
                    ExpiresOnTick = info.Tick + 2,
                    StackKey = WaterSlowStackKey,
                });
            }

            Breathe(sim, in info);
        }

        private void Breathe(CharacterSim sim, in SimTickInfo info)
        {
            if (!HeadUnder)
            {
                _breath = _tuning.BreathTicks;
                return;
            }

            if (_breath > 0)
            {
                _breath--;
                return;
            }

            if (!sim.Vitals.IsAlive) return;
            if (info.Tick < _nextDrownTick) return;

            sim.Vitals.ApplyDamage(_tuning.DrownDamage, info.Tick);
            _nextDrownTick = info.Tick + _tuning.DrownIntervalTicks;
        }
    }
}
