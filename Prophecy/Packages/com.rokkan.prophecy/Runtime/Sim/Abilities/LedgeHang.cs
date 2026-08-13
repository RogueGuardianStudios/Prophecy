using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Catches a ledge on the way past it and holds there.
    ///
    /// <para>While hanging, this pins position and zeroes velocity every tick rather than trusting
    /// gravity to stay cancelled. A hang implemented as "set velocity to zero once" drifts the
    /// moment anything else writes to velocity, and the drift is slow enough to look like a
    /// physics bug rather than an ordering one.</para>
    ///
    /// <para><b>It owns the lock but not the outcome.</b> Grabbing suppresses movement and jumping
    /// so nothing fights the pin; letting go is triggered by pressing down, jumping, or by another
    /// ability ending the attachment — which is how pull-up works without either module knowing
    /// the other exists. This watches <see cref="CharacterState.Attachment"/>, and when it stops
    /// saying <c>Ledge</c>, it releases. Turn pull-up off and hanging still works, you just cannot
    /// climb up.</para>
    ///
    /// <para>The re-grab cooldown is what makes dropping possible at all: without it, releasing
    /// leaves the character exactly where the ledge is still detectable, and the next tick
    /// re-grabs it forever.</para>
    /// </summary>
    public sealed class LedgeHang : AbilityModule
    {
        private const LockFlags HangLock = LockFlags.Move | LockFlags.Jump;

        private readonly MovementTuningData _tuning;

        private long _releasedTick = long.MinValue;

        public LedgeHang(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.LedgeHang;
        public override int Order => ModuleOrder.LedgeHang;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (sim.HoldsLock(this))
            {
                // Someone else ended the attachment — a pull-up, a teleport, a scene change.
                if (state.Attachment != AttachmentKind.Ledge)
                {
                    ReleaseLock(sim, in info);
                    return;
                }

                Hold(sim, in input, in info);
                return;
            }

            TryGrab(sim, in info);
        }

        public override void Reset()
        {
            _releasedTick = long.MinValue;
        }

        private void TryGrab(CharacterSim sim, in SimTickInfo info)
        {
            var state = sim.State;

            if (state.Grounded) return;
            if (state.Attachment != AttachmentKind.None) return;
            if (_tuning.LedgeGrabRequiresFalling && state.Velocity.y > 0f) return;
            if (!sim.Can(LockFlags.Move)) return;

            if (_releasedTick != long.MinValue &&
                info.Tick - _releasedTick < _tuning.LedgeRegrabCooldownTicks) return;

            if (!sim.World.TryFindLedge(state.Body, state.Facing,
                                        _tuning.LedgeGrabMinHeight, _tuning.LedgeGrabMaxHeight,
                                        _tuning.LedgeProbeDistance,
                                        out float ledgeTopY, out float ledgeFaceX)) return;

            if (!sim.TryLock(this, HangLock, LockPriority.Movement)) return;

            // Hang with the body's leading edge against the wall face, feet the authored drop below
            // the surface — so the pose is a function of the geometry, not of where the player
            // happened to be when they caught it.
            float anchorX = ledgeFaceX - state.Facing * (state.BodySize.x * 0.5f);
            float anchorY = ledgeTopY - _tuning.LedgeHangDrop;

            state.Attachment = AttachmentKind.Ledge;
            state.AttachmentAnchor = new UnityEngine.Vector2(anchorX, anchorY);
            state.Position = state.AttachmentAnchor;
            state.Velocity = UnityEngine.Vector2.zero;
        }

        private void Hold(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            state.Position = state.AttachmentAnchor;
            state.Velocity = UnityEngine.Vector2.zero;

            if (input.Move.y <= -_tuning.CrouchInputThreshold)
            {
                Detach(sim, in info);
                return;
            }

            if (input.Jump.Pressed)
            {
                Detach(sim, in info);
                state.Velocity.y = _tuning.JumpVelocity;
                state.Stance = Stance.Air;
            }
        }

        private void Detach(CharacterSim sim, in SimTickInfo info)
        {
            sim.State.Attachment = AttachmentKind.None;
            ReleaseLock(sim, in info);
        }

        private void ReleaseLock(CharacterSim sim, in SimTickInfo info)
        {
            sim.ReleaseLock(this);
            _releasedTick = info.Tick;
        }
    }
}
