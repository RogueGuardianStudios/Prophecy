using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;

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
        /// <param name="combat">The moveset. Defaults are used when null, so a movement test needs
        /// no combat asset and still builds the same character the game does.</param>
        /// <param name="combatWorld">Who else is in the fight. Null is legitimate and common — a
        /// character with nothing to hit still swings, takes the lock and runs its timeline; the
        /// hits simply land on nobody.</param>
        public static CharacterSim Create(
            CollisionWorld world,
            MovementTuningData tuning,
            MovementSpace space = MovementSpace.SideScroll,
            AbilityLoadoutData loadout = null,
            CombatTuningData combat = null,
            ICombatWorld combatWorld = null)
        {
            if (tuning == null) tuning = new MovementTuningData();
            if (combat == null) combat = new CombatTuningData();

            var sim = new CharacterSim(world);
            sim.State.Space = space;
            sim.CombatWorld = combatWorld;
            tuning.ApplyBody(sim.State);

            // Locomotion.
            sim.Add(new GravityModule(tuning));
            sim.Add(new GroundMove(tuning));
            sim.Add(new TopDownMove(tuning));
            sim.Add(new Crouch(tuning));
            sim.Add(new DropThroughPlatform(tuning));
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

            // Combat. Offence, then the three answers to it — the last of which is not an answer
            // at all but the consequence of failing to make one.
            sim.Add(new AttackModule(combat));
            sim.Add(new Block(combat));
            sim.Add(new Parry(combat));
            sim.Add(new DodgeStep(combat));
            sim.Add(new HitReact(combat));

            sim.Vitals.MaxHealth = combat.MaxHealth;
            sim.Vitals.Reset();
            sim.HitStunTicks = combat.HitStunTicks;

            // Combat-adjacent movement. The down-thrust takes the combat tuning too: it swings its
            // own blade, which is what lets it bounce itself off what it hits.
            sim.Add(new DownThrust(tuning, combat));
            sim.Add(new Interact(tuning));

            // Declared, not yet built. See PlannedAbilities.
            sim.Add(new Crawl());
            sim.Add(new FlameArt());

            loadout?.Apply(sim);

            return sim;
        }
    }
}
