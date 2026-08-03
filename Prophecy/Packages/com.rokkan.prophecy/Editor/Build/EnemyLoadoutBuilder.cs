using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Sim;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// The abilities an enemy is allowed to have.
    ///
    /// <para><b>Every module defaults to enabled, and an enemy with no loadout gets all of them.</b>
    /// That is the right default for the player, who is meant to accumulate the whole moveset, and
    /// entirely wrong for a grunt: with no loadout assigned the capsule quietly owned double jump,
    /// the dodge, the down-thrust, ledge hangs and drop-through. An ambusher pressing jump to spring
    /// was double-jumping because nothing had ever said it could not.</para>
    ///
    /// <para><b>The list is exhaustive on purpose.</b> Naming only what to switch off would mean a
    /// module added later arrives switched on for every enemy in the game, discovered whenever
    /// someone notices a skeleton wall-jumping. Every id is listed and answered, so a new one shows
    /// up as a compile-time gap here rather than as a behaviour in play.</para>
    /// </summary>
    public static class EnemyLoadoutBuilder
    {
        public const string LoadoutPath = "Assets/_Prophecy/Data/AbilityLoadout_Enemy.asset";

        /// <summary>
        /// What a melee enemy needs to walk, fall, swing and be hurt — and nothing else.
        ///
        /// <para>Jump is on because an ambusher springs with it. Block and Parry are off: an enemy
        /// that guards is a design decision with tuning attached, not something to inherit by
        /// accident from the player's list.</para>
        /// </summary>
        private static readonly (AbilityId Id, bool Enabled)[] Enemy =
        {
            (AbilityId.Gravity,     true),
            (AbilityId.GroundMove,  true),
            (AbilityId.TopDownMove, true),   // harmless in side-scroll; needed in the overworld
            (AbilityId.FallLand,    true),
            (AbilityId.Jump,        true),
            (AbilityId.Attack,      true),
            (AbilityId.HitReact,    true),

            (AbilityId.Crouch,      false),
            (AbilityId.Crawl,       false),
            (AbilityId.DoubleJump,  false),  // the ambusher's spring was becoming a double jump
            (AbilityId.WallSlide,   false),
            (AbilityId.WallJump,    false),
            (AbilityId.DodgeStep,   false),
            (AbilityId.DownThrust,  false),
            (AbilityId.LedgeHang,   false),
            (AbilityId.LedgePullUp, false),
            (AbilityId.LadderClimb, false),
            (AbilityId.FlameArt,    false),
            (AbilityId.Interact,    false),
            (AbilityId.DropThrough, false),
            (AbilityId.Block,       false),
            (AbilityId.Parry,       false),
        };

        [MenuItem("Prophecy/Build/Generate Enemy Ability Loadout", priority = 43)]
        public static AbilityLoadout Generate()
        {
            var loadout = AssetDatabase.LoadAssetAtPath<AbilityLoadout>(LoadoutPath);

            if (loadout == null)
            {
                loadout = ScriptableObject.CreateInstance<AbilityLoadout>();
                AssetDatabase.CreateAsset(loadout, LoadoutPath);
            }

            var serialized = new SerializedObject(loadout);
            var abilities = serialized.FindProperty("_data").FindPropertyRelative("Abilities");

            if (abilities == null || !abilities.isArray)
            {
                Debug.LogError("[Prophecy] AbilityLoadout has no _data.Abilities array — shape changed?");
                return null;
            }

            abilities.ClearArray();

            for (int i = 0; i < Enemy.Length; i++)
            {
                abilities.InsertArrayElementAtIndex(i);
                var entry = abilities.GetArrayElementAtIndex(i);

                entry.FindPropertyRelative("Id").enumValueIndex = (int)Enemy[i].Id;
                entry.FindPropertyRelative("Enabled").boolValue = Enemy[i].Enabled;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loadout);
            AssetDatabase.SaveAssets();

            return loadout;
        }
    }
}
