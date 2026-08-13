namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// Anything that can answer "what is your Might". HopeFell's <c>IStatProvider</c>, kept for the
    /// same reason: a skill or a hit box should not care whether it is being thrown by the player's
    /// <see cref="StatBlock"/>, by an enemy's, or by a test double with fixed numbers.
    /// </summary>
    public interface IStatSource
    {
        /// <summary>The stat after modifiers. Fractional, because modifiers are.</summary>
        float Effective(StatKind kind);

        /// <summary>The raw level, before any modifier. What progression and the power gate read.</summary>
        int LevelOf(StatKind kind);
    }

    /// <summary>
    /// Fixed stats that never change. For enemies with no progression, and for tests that want a
    /// specific number rather than a whole <see cref="StatBlock"/>.
    /// </summary>
    public sealed class FixedStats : IStatSource
    {
        private readonly int[] _levels;

        public FixedStats(int might = StatBlock.MinLevel,
                          int flame = StatBlock.MinLevel,
                          int heart = StatBlock.MinLevel)
        {
            _levels = new int[StatBlock.Count];

            // Assigned by index so adding a stat leaves this compiling with a sensible default
            // rather than silently omitting the new one.
            for (int i = 0; i < _levels.Length; i++) _levels[i] = StatBlock.MinLevel;

            Set(StatKind.Might, might);
            Set(StatKind.Flame, flame);
            Set(StatKind.Heart, heart);
        }

        private void Set(StatKind kind, int level)
        {
            int index = (int)kind;
            if (index >= 0 && index < _levels.Length) _levels[index] = level;
        }

        public float Effective(StatKind kind) => LevelOf(kind);

        public int LevelOf(StatKind kind)
        {
            int index = (int)kind;
            return index >= 0 && index < _levels.Length ? _levels[index] : StatBlock.MinLevel;
        }
    }
}
