using System.IO;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Everything the scene generators share: the scene lifecycle, the lighting rig, the
    /// tuning loads, the primitive spawners, the portal recipe and the serialized-field
    /// setter — extracted for the same reason <see cref="GrayBoxDoors"/> was, and with the
    /// same scars to point at. A per-builder copy of this scaffolding is a fix that lands
    /// in one scene out of five: the tester's dummy got a floor-level hurtbox from a
    /// private SetPrivate, and the shadow-bias fix reached only the overworld's copy of
    /// the light.
    /// </summary>
    internal static class GrayBoxSceneScaffold
    {
        /// <summary>Z thickness of the play-plane boxes, so the side-on camera sees solid
        /// geometry rather than paper edges.</summary>
        public const float Depth = 3f;

        public const float GroundThickness = 2f;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// The lifecycle every generator shares: refuse over unsaved work, remember what
        /// was open, build into a fresh single scene, save, register with the build
        /// settings, and put the editor back the way it was found.
        ///
        /// <para><paramref name="buildContents"/> returns false to abort — nothing is saved,
        /// and the previous scene setup still comes back. <paramref name="afterSave"/> runs
        /// after the scene is saved and registered but before the previous setup returns:
        /// the one window in which the saved file can be inspected. Returns true only when
        /// the scene was built and saved; callers own their completion log, because what is
        /// worth saying about a course differs from what is worth saying about a HUD.</para>
        /// </summary>
        public static bool GenerateScene(string scenePath, System.Func<bool> buildContents,
                                         System.Action afterSave = null)
        {
            // Refuse rather than prompt. A modal "save your changes?" dialog would deadlock
            // this when it is driven from the command line or a tool, and losing someone's
            // unsaved scene to a generator is a far worse outcome than being told to save
            // first.
            if (HasUnsavedChanges()) return false;

            // Remember what was open so it can be put back. An untitled scene has no path
            // and cannot be restored — but it also has nothing in it worth restoring.
            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = false;
            for (int i = 0; setup != null && i < setup.Length; i++)
                if (!string.IsNullOrEmpty(setup[i].path)) canRestore = true;

            // Single, not Additive: Unity refuses to create an additive scene while an
            // untitled one is open, which is the state a freshly opened editor is always in.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            bool built = buildContents();

            if (built)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(scenePath).Replace('\\', '/'));
                EditorSceneManager.SaveScene(scene, scenePath);

                AssetDatabase.Refresh();
                BuildSettings.EnsureInBuildSettings(scenePath);

                afterSave?.Invoke();
            }

            if (canRestore) EditorSceneManager.RestoreSceneManagerSetup(setup);

            return built;
        }

        /// <summary>True (and complains) if any open scene has unsaved edits.</summary>
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

        // ------------------------------------------------------------------ tuning loads

        public static MovementTuning LoadMovementTuning()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MovementTuning>(ProphecyAssetBootstrap.MovementTuningPath);

            if (asset == null)
                Debug.LogError($"[Prophecy] No MovementTuning at {ProphecyAssetBootstrap.MovementTuningPath}. " +
                               "Run Prophecy > Build > Create Missing Data Assets first — every " +
                               "gray-box dimension is derived from it.");

            return asset;
        }

        public static CombatTuning LoadCombatTuning()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CombatTuning>(ProphecyAssetBootstrap.CombatTuningPath);

            if (asset == null)
                Debug.LogError($"[Prophecy] No CombatTuning at {ProphecyAssetBootstrap.CombatTuningPath}. " +
                               "Run Prophecy > Build > Create Missing Data Assets first — every " +
                               "combat distance is derived from the moveset.");

            return asset;
        }

        // ------------------------------------------------------------------ furniture

        /// <summary>
        /// The one directional light. The custom shadow bias is load-bearing: the
        /// overworld's tile world is thin lips and fine stair risers, and the default bias
        /// let their self-shadowing swim while the camera moved — which read exactly like
        /// z-fighting at the stair/wall junctions (a full coplanarity audit of the built
        /// world found zero actual fights). Normal bias flattens the acne; depth bias stays
        /// low so contact shadows keep touching their pieces. The side-scroll boxes are
        /// chunky enough never to have shown it, but they get the same light on purpose —
        /// one rig, or the next shadow fix reaches one scene out of five.
        /// </summary>
        public static void CreateLighting()
        {
            var light = new GameObject("Directional Light");
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var component = light.AddComponent<Light>();
            component.type = LightType.Directional;
            component.intensity = 1.1f;
            component.shadows = LightShadows.Soft;

            component.shadowBias = 0.4f;
            component.shadowNormalBias = 1.8f;

            var data = light.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            data.usePipelineSettings = false;
        }

        /// <summary>
        /// A portal the camera can read: an upright slab in the portal colour, the volume
        /// sized a touch past it so touching it is entering it. No collider — the sim must
        /// not bake a wall across the lane, and the portal tests the player's feet itself.
        /// One recipe for every scene, because the slab is vocabulary: the player is being
        /// taught "this shape moves you", and the tester's portal quietly diverging into a
        /// cube was a lesson taught in two dialects.
        /// </summary>
        public static void PortalSlab(Transform parent, string name, Vector3 basePosition,
                                      string targetScene, string targetSpawnId)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.localScale = new Vector3(1.4f, 2.4f, 0.4f);
            slab.transform.position = basePosition + new Vector3(0f, 1.2f, 0f);

            Object.DestroyImmediate(slab.GetComponent<Collider>());
            slab.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Portal();

            var portal = slab.AddComponent<Portal>();
            SetPrivate(portal, "_targetScene", targetScene);
            SetPrivate(portal, "_targetSpawnId", targetSpawnId);
            SetPrivate(portal, "_halfExtents", new Vector3(0.9f, 1.4f, 1.5f));
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Flat ground from <paramref name="startX"/>, returning the X it ends at.</summary>
        public static float Ground(Transform parent, string name, float startX, float length,
                                   float topY = 0f)
        {
            Box(parent, name,
                new Vector2(startX, topY - GroundThickness),
                new Vector2(startX + length, topY));

            return startX + length;
        }

        /// <summary>An axis-aligned box spanning min..max in the XY play plane.</summary>
        public static Transform Box(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);

            var size = max - min;
            cube.transform.localScale = new Vector3(size.x, size.y, Depth);
            cube.transform.position = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);

            return cube.transform;
        }

        // ------------------------------------------------------------------ serialization

        /// <summary>
        /// Writes a private <c>[SerializeField]</c>. The alternative is making every world
        /// component's fields public purely so a generator can reach them, which would trade
        /// a contained bit of editor reflection for a permanently looser runtime API.
        ///
        /// <para>Failures are LOUD on purpose. A missed write is invisible in the generated
        /// scene until something depends on it — the tester's dummy shipped a floor-level
        /// hurtbox off exactly that — so a missing field or an unhandled type is an error
        /// naming both, never a shrug.</para>
        /// </summary>
        public static void SetPrivate(Object target, string fieldName, object value)
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
                // Clearing a reference is a legitimate write, and a null matches no type
                // pattern below — so it is answered first rather than falling through to
                // the unsupported-type error.
                case null: property.objectReferenceValue = null; break;
                case string s: property.stringValue = s; break;
                case int i: property.intValue = i; break;
                case float f: property.floatValue = f; break;
                case bool b: property.boolValue = b; break;
                // enumValueFlag rather than enumValueIndex: it carries the declared value,
                // which is what a [Flags] enum like MovementSpace needs and what a plain one
                // still gets right.
                case System.Enum e: property.enumValueFlag = System.Convert.ToInt32(e); break;
                case Vector2 v2: property.vector2Value = v2; break;
                case Vector3 v3: property.vector3Value = v3; break;
                case Object o: property.objectReferenceValue = o; break;
                default:
                    Debug.LogError($"[Prophecy] Cannot write '{fieldName}' on {target.GetType().Name}: " +
                                   $"unsupported type {value.GetType().Name}.");
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
