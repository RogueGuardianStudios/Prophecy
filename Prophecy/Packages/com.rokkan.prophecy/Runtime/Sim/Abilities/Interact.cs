using RGS.Core.Sim;
using Rokkan.Prophecy.Sim.Collision;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Turns an interact press into a request the world layer can answer, plus the volume to
    /// answer it in.
    ///
    /// <para>It deliberately does <b>not</b> know what an interactable is. Doors, signs, portals
    /// and NPCs live in the world layer, which reads <see cref="ProbeBox"/> and calls
    /// <see cref="ConsumeRequest"/> when it finds a match. The alternative — the sim holding a
    /// list of interactables — would drag scene objects across the headless line for no gain.</para>
    ///
    /// <para>Requests are latched for the buffer window rather than consumed on the tick they
    /// arrive, so pressing interact a hair before reaching a door still opens it. Same forgiveness
    /// as the jump buffer, same reason.</para>
    ///
    /// <para>Valid in both spaces: talking to someone in the overworld and opening a door in a
    /// side-scroll area are the same verb, and forking them would be two things to maintain.</para>
    /// </summary>
    public sealed class Interact : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        private long _requestedTick = long.MinValue;

        public Interact(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.Interact;
        public override int Order => ModuleOrder.Interact;
        public override MovementSpace ValidIn => MovementSpace.Both;

        /// <summary>The box to search for interactables: the body, grown forward by the reach.</summary>
        public Aabb ProbeBox { get; private set; }

        /// <summary>True while an unanswered interact press is still within its buffer window.</summary>
        public bool HasPendingRequest { get; private set; }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (input.Interact.Pressed)
                _requestedTick = info.Tick;

            HasPendingRequest = _requestedTick != long.MinValue &&
                                info.Tick - _requestedTick <= _tuning.InteractBufferTicks;

            if (!HasPendingRequest)
                _requestedTick = long.MinValue;

            var body = state.Body;
            float reach = _tuning.InteractRange * state.Facing;

            ProbeBox = new Aabb(
                new Vector2(Mathf.Min(body.Min.x, body.Min.x + reach), body.Min.y),
                new Vector2(Mathf.Max(body.Max.x, body.Max.x + reach), body.Max.y));
        }

        /// <summary>Called by the world layer once it has acted on the request.</summary>
        public void ConsumeRequest()
        {
            _requestedTick = long.MinValue;
            HasPendingRequest = false;
        }

        public override void Reset()
        {
            _requestedTick = long.MinValue;
            HasPendingRequest = false;
            ProbeBox = default;
        }
    }
}
