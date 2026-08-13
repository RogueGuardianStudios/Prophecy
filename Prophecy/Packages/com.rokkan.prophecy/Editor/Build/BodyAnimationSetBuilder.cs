using System.Collections.Generic;
using System.Text;
using Rokkan.Prophecy.Presentation;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Builds <c>BodyAnimationSet.asset</c> from the clips that are in the project, with reference
    /// speeds measured rather than guessed.
    ///
    /// <para>A generator rather than a hand-filled asset, for the same reason the gray-box level
    /// is generated: the mapping is <i>derived</i> from measurements and tuning, and a hand-filled
    /// version silently rots the moment either changes. Re-run it after adding clips or retuning
    /// movement, and the mapping stays honest.</para>
    ///
    /// <para>Idempotent. It overwrites the mapping and leaves anything else about the asset alone.</para>
    /// </summary>
    public static class BodyAnimationSetBuilder
    {
        public const string AssetPath = "Assets/_Prophecy/Data/BodyAnimationSet.asset";
        private const string ClipRoot = "Assets/_Prophecy/Animation";

        /// <summary>
        /// The mapping, and the whole reason this is generated.
        ///
        /// <para><b>Clips are chosen by measured speed, not by name.</b> Prophecy's "walk" is
        /// 4 m/s and its "run" is 7.5 m/s — brisk, because it is a Zelda II homage and not a
        /// third-person adventure. Synty's <c>A_Walk</c> is authored at 1.46 m/s, so mapping it to
        /// <c>Walk</c> by name would need a 2.7x playback multiplier and produce a character
        /// sprinting on the spot. Synty's <i>run</i> is the right cadence for Prophecy's walk, and
        /// its <i>sprint</i> for Prophecy's run — which lands both multipliers near 1.</para>
        ///
        /// <para>The state names are gait <i>tiers</i>, not real-world gaits. Read them that way.</para>
        /// </summary>
        private static readonly (BodyState state, string clip, bool loop, float refSpeed)[] Map =
        {
            // ---- ground. Reference speeds measured from the RootMotion counterparts; see
            //      Prophecy > Animation > Measure Reference Speeds.
            (BodyState.Idle,        "A_Idle_Standing_Masc",       true,  0f),
            (BodyState.Walk,        "A_Run_FwdStrafeF_Masc",      true,  2.595f),
            (BodyState.Run,         "A_Sprint_F_Masc",            true,  7.271f),
            (BodyState.CrouchIdle,  "A_Idle_Crouching_Masc",      true,  0f),
            (BodyState.CrouchWalk,  "A_Crouch_FwdStrafeF_Masc",   true,  1.457f),

            // ---- air. No horizontal reference: scaling a fall by run speed would speed up the
            //      whole body for moving sideways, which is not what falling looks like.
            (BodyState.JumpRise,    "A_Jump_Idle_Masc",           false, 0f),
            (BodyState.Fall,        "A_InAir_FallShort_Masc",     true,  0f),
            (BodyState.Land,        "A_Land_IdleSoft_Masc",       false, 0f),
            (BodyState.WallJump,    "A_Jump_Idle_Masc",           false, 0f),

            // ---- defence
            (BodyState.Block,       "A_Block_Loop_Sword",         true,  0f),
            (BodyState.Parry,       "A_Parry_F_Sword",            false, 0f),
            (BodyState.HitReact,    "A_Hit_F_React_Sword",        false, 0f),
            (BodyState.Dodge,       "A_Dodge_B_Sword",            false, 0f),

            // ---- offence. A and B must differ visibly or the combo reads as a stutter.
            (BodyState.AttackStandA, "A_Attack_LightCombo01A_Sword", false, 0f),
            (BodyState.AttackStandB, "A_Attack_LightCombo01B_Sword", false, 0f),
            (BodyState.AttackCrouch, "A_Attack_HeavyStab01_Sword",   false, 0f),

            // ---- terminal
            (BodyState.Death,       "A_Death_F_01_Sword",         false, 0f),

            // Deliberately unmapped, because no clip exists in either Synty pack and a wrong clip
            // is worse than an honest gap: WallSlide, LedgeHang, LedgeClimb, LadderIdle,
            // LadderClimb, DownThrust. See Plans/Animation-Requirements.md §3.
        };

        [MenuItem("Prophecy/Build/Generate Body Animation Set", priority = 30)]
        public static void Generate()
        {
            var clips = IndexClips();
            var entries = new List<BodyAnimationSet.Entry>();
            var missing = new List<string>();

            foreach (var (state, clipName, loop, refSpeed) in Map)
            {
                if (!clips.TryGetValue(clipName, out var clip))
                {
                    missing.Add($"{state} -> {clipName}");
                    continue;
                }

                entries.Add(new BodyAnimationSet.Entry
                {
                    State = state,
                    Clip = clip,
                    Loop = loop,
                    ReferenceSpeed = refSpeed,
                });
            }

            var set = AssetDatabase.LoadAssetAtPath<BodyAnimationSet>(AssetPath);
            bool created = set == null;

            if (created)
            {
                set = ScriptableObject.CreateInstance<BodyAnimationSet>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }

            var serialized = new SerializedObject(set);
            var list = serialized.FindProperty("_entries");
            list.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("State").enumValueIndex = (int)entries[i].State;
                element.FindPropertyRelative("Clip").objectReferenceValue = entries[i].Clip;
                element.FindPropertyRelative("Loop").boolValue = entries[i].Loop;
                element.FindPropertyRelative("ReferenceSpeed").floatValue = entries[i].ReferenceSpeed;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            var report = new StringBuilder();
            report.AppendLine($"[Prophecy] {(created ? "Created" : "Updated")} {AssetPath}");
            report.AppendLine($"  mapped  : {entries.Count} states");

            var unmapped = set.MissingStates();
            report.AppendLine($"  unmapped: {unmapped.Count} — {string.Join(", ", unmapped)}");

            if (missing.Count > 0)
            {
                report.AppendLine("  CLIP NOT FOUND (these were expected to exist):");
                foreach (var m in missing) report.AppendLine($"    {m}");
            }

            if (missing.Count > 0) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
        }

        /// <summary>Every clip under the animation root, by name. Sub-assets included — an FBX
        /// carries its clips inside it rather than beside it.</summary>
        private static Dictionary<string, AnimationClip> IndexClips()
        {
            var clips = new Dictionary<string, AnimationClip>();

            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                        clips[clip.name] = clip;
                }
            }

            return clips;
        }
    }
}
