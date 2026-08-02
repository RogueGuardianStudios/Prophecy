using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// "This scales off that stat, by this much." The authoring unit for anything whose strength
    /// depends on the character throwing it.
    ///
    /// <para>HopeFell's equivalent is <c>StatScaleStruct</c>, held as a <c>List</c> on
    /// <c>SkillStratagy</c> so one skill can scale off several stats at once. Prophecy needs the
    /// same the moment a flame-art costs Flame and hits for Might, so the list form is kept.</para>
    /// </summary>
    [Serializable]
    public struct StatScale
    {
        [SerializeField, Tooltip("Which stat this scales off.")]
        private StatKind _kind;

        [SerializeField, Tooltip("How much each effective point of that stat is worth. " +
                                 "0.25 means a stat of 4 contributes 1.")]
        private float _perPoint;

        public StatKind Kind => _kind;

        public float PerPoint => _perPoint;

        public StatScale(StatKind kind, float perPoint)
        {
            _kind = kind;
            _perPoint = perPoint;
        }

        /// <summary>What this scale contributes, given a source's stats.</summary>
        public float Evaluate(IStatSource source) =>
            source == null ? 0f : source.Effective(_kind) * _perPoint;

        public override string ToString() => $"{_kind} x{_perPoint:0.###}";
    }

    /// <summary>Helpers for a set of scales, so callers do not each write the same loop.</summary>
    public static class StatScaleExtensions
    {
        /// <summary>
        /// Sum of every scale's contribution. An empty or null list contributes nothing, which is
        /// the right default: an unauthored skill should do its flat damage, not zero.
        /// </summary>
        public static float Evaluate(this IReadOnlyList<StatScale> scales, IStatSource source)
        {
            if (scales == null || source == null) return 0f;

            float total = 0f;
            for (int i = 0; i < scales.Count; i++) total += scales[i].Evaluate(source);
            return total;
        }

        /// <summary>
        /// Authored damage plus what the scales add, floored at one so a scaled hit that lands is
        /// never free. The shape combat actually wants.
        /// </summary>
        public static int ApplyToDamage(this IReadOnlyList<StatScale> scales, IStatSource source,
                                        int authoredDamage)
        {
            if (authoredDamage <= 0) return authoredDamage;

            return Mathf.Max(1, Mathf.RoundToInt(authoredDamage + scales.Evaluate(source)));
        }
    }
}
