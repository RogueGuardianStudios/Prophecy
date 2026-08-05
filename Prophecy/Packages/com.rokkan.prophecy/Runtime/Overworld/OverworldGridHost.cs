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

        private OverworldBuildOutput _built;
        private ITopDownGround _published;
        private Transform _walkableRoot;
        private UnityEngine.AI.NavMeshDataInstance _navMeshInstance;

        /// <summary>The compiled tile grid — region queries, the darkening repaint, and the
        /// encounter spawner's placement checks all read from here. Null before Awake.</summary>
        public OverworldTileGrid Grid => _built?.Grid;

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

            // Walkable tops and scenery under separate roots, so the NavMesh bakes from the
            // walkable half alone — a cliff face that is never a bake source can never be
            // climbed, whatever the slope limit says. The assembly itself lives in
            // OverworldWorldBuilder, shared with the editor's live preview: what the map tool
            // shows IS what this host builds.
            _walkableRoot = new GameObject("Ground_Walkable").transform;
            _walkableRoot.SetParent(transform, false);
            var sceneryRoot = new GameObject("Ground_Scenery").transform;
            sceneryRoot.SetParent(transform, false);

            _built = OverworldWorldBuilder.Build(_map, _tiles, _walkableRoot, sceneryRoot,
                                                 transform.position, _stairsForRamps);

            BakeNavMesh();

            _published = _groundAuthority == GroundAuthority.NavMesh
                ? (ITopDownGround)new NavMeshGround()
                : new TileGridGround(_built.Grid);
            Presentation.TopDownGroundSource.Current = _published;
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
