using RGS.Core.Sim;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// One ability, as a plain C# system ticked by <see cref="CharacterSim"/>.
    ///
    /// <para><b>The rule: a module never references another module.</b> Everything it needs — the
    /// character's state, whether an action is currently permitted, the collision world — comes
    /// from the <see cref="CharacterSim"/> passed to <see cref="Tick"/>. That is what makes
    /// progression cheap: the whole final moveset exists from the start, and unlocking an ability
    /// is <see cref="Enabled"/> going true, not a code change.</para>
    ///
    /// <para>It is also the architecture's own test. If adding a new module requires editing an
    /// existing one, the boundary has been broken and the fix belongs in the lock arbiter, not
    /// in a cross-reference.</para>
    /// </summary>
    public abstract class AbilityModule
    {
        /// <summary>
        /// Whether this module ticks. Progression is a schedule of flips to this flag, authored in
        /// an <see cref="AbilityLoadoutData"/> rather than set here by hand.
        /// </summary>
        public bool Enabled = true;

        /// <summary>
        /// Stable identity, for anything outside the sim that needs to name this ability — a
        /// loadout asset, a save file, a debug toggle. Overridden by every shipped module;
        /// <see cref="AbilityId.Custom"/> is the default only so test doubles need not care.
        /// </summary>
        public virtual AbilityId Id => AbilityId.Custom;

        /// <summary>
        /// Tick order within a character, low to high. Stable ordering is part of determinism —
        /// two modules writing the same velocity component in a different order would produce a
        /// different result, so this must never depend on registration accident.
        /// </summary>
        public abstract int Order { get; }

        /// <summary>Which play modes this module applies to. The sim skips it in any other.</summary>
        public virtual MovementSpace ValidIn => MovementSpace.SideScroll;

        /// <summary>Human-readable name for debug overlays and test failure messages.</summary>
        public virtual string Name => GetType().Name;

        /// <summary>
        /// Advance this ability by one fixed tick. Read <paramref name="sim"/>.State, consult
        /// <c>sim.Can(...)</c> before acting, and write to velocity or request a lock. Must not
        /// touch any engine scene type.
        /// </summary>
        public abstract void Tick(CharacterSim sim, in InputFrame input, in SimTickInfo info);

        /// <summary>Called when the sim resets the character (respawn, scene change) so a module
        /// holding internal timers can clear them.</summary>
        public virtual void Reset() { }
    }
}
