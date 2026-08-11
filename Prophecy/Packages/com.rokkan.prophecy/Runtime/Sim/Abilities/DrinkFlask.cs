using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// D-pad Up: drink. Immediate, no confirm (spec §1.2) — a whole heart from one flask,
    /// and drinking at full health still spends it, which is the player's own lesson.
    ///
    /// <para><b>drinkBreaksGuard</b> (spec §1.4 OPEN, lean yes, authored as a flag): when
    /// true, the drink takes a brief FORCE lock that includes Defend — the same mechanism a
    /// hit-react uses to knock a shield down — so a turtled player pays a moment of exposure
    /// for the heal. When false, the drink is a free action and the guard never wavers.</para>
    /// </summary>
    public sealed class DrinkFlask : AbilityModule
    {
        private const LockFlags DrinkLock = LockFlags.Move | LockFlags.Attack | LockFlags.Defend;

        private readonly CombatTuningData _combat;

        private bool _drinking;
        private long _startTick;

        public DrinkFlask(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.DrinkFlask;
        public override int Order => ModuleOrder.DrinkFlask;

        /// <summary>Mid-swig. For the HUD's flask row to wobble someday.</summary>
        public bool IsDrinking => _drinking;

        public override void Reset()
        {
            _drinking = false;
        }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_drinking)
            {
                if (info.Tick - _startTick < _combat.DrinkTicks) return;

                _drinking = false;
                sim.ReleaseLock(this);
                return;
            }

            if (!input.DrinkFlask.Pressed) return;
            if (!sim.Vitals.IsAlive) return;
            if (!sim.Flasks.CanDrink) return;

            // The heal is immediate; the lock, when the flag says so, is the price.
            sim.Flasks.TryDrink(sim.Vitals);

            if (_combat.DrinkBreaksGuard &&
                sim.ForceLock(this, DrinkLock, LockPriority.Reaction))
            {
                _drinking = true;
                _startTick = info.Tick;
            }
        }
    }
}
