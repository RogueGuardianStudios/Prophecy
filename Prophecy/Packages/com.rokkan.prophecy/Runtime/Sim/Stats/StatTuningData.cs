using System;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// What a stat level is worth. Plain serialisable data, the same shape as
    /// <c>MovementTuningData</c> and <c>CombatTuningData</c>.
    ///
    /// <para><b>Derived, never stored.</b> A save holds "Heart 4", not "max health 220". Retuning
    /// what a Heart is worth then reaches every existing save instead of only new ones — and the
    /// numbers here are certain to move, because none of them has been played yet.</para>
    /// </summary>
    [Serializable]
    public class StatTuningData
    {
        [Header("Heart — life")]
        [Tooltip("Health at Heart 1.")]
        public int BaseHealth = 100;

        [Tooltip("Health added per Heart level above 1.")]
        public int HealthPerHeart = 40;

        [Header("Flame — magic")]
        [Tooltip("Flame meter at Flame 1.")]
        public int BaseFlame = 40;

        [Tooltip("Flame added per level above 1.")]
        public int FlamePerLevel = 20;

        [Header("Might — attack")]
        [Tooltip("Damage multiplier at Might 1. One means authored damage is what lands.")]
        public float BaseDamageScale = 1f;

        [Tooltip("Added to the damage multiplier per Might level above 1. 0.25 makes Might 8 " +
                 "deal 2.75x — a large spread, deliberately: the power spike from binding a " +
                 "Protector has to feel enormous or the whole tragedy does not land (§6.2).")]
        public float DamageScalePerMight = 0.25f;

        [Header("Resolve — the XP curve")]
        [Tooltip("Resolve needed for the first level-up.")]
        public int BaseResolveCost = 50;

        [Tooltip("Multiplied into the cost per level already earned. 1.35 means each level costs " +
                 "about a third more than the last.")]
        public float ResolveCostGrowth = 1.35f;

        /// <summary>Health at a given effective Heart.</summary>
        public int MaxHealthFor(float heart) =>
            Mathf.Max(1, Mathf.RoundToInt(BaseHealth + (heart - 1f) * HealthPerHeart));

        /// <summary>Flame meter at a given effective Flame.</summary>
        public int MaxFlameFor(float flame) =>
            Mathf.Max(0, Mathf.RoundToInt(BaseFlame + (flame - 1f) * FlamePerLevel));

        /// <summary>Outgoing damage multiplier at a given effective Might.</summary>
        public float DamageScaleFor(float might) =>
            Mathf.Max(0f, BaseDamageScale + (might - 1f) * DamageScalePerMight);

        /// <summary>
        /// Resolve required to earn the next level, given how many are already held.
        ///
        /// <para>Geometric, so early levels arrive quickly and late ones are a commitment. The
        /// starting level in each stat is free, so the cost curve counts from there.</para>
        /// </summary>
        public int ResolveForNextLevel(int totalLevelsHeld)
        {
            int earned = Mathf.Max(0, totalLevelsHeld - StatKinds.ProgressionCount * StatBlock.MinLevel);
            return Mathf.Max(1, Mathf.RoundToInt(BaseResolveCost * Mathf.Pow(ResolveCostGrowth, earned)));
        }

        /// <summary>Complain about anything that would produce nonsense. Editor-facing.</summary>
        public string Validate()
        {
            var problems = new System.Text.StringBuilder();

            if (BaseHealth < 1) problems.AppendLine("BaseHealth must be at least 1.");
            if (BaseResolveCost < 1) problems.AppendLine("BaseResolveCost must be at least 1.");

            if (ResolveCostGrowth < 1f)
                problems.AppendLine("ResolveCostGrowth below 1 makes later levels cheaper than " +
                                    "earlier ones, which inverts the whole progression.");

            if (DamageScalePerMight < 0f)
                problems.AppendLine("DamageScalePerMight is negative — raising Might would weaken you.");

            return problems.ToString();
        }
    }
}
