using System.IO;
using Rokkan.Prophecy.Presentation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Builds <c>GrayBox_UI.unity</c> — the HUD and menu scene <see cref="Rokkan.Prophecy.World.SceneDirector"/>
    /// loads additively at start-up and never unloads. It lives beside Bootstrap in the
    /// persistent layer, not among the world scenes: it carries no <c>SceneDescriptor</c>, so
    /// the director's world-scene discovery and transitions never touch it.
    ///
    /// <para><b>Why a scene of two components.</b> The whole widget tree is built at runtime
    /// by <see cref="HudController"/> and <see cref="MenuRoot"/> (see <c>UiBuild</c> for the
    /// reasoning), so the only serialized content is the two hosts. That keeps this builder
    /// honest — regenerating cannot drift a hand-tweaked hierarchy, because there is no
    /// hierarchy to tweak — and keeps the UI out of the hand-built Bootstrap scene, which
    /// stays exactly as it is.</para>
    /// </summary>
    public static class UiSceneBuilder
    {
        public const string ScenePath = "Assets/_Prophecy/Scenes/GrayBox_UI.unity";

        public static string SceneName => Path.GetFileNameWithoutExtension(ScenePath);

        [MenuItem("Prophecy/Build/Generate GrayBox_UI", priority = 44)]
        public static void Generate()
        {
            if (HasUnsavedChanges()) return;

            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = false;
            for (int i = 0; setup != null && i < setup.Length; i++)
                if (!string.IsNullOrEmpty(setup[i].path)) canRestore = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("UIRoot");
            root.AddComponent<HudController>();
            root.AddComponent<MenuRoot>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.Refresh();
            BuildSettings.EnsureInBuildSettings(ScenePath);

            if (canRestore) EditorSceneManager.RestoreSceneManagerSetup(setup);

            Debug.Log($"[Prophecy] Generated {ScenePath} — the HUD and menu layer.");
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
