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
    public sealed class StatBlock : IStatSource
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

        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        // The earning — Resolve, banked levels, the power gate — lives in Progression, which
        // wraps this block for the one character that levels. This class is every combatant's:
        // levels, the modifier algebra, and the numbers that fall out.

        // ---------------------------------------------------------------- levels

        public int LevelOf(StatKind kind) => _levels[(int)kind];

        public void SetLevel(StatKind kind, int level) =>
            _levels[(int)kind] = Mathf.Clamp(level, MinLevel, MaxLevel);

        // ---------------------------------------------------------------- modifiers

        /// <summary>
        /// Add a modifier with no stacking rules at all. Gear, level-ups, anything whose second
        /// copy is genuinely a second copy.
        /// </summary>
        public void Add(in StatModifier modifier) => _modifiers.Add(modifier);

        /// <summary>Drop everything that declared itself room-lived. Called by the sim when the
        /// body passes through a door — the room-scoped half of the section-change rule.</summary>
        public void ClearRoomScoped()
        {
            for (int i = _modifiers.Count - 1; i >= 0; i--)
                if (_modifiers[i].RoomScoped) _modifiers.RemoveAt(i);
        }

        /// <summary>
        /// Apply a modifier, honouring its stacking rules. The entry point for anything reappliable
        /// — debuffs, DOTs, auras.
        ///
        /// <para><b>Three rules, in this order</b>, for a modifier that names a
        /// <see cref="StatModifier.StackKey"/>:</para>
        ///
        /// <list type="number">
        /// <item><b>Refresh.</b> Every instance already in the group takes the incoming expiry.
        /// The whole stack runs out together, which is what makes a stack readable — one timer on
        /// screen rather than three drifting apart.</item>
        /// <item><b>Strongest wins.</b> If the incoming is stronger by magnitude, the group is
        /// upgraded to it. A weak reapplication can therefore refresh a strong debuff but never
        /// dilute it, which is the behaviour that stops a trash mob's feeble poison cancelling a
        /// boss's.</item>
        /// <item><b>Cap.</b> A further instance is added only while the group is below
        /// <see cref="StatModifier.StackLimit"/> — one, unless the effect says otherwise.</item>
        /// </list>
        ///
        /// <para>A modifier with no stack key skips all three and is simply added, so two rings of
        /// +1 Might still give +2.</para>
        /// </summary>
        public void Apply(in StatModifier modifier)
        {
            if (modifier.StackKey == 0)
            {
                _modifiers.Add(modifier);
                return;
            }

            int inGroup = 0;
            float strongest = Mathf.Abs(modifier.Value);

            // Strongest is decided across the whole group first, so the upgrade pass cannot depend
            // on the order the list happens to be in.
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (!modifier.SameEffectAs(_modifiers[i])) continue;

                inGroup++;
                strongest = Mathf.Max(strongest, Mathf.Abs(_modifiers[i].Value));
            }

            if (inGroup == 0)
            {
                _modifiers.Add(modifier);
                return;
            }

            // Keep the incoming sign: a group is one effect, so a stronger value of the opposite
            // sign would be a different effect wearing the same key.
            float sign = modifier.Value < 0f ? -1f : 1f;
            float value = strongest * sign;

            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (!modifier.SameEffectAs(_modifiers[i])) continue;

                var existing = _modifiers[i];
                existing.ExpiresOnTick = modifier.ExpiresOnTick;   // 1. refresh
                existing.Value = value;                            // 2. strongest
                _modifiers[i] = existing;
            }

            // 3. cap
            if (inGroup < modifier.StackLimit)
            {
                var added = modifier;
                added.Value = value;
                _modifiers.Add(added);
            }
        }

        /// <summary>How many instances of an effect are currently stacked.</summary>
        public int StackCountOf(in StatModifier like)
        {
            if (like.StackKey == 0) return 0;

            int count = 0;
            for (int i = 0; i < _modifiers.Count; i++)
                if (like.SameEffectAs(_modifiers[i])) count++;

            return count;
        }

        /// <summary>Add several at once — equipping, or a Protector's whole boon.</summary>
        public void AddRange(IReadOnlyList<StatModifier> modifiers)
        {
            if (modifiers == null) return;
            for (int i = 0; i < modifiers.Count; i++) _modifiers.Add(modifiers[i]);
        }

        /// <summary>
        /// Author a set of modifiers onto this block, starting now.
        ///
        /// <para>The equip path: gear carries <see cref="StatModifierSpec"/> with durations, and
        /// this is where a duration becomes an expiry tick.</para>
        /// </summary>
        public void AddSpecs(IReadOnlyList<StatModifierSpec> specs, long currentTick, int sourceId)
        {
            if (specs == null) return;
            for (int i = 0; i < specs.Count; i++) Apply(specs[i].Resolve(currentTick, sourceId));
        }

        /// <summary>
        /// Remove one modifier by its id, leaving its siblings alone. Ids of zero are
        /// unidentified and never match, so this cannot remove something at random.
        /// </summary>
        /// <returns>True if anything was removed.</returns>
        public bool RemoveId(int id)
        {
            if (id == 0) return false;

            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (_modifiers[i].Id != id) continue;

                _modifiers.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>Remove several sources at once — unequipping a whole loadout.</summary>
        public void RemoveSources(IReadOnlyList<int> sourceIds)
        {
            if (sourceIds == null || sourceIds.Count == 0) return;

            _keep.Clear();

            for (int i = 0; i < _modifiers.Count; i++)
            {
                bool drop = false;
                for (int s = 0; s < sourceIds.Count && !drop; s++)
                    drop = _modifiers[i].SourceId == sourceIds[s];

                if (!drop) _keep.Add(_modifiers[i]);
            }

            _modifiers.Clear();
            _modifiers.AddRange(_keep);
        }

        /// <summary>
        /// End a modifier early, as if it had expired.
        ///
        /// <para>HopeFell's <c>MarkForRemoval</c>. Dispelling a buff, or a channelled effect the
        /// caster stopped paying for — cases where the duration was never the thing that ended
        /// it.</para>
        /// </summary>
        public bool Cancel(int id) => RemoveId(id);

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
        /// <param name="expired">Optional. Filled with whatever lapsed, so a caller can play the
        /// "buff ended" sting without polling. HopeFell raises an <c>OnDispose</c> event for this;
        /// a caller-supplied list does the same job without allocating or letting a subscriber run
        /// inside the sim's tick.</param>
        public void PruneExpired(long tick, List<StatModifier> expired = null)
        {
            bool any = false;
            for (int i = 0; i < _modifiers.Count && !any; i++)
                any = !_modifiers[i].IsActiveOn(tick);

            if (!any) return;

            _keep.Clear();
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (_modifiers[i].IsActiveOn(tick)) _keep.Add(_modifiers[i]);
                else expired?.Add(_modifiers[i]);
            }

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
        public int ScaleDamage(int authored) => Combat.DamageMath.Scale(authored, DamageScale);

        /// <summary>Reset to a fresh hero's levels and a clean modifier set. The economy's own
        /// bank is <see cref="Progression"/>'s to clear — a block reset must not quietly rob it.</summary>
        public void Reset()
        {
            for (int i = 0; i < _levels.Length; i++) _levels[i] = MinLevel;

            _modifiers.Clear();
        }
    }
}
