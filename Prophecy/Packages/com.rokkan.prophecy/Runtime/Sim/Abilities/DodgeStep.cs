using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// A short committed step that is briefly not there. The only thing in the game that grants
    /// invulnerability on purpose.
    ///
    /// <para><b>The startup is the whole design.</b> A dodge that is invulnerable from its first
    /// tick is a panic button: mash it and nothing can touch you. Three ticks of vulnerable
    /// wind-up mean the dodge has to be <i>predicted</i> rather than reacted with, which is the
    /// same argument the parry's startup makes and the reason both are readable answers instead of
    /// reflex tests.</para>
    ///
    /// <para><b>Distance is authored, speed is derived.</b> The step covers
    /// <see cref="CombatTuningData.DodgeDistance"/> metres over its active phase, so the number in
    /// the asset is one you can measure against level geometry and against an enemy's reach —
    /// exactly the argument that makes jumps authored as heights. Velocity is re-asserted every
    /// active tick because locomotion runs afterwards and would otherwise apply friction to it,
    /// making the distance depend on how the step began.</para>
    ///
    /// <para><b>Reaction priority, like the parry.</b> It can take over an attack's recovery
    /// through the cancel window, and can be taken out of a block. It cannot take over a parry and
    /// a parry cannot take over it — equal priorities do not displace each other, so committing to
    /// one defensive answer means committing to it.</para>
    ///
    /// <para>Grounded only. An air dodge is a different feel decision — it changes what jumping
    /// commits you to — and should be made deliberately rather than inherited.</para>
    /// </summary>
    public sealed class DodgeStep : AbilityModule, IDamageGate
    {
        /// <summary>Everything except Defend, matching the parry: committing to a dodge means
        /// committing to it, but the defensive slot stays open for the arbiter's sake.</summary>
        private const LockFlags StepLock =
            LockFlags.Move | LockFlags.Turn | LockFlags.Jump | LockFlags.Attack;

        /// <summary>Half deflection. Below this the stick is treated as neutral, so a controller
        /// resting a little off-centre produces the backstep the player asked for rather than a
        /// sidestep they did not.</summary>
        private const float DirectionThreshold = 0.5f;

        private readonly CombatTuningData _combat;
        private readonly AttackTimeline _timeline = new AttackTimeline();

        private int _direction = 1;

        public DodgeStep(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.DodgeStep;
        public override int Order => ModuleOrder.DodgeStep;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public int GateOrder => Combat.GateOrder.IFrames;

        /// <summary>Stat scaling for the i-frame window. Resolved at arm and held for the whole
        /// step, so a buff expiring mid-dodge cannot shorten the window already committed to.</summary>
        public AttackModifiers Modifiers = AttackModifiers.None;

        public AttackTimeline Timeline => _timeline;

        public bool IsDodging => _timeline.IsArmed;

        /// <summary>True on the ticks the step is actually intangible.</summary>
        public bool WindowOpen => _timeline.IsInvulnerable;

        /// <summary>-1 or +1: which way the current step is going.</summary>
        public int Direction => _direction;

        /// <summary>
        /// Metres per second needed to cover the authored distance in the active phase. Derived so
        /// there is one number to tune and it means something on a level.
        /// </summary>
        public float StepSpeed
        {
            get
            {
                var definition = _combat?.DodgeAction;
                if (definition == null || definition.ActiveTicks <= 0) return 0f;

                return _combat.DodgeDistance / (definition.ActiveTicks * SimConstants.FixedDeltaSeconds);
            }
        }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_combat == null || _combat.DodgeAction == null) return;

            // Outranked mid-step — a hit that got through the startup, a death. Drop it rather than
            // keep a window open for a character who is no longer stepping anywhere.
            if (_timeline.IsArmed && !sim.HoldsLock(this))
            {
                _timeline.Disarm();
                return;
            }

            if (!_timeline.IsArmed)
            {
                // TryStart already advanced to the step's tick zero; advancing again here would
                // spend two ticks on one frame and open the i-frames early.
                TryStart(sim, in input);
                if (_timeline.IsArmed) Step(sim);
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
            Step(sim);
        }

        public override void Reset()
        {
            _timeline.Disarm();
        }

        private void TryStart(CharacterSim sim, in InputFrame input)
        {
            if (!input.Dodge.Pressed) return;

            var state = sim.State;
            if (!state.Grounded) return;
            if (!sim.Can(LockFlags.Defend)) return;

            if (!sim.TryLock(this, StepLock, LockPriority.Reaction)) return;

            // Held direction if there is one, otherwise backwards. A neutral dodge stepping away
            // from what you are facing is the reading a player expects, and it means the move is
            // useful without also being a movement option.
            _direction = Mathf.Abs(input.Move.x) >= DirectionThreshold
                ? (input.Move.x < 0f ? -1 : 1)
                : -state.Facing;

            _timeline.Arm(_combat.DodgeAction, Modifiers);
            _timeline.Advance();
        }

        /// <summary>
        /// Drive the step. Re-asserted every active tick rather than set once, because locomotion
        /// runs after this and applies friction to a velocity it is suppressing — the same reason
        /// the down-thrust pins its dive speed.
        ///
        /// <para>Outside the active phase nothing is written, so the wind-up and the recovery are
        /// stationary and the friction that follows the step is what stops it.</para>
        /// </summary>
        private void Step(CharacterSim sim)
        {
            if (_timeline.CurrentPhase != AttackTimeline.Phase.Active) return;

            sim.State.Velocity.x = _direction * StepSpeed;
        }

        // ---------------------------------------------------------------- the gate

        public HitResult Evaluate(CharacterSim sim, in HitEvent hit)
        {
            if (!_timeline.IsInvulnerable) return HitResult.Continue;
            if (!hit.CanBe(DefensiveAnswer.IFrames)) return HitResult.Continue;

            return new HitResult(HitOutcome.Invulnerable);
        }
    }
}
