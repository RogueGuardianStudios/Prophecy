using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Which kind of surface a triangle belongs to. One vocabulary for the whole set, so the
    /// same colour means the same thing on every UV guide and a future trim-sheet or triplanar
    /// pass can key off it.
    /// </summary>
    internal enum TileRegion
    {
        Top,    // walkable: caps, treads, the ramp grade
        Face,   // vertical rock: cliff fronts, wedge sides, stair side columns
        Rim,    // the ALttP lip along a cliff/cheek top edge
        Skirt,  // buried/underwater continuation below the footline
        Riser,  // stair fronts
        Bank,   // shore slope
        Trim,   // hidden or mating surfaces: backs, bottoms, flat cut ends
        Water,  // the sea plane
    }

    /// <summary>One entry of a tile's UV guide: a region, where it sits in UV space, and what maps there.</summary>
    internal struct RegionNote
    {
        public TileRegion Region;
        public Rect UvRect;
        public string What;
    }

    /// <summary>A built tile: geometry, per-triangle regions, and the notes the guide is written from.</summary>
    internal sealed class TileMeshData
    {
        public string Id;
        public string Display;
        public string WorldNote;
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();
        public readonly List<TileRegion> TriangleRegions = new List<TileRegion>();
        public readonly List<RegionNote> Notes = new List<RegionNote>();

        public void CopyInto(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(Vertices);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    /// <summary>
    /// Builds every overworld tile mesh from the dimensions in <c>Plans/Overworld-Tile-Set.md</c>.
    ///
    /// <para>All flat-shaded: every face owns its vertices, so <c>RecalculateNormals</c> yields
    /// hard edges. Faces are axis-aligned wherever the contract allows, which is also the best
    /// case for a triplanar shader if texturing goes that way instead of painted UVs.</para>
    ///
    /// <para>Canonical orientations per the contract: edge pieces front toward −Z, ramps ascend
    /// toward +Z, corner posts keep the high/land quadrant at +X+Z. Only the cheek mirrors.</para>
    /// </summary>
    internal static class OverworldTileGeometry
    {
        // The contract's constants. Step comes from the tile grid — ONE source of truth for the
        // terrace height, because a geometry step and a data step that drift apart put the walls
        // in different places than the walkability refusals.
        private const float Step = Rokkan.Prophecy.Overworld.OverworldTileGrid.Step;
        private const int RampRun = Rokkan.Prophecy.Overworld.OverworldTileGrid.RampRun;
        private const float RampRise = Step / RampRun;   // one run cell's climb

        /// <summary>How far below its own foot plane a run piece's sides and underside reach:
        /// the run's UPPER cells float above the low terrace by up to (RampRun−1) rises, and
        /// without this skirt their open flanks show the void (and the sea) through every slot
        /// where a run meets a wall. On the foot cell the skirt simply buries.</summary>
        private const float RunSkirt = RampRise * (RampRun - 1) + BaseT;
        private const float RimH = 0.20f;      // lip height above the high cap
        private const float RimOver = 0.15f;   // lip overlap onto the high cap (+Z)
        private const float RimProud = 0.08f;  // lip proud of the face (−Z)
        private const float SkirtD = 0.5f;     // buried under-skirt below the footline
        private const float WallT = 0.15f;     // wall body thickness behind the face plane
        private const float CapT = 0.15f;      // ground slab thickness
        private const float BaseT = 0.15f;     // ramp/stair base slab (no knife edge at the foot)
        private const float CheekSkirt = 0.2f; // skirt below the cheek's diagonal
        private const float PostBulge = 0.25f; // outer post bulge into the low quadrants
        private const float NotchBulge = 0.2f; // inner post bulge into the notch
        private const float PostRimH = 0.24f;  // post lips ride proud of face lips — flush
                                               // rims are coplanar rim tops, which z-fight
        private const float BankDrop = 0.4f;   // shore bank vertical drop (45°)
        private const float BankReach = 0.4f;  // shore bank horizontal reach into the sea cell
        private const float ShoreBottom = -0.9f;

        public static List<TileMeshData> BuildAll()
        {
            var tiles = new List<TileMeshData>
            {
                BuildCap(),
                BuildRamp(),
                BuildStairs(),
                BuildWater(),
            };

            for (int n = 1; n <= 3; n++) tiles.Add(BuildFace(n));
            for (int n = 1; n <= 3; n++) tiles.Add(BuildCornerPost(n, PostBulge, false));
            for (int n = 1; n <= 3; n++) tiles.Add(BuildCornerPost(n, NotchBulge, true));
            tiles.Add(BuildCheek(false));
            tiles.Add(BuildCheek(true));
            tiles.Add(BuildShoreEdge());
            tiles.Add(BuildShoreCape());

            return tiles;
        }

        // ---------------------------------------------------------------- builder plumbing

        private sealed class B
        {
            public readonly TileMeshData Data = new TileMeshData();

            /// <summary>
            /// One quad, corners named as seen from OUTSIDE the tile: bottom-left, top-left,
            /// top-right, bottom-right. UVs map bl→(min,min), tr→(max,max). If the winding
            /// disagrees with <paramref name="outward"/> the quad is mirrored so it can never
            /// face inward — a backwards face renders as a hole, which is the silent failure.
            /// </summary>
            public void Quad(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br,
                             TileRegion region, Rect uv, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(tl - bl, tr - bl), outward) < 0f)
                {
                    (bl, br) = (br, bl);
                    (tl, tr) = (tr, tl);
                }

                int i = Data.Vertices.Count;
                Data.Vertices.Add(bl); Data.Vertices.Add(tl);
                Data.Vertices.Add(tr); Data.Vertices.Add(br);
                Data.Uvs.Add(new Vector2(uv.xMin, uv.yMin));
                Data.Uvs.Add(new Vector2(uv.xMin, uv.yMax));
                Data.Uvs.Add(new Vector2(uv.xMax, uv.yMax));
                Data.Uvs.Add(new Vector2(uv.xMax, uv.yMin));
                Data.Triangles.Add(i); Data.Triangles.Add(i + 1); Data.Triangles.Add(i + 2);
                Data.Triangles.Add(i); Data.Triangles.Add(i + 2); Data.Triangles.Add(i + 3);
                Data.TriangleRegions.Add(region);
                Data.TriangleRegions.Add(region);
            }

            /// <summary>An axis-aligned face: centre, unit right/up directions, size. Outward is cross(up, right).</summary>
            public void AxisQuad(Vector3 centre, Vector3 right, Vector3 up, float w, float h,
                                 TileRegion region, Rect uv)
            {
                Vector3 rw = right * (w * 0.5f);
                Vector3 uh = up * (h * 0.5f);
                Quad(centre - rw - uh, centre - rw + uh, centre + rw + uh, centre + rw - uh,
                     region, uv, Vector3.Cross(up, right));
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc,
                            TileRegion region, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                {
                    (b, c) = (c, b);
                    (ub, uc) = (uc, ub);
                }

                int i = Data.Vertices.Count;
                Data.Vertices.Add(a); Data.Vertices.Add(b); Data.Vertices.Add(c);
                Data.Uvs.Add(ua); Data.Uvs.Add(ub); Data.Uvs.Add(uc);
                Data.Triangles.Add(i); Data.Triangles.Add(i + 1); Data.Triangles.Add(i + 2);
                Data.TriangleRegions.Add(region);
            }

            public void Note(TileRegion region, Rect uv, string what) =>
                Data.Notes.Add(new RegionNote { Region = region, UvRect = uv, What = what });
        }

        private static Rect R(float x0, float y0, float x1, float y1) =>
            new Rect(x0, y0, x1 - x0, y1 - y0);

        /// <summary>Cell i of n across a rect, with a small gutter so islands stay distinct.</summary>
        private static Rect CellX(Rect r, int i, int n)
        {
            const float gutter = 0.006f;
            float w = r.width / n;
            return new Rect(r.xMin + w * i + gutter, r.yMin, w - gutter * 2f, r.height);
        }

        // ---------------------------------------------------------------- cell pieces

        /// <summary>Flat ground cap. Pivot: centre of footprint at the TOP surface.</summary>
        private static TileMeshData BuildCap()
        {
            var b = new B();
            b.Data.Id = "GRD_Cap";
            b.Data.Display = "ground cap, flat 1×1 m slab";
            b.Data.WorldNote = "Placed at every Ground land cell, top at level × 0.7. One cap serves all levels.";

            Rect top = R(0.01f, 0.26f, 0.99f, 0.99f);
            Rect sides = R(0.01f, 0.14f, 0.99f, 0.24f);
            Rect bottom = R(0.01f, 0.01f, 0.99f, 0.12f);
            b.Note(TileRegion.Top, top, "walk surface, 1.0 × 1.0 m — the region biome materials repaint");
            b.Note(TileRegion.Face, sides, "four slab edges — hidden between caps, but a bridge " +
                                           "deck's edge and a cave lintel wear them in the open");
            b.Note(TileRegion.Face, bottom, "underside — visible beneath bridge decks and overhangs");

            b.AxisQuad(new Vector3(0f, 0f, 0f), Vector3.right, Vector3.forward, 1f, 1f, TileRegion.Top, top);

            // The sides tuck 0.02 INSIDE the cell boundary. On the boundary plane itself they are
            // coplanar-and-same-facing with whatever covers that edge — a cliff face's front, a
            // cheek — and the slab's dark edge z-fights through it as a boiling stipple. Inset,
            // the covering piece owns the plane alone; between same-level caps the two insets
            // face each other inside the seam, and the full-size top hides the ring from above.
            const float inset = 0.02f;
            float mid = -CapT * 0.5f;
            float edge = 0.5f - inset;
            b.AxisQuad(new Vector3(0f, mid, -edge), Vector3.right, Vector3.up, 1f, CapT, TileRegion.Face, CellX(sides, 0, 4));
            b.AxisQuad(new Vector3(0f, mid, edge), -Vector3.right, Vector3.up, 1f, CapT, TileRegion.Face, CellX(sides, 1, 4));
            b.AxisQuad(new Vector3(-edge, mid, 0f), -Vector3.forward, Vector3.up, 1f, CapT, TileRegion.Face, CellX(sides, 2, 4));
            b.AxisQuad(new Vector3(edge, mid, 0f), Vector3.forward, Vector3.up, 1f, CapT, TileRegion.Face, CellX(sides, 3, 4));
            b.AxisQuad(new Vector3(0f, -CapT, 0f), -Vector3.right, Vector3.forward, 1f, 1f, TileRegion.Face, bottom);

            return b.Data;
        }

        /// <summary>Solid wedge rising one RUN CELL's worth — Step/RampRun over the cell, so a
        /// full climb is RampRun of these chained at rising heights. Pivot: footprint centre at
        /// the cell's own foot plane; ascends +Z.</summary>
        private static TileMeshData BuildRamp()
        {
            var b = new B();
            b.Data.Id = "RMP_Ramp";
            b.Data.Display = $"ramp run cell, solid wedge rising 0 → {RampRise:0.#} m along +Z";
            b.Data.WorldNote = $"One cell of a {RampRun}-cell climb; the planner chains them. " +
                               "Sides are the closed cheeks of register #8.";

            Rect top = R(0.01f, 0.44f, 0.99f, 0.99f);
            Rect side = R(0.01f, 0.12f, 0.99f, 0.42f);
            Rect trim = R(0.01f, 0.01f, 0.99f, 0.10f);
            b.Note(TileRegion.Top, top, "the grade, 1.0 m wide");
            b.Note(TileRegion.Face, side, "closed wedge sides, one cell each (left −X side, right +X side)");
            b.Note(TileRegion.Trim, trim, "foot edge, head face, underside (left to right)");

            b.Quad(new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, RampRise, 0.5f),
                   new Vector3(0.5f, RampRise, 0.5f), new Vector3(0.5f, 0f, -0.5f),
                   TileRegion.Top, top, new Vector3(0f, 1f, -RampRise));

            // Side profiles, skirted RunSkirt deep — see the constant.
            b.Quad(new Vector3(0.5f, -RunSkirt, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                   new Vector3(0.5f, RampRise, 0.5f), new Vector3(0.5f, -RunSkirt, 0.5f),
                   TileRegion.Face, CellX(side, 1, 2), Vector3.right);
            b.Quad(new Vector3(-0.5f, -RunSkirt, 0.5f), new Vector3(-0.5f, RampRise, 0.5f),
                   new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, -RunSkirt, -0.5f),
                   TileRegion.Face, CellX(side, 0, 2), -Vector3.right);

            b.AxisQuad(new Vector3(0f, -RunSkirt * 0.5f, -0.5f), Vector3.right, Vector3.up, 1f, RunSkirt,
                       TileRegion.Trim, CellX(trim, 0, 3));
            b.AxisQuad(new Vector3(0f, (RampRise - RunSkirt) * 0.5f, 0.5f), -Vector3.right, Vector3.up, 1f, RampRise + RunSkirt,
                       TileRegion.Trim, CellX(trim, 1, 3));
            b.AxisQuad(new Vector3(0f, -RunSkirt, 0f), -Vector3.right, Vector3.forward, 1f, 1f,
                       TileRegion.Trim, CellX(trim, 2, 3));

            return b.Data;
        }

        /// <summary>Same envelope as the ramp, top stepped. Eight risers so each stays under the
        /// NavMesh agentClimb (0.45) at the 3 m step — the treads are flat, so a staircase
        /// routes where the 72° ramp cannot. Ascends +Z.</summary>
        private static TileMeshData BuildStairs()
        {
            const int steps = 8;

            var b = new B();
            b.Data.Id = "RMP_Stairs";
            b.Data.Display = $"stair run cell, {steps} risers of {RampRise / steps:0.###} m along +Z";
            b.Data.WorldNote = "The walkable level change — interchangeable with RMP_Ramp on any run cell.";

            float riserH = RampRise / steps;
            float treadD = 1f / steps;

            Rect treads = R(0.01f, 0.74f, 0.99f, 0.99f);
            Rect risers = R(0.01f, 0.46f, 0.99f, 0.71f);
            Rect side = R(0.01f, 0.14f, 0.99f, 0.43f);
            Rect trim = R(0.01f, 0.01f, 0.99f, 0.12f);
            b.Note(TileRegion.Top, treads, $"{steps} treads, foot to head left to right");
            b.Note(TileRegion.Riser, risers, $"{steps} risers, foot to head left to right");
            b.Note(TileRegion.Face, side, "side columns under each tread (both sides share cells, mirrored)");
            b.Note(TileRegion.Trim, trim, "foot edge, head face, underside (left to right)");

            for (int i = 0; i < steps; i++)
            {
                float z0 = -0.5f + treadD * i;
                float topY = riserH * (i + 1);

                b.AxisQuad(new Vector3(0f, topY - riserH * 0.5f, z0), Vector3.right, Vector3.up,
                           1f, riserH, TileRegion.Riser, CellX(risers, i, steps));
                b.AxisQuad(new Vector3(0f, topY, z0 + treadD * 0.5f), Vector3.right, Vector3.forward,
                           1f, treadD, TileRegion.Top, CellX(treads, i, steps));

                // Side columns: distinct z-ranges, so nothing coplanar overlaps (no z-fighting).
                // Skirted RunSkirt deep so an upper run cell's flanks reach the ground below.
                float colH = topY + RunSkirt;
                float colMid = (topY - RunSkirt) * 0.5f;
                b.AxisQuad(new Vector3(0.5f, colMid, z0 + treadD * 0.5f), Vector3.forward, Vector3.up,
                           treadD, colH, TileRegion.Face, CellX(side, i, steps));
                b.AxisQuad(new Vector3(-0.5f, colMid, z0 + treadD * 0.5f), -Vector3.forward, Vector3.up,
                           treadD, colH, TileRegion.Face, CellX(side, i, steps));
            }

            b.AxisQuad(new Vector3(0f, -RunSkirt * 0.5f, -0.5f), Vector3.right, Vector3.up, 1f, RunSkirt,
                       TileRegion.Trim, CellX(trim, 0, 3));
            b.AxisQuad(new Vector3(0f, (RampRise - RunSkirt) * 0.5f, 0.5f), -Vector3.right, Vector3.up, 1f, RampRise + RunSkirt,
                       TileRegion.Trim, CellX(trim, 1, 3));
            b.AxisQuad(new Vector3(0f, -RunSkirt, 0f), -Vector3.right, Vector3.forward, 1f, 1f,
                       TileRegion.Trim, CellX(trim, 2, 3));

            return b.Data;
        }

        /// <summary>The sea. Pivot at the surface; the renderer places it at −0.35.</summary>
        private static TileMeshData BuildWater()
        {
            var b = new B();
            b.Data.Id = "WTR_Fill";
            b.Data.Display = "water cell, flat 1×1 m plane";
            b.Data.WorldNote = "Sea cells; the renderer may merge all of them into one plane and probably should.";

            Rect water = R(0.05f, 0.05f, 0.95f, 0.95f);
            b.Note(TileRegion.Water, water, "the sea surface");
            b.AxisQuad(Vector3.zero, Vector3.right, Vector3.forward, 1f, 1f, TileRegion.Water, water);

            return b.Data;
        }

        // ---------------------------------------------------------------- cliff pieces

        /// <summary>The rim lip along a top edge running down +X. Shared by faces, posts and the cheek.</summary>
        private static void RimAlongX(B b, float topY, float halfW, Rect front, Rect top, Rect back, Rect ends)
        {
            float rimMidY = topY + RimH * 0.5f;
            float rimMidZ = (RimOver - RimProud) * 0.5f;
            float rimD = RimOver + RimProud;

            b.AxisQuad(new Vector3(0f, rimMidY, -RimProud), Vector3.right, Vector3.up, halfW * 2f, RimH,
                       TileRegion.Rim, front);
            b.AxisQuad(new Vector3(0f, topY + RimH, rimMidZ), Vector3.right, Vector3.forward, halfW * 2f, rimD,
                       TileRegion.Rim, top);
            b.AxisQuad(new Vector3(0f, rimMidY, RimOver), -Vector3.right, Vector3.up, halfW * 2f, RimH,
                       TileRegion.Rim, back);
            // Rim ends are visible wherever a run terminates in the open — the jambs of every
            // stair cutting — so they wear the lip's colour, not trim darkness.
            b.AxisQuad(new Vector3(halfW, rimMidY, rimMidZ), Vector3.forward, Vector3.up, rimD, RimH,
                       TileRegion.Rim, ends);
            b.AxisQuad(new Vector3(-halfW, rimMidY, rimMidZ), -Vector3.forward, Vector3.up, rimD, RimH,
                       TileRegion.Rim, ends);

            // The proud strip's underside — without it a low camera sees an unlit slit where the
            // lip overhangs the face.
            b.AxisQuad(new Vector3(0f, topY, -RimProud * 0.5f), -Vector3.right, Vector3.forward,
                       halfW * 2f, RimProud, TileRegion.Rim, back);
        }

        /// <summary>Straight cliff face, n steps. Pivot: edge midpoint at the footline; front −Z.</summary>
        private static TileMeshData BuildFace(int n)
        {
            float h = n * Step;
            var b = new B();
            b.Data.Id = $"CLF_Face{n}";
            b.Data.Display = $"cliff face, straight, {n} step{(n > 1 ? "s" : "")} ({h:0.0} m)";
            b.Data.WorldNote = "Placed on any differing cell edge after the greedy 3-split, yawed to face the low side.";

            Rect rimFront = R(0.01f, 0.955f, 0.99f, 0.995f);
            Rect rimTop = R(0.01f, 0.910f, 0.99f, 0.950f);
            Rect rimBack = R(0.01f, 0.870f, 0.99f, 0.905f);
            Rect face = R(0.01f, 0.42f, 0.99f, 0.86f);
            Rect skirt = R(0.01f, 0.22f, 0.99f, 0.40f);
            Rect trim = R(0.01f, 0.01f, 0.99f, 0.20f);
            b.Note(TileRegion.Rim, R(0.01f, 0.87f, 0.99f, 0.995f),
                   "the lip: front, top, back substrips (top to bottom), 1.0 m long");
            b.Note(TileRegion.Face, face, $"the rock face, 1.0 × {h:0.0} m — the main paint surface");
            b.Note(TileRegion.Skirt, skirt, "buried continuation, 1.0 × 0.5 m below the footline");
            b.Note(TileRegion.Trim, trim, "back wall, cut ends, underside, rim ends (left to right)");

            b.AxisQuad(new Vector3(0f, h * 0.5f, 0f), Vector3.right, Vector3.up, 1f, h, TileRegion.Face, face);
            b.AxisQuad(new Vector3(0f, -SkirtD * 0.5f, 0f), Vector3.right, Vector3.up, 1f, SkirtD, TileRegion.Skirt, skirt);
            RimAlongX(b, h, 0.5f, rimFront, rimTop, rimBack, CellX(trim, 4, 5));

            float bodyMidY = (h - SkirtD) * 0.5f;
            float bodyH = h + SkirtD;
            b.AxisQuad(new Vector3(0f, bodyMidY, WallT), -Vector3.right, Vector3.up, 1f, bodyH,
                       TileRegion.Trim, CellX(trim, 0, 5));

            // Side cut ends wear the rock face colour: in a run they butt invisibly, but
            // wherever the cliff line steps DOWN in height the taller piece's end is exposed —
            // trim-dark there reads as a hole in the wall.
            b.AxisQuad(new Vector3(0.5f, bodyMidY, WallT * 0.5f), Vector3.forward, Vector3.up, WallT, bodyH,
                       TileRegion.Face, face);
            b.AxisQuad(new Vector3(-0.5f, bodyMidY, WallT * 0.5f), -Vector3.forward, Vector3.up, WallT, bodyH,
                       TileRegion.Face, face);
            b.AxisQuad(new Vector3(0f, -SkirtD, WallT * 0.5f), -Vector3.right, Vector3.forward, 1f, WallT,
                       TileRegion.Trim, CellX(trim, 3, 5));

            return b.Data;
        }

        /// <summary>
        /// Convex or concave corner post, n steps. Pivot: the corner line at the band foot; the
        /// high (or land) quadrant is +X+Z, so the two rock faces look −X and −Z and the two flat
        /// planes at +X/+Z sit against the ends of the adjoining straight faces.
        /// </summary>
        private static TileMeshData BuildCornerPost(int n, float bulge, bool inner)
        {
            float h = n * Step;
            var b = new B();
            b.Data.Id = $"CLF_{(inner ? "Inner" : "Outer")}Post{n}";
            b.Data.Display = $"cliff {(inner ? "inner (concave)" : "outer (convex)")} corner post, {n} step{(n > 1 ? "s" : "")}";
            b.Data.WorldNote = inner
                ? "Band rule, three high cells around a low notch. Fills the crease where two faces meet inward."
                : "Band rule, one high cell at the corner. Also placed back-to-back in pairs for diagonal contacts.";

            Rect rimFront = R(0.01f, 0.93f, 0.99f, 0.995f);
            Rect rimTop = R(0.30f, 0.845f, 0.70f, 0.925f);
            Rect face = R(0.01f, 0.42f, 0.99f, 0.82f);
            Rect skirt = R(0.01f, 0.22f, 0.99f, 0.40f);
            Rect trim = R(0.01f, 0.01f, 0.99f, 0.20f);
            b.Note(TileRegion.Rim, R(0.01f, 0.845f, 0.99f, 0.995f),
                   "lip fronts (−Z then −X) on the top strip, lip top cap on the square below");
            b.Note(TileRegion.Face, face, $"the two rock faces, {bulge + WallT:0.00} × {h:0.0} m each (−Z face left, −X face right)");
            b.Note(TileRegion.Skirt, skirt, "buried continuation of both faces, 0.5 m");
            b.Note(TileRegion.Trim, trim, "flat back planes (+Z, +X), underside, rim backs and ends");

            float w = bulge + WallT;                 // footprint span on each axis
            float faceMidY = h * 0.5f;
            float cx = (WallT - bulge) * 0.5f;       // footprint centre offset from the corner line

            // The two outward rock faces and their skirts.
            b.AxisQuad(new Vector3(cx, faceMidY, -bulge), Vector3.right, Vector3.up, w, h,
                       TileRegion.Face, CellX(face, 0, 2));
            b.AxisQuad(new Vector3(cx, -SkirtD * 0.5f, -bulge), Vector3.right, Vector3.up, w, SkirtD,
                       TileRegion.Skirt, CellX(skirt, 0, 2));
            b.AxisQuad(new Vector3(-bulge, faceMidY, cx), -Vector3.forward, Vector3.up, w, h,
                       TileRegion.Face, CellX(face, 1, 2));
            b.AxisQuad(new Vector3(-bulge, -SkirtD * 0.5f, cx), -Vector3.forward, Vector3.up, w, SkirtD,
                       TileRegion.Skirt, CellX(skirt, 1, 2));

            // Flat planes against the adjoining faces' cut ends. Rock-coloured, not trim: at a
            // height transition the taller band's post stands above the lower cap and these
            // planes are in plain view.
            float bodyMidY = (h - SkirtD) * 0.5f;
            float bodyH = h + SkirtD;
            b.AxisQuad(new Vector3(cx, bodyMidY, WallT), -Vector3.right, Vector3.up, w, bodyH,
                       TileRegion.Face, CellX(face, 0, 2));
            b.AxisQuad(new Vector3(WallT, bodyMidY, cx), Vector3.forward, Vector3.up, w, bodyH,
                       TileRegion.Face, CellX(face, 1, 2));
            b.AxisQuad(new Vector3(cx, -SkirtD, cx), -Vector3.right, Vector3.forward, w, w,
                       TileRegion.Trim, CellX(trim, 2, 4));

            // Rim cap: a box over the whole footprint, proud on the two low sides — and 0.02
            // TALLER than the face rims it overlaps, so the knuckle stands clear instead of
            // sharing a coplanar top with them.
            float rimMin = -bulge - RimProud;
            float rimW = WallT - rimMin;
            float rimCx = (WallT + rimMin) * 0.5f;
            float rimMidY = h + PostRimH * 0.5f;
            b.AxisQuad(new Vector3(rimCx, rimMidY, rimMin), Vector3.right, Vector3.up, rimW, PostRimH,
                       TileRegion.Rim, CellX(rimFront, 0, 2));
            b.AxisQuad(new Vector3(rimMin, rimMidY, rimCx), -Vector3.forward, Vector3.up, rimW, PostRimH,
                       TileRegion.Rim, CellX(rimFront, 1, 2));
            b.AxisQuad(new Vector3(rimCx, h + PostRimH, rimCx), Vector3.right, Vector3.forward, rimW, rimW,
                       TileRegion.Rim, rimTop);
            b.AxisQuad(new Vector3(rimCx, rimMidY, WallT), -Vector3.right, Vector3.up, rimW, PostRimH,
                       TileRegion.Rim, CellX(rimFront, 0, 2));
            b.AxisQuad(new Vector3(WallT, rimMidY, rimCx), Vector3.forward, Vector3.up, rimW, PostRimH,
                       TileRegion.Rim, CellX(rimFront, 1, 2));

            return b.Data;
        }

        /// <summary>
        /// The ramp-cutting retaining wall. Pivot: edge midpoint at the ramp-foot walk level;
        /// front −Z; the tall end at −X (mirrored: +X). The two walls of one cutting are the
        /// piece and its baked twin — never a negative scale, which reverses winding and renders
        /// the mesh inside-out. That artifact happened; the twin is the fix.
        /// </summary>
        private static TileMeshData BuildCheek(bool mirrored)
        {
            var b = new B();
            b.Data.Id = mirrored ? "CLF_CheekM" : "CLF_Cheek";
            b.Data.Display = mirrored
                ? $"notch cutting wall, mirrored twin, {RampRise:0.#} m tall end at +X"
                : $"notch cutting wall, right triangle, {RampRise:0.#} m tall end at −X";
            b.Data.WorldNote = "Flanks the run's TOP cell where it notches into the high " +
                               "terrace. One wall per chirality — a baked pair, not a mirror scale.";

            Rect rimFront = R(0.01f, 0.955f, 0.99f, 0.995f);
            Rect rimTop = R(0.01f, 0.910f, 0.99f, 0.950f);
            Rect rimBack = R(0.01f, 0.870f, 0.99f, 0.905f);
            Rect face = R(0.01f, 0.45f, 0.99f, 0.85f);
            Rect skirt = R(0.01f, 0.24f, 0.99f, 0.43f);
            Rect trim = R(0.01f, 0.01f, 0.99f, 0.22f);
            b.Note(TileRegion.Rim, R(0.01f, 0.87f, 0.99f, 0.995f), "the lip along the horizontal top edge");
            b.Note(TileRegion.Face, face, "the triangular face above the diagonal");
            b.Note(TileRegion.Skirt, skirt, "the 0.2 m strip buried under the ramp surface, following the diagonal");
            b.Note(TileRegion.Trim, trim, "back, both cut ends, sloped underside");

            // Every vertex and outward hint routes through the mirror map; the winding auto-fix
            // in Quad/Tri does the rest.
            Vector3 M(Vector3 v) => mirrored ? new Vector3(-v.x, v.y, v.z) : v;

            // One cell long: it flanks only the recessed top cell, its diagonal matching that
            // cell's rise.
            const float hl = 0.5f;

            // Front: triangle above the ramp line (from (−0.5, 0) up to (+0.5, 0.7)), then the
            // parallelogram skirt strip below it.
            b.Tri(M(new Vector3(-hl, 0f, 0f)), M(new Vector3(-hl, RampRise, 0f)), M(new Vector3(hl, RampRise, 0f)),
                  new Vector2(face.xMin, face.yMin), new Vector2(face.xMin, face.yMax),
                  new Vector2(face.xMax, face.yMax), TileRegion.Face, -Vector3.forward);
            b.Quad(M(new Vector3(-hl, -CheekSkirt, 0f)), M(new Vector3(-hl, 0f, 0f)),
                   M(new Vector3(hl, RampRise, 0f)), M(new Vector3(hl, RampRise - CheekSkirt, 0f)),
                   TileRegion.Skirt, skirt, -Vector3.forward);

            RimAlongX(b, RampRise, hl, rimFront, rimTop, rimBack, CellX(trim, 3, 4));

            // Back: same outline at z = WallT, facing +Z.
            b.Tri(M(new Vector3(-hl, 0f, WallT)), M(new Vector3(-hl, RampRise, WallT)), M(new Vector3(hl, RampRise, WallT)),
                  new Vector2(CellX(trim, 0, 4).xMin, trim.yMin), new Vector2(CellX(trim, 0, 4).xMin, trim.yMax),
                  new Vector2(CellX(trim, 0, 4).xMax, trim.yMax), TileRegion.Trim, Vector3.forward);
            b.Quad(M(new Vector3(-hl, -CheekSkirt, WallT)), M(new Vector3(-hl, 0f, WallT)),
                   M(new Vector3(hl, RampRise, WallT)), M(new Vector3(hl, RampRise - CheekSkirt, WallT)),
                   TileRegion.Trim, CellX(trim, 0, 4), Vector3.forward);

            // Ends (tall then head). These are VISIBLE — the tall end is the jamb at the mouth
            // of every stair cutting — so they wear the rock face region, not trim.
            b.Quad(M(new Vector3(-hl, -CheekSkirt, WallT)), M(new Vector3(-hl, RampRise, WallT)),
                   M(new Vector3(-hl, RampRise, 0f)), M(new Vector3(-hl, -CheekSkirt, 0f)),
                   TileRegion.Face, face, M(new Vector3(-1f, 0f, 0f)));
            b.Quad(M(new Vector3(hl, RampRise - CheekSkirt, 0f)), M(new Vector3(hl, RampRise, 0f)),
                   M(new Vector3(hl, RampRise, WallT)), M(new Vector3(hl, RampRise - CheekSkirt, WallT)),
                   TileRegion.Face, face, M(new Vector3(1f, 0f, 0f)));
            b.Quad(M(new Vector3(-hl, -CheekSkirt, WallT)), M(new Vector3(-hl, -CheekSkirt, 0f)),
                   M(new Vector3(hl, RampRise - CheekSkirt, 0f)), M(new Vector3(hl, RampRise - CheekSkirt, WallT)),
                   TileRegion.Trim, CellX(trim, 2, 4), new Vector3(0f, -1f, 0f));

            return b.Data;
        }

        // ---------------------------------------------------------------- shore pieces

        /// <summary>Straight shore bank. Pivot: edge midpoint at walk level; the sea toward −Z.</summary>
        private static TileMeshData BuildShoreEdge()
        {
            var b = new B();
            b.Data.Id = "SHR_Edge";
            b.Data.Display = "shoreline bank, straight, 45° drop past the waterline";
            b.Data.WorldNote = "Level-0 land against sea. 45°, not gentler, so the NavMesh bake can never walk onto it.";

            Rect bank = R(0.01f, 0.55f, 0.99f, 0.99f);
            Rect skirt = R(0.01f, 0.30f, 0.99f, 0.52f);
            Rect trim = R(0.01f, 0.01f, 0.99f, 0.27f);
            b.Note(TileRegion.Bank, bank, "the bank, 1.0 m wide, dropping 0.4 m over 0.4 m — waterline (−0.35) crosses near its foot");
            b.Note(TileRegion.Skirt, skirt, "underwater continuation down to −0.9");
            b.Note(TileRegion.Trim, trim, "back wall, underside, both cut ends (left to right)");

            b.Quad(new Vector3(-0.5f, -BankDrop, -BankReach), new Vector3(-0.5f, 0f, 0f),
                   new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, -BankDrop, -BankReach),
                   TileRegion.Bank, bank, new Vector3(0f, BankReach, -BankDrop));

            float skirtMidY = (ShoreBottom - BankDrop) * 0.5f;
            float skirtH = -ShoreBottom - BankDrop;
            b.AxisQuad(new Vector3(0f, skirtMidY, -BankReach), Vector3.right, Vector3.up, 1f, skirtH,
                       TileRegion.Skirt, skirt);

            b.AxisQuad(new Vector3(0f, ShoreBottom * 0.5f, 0f), -Vector3.right, Vector3.up, 1f, -ShoreBottom,
                       TileRegion.Trim, CellX(trim, 0, 4));
            b.AxisQuad(new Vector3(0f, ShoreBottom, -BankReach * 0.5f), -Vector3.right, Vector3.forward,
                       1f, BankReach, TileRegion.Trim, CellX(trim, 1, 4));

            // End caps: the cross-section polygon, one per side.
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? 0.5f : -0.5f;
                Vector3 outward = s == 0 ? Vector3.right : -Vector3.right;
                Rect cell = CellX(trim, 2 + s, 4);
                b.Quad(new Vector3(x, ShoreBottom, 0f), new Vector3(x, 0f, 0f),
                       new Vector3(x, -BankDrop, -BankReach), new Vector3(x, ShoreBottom, -BankReach),
                       TileRegion.Trim, cell, outward);
            }

            return b.Data;
        }

        /// <summary>
        /// Shore outside corner — a 45° chamfer wedge filling exactly the quadrant the two
        /// adjoining shore edges leave open. Its straight sides coincide with the edges' end cut
        /// planes, so nothing overlaps and nothing is coplanar — the mitred-quad version of this
        /// piece z-fought both neighbouring banks and jutted a stray triangle into the sea.
        ///
        /// <para>There is deliberately no inner-corner piece at all: two banks meeting inward
        /// already intersect in a clean valley along the diagonal, and any patch laid over them
        /// is coplanar with both by construction. The union IS the corner.</para>
        /// </summary>
        private static TileMeshData BuildShoreCape()
        {
            var b = new B();
            b.Data.Id = "SHR_OuterPost";
            b.Data.Display = "cape, shore outside corner — 45° chamfer";
            b.Data.WorldNote = "One level-0 land cell at the corner. The chamfer's cut sides mate " +
                               "with the adjoining shore edges' end planes.";

            Rect bank = R(0.01f, 0.55f, 0.99f, 0.99f);
            Rect skirt = R(0.01f, 0.30f, 0.99f, 0.52f);
            b.Note(TileRegion.Bank, bank, "the chamfer triangle turning the coast 90° in one facet");
            b.Note(TileRegion.Skirt, skirt, "underwater continuation down to −0.9");

            var apex = new Vector3(0f, 0f, 0f);
            var footA = new Vector3(0f, -BankDrop, -BankReach);
            var footB = new Vector3(-BankReach, -BankDrop, 0f);

            b.Tri(apex, footA, footB,
                  new Vector2((bank.xMin + bank.xMax) * 0.5f, bank.yMax),
                  new Vector2(bank.xMin, bank.yMin), new Vector2(bank.xMax, bank.yMin),
                  TileRegion.Bank, new Vector3(-1f, 1f, -1f));

            b.Quad(new Vector3(0f, ShoreBottom, -BankReach), footA, footB,
                   new Vector3(-BankReach, ShoreBottom, 0f),
                   TileRegion.Skirt, skirt, new Vector3(-1f, 0f, -1f));

            return b.Data;
        }
    }
}
