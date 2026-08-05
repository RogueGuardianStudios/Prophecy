using System.Collections.Generic;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>What one build produced: the compiled grid, the plan it was placed from, the
    /// chunk roots, and every transient Object (meshes, materials) the owner must dispose.
    /// The play host lets scene unload collect them; the editor preview destroys them
    /// explicitly so repeated rebuilds never leak.</summary>
    public sealed class OverworldBuildOutput
    {
        public OverworldTileGrid Grid;
        public List<TilePlacement> Placements;

        /// <summary>Per-chunk instance roots, keyed by chunk coords. One walkable and one
        /// scenery root per chunk that has content.</summary>
        public readonly Dictionary<Vector2Int, ChunkRoots> Chunks = new Dictionary<Vector2Int, ChunkRoots>();

        /// <summary>Meshes and materials created by the build.</summary>
        public readonly List<Object> Transient = new List<Object>();

        // What the build was made FROM — kept so RebuildChunks can recompile and re-place
        // without the caller re-threading every argument.
        public OverworldMap Map;
        public OverworldTileSet Tiles;
        public OverworldBiomePalette Biomes;   // null = gray-box everywhere
        public Transform WalkableRoot;
        public Transform SceneryRoot;
        public Vector3 WorldOffset;
        public bool StairsForRamps;

        // The global merged meshes and the prop root, rebuilt whole on every rebuild (they
        // are cheap — a few meshes and a handful of prop instances).
        public GameObject RoadsObject;
        public GameObject WaterfallsObject;
        public GameObject PropsObject;
    }

    public struct ChunkRoots
    {
        public Transform Walkable;
        public Transform Scenery;
    }

    /// <summary>
    /// The one place the overworld's visible world is assembled from a map — shared by the play
    /// host (Awake) and the editor's live preview, so what the tool shows IS what the game
    /// builds. Instances are bucketed into <see cref="ChunkSize"/>-square chunk roots:
    /// an edit re-instantiates only the chunks it touched (compile + plan are pure C# and
    /// re-run whole — instantiation is the expensive part, not planning), and the chunk
    /// structure is the natural hook for streaming and per-chunk mesh combining later.
    /// </summary>
    public static class OverworldWorldBuilder
    {
        /// <summary>Chunk edge, in cells.</summary>
        public const int ChunkSize = 16;

        /// <summary>How far road paint rides above the walk surface. Enough that the strip and
        /// the cap are never the same depth, far less than any rim.</summary>
        private const float RoadLift = 0.02f;

        public static OverworldBuildOutput Build(OverworldMap map, OverworldTileSet tiles,
                                                 Transform walkableRoot, Transform sceneryRoot,
                                                 Vector3 worldOffset, bool stairsForRamps,
                                                 OverworldBiomePalette biomes = null)
        {
            var output = new OverworldBuildOutput
            {
                Map = map,
                Tiles = tiles,
                Biomes = biomes,
                WalkableRoot = walkableRoot,
                SceneryRoot = sceneryRoot,
                WorldOffset = worldOffset,
                StairsForRamps = stairsForRamps,
            };

            output.Grid = OverworldTileGridCompiler.Compile(map, worldOffset);
            output.Placements = TilePiecePlanner.Plan(output.Grid, stairsForRamps);

            for (int i = 0; i < output.Placements.Count; i++)
                Place(output, output.Placements[i]);

            BuildRoads(output);
            BuildWaterfalls(output);
            SpawnProps(output);

            return output;
        }

        /// <summary>
        /// Recompile, replan, and re-instantiate ONLY the named chunks (plus the global merged
        /// meshes, which rebuild whole every time). Callers derive the dirty set from an edit's
        /// touched cells inflated by one cell — edge and corner pieces depend on neighbours —
        /// via <see cref="ChunksTouchedBy"/>.
        /// </summary>
        public static void RebuildChunks(OverworldBuildOutput output, HashSet<Vector2Int> dirty)
        {
            output.Grid = OverworldTileGridCompiler.Compile(output.Map, output.WorldOffset);
            output.Placements = TilePiecePlanner.Plan(output.Grid, output.StairsForRamps);

            foreach (var chunk in dirty)
            {
                if (!output.Chunks.TryGetValue(chunk, out var roots)) continue;
                DestroyChildren(roots.Walkable);
                DestroyChildren(roots.Scenery);
            }

            for (int i = 0; i < output.Placements.Count; i++)
            {
                if (!dirty.Contains(ChunkOf(output, output.Placements[i].Position))) continue;
                Place(output, output.Placements[i]);
            }

            RebuildGlobalMeshes(output);
        }

        /// <summary>The dirty chunk set for an edit spanning the given cell rectangle
        /// (inclusive), inflated one cell for the neighbour-dependent pieces.</summary>
        public static HashSet<Vector2Int> ChunksTouchedBy(int minX, int minZ, int maxX, int maxZ)
        {
            var set = new HashSet<Vector2Int>();
            int cMinX = Mathf.FloorToInt((minX - 1) / (float)ChunkSize);
            int cMinZ = Mathf.FloorToInt((minZ - 1) / (float)ChunkSize);
            int cMaxX = Mathf.FloorToInt((maxX + 1) / (float)ChunkSize);
            int cMaxZ = Mathf.FloorToInt((maxZ + 1) / (float)ChunkSize);
            for (int cz = cMinZ; cz <= cMaxZ; cz++)
                for (int cx = cMinX; cx <= cMaxX; cx++)
                    set.Add(new Vector2Int(cx, cz));
            return set;
        }

        /// <summary>Destroy the global merged meshes and the prop root, and rebuild them from
        /// the current grid — part of <see cref="RebuildChunks"/>, public for owners that need
        /// it alone. Props re-place wholesale so a terrain edit under one re-derives its
        /// height.</summary>
        public static void RebuildGlobalMeshes(OverworldBuildOutput output)
        {
            DestroyMerged(output, ref output.RoadsObject);
            DestroyMerged(output, ref output.WaterfallsObject);
            if (output.PropsObject != null)
            {
                DestroySmart(output.PropsObject);
                output.PropsObject = null;
            }
            BuildRoads(output);
            BuildWaterfalls(output);
            SpawnProps(output);
        }

        // ---------------------------------------------------------------- placement

        private static void Place(OverworldBuildOutput output, TilePlacement placement)
        {
            // The geometry LUT: the owner cell's dominant biome picks a variant, seeded from
            // the map seed so the same map renders the same world every load. No palette, no
            // biome, or an empty slot all fall back to the gray-box base set.
            var prefab = OverworldBiomePicker.Pick(output.Biomes, output.Grid, output.Map.Seed,
                                                   placement.Piece, placement.OwnerCell,
                                                   output.Tiles.For(placement.Piece));

            bool walkable = placement.Piece == OverworldTilePiece.Cap ||
                            placement.Piece == OverworldTilePiece.Ramp ||
                            placement.Piece == OverworldTilePiece.Stairs;

            var roots = GetChunk(output, ChunkOf(output, placement.Position));
            var instance = Object.Instantiate(prefab, walkable ? roots.Walkable : roots.Scenery);
            instance.transform.position = placement.Position;
            instance.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);
            instance.transform.localScale = placement.Scale;
        }

        private static Vector2Int ChunkOf(OverworldBuildOutput output, Vector3 position)
        {
            var origin = output.Grid.Origin;
            float span = OverworldTileGrid.CellSize * ChunkSize;
            return new Vector2Int(Mathf.FloorToInt((position.x - origin.x) / span),
                                  Mathf.FloorToInt((position.z - origin.y) / span));
        }

        private static ChunkRoots GetChunk(OverworldBuildOutput output, Vector2Int chunk)
        {
            if (output.Chunks.TryGetValue(chunk, out var roots)) return roots;

            roots = new ChunkRoots
            {
                Walkable = new GameObject($"Chunk_{chunk.x}_{chunk.y}").transform,
                Scenery = new GameObject($"Chunk_{chunk.x}_{chunk.y}").transform,
            };
            roots.Walkable.SetParent(output.WalkableRoot, false);
            roots.Scenery.SetParent(output.SceneryRoot, false);
            output.Chunks[chunk] = roots;
            return roots;
        }

        // ---------------------------------------------------------------- merged meshes

        /// <summary>
        /// Roads are paint, not ground: one merged mesh of per-cell quads riding just above the
        /// walk surface — the cap, or the bridge deck where the course crosses a river — parented
        /// under scenery so the NavMesh never sees them.
        /// </summary>
        private static void BuildRoads(OverworldBuildOutput output)
        {
            var grid = output.Grid;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.RoadAt(x, z)) continue;

                    int level = grid.TryOverlayAt(x, z, out int overlay) ? overlay : grid.LevelAt(x, z);
                    float y = level * OverworldTileGrid.Step + RoadLift;
                    var corner = grid.CornerPoint(x, z);

                    int v = vertices.Count;
                    vertices.Add(new Vector3(corner.x, y, corner.y));
                    vertices.Add(new Vector3(corner.x, y, corner.y + OverworldTileGrid.CellSize));
                    vertices.Add(new Vector3(corner.x + OverworldTileGrid.CellSize, y,
                                             corner.y + OverworldTileGrid.CellSize));
                    vertices.Add(new Vector3(corner.x + OverworldTileGrid.CellSize, y, corner.y));
                    triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                    triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
                }
            }

            if (vertices.Count == 0) return;

            var mesh = new Mesh { name = "Roads" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            output.Transient.Add(mesh);

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = "Road_Surface",
                color = new Color(0.72f, 0.62f, 0.47f),
            };
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            output.Transient.Add(material);

            var roads = new GameObject("Roads");
            roads.transform.SetParent(output.SceneryRoot, false);
            roads.AddComponent<MeshFilter>().sharedMesh = mesh;
            roads.AddComponent<MeshRenderer>().sharedMaterial = material;
            output.RoadsObject = roads;
        }

        /// <summary>
        /// Waterfall sheets — vertical quads where elevated water drops to lower water, meshed
        /// here for the same reason roads are: they are not tile-set pieces, and the planner's
        /// placements carry no pitch. They wear the water tile's own material, so a palette or
        /// shader change to the sea carries the falls with it.
        /// </summary>
        private static void BuildWaterfalls(OverworldBuildOutput output)
        {
            var falls = TilePiecePlanner.PlanWaterfalls(output.Grid);
            if (falls.Count == 0) return;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();

            for (int i = 0; i < falls.Count; i++)
            {
                var fall = falls[i];
                var right = new Vector3(-fall.Toward.y, 0f, fall.Toward.x) * (OverworldTileGrid.CellSize * 0.5f);
                var down = Vector3.down * fall.Drop;

                int v = vertices.Count;
                vertices.Add(fall.TopCentre - right);
                vertices.Add(fall.TopCentre + right);
                vertices.Add(fall.TopCentre + right + down);
                vertices.Add(fall.TopCentre - right + down);
                // Every vertex samples the middle of the water tile's UV island: the sheet
                // wears the sea's flat colour, and stays with it through any reskin.
                for (int u = 0; u < 4; u++) uvs.Add(new Vector2(0.5f, 0.5f));
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
            }

            var mesh = new Mesh { name = "Waterfalls" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            output.Transient.Add(mesh);

            var water = output.Tiles.For(OverworldTilePiece.Water);
            var renderer = water != null ? water.GetComponentInChildren<MeshRenderer>() : null;

            var sheets = new GameObject("Waterfalls");
            sheets.transform.SetParent(output.SceneryRoot, false);
            sheets.AddComponent<MeshFilter>().sharedMesh = mesh;
            var sheetRenderer = sheets.AddComponent<MeshRenderer>();
            if (renderer != null) sheetRenderer.sharedMaterial = renderer.sharedMaterial;
            output.WaterfallsObject = sheets;
        }

        // ---------------------------------------------------------------- props

        /// <summary>
        /// Placed objects, standing on whatever the ground turned out to be: height derives
        /// from the grid at spawn — the base surface, or the overlay deck when the prop says
        /// so — so terrain retunes never strand a prop in the air. Their BLOCKING lives in the
        /// compiled grid (RasterProps), not here; this is only the visible half.
        /// </summary>
        private static void SpawnProps(OverworldBuildOutput output)
        {
            var props = output.Map.Props;
            if (props == null || props.Length == 0) return;

            var root = new GameObject("Props");
            root.transform.SetParent(output.SceneryRoot, false);
            output.PropsObject = root;

            var offset = new Vector2(output.WorldOffset.x, output.WorldOffset.z);
            for (int i = 0; i < props.Length; i++)
            {
                var prop = props[i];
                if (prop.Prefab == null) continue;

                var world = prop.Position + offset;
                if (!output.Grid.TryCellAt(world, out int x, out int z)) continue;

                float y = prop.SurfaceLayer == 1 && output.Grid.TryOverlayAt(x, z, out int overlay)
                    ? overlay * OverworldTileGrid.Step
                    : output.Grid.SurfaceHeight(x, z, world);

                var instance = Object.Instantiate(prop.Prefab, root.transform);
                instance.name = prop.Name;
                instance.transform.position = new Vector3(world.x, y, world.y);
                instance.transform.rotation = Quaternion.Euler(0f, prop.YawDegrees, 0f);
            }
        }

        // ---------------------------------------------------------------- teardown helpers

        private static void DestroyChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
                DestroySmart(root.GetChild(i).gameObject);
        }

        private static void DestroyMerged(OverworldBuildOutput output, ref GameObject merged)
        {
            if (merged == null) return;

            // The merged object's mesh and material are in Transient — pull them out and
            // destroy them with it, so a thousand rebuilds don't stack a thousand meshes.
            var filter = merged.GetComponent<MeshFilter>();
            var renderer = merged.GetComponent<MeshRenderer>();
            if (filter != null && filter.sharedMesh != null)
            {
                output.Transient.Remove(filter.sharedMesh);
                DestroySmart(filter.sharedMesh);
            }
            if (renderer != null && renderer.sharedMaterial != null &&
                output.Transient.Remove(renderer.sharedMaterial))
            {
                DestroySmart(renderer.sharedMaterial);
            }

            DestroySmart(merged);
            merged = null;
        }

        /// <summary>Play mode defers destruction; edit mode must not.</summary>
        public static void DestroySmart(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
