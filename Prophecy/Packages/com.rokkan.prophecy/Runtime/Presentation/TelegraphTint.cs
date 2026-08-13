using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The one rule for what colour a telegraphing body is.
    ///
    /// <para>Two components tint a wind-up: <see cref="TrainingAttacker"/> colours the arena's
    /// scripted dummies and <see cref="AttackTelegraph"/> colours every simulated attacker. The
    /// ramp and the colours must read identically in both, or the training does not transfer —
    /// a player who learned "deep orange means block NOW" on a dummy has to be able to read a
    /// real enemy by the same light. Each carrying its own copy of the ramp was one colour edit
    /// away from breaking that, so both delegate here, and the defaults below are the one
    /// source their serialized colours start from.</para>
    /// </summary>
    public static class TelegraphTint
    {
        /// <summary>Wind-up. The window where a block or parry can still be timed.</summary>
        public static readonly Color DefaultWindUp = new Color(1f, 0.78f, 0.25f);

        /// <summary>Committed. Hit boxes are live.</summary>
        public static readonly Color DefaultActive = new Color(1f, 0.2f, 0.12f);

        /// <summary>
        /// The tint for a swing in <paramref name="phase"/>, or null when nothing should show.
        ///
        /// <para>The wind-up ramps toward the active colour rather than holding flat, so the
        /// tell reads as time running out instead of as a state that might last forever —
        /// squared, so the change crowds toward the moment it matters. Active snaps to the full
        /// colour. Recovery is the punish window, and colouring it would suggest otherwise.</para>
        /// </summary>
        public static Color? For(AttackTimeline.Phase phase, int elapsedTicks, int startupTicks,
                                 Color windUp, Color active)
        {
            switch (phase)
            {
                case AttackTimeline.Phase.Startup:
                    float through = startupTicks > 0
                        ? Mathf.Clamp01(elapsedTicks / (float)startupTicks)
                        : 1f;
                    return Color.Lerp(windUp, active, through * through);

                case AttackTimeline.Phase.Active:
                    return active;

                default:
                    return null;
            }
        }
    }
}
