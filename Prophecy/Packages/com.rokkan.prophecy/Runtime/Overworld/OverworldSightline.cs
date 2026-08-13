using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// Can the camera actually SEE the player? The halo cutout only earns its hole when the
    /// answer is no — a cylinder that cuts whatever it grazes starts dissolving walls while
    /// the player is still in plain sight (Matt, live). No colliders exist to raycast, so
    /// the line marches the grid analytically, the same way the scene painter picks ground:
    /// terrain mass blocks below its surface, greeble and unwalkable cells carry a tree's
    /// worth of extra mass, and a bridge deck blocks where the line CROSSES its slab —
    /// passing under or over one is clear. Pure, so the tests pin it headless.
    /// </summary>
    public static class OverworldSightline
    {
        private const float StepMetres = 0.5f;
        private const float NearCameraSkip = 1.5f;

        /// <summary>The sightline may be LIBERAL: since the halo's cut is a cone through a
        /// player-sized disc, a false trigger cuts nothing visible — nothing screens the
        /// body. Firing beside a wall is harmless; NOT firing behind one hid the player
        /// entirely (the hugging exemption this replaces did exactly that).</summary>
        private const float NearTargetSkip = 0.6f;

        /// <summary>Height of the standing mass on greeble/unwalkable cells — a gray-box
        /// tree to the TOP of its canopy, not its trunk. The steep top-down ray clears a
        /// trunk-height mass almost everywhere, which left the player swallowed by the
        /// grove's canopies with no halo at all.</summary>
        private const float ThicketMass = 2.5f;

        private const float DeckThickness = 0.35f;

        public static bool Blocked(OverworldTileGrid grid, Vector3 from, Vector3 to)
        {
            if (grid == null) return false;

            var span = to - from;
            float length = span.magnitude;
            if (length < NearCameraSkip + NearTargetSkip) return false;
            var direction = span / length;

            var previous = from + direction * NearCameraSkip;
            for (float t = NearCameraSkip + StepMetres;
                 t < length - NearTargetSkip;
                 t += StepMetres)
            {
                var sample = from + direction * t;

                if (grid.TryCellAt(new Vector2(sample.x, sample.z), out int x, out int z))
                {
                    float terrainTop = grid.LevelAt(x, z) * OverworldTileGrid.Step;

                    // Inside the terrain mass itself.
                    if (sample.y < terrainTop - 0.05f) return true;

                    // Inside a grove's or a blocked cell's standing mass.
                    if (sample.y < terrainTop + ThicketMass &&
                        (grid.GreebleAt(x, z) || grid.UnwalkableAt(x, z)))
                        return true;

                    // Crossing a deck slab: only a bridge-style overlay (above the terrain)
                    // is a lid; a cave floor below it is not. Segment straddle, not point
                    // proximity — the ray from a high camera drops fast enough to step
                    // clean over a thin slab.
                    if (grid.TryCoverAt(x, z, out bool isCave) && !isCave &&
                        grid.TryOverlayAt(x, z, out int deckLevel))
                    {
                        float deckTop = deckLevel * OverworldTileGrid.Step + DeckThickness;
                        float deckBottom = deckLevel * OverworldTileGrid.Step - 0.05f;
                        if (previous.y >= deckBottom && sample.y <= deckTop)
                            return true;
                    }
                }

                previous = sample;
            }

            return false;
        }
    }
}
