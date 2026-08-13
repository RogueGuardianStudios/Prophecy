using System.Collections.Generic;
using Rokkan.Prophecy.Overworld;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.MapTool
{
    /// <summary>
    /// The paint ENGINE and the minimap in one: the mirror dict, the stroke lifecycle, and the
    /// brush application live here and are driven by BOTH surfaces — the Scene-view painter
    /// (the primary, paint-where-you-look surface) and this class's own 2D canvas (the
    /// overview: one texture pixel per cell, point-filtered, double-click to frame the Scene
    /// view there). Nothing touches the asset until a stroke ends; then the window commits it
    /// as one undo step.
    ///
    /// <para>Relative brushes (Raise/Lower) read the grid COMPILED AT STROKE START and apply
    /// once per cell per stroke — dragging across a terrace raises it one level, not one level
    /// per repaint.</para>
    /// </summary>
    internal sealed class OverworldMapCanvas
    {
        public PaintBrush ActiveBrush = PaintBrush.Raise;
        public int PaintLevel;
        public int BrushSize = 1;

        /// <summary>The palette index the Biome brush pins.</summary>
        public int PaintBiome;

        /// <summary>Tint cells by their governing province — the gameplay view.</summary>
        public bool ShowProvinces;

        /// <summary>A momentary substitute for the active brush — the Scene painter sets this
        /// while Shift is held so Raise flips to Lower without touching the picked brush.</summary>
        public PaintBrush? BrushOverride;

        private readonly Dictionary<Vector2Int, AuthoredCellOverride> _mirror =
            new Dictionary<Vector2Int, AuthoredCellOverride>();

        private Texture2D _texture;
        private bool _textureDirty = true;

        private OverworldTileGrid _grid;
        private bool _gridDirty = true;

        private bool _stroke;
        private readonly HashSet<Vector2Int> _strokeVisited = new HashSet<Vector2Int>();
        private int _strokeMinX, _strokeMinZ, _strokeMaxX, _strokeMaxZ;

        private float _zoom = 8f;
        private Vector2 _pan;

        /// <summary>The compiler's authoring audits from the last canvas compile — the window
        /// shows these instead of letting them spam the console on every stroke.</summary>
        public readonly List<string> LastNotes = new List<string>();

        /// <summary>The grid the tool paints against — compiled here with the in-progress
        /// stroke baked in. The Scene painter reads cell state and dimensions from this.</summary>
        public OverworldTileGrid Grid => _grid;

        // ---------------------------------------------------------------- stroke lifecycle

        public void BeginStroke(Vector2Int cell)
        {
            _stroke = true;
            _strokeVisited.Clear();
            _strokeMinX = _strokeMaxX = cell.x;
            _strokeMinZ = _strokeMaxZ = cell.y;
        }

        public bool StrokeActive => _stroke;

        /// <summary>Apply the active brush at <paramref name="centre"/> with the current brush
        /// size. Safe to call every drag event — each cell takes the brush once per stroke.</summary>
        public void StrokeCell(Vector2Int centre)
        {
            if (!_stroke || _grid == null) return;

            int reach = Mathf.Max(0, BrushSize - 1);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    var cell = new Vector2Int(centre.x + dx, centre.y + dz);
                    if (cell.x < 0 || cell.x >= _grid.Width ||
                        cell.y < 0 || cell.y >= _grid.Height) continue;
                    if (!_strokeVisited.Add(cell)) continue;

                    _strokeMinX = Mathf.Min(_strokeMinX, cell.x);
                    _strokeMaxX = Mathf.Max(_strokeMaxX, cell.x);
                    _strokeMinZ = Mathf.Min(_strokeMinZ, cell.y);
                    _strokeMaxZ = Mathf.Max(_strokeMaxZ, cell.y);

                    ApplyBrush(cell);
                }
            }

            _textureDirty = true;
        }

        /// <summary>Commit the stroke through the window: one undo step, one partial rebuild.</summary>
        public void EndStroke(OverworldMapToolWindow window)
        {
            if (!_stroke) return;
            _stroke = false;
            window.CommitCellOverrides(ToArray(), _strokeMinX, _strokeMinZ,
                                       _strokeMaxX, _strokeMaxZ);
            _gridDirty = true;
            _textureDirty = true;
        }

        private void ApplyBrush(Vector2Int cell)
        {
            var brush = BrushOverride ?? ActiveBrush;
            if (brush == PaintBrush.Clear)
            {
                _mirror.Remove(cell);
                return;
            }

            if (brush == PaintBrush.Biome)
            {
                Entry(cell).Biome = PaintBiome;
                return;
            }

            if (brush == PaintBrush.GreebleAdd || brush == PaintBrush.GreebleRemove)
            {
                Entry(cell).Greeble = brush == PaintBrush.GreebleAdd
                    ? GreebleOverride.Add
                    : GreebleOverride.Remove;
                return;
            }

            if (brush == PaintBrush.UnwalkableAdd || brush == PaintBrush.UnwalkableRemove)
            {
                Entry(cell).Unwalkable = brush == PaintBrush.UnwalkableAdd
                    ? UnwalkableOverride.Add
                    : UnwalkableOverride.Remove;
                return;
            }

            // Relative brushes read the stroke-start compile, PLUS this stroke's own edit if
            // one already landed on the cell — visited-set guarantees it hasn't.
            var kind = _grid.KindAt(cell.x, cell.y);
            int level = _grid.LevelAt(cell.x, cell.y);

            if (!OverworldPaintBrushes.Apply(brush, kind, level, PaintLevel,
                                             out var terrain, out int terrainLevel,
                                             out var road)) return;

            var entry = Entry(cell);
            if (terrain != TerrainOverride.None)
            {
                entry.Terrain = terrain;
                entry.Level = terrainLevel;
            }
            if (road != RoadOverride.None)
                entry.Road = road;
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

        // ---------------------------------------------------------------- asset sync

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
                    Biome = o.Biome, Greeble = o.Greeble, Unwalkable = o.Unwalkable,
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

        /// <summary>Compile the paint grid if stale. The Scene painter calls this before
        /// reading <see cref="Grid"/>; the canvas calls it before drawing.</summary>
        public void RefreshGrid(OverworldMap map, Vector3 worldOrigin)
        {
            if (!_gridDirty && _grid != null) return;

            // The canvas compiles for itself rather than borrowing the preview's grid: painting
            // must show truth whether or not the 3D preview is running, and a compile of the
            // whole map is milliseconds. The mirror is baked in through a temporary override
            // array so the in-progress stroke reads exactly as it will commit. Audits stay out
            // of the console — they land in LastNotes for the window to show.
            var saved = map.CellOverrides;
            map.CellOverrides = ToArray();
            try
            {
                var compiled = OverworldTileGridCompiler.CompileWithReport(
                    map, worldOrigin, logAuditsToConsole: false);
                _grid = compiled.Grid;
                LastNotes.Clear();
                LastNotes.AddRange(compiled.Notes);
            }
            finally
            {
                map.CellOverrides = saved;
            }
            _gridDirty = false;
        }

        // ---------------------------------------------------------------- the minimap

        public void OnGUI(Rect rect, OverworldMap map, OverworldMapToolWindow window,
                          Vector3 worldOrigin, bool paintEnabled)
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

            HandleEvents(rect, window, paintEnabled);

            // Hover readout, drawn under the map: which cell, what's there.
            var hover = CellAt(rect, Event.current.mousePosition);
            if (hover.HasValue)
            {
                var c = hover.Value;
                int hoverBiome = _grid.DominantBiomeAt(c.x, c.y);
                var hoverProvince = _grid.ProvinceAt(c.x, c.y);
                var info = $"cell ({c.x}, {c.y})  {_grid.KindAt(c.x, c.y)} L{_grid.LevelAt(c.x, c.y)}" +
                           (_grid.RoadAt(c.x, c.y) ? "  road" : "") +
                           (_grid.GreebleAt(c.x, c.y) ? "  greeble"
                               : _grid.UnwalkableAt(c.x, c.y) ? "  unwalkable" : "") +
                           (_grid.TryOverlayAt(c.x, c.y, out int ov) ? $"  overlay L{ov}" : "") +
                           (hoverBiome >= 0 ? $"  biome {hoverBiome}" : "") +
                           (hoverProvince != null ? $"  [{hoverProvince.DisplayName}]" : "") +
                           (_mirror.ContainsKey(c) ? "  painted" : "");
                GUI.Label(new Rect(rect.x + 4f, rect.yMax - 20f, rect.width - 8f, 18f), info,
                          EditorStyles.miniBoldLabel);
            }
        }

        private void HandleEvents(Rect rect, OverworldMapToolWindow window, bool paintEnabled)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) && !_stroke) return;

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

            // Double-click: frame the Scene view on that cell — the minimap is a map first.
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
            {
                var cell = CellAt(rect, e.mousePosition);
                if (cell.HasValue && SceneView.lastActiveSceneView != null)
                {
                    var world = _grid.CellCentre(cell.Value.x, cell.Value.y);
                    float y = _grid.SurfaceHeight(cell.Value.x, cell.Value.y, world);
                    SceneView.lastActiveSceneView.Frame(
                        new Bounds(new Vector3(world.x, y, world.y), Vector3.one * 12f), false);
                    e.Use();
                    return;
                }
            }

            if (paintEnabled && e.button == 0 &&
                (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                var cell = CellAt(rect, e.mousePosition);
                if (cell.HasValue)
                {
                    if (!_stroke) BeginStroke(cell.Value);
                    StrokeCell(cell.Value);
                    e.Use();
                }
            }
            else if (_stroke && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                EndStroke(window);
                e.Use();
            }
        }

        private Vector2Int? CellAt(Rect rect, Vector2 mouse)
        {
            if (_grid == null || !rect.Contains(mouse)) return null;
            var local = mouse - rect.min - _pan;
            int x = Mathf.FloorToInt(local.x / _zoom);
            int guiRow = Mathf.FloorToInt(local.y / _zoom);
            int z = _grid.Height - 1 - guiRow;   // GUI y grows down; north stays up
            if (x < 0 || x >= _grid.Width || z < 0 || z >= _grid.Height) return null;
            return new Vector2Int(x, z);
        }

        // ---------------------------------------------------------------- rendering

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

        // The minimap's ink, public like BiomeTint so the window's legend swatches ARE the
        // paint — a key that re-types its colours drifts the first time one is retuned.
        public static readonly Color GroundLowColour = new Color(0.2f, 0.42f, 0.22f);
        public static readonly Color GroundHighColour = new Color(0.75f, 0.9f, 0.55f);
        public static readonly Color WaterColour = new Color(0.16f, 0.32f, 0.62f);
        public static readonly Color StairsColour = new Color(0.85f, 0.62f, 0.25f);
        public static readonly Color CoverColour = new Color(0.65f, 0.4f, 0.9f);
        public static readonly Color RoadColour = new Color(0.78f, 0.66f, 0.45f);
        public static readonly Color PaintedColour = new Color(1f, 0.35f, 0.75f);

        private Color32 CellColour(int x, int z)
        {
            Color colour;
            var kind = _grid.KindAt(x, z);
            int level = _grid.LevelAt(x, z);

            if (kind == TileCellKind.Sea)
                colour = Color.Lerp(WaterColour, new Color(0.45f, 0.7f, 0.95f), level / 4f);
            else if (kind == TileCellKind.Ramp)
                colour = StairsColour;
            else
                colour = Color.Lerp(GroundLowColour, GroundHighColour, level / 4f);

            if (_grid.TryOverlayAt(x, z, out _))
                colour = Color.Lerp(colour, CoverColour, 0.45f);

            if (_grid.RoadAt(x, z))
                colour = Color.Lerp(colour, RoadColour, 0.6f);

            int biome = _grid.DominantBiomeAt(x, z);
            if (biome >= 0)
                colour = Color.Lerp(colour, BiomeTint(biome), 0.3f);

            if (_grid.GreebleAt(x, z))
                colour = Color.Lerp(colour, new Color(0.08f, 0.25f, 0.1f), 0.55f);
            else if (_grid.UnwalkableAt(x, z))
                colour = Color.Lerp(colour, new Color(0.3f, 0.12f, 0.12f), 0.55f);

            if (ShowProvinces)
            {
                var province = _grid.ProvinceAt(x, z);
                if (province != null)
                    colour = Color.Lerp(colour, province.MapTint, 0.45f);
            }

            if (_mirror.ContainsKey(new Vector2Int(x, z)))
                colour = Color.Lerp(colour, PaintedColour, 0.22f);

            return colour;
        }

        /// <summary>A stable, distinct tint per biome index — until the palette asset brings
        /// authored colours, hue-spread does the job.</summary>
        public static Color BiomeTint(int index) =>
            Color.HSVToRGB((index * 0.137f) % 1f, 0.65f, 0.95f);
    }
}
