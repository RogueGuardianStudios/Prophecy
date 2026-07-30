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

        /// <summary>
        /// <b>First of the input-driven modules, ahead of the attack that reads its result.</b>
        /// Stance is what chooses between the high and the low attack, so a stance settled after
        /// the attack was picked would hand the player a chest-height slash on the tick they
        /// pressed down and attack together — the one input where getting it wrong is most
        /// obvious. It depends on nothing that runs before it, only on grounded state, which the
        /// sim refreshes at the top of every tick.
        /// </summary>
        public const int Crouch = 2;

        /// <summary>
        /// <b>Ahead of everything voluntary.</b> A hit-react takes the character away from whatever
        /// they were doing, and running after the attack module would let an interrupted swing
        /// resolve one more tick of hits out of a body that had already lost control.
        /// </summary>
        public const int HitReact = 3;

        /// <summary>
        /// <b>Between stance and locomotion, and that placement is the whole point.</b> After
        /// <see cref="Crouch"/> so a swing is chosen from this tick's stance rather than last
        /// tick's; before <see cref="GroundMove"/> and the jumps so that the tick an attack
        /// releases its lock is a tick the character can already move on. Running after them
        /// instead would hand control back one tick late, which at 60 Hz is the difference between
        /// a swing that ends and one that feels sticky.
        /// </summary>
        public const int Attack = 5;

        /// <summary>
        /// <b>Before <see cref="Block"/>, which is the whole reason for the number.</b> A parry
        /// raised out of a guard takes the lock first, so the guard sees it lost on the same tick
        /// and lowers immediately. The other way round leaves both flags true for a tick — the
        /// gate chain would still resolve it correctly, but a shield that is briefly up and
        /// down at once is exactly the sort of thing that is true until it is load-bearing.
        /// </summary>
        public const int Parry = 6;

        /// <summary>After <see cref="Crouch"/> for the same reason the attack is: stance decides
        /// whether the guard answers high or low, so it has to be settled first.</summary>
        public const int Block = 7;

        public const int GroundMove = 10;
        public const int TopDownMove = 15;

        /// <summary>After <see cref="Crouch"/>: dropping through a platform is what holding down
        /// escalates to, so it needs to see the stance that holding down just produced.</summary>
        public const int DropThrough = 22;

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
