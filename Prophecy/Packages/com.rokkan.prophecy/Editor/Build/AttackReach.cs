using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Measures what an attack can actually touch, read off its authored hit boxes rather
    /// than guessed. Shared between the arena — which spaces its stations off the player's
    /// reach — and the enemy builder, whose planner promises a swing will connect at the
    /// same distance the sim then checks. One measurement, or the two quietly disagree
    /// about what a swing covers.
    /// </summary>
    internal static class AttackReach
    {
        /// <summary>
        /// The furthest and tallest any of the move's boxes gets. Multi-hit moves sweep more
        /// than one volume and the caller has to accommodate the whole move, not box zero.
        /// A null or box-less attack measures as all zeros.
        /// </summary>
        public static void Measure(AttackDefinition definition, out float reach, out float near,
                                   out float low, out float high)
        {
            reach = 0f;
            near = 0f;
            low = 0f;
            high = 0f;

            if (definition?.HitBoxes == null || definition.HitBoxes.Length == 0) return;

            low = float.MaxValue;
            near = float.MaxValue;

            for (int i = 0; i < definition.HitBoxes.Length; i++)
            {
                var box = definition.HitBoxes[i];
                reach = Mathf.Max(reach, box.Offset.x + box.HalfExtents.x);
                near = Mathf.Min(near, box.Offset.x - box.HalfExtents.x);
                low = Mathf.Min(low, box.Offset.y - box.HalfExtents.y);
                high = Mathf.Max(high, box.Offset.y + box.HalfExtents.y);
            }
        }
    }
}
