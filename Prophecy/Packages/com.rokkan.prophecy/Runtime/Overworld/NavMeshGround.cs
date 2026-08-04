using Rokkan.Prophecy.Sim;
using UnityEngine;
using UnityEngine.AI;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The NavMesh answering the ground seam: walkable is wherever the baked mesh is, and a step
    /// is legal when the mesh connects its two ends.
    ///
    /// <para><b>One authority for everyone.</b> The same bake that will route AI agents bounds
    /// the player, so the space a wanderer can path through and the space a player can walk can
    /// never disagree — the argument that earned this implementation its place beside
    /// <see cref="OverworldWalkGrid"/>. Step height and slope limits come from the bake's agent
    /// settings rather than a constant here: a 0.4 m step is ground, a 0.7 m terrace is a cliff,
    /// because that is what the bake was told a walker can climb.</para>
    ///
    /// <para><b>The engine dependency is the trade, and it stays behind the seam.</b> These are
    /// play-mode answers from baked engine data — headless tests keep proving the sim against
    /// fakes and the walk grid, exactly as they already do. The sim cannot tell which
    /// implementation is speaking, which is the entire point of the interface.</para>
    /// </summary>
    public sealed class NavMeshGround : ITopDownGround
    {
        /// <summary>How far above the highest terrace the height probe starts.</summary>
        private const float ProbeHeight = 6f;

        /// <summary>Vertical reach of the probe — covers sea level to the tallest authored step.</summary>
        private const float ProbeRange = 10f;

        /// <summary>A sample that lands farther than this from the asked-for XZ is the mesh
        /// nearby, not the mesh HERE — the coast is not walkable from half a metre out at sea.</summary>
        private const float HorizontalTolerance = 0.3f;

        public bool CanStep(Vector2 from, Vector2 to)
        {
            // The FlatWorldCeiling that used to sit here is gone: presentation now rides
            // HeightAt, the bake is slope-limited so skirts stay cliffs, and the authored ramps
            // are the mesh's only way between elevations. The mesh is finally trusted whole.
            if (!TryFindFloor(to, out var toFloor)) return false;

            // Standing off the mesh — teleport, old save, bug — must never trap: any step onto
            // real floor is an escape. Same rule as the walk grid, for the same reason.
            if (!TryFindFloor(from, out var fromFloor)) return true;

            // Connectivity is the step test. A terrace edge exceeds the bake's agent step height,
            // so the mesh simply does not join across it and the raycast reports blocked.
            return !NavMesh.Raycast(fromFloor, toFloor, out _, NavMesh.AllAreas);
        }

        public float HeightAt(Vector2 point) =>
            TryFindFloor(point, out var floor) ? floor.y : 0f;

        /// <summary>
        /// The mesh position under a plane point, if the mesh is genuinely there.
        ///
        /// <para>Sampled from above with a wide vertical reach and a tight horizontal one:
        /// <c>SamplePosition</c> finds the NEAREST mesh point, and without the horizontal check a
        /// point half a metre out to sea would helpfully snap onto the coast and report the sea
        /// walkable.</para>
        /// </summary>
        private static bool TryFindFloor(Vector2 point, out Vector3 floor)
        {
            var probe = new Vector3(point.x, ProbeHeight, point.y);

            if (NavMesh.SamplePosition(probe, out var hit, ProbeRange, NavMesh.AllAreas))
            {
                float dx = hit.position.x - point.x;
                float dz = hit.position.z - point.y;

                if (dx * dx + dz * dz <= HorizontalTolerance * HorizontalTolerance)
                {
                    floor = hit.position;
                    return true;
                }
            }

            floor = default;
            return false;
        }
    }
}
