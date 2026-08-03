using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Strategies;
using UnityEngine;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// One enemy must never be able to reach another enemy's state.
    ///
    /// <para><b>The trap this closes.</b> A GOAP strategy and a GOAP sensor are
    /// <c>ScriptableObject</c>s, so there is exactly one instance of each for the whole game —
    /// every enemy running "Swing" is running the same object. A field on that object is a global
    /// variable wearing a member's clothing. It cost a caster that never fired: the grunt stamped
    /// its swing tick on the shared strategy, the caster read it, concluded it had already
    /// attacked, and skipped the shot. No error, no warning, no failed plan — an enemy that simply
    /// never did the thing it was planning to do.</para>
    ///
    /// <para><b>Why a test rather than a convention.</b> The failure is invisible with one enemy in
    /// the scene and only appears once two of the same kind exist, which is the point at which it
    /// is hardest to attribute. A structural check fails the moment the field is written, in the
    /// suite, next to the reason.</para>
    ///
    /// <para>Serialized fields are exempt on purpose: those are authored configuration, identical
    /// for every agent by design and never written at runtime. Per-agent working state belongs on
    /// <c>EnemyBrainHost.ActionScratch</c>, which exists once per enemy.</para>
    /// </summary>
    public sealed class StrategyIsolationGateTests
    {
        /// <summary>Every shared GOAP asset type Prophecy defines.</summary>
        private static IEnumerable<Type> SharedAssetTypes()
        {
            var assembly = typeof(Rokkan.Prophecy.Goap.SwingStrategy).Assembly;

            return assembly.GetTypes()
                .Where(t => !t.IsAbstract)
                .Where(t => typeof(BaseGoapActionStrategy).IsAssignableFrom(t) ||
                            typeof(GoapSensorSO).IsAssignableFrom(t))
                .OrderBy(t => t.Name);
        }

        [Test]
        public void NoSharedGoapAssetCarriesPerAgentState(
            [ValueSource(nameof(SharedAssetTypes))] Type type)
        {
            var offenders = new List<string>();

            // Declared only: a base class is checked by its own case, and inherited Unity fields
            // (hideFlags and friends) are not ours to answer for.
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsInitOnly) continue;                                  // readonly: cannot drift
                if (field.IsDefined(typeof(SerializeField), inherit: true)) continue;  // authored config
                if (field.IsPublic) continue;                                    // authored config
                if (field.IsDefined(typeof(NonSerializedAttribute), inherit: true) &&
                    field.Name.StartsWith("<")) continue;                        // compiler backing

                offenders.Add(field.Name);
            }

            Assert.IsEmpty(offenders,
                $"{type.Name} keeps mutable state in {string.Join(", ", offenders)}. It is a " +
                "ScriptableObject — one instance shared by every agent — so that field is common " +
                "to all of them: one enemy's attack would drive another's, and a cooldown started " +
                "by one would be observed by the next. Move it to EnemyBrainHost.ActionScratch, " +
                "which exists once per enemy.");
        }

        [Test]
        public void TheHostOwnsPerAgentScratch()
        {
            // The counterpart to the rule above: the place state is supposed to live must be an
            // instance member of a per-agent component, not a static of any kind.
            var scratch = typeof(Rokkan.Prophecy.Presentation.EnemyBrainHost)
                .GetProperty("Scratch", BindingFlags.Instance | BindingFlags.Public);

            Assert.IsNotNull(scratch,
                "EnemyBrainHost.Scratch is gone. Strategies keep their per-agent working state " +
                "there precisely because the host is per-agent and the strategy asset is not.");

            Assert.IsFalse(scratch.PropertyType.IsAbstract && scratch.PropertyType.IsSealed,
                "Scratch must not be a static holder — that would reintroduce the shared state it exists to avoid.");
        }
    }
}
