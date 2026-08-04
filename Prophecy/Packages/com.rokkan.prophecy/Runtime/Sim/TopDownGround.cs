using UnityEngine;

namespace Rokkan.Prophecy.Sim
{
    /// <summary>
    /// What the overworld's ground answers: can a body standing at <c>from</c> put its feet at
    /// <c>to</c>, and how high is the floor there.
    ///
    /// <para><b>The seam exists so the sim never meets the grid.</b> Top-down walkability is
    /// authored on the Stålberg grid, which lives in a package the sim must not reference — it
    /// drags Burst and Collections into every consumer, and the sim's contract is plain C# that
    /// runs headless. So the sim asks this interface, the overworld assembly answers it from the
    /// grid, and a test answers it from a hand-built fake. Same shape as <c>ICombatWorld</c>,
    /// same reason.</para>
    ///
    /// <para><b>CanStep is asked about a move, not a place.</b> Whether a step is legal depends
    /// on both ends — a 0.7 m terrace is a wall from below and a ledge from above — and folding
    /// that into the answer keeps the sim free of any notion of ground height. A sim with no
    /// ground assigned moves freely, which is what every scene before the overworld already
    /// did.</para>
    /// </summary>
    public interface ITopDownGround
    {
        /// <summary>May feet move from <paramref name="from"/> to <paramref name="to"/>? Both are
        /// plane positions (world XZ in top-down).</summary>
        bool CanStep(Vector2 from, Vector2 to);

        /// <summary>Floor elevation at a plane position, or 0 where there is no floor. For
        /// presentation — nothing in the sim reads height.</summary>
        float HeightAt(Vector2 point);
    }
}
