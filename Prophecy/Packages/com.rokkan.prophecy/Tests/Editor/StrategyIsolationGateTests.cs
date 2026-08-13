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

        /// <summary>The settings classes ride the same shared assets — a brain authored once is
        /// run by every enemy of its kind, so a PatrolSettings is exactly as global as the
        /// strategy holding it.</summary>
        private static IEnumerable<Type> SharedSettingsTypes()
        {
            var assembly = typeof(Rokkan.Prophecy.Goap.SwingStrategy).Assembly;

            return assembly.GetTypes()
                .Where(t => !t.IsAbstract)
                .Where(t => typeof(BaseStrategySettings).IsAssignableFrom(t))
                .OrderBy(t => t.Name);
        }

        /// <summary>Types whose readonly reference still lets contents drift between agents —
        /// a <c>readonly List</c> is shared mutable state wearing a readonly modifier.</summary>
        private static bool IsMutableCollection(Type t)
        {
            if (t.IsArray) return true;
            if (!t.IsGenericType) return false;

            var open = t.GetGenericTypeDefinition();
            return open == typeof(List<>) || open == typeof(Dictionary<,>) ||
                   open == typeof(HashSet<>) || open == typeof(Queue<>) || open == typeof(Stack<>);
        }

        private static List<string> PerAgentStateOffenders(Type type)
        {
            var offenders = new List<string>();

            // Declared only: a base class is checked by its own case, and inherited Unity fields
            // (hideFlags and friends) are not ours to answer for.
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // readonly cannot be re-pointed — but a readonly collection's CONTENTS drift
                // exactly like a plain field, which is how the original bug would come back
                // wearing a modifier.
                if (field.IsInitOnly)
                {
                    if (IsMutableCollection(field.FieldType))
                        offenders.Add($"{field.Name} (readonly {field.FieldType.Name} — its contents are still shared)");
                    continue;
                }

                // Public and [SerializeField] fields are AUTHORED CONFIG by convention — the
                // inspector writes them, runtime code must not. Nothing enforces that second
                // half; it is the one exemption this gate takes on trust.
                if (field.IsDefined(typeof(SerializeField), inherit: true)) continue;
                if (field.IsPublic) continue;
                if (field.IsDefined(typeof(NonSerializedAttribute), inherit: true) &&
                    field.Name.StartsWith("<")) continue;                        // compiler backing

                offenders.Add(field.Name);
            }

            return offenders;
        }

        [Test]
        public void NoSharedGoapAssetCarriesPerAgentState(
            [ValueSource(nameof(SharedAssetTypes))] Type type)
        {
            var offenders = PerAgentStateOffenders(type);

            Assert.IsEmpty(offenders,
                $"{type.Name} keeps mutable state in {string.Join(", ", offenders)}. It is a " +
                "ScriptableObject — one instance shared by every agent — so that field is common " +
                "to all of them: one enemy's attack would drive another's, and a cooldown started " +
                "by one would be observed by the next. Move it to EnemyBrainHost.ActionScratch, " +
                "which exists once per enemy.");
        }

        [Test]
        public void NoSharedSettingsClassCarriesPerAgentState(
            [ValueSource(nameof(SharedSettingsTypes))] Type type)
        {
            var offenders = PerAgentStateOffenders(type);

            Assert.IsEmpty(offenders,
                $"{type.Name} keeps mutable state in {string.Join(", ", offenders)}. Settings " +
                "ride the shared brain asset — one instance per authored brain, run by every " +
                "enemy of its kind — so per-agent state here is the same trap as on the strategy.");
        }

        [Test]
        public void NoSharedGoapAssetKeepsStaticState(
            [ValueSource(nameof(SharedAssetTypes))] Type type)
        {
            // Statics were not scanned at all — and a static is MORE global than the instance
            // field that caused the original caster-never-fired bug, with no authored-config
            // excuse available: config lives on the instance the inspector serializes.
            var offenders = new List<string>();

            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public |
                                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral) continue;
                if (field.IsInitOnly && !IsMutableCollection(field.FieldType)) continue;

                offenders.Add($"{field.Name} : {field.FieldType.Name}");
            }

            Assert.IsEmpty(offenders,
                $"{type.Name} keeps static state in {string.Join(", ", offenders)} — shared " +
                "across every agent AND every brain. There is no authored-config excuse for a " +
                "static; whatever this is, it belongs on the host.");
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
