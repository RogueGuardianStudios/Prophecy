using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>What one compile produced: the grid, and the authoring audits gathered on the
    /// way — inert ramps, spilling water, refused road paint, off-grid Ys. The audits travel
    /// WITH their result instead of through statics, so an editor-preview compile and a
    /// play-mode load can overlap without interleaving each other's notes.</summary>
    public sealed class OverworldCompileResult
    {
        public readonly OverworldTileGrid Grid;
        public readonly IReadOnlyList<string> Notes;

        public OverworldCompileResult(OverworldTileGrid grid, IReadOnlyList<string> notes)
        {
            Grid = grid;
            Notes = notes;
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

        /// <summary>The per-compile audit sink. Console logging is the scene-load channel, where
        /// the console is the only surface; the map tool compiles on every stroke and keeps its
        /// audits in the result — a repeated audit is feedback once and noise forever after.</summary>
        private sealed class Audit
        {
            public readonly List<string> Notes = new List<string>();
            private readonly bool _toConsole;

            public Audit(bool toConsole) => _toConsole = toConsole;

            public void Note(string message)
            {
                Notes.Add(message);
                if (_toConsole) Debug.LogWarning(message);
            }
        }

        /// <summary>Compile with the audits going to the console — the scene-load entry, and the
        /// one the headless tests assert warnings against.</summary>
        public static OverworldTileGrid Compile(OverworldMap map, Vector3 worldOffset) =>
            CompileWithReport(map, worldOffset, logAuditsToConsole: true).Grid;

        public static OverworldCompileResult CompileWithReport(OverworldMap map, Vector3 worldOffset,
                                                               bool logAuditsToConsole)
        {
            var audit = new Audit(logAuditsToConsole);
            int width = Mathf.Max(1, Mathf.RoundToInt(map.BoundsSize.x / OverworldTileGrid.CellSize));
            int height = Mathf.Max(1, Mathf.RoundToInt(map.BoundsSize.y / OverworldTileGrid.CellSize));
            var origin = new Vector2(worldOffset.x - map.BoundsSize.x * 0.5f,
                                     worldOffset.z - map.BoundsSize.y * 0.5f);

            var grid = new OverworldTileGrid(width, height, origin);
            var offset = new Vector2(worldOffset.x, worldOffset.z);

            RasterRegions(grid, map, offset, audit);
            RasterRivers(grid, map, offset);
            ApplyTerrainOverrides(grid, map);
            WarnWhereWaterSpills(grid, audit);
            ConvertRamps(grid, map, offset, audit);
            RasterLayers(grid, map, offset, audit);
            RasterRoads(grid, map, offset);
            ApplyRoadOverrides(grid, map, audit);
            RasterBiomes(grid, map, offset);
            RasterGreebles(grid, map, offset);
            RasterProvinces(grid, map, offset);
            RasterProps(grid, map, offset);
            ApplyUnwalkableOverrides(grid, map);
            grid.BakeCoverRegions();   // derived data, after everything that shapes terrain

            return new OverworldCompileResult(grid, audit.Notes);
        }

        /// <summary>
        /// Visit every cell whose CENTRE the rotated rect could claim: the rect's axis-aligned
        /// cell bounds first, the exact |local| ≤ half test only inside them — an authored shape
        /// costs its own footprint, not the whole map. The transform is the raster convention
        /// every shape shares (world to shape-local rotates through −RotationDegrees) and the
        /// test is inclusive at the edge, so every shape pass keeps the exact footprint a
        /// full-grid scan would give it. The bounds are padded a cell so a centre sitting
        /// exactly on the boundary can never fall to float rounding at the box edge.
        /// </summary>
        private static void ForEachCellInRect(OverworldTileGrid grid, Vector2 worldCentre,
                                              Vector2 size, float rotationDegrees,
                                              System.Action<int, int> visit)
        {
            float rad = -rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            var half = size * 0.5f;

            float extentX = Mathf.Abs(half.x * cos) + Mathf.Abs(half.y * sin);
            float extentZ = Mathf.Abs(half.x * sin) + Mathf.Abs(half.y * cos);
            int minX = Mathf.Max(0, Mathf.FloorToInt(
                (worldCentre.x - extentX - grid.Origin.x) / OverworldTileGrid.CellSize) - 1);
            int maxX = Mathf.Min(grid.Width - 1, Mathf.FloorToInt(
                (worldCentre.x + extentX - grid.Origin.x) / OverworldTileGrid.CellSize) + 1);
            int minZ = Mathf.Max(0, Mathf.FloorToInt(
                (worldCentre.y - extentZ - grid.Origin.y) / OverworldTileGrid.CellSize) - 1);
            int maxZ = Mathf.Min(grid.Height - 1, Mathf.FloorToInt(
                (worldCentre.y + extentZ - grid.Origin.y) / OverworldTileGrid.CellSize) + 1);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = grid.CellCentre(x, z) - worldCentre;
                    float localX = p.x * cos - p.y * sin;
                    float localZ = p.x * sin + p.y * cos;
                    if (Mathf.Abs(localX) <= half.x && Mathf.Abs(localZ) <= half.y)
                        visit(x, z);
                }
            }
        }

        /// <summary>
        /// Painted walkability, applied dead LAST: Add blocks a cell bare (no scatter — bare
        /// unwalkable ground); Remove unblocks ANYTHING, including greeble masses and prop
        /// footprints — the hand's last word over every other source of blocking.
        /// </summary>
        private static void ApplyUnwalkableOverrides(OverworldTileGrid grid, OverworldMap map)
        {
            var overrides = map.CellOverrides;

            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var cell = overrides[i];
                if (cell.Unwalkable == UnwalkableOverride.Add)
                {
                    grid.SetUnwalkable(cell.X, cell.Z);
                }
                else if (cell.Unwalkable == UnwalkableOverride.Remove)
                {
                    grid.ClearGreeble(cell.X, cell.Z);   // also clears its blocked flag
                    grid.ClearUnwalkable(cell.X, cell.Z);
                }
            }
        }

        /// <summary>
        /// Provinces are the named shapes themselves — a shape with a Province reference
        /// stamps its cells with those rules, in raster order (regions, then biome areas by
        /// their HARD rect, then greebles), later wins. No separate bounds ever exist to
        /// drift from the places they describe.
        /// </summary>
        private static void RasterProvinces(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            void StampRect(Vector2 centre, Vector2 size, float rotationDegrees,
                           OverworldProvince province)
            {
                if (province == null) return;
                ForEachCellInRect(grid, centre + offset, size, rotationDegrees,
                                  (x, z) => grid.SetProvince(x, z, province));
            }

            var regions = map.Regions;
            for (int i = 0; regions != null && i < regions.Length; i++)
                StampRect(regions[i].Centre, regions[i].Size, regions[i].RotationDegrees,
                          regions[i].Province);

            var areas = map.BiomeAreas;
            for (int i = 0; areas != null && i < areas.Length; i++)
                StampRect(areas[i].Centre, areas[i].Size, areas[i].RotationDegrees,
                          areas[i].Province);

            var greebles = map.Greebles;
            for (int i = 0; greebles != null && i < greebles.Length; i++)
                StampRect(greebles[i].Centre, greebles[i].Size, greebles[i].RotationDegrees,
                          greebles[i].Province);
        }

        /// <summary>
        /// Impassable scatter-filled masses: shape rects plant them on flat GROUND cells only
        /// (a greeble never grows on water, a slope, or a road — the road wins, it is the
        /// carved path through the forest), and the painted overrides add or carve per cell.
        /// Greeble blocks exactly like a prop footprint — the ground seam refuses, the escape
        /// rule frees a stranded body.
        /// </summary>
        private static void RasterGreebles(OverworldTileGrid grid, OverworldMap map, Vector2 offset)
        {
            var greebles = map.Greebles;

            for (int i = 0; greebles != null && i < greebles.Length; i++)
            {
                var greeble = greebles[i];
                ForEachCellInRect(grid, greeble.Centre + offset, greeble.Size,
                                  greeble.RotationDegrees,
                                  (x, z) =>
                                  {
                                      if (grid.KindAt(x, z) == TileCellKind.Ground &&
                                          !grid.RoadAt(x, z))
                                          grid.SetGreeble(x, z);
                                  });
            }

            var overrides = map.CellOverrides;
            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var cell = overrides[i];
                if (cell.Greeble == GreebleOverride.Remove)
                    grid.ClearGreeble(cell.X, cell.Z);
                else if (cell.Greeble == GreebleOverride.Add &&
                         grid.KindAt(cell.X, cell.Z) == TileCellKind.Ground &&
                         !grid.RoadAt(cell.X, cell.Z))
                    grid.SetGreeble(cell.X, cell.Z);
            }
        }

        /// <summary>
        /// The biome splat: every authored area contributes feathered influence per cell (1
        /// inside its rect, fading over Feather metres beyond); a painted cell adds a hand's
        /// weight of its own biome on top — the hand beats the field. The two strongest
        /// influences become the cell's dominant/secondary/blend. Purely additive data: with
        /// nothing authored no cell has a biome, and everything renders gray-box exactly as
        /// before.
        ///
        /// <para>Contributions gather per cell in authored order — areas first, paint after —
        /// and the fold below sums each biome in exactly the order its influences arrived, so
        /// dominance ties resolve the same way however the cells are visited.</para>
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

            var byCell = new List<(int biome, float weight)>[grid.Width * grid.Height];

            List<(int biome, float weight)> At(int x, int z)
            {
                int idx = z * grid.Width + x;
                return byCell[idx] ?? (byCell[idx] = new List<(int biome, float weight)>());
            }

            for (int i = 0; anyAreas && i < areas.Length; i++)
            {
                var area = areas[i];
                // The feather reaches past the hard rect, so the visited bounds must too.
                float pad = Mathf.Max(0f, area.Feather);
                ForEachCellInRect(grid, area.Centre + offset,
                                  area.Size + new Vector2(pad, pad) * 2f, area.RotationDegrees,
                                  (x, z) =>
                                  {
                                      float w = AreaInfluence(area, grid.CellCentre(x, z) - offset);
                                      if (w <= 0f) return;
                                      At(x, z).Add((area.BiomeIndex, w));
                                  });
            }

            for (int i = 0; anyPaint && i < overrides.Length; i++)
            {
                if (overrides[i].Biome < 0 || !grid.InBounds(overrides[i].X, overrides[i].Z))
                    continue;
                At(overrides[i].X, overrides[i].Z).Add((overrides[i].Biome, PaintWeight));
            }

            var sums = new List<(int biome, float weight)>();
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var contributions = byCell[z * grid.Width + x];
                    if (contributions == null) continue;

                    sums.Clear();
                    for (int c = 0; c < contributions.Count; c++)
                    {
                        var contribution = contributions[c];
                        int slot = -1;
                        for (int s = 0; s < sums.Count; s++)
                            if (sums[s].biome == contribution.biome) { slot = s; break; }
                        if (slot < 0) sums.Add(contribution);
                        else sums[slot] = (contribution.biome,
                                           sums[slot].weight + contribution.weight);
                    }

                    int bestBiome = -1, secondBiome = -1;
                    float bestW = 0f, secondW = 0f;
                    for (int s = 0; s < sums.Count; s++)
                    {
                        if (sums[s].weight > bestW)
                        {
                            secondBiome = bestBiome; secondW = bestW;
                            bestBiome = sums[s].biome; bestW = sums[s].weight;
                        }
                        else if (sums[s].weight > secondW)
                        {
                            secondBiome = sums[s].biome; secondW = sums[s].weight;
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
                        grid.SetUnwalkable(cx - halfX + dx, cz - halfZ + dz);
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
        private static void ApplyRoadOverrides(OverworldTileGrid grid, OverworldMap map, Audit audit)
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
                audit.Note($"[Prophecy] {refused} painted road cell(s) refused — road " +
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
        private static void WarnWhereWaterSpills(OverworldTileGrid grid, Audit audit)
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
                audit.Note($"[Prophecy] River water hangs over lower ground at {spills} " +
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
        private static void RasterLayers(OverworldTileGrid grid, OverworldMap map, Vector2 offset,
                                         Audit audit)
        {
            var layers = map.Layers;

            for (int i = 0; layers != null && i < layers.Length; i++)
            {
                var authored = layers[i];
                int level = QuantizeLevel(authored.Y, authored.Name, audit);

                ForEachCellInRect(grid, authored.Centre + offset, authored.Size,
                                  authored.RotationDegrees,
                                  (x, z) =>
                                  {
                                      if (authored.Cover == CoverStyle.Auto)
                                          grid.SetOverlay(x, z, level);   // derive cave/bridge
                                      else
                                          grid.SetOverlay(x, z, level, authored.Cover);
                                  });
            }
        }

        /// <summary>
        /// A cell is land iff its centre lies inside an authored footprint; later regions win
        /// overlaps, which is the deterministic reading of "terraces are authored on top of the
        /// heartland". Rotated footprints rasterize into staircase coasts — correctly Zelda.
        /// </summary>
        private static void RasterRegions(OverworldTileGrid grid, OverworldMap map, Vector2 offset,
                                          Audit audit)
        {
            var regions = map.Regions;
            if (regions == null || regions.Length == 0) return;

            for (int i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                int level = QuantizeLevel(region.Y, region.Name, audit);

                ForEachCellInRect(grid, region.Centre + offset, region.Size,
                                  region.RotationDegrees,
                                  (x, z) => grid.Set(x, z, TileCellKind.Ground, level));
            }
        }

        /// <summary>
        /// An authored ramp names a strip; the compiler finds every one-step terrace boundary
        /// inside it and converts the LOW cell at the boundary into a Ramp cell facing uphill —
        /// so a compiled ramp is always exactly where two terraces actually meet, whatever the
        /// authored segment's length. A strip containing no such boundary compiles to nothing
        /// and says so: an inert ramp is an authoring mistake, not a silent success.
        /// </summary>
        private static void ConvertRamps(OverworldTileGrid grid, OverworldMap map, Vector2 offset,
                                         Audit audit)
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
                    audit.Note($"[Prophecy] Ramp '{ramp.Name}' converted no cells — its " +
                               "strip contains no one-step boundary with room for the " +
                               $"run ({OverworldTileGrid.RampRun - OverworldTileGrid.RampRecess} " +
                               $"low cells, {OverworldTileGrid.RampRecess} notched into the " +
                               "high terrace, and high ground beyond). It climbs nothing.");
            }
        }

        private static int QuantizeLevel(float y, string regionName, Audit audit)
        {
            int level = Mathf.Max(0, Mathf.RoundToInt(y / OverworldTileGrid.Step));

            if (Mathf.Abs(y - level * OverworldTileGrid.Step) > QuantizeTolerance)
                audit.Note($"[Prophecy] Region '{regionName}' Y={y:0.00} is not a multiple " +
                           $"of the {OverworldTileGrid.Step} m step — quantized to level " +
                           $"{level} ({level * OverworldTileGrid.Step:0.00} m).");

            return level;
        }
    }
}
