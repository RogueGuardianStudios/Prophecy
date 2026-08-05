using System;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// One hand-authored patch of the overworld: a footprint on the XZ plane that becomes floor.
    ///
    /// <para>This is the authoring vocabulary, kept deliberately small: a designer places named
    /// rectangles, and the Stålberg grid turns their union into an organic landmass — marching
    /// squares rounds the corners, jitter roughens the edges, and the seams between overlapping
    /// rects disappear. The hand authors the <i>shape</i>; the grid authors the <i>texture</i> of
    /// the shape. Elevation, biome palettes and the reserved region kinds (Settlement, Road,
    /// River…) layer onto this later without changing what an authored region is.</para>
    /// </summary>
    [Serializable]
    public sealed class AuthoredRegion
    {
        [Tooltip("For the inspector and for queries later. Carries no behaviour.")]
        public string Name = "Region";

        [Tooltip("Centre of the footprint on the ground plane, in metres. X is world X, Y is world Z.")]
        public Vector2 Centre;

        [Tooltip("Footprint size in metres.")]
        public Vector2 Size = new Vector2(20f, 20f);

        [Tooltip("Rotation of the footprint about its centre, in degrees.")]
        public float RotationDegrees;

        [Tooltip("Floor elevation. Keep 0 until the sim understands top-down height.")]
        public float Y;

        [Tooltip("Square lattice instead of organic — the footprint is injected as a structured " +
                 "region with a conforming seam into the surrounding hex. For coasts, settlements, " +
                 "anywhere the map should read built rather than grown.")]
        public bool Structured;
    }

    /// <summary>
    /// A hand-authored ramp: a strip of ground whose floor climbs linearly from one end to the
    /// other. The way up a terrace — the only way, since everything steeper is a cliff.
    /// </summary>
    [Serializable]
    public sealed class AuthoredRamp
    {
        [Tooltip("For the inspector. Carries no behaviour.")]
        public string Name = "Ramp";

        [Tooltip("Foot of the ramp on the ground plane (X, world Z).")]
        public Vector2 Start;

        [Tooltip("Head of the ramp.")]
        public Vector2 End;

        [Tooltip("Floor elevation at the foot.")]
        public float StartY;

        [Tooltip("Floor elevation at the head.")]
        public float EndY = OverworldTileGrid.Step;

        [Tooltip("Half the ramp's width, in metres. Wide enough to walk without hugging an edge.")]
        public float HalfWidth = 2f;
    }

    /// <summary>
    /// The hand-authored overworld, as data: grid settings plus the regions that shape it.
    ///
    /// <para>An asset rather than fields on the scene object, per the project rule — tuning lives
    /// in ScriptableObjects so edits survive play mode, and the overworld's shape is exactly the
    /// kind of thing that gets tuned by walking around in it.</para>
    /// </summary>
    /// <summary>How the ground's lattice is laid.</summary>
    public enum MapTopology
    {
        /// <summary>Townscaper: jittered hex-organic quads. Coastlines meander, corners round,
        /// the map reads grown.</summary>
        Organic,

        /// <summary>A Link to the Past: a square lattice, no jitter. Every wall, cliff and ramp
        /// boundary is an exact tile edge, and the map reads authored — because it is.</summary>
        Rectangular,
    }

    /// <summary>
    /// A hand-authored SECOND walkable surface over a footprint: below the terrain it is a cave
    /// floor (the terrain above becomes the roof), above it a bridge deck or overhang. Its edges
    /// connect to any adjacent surface at the same level automatically — a cave mouth is just
    /// where the floor meets open ground, no extra authoring.
    /// </summary>
    [Serializable]
    public sealed class AuthoredLayer
    {
        [Tooltip("For the inspector. Carries no behaviour.")]
        public string Name = "Layer";

        [Tooltip("Centre of the footprint on the ground plane, in metres. X is world X, Y is world Z.")]
        public Vector2 Centre;

        [Tooltip("Footprint size in metres.")]
        public Vector2 Size = new Vector2(4f, 3f);

        [Tooltip("Rotation of the footprint about its centre, in degrees.")]
        public float RotationDegrees;

        [Tooltip("Elevation of the extra surface. Below the terrain here: a cave floor. Above " +
                 "it: a bridge deck or overhang.")]
        public float Y;
    }

    /// <summary>
    /// A hand-authored river: a polyline course carved into the land as sea cells. Banks appear
    /// automatically (they are the same land-meets-sea boundary the coast is), the water is the
    /// same one sea plane, and crossing one takes a bridge — an <see cref="AuthoredLayer"/> deck
    /// over the channel at bank height.
    /// </summary>
    [Serializable]
    public sealed class AuthoredRiver
    {
        [Tooltip("For the inspector. Carries no behaviour.")]
        public string Name = "River";

        [Tooltip("The river's course on the ground plane (X, world Z), source to mouth.")]
        public Vector2[] Points = Array.Empty<Vector2>();

        [Tooltip("Half the channel's width, in metres.")]
        public float HalfWidth = 1.5f;
    }

    /// <summary>
    /// A hand-authored road: paint, not ground. Cells near the course are flagged and the host
    /// draws one flat strip over their walk surface — including across bridge decks, which is
    /// how a road crosses a river. Walkability is untouched; sloped cells carry no road (an
    /// ALttP road breaks at a stair and resumes beyond it).
    /// </summary>
    [Serializable]
    public sealed class AuthoredRoad
    {
        [Tooltip("For the inspector. Carries no behaviour.")]
        public string Name = "Road";

        [Tooltip("The road's course on the ground plane (X, world Z).")]
        public Vector2[] Points = Array.Empty<Vector2>();

        [Tooltip("Half the road's width, in metres.")]
        public float HalfWidth = 1f;
    }

    /// <summary>
    /// One feathered patch of biome INFLUENCE — the splat model. Influence is 1 inside the
    /// rect, fading to 0 over <see cref="Feather"/> metres beyond it; overlapping areas of
    /// different biomes blend, and the compiler resolves each cell's dominant + secondary +
    /// blend. Geometry follows the DOMINANT biome (discrete, deterministic); the terrain
    /// shader gets the raw blend, which is what hides the flip.
    /// </summary>
    [Serializable]
    public sealed class AuthoredBiomeArea
    {
        [Tooltip("For the inspector. Carries no behaviour.")]
        public string Name = "Biome Area";

        [Tooltip("Which biome this area spreads. Palette slot index — the OverworldBiome " +
                 "asset arrives with the variant picker; the index is stable either way.")]
        public int BiomeIndex;

        [Tooltip("Centre of the footprint on the ground plane, in metres. X is world X, Y is world Z.")]
        public Vector2 Centre;

        [Tooltip("Footprint size in metres — full influence inside.")]
        public Vector2 Size = new Vector2(20f, 20f);

        [Tooltip("Rotation of the footprint about its centre, in degrees.")]
        public float RotationDegrees;

        [Tooltip("Metres past the rect edge over which influence fades to zero.")]
        public float Feather = 6f;
    }

    /// <summary>
    /// A hand-authored impassable mass — the ALttP forest blob. Thicket cells REFUSE walking
    /// (the same no-surface rule prop footprints use) and the biome's scatter fills them with
    /// trees; open walkable ground is never scattered, because open space is for travelling
    /// (Matt, 2026-08-05) — dress it with hand-placed props or shaders instead.
    /// </summary>
    [Serializable]
    public sealed class AuthoredThicket
    {
        [Tooltip("For the inspector. Carries no behaviour.")]
        public string Name = "Thicket";

        [Tooltip("Centre of the footprint on the ground plane, in metres. X is world X, Y is world Z.")]
        public Vector2 Centre;

        [Tooltip("Footprint size in metres.")]
        public Vector2 Size = new Vector2(6f, 6f);

        [Tooltip("Rotation of the footprint about its centre, in degrees.")]
        public float RotationDegrees;
    }

    /// <summary>What a painted cell says about thicket. Add plants, Remove carves a clearing
    /// out of a shape-authored mass.</summary>
    public enum ThicketOverride : byte
    {
        None,
        Add,
        Remove,
    }

    /// <summary>What a painted cell says about its terrain. None = whatever the shapes made.</summary>
    public enum TerrainOverride : byte
    {
        None,
        Ground,
        Sea,
    }

    /// <summary>What a painted cell says about road paint. Add and Remove both beat the shapes.</summary>
    public enum RoadOverride : byte
    {
        None,
        Add,
        Remove,
    }

    /// <summary>
    /// One hand-painted cell: the fine-control half of the hybrid authoring grain. The shapes
    /// block out the macro structure; these pin individual cells where the shapes aren't
    /// enough. Sparse by design — overrides are exceptions, and a sparse list survives bounds
    /// changes and diffs cleanly. Applied after regions and rivers (painted land can restore a
    /// carved channel) and before ramps (a painted terrace edge is rampable).
    /// </summary>
    [Serializable]
    public sealed class AuthoredCellOverride
    {
        public int X;
        public int Z;

        public TerrainOverride Terrain;

        [Tooltip("Meaningful when Terrain is set. For Sea this is the WATER level.")]
        public int Level;

        public RoadOverride Road;

        [Tooltip("Painted biome: −1 = none, otherwise a palette index. A painted cell " +
                 "dominates the feathered areas — the hand beats the field.")]
        public int Biome = -1;

        [Tooltip("Painted thicket: Add plants an impassable scatter-filled cell, Remove " +
                 "carves a clearing from a shape-authored mass.")]
        public ThicketOverride Thicket;
    }

    /// <summary>
    /// A placed object on the tiles: a town, a tree, a signpost. Position is plane-only ON
    /// PURPOSE — height derives from the ground at spawn (the base surface, or the overlay
    /// deck when SurfaceLayer says so), so a terrain retune never strands a prop in the air.
    /// The compiler reads only the footprint fields; the Prefab is the builder's business —
    /// the headless contract holds.
    /// </summary>
    [Serializable]
    public sealed class AuthoredProp
    {
        [Tooltip("For the inspector. Carries no behaviour.")]
        public string Name = "Prop";

        [Tooltip("What to spawn. Read by the world builder only, never the compiler.")]
        public GameObject Prefab;

        [Tooltip("Where it stands on the ground plane (X, world Z).")]
        public Vector2 Position;

        [Tooltip("Yaw in degrees. Cosmetic — the blocking footprint stays axis-aligned.")]
        public float YawDegrees;

        [Tooltip("0 = the base terrain; 1 = the overlay deck at this cell, when one exists.")]
        public int SurfaceLayer;

        [Tooltip("Whether walking through this prop's cells is refused.")]
        public bool BlockCells;

        [Tooltip("Blocking footprint in cells, centred on Position. Axis-aligned; yaw does " +
                 "not rotate it (v1 limitation).")]
        public Vector2Int BlockSize = Vector2Int.one;
    }

    [CreateAssetMenu(menuName = "Prophecy/Overworld Map", fileName = "OverworldMap")]
    public sealed class OverworldMap : ScriptableObject
    {
        [Header("Grid")]
        [Tooltip("Square tiles for legible Zelda boundaries, or organic quads for a grown look. " +
                 "In Rectangular, Spacing is the tile size and Jitter is ignored.")]
        public MapTopology Topology = MapTopology.Rectangular;

        [Tooltip("Seed for the organic jitter. The same seed always produces the same coastline " +
                 "— determinism is the grid package's hard contract.")]
        public uint Seed = 7;

        [Tooltip("Metres between grid vertices. Smaller is smoother coastline and more tiles.")]
        public float Spacing = 3f;

        [Range(0f, 1f), Tooltip("How far vertices wander from their lattice positions. The " +
                                "difference between graph paper and a hand-drawn map.")]
        public float Jitter = 0.35f;

        [Tooltip("Total generated area in metres, XZ. Regions outside it are clipped.")]
        public Vector2 BoundsSize = new Vector2(96f, 72f);

        [Header("The land")]
        [Tooltip("Union of these footprints becomes the floor. Order does not matter.")]
        public AuthoredRegion[] Regions = Array.Empty<AuthoredRegion>();

        [Tooltip("Sloped strips connecting elevations. Painted after the regions, so a ramp " +
                 "cuts its grade into whatever terraces it spans.")]
        public AuthoredRamp[] Ramps = Array.Empty<AuthoredRamp>();

        [Tooltip("Extra walkable surfaces over the terrain: cave floors below it, bridge decks " +
                 "and overhangs above it.")]
        public AuthoredLayer[] Layers = Array.Empty<AuthoredLayer>();

        [Tooltip("Channels carved through the land after the regions, before the ramps. Cross " +
                 "one on a bridge: a Layer deck over the channel at bank height.")]
        public AuthoredRiver[] Rivers = Array.Empty<AuthoredRiver>();

        [Tooltip("Painted strips over the walk surface. Purely visual — walkability never " +
                 "changes. A road runs across any bridge deck on its course.")]
        public AuthoredRoad[] Roads = Array.Empty<AuthoredRoad>();

        [Tooltip("Hand-painted per-cell exceptions to whatever the shapes produce. Authored " +
                 "with the Overworld Map Tool, not by hand.")]
        public AuthoredCellOverride[] CellOverrides = Array.Empty<AuthoredCellOverride>();

        [Tooltip("Placed objects standing on the tiles: towns, trees, detail. Height derives " +
                 "from the ground at spawn.")]
        public AuthoredProp[] Props = Array.Empty<AuthoredProp>();

        [Tooltip("Feathered biome influence patches — the splat. Overlaps blend; the biome " +
                 "paint brush pins individual cells over them.")]
        public AuthoredBiomeArea[] BiomeAreas = Array.Empty<AuthoredBiomeArea>();

        [Tooltip("Impassable scatter-filled masses — forests, briars. Walking refuses; the " +
                 "biome's scatter list fills them.")]
        public AuthoredThicket[] Thickets = Array.Empty<AuthoredThicket>();
    }
}
