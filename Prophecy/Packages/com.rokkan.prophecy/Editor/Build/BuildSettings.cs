using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Keeps generated scenes in the build settings scene list.
    ///
    /// <para>A scene that is not in this list cannot be loaded by name at runtime — and the failure
    /// is a runtime error in a coroutine, long after the moment anyone would connect it to having
    /// generated a scene. Since the generators create the scenes, they should also register them;
    /// leaving it as a manual step means it gets missed exactly once and then costs twenty minutes.</para>
    ///
    /// <para>Bootstrap is forced to index 0 because a build starts at the first enabled scene, and
    /// nothing else in this project is a valid place to start.</para>
    /// </summary>
    public static class BuildSettings
    {
        public const string BootstrapScenePath = "Assets/_Prophecy/Scenes/Bootstrap.unity";

        /// <summary>Add <paramref name="scenePath"/> if absent, enabled. Returns true if the list changed.</summary>
        public static bool EnsureInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != scenePath) continue;

                if (scenes[i].enabled) return false;

                scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                return true;
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[Prophecy] Added to build settings: {scenePath}");
            return true;
        }

        /// <summary>Move Bootstrap to index 0, adding it if missing. A build must start there.</summary>
        public static void EnsureBootstrapFirst()
        {
            EnsureInBuildSettings(BootstrapScenePath);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == BootstrapScenePath);

            if (index <= 0) return;

            var bootstrap = scenes[index];
            scenes.RemoveAt(index);
            scenes.Insert(0, bootstrap);

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Prophecy] Moved Bootstrap to build index 0.");
        }
    }
}
