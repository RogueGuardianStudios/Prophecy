using System;
using System.IO;
using Rokkan.Prophecy.Core;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Creates the project's authored data assets if they are missing, idempotently.
    ///
    /// <para><b>Why this exists rather than "someone right-clicks Create".</b> An asset conjured by
    /// hand is a step that has to be remembered, and a fresh clone of this repo would come up with
    /// a null <c>MovementTuning</c> reference and no obvious reason why. Running this makes the
    /// project whole from a clean checkout, and running it twice does nothing — so it is safe to
    /// call from a menu, from CI, or on a hunch.</para>
    ///
    /// <para><b>Two entry points, one body.</b> <see cref="CreateDataAssets"/> is the menu item,
    /// for when an Editor is already open. <see cref="CreateDataAssetsFromCommandLine"/> is the
    /// <c>-executeMethod</c> target, for when it is not:</para>
    /// <code>
    /// Unity.exe -batchmode -nographics -projectPath &lt;Prophecy&gt; -logFile - \
    ///           -executeMethod Rokkan.Prophecy.Editor.Build.ProphecyAssetBootstrap.CreateDataAssetsFromCommandLine
    /// </code>
    /// <para>That command needs the Editor <b>closed</b> — Unity refuses to open a project another
    /// instance holds, the same restriction that pushes the test suite through
    /// <c>Prophecy &gt; Tests &gt; Run EditMode Tests</c> during normal development.</para>
    ///
    /// <para>Note this is <i>data</i> bootstrap, not the gray box geometry generator. That one is
    /// a different thing on purpose: level dimensions are <i>derived from</i> MovementTuning and
    /// must be regenerated whenever a number changes, whereas these assets are created once and
    /// then authored by hand.</para>
    /// </summary>
    public static class ProphecyAssetBootstrap
    {
        public const string DataFolder = "Assets/_Prophecy/Data";
        public const string MovementTuningPath = DataFolder + "/MovementTuning.asset";

        [MenuItem("Prophecy/Build/Create Missing Data Assets", priority = 20)]
        public static void CreateDataAssets()
        {
            int created = 0;

            if (EnsureAsset<MovementTuning>(MovementTuningPath)) created++;

            if (created > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[Prophecy] Created {created} data asset(s).");
            }
            else
            {
                Debug.Log("[Prophecy] Data assets already present — nothing to do.");
            }
        }

        /// <summary>
        /// <c>-executeMethod</c> target. Exits with a non-zero code on failure so a build script
        /// can actually tell — batchmode otherwise reports success no matter what was logged.
        /// </summary>
        public static void CreateDataAssetsFromCommandLine()
        {
            try
            {
                CreateDataAssets();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Prophecy] Data asset bootstrap failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        private static bool EnsureAsset<T>(string path) where T : ScriptableObject
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) != null) return false;

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return true;
        }

        /// <summary>Creates a folder and any missing parents. <c>AssetDatabase.CreateFolder</c>
        /// only makes one level at a time and fails silently on a missing parent.</summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);

            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
