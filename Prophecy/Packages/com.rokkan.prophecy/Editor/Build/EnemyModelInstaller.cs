using Rokkan.Animation;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Presentation;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Puts the Iron Roc placeholder on the grunt prefab in place of the capsule proxy — the
    /// same operation <see cref="HeroModelInstaller"/> performs for the player, for the same
    /// reasons: idempotent (a re-export means re-running, not re-assembling), capsule disabled
    /// rather than deleted, model scaled to the height the simulation thinks the body is.
    ///
    /// <para><b>The importer is set to Humanoid here rather than by hand.</b> The hero's
    /// install stalled once on a manual "set Animation Type and apply" step; this one flips
    /// the importer itself and reimports before asking for the avatar. Humanoid matters
    /// because it is what lets the player's clips retarget onto the Roc's rig — the grunt
    /// walks and swings with the shared <c>BodyAnimationSet</c> until it earns clips of its
    /// own (a Meshy walk clip sits beside the model, unused for now).</para>
    /// </summary>
    public static class EnemyModelInstaller
    {
        private const string PrefabPath = "Assets/_Prophecy/Prefabs/Enemy_Capsule.prefab";
        private const string SetPath = "Assets/_Prophecy/Data/BodyAnimationSet.asset";
        private const string TuningPath = "Assets/_Prophecy/Data/MovementTuning.asset";
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
        public static void Install()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Prophecy] No Roc model at {ModelPath}. Update ModelPath.");
                return;
            }

            EnsureHumanoid();

            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[Prophecy] {ModelPath} still has no valid humanoid Avatar " +
                               "after reimport — the rig may not be a biped Meshy thinks it " +
                               $"is. (found: {(avatar == null ? "none" : $"valid={avatar.isValid} human={avatar.isHuman}")})");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);

            try
            {
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
                Debug.Log($"[Prophecy] Roc model installed on {PrefabPath}. The capsule is " +
                          "disabled, not deleted — re-enable Body's renderer for the proxy.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Flip the FBX importer to Humanoid if it is not already — the avatar the
        /// whole install depends on only exists in that mode.</summary>
        private static void EnsureHumanoid()
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer) return;
            if (importer.animationType == ModelImporterAnimationType.Human) return;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.SaveAndReimport();
            Debug.Log("[Prophecy] Roc importer set to Humanoid and reimported.");
        }

        private static void ScaleToStandHeight(GameObject instance)
        {
            float standHeight = 1.8f;

            var tuning = AssetDatabase.LoadAssetAtPath<MovementTuning>(TuningPath);
            if (tuning != null) standHeight = tuning.Data.StandHeight;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[Prophecy] Roc model has no renderers; leaving scale at 1.");
                return;
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            if (bounds.size.y <= 0.0001f)
            {
                Debug.LogWarning("[Prophecy] Roc model has zero height; leaving scale at 1.");
                return;
            }

            // Exactly StandHeight, not "a bit bigger for menace": the hurtbox is sim-side
            // and tuning-derived, and an oversized model would lie about where it can be hit.
            float scale = standHeight / bounds.size.y;
            instance.transform.localScale = Vector3.one * scale;

            Debug.Log($"[Prophecy] Roc measured {bounds.size.y:F3} m tall, scaled by {scale:F3} " +
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

        private static void PointTheViewAtTheModel(GameObject root, GameObject model)
        {
            var view = root.GetComponentInChildren<CharacterView>();
            if (view == null)
            {
                Debug.LogWarning("[Prophecy] No CharacterView on the grunt; the Roc will not turn.");
                return;
            }

            var serialized = new SerializedObject(view);

            var body = serialized.FindProperty("_body");
            if (body != null) body.objectReferenceValue = model.transform;

            var resize = serialized.FindProperty("_resizeProxyOnCrouch");
            if (resize != null) resize.boolValue = false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

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
                Debug.LogWarning($"[Prophecy] No material found beside the Roc in {folder}. " +
                                 "It will render untextured.");
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
