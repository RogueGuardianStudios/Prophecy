using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Losing control. Picks up a parked stun, takes the character away from whatever they were
    /// doing, shoves them, and hands them back after an authored number of ticks.
    ///
    /// <para><b>It force-locks.</b> Every voluntary action goes through <c>TryLock</c>, which
    /// demands the holder's cancel window be open — that rule is what makes an attack a
    /// commitment. A hit-react obeying it would never interrupt the swing it is a reaction to,
    /// which is the only thing it exists to do. So this is one of the few callers of
    /// <c>ForceLock</c>, and being hit is exactly the case that justifies it.</para>
    ///
    /// <para><b>Being hit and being parried are the same event here.</b> Both are "you are stunned,
    /// for this long, shoved this way", and the cause is carried only so presentation and tuning
    /// can tell a clean hit from a punished whiff. One reaction path means a parry's reward cannot
    /// drift away from a hit's cost.</para>
    ///
    /// <para><b>The i-frames it grants are a multi-hit guard, not a reward.</b> Without them a
    /// lingering hit box would re-hit on the tick control returned and chain the player to death
    /// with no chance to answer. They follow a clean hit only — granting them on a block would make
    /// the guard strictly better than not being there.</para>
    ///
    /// <para>Runs before the attack and locomotion modules so an interrupted swing sees the lost
    /// lock on the same tick it was taken, rather than resolving one more tick of hits first.</para>
    /// </summary>
    public sealed class HitReact : AbilityModule, IDamageGate
    {
        /// <summary>Everything. Being hit is not a moment you get to act in.</summary>
        private const LockFlags ReactLock = LockFlags.All;

        private readonly CombatTuningData _combat;

        private long _endTick = long.MinValue;
        private long _invulnerableUntilTick = long.MinValue;

        public HitReact(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.HitReact;
        public override int Order => ModuleOrder.HitReact;
        public override MovementSpace ValidIn => MovementSpace.Both;

        public int GateOrder => Combat.GateOrder.IFrames;

        public bool IsReacting { get; private set; }

        /// <summary>What caused the reaction currently running.</summary>
        public HitOutcome Cause { get; private set; }

        /// <summary>Ticks of stun left. Zero when free. Debug overlay.</summary>
        public int TicksRemaining(long currentTick) =>
            IsReacting && _endTick > currentTick ? (int)(_endTick - currentTick) : 0;

        /// <summary>Ticks of post-hit invulnerability left. Debug overlay.</summary>
        public int InvulnerableTicksRemaining(long currentTick) =>
            _invulnerableUntilTick > currentTick ? (int)(_invulnerableUntilTick - currentTick) : 0;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_combat == null) return;

            if (IsReacting)
            {
                // Outranked by something even stronger — death, a scene change. Let go quietly.
                if (!sim.HoldsLock(this))
                {
                    IsReacting = false;
                }
                else if (info.Tick >= _endTick)
                {
                    sim.ReleaseLock(this);
                    IsReacting = false;
                }
            }

            // Checked even while already reacting: a second hit during hit-stun should extend and
            // re-shove rather than be swallowed, or a character can be safely pinned by the first
            // attack that connects.
            if (!sim.TryConsumeStun(out var stun)) return;

            if (!sim.ForceLock(this, ReactLock, LockPriority.HitReact)) return;

            IsReacting = true;
            Cause = stun.Cause;
            _endTick = info.Tick + stun.Ticks;

            Shove(sim, stun);

            if (stun.Cause == HitOutcome.Landed)
                _invulnerableUntilTick = info.Tick + _combat.HitInvulnerabilityTicks;
        }

        public override void Reset()
        {
            IsReacting = false;
            _endTick = long.MinValue;
            _invulnerableUntilTick = long.MinValue;
        }

        /// <summary>
        /// Knockback written straight to velocity, in the direction the blow travelled. Gravity has
        /// already run this tick — it is first in the order precisely so a launch set later
        /// integrates in full — so the lift survives to be integrated rather than being shaved off
        /// before it applies.
        ///
        /// <para>Nothing here decays it. The lock suppresses Move, so locomotion's own friction
        /// brings the slide to a stop, which is both fewer numbers and the same curve a landing
        /// uses.</para>
        /// </summary>
        private void Shove(CharacterSim sim, in PendingStun stun)
        {
            var state = sim.State;

            state.Velocity.x = stun.Direction * _combat.KnockbackSpeed;

            // Only a clean hit lifts you off the floor. A parried attacker is shoved back on their
            // heels, not launched — the punish is the ticks, not the trajectory.
            if (stun.Cause == HitOutcome.Landed && state.Grounded)
                state.Velocity.y = _combat.KnockbackLift;
        }

        // ---------------------------------------------------------------- the gate

        public HitResult Evaluate(IDefendable owner, in HitEvent hit)
        {
            if (hit.Tick >= _invulnerableUntilTick) return HitResult.Continue;

            // The authored "cannot be dodged" flag means every i-frame source, this one
            // included — an unavoidable grab that whiffed against whoever was recently hit
            // would read as a random miss, with the cause invisible from the data.
            if (!hit.CanBe(DefensiveAnswer.IFrames)) return HitResult.Continue;

            return new HitResult(HitOutcome.Invulnerable);
        }
    }
}
