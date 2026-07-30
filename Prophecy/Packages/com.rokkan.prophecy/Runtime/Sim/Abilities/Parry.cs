using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The timed answer. A short action with a window inside it — get the window over the hit and
    /// the attacker pays; miss and the recovery is the price.
    ///
    /// <para><b>Authored as an attack with no hit boxes, and run on the same
    /// <see cref="AttackTimeline"/>.</b> A parry has startup, a window, and a recovery that
    /// punishes a guess — which is an attack's shape exactly. Giving it a second timeline type
    /// would mean two implementations of tick windows drifting apart, and the parry window is
    /// precisely where HopeFell's version rotted: theirs was <c>0.2f</c> seconds accumulated in
    /// <c>Update()</c>, so it was quietly more generous on a slow machine.</para>
    ///
    /// <para><b>It is the reward that makes it a mechanic, not the negation.</b> A parry that only
    /// cancelled damage would be a better block. This stuns the attacker, and the length of that
    /// stun is the whole design knob: too short and a correct read buys nothing, too long and one
    /// parry ends the fight.</para>
    ///
    /// <para>Priority <see cref="LockPriority.Reaction"/>, so it can take over an attack's recovery
    /// through the cancel window the arbiter already understands — and can be raised straight out
    /// of a block, which holds its cancel window open for exactly this reason.</para>
    /// </summary>
    public sealed class Parry : AbilityModule, IDamageGate
    {
        /// <summary>Everything except Defend. Committing to a parry means committing to it.</summary>
        private const LockFlags ParryLock =
            LockFlags.Move | LockFlags.Turn | LockFlags.Jump | LockFlags.Attack;

        private readonly CombatTuningData _combat;
        private readonly AttackTimeline _timeline = new AttackTimeline();

        public Parry(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.Parry;
        public override int Order => ModuleOrder.Parry;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public int GateOrder => Combat.GateOrder.Parry;

        /// <summary>Stat scaling for the parry window. Set by whatever owns equipment; resolved at
        /// arm and held for the whole action, so a buff expiring mid-parry cannot shorten the
        /// window the player already committed to.</summary>
        public AttackModifiers Modifiers = AttackModifiers.None;

        public AttackTimeline Timeline => _timeline;

        public bool IsActive => _timeline.IsArmed;

        /// <summary>True on the ticks an incoming hit would be turned. The overlay's headline.</summary>
        public bool WindowOpen => _timeline.IsParrying;

        /// <summary>Tick of the most recent successful parry. <c>long.MinValue</c> if never.</summary>
        public long LastParryTick { get; private set; } = long.MinValue;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_combat == null || _combat.ParryAction == null) return;

            // Outranked mid-parry — a hit that got through, a death. Drop it rather than keep a
            // window open for a character who is no longer holding anything up.
            if (_timeline.IsArmed && !sim.HoldsLock(this))
            {
                _timeline.Disarm();
                return;
            }

            if (!_timeline.IsArmed)
            {
                // TryStart already advanced to the action's tick zero. Falling through to the
                // advance below would spend a second tick on the same frame and open the window
                // one tick early — which is a parry that is not the parry that was authored.
                TryStart(sim, in input);
                if (_timeline.IsArmed) sim.SetCancelWindow(this, _timeline.IsCancellable);
                return;
            }

            _timeline.Advance();

            if (_timeline.IsComplete)
            {
                _timeline.Disarm();
                sim.ReleaseLock(this);
                return;
            }

            sim.SetCancelWindow(this, _timeline.IsCancellable);
        }

        public override void Reset()
        {
            _timeline.Disarm();
        }

        private void TryStart(CharacterSim sim, in InputFrame input)
        {
            if (!input.Parry.Pressed) return;

            var state = sim.State;
            if (!state.Grounded) return;
            if (!sim.Can(LockFlags.Defend)) return;

            if (!sim.TryLock(this, ParryLock, LockPriority.Reaction)) return;

            _timeline.Arm(_combat.ParryAction, Modifiers);

            // Armed and advanced within the same tick, so the action's tick zero is the tick the
            // button was pressed. A parry that started a tick late would be a parry authored one
            // tick differently from the one being read off the telegraph.
            _timeline.Advance();
        }

        // ---------------------------------------------------------------- the gate

        public HitResult Evaluate(CharacterSim sim, in HitEvent hit)
        {
            if (!_timeline.IsParrying) return HitResult.Continue;
            if (!hit.CanBe(DefensiveAnswer.Parry)) return HitResult.Continue;

            // A parry covers the front, like a block. Turning your back mid-window is not a read.
            if (sim.State.Facing == hit.Facing) return HitResult.Continue;

            LastParryTick = hit.Tick;

            return new HitResult(HitOutcome.Parried, 0, _combat.ParryStunTicks);
        }
    }
}
