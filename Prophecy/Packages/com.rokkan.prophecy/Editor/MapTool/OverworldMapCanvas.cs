using System.Collections.Generic;
using Rokkan.Prophecy.Overworld;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.MapTool
{
    /// <summary>
    /// The 2D top-down paint surface: one texture pixel per cell, blown up point-filtered, so
    /// 6,912 cells cost one draw — never a widget per cell. The canvas renders the COMPILED
    /// grid (the designer paints against truth, not against the shape list) with the
    /// in-progress stroke overlaid from the mirror dict; nothing touches the asset until the
    /// stroke ends, when the window commits it as one undo step.
    /// </summary>
    internal sealed class OverworldMapCanvas
    {
        public enum Brush
        {
            PaintGround,
            PaintSea,
            RoadAdd,
            RoadRemove,
            ClearOverride,
        }

        public Brush ActiveBrush = Brush.PaintGround;
        public int PaintLevel;
        public int BrushSize = 1;

        private readonly Dictionary<Vector2Int, AuthoredCellOverride> _mirror =
            new Dictionary<Vector2Int, AuthoredCellOverride>();

        private Texture2D _texture;
        private bool _textureDirty = true;

        private OverworldTileGrid _grid;
        private bool _gridDirty = true;

        private bool _stroke;
        private int _strokeMinX, _strokeMinZ, _strokeMaxX, _strokeMaxZ;

        /// <summary>The compiler's authoring audits from the last canvas compile — the window
        /// shows these instead of letting them spam the console on every stroke.</summary>
        public readonly List<string> LastNotes = new List<string>();

        private float _zoom = 8f;
        private Vector2 _pan;

        /// <summary>The committed overrides, exactly as the asset should store them —
        /// deterministic order for stable diffs.</summary>
        public AuthoredCellOverride[] ToArray()
        {
            var list = new List<AuthoredCellOverride>(_mirror.Values);
            list.Sort((a, b) => a.Z != b.Z ? a.Z - b.Z : a.X - b.X);
            return list.ToArray();
        }

        /// <summary>Wholesale resync from the asset — undo, focus, external edits. The mirror
        /// is a cache of the truth, never a second truth.</summary>
        public void SyncFromMap(OverworldMap map)
        {
            _mirror.Clear();
            var overrides = map != null ? map.CellOverrides : null;
            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var o = overrides[i];
                _mirror[new Vector2Int(o.X, o.Z)] = new AuthoredCellOverride
                {
                    X = o.X, Z = o.Z, Terrain = o.Terrain, Level = o.Level, Road = o.Road,
                };
            }

            _gridDirty = true;
            _textureDirty = true;
        }

        public void MarkStale()
        {
            _gridDirty = true;
            _textureDirty = true;
        }

        public void OnGUI(Rect rect, OverworldMap map, OverworldMapToolWindow window,
                          Vector3 worldOrigin)
        {
            if (map == null) return;

            RefreshGrid(map, worldOrigin);
            if (_grid == null) return;
            RefreshTexture();

            GUI.Box(rect, GUIContent.none);
            GUI.BeginClip(rect);
            var local = new Rect(_pan.x, _pan.y, _grid.Width * _zoom, _grid.Height * _zoom);
            GUI.DrawTexture(local, _texture, ScaleMode.StretchToFill, false);
            GUI.EndClip();

            HandleEvents(rect, map, window);
        }

        // ---------------------------------------------------------------- events

        private void HandleEvents(Rect rect, OverworldMap map, OverworldMapToolWindow window)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) && !_stroke) return;

            // Zoom about the cursor; pan with middle drag.
            if (e.type == EventType.ScrollWheel)
            {
                float old = _zoom;
                _zoom = Mathf.Clamp(_zoom * (e.delta.y < 0 ? 1.15f : 1f / 1.15f), 3f, 28f);
                var cursor = e.mousePosition - rect.min;
                _pan = cursor - (cursor - _pan) * (_zoom / old);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 2)
            {
                _pan += e.delta;
                e.Use();
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (e.button == 0 && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                var cell = CellAt(rect, e.mousePosition);
                if (cell.HasValue)
                {
                    if (!_stroke)
                    {
                        _stroke = true;
                        _strokeMinX = _strokeMaxX = cell.Value.x;
                        _strokeMinZ = _strokeMaxZ = cell.Value.y;
                    }

                    ApplyBrush(cell.Value);
                    e.Use();
                }
            }
            else if (_stroke && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                _stroke = false;
                window.CommitCellOverrides(ToArray(), _strokeMinX, _strokeMinZ,
                                           _strokeMaxX, _strokeMaxZ);
                _gridDirty = true;
                _textureDirty = true;
                e.Use();
            }
        }

        private Vector2Int? CellAt(Rect rect, Vector2 mouse)
        {
            var local = mouse - rect.min - _pan;
            int x = Mathf.FloorToInt(local.x / _zoom);
            int guiRow = Mathf.FloorToInt(local.y / _zoom);
            int z = _grid.Height - 1 - guiRow;   // GUI y grows down; north stays up
            if (x < 0 || x >= _grid.Width || z < 0 || z >= _grid.Height) return null;
            return new Vector2Int(x, z);
        }

        private void ApplyBrush(Vector2Int centre)
        {
            int reach = Mathf.Max(0, BrushSize - 1);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    var cell = new Vector2Int(centre.x + dx, centre.y + dz);
                    if (cell.x < 0 || cell.x >= _grid.Width ||
                        cell.y < 0 || cell.y >= _grid.Height) continue;

                    _strokeMinX = Mathf.Min(_strokeMinX, cell.x);
                    _strokeMaxX = Mathf.Max(_strokeMaxX, cell.x);
                    _strokeMinZ = Mathf.Min(_strokeMinZ, cell.y);
                    _strokeMaxZ = Mathf.Max(_strokeMaxZ, cell.y);

                    switch (ActiveBrush)
                    {
                        case Brush.ClearOverride:
                            _mirror.Remove(cell);
                            break;

                        case Brush.PaintGround:
                            Entry(cell).Terrain = TerrainOverride.Ground;
                            Entry(cell).Level = PaintLevel;
                            break;

                        case Brush.PaintSea:
                            Entry(cell).Terrain = TerrainOverride.Sea;
                            Entry(cell).Level = PaintLevel;
                            break;

                        case Brush.RoadAdd:
                            Entry(cell).Road = RoadOverride.Add;
                            break;

                        case Brush.RoadRemove:
                            Entry(cell).Road = RoadOverride.Remove;
                            break;
                    }
                }
            }

            _textureDirty = true;
        }

        private AuthoredCellOverride Entry(Vector2Int cell)
        {
            if (!_mirror.TryGetValue(cell, out var entry))
            {
                entry = new AuthoredCellOverride { X = cell.x, Z = cell.y };
                _mirror[cell] = entry;
            }

            return entry;
        }

        // ---------------------------------------------------------------- rendering

        private void RefreshGrid(OverworldMap map, Vector3 worldOrigin)
        {
            if (!_gridDirty && _grid != null) return;

            // The canvas compiles for itself rather than borrowing the preview's grid: painting
            // must show truth whether or not the 3D preview is running, and a compile of the
            // whole map is milliseconds. The mirror is baked in through a temporary override
            // array so the in-progress stroke reads exactly as it will commit. Audits stay out
            // of the console — they land in LastNotes for the window to show.
            var saved = map.CellOverrides;
            map.CellOverrides = ToArray();
            OverworldTileGridCompiler.LogToConsole = false;
            try
            {
                _grid = OverworldTileGridCompiler.Compile(map, worldOrigin);
            }
            finally
            {
                OverworldTileGridCompiler.LogToConsole = true;
                map.CellOverrides = saved;
            }
            LastNotes.Clear();
            LastNotes.AddRange(OverworldTileGridCompiler.Notes);
            _gridDirty = false;
        }

        private void RefreshTexture()
        {
            if (_texture == null || _texture.width != _grid.Width || _texture.height != _grid.Height)
            {
                Object.DestroyImmediate(_texture);
                _texture = new Texture2D(_grid.Width, _grid.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _textureDirty = true;
            }

            if (!_textureDirty) return;

            var pixels = new Color32[_grid.Width * _grid.Height];
            for (int z = 0; z < _grid.Height; z++)
                for (int x = 0; x < _grid.Width; x++)
                    pixels[z * _grid.Width + x] = CellColour(x, z);

            _texture.SetPixels32(pixels);
            _texture.Apply();
            _textureDirty = false;
        }

        private Color32 CellColour(int x, int z)
        {
            Color colour;
            var kind = _grid.KindAt(x, z);
            int level = _grid.LevelAt(x, z);

            if (kind == TileCellKind.Sea)
                colour = Color.Lerp(new Color(0.16f, 0.32f, 0.62f), new Color(0.45f, 0.7f, 0.95f),
                                    level / 4f);
            else if (kind == TileCellKind.Ramp)
                colour = new Color(0.85f, 0.62f, 0.25f);
            else
                colour = Color.Lerp(new Color(0.2f, 0.42f, 0.22f), new Color(0.75f, 0.9f, 0.55f),
                                    level / 4f);

            if (_grid.TryOverlayAt(x, z, out _))
                colour = Color.Lerp(colour, new Color(0.65f, 0.4f, 0.9f), 0.45f);

            if (_grid.RoadAt(x, z))
                colour = Color.Lerp(colour, new Color(0.78f, 0.66f, 0.45f), 0.6f);

            if (_mirror.ContainsKey(new Vector2Int(x, z)))
                colour = Color.Lerp(colour, new Color(1f, 0.35f, 0.75f), 0.22f);

            return colour;
        }
    }
}
