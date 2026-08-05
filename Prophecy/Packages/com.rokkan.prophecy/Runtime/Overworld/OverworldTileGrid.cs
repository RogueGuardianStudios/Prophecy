using System.Collections.Generic;
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

        // A cell's optional SECOND walkable surface — a bridge deck above the base terrain, or
        // a cave floor beneath it (the base then reads as the roof). Sparse because overlaps
        // are rare; one extra surface per cell is the deliberate cap of this model. Flat Ground
        // only — overlay ramps are a future decision, made with the feature that needs them.
        private readonly Dictionary<int, sbyte> _overlays = new Dictionary<int, sbyte>();

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

        /// <summary>The cell's overlay surface — bridge deck or cave floor — if it has one.</summary>
        public bool TryOverlayAt(int x, int z, out int level)
        {
            if (InBounds(x, z) && _overlays.TryGetValue(z * _width + x, out sbyte stored))
            {
                level = stored;
                return true;
            }

            level = 0;
            return false;
        }

        public void SetOverlay(int x, int z, int level)
        {
            if (InBounds(x, z)) _overlays[z * _width + x] = (sbyte)level;
        }

        // Road cells are paint: the host draws a strip over their walk surface and nothing else
        // reads them. Sparse for the same reason overlays are.
        private readonly HashSet<int> _roads = new HashSet<int>();

        /// <summary>Whether this cell carries road paint on its walk surface.</summary>
        public bool RoadAt(int x, int z) => InBounds(x, z) && _roads.Contains(z * _width + x);

        public void SetRoad(int x, int z)
        {
            if (InBounds(x, z)) _roads.Add(z * _width + x);
        }

        public void ClearRoad(int x, int z)
        {
            if (InBounds(x, z)) _roads.Remove(z * _width + x);
        }

        // Cells a blocking prop stands on. Walkability treats them as having no surface at
        // all, which keeps the escape rule: a body somehow stranded INSIDE a prop's footprint
        // may always step out, it just can't step in.
        private readonly HashSet<int> _blocked = new HashSet<int>();

        /// <summary>Whether a blocking prop occupies this cell.</summary>
        public bool BlockedAt(int x, int z) => InBounds(x, z) && _blocked.Contains(z * _width + x);

        public void SetBlocked(int x, int z)
        {
            if (InBounds(x, z)) _blocked.Add(z * _width + x);
        }

        public void ClearBlocked(int x, int z)
        {
            if (InBounds(x, z)) _blocked.Remove(z * _width + x);
        }

        // Thicket cells: blocked like a prop footprint AND scatter-filled by the biome. The
        // separate set exists because the scatter needs to know which blocked cells are
        // thicket (fill with trees) versus prop footprints (already filled by the prop).
        private readonly HashSet<int> _thicket = new HashSet<int>();

        /// <summary>Whether this cell is an impassable scatter-filled mass.</summary>
        public bool ThicketAt(int x, int z) => InBounds(x, z) && _thicket.Contains(z * _width + x);

        public void SetThicket(int x, int z)
        {
            if (!InBounds(x, z)) return;
            _thicket.Add(z * _width + x);
            _blocked.Add(z * _width + x);
        }

        public void ClearThicket(int x, int z)
        {
            if (!InBounds(x, z)) return;
            _thicket.Remove(z * _width + x);
            _blocked.Remove(z * _width + x);
        }

        // The province stamp: which named place's RULES govern each cell. Stored as an index
        // into the compiled list so the cells stay bytes; 255 = wilderness (no province).
        private readonly List<OverworldProvince> _provinceList = new List<OverworldProvince>();
        private byte[] _provinceCells;

        /// <summary>The province governing this cell, or null for wilderness.</summary>
        public OverworldProvince ProvinceAt(int x, int z)
        {
            if (_provinceCells == null || !InBounds(x, z)) return null;
            byte index = _provinceCells[z * _width + x];
            return index == 255 ? null : _provinceList[index];
        }

        public void SetProvince(int x, int z, OverworldProvince province)
        {
            if (!InBounds(x, z) || province == null) return;
            if (_provinceCells == null)
            {
                _provinceCells = new byte[_width * _height];
                for (int i = 0; i < _provinceCells.Length; i++) _provinceCells[i] = 255;
            }

            int index = _provinceList.IndexOf(province);
            if (index < 0)
            {
                if (_provinceList.Count >= 255) return;
                index = _provinceList.Count;
                _provinceList.Add(province);
            }

            _provinceCells[z * _width + x] = (byte)index;
        }

        // The biome splat, resolved: per cell a dominant biome, a secondary, and how far the
        // blend leans toward the secondary. Geometry reads only the dominant (discrete,
        // deterministic); the terrain shader reads the blend. 255 = no biome — the gray-box
        // fallback everywhere until influence is authored.
        public const byte NoBiome = 255;

        private byte[] _biomeA;
        private byte[] _biomeB;
        private byte[] _biomeBlend;

        /// <summary>The cell's dominant biome, or −1 where none has influence.</summary>
        public int DominantBiomeAt(int x, int z)
        {
            if (_biomeA == null || !InBounds(x, z)) return -1;
            byte a = _biomeA[z * _width + x];
            return a == NoBiome ? -1 : a;
        }

        /// <summary>The full blend at a cell: dominant, secondary (−1 when pure), and the lean
        /// toward the secondary in 0..1. For the shader LUT bake.</summary>
        public void BiomeBlendAt(int x, int z, out int dominant, out int secondary, out float lean)
        {
            dominant = DominantBiomeAt(x, z);
            secondary = -1;
            lean = 0f;
            if (dominant < 0 || _biomeB == null) return;

            int i = z * _width + x;
            if (_biomeB[i] != NoBiome)
            {
                secondary = _biomeB[i];
                lean = _biomeBlend[i] / 255f;
            }
        }

        public void SetBiome(int x, int z, int dominant, int secondary, float lean)
        {
            if (!InBounds(x, z)) return;
            if (_biomeA == null)
            {
                _biomeA = new byte[_width * _height];
                _biomeB = new byte[_width * _height];
                for (int i = 0; i < _biomeA.Length; i++)
                {
                    _biomeA[i] = NoBiome;
                    _biomeB[i] = NoBiome;
                }
                _biomeBlend = new byte[_width * _height];
            }

            int idx = z * _width + x;
            _biomeA[idx] = dominant < 0 ? NoBiome : (byte)dominant;
            _biomeB[idx] = secondary < 0 ? NoBiome : (byte)secondary;
            _biomeBlend[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(lean * 255f), 0, 255);
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

        /// <summary>The authoring audits from the LAST Compile — inert ramps, spilling water,
        /// refused road paint, off-grid Ys. The map tool reads these into its own UI.</summary>
        public static readonly List<string> Notes = new List<string>();

        /// <summary>Whether audits also go to the console. True for scene loads, where the
        /// console is the only surface; the map tool compiles on every stroke and turns this
        /// off — a repeated audit is feedback once and noise forever after.</summary>
        public static bool LogToConsole = true;

        private static void Note(string message)
        {
            Notes.Add(message);
            if (LogToConsole) Debug.LogWarning(message);
        }

        public static OverworldTileGrid Compile(OverworldMap map, Vector3 worldOffset)
        {
            Notes.Clear();
            int width = Mathf.Max(1, Mathf.RoundToInt(map.BoundsSize.x / OverworldTileGrid.CellSize));
            int height = Mathf.Max(1, Mathf.RoundToInt(map.BoundsSize.y / OverworldTileGrid.CellSize));
            var origin = new Vector2(worldOffset.x - map.BoundsSize.x * 0.5f,
                                     worldOffset.z - map.BoundsSize.y * 0.5f);

            var grid = new OverworldTileGrid(width, height, origin);
            var offset = new Vector2(worldOffset.x, worldOffset.z);

            RasterRegions(grid, map, offset);
            RasterRivers(grid, map, offset);
            ApplyTerrainOverrides(grid, map);
            WarnWhereWaterSpills(grid);
            ConvertRamps(grid, map, offset);
            RasterLayers(grid, map, offset);
            RasterRoads(grid, map, offset);
            ApplyRoadOverrides(grid, map);
            RasterBiomes(grid, map, offset);
            RasterThickets(grid, map, offset);
            RasterProvinces(grid, map, offset);
            RasterProps(grid, map, offset);
            ApplyBlockOverrides(grid, map);

            return grid;
        }

        /// <summary>
        /// Painted walkability, applied dead LAST: Add blocks a cell bare (no scatter — bare
        /// unwalkable ground); Remove unblocks ANYTHING, including thicket masses and prop
        /// footprints — the hand's last word over every other source of blocking.
        /// </summary>
        private static void ApplyBlockOverrides(OverworldTileGrid grid, OverworldMap map)
        {
            var overrides = map.CellOverrides;

            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var cell = overrides[i];
                if (cell.Block == BlockOverride.Add)
                {
                    grid.SetBlocked(cell.X, cell.Z);
                }
                else if (cell.Block == BlockOverride.Remove)
                {
                    grid.ClearThicket(cell.X, cell.Z);   // also clears its blocked flag
                    grid.ClearBlocked(cell.X, cell.Z);
                }
            }
        }

        /// <summary>
        /// Provinces are the named shapes themselves — a shape with a Province reference
        /// stamps its cells with those rules, in raster order (regions, then biome areas by
        /// their HARD rect, then thickets), later wins. No separate bounds ever exist to
        /// drift from the places they describe.
        /// </summary>
        private static void RasterProvinces(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            void StampRect(Vector2 centre, Vector2 size, float rotationDegrees,
                           OverworldProvince province)
            {
                if (province == null) return;
                var worldCentre = centre + offset;
                float rad = -rotationDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                var half = size * 0.5f;

                for (int z = 0; z < grid.Height; z++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var p = grid.CellCentre(x, z) - worldCentre;
                        float localX = p.x * cos - p.y * sin;
                        float localZ = p.x * sin + p.y * cos;
                        if (Mathf.Abs(localX) <= half.x && Mathf.Abs(localZ) <= half.y)
                            grid.SetProvince(x, z, province);
                    }
                }
            }

            var regions = map.Regions;
            for (int i = 0; regions != null && i < regions.Length; i++)
                StampRect(regions[i].Centre, regions[i].Size, regions[i].RotationDegrees,
                          regions[i].Province);

            var areas = map.BiomeAreas;
            for (int i = 0; areas != null && i < areas.Length; i++)
                StampRect(areas[i].Centre, areas[i].Size, areas[i].RotationDegrees,
                          areas[i].Province);

            var thickets = map.Thickets;
            for (int i = 0; thickets != null && i < thickets.Length; i++)
                StampRect(thickets[i].Centre, thickets[i].Size, thickets[i].RotationDegrees,
                          thickets[i].Province);
        }

        /// <summary>
        /// Impassable scatter-filled masses: shape rects plant them on flat GROUND cells only
        /// (a thicket never grows on water, a slope, or a road — the road wins, it is the
        /// carved path through the forest), and the painted overrides add or carve per cell.
        /// Thicket blocks exactly like a prop footprint — the ground seam refuses, the escape
        /// rule frees a stranded body.
        /// </summary>
        private static void RasterThickets(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var thickets = map.Thickets;

            for (int i = 0; thickets != null && i < thickets.Length; i++)
            {
                var thicket = thickets[i];
                var centre = thicket.Centre + offset;
                float rad = -thicket.RotationDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                var half = thicket.Size * 0.5f;

                for (int z = 0; z < grid.Height; z++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var p = grid.CellCentre(x, z) - centre;
                        float localX = p.x * cos - p.y * sin;
                        float localZ = p.x * sin + p.y * cos;
                        if (Mathf.Abs(localX) > half.x || Mathf.Abs(localZ) > half.y) continue;

                        if (grid.KindAt(x, z) == TileCellKind.Ground && !grid.RoadAt(x, z))
                            grid.SetThicket(x, z);
                    }
                }
            }

            var overrides = map.CellOverrides;
            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var cell = overrides[i];
                if (cell.Thicket == ThicketOverride.Remove)
                    grid.ClearThicket(cell.X, cell.Z);
                else if (cell.Thicket == ThicketOverride.Add &&
                         grid.KindAt(cell.X, cell.Z) == TileCellKind.Ground &&
                         !grid.RoadAt(cell.X, cell.Z))
                    grid.SetThicket(cell.X, cell.Z);
            }
        }

        /// <summary>
        /// The biome splat: every authored area contributes feathered influence per cell (1
        /// inside its rect, fading over Feather metres beyond); a painted cell adds a hand's
        /// weight of its own biome on top — the hand beats the field. The two strongest
        /// influences become the cell's dominant/secondary/blend. Purely additive data: with
        /// nothing authored no cell has a biome, and everything renders gray-box exactly as
        /// before.
        /// </summary>
        private static void RasterBiomes(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var areas = map.BiomeAreas;
            var overrides = map.CellOverrides;
            bool anyAreas = areas != null && areas.Length > 0;
            bool anyPaint = false;
            for (int i = 0; overrides != null && i < overrides.Length; i++)
                if (overrides[i].Biome >= 0) { anyPaint = true; break; }
            if (!anyAreas && !anyPaint) return;

            // A hand-painted cell outweighs any field: full influence is 1, the hand adds 2.
            const float PaintWeight = 2f;

            var influence = new Dictionary<int, float>();

            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    influence.Clear();
                    var centre = grid.CellCentre(x, z);

                    for (int i = 0; anyAreas && i < areas.Length; i++)
                    {
                        float w = AreaInfluence(areas[i], centre - offset);
                        if (w <= 0f) continue;
                        influence.TryGetValue(areas[i].BiomeIndex, out float sum);
                        influence[areas[i].BiomeIndex] = sum + w;
                    }

                    for (int i = 0; anyPaint && i < overrides.Length; i++)
                    {
                        if (overrides[i].Biome < 0 ||
                            overrides[i].X != x || overrides[i].Z != z) continue;
                        influence.TryGetValue(overrides[i].Biome, out float sum);
                        influence[overrides[i].Biome] = sum + PaintWeight;
                    }

                    if (influence.Count == 0) continue;

                    int bestBiome = -1, secondBiome = -1;
                    float bestW = 0f, secondW = 0f;
                    foreach (var kv in influence)
                    {
                        if (kv.Value > bestW)
                        {
                            secondBiome = bestBiome; secondW = bestW;
                            bestBiome = kv.Key; bestW = kv.Value;
                        }
                        else if (kv.Value > secondW)
                        {
                            secondBiome = kv.Key; secondW = kv.Value;
                        }
                    }

                    float lean = secondBiome < 0 ? 0f : secondW / (bestW + secondW);
                    grid.SetBiome(x, z, bestBiome, secondBiome, lean);
                }
            }
        }

        /// <summary>Feathered rect influence: 1 inside, 1→0 over Feather metres past the edge,
        /// using the rasterizer's rotation convention.</summary>
        private static float AreaInfluence(AuthoredBiomeArea area, Vector2 point)
        {
            var p = point - area.Centre;
            float rad = -area.RotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            float localX = p.x * cos - p.y * sin;
            float localZ = p.x * sin + p.y * cos;

            var half = area.Size * 0.5f;
            float d = Mathf.Max(Mathf.Abs(localX) - half.x, Mathf.Abs(localZ) - half.y);
            if (d <= 0f) return 1f;
            if (area.Feather <= 0f) return 0f;
            return Mathf.Clamp01(1f - d / area.Feather);
        }

        /// <summary>
        /// Blocking props stamp their footprints LAST, over the finished ground. The compiler
        /// reads only positions and footprints — the Prefab field is the world builder's, and
        /// this pass compiling headless is what keeps prop collision testable.
        /// </summary>
        private static void RasterProps(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var props = map.Props;

            for (int i = 0; props != null && i < props.Length; i++)
            {
                var prop = props[i];
                if (!prop.BlockCells) continue;
                if (!grid.TryCellAt(prop.Position + offset, out int cx, out int cz)) continue;

                // The footprint centres on the prop's cell: odd sizes sit symmetric, even
                // sizes bias toward positive — deterministic either way.
                int halfX = (prop.BlockSize.x - 1) / 2;
                int halfZ = (prop.BlockSize.y - 1) / 2;
                for (int dz = 0; dz < Mathf.Max(1, prop.BlockSize.y); dz++)
                    for (int dx = 0; dx < Mathf.Max(1, prop.BlockSize.x); dx++)
                        grid.SetBlocked(cx - halfX + dx, cz - halfZ + dz);
            }
        }

        /// <summary>
        /// The hand-painted cells, the fine half of the hybrid grain: applied AFTER regions and
        /// rivers (painting Ground over a carved channel restores land; painting Sea carves a
        /// cell at its own water level) and BEFORE the ramps (a painted terrace boundary is
        /// rampable). The spill warning runs after this pass so painted water is checked too.
        /// </summary>
        private static void ApplyTerrainOverrides(OverworldTileGrid grid, OverworldMap map)
        {
            var overrides = map.CellOverrides;

            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var cell = overrides[i];
                if (cell.Terrain == TerrainOverride.None) continue;

                grid.Set(cell.X, cell.Z,
                         cell.Terrain == TerrainOverride.Sea ? TileCellKind.Sea : TileCellKind.Ground,
                         cell.Level);
            }
        }

        /// <summary>
        /// Road paint overrides, LAST: Remove erases what the shape courses drew; Add obeys the
        /// same invariant the courses do — flat Ground or an overlay deck, never a slope or
        /// open water.
        /// </summary>
        private static void ApplyRoadOverrides(OverworldTileGrid grid, OverworldMap map)
        {
            var overrides = map.CellOverrides;
            int refused = 0;
            int firstX = 0, firstZ = 0;

            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var cell = overrides[i];
                if (cell.Road == RoadOverride.None) continue;

                if (cell.Road == RoadOverride.Remove)
                {
                    grid.ClearRoad(cell.X, cell.Z);
                    continue;
                }

                if (grid.TryOverlayAt(cell.X, cell.Z, out _) ||
                    grid.KindAt(cell.X, cell.Z) == TileCellKind.Ground)
                {
                    grid.SetRoad(cell.X, cell.Z);
                }
                else
                {
                    if (refused == 0) { firstX = cell.X; firstZ = cell.Z; }
                    refused++;
                }
            }

            if (refused > 0)
                Note($"[Prophecy] {refused} painted road cell(s) refused — road " +
                                 $"rides flat ground or a deck, first refusal at ({firstX}, {firstZ}).");
        }

        /// <summary>
        /// Rivers carve AFTER the regions (a channel cuts whatever terrace it crosses) and
        /// BEFORE the ramps (a climb never converts into a river bed). A carved cell is sea AT
        /// A LEVEL: the water inherits the level of the terrain along the course, monotonically
        /// non-increasing from source to mouth — so a river holds its terrace's height and
        /// FALLS exactly where the land falls. The coast machinery grows its banks at whatever
        /// level the water holds; a Layer deck over it is a bridge; where water at level N
        /// meets water below, the planner hangs a waterfall sheet.
        /// </summary>
        private static void RasterRivers(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var rivers = map.Rivers;

            for (int i = 0; rivers != null && i < rivers.Length; i++)
            {
                var river = rivers[i];
                var pts = river.Points;
                if (pts == null || pts.Length == 0) continue;

                // Sample FIRST, carve SECOND: the level under the course caps the water, and a
                // carving that ran ahead of the sampling would raise the downstream terrain to
                // its own water level before the centreline read it — the fall would never
                // drop. Downstream the water only descends (off the map is the ocean); a course
                // that climbs back up cuts a canyon through the rise instead of flowing uphill.
                var points = new List<Vector2>();
                var levels = new List<int>();
                int water = int.MaxValue;
                float sample = OverworldTileGrid.CellSize * 0.25f;

                for (int s = 0; s < Mathf.Max(1, pts.Length - 1); s++)
                {
                    Vector2 a = pts[s] + offset;
                    Vector2 b = (pts.Length > 1 ? pts[s + 1] : pts[s]) + offset;
                    int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / sample));

                    for (int k = 0; k <= steps; k++)
                    {
                        var p = Vector2.Lerp(a, b, k / (float)steps);
                        water = Mathf.Min(water, grid.TryCellAt(p, out int px, out int pz)
                            ? grid.LevelAt(px, pz)
                            : 0);
                        points.Add(p);
                        levels.Add(water);
                    }
                }

                for (int s = 0; s < points.Count; s++)
                    CarveDisc(grid, points[s], river.HalfWidth, levels[s]);
            }
            // The spill warning runs from Compile, after the terrain overrides — painted
            // water deserves the same check the carved kind gets.
        }

        private static void CarveDisc(OverworldTileGrid grid, Vector2 centre, float radius, int level)
        {
            int minX = Mathf.FloorToInt((centre.x - radius - grid.Origin.x) / OverworldTileGrid.CellSize);
            int maxX = Mathf.FloorToInt((centre.x + radius - grid.Origin.x) / OverworldTileGrid.CellSize);
            int minZ = Mathf.FloorToInt((centre.y - radius - grid.Origin.y) / OverworldTileGrid.CellSize);
            int maxZ = Mathf.FloorToInt((centre.y + radius - grid.Origin.y) / OverworldTileGrid.CellSize);

            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    if (grid.InBounds(x, z) &&
                        (grid.CellCentre(x, z) - centre).magnitude <= radius)
                        grid.Set(x, z, TileCellKind.Sea, level);
        }

        /// <summary>
        /// Elevated water beside LOWER LAND has nowhere sensible to go — the map would show a
        /// river surface hanging over open ground. Legal beside lower WATER (that is a
        /// waterfall); an authoring mistake beside lower land, flagged like an inert ramp.
        /// </summary>
        private static void WarnWhereWaterSpills(OverworldTileGrid grid)
        {
            int spills = 0;
            int firstX = 0, firstZ = 0;

            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (grid.KindAt(x, z) != TileCellKind.Sea || grid.LevelAt(x, z) <= 0) continue;

                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                        int nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (grid.KindAt(nx, nz) != TileCellKind.Ground) continue;
                        if (grid.LevelAt(nx, nz) >= grid.LevelAt(x, z)) continue;

                        if (spills == 0) { firstX = x; firstZ = z; }
                        spills++;
                    }
                }
            }

            if (spills > 0)
                Note($"[Prophecy] River water hangs over lower ground at {spills} " +
                                 $"cell edge(s), first at cell ({firstX}, {firstZ}) — bank the " +
                                 "course with terrain at or above its level, or lower it.");
        }

        /// <summary>
        /// Roads raster LAST, over the finished ground: flat Ground cells take the flag, and a
        /// cell with an overlay takes it too — that is a road crossing a bridge deck. Sea gaps
        /// and sloped ramp cells stay bare.
        /// </summary>
        private static void RasterRoads(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var roads = map.Roads;

            for (int i = 0; roads != null && i < roads.Length; i++)
            {
                var road = roads[i];
                if (road.Points == null || road.Points.Length == 0) continue;

                for (int z = 0; z < grid.Height; z++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (DistanceToPolyline(grid.CellCentre(x, z), road.Points, offset) > road.HalfWidth)
                            continue;

                        if (grid.TryOverlayAt(x, z, out _) || grid.KindAt(x, z) == TileCellKind.Ground)
                            grid.SetRoad(x, z);
                    }
                }
            }
        }

        private static float DistanceToPolyline(Vector2 p, Vector2[] points, Vector2 offset)
        {
            if (points.Length == 1) return (p - (points[0] + offset)).magnitude;

            float best = float.MaxValue;
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector2 a = points[i] + offset;
                Vector2 ab = points[i + 1] + offset - a;
                float t = ab.sqrMagnitude < 1e-6f
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
            }

            return best;
        }

        /// <summary>
        /// Extra surfaces last, over whatever the terrain became: each authored layer footprint
        /// writes one overlay level per cell. A footprint over sea is a bridge over water — the
        /// deck is then the cell's only surface.
        /// </summary>
        private static void RasterLayers(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var layers = map.Layers;

            for (int i = 0; layers != null && i < layers.Length; i++)
            {
                var authored = layers[i];
                int level = QuantizeLevel(authored.Y, authored.Name);

                var centre = authored.Centre + offset;
                float rad = -authored.RotationDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                var half = authored.Size * 0.5f;

                for (int z = 0; z < grid.Height; z++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var p = grid.CellCentre(x, z) - centre;
                        float localX = p.x * cos - p.y * sin;
                        float localZ = p.x * sin + p.y * cos;

                        if (Mathf.Abs(localX) <= half.x && Mathf.Abs(localZ) <= half.y)
                            grid.SetOverlay(x, z, level);
                    }
                }
            }
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
                    Note($"[Prophecy] Ramp '{ramp.Name}' converted no cells — its " +
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
                Note($"[Prophecy] Region '{regionName}' Y={y:0.00} is not a multiple " +
                                 $"of the {OverworldTileGrid.Step} m step — quantized to level " +
                                 $"{level} ({level * OverworldTileGrid.Step:0.00} m).");

            return level;
        }
    }
}
