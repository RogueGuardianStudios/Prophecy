using System;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>
    /// A span of fixed sim ticks, measured from the start of an action.
    ///
    /// <para><b>Half-open: <c>[Start, End)</c>.</b> The opening tick is inside the window and the
    /// closing tick is not, so adjacent ranges tile without overlapping and
    /// <c>End - Start</c> is the duration with no off-by-one. A one-tick window is
    /// <c>(n, n+1)</c>.</para>
    ///
    /// <para><b>Why ticks and not seconds or normalised clip time.</b> A tick is the same integer
    /// on every machine and reproduces headless with no animation present. Normalised clip time
    /// re-couples timing to a presentation asset's duration — retime the clip and the parry window
    /// silently moves. Seconds drift with frame rate. This project's acceptance test is identical
    /// combat results at 30, 60 and 144 fps, and only integers get you that.</para>
    /// </summary>
    [Serializable]
    public readonly struct TickRange : IEquatable<TickRange>
    {
        public readonly int Start;
        public readonly int End;

        public TickRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        /// <summary>A range that never opens. The authoring default for "this action has no such window".</summary>
        public static readonly TickRange None = new TickRange(0, 0);

        public int Duration => End - Start;

        /// <summary>True if this range covers at least one tick. Zero-length ranges are "absent",
        /// not "instantaneous" — a window nothing can ever be inside is not a window.</summary>
        public bool IsActive => End > Start && Start >= 0;

        /// <summary>True if <paramref name="tick"/> falls inside, counting from action start.</summary>
        public bool Contains(int tick) => tick >= Start && tick < End;

        /// <summary>Shift the whole range later by <paramref name="ticks"/>.</summary>
        public TickRange Offset(int ticks) => new TickRange(Start + ticks, End + ticks);

        /// <summary>Trim to fit inside <c>[0, limit)</c>. Used when a scaled window would otherwise
        /// run past the end of the action that owns it.</summary>
        public TickRange ClampedTo(int limit)
        {
            int start = Start < 0 ? 0 : Start;
            int end = End > limit ? limit : End;
            return end > start ? new TickRange(start, end) : new TickRange(start, start);
        }

        public bool Equals(TickRange other) => Start == other.Start && End == other.End;
        public override bool Equals(object obj) => obj is TickRange other && Equals(other);
        public override int GetHashCode() => (Start * 397) ^ End;

        public override string ToString() => IsActive ? $"[{Start}..{End}) {Duration}t" : "none";
    }
}
