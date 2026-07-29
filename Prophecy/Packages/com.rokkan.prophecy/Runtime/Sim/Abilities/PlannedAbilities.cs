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
    /// order and its button on the pad now, so that implementing it later is genuinely a matter of
    /// filling in one file. If any of them turns out to need a hook inside <see cref="GroundMove"/>
    /// or <see cref="Jump"/>, the boundary was wrong and the fix belongs in the lock arbiter.</para>
    ///
    /// <para>They are also the progression system in miniature: the whole final moveset exists
    /// from the first build, and unlocking an ability is <see cref="AbilityModule.Enabled"/> going
    /// true — not a code change, not a controller swap.</para>
    ///
    /// <para>When one is implemented it moves to its own file. This one holds only declarations.</para>
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

    /// <summary>A second jump in mid-air. Ticks after <see cref="Jump"/> so it sees the ascent
    /// the ground jump started, and re-arms on landing rather than on the coyote window.</summary>
    public sealed class DoubleJump : PlannedAbility
    {
        public override int Order => ModuleOrder.DoubleJump;
    }

    /// <summary>Movement while crouched under geometry too low to stand in. Shares the headroom
    /// question with <see cref="Crouch"/> via <c>CharacterSim.HasHeadroomToStand</c>, which is why
    /// that lives on the sim.</summary>
    public sealed class Crawl : PlannedAbility
    {
        public override int Order => ModuleOrder.Crawl;
    }

    /// <summary>A short committed step with invulnerability frames. Takes a Reaction-priority
    /// lock, so it can cancel an attack's recovery but not its active frames.</summary>
    public sealed class DodgeStep : PlannedAbility
    {
        public override int Order => ModuleOrder.DodgeStep;
    }

    /// <summary>Catching a ledge on the way past it. Needs an edge query the
    /// <c>CollisionWorld</c> does not expose yet.</summary>
    public sealed class LedgeHang : PlannedAbility
    {
        public override int Order => ModuleOrder.LedgeHang;
    }

    /// <summary>Climbing up from a hang. Must check standing headroom at the destination before
    /// committing, or it pulls the character into the ceiling.</summary>
    public sealed class LedgePullUp : PlannedAbility
    {
        public override int Order => ModuleOrder.LedgePullUp;
    }

    /// <summary>Vertical movement on ladders, with gravity suppressed while attached. Suppression
    /// is a velocity the module writes, never a flag reaching into <see cref="GravityModule"/>.</summary>
    public sealed class LadderClimb : PlannedAbility
    {
        public override int Order => ModuleOrder.LadderClimb;
    }

    /// <summary>Casting a Flame-Art (design bible §6.4). Movement-side only: the commitment lock
    /// and the rooted cast; the spell itself belongs to the combat layer.</summary>
    public sealed class FlameArt : PlannedAbility
    {
        public override int Order => ModuleOrder.FlameArt;
    }
}
