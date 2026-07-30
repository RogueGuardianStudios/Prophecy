using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Holding the guard up. A held stance, not an action — and the gate that answers a hit while
    /// it is up.
    ///
    /// <para><b>Stance chooses the guard, the same way it chooses the attack.</b> Standing answers
    /// high, crouching answers low, and the block lock freezes stance for as long as it is held —
    /// so committing to a guard height is the decision, and switching costs the time it takes to
    /// drop the shield. That is Zelda II's shield, and it is why blocking is interesting at all: a
    /// guard that answers everything is just a button that makes you invincible.</para>
    ///
    /// <para><b>It plants your feet.</b> The lock takes Move and Attack, so a block cannot be held
    /// while repositioning or swinging. Being unable to reposition is the cost that makes blocking
    /// a read rather than a default — and the cancel window is left permanently open, because a
    /// block is a stance you can always drop for something better, not a committed action.</para>
    ///
    /// <para><b>Directional.</b> Only hits arriving at the front are answered. A block cannot cover
    /// a back you turned yourself.</para>
    /// </summary>
    public sealed class Block : AbilityModule, IDamageGate
    {
        /// <summary>
        /// Move and Attack, not Defend: the guard suppresses walking and swinging, but leaves the
        /// defensive slot open so a parry can be raised straight out of it.
        /// </summary>
        private const LockFlags GuardLock = LockFlags.Move | LockFlags.Attack;

        private readonly CombatTuningData _combat;

        public Block(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.Block;
        public override int Order => ModuleOrder.Block;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public int GateOrder => Combat.GateOrder.Block;

        /// <summary>True while the guard is up. Read by presentation and the overlay.</summary>
        public bool IsGuarding { get; private set; }

        /// <summary>The height this guard currently answers. Meaningless when not guarding.</summary>
        public AttackHeight Guarding { get; private set; } = AttackHeight.High;

        /// <summary>Tick of the most recent hit this guard turned away. Debug and presentation.</summary>
        public long LastBlockedTick { get; private set; } = long.MinValue;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_combat == null) return;

            var state = sim.State;

            // Lost the lock to something with a stronger claim — a parry, a hit-react. The guard is
            // down and the gate stops answering on the same tick, not the next one.
            if (IsGuarding && !sim.HoldsLock(this))
            {
                IsGuarding = false;
                return;
            }

            bool wants = input.Block.Held && state.Grounded && state.Space == MovementSpace.SideScroll;

            if (!wants)
            {
                if (IsGuarding)
                {
                    sim.ReleaseLock(this);
                    IsGuarding = false;
                }
                return;
            }

            if (!IsGuarding)
            {
                if (!sim.Can(LockFlags.Defend)) return;
                if (!sim.TryLock(this, GuardLock, LockPriority.Defend)) return;

                IsGuarding = true;
            }

            // Re-asserted every tick: the height is read from the stance the guard froze, and the
            // cancel window stays open so a parry or a hit-react is never fighting the shield.
            Guarding = state.Stance == Stance.Crouch ? AttackHeight.Low : AttackHeight.High;
            sim.SetCancelWindow(this, true);
        }

        public override void Reset()
        {
            IsGuarding = false;
            Guarding = AttackHeight.High;
        }

        // ---------------------------------------------------------------- the gate

        public HitResult Evaluate(CharacterSim sim, in HitEvent hit)
        {
            // Asked rather than assumed: a hit-react may have force-locked this character between
            // the guard's last tick and this hit, and a shield held by someone who has just been
            // knocked off their feet is not a shield.
            if (!IsGuarding || !sim.HoldsLock(this)) return HitResult.Continue;

            if (hit.Unblockable) return HitResult.Continue;
            if (!FacesTheHit(sim.State.Facing, hit.Facing)) return HitResult.Continue;
            if (!Answers(hit.Height)) return HitResult.Continue;

            LastBlockedTick = hit.Tick;

            // Pushback, not a stun.
            //
            // Parking a stun would hand the character to the hit-react module, which force-locks —
            // and taking the lock takes the guard down. That is guard-break, arrived at by
            // accident, on every blocked hit. A block is also already a lock that suppresses moving
            // and attacking, so "blockstun" here would be a duration during which nothing further
            // is prevented. So the pressure a blocked hit applies is that it moves you, and the
            // cost is chip.
            sim.State.Velocity.x = hit.Facing * _combat.BlockPushbackSpeed;

            return new HitResult(HitOutcome.Blocked, Chip(hit.Damage));
        }

        /// <summary>
        /// <paramref name="hitFacing"/> is the direction the blow travels, so the defender is
        /// facing it when they face the opposite way. Two signs that are easy to get backwards and
        /// produce a guard that only works with your back turned.
        /// </summary>
        private static bool FacesTheHit(int defenderFacing, int hitFacing) =>
            defenderFacing != hitFacing;

        private bool Answers(AttackHeight height) =>
            height == AttackHeight.Any || height == Guarding;

        /// <summary>
        /// Chip damage, rounded half-up and never rounded away entirely. A blocked hit that costs
        /// literally nothing makes holding the guard the correct play against every blockable
        /// attack in the game — the floor of one is what keeps a turtle on a timer.
        /// </summary>
        private int Chip(int damage)
        {
            if (damage <= 0 || _combat.BlockedDamageFraction <= 0f) return 0;

            int chip = (int)(damage * _combat.BlockedDamageFraction + 0.5f);
            return Mathf.Max(1, chip);
        }
    }
}
