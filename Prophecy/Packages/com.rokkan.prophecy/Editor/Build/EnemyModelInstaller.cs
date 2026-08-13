using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Puts the Iron Roc placeholder on the grunt prefab in place of the capsule proxy —
    /// <see cref="ModelInstaller"/> holds the procedure; this holds only the Roc's paths.
    ///
    /// <para>Humanoid matters because it is what lets the player's clips retarget onto the
    /// Roc's rig — the grunt walks and swings with the shared <c>BodyAnimationSet</c> until
    /// it earns clips of its own (a Meshy walk clip sits beside the model, unused for now).</para>
    /// </summary>
    public static class EnemyModelInstaller
    {
        private const string ModelRootName = "RocModel";

        /// <summary>Where the rigged Roc lives. Update when Meshy produces a new one. This
        /// folder is deliberately untracked (same as the hero's) — placeholder generations
        /// are local, and the prefab reference survives on Matt's machine, which is the only
        /// machine.</summary>
        private const string ModelPath =
            "Assets/MeshyImports/Iron Roc Warrior_20260803_162724/Meshy_AI_Iron_Roc_Warrior_biped_Character_output.fbx";

        /// <summary>
        /// The generators' entry point: install if the model exists, say nothing if it does not.
        /// <see cref="EnemyBuilder.Generate"/> rebuilds the grunt prefab from scratch, which is
        /// how the Roc silently fell off it once (the attack-director session regenerated
        /// enemies and committed the capsule back). MeshyImports is untracked, so on a machine
        /// without the model this must be a quiet no-op, not an error.
        /// </summary>
        public static void InstallIfModelPresent()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return;
            Install();
        }

        [MenuItem("Prophecy/Build/Install Roc Model On Grunt", priority = 32)]
        public static void Install() =>
            ModelInstaller.Install(EnemyBuilder.GruntPrefabPath, ModelPath, ModelRootName, "Roc");
    }
}
