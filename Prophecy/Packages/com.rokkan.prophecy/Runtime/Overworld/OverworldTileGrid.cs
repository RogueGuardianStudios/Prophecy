using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>What a tile cell is. Sea is the default everywhere the map doesn't say otherwise.</summary>
    public enum TileCellKind : byte
    {
        Sea,
        Ground,
        Ramp,
    }

    /// <summary>A ramp cell's ascending direction: walking this way climbs level N to N+1.</summary>
    public enum RampFacing : byte
    {
        PlusZ,
        PlusX,
        MinusZ,
        MinusX,
    }

    /// <summary>
    /// The overworld as a discrete tile grid: per cell an integer elevation, and a kind — Ground,
    /// Ramp with a facing, or Sea. This is the 3D tile structure decision made data: connectivity
    /// is a stated fact of these cells, not an emergent property of mesh geometry, so an authoring
    /// edit can never silently re-roll which terraces are reachable.
    ///
    /// <para>Cliffs do not appear here at all. Two neighbours differing in level IS the cliff;
    /// the renderer draws faces there and walkability refuses there, both reading the same cells,
    /// so they cannot disagree.</para>
    ///
    /// <para>Plain C#: the sim's ground seam and a headless test consume this directly.</para>
    /// </summary>
    public sealed class OverworldTileGrid
    {
        /// <summary>One tile is one metre. The ALttP lattice constant, and the tile prefabs'
        /// authored footprint.</summary>
        public const float CellSize = 1f;

        /// <summary>One level step in metres — the terrace height the whole overworld is built
        /// on. Tuned by walking it: 0.7 read as a kerb, 3 overshot into fortress walls, and 2 —
        /// just over the 1.8 m player — is where a terrace reads as a wall without dwarfing the
        /// map (Matt, 2026-08-05).</summary>
        public const float Step = 2f;

        /// <summary>Cells one climb spans (Matt, same evening: one cell per 3 m step was a
        /// ladder). A ramp is a RUN of this many consecutive cells, each rising Step/RampRun
        /// along the shared facing; the cell's <see cref="RampIndexAt"/> is its position in the
        /// run, 0 at the foot.</summary>
        public const int RampRun = 2;

        /// <summary>How many of the run's top cells are RECESSED into the high terrace (Matt,
        /// 2026-08-05: one tile at the bottom, one notched into the wall at the top — the ALttP
        /// inset stair). The compiler carves these out of level N+1 ground.</summary>
        public const int RampRecess = RampRun / 2;

        private readonly int _width;
        private readonly int _height;
        private readonly Vector2 _origin;
        private readonly TileCellKind[] _kinds;
        private readonly sbyte[] _levels;
        private readonly RampFacing[] _facings;
        private readonly byte[] _rampIndices;

        public OverworldTileGrid(int width, int height, Vector2 originXZ)
        {
            _width = Mathf.Max(1, width);
            _height = Mathf.Max(1, height);
            _origin = originXZ;
            _kinds = new TileCellKind[_width * _height];
            _levels = new sbyte[_width * _height];
            _facings = new RampFacing[_width * _height];
            _rampIndices = new byte[_width * _height];
        }

        public int Width => _width;
        public int Height => _height;

        /// <summary>World XZ of cell (0, 0)'s minimum corner.</summary>
        public Vector2 Origin => _origin;

        public bool InBounds(int x, int z) => x >= 0 && x < _width && z >= 0 && z < _height;

        /// <summary>Out of bounds is sea — the island always ends before the grid does.</summary>
        public TileCellKind KindAt(int x, int z) =>
            InBounds(x, z) ? _kinds[z * _width + x] : TileCellKind.Sea;

        /// <summary>Sea answers level 0 — the level cliffs into the water are measured against.</summary>
        public int LevelAt(int x, int z) => InBounds(x, z) ? _levels[z * _width + x] : 0;

        public RampFacing FacingAt(int x, int z) =>
            InBounds(x, z) ? _facings[z * _width + x] : RampFacing.PlusZ;

        /// <summary>Position of a ramp cell within its run: 0 at the foot, RampRun−1 meeting the
        /// high terrace. Meaningless for other kinds.</summary>
        public int RampIndexAt(int x, int z) =>
            InBounds(x, z) ? _rampIndices[z * _width + x] : 0;

        public void Set(int x, int z, TileCellKind kind, int level,
                        RampFacing facing = RampFacing.PlusZ, int rampIndex = 0)
        {
            if (!InBounds(x, z)) return;
            int i = z * _width + x;
            _kinds[i] = kind;
            _levels[i] = (sbyte)level;
            _facings[i] = facing;
            _rampIndices[i] = (byte)Mathf.Clamp(rampIndex, 0, RampRun - 1);
        }

        public bool TryCellAt(Vector2 worldXZ, out int x, out int z)
        {
            x = Mathf.FloorToInt((worldXZ.x - _origin.x) / CellSize);
            z = Mathf.FloorToInt((worldXZ.y - _origin.y) / CellSize);
            return InBounds(x, z);
        }

        /// <summary>World XZ of a cell's centre.</summary>
        public Vector2 CellCentre(int x, int z) =>
            new Vector2(_origin.x + (x + 0.5f) * CellSize, _origin.y + (z + 0.5f) * CellSize);

        /// <summary>World XZ of the grid corner shared by cells (x−1,z−1)…(x,z).</summary>
        public Vector2 CornerPoint(int x, int z) =>
            new Vector2(_origin.x + x * CellSize, _origin.y + z * CellSize);

        /// <summary>
        /// The walk surface height at a world point inside cell (x, z). Ground is flat at
        /// level × Step; a ramp interpolates one step along its facing; sea answers 0.
        /// </summary>
        public float SurfaceHeight(int x, int z, Vector2 worldXZ)
        {
            switch (KindAt(x, z))
            {
                case TileCellKind.Ground:
                    return LevelAt(x, z) * Step;

                case TileCellKind.Ramp:
                {
                    float t;
                    switch (FacingAt(x, z))
                    {
                        case RampFacing.PlusZ: t = (worldXZ.y - _origin.y) / CellSize - z; break;
                        case RampFacing.MinusZ: t = 1f - ((worldXZ.y - _origin.y) / CellSize - z); break;
                        case RampFacing.PlusX: t = (worldXZ.x - _origin.x) / CellSize - x; break;
                        default: t = 1f - ((worldXZ.x - _origin.x) / CellSize - x); break;
                    }
                    float run = (RampIndexAt(x, z) + Mathf.Clamp01(t)) / RampRun;
                    return (LevelAt(x, z) + run) * Step;
                }

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// The surface level at one of a cell's four corners — <paramref name="cornerDx"/> and
        /// <paramref name="cornerDz"/> are 0 or 1, in whole levels for the corner band rule. A
        /// ramp reaches level+1 only at the FINAL run cell's high-edge corners; every other run
        /// corner rounds down to its base level, so mid-run corners never invent cliff bands.
        /// </summary>
        public int CornerLevelOf(int x, int z, int cornerDx, int cornerDz)
        {
            if (KindAt(x, z) != TileCellKind.Ramp) return LevelAt(x, z);

            bool high;
            switch (FacingAt(x, z))
            {
                case RampFacing.PlusZ: high = cornerDz == 1; break;
                case RampFacing.MinusZ: high = cornerDz == 0; break;
                case RampFacing.PlusX: high = cornerDx == 1; break;
                default: high = cornerDx == 0; break;
            }

            bool tops = high && RampIndexAt(x, z) == RampRun - 1;
            return LevelAt(x, z) + (tops ? 1 : 0);
        }
    }

    /// <summary>
    /// Compiles the hand-authored <see cref="OverworldMap"/> into the tile grid: regions quantize
    /// to integer levels, and authored ramps become Ramp cells at exactly the terrace boundaries
    /// they span. The authoring surface survives unchanged — only the compiler behind it is new.
    /// </summary>
    public static class OverworldTileGridCompiler
    {
        /// <summary>How far an authored Y may sit from a level multiple before it earns a warning
        /// (it quantizes either way — the warning is for the author, not the compiler).</summary>
        private const float QuantizeTolerance = 0.05f;

        public static OverworldTileGrid Compile(OverworldMap map, Vector3 worldOffset)
        {
            int width = Mathf.Max(1, Mathf.RoundToInt(map.BoundsSize.x / OverworldTileGrid.CellSize));
            int height = Mathf.Max(1, Mathf.RoundToInt(map.BoundsSize.y / OverworldTileGrid.CellSize));
            var origin = new Vector2(worldOffset.x - map.BoundsSize.x * 0.5f,
                                     worldOffset.z - map.BoundsSize.y * 0.5f);

            var grid = new OverworldTileGrid(width, height, origin);
            var offset = new Vector2(worldOffset.x, worldOffset.z);

            RasterRegions(grid, map, offset);
            ConvertRamps(grid, map, offset);

            return grid;
        }

        /// <summary>
        /// A cell is land iff its centre lies inside an authored footprint; later regions win
        /// overlaps, which is the deterministic reading of "terraces are authored on top of the
        /// heartland". Rotated footprints rasterize into staircase coasts — correctly Zelda.
        /// </summary>
        private static void RasterRegions(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var regions = map.Regions;
            if (regions == null || regions.Length == 0) return;

            for (int i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                int level = QuantizeLevel(region.Y, region.Name);

                var centre = region.Centre + offset;
                float rad = -region.RotationDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                var half = region.Size * 0.5f;

                for (int z = 0; z < grid.Height; z++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var p = grid.CellCentre(x, z) - centre;
                        float localX = p.x * cos - p.y * sin;
                        float localZ = p.x * sin + p.y * cos;

                        if (Mathf.Abs(localX) <= half.x && Mathf.Abs(localZ) <= half.y)
                            grid.Set(x, z, TileCellKind.Ground, level);
                    }
                }
            }
        }

        /// <summary>
        /// An authored ramp names a strip; the compiler finds every one-step terrace boundary
        /// inside it and converts the LOW cell at the boundary into a Ramp cell facing uphill —
        /// so a compiled ramp is always exactly where two terraces actually meet, whatever the
        /// authored segment's length. A strip containing no such boundary compiles to nothing
        /// and says so: an inert ramp is an authoring mistake, not a silent success.
        /// </summary>
        private static void ConvertRamps(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var ramps = map.Ramps;

            for (int i = 0; ramps != null && i < ramps.Length; i++)
            {
                var ramp = ramps[i];
                var start = ramp.Start + offset;
                var end = ramp.End + offset;

                var along = end - start;
                if (ramp.EndY < ramp.StartY) { var t = start; start = end; end = t; along = -along; }

                // The facing is the dominant axis of the climb — the lattice has no diagonals.
                RampFacing facing;
                Vector2Int dir;
                if (Mathf.Abs(along.x) >= Mathf.Abs(along.y))
                {
                    facing = along.x >= 0f ? RampFacing.PlusX : RampFacing.MinusX;
                    dir = along.x >= 0f ? Vector2Int.right : Vector2Int.left;
                }
                else
                {
                    facing = along.y >= 0f ? RampFacing.PlusZ : RampFacing.MinusZ;
                    dir = along.y >= 0f ? Vector2Int.up : Vector2Int.down;
                }

                int converted = 0;
                float length = along.magnitude;
                var alongDir = length > 0.0001f ? along / length : Vector2.right;

                bool InStrip(int cx, int cz)
                {
                    var toCell = grid.CellCentre(cx, cz) - start;
                    float project = Vector2.Dot(toCell, alongDir);
                    if (project < -OverworldTileGrid.CellSize ||
                        project > length + OverworldTileGrid.CellSize) return false;
                    float aside = Mathf.Abs(toCell.x * alongDir.y - toCell.y * alongDir.x);
                    return aside <= ramp.HalfWidth;
                }

                for (int z = 0; z < grid.Height; z++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (grid.KindAt(x, z) != TileCellKind.Ground) continue;
                        if (!InStrip(x, z)) continue;

                        int nx = x + dir.x;
                        int nz = z + dir.y;
                        if (grid.KindAt(nx, nz) != TileCellKind.Ground) continue;

                        int level = grid.LevelAt(x, z);
                        if (grid.LevelAt(nx, nz) != level + 1) continue;

                        // The run is RECESSED: its top RampRecess cells are carved OUT of the
                        // high terrace and the rest stand on the low one — one at the bottom,
                        // one notched into the wall for a 2-cell run. (x, z) is the low cell at
                        // the boundary; run cell i sits at offset i − (RampRun − RampRecess − 1)
                        // along the facing. The head must land on high ground beyond the notch.
                        int k = OverworldTileGrid.RampRun;
                        int r = OverworldTileGrid.RampRecess;

                        bool fits = true;
                        for (int ri = 0; ri < k && fits; ri++)
                        {
                            int off = ri - (k - r - 1);
                            int px = x + dir.x * off;
                            int pz = z + dir.y * off;
                            int want = ri < k - r ? level : level + 1;
                            fits = grid.KindAt(px, pz) == TileCellKind.Ground &&
                                   grid.LevelAt(px, pz) == want && InStrip(px, pz);
                        }

                        int hx = x + dir.x * (r + 1);
                        int hz = z + dir.y * (r + 1);
                        fits = fits && grid.KindAt(hx, hz) == TileCellKind.Ground &&
                               grid.LevelAt(hx, hz) == level + 1;
                        if (!fits) continue;

                        for (int ri = 0; ri < k; ri++)
                        {
                            int off = ri - (k - r - 1);
                            grid.Set(x + dir.x * off, z + dir.y * off,
                                     TileCellKind.Ramp, level, facing, ri);
                        }
                        converted++;
                    }
                }

                if (converted == 0)
                    Debug.LogWarning($"[Prophecy] Ramp '{ramp.Name}' converted no cells — its " +
                                     "strip contains no one-step boundary with room for the " +
                                     $"run ({OverworldTileGrid.RampRun - OverworldTileGrid.RampRecess} " +
                                     $"low cells, {OverworldTileGrid.RampRecess} notched into the " +
                                     "high terrace, and high ground beyond). It climbs nothing.");
            }
        }

        private static int QuantizeLevel(float y, string regionName)
        {
            int level = Mathf.Max(0, Mathf.RoundToInt(y / OverworldTileGrid.Step));

            if (Mathf.Abs(y - level * OverworldTileGrid.Step) > QuantizeTolerance)
                Debug.LogWarning($"[Prophecy] Region '{regionName}' Y={y:0.00} is not a multiple " +
                                 $"of the {OverworldTileGrid.Step} m step — quantized to level " +
                                 $"{level} ({level * OverworldTileGrid.Step:0.00} m).");

            return level;
        }
    }
}
