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

        private GridRegistry _registry;

        /// <summary>The live registry, for anything that wants to query the ground — a future
        /// walkability check, a region lookup, the darkening repaint. Null before Awake.</summary>
        public GridRegistry Registry => _registry;

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

            var context = new CreationContext
            {
                worldOffset = transform.position,
                topology = TopologyKind.HexOrganic,
                spacing = Mathf.Max(0.5f, _map.Spacing),
                jitter = _map.Jitter,
                mode = ConnectionMode.Supplementing,
                seed = _map.Seed,
                origin = "prophecy.overworld",
            };

            var bounds = new Bounds(transform.position,
                                    new Vector3(_map.BoundsSize.x, 0f, _map.BoundsSize.y));

            WorldGridId = _registry.CreateWorldGrid(bounds, context, null, _tileSet, null, _config);

            var entry = _registry.Get(WorldGridId);

            PaintAuthoredRegions(entry);

            StalbergTilePlacer.PlaceTiles(entry.Grid, _tileSet, transform);

            StripColliders();
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
                    Region.Room((byte)(i + 1), footprint, authored.Y));
            }

            VertexTopologyPainter.Paint(entry.Grid, _config);
        }

        private void StripColliders()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Destroy(colliders[i]);
        }

        private void OnDestroy()
        {
            // NativeContainers all the way down — the registry owns every grid's buffers and the
            // scene unloading is the one reliable moment to let go of them.
            _registry?.Dispose();
            _registry = null;
        }
    }
}
