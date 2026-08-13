using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// A modifier as an author writes it: a duration, not an expiry tick.
    ///
    /// <para>HopeFell's <c>StatModifierDetails</c> exists for exactly this, and gear is the reason
    /// — <c>EquippableItem</c> holds a <c>List&lt;StatModifierDetails&gt;</c> and turns it into
    /// live modifiers on equip. The distinction matters because a <see cref="StatModifier"/> holds
    /// an <i>absolute</i> tick, which is meaningless until the moment it is applied: authored as
    /// "tick 4000" a buff would already have expired the second time you equipped the sword.</para>
    ///
    /// <para>So this is the serialised half and <see cref="Resolve"/> is the join. It is also why
    /// the live struct can stay allocation-free without making the inspector unusable.</para>
    /// </summary>
    [Serializable]
    public struct StatModifierSpec
    {
        [SerializeField] private StatKind _kind;
        [SerializeField] private StatStage _stage;

        [SerializeField, Tooltip("How much. A fraction for Percent — 0.25 is +25%.")]
        private float _value;

        [SerializeField, Tooltip("How long it lasts, in ticks at 60 Hz. ZERO OR LESS MEANS " +
                                 "PERMANENT, which is what worn gear and bound Protectors want.")]
        private int _durationTicks;

        [SerializeField, Tooltip("Identifies the effect for stacking — every poison shares one " +
                                 "key. LEAVE AT ZERO for gear and anything whose second copy is " +
                                 "genuinely a second copy: two rings of +1 Might should give +2.")]
        private int _stackKey;

        [SerializeField, Tooltip("How many can coexist. Reapplying always refreshes the duration " +
                                 "and always keeps the stronger value; this only decides whether a " +
                                 "further instance is added. Zero or less means one.")]
        private int _maxStacks;

        public StatKind Kind => _kind;
        public StatStage Stage => _stage;
        public float Value => _value;
        public int DurationTicks => _durationTicks;
        public int StackKey => _stackKey;
        public int MaxStacks => _maxStacks;
        public bool IsPermanent => _durationTicks <= 0;

        public StatModifierSpec(StatKind kind, StatStage stage, float value, int durationTicks = 0,
                                int stackKey = 0, int maxStacks = 0)
        {
            _kind = kind;
            _stage = stage;
            _value = value;
            _durationTicks = durationTicks;
            _stackKey = stackKey;
            _maxStacks = maxStacks;
        }

        /// <summary>
        /// Turn this into a live modifier starting now.
        /// </summary>
        public StatModifier Resolve(long currentTick, int sourceId = 0, int id = 0) =>
            new StatModifier
            {
                Kind = _kind,
                Stage = _stage,
                Value = _value,
                ExpiresOnTick = IsPermanent ? StatModifier.Permanent : currentTick + _durationTicks,
                SourceId = sourceId,
                Id = id,
                StackKey = _stackKey,
                MaxStacks = _maxStacks,
            };

        public override string ToString() =>
            $"{_kind} {_stage} {_value:+0.##;-0.##}" +
            (IsPermanent ? " permanent" : $" for {_durationTicks}t") +
            (_stackKey == 0 ? "" : $" stack:{_stackKey} x{(_maxStacks < 1 ? 1 : _maxStacks)}");
    }

    /// <summary>Turning a whole authored set into live modifiers — the equip case.</summary>
    public static class StatModifierSpecExtensions
    {
        /// <summary>
        /// Resolve every spec against the current tick, appending into <paramref name="into"/>.
        ///
        /// <para>Appends rather than returning a new list so equipping does not allocate, which
        /// matters once every enemy in a room has gear.</para>
        /// </summary>
        public static void ResolveAll(this IReadOnlyList<StatModifierSpec> specs, long currentTick,
                                      List<StatModifier> into, int sourceId = 0)
        {
            if (specs == null || into == null) return;

            for (int i = 0; i < specs.Count; i++)
                into.Add(specs[i].Resolve(currentTick, sourceId));
        }
    }
}
