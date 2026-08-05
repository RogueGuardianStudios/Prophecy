using Rokkan.Prophecy.Editor.MapTool;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Mock props for exercising the map tool's prop system before real art exists — generated,
    /// like all gray-box content, so retuning a proportion here and regenerating never rots.
    /// Also creates (or refreshes) the default <see cref="OverworldPropPalette"/> so the tool's
    /// Props mode has something on its shelf the moment this runs.
    ///
    /// <para>Props carry NO colliders: tiles have none either, and blocking lives in the
    /// compiled grid where the sim can test it headless. The prefab root's pivot is at the
    /// FEET — the spawner sets the root to the surface height.</para>
    /// </summary>
    internal static class MockPropBuilder
    {
        private const string PropsFolder = "Assets/_Prophecy/Prefabs/Props";
        private const string PalettePath = "Assets/_Prophecy/Data/OverworldPropPalette.asset";

        [MenuItem("Prophecy/Build/Generate Mock Props", priority = 46)]
        public static void Generate()
        {
            EnsureFolder();

            var tree = BuildTree();
            var treePrefab = SavePrefab(tree, "Prop_Tree");

            EnsurePalette(treePrefab);
            AssetDatabase.SaveAssets();

            Debug.Log("[Prophecy] Mock props generated: Prop_Tree, and the default prop " +
                      "palette points at it. Open the Overworld Map Tool, Props mode.");
        }

        /// <summary>A gray-box tree with the ALttP read from overhead: a fat green blob on a
        /// stub of trunk, about a cell wide and a body taller than the player is not.</summary>
        private static GameObject BuildTree()
        {
            var root = new GameObject("Prop_Tree");

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            Object.DestroyImmediate(trunk.GetComponent<Collider>());
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);   // cylinder is 2 m tall at scale 1
            trunk.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.PropWood();

            var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = "Canopy";
            Object.DestroyImmediate(canopy.GetComponent<Collider>());
            canopy.transform.SetParent(root.transform, false);
            canopy.transform.localScale = new Vector3(1.5f, 1.3f, 1.5f);
            canopy.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            canopy.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.PropFoliage();

            return root;
        }

        private static GameObject SavePrefab(GameObject built, string name)
        {
            string path = $"{PropsFolder}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(built, path);
            Object.DestroyImmediate(built);
            return prefab;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Prophecy/Prefabs"))
                AssetDatabase.CreateFolder("Assets/_Prophecy", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PropsFolder))
                AssetDatabase.CreateFolder("Assets/_Prophecy/Prefabs", "Props");
        }

        /// <summary>The default palette the map tool auto-loads: created if missing, and any
        /// prop generated here that it lacks is appended — never reordered, never removed, so a
        /// designer's own additions survive regeneration.</summary>
        private static void EnsurePalette(params GameObject[] prefabs)
        {
            var palette = AssetDatabase.LoadAssetAtPath<OverworldPropPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<OverworldPropPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }

            var list = new System.Collections.Generic.List<GameObject>(
                palette.Prefabs ?? System.Array.Empty<GameObject>());
            foreach (var prefab in prefabs)
                if (prefab != null && !list.Contains(prefab))
                    list.Add(prefab);

            palette.Prefabs = list.ToArray();
            EditorUtility.SetDirty(palette);
        }
    }
}
