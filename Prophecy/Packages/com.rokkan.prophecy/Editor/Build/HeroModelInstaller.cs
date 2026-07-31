using Rokkan.Animation;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Presentation;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Puts the gray-box hero mesh on the player prefab in place of the capsule proxy, wires the
    /// animation system, and scales the model to the height the simulation thinks the body is.
    ///
    /// <para><b>A command rather than hand-assembly, because this is not a one-off.</b> The hero
    /// has already been re-exported once — the first Meshy export came back unrigged — and any
    /// future re-export means doing all of this again. Idempotent: it replaces its own previous
    /// output rather than stacking a second model under the player.</para>
    ///
    /// <para>The capsule is disabled, not deleted. It is the fallback if the model turns out to be
    /// wrong, and <c>CharacterView</c> still holds a reference to that transform.</para>
    /// </summary>
    public static class HeroModelInstaller
    {
        private const string PrefabPath = "Assets/_Prophecy/Prefabs/Player.prefab";
        private const string SetPath = "Assets/_Prophecy/Data/BodyAnimationSet.asset";
        private const string TuningPath = "Assets/_Prophecy/Data/MovementTuning.asset";
        private const string ModelRootName = "HeroModel";

        /// <summary>Where the rigged hero lives. Update when Meshy produces a new one.</summary>
        private const string ModelPath =
            "Assets/MeshyImports/T-Pose Figure_20260731_110125/Meshy_AI_T_Pose_Figure_biped_Character_output.fbx";

        [MenuItem("Prophecy/Build/Install Hero Model On Player", priority = 31)]
        public static void Install()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Prophecy] No hero model at {ModelPath}. Update ModelPath.");
                return;
            }

            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[Prophecy] {ModelPath} has no valid humanoid Avatar. Set the " +
                               "importer's Animation Type to Humanoid and apply, then re-run. " +
                               $"(found: {(avatar == null ? "none" : $"valid={avatar.isValid} human={avatar.isHuman}")})");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);

            try
            {
                // Replace rather than accumulate, so re-running after a re-export is safe.
                var existing = root.transform.Find(ModelRootName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = ModelRootName;
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                ScaleToStandHeight(instance);

                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;   // the sim owns position; see the contract
                animator.runtimeAnimatorController = null;

                if (instance.GetComponent<AnimationSystem>() == null)
                    instance.AddComponent<AnimationSystem>();

                ApplyImportedMaterial(instance);

                HideCapsule(root);
                WireAnimator(root, instance);
                PointTheViewAtTheModel(root, instance);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[Prophecy] Hero model installed on {PrefabPath}. The capsule is disabled, " +
                          "not deleted — re-enable Body's renderer to get the proxy back.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Scale the model so it stands exactly <c>StandHeight</c> tall.
        ///
        /// <para>Measured from the renderer bounds rather than trusted from the exporter: a
        /// generated mesh arrives at whatever scale the generator felt like, and a character who
        /// is subtly the wrong size makes every jump distance and every hit box read as wrong
        /// while all the numbers behind them are right.</para>
        /// </summary>
        private static void ScaleToStandHeight(GameObject instance)
        {
            float standHeight = 1.8f;

            var tuning = AssetDatabase.LoadAssetAtPath<MovementTuning>(TuningPath);
            if (tuning != null) standHeight = tuning.Data.StandHeight;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[Prophecy] Hero model has no renderers; leaving scale at 1.");
                return;
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            if (bounds.size.y <= 0.0001f)
            {
                Debug.LogWarning("[Prophecy] Hero model has zero height; leaving scale at 1.");
                return;
            }

            float scale = standHeight / bounds.size.y;
            instance.transform.localScale = Vector3.one * scale;

            Debug.Log($"[Prophecy] Hero measured {bounds.size.y:F3} m tall, scaled by {scale:F3} " +
                      $"to match StandHeight {standHeight} m.");
        }

        private static void HideCapsule(GameObject root)
        {
            var body = root.transform.Find("Body");
            if (body == null) return;

            var renderer = body.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
        }

        private static void WireAnimator(GameObject root, GameObject model)
        {
            var animator = root.GetComponent<CharacterAnimator>() ?? root.AddComponent<CharacterAnimator>();

            var set = AssetDatabase.LoadAssetAtPath<BodyAnimationSet>(SetPath);
            if (set == null)
                Debug.LogWarning($"[Prophecy] No BodyAnimationSet at {SetPath}. Run " +
                                 "Prophecy > Build > Generate Body Animation Set first.");

            var serialized = new SerializedObject(animator);
            serialized.FindProperty("_host").objectReferenceValue = root.GetComponent<PlayerCharacterHost>();
            serialized.FindProperty("_set").objectReferenceValue = set;
            serialized.FindProperty("_animation").objectReferenceValue = model.GetComponent<AnimationSystem>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Point <c>CharacterView</c> at the model, and stop it squashing anything.
        ///
        /// <para><b>The view rotates <c>_body</c>, not the root.</b> That was invisible while the
        /// body was a capsule — a capsule looks identical from every angle — and became obvious the
        /// moment a character arrived: the model was a sibling of the capsule, so it inherited no
        /// rotation at all and stood facing +Z, straight away from the camera, while the capsule it
        /// was standing next to turned correctly and invisibly.</para>
        ///
        /// <para>The crouch resize goes off with the same call. Squashing a capsule was a fair
        /// proxy for crouching; applied to a character it is a funhouse mirror, and it would fight
        /// the crouch clip for the same information.</para>
        /// </summary>
        private static void PointTheViewAtTheModel(GameObject root, GameObject model)
        {
            var view = root.GetComponentInChildren<CharacterView>();
            if (view == null)
            {
                Debug.LogWarning("[Prophecy] No CharacterView on the player; the hero will not turn.");
                return;
            }

            var serialized = new SerializedObject(view);

            var body = serialized.FindProperty("_body");
            if (body != null) body.objectReferenceValue = model.transform;

            var resize = serialized.FindProperty("_resizeProxyOnCrouch");
            if (resize != null) resize.boolValue = false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Bind the material that came in beside the model.
        ///
        /// <para>The importer arrives with <c>externalObjects: {}</c> — the FBX's material slot is
        /// not remapped to the <c>.mat</c> Meshy exported next to it, so the model renders in
        /// Unity's default grey with the textures sitting unused in the same folder. Assigned on
        /// the renderers rather than through an importer remap because the remap keys on the
        /// material's name inside the FBX, which is Meshy's to choose and has already changed once
        /// between exports.</para>
        /// </summary>
        private static void ApplyImportedMaterial(GameObject instance)
        {
            string folder = System.IO.Path.GetDirectoryName(ModelPath)!.Replace('\\', '/');

            Material material = null;

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null) break;
            }

            if (material == null)
            {
                Debug.LogWarning($"[Prophecy] No material found beside the model in {folder}. " +
                                 "The hero will render untextured.");
                return;
            }

            var renderers = instance.GetComponentsInChildren<Renderer>();

            foreach (var renderer in renderers)
            {
                var slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }

            Debug.Log($"[Prophecy] Applied '{material.name}' to {renderers.Length} renderer(s).");
        }
    }
}
