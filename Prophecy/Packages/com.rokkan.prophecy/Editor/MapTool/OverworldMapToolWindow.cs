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
        [SerializeField] private OverworldMap _map;
        [SerializeField] private OverworldTileSet _tiles;
        [SerializeField] private bool _stairsForRamps = true;
        [SerializeField] private bool _previewEnabled;
        [SerializeField] private bool _handlesEnabled = true;

        private OverworldMapPreview _preview;
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
            _preview.Attach(_map, _tiles, _worldOrigin, _stairsForRamps);
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

            AdoptSceneHost();
            if (_map == null)
                _map = AssetDatabase.LoadAssetAtPath<OverworldMap>(
                    "Assets/_Prophecy/Data/OverworldMap.asset");
            if (_tiles == null)
                _tiles = AssetDatabase.LoadAssetAtPath<OverworldTileSet>(OverworldTileBuilder.TileSetPath);

            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

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
            _stairsForRamps = so.FindProperty("_stairsForRamps").boolValue;
            _worldOrigin = host.transform.position;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _map = (OverworldMap)EditorGUILayout.ObjectField("Map", _map, typeof(OverworldMap), false);
            _tiles = (OverworldTileSet)EditorGUILayout.ObjectField("Tile Set", _tiles,
                                                                   typeof(OverworldTileSet), false);
            _stairsForRamps = EditorGUILayout.Toggle("Stairs For Ramps", _stairsForRamps);
            bool assetsChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_map == null || _tiles == null ||
                                               EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                _previewEnabled = EditorGUILayout.Toggle("Live Preview", _previewEnabled);
                bool previewToggled = EditorGUI.EndChangeCheck();

                if (previewToggled || (assetsChanged && _previewEnabled))
                {
                    if (_previewEnabled)
                        _preview.Attach(_map, _tiles, _worldOrigin, _stairsForRamps);
                    else
                        _preview.Teardown();
                }

                _handlesEnabled = EditorGUILayout.Toggle("Shape Handles", _handlesEnabled);

                if (GUILayout.Button("Rebuild Preview") && _previewEnabled)
                    _preview.MarkAll();
            }

            EditorGUILayout.Space();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Play mode owns the world — the tool resumes when it ends.",
                                        MessageType.Info);
            else if (_preview.Active)
                EditorGUILayout.LabelField(
                    $"Preview live — last rebuild {_preview.LastBuildMs:0} ms.",
                    EditorStyles.miniLabel);

            EditorGUILayout.HelpBox(
                "Scene view: drag centres, edges and rotation discs on regions and layers; " +
                "drag ramp ends and widths; drag river/road points, Ctrl+click a segment to " +
                "insert a point, Shift+click a point to delete it.", MessageType.None);
        }

        private void OnSceneGui(SceneView view)
        {
            if (!_handlesEnabled || _map == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            OverworldShapeHandles.Draw(_map, _preview, _worldOrigin);
        }

        private void OnEditorUpdate() => _preview.Tick();

        /// <summary>Undo's touched cells are unknowable from here — full rebuild fallback.</summary>
        private void OnUndoRedo()
        {
            if (_previewEnabled && _preview.Active) _preview.MarkAll();
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                _preview.Teardown();
            else if (change == PlayModeStateChange.EnteredEditMode && _previewEnabled &&
                     _map != null && _tiles != null)
                _preview.Attach(_map, _tiles, _worldOrigin, _stairsForRamps);

            Repaint();
        }
    }
}
