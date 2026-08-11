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

        /// <summary>Scene name portals target. Derived so it cannot drift from the asset.</summary>
        public static string SceneName => Path.GetFileNameWithoutExtension(ScenePath);

        /// <summary>The one arrival spawn, west of the respawn post.</summary>
        public const string DefaultSpawnId = "default";

        private const string GruntPrefabPath = "Assets/_Prophecy/Prefabs/Enemy_Capsule.prefab";

        private const float FloorWidth = 46f;
        private const float Depth = 3f;
        private const float GroundThickness = 2f;

        // Three rooms on one flat floor (Matt: split the arena into rooms with doors). West
        // to east: the ANTECHAMBER (exit portal, up-thrust dummy — the quiet room), the
        // ARRIVAL (the spawn), and the DUEL (the Roc, behind a door — entering the fight is
        // a committed choice). The flat floor is every door's landing pad for free.
        private const float DoorWestX = -9f;
        private const float DoorEastX = 5f;

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
            CreateExitPortal(geometry);
            CreateUpThrustTarget(geometry);
            CreateRooms(geometry, markers);
        }

        private static void CreateRooms(Transform geometry, Transform markers)
        {
            float west = -FloorWidth * 0.5f;
            float east = FloorWidth * 0.5f;

            GrayBoxDoors.Doorway(geometry, "Door_Antechamber", DoorWestX, 1, 2, Depth);
            GrayBoxDoors.Doorway(geometry, "Door_Duel", DoorEastX, 2, 3, Depth);

            GrayBoxDoors.Bounds(markers, 1, -3.6f, 40f, west - 4f, DoorWestX);
            GrayBoxDoors.Bounds(markers, 2, -3.6f, 40f, DoorWestX, DoorEastX);
            GrayBoxDoors.Bounds(markers, 3, -3.6f, 40f, DoorEastX, east + 4f);
        }

        /// <summary>
        /// The up-thrust's proof: a dummy hung over the floor, placed by arithmetic where only
        /// the rising blade reaches. The blade spans feet+1.65..feet+2.35 and a jump lifts the
        /// feet 2.4, so a body centred at 3.2 (spanning 2.75..3.65) connects mid-rise, sits
        /// above every grounded swing, and no jump puts feet above it to dive on — whatever
        /// hits this was an up-thrust. Away from the respawn post so the duel stays a duel.
        /// </summary>
        private static void CreateUpThrustTarget(Transform parent)
        {
            const float x = -17f;   // the antechamber: practice hangs in the quiet room
            const float centreY = 3.2f;
            var size = new Vector2(0.9f, 0.9f);

            var root = new GameObject("Dummy_UpThrust");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(size.x, size.y, 0.9f);
            body.transform.localPosition = new Vector3(0f, centreY, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            // A visual stalk so the thing reads as mounted rather than levitating. No collider —
            // the sim never sees it, and a pole you could stand on would put feet above the box.
            var stalk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stalk.name = "Stalk";
            stalk.transform.SetParent(root.transform, false);
            float stalkTop = centreY - size.y * 0.5f;
            stalk.transform.localScale = new Vector3(0.08f, stalkTop, 0.08f);
            stalk.transform.localPosition = new Vector3(0f, stalkTop * 0.5f, 0f);
            Object.DestroyImmediate(stalk.GetComponent<Collider>());

            var combatant = root.AddComponent<Combatant>();
            SetPrivate(combatant, "_combatId", 40);
            SetPrivate(combatant, "_team", 2);
            SetPrivate(combatant, "_size", size);
            SetPrivate(combatant, "_offset", new Vector2(0f, centreY));
            SetPrivate(combatant, "_maxHealth", 40);   // quarters: ten hearts of practice
        }

        /// <summary>
        /// The way home, at the west edge — behind the arriving player, with the fight the
        /// other way, so leaving is a decision to disengage rather than a hazard beside the
        /// duel. It targets the overworld's <see cref="SceneDirector.ReturnSpawnId"/>: you
        /// stand back up exactly where you left the map, Zelda II's encounter rule.
        /// </summary>
        private static void CreateExitPortal(Transform parent)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Portal_Overworld";
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = new Vector3(1.6f, 1.6f, 1.6f);
            cube.transform.position = new Vector3(-FloorWidth * 0.5f + 2f, 0.8f, 0f);

            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Portal();

            var portal = cube.AddComponent<Portal>();
            SetPrivate(portal, "_targetScene", GrayBoxOverworldBuilder.SceneName);
            SetPrivate(portal, "_targetSpawnId", SceneDirector.ReturnSpawnId);
            SetPrivate(portal, "_halfExtents", new Vector3(1.3f, 1.5f, 1.3f));
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
            // The arrival room, between the doors.
            var spawnObject = new GameObject("Spawn_default");
            spawnObject.transform.SetParent(markers, false);
            spawnObject.transform.position = new Vector3(-2f, 0f, 0f);

            var spawn = spawnObject.AddComponent<SpawnPoint>();
            SetPrivate(spawn, "_id", "default");
            SetPrivate(spawn, "_facing", 1);
            SetPrivate(spawn, "_room", 2);

            var descriptorObject = new GameObject("SceneDescriptor");
            descriptorObject.transform.SetParent(markers, false);
            var descriptor = descriptorObject.AddComponent<SceneDescriptor>();
            SetPrivate(descriptor, "_space", MovementSpace.SideScroll);

            // Camera bounds ON: the arriving rig learns the scene's rooms in
            // SetVerticalBounds, and the room clamps hang off that path. One lane below
            // the floor, the project constant.
            SetPrivate(descriptor, "_useCameraBounds", true);
            SetPrivate(descriptor, "_cameraFloorY", -3.6f);
            SetPrivate(descriptor, "_cameraCeilingY", 40f);
        }

        private static void CreateRespawner(Transform markers)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GruntPrefabPath);
            if (prefab == null)
                Debug.LogWarning($"[Prophecy] No grunt prefab at {GruntPrefabPath} — run " +
                                 "Prophecy > Build > Generate Enemies first, then regenerate. " +
                                 "The respawner was created unarmed.");

            // Deep in the duel room, so the fight starts with ground to give.
            var post = new GameObject("RespawnPost_Grunt");
            post.transform.SetParent(markers, false);
            post.transform.position = new Vector3(14f, 0f, 0f);

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
                case Vector2 v2: property.vector2Value = v2; break;
                case Vector3 v: property.vector3Value = v; break;
                case MovementSpace space: property.enumValueIndex = (int)space; break;
                default:
                    Debug.LogError($"[Prophecy] Unhandled type for '{field}'.");
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
