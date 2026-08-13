using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The island's walkability, rastered once at load: a plain grid of "floor here, at this
    /// height" cells that <see cref="ITopDownGround"/> queries answer from in constant time.
    ///
    /// <para><b>Derived from the same quads the tiles are placed from</b>, which is what makes
    /// collision and visuals incapable of disagreeing: a quad whose four corners are all floor is
    /// filled walkable at its corners' height; everything else — coast edge, void, the face of a
    /// cliff — stays unwalkable. The partial quads at a coastline are deliberately counted as
    /// sea, so collision is one tile <i>tighter</i> than the marching-squares art. Stopping a
    /// step short of a painted edge feels safe; sliding one tile past it into the void reads as
    /// falling out of the world.</para>
    ///
    /// <para><b>Steps are legal only when the ground rises or falls gently.</b> A terrace edge is
    /// a wall from below and a ledge from above; both directions refuse. When ramps are authored
    /// (a Linear-Y region), their gradual cells pass the same test without any new rule.</para>
    ///
    /// <para>Plain C#, filled through <see cref="FillQuad"/> with plain values — the grid host
    /// feeds it Stålberg quads, and a test feeds it hand-made ones. Nothing here knows the
    /// worldgen package exists.</para>
    /// </summary>
    public sealed class OverworldWalkGrid : ITopDownGround
    {
        /// <summary>Biggest height difference one step may cross, in metres. Above this, ground
        /// is a wall. Half a kerb, not half a terrace.</summary>
        public const float MaxStep = 0.35f;

        private readonly Vector2 _origin;
        private readonly float _cellSize;
        private readonly int _width;
        private readonly int _height;
        private readonly bool[] _walkable;
        private readonly float[] _floorY;

        public OverworldWalkGrid(Vector2 origin, Vector2 size, float cellSize = 0.5f)
        {
            _origin = origin;
            _cellSize = Mathf.Max(0.1f, cellSize);
            _width = Mathf.Max(1, Mathf.CeilToInt(size.x / _cellSize));
            _height = Mathf.Max(1, Mathf.CeilToInt(size.y / _cellSize));
            _walkable = new bool[_width * _height];
            _floorY = new float[_width * _height];
        }

        public int CellCount => _width * _height;

        /// <summary>
        /// Mark every cell whose centre lies inside the (convex, CCW) quad as floor at
        /// <paramref name="floorY"/>. Called once per all-floor quad at build; later fills win
        /// ties, which does not matter — two floor quads sharing a cell agree about it being
        /// floor, and their heights differ at most by a seam.
        /// </summary>
        public void FillQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float floorY)
        {
            float minX = Mathf.Min(a.x, b.x, c.x, d.x);
            float maxX = Mathf.Max(a.x, b.x, c.x, d.x);
            float minY = Mathf.Min(a.y, b.y, c.y, d.y);
            float maxY = Mathf.Max(a.y, b.y, c.y, d.y);

            int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - _origin.x) / _cellSize));
            int x1 = Mathf.Min(_width - 1, Mathf.FloorToInt((maxX - _origin.x) / _cellSize));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((minY - _origin.y) / _cellSize));
            int y1 = Mathf.Min(_height - 1, Mathf.FloorToInt((maxY - _origin.y) / _cellSize));

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var centre = new Vector2(_origin.x + (x + 0.5f) * _cellSize,
                                             _origin.y + (y + 0.5f) * _cellSize);

                    if (!InsideQuad(centre, a, b, c, d)) continue;

                    int index = y * _width + x;
                    _walkable[index] = true;
                    _floorY[index] = floorY;
                }
            }
        }

        // ---------------------------------------------------------------- ITopDownGround

        public bool CanStep(Vector2 from, Vector2 to)
        {
            if (!TryCell(to, out int toIndex) || !_walkable[toIndex]) return false;

            // Standing somewhere unwalkable — a teleport, an old save, a bug — must never trap:
            // any step onto real floor is an escape, whatever the height difference.
            if (!TryCell(from, out int fromIndex) || !_walkable[fromIndex]) return true;

            return Mathf.Abs(_floorY[toIndex] - _floorY[fromIndex]) <= MaxStep;
        }

        public float HeightAt(Vector2 point) =>
            TryCell(point, out int index) && _walkable[index] ? _floorY[index] : 0f;

        // ---------------------------------------------------------------- internals

        private bool TryCell(Vector2 point, out int index)
        {
            int x = Mathf.FloorToInt((point.x - _origin.x) / _cellSize);
            int y = Mathf.FloorToInt((point.y - _origin.y) / _cellSize);

            if (x < 0 || x >= _width || y < 0 || y >= _height)
            {
                index = -1;
                return false;
            }

            index = y * _width + x;
            return true;
        }

        /// <summary>Point-in-convex-quad by same-side cross products. Winding-agnostic: all four
        /// signs simply have to agree.</summary>
        private static bool InsideQuad(Vector2 point, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float ab = Cross(b - a, point - a);
            float bc = Cross(c - b, point - b);
            float cd = Cross(d - c, point - c);
            float da = Cross(a - d, point - d);

            bool anyNegative = ab < 0f || bc < 0f || cd < 0f || da < 0f;
            bool anyPositive = ab > 0f || bc > 0f || cd > 0f || da > 0f;

            return !(anyNegative && anyPositive);
        }

        private static float Cross(Vector2 lhs, Vector2 rhs) => lhs.x * rhs.y - lhs.y * rhs.x;
    }
}
