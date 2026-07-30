using RGS.Core.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Ducking. Cosmetically small, mechanically load-bearing: stance is what gates Zelda II's
    /// high and low attacks, so this module decides which half of the moveset is available.
    ///
    /// <para>Standing back up asks <see cref="CharacterSim.HasHeadroomToStand"/> rather than
    /// deciding for itself. Under a low ceiling the character stays crouched even with the stick
    /// released — otherwise releasing down inside a crawl space would grow the body into the
    /// geometry, and the depenetration that followed would fire the player through the ceiling.
    /// The same question is what a future crawl or ledge pull-up has to ask, which is why it
    /// lives on the sim and not here.</para>
    ///
    /// <para>Airborne stance belongs to the sim, not this module — <c>UpdateStance</c> forces
    /// <see cref="Stance.Air"/> whenever the character is off the ground, because the down-thrust
    /// is gated on it and one owner of that fact is enough.</para>
    /// </summary>
    public sealed class Crouch : AbilityModule
    {
        private readonly MovementTuningData _tuning;

        public Crouch(MovementTuningData tuning)
        {
            _tuning = tuning;
        }

        public override AbilityId Id => AbilityId.Crouch;
        public override int Order => ModuleOrder.Crouch;
        public override MovementSpace ValidIn => MovementSpace.SideScroll;

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            var state = sim.State;

            if (!state.Grounded) return;

            // Stance is frozen while another action owns the body, rather than being read as
            // "down was released". Folding the lock into the crouch test instead would stand the
            // character up on the second tick of their own crouching attack — the swing suppresses
            // Move, which is indistinguishable from letting go of the stick. Stance gates which
            // half of the moveset is live, so it must not change underneath an attack that was
            // chosen from it.
            if (!sim.Can(LockFlags.Move)) return;

            bool wantsCrouch = input.Move.y <= -_tuning.CrouchInputThreshold;

            if (wantsCrouch)
            {
                state.Stance = Stance.Crouch;
            }
            else if (state.Stance == Stance.Crouch && sim.HasHeadroomToStand())
            {
                state.Stance = Stance.Stand;
            }
        }
    }
}
