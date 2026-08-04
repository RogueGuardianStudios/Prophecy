using Rokkan.Prophecy.Sim;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Where the scene's ground oracle is published, and where the hosts find it.
    ///
    /// <para>The overworld's grid host cannot be referenced from here — it lives in the assembly
    /// that knows the worldgen package, which depends on this one, not the reverse. So the ground
    /// arrives the way the fight does: whoever builds it publishes here, every character host
    /// re-points its sim each tick, and a scene without ground publishes nothing. Same seam
    /// pattern as <c>CombatDirector.Instance</c>, one layer simpler because ground has no tick of
    /// its own.</para>
    /// </summary>
    public static class TopDownGroundSource
    {
        /// <summary>The loaded scene's ground, or null. Set by whoever built it; cleared by the
        /// same object on destroy.</summary>
        public static ITopDownGround Current;
    }
}
