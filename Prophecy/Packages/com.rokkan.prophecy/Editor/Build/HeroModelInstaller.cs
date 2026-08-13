using UnityEditor;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Puts the gray-box hero mesh on the player prefab in place of the capsule proxy —
    /// <see cref="ModelInstaller"/> holds the procedure; this holds only the hero's paths.
    ///
    /// <para><b>A command rather than hand-assembly, because this is not a one-off.</b> The
    /// hero has already been re-exported once — the first Meshy export came back unrigged —
    /// and any future re-export means doing all of this again.</para>
    /// </summary>
    public static class HeroModelInstaller
    {
        private const string PrefabPath = "Assets/_Prophecy/Prefabs/Player.prefab";
        private const string ModelRootName = "HeroModel";

        /// <summary>Where the rigged hero lives. Update when Meshy produces a new one.</summary>
        private const string ModelPath =
            "Assets/MeshyImports/T-Pose Figure_20260731_110125/Meshy_AI_T_Pose_Figure_biped_Character_output.fbx";

        [MenuItem("Prophecy/Build/Install Hero Model On Player", priority = 31)]
        public static void Install() =>
            ModelInstaller.Install(PrefabPath, ModelPath, ModelRootName, "Hero");
    }
}
