using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>The map tool's brushes. Semantics live here, in plain C#, so they are tested
    /// headless like everything else that decides what the world IS.</summary>
    public enum PaintBrush
    {
        /// <summary>One level up. Water becomes ground at its own level — raising a flooded
        /// cell drains it before it climbs.</summary>
        Raise,

        /// <summary>One level down. Ground at level 0 becomes sea — digging below the plain
        /// floods it.</summary>
        Lower,

        /// <summary>Flatten to an exact level — the absolute half of the both-brushes answer.</summary>
        SetLevel,

        /// <summary>Water at the cell's CURRENT level: a pool on a terrace keeps the terrace's
        /// waterline, exactly like a carved river reach.</summary>
        Water,

        /// <summary>Road paint on.</summary>
        RoadAdd,

        /// <summary>Road paint off — beats the shape courses.</summary>
        RoadRemove,

        /// <summary>Remove the cell's override entirely; the shapes' answer returns.</summary>
        Clear,
    }

    /// <summary>
    /// What one brush application does to one cell, as data: reads the cell's CURRENT compiled
    /// state, writes the override that realises the brush's intent. Pure — the scene painter
    /// and the minimap both drive it, and the tests pin it without an editor.
    /// </summary>
    public static class OverworldPaintBrushes
    {
        public const int MaxLevel = 4;

        /// <summary>
        /// Apply <paramref name="brush"/> to a cell whose compiled state is
        /// (<paramref name="kind"/>, <paramref name="level"/>). Returns false when the brush
        /// clears the override instead of writing one; otherwise fills the override's terrain
        /// and road intents. Road brushes leave terrain alone and vice versa — merging with an
        /// existing override entry is the caller's job.
        /// </summary>
        public static bool Apply(PaintBrush brush, TileCellKind kind, int level, int paintLevel,
                                 out TerrainOverride terrain, out int terrainLevel,
                                 out RoadOverride road)
        {
            terrain = TerrainOverride.None;
            terrainLevel = 0;
            road = RoadOverride.None;

            switch (brush)
            {
                case PaintBrush.Raise:
                    terrain = TerrainOverride.Ground;
                    terrainLevel = kind == TileCellKind.Sea
                        ? Mathf.Clamp(level, 0, MaxLevel)
                        : Mathf.Clamp(level + 1, 0, MaxLevel);
                    return true;

                case PaintBrush.Lower:
                    if (kind != TileCellKind.Sea && level <= 0)
                    {
                        terrain = TerrainOverride.Sea;
                        terrainLevel = 0;
                    }
                    else if (kind == TileCellKind.Sea)
                    {
                        terrain = TerrainOverride.Sea;
                        terrainLevel = Mathf.Max(0, level - 1);
                    }
                    else
                    {
                        terrain = TerrainOverride.Ground;
                        terrainLevel = level - 1;
                    }
                    return true;

                case PaintBrush.SetLevel:
                    terrain = TerrainOverride.Ground;
                    terrainLevel = Mathf.Clamp(paintLevel, 0, MaxLevel);
                    return true;

                case PaintBrush.Water:
                    terrain = TerrainOverride.Sea;
                    terrainLevel = Mathf.Clamp(level, 0, MaxLevel);
                    return true;

                case PaintBrush.RoadAdd:
                    road = RoadOverride.Add;
                    return true;

                case PaintBrush.RoadRemove:
                    road = RoadOverride.Remove;
                    return true;

                default:
                    return false;   // Clear: the caller removes the entry
            }
        }
    }
}
