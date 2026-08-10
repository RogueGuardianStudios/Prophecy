using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Combat;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// The up-thrust: jump, hold up, attack, and stab through whatever hangs above you. The
    /// rising mirror of <see cref="DownThrust"/> — the signature Zelda II pair (gray-box doc §3:
    /// live from day one; taught in Threnhold where the dive is Aldhearth's).
    ///
    /// <para><b>Like the dive, it swings its own blade</b> through the shared
    /// <see cref="HitSweep"/> — one action with a movement half and a damage half, nothing
    /// reaching into another module. <b>Unlike the dive, it drives no velocity.</b> The jump arc
    /// IS the movement half; the thrust rides it. That is also what ends it: the blade is out
    /// while the body rises and the apex sheathes it — falling is the down-thrust's domain, and
    /// the pair partitions the air between them.</para>
    ///
    /// <para>No bounce, deliberately. The dive's pop is the reward for landing the
    /// hardest-to-place attack in the game; the up-thrust is placed by the jump that carries it,
    /// so its reward is simply the hit. The sweep's dedup means each target is struck once per
    /// thrust — a fresh jump is a fresh blade.</para>
    ///
    /// <para>It commits: for <see cref="MovementTuningData.UpThrustMinTicks"/> the lock's cancel
    /// window stays shut, then a higher-priority reaction can take over — the dive's
    /// arrangement, unchanged.</para>
    /// </summary>
    public sealed class UpThrust : AbilityModule
    {
        private const LockFlags ThrustLock = LockFlags.Move | LockFlags.Turn | LockFlags.Jump | LockFlags.Attack;

        private readonly MovementTuningData _tuning;
        private readonly CombatTuningData _combat;
        private readonly HitSweep _sweep = new HitSweep();

        private bool _active;
        private long _startTick;

        public UpThrust(MovementTuningData tuning, CombatTuningData combat = null)
        {
            _tuning = tuning;
            _combat = combat;
        }

        public override AbilityId Id => AbilityId.UpThrust;
        public override int Order => ModuleOrder.UpThrust;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>True while the thrust is in progress. Debug overlay and the animator.</summary>
        public bool IsActive => _active;

        /// <summary>The box this thrust swings, for the overlay — live for the whole rise, so
        /// it is drawn the way projectiles are: dangerous for its entire existence.</summary>
        public AttackHitBox Volume => _combat != null ? _combat.UpThrustBox : default;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_active)
            {
                Continue(sim, in input, in info);
                return;
            }

            TryStart(sim, in input, in info);
        }

        public override void Reset()
        {
            _active = false;
            _startTick = 0;
        }

        private void TryStart(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (!input.Attack.Pressed) return;
            if (input.Move.y < _tuning.CrouchInputThreshold) return;

            // Rising is the WHOLE condition — the grounded flag is not consulted because a
            // grounded body never carries upward velocity; the velocity says everything the
            // flag would, without going stale at the top of the tick. A falling body holding
            // up gets nothing — the dive owns the way down, and an up-thrust that fired on the
            // descent would blur the pair into one air slash. (A press on the EXACT tick of
            // the jump belongs to the standing slash, whose lock also refuses the jump — an
            // attack is a commitment. The thrust's input is jump, THEN attack while rising;
            // pinned in ASameTickJumpStabBelongsToTheSlash.)
            if (state.Velocity.y <= 0f) return;

            if (!sim.Can(LockFlags.Attack)) return;
            if (!sim.TryLock(this, ThrustLock, LockPriority.Attack)) return;

            _active = true;
            _startTick = info.Tick;

            // A fresh thrust, so everything over it is hittable again — including whatever the
            // last jump already struck.
            _sweep.Begin();
        }

        /// <summary>
        /// Keep stabbing. The thrust ends at the apex, on the ground, and nowhere else — the
        /// rise is its whole lifetime, so there is no hold-to-extend to learn.
        /// </summary>
        private void Continue(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            // A landing mid-rise (a moving platform arriving under the feet) ends it the same
            // way it ends the dive: grounded is not an airborne move's state.
            if (state.Grounded)
            {
                Stop(sim);
                return;
            }

            // The apex sheathes the blade. Gravity runs before this module, so the tick the
            // velocity crosses zero is the tick the rise is over.
            if (state.Velocity.y <= 0f)
            {
                Stop(sim);
                return;
            }

            sim.SetCancelWindow(this, info.Tick - _startTick >= _tuning.UpThrustMinTicks);

            Strike(sim, in info);
        }

        /// <summary>
        /// Swing the blade over the head. No cover query, same argument as the dive: the volume
        /// is directly overhead, and a wall between a body and the thing it is jumping into is
        /// not a situation the geometry can produce.
        /// </summary>
        private void Strike(CharacterSim sim, in SimTickInfo info)
        {
            if (_combat == null || sim.CombatWorld == null) return;

            var box = _combat.UpThrustBox;
            if (box.HalfExtents.x <= 0f || box.HalfExtents.y <= 0f) return;

            var state = sim.State;
            var attacker = Attacker.FromBody(state.CombatId, state.Position, state.BodySize,
                                             state.Facing, state.Team,
                                             sim.Stats.DamageScale);

            var result = _sweep.Sweep(0, box, attacker, sim.CombatWorld, null,
                                      info.Tick, _combat.UpThrustAttackId);

            if (result.Punished)
            {
                // Parried at the top of a jump. The stun rides the rest of the arc — reading an
                // up-thrust means the diver-in-reverse falls with nothing left to say.
                sim.Stun(result.AttackerStunTicks, -state.Facing, info.Tick, result.LastOutcome);
                Stop(sim);
            }

            // A connect changes nothing about the movement: no bounce, no hang. The jump arc
            // was the commitment and it continues; the sweep's dedup is what keeps one thrust
            // from milling a target it rises past.
        }

        private void Stop(CharacterSim sim)
        {
            _active = false;
            sim.ReleaseLock(this);
        }
    }
}
