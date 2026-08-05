using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>The tile set's vocabulary — one entry per piece in Plans/Overworld-Tile-Set.md.</summary>
    public enum OverworldTilePiece
    {
        Cap,
        Ramp,
        Stairs,
        Water,
        Face1,
        Face2,
        Face3,
        OuterPost1,
        OuterPost2,
        OuterPost3,
        InnerPost1,
        InnerPost2,
        InnerPost3,
        Cheek,
        CheekMirrored,
        ShoreEdge,
        ShoreOuterPost,
    }

    /// <summary>One placed piece: which, where, turned how. No mirror flag — the cheek's mirrored
    /// twin is a baked piece, because negative scale reverses winding and renders inside-out.
    /// There is no shore inner-corner piece either: two banks meeting inward already intersect in
    /// a clean valley, and anything laid over them is coplanar with both.</summary>
    public struct TilePlacement
    {
        public OverworldTilePiece Piece;
        public Vector3 Position;
        public float Yaw;
        public Vector3 Scale;
    }

    /// <summary>
    /// Turns the tile grid into a list of piece placements — the placement contract from
    /// Plans/Overworld-Tile-Set.md as executable rules. Pure C#: no scene, no prefabs, so a test
    /// can assert exactly which pieces a map produces and where.
    ///
    /// <para>Caps at Ground cells; ramp or stairs at Ramp cells; faces on every differing edge
    /// with the greedy 3-split; posts from the per-corner band rule (diagonal contacts become two
    /// back-to-back outer posts); cheeks where a ramp cuts through a cliff, mirrored in pairs;
    /// shore pieces where level-0 land meets sea; stacked faces where higher land meets sea
    /// directly; one water plane under everything.</para>
    /// </summary>
    public static class TilePiecePlanner
    {
        private const float Step = OverworldTileGrid.Step;
        private const float Cell = OverworldTileGrid.CellSize;

        public static List<TilePlacement> Plan(OverworldTileGrid grid, bool stairsForRamps = true)
        {
            var placements = new List<TilePlacement>(grid.Width * grid.Height);

            PlanCells(grid, placements, stairsForRamps);
            var covered = PlanCheeks(grid, placements);
            PlanEdges(grid, placements, covered);
            PlanCorners(grid, placements);
            PlanWater(grid, placements);

            return placements;
        }

        // ---------------------------------------------------------------- cells

        private static void PlanCells(OverworldTileGrid grid, List<TilePlacement> list, bool stairs)
        {
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var kind = grid.KindAt(x, z);
                    if (kind == TileCellKind.Sea) continue;

                    var centre = grid.CellCentre(x, z);

                    if (kind == TileCellKind.Ground)
                    {
                        Add(list, OverworldTilePiece.Cap,
                            new Vector3(centre.x, grid.LevelAt(x, z) * Step, centre.y), 0f);
                        continue;
                    }

                    float yaw;
                    switch (grid.FacingAt(x, z))
                    {
                        case RampFacing.PlusZ: yaw = 0f; break;
                        case RampFacing.PlusX: yaw = 90f; break;
                        case RampFacing.MinusZ: yaw = 180f; break;
                        default: yaw = 270f; break;
                    }

                    // Run cell i sits i/RampRun of a step up; the pieces each rise Step/RampRun,
                    // so consecutive cells chain into one continuous climb.
                    float rampY = (grid.LevelAt(x, z) +
                                   grid.RampIndexAt(x, z) / (float)OverworldTileGrid.RampRun) * Step;
                    Add(list, stairs ? OverworldTilePiece.Stairs : OverworldTilePiece.Ramp,
                        new Vector3(centre.x, rampY, centre.y), yaw);
                }
            }
        }

        // ---------------------------------------------------------------- cheek runs

        /// <summary>
        /// Cutting walls. The run's TOP cell is notched into the high terrace (the compiler's
        /// recess); where ground at N+1 or higher flanks that notch, a one-cell cheek covers the
        /// band between its rising surface and the wall top (front toward the stairs, tall end
        /// at its foot — the far wall is the mirrored twin), with plain faces above where the
        /// wall is taller. Covered edges are returned so the edge pass leaves them alone. Walls
        /// beside LOWER run cells (deeper recesses than the compiler writes) fall back to plain
        /// faces from the generic pass.
        /// </summary>
        private static HashSet<long> PlanCheeks(OverworldTileGrid grid, List<TilePlacement> list)
        {
            var covered = new HashSet<long>();
            int last = OverworldTileGrid.RampRun - 1;

            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (grid.KindAt(x, z) != TileCellKind.Ramp ||
                        grid.RampIndexAt(x, z) != last) continue;

                    var dir = FacingDir(grid.FacingAt(x, z));
                    var perp = new Vector2Int(dir.y, -dir.x);
                    int level = grid.LevelAt(x, z);

                    PlanCheekSide(grid, list, covered, x, z, dir, perp, level);
                    PlanCheekSide(grid, list, covered, x, z, dir, -perp, level);
                }
            }

            return covered;
        }

        private static void PlanCheekSide(OverworldTileGrid grid, List<TilePlacement> list,
                                          HashSet<long> covered, int x, int z, Vector2Int dir,
                                          Vector2Int side, int level)
        {
            int nx = x + side.x;
            int nz = z + side.y;
            if (grid.KindAt(nx, nz) != TileCellKind.Ground ||
                grid.LevelAt(nx, nz) < level + 1) return;

            var mid = EdgeMid(grid, x, z, side.x, side.y);
            Orient(-side, -dir, out float yaw, out bool mirror);

            // The cheek's pivot is the NOTCH cell's own foot plane — one rise below the wall top.
            float footY = (level + (OverworldTileGrid.RampRun - 1)
                           / (float)OverworldTileGrid.RampRun) * Step;
            Add(list, mirror ? OverworldTilePiece.CheekMirrored : OverworldTilePiece.Cheek,
                new Vector3(mid.x, footY, mid.y), yaw);

            covered.Add(EdgeKey(grid, x, z, side.x, side.y));

            int wall = grid.LevelAt(nx, nz);
            if (wall > level + 1)
                EmitFaces(list, mid, wall, level + 1, FrontYaw(-side.x, -side.y), -side);
        }

        /// <summary>Canonical id of the edge leaving (ax, az) toward (ax+dx, az+dz).</summary>
        private static long EdgeKey(OverworldTileGrid grid, int ax, int az, int dx, int dz)
        {
            // Canonical form: the lower cell along the axis, so both sides agree on the id.
            int axis;
            if (dx != 0) { axis = 0; if (dx < 0) ax -= 1; }
            else { axis = 1; if (dz < 0) az -= 1; }

            return ((long)((az + 1) * (grid.Width + 2) + (ax + 1)) << 1) | (uint)axis;
        }

        // ---------------------------------------------------------------- edges

        private static void PlanEdges(OverworldTileGrid grid, List<TilePlacement> list,
                                      HashSet<long> covered)
        {
            // Vertical edges (between (x,z) and (x+1,z)), including both map borders.
            for (int z = 0; z < grid.Height; z++)
                for (int x = -1; x < grid.Width; x++)
                    PlanEdge(grid, list, covered, x, z, 1, 0);

            // Horizontal edges (between (x,z) and (x,z+1)).
            for (int x = 0; x < grid.Width; x++)
                for (int z = -1; z < grid.Height; z++)
                    PlanEdge(grid, list, covered, x, z, 0, 1);
        }

        private static void PlanEdge(OverworldTileGrid grid, List<TilePlacement> list,
                                     HashSet<long> covered, int ax, int az, int dx, int dz)
        {
            int bx = ax + dx;
            int bz = az + dz;

            var kindA = grid.KindAt(ax, az);
            var kindB = grid.KindAt(bx, bz);
            if (kindA == TileCellKind.Sea && kindB == TileCellKind.Sea) return;
            if (covered.Contains(EdgeKey(grid, ax, az, dx, dz))) return;

            var mid = EdgeMid(grid, ax, az, dx, dz);

            // Shore or cliff-into-sea.
            if (kindA == TileCellKind.Sea || kindB == TileCellKind.Sea)
            {
                bool aIsLand = kindA != TileCellKind.Sea;
                int lx = aIsLand ? ax : bx;
                int lz = aIsLand ? az : bz;
                int sdx = aIsLand ? dx : -dx;   // land → sea direction
                int sdz = aIsLand ? dz : -dz;

                int landEdge = FaceEdgeLevel(grid, lx, lz, sdx, sdz);
                if (landEdge <= 0)
                    Add(list, OverworldTilePiece.ShoreEdge, new Vector3(mid.x, 0f, mid.y),
                        FrontYaw(sdx, sdz));
                else if (landEdge % OverworldTileGrid.RampRun == 0)
                    EmitFaces(list, mid, landEdge / OverworldTileGrid.RampRun, 0,
                              FrontYaw(sdx, sdz), new Vector2Int(sdx, sdz));
                return;
            }

            int edgeA = FaceEdgeLevel(grid, ax, az, dx, dz);
            int edgeB = FaceEdgeLevel(grid, bx, bz, -dx, -dz);
            if (edgeA == edgeB) return;

            // Fractional heights only occur along a run's interior, where consecutive cells
            // match exactly — a mismatch involving a fraction has nothing whole to draw.
            int hi = Mathf.Max(edgeA, edgeB);
            int lo = Mathf.Min(edgeA, edgeB);
            if (hi % OverworldTileGrid.RampRun != 0 || lo % OverworldTileGrid.RampRun != 0) return;

            bool aHigh = edgeA > edgeB;
            var front = aHigh ? new Vector2Int(dx, dz) : new Vector2Int(-dx, -dz);
            EmitFaces(list, mid, hi / OverworldTileGrid.RampRun, lo / OverworldTileGrid.RampRun,
                      FrontYaw(front.x, front.y), front);
        }

        /// <summary>
        /// The height a cell presents to cliff-face planning at one edge, in 1/RampRun level
        /// units. Sea presents 0; ground its level; run cell i presents +i / +i+1 at its low and
        /// high edges and its base level at its sides — the cheek covers the wedge band, faces
        /// stack below.
        /// </summary>
        private static int FaceEdgeLevel(OverworldTileGrid grid, int x, int z, int dx, int dz)
        {
            switch (grid.KindAt(x, z))
            {
                case TileCellKind.Sea: return 0;
                case TileCellKind.Ground: return grid.LevelAt(x, z) * OverworldTileGrid.RampRun;
                default:
                {
                    int scaled = grid.LevelAt(x, z) * OverworldTileGrid.RampRun;
                    var facing = FacingDir(grid.FacingAt(x, z));
                    bool towardHigh = facing.x == dx && facing.y == dz;
                    bool towardLow = facing.x == -dx && facing.y == -dz;
                    if (!towardHigh && !towardLow) return scaled;   // side

                    return scaled + grid.RampIndexAt(x, z) + (towardHigh ? 1 : 0);
                }
            }
        }

        /// <summary>Faces for a drop from <paramref name="top"/> down to <paramref name="bottom"/>,
        /// split greedily top-down into 3s — the tall stratum rides at plateau eye level and the
        /// remainder reads as a talus band at the foot. Stacked upper pieces step 0.01 m toward
        /// the low side per tier: an upper piece's skirt lies on the same plane as the front of
        /// the piece below it, and coplanar same-facing quads boil while the camera moves.</summary>
        private static void EmitFaces(List<TilePlacement> list, Vector2 mid, int top, int bottom,
                                      float yaw, Vector2Int front)
        {
            int pieces = (top - bottom + 2) / 3;
            int level = top;
            int emitted = 0;

            while (level > bottom)
            {
                int h = Mathf.Min(3, level - bottom);
                level -= h;

                float proud = 0.01f * (pieces - 1 - emitted);
                Add(list, FacePiece(h),
                    new Vector3(mid.x + front.x * proud, level * Step, mid.y + front.y * proud),
                    yaw);
                emitted++;
            }
        }

        // ---------------------------------------------------------------- corners

        private static void PlanCorners(OverworldTileGrid grid, List<TilePlacement> list)
        {
            var levels = new int[4];     // cell (i, j) at index j * 2 + i
            var land = new bool[4];

            for (int cz = 0; cz <= grid.Height; cz++)
            {
                for (int cx = 0; cx <= grid.Width; cx++)
                {
                    bool anyLand = false;
                    int min = int.MaxValue, max = int.MinValue;

                    for (int j = 0; j < 2; j++)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            int x = cx - 1 + i;
                            int z = cz - 1 + j;
                            bool isLand = grid.KindAt(x, z) != TileCellKind.Sea;
                            int level = isLand ? grid.CornerLevelOf(x, z, 1 - i, 1 - j) : 0;

                            land[j * 2 + i] = isLand;
                            levels[j * 2 + i] = level;
                            anyLand |= isLand;
                            if (level < min) min = level;
                            if (level > max) max = level;
                        }
                    }

                    if (!anyLand) continue;

                    var point = grid.CornerPoint(cx, cz);

                    if (max > min) PlanCliffBands(list, point, levels, min, max);
                    else if (max == 0) PlanShoreCorner(list, point, land);
                }
            }
        }

        /// <summary>
        /// The band rule: between consecutive levels, the 2×2 high/low occupancy is straight
        /// (faces butt — nothing), one high (outer post), three high (inner post), or diagonal
        /// (two outer posts back to back). Runs of bands with the same occupancy merge and take
        /// the same greedy height split as faces.
        /// </summary>
        private static void PlanCliffBands(List<TilePlacement> list, Vector2 point,
                                           int[] levels, int min, int max)
        {
            int runStart = min;
            int runMask = HighMask(levels, min);

            for (int b = min + 1; b <= max; b++)
            {
                int mask = b < max ? HighMask(levels, b) : -1;
                if (mask == runMask) continue;

                EmitPosts(list, point, runMask, runStart, b - runStart);
                runStart = b;
                runMask = mask;
            }
        }

        private static int HighMask(int[] levels, int band)
        {
            int mask = 0;
            for (int c = 0; c < 4; c++)
                if (levels[c] >= band + 1) mask |= 1 << c;
            return mask;
        }

        private static void EmitPosts(List<TilePlacement> list, Vector2 point, int mask,
                                      int foot, int height)
        {
            int count = CountBits(mask);
            if (count == 0 || count == 4) return;

            bool diagonal = mask == 0b1001 || mask == 0b0110;
            if (count == 2 && !diagonal) return;   // straight — collinear faces butt cleanly

            // Split the run greedily top-down, then place bottom-up. Stacked pieces step 0.01 m
            // toward their low side per tier — the same coplanar-skirt rule as stacked faces.
            int pieces = (height + 2) / 3;
            int level = foot + height;
            int emitted = 0;

            while (level > foot)
            {
                int h = Mathf.Min(3, level - foot);
                level -= h;
                float proud = 0.01f * (pieces - 1 - emitted);
                emitted++;

                if (count == 3)
                {
                    int lowCell = ~mask & 0b1111;
                    var low = Quadrant(SingleQuadrant(lowCell));
                    Add(list, InnerPiece(h),
                        new Vector3(point.x + low.x * proud, level * Step, point.y + low.y * proud),
                        InnerYaw(SingleQuadrant(lowCell)));
                }
                else
                {
                    for (int c = 0; c < 4; c++)
                        if ((mask & (1 << c)) != 0)
                        {
                            var q = Quadrant(c);
                            Add(list, OuterPiece(h),
                                new Vector3(point.x - q.x * proud, level * Step, point.y - q.y * proud),
                                OuterYaw(q));
                        }
                }
            }
        }

        /// <summary>Shore corners exist only where every land cell at the corner is level 0 —
        /// any higher land already produced cliff posts whose skirts own the junction.</summary>
        private static void PlanShoreCorner(List<TilePlacement> list, Vector2 point, bool[] land)
        {
            int mask = 0;
            for (int c = 0; c < 4; c++)
                if (land[c]) mask |= 1 << c;

            int count = CountBits(mask);
            if (count == 4) return;

            bool diagonal = mask == 0b1001 || mask == 0b0110;
            if (count == 2 && !diagonal) return;

            // Three land, one sea notch: NOTHING. The two shore edges' banks intersect in a
            // clean valley along the corner diagonal — the union already is the inside corner,
            // and a patch over it would be coplanar with both banks.
            if (count == 3) return;

            for (int c = 0; c < 4; c++)
                if ((mask & (1 << c)) != 0)
                    Add(list, OverworldTilePiece.ShoreOuterPost,
                        new Vector3(point.x, 0f, point.y), OuterYaw(Quadrant(c)));
        }

        // ---------------------------------------------------------------- water

        /// <summary>Sea level is a world constant, deliberately NOT derived from the step: the
        /// shore pieces' bank and skirt are sculpted around this waterline, and a step retune
        /// must move the cliffs, not drain the sea out from under the beaches.</summary>
        private const float WaterY = -0.35f;

        private static void PlanWater(OverworldTileGrid grid, List<TilePlacement> list)
        {
            var centre = grid.Origin + new Vector2(grid.Width, grid.Height) * (Cell * 0.5f);

            list.Add(new TilePlacement
            {
                Piece = OverworldTilePiece.Water,
                Position = new Vector3(centre.x, WaterY, centre.y),
                Yaw = 0f,
                Scale = new Vector3(grid.Width * Cell + 8f, 1f, grid.Height * Cell + 8f),
            });
        }

        // ---------------------------------------------------------------- orientation helpers

        private static Vector2Int FacingDir(RampFacing facing)
        {
            switch (facing)
            {
                case RampFacing.PlusZ: return Vector2Int.up;
                case RampFacing.PlusX: return Vector2Int.right;
                case RampFacing.MinusZ: return Vector2Int.down;
                default: return Vector2Int.left;
            }
        }

        /// <summary>Yaw that points a canonical −Z front along (dx, dz).</summary>
        private static float FrontYaw(int dx, int dz)
        {
            if (dz < 0) return 0f;
            if (dz > 0) return 180f;
            return dx < 0 ? 90f : 270f;
        }

        /// <summary>Yaw 90° maps (dx, dz) → (dz, −dx) — the +X+Z quadrant to +X−Z.</summary>
        private static Vector2Int Rot90(Vector2Int d) => new Vector2Int(d.y, -d.x);

        /// <summary>
        /// Solve yaw and mirror for an edge piece authored with front −Z and tall end −X. The
        /// eight (yaw, mirror) poses cover both walls of a cutting; only the cheek ever needs
        /// the mirrored four.
        /// </summary>
        private static void Orient(Vector2Int front, Vector2Int tall, out float yaw, out bool mirror)
        {
            for (int m = 0; m < 2; m++)
            {
                var f = new Vector2Int(0, -1);
                var t = new Vector2Int(m == 0 ? -1 : 1, 0);

                for (int k = 0; k < 4; k++)
                {
                    if (f == front && t == tall)
                    {
                        yaw = 90f * k;
                        mirror = m == 1;
                        return;
                    }

                    f = Rot90(f);
                    t = Rot90(t);
                }
            }

            yaw = 0f;
            mirror = false;
        }

        private static Vector2Int Quadrant(int cell) =>
            new Vector2Int((cell & 1) == 1 ? 1 : -1, (cell & 2) == 2 ? 1 : -1);

        private static int SingleQuadrant(int mask)
        {
            for (int c = 0; c < 4; c++)
                if ((mask & (1 << c)) != 0) return c;
            return 0;
        }

        /// <summary>Outer posts are authored with the high quadrant at +X+Z.</summary>
        private static float OuterYaw(Vector2Int highQuadrant)
        {
            if (highQuadrant.x > 0) return highQuadrant.y > 0 ? 0f : 90f;
            return highQuadrant.y > 0 ? 270f : 180f;
        }

        /// <summary>Inner posts are authored with the low (notch) quadrant at −X−Z.</summary>
        private static float InnerYaw(int lowCell)
        {
            var q = Quadrant(lowCell);
            if (q.x < 0) return q.y < 0 ? 0f : 90f;
            return q.y < 0 ? 270f : 180f;
        }

        private static Vector2 EdgeMid(OverworldTileGrid grid, int ax, int az, int dx, int dz)
        {
            // Canonicalize negative directions (the cheek pass asks for ±1 sides; the edge pass
            // only ever asks +1). Without this, a west or south side resolves one whole cell too
            // far — a cheek placed INSIDE the wall is a hole where a wall should be.
            if (dx < 0) { ax -= 1; dx = 1; }
            if (dz < 0) { az -= 1; dz = 1; }

            var corner = grid.CornerPoint(ax + dx, az + dz);
            return dx != 0
                ? new Vector2(corner.x, corner.y + Cell * 0.5f)
                : new Vector2(corner.x + Cell * 0.5f, corner.y);
        }

        private static OverworldTilePiece FacePiece(int h) =>
            h == 1 ? OverworldTilePiece.Face1
            : h == 2 ? OverworldTilePiece.Face2
            : OverworldTilePiece.Face3;

        private static OverworldTilePiece OuterPiece(int h) =>
            h == 1 ? OverworldTilePiece.OuterPost1
            : h == 2 ? OverworldTilePiece.OuterPost2
            : OverworldTilePiece.OuterPost3;

        private static OverworldTilePiece InnerPiece(int h) =>
            h == 1 ? OverworldTilePiece.InnerPost1
            : h == 2 ? OverworldTilePiece.InnerPost2
            : OverworldTilePiece.InnerPost3;

        private static int CountBits(int mask)
        {
            int count = 0;
            for (int c = 0; c < 4; c++)
                if ((mask & (1 << c)) != 0) count++;
            return count;
        }

        private static void Add(List<TilePlacement> list, OverworldTilePiece piece,
                                Vector3 position, float yaw)
        {
            list.Add(new TilePlacement
            {
                Piece = piece,
                Position = position,
                Yaw = yaw,
                Scale = Vector3.one,
            });
        }
    }
}
