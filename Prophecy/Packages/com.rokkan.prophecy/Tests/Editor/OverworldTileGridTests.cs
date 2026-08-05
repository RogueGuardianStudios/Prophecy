using System.Collections.Generic;
using NUnit.Framework;
using Rokkan.Prophecy.Overworld;
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
        // ---------------------------------------------------------------- the compiler

        private static OverworldMap Map(System.Action<OverworldMap> fill)
        {
            var map = ScriptableObject.CreateInstance<OverworldMap>();
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
    }
}
