using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// The flask row. CAPACITY NEVER SHRINKS (spec §4.3) — what shrinks as the world darkens
    /// is the world's ability to fill the slots, so the HUD always shows the same row and, by
    /// Phase 3, it is a row of empty outlines that nothing ever explains. A drink restores a
    /// WHOLE heart (four quarters), keeping the granularity meaningful.
    /// </summary>
    public sealed class Flasks
    {
        public const int HealQuarters = Vitals.QuartersPerHeart;

        public Flasks(int capacity = 4)
        {
            Capacity = capacity;
            Filled = capacity;
        }

        public int Capacity { get; private set; }
        public int Filled { get; private set; }

        public bool CanDrink => Filled > 0;

        /// <summary>Empty one flask into <paramref name="vitals"/>. A full-health drink still
        /// spends the flask — no confirm, no refusal (spec §1.2: DrinkFlask is immediate);
        /// wasting one is the player's own lesson to learn.</summary>
        public bool TryDrink(Vitals vitals)
        {
            if (Filled <= 0) return false;

            Filled--;
            vitals?.Heal(HealQuarters);
            return true;
        }

        public void RefillAll() => Filled = Capacity;

        /// <summary>Fill one slot — the Chalice's conversion, or a Warding Flame's grace.
        /// False on a full row.</summary>
        public bool FillOne()
        {
            if (Filled >= Capacity) return false;

            Filled++;
            return true;
        }

        public void SetCapacity(int capacity)
        {
            Capacity = Mathf.Max(0, capacity);
            Filled = Mathf.Min(Filled, Capacity);
        }
    }
}
