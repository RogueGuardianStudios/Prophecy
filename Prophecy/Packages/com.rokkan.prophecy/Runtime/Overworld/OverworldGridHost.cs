using Rokkan.Prophecy.Sim;
using Rokkan.WorldGen.StalbergGrid;
using Rokkan.WorldGen.StalbergGrid.Data;
using Rokkan.WorldGen.StalbergGrid.Generation;
using Rokkan.WorldGen.StalbergGrid.Mesh;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// Builds the overworld's ground from a hand-authored <see cref="OverworldMap"/>: an organic
    /// Stålberg grid, shaped by the authored regions, populated by the marching-squares tile set.
    ///
    /// <para><b>Built at load, not baked into the scene.</b> The grid is deterministic from the
    /// map's seed, so the scene stores the recipe rather than nine hundred tile instances — the
    /// same reasoning as every gray-box generator, moved to runtime because that is how the
    /// package is designed to run, and because the darkening overworld (design bible §5) will
    /// eventually repaint regions in play, which a baked scene cannot.</para>
    ///
    /// <para><b>The colliders are stripped from every placed tile, deliberately.</b> Top-down has
    /// no collision, and a tile that bakes into the sim's <c>CollisionWorld</c> is worse than
    /// useless — the XZ projection turns floor into one giant solid that occludes every line of
    /// sight, which is the lesson the flat-plain overworld already taught once. When top-down
    /// walkability becomes real it will be answered from the grid's own vertex state
    /// (<c>isFloor</c>), not from colliders.</para>
    /// </summary>
    public sealed class OverworldGridHost : MonoBehaviour
    {
        [SerializeField, Tooltip("The hand-authored map this builds.")]
        private OverworldMap _map;

        [SerializeField, Tooltip("Grid tuning — spacing caps, ramp rules, wall height.")]
        private StalbergGridConfig _config;

        [SerializeField, Tooltip("The six marching-squares tile prefabs, plus door and stair.")]
        private StalbergTileSet _tileSet;

        [SerializeField, Tooltip("Applied to every placed tile. The prefabs ship pointing at the " +
                                 "built-in Default-Diffuse, which URP renders magenta.")]
        private Material _tileMaterial;

        /// <summary>Which implementation answers the sim's ground seam.</summary>
        public enum GroundAuthority
        {
            /// <summary>The baked NavMesh — one authority shared with future AI routing; step
            /// and slope rules come from the bake's agent settings.</summary>
            NavMesh,

            /// <summary>The plain-C# raster over the grid's floor quads — headless-reproducible,
            /// no engine data in the answers.</summary>
            WalkGrid,
        }

        [SerializeField, Tooltip("Who bounds movement here. Both exist behind the same seam; " +
                                 "flip and re-enter the scene to feel the difference.")]
        private GroundAuthority _groundAuthority = GroundAuthority.NavMesh;

        private GridRegistry _registry;
        private OverworldWalkGrid _walkGrid;

        /// <summary>The live registry, for anything that wants to query the ground — a future
        /// region lookup, the darkening repaint. Null before Awake.</summary>
        public GridRegistry Registry => _registry;

        /// <summary>The walkability raster the sim collides against. Null before Awake.</summary>
        public OverworldWalkGrid WalkGrid => _walkGrid;

        /// <summary>The world grid's id within <see cref="Registry"/>.</summary>
        public GridId WorldGridId { get; private set; }

        private void Awake()
        {
            if (_map == null || _config == null || _tileSet == null)
            {
                Debug.LogError($"{name}: map, config and tile set are all required — the " +
                               "overworld has no ground without them.", this);
                return;
            }

            _registry = new GridRegistry();

            // Regions marked Structured are injected into the factory itself, not merely painted:
            // a Mixed grid lays a square lattice inside each one with a conforming seam to the
            // surrounding hex. That is the "built, not grown" look — the lever the coasts and
            // settlements experiment on.
            var structured = CollectStructuredRegions();

            // Rectangular is the whole lattice, so the per-region Structured toggle has nothing
            // left to inject there — square rooms in a square world is just the world.
            var topology = _map.Topology == MapTopology.Rectangular
                ? TopologyKind.RectAxisAligned
                : structured == null ? TopologyKind.HexOrganic : TopologyKind.Mixed;

            var context = new CreationContext
            {
                worldOffset = transform.position,
                topology = topology,
                spacing = Mathf.Max(0.5f, _map.Spacing),
                jitter = _map.Topology == MapTopology.Rectangular ? 0f : _map.Jitter,
                mode = ConnectionMode.Supplementing,
                seed = _map.Seed,
                origin = "prophecy.overworld",
            };

            var bounds = new Bounds(transform.position,
                                    new Vector3(_map.BoundsSize.x, 0f, _map.BoundsSize.y));

            WorldGridId = _registry.CreateWorldGrid(bounds, context, structured, _tileSet, null, _config);

            var entry = _registry.Get(WorldGridId);

            PaintAuthoredRegions(entry);

            StalbergTilePlacer.PlaceTiles(entry.Grid, _tileSet, transform);

            StripColliders();
            ApplyTileMaterial();

            BuildWalkGrid(entry);
            BakeNavMesh();

            // Both grounds exist; the toggle decides who answers the sim. The walk grid is built
            // regardless — it costs milliseconds and keeps the A/B honest.
            _published = _groundAuthority == GroundAuthority.NavMesh
                ? (ITopDownGround)new NavMeshGround()
                : _walkGrid;
            Presentation.TopDownGroundSource.Current = _published;
        }

        private ITopDownGround _published;

        /// <summary>
        /// Bake the NavMesh over the placed tiles, at load, from their render meshes — the
        /// colliders were stripped on purpose and the bake does not need them.
        ///
        /// <para>This is the authority AI routing will use later, so baking it here even when the
        /// walk grid answers the seam keeps the two futures on one surface. The bake runs under
        /// the transition veil; nobody sees the frame it costs.</para>
        /// </summary>
        private void BakeNavMesh()
        {
            // Slope-limited on purpose: the default 45° walker climbs the tiles' skirt geometry
            // and connects every terrace to the plain everywhere, making the cliffs decorative.
            // At 25° the skirts stay cliffs and the authored ramps (a few degrees) are the only
            // way up — elevation you must find the road to.
            //
            // Baked through NavMeshBuilder rather than a NavMeshSurface, and not only for the
            // lighter dependency: CreateSettings/GetSettingsByID hand back STRUCT COPIES, so a
            // surface pointed at "custom" settings quietly bakes with the registry's defaults —
            // which is exactly the 45°-skirt bug this replaced. BuildNavMeshData takes the
            // settings by value; what you set is what bakes.
            var settings = UnityEngine.AI.NavMesh.GetSettingsByID(0);
            settings.agentSlope = 25f;
            settings.agentClimb = 0.3f;
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;

            var sources = new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(
                transform, ~0, UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes, 0,
                new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);

            var bounds = new Bounds(Vector3.zero,
                                    new Vector3(_map.BoundsSize.x + 8f, 24f, _map.BoundsSize.y + 8f));

            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, transform.position, Quaternion.identity);

            _navMeshInstance = UnityEngine.AI.NavMesh.AddNavMeshData(data);
        }

        private UnityEngine.AI.NavMeshDataInstance _navMeshInstance;

        /// <summary>
        /// Raster the grid's floor quads into the walkability grid the sim collides against.
        ///
        /// <para>A quad counts as floor only when all four of its vertices do — the partial quads
        /// at a coastline stay sea, so collision runs one tile tighter than the marching-squares
        /// art. Height is the corners' average, which within one quad differ at most by jitter.</para>
        /// </summary>
        private void BuildWalkGrid(GridEntry entry)
        {
            _walkGrid = new OverworldWalkGrid(
                new Vector2(transform.position.x - _map.BoundsSize.x * 0.5f,
                            transform.position.z - _map.BoundsSize.y * 0.5f),
                _map.BoundsSize);

            var grid = entry.Grid;

            for (int i = 0; i < grid.Quads.Length; i++)
            {
                var quad = grid.Quads[i];

                var a = grid.Vertices[quad.vertexIndices.x];
                var b = grid.Vertices[quad.vertexIndices.y];
                var c = grid.Vertices[quad.vertexIndices.z];
                var d = grid.Vertices[quad.vertexIndices.w];

                if (!a.isFloor || !b.isFloor || !c.isFloor || !d.isFloor) continue;

                _walkGrid.FillQuad(
                    new Vector2(a.position.x, a.position.z),
                    new Vector2(b.position.x, b.position.z),
                    new Vector2(c.position.x, c.position.z),
                    new Vector2(d.position.x, d.position.z),
                    (a.position.y + b.position.y + c.position.y + d.position.y) * 0.25f);
            }
        }

        private System.Collections.Generic.List<StructuredRegion> CollectStructuredRegions()
        {
            System.Collections.Generic.List<StructuredRegion> structured = null;

            var regions = _map.Regions;
            for (int i = 0; regions != null && i < regions.Length; i++)
            {
                if (!regions[i].Structured) continue;

                structured ??= new System.Collections.Generic.List<StructuredRegion>();
                structured.Add(new StructuredRegion
                {
                    center = new Vector2(regions[i].Centre.x + transform.position.x,
                                         regions[i].Centre.y + transform.position.z),
                    size = regions[i].Size,
                    rotationDegrees = regions[i].RotationDegrees,
                    regionId = (byte)(i + 1),
                    worldY = regions[i].Y,
                });
            }

            return structured;
        }

        /// <summary>
        /// The hand's half of the bargain: every authored footprint becomes a floor region, and
        /// the painter takes it from there. Ids are 1..n in authored order — stable for a given
        /// asset, which keeps the paint deterministic alongside the grid itself.
        /// </summary>
        private void PaintAuthoredRegions(GridEntry entry)
        {
            var regions = _map.Regions;

            if (regions == null || regions.Length == 0)
            {
                Debug.LogWarning($"{name}: the map has no authored regions — the overworld is " +
                                 "all coastline and no land.", this);
                return;
            }

            for (int i = 0; i < regions.Length; i++)
            {
                var authored = regions[i];

                var footprint = new OrientedRect(
                    new Vector2(authored.Centre.x + transform.position.x,
                                authored.Centre.y + transform.position.z),
                    authored.Size, authored.RotationDegrees);

                entry.Grid.Registry.AddRegion(
                    Region.Room((byte)(i + 1), footprint, authored.Y,
                                shape: authored.Structured ? RegionShape.AxisAligned
                                                           : RegionShape.Organic));
            }

            // Ramps go in after the regions so their linear grade paints over the terraces it
            // spans — the corridor's Y function is the whole reason elevations are climbable.
            var ramps = _map.Ramps;
            var offset = new Vector2(transform.position.x, transform.position.z);

            for (int i = 0; ramps != null && i < ramps.Length; i++)
            {
                var ramp = ramps[i];

                entry.Grid.Registry.AddRegion(Region.Corridor(
                    (byte)(regions.Length + i + 1),
                    ramp.Start + offset, ramp.StartY,
                    ramp.End + offset, ramp.EndY,
                    Mathf.Max(0.5f, ramp.HalfWidth)));
            }

            VertexTopologyPainter.Paint(entry.Grid, _config);
        }

        private void StripColliders()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Destroy(colliders[i]);
        }

        /// <summary>One ground material over every placed tile — see the field's tooltip.</summary>
        private void ApplyTileMaterial()
        {
            if (_tileMaterial == null) return;

            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = _tileMaterial;
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

            // NativeContainers all the way down — the registry owns every grid's buffers and the
            // scene unloading is the one reliable moment to let go of them.
            _registry?.Dispose();
            _registry = null;
        }
    }
}
