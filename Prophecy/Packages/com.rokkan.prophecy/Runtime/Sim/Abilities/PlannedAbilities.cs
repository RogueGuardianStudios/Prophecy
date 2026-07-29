using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Base for an ability that is declared but not yet built. Ships disabled and ticks to
    /// nothing.
    ///
    /// <para>These exist as real registered modules rather than as a to-do list because that is
    /// what tests the architecture. The claim is that adding an ability later must not require
    /// editing an existing one; the way to find out is to reserve each ability's slot in the tick
    /// order and its button on the pad now.</para>
    ///
    /// <para>The claim has since been tested for real. Double jump, wall slide, wall jump, ledge
    /// hang, ledge pull-up and ladder climbing all moved out of this file into working modules,
    /// and not one of them required a line of change to <see cref="GroundMove"/>,
    /// <see cref="Jump"/> or <see cref="GravityModule"/>. The two things they did need — new
    /// collision queries and a shared attachment field on the character — are exactly the shared
    /// vocabulary the design says coordination should go through.</para>
    ///
    /// <para>What remains here is genuinely unbuilt. When one is implemented it moves to its own
    /// file, like the others did.</para>
    /// </summary>
    public abstract class PlannedAbility : AbilityModule
    {
        protected PlannedAbility()
        {
            Enabled = false;
        }

        public override void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info)
        {
            // Intentionally empty. Enabling this module without implementing it does nothing,
            // which is the correct failure mode for an unfinished ability.
        }
    }

    /// <summary>Movement while crouched under geometry too low to stand in. Shares the headroom
    /// question with <see cref="Crouch"/> via <c>CharacterSim.HasHeadroomToStand</c>.</summary>
    public sealed class Crawl : PlannedAbility
    {
        public override AbilityId Id => AbilityId.Crawl;
        public override int Order => ModuleOrder.Crawl;
    }

    /// <summary>A short committed step with invulnerability frames. Takes a Reaction-priority
    /// lock, so it can cancel an attack's recovery but not its active frames.</summary>
    public sealed class DodgeStep : PlannedAbility
    {
        public override AbilityId Id => AbilityId.DodgeStep;
        public override int Order => ModuleOrder.DodgeStep;
    }

    /// <summary>Casting a Flame-Art (design bible §6.4). Movement-side only: the commitment lock
    /// and the rooted cast; the spell itself belongs to the combat layer.</summary>
    public sealed class FlameArt : PlannedAbility
    {
        public override AbilityId Id => AbilityId.FlameArt;
        public override int Order => ModuleOrder.FlameArt;
    }
}
