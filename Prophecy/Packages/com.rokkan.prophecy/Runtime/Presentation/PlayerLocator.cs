namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Where the player is published for anything that needs their feet — portals, encounter
    /// spawners, the cutaway drivers, the HUD.
    ///
    /// <para>The same seam shape as <c>TopDownGroundSource.Current</c>, for the same reason:
    /// these readers used to resolve the player through <c>SceneDirector.Instance</c>, which
    /// conscripted a scene-flow singleton as a player registry — none of them could run, or be
    /// play-tested in isolation, without Bootstrap's director existing. Who owns scene loading
    /// and where the player's body is are different questions.</para>
    ///
    /// <para>Published by <see cref="PlayerInputCapture"/> — the one component only the body a
    /// human drives carries, which is what keeps an enemy's <c>PlayerCharacterHost</c> from
    /// ever landing here — and re-published by the director for its serialized player, so both
    /// the direct-play and Bootstrap flows arrive at the same answer.</para>
    /// </summary>
    public static class PlayerLocator
    {
        /// <summary>The player's body, or null before one exists.</summary>
        public static PlayerCharacterHost Current { get; private set; }

        public static void Publish(PlayerCharacterHost host)
        {
            if (host != null) Current = host;
        }

        /// <summary>Only the published host may withdraw itself — a stale reference cannot
        /// unregister someone else's player.</summary>
        public static void Withdraw(PlayerCharacterHost host)
        {
            if (Current == host) Current = null;
        }
    }
}
