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
    /// <see cref="AbilityModule.Order"/> — so this list is grouped for reading, not for execution.
    /// The planned abilities are registered disabled alongside the built ones on purpose: the
    /// moveset is complete from day one and progression is a sequence of flag flips.</para>
    /// </summary>
    public static class PlayerCharacterFactory
    {
        public static CharacterSim Create(
            CollisionWorld world,
            MovementTuningData tuning,
            MovementSpace space = MovementSpace.SideScroll)
        {
            if (tuning == null) tuning = new MovementTuningData();

            var sim = new CharacterSim(world);
            sim.State.Space = space;
            tuning.ApplyBody(sim.State);

            // Built.
            sim.Add(new GravityModule(tuning));
            sim.Add(new GroundMove(tuning));
            sim.Add(new TopDownMove(tuning));
            sim.Add(new Crouch(tuning));
            sim.Add(new Jump(tuning));
            sim.Add(new DownThrust(tuning));
            sim.Add(new Interact(tuning));
            sim.Add(new FallLand(tuning));

            // Declared, shipping disabled. See PlannedAbilities.
            sim.Add(new Crawl());
            sim.Add(new DoubleJump());
            sim.Add(new DodgeStep());
            sim.Add(new LedgeHang());
            sim.Add(new LedgePullUp());
            sim.Add(new LadderClimb());
            sim.Add(new FlameArt());

            return sim;
        }
    }
}
