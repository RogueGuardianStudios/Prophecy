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

        /// <summary>
        /// What effect this <i>is</i>, for stacking. Zero means "does not stack-manage" — every
        /// application is independent.
        ///
        /// <para><b>Distinct from both other ids, and it has to be.</b> <see cref="SourceId"/> is
        /// which thing granted it and <see cref="Id"/> is this exact instance; neither answers "is
        /// this the same poison". Two rings of +1 Might should give +2, while two applications of
        /// one poison should not — and that difference is the whole of stacking.</para>
        ///
        /// <para>Zero by default, so gear keeps adding up and only things that opt in are
        /// managed.</para>
        /// </summary>
        public int StackKey;

        /// <summary>
        /// How many instances of this <see cref="StackKey"/> may coexist. Zero or less means one,
        /// because a debuff that silently stacked to infinity is the worse default.
        /// </summary>
        public int MaxStacks;

        /// <summary>Effective stack cap — at least one.</summary>
        public int StackLimit => MaxStacks < 1 ? 1 : MaxStacks;

        /// <summary>
        /// Whether two modifiers are the same effect for stacking. Same key, same stat, same
        /// stage — so one poison debuffing both Might and Heart manages each independently, and
        /// "the strongest" always compares like with like.
        /// </summary>
        public bool SameEffectAs(in StatModifier other) =>
            StackKey != 0 && StackKey == other.StackKey &&
            Kind == other.Kind && Stage == other.Stage;

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
