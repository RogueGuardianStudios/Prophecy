using System.IO;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Generates <c>GrayBox_CombatTester</c> — a flat, featureless sparring floor where the
    /// Roc-skinned grunt respawns forever. The arena asks its stations' careful questions;
    /// this scene asks only one, over and over: is the fight good yet?
    ///
    /// <para><b>Flat on purpose.</b> No ledges, no cover, no stations — terrain answers are
    /// the arena's job. Here nothing exists that could excuse the AI: if the grunt reads
    /// poorly on an empty floor, the AI is what needs work.</para>
    ///
    /// <para>Play it by opening the scene and pressing Play — <see cref="BootstrapLoader"/>
    /// pulls Bootstrap in on top, same as every scene. The enemy comes from a
    /// <see cref="CombatTesterRespawner"/> rather than being baked in, so every death is
    /// followed by a fresh opponent a beat later.</para>
    /// </summary>
    public static class GrayBoxCombatTesterBuilder
    {
        public const string ScenePath = "Assets/_Prophecy/Scenes/GrayBox_CombatTester.unity";

        private const string GruntPrefabPath = "Assets/_Prophecy/Prefabs/Enemy_Capsule.prefab";

        private const float FloorWidth = 30f;
        private const float Depth = 3f;
        private const float GroundThickness = 2f;

        [MenuItem("Prophecy/Build/Generate GrayBox_CombatTester", priority = 42)]
        public static void Generate()
        {
            if (HasUnsavedChanges()) return;

            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = false;
            for (int i = 0; setup != null && i < setup.Length; i++)
                if (!string.IsNullOrEmpty(setup[i].path)) canRestore = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildContents();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.Refresh();
            BuildSettings.EnsureInBuildSettings(ScenePath);

            if (canRestore) EditorSceneManager.RestoreSceneManagerSetup(setup);

            Debug.Log($"[Prophecy] Generated {ScenePath} — the sparring floor.");
        }

        private static void BuildContents()
        {
            var geometry = new GameObject("Geometry").transform;
            var markers = new GameObject("Markers").transform;

            CreateLighting();
            CreateFloor(geometry);
            CreateCombatDirector();
            CreateDescriptorAndSpawn(markers);
            CreateRespawner(markers);
        }

        private static void CreateLighting()
        {
            var light = new GameObject("Directional Light");
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var component = light.AddComponent<Light>();
            component.type = LightType.Directional;
            component.intensity = 1.1f;
            component.shadows = LightShadows.Soft;
        }

        private static void CreateFloor(Transform geometry)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(geometry, false);
            floor.transform.localPosition = new Vector3(0f, -GroundThickness * 0.5f, 0f);
            floor.transform.localScale = new Vector3(FloorWidth, GroundThickness, Depth);
            floor.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Ground();
        }

        private static void CreateCombatDirector()
        {
            var director = new GameObject("CombatDirector");
            var component = director.AddComponent<CombatDirector>();
            SetPrivate(component, "_space", MovementSpace.SideScroll);
        }

        private static void CreateDescriptorAndSpawn(Transform markers)
        {
            var spawnObject = new GameObject("Spawn_default");
            spawnObject.transform.SetParent(markers, false);
            spawnObject.transform.position = new Vector3(-6f, 0f, 0f);

            var spawn = spawnObject.AddComponent<SpawnPoint>();
            SetPrivate(spawn, "_id", "default");
            SetPrivate(spawn, "_facing", 1);

            var descriptorObject = new GameObject("SceneDescriptor");
            descriptorObject.transform.SetParent(markers, false);
            var descriptor = descriptorObject.AddComponent<SceneDescriptor>();
            SetPrivate(descriptor, "_space", MovementSpace.SideScroll);
        }

        private static void CreateRespawner(Transform markers)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GruntPrefabPath);
            if (prefab == null)
                Debug.LogWarning($"[Prophecy] No grunt prefab at {GruntPrefabPath} — run " +
                                 "Prophecy > Build > Generate Enemies first, then regenerate. " +
                                 "The respawner was created unarmed.");

            var post = new GameObject("RespawnPost_Grunt");
            post.transform.SetParent(markers, false);
            post.transform.position = new Vector3(6f, 0f, 0f);

            var respawner = post.AddComponent<CombatTesterRespawner>();
            SetPrivate(respawner, "_prefab", prefab);
        }

        // ------------------------------------------------------------------ helpers

        private static bool HasUnsavedChanges()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;

                Debug.LogError($"[Prophecy] '{(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name)}' " +
                               "has unsaved changes. Save or discard them, then run this again — " +
                               "generating replaces the open scene.");
                return true;
            }

            return false;
        }

        private static void SetPrivate(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Prophecy] No serialized field '{field}' on {target.GetType().Name}.");
                return;
            }

            switch (value)
            {
                case UnityEngine.Object o: property.objectReferenceValue = o; break;
                case string s: property.stringValue = s; break;
                case float f: property.floatValue = f; break;
                case int i: property.intValue = i; break;
                case bool b: property.boolValue = b; break;
                case MovementSpace space: property.enumValueIndex = (int)space; break;
                default:
                    Debug.LogError($"[Prophecy] Unhandled type for '{field}'.");
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
