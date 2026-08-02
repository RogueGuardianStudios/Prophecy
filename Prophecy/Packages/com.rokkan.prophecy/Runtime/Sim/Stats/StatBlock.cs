using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// A character's three stat levels, the modifiers acting on them, and the numbers that fall out.
    ///
    /// <para>Plain C#. It is read every time an attack is armed and every time damage is dealt, so
    /// it holds no engine object, allocates nothing per query, and can be stepped in a headless
    /// test like everything else in the sim.</para>
    ///
    /// <para><b>Resolution is a pure function of level and modifier set.</b> Two machines with the
    /// same level and the same modifiers compute the same number regardless of the order those
    /// modifiers arrived — see <see cref="StatStage"/> for why that is not automatic.</para>
    /// </summary>
    public sealed class StatBlock
    {
        /// <summary>Lowest a stat can be. Level 1 is the starting hero, not zero.</summary>
        public const int MinLevel = 1;

        /// <summary>Highest. Zelda II capped at 8 and the ceiling is part of the pacing.</summary>
        public const int MaxLevel = 8;

        /// <summary>
        /// How many stats there are, read from <see cref="StatKind"/>.
        ///
        /// <para>Sized from the enum rather than written as 3, because the first version was
        /// written as 3 in four places and adding a fourth stat would have thrown on the first
        /// read rather than failing to compile. A count that can disagree with the thing it counts
        /// is a trap however few there are.</para>
        /// </summary>
        public static readonly int Count = System.Enum.GetValues(typeof(StatKind)).Length;

        private readonly int[] _levels = NewLevels();

        private static int[] NewLevels()
        {
            var levels = new int[Count];
            for (int i = 0; i < levels.Length; i++) levels[i] = MinLevel;
            return levels;
        }
        private readonly List<StatModifier> _modifiers = new List<StatModifier>();
        private readonly List<StatModifier> _keep = new List<StatModifier>();

        private StatTuningData _tuning = new StatTuningData();

        /// <summary>How the levels turn into numbers. Never null.</summary>
        public StatTuningData Tuning
        {
            get => _tuning;
            set => _tuning = value ?? new StatTuningData();
        }

        /// <summary>Resolve banked toward the next level-up. Design bible §6.2 — this is XP.</summary>
        public int Resolve { get; private set; }

        /// <summary>Level-ups earned and not yet spent. The player picks which stat.</summary>
        public int UnspentLevels { get; private set; }

        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        // ---------------------------------------------------------------- levels

        public int LevelOf(StatKind kind) => _levels[(int)kind];

        public void SetLevel(StatKind kind, int level) =>
            _levels[(int)kind] = Mathf.Clamp(level, MinLevel, MaxLevel);

        /// <summary>
        /// Spend a banked level-up on a stat. Refuses if there is nothing banked or the stat is
        /// capped, and says so, because silently swallowing the choice is how a player loses a
        /// level-up and cannot tell you what happened.
        /// </summary>
        public bool SpendLevel(StatKind kind)
        {
            if (UnspentLevels <= 0) return false;
            if (_levels[(int)kind] >= MaxLevel) return false;

            _levels[(int)kind]++;
            UnspentLevels--;
            return true;
        }

        /// <summary>
        /// Bank Resolve, and convert it into level-ups at the authored thresholds.
        /// </summary>
        /// <returns>How many level-ups this award produced.</returns>
        public int AwardResolve(int amount)
        {
            if (amount <= 0) return 0;

            Resolve += amount;
            int earned = 0;

            // A loop rather than a single check: a Protector's essence is a large, deliberate
            // spike (§6.2) and can cross several thresholds at once. Awarding one level and
            // discarding the rest would quietly rob the moment the whole progression is built on.
            while (Resolve >= _tuning.ResolveForNextLevel(TotalLevels + earned))
            {
                Resolve -= _tuning.ResolveForNextLevel(TotalLevels + earned);
                earned++;

                if (TotalLevels + earned >= MaxLevel * Count) break;   // every stat capped
            }

            UnspentLevels += earned;
            return earned;
        }

        /// <summary>Sum of every level. What the finale's power gate reads (§6.3).</summary>
        public int TotalLevels
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _levels.Length; i++) total += _levels[i];
                return total;
            }
        }

        // ---------------------------------------------------------------- modifiers

        public void Add(in StatModifier modifier) => _modifiers.Add(modifier);

        /// <summary>Remove everything from one source — unequipping, or a Protector released.</summary>
        public void RemoveSource(int sourceId)
        {
            _keep.Clear();

            for (int i = 0; i < _modifiers.Count; i++)
                if (_modifiers[i].SourceId != sourceId) _keep.Add(_modifiers[i]);

            _modifiers.Clear();
            _modifiers.AddRange(_keep);
        }

        /// <summary>
        /// Drop modifiers that have expired. Called once a tick by the character.
        ///
        /// <para>Pruning rather than filtering at query time: a stat is read several times a tick
        /// and expiry only changes on a tick boundary, so doing it once is both cheaper and the
        /// only way two reads in the same tick are guaranteed to agree.</para>
        /// </summary>
        public void PruneExpired(long tick)
        {
            bool any = false;
            for (int i = 0; i < _modifiers.Count && !any; i++)
                any = !_modifiers[i].IsActiveOn(tick);

            if (!any) return;

            _keep.Clear();
            for (int i = 0; i < _modifiers.Count; i++)
                if (_modifiers[i].IsActiveOn(tick)) _keep.Add(_modifiers[i]);

            _modifiers.Clear();
            _modifiers.AddRange(_keep);
        }

        public void ClearModifiers() => _modifiers.Clear();

        // ---------------------------------------------------------------- resolution

        /// <summary>
        /// The effective level of a stat, with modifiers applied in stage order.
        ///
        /// <para><c>(level + flats) × (1 + percents) + finals</c>. Every stage sums, so the result
        /// does not depend on the order anything was picked up in.</para>
        /// </summary>
        public float Effective(StatKind kind)
        {
            float flat = 0f, percent = 0f, final = 0f;

            for (int i = 0; i < _modifiers.Count; i++)
            {
                var modifier = _modifiers[i];
                if (modifier.Kind != kind) continue;

                switch (modifier.Stage)
                {
                    case StatStage.Flat:    flat += modifier.Value;    break;
                    case StatStage.Percent: percent += modifier.Value; break;
                    case StatStage.Final:   final += modifier.Value;   break;
                }
            }

            float value = (_levels[(int)kind] + flat) * (1f + percent) + final;

            // A stat can be debuffed but never inverted: negative Might healing the target is the
            // sort of thing a stacked debuff produces and nobody ever intends.
            return Mathf.Max(0f, value);
        }

        // ---------------------------------------------------------------- derived numbers

        /// <summary>Maximum health, from Heart.</summary>
        public int MaxHealth => _tuning.MaxHealthFor(Effective(StatKind.Heart));

        /// <summary>Maximum Flame, from Flame. The meter the flame-arts draw from (§6.4).</summary>
        public int MaxFlame => _tuning.MaxFlameFor(Effective(StatKind.Flame));

        /// <summary>Outgoing damage multiplier, from Might.</summary>
        public float DamageScale => _tuning.DamageScaleFor(Effective(StatKind.Might));

        /// <summary>Scale an authored damage number by Might, never below one point.</summary>
        public int ScaleDamage(int authored)
        {
            if (authored <= 0) return authored;

            // Rounded, then floored at one: a heavy debuff should make a hit feeble, not free.
            return Mathf.Max(1, Mathf.RoundToInt(authored * DamageScale));
        }

        /// <summary>Reset to a fresh hero. Used by respawn and by tests.</summary>
        public void Reset()
        {
            for (int i = 0; i < _levels.Length; i++) _levels[i] = MinLevel;

            _modifiers.Clear();
            Resolve = 0;
            UnspentLevels = 0;
        }
    }
}
