using NUnit.Framework;
using RGS.Core.Sim;
using Rokkan.Prophecy.Overworld;
using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The overworld's collision, both halves: the sim resolving movement against the ground
    /// seam (proved with a hand-built fake, no grid anywhere), and the walk-grid raster the real
    /// overworld answers from.
    /// </summary>
    public sealed class TopDownGroundTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>Walkable everywhere west of a wall at x = 5, flat.</summary>
        private sealed class WallAtFive : ITopDownGround
        {
            public bool CanStep(Vector2 from, Vector2 to) => to.x < 5f;
            public float HeightAt(Vector2 point) => 0f;
        }

        private static CharacterSim TopDownSim(out MovementTuningData tuning)
        {
            tuning = new MovementTuningData();
            var sim = PlayerCharacterFactory.Create(new Sim.Collision.CollisionWorld(), tuning,
                                                    MovementSpace.TopDown);
            sim.Teleport(Vector2.zero);
            return sim;
        }

        private static void Step(CharacterSim sim, InputFrame input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                sim.SetInput(input);
                sim.Tick(new SimTickInfo(sim.CurrentTick + 1, Dt));
            }
        }

        private static InputFrame Hold(float x, float y) => new InputFrame(new Vector2(x, y));

        // ---------------------------------------------------------------- the sim's half

        [Test]
        public void NoGroundMeansFreeMovement()
        {
            var sim = TopDownSim(out _);

            Step(sim, Hold(1f, 0f), 120);

            Assert.Greater(sim.State.Position.x, 10f,
                "With no ground assigned the overworld must behave exactly as it did before one existed.");
        }

        [Test]
        public void AWallStopsTheBodyAtItsEdgeNotItsCentre()
        {
            var sim = TopDownSim(out _);
            sim.Ground = new WallAtFive();

            Step(sim, Hold(1f, 0f), 240);

            float halfBody = sim.State.BodySize.x * 0.5f;

            Assert.Less(sim.State.Position.x, 5f - halfBody + 0.05f,
                "The leading edge must stop at the wall — a centre-point test lets half the body overhang.");
            Assert.Greater(sim.State.Position.x, 5f - halfBody - 0.6f,
                "It should stop AT the wall, not flinch away from it.");
            Assert.AreEqual(0f, sim.State.Velocity.x, 0.001f, "Pushing into a wall builds no speed.");
        }

        [Test]
        public void BlockedInXStillMovesInY()
        {
            var sim = TopDownSim(out _);
            sim.Ground = new WallAtFive();
            sim.Teleport(new Vector2(4.6f, 0f));

            Step(sim, Hold(1f, 1f), 60);

            Assert.AreEqual(0f, sim.State.Velocity.x, 0.001f, "x is against the wall");
            Assert.Greater(sim.State.Position.y, 2f,
                "Axis separation: sliding along a wall is what stops it feeling sticky.");
        }

        // ---------------------------------------------------------------- the raster's half

        private static OverworldWalkGrid SingleQuadGrid(float floorY = 0f)
        {
            // A 10×10 floor quad in a 20×20 world; everything outside it is sea.
            var grid = new OverworldWalkGrid(new Vector2(-10f, -10f), new Vector2(20f, 20f));
            grid.FillQuad(new Vector2(-5f, -5f), new Vector2(5f, -5f),
                          new Vector2(5f, 5f), new Vector2(-5f, 5f), floorY);
            return grid;
        }

        [Test]
        public void FloorIsWalkableAndSeaIsNot()
        {
            var grid = SingleQuadGrid();

            Assert.IsTrue(grid.CanStep(Vector2.zero, new Vector2(3f, 3f)), "inside the quad");
            Assert.IsFalse(grid.CanStep(Vector2.zero, new Vector2(8f, 0f)), "off the coast");
            Assert.IsFalse(grid.CanStep(Vector2.zero, new Vector2(0f, 40f)), "outside the world entirely");
        }

        [Test]
        public void ATerraceEdgeRefusesBothDirections()
        {
            var grid = new OverworldWalkGrid(new Vector2(-10f, -10f), new Vector2(20f, 20f));
            grid.FillQuad(new Vector2(-8f, -8f), new Vector2(0f, -8f), new Vector2(0f, 8f), new Vector2(-8f, 8f), 0f);
            grid.FillQuad(new Vector2(0f, -8f), new Vector2(8f, -8f), new Vector2(8f, 8f), new Vector2(0f, 8f), 0.7f);

            var low = new Vector2(-2f, 0f);
            var high = new Vector2(2f, 0f);

            Assert.IsFalse(grid.CanStep(low, high), "a 0.7 m rise is a wall from below");
            Assert.IsFalse(grid.CanStep(high, low), "and a ledge from above");
        }

        [Test]
        public void AGentleRiseIsJustGround()
        {
            var grid = new OverworldWalkGrid(new Vector2(-10f, -10f), new Vector2(20f, 20f));
            grid.FillQuad(new Vector2(-8f, -8f), new Vector2(0f, -8f), new Vector2(0f, 8f), new Vector2(-8f, 8f), 0f);
            grid.FillQuad(new Vector2(0f, -8f), new Vector2(8f, -8f), new Vector2(8f, 8f), new Vector2(0f, 8f), 0.3f);

            Assert.IsTrue(grid.CanStep(new Vector2(-2f, 0f), new Vector2(2f, 0f)),
                "Below MaxStep the ground is a ramp, not a wall — authored ramps need no new rule.");
        }

        [Test]
        public void StrandedOffFloorCanAlwaysStepBackOn()
        {
            var grid = SingleQuadGrid(floorY: 2f);

            Assert.IsTrue(grid.CanStep(new Vector2(9f, 9f), new Vector2(3f, 3f)),
                "A body standing on nothing — teleport, old save, bug — must never be trapped there.");
        }

        [Test]
        public void HeightComesBackFromTheFloor()
        {
            var grid = SingleQuadGrid(floorY: 1.4f);

            Assert.AreEqual(1.4f, grid.HeightAt(new Vector2(1f, 1f)), 0.001f);
            Assert.AreEqual(0f, grid.HeightAt(new Vector2(9f, 9f)), 0.001f, "sea has no height");
        }
    }
}
