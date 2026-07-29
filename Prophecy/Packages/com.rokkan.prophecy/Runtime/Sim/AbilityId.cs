namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// A stable name for each ability, so something outside the sim can talk about one without
    /// holding a reference to it.
    ///
    /// <para>This is what makes progression expressible. The design already says unlocking an
    /// ability is <see cref="AbilityModule.Enabled"/> going true rather than a code change — but
    /// that only helps if a save file, a designer, or a debug menu can name the ability being
    /// unlocked. A C# type is not a thing you can serialise into a save; an enum value is.</para>
    ///
    /// <para>Values are never renumbered or reused. A saved game stores these, and shuffling them
    /// would silently grant the wrong abilities to every existing save.</para>
    /// </summary>
    public enum AbilityId
    {
        /// <summary>Not a real ability — the default for test doubles and anything that forgot to
        /// declare one. Never assign it to a shipped module.</summary>
        Custom = 0,

        Gravity = 1,
        GroundMove = 2,
        TopDownMove = 3,
        Crouch = 4,
        Crawl = 5,
        Jump = 6,
        DoubleJump = 7,
        WallSlide = 8,
        WallJump = 9,
        DodgeStep = 10,
        DownThrust = 11,
        LedgeHang = 12,
        LedgePullUp = 13,
        LadderClimb = 14,
        FlameArt = 15,
        Interact = 16,
        FallLand = 17,
    }
}
