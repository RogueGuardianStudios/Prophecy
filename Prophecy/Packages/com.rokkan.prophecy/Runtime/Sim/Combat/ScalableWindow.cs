using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// A tick window whose length can be scaled by a stat — the shape parry and i-frame windows
    /// need so gear can widen them.
    ///
    /// <para><b>Start and duration. The opening tick never moves.</b> Every window opens on the
    /// tick it was authored to open on, and a stat only ever changes how long it stays open. That
    /// is the property the whole combat read depends on: the opening tick is the cue a player
    /// learns off a telegraph, and a better shield must not shift the moment they are learning.
    /// Gear makes an answer easier to <i>hold</i>, never earlier to <i>start</i>.</para>
    ///
    /// <para>It also removes a whole class of arithmetic problem. A centred window cannot be
    /// symmetric at even durations — there is no half tick, so a 2-tick window around tick 5 must
    /// be either {4,5} or {5,6}, and the choice leaks into feel. Anchoring at the start means only
    /// the duration is ever rounded and the opening tick is exact by construction.</para>
    ///
    /// <para>The authored <see cref="BaseStart"/>/<see cref="BaseDuration"/> is the unmodified
    /// truth: what the move does with no equipment. <see cref="Resolve"/> is pure and always
    /// derives from that base, so two sources of the same buff cannot compound.</para>
    ///
    /// <para><b>Clamps are not optional.</b> Gear stacks, and an unbounded parry window becomes an
    /// exploit the moment two sources of the same bonus exist.</para>
    /// </summary>
    [Serializable]
    public struct ScalableWindow
    {
        [Tooltip("Ticks from the start of the action at which the window opens. A stat never moves this.")]
        public int BaseStart;

        [Tooltip("Length of the unmodified window in ticks. 0 means the action has no such window.")]
        public int BaseDuration;

        [Tooltip("Shortest the window may become after stat scaling. Stops a debuff erasing it entirely.")]
        public int MinTicks;

        [Tooltip("Longest the window may become after stat scaling. Stops stacked gear making it free.")]
        public int MaxTicks;

        public ScalableWindow(int baseStart, int baseDuration, int minTicks = 1, int maxTicks = 60)
        {
            BaseStart = baseStart;
            BaseDuration = baseDuration;
            MinTicks = minTicks;
            MaxTicks = maxTicks;
        }

        /// <summary>An absent window. Scaling it by anything still yields nothing.</summary>
        public static ScalableWindow None => new ScalableWindow(0, 0);

        /// <summary>True if this window exists at all before any scaling.</summary>
        public bool IsAuthored => BaseDuration > 0;

        public TickRange BaseRange => new TickRange(BaseStart, BaseStart + BaseDuration);

        /// <summary>
        /// The effective window with <paramref name="multiplier"/> applied — 1.0 for no gear,
        /// 1.25 for a 25% longer parry, 0.5 for a debuff that halves it.
        ///
        /// <para>An unauthored window stays unauthored: no multiplier conjures a parry onto a move
        /// that never had one.</para>
        /// </summary>
        public TickRange Resolve(float multiplier = 1f)
        {
            if (!IsAuthored) return TickRange.None;

            int duration = RoundHalfUp(BaseDuration * Mathf.Max(0f, multiplier));

            int min = Mathf.Max(1, MinTicks);
            int max = MaxTicks > 0 ? MaxTicks : int.MaxValue;
            duration = Mathf.Clamp(duration, min, Mathf.Max(min, max));

            return new TickRange(BaseStart, BaseStart + duration);
        }

        /// <summary>
        /// Deliberately not <c>Mathf.RoundToInt</c>, which rounds half to even: it turns 2.5 into 2
        /// but 3.5 into 4. A +25% stat on a 2-tick window would then round *down* to nothing while
        /// the same stat on a 3-tick window rounded up — the kind of inconsistency that reads as a
        /// bug and is miserable to track down. Half-up is boring and predictable.
        /// </summary>
        private static int RoundHalfUp(float value) => (int)(value + 0.5f);
    }
}
