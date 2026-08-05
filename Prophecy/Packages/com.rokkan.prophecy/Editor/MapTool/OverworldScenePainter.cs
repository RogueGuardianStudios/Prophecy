using Rokkan.Prophecy.Overworld;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.MapTool
{
    /// <summary>
    /// Painting directly in the Scene view — the primary surface: the brush footprint
    /// highlights the cells under the mouse ON the live preview, click-drag strokes them, and
    /// the world rebuilds behind the stroke. Drives the same engine the minimap does, so the
    /// two surfaces cannot disagree. Shift temporarily flips Raise to Lower — the sculpt idiom.
    /// </summary>
    internal static class OverworldScenePainter
    {
        public static void Draw(SceneView view, OverworldMapToolWindow window,
                                OverworldMapCanvas canvas, OverworldMap map, Vector3 worldOrigin)
        {
            canvas.RefreshGrid(map, worldOrigin);
            var grid = canvas.Grid;
            if (grid == null) return;

            // Own the mouse: without this the scene's default tools eat the clicks.
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var e = Event.current;

            canvas.BrushOverride = e.shift && canvas.ActiveBrush == PaintBrush.Raise
                ? PaintBrush.Lower
                : (PaintBrush?)null;

            bool hasCell = TryCell(grid, e.mousePosition, out var cell);

            if (hasCell)
            {
                DrawFootprint(grid, cell, canvas.BrushSize,
                              canvas.BrushOverride ?? canvas.ActiveBrush);
                view.Repaint();
            }

            if (e.button == 0 && !e.alt && hasCell &&
                (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                if (!canvas.StrokeActive) canvas.BeginStroke(cell);
                canvas.StrokeCell(cell);
                e.Use();
            }
            else if (canvas.StrokeActive &&
                     (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                canvas.EndStroke(window);
                e.Use();
            }
        }

        private static void DrawFootprint(OverworldTileGrid grid, Vector2Int centre, int size,
                                          PaintBrush brush)
        {
            int reach = Mathf.Max(0, size - 1);
            var fill = brush == PaintBrush.Lower || brush == PaintBrush.Water
                ? new Color(0.25f, 0.5f, 1f, 0.25f)
                : new Color(0.4f, 1f, 0.5f, 0.25f);
            var outline = new Color(fill.r, fill.g, fill.b, 0.9f);

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = centre.x + dx;
                    int z = centre.y + dz;
                    if (!grid.InBounds(x, z)) continue;

                    var corner = grid.CornerPoint(x, z);
                    var mid = grid.CellCentre(x, z);
                    float y = grid.SurfaceHeight(x, z, mid) + 0.05f;
                    var quad = new[]
                    {
                        new Vector3(corner.x, y, corner.y),
                        new Vector3(corner.x, y, corner.y + OverworldTileGrid.CellSize),
                        new Vector3(corner.x + OverworldTileGrid.CellSize, y,
                                    corner.y + OverworldTileGrid.CellSize),
                        new Vector3(corner.x + OverworldTileGrid.CellSize, y, corner.y),
                    };
                    Handles.DrawSolidRectangleWithOutline(quad, fill, outline);
                }
            }

            var c = grid.CellCentre(centre.x, centre.y);
            float labelY = grid.SurfaceHeight(centre.x, centre.y, c) + 0.6f;
            Handles.Label(new Vector3(c.x, labelY, c.y),
                          $"{brush}  ·  {grid.KindAt(centre.x, centre.y)} L{grid.LevelAt(centre.x, centre.y)}");
        }

        /// <summary>March the mouse ray against the analytic base surface — tiles carry no
        /// colliders, and the compiled grid IS the truth anyway.</summary>
        private static bool TryCell(OverworldTileGrid grid, Vector2 mouse, out Vector2Int cell)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            for (float t = 0f; t < 300f; t += 0.25f)
            {
                var p = ray.origin + ray.direction * t;
                var plane = new Vector2(p.x, p.z);
                if (!grid.TryCellAt(plane, out int x, out int z)) continue;
                if (p.y > grid.SurfaceHeight(x, z, plane)) continue;

                cell = new Vector2Int(x, z);
                return true;
            }

            cell = default;
            return false;
        }
    }
}
