using System.IO;
using Rokkan.Prophecy.Presentation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Builds <c>GrayBox_UI.unity</c> — the HUD and menu scene <see cref="Rokkan.Prophecy.World.SceneDirector"/>
    /// loads additively at start-up and never unloads. It lives beside Bootstrap in the
    /// persistent layer, not among the world scenes: it carries no <c>SceneDescriptor</c>, so
    /// the director's world-scene discovery and transitions never touch it.
    ///
    /// <para><b>UI Toolkit's three pieces, all generated.</b> A runtime panel needs a
    /// <see cref="PanelSettings"/> asset, the settings need a theme for text to render, and a
    /// scene needs a <see cref="UIDocument"/> wired to the settings. All three are restated
    /// by this builder: the theme is the one-line import of Unity's default runtime theme,
    /// the settings scale against the same 1920×1080 design space the old canvas did, and
    /// the document carries no UXML — the whole element tree is built at runtime by
    /// <see cref="HudController"/> and <see cref="MenuRoot"/> (see <c>UiBuild</c> for why),
    /// so regenerating cannot drift a hand-tweaked hierarchy: there is none to tweak.</para>
    /// </summary>
    public static class UiSceneBuilder
    {
        public const string ScenePath = "Assets/_Prophecy/Scenes/GrayBox_UI.unity";
        public const string ThemePath = "Assets/_Prophecy/UI/UnityDefaultRuntimeTheme.tss";
        public const string PanelSettingsPath = "Assets/_Prophecy/UI/PanelSettings.asset";

        public static string SceneName => Path.GetFileNameWithoutExtension(ScenePath);

        [MenuItem("Prophecy/Build/Generate GrayBox_UI", priority = 44)]
        public static void Generate()
        {
            if (HasUnsavedChanges()) return;

            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = false;
            for (int i = 0; setup != null && i < setup.Length; i++)
                if (!string.IsNullOrEmpty(setup[i].path)) canRestore = true;

            EnsurePanelSettings(EnsureTheme());

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Re-loaded AFTER the scene churn, deliberately. The first build of this scene
            // assigned the instance held from before NewScene, and the saved file came out
            // with m_PanelSettings: {fileID: 0} — a stale object reference serializes as
            // silently as null. Loading by path here guarantees the reference being wired is
            // the live imported asset.
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                Debug.LogError($"[Prophecy] {PanelSettingsPath} failed to load — scene not built.");
                return;
            }

            var root = new GameObject("UIRoot");
            var document = root.AddComponent<UIDocument>();
            document.panelSettings = settings;
            root.AddComponent<HudController>();
            root.AddComponent<MenuRoot>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.Refresh();
            BuildSettings.EnsureInBuildSettings(ScenePath);

            // Trust nothing about that assignment: read the saved file back and prove the
            // reference survived. A HUD scene whose document lost its settings renders
            // NOTHING at runtime, with no error anywhere — this is the only place the
            // failure is cheap to catch.
            string saved = File.ReadAllText(ScenePath);
            if (saved.Contains("m_PanelSettings: {fileID: 0}"))
                Debug.LogError($"[Prophecy] {ScenePath} saved WITHOUT its PanelSettings " +
                               "reference — the HUD will not render. Re-run this generator.");

            if (canRestore) EditorSceneManager.RestoreSceneManagerSetup(setup);

            Debug.Log($"[Prophecy] Generated {ScenePath} — the HUD and menu layer, on UI Toolkit.");
        }

        /// <summary>The default runtime theme, as an asset in the project. The file is the
        /// documented one-liner importing Unity's built-in theme — which is what gives every
        /// Label a font without this project shipping one.</summary>
        private static ThemeStyleSheet EnsureTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null) return theme;

            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath).Replace('\\', '/'));
            File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceUpdate);

            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        }

        /// <summary>The panel's rendering contract, restated on every run: the same
        /// 1920×1080 scale-with-screen space the uGUI canvas used, so every layout number
        /// in the controllers kept its meaning across the port.</summary>
        private static PanelSettings EnsurePanelSettings(ThemeStyleSheet theme)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.match = 0.5f;
            settings.sortingOrder = 10;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            return settings;
        }

        private static bool HasUnsavedChanges()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var open = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!open.isDirty) continue;

                Debug.LogError($"[Prophecy] '{(string.IsNullOrEmpty(open.name) ? "Untitled" : open.name)}' " +
                               "has unsaved changes. Save or discard them, then run this again — " +
                               "generating replaces the open scene.");
                return true;
            }

            return false;
        }
    }
}
