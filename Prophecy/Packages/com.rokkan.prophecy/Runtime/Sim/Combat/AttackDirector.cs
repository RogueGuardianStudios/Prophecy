using System;
using System.Collections.Generic;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>Which budget an attack draws on. A bolt from twelve metres and a swing at arm's
    /// length are different kinds of pressure and must never compete for the same permission.</summary>
    public enum AttackPool : byte
    {
        Melee,
        Ranged,
    }

    /// <summary>Implemented by a combat world that wants to know when attacks ACTUALLY run —
    /// the closed loop that makes pacing honest. A side interface rather than a widening of
    /// <see cref="ICombatWorld"/>, so test fakes that only care about hits stay untouched.</summary>
    public interface IAttackObserver
    {
        void OnAttackStarted(int combatId, long tick, int totalTicks);
        void OnAttackEnded(int combatId, long tick);
    }

    /// <summary>The attack director's dials. A plain serializable class so the presentation
    /// director can expose it in the inspector as a LIVE tuning surface — none of these numbers
    /// have been felt yet, and every one of them is expected to move.</summary>
    [Serializable]
    public sealed class AttackDirectorTuning
    {
        /// <summary>Concurrent melee attacks allowed fight-wide. One: the duel.</summary>
        public int MeleeBudget = 1;

        public int RangedBudget = 1;

        /// <summary>Minimum ticks between ANY two enemy attack starts, across pools — the
        /// stagger that keeps a swing and a bolt from arriving as one unreadable moment.</summary>
        public int GlobalMinGapTicks = 10;

        /// <summary>The beat's floor is the attack's OWN length plus this many ticks of true
        /// neutral. Derived per swing from the definition actually thrown, which makes the
        /// authored-beat-shorter-than-the-move class of drift structurally impossible.</summary>
        public int MinNeutralGapTicks = 20;

        /// <summary>A granted token lapses after this many ticks without a fresh request —
        /// the holder died, was stunned blind, or lost its target before pressing.</summary>
        public int RequestLapseTicks = 15;

        /// <summary>An in-flight token is force-released at start + total × this — the net
        /// under teleports and resets that skip the module's own end notification.</summary>
        public int InFlightSafetyFactor = 2;
    }

    /// <summary>A pacing record's outward face, for the overlay and the tests.</summary>
    public readonly struct AttackTokenView
    {
        public readonly AttackPool Pool;
        public readonly bool HoldsToken;
        public readonly bool InFlight;
        public readonly long TicksUntilEligible;

        public AttackTokenView(AttackPool pool, bool holdsToken, bool inFlight, long ticksUntilEligible)
        {
            Pool = pool;
            HoldsToken = holdsToken;
            InFlight = inFlight;
            TicksUntilEligible = ticksUntilEligible;
        }
    }

    /// <summary>
    /// The fight's attack arbiter — WHO may swing, and WHEN. Enemies place a standing request
    /// every tick they want to attack; this grants tokens from small budgets (one melee, one
    /// ranged: the duel), and the enemy's press is stripped unless it holds one. The token is
    /// consumed when an attack ACTUALLY starts — <see cref="AttackModule"/> reports real starts
    /// and ends through <see cref="IAttackObserver"/> — so a press stripped by a lock or a
    /// stun never spends a beat, and an interrupted swing returns its budget the same tick.
    ///
    /// <para><b>The beat is drift-proof by construction.</b> The next allowed start is
    /// <c>max(authored beat, the thrown attack's total ticks + neutral gap)</c>, taken from
    /// the definition at the moment of the swing. The rapid-fire this system exists to end was
    /// caused by an authored recovery number reasoned against a move that later grew — a
    /// derived floor cannot be outgrown. A parried attack STILL stamps its beat (Matt): the
    /// parry buys the stun AND the full beat, never a faster comeback.</para>
    ///
    /// <para><b>Grants are deterministic and space-agnostic.</b> Longest-idle first (by last
    /// attack start; the never-attacked go before everyone), ties to the lowest id. No
    /// positional slots — a spatial policy is per-space and pluggable later, per the design
    /// brief. Plain C# with no engine types: this is authoritative fight state, it lives on
    /// the sim side of the line, and the tests run it headless.</para>
    /// </summary>
    public sealed class AttackDirector
    {
        private struct Record
        {
            public AttackPool Pool;
            public int BeatTicks;
            public long LastRequestTick;
            public long LastAttackStartTick;
            public long NextAllowedTick;
            public bool HoldsToken;
            public bool InFlight;
            public long InFlightDeadline;
        }

        /// <summary>How recently a request must have been renewed to count as standing.
        /// THREE ticks, not one: the director ticks on the clock's tick while an enemy
        /// stamps its request with its sim's pre-advance tick, so a perfectly healthy
        /// standing request legitimately trails the grant pass by two. One tick of margin
        /// on top; true silence is the lapse mechanism's job, not this one's.</summary>
        private const int RequestFreshTicks = 3;

        /// <summary>Live-editable dials; the presentation director exposes this instance.</summary>
        public AttackDirectorTuning Tuning = new AttackDirectorTuning();

        private readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();
        private readonly List<int> _ids = new List<int>();
        private long _lastAnyAttackStartTick = long.MinValue;

        /// <summary>
        /// A standing request: call once per tick while this enemy wants to attack at all.
        /// First call creates the record — request implies registration. The authored beat
        /// rides along so retuning it reaches the director without a re-wire.
        /// </summary>
        public void Request(int combatId, AttackPool pool, int beatTicks, long tick)
        {
            if (!_records.TryGetValue(combatId, out var record))
            {
                record = new Record
                {
                    LastAttackStartTick = long.MinValue,
                    NextAllowedTick = long.MinValue,
                };
                _ids.Add(combatId);
            }

            record.Pool = pool;
            record.BeatTicks = beatTicks;
            record.LastRequestTick = tick;
            _records[combatId] = record;
        }

        public bool HoldsToken(int combatId) =>
            _records.TryGetValue(combatId, out var record) && record.HoldsToken;

        /// <summary>An attack genuinely began: consume the token, stamp the beat.</summary>
        public void OnAttackStarted(int combatId, long tick, int totalTicks)
        {
            if (!_records.TryGetValue(combatId, out var record)) return;   // the player, passing through

            long beat = Math.Max(record.BeatTicks, totalTicks + Tuning.MinNeutralGapTicks);
            record.LastAttackStartTick = tick;
            record.NextAllowedTick = tick + beat;
            record.HoldsToken = false;
            record.InFlight = true;
            record.InFlightDeadline = tick + Math.Max(1, totalTicks) * Math.Max(1, Tuning.InFlightSafetyFactor);
            _records[combatId] = record;

            _lastAnyAttackStartTick = tick;
        }

        /// <summary>The attack finished or was interrupted: the budget frees the same tick.
        /// The beat stamped at the start still holds the attacker out — an interruption is
        /// never a faster comeback.</summary>
        public void OnAttackEnded(int combatId, long tick)
        {
            if (!_records.TryGetValue(combatId, out var record)) return;

            record.InFlight = false;
            _records[combatId] = record;
        }

        /// <summary>Lapse the stale, net the stuck, grant the free budget. Runs FIRST in the
        /// fight's tick so grants land before any enemy consumes its input.</summary>
        public void Tick(long tick)
        {
            for (int i = 0; i < _ids.Count; i++)
            {
                int id = _ids[i];
                var record = _records[id];

                // A holder that stopped asking is a holder that died, got stunned blind, or
                // lost its target — the token goes back rather than idling in a pocket.
                if (record.HoldsToken && tick - record.LastRequestTick > Tuning.RequestLapseTicks)
                {
                    record.HoldsToken = false;
                    _records[id] = record;
                }

                // The net under paths that skip the module's end notification entirely.
                if (record.InFlight && tick > record.InFlightDeadline)
                {
                    record.InFlight = false;
                    _records[id] = record;
                }
            }

            Grant(AttackPool.Melee, Tuning.MeleeBudget, tick);
            Grant(AttackPool.Ranged, Tuning.RangedBudget, tick);
        }

        private void Grant(AttackPool pool, int budget, long tick)
        {
            int used = 0;
            for (int i = 0; i < _ids.Count; i++)
            {
                var record = _records[_ids[i]];
                if (record.Pool == pool && (record.HoldsToken || record.InFlight)) used++;
            }

            while (used < budget)
            {
                int chosen = -1;
                long chosenIdle = long.MaxValue;

                for (int i = 0; i < _ids.Count; i++)
                {
                    int id = _ids[i];
                    var record = _records[id];

                    if (record.Pool != pool || record.HoldsToken || record.InFlight) continue;
                    if (tick - record.LastRequestTick > RequestFreshTicks) continue;   // asking NOW
                    if (tick < record.NextAllowedTick) continue;                       // its own beat
                    if (_lastAnyAttackStartTick != long.MinValue &&
                        tick - _lastAnyAttackStartTick < Tuning.GlobalMinGapTicks) continue;

                    // Longest idle first; the never-attacked (MinValue) beat everyone; ties
                    // break to the lowest id so the outcome is identical on every machine.
                    if (record.LastAttackStartTick < chosenIdle ||
                        (record.LastAttackStartTick == chosenIdle && id < chosen))
                    {
                        chosen = id;
                        chosenIdle = record.LastAttackStartTick;
                    }
                }

                if (chosen < 0) return;

                var granted = _records[chosen];
                granted.HoldsToken = true;
                _records[chosen] = granted;
                used++;
            }
        }

        public void Forget(int combatId)
        {
            if (_records.Remove(combatId)) _ids.Remove(combatId);
        }

        public void Clear()
        {
            _records.Clear();
            _ids.Clear();
            _lastAnyAttackStartTick = long.MinValue;
        }

        /// <summary>The record's outward face, for the overlay and the tests. False = this id
        /// has never requested.</summary>
        public bool TryDescribe(int combatId, long tick, out AttackTokenView view)
        {
            if (!_records.TryGetValue(combatId, out var record))
            {
                view = default;
                return false;
            }

            view = new AttackTokenView(
                record.Pool, record.HoldsToken, record.InFlight,
                Math.Max(0, record.NextAllowedTick == long.MinValue ? 0 : record.NextAllowedTick - tick));
            return true;
        }
    }
}
