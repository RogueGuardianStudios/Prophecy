using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// The one rounding law for scaled damage: round to nearest, never below one point.
    ///
    /// <para>Rounded rather than truncated so a 1.5x scale on a 3-damage jab is 5 and not 4;
    /// floored at one so a heavy debuff makes a hit feeble rather than free. The law lived in
    /// three places — the live sweep, the stat block, the scale helpers — and a policy change
    /// landing in one copy would have kept the tests green against the copies they test while
    /// shipped combat behaved differently.</para>
    /// </summary>
    public static class DamageMath
    {
        /// <summary>Authored damage under a multiplier — Might's effect on a swing.</summary>
        public static int Scale(int authored, float scale)
        {
            if (authored <= 0) return authored;
            return Finish(authored * scale);
        }

        /// <summary>Authored damage plus a flat contribution — a stat scale's addition.</summary>
        public static int Add(int authored, float addition)
        {
            if (authored <= 0) return authored;
            return Finish(authored + addition);
        }

        private static int Finish(float raw) => Mathf.Max(1, Mathf.RoundToInt(raw));
    }
}
