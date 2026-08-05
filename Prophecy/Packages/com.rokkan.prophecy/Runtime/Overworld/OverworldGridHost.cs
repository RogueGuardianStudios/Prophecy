using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// Builds the overworld's ground from a hand-authored <see cref="OverworldMap"/> — now on the
    /// 3D tile structure: the map compiles to a discrete <see cref="OverworldTileGrid"/>
    /// (integer levels, Ground/Ramp cells), <see cref="TilePiecePlanner"/> turns the grid into
    /// piece placements, and this host instantiates the tile prefabs. Connectivity is authored
    /// data; cliffs are the automatic consequence of neighbours differing.
    ///
    /// <para><b>Built at load, not baked into the scene.</b> The scene stores the recipe rather
    /// than thousands of tile instances, and the darkening overworld (design bible §5) will
    /// eventually repaint regions in play, which a baked scene cannot.</para>
    ///
    /// <para><b>Tiles carry no colliders</b> — the prefabs are MeshFilter + MeshRenderer only,
    /// which is the flat plain's occlusion lesson enforced by construction. Walkability comes
    /// from the same cells the tiles are placed from, published through the ground seam.</para>
    /// </summary>
    public sealed class OverworldGridHost : MonoBehaviour
    {
        [SerializeField, Tooltip("The hand-authored map this builds.")]
        private OverworldMap _map;

        [SerializeField, Tooltip("The 17 tile prefabs. Auto-filled by Prophecy > Build > " +
                                 "Generate Overworld Tiles.")]
        private OverworldTileSet _tiles;

        [SerializeField, Tooltip("Stairs read classic ALttP at cliff cuttings; ramps suit roads. " +
                                 "One choice for the whole map until per-ramp authoring exists.")]
        private bool _stairsForRamps = true;

        /// <summary>Which implementation answers the sim's ground seam.</summary>
        public enum GroundAuthority
        {
            /// <summary>The baked NavMesh — kept for A/B and because AI routing will use it.</summary>
            NavMesh,

            /// <summary>The tile grid itself — exact, headless, and by construction identical to
            /// what the tiles show. The default, and the point of the whole rebuild.</summary>
            TileGrid,
        }

        [SerializeField, Tooltip("Who bounds movement here. The tile grid is the authority the " +
                                 "rebuild exists to provide; NavMesh remains for comparison.")]
        private GroundAuthority _groundAuthority = GroundAuthority.TileGrid;

        private OverworldTileGrid _grid;
        private ITopDownGround _published;
        private Transform _walkableRoot;
        private Transform _sceneryRoot;
        private UnityEngine.AI.NavMeshDataInstance _navMeshInstance;

        /// <summary>The compiled tile grid — region queries, the darkening repaint, and the
        /// encounter spawner's placement checks all read from here. Null before Awake.</summary>
        public OverworldTileGrid Grid => _grid;

        private void Awake()
        {
            if (_map == null || _tiles == null)
            {
                Debug.LogError($"{name}: map and tile set are both required — the overworld has " +
                               "no ground without them.", this);
                return;
            }

            if (!_tiles.IsComplete(out string missing))
            {
                Debug.LogError($"{name}: tile set slot '{missing}' is empty. Run Prophecy > " +
                               "Build > Generate Overworld Tiles.", this);
                return;
            }

            _grid = OverworldTileGridCompiler.Compile(_map, transform.position);

            // Walkable tops and scenery under separate roots, so the NavMesh bakes from the
            // walkable half alone — a cliff face that is never a bake source can never be
            // climbed, whatever the slope limit says.
            _walkableRoot = new GameObject("Ground_Walkable").transform;
            _walkableRoot.SetParent(transform, false);
            _sceneryRoot = new GameObject("Ground_Scenery").transform;
            _sceneryRoot.SetParent(transform, false);

            var placements = TilePiecePlanner.Plan(_grid, _stairsForRamps);
            for (int i = 0; i < placements.Count; i++) Place(placements[i]);

            BuildRoads();
            BuildWaterfalls();

            BakeNavMesh();

            _published = _groundAuthority == GroundAuthority.NavMesh
                ? (ITopDownGround)new NavMeshGround()
                : new TileGridGround(_grid);
            Presentation.TopDownGroundSource.Current = _published;
        }

        private void Place(TilePlacement placement)
        {
            var prefab = _tiles.For(placement.Piece);

            bool walkable = placement.Piece == OverworldTilePiece.Cap ||
                            placement.Piece == OverworldTilePiece.Ramp ||
                            placement.Piece == OverworldTilePiece.Stairs;

            var instance = Instantiate(prefab, walkable ? _walkableRoot : _sceneryRoot);
            instance.transform.position = placement.Position;
            instance.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);
            instance.transform.localScale = placement.Scale;
        }

        /// <summary>How far road paint rides above the walk surface. Enough that the strip and
        /// the cap are never the same depth, far less than any rim.</summary>
        private const float RoadLift = 0.02f;

        /// <summary>
        /// Roads are paint, not ground: one merged mesh of per-cell quads riding just above the
        /// walk surface — the cap, or the bridge deck where the course crosses a river — parented
        /// under scenery so the NavMesh never sees them. Built after the tiles because it reads
        /// the same compiled cells; a regenerated map re-draws its roads by construction.
        /// </summary>
        private void BuildRoads()
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();

            for (int z = 0; z < _grid.Height; z++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    if (!_grid.RoadAt(x, z)) continue;

                    int level = _grid.TryOverlayAt(x, z, out int overlay) ? overlay : _grid.LevelAt(x, z);
                    float y = level * OverworldTileGrid.Step + RoadLift;
                    var corner = _grid.CornerPoint(x, z);

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

            var roads = new GameObject("Roads");
            roads.transform.SetParent(_sceneryRoot, false);
            roads.AddComponent<MeshFilter>().sharedMesh = mesh;
            roads.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Waterfall sheets — vertical quads where elevated water drops to lower water, meshed
        /// here for the same reason roads are: they are not tile-set pieces, and the planner's
        /// placements carry no pitch. They wear the water tile's own material, so a palette or
        /// shader change to the sea carries the falls with it.
        /// </summary>
        private void BuildWaterfalls()
        {
            var falls = TilePiecePlanner.PlanWaterfalls(_grid);
            if (falls.Count == 0) return;

            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var uvs = new System.Collections.Generic.List<Vector2>();

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

            var water = _tiles.For(OverworldTilePiece.Water);
            var renderer = water != null ? water.GetComponentInChildren<MeshRenderer>() : null;

            var sheets = new GameObject("Waterfalls");
            sheets.transform.SetParent(_sceneryRoot, false);
            sheets.AddComponent<MeshFilter>().sharedMesh = mesh;
            var sheetRenderer = sheets.AddComponent<MeshRenderer>();
            if (renderer != null) sheetRenderer.sharedMaterial = renderer.sharedMaterial;
        }

        /// <summary>
        /// Bake the NavMesh from the walkable tops alone — caps, ramps, stairs — so cliff faces
        /// are not merely too steep to walk, they are absent from the bake entirely. Adjacent
        /// caps a full step apart never merge under agentClimb 0.45, so terraces stay terraces.
        ///
        /// <para>Stairs are the routable level change: their treads are flat and their risers
        /// (step ÷ 8 = 0.375) merge under the climb. The smooth ramp piece rises the whole step
        /// over one cell — 72° at the 3 m step — and correctly fails the 40° slope, which is
        /// fine: stairs are the host's default. Baked through NavMeshBuilder because
        /// CreateSettings/GetSettingsByID return struct copies and a NavMeshSurface pointed at
        /// "custom" settings silently bakes with defaults — the recorded trap.</para>
        /// </summary>
        private void BakeNavMesh()
        {
            var settings = UnityEngine.AI.NavMesh.GetSettingsByID(0);
            settings.agentSlope = 40f;
            settings.agentClimb = 0.45f;
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;

            var sources = new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(
                _walkableRoot, ~0, UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes, 0,
                new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);

            var bounds = new Bounds(Vector3.zero,
                                    new Vector3(_map.BoundsSize.x + 8f, 24f, _map.BoundsSize.y + 8f));

            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, transform.position, Quaternion.identity);

            _navMeshInstance = UnityEngine.AI.NavMesh.AddNavMeshData(data);
        }

        private void OnDestroy()
        {
            // Withdraw the ground before it dangles — a host in the NEXT scene republishes.
            if (_published != null &&
                ReferenceEquals(Presentation.TopDownGroundSource.Current, _published))
                Presentation.TopDownGroundSource.Current = null;

            // The baked data would otherwise outlive its island and answer for a scene that is
            // no longer there.
            _navMeshInstance.Remove();
        }
    }
}
