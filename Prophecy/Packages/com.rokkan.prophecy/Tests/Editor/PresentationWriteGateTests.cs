using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The other half of the split. <c>SimArchitectureGateTests</c> guards sim→engine; nothing
    /// guarded presentation→sim, and sim state is publicly mutable — any of the presentation
    /// classes could write <c>sim.State.Velocity</c> with no compile-time or gate-time
    /// resistance, and once did (the arts volume casting from UI code).
    ///
    /// <para><b>A source scan, honestly limited.</b> It catches direct writes through a
    /// <c>.State.</c> or sanctioned-member path; a write laundered through a local alias
    /// (<c>var s = sim.State; s.X = …</c>) slips it. That gap is accepted: the pattern this
    /// gate exists to stop is the casual convenience write, which is always direct. The
    /// structural fix — sim state internal to its own assembly — is the recorded follow-up.</para>
    /// </summary>
    public class PresentationWriteGateTests
    {
        private static readonly string[] ScannedFolders =
        {
            "Packages/com.rokkan.prophecy/Runtime/Presentation",
            "Packages/com.rokkan.prophecy/Runtime/World",
            "Packages/com.rokkan.prophecy/Runtime/Overworld",
            "Packages/com.rokkan.prophecy/Runtime/Goap",
        };

        // The sanctioned writes, each named with its reason. A new entry here is a design
        // decision — it should arrive with the same scrutiny these three did.
        private static readonly (string File, string MustContain, string Why)[] Sanctioned =
        {
            ("PlayerCharacterHost.cs", ".State.Space",
             "the host assembles its sim and owns which plane it plays in"),
            ("EnemyBrainHost.cs", ".State.Team",
             "identity seeding — who the body fights for is wiring, not gameplay"),
            ("ArtsVolumeMenu.cs", ".EquippedArt",
             "equipping is a loadout edit, sanctioned by InputFrame's doc; casts go through RequestCast"),
        };

        [Test]
        public void PresentationNeverWritesSimState()
        {
            var writes = new Regex(@"\.(State\.[A-Za-z_]\w*|EquippedArt|HitStunTicks)\s*=(?!=)");
            var offenders = new List<string>();

            foreach (var folder in ScannedFolders)
            {
                string root = Path.GetFullPath(folder);
                if (!Directory.Exists(root)) continue;

                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(file);
                    var lines = File.ReadAllLines(file);

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                            trimmed.StartsWith("*", StringComparison.Ordinal)) continue;

                        if (!writes.IsMatch(lines[i])) continue;

                        bool sanctioned = false;
                        foreach (var allow in Sanctioned)
                        {
                            if (name == allow.File && lines[i].Contains(allow.MustContain))
                            {
                                sanctioned = true;
                                break;
                            }
                        }

                        if (!sanctioned) offenders.Add($"{name}:{i + 1}  {trimmed}");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "MonoBehaviours read sim state and capture input; they never decide gameplay " +
                "outcomes. A menu or view that needs the sim to DO something parks a request " +
                "(CharacterSim.RequestCast is the pattern) and lets a tick consume it:\n  " +
                string.Join("\n  ", offenders));
        }
    }
}
