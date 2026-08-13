using Rokkan.Prophecy.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Pushes the camera rig's code defaults onto the rig in Bootstrap.
    ///
    /// <para><b>Because a changed default does not reach an existing scene.</b> Unity only applies
    /// a field's C# initialiser when the field is absent from the serialised YAML — so renaming a
    /// field silently adopts the new default, while retuning an existing one silently does not. The
    /// two behaving oppositely is exactly the trap: the inspector shows the old number, the source
    /// shows the new one, and both are telling the truth.</para>
    ///
    /// <para>Run after retuning a framing default in <c>LaneCameraRig</c>. It reports what moved,
    /// so a run that changes nothing says so rather than looking like it worked.</para>
    /// </summary>
    public static class CameraFramingSync
    {
        private const string ScenePath = "Assets/_Prophecy/Scenes/Bootstrap.unity";

        [MenuItem("Prophecy/Build/Sync Camera Framing To Code Defaults", priority = 32)]
        public static void Sync()
        {
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = false;

            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                opened = true;
            }

            var rig = Object.FindAnyObjectByType<LaneCameraRig>(FindObjectsInactive.Include);

            if (rig == null)
            {
                Debug.LogError($"[Prophecy] No LaneCameraRig in {ScenePath}.");
                return;
            }

            // A fresh instance carries the code defaults; the scene's carries whatever was
            // serialised. Comparing the two is the only way to see the divergence.
            var reference = new GameObject("~framing-reference") { hideFlags = HideFlags.HideAndDontSave };

            try
            {
                var defaults = reference.AddComponent<LaneCameraRig>();

                var from = new SerializedObject(rig);
                var to = new SerializedObject(defaults);

                string[] fields =
                {
                    "_characterViewportShare",
                    "_feetViewportY",
                    "_fieldOfView",
                    "_verticalDeadZoneJumps",
                    "_horizontalDeadZone",
                    "_dampingHorizontal",
                    "_dampingVertical",
                };

                int changed = 0;

                foreach (var name in fields)
                {
                    var current = from.FindProperty(name);
                    var wanted = to.FindProperty(name);
                    if (current == null || wanted == null) continue;
                    if (Mathf.Approximately(current.floatValue, wanted.floatValue)) continue;

                    Debug.Log($"[Prophecy] {name}: {current.floatValue} -> {wanted.floatValue}");
                    current.floatValue = wanted.floatValue;
                    changed++;
                }

                if (changed == 0)
                {
                    Debug.Log("[Prophecy] Camera framing already matches the code defaults.");
                    return;
                }

                from.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rig);
                EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
                EditorSceneManager.SaveScene(rig.gameObject.scene);

                Debug.Log($"[Prophecy] Synced {changed} framing value(s) and saved {ScenePath}.");
            }
            finally
            {
                Object.DestroyImmediate(reference);
                if (opened) { /* leave the scene open; the caller was working in it */ }
            }
        }
    }
}
