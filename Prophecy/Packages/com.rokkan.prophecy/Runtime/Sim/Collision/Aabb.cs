using UnityEngine;

namespace Rokkan.Prophecy.Sim.Collision
{
    /// <summary>
    /// An axis-aligned box in the sim's 2D movement plane. A plain value type — <c>Vector2</c> is
    /// a math struct with no scene coupling, so this stays headless (see
    /// <see cref="CollisionWorld"/> for why the sim owns its own collision representation at all).
    ///
    /// <para>Stored as min/max rather than centre/extents because every query here is a
    /// separating-axis comparison, and min/max makes those direct.</para>
    /// </summary>
    public readonly struct Aabb
    {
        public readonly Vector2 Min;
        public readonly Vector2 Max;

        public Aabb(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }

        public static Aabb FromCenterSize(Vector2 center, Vector2 size)
        {
            var half = size * 0.5f;
            return new Aabb(center - half, center + half);
        }

        /// <summary>Bottom-centre anchored, the natural way to place a character standing on ground.</summary>
        public static Aabb FromFootSize(Vector2 foot, Vector2 size)
        {
            var halfWidth = size.x * 0.5f;
            return new Aabb(
                new Vector2(foot.x - halfWidth, foot.y),
                new Vector2(foot.x + halfWidth, foot.y + size.y));
        }

        public Vector2 Center => (Min + Max) * 0.5f;
        public Vector2 Size => Max - Min;
        public float Width => Max.x - Min.x;
        public float Height => Max.y - Min.y;

        public Aabb Translated(Vector2 delta) => new Aabb(Min + delta, Max + delta);

        /// <summary>
        /// Strict overlap: boxes that merely touch (one's max exactly equals the other's min) do
        /// NOT overlap. This matters — a character resting exactly on the ground plane must not
        /// read as intersecting it, or every grounded frame would trigger depenetration.
        /// </summary>
        public bool Overlaps(in Aabb other) =>
            Min.x < other.Max.x && Max.x > other.Min.x &&
            Min.y < other.Max.y && Max.y > other.Min.y;

        /// <summary>Overlap on the X axis only — used by the vertical sweep to decide which
        /// solids are even candidates.</summary>
        public bool OverlapsX(in Aabb other) => Min.x < other.Max.x && Max.x > other.Min.x;

        /// <summary>Overlap on the Y axis only.</summary>
        public bool OverlapsY(in Aabb other) => Min.y < other.Max.y && Max.y > other.Min.y;

        public Aabb Expanded(float amount) => new Aabb(
            new Vector2(Min.x - amount, Min.y - amount),
            new Vector2(Max.x + amount, Max.y + amount));

        public override string ToString() => $"Aabb[{Min} .. {Max}]";
    }
}
