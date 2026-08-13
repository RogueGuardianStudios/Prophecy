using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>
    /// Asset shell over <see cref="StatTuningData"/> — what a stat level is worth.
    ///
    /// <para>Same shape as <c>MovementTuning</c> and <c>CombatTuning</c>: the data is plain C# so
    /// the sim can hold it, and this exists only so a designer can edit it in the inspector and
    /// have the change survive play mode.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Stat Tuning", fileName = "StatTuning")]
    public sealed class StatTuning : ScriptableObject
    {
        [SerializeField] private StatTuningData _data = new StatTuningData();

        public StatTuningData Data => _data;

        private void OnValidate()
        {
            string problems = _data.Validate();

            if (!string.IsNullOrWhiteSpace(problems))
                Debug.LogWarning($"{name}:\n{problems}", this);
        }
    }

    /// <summary>
    /// The stats a character starts with, and any modifiers they are simply born with.
    ///
    /// <para>HopeFell's <c>BaseStats</c>. The player begins at 1/1/1 and earns the rest, but every
    /// other combatant needs its numbers authored — a Protector is not a level-1 hero, and neither
    /// is a late-region grunt. Without this each enemy would have to be levelled up in code.</para>
    ///
    /// <para><b>Levels, not derived numbers.</b> An enemy asset says "Might 5", never "deals 2.0x
    /// damage", so retuning what a Might is worth reaches every enemy already authored.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Prophecy/Stat Profile", fileName = "StatProfile")]
    public sealed class StatProfile : ScriptableObject
    {
        [SerializeField, Range(StatBlock.MinLevel, StatBlock.MaxLevel),
         Tooltip("Attack. Scales damage dealt.")]
        private int _might = StatBlock.MinLevel;

        [SerializeField, Range(StatBlock.MinLevel, StatBlock.MaxLevel),
         Tooltip("Magic. Sizes the Flame meter.")]
        private int _flame = StatBlock.MinLevel;

        [SerializeField, Range(StatBlock.MinLevel, StatBlock.MaxLevel),
         Tooltip("Life. Sizes maximum health.")]
        private int _heart = StatBlock.MinLevel;

        [SerializeField, Tooltip("Modifiers this character simply has — a Protector's aura, a " +
                                 "cursed enemy. Applied at spawn and never removed unless " +
                                 "something removes them by source.")]
        private StatModifierSpec[] _innate = System.Array.Empty<StatModifierSpec>();

        public int Might => _might;
        public int Flame => _flame;
        public int Heart => _heart;
        public System.Collections.Generic.IReadOnlyList<StatModifierSpec> Innate => _innate;

        /// <summary>Source id used for innate modifiers, so they can be told apart from gear.</summary>
        public const int InnateSourceId = -1;

        /// <summary>
        /// Stamp these stats onto a block, replacing whatever was there.
        ///
        /// <para>Clears first so applying a profile twice — respawning an enemy from a pool — does
        /// not stack its innate modifiers into double strength.</para>
        /// </summary>
        public void ApplyTo(StatBlock block, long currentTick)
        {
            if (block == null) return;

            block.RemoveSource(InnateSourceId);

            block.SetLevel(StatKind.Might, _might);
            block.SetLevel(StatKind.Flame, _flame);
            block.SetLevel(StatKind.Heart, _heart);

            block.AddSpecs(_innate, currentTick, InnateSourceId);
        }
    }
}
