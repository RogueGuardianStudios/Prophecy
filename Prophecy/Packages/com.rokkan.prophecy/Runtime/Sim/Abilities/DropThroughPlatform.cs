using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Drops the character down through a platform they are standing on.
    ///
    /// <para><b>The hold duration is the whole problem.</b> Down is already crouch, and crouching
    /// on a platform is far more common than wanting to leave through it — so a drop triggered on
    /// the press would make crouching near a ledge unreliable in a way players experience as the
    /// game randomly dropping them. Requiring the direction to be <i>held</i> keeps a tap as a
    /// crouch and makes the drop unambiguously deliberate. It is the same reasoning as the jump
    /// buffer, pointed the other way: forgiveness for what was probably meant.</para>
    ///
    /// <para>Only platforms that permit it. A strict one-way platform is the sealed floor of an
    /// area you climbed into, and dropping through one would put the player back where they came
    /// from by crouching — see <see cref="Collision.Solid.AllowsDropThrough"/>.</para>
    ///
    /// <para>The permission expires on a timer as well as on landing. Landing alone is not enough:
    /// a character who drops and immediately lands on another pass-through platform would fall
    /// through that one too, and then the next, all the way out of the level.</para>
    /// </summary>
    public sealed class DropThroughPlatform : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private int _heldTicks;
        private long _dropStartTick = long.MinValue;

        public DropThroughPlatform(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.DropThrough;
        public override int Order => ModuleOrder.DropThrough;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>Ticks of held input accumulated so far. Debug overlay.</summary>
        public int ChargeTicks => _heldTicks;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            // A climb owns the pass-through permission for as long as it lasts — see
            // LadderClimb. Two modules writing the same flag on the same tick is the one way this
            // gets genuinely confusing, so ownership is explicit rather than decided by tick order.
            if (state.Attachment != AttachmentKind.None)
            {
                _heldTicks = 0;
                _dropStartTick = long.MinValue;
                return;
            }

            if (state.DropThrough)
            {
                Maintain(sim, in info);
                return;
            }

            TryStart(sim, in input, in info);
        }

        public override void Reset()
        {
            _heldTicks = 0;
            _dropStartTick = long.MinValue;
        }

        private void Maintain(CharacterSim sim, in SimTickInfo info)
        {
            var state = sim.State;

            bool expired = _dropStartTick == long.MinValue ||
                           info.Tick - _dropStartTick >= _tuning.DropThroughTicks;

            if (!state.Grounded && !expired) return;

            state.DropThrough = false;
            _dropStartTick = long.MinValue;
            _heldTicks = 0;
        }

        private void TryStart(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            bool holdingDown = input.Move.y <= -_tuning.CrouchInputThreshold;

            if (!holdingDown || !state.Grounded || !sim.Can(LockFlags.Move))
            {
                _heldTicks = 0;
                return;
            }

            if (!sim.World.StandingOnDroppablePlatform(state.Body))
            {
                _heldTicks = 0;
                return;
            }

            _heldTicks++;
            if (_heldTicks < _tuning.DropThroughHoldTicks) return;

            state.DropThrough = true;
            state.Stance = Stance.Air;
            _dropStartTick = info.Tick;
            _heldTicks = 0;
        }
    }
}
