using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The cave-room mask, one byte per cell: region id + 1, zero for "no cave here". The
    /// invert cutout's fullscreen pass reconstructs a pixel's world position from depth,
    /// samples this, and blacks everything whose room is not the active one. Same world-space
    /// LUT pattern as the ground biome LUT — pure bake here, texture upload in the builder.
    /// </summary>
    public static class OverworldCoverLut
    {
        public static byte[] Bake(OverworldTileGrid grid)
        {
            var texels = new byte[grid.Width * grid.Height];
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int region = grid.CoverRegionAt(x, z);
                    texels[z * grid.Width + x] = (byte)(region < 0 ? 0 : region + 1);
                }
            }
            return texels;
        }
    }

    /// <summary>
    /// The invert cutout's one decision, as a plain function: which cave room, if any, the
    /// player is INSIDE. Being inside means standing on the covered floor — the overlay,
    /// below the terrain that roofs it — not walking the roof itself, and not merely having
    /// the room's footprint underfoot from above.
    /// </summary>
    public static class OverworldCoverRules
    {
        /// <summary>Feet must be this far below the roof surface to count as inside — half a
        /// step keeps a body standing ON the roof from triggering the reveal beneath it.</summary>
        public const float RoofClearance = 1f;

        /// <summary>The cave room <paramref name="feet"/> stands inside, or −1.</summary>
        public static int ActiveCaveRegion(OverworldTileGrid grid, Vector3 feet)
        {
            if (grid == null) return -1;
            if (!grid.TryCellAt(new Vector2(feet.x, feet.z), out int x, out int z)) return -1;

            int region = grid.CoverRegionAt(x, z);
            if (region < 0) return -1;

            float roofY = grid.LevelAt(x, z) * OverworldTileGrid.Step;
            return feet.y < roofY - RoofClearance ? region : -1;
        }
    }
}
