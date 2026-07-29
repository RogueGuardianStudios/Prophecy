namespace Rokkan.Prophecy.Sim.Abilities
{
    /// <summary>
    /// Tick order for every ability, in one place.
    ///
    /// <para>Modules never reference each other, so this table is the only statement of who runs
    /// before whom — and ordering is a real determinism concern, since two modules writing the
    /// same velocity component in a different order produce a different result. Keeping the
    /// numbers here rather than as literals on each class means the whole order is arguable at a
    /// glance instead of being reconstructed by opening fifteen files.</para>
    ///
    /// <para><b>Gravity runs first, at zero.</b> That way a module setting a launch velocity later
    /// in the same tick gets that velocity integrated in full, rather than having gravity shaved
    /// off it before it ever applies — a jump reaches the height it was authored to reach.</para>
    ///
    /// <para><b>FallLand runs last</b> because it reports on the tick rather than steering it.</para>
    ///
    /// <para>Gaps are intentional: a new ability slots in without renumbering its neighbours.</para>
    /// </summary>
    public static class ModuleOrder
    {
        public const int Gravity = 0;

        public const int GroundMove = 10;
        public const int TopDownMove = 15;

        public const int Crouch = 20;
        public const int Crawl = 25;

        public const int Jump = 30;

        /// <summary>Ahead of <see cref="DoubleJump"/> deliberately: one press next to a wall
        /// should spend the wall, not an air jump.</summary>
        public const int WallJump = 32;

        public const int DoubleJump = 35;

        /// <summary>After the jumps, so a launch this tick is not immediately clamped by the slide.</summary>
        public const int WallSlide = 36;

        public const int DodgeStep = 40;
        public const int DownThrust = 50;

        public const int LedgeHang = 60;
        public const int LedgePullUp = 65;
        public const int LadderClimb = 70;

        public const int FlameArt = 80;
        public const int Interact = 90;

        public const int FallLand = 100;
    }
}
