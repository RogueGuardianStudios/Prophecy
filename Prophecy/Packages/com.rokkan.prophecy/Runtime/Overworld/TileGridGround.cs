using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The ground seam answered straight from the tile grid — the third and best
    /// <see cref="ITopDownGround"/>: exact, plain C#, headless, and incapable of disagreeing with
    /// the rendered tiles because both read the same cells.
    ///
    /// <para><b>Connectivity is the data's rule, not a height comparison.</b> Neighbouring cells
    /// connect iff they are the same level, or a ramp cell joins them: a ramp's low edge meets
    /// its level, its high edge meets level+1, and its sides meet nothing — you cannot board a
    /// stair from the side, exactly as in the game this grammar comes from. Parallel ramp cells
    /// of the same facing and level connect sideways, so a wide stair is one stair.</para>
    ///
    /// <para>The escape rule survives from the walk grid: a body standing somewhere unwalkable —
    /// a teleport, an old save, a bug — may always step onto real floor, whatever the height.</para>
    /// </summary>
    public sealed class TileGridGround : ITopDownGround
    {
        private readonly OverworldTileGrid _grid;

        public TileGridGround(OverworldTileGrid grid) => _grid = grid;

        public float HeightAt(Vector2 point) =>
            _grid.TryCellAt(point, out int x, out int z) ? _grid.SurfaceHeight(x, z, point) : 0f;

        public bool CanStep(Vector2 from, Vector2 to)
        {
            if (!_grid.TryCellAt(to, out int tx, out int tz) ||
                _grid.KindAt(tx, tz) == TileCellKind.Sea)
                return false;

            if (!_grid.TryCellAt(from, out int fx, out int fz) ||
                _grid.KindAt(fx, fz) == TileCellKind.Sea)
                return true;   // the escape rule

            if (fx == tx && fz == tz) return true;

            int dx = tx - fx;
            int dz = tz - fz;

            if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1)
            {
                if (dx == 0 || dz == 0) return Connected(fx, fz, tx, tz);

                // A diagonal crossing is legal iff either L-shaped path is — the body cannot cut
                // a corner two flat cells refuse to share.
                return (Connected(fx, fz, tx, fz) && Connected(tx, fz, tx, tz)) ||
                       (Connected(fx, fz, fx, tz) && Connected(fx, tz, tx, tz));
            }

            // A probe longer than a cell only happens for teleport-scale moves; walk the segment
            // in half-cell samples so the answer is still the cells' answer.
            var delta = to - from;
            int samples = Mathf.CeilToInt(delta.magnitude / (OverworldTileGrid.CellSize * 0.5f));
            var previous = from;
            for (int i = 1; i <= samples; i++)
            {
                var next = from + delta * (i / (float)samples);
                if (!CanStep(previous, next)) return false;
                previous = next;
            }

            return true;
        }

        /// <summary>Are these two 4-adjacent, non-sea cells connected across their shared edge?</summary>
        private bool Connected(int ax, int az, int bx, int bz)
        {
            var kindA = _grid.KindAt(ax, az);
            var kindB = _grid.KindAt(bx, bz);
            if (kindA == TileCellKind.Sea || kindB == TileCellKind.Sea) return false;

            int dx = bx - ax;
            int dz = bz - az;

            // A wide stair: two run cells side by side at the same position in their climbs
            // share a continuously equal edge — walking across the stair is allowed.
            if (kindA == TileCellKind.Ramp && kindB == TileCellKind.Ramp &&
                _grid.FacingAt(ax, az) == _grid.FacingAt(bx, bz) &&
                _grid.LevelAt(ax, az) == _grid.LevelAt(bx, bz) &&
                _grid.RampIndexAt(ax, az) == _grid.RampIndexAt(bx, bz) &&
                IsSide(_grid.FacingAt(ax, az), dx, dz))
                return true;

            int edgeA = EdgeLevel(ax, az, dx, dz);
            int edgeB = EdgeLevel(bx, bz, -dx, -dz);

            return edgeA >= 0 && edgeB >= 0 && edgeA == edgeB;
        }

        /// <summary>
        /// The height a cell presents at the edge in direction (dx, dz), in 1/RampRun level
        /// units, or −1 for "no entry" (a ramp's side). Ground presents level×RampRun
        /// everywhere; run cell i presents +i at its low edge and +i+1 at its high edge — so a
        /// climb chains foot → run cells → head on exact integer matches, no float anywhere.
        /// </summary>
        private int EdgeLevel(int x, int z, int dx, int dz)
        {
            int scaled = _grid.LevelAt(x, z) * OverworldTileGrid.RampRun;
            if (_grid.KindAt(x, z) != TileCellKind.Ramp) return scaled;

            var facing = _grid.FacingAt(x, z);
            if (IsSide(facing, dx, dz)) return -1;

            bool towardHigh =
                (facing == RampFacing.PlusZ && dz > 0) ||
                (facing == RampFacing.MinusZ && dz < 0) ||
                (facing == RampFacing.PlusX && dx > 0) ||
                (facing == RampFacing.MinusX && dx < 0);

            return scaled + _grid.RampIndexAt(x, z) + (towardHigh ? 1 : 0);
        }

        private static bool IsSide(RampFacing facing, int dx, int dz) =>
            facing == RampFacing.PlusZ || facing == RampFacing.MinusZ ? dz == 0 : dx == 0;
    }
}
