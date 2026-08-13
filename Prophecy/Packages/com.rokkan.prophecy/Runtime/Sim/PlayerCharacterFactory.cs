using RGS.Core;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Collision;
using Rokkan.Prophecy.Sim.Combat;
using Rokkan.Prophecy.Sim.Items;

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

            // Water: the sink that is the global rule, and the art that makes peace with it.
            sim.Add(new Swim(tuning));
            sim.Add(new Buoyancy(tuning));

            // Doors: the committed crossing between rooms — Zelda II's screens.
            sim.Add(new DoorTransit(tuning));

            // Climbing.
            sim.Add(new LedgeHang(tuning));
            sim.Add(new LedgePullUp(tuning));
            sim.Add(new LadderClimb(tuning));

            // Combat. Offence, then the three answers to it — the last of which is not an answer
            // at all but the consequence of failing to make one.
            sim.Add(new AttackModule(combat));
            sim.Add(new Block(combat));
            sim.Add(new DodgeStep(combat));
            sim.Add(new HitReact(combat));

            // The stat sheet is the ONLY authority on the health cap — the sim re-syncs
            // Vitals.MaxHealth from Stats.MaxHealth every tick. Seeding the curve's base from
            // the authored tuning is what makes a 12-quarter grunt STAY 12 quarters; without
            // it the first tick quietly stomps every character to the default curve's Heart 1,
            // which is exactly how the old 100 survived the quarters renumber for a day.
            sim.Stats.Tuning.BaseHealth = combat.MaxHealth;

            // DEV ONLY (Matt, 2026-08-12): half the first rite's cost, so the pack's Resolve
            // seam has something to show while nothing awards Resolve yet. Safe against
            // levelling — AwardResolve only converts at the full cost. Remove when the
            // Resolve economy exists; listed in Plans/Release-Checklist.md.
            sim.Stats.AwardResolve(sim.Stats.Tuning.ResolveForNextLevel(sim.Stats.TotalLevels) / 2);

            sim.Vitals.MaxHealth = combat.MaxHealth;
            sim.Vitals.Reset();
            sim.HitStunTicks = combat.HitStunTicks;

            // Combat-adjacent movement. The thrusts take the combat tuning too: each swings its
            // own blade, which is what lets the dive bounce itself off what it hits.
            sim.Add(new DownThrust(tuning, combat));
            sim.Add(new UpThrust(tuning, combat));
            sim.Add(new Interact(tuning));

            // The cast button and the flask row — the HUD's verbs.
            sim.Add(new CastArt(combat));
            sim.Add(new DrinkFlask(combat));

            // Every art known in the gray box; the loadout's art toggles trim from here.
            var catalog = Arts.ArtCatalog.All;
            for (int i = 0; i < catalog.Length; i++) sim.KnownArts.Add(catalog[i].Id);

            // The sim already swings and guards, so the sheet says so from the first boot.
            // Code-built data for the gray box; authored ItemDetails assets take over when
            // items are found in the world rather than born with.
            sim.Worn[EquipSlot.Sword] = new ItemData
            {
                Id = SerializableGuid.NewGuid(),
                Name = "a plain sword",
                Slot = ItemSlotType.Sword,
            };
            sim.Worn[EquipSlot.Shield] = new ItemData
            {
                Id = SerializableGuid.NewGuid(),
                Name = "a plain shield",
                Slot = ItemSlotType.Shield,
            };

            // Declared, not yet built. See PlannedAbilities.
            sim.Add(new Crawl());

            loadout?.Apply(sim);

            return sim;
        }
    }
}
