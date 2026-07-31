using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Reports the travel speed each locomotion clip was authored at, so
    /// <c>BodyAnimationSet.ReferenceSpeed</c> can be a measured number rather than a guess.
    ///
    /// <para><b>Why this cannot be read at runtime.</b> The clips in use are in-place variants —
    /// the character runs on the spot and something else moves them, which is exactly right for a
    /// simulation that owns position. The consequence is that their root barely moves, so
    /// <c>AnimationClip.averageSpeed</c> is near zero and there is no stride length to divide by.
    /// The number has to come from the RootMotion counterpart, which does travel.</para>
    ///
    /// <para>So this walks every clip it can find, pairs each in-place clip with its
    /// <c>_RootMotion_</c> sibling, and prints the sibling's speed. Measured once, typed into the
    /// asset, and then fixed — the clips are not going to change.</para>
    /// </summary>
    public static class ReferenceSpeedMeasurer
    {
        private const string SearchRoot = "Assets/_Prophecy/Animation";

        [MenuItem("Prophecy/Animation/Measure Reference Speeds", priority = 20)]
        public static void Measure()
        {
            var report = new StringBuilder();
            report.AppendLine("Reference speeds — the m/s each clip was authored to travel at.");
            report.AppendLine("Put these in BodyAnimationSet.ReferenceSpeed. Zero means 'do not scale'.");
            report.AppendLine();

            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { SearchRoot });

            if (guids.Length == 0)
            {
                Debug.LogWarning($"[Prophecy] No AnimationClips under {SearchRoot}.");
                return;
            }

            var rows = new List<(string name, float inPlace, float rootMotion, float length)>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview"))
                        continue;

                    float inPlace = clip.averageSpeed.magnitude;
                    float rootMotion = SpeedOfRootMotionSibling(path, clip.name);

                    rows.Add((clip.name, inPlace, rootMotion, clip.length));
                }
            }

            rows.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            report.AppendLine($"{"clip",-46} {"in-place",9} {"rootmotion",11} {"length",7}");
            report.AppendLine(new string('-', 78));

            int inPlaceCount = 0;

            foreach (var row in rows)
            {
                if (row.inPlace < 0.05f) inPlaceCount++;

                string rm = row.rootMotion > 0f ? row.rootMotion.ToString("F3") : "  —  ";
                report.AppendLine($"{row.name,-46} {row.inPlace,9:F3} {rm,11} {row.length,7:F2}");
            }

            report.AppendLine();
            report.AppendLine($"{inPlaceCount} of {rows.Count} clips are in-place (own speed under 0.05 m/s).");
            report.AppendLine("That is the expected shape: in-place clips are the ones to play, and");
            report.AppendLine("their RootMotion siblings are the ones that carry the measurement.");

            string outPath = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName, "Logs", "reference-speeds.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, report.ToString());

            Debug.Log($"[Prophecy] Measured {rows.Count} clips. Written to:\n{outPath}\n\n{report}");
        }

        /// <summary>
        /// The authored speed of the <c>_RootMotion_</c> variant of a clip, if it is in the project.
        /// Zero when there is none — which is the normal case here, since only the in-place clips
        /// were copied in.
        /// </summary>
        private static float SpeedOfRootMotionSibling(string path, string clipName)
        {
            // Synty's convention: A_Walk_FwdStrafeF_Masc -> A_Walk_FwdStrafeF_RootMotion_Masc.
            int lastUnderscore = clipName.LastIndexOf('_');
            if (lastUnderscore < 0) return 0f;

            string candidate = clipName.Insert(lastUnderscore, "_RootMotion");

            foreach (var guid in AssetDatabase.FindAssets($"{candidate} t:AnimationClip"))
            {
                string siblingPath = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(siblingPath))
                    if (asset is AnimationClip sibling && sibling.name == candidate)
                        return sibling.averageSpeed.magnitude;
            }

            return 0f;
        }
    }
}
