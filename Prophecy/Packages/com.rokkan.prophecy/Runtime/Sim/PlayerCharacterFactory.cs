using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// Assembles the player's <see cref="CharacterSim"/> — the one place that knows the full
    /// moveset.
    ///
    /// <para>Having a factory at all is what keeps the sim honest: the same call builds the
    /// character a headless test drives and the character the scene drives, so a test can never
    /// be passing against a differently-configured character than the one being played. If the
    /// prefab assembled its own module list, the tuning locked in a unit test would mean nothing.</para>
    ///
    /// <para>Registration order here is irrelevant — <see cref="CharacterSim.Add{T}"/> sorts by
    /// <see cref="AbilityModule.Order"/> — so this list is grouped for reading, not for execution.</para>
    ///
    /// <para>Every ability is registered, always. Which of them actually tick is decided by the
    /// <see cref="AbilityLoadoutData"/>, so progression, debug toggles and a late-game test build
    /// are all the same mechanism: data, not a different character.</para>
    /// </summary>
    public static class PlayerCharacterFactory
    {
        public static CharacterSim Create(
            CollisionWorld world,
            MovementTuningData tuning,
            MovementSpace space = MovementSpace.SideScroll,
            AbilityLoadoutData loadout = null)
        {
            if (tuning == null) tuning = new MovementTuningData();

            var sim = new CharacterSim(world);
            sim.State.Space = space;
            tuning.ApplyBody(sim.State);

            // Locomotion.
            sim.Add(new GravityModule(tuning));
            sim.Add(new GroundMove(tuning));
            sim.Add(new TopDownMove(tuning));
            sim.Add(new Crouch(tuning));
            sim.Add(new FallLand(tuning));

            // Jumps.
            sim.Add(new Jump(tuning));
            sim.Add(new DoubleJump(tuning));

            // Walls.
            sim.Add(new WallJump(tuning));
            sim.Add(new WallSlide(tuning));

            // Climbing.
            sim.Add(new LedgeHang(tuning));
            sim.Add(new LedgePullUp(tuning));
            sim.Add(new LadderClimb(tuning));

            // Combat-adjacent movement.
            sim.Add(new DownThrust(tuning));
            sim.Add(new Interact(tuning));

            // Declared, not yet built. See PlannedAbilities.
            sim.Add(new Crawl());
            sim.Add(new DodgeStep());
            sim.Add(new FlameArt());

            loadout?.Apply(sim);

            return sim;
        }
    }
}
