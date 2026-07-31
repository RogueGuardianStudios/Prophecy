using System.Collections.Generic;
using UnityEngine;
using Rokkan.Prophecy.Sim.Collision;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>What one sweep of one volume turned into.</summary>
    public readonly struct SweepResult
    {
        /// <summary>
        /// Targets that were <i>actually touched</i> — landed or blocked. A hit that passed
        /// through invulnerability, or found nobody, is not a connection.
        ///
        /// <para>The distinction earns its place with the down-thrust: bouncing off something you
        /// struck is right, bouncing off a target you phased through is a free extra jump.</para>
        /// </summary>
        public readonly int Connected;

        /// <summary>Ticks the attacker owes for having been parried. Zero normally.</summary>
        public readonly int AttackerStunTicks;

        public readonly HitOutcome LastOutcome;

        public SweepResult(int connected, int attackerStunTicks, HitOutcome lastOutcome)
        {
            Connected = connected;
            AttackerStunTicks = attackerStunTicks;
            LastOutcome = lastOutcome;
        }

        public bool Punished => AttackerStunTicks > 0;
    }

    /// <summary>
    /// Swings one volume at the world and remembers who it already got.
    ///
    /// <para><b>Extracted because it was about to exist three times.</b> The attack module, the
    /// training attacker and the down-thrust all need the same five steps — resolve the box, skip
    /// anyone this action already hit, report the hit, count what connected, notice being parried —
    /// and three copies of a dedup rule is three chances for them to disagree about what one hit
    /// means.</para>
    ///
    /// <para><b>Dedup is per box, per target, per action.</b> Keyed on the box index rather than
    /// the whole attack, because a multi-hit move's second sweep is a genuinely separate hit and
    /// should connect again. <see cref="Begin"/> starts a new action — a fresh swing, the next
    /// chain link, another dive.</para>
    ///
    /// <para>Plain C# holding no state about <i>who</i> is swinging, so the same sweep serves a
    /// simulated character and a dummy on a timer.</para>
    /// </summary>
    public sealed class HitSweep
    {
        private readonly List<int> _hits = new List<int>();
        private readonly List<int> _candidates = new List<int>();
        private readonly HashSet<long> _spent = new HashSet<long>();

        /// <summary>Start a new action. Everyone becomes hittable again.</summary>
        public void Begin() => _spent.Clear();

        /// <summary>
        /// Resolve <paramref name="box"/> against the world and report every target it newly
        /// touched.
        /// </summary>
        /// <param name="level">Geometry for cover queries. Null skips them, which is correct for
        /// an attack that no wall should stop.</param>
        /// <param name="maxTargets">Stop after this many connect. Zero is unlimited, which is what
        /// a sword swing and an area attack both want; a piercing shot with a limit of one has to
        /// stop <i>inside</i> the sweep, because the people it would have hit are all standing in
        /// the same volume on the same tick.</param>
        /// <summary>
        /// Authored damage after the attacker's Might. Floored at one so a heavy debuff makes a
        /// hit feeble rather than free, and rounded rather than truncated so a 1.5x scale on a
        /// 3-damage jab is 5 and not 4.
        /// </summary>
        private static int ScaleDamage(int authored, float scale)
        {
            if (authored <= 0) return authored;
            return Mathf.Max(1, Mathf.RoundToInt(authored * scale));
        }

        public SweepResult Sweep(int boxIndex, in AttackHitBox box, in Attacker attacker,
                                 ICombatWorld world, CollisionWorld level,
                                 long tick, string attackId, int maxTargets = 0)
        {
            if (world == null) return default;

            var targets = world.Hurtboxes;
            if (targets == null || targets.Count == 0) return default;

            if (HitResolver.Resolve(box, attacker, targets, level, _hits, _candidates) == 0)
                return default;

            int connected = 0;
            var last = HitOutcome.Ignored;

            for (int i = 0; i < _hits.Count; i++)
            {
                var target = targets[_hits[i]];

                if (!_spent.Add(SpendKey(boxIndex, target.OwnerId))) continue;

                var answer = world.OnHit(new HitEvent(
                    attacker.Id, target.OwnerId, ScaleDamage(box.Damage, attacker.DamageScale),
                    attacker.Facing,
                    tick, attackId, boxIndex, box.Height, box.Defeats));

                last = answer.Outcome;
                if (!answer.Negated) connected++;

                if (maxTargets > 0 && connected >= maxTargets)
                    return new SweepResult(connected, answer.AttackerStunTicks, answer.Outcome);

                // Parried. The sweep stops here: a swing that has been turned should not keep
                // connecting with whoever was standing behind the person who turned it.
                if (answer.AttackerStunTicks > 0)
                    return new SweepResult(connected, answer.AttackerStunTicks, answer.Outcome);
            }

            return new SweepResult(connected, 0, last);
        }

        /// <summary>Box index and target id packed into one key. Explicit packing rather than a
        /// hash, so two different pairs can never collide and silently eat a hit.</summary>
        private static long SpendKey(int boxIndex, int ownerId) =>
            ((long)boxIndex << 32) | (uint)ownerId;
    }
}
