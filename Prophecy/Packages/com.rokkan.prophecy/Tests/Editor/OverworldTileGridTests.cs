using System.Collections.Generic;
using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Overworld;
using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The 3D tile structure, all three layers: the compiler that quantizes the authored map into
    /// discrete cells, the ground seam answered exactly from those cells, and the piece planner
    /// that turns them into tile placements. Connectivity is authored data here — these tests are
    /// what "an authoring edit cannot silently re-roll reachability" means in practice.
    /// </summary>
    public sealed class OverworldTileGridTests
    {
        // A CreateInstance with no matching destroy is a leak that outlives the test run, so
        // every authored map this fixture builds is reclaimed in teardown.
        private readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _cleanup)
                if (obj != null) Object.DestroyImmediate(obj);
            _cleanup.Clear();
        }

        // ---------------------------------------------------------------- the compiler

        private OverworldMap Map(System.Action<OverworldMap> fill)
        {
            var map = ScriptableObject.CreateInstance<OverworldMap>();
            _cleanup.Add(map);
            map.BoundsSize = new Vector2(8f, 8f);
            fill(map);
            return map;
        }

        [Test]
        public void ARegionQuantizesToItsLevel()
        {
            var map = Map(m => m.Regions = new[]
            {
                new AuthoredRegion { Name = "Terrace", Centre = Vector2.zero,
                                     Size = new Vector2(8f, 8f), Y = OverworldTileGrid.Step },
            });

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.AreEqual(TileCellKind.Ground, grid.KindAt(4, 4));
            Assert.AreEqual(1, grid.LevelAt(4, 4),
                "Y of exactly one step must quantize to level 1.");
        }

        [Test]
        public void ALaterRegionWinsTheOverlap()
        {
            var map = Map(m => m.Regions = new[]
            {
                new AuthoredRegion { Name = "Low", Centre = Vector2.zero,
                                     Size = new Vector2(8f, 8f), Y = 0f },
                new AuthoredRegion { Name = "High", Centre = Vector2.zero,
                                     Size = new Vector2(4f, 4f), Y = 2f * OverworldTileGrid.Step },
            });

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.AreEqual(2, grid.LevelAt(4, 4), "The terrace authored on top must win.");
            Assert.AreEqual(0, grid.LevelAt(0, 0), "Outside the terrace the heartland remains.");
        }

        [Test]
        public void AnAuthoredRampBecomesRampCellsAtTheTerraceBoundary()
        {
            var map = Map(m =>
            {
                m.Regions = new[]
                {
                    new AuthoredRegion { Name = "Low", Centre = Vector2.zero,
                                         Size = new Vector2(8f, 8f), Y = 0f },
                    new AuthoredRegion { Name = "High", Centre = new Vector2(0f, 2f),
                                         Size = new Vector2(8f, 4f), Y = OverworldTileGrid.Step },
                };
                m.Ramps = new[]
                {
                    new AuthoredRamp { Name = "Up", Start = new Vector2(0f, -1f),
                                       End = new Vector2(0f, 1f), StartY = 0f,
                                       EndY = OverworldTileGrid.Step, HalfWidth = 1f },
                };
            });

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            // The strip covers the two columns beside x = 0; each boundary converts a RECESSED
            // run — the low boundary cell as the foot, plus a notch carved OUT of the high
            // terrace as the top. One tile at the bottom, one in the wall.
            Assert.AreEqual(TileCellKind.Ramp, grid.KindAt(3, 3));
            Assert.AreEqual(TileCellKind.Ramp, grid.KindAt(3, 4));
            Assert.AreEqual(0, grid.RampIndexAt(3, 3), "The low boundary cell is the foot.");
            Assert.AreEqual(OverworldTileGrid.RampRun - 1, grid.RampIndexAt(3, 4),
                "The notch carved from the high terrace is the top.");
            Assert.AreEqual(TileCellKind.Ground, grid.KindAt(3, 2),
                "The cell behind the foot stays ordinary ground.");
            Assert.AreEqual(RampFacing.PlusZ, grid.FacingAt(3, 4));
            Assert.AreEqual(0, grid.LevelAt(3, 4), "A run cell keeps the LOW level it climbs from.");
            Assert.AreEqual(TileCellKind.Ground, grid.KindAt(1, 3),
                "Outside the authored strip the boundary stays a cliff.");
        }

        // ---------------------------------------------------------------- the ground seam

        private static Vector2 Centre(int x, int z) => new Vector2(x + 0.5f, z + 0.5f);

        private static OverworldTileGrid Flat(int w, int h, int level = 0)
        {
            var grid = new OverworldTileGrid(w, h, Vector2.zero);
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    grid.Set(x, z, TileCellKind.Ground, level);
            return grid;
        }

        [Test]
        public void SameLevelNeighboursConnect()
        {
            var ground = new TileGridGround(Flat(4, 4));

            Assert.IsTrue(ground.CanStep(Centre(0, 0), Centre(1, 0)));
        }

        [Test]
        public void ACliffRefusesBothWays()
        {
            var grid = Flat(4, 4);
            grid.Set(1, 0, TileCellKind.Ground, 1);
            var ground = new TileGridGround(grid);

            Assert.IsFalse(ground.CanStep(Centre(0, 0), Centre(1, 0)),
                "A terrace is a wall from below.");
            Assert.IsFalse(ground.CanStep(Centre(1, 0), Centre(0, 0)),
                "And a ledge from above.");
        }

        [Test]
        public void ARampRunJoinsItsTwoEndsAndRefusesItsSides()
        {
            var grid = Flat(4, 2);
            grid.Set(1, 0, TileCellKind.Ramp, 0, RampFacing.PlusX, 0);
            grid.Set(2, 0, TileCellKind.Ramp, 0, RampFacing.PlusX, 1);
            grid.Set(3, 0, TileCellKind.Ground, 1);
            var ground = new TileGridGround(grid);

            Assert.IsTrue(ground.CanStep(Centre(0, 0), Centre(1, 0)), "Onto the foot.");
            Assert.IsTrue(ground.CanStep(Centre(1, 0), Centre(2, 0)),
                "Between the run's cells — the climb is continuous.");
            Assert.IsTrue(ground.CanStep(Centre(2, 0), Centre(3, 0)), "Off the head, one level up.");
            Assert.IsTrue(ground.CanStep(Centre(3, 0), Centre(2, 0)), "And back down.");
            Assert.IsFalse(ground.CanStep(Centre(1, 1), Centre(1, 0)),
                "You cannot board a stair from the side.");
            Assert.IsTrue(ground.CanStep(Centre(0, 0), Centre(2, 0)),
                "A long probe decomposes along its segment — foot to mid-run is a legal " +
                "continuous walk, not a skip.");
        }

        [Test]
        public void ParallelRampColumnsConnectSideways()
        {
            var grid = Flat(3, 2);
            grid.Set(1, 0, TileCellKind.Ramp, 0, RampFacing.PlusX);
            grid.Set(1, 1, TileCellKind.Ramp, 0, RampFacing.PlusX);
            var ground = new TileGridGround(grid);

            Assert.IsTrue(ground.CanStep(Centre(1, 0), Centre(1, 1)),
                "A wide stair is one stair — walking across it is legal.");
        }

        [Test]
        public void SeaRefusesAndTheEscapeRuleHolds()
        {
            var grid = new OverworldTileGrid(4, 4, Vector2.zero);
            grid.Set(1, 1, TileCellKind.Ground, 0);
            var ground = new TileGridGround(grid);

            Assert.IsFalse(ground.CanStep(Centre(1, 1), Centre(2, 1)), "Sea is not floor.");
            Assert.IsTrue(ground.CanStep(Centre(3, 3), Centre(1, 1)),
                "A body somehow standing in the sea may always escape onto real floor.");
        }

        [Test]
        public void RampHeightInterpolatesAlongItsFacing()
        {
            var grid = Flat(3, 1);
            grid.Set(1, 0, TileCellKind.Ramp, 0, RampFacing.PlusX);
            var ground = new TileGridGround(grid);

            Assert.AreEqual(OverworldTileGrid.Step * 0.5f / OverworldTileGrid.RampRun,
                ground.HeightAt(Centre(1, 0)), 0.001f,
                "The middle of the run's FOOT cell is half of one cell's rise up.");
        }

        // ---------------------------------------------------------------- the planner

        private static int Count(List<TilePlacement> plan, OverworldTilePiece piece) =>
            plan.FindAll(p => p.Piece == piece).Count;

        [Test]
        public void AOneCellPlateauGetsFourFacesAndFourOuterPosts()
        {
            var grid = Flat(3, 3);
            grid.Set(1, 1, TileCellKind.Ground, 1);

            var plan = TilePiecePlanner.Plan(grid);

            Assert.AreEqual(4, Count(plan, OverworldTilePiece.Face1));
            Assert.AreEqual(4, Count(plan, OverworldTilePiece.OuterPost1));
            Assert.AreEqual(9, Count(plan, OverworldTilePiece.Cap));
        }

        [Test]
        public void AFourStepDropSplitsGreedilyThreePlusOne()
        {
            var grid = Flat(2, 1);
            grid.Set(1, 0, TileCellKind.Ground, 4);

            var plan = TilePiecePlanner.Plan(grid);

            // Tolerance 0.05: stacked upper pieces deliberately step 0.01 m toward the low side
            // per tier so their skirts never share a plane with the front below them.
            var face3 = plan.Find(p => p.Piece == OverworldTilePiece.Face3 &&
                                       Mathf.Abs(p.Position.x - 1f) < 0.05f &&
                                       Mathf.Abs(p.Position.z - 0.5f) < 0.05f);
            var face1 = plan.Find(p => p.Piece == OverworldTilePiece.Face1 &&
                                       Mathf.Abs(p.Position.x - 1f) < 0.05f &&
                                       Mathf.Abs(p.Position.z - 0.5f) < 0.05f);

            Assert.AreEqual(OverworldTileGrid.Step, face3.Position.y, 0.001f,
                "The tall stratum rides on top: its footline is level 1.");
            Assert.AreEqual(0f, face1.Position.y, 0.001f,
                "The talus band sits at the base.");
        }

        [Test]
        public void ThreeHighCellsAroundANotchGetAnInnerPost()
        {
            var grid = Flat(2, 2);
            grid.Set(1, 0, TileCellKind.Ground, 1);
            grid.Set(0, 1, TileCellKind.Ground, 1);
            grid.Set(1, 1, TileCellKind.Ground, 1);

            var plan = TilePiecePlanner.Plan(grid);

            Assert.AreEqual(1, Count(plan, OverworldTilePiece.InnerPost1));
        }

        [Test]
        public void ADiagonalContactBecomesTwoBackToBackOuterPosts()
        {
            var grid = Flat(2, 2);
            grid.Set(0, 0, TileCellKind.Ground, 1);
            grid.Set(1, 1, TileCellKind.Ground, 1);

            var plan = TilePiecePlanner.Plan(grid);

            var atCentre = plan.FindAll(p => p.Piece == OverworldTilePiece.OuterPost1 &&
                                             Mathf.Abs(p.Position.x - 1f) < 0.01f &&
                                             Mathf.Abs(p.Position.z - 1f) < 0.01f);
            Assert.AreEqual(2, atCentre.Count,
                "The checkerboard corner is two convex noses interpenetrating, not a saddle tile.");
        }

        [Test]
        public void ARampCutThroughACliffGetsAMirroredCheekPair()
        {
            var grid = Flat(3, 4);
            grid.Set(0, 1, TileCellKind.Ground, 1);
            grid.Set(2, 1, TileCellKind.Ground, 1);
            grid.Set(0, 2, TileCellKind.Ground, 1);
            grid.Set(2, 2, TileCellKind.Ground, 1);
            grid.Set(0, 3, TileCellKind.Ground, 1);
            grid.Set(1, 3, TileCellKind.Ground, 1);
            grid.Set(2, 3, TileCellKind.Ground, 1);
            grid.Set(1, 1, TileCellKind.Ramp, 0, RampFacing.PlusZ, 0);
            grid.Set(1, 2, TileCellKind.Ramp, 0, RampFacing.PlusZ, 1);

            var plan = TilePiecePlanner.Plan(grid);

            Assert.AreEqual(1, Count(plan, OverworldTilePiece.Cheek),
                "One wall of the notch takes the canonical piece…");
            Assert.AreEqual(1, Count(plan, OverworldTilePiece.CheekMirrored),
                "…and the other its baked mirror twin — never a negative scale, which " +
                "reverses winding and renders inside-out.");

            var cheeks = plan.FindAll(p => p.Piece == OverworldTilePiece.Cheek ||
                                           p.Piece == OverworldTilePiece.CheekMirrored);
            Assert.AreEqual(1f, Mathf.Min(cheeks[0].Position.x, cheeks[1].Position.x), 0.01f,
                "One wall sits exactly on the notch's west edge plane…");
            Assert.AreEqual(2f, Mathf.Max(cheeks[0].Position.x, cheeks[1].Position.x), 0.01f,
                "…and the other on its east edge plane — a negative side direction must not " +
                "resolve one cell into the wall.");

            var stairs = plan.FindAll(p => p.Piece == OverworldTilePiece.Stairs);
            Assert.AreEqual(2, stairs.Count, "One stair piece per run cell.");
            Assert.AreEqual(0f, stairs[0].Yaw, 0.01f, "Ascending +Z is the canonical pose.");
            Assert.AreNotEqual(stairs[0].Position.y, stairs[1].Position.y,
                "The pieces chain at rising heights.");
        }

        [Test]
        public void CoastIsShoreAtLevelZeroAndCliffAboveIt()
        {
            var lowIsland = new OverworldTileGrid(3, 3, Vector2.zero);
            lowIsland.Set(1, 1, TileCellKind.Ground, 0);
            var lowPlan = TilePiecePlanner.Plan(lowIsland);

            Assert.AreEqual(4, Count(lowPlan, OverworldTilePiece.ShoreEdge));
            Assert.AreEqual(4, Count(lowPlan, OverworldTilePiece.ShoreOuterPost));
            Assert.AreEqual(0, Count(lowPlan, OverworldTilePiece.Face1),
                "Level-0 coast is beach, not cliff.");

            var highIsland = new OverworldTileGrid(3, 3, Vector2.zero);
            highIsland.Set(1, 1, TileCellKind.Ground, 1);
            var highPlan = TilePiecePlanner.Plan(highIsland);

            Assert.AreEqual(4, Count(highPlan, OverworldTilePiece.Face1),
                "Raised land plunges into the sea as cliff — register #6.");
            Assert.AreEqual(0, Count(highPlan, OverworldTilePiece.ShoreEdge));
            Assert.AreEqual(4, Count(highPlan, OverworldTilePiece.OuterPost1),
                "And its corners are cliff posts, not shore posts.");
        }

        [Test]
        public void ExactlyOneWaterPlaneIsPlanned()
        {
            var plan = TilePiecePlanner.Plan(Flat(4, 4));

            Assert.AreEqual(1, Count(plan, OverworldTilePiece.Water));
        }

        // ---------------------------------------------------------------- layers

        [Test]
        public void ABridgeCarriesYouOverAndTheGroundContinuesUnder()
        {
            var grid = Flat(3, 2);
            grid.Set(0, 1, TileCellKind.Ground, 1);
            grid.Set(2, 1, TileCellKind.Ground, 1);
            grid.SetOverlay(1, 1, 1);                    // the deck between the terraces
            var ground = new TileGridGround(grid);

            int layer = 0;
            Assert.IsTrue(ground.CanStep(Centre(0, 1), Centre(1, 1), ref layer), "Onto the deck…");
            Assert.AreEqual(1, layer, "…which is the overlay surface.");
            Assert.AreEqual(OverworldTileGrid.Step, ground.HeightAt(Centre(1, 1), layer), 0.001f,
                "The deck rides at its own level.");
            Assert.IsFalse(ground.CanStep(Centre(1, 1), Centre(1, 0), ref layer),
                "Stepping off the deck's open side is a cliff edge and refuses.");
            Assert.IsTrue(ground.CanStep(Centre(1, 1), Centre(2, 1), ref layer), "Off the far end…");
            Assert.AreEqual(0, layer, "…back on base terrain.");

            int under = 0;
            Assert.IsTrue(ground.CanStep(Centre(1, 0), Centre(1, 1), ref under),
                "Meanwhile the ground under the deck is still a road…");
            Assert.AreEqual(0, under, "…on the base surface.");
            Assert.AreEqual(0f, ground.HeightAt(Centre(1, 1), under), 0.001f,
                "Two heights at one plane position, told apart by the layer token.");
        }

        [Test]
        public void ACaveMouthWalksInUnderTheTerrace()
        {
            var grid = Flat(1, 3);
            grid.Set(0, 1, TileCellKind.Ground, 1);      // the roof over the cave
            grid.Set(0, 2, TileCellKind.Ground, 1);      // solid rock behind it
            grid.SetOverlay(0, 1, 0);                    // the cave floor
            var ground = new TileGridGround(grid);

            int layer = 0;
            Assert.IsTrue(ground.CanStep(Centre(0, 0), Centre(0, 1), ref layer),
                "In through the mouth — the floor meets the outside ground…");
            Assert.AreEqual(1, layer, "…as the overlay surface.");
            Assert.AreEqual(0f, ground.HeightAt(Centre(0, 1), layer), 0.001f);
            Assert.IsFalse(ground.CanStep(Centre(0, 1), Centre(0, 2), ref layer),
                "The cave's back wall refuses.");

            int roof = 0;
            Assert.IsTrue(ground.CanStep(Centre(0, 2), Centre(0, 1), ref roof),
                "While the roof overhead carries its own traffic…");
            Assert.AreEqual(0, roof, "…on the base surface.");
        }

        [Test]
        public void TheLayerTokenTracksTheFeetNotTheToes()
        {
            // Terrace, two deck cells, terrace — every surface on the path is at one height, so
            // any tick where height and token disagree is the leading-edge bug: the old code
            // committed the layer from a probe half a body ahead of the feet, and walking off a
            // deck dropped the body through it until the feet caught up.
            var grid = Flat(4, 2);
            grid.Set(0, 1, TileCellKind.Ground, 1);
            grid.Set(3, 1, TileCellKind.Ground, 1);
            grid.SetOverlay(1, 1, 1);
            grid.SetOverlay(2, 1, 1);
            var ground = new TileGridGround(grid);

            var sim = PlayerCharacterFactory.Create(new Rokkan.Prophecy.Sim.Collision.CollisionWorld(),
                                                    new MovementTuningData(), MovementSpace.TopDown);
            sim.Ground = ground;
            sim.Teleport(new Vector2(0.5f, 1.5f));   // on the west terrace, layer 0

            const float dt = 1f / 60f;
            for (int i = 0; i < 240; i++)
            {
                sim.SetInput(new InputFrame(new Vector2(1f, 0f)));
                sim.Tick(new SimTickInfo(sim.CurrentTick + 1, dt));

                float height = ground.HeightAt(sim.State.Position, sim.State.GroundLayer);
                Assert.AreEqual(OverworldTileGrid.Step, height, 0.001f,
                    $"Tick {i} at x={sim.State.Position.x:0.00}: the token says a surface the " +
                    "feet are not on.");
            }

            Assert.Greater(sim.State.Position.x, 3f,
                "The walk must actually cross both junctions for the assertion to mean anything.");
        }

        [Test]
        public void ARiverCarvesTheLandAndABridgeDeckCrossesIt()
        {
            var map = Map(m =>
            {
                m.Regions = new[]
                {
                    new AuthoredRegion { Name = "Plain", Centre = Vector2.zero,
                                         Size = new Vector2(8f, 8f), Y = 0f },
                };
                m.Rivers = new[]
                {
                    new AuthoredRiver { Name = "Channel",
                                        Points = new[] { new Vector2(0f, -4f), new Vector2(0f, 4f) },
                                        HalfWidth = 0.6f },
                };
                m.Layers = new[]
                {
                    new AuthoredLayer { Name = "Bridge", Centre = new Vector2(0f, 0.5f),
                                        Size = new Vector2(2f, 1f), Y = 0f },
                };
            });

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);
            var ground = new TileGridGround(grid);

            // The compiled grid's origin is not (0,0) — probe by the grid's own cell centres.
            Assert.AreEqual(TileCellKind.Sea, grid.KindAt(3, 2), "The channel is carved to sea…");
            Assert.AreEqual(TileCellKind.Ground, grid.KindAt(2, 2), "…and its banks are land.");
            Assert.IsFalse(ground.CanStep(grid.CellCentre(2, 2), grid.CellCentre(3, 2)),
                "Off the bank into open water refuses, like any coast.");

            int layer = 0;
            Assert.IsTrue(ground.CanStep(grid.CellCentre(2, 4), grid.CellCentre(3, 4), ref layer),
                "Where the bridge spans the channel, the bank walks onto the deck…");
            Assert.AreEqual(1, layer);
            Assert.IsTrue(ground.CanStep(grid.CellCentre(3, 4), grid.CellCentre(4, 4), ref layer) &&
                          ground.CanStep(grid.CellCentre(4, 4), grid.CellCentre(5, 4), ref layer),
                "…across it, and off the far side…");
            Assert.AreEqual(0, layer, "…back onto plain ground.");
        }

        [Test]
        public void ARoadPaintsTheGroundAndRidesItsBridge()
        {
            var map = Map(m =>
            {
                m.Regions = new[]
                {
                    new AuthoredRegion { Name = "Plain", Centre = Vector2.zero,
                                         Size = new Vector2(8f, 8f), Y = 0f },
                };
                m.Rivers = new[]
                {
                    new AuthoredRiver { Name = "Channel",
                                        Points = new[] { new Vector2(0f, -4f), new Vector2(0f, 4f) },
                                        HalfWidth = 0.6f },
                };
                m.Layers = new[]
                {
                    new AuthoredLayer { Name = "Bridge", Centre = new Vector2(0f, 0.5f),
                                        Size = new Vector2(2f, 1f), Y = 0f },
                };
                m.Roads = new[]
                {
                    new AuthoredRoad { Name = "Bridged",
                                       Points = new[] { new Vector2(-4f, 0.5f), new Vector2(4f, 0.5f) },
                                       HalfWidth = 0.4f },
                    new AuthoredRoad { Name = "Fords nothing",
                                       Points = new[] { new Vector2(-4f, -1.5f), new Vector2(4f, -1.5f) },
                                       HalfWidth = 0.4f },
                };
            });

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.IsTrue(grid.RoadAt(1, 4), "The road paints the plain…");
            Assert.IsTrue(grid.RoadAt(3, 4), "…and rides the bridge deck over the channel.");
            Assert.IsTrue(grid.RoadAt(1, 2), "The unbridged road paints its banks…");
            Assert.IsFalse(grid.RoadAt(3, 2), "…but open water carries no paint.");
            Assert.IsFalse(grid.RoadAt(1, 6), "Off the course, no paint anywhere.");
        }

        [Test]
        public void ARiverHoldsItsTerraceAndFallsWhereTheLandDoes()
        {
            var map = Map(m =>
            {
                m.Regions = new[]
                {
                    new AuthoredRegion { Name = "Plain", Centre = Vector2.zero,
                                         Size = new Vector2(8f, 8f), Y = 0f },
                    new AuthoredRegion { Name = "Terrace", Centre = new Vector2(0f, 2f),
                                         Size = new Vector2(8f, 4f), Y = OverworldTileGrid.Step },
                };
                // Source INSIDE the terrace, not in the border cell: out of bounds is sea level
                // 0, and a source carved into the map's edge cell pours off the world — a real
                // second waterfall, correct and distracting.
                m.Rivers = new[]
                {
                    new AuthoredRiver { Name = "Fallwater",
                                        Points = new[] { new Vector2(0.5f, 2.5f), new Vector2(0.5f, -3.5f) },
                                        HalfWidth = 0.6f },
                };
            });

            var grid = OverworldTileGridCompiler.Compile(map, Vector3.zero);

            Assert.AreEqual(TileCellKind.Sea, grid.KindAt(4, 6), "The terrace reach is water…");
            Assert.AreEqual(1, grid.LevelAt(4, 6), "…holding the terrace's level.");
            Assert.AreEqual(TileCellKind.Sea, grid.KindAt(4, 1), "The plain reach is water…");
            Assert.AreEqual(0, grid.LevelAt(4, 1), "…down at sea level.");

            var falls = TilePiecePlanner.PlanWaterfalls(grid);
            Assert.AreEqual(1, falls.Count,
                "Exactly one sheet, hanging where the water steps down with the land.");
            Assert.AreEqual(OverworldTileGrid.Step + TilePiecePlanner.WaterY,
                falls[0].TopCentre.y, 0.001f, "Its top edge rides the high waterline…");
            Assert.AreEqual(OverworldTileGrid.Step + TilePiecePlanner.FallTuck,
                falls[0].Drop, 0.001f, "…and it tucks below the low one.");
            Assert.AreEqual(new Vector2Int(0, -1), falls[0].Toward, "Facing downstream.");

            var placements = TilePiecePlanner.Plan(grid, true);
            bool elevatedQuad = false, elevatedShore = false;
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].Piece == OverworldTilePiece.Water &&
                    Mathf.Abs(placements[i].Position.y - (OverworldTileGrid.Step + TilePiecePlanner.WaterY)) < 0.001f)
                    elevatedQuad = true;
                if (placements[i].Piece == OverworldTilePiece.ShoreEdge &&
                    Mathf.Abs(placements[i].Position.y - OverworldTileGrid.Step) < 0.001f)
                    elevatedShore = true;
            }
            Assert.IsTrue(elevatedQuad, "The terrace reach draws water at its own waterline.");
            Assert.IsTrue(elevatedShore, "Its banks are shore pieces at the terrace height.");
        }

        [Test]
        public void ArrivalsPickTheSurfaceNearestTheirHeight()
        {
            // A spawn point or a return portal knows only a world position — LayerFor turns its
            // height into the token connectivity would have produced by walking there.
            var grid = Flat(2, 2);
            grid.SetOverlay(0, 0, 1);                    // a deck one step up
            grid.Set(1, 1, TileCellKind.Sea, 0);
            grid.SetOverlay(1, 1, 1);                    // a bridge over open water
            var ground = new TileGridGround(grid);

            Assert.AreEqual(0, ground.LayerFor(Centre(0, 0), 0.1f),
                "An arrival near the ground stands on the base surface.");
            Assert.AreEqual(1, ground.LayerFor(Centre(0, 0), OverworldTileGrid.Step - 0.1f),
                "An arrival near the deck stands on the overlay.");
            Assert.AreEqual(0, ground.LayerFor(Centre(1, 0), 50f),
                "Without an overlay there is exactly one answer.");
            Assert.AreEqual(1, ground.LayerFor(Centre(1, 1), 0f),
                "Over open sea the deck is the only surface, whatever the height claims.");
        }

        [Test]
        public void ATeleportCanArriveOnANamedSurface()
        {
            // Walk-based layer resolution can never learn anything from a teleport — the arrival
            // says which surface it means, and the sim stores the token verbatim.
            var grid = Flat(2, 2);
            grid.SetOverlay(0, 0, 1);
            var ground = new TileGridGround(grid);

            var sim = PlayerCharacterFactory.Create(new Rokkan.Prophecy.Sim.Collision.CollisionWorld(),
                                                    new MovementTuningData(), MovementSpace.TopDown);
            sim.Ground = ground;
            sim.Teleport(Centre(0, 0), 0, ground.LayerFor(Centre(0, 0), OverworldTileGrid.Step));

            Assert.AreEqual(1, sim.State.GroundLayer, "The return landed on the deck…");
            Assert.AreEqual(OverworldTileGrid.Step,
                ground.HeightAt(sim.State.Position, sim.State.GroundLayer), 0.001f,
                "…and stands at its height.");
        }

        [Test]
        public void TheMouthIsAnOpeningAndTheCaveGrowsInteriorWalls()
        {
            var grid = Flat(1, 3);
            grid.Set(0, 1, TileCellKind.Ground, 1);
            grid.Set(0, 2, TileCellKind.Ground, 1);
            grid.SetOverlay(0, 1, 0);

            var plan = TilePiecePlanner.Plan(grid);

            var mouth = plan.FindAll(p => p.Piece == OverworldTilePiece.Face1 &&
                                          Mathf.Abs(p.Position.x - 0.5f) < 0.01f &&
                                          Mathf.Abs(p.Position.z - 1f) < 0.01f);
            Assert.AreEqual(0, mouth.Count,
                "The wall the mouth pierces is suppressed — the opening IS the doorway.");

            var backWall = plan.FindAll(p => p.Piece == OverworldTilePiece.Face1 &&
                                             Mathf.Abs(p.Position.x - 0.5f) < 0.01f &&
                                             Mathf.Abs(p.Position.z - 2f) < 0.01f &&
                                             p.Position.y < 0.01f);
            Assert.AreEqual(1, backWall.Count,
                "The cave's back wall exists, footed at the floor, facing the interior.");

            Assert.AreEqual(4, Count(plan, OverworldTilePiece.Cap),
                "Three base caps plus the cave floor's own.");
        }
    }
}
