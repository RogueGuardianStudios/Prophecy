using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rokkan.Prophecy.Sim;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The gate, with teeth. Enforces the contract's core rule — <i>simulation is plain C# on a
    /// fixed tick and must run headless</i> — as a static property of the code rather than a
    /// convention people remember.
    ///
    /// <para>The distinction it draws is exactly the right one: <c>UnityEngine.Object</c> is the
    /// base of everything scene-coupled (GameObject, Component, Transform, MonoBehaviour,
    /// ScriptableObject, Material), while <c>Vector2</c>, <c>Mathf</c> and friends are plain
    /// value types with no engine state. So math is allowed and scene access is not, without
    /// anyone having to adjudicate case by case.</para>
    ///
    /// <para>A runtime "does it throw headless?" check would not work here — EditMode tests run
    /// inside a live Editor, where creating a GameObject succeeds. Reflection catches the
    /// coupling at the moment it is written instead of waiting for a headless build to fail.</para>
    /// </summary>
    public class SimArchitectureGateTests
    {
        private const string SimNamespace = "Rokkan.Prophecy.Sim";

        private static IEnumerable<Type> SimTypes =>
            typeof(CharacterSim).Assembly
                .GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.StartsWith(SimNamespace, StringComparison.Ordinal));

        private static bool IsEngineObject(Type t) =>
            typeof(UnityEngine.Object).IsAssignableFrom(t);

        [Test]
        public void SimNamespace_IsNotEmpty()
        {
            // Guards the gate itself: if the namespace were renamed, every test below would
            // vacuously pass over an empty set and the gate would silently stop guarding.
            Assert.IsNotEmpty(SimTypes.ToList(), $"no types found under {SimNamespace} — has it moved?");
        }

        [Test]
        public void NoSimType_DerivesFromAUnityObject()
        {
            var offenders = SimTypes.Where(IsEngineObject).Select(t => t.FullName).ToList();

            Assert.IsEmpty(offenders,
                "Sim types must be plain C#. A MonoBehaviour or ScriptableObject here cannot be " +
                "constructed in a headless test:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void NoSimType_HoldsAUnityObjectField()
        {
            var offenders = new List<string>();

            foreach (var type in SimTypes)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.Instance | BindingFlags.Static |
                                            BindingFlags.DeclaredOnly);
                foreach (var f in fields)
                {
                    if (IsEngineObject(f.FieldType))
                        offenders.Add($"{type.FullName}.{f.Name} : {f.FieldType.Name}");
                }
            }

            Assert.IsEmpty(offenders,
                "Sim state must not hold engine objects — a Transform or Animator reference is " +
                "exactly the coupling that makes a sim unrunnable headless:\n  " +
                string.Join("\n  ", offenders));
        }

        [Test]
        public void NoSimType_ExposesAUnityObjectProperty()
        {
            var offenders = new List<string>();

            foreach (var type in SimTypes)
            {
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.Static |
                                               BindingFlags.DeclaredOnly);
                foreach (var p in props)
                {
                    if (IsEngineObject(p.PropertyType))
                        offenders.Add($"{type.FullName}.{p.Name} : {p.PropertyType.Name}");
                }
            }

            Assert.IsEmpty(offenders,
                "Sim types must not expose engine objects:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void NoSimMethod_TakesOrReturnsAUnityObject()
        {
            var offenders = new List<string>();

            foreach (var type in SimTypes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static |
                                              BindingFlags.DeclaredOnly);
                foreach (var m in methods)
                {
                    if (m.IsSpecialName) continue; // property accessors, covered above

                    if (IsEngineObject(m.ReturnType))
                        offenders.Add($"{type.FullName}.{m.Name} returns {m.ReturnType.Name}");

                    foreach (var p in m.GetParameters())
                    {
                        if (IsEngineObject(p.ParameterType))
                            offenders.Add($"{type.FullName}.{m.Name}({p.Name} : {p.ParameterType.Name})");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "Sim API must not speak in engine objects — presentation reads sim state, never " +
                "the other way round:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Proves the gate can actually fail. A gate nobody has ever seen fail is indistinguishable
        /// from a gate that cannot fail, and this suite is the only thing standing between the
        /// project and a slow drift back into presentation-coupled gameplay.
        /// </summary>
        [Test]
        public void TheGateDetectsCoupling_WhenItExists()
        {
            Assert.IsTrue(IsEngineObject(typeof(UnityEngine.Transform)),
                "a Transform field would be caught");
            Assert.IsTrue(IsEngineObject(typeof(UnityEngine.MonoBehaviour)),
                "a MonoBehaviour base would be caught");
            Assert.IsFalse(IsEngineObject(typeof(UnityEngine.Vector2)),
                "plain math structs must remain allowed");
        }
    }
}
