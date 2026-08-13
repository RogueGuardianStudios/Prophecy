using Rokkan.Animation;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Editor.Build;
using Rokkan.Prophecy.Presentation;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Puts a rigged Meshy placeholder onto a character prefab in place of the capsule
    /// proxy, wires the animation system, and scales the model to the height the simulation
    /// thinks the body is. <see cref="HeroModelInstaller"/> and
    /// <see cref="EnemyModelInstaller"/> are the entry points; each holds only its paths.
    ///
    /// <para><b>One installer, on purpose.</b> The hero and the Roc were installed by two
    /// two-hundred-line copies of this procedure, and the copies drifted the way copies do:
    /// the enemy's grew the automatic Humanoid flip while the hero's still stalled on a
    /// manual "set Animation Type and apply" step. Shared, a fix reaches every character.</para>
    ///
    /// <para>Idempotent — a re-export means re-running, not re-assembling; the previous
    /// model root is replaced rather than accumulated. The capsule is disabled, not
    /// deleted: it is the fallback if the model turns out to be wrong, and
    /// <c>CharacterView</c> still holds a reference to that transform.</para>
    /// </summary>
    internal static class ModelInstaller
    {
        public static void Install(string prefabPath, string modelPath, string modelRootName,
                                   string displayName)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[Prophecy] No {displayName} model at {modelPath}. Update ModelPath.");
                return;
            }

            EnsureHumanoid(modelPath, displayName);

            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(modelPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[Prophecy] {modelPath} still has no valid humanoid Avatar " +
                               "after reimport — the rig may not be the biped Meshy thinks it " +
                               $"is. (found: {(avatar == null ? "none" : $"valid={avatar.isValid} human={avatar.isHuman}")})");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                // Replace rather than accumulate, so re-running after a re-export is safe.
                var existing = root.transform.Find(modelRootName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = modelRootName;
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                ScaleToStandHeight(instance, displayName);

                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.applyRootMotion = false;   // the sim owns position; see the contract
                animator.runtimeAnimatorController = null;

                if (instance.GetComponent<AnimationSystem>() == null)
                    instance.AddComponent<AnimationSystem>();

                ApplyImportedMaterial(instance, modelPath, displayName);

                HideCapsule(root);
                WireAnimator(root, instance);
                PointTheViewAtTheModel(root, instance, displayName);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[Prophecy] {displayName} model installed on {prefabPath}. The capsule " +
                          "is disabled, not deleted — re-enable Body's renderer for the proxy.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Flip the FBX importer to Humanoid if it is not already — the avatar the
        /// whole install depends on only exists in that mode. Automatic because the manual
        /// version of this step once stalled the hero's install.</summary>
        private static void EnsureHumanoid(string modelPath, string displayName)
        {
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer) return;
            if (importer.animationType == ModelImporterAnimationType.Human) return;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.SaveAndReimport();
            Debug.Log($"[Prophecy] {displayName} importer set to Humanoid and reimported.");
        }

        /// <summary>
        /// Scale the model so it stands exactly <c>StandHeight</c> tall.
        ///
        /// <para>Measured from the renderer bounds rather than trusted from the exporter: a
        /// generated mesh arrives at whatever scale the generator felt like, and a character
        /// who is subtly the wrong size makes every jump distance and every hit box read as
        /// wrong while all the numbers behind them are right. Exactly StandHeight, not "a
        /// bit bigger for menace" — the hurtbox is sim-side and tuning-derived, and an
        /// oversized model would lie about where the body can be hit.</para>
        /// </summary>
        private static void ScaleToStandHeight(GameObject instance, string displayName)
        {
            float standHeight = 1.8f;

            var tuning = AssetDatabase.LoadAssetAtPath<MovementTuning>(
                ProphecyAssetBootstrap.MovementTuningPath);
            if (tuning != null) standHeight = tuning.Data.StandHeight;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[Prophecy] {displayName} model has no renderers; leaving scale at 1.");
                return;
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            if (bounds.size.y <= 0.0001f)
            {
                Debug.LogWarning($"[Prophecy] {displayName} model has zero height; leaving scale at 1.");
                return;
            }

            float scale = standHeight / bounds.size.y;
            instance.transform.localScale = Vector3.one * scale;

            Debug.Log($"[Prophecy] {displayName} measured {bounds.size.y:F3} m tall, scaled by " +
                      $"{scale:F3} to match StandHeight {standHeight} m.");
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

            var set = AssetDatabase.LoadAssetAtPath<BodyAnimationSet>(BodyAnimationSetBuilder.AssetPath);
            if (set == null)
                Debug.LogWarning($"[Prophecy] No BodyAnimationSet at {BodyAnimationSetBuilder.AssetPath}. " +
                                 "Run Prophecy > Build > Generate Body Animation Set first.");

            var serialized = new SerializedObject(animator);
            serialized.FindProperty("_host").objectReferenceValue = root.GetComponent<PlayerCharacterHost>();
            serialized.FindProperty("_set").objectReferenceValue = set;
            serialized.FindProperty("_animation").objectReferenceValue = model.GetComponent<AnimationSystem>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Point <c>CharacterView</c> at the model, and stop it squashing anything.
        ///
        /// <para><b>The view rotates <c>_body</c>, not the root.</b> That was invisible while
        /// the body was a capsule — a capsule looks identical from every angle — and became
        /// obvious the moment a character arrived: the model was a sibling of the capsule, so
        /// it inherited no rotation at all and stood facing +Z, straight away from the camera,
        /// while the capsule it was standing next to turned correctly and invisibly.</para>
        ///
        /// <para>The crouch resize goes off with the same call. Squashing a capsule was a fair
        /// proxy for crouching; applied to a character it is a funhouse mirror, and it would
        /// fight the crouch clip for the same information.</para>
        /// </summary>
        private static void PointTheViewAtTheModel(GameObject root, GameObject model,
                                                   string displayName)
        {
            var view = root.GetComponentInChildren<CharacterView>();
            if (view == null)
            {
                Debug.LogWarning($"[Prophecy] No CharacterView on the prefab; the {displayName} " +
                                 "will not turn.");
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
        /// <para>The importer arrives with <c>externalObjects: {}</c> — the FBX's material
        /// slot is not remapped to the <c>.mat</c> Meshy exported next to it, so the model
        /// renders in Unity's default grey with the textures sitting unused in the same
        /// folder. Assigned on the renderers rather than through an importer remap because
        /// the remap keys on the material's name inside the FBX, which is Meshy's to choose
        /// and has already changed once between exports.</para>
        /// </summary>
        private static void ApplyImportedMaterial(GameObject instance, string modelPath,
                                                  string displayName)
        {
            string folder = System.IO.Path.GetDirectoryName(modelPath)!.Replace('\\', '/');

            Material material = null;

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null) break;
            }

            if (material == null)
            {
                Debug.LogWarning($"[Prophecy] No material found beside the {displayName} model in " +
                                 $"{folder}. It will render untextured.");
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
