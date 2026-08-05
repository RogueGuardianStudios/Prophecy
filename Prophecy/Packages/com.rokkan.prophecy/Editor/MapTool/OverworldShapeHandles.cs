using System.Collections.Generic;
using Rokkan.Prophecy.Overworld;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.MapTool
{
    /// <summary>
    /// Scene-view handles for the map's shape vocabulary: drag region and layer rects, ramp
    /// strips, river and road courses where they actually live, over the live preview. Every
    /// mutation is one undo step (Unity groups per drag), dirties the asset, and marks the
    /// shape's old and new cell footprints on the preview so only the touched chunks rebuild.
    ///
    /// <para>The transform convention matches the RASTERIZER, not intuition: the compiler maps
    /// world to shape-local by rotating through <c>-RotationDegrees</c>, so local-to-world here
    /// rotates by <c>+RotationDegrees</c> — the drawn outline must be the compiled footprint or
    /// the tool lies.</para>
    /// </summary>
    internal static class OverworldShapeHandles
    {
        private static readonly Color RegionColour = new Color(0.35f, 0.9f, 0.4f, 0.9f);
        private static readonly Color LayerColour = new Color(0.8f, 0.5f, 1f, 0.9f);
        private static readonly Color RampColour = new Color(1f, 0.8f, 0.25f, 0.9f);
        private static readonly Color RiverColour = new Color(0.3f, 0.6f, 1f, 0.9f);
        private static readonly Color RoadColour = new Color(0.85f, 0.7f, 0.45f, 0.9f);

        public static void Draw(OverworldMap map, OverworldMapPreview preview, Vector3 worldOffset,
                                bool regions = true, bool ramps = true, bool layers = true,
                                bool rivers = true, bool roads = true)
        {
            if (map == null) return;
            var offset = new Vector2(worldOffset.x, worldOffset.z);

            for (int i = 0; regions && map.Regions != null && i < map.Regions.Length; i++)
                DrawRect(map, preview, offset,
                         () => (map.Regions[i].Centre, map.Regions[i].Size,
                                map.Regions[i].RotationDegrees, map.Regions[i].Y),
                         (c, s, r) =>
                         {
                             map.Regions[i].Centre = c;
                             map.Regions[i].Size = s;
                             map.Regions[i].RotationDegrees = r;
                         },
                         RegionColour, map.Regions[i].Name);

            for (int i = 0; layers && map.Layers != null && i < map.Layers.Length; i++)
                DrawRect(map, preview, offset,
                         () => (map.Layers[i].Centre, map.Layers[i].Size,
                                map.Layers[i].RotationDegrees, map.Layers[i].Y),
                         (c, s, r) =>
                         {
                             map.Layers[i].Centre = c;
                             map.Layers[i].Size = s;
                             map.Layers[i].RotationDegrees = r;
                         },
                         LayerColour, map.Layers[i].Name);

            for (int i = 0; ramps && map.Ramps != null && i < map.Ramps.Length; i++)
                DrawRamp(map, preview, offset, map.Ramps[i]);

            for (int i = 0; rivers && map.Rivers != null && i < map.Rivers.Length; i++)
                DrawCourse(map, preview, offset, map.Rivers[i].Name,
                           () => map.Rivers[i].Points, p => map.Rivers[i].Points = p,
                           map.Rivers[i].HalfWidth, RiverColour);

            for (int i = 0; roads && map.Roads != null && i < map.Roads.Length; i++)
                DrawCourse(map, preview, offset, map.Roads[i].Name,
                           () => map.Roads[i].Points, p => map.Roads[i].Points = p,
                           map.Roads[i].HalfWidth, RoadColour);
        }

        // ---------------------------------------------------------------- rects

        private static void DrawRect(OverworldMap map, OverworldMapPreview preview, Vector2 offset,
                                     System.Func<(Vector2 centre, Vector2 size, float rot, float y)> read,
                                     System.Action<Vector2, Vector2, float> write,
                                     Color colour, string label)
        {
            var (centre, size, rot, y) = read();
            float height = Mathf.Round(y / OverworldTileGrid.Step) * OverworldTileGrid.Step + 0.1f;
            var world = centre + offset;
            var pos = new Vector3(world.x, height, world.y);
            var half = size * 0.5f;

            Handles.color = colour;
            var corners = new Vector3[5];
            corners[0] = RectPoint(world, new Vector2(-half.x, -half.y), rot, height);
            corners[1] = RectPoint(world, new Vector2(-half.x, half.y), rot, height);
            corners[2] = RectPoint(world, new Vector2(half.x, half.y), rot, height);
            corners[3] = RectPoint(world, new Vector2(half.x, -half.y), rot, height);
            corners[4] = corners[0];
            Handles.DrawAAPolyLine(3f, corners);
            Handles.Label(pos + Vector3.up * 0.4f, label);

            // Centre move.
            EditorGUI.BeginChangeCheck();
            var moved = Handles.FreeMoveHandle(pos, HandleUtility.GetHandleSize(pos) * 0.12f,
                                               Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Commit(map, preview, offset, centre, size, rot,
                       new Vector2(moved.x, moved.z) - offset, size, rot, write);
                return;
            }

            // Edge sliders for size, along the shape's own axes.
            var axisX = RectDir(new Vector2(1f, 0f), rot);
            var axisZ = RectDir(new Vector2(0f, 1f), rot);
            TrySizeSlider(map, preview, offset, centre, size, rot, write, pos, axisX, half.x, true);
            TrySizeSlider(map, preview, offset, centre, size, rot, write, pos, -axisX, half.x, true);
            TrySizeSlider(map, preview, offset, centre, size, rot, write, pos, axisZ, half.y, false);
            TrySizeSlider(map, preview, offset, centre, size, rot, write, pos, -axisZ, half.y, false);

            // Rotation disc around the centre.
            EditorGUI.BeginChangeCheck();
            var q = Handles.Disc(Quaternion.Euler(0f, -rot, 0f), pos, Vector3.up,
                                 Mathf.Min(half.x, half.y) * 0.7f, false, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newRot = -q.eulerAngles.y;
                if (newRot < -180f) newRot += 360f;
                Commit(map, preview, offset, centre, size, rot, centre, size, newRot, write);
            }
        }

        private static void TrySizeSlider(OverworldMap map, OverworldMapPreview preview,
                                          Vector2 offset, Vector2 centre, Vector2 size, float rot,
                                          System.Action<Vector2, Vector2, float> write,
                                          Vector3 pos, Vector3 dir, float half, bool isX)
        {
            var handle = pos + dir * half;
            EditorGUI.BeginChangeCheck();
            var moved = Handles.Slider(handle, dir, HandleUtility.GetHandleSize(handle) * 0.08f,
                                       Handles.CubeHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck()) return;

            float newHalf = Mathf.Max(0.5f, Vector3.Dot(moved - pos, dir));
            var newSize = isX ? new Vector2(newHalf * 2f, size.y) : new Vector2(size.x, newHalf * 2f);
            Commit(map, preview, offset, centre, size, rot, centre, newSize, rot, write);
        }

        private static void Commit(OverworldMap map, OverworldMapPreview preview, Vector2 offset,
                                   Vector2 oldCentre, Vector2 oldSize, float oldRot,
                                   Vector2 newCentre, Vector2 newSize, float newRot,
                                   System.Action<Vector2, Vector2, float> write)
        {
            Undo.RecordObject(map, "Edit Overworld Shape");
            write(newCentre, newSize, newRot);
            EditorUtility.SetDirty(map);
            MarkRect(preview, offset, oldCentre, oldSize);
            MarkRect(preview, offset, newCentre, newSize);
        }

        /// <summary>Shape-local to world, the rasterizer's inverse: rotate by +RotationDegrees.</summary>
        private static Vector3 RectPoint(Vector2 worldCentre, Vector2 local, float rotDeg, float y)
        {
            var r = RotateXZ(local, rotDeg);
            return new Vector3(worldCentre.x + r.x, y, worldCentre.y + r.y);
        }

        private static Vector3 RectDir(Vector2 local, float rotDeg)
        {
            var r = RotateXZ(local, rotDeg);
            return new Vector3(r.x, 0f, r.y).normalized;
        }

        private static Vector2 RotateXZ(Vector2 v, float rotDeg)
        {
            float rad = rotDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        // ---------------------------------------------------------------- ramps

        private static void DrawRamp(OverworldMap map, OverworldMapPreview preview, Vector2 offset,
                                     AuthoredRamp ramp)
        {
            var a = ramp.Start + offset;
            var b = ramp.End + offset;
            var posA = new Vector3(a.x, ramp.StartY + 0.1f, a.y);
            var posB = new Vector3(b.x, ramp.EndY + 0.1f, b.y);

            Handles.color = RampColour;
            Handles.DrawAAPolyLine(3f, posA, posB);
            var dir = (b - a).normalized;
            var perp = new Vector3(-dir.y, 0f, dir.x);
            Handles.DrawAAPolyLine(2f, posA - perp * ramp.HalfWidth, posB - perp * ramp.HalfWidth);
            Handles.DrawAAPolyLine(2f, posA + perp * ramp.HalfWidth, posB + perp * ramp.HalfWidth);
            Handles.Label((posA + posB) * 0.5f + Vector3.up * 0.4f, ramp.Name);

            EditorGUI.BeginChangeCheck();
            var movedA = Handles.FreeMoveHandle(posA, HandleUtility.GetHandleSize(posA) * 0.1f,
                                                Vector3.zero, Handles.SphereHandleCap);
            var movedB = Handles.FreeMoveHandle(posB, HandleUtility.GetHandleSize(posB) * 0.1f,
                                                Vector3.zero, Handles.SphereHandleCap);
            var mid = (posA + posB) * 0.5f + perp * ramp.HalfWidth;
            var movedW = Handles.Slider(mid, perp, HandleUtility.GetHandleSize(mid) * 0.07f,
                                        Handles.CubeHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(map, "Edit Overworld Ramp");
            MarkStrip(preview, offset, ramp.Start, ramp.End, ramp.HalfWidth);
            ramp.Start = new Vector2(movedA.x, movedA.z) - offset;
            ramp.End = new Vector2(movedB.x, movedB.z) - offset;
            ramp.HalfWidth = Mathf.Max(0.5f, Vector3.Dot(movedW - (posA + posB) * 0.5f, perp));
            EditorUtility.SetDirty(map);
            MarkStrip(preview, offset, ramp.Start, ramp.End, ramp.HalfWidth);
        }

        // ---------------------------------------------------------------- polyline courses

        private static void DrawCourse(OverworldMap map, OverworldMapPreview preview, Vector2 offset,
                                       string label, System.Func<Vector2[]> read,
                                       System.Action<Vector2[]> write, float halfWidth, Color colour)
        {
            var points = read();
            if (points == null || points.Length == 0) return;

            var preview3 = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var w = points[i] + offset;
                preview3[i] = new Vector3(w.x, CourseHeight(preview, w), w.y);
            }

            Handles.color = colour;
            if (preview3.Length > 1) Handles.DrawAAPolyLine(4f, preview3);
            Handles.Label(preview3[0] + Vector3.up * 0.5f, label);

            var e = Event.current;

            // Shift+click a point deletes it (two points minimum stay).
            // Otherwise every point is a free-move handle.
            for (int i = 0; i < points.Length; i++)
            {
                if (e.shift && points.Length > 2)
                {
                    if (Handles.Button(preview3[i], Quaternion.identity,
                                       HandleUtility.GetHandleSize(preview3[i]) * 0.12f,
                                       HandleUtility.GetHandleSize(preview3[i]) * 0.15f,
                                       Handles.SphereHandleCap))
                    {
                        Undo.RecordObject(map, "Delete Course Point");
                        var list = new List<Vector2>(points);
                        MarkStrip(preview, offset, points[Mathf.Max(0, i - 1)],
                                  points[Mathf.Min(points.Length - 1, i + 1)], halfWidth);
                        list.RemoveAt(i);
                        write(list.ToArray());
                        EditorUtility.SetDirty(map);
                        return;
                    }
                    continue;
                }

                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(preview3[i],
                                                   HandleUtility.GetHandleSize(preview3[i]) * 0.1f,
                                                   Vector3.zero, Handles.SphereHandleCap);
                if (!EditorGUI.EndChangeCheck()) continue;

                Undo.RecordObject(map, "Move Course Point");
                var old = points[i];
                points[i] = new Vector2(moved.x, moved.z) - offset;
                write(points);
                EditorUtility.SetDirty(map);
                MarkStrip(preview, offset, old, points[i], halfWidth);
            }

            // Ctrl+click inserts a point on the nearest segment.
            if (e.control && e.type == EventType.MouseDown && e.button == 0 && points.Length > 1)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Mathf.Abs(ray.direction.y) > 1e-4f)
                {
                    float t = -ray.origin.y / ray.direction.y;
                    var hit = ray.origin + ray.direction * t;
                    var p = new Vector2(hit.x, hit.z) - offset;

                    int best = -1;
                    float bestDist = 2f;   // metres — a deliberate click, not a stray one
                    for (int i = 0; i + 1 < points.Length; i++)
                    {
                        float d = DistanceToSegment(p, points[i], points[i + 1]);
                        if (d < bestDist) { bestDist = d; best = i; }
                    }

                    if (best >= 0)
                    {
                        Undo.RecordObject(map, "Insert Course Point");
                        var list = new List<Vector2>(points);
                        list.Insert(best + 1, p);
                        write(list.ToArray());
                        EditorUtility.SetDirty(map);
                        MarkStrip(preview, offset, points[best], points[best + 1], halfWidth);
                        e.Use();
                    }
                }
            }
        }

        private static float CourseHeight(OverworldMapPreview preview, Vector2 world)
        {
            var grid = preview?.Grid;
            if (grid != null && grid.TryCellAt(world, out int x, out int z))
                return grid.SurfaceHeight(x, z, world) + 0.3f;
            return 0.3f;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        // ---------------------------------------------------------------- dirty marking

        private static void MarkRect(OverworldMapPreview preview, Vector2 offset,
                                     Vector2 centre, Vector2 size)
        {
            var grid = preview?.Grid;
            if (grid == null) { preview?.MarkAll(); return; }

            // Conservative: the rotated rect's axis-aligned bounds. Rotation extents are at
            // most the half-diagonal, so use it outright — a slightly fat dirty set beats a
            // hole in the preview.
            float reach = size.magnitude * 0.5f;
            var world = centre + offset;
            int minX = Mathf.FloorToInt(world.x - reach - grid.Origin.x);
            int maxX = Mathf.CeilToInt(world.x + reach - grid.Origin.x);
            int minZ = Mathf.FloorToInt(world.y - reach - grid.Origin.y);
            int maxZ = Mathf.CeilToInt(world.y + reach - grid.Origin.y);
            preview.MarkCells(minX, minZ, maxX, maxZ);
        }

        private static void MarkStrip(OverworldMapPreview preview, Vector2 offset,
                                      Vector2 a, Vector2 b, float halfWidth)
        {
            var grid = preview?.Grid;
            if (grid == null) { preview?.MarkAll(); return; }

            var wa = a + offset;
            var wb = b + offset;
            int minX = Mathf.FloorToInt(Mathf.Min(wa.x, wb.x) - halfWidth - grid.Origin.x);
            int maxX = Mathf.CeilToInt(Mathf.Max(wa.x, wb.x) + halfWidth - grid.Origin.x);
            int minZ = Mathf.FloorToInt(Mathf.Min(wa.y, wb.y) - halfWidth - grid.Origin.y);
            int maxZ = Mathf.CeilToInt(Mathf.Max(wa.y, wb.y) + halfWidth - grid.Origin.y);
            preview.MarkCells(minX, minZ, maxX, maxZ);
        }
    }
}
