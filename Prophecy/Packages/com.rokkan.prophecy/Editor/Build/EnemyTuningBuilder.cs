using Rokkan.Prophecy.Core;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Derives an enemy moveset from the player's by slowing the wind-up.
    ///
    /// <para><b>A player's attack and an enemy's want opposite things.</b> The player's
    /// <c>slash_high</c> starts up in 6 ticks — 100 ms — because a weapon that lags behind the
    /// button feels broken in your hands. Pointed at the player, that same 100 ms is shorter than
    /// human reaction time, so the swing cannot be answered: the telegraph is invisible, the 8-tick
    /// parry window can only be guessed at, and blocking is luck. Every "it hits too fast", "I can't
    /// see it coming" and "parrying does nothing" is that one number.</para>
    ///
    /// <para><b>Derived rather than authored from scratch, on purpose.</b> Damage, reach, hit-box
    /// offsets and the combo chain are all tuned already and should stay tied to the player's — an
    /// enemy that reaches further than it appears to is a different bug. Only the timing changes,
    /// and everything downstream of a tick offset moves with it: hit boxes open later by exactly
    /// the amount the wind-up grew, so the swing still lands on the frame it looks like it should.
    /// Regenerate after retuning the player and the enemy follows.</para>
    ///
    /// <para>The result is a normal <c>CombatTuning</c> asset, so it stays the live tuning surface —
    /// edit it in play mode and the change applies on the next tick.</para>
    /// </summary>
    public static class EnemyTuningBuilder
    {
        public const string GruntTuningPath = "Assets/_Prophecy/Data/CombatTuning_Grunt.asset";
        private const string PlayerTuningPath = "Assets/_Prophecy/Data/CombatTuning.asset";

        /// <summary>
        /// Ticks of wind-up before an enemy swing becomes dangerous.
        ///
        /// <para>24 ticks is 400 ms. Reaction to an expected visual cue is roughly 250 ms, so this
        /// leaves room to see the telegraph, decide, and still press guard inside the 8-tick parry
        /// window rather than ahead of it. It is the smallest number that makes the defence real;
        /// slower reads as sluggish, faster reads as unfair.</para>
        /// </summary>
        private const int EnemyStartupTicks = 24;

        [MenuItem("Prophecy/Build/Generate Enemy Combat Tuning", priority = 41)]
        public static CombatTuning Generate()
        {
            var player = AssetDatabase.LoadAssetAtPath<CombatTuning>(PlayerTuningPath);

            if (player == null)
            {
                Debug.LogError($"[Prophecy] no player CombatTuning at {PlayerTuningPath}.");
                return null;
            }

            // Copied from the player's asset every time rather than edited in place, so a retune of
            // the player's damage or reach reaches the enemy on the next regenerate instead of
            // quietly diverging. CONTENT copy, not file copy — see the helper.
            var grunt = CopyContentKeepingIdentity(PlayerTuningPath, GruntTuningPath);
            if (grunt == null) return null;

            var serialized = new SerializedObject(grunt);

            var attacks = serialized.FindProperty("_data").FindPropertyRelative("Attacks");
            if (attacks == null || !attacks.isArray)
            {
                Debug.LogError("[Prophecy] CombatTuning has no _data.Attacks array — shape changed?");
                return null;
            }

            for (int i = 0; i < attacks.arraySize; i++)
                SlowWindUp(attacks.GetArrayElementAtIndex(i));

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(grunt);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Prophecy] Enemy combat tuning generated at {GruntTuningPath} " +
                      $"(wind-up {EnemyStartupTicks} ticks).");
            return grunt;
        }

        /// <summary>
        /// Overwrite the asset at <paramref name="targetPath"/> with the content of the one at
        /// <paramref name="sourcePath"/>, creating it only if it does not exist yet.
        ///
        /// <para><b>The old delete-and-copy minted a new GUID every run</b>, and a GUID is the
        /// asset's identity: every prefab referencing the tuning was silently re-pointed on each
        /// regenerate, and any referencer NOT regenerated in the same pass kept a dangling GUID
        /// and resolved to nothing — which for a moveset means an enemy that cannot attack, with
        /// no error saying why. Copying the serialized content into the existing file changes
        /// what the asset says without changing what it <i>is</i>.</para>
        /// </summary>
        private static CombatTuning CopyContentKeepingIdentity(string sourcePath, string targetPath)
        {
            var source = AssetDatabase.LoadAssetAtPath<CombatTuning>(sourcePath);
            if (source == null)
            {
                Debug.LogError($"[Prophecy] no CombatTuning at {sourcePath} to copy from.");
                return null;
            }

            var target = AssetDatabase.LoadAssetAtPath<CombatTuning>(targetPath);

            if (target == null)
            {
                // First run only — the one time a file copy is the right tool.
                if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                {
                    Debug.LogError($"[Prophecy] could not copy {sourcePath} to {targetPath}.");
                    return null;
                }

                return AssetDatabase.LoadAssetAtPath<CombatTuning>(targetPath);
            }

            EditorUtility.CopySerialized(source, target);

            // CopySerialized brings the name along too, and an asset whose display name disagrees
            // with its file name reads as a different asset in every picker.
            target.name = System.IO.Path.GetFileNameWithoutExtension(targetPath);
            EditorUtility.SetDirty(target);

            return target;
        }

        public const string CasterTuningPath = "Assets/_Prophecy/Data/CombatTuning_Caster.asset";

        /// <summary>
        /// The caster's moveset: the grunt's timing, but the swing throws something instead of
        /// reaching.
        ///
        /// <para><b>A ranged attack is not a melee one with a longer arm.</b> Left as-is, a caster
        /// firing from 12 m runs a hit box authored to sit 0.7 m in front of its chest — the attack
        /// plays, the planner is satisfied, and nothing is ever in the box. So the melee boxes come
        /// off and a projectile goes on: the reach becomes the bolt's problem, which is the only
        /// version of this that can miss for reasons the player can see.</para>
        /// </summary>
        [MenuItem("Prophecy/Build/Generate Caster Combat Tuning", priority = 42)]
        public static CombatTuning GenerateCaster()
        {
            // Built from the enemy tuning rather than the player's, so the readable wind-up is
            // inherited rather than restated — a caster you cannot see winding up is the same bug.
            var enemy = Generate();
            if (enemy == null) return null;

            var caster = CopyContentKeepingIdentity(GruntTuningPath, CasterTuningPath);
            if (caster == null) return null;

            var serialized = new SerializedObject(caster);

            var attacks = serialized.FindProperty("_data").FindPropertyRelative("Attacks");
            if (attacks == null || !attacks.isArray || attacks.arraySize == 0)
            {
                Debug.LogError("[Prophecy] caster tuning has no attacks to convert.");
                return null;
            }

            for (int i = 0; i < attacks.arraySize; i++)
                MakeRanged(attacks.GetArrayElementAtIndex(i));

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(caster);
            AssetDatabase.SaveAssets();

            // ProjectileDefinition ships a validator that says exactly what is wrong with a bolt
            // that cannot work, and not calling it cost a caster that fired into nothing for a
            // whole session. Generation now refuses to pass quietly.
            foreach (var attack in caster.Data.Attacks)
            {
                if (attack?.Spawns == null) continue;

                for (int i = 0; i < attack.Spawns.Length; i++)
                {
                    string malformed = attack.Spawns[i].Projectile?.Validate();
                    if (malformed == null) continue;

                    Debug.LogError($"[Prophecy] {CasterTuningPath} '{attack.Id}' spawn {i} " +
                                   $"cannot work: {malformed}");
                    return null;
                }
            }

            Debug.Log($"[Prophecy] Caster combat tuning generated at {CasterTuningPath}.");
            return caster;
        }

        /// <summary>
        /// Strips an attack's melee boxes and gives it a bolt, launched the tick it would have
        /// become dangerous.
        /// </summary>
        private static void MakeRanged(SerializedProperty attack)
        {
            var startup = attack.FindPropertyRelative("StartupTicks");
            int launchTick = startup?.intValue ?? EnemyStartupTicks;

            // Carried over from the melee box being removed, so the caster inherits the tuned
            // damage rather than a number invented here — and so a projectile authored with the
            // default 0 cannot ship as a bolt that politely does nothing.
            var boxes = attack.FindPropertyRelative("HitBoxes");
            int damage = 0;

            if (boxes != null && boxes.isArray && boxes.arraySize > 0)
            {
                var first = boxes.GetArrayElementAtIndex(0).FindPropertyRelative("Damage");
                if (first != null) damage = first.intValue;
            }

            if (boxes != null && boxes.isArray) boxes.ClearArray();

            var spawns = attack.FindPropertyRelative("Spawns");
            if (spawns == null || !spawns.isArray) return;

            spawns.ClearArray();
            spawns.InsertArrayElementAtIndex(0);

            var spawn = spawns.GetArrayElementAtIndex(0);
            var tick = spawn.FindPropertyRelative("Tick");
            if (tick != null) tick.intValue = launchTick;

            // Serialized inline on the spawn, so the fields are reachable by relative path.
            var bolt = spawn.FindPropertyRelative("Projectile");
            if (bolt == null) return;

            // EVERY field, explicitly.
            //
            // SerializedProperty.InsertArrayElementAtIndex creates a ZEROED element — C# field
            // initializers do not run for it. ProjectileDefinition declares LifetimeTicks = 120,
            // MaxTargets = 1 and StoppedByGeometry = true, and the bolt got 0, 0 and false: it
            // expired the tick it spawned. The caster fired, correctly, forever, and nothing ever
            // came out. Anything left unnamed here is silently zero, so nothing is left unnamed.
            Set(bolt, "Id", "caster_bolt");
            SetVector(bolt, "SpawnOffset", new Vector2(0.8f, 1.0f));
            SetFloat(bolt, "Speed", 9f);            // slow enough to be dodged on sight
            SetFloat(bolt, "Gravity", 0f);
            SetVector(bolt, "HalfExtents", new Vector2(0.25f, 0.25f));
            SetVector(bolt, "GrowthPerSecond", Vector2.zero);
            SetInt(bolt, "Damage", damage);
            SetInt(bolt, "Height", 0);              // AttackHeight.Any
            SetInt(bolt, "Defeats", 0);             // DefensiveAnswer.None — every answer works
            SetInt(bolt, "LifetimeTicks", 120);     // 2 seconds; the one that was killing it
            SetBool(bolt, "StoppedByGeometry", true);
            SetInt(bolt, "MaxTargets", 1);
        }

        private static void Set(SerializedProperty parent, string name, string value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.stringValue = value;
        }

        private static void SetBool(SerializedProperty parent, string name, bool value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.boolValue = value;
        }

        private static void SetInt(SerializedProperty parent, string name, int value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.intValue = value;
        }

        private static void SetFloat(SerializedProperty parent, string name, float value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.floatValue = value;
        }

        private static void SetVector(SerializedProperty parent, string name, Vector2 value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.vector2Value = value;
        }

        /// <summary>
        /// Pushes one attack's wind-up out, carrying everything measured from it along.
        ///
        /// <para>Hit-box windows and the cancel window are absolute tick offsets inside the attack,
        /// not relative to the phase they sit in. Change the startup without shifting them and the
        /// box opens during the wind-up — an attack whose telegraph is already dangerous, which is
        /// worse than no telegraph at all because it teaches the wrong lesson.</para>
        /// </summary>
        private static void SlowWindUp(SerializedProperty attack)
        {
            var startup = attack.FindPropertyRelative("StartupTicks");
            if (startup == null) return;

            int delta = EnemyStartupTicks - startup.intValue;
            if (delta == 0) return;

            startup.intValue = EnemyStartupTicks;

            var boxes = attack.FindPropertyRelative("HitBoxes");
            if (boxes != null && boxes.isArray)
            {
                for (int i = 0; i < boxes.arraySize; i++)
                {
                    var box = boxes.GetArrayElementAtIndex(i);
                    Shift(box.FindPropertyRelative("OpenTick"), delta);
                    Shift(box.FindPropertyRelative("CloseTick"), delta);
                }
            }

            var spawns = attack.FindPropertyRelative("Spawns");
            if (spawns != null && spawns.isArray)
            {
                for (int i = 0; i < spawns.arraySize; i++)
                    Shift(spawns.GetArrayElementAtIndex(i).FindPropertyRelative("Tick"), delta);
            }

            // Windows that describe "when during this attack", so they move with it. IFrames and
            // ParryWindow are authored at zero duration on these attacks; shifting a zero-length
            // window is harmless and keeps the rule uniform.
            ShiftWindow(attack.FindPropertyRelative("IFrames"), delta);
            ShiftWindow(attack.FindPropertyRelative("ParryWindow"), delta);
            ShiftWindow(attack.FindPropertyRelative("CancelWindow"), delta);
        }

        private static void Shift(SerializedProperty tick, int delta)
        {
            if (tick != null) tick.intValue = Mathf.Max(0, tick.intValue + delta);
        }

        private static void ShiftWindow(SerializedProperty window, int delta)
        {
            var start = window?.FindPropertyRelative("BaseStart");
            if (start != null && start.intValue > 0) start.intValue = Mathf.Max(0, start.intValue + delta);
        }
    }
}
