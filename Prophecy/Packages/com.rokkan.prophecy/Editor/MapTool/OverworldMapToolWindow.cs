using Rokkan.Prophecy.Editor.Build;
using Rokkan.Prophecy.Overworld;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.MapTool
{
    /// <summary>
    /// The overworld hand-crafting tool: owns the live edit-mode preview and the Scene-view
    /// shape handles. The real overworld will be majority hand-authored (Matt, 2026-08-05),
    /// and this window is where that happens — the map asset stays the single source of truth,
    /// this is only a hand on it.
    ///
    /// <para>Slice 1: shape handles over a live chunked preview. The 2D paint canvas (cell
    /// overrides) and prop placement arrive in later slices; this shell already owns the
    /// lifecycle they will ride on.</para>
    /// </summary>
    public sealed class OverworldMapToolWindow : EditorWindow
    {
        /// <summary>What clicking in the Scene view does. One mode at a time — the cure for
        /// "a lot going on": the cursor means one thing, and the window shows only that
        /// thing's controls.</summary>
        private enum ToolMode
        {
            Select,
            Paint,
            Props,
        }

        [SerializeField] private OverworldMap _map;
        [SerializeField] private OverworldTileSet _tiles;
        [SerializeField] private bool _stairsForRamps = true;
        [SerializeField] private bool _previewEnabled = true;
        [SerializeField] private bool _handlesEnabled = true;
        [SerializeField] private ToolMode _mode = ToolMode.Paint;
        [SerializeField] private int _selectedKind = -1;
        [SerializeField] private int _selectedIndex = -1;

        [SerializeField] private bool _showRegions = true;
        [SerializeField] private bool _showRamps = true;
        [SerializeField] private bool _showLayers = true;
        [SerializeField] private bool _showRivers = true;
        [SerializeField] private bool _showRoads = true;
        [SerializeField] private bool _showBiomeAreas = true;
        [SerializeField] private bool _showGreebles = true;
        [SerializeField] private bool _showProvinceView;

        [SerializeField] private OverworldPropPalette _palette;
        [SerializeField] private OverworldBiomePalette _biomePalette;
        [SerializeField] private int _paletteIndex;
        [SerializeField] private bool _snapToCell = true;
        [SerializeField] private float _placeYaw;
        [SerializeField] private bool _placeBlocks = true;
        [SerializeField] private Vector2Int _placeBlockSize = Vector2Int.one;
        [SerializeField] private int _placeSurfaceLayer;

        private OverworldMapPreview _preview;
        private OverworldMapCanvas _canvas;
        private Vector3 _worldOrigin;

        /// <summary>Whether the hidden preview world currently exists.</summary>
        public bool PreviewActive => _preview != null && _preview.Active;

        /// <summary>Milliseconds the preview's last rebuild took.</summary>
        public double PreviewLastBuildMs => _preview?.LastBuildMs ?? 0;

        /// <summary>Attach and flush the preview immediately — the programmatic twin of the
        /// Live Preview toggle, for editor scripts and smoke tests.</summary>
        public void RebuildPreviewNow()
        {
            if (_map == null || _tiles == null ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;

            _previewEnabled = true;
            _preview.Attach(_map, _tiles, _worldOrigin, _stairsForRamps, _biomePalette);
            _preview.Flush();
        }

        /// <summary>Mark a cell rectangle dirty and rebuild its chunks now. The paint canvas
        /// commits through this seam; it also lets scripts exercise partial rebuilds.</summary>
        public void RebuildPreviewCells(int minX, int minZ, int maxX, int maxZ)
        {
            if (!PreviewActive) return;
            _preview.MarkCells(minX, minZ, maxX, maxZ);
            _preview.Flush();
        }

        /// <summary>One paint stroke becomes one undo step and one partial preview rebuild.
        /// RegisterCompleteObjectUndo rather than RecordObject — the override ARRAY resizes,
        /// and delta recording is unreliable across resizes.</summary>
        internal void CommitCellOverrides(AuthoredCellOverride[] overrides,
                                          int minX, int minZ, int maxX, int maxZ)
        {
            if (_map == null) return;

            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(_map, "Paint Overworld Cells");
            _map.CellOverrides = overrides;
            EditorUtility.SetDirty(_map);

            if (PreviewActive) RebuildPreviewCells(minX, minZ, maxX, maxZ);
        }

        [MenuItem("Prophecy/Overworld Map Tool", priority = 10)]
        private static void Open()
        {
            var window = GetWindow<OverworldMapToolWindow>("Overworld Map");
            window.minSize = new Vector2(320f, 200f);
        }

        private void OnEnable()
        {
            OverworldMapPreview.SweepOrphans();
            _preview = new OverworldMapPreview();
            _canvas = new OverworldMapCanvas();

            AdoptSceneHost();
            if (_map == null)
                _map = AssetDatabase.LoadAssetAtPath<OverworldMap>(
                    "Assets/_Prophecy/Data/OverworldMap.asset");
            if (_tiles == null)
                _tiles = AssetDatabase.LoadAssetAtPath<OverworldTileSet>(OverworldTileBuilder.TileSetPath);
            if (_palette == null)
                _palette = AssetDatabase.LoadAssetAtPath<OverworldPropPalette>(
                    "Assets/_Prophecy/Data/OverworldPropPalette.asset");
            if (_biomePalette == null)
                _biomePalette = AssetDatabase.LoadAssetAtPath<OverworldBiomePalette>(
                    "Assets/_Prophecy/Data/OverworldBiomePalette.asset");

            _canvas.SyncFromMap(_map);
            _canvas.ShowProvinces = _showProvinceView;

            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // The preview IS the tool — without it the scene view shows handles floating over
            // an empty scene while painting "does nothing". On by default, attached on open.
            if (_previewEnabled && _map != null && _tiles != null &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
                _preview.Attach(_map, _tiles, _worldOrigin, _stairsForRamps, _biomePalette);
        }

        private void OnFocus() => _canvas?.SyncFromMap(_map);

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _preview.Teardown();
        }

        /// <summary>If the open scene carries an OverworldGridHost, author against ITS map,
        /// tile set and position — what the tool previews must be what that scene builds.</summary>
        private void AdoptSceneHost()
        {
            var host = FindAnyObjectByType<OverworldGridHost>(FindObjectsInactive.Include);
            if (host == null) return;

            var so = new SerializedObject(host);
            _map = so.FindProperty("_map").objectReferenceValue as OverworldMap;
            _tiles = so.FindProperty("_tiles").objectReferenceValue as OverworldTileSet;
            var biomes = so.FindProperty("_biomes");
            if (biomes != null && biomes.objectReferenceValue != null)
                _biomePalette = biomes.objectReferenceValue as OverworldBiomePalette;
            _stairsForRamps = so.FindProperty("_stairsForRamps").boolValue;
            _worldOrigin = host.transform.position;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _map = (OverworldMap)EditorGUILayout.ObjectField("Map", _map, typeof(OverworldMap), false);
            _tiles = (OverworldTileSet)EditorGUILayout.ObjectField("Tile Set", _tiles,
                                                                   typeof(OverworldTileSet), false);
            bool assetsChanged = EditorGUI.EndChangeCheck();
            if (assetsChanged)
            {
                _canvas.SyncFromMap(_map);
                if (_previewEnabled && _map != null && _tiles != null)
                    _preview.Attach(_map, _tiles, _worldOrigin, _stairsForRamps, _biomePalette);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play mode owns the world — the tool resumes when it ends.",
                                        MessageType.Info);
                return;
            }

            if (_map == null || _tiles == null) return;

            // ------------------------------------------------------------ the mode bar
            EditorGUILayout.Space();
            var mode = (ToolMode)GUILayout.Toolbar((int)_mode,
                new[] { "Select / Edit", "Paint", "Props" }, GUILayout.Height(28f));
            if (mode != _mode)
            {
                _mode = mode;
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();

            switch (_mode)
            {
                case ToolMode.Paint: PaintControls(); break;
                case ToolMode.Props: PropControls(); break;
                default: SelectControls(); break;
            }

            // ------------------------------------------------------------ the minimap
            EditorGUILayout.Space();
            bool provinceView = GUILayout.Toggle(_showProvinceView,
                "Province View — tint the map by whose rules govern each cell", "Button");
            if (provinceView != _showProvinceView)
            {
                _showProvinceView = provinceView;
                _canvas.ShowProvinces = provinceView;
                _canvas.MarkStale();
            }
            var canvasRect = GUILayoutUtility.GetRect(position.width, 100f, position.width,
                                                      float.MaxValue, GUILayout.ExpandHeight(true));
            _canvas.OnGUI(canvasRect, _map, this, _worldOrigin, _mode == ToolMode.Paint);

            EditorGUILayout.LabelField(
                _mode == ToolMode.Paint
                    ? "Minimap: LMB paint · MMB pan · scroll zoom · double-click frames the Scene view"
                    : "Minimap: MMB pan · scroll zoom · double-click frames the Scene view",
                EditorStyles.miniLabel);
            Legend();

            for (int i = 0; i < _canvas.LastNotes.Count; i++)
                EditorGUILayout.HelpBox(_canvas.LastNotes[i], MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Preview")) RebuildPreviewNow();
                if (_preview.Active)
                    GUILayout.Label($"last rebuild {_preview.LastBuildMs:0} ms",
                                    EditorStyles.miniLabel, GUILayout.Width(120f));
            }
        }

        private void PaintControls()
        {
            EditorGUILayout.LabelField("Paint — in the Scene view or on the minimap below",
                                       EditorStyles.boldLabel);

            _canvas.ActiveBrush = (PaintBrush)GUILayout.SelectionGrid((int)_canvas.ActiveBrush,
                new[] { "Raise", "Lower", "Set Level", "Water", "Road +", "Road −",
                        "Clear", "Biome", "Greeble +", "Greeble −",
                        "Unwalkable +", "Unwalkable −" }, 6);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_canvas.ActiveBrush != PaintBrush.SetLevel))
                    _canvas.PaintLevel = EditorGUILayout.IntSlider("Set To Level",
                                                                   _canvas.PaintLevel, 0, 4);
                _canvas.BrushSize = EditorGUILayout.IntSlider("Brush Size", _canvas.BrushSize, 1, 6);
            }

            if (_canvas.ActiveBrush == PaintBrush.Biome)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _canvas.PaintBiome = EditorGUILayout.IntSlider("Biome Index",
                                                                   _canvas.PaintBiome, 0, 7);
                    var swatch = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f));
                    EditorGUI.DrawRect(swatch, OverworldMapCanvas.BiomeTint(_canvas.PaintBiome));
                }
            }

            EditorGUILayout.HelpBox(
                "Raise lifts a cell one terrace; Shift while painting flips it to Lower. " +
                "Lowering the plain floods it; Water pools at the cell's own level. " +
                "Clear removes the paint and the shapes' answer returns. One stroke = one undo.",
                MessageType.None);
        }

        private void SelectControls()
        {
            EditorGUILayout.LabelField("Select / Edit shapes", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _showRegions = GUILayout.Toggle(_showRegions, "Regions", "Button");
                _showRamps = GUILayout.Toggle(_showRamps, "Ramps", "Button");
                _showLayers = GUILayout.Toggle(_showLayers, "Layers", "Button");
                _showRivers = GUILayout.Toggle(_showRivers, "Rivers", "Button");
                _showRoads = GUILayout.Toggle(_showRoads, "Roads", "Button");
                _showBiomeAreas = GUILayout.Toggle(_showBiomeAreas, "Biomes", "Button");
                _showGreebles = GUILayout.Toggle(_showGreebles, "Greebles", "Button");
            }

            EditorGUILayout.HelpBox(
                "Click a shape's dot in the Scene view to select it — only the selected shape " +
                "shows its full handles. Rects: drag centre, edges, rotation disc. Courses: " +
                "drag points, Ctrl+click inserts, Shift+click deletes. Props: drag to move, " +
                "Shift+click deletes.", MessageType.None);

            _stairsForRamps = EditorGUILayout.Toggle("Stairs For Ramps", _stairsForRamps);

            SelectionPanel();
        }

        /// <summary>The selected shape's inspector: rename it, and give a named place its
        /// PROVINCE — the gameplay rules — right here, because the shapes ARE the provinces
        /// and the tool is where they are authored.</summary>
        private void SelectionPanel()
        {
            string kindLabel = null;
            string shapeName = null;
            OverworldProvince province = null;
            bool takesProvince = false;
            CoverStyle cover = CoverStyle.Auto;
            bool takesCover = false;

            switch (_selectedKind)
            {
                case OverworldShapeHandles.KindRegion when InRange(_map.Regions):
                    kindLabel = "Terrain";
                    shapeName = _map.Regions[_selectedIndex].Name;
                    province = _map.Regions[_selectedIndex].Province;
                    takesProvince = true;
                    break;
                case OverworldShapeHandles.KindBiomeArea when InRange(_map.BiomeAreas):
                    kindLabel = "Biome Area";
                    shapeName = _map.BiomeAreas[_selectedIndex].Name;
                    province = _map.BiomeAreas[_selectedIndex].Province;
                    takesProvince = true;
                    break;
                case OverworldShapeHandles.KindGreeble when InRange(_map.Greebles):
                    kindLabel = "Greeble Mass";
                    shapeName = _map.Greebles[_selectedIndex].Name;
                    province = _map.Greebles[_selectedIndex].Province;
                    takesProvince = true;
                    break;
                case OverworldShapeHandles.KindRamp when InRange(_map.Ramps):
                    kindLabel = "Ramp";
                    shapeName = _map.Ramps[_selectedIndex].Name;
                    break;
                case OverworldShapeHandles.KindRiver when InRange(_map.Rivers):
                    kindLabel = "River";
                    shapeName = _map.Rivers[_selectedIndex].Name;
                    break;
                case OverworldShapeHandles.KindRoad when InRange(_map.Roads):
                    kindLabel = "Road";
                    shapeName = _map.Roads[_selectedIndex].Name;
                    break;
                case OverworldShapeHandles.KindLayer when InRange(_map.Layers):
                    kindLabel = "Layer";
                    shapeName = _map.Layers[_selectedIndex].Name;
                    cover = _map.Layers[_selectedIndex].Cover;
                    takesCover = true;
                    break;
            }

            if (kindLabel == null)
            {
                EditorGUILayout.LabelField("Nothing selected.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Selected {kindLabel}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField("Name", shapeName);
            OverworldProvince newProvince = province;
            if (takesProvince)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    newProvince = (OverworldProvince)EditorGUILayout.ObjectField(
                        "Province", province, typeof(OverworldProvince), false);
                    if (GUILayout.Button("New", GUILayout.Width(44f)))
                        newProvince = CreateProvinceAsset(newName);
                }
            }
            CoverStyle newCover = cover;
            if (takesCover)
                newCover = (CoverStyle)EditorGUILayout.EnumPopup(
                    new GUIContent("Cover", "Cave inverts the picture when the player is " +
                                            "underneath; Bridge does not. Auto derives it: " +
                                            "floor below terrain = Cave, deck above = Bridge."),
                    cover);

            if (!EditorGUI.EndChangeCheck() && newProvince == province && newCover == cover)
                return;

            Undo.RecordObject(_map, "Edit Overworld Shape");
            switch (_selectedKind)
            {
                case OverworldShapeHandles.KindRegion:
                    _map.Regions[_selectedIndex].Name = newName;
                    _map.Regions[_selectedIndex].Province = newProvince;
                    break;
                case OverworldShapeHandles.KindBiomeArea:
                    _map.BiomeAreas[_selectedIndex].Name = newName;
                    _map.BiomeAreas[_selectedIndex].Province = newProvince;
                    break;
                case OverworldShapeHandles.KindGreeble:
                    _map.Greebles[_selectedIndex].Name = newName;
                    _map.Greebles[_selectedIndex].Province = newProvince;
                    break;
                case OverworldShapeHandles.KindRamp:
                    _map.Ramps[_selectedIndex].Name = newName; break;
                case OverworldShapeHandles.KindRiver:
                    _map.Rivers[_selectedIndex].Name = newName; break;
                case OverworldShapeHandles.KindRoad:
                    _map.Roads[_selectedIndex].Name = newName; break;
                case OverworldShapeHandles.KindLayer:
                    _map.Layers[_selectedIndex].Name = newName;
                    _map.Layers[_selectedIndex].Cover = newCover;
                    break;
            }
            EditorUtility.SetDirty(_map);
            _canvas.MarkStale();
            if (PreviewActive) _preview.MarkAll();
        }

        private bool InRange<T>(T[] array) =>
            array != null && _selectedIndex >= 0 && _selectedIndex < array.Length;

        /// <summary>A fresh province asset named for its place, with a legible random tint.</summary>
        private static OverworldProvince CreateProvinceAsset(string placeName)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Prophecy/Data/Provinces"))
                AssetDatabase.CreateFolder("Assets/_Prophecy/Data", "Provinces");

            var province = ScriptableObject.CreateInstance<OverworldProvince>();
            province.DisplayName = placeName;
            province.MapTint = Color.HSVToRGB(
                (Mathf.Abs(placeName.GetHashCode()) % 100) / 100f, 0.55f, 0.95f);
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"Assets/_Prophecy/Data/Provinces/Province_{placeName.Replace(" ", "")}.asset");
            AssetDatabase.CreateAsset(province, path);
            AssetDatabase.SaveAssets();
            return province;
        }

        private void PropControls()
        {
            EditorGUILayout.LabelField("Place props — click the ground in the Scene view",
                                       EditorStyles.boldLabel);

            _palette = (OverworldPropPalette)EditorGUILayout.ObjectField(
                "Palette", _palette, typeof(OverworldPropPalette), false);

            if (_palette == null || _palette.Prefabs == null || _palette.Prefabs.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Assign a palette (Create > Prophecy > Overworld Prop Palette) and add " +
                    "prefabs to it — towns, trees, anything that stands on tiles.",
                    MessageType.Info);
                return;
            }

            var names = new string[_palette.Prefabs.Length];
            for (int i = 0; i < names.Length; i++)
                names[i] = _palette.Prefabs[i] != null ? _palette.Prefabs[i].name : "(empty)";
            _paletteIndex = Mathf.Clamp(
                EditorGUILayout.Popup("Prefab", _paletteIndex, names), 0, names.Length - 1);

            using (new EditorGUILayout.HorizontalScope())
            {
                _snapToCell = EditorGUILayout.ToggleLeft("Snap to cell", _snapToCell,
                                                         GUILayout.Width(110f));
                _placeBlocks = EditorGUILayout.ToggleLeft("Unwalkable", _placeBlocks,
                                                          GUILayout.Width(90f));
                _placeBlockSize = EditorGUILayout.Vector2IntField(GUIContent.none, _placeBlockSize);
                _placeSurfaceLayer = EditorGUILayout.IntSlider(_placeSurfaceLayer, 0, 1);
            }

            EditorGUILayout.HelpBox("Click places · R rotates 15° · Shift+click an existing " +
                                    "prop deletes it · Esc returns to Select.", MessageType.None);
        }

        /// <summary>The minimap's colour key — cryptic tints were half of "unintuitive".</summary>
        private static void Legend()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Swatch(new Color(0.2f, 0.42f, 0.22f), "low ground");
                Swatch(new Color(0.75f, 0.9f, 0.55f), "high ground");
                Swatch(new Color(0.16f, 0.32f, 0.62f), "water");
                Swatch(new Color(0.85f, 0.62f, 0.25f), "stairs");
                Swatch(new Color(0.65f, 0.4f, 0.9f), "deck/cave");
                Swatch(new Color(0.78f, 0.66f, 0.45f), "road");
                Swatch(new Color(1f, 0.35f, 0.75f), "painted");
            }
        }

        private static void Swatch(Color colour, string label)
        {
            var rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f));
            rect.y += 3f;
            EditorGUI.DrawRect(rect, colour);
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.Space(6f);
        }

        private void OnSceneGui(SceneView view)
        {
            if (_map == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            switch (_mode)
            {
                case ToolMode.Paint:
                    OverworldScenePainter.Draw(view, this, _canvas, _map, _worldOrigin);
                    break;

                case ToolMode.Props:
                    DrawExistingProps();
                    DrawPlacement(view);
                    break;

                default:
                    OverworldShapeHandles.Draw(_map, _preview, _worldOrigin,
                                               _showRegions, _showRamps, _showLayers,
                                               _showRivers, _showRoads,
                                               _showBiomeAreas, _showGreebles,
                                               _selectedKind, _selectedIndex,
                                               (kind, index) =>
                                               {
                                                   _selectedKind = kind;
                                                   _selectedIndex = index;
                                                   Repaint();
                                               });
                    DrawExistingProps();
                    break;
            }
        }

        // ---------------------------------------------------------------- prop placement

        /// <summary>Existing props: a sphere handle each — drag to move (height re-derives on
        /// rebuild), Shift+click to delete.</summary>
        private void DrawExistingProps()
        {
            var grid = _preview.Grid;
            var offset = new Vector2(_worldOrigin.x, _worldOrigin.z);

            for (int i = 0; _map.Props != null && i < _map.Props.Length; i++)
            {
                var prop = _map.Props[i];
                var world = prop.Position + offset;
                float y = 0.2f;
                if (grid != null && grid.TryCellAt(world, out int cx, out int cz))
                    y = (prop.SurfaceLayer == 1 && grid.TryOverlayAt(cx, cz, out int overlay)
                            ? overlay * OverworldTileGrid.Step
                            : grid.SurfaceHeight(cx, cz, world)) + 0.2f;
                var pos = new Vector3(world.x, y, world.y);

                Handles.color = new Color(1f, 0.5f, 0.2f, 0.9f);
                Handles.Label(pos + Vector3.up * 0.5f, prop.Name);

                if (Event.current.shift)
                {
                    if (Handles.Button(pos, Quaternion.identity,
                                       HandleUtility.GetHandleSize(pos) * 0.14f,
                                       HandleUtility.GetHandleSize(pos) * 0.18f,
                                       Handles.SphereHandleCap))
                    {
                        Undo.RecordObject(_map, "Delete Overworld Prop");
                        var list = new System.Collections.Generic.List<AuthoredProp>(_map.Props);
                        list.RemoveAt(i);
                        _map.Props = list.ToArray();
                        EditorUtility.SetDirty(_map);
                        MarkPropCells(prop);
                        return;
                    }
                    continue;
                }

                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(pos, HandleUtility.GetHandleSize(pos) * 0.12f,
                                                   Vector3.zero, Handles.SphereHandleCap);
                if (!EditorGUI.EndChangeCheck()) continue;

                Undo.RecordObject(_map, "Move Overworld Prop");
                MarkPropCells(prop);
                prop.Position = new Vector2(moved.x, moved.z) - offset;
                if (_snapToCell)
                    prop.Position = new Vector2(Mathf.Floor(prop.Position.x) + 0.5f,
                                                Mathf.Floor(prop.Position.y) + 0.5f);
                EditorUtility.SetDirty(_map);
                MarkPropCells(prop);
            }
        }

        /// <summary>The armed placement: ray-march the analytic ground (tiles have no
        /// colliders), ghost the footprint, click commits, R rotates, Esc disarms.</summary>
        private void DrawPlacement(SceneView view)
        {
            var prefab = _palette != null && _palette.Prefabs != null &&
                         _paletteIndex < _palette.Prefabs.Length
                ? _palette.Prefabs[_paletteIndex]
                : null;
            var grid = _preview.Grid;
            if (prefab == null || grid == null) return;

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var e = Event.current;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _mode = ToolMode.Select;
                Repaint();
                e.Use();
                return;
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.R)
            {
                _placeYaw = Mathf.Repeat(_placeYaw + 15f, 360f);
                e.Use();
            }

            if (!TryGroundHit(grid, e.mousePosition, out var hit)) return;

            var offset = new Vector2(_worldOrigin.x, _worldOrigin.z);
            var plane = new Vector2(hit.x, hit.z) - offset;
            if (_snapToCell)
                plane = new Vector2(Mathf.Floor(plane.x) + 0.5f, Mathf.Floor(plane.y) + 0.5f);
            var ghost = new Vector3(plane.x + offset.x, hit.y, plane.y + offset.y);

            Handles.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Handles.DrawWireCube(ghost + Vector3.up * 0.5f,
                                 new Vector3(_placeBlockSize.x, 1f, _placeBlockSize.y));
            Handles.Label(ghost + Vector3.up * 1.4f, $"{prefab.name}  yaw {_placeYaw:0}°");
            view.Repaint();

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Undo.RecordObject(_map, "Place Overworld Prop");
                var list = _map.Props != null
                    ? new System.Collections.Generic.List<AuthoredProp>(_map.Props)
                    : new System.Collections.Generic.List<AuthoredProp>();
                var prop = new AuthoredProp
                {
                    Name = prefab.name,
                    Prefab = prefab,
                    Position = plane,
                    YawDegrees = _placeYaw,
                    SurfaceLayer = _placeSurfaceLayer,
                    BlockCells = _placeBlocks,
                    BlockSize = Vector2Int.Max(Vector2Int.one, _placeBlockSize),
                };
                list.Add(prop);
                _map.Props = list.ToArray();
                EditorUtility.SetDirty(_map);
                MarkPropCells(prop);
                e.Use();
            }
        }

        /// <summary>March a scene ray against the grid's analytic surface until it dips under.</summary>
        private bool TryGroundHit(OverworldTileGrid grid, Vector2 mouse, out Vector3 hit)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            for (float t = 0f; t < 300f; t += 0.25f)
            {
                var p = ray.origin + ray.direction * t;
                var plane = new Vector2(p.x, p.z);
                if (!grid.TryCellAt(plane, out int x, out int z)) continue;

                float surface = _placeSurfaceLayer == 1 && grid.TryOverlayAt(x, z, out int overlay)
                    ? overlay * OverworldTileGrid.Step
                    : grid.SurfaceHeight(x, z, plane);
                if (p.y > surface) continue;

                hit = new Vector3(p.x, surface, p.z);
                return true;
            }

            hit = default;
            return false;
        }

        /// <summary>A prop edit dirties its footprint's chunks — the blocking recompiles and
        /// the prop root re-spawns with the rebuild.</summary>
        private void MarkPropCells(AuthoredProp prop)
        {
            var grid = _preview.Grid;
            if (!PreviewActive || grid == null) return;

            var world = prop.Position + new Vector2(_worldOrigin.x, _worldOrigin.z);
            if (!grid.TryCellAt(world, out int x, out int z)) return;
            int rx = Mathf.Max(1, prop.BlockSize.x);
            int rz = Mathf.Max(1, prop.BlockSize.y);
            RebuildPreviewCells(x - rx, z - rz, x + rx, z + rz);
        }

        private void OnEditorUpdate() => _preview.Tick();

        /// <summary>Undo's touched cells are unknowable from here — full rebuild fallback.</summary>
        private void OnUndoRedo()
        {
            _canvas?.SyncFromMap(_map);
            if (_previewEnabled && _preview.Active) _preview.MarkAll();
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                _preview.Teardown();
            else if (change == PlayModeStateChange.EnteredEditMode && _previewEnabled &&
                     _map != null && _tiles != null)
                _preview.Attach(_map, _tiles, _worldOrigin, _stairsForRamps, _biomePalette);

            Repaint();
        }
    }
}
