using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The ground seam answered straight from the tile grid — exact, plain C#, headless, and
    /// incapable of disagreeing with the rendered tiles because both read the same cells.
    ///
    /// <para><b>Connectivity is the data's rule, not a height comparison.</b> Neighbouring
    /// surfaces connect iff their shared edge heights match exactly (in 1/RampRun fixed-point
    /// units): flats to flats, run cells end-to-end, a ramp's sides to nothing — you cannot
    /// board a stair from the side. Parallel run columns connect only at the same position in
    /// their climbs.</para>
    ///
    /// <para><b>Layers.</b> A cell may carry a second surface — a bridge deck above the terrain
    /// or a cave floor beneath it. Which surface a body is on is resolved by connectivity and
    /// carried in the sim's opaque layer token (0 = base, 1 = overlay): stepping across an edge
    /// picks the target surface whose edge height matches the current one, and updates the
    /// token. Position alone is ambiguous over an overlap; position plus token is exact.</para>
    ///
    /// <para>The escape rule survives: a body standing somewhere with no surface at all — a
    /// teleport, an old save, a bug — may always step onto real floor.</para>
    /// </summary>
    public sealed class TileGridGround : ITopDownGround
    {
        private const int Run = OverworldTileGrid.RampRun;

        private readonly OverworldTileGrid _grid;

        public TileGridGround(OverworldTileGrid grid) => _grid = grid;

        public float HeightAt(Vector2 point) => HeightAt(point, 0);

        public float HeightAt(Vector2 point, int layer)
        {
            if (!_grid.TryCellAt(point, out int x, out int z)) return 0f;

            if (ResolveLayer(x, z, layer) == 1 && _grid.TryOverlayAt(x, z, out int overlay))
                return overlay * OverworldTileGrid.Step;

            return _grid.SurfaceHeight(x, z, point);
        }

        public bool CanStep(Vector2 from, Vector2 to)
        {
            int layer = 0;
            return CanStep(from, to, ref layer);
        }

        /// <summary>Nearest surface to <paramref name="height"/> wins; the base surface wins
        /// ties. Over open sea the deck is the only surface, whatever the height says.</summary>
        public int LayerFor(Vector2 point, float height)
        {
            if (!_grid.TryCellAt(point, out int x, out int z)) return 0;
            if (!_grid.TryOverlayAt(x, z, out int overlay)) return 0;
            if (_grid.KindAt(x, z) == TileCellKind.Sea) return 1;

            float baseH = _grid.SurfaceHeight(x, z, point);
            float overlayH = overlay * OverworldTileGrid.Step;
            return Mathf.Abs(overlayH - height) < Mathf.Abs(baseH - height) ? 1 : 0;
        }

        public bool CanStep(Vector2 from, Vector2 to, ref int layer)
        {
            if (!_grid.TryCellAt(to, out int tx, out int tz) || !HasSurface(tx, tz))
                return false;

            if (!_grid.TryCellAt(from, out int fx, out int fz) || !HasSurface(fx, fz))
            {
                // The escape rule — and the escapee lands on the cell's real surface.
                layer = _grid.KindAt(tx, tz) != TileCellKind.Sea ? 0 : 1;
                return true;
            }

            int fromLayer = ResolveLayer(fx, fz, layer);

            if (fx == tx && fz == tz)
            {
                layer = fromLayer;
                return true;
            }

            int dx = tx - fx;
            int dz = tz - fz;

            if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1)
            {
                if (dx == 0 || dz == 0)
                    return StepAdjacent(fx, fz, fromLayer, tx, tz, ref layer);

                // A diagonal crossing is legal iff either L-shaped path is, threading the layer
                // through the corner cell — the body cannot cut a corner two surfaces refuse.
                int mid = fromLayer;
                if (StepAdjacent(fx, fz, fromLayer, tx, fz, ref mid) &&
                    StepAdjacent(tx, fz, mid, tx, tz, ref layer)) return true;

                mid = fromLayer;
                return StepAdjacent(fx, fz, fromLayer, fx, tz, ref mid) &&
                       StepAdjacent(fx, tz, mid, tx, tz, ref layer);
            }

            // A probe longer than a cell only happens for teleport-scale moves; walk the segment
            // in half-cell samples so the answer is still the cells' answer.
            var delta = to - from;
            int samples = Mathf.CeilToInt(delta.magnitude / (OverworldTileGrid.CellSize * 0.5f));
            var previous = from;
            int walking = fromLayer;
            for (int i = 1; i <= samples; i++)
            {
                var next = from + delta * (i / (float)samples);
                if (!CanStep(previous, next, ref walking)) return false;
                previous = next;
            }

            layer = walking;
            return true;
        }

        // ---------------------------------------------------------------- internals

        // A blocked cell (a prop's footprint) reads as having no surface at all: stepping INTO
        // one refuses, while a body stranded ON one gets the escape rule and may step off —
        // the same grace the sea gives.
        private bool HasSurface(int x, int z) =>
            !_grid.UnwalkableAt(x, z) &&
            (_grid.KindAt(x, z) != TileCellKind.Sea || _grid.TryOverlayAt(x, z, out _));

        /// <summary>The surface the token actually names on this cell: base unless the token
        /// says overlay AND one exists. A body over open sea under a bridge is on the deck
        /// whatever its token claims — the deck is the only surface there.</summary>
        private int ResolveLayer(int x, int z, int layer)
        {
            bool hasOverlay = _grid.TryOverlayAt(x, z, out _);
            if (layer == 1 && hasOverlay) return 1;
            if (_grid.KindAt(x, z) == TileCellKind.Sea && hasOverlay) return 1;
            return 0;
        }

        private bool StepAdjacent(int fx, int fz, int fromLayer, int tx, int tz, ref int layer)
        {
            int dx = tx - fx;
            int dz = tz - fz;

            // A wide stair: run cells side by side at the same position in their climbs share
            // a continuously equal edge — walking across the stair is allowed.
            if (fromLayer == 0 &&
                _grid.KindAt(fx, fz) == TileCellKind.Ramp &&
                _grid.KindAt(tx, tz) == TileCellKind.Ramp &&
                _grid.FacingAt(fx, fz) == _grid.FacingAt(tx, tz) &&
                _grid.LevelAt(fx, fz) == _grid.LevelAt(tx, tz) &&
                _grid.RampIndexAt(fx, fz) == _grid.RampIndexAt(tx, tz) &&
                IsSide(_grid.FacingAt(fx, fz), dx, dz))
            {
                layer = 0;
                return true;
            }

            int fromEdge = EdgeHeight(fx, fz, fromLayer, dx, dz, out bool fromExists);
            if (!fromExists || fromEdge < 0) return false;

            // Base surface first, then the overlay — where both would match they are the same
            // height, and the base is the terrain.
            for (int candidate = 0; candidate < 2; candidate++)
            {
                int toEdge = EdgeHeight(tx, tz, candidate, -dx, -dz, out bool exists);
                if (!exists || toEdge < 0 || toEdge != fromEdge) continue;

                layer = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The height a cell's surface presents at the edge in direction (dx, dz), in 1/RampRun
        /// level units; −1 is "no entry" — <see cref="OverworldTileGrid.EdgeLevelAt"/> with the
        /// seam's role calls layered on. Sea has no base surface at all, and a ramp's SIDE is a
        /// refusal: you cannot board a stair from the side. The face planner's wrapper makes
        /// the opposite calls (water presents its level, sides present their base); the
        /// arithmetic between them lives once, on the grid. An overlay deck is flat and
        /// presents its level on every edge.
        /// </summary>
        private int EdgeHeight(int x, int z, int surfaceLayer, int dx, int dz, out bool exists)
        {
            // Blocked kills both surfaces: without this, the diagonal corner-threading path
            // could route THROUGH a prop's corner cell that HasSurface already refuses.
            if (_grid.UnwalkableAt(x, z))
            {
                exists = false;
                return -1;
            }

            if (surfaceLayer == 1)
            {
                exists = _grid.TryOverlayAt(x, z, out int overlay);
                return exists ? overlay * Run : -1;
            }

            exists = _grid.KindAt(x, z) != TileCellKind.Sea;
            if (!exists) return -1;

            int edge = _grid.EdgeLevelAt(x, z, dx, dz, out bool rampSide);
            return rampSide ? -1 : edge;
        }

        private static bool IsSide(RampFacing facing, int dx, int dz) =>
            facing == RampFacing.PlusZ || facing == RampFacing.MinusZ ? dz == 0 : dx == 0;
    }
}
