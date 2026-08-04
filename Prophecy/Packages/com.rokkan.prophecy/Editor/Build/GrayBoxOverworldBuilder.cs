using System.IO;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Generates <c>GrayBox_Overworld</c> — the first top-down scene, and deliberately almost
    /// nothing: a plain, a portal back, and the camera that knows how to shoot it.
    ///
    /// <para><b>What this scene is for.</b> The overworld proper is a design problem (regions,
    /// darkening, encounters) that cannot be worked on until the <i>mode</i> works: arriving from
    /// a side-scroll scene, moving on the XZ plane under a fixed three-quarter camera, and leaving
    /// again. This is the smallest world in which that whole loop is real. Everything on the plain
    /// earns its place by exercising part of the loop; nothing here is level design.</para>
    ///
    /// <para>Known, recorded gap: top-down has no collision (<c>CharacterSim.Integrate</c> skips
    /// the sweep), so the plain's edge is a suggestion. The kill plane is off — there is nothing
    /// to fall off — so walking into the void is safe, just featureless.</para>
    /// </summary>
    public static class GrayBoxOverworldBuilder
    {
        public const string ScenePath = "Assets/_Prophecy/Scenes/GrayBox_Overworld.unity";

        /// <summary>Scene name portals target. Derived so it cannot drift from the asset.</summary>
        public static string SceneName => Path.GetFileNameWithoutExtension(ScenePath);

        // Arrival spawns, flanking the portal cube. Leaving the traversal course by different
        // ends arrives at different sides of it — the beginnings of the level occupying a place
        // on the map rather than being a menu entry.
        public const string WestSpawnId = "west";
        public const string EastSpawnId = "east";

        // The plain. Sized in metres like everything else; roomy enough that the camera's frame
        // fits well inside it from the centre, small enough that the portal is never out of sight.
        private const float PlainWidth = 64f;    // X
        private const float PlainDepth = 44f;    // Z

        [MenuItem("Prophecy/Build/Generate GrayBox_Overworld", priority = 41)]
        public static void Generate()
        {
            var tuning = LoadTuning();
            if (tuning == null) return;

            if (HasUnsavedChanges()) return;

            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = false;
            for (int i = 0; setup != null && i < setup.Length; i++)
                if (!string.IsNullOrEmpty(setup[i].path)) canRestore = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildContents(tuning);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.Refresh();
            BuildSettings.EnsureInBuildSettings(ScenePath);

            if (canRestore) EditorSceneManager.RestoreSceneManagerSetup(setup);

            Debug.Log($"[Prophecy] Generated {ScenePath}");
        }

        /// <summary><c>-executeMethod</c> target.</summary>
        public static void GenerateFromCommandLine()
        {
            try
            {
                Generate();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Prophecy] Overworld generation failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ construction

        private static void BuildContents(MovementTuning tuning)
        {
            var geometry = new GameObject("Geometry").transform;
            var markers = new GameObject("Markers").transform;

            CreateLighting();
            CreateStalbergGround();
            CreateReturnPortal(geometry);
            CreateDescriptorAndSpawns(markers);
            CreateCamera(tuning);
            CreateCombatDirector();
            CreateEncounters(markers);
        }

        /// <summary>
        /// The ground is a Stålberg grid built at load from a hand-authored map, replacing the
        /// flat plain-and-rim. The map asset is created here only if missing — its whole point is
        /// to be hand-edited afterwards, so a regenerate must never flatten someone's authoring.
        /// The starter layout is an island: one big landmass with limbs, sized to keep the
        /// existing furniture (spawns, cube, spawner ring) comfortably on dry land.
        /// </summary>
        private static void CreateStalbergGround()
        {
            const string mapPath = "Assets/_Prophecy/Data/OverworldMap.asset";

            var map = AssetDatabase.LoadAssetAtPath<Rokkan.Prophecy.Overworld.OverworldMap>(mapPath);

            if (map == null)
            {
                map = ScriptableObject.CreateInstance<Rokkan.Prophecy.Overworld.OverworldMap>();
                map.Seed = 7;
                map.Spacing = 3f;
                map.Jitter = 0.35f;
                map.BoundsSize = new Vector2(PlainWidth + 32f, PlainDepth + 28f);
                map.Regions = new[]
                {
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "Heartland", Centre = Vector2.zero, Size = new Vector2(PlainWidth, PlainDepth) },
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "West Reach", Centre = new Vector2(-PlainWidth * 0.55f, 8f), Size = new Vector2(24f, 18f), RotationDegrees = 15f },
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "East Cape", Centre = new Vector2(PlainWidth * 0.55f, -6f), Size = new Vector2(20f, 16f), RotationDegrees = -20f },
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "North Spur", Centre = new Vector2(10f, PlainDepth * 0.55f), Size = new Vector2(18f, 14f) },
                };
                AssetDatabase.CreateAsset(map, mapPath);
            }

            var host = new GameObject("OverworldGrid");
            var component = host.AddComponent<Rokkan.Prophecy.Overworld.OverworldGridHost>();

            SetPrivate(component, "_map", map);
            SetPrivate(component, "_config", AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/ScriptableObjects/Stalberg/StalbergGridConfig.asset"));
            SetPrivate(component, "_tileSet", AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/ScriptableObjects/Stalberg/StalbergTileSet.asset"));
            SetPrivate(component, "_tileMaterial", GrayBoxMaterials.Ground());
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

        // The flat plain-and-rim ground this scene started with is gone: the ground is now the
        // Stålberg grid, built at load by OverworldGridHost from the hand-authored map asset.
        // The collider discipline survives inside the host — placed tiles are stripped, because
        // top-down has no collision and a floor baked into the XZ projection occludes every line
        // of sight (the lesson the plain taught).

        /// <summary>
        /// The cube that carries you back — the portal colour, standing proud of the plain at its
        /// centre, volume a little larger than the cube so touching it is entering it.
        /// </summary>
        private static void CreateReturnPortal(Transform parent)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Portal_Traversal";
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = new Vector3(1.6f, 1.6f, 1.6f);
            cube.transform.position = new Vector3(0f, 0.8f, 0f);

            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Portal();

            var portal = cube.AddComponent<Portal>();
            SetPrivate(portal, "_targetScene", GrayBoxTraversalBuilder.SceneName);
            SetPrivate(portal, "_targetSpawnId", GrayBoxTraversalBuilder.CentreSpawnId);
            SetPrivate(portal, "_halfExtents", new Vector3(1.3f, 1.5f, 1.3f));
        }

        private static void CreateDescriptorAndSpawns(Transform markers)
        {
            var west = Spawn(markers, WestSpawnId, new Vector3(-4f, 0f, 0f), facing: 1);
            Spawn(markers, EastSpawnId, new Vector3(4f, 0f, 0f), facing: -1);

            var descriptorObject = new GameObject("SceneDescriptor");
            descriptorObject.transform.SetParent(markers, false);

            var descriptor = descriptorObject.AddComponent<SceneDescriptor>();
            SetPrivate(descriptor, "_space", MovementSpace.TopDown);
            SetPrivate(descriptor, "_defaultSpawn", west);
            SetPrivate(descriptor, "_displayName", "Gray Box — Overworld");

            // Nothing to fall off, and nothing for the side-scroll camera bounds to mean.
            SetPrivate(descriptor, "_killPlaneEnabled", false);
            SetPrivate(descriptor, "_useCameraBounds", false);
        }

        private static SpawnPoint Spawn(Transform parent, string id, Vector3 position, int facing)
        {
            var spawnObject = new GameObject("Spawn_" + id);
            spawnObject.transform.SetParent(parent, false);
            spawnObject.transform.position = position;

            var spawn = spawnObject.AddComponent<SpawnPoint>();
            SetPrivate(spawn, "_id", id);
            SetPrivate(spawn, "_facing", facing);

            return spawn;
        }

        /// <summary>
        /// The scene's own camera, out-ranking the Bootstrap lane rig while this scene is loaded.
        /// Priority is the whole handover mechanism, and the rig owns it — adding the one
        /// component is the entire setup, which is what keeps this assembly free of Cinemachine.
        /// RequireComponent supplies the CinemachineCamera; the rig adds its own composer.
        /// </summary>
        private static void CreateCamera(MovementTuning tuning)
        {
            var rig = new GameObject("OverworldCamera");

            var component = rig.AddComponent<OverworldCameraRig>();
            SetPrivate(component, "_tuning", tuning);
        }

        /// <summary>
        /// The fight, such as it is. Not for combat — nothing here swings — but perception runs
        /// through the combat registry: a wanderer finds the player by querying the fight's
        /// hurtboxes, and contact (its only threat) resolves through the same OnHit as everything
        /// else. No director, and every wanderer is blind.
        /// </summary>
        private static void CreateCombatDirector()
        {
            var director = new GameObject("CombatDirector");
            var component = director.AddComponent<CombatDirector>();
            SetPrivate(component, "_space", MovementSpace.TopDown);
        }

        /// <summary>The Zelda II layer: wanderers popping up around the player.</summary>
        private static void CreateEncounters(Transform markers)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Prophecy/Prefabs/Enemy_Wanderer.prefab");

            if (prefab == null)
                Debug.LogWarning("[Prophecy] No Enemy_Wanderer prefab — run Prophecy > Build > " +
                                 "Generate Enemies first, then regenerate the overworld. The " +
                                 "spawner was created unarmed.");

            var spawnerObject = new GameObject("OverworldEncounters");
            spawnerObject.transform.SetParent(markers, false);

            var spawner = spawnerObject.AddComponent<OverworldEncounterSpawner>();
            SetPrivate(spawner, "_wandererPrefab", prefab);

            // Sized from the ground this builder just generated — the spawner must not learn
            // the plain's size a second way.
            SetPrivate(spawner, "_plainHalfExtents",
                       new Vector2(PlainWidth * 0.5f, PlainDepth * 0.5f));

            // Touching a wanderer carries the player to the side-scroll course, arriving at the
            // same mid-level spawn the return cube uses. When a real battle scene exists, this is
            // the one string to re-point.
            SetPrivate(spawner, "_encounterScene", GrayBoxTraversalBuilder.SceneName);
            SetPrivate(spawner, "_encounterSpawnId", GrayBoxTraversalBuilder.CentreSpawnId);
        }

        // ------------------------------------------------------------------ helpers

        private static bool HasUnsavedChanges()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;

                Debug.LogError($"[Prophecy] '{(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name)}' " +
                               "has unsaved changes. Save or discard them, then run this again — " +
                               "generating replaces the open scene.");
                return true;
            }

            return false;
        }

        private static MovementTuning LoadTuning()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MovementTuning>(ProphecyAssetBootstrap.MovementTuningPath);

            if (asset == null)
                Debug.LogError($"[Prophecy] No MovementTuning at {ProphecyAssetBootstrap.MovementTuningPath}. " +
                               "Run Prophecy > Build > Create Missing Data Assets first.");

            return asset;
        }

        private static void SetPrivate(Object target, string fieldName, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[Prophecy] {target.GetType().Name} has no serialized field '{fieldName}'.");
                return;
            }

            switch (value)
            {
                case string s: property.stringValue = s; break;
                case int i: property.intValue = i; break;
                case float f: property.floatValue = f; break;
                case bool b: property.boolValue = b; break;
                case System.Enum e: property.enumValueFlag = System.Convert.ToInt32(e); break;
                case Vector2 v2: property.vector2Value = v2; break;
                case Vector3 v: property.vector3Value = v; break;
                case Object o: property.objectReferenceValue = o; break;
                default:
                    Debug.LogError($"[Prophecy] Unsupported field type for '{fieldName}'.");
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
