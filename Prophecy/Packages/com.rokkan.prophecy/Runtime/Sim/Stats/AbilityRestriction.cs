using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Stats
{
    /// <summary>
    /// Something the character temporarily may not do. A silence, a disarm, a root.
    ///
    /// <para><b>Not a stat, and deliberately not modelled as one.</b> A restriction has no value to
    /// scale and no "strongest" to compare — it is on or off. Squeezing it into
    /// <see cref="StatModifier"/> would have meant carrying a meaningless value and stage on every
    /// one, and a <c>Percent</c> silence is nonsense. It shares the <i>lifecycle</i> though —
    /// duration in ticks, refresh on reapply, removal by source — so it shares those rules and
    /// nothing else.</para>
    /// </summary>
    [Serializable]
    public struct AbilityRestriction
    {
        /// <summary>
        /// Broad actions barred. Root is <c>Move | Jump</c>; disarm is <c>Attack</c>.
        ///
        /// <para>The same flags the action lock uses, so a restriction and a committed attack speak
        /// one vocabulary rather than two that must be kept in step.</para>
        /// </summary>
        public LockFlags Flags;

        /// <summary>
        /// One specific ability barred, for things the flags cannot name. Silence is
        /// <c>FlameArt</c>; a curse that forbids the down-thrust is <c>DownThrust</c>.
        ///
        /// <para><see cref="AbilityId.Custom"/> — the zero value — means "no specific ability",
        /// which is why it must never be a shipped module's id.</para>
        /// </summary>
        public AbilityId Ability;

        /// <summary>The tick this lifts on. <see cref="StatModifier.Permanent"/> for a curse.</summary>
        public long ExpiresOnTick;

        /// <summary>What applied it, so it can be lifted with everything else from that source.</summary>
        public int SourceId;

        /// <summary>This exact instance, for lifting one restriction without its siblings.</summary>
        public int Id;

        /// <summary>
        /// Identifies the effect for stacking. Reapplying refreshes the duration; there is no
        /// second stack of a silence, because being silenced twice is being silenced.
        /// </summary>
        public int StackKey;

        public bool IsActiveOn(long tick) => tick < ExpiresOnTick;

        public bool Bars(AbilityId ability) =>
            Ability != AbilityId.Custom && Ability == ability;

        public static AbilityRestriction Lock(LockFlags flags, long expiresOnTick,
                                              int sourceId = 0, int id = 0, int stackKey = 0) =>
            new AbilityRestriction
            {
                Flags = flags, Ability = AbilityId.Custom, ExpiresOnTick = expiresOnTick,
                SourceId = sourceId, Id = id, StackKey = stackKey,
            };

        public static AbilityRestriction Silence(AbilityId ability, long expiresOnTick,
                                                 int sourceId = 0, int id = 0, int stackKey = 0) =>
            new AbilityRestriction
            {
                Flags = LockFlags.None, Ability = ability, ExpiresOnTick = expiresOnTick,
                SourceId = sourceId, Id = id, StackKey = stackKey,
            };

        public override string ToString() =>
            $"bar {(Ability == AbilityId.Custom ? Flags.ToString() : Ability.ToString())}" +
            (ExpiresOnTick == StatModifier.Permanent ? " permanently" : $" until {ExpiresOnTick}");
    }

    /// <summary>
    /// Everything the character currently may not do.
    ///
    /// <para>Queried on the hot path — every module asks before it acts — so the barred set is
    /// folded once per change rather than walked per question.</para>
    /// </summary>
    public sealed class RestrictionSet
    {
        private readonly List<AbilityRestriction> _restrictions = new List<AbilityRestriction>();
        private readonly List<AbilityRestriction> _keep = new List<AbilityRestriction>();

        private LockFlags _barredFlags;

        public IReadOnlyList<AbilityRestriction> All => _restrictions;

        /// <summary>Every action currently barred, folded. What <c>CharacterSim.Can</c> consults.</summary>
        public LockFlags BarredFlags => _barredFlags;

        public bool Any => _restrictions.Count > 0;

        /// <summary>
        /// Apply a restriction. Reapplying one with the same stack key refreshes its duration
        /// rather than adding a second — being silenced twice is being silenced.
        /// </summary>
        public void Apply(in AbilityRestriction restriction)
        {
            if (restriction.StackKey != 0)
            {
                for (int i = 0; i < _restrictions.Count; i++)
                {
                    if (_restrictions[i].StackKey != restriction.StackKey) continue;

                    var existing = _restrictions[i];

                    // Never shorten. A brief reapplication of a long silence must not cure it.
                    if (restriction.ExpiresOnTick > existing.ExpiresOnTick)
                        existing.ExpiresOnTick = restriction.ExpiresOnTick;

                    existing.Flags |= restriction.Flags;
                    _restrictions[i] = existing;

                    Refold();
                    return;
                }
            }

            _restrictions.Add(restriction);
            Refold();
        }

        /// <summary>True if this ability may not act — either named directly or covered by flags.</summary>
        public bool IsBarred(AbilityId ability)
        {
            for (int i = 0; i < _restrictions.Count; i++)
                if (_restrictions[i].Bars(ability)) return true;

            return false;
        }

        /// <summary>True if any barred flag overlaps <paramref name="action"/>.</summary>
        public bool Blocks(LockFlags action) => (_barredFlags & action) != 0;

        public bool RemoveId(int id)
        {
            if (id == 0) return false;

            for (int i = 0; i < _restrictions.Count; i++)
            {
                if (_restrictions[i].Id != id) continue;

                _restrictions.RemoveAt(i);
                Refold();
                return true;
            }

            return false;
        }

        public void RemoveSource(int sourceId)
        {
            _keep.Clear();

            for (int i = 0; i < _restrictions.Count; i++)
                if (_restrictions[i].SourceId != sourceId) _keep.Add(_restrictions[i]);

            if (_keep.Count == _restrictions.Count) return;

            _restrictions.Clear();
            _restrictions.AddRange(_keep);
            Refold();
        }

        /// <summary>Drop what has lapsed. Called once a tick, beside the stat prune.</summary>
        public void PruneExpired(long tick, List<AbilityRestriction> expired = null)
        {
            bool any = false;
            for (int i = 0; i < _restrictions.Count && !any; i++)
                any = !_restrictions[i].IsActiveOn(tick);

            if (!any) return;

            _keep.Clear();
            for (int i = 0; i < _restrictions.Count; i++)
            {
                if (_restrictions[i].IsActiveOn(tick)) _keep.Add(_restrictions[i]);
                else expired?.Add(_restrictions[i]);
            }

            _restrictions.Clear();
            _restrictions.AddRange(_keep);
            Refold();
        }

        public void Clear()
        {
            _restrictions.Clear();
            _barredFlags = LockFlags.None;
        }

        private void Refold()
        {
            _barredFlags = LockFlags.None;
            for (int i = 0; i < _restrictions.Count; i++) _barredFlags |= _restrictions[i].Flags;
        }
    }
}
