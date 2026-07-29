using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Climbs from a hang onto the surface above.
    ///
    /// <para>A separate ability from hanging on purpose, and not only because they unlock at
    /// different times. It reads <see cref="CharacterState.Attachment"/> to know a ledge is being
    /// held and clears it to say the climb happened — it never touches the module that did the
    /// grabbing. Switch this off and the character can still catch ledges and drop from them,
    /// which is a genuinely different and usable moveset rather than a broken one.</para>
    ///
    /// <para><b>It checks headroom at the destination before committing.</b> Pulling up into a
    /// low ceiling would leave the body inside geometry, and the depenetration that followed
    /// would fire the character somewhere alarming. The check costs one query and turns an
    /// occasional spectacular bug into simply not climbing.</para>
    ///
    /// <para>The climb takes authored ticks rather than teleporting, so there is a window for the
    /// animation that will eventually cover it — and so the movement is timed in the same units
    /// as everything else in the sim.</para>
    /// </summary>
    public sealed class LedgePullUp : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private bool _climbing;
        private long _startTick;
        private Vector2 _from;
        private Vector2 _to;

        public LedgePullUp(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.LedgePullUp;
        public override int Order => ModuleOrder.LedgePullUp;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public bool IsClimbing => _climbing;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            if (_climbing)
            {
                Advance(sim, in info);
                return;
            }

            TryStart(sim, in input, in info);
        }

        public override void Reset()
        {
            _climbing = false;
        }

        private void TryStart(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (state.Attachment != AttachmentKind.Ledge) return;
            if (input.Move.y < _tuning.CrouchInputThreshold) return;   // holding up

            // Where the feet land: on top of the surface the hands are holding.
            var destination = new Vector2(
                state.Position.x + state.Facing * state.BodySize.x,
                state.Position.y + _tuning.LedgeHangDrop);

            if (sim.World.OverlapsAnySolid(state.BodyAt(destination))) return;

            _climbing = true;
            _startTick = info.Tick;
            _from = state.Position;
            _to = destination;

            // Claiming the character for the climb, and ending the attachment, is what tells the
            // hang to let go — without this module ever naming it.
            sim.TryLock(this, LockFlags.Move | LockFlags.Jump, LockPriority.Movement);
            state.Attachment = AttachmentKind.None;
        }

        private void Advance(CharacterSim sim, in SimTickInfo info)
        {
            var state = sim.State;

            int elapsed = (int)(info.Tick - _startTick);
            int duration = Mathf.Max(1, _tuning.LedgePullUpTicks);

            float t = Mathf.Clamp01(elapsed / (float)duration);

            state.Position = Vector2.Lerp(_from, _to, t);
            state.Velocity = Vector2.zero;

            if (elapsed < duration) return;

            state.Position = _to;
            _climbing = false;
            sim.ReleaseLock(this);
        }
    }
}
