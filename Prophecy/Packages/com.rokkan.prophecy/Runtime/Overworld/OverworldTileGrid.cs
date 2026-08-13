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
    /// <para>The storage is a stack of CHANNELS over the same cell lattice — the terrain core,
    /// then each sparse decoration (overlays and cover, roads, blocking, provinces, the biome
    /// splat) as its own contiguous block below it. A new channel is a new block with its own
    /// fields and accessors; nothing above it needs touching.</para>
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

        // ---------------------------------------------------------------- the lattice

        private readonly int _width;
        private readonly int _height;
        private readonly Vector2 _origin;

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

        // ---------------------------------------------------------------- terrain channel

        private readonly TileCellKind[] _kinds;
        private readonly sbyte[] _levels;
        private readonly RampFacing[] _facings;
        private readonly byte[] _rampIndices;

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

        // ---------------------------------------------------------------- overlay & cover channel

        // A cell's optional SECOND walkable surface — a bridge deck above the base terrain, or
        // a cave floor beneath it (the base then reads as the roof). Sparse because overlaps
        // are rare; one extra surface per cell is the deliberate cap of this model. Flat Ground
        // only — overlay ramps are a future decision, made with the feature that needs them.
        private readonly Dictionary<int, sbyte> _overlays = new Dictionary<int, sbyte>();

        // Resolved cover style per overlay cell (true = cave), and the cave-region ids the
        // compiler bakes last: connected cave-covered cells share an id, so "which room am I
        // in" is one array read at runtime. Bridges need no regions — their behaviour is
        // per-cell (the halo through the deck), not per-room.
        private readonly Dictionary<int, bool> _caveCover = new Dictionary<int, bool>();
        private byte[] _coverRegions;
        private int _coverRegionCount;

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
            // Auto style: a floor below the terrain is a cave (the terrain is its roof), a
            // deck above it a bridge. Resolved HERE, at stamp time, because this is the one
            // moment both the overlay's level and the finished terrain level are in hand.
            SetOverlay(x, z, level,
                       level < LevelAt(x, z) && KindAt(x, z) == TileCellKind.Ground
                           ? CoverStyle.Cave : CoverStyle.Bridge);
        }

        public void SetOverlay(int x, int z, int level, CoverStyle resolved)
        {
            if (!InBounds(x, z)) return;
            _overlays[z * _width + x] = (sbyte)level;
            _caveCover[z * _width + x] = resolved == CoverStyle.Cave;
        }

        /// <summary>True when the cell has cover at all; <paramref name="isCave"/> says which
        /// kind — cave cover inverts the picture, bridge cover does not.</summary>
        public bool TryCoverAt(int x, int z, out bool isCave)
        {
            if (InBounds(x, z) && _caveCover.TryGetValue(z * _width + x, out bool cave))
            {
                isCave = cave;
                return true;
            }

            isCave = false;
            return false;
        }

        /// <summary>The connected cave room this cell belongs to, or −1. Baked by the
        /// compiler after everything that can move terrain has had its say.</summary>
        public int CoverRegionAt(int x, int z)
        {
            if (_coverRegions == null || !InBounds(x, z)) return -1;
            byte id = _coverRegions[z * _width + x];
            return id == 255 ? -1 : id;
        }

        public int CoverRegionCount => _coverRegionCount;

        /// <summary>Flood-fill connected cave-covered cells into rooms (4-connected, capped at
        /// 255 rooms — a map with more has bigger problems). Public so hand-built test grids
        /// can bake without the compiler.</summary>
        public void BakeCoverRegions()
        {
            _coverRegions = new byte[_width * _height];
            for (int i = 0; i < _coverRegions.Length; i++) _coverRegions[i] = 255;
            _coverRegionCount = 0;

            var stack = new Stack<(int x, int z)>();
            for (int z = 0; z < _height; z++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (_coverRegions[z * _width + x] != 255) continue;
                    if (!TryCoverAt(x, z, out bool cave) || !cave) continue;
                    if (_coverRegionCount >= 255) return;

                    byte id = (byte)_coverRegionCount++;
                    stack.Push((x, z));
                    while (stack.Count > 0)
                    {
                        var (cx, cz) = stack.Pop();
                        if (!InBounds(cx, cz)) continue;
                        if (_coverRegions[cz * _width + cx] != 255) continue;
                        if (!TryCoverAt(cx, cz, out bool c) || !c) continue;

                        _coverRegions[cz * _width + cx] = id;
                        stack.Push((cx + 1, cz));
                        stack.Push((cx - 1, cz));
                        stack.Push((cx, cz + 1));
                        stack.Push((cx, cz - 1));
                    }
                }
            }
        }

        // ---------------------------------------------------------------- road channel

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

        // ---------------------------------------------------------------- blocking channels

        // Cells a blocking prop stands on. Walkability treats them as having no surface at
        // all, which keeps the escape rule: a body somehow stranded INSIDE a prop's footprint
        // may always step out, it just can't step in.
        private readonly HashSet<int> _unwalkable = new HashSet<int>();

        // Greeble cells: blocked like a prop footprint AND scatter-filled by the biome. The
        // separate set exists because the scatter needs to know which blocked cells are
        // greeble (fill with trees) versus prop footprints (already filled by the prop).
        private readonly HashSet<int> _greeble = new HashSet<int>();

        /// <summary>Whether a blocking prop occupies this cell.</summary>
        public bool UnwalkableAt(int x, int z) => InBounds(x, z) && _unwalkable.Contains(z * _width + x);

        public void SetUnwalkable(int x, int z)
        {
            if (InBounds(x, z)) _unwalkable.Add(z * _width + x);
        }

        public void ClearUnwalkable(int x, int z)
        {
            if (InBounds(x, z)) _unwalkable.Remove(z * _width + x);
        }

        /// <summary>Whether this cell is an impassable scatter-filled mass.</summary>
        public bool GreebleAt(int x, int z) => InBounds(x, z) && _greeble.Contains(z * _width + x);

        public void SetGreeble(int x, int z)
        {
            if (!InBounds(x, z)) return;
            _greeble.Add(z * _width + x);
            _unwalkable.Add(z * _width + x);
        }

        public void ClearGreeble(int x, int z)
        {
            if (!InBounds(x, z)) return;
            _greeble.Remove(z * _width + x);
            _unwalkable.Remove(z * _width + x);
        }

        // ---------------------------------------------------------------- province channel

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

        // ---------------------------------------------------------------- biome splat channel

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

        // ---------------------------------------------------------------- surface queries

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
        /// The height this cell's base surface presents at its edge in direction
        /// (<paramref name="dx"/>, <paramref name="dz"/>), in 1/RampRun level units — the one
        /// arithmetic behind "visuals and walkability cannot disagree". The face planner, the
        /// ground seam and the corner band rule all read edges through here; their deliberate
        /// differences live in their wrappers, never re-derived.
        ///
        /// <para>Ground and sea present their level (a river reach carved through a terrace
        /// holds that terrace's level); run cell i presents +i at its low edge and +i+1 at its
        /// high edge. A ramp's SIDE answers its base level with <paramref name="rampSide"/>
        /// raised — whether a side is a surface to draw faces against or a refusal to walk
        /// through is each caller's role, not this cell's.</para>
        /// </summary>
        public int EdgeLevelAt(int x, int z, int dx, int dz, out bool rampSide)
        {
            rampSide = false;
            int scaled = LevelAt(x, z) * RampRun;
            if (KindAt(x, z) != TileCellKind.Ramp) return scaled;

            FacingDelta(FacingAt(x, z), out int fx, out int fz);
            bool towardHigh = fx == dx && fz == dz;
            bool towardLow = fx == -dx && fz == -dz;
            if (!towardHigh && !towardLow)
            {
                rampSide = true;
                return scaled;
            }

            return scaled + RampIndexAt(x, z) + (towardHigh ? 1 : 0);
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
            if (!high) return LevelAt(x, z);

            // The corner rule's reading of the shared edge arithmetic: only where the high
            // edge presents a WHOLE level (the run's final cell) does the corner top out;
            // every fractional edge rounds down to base.
            FacingDelta(FacingAt(x, z), out int fx, out int fz);
            int edge = EdgeLevelAt(x, z, fx, fz, out _);
            return edge % RampRun == 0 ? edge / RampRun : LevelAt(x, z);
        }

        /// <summary>The lattice direction a facing climbs.</summary>
        private static void FacingDelta(RampFacing facing, out int dx, out int dz)
        {
            switch (facing)
            {
                case RampFacing.PlusZ: dx = 0; dz = 1; break;
                case RampFacing.PlusX: dx = 1; dz = 0; break;
                case RampFacing.MinusZ: dx = 0; dz = -1; break;
                default: dx = -1; dz = 0; break;
            }
        }
    }
}
