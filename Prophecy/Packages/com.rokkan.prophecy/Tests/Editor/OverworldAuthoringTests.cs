using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Overworld;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The authoring tool's spine: the shared world builder that the play host and the editor
    /// preview both call. These tests build tiny worlds with a dummy tile set and assert the
    /// CHUNK contract — instances bucket into chunk roots, and a rebuild re-instantiates only
    /// the chunks an edit touched. Instance identity is checked by reference, never by
    /// GetInstanceID (error-level obsolete on 6000.5).
    /// </summary>
    public sealed class OverworldAuthoringTests
    {
        // Objects, not GameObjects: the fixture creates ScriptableObjects too — tile sets, maps,
        // biomes, provinces — and a CreateInstance with no matching destroy is a leak that
        // outlives the test run.
        private readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _cleanup)
                if (obj != null) Object.DestroyImmediate(obj);
            _cleanup.Clear();
        }

        /// <summary>Register anything created for one test so teardown reclaims it.</summary>
        private T Track<T>(T created) where T : Object
        {
            _cleanup.Add(created);
            return created;
        }

        private OverworldTileSet DummyTiles()
        {
            var prefab = Track(new GameObject("DummyTile"));

            var tiles = Track(ScriptableObject.CreateInstance<OverworldTileSet>());
            tiles.Cap = tiles.Ramp = tiles.Stairs = tiles.Water = prefab;
            tiles.Face1 = tiles.Face2 = tiles.Face3 = prefab;
            tiles.OuterPost1 = tiles.OuterPost2 = tiles.OuterPost3 = prefab;
            tiles.InnerPost1 = tiles.InnerPost2 = tiles.InnerPost3 = prefab;
            tiles.Cheek = tiles.CheekMirrored = prefab;
            tiles.ShoreEdge = tiles.ShoreOuterPost = prefab;
            return tiles;
        }

        private OverworldMap PlainMap(float size)
        {
            var map = Track(ScriptableObject.CreateInstance<OverworldMap>());
            map.BoundsSize = new Vector2(size, size);
            map.Regions = new[]
            {
                new AuthoredRegion { Name = "Plain", Centre = Vector2.zero,
                                     Size = new Vector2(size, size), Y = 0f },
            };
            return map;
        }

        private OverworldBuildOutput Build(OverworldMap map)
        {
            var walkable = Track(new GameObject("Walkable")).transform;
            var scenery = Track(new GameObject("Scenery")).transform;

            return OverworldWorldBuilder.Build(map, DummyTiles(), walkable, scenery,
                                               Vector3.zero, true);
        }

        [Test]
        public void TheBuilderBucketsInstancesIntoChunkRoots()
        {
            // 40 cells / 16 per chunk = chunks 0..2 on each axis, ground everywhere: nine
            // chunks, every one populated, and every cap under a chunk root — never loose.
            var output = Build(PlainMap(40f));

            Assert.AreEqual(9, output.Chunks.Count, "40×40 cells span a 3×3 chunk lattice.");

            int caps = 0;
            foreach (var roots in output.Chunks.Values)
            {
                Assert.AreEqual(output.WalkableRoot, roots.Walkable.parent,
                    "Chunk walkable roots hang under the walkable root.");
                caps += roots.Walkable.childCount;
            }
            Assert.AreEqual(40 * 40, caps, "One cap per ground cell, all bucketed.");
            Assert.AreEqual(output.Chunks.Count, output.WalkableRoot.childCount,
                "Only chunk roots hang directly under the walkable root.");
        }

        [Test]
        public void APaintedCellBeatsTheShapesAndTheRiver()
        {
            var map = PlainMap(8f);
            map.Rivers = new[]
            {
                new AuthoredRiver { Name = "Channel",
                                    Points = new[] { new Vector2(0.5f, -4f), new Vector2(0.5f, 4f) },
                                    HalfWidth = 0.6f },
            };
            map.CellOverrides = new[]
            {
                // Land painted back over the carved channel — a ford of solid ground.
                new AuthoredCellOverride { X = 4, Z = 4, Terrain = TerrainOverride.Ground, Level = 0 },
                // And a hand-carved pool with its own water level, off in dry land.
                new AuthoredCellOverride { X = 1, Z = 1, Terrain = TerrainOverride.Sea, Level = 1 },
            };

            // Elevated painted water beside lower land IS the spill case — the warning firing
            // here proves painted water gets the same audit carved water does.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("hangs over lower ground"));

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.AreEqual(TileCellKind.Sea, grid.KindAt(4, 3), "The river still carves…");
            Assert.AreEqual(TileCellKind.Ground, grid.KindAt(4, 4),
                "…except where a painted cell restored the land.");
            Assert.AreEqual(TileCellKind.Sea, grid.KindAt(1, 1), "Painted water carves…");
            Assert.AreEqual(1, grid.LevelAt(1, 1), "…at its own painted water level.");
        }

        [Test]
        public void APaintedTerraceEdgeIsRampable()
        {
            // No region terrace at all: the boundary exists ONLY because painted cells say so,
            // and the authored ramp must still convert across it — overrides run before ramps.
            var map = PlainMap(8f);
            map.CellOverrides = new[]
            {
                new AuthoredCellOverride { X = 4, Z = 5, Terrain = TerrainOverride.Ground, Level = 1 },
                new AuthoredCellOverride { X = 4, Z = 6, Terrain = TerrainOverride.Ground, Level = 1 },
                new AuthoredCellOverride { X = 4, Z = 7, Terrain = TerrainOverride.Ground, Level = 1 },
            };
            map.Ramps = new[]
            {
                new AuthoredRamp { Name = "Up", Start = new Vector2(0.5f, -1f),
                                   End = new Vector2(0.5f, 3f), StartY = 0f,
                                   EndY = OverworldTileGrid.Step, HalfWidth = 0.6f },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.AreEqual(TileCellKind.Ramp, grid.KindAt(4, 4),
                "The run's foot converts on the low side of the painted boundary…");
            Assert.AreEqual(TileCellKind.Ramp, grid.KindAt(4, 5),
                "…and its recessed top notches into the painted terrace.");
        }

        [Test]
        public void RoadOverridesAddAndRemoveAgainstTheCourses()
        {
            var map = PlainMap(8f);
            map.Roads = new[]
            {
                new AuthoredRoad { Name = "Course",
                                   Points = new[] { new Vector2(-4f, 0.5f), new Vector2(4f, 0.5f) },
                                   HalfWidth = 0.4f },
            };
            map.Rivers = new[]
            {
                new AuthoredRiver { Name = "Channel",
                                    Points = new[] { new Vector2(-2.5f, -4f), new Vector2(-2.5f, 4f) },
                                    HalfWidth = 0.4f },
            };
            map.CellOverrides = new[]
            {
                new AuthoredCellOverride { X = 2, Z = 4, Road = RoadOverride.Remove },
                new AuthoredCellOverride { X = 5, Z = 6, Road = RoadOverride.Add },
                // Painting road onto open water is refused, with a warning.
                new AuthoredCellOverride { X = 1, Z = 6, Road = RoadOverride.Add },
            };

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("painted road cell"));

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.IsTrue(grid.RoadAt(3, 4), "The course still paints its strip…");
            Assert.IsFalse(grid.RoadAt(2, 4), "…minus the removed cell.");
            Assert.IsTrue(grid.RoadAt(5, 6), "An added cell paints off the course.");
            Assert.IsFalse(grid.RoadAt(1, 6), "Open water refuses the paint.");
        }

        [Test]
        public void ABlockingPropRefusesItsCellsButNeverTrapsABody()
        {
            var map = PlainMap(8f);
            map.Props = new[]
            {
                new AuthoredProp { Name = "Tree", Position = new Vector2(0.5f, 0.5f),
                                   BlockCells = true, BlockSize = new Vector2Int(2, 1) },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var ground = new TileGridGround(grid);

            // Position (0.5, 0.5) is cell (4, 4); an even width biases positive: (4,4)+(5,4).
            Assert.IsTrue(grid.UnwalkableAt(4, 4) && grid.UnwalkableAt(5, 4),
                "Exactly the 2×1 footprint stamps…");
            Assert.IsFalse(grid.UnwalkableAt(3, 4) || grid.UnwalkableAt(4, 5) || grid.UnwalkableAt(6, 4),
                "…and nothing beyond it.");

            Assert.IsFalse(ground.CanStep(grid.CellCentre(4, 3), grid.CellCentre(4, 4)),
                "Stepping into the prop refuses.");
            Assert.IsTrue(ground.CanStep(grid.CellCentre(4, 4), grid.CellCentre(4, 3)),
                "A body stranded inside may always step out — the escape rule.");
        }

        [Test]
        public void DiagonalsCannotThreadThroughBlockedCorners()
        {
            var map = PlainMap(8f);
            map.Props = new[]
            {
                new AuthoredProp { Name = "PostA", Position = new Vector2(0.5f, -0.5f),
                                   BlockCells = true, BlockSize = Vector2Int.one },   // cell (4,3)
                new AuthoredProp { Name = "PostB", Position = new Vector2(-0.5f, 0.5f),
                                   BlockCells = true, BlockSize = Vector2Int.one },   // cell (3,4)
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var ground = new TileGridGround(grid);

            Assert.IsFalse(ground.CanStep(grid.CellCentre(3, 3), grid.CellCentre(4, 4)),
                "Both L-path corners blocked: the diagonal cannot thread between the posts.");
            Assert.IsTrue(ground.CanStep(grid.CellCentre(4, 4), grid.CellCentre(5, 3)),
                "One open corner is enough — the other L-path carries the step.");
        }

        [Test]
        public void ADeckPropStandsOnTheDeckAndABasePropOnTheGround()
        {
            var map = PlainMap(8f);
            map.Layers = new[]
            {
                new AuthoredLayer { Name = "Deck", Centre = new Vector2(0.5f, 0.5f),
                                    Size = new Vector2(1f, 1f), Y = OverworldTileGrid.Step },
            };

            var prefab = new GameObject("PropPrefab");
            _cleanup.Add(prefab);
            map.Props = new[]
            {
                new AuthoredProp { Name = "OnDeck", Prefab = prefab,
                                   Position = new Vector2(0.5f, 0.5f), SurfaceLayer = 1 },
                new AuthoredProp { Name = "OnGround", Prefab = prefab,
                                   Position = new Vector2(2.5f, 0.5f), SurfaceLayer = 0 },
            };

            var output = Build(map);
            var props = output.PropsObject.transform;

            Assert.AreEqual(2, props.childCount, "Both props spawned under the props root.");
            Assert.AreEqual(OverworldTileGrid.Step, props.GetChild(0).position.y, 0.001f,
                "The deck prop derives the deck's height.");
            Assert.AreEqual(0f, props.GetChild(1).position.y, 0.001f,
                "The ground prop derives the terrain's height.");
        }

        [Test]
        public void TheBrushesSculptRelatively()
        {
            // Raise climbs one level; on water it drains to ground at the water's own level.
            Assert.IsTrue(OverworldPaintBrushes.Apply(PaintBrush.Raise, TileCellKind.Ground, 1, 0,
                                                      out var t, out int l, out _));
            Assert.AreEqual(TerrainOverride.Ground, t);
            Assert.AreEqual(2, l, "Ground raises one terrace.");

            OverworldPaintBrushes.Apply(PaintBrush.Raise, TileCellKind.Sea, 1, 0, out t, out l, out _);
            Assert.AreEqual(TerrainOverride.Ground, t);
            Assert.AreEqual(1, l, "Raising water drains it before it climbs.");

            // Lower descends; digging below the plain floods it.
            OverworldPaintBrushes.Apply(PaintBrush.Lower, TileCellKind.Ground, 2, 0, out t, out l, out _);
            Assert.AreEqual(TerrainOverride.Ground, t);
            Assert.AreEqual(1, l);

            OverworldPaintBrushes.Apply(PaintBrush.Lower, TileCellKind.Ground, 0, 0, out t, out l, out _);
            Assert.AreEqual(TerrainOverride.Sea, t);
            Assert.AreEqual(0, l, "Digging the plain floods it.");

            // Water pools at the cell's own level — the terrace lake rule.
            OverworldPaintBrushes.Apply(PaintBrush.Water, TileCellKind.Ground, 2, 0, out t, out l, out _);
            Assert.AreEqual(TerrainOverride.Sea, t);
            Assert.AreEqual(2, l);

            // SetLevel is the absolute flattener.
            OverworldPaintBrushes.Apply(PaintBrush.SetLevel, TileCellKind.Sea, 0, 3, out t, out l, out _);
            Assert.AreEqual(TerrainOverride.Ground, t);
            Assert.AreEqual(3, l);

            // Clear writes nothing — the caller removes the entry.
            Assert.IsFalse(OverworldPaintBrushes.Apply(PaintBrush.Clear, TileCellKind.Ground, 0, 0,
                                                       out _, out _, out _));
        }

        [Test]
        public void BiomeInfluenceBlendsAndTheHandBeatsTheField()
        {
            var map = PlainMap(8f);
            map.BiomeAreas = new[]
            {
                // Two areas meeting mid-map: full influence in their own halves, blending
                // through the feather between them.
                new AuthoredBiomeArea { Name = "West", BiomeIndex = 1,
                                        Centre = new Vector2(-3f, 0f), Size = new Vector2(2f, 8f),
                                        Feather = 4f },
                new AuthoredBiomeArea { Name = "East", BiomeIndex = 2,
                                        Centre = new Vector2(3f, 0f), Size = new Vector2(2f, 8f),
                                        Feather = 4f },
            };
            map.CellOverrides = new[]
            {
                // One painted cell deep inside East territory, pinned to biome 5.
                new AuthoredCellOverride { X = 6, Z = 4, Biome = 5 },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.AreEqual(1, grid.DominantBiomeAt(0, 4), "The west edge is pure West…");
            Assert.AreEqual(2, grid.DominantBiomeAt(7, 4), "…the east edge pure East.");

            grid.BiomeBlendAt(0, 4, out _, out int secondary, out float lean);
            Assert.IsTrue(secondary < 0 || lean < 0.1f, "Deep in one field the blend is pure.");

            grid.BiomeBlendAt(4, 4, out int mid, out int midSecondary, out float midLean);
            Assert.IsTrue(mid >= 0 && midSecondary >= 0,
                "Between the fields both influences register…");
            Assert.Greater(midLean, 0.2f, "…with a real blend, not a hard flip.");

            Assert.AreEqual(5, grid.DominantBiomeAt(6, 4),
                "A painted cell dominates any field — the hand beats the splat.");
        }

        [Test]
        public void NoAuthoredBiomeMeansNoBiomeAnywhere()
        {
            var grid = OverworldTileGridCompiler.Compile(PlainMap(8f), Vector3.zero);

            Assert.AreEqual(-1, grid.DominantBiomeAt(4, 4),
                "With nothing authored the splat is silent and everything stays gray-box.");
        }

        [Test]
        public void TheGroundLutBakesTheSplat()
        {
            var map = PlainMap(8f);
            map.BiomeAreas = new[]
            {
                new AuthoredBiomeArea { Name = "West", BiomeIndex = 1,
                                        Centre = new Vector2(-3f, 0f), Size = new Vector2(2f, 8f),
                                        Feather = 4f },
                new AuthoredBiomeArea { Name = "East", BiomeIndex = 2,
                                        Centre = new Vector2(3f, 0f), Size = new Vector2(2f, 8f),
                                        Feather = 4f },
            };
            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var pixels = OverworldGroundLut.Bake(grid);
            int w = grid.Width;

            Assert.AreEqual(1, pixels[4 * w + 0].r, "West edge texel carries West's index.");
            var mid = pixels[4 * w + 4];
            Assert.IsTrue(mid.r < 16 && mid.g < 16 && mid.b > 50,
                "The seam texel carries both indices and a real lean.");

            var bare = OverworldGroundLut.Bake(
                OverworldTileGridCompiler.Compile(PlainMap(8f), Vector3.zero));
            Assert.AreEqual(255, bare[4 * w + 4].r,
                "No biome bakes 255 — the shader's default-ground sentinel.");
        }

        [Test]
        public void TheVariantPickerIsDeterministicAndFallsBackToGrayBox()
        {
            var map = PlainMap(8f);
            map.BiomeAreas = new[]
            {
                new AuthoredBiomeArea { Name = "All", BiomeIndex = 0, Centre = Vector2.zero,
                                        Size = new Vector2(8f, 8f), Feather = 0f },
            };
            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            var fallback = new GameObject("Base");
            var v1 = new GameObject("V1");
            var v2 = new GameObject("V2");
            _cleanup.Add(fallback); _cleanup.Add(v1); _cleanup.Add(v2);

            var biome = Track(ScriptableObject.CreateInstance<OverworldBiome>());
            biome.Cap = new[]
            {
                new OverworldBiomeVariant { Prefab = v1, Weight = 1f },
                new OverworldBiomeVariant { Prefab = v2, Weight = 1f },
            };
            var palette = Track(ScriptableObject.CreateInstance<OverworldBiomePalette>());
            palette.Biomes = new[] { biome };

            var cell = new Vector2Int(4, 4);

            Assert.AreSame(fallback,
                OverworldBiomePicker.Pick(null, grid, 7u, OverworldTilePiece.Cap, cell, fallback),
                "No palette: the gray-box base set.");
            Assert.AreSame(fallback,
                OverworldBiomePicker.Pick(palette, grid, 7u, OverworldTilePiece.Cap,
                                          new Vector2Int(-1, -1), fallback),
                "No owner (the global sea plane): base set.");
            Assert.AreSame(fallback,
                OverworldBiomePicker.Pick(palette, grid, 7u, OverworldTilePiece.Face1, cell, fallback),
                "An EMPTY slot falls back — a biome ships with only its caps done.");

            var first = OverworldBiomePicker.Pick(palette, grid, 7u, OverworldTilePiece.Cap,
                                                  cell, fallback);
            var second = OverworldBiomePicker.Pick(palette, grid, 7u, OverworldTilePiece.Cap,
                                                   cell, fallback);
            Assert.AreSame(first, second,
                "Same map seed, same cell, same piece: the same variant, every load, forever.");
            Assert.IsTrue(first == v1 || first == v2, "…and it is a real variant.");
        }

        [Test]
        public void TheHighCellOwnsItsWallsAndLandOwnsItsShore()
        {
            var map = Track(ScriptableObject.CreateInstance<OverworldMap>());
            map.BoundsSize = new Vector2(8f, 8f);
            map.Regions = new[]
            {
                // An island with a sea ring, and a terrace on its north half: shore pieces AND
                // cliff faces in one compile.
                new AuthoredRegion { Name = "Isle", Centre = Vector2.zero,
                                     Size = new Vector2(6f, 6f), Y = 0f },
                new AuthoredRegion { Name = "Terrace", Centre = new Vector2(0f, 1.5f),
                                     Size = new Vector2(6f, 3f), Y = OverworldTileGrid.Step },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var placements = TilePiecePlanner.Plan(grid, true);

            int checkedFaces = 0, checkedShores = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                if (p.Piece == OverworldTilePiece.Face1 && Mathf.Abs(p.Position.z) < 0.02f)
                {
                    // The terrace wall between low z=3 cells and high z=4 cells sits at world
                    // z=0 — its faces must belong to the HIGH row.
                    Assert.AreEqual(4, p.OwnerCell.y, "The high cell owns its wall.");
                    checkedFaces++;
                }

                if (p.Piece == OverworldTilePiece.ShoreEdge)
                {
                    Assert.AreEqual(TileCellKind.Ground,
                        grid.KindAt(p.OwnerCell.x, p.OwnerCell.y), "Land owns its shore.");
                    checkedShores++;
                }
            }

            Assert.Greater(checkedFaces, 0, "The terrace wall must actually exist.");
            Assert.Greater(checkedShores, 0, "The coast must actually exist.");
        }

        [Test]
        public void AGreebleMassBlocksItsGroundAndTheRoadCutsThrough()
        {
            var map = PlainMap(8f);
            map.Roads = new[]
            {
                new AuthoredRoad { Name = "Path",
                                   Points = new[] { new Vector2(-4f, 0.5f), new Vector2(4f, 0.5f) },
                                   HalfWidth = 0.4f },
            };
            map.Greebles = new[]
            {
                new AuthoredGreeble { Name = "Grove", Centre = new Vector2(0f, 0f),
                                      Size = new Vector2(4f, 4f) },
            };
            map.CellOverrides = new[]
            {
                // A painted clearing inside the grove, and a painted lone tree outside it.
                new AuthoredCellOverride { X = 3, Z = 3, Greeble = GreebleOverride.Remove },
                new AuthoredCellOverride { X = 7, Z = 7, Greeble = GreebleOverride.Add },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var ground = new TileGridGround(grid);

            Assert.IsTrue(grid.GreebleAt(2, 2), "The grove plants its ground cells…");
            Assert.IsFalse(grid.GreebleAt(3, 4),
                "…but never a road cell — the road is the carved path through the forest.");
            Assert.IsFalse(grid.GreebleAt(3, 3), "The painted clearing is open…");
            Assert.IsTrue(grid.GreebleAt(7, 7), "…and the painted lone cell is planted.");

            Assert.IsFalse(ground.CanStep(grid.CellCentre(3, 4), grid.CellCentre(3, 5)),
                "Walking off the road into the trees refuses.");
            Assert.IsTrue(ground.CanStep(grid.CellCentre(2, 4), grid.CellCentre(3, 4)),
                "Walking the road through the grove is open.");
        }

        [Test]
        public void ScatterFillsGreeblesDeterministically()
        {
            var map = PlainMap(8f);
            map.Greebles = new[]
            {
                new AuthoredGreeble { Name = "Grove", Centre = new Vector2(0f, 0f),
                                      Size = new Vector2(3f, 3f) },
            };

            var tree = Track(new GameObject("TreePrefab"));
            var palette = Track(ScriptableObject.CreateInstance<OverworldBiomePalette>());
            palette.DefaultScatter = new[]
            {
                new OverworldBiomeVariant { Prefab = tree, Weight = 1f },
            };

            var walkable1 = new GameObject("W1").transform;
            var scenery1 = new GameObject("S1").transform;
            var walkable2 = new GameObject("W2").transform;
            var scenery2 = new GameObject("S2").transform;
            _cleanup.Add(walkable1.gameObject); _cleanup.Add(scenery1.gameObject);
            _cleanup.Add(walkable2.gameObject); _cleanup.Add(scenery2.gameObject);

            var first = OverworldWorldBuilder.Build(map, DummyTiles(), walkable1, scenery1,
                                                    Vector3.zero, true, palette);
            var second = OverworldWorldBuilder.Build(map, DummyTiles(), walkable2, scenery2,
                                                     Vector3.zero, true, palette);

            int greebleCells = 0;
            for (int z = 0; z < first.Grid.Height; z++)
                for (int x = 0; x < first.Grid.Width; x++)
                    if (first.Grid.GreebleAt(x, z)) greebleCells++;

            Assert.Greater(greebleCells, 0, "The grove must exist.");
            Assert.IsNotNull(first.ScatterObject, "The scatter root spawned.");
            Assert.AreEqual(greebleCells, first.ScatterObject.transform.childCount,
                "One pick per greeble cell — the forest is exactly its mass.");

            var a = first.ScatterObject.transform.GetChild(0);
            var b = second.ScatterObject.transform.GetChild(0);
            Assert.AreEqual(a.position, b.position,
                "Same map, same seed: the same tree in the same place, every build.");
            Assert.AreEqual(a.rotation, b.rotation, "Down to its yaw.");
        }

        [Test]
        public void TheNamedShapesAreTheProvinces()
        {
            var heartland = Track(ScriptableObject.CreateInstance<OverworldProvince>());
            heartland.DisplayName = "Heartland";
            var westwood = Track(ScriptableObject.CreateInstance<OverworldProvince>());
            westwood.DisplayName = "Westwood";

            var map = PlainMap(8f);
            map.Regions[0].Province = heartland;
            map.Greebles = new[]
            {
                new AuthoredGreeble { Name = "Westwood", Centre = new Vector2(2f, 2f),
                                      Size = new Vector2(3f, 3f), Province = westwood },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.AreSame(heartland, grid.ProvinceAt(1, 1),
                "The terrain region's cells answer with its province…");
            Assert.AreSame(westwood, grid.ProvinceAt(6, 6),
                "…and the greeble, rastered later, wins its own footprint — the Westwood IS " +
                "a province, not a rectangle drawn twice.");

            var bare = OverworldTileGridCompiler.Compile(PlainMap(8f), Vector3.zero);
            Assert.IsNull(bare.ProvinceAt(4, 4), "No reference, no province: wilderness.");
        }

        [Test]
        public void PaintedBlocksAreBareAndTheHandUnblocksAnything()
        {
            var map = PlainMap(8f);
            map.Greebles = new[]
            {
                new AuthoredGreeble { Name = "Grove", Centre = new Vector2(0f, 0f),
                                      Size = new Vector2(3f, 3f) },
            };
            map.CellOverrides = new[]
            {
                // A bare painted block out in the open, and the hand unblocking one grove cell.
                new AuthoredCellOverride { X = 6, Z = 6, Unwalkable = UnwalkableOverride.Add },
                new AuthoredCellOverride { X = 4, Z = 4, Unwalkable = UnwalkableOverride.Remove },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var ground = new TileGridGround(grid);

            Assert.IsTrue(grid.UnwalkableAt(6, 6), "The painted block blocks…");
            Assert.IsFalse(grid.GreebleAt(6, 6), "…with NO scatter — bare unwalkable ground.");
            Assert.IsFalse(ground.CanStep(grid.CellCentre(5, 6), grid.CellCentre(6, 6)));

            Assert.IsFalse(grid.UnwalkableAt(4, 4),
                "Unwalkable − unblocked a grove cell — the hand's last word over the greeble…");
            Assert.IsFalse(grid.GreebleAt(4, 4), "…and it carries no scatter either.");
            Assert.IsTrue(grid.GreebleAt(3, 4), "The rest of the grove stands.");
        }

        [Test]
        public void RebuildTouchesOnlyTheDirtyChunks()
        {
            var map = PlainMap(40f);
            var output = Build(map);

            // A witness in a far chunk, held by reference: the rebuild must not disturb it.
            var farChunk = output.Chunks[new Vector2Int(2, 2)];
            var witness = farChunk.Walkable.GetChild(0);

            // And a witness in the chunk the edit will touch: it must be replaced.
            var nearChunk = output.Chunks[new Vector2Int(0, 0)];
            var casualty = nearChunk.Walkable.GetChild(0);

            // The edit: a terrace over cells ~(1..4, 1..4) — the map spans -20..20, so a rect
            // centred (-17.5, -17.5) sits in the south-west corner, chunk (0, 0) only.
            var regions = new List<AuthoredRegion>(map.Regions)
            {
                new AuthoredRegion { Name = "Knoll", Centre = new Vector2(-17.5f, -17.5f),
                                     Size = new Vector2(3f, 3f), Y = OverworldTileGrid.Step },
            };
            map.Regions = regions.ToArray();

            var dirty = OverworldWorldBuilder.ChunksTouchedBy(1, 1, 4, 4);
            Assert.AreEqual(1, dirty.Count, "A corner edit inflates to exactly one chunk.");

            OverworldWorldBuilder.RebuildChunks(output, dirty);

            Assert.IsTrue(witness != null && witness.parent == farChunk.Walkable,
                "An untouched chunk keeps its exact instances — that is the entire point.");
            Assert.IsTrue(casualty == null,
                "The dirty chunk was torn down and re-instantiated.");
            Assert.Greater(nearChunk.Walkable.childCount, 0,
                "…and repopulated from the fresh plan.");
            Assert.AreEqual(1, output.Grid.LevelAt(2, 2),
                "The rebuild compiled the edited map, not the remembered one.");
        }

        [Test]
        public void TheProvinceTableSaysWhatWandersAndEmptyMeansSafe()
        {
            var grunt = new GameObject("Grunt");
            var brute = new GameObject("Brute");
            try
            {
                var table = new[]
                {
                    new ProvinceWanderer { Prefab = grunt, Weight = 1f },
                    new ProvinceWanderer { Prefab = brute, Weight = 3f },
                };

                Assert.AreSame(grunt, OverworldEncounterRules.PickWanderer(table, 0.1f),
                    "A low roll lands in the first entry's quarter of the weight…");
                Assert.AreSame(brute, OverworldEncounterRules.PickWanderer(table, 0.9f),
                    "…and a high roll in the heavy entry's three quarters.");

                Assert.IsNull(OverworldEncounterRules.PickWanderer(
                        System.Array.Empty<ProvinceWanderer>(), 0.5f),
                    "An empty table spawns nothing — that is what a safe province IS.");
                Assert.IsNull(OverworldEncounterRules.PickWanderer(
                        new[] { new ProvinceWanderer { Prefab = grunt, Weight = 0f } }, 0.5f),
                    "All-zero weights are an empty table with extra steps.");
            }
            finally
            {
                Object.DestroyImmediate(grunt);
                Object.DestroyImmediate(brute);
            }
        }

        [Test]
        public void TheContactCellDecidesWhereTheFightHappens()
        {
            var moor = Track(ScriptableObject.CreateInstance<OverworldProvince>());
            moor.DisplayName = "Moor";
            moor.EncounterScene = "Moor_Section";
            moor.EncounterSpawnId = "east";

            var map = PlainMap(8f);
            map.Regions[0].Province = moor;
            map.CellOverrides = new[]
            {
                new AuthoredCellOverride { X = 2, Z = 2, Road = RoadOverride.Add },
            };
            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            OverworldEncounterRules.ResolveContact(grid, 2, 2, "RoadCrossing", "mid",
                                                   "Wilds", "default",
                                                   out var scene, out var spawn);
            Assert.AreEqual("RoadCrossing", scene,
                "A road cell gives the safe crossing WHATEVER province the road runs through.");
            Assert.AreEqual("mid", spawn);

            OverworldEncounterRules.ResolveContact(grid, 5, 5, "RoadCrossing", "mid",
                                                   "Wilds", "default", out scene, out spawn);
            Assert.AreEqual("Moor_Section", scene,
                "Off the road, the contact cell's province names its own section.");
            Assert.AreEqual("east", spawn);

            var wilds = OverworldTileGridCompiler.Compile(PlainMap(8f), Vector3.zero);
            OverworldEncounterRules.ResolveContact(wilds, 5, 5, "RoadCrossing", "mid",
                                                   "Wilds", "default", out scene, out spawn);
            Assert.AreEqual("Wilds", scene, "No province at all: the wilderness fallback.");
        }

        [Test]
        public void CaveAndBridgeDeriveFromGeometryAndTheFlagOverrides()
        {
            var map = PlainMap(12f);
            map.Regions = new[]
            {
                map.Regions[0],
                new AuthoredRegion { Name = "Terrace", Centre = new Vector2(-3f, 0f),
                                     Size = new Vector2(6f, 6f), Y = OverworldTileGrid.Step },
            };
            map.Layers = new[]
            {
                // A floor at plain level under the terrace: cave, by geometry.
                new AuthoredLayer { Name = "Hollow", Centre = new Vector2(-3f, 0f),
                                    Size = new Vector2(2f, 2f), Y = 0f },
                // A deck above open ground: bridge, by geometry.
                new AuthoredLayer { Name = "Deck", Centre = new Vector2(3f, 3f),
                                    Size = new Vector2(2f, 2f), Y = OverworldTileGrid.Step },
                // A deck above open ground FLAGGED Cave — the overhang case.
                new AuthoredLayer { Name = "Overhang", Centre = new Vector2(3f, -3f),
                                    Size = new Vector2(2f, 2f), Y = OverworldTileGrid.Step,
                                    Cover = CoverStyle.Cave },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.IsTrue(grid.TryCoverAt(3, 6, out bool hollowCave) && hollowCave,
                "A floor below the terrain derives Cave…");
            Assert.GreaterOrEqual(grid.CoverRegionAt(3, 6), 0, "…and gets a room id.");

            Assert.IsTrue(grid.TryCoverAt(9, 9, out bool deckCave) && !deckCave,
                "A deck above the terrain derives Bridge…");
            Assert.AreEqual(-1, grid.CoverRegionAt(9, 9), "…and bridges have no rooms.");

            Assert.IsTrue(grid.TryCoverAt(9, 3, out bool overhangCave) && overhangCave,
                "The flag overrides geometry: an overhang can insist on being a cave.");

            Assert.IsFalse(grid.TryCoverAt(6, 6, out _), "Open ground has no cover at all.");
        }

        [Test]
        public void EachCaveIsItsOwnRoom()
        {
            var map = PlainMap(12f);
            map.Regions = new[]
            {
                map.Regions[0],
                new AuthoredRegion { Name = "Terrace", Centre = Vector2.zero,
                                     Size = new Vector2(12f, 12f), Y = OverworldTileGrid.Step },
            };
            map.Layers = new[]
            {
                new AuthoredLayer { Name = "West Hollow", Centre = new Vector2(-4f, 0f),
                                    Size = new Vector2(2f, 2f), Y = 0f },
                new AuthoredLayer { Name = "East Hollow", Centre = new Vector2(4f, 0f),
                                    Size = new Vector2(2f, 2f), Y = 0f },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            int west = grid.CoverRegionAt(2, 6);
            int east = grid.CoverRegionAt(10, 6);
            Assert.GreaterOrEqual(west, 0);
            Assert.GreaterOrEqual(east, 0);
            Assert.AreNotEqual(west, east, "Disconnected hollows are different rooms.");
            Assert.AreEqual(2, grid.CoverRegionCount);
            Assert.AreEqual(west, grid.CoverRegionAt(1, 6),
                "Adjacent cells of one hollow share its room.");

            var lut = OverworldCoverLut.Bake(grid);
            Assert.AreEqual(west + 1, lut[6 * grid.Width + 2],
                "The mask names the room, id plus one…");
            Assert.AreEqual(0, lut[0], "…and zero where there is no cave.");
        }

        [Test]
        public void InsideMeansUnderTheRoofNotOnIt()
        {
            var map = PlainMap(12f);
            map.Regions = new[]
            {
                map.Regions[0],
                new AuthoredRegion { Name = "Terrace", Centre = new Vector2(-3f, 0f),
                                     Size = new Vector2(6f, 6f), Y = OverworldTileGrid.Step },
            };
            map.Layers = new[]
            {
                new AuthoredLayer { Name = "Hollow", Centre = new Vector2(-3f, 0f),
                                    Size = new Vector2(2f, 2f), Y = 0f },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var floor = grid.CellCentre(3, 6);

            Assert.GreaterOrEqual(
                OverworldCoverRules.ActiveCaveRegion(grid, new Vector3(floor.x, 0f, floor.y)),
                0, "Feet on the covered floor are inside the room.");
            Assert.AreEqual(-1,
                OverworldCoverRules.ActiveCaveRegion(
                    grid, new Vector3(floor.x, OverworldTileGrid.Step, floor.y)),
                "Feet on the ROOF are not inside the room beneath them.");

            var open = grid.CellCentre(9, 6);
            Assert.AreEqual(-1,
                OverworldCoverRules.ActiveCaveRegion(grid, new Vector3(open.x, 0f, open.y)),
                "Open ground is inside nothing.");
        }

        [Test]
        public void TheSightlineKnowsWhenTheCameraCannotSee()
        {
            var map = PlainMap(12f);
            map.Regions = new[]
            {
                map.Regions[0],
                new AuthoredRegion { Name = "Terrace", Centre = Vector2.zero,
                                     Size = new Vector2(4f, 4f), Y = 2f * OverworldTileGrid.Step },
            };
            map.Layers = new[]
            {
                new AuthoredLayer { Name = "Deck", Centre = new Vector2(4f, 4f),
                                    Size = new Vector2(2f, 2f), Y = OverworldTileGrid.Step },
            };

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            // A camera-height point south, a chest-height point north, the tall terrace
            // between them: blocked.
            Assert.IsTrue(OverworldSightline.Blocked(
                    grid, new Vector3(0f, 10f, -9f), new Vector3(0f, 0.9f, 3f)),
                "The terrace stands between the camera and the player.");

            // The same geometry, but looking along open ground beside the terrace: clear.
            Assert.IsFalse(OverworldSightline.Blocked(
                    grid, new Vector3(-5f, 10f, -9f), new Vector3(-5f, 0.9f, 3f)),
                "Nothing between them over the open plain.");

            // A chest UNDER the deck: the ray must cross the slab to reach it.
            Assert.IsTrue(OverworldSightline.Blocked(
                    grid, new Vector3(4f, 10f, -6f), new Vector3(4f, 0.9f, 4.2f)),
                "The deck lids the player standing beneath it.");

            // A chest ON the deck: the ray arrives from above without crossing it.
            Assert.IsFalse(OverworldSightline.Blocked(
                    grid, new Vector3(4f, 10f, -6f), new Vector3(4f, 2.9f, 4.2f)),
                "Standing on top of the deck is plain sight.");
        }

        [Test]
        public void ACameraCutZoneKnowsItsInside()
        {
            var half = new Vector3(3f, 2f, 1.5f);
            Assert.IsTrue(Rokkan.Prophecy.Presentation.CameraCutZone.Contains(
                half, new Vector3(2.9f, -1.9f, 1.4f)), "Corners count…");
            Assert.IsFalse(Rokkan.Prophecy.Presentation.CameraCutZone.Contains(
                half, new Vector3(0f, 0f, 1.6f)), "…a step past any face does not.");
        }

        [Test]
        public void WanderersSpawnOnPlainGroundOnly()
        {
            var map = PlainMap(8f);
            map.CellOverrides = new[]
            {
                new AuthoredCellOverride { X = 1, Z = 1, Terrain = TerrainOverride.Sea },
                new AuthoredCellOverride { X = 2, Z = 2, Unwalkable = UnwalkableOverride.Add },
            };
            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.IsTrue(OverworldEncounterRules.CanHostAWanderer(grid, 4, 4),
                "Plain walkable ground hosts a spawn.");
            Assert.IsFalse(OverworldEncounterRules.CanHostAWanderer(grid, 1, 1),
                "Not in the water…");
            Assert.IsFalse(OverworldEncounterRules.CanHostAWanderer(grid, 2, 2),
                "…not on refused ground…");
            Assert.IsFalse(OverworldEncounterRules.CanHostAWanderer(grid, -1, 3),
                "…and not off the map's edge.");
        }
    }
}
