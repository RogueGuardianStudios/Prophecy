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
        private readonly List<GameObject> _cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _cleanup)
                if (go != null) Object.DestroyImmediate(go);
            _cleanup.Clear();
        }

        private OverworldTileSet DummyTiles()
        {
            var prefab = new GameObject("DummyTile");
            _cleanup.Add(prefab);

            var tiles = ScriptableObject.CreateInstance<OverworldTileSet>();
            tiles.Cap = tiles.Ramp = tiles.Stairs = tiles.Water = prefab;
            tiles.Face1 = tiles.Face2 = tiles.Face3 = prefab;
            tiles.OuterPost1 = tiles.OuterPost2 = tiles.OuterPost3 = prefab;
            tiles.InnerPost1 = tiles.InnerPost2 = tiles.InnerPost3 = prefab;
            tiles.Cheek = tiles.CheekMirrored = prefab;
            tiles.ShoreEdge = tiles.ShoreOuterPost = prefab;
            return tiles;
        }

        private static OverworldMap PlainMap(float size)
        {
            var map = ScriptableObject.CreateInstance<OverworldMap>();
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
            var walkable = new GameObject("Walkable").transform;
            var scenery = new GameObject("Scenery").transform;
            _cleanup.Add(walkable.gameObject);
            _cleanup.Add(scenery.gameObject);

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
    }
}
