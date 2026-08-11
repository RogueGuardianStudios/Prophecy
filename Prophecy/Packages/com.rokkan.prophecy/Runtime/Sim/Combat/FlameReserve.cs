using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// The pool arts are cast from — the CONTINUOUS resource, never to be confused with the
    /// Flame RANK (the stat the rite raises; the rank sets this pool's capacity). The UI/Input
    /// spec §2.2 is strict about the split: the rank is a numeral in the pack, the reserve is
    /// a bar on the HUD, and the bar is never labelled "Flame" — if the reserve ever needs a
    /// word, it is "kindling".
    ///
    /// <para>Gray box: capacity is flat and the pool starts full. The rank-driven capacity
    /// curve arrives with the rite; refill contraction arrives with the Warding Flames.</para>
    /// </summary>
    public sealed class FlameReserve
    {
        public FlameReserve(float max = 10f)
        {
            Max = max;
            Current = max;
        }

        public float Current { get; private set; }
        public float Max { get; private set; }

        public float Fraction => Max <= 0f ? 0f : Current / Max;

        public bool CanAfford(float cost) => cost <= Current;

        /// <summary>Spend, all or nothing — a cast the pool cannot cover fails silently
        /// (spec §3.1: no error state beyond the HUD emblem already reading "not enough").</summary>
        public bool TrySpend(float cost)
        {
            if (cost > Current) return false;

            Current = Mathf.Max(0f, Current - cost);
            return true;
        }

        public void Refill() => Current = Max;

        public void SetMax(float max)
        {
            Max = Mathf.Max(0f, max);
            Current = Mathf.Min(Current, Max);
        }
    }
}
