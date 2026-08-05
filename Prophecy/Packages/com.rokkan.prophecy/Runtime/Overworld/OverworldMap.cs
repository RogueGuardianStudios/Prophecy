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
    }
}
