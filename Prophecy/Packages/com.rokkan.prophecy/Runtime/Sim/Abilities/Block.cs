using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The guard. One button, and <b>when</b> you pressed it decides what it was.
    ///
    /// <para><b>A parry is a guard raised on time, not a separate move.</b> The first few ticks
    /// after the press are a parry window; a hit arriving inside it is turned and punishes the
    /// attacker, and a hit arriving after it is an ordinary block. Holding the button therefore
    /// spends its parry immediately and settles into a guard — which is the whole shape of the
    /// mechanic: you cannot hold a parry, only time one.</para>
    ///
    /// <para><b>Stance chooses the guard, the same way it chooses the attack.</b> Standing answers
    /// high, crouching answers low, and the guard lock freezes stance for as long as it is held, so
    /// committing to a height is the decision. A parry ignores height — timing beat the attack, and
    /// refusing a well-timed deflection for being aimed at the wrong part of a shield would make
    /// the window a worse version of the block rather than a better one.</para>
    ///
    /// <para><b>It plants your feet.</b> The lock takes Move and Attack, so a guard cannot be held
    /// while repositioning or swinging. The cancel window is left permanently open, because a guard
    /// is a stance you can always drop for something better, not a committed action.</para>
    ///
    /// <para><b>Directional.</b> Only hits arriving at the front are answered, parry or block. A
    /// shield cannot cover a back you turned yourself.</para>
    /// </summary>
    public sealed class Block : AbilityModule, IDamageGate
    {
        /// <summary>
        /// Move and Attack, not Defend: the guard suppresses walking and swinging, but leaves the
        /// defensive slot open so a dodge can be taken straight out of it.
        /// </summary>
        private const LockFlags GuardLock = LockFlags.Move | LockFlags.Attack;

        private readonly CombatTuningData _combat;

        private long _guardStartTick = long.MinValue;
        private TickRange _parryWindow = TickRange.None;

        public Block(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.Block;
        public override int Order => ModuleOrder.Block;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>
        /// At the parry's precedence, not the block's — one gate now answers both, and a well-timed
        /// guard must not be downgraded into the block it would otherwise have been.
        /// </summary>
        public int GateOrder => Combat.GateOrder.Parry;

        /// <summary>Stat scaling for the parry window. Resolved when the guard goes up and held for
        /// that guard, so a buff expiring mid-press cannot shorten a window already committed to.</summary>
        public AttackModifiers Modifiers = AttackModifiers.None;

        /// <summary>True while the guard is up.</summary>
        public bool IsGuarding { get; private set; }

        /// <summary>The height this guard answers. Meaningless when not guarding.</summary>
        public AttackHeight Guarding { get; private set; } = AttackHeight.High;

        /// <summary>Tick of the most recent hit this guard turned away.</summary>
        public long LastBlockedTick { get; private set; } = long.MinValue;

        /// <summary>Tick of the most recent successful parry.</summary>
        public long LastParryTick { get; private set; } = long.MinValue;

        /// <summary>The resolved parry window, in ticks from the press. Debug overlay.</summary>
        public TickRange ParryWindow => _parryWindow;

        /// <summary>Ticks the guard has been up. -1 when down.</summary>
        public int GuardTicks(long currentTick) =>
            IsGuarding && _guardStartTick != long.MinValue ? (int)(currentTick - _guardStartTick) : -1;

        /// <summary>True on the ticks a hit would be parried rather than blocked.</summary>
        public bool ParryWindowOpen(long currentTick) =>
            IsGuarding && _parryWindow.Contains(GuardTicks(currentTick));

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_combat == null) return;

            var state = sim.State;

            // Lost the lock to something with a stronger claim — a dodge, a hit-react. The guard is
            // down and the gate stops answering on the same tick, not the next one.
            if (IsGuarding && !sim.HoldsLock(this))
            {
                Lower();
                return;
            }

            bool wants = input.Block.Held && state.Grounded && state.Space == MovementSpace.SideScroll;

            if (!wants)
            {
                if (IsGuarding)
                {
                    sim.ReleaseLock(this);
                    Lower();
                }
                return;
            }

            if (!IsGuarding)
            {
                if (!sim.Can(LockFlags.Defend)) return;
                if (!sim.TryLock(this, GuardLock, LockPriority.Defend)) return;

                Raise(info.Tick);
            }

            // Re-asserted every tick: the height is read from the stance the guard froze, and the
            // cancel window stays open so a dodge or a hit-react is never fighting the shield.
            Guarding = state.Stance == Stance.Crouch ? AttackHeight.Low : AttackHeight.High;
            sim.SetCancelWindow(this, true);
        }

        public override void Reset()
        {
            Lower();
            Guarding = AttackHeight.High;
        }

        private void Raise(long tick)
        {
            IsGuarding = true;
            _guardStartTick = tick;

            // Resolved once, when the guard goes up. The window a player commits to must not change
            // length underneath them because a buff ticked over mid-press.
            _parryWindow = _combat.ParryWindow.Resolve(Modifiers.ParryScale);
        }

        private void Lower()
        {
            IsGuarding = false;
            _guardStartTick = long.MinValue;
            _parryWindow = TickRange.None;
        }

        // ---------------------------------------------------------------- the gate

        public HitResult Evaluate(IDefendable owner, in HitEvent hit)
        {
            // Asked rather than assumed: a hit-react may have force-locked this character between
            // the guard's last tick and this hit, and a shield held by someone who has just been
            // knocked off their feet is not a shield.
            if (!IsGuarding || !owner.HoldsLock(this)) return HitResult.Continue;

            // Facing is the one requirement both answers share.
            if (!FacesTheHit(owner.Facing, hit.Facing)) return HitResult.Continue;

            if (hit.CanBe(DefensiveAnswer.Parry) && ParryWindowOpen(hit.Tick))
            {
                LastParryTick = hit.Tick;
                return new HitResult(HitOutcome.Parried, 0, _combat.ParryStunTicks);
            }

            if (!hit.CanBe(DefensiveAnswer.Block)) return HitResult.Continue;
            if (!Answers(hit.Height)) return HitResult.Continue;

            LastBlockedTick = hit.Tick;

            // Pushback, not a stun.
            //
            // Parking a stun would hand the character to the hit-react module, which force-locks —
            // and taking the lock takes the guard down. That is guard-break, arrived at by accident,
            // on every blocked hit. A guard is also already a lock that suppresses moving and
            // attacking, so "blockstun" would be a duration in which nothing further is prevented.
            // So the pressure a blocked hit applies is that it moves you, and the cost is chip.
            // Parked rather than written: this runs during the ATTACKER's tick, and the defender
            // takes the shove up on their own — the same law the stun obeys, for the same reason.
            owner.Impulse(hit.Facing * _combat.BlockPushbackSpeed, hit.Tick);

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
