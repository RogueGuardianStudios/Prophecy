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

                HideCapsule(root);
                WireAnimator(root, instance);
                StopScalingTheBodyOnCrouch(root);

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
        /// Turn off the crouch proxy resize.
        ///
        /// <para>Squashing a capsule was a fair way to show a crouch when the body was a capsule.
        /// Applied to a character it is a funhouse mirror, and it would fight the crouch animation
        /// for the same information.</para>
        /// </summary>
        private static void StopScalingTheBodyOnCrouch(GameObject root)
        {
            var view = root.GetComponentInChildren<CharacterView>();
            if (view == null) return;

            var serialized = new SerializedObject(view);
            var property = serialized.FindProperty("_resizeProxyOnCrouch");
            if (property == null) return;

            property.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
