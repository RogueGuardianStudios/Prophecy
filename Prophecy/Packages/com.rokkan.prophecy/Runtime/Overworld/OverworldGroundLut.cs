using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The shader half of the biome splat: one texel per cell — R = dominant biome index,
    /// G = secondary index, B = lean toward the secondary, A reserved for the Consumed
    /// darkening. 255 in R means "no biome": the shader's default ground. The bake is pure so
    /// the tests can pin it; the builder wraps the bytes in a point-filtered Texture2D and the
    /// ground shader does its own bilinear blend across texels — indices cannot be filtered,
    /// colours can.
    /// </summary>
    public static class OverworldGroundLut
    {
        public static Color32[] Bake(OverworldTileGrid grid)
        {
            var pixels = new Color32[grid.Width * grid.Height];

            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    grid.BiomeBlendAt(x, z, out int dominant, out int secondary, out float lean);
                    pixels[z * grid.Width + x] = new Color32(
                        dominant < 0 ? (byte)255 : (byte)dominant,
                        secondary < 0 ? (byte)255 : (byte)secondary,
                        (byte)Mathf.Clamp(Mathf.RoundToInt(lean * 255f), 0, 255),
                        0);
                }
            }

            return pixels;
        }
    }
}
