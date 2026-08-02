using System;

namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// One temporary or permanent change to one stat.
    ///
    /// <para><b>A struct, and expiring on a tick.</b> HopeFell's is a class holding a
    /// <c>CountdownTimer</c> that ticks on wall clock, with an event subscription per instance and
    /// a closure to unsubscribe. Two problems here: the allocation is per-modifier-per-entity in a
    /// game aiming at a hundred combatants, and — worse — a buff that expires on real time lasts a
    /// different number of ticks at 30 fps than at 144. Every other window in this project is
    /// authored in ticks precisely so that cannot happen.</para>
    ///
    /// <para>So duration is a tick count and expiry is a comparison. No timer, no event, no
    /// allocation, and a buff lasts exactly as long on every machine.</para>
    /// </summary>
    [Serializable]
    public struct StatModifier
    {
        /// <summary>Which stat this touches.</summary>
        public StatKind Kind;

        /// <summary>When it applies. See <see cref="StatStage"/> — this is what makes the result
        /// independent of the order modifiers were picked up in.</summary>
        public StatStage Stage;

        /// <summary>How much. A fraction for <see cref="StatStage.Percent"/> — 0.25 is +25%.</summary>
        public float Value;

        /// <summary>
        /// The tick this stops applying on. <see cref="Permanent"/> for gear and level-ups.
        ///
        /// <para>An absolute tick rather than a remaining duration, so nothing has to be counted
        /// down and a modifier is immutable once created.</para>
        /// </summary>
        public long ExpiresOnTick;

        /// <summary>Never expires. Level-ups, bound Protectors, worn gear.</summary>
        public const long Permanent = long.MaxValue;

        /// <summary>A source tag, so everything from one item can be removed together.</summary>
        public int SourceId;

        /// <summary>
        /// Identifies this exact modifier, so one effect can be cancelled without taking its
        /// siblings with it.
        ///
        /// <para>HopeFell removes a modifier by object reference, which a struct cannot offer —
        /// and "remove the first one that compares equal" is ambiguous the moment two identical
        /// buffs are stacked. An explicit id is unambiguous and survives being copied.</para>
        ///
        /// <para>Zero means unidentified: still removable by source, just not individually.</para>
        /// </summary>
        public int Id;

        public bool IsPermanent => ExpiresOnTick == Permanent;

        public bool IsActiveOn(long tick) => tick < ExpiresOnTick;

        public static StatModifier Flat(StatKind kind, float value,
                                        long expiresOnTick = Permanent, int sourceId = 0, int id = 0) =>
            new StatModifier
            {
                Kind = kind, Stage = StatStage.Flat, Value = value,
                ExpiresOnTick = expiresOnTick, SourceId = sourceId, Id = id,
            };

        public static StatModifier Percent(StatKind kind, float fraction,
                                           long expiresOnTick = Permanent, int sourceId = 0, int id = 0) =>
            new StatModifier
            {
                Kind = kind, Stage = StatStage.Percent, Value = fraction,
                ExpiresOnTick = expiresOnTick, SourceId = sourceId, Id = id,
            };

        public static StatModifier Final(StatKind kind, float value,
                                         long expiresOnTick = Permanent, int sourceId = 0, int id = 0) =>
            new StatModifier
            {
                Kind = kind, Stage = StatStage.Final, Value = value,
                ExpiresOnTick = expiresOnTick, SourceId = sourceId, Id = id,
            };

        public override string ToString() =>
            $"{Kind} {Stage} {Value:+0.##;-0.##}" + (IsPermanent ? "" : $" until {ExpiresOnTick}");
    }
}
