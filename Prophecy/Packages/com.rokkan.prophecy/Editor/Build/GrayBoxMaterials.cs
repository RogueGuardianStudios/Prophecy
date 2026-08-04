using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// The few materials the gray boxes share, created idempotently.
    ///
    /// <para>Gray-box geometry is deliberately colourless, which makes the exceptions the
    /// vocabulary: a portal must not look like scenery, and it must look like the same thing in
    /// every scene it appears in — the player is being taught "this colour moves you", and one
    /// asset used everywhere is what keeps the lesson consistent.</para>
    /// </summary>
    internal static class GrayBoxMaterials
    {
        private const string Folder = "Assets/_Prophecy/Data/Materials";

        /// <summary>The portal colour, everywhere a portal appears.</summary>
        public static Material Portal() =>
            Ensure("GrayBox_Portal.mat", new Color(0.2f, 0.75f, 1f));

        /// <summary>
        /// The overworld's ground. The tile prefabs ship pointing at Unity's built-in
        /// Default-Diffuse, which URP renders as eye-searing magenta — so the grid host repaints
        /// every placed tile with this. One material for all of them until per-region palettes
        /// (the darkening) take over.
        /// </summary>
        public static Material Ground() =>
            Ensure("GrayBox_Ground.mat", new Color(0.52f, 0.58f, 0.5f));

        private static Material Ensure(string fileName, Color colour)
        {
            string path = $"{Folder}/{fileName}";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                    AssetDatabase.CreateFolder("Assets/_Prophecy/Data", "Materials");

                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            // Set on every run, not just on creation, so retuning the colour here reaches scenes
            // on their next regeneration — the same idempotence contract as every generator.
            material.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(material);

            return material;
        }
    }
}
