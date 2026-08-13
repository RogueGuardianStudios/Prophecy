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
        /// The deep check: an engine object hiding inside a container is the same coupling as
        /// one held directly. A <c>List&lt;Transform&gt;</c> passed the direct checks because the
        /// list type itself is not a <c>UnityEngine.Object</c> — precisely the shape drift
        /// arrives in once the obvious shapes are gated.
        /// </summary>
        private static bool SmugglesEngineObject(Type t, int depth = 0)
        {
            if (t == null || depth > 4) return false;
            if (IsEngineObject(t)) return true;
            if (t.IsArray && SmugglesEngineObject(t.GetElementType(), depth + 1)) return true;

            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    if (SmugglesEngineObject(arg, depth + 1)) return true;
            }

            return false;
        }

        [Test]
        public void NoSimType_SmugglesAnEngineObjectInsideAContainer()
        {
            var offenders = new List<string>();

            foreach (var type in SimTypes)
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static |
                                                 BindingFlags.DeclaredOnly))
                {
                    if (SmugglesEngineObject(f.FieldType))
                        offenders.Add($"{type.FullName}.{f.Name} : {f.FieldType.Name}");
                }

                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                     BindingFlags.Instance | BindingFlags.Static |
                                                     BindingFlags.DeclaredOnly))
                {
                    if (SmugglesEngineObject(p.PropertyType))
                        offenders.Add($"{type.FullName}.{p.Name} : {p.PropertyType.Name}");
                }
            }

            Assert.IsEmpty(offenders,
                "A List<Transform> is a Transform field wearing a coat:\n  " +
                string.Join("\n  ", offenders));
        }

        [Test]
        public void NoSimConstructor_TakesAUnityObject()
        {
            // GetMethods never returns constructors, so a sim type could be handed an Animator
            // at birth and read it without ever storing it — invisible to every check above.
            var offenders = new List<string>();

            foreach (var type in SimTypes)
            {
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic |
                                                          BindingFlags.Instance))
                {
                    foreach (var p in ctor.GetParameters())
                    {
                        if (SmugglesEngineObject(p.ParameterType))
                            offenders.Add($"{type.FullName}..ctor({p.Name} : {p.ParameterType.Name})");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "Sim constructors must not take engine objects:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void NoSimType_KeepsMutableStaticState()
        {
            // A static mutable field is shared across every character and every test — the
            // determinism hazard that has nothing to do with UnityEngine.Object and so slips
            // every check above. Constants and readonly immutables are fine; a writable static
            // is state the replay cannot see.
            var offenders = new List<string>();

            foreach (var type in SimTypes)
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral || f.IsInitOnly) continue;
                    if (f.Name.StartsWith("<", StringComparison.Ordinal)) continue; // compiler-generated

                    offenders.Add($"{type.FullName}.{f.Name} : {f.FieldType.Name}");
                }
            }

            Assert.IsEmpty(offenders,
                "Sim types must not keep writable static state — it is invisible to a replay " +
                "and shared by every character:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void GroundProviders_ObeyTheSimRules_WhereverTheyLive()
        {
            // ITopDownGround implementors are handed to the sim as sim.Ground and ticked inside
            // it — they ARE sim code, whatever namespace they were authored in. The Overworld
            // assembly's providers sat outside the scanned namespace for exactly that reason.
            var offenders = new List<string>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    if (type.IsInterface) continue;
                    if (!typeof(ITopDownGround).IsAssignableFrom(type)) continue;

                    foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                     BindingFlags.Instance | BindingFlags.Static |
                                                     BindingFlags.DeclaredOnly))
                    {
                        if (SmugglesEngineObject(f.FieldType))
                            offenders.Add($"{type.FullName}.{f.Name} : {f.FieldType.Name}");
                    }

                    foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                         BindingFlags.Instance | BindingFlags.Static |
                                                         BindingFlags.DeclaredOnly))
                    {
                        if (SmugglesEngineObject(p.PropertyType))
                            offenders.Add($"{type.FullName}.{p.Name} : {p.PropertyType.Name}");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "Anything the sim ticks must obey the sim's rules:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void NoSimSource_ReadsFrameTimeOrLivePhysics()
        {
            // "Never Time.deltaTime" is a headline rule of the contract, and reflection cannot
            // see a static API CALL — only source can. Comment lines are skipped so prose about
            // the rule does not trip the rule.
            string root = System.IO.Path.GetFullPath("Packages/com.rokkan.prophecy/Runtime/Sim");
            Assert.IsTrue(System.IO.Directory.Exists(root), $"sim source not found at {root}");

            var forbidden = new System.Text.RegularExpressions.Regex(
                @"\b(Time|Physics|Physics2D)\s*\.");
            var offenders = new List<string>();

            foreach (var file in System.IO.Directory.GetFiles(root, "*.cs",
                                                              System.IO.SearchOption.AllDirectories))
            {
                var lines = System.IO.File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                        trimmed.StartsWith("*", StringComparison.Ordinal)) continue;

                    if (forbidden.IsMatch(lines[i]))
                        offenders.Add($"{System.IO.Path.GetFileName(file)}:{i + 1}  {trimmed}");
                }
            }

            Assert.IsEmpty(offenders,
                "Sim code reads SimTickInfo and its own CollisionWorld, never frame time or " +
                "live physics:\n  " + string.Join("\n  ", offenders));
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
            Assert.IsTrue(SmugglesEngineObject(typeof(List<UnityEngine.Transform>)),
                "a List<Transform> would be caught");
            Assert.IsTrue(SmugglesEngineObject(typeof(UnityEngine.Transform[])),
                "a Transform[] would be caught");
            Assert.IsFalse(SmugglesEngineObject(typeof(List<UnityEngine.Vector2>)),
                "a List<Vector2> must remain allowed");
        }
    }
}
