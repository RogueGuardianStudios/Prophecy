using UnityEngine;

namespace Rokkan.Prophecy.Sim.AI
{
    /// <summary>
    /// Where a wandering thing wants to go: Zelda II's overworld menace, as one pure function.
    ///
    /// <para><b>Stateless on purpose, and the shape is forced by two constraints at once.</b>
    /// GOAP strategies are shared ScriptableObjects, so per-agent wander state on the strategy is
    /// the global-variable trap CLAUDE.md documents; and the sim's rules forbid wall-clock
    /// randomness that two machines could disagree about. Hashing (who, when) instead satisfies
    /// both: every agent gets its own heading, every window gets a fresh one, nothing is stored
    /// anywhere, and the same tick always produces the same answer.</para>
    ///
    /// <para>The bias is what makes a wanderer a threat rather than a screensaver. Zelda II's
    /// overworld blobs do not path at you — they meander, weighted toward you, so the map reads
    /// as alive and closing in rather than as pursuit. Bias 0 is a pure drunkard's walk; bias 1
    /// is a homing missile; the interesting range is the middle.</para>
    /// </summary>
    public static class RoamSteering
    {
        /// <summary>
        /// The unit heading agent <paramref name="agentId"/> wants during the window containing
        /// <paramref name="tick"/>.
        /// </summary>
        /// <param name="agentId">Who is wandering. Different agents get different walks.</param>
        /// <param name="tick">The sim tick. Headings hold for a window and then re-roll.</param>
        /// <param name="repickTicks">Window length — how long one heading is kept.</param>
        /// <param name="toTarget">Vector to whatever is being menaced, or zero for nothing.</param>
        /// <param name="bias">0..1: how much the roll is bent toward the target.</param>
        public static Vector2 Heading(int agentId, long tick, int repickTicks,
                                      Vector2 toTarget, float bias)
        {
            long window = tick / Mathf.Max(1, repickTicks);

            var random = RandomDirection(agentId, window);

            if (toTarget.sqrMagnitude < 0.0001f) return random;

            var blended = random * (1f - Mathf.Clamp01(bias)) + toTarget.normalized * Mathf.Clamp01(bias);

            // A blend can cancel to ~zero when the roll points straight away from the target at
            // bias one half. A wanderer that stands still reads as broken, so the roll wins ties.
            return blended.sqrMagnitude < 0.0001f ? random : blended.normalized;
        }

        /// <summary>A unit vector that is arbitrary but repeatable for (who, when).</summary>
        private static Vector2 RandomDirection(int agentId, long window)
        {
            // Knuth-style integer mixing. The constants are the usual multiplicative hashes; all
            // that matters is that neighbouring (agent, window) pairs land far apart.
            uint hash = (uint)agentId * 2654435761u;
            hash ^= (uint)window * 2246822519u;
            hash ^= hash >> 15;
            hash *= 2654435761u;
            hash ^= hash >> 13;

            float angle = (hash % 62832u) * 0.0001f;   // 0 .. 2π in ten-thousandths of a radian

            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
    }
}
