using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// Converts between the sim's 2D movement plane and 3D world space.
    ///
    /// <para>The sim thinks in a <c>Vector2</c> because gameplay is axis-constrained; the world is
    /// cel-shaded 3D. Which two world axes that plane maps onto depends on the play mode —
    /// side-scroll runs on XY with Z railed, the top-down overworld runs on XZ with Y as height.
    /// Everything that crosses the boundary (the collision bake, the character view, the camera)
    /// needs the same answer, so the mapping lives in exactly one place.</para>
    ///
    /// <para>Getting this wrong is invisible until it isn't: a baker projecting onto the wrong pair
    /// of axes produces a collision world that looks plausible and collides with nothing.</para>
    /// </summary>
    public static class SpaceMapping
    {
        /// <summary>Project a world position onto the sim plane.</summary>
        public static Vector2 ToPlane(Vector3 world, MovementSpace space) =>
            space == MovementSpace.TopDown
                ? new Vector2(world.x, world.z)
                : new Vector2(world.x, world.y);

        /// <summary>Lift a sim-plane position back into the world. <paramref name="depth"/> fills
        /// the railed axis — Z in side-scroll, Y in top-down.</summary>
        public static Vector3 ToWorld(Vector2 plane, MovementSpace space, float depth = 0f) =>
            space == MovementSpace.TopDown
                ? new Vector3(plane.x, depth, plane.y)
                : new Vector3(plane.x, plane.y, depth);

        /// <summary>The world axis the sim treats as "up". Meaningless in top-down, where there
        /// is no gravity, but defined so callers do not have to special-case it.</summary>
        public static Vector3 UpAxis(MovementSpace space) =>
            space == MovementSpace.TopDown ? Vector3.forward : Vector3.up;
    }
}
