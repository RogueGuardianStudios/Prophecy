using System.Collections.Generic;
using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The character's attacks: chooses one from the stance, commits the character to it, runs its
    /// timeline, and asks the world who the live hit boxes touched.
    ///
    /// <para>This is the join between the combat timing spine and the rest of the sim, and it is
    /// deliberately thin. <see cref="AttackTimeline"/> already knows what is true on a given tick,
    /// <see cref="ComboRunner"/> already knows how a chain links, <see cref="HitResolver"/> already
    /// knows who a box touches. What was missing was the piece that takes the lock, spends the
    /// input, and turns "box 0 is live" into a hit — so that is all this does.</para>
    ///
    /// <para><b>The cancel window is not re-implemented here.</b> It is published to the arbiter
    /// via <c>SetCancelWindow</c> every tick, and the arbiter's existing rule — a takeover needs
    /// strictly higher priority <i>and</i> an open window — then gives parry-cancels-recovery and
    /// hit-react-interrupts-anything for free. Nothing in this module knows a parry exists.</para>
    ///
    /// <para><b>One hit per box, per target, per action.</b> A box live for four ticks would
    /// otherwise deal its damage four times. Dedup is keyed on the box index rather than the whole
    /// attack, because a multi-hit move's second sweep is a genuinely separate hit and should
    /// connect again — and it clears on every new chain link, so the follow-up re-hits whoever the
    /// opener did.</para>
    ///
    /// <para><b>Side-scroll only.</b> The overworld is traversal; encounters resolve by dropping
    /// into a side-scroll scene, which is Zelda II's structure and the reason
    /// <see cref="CharacterSim"/> forces <see cref="Stance.Stand"/> in top-down.</para>
    /// </summary>
    public sealed class AttackModule : AbilityModule, IDamageGate
    {
        /// <summary>
        /// Same flags the down-thrust commits with: a swing takes away movement, turning and
        /// jumping, and stops another attack starting from neutral. Chains are not blocked by this
        /// — they go through the cancel window, not through <c>Can</c>.
        /// </summary>
        private const LockFlags SwingLock =
            LockFlags.Move | LockFlags.Turn | LockFlags.Jump | LockFlags.Attack;

        private readonly CombatTuningData _combat;
        private readonly HitSweep _sweep = new HitSweep();

        private ComboRunner _runner;
        private IReadOnlyDictionary<string, AttackDefinition> _runnerMoveset;

        public AttackModule(CombatTuningData combat)
        {
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.Attack;
        public override int Order => ModuleOrder.Attack;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public int GateOrder => Combat.GateOrder.IFrames;

        /// <summary>
        /// Stat scaling applied to the next attack armed. Set by whatever owns equipment; windows
        /// are resolved at <c>Arm</c> and held, so changing this mid-swing affects the next attack
        /// and never the one being thrown.
        /// </summary>
        public AttackModifiers Modifiers = AttackModifiers.None;

        /// <summary>The chain state. Exposed for the debug overlay and for tests.</summary>
        public ComboRunner Runner => _runner;

        public bool IsAttacking => _runner != null && _runner.IsAttacking;

        /// <summary>The attack running right now, or null.</summary>
        public AttackDefinition Current => _runner?.Current;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_combat == null) return;

            EnsureRunner();

            if (input.Attack.Pressed) _runner.QueueInput(info.Tick);
            _runner.BufferTicks = _combat.AttackBufferTicks;

            // Outranked mid-swing — a hit-react, a death, a scene change. Drop the attack rather
            // than keep resolving hits for a character who is no longer swinging. Checked before
            // anything else so no hit lands on the tick control was taken away.
            if (_runner.IsAttacking && !sim.HoldsLock(this))
            {
                Abandon();
                return;
            }

            if (!_runner.IsAttacking) TryStart(sim, in info);
            if (!_runner.IsAttacking) return;

            // A new link starts a fresh action, so whoever the opener hit is hittable again.
            if (_runner.Advance(info.Tick, Modifiers)) _sweep.Begin();

            if (!_runner.IsAttacking)
            {
                // Ran out with nothing queued. Control comes back this tick, not next.
                Finish(sim);
                return;
            }

            sim.SetCancelWindow(this, _runner.Timeline.IsCancellable);
            ResolveHits(sim, in info);
        }

        public override void Reset()
        {
            _runner?.End();
            _sweep.Begin();
        }

        // ------------------------------------------------------------------ starting

        private void TryStart(CharacterSim sim, in SimTickInfo info)
        {
            // No press remembered. The buffer expires on its own, so a press made while locked out
            // does not fire five seconds later when control returns.
            if (_runner.BufferedTicksRemaining(info.Tick) <= 0) return;

            if (!sim.Can(LockFlags.Attack)) return;

            var state = sim.State;

            string id = _combat.IdForStance(state.Stance);
            if (string.IsNullOrEmpty(id)) return;

            if (!_combat.Moveset.TryGetValue(id, out var definition) || definition == null) return;
            if (definition.RequiredStance != state.Stance) return;

            // Claimed before arming, so a refused lock leaves no half-started attack behind.
            if (!sim.TryLock(this, SwingLock, LockPriority.Attack)) return;

            if (!_runner.Begin(id, Modifiers))
            {
                sim.ReleaseLock(this);
                return;
            }

            _sweep.Begin();
        }

        private void Finish(CharacterSim sim)
        {
            sim.ReleaseLock(this);
            _sweep.Begin();
        }

        /// <summary>Interrupted by something with a stronger claim. The lock is already theirs, so
        /// releasing it here would free <i>their</i> claim — hence only local state is cleared.</summary>
        private void Abandon()
        {
            _runner.End();
            _sweep.Begin();
        }

        // ------------------------------------------------------------------ hits

        private void ResolveHits(CharacterSim sim, in SimTickInfo info)
        {
            var timeline = _runner.Timeline;
            int boxes = timeline.HitBoxCount;
            if (boxes == 0) return;

            var state = sim.State;
            var attacker = Attacker.FromBody(state.CombatId, state.Position, state.BodySize,
                                             state.Facing, state.Team);

            var definition = _runner.Current;
            string attackId = definition != null ? definition.Id : null;

            for (int i = 0; i < boxes; i++)
            {
                if (!timeline.IsHitBoxLive(i)) continue;

                var result = _sweep.Sweep(i, timeline.GetHitBox(i), attacker,
                                          sim.CombatWorld, sim.World, info.Tick, attackId);

                if (!result.Punished) continue;

                // Parried. Knocked back the way we came.
                sim.Stun(result.AttackerStunTicks, -state.Facing, info.Tick, result.LastOutcome);
                return;
            }
        }

        // ------------------------------------------------------------------ the gate

        /// <summary>
        /// I-frames authored on the attack currently running. A dodge is mostly this; most attacks
        /// have none, and an unarmed character has none at all.
        /// </summary>
        public HitResult Evaluate(CharacterSim sim, in HitEvent hit)
        {
            if (_runner == null || !_runner.IsAttacking) return HitResult.Continue;
            if (!_runner.Timeline.IsInvulnerable) return HitResult.Continue;

            return new HitResult(HitOutcome.Invulnerable);
        }

        // ------------------------------------------------------------------ moveset

        /// <summary>
        /// Rebuilds the runner if the moveset was replaced — which is what adding or removing an
        /// attack in the inspector does. Only while idle: swapping the lookup mid-swing would
        /// change what the current attack chains into halfway through it.
        /// </summary>
        private void EnsureRunner()
        {
            var moveset = _combat.Moveset;

            if (_runner != null && ReferenceEquals(_runnerMoveset, moveset)) return;
            if (_runner != null && _runner.IsAttacking) return;

            _runner = new ComboRunner(moveset, _combat.AttackBufferTicks);
            _runnerMoveset = moveset;
            _sweep.Begin();
        }
    }
}
