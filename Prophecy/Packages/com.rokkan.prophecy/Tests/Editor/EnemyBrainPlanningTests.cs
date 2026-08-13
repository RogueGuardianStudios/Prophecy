using System.Collections.Generic;
using NUnit.Framework;
using RGS.GOAP.Core;
using RGS.GOAP.Core.Burst;
using RGS.GOAP.Core.Planning;
using Unity.Collections;
using UnityEditor;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// Proves the capsule enemy's brain can actually be planned over.
    ///
    /// <para><b>Why this is a test and not a play-mode check.</b> Every way this asset can be wrong
    /// fails silently and identically: an unmapped sensor output, a belief the registry drops, an
    /// action whose effect bit never gets set. From outside, all of them are a capsule walking its
    /// fallback patrol, and the planner's own message ("No action path found") does not say which
    /// bit is missing. The planner's graph builder is public and needs no scene, so the masks can be
    /// read directly — and the answer is the same every run.</para>
    ///
    /// <para>These assert the brain the generator produces, so a regression in
    /// <c>EnemyBuilder</c> fails here rather than in a play session.</para>
    /// </summary>
    [Category(GeneratedEnemyAssets.Category)]
    public sealed class EnemyBrainPlanningTests
    {
        [SetUp]
        public void RequireTheGeneratorToHaveRun() => GeneratedEnemyAssets.IgnoreIfAbsent();

        /// <summary>
        /// Every archetype's brain. Named rather than globbed so a brain that stops being generated
        /// fails here instead of quietly reducing the suite's coverage to whatever still exists.
        /// </summary>
        private static readonly string[] Archetypes = GeneratedEnemyAssets.Archetypes;

        private static string PathFor(string archetype) => GeneratedEnemyAssets.BrainPath(archetype);

        private static GoapBrainSO LoadBrain(string archetype = "Grunt")
        {
            string path = PathFor(archetype);
            var brain = AssetDatabase.LoadAssetAtPath<GoapBrainSO>(path);
            Assert.IsNotNull(brain, $"No brain at {path}. Run Prophecy → Build → Generate Enemies.");
            return brain;
        }

        /// <summary>Rebuilds the registry exactly as GoapPlanner.Initialize does.</summary>
        private static List<GoapBeliefInstance> BuildRegistry(GoapBrainSO brain)
        {
            var set = new HashSet<GoapBeliefInstance>();
            foreach (var state in brain.States)
                if (state != null)
                    GoapBrainBuilder.GatherBeliefs(state, set);

            var seen = new HashSet<RGS.GOAP.Core.Internal.SerializableGuid>();
            var registry = new List<GoapBeliefInstance>();
            foreach (var inst in set)
                if (inst != null && inst.IsValid && seen.Add(inst.InstanceId))
                    registry.Add(inst);

            return registry;
        }

        /// <summary>
        /// Every archetype must have a plannable idle.
        ///
        /// <para>The lowest-priority goal is the one that runs when nothing is happening, and it is
        /// the difference between an enemy that waits and one that spends its dormant life logging
        /// planning failures. An archetype whose only behaviour is conditional — an ambusher with
        /// nothing nearby — has to be able to plan <i>doing nothing</i>.</para>
        /// </summary>
        [Test]
        public void EveryArchetypeCanPlanItsIdle([ValueSource(nameof(Archetypes))] string archetype)
        {
            var brain = LoadBrain(archetype);
            var registry = BuildRegistry(brain);
            var state = brain.States[0];

            var idle = state.Goals.Find(g => g.Name == "Wander");
            Assert.IsNotNull(idle, $"{archetype} has no idle goal, so it plans nothing when alone.");

            var actions = GoapBrainBuilder.GetNativeActions(state, Allocator.Temp, registry);

            try
            {
                var goal = idle.Bake(registry);
                Assert.IsTrue(Satisfies(actions, goal),
                    $"{archetype}'s idle goal is unreachable — no action produces what it wants.");
            }
            finally
            {
                if (actions.IsCreated) actions.Dispose();
            }
        }

        /// <summary>True when any single action's effects meet the goal outright.</summary>
        private static bool Satisfies(NativeArray<BurstAction> actions, GoalData goal)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                if ((a.EffectsMask & goal.GoalMask) == goal.GoalMask &&
                    (a.EffectsValue & goal.GoalMask) == (goal.GoalValue & goal.GoalMask))
                    return true;
            }

            return false;
        }

        [Test]
        public void EveryArchetypeKeepsItsStrategiesBound([ValueSource(nameof(Archetypes))] string archetype)
        {
            var brain = LoadBrain(archetype);
            var state = brain.States[0];

            Assert.IsNotEmpty(state.Actions, $"{archetype} has no actions.");

            // A ScriptableObject sharing a file with another type serializes as m_Script: 0, loads
            // as null, and is dropped from the action graph without a word. That cost a long
            // debugging session once; it costs one assertion now.
            foreach (var action in state.Actions)
                Assert.IsNotNull(action.Strategy,
                    $"{archetype}'s '{action.Name}' has a null Strategy — the asset's script " +
                    "binding is broken, and the planner will silently drop the action.");
        }

        /// <summary>
        /// No goal may be satisfiable by what the sensors report alone.
        ///
        /// <para><b>A goal must be reachable, but not already reached.</b> Both halves have bitten:
        /// the grunt's Swing once required a sensor belief no action could produce, so no plan
        /// existed; the chaser then wanted a sensor belief that was <i>already</i> true on arrival,
        /// so the planner returned a zero-step plan, completed it the same frame, and replanned —
        /// every frame, at about a megabyte of log per second.</para>
        ///
        /// <para>The rule that rules both out: every goal must want at least one belief backed by a
        /// key no sensor writes, so reaching it always costs an action.</para>
        /// </summary>
        [Test]
        public void NoGoalIsSatisfiedBySensorStateAlone([ValueSource(nameof(Archetypes))] string archetype)
        {
            var brain = LoadBrain(archetype);
            var state = brain.States[0];

            var sensorWritten = new HashSet<string>();
            foreach (var sensor in brain.Sensors)
            {
                if (sensor == null) continue;
                foreach (var output in sensor.GetOutputs()) sensorWritten.Add(output.Name);
            }

            foreach (var goal in state.Goals)
            {
                bool needsAnAction = false;

                foreach (var condition in goal.DesiredState)
                {
                    string key = KeyNameOf(brain, condition.Belief);
                    if (key != null && !sensorWritten.Contains(key)) { needsAnAction = true; break; }
                }

                Assert.IsTrue(needsAnAction,
                    $"{archetype}'s goal '{goal.Name}' wants only things the sensors report, so the " +
                    "world can satisfy it with no action at all — that is a zero-step plan every " +
                    "frame, not a behaviour.");
            }
        }

        /// <summary>The blackboard key a belief reads, by name, or null if it is not key-backed.</summary>
        private static string KeyNameOf(GoapBrainSO brain, GoapBeliefInstance belief)
        {
            switch (belief?.DefaultSettings)
            {
                case BoolCheckSettings b: return brain.GetKeyName(b.TargetKeyId);
                case SimpleCompareSettings c: return brain.GetKeyName(c.TargetKeyId);
                default: return null;
            }
        }

        /// <summary>
        /// The caster's bolt has to be able to exist.
        ///
        /// <para><b>A projectile that cannot work still fires.</b> The attack plays, the planner is
        /// satisfied, the press is logged — and nothing comes out, because the bolt expired on the
        /// tick it spawned. That is what a zero lifetime does, and it is what a generator gets when
        /// it builds a serialized array element and trusts the class's field initializers:
        /// <c>InsertArrayElementAtIndex</c> zeroes the element instead of running them.</para>
        /// </summary>
        [Test]
        public void TheCastersProjectilesCanActuallyExist()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<Rokkan.Prophecy.Core.CombatTuning>(
                GeneratedEnemyAssets.CasterTuningPath);

            Assert.IsNotNull(tuning, "No caster tuning. Run Prophecy → Build → Generate Enemies.");

            int spawns = 0;

            foreach (var attack in tuning.Data.Attacks)
            {
                if (attack?.Spawns == null) continue;

                for (int i = 0; i < attack.Spawns.Length; i++)
                {
                    var projectile = attack.Spawns[i].Projectile;
                    Assert.IsNotNull(projectile, $"'{attack.Id}' spawn {i} has no projectile.");

                    Assert.IsNull(projectile.Validate(),
                        $"'{attack.Id}' spawn {i}: {projectile.Validate()}");

                    spawns++;
                }
            }

            Assert.Greater(spawns, 0,
                "The caster's moveset spawns nothing — it is a melee enemy with no reach.");
        }

        [Test]
        public void EveryBeliefTheBrainUsesSurvivesIntoTheRegistry()
        {
            var brain = LoadBrain();
            var registry = BuildRegistry(brain);

            // IsValid is BeliefSO != null && DefaultSettings != null, and a belief that fails it is
            // dropped without a word — taking its bit, and any plan that needed it, with it.
            Assert.IsNotEmpty(registry, "The belief registry is empty; nothing can be planned.");

            foreach (var state in brain.States)
                foreach (var action in state.Actions)
                    foreach (var effect in action.Effects)
                        Assert.IsTrue(effect.Belief != null && effect.Belief.IsValid,
                                      $"Action '{action.Name}' has an effect whose belief is null or invalid.");
        }

        [Test]
        public void ThePatrolActionAdvertisesTheBitTheWanderGoalWants()
        {
            var brain = LoadBrain();
            var registry = BuildRegistry(brain);
            var state = brain.States[0];

            var actions = GoapBrainBuilder.GetNativeActions(state, Allocator.Temp, registry);

            try
            {
                Assert.IsNotEmpty(state.Actions, "The state has no actions at all.");
                Assert.AreEqual(state.Actions.Count, actions.Length,
                                "An action was dropped from the native graph — its Strategy is probably null.");

                var wander = state.Goals.Find(g => g.Name == "Wander");
                Assert.IsNotNull(wander, "No 'Wander' goal on the state.");

                var goal = wander.Bake(registry);

                // The whole question, in one assertion: does anything the planner can do produce
                // the world state the goal is asking for?
                bool reachable = false;
                for (int i = 0; i < actions.Length; i++)
                {
                    var a = actions[i];
                    if ((a.EffectsMask & goal.GoalMask) == goal.GoalMask &&
                        (a.EffectsValue & goal.GoalMask) == (goal.GoalValue & goal.GoalMask))
                    {
                        reachable = true;
                        break;
                    }
                }

                Assert.IsTrue(reachable,
                    "No action's effects satisfy the Wander goal. The goal wants a bit that no " +
                    "action sets — usually because the belief behind it was dropped from the " +
                    "registry, so the goal and the effect were assigned different bit indices.");
            }
            finally
            {
                if (actions.IsCreated) actions.Dispose();
            }
        }
    }

    /// <summary>
    /// The gate for the <c>GeneratedAssetAudit</c> fixtures. They read what
    /// <c>Prophecy → Build → Generate Enemies</c> writes, so on a tree where the generator has
    /// never run every one of them is red for the same uninteresting reason — and the wall of
    /// failures says nothing about what to do. Absent assets therefore skip with instructions;
    /// assets that are PRESENT but miswired stay hard failures, because that is the regression
    /// these fixtures exist to catch.
    /// </summary>
    internal static class GeneratedEnemyAssets
    {
        public const string Category = "GeneratedAssetAudit";

        /// <summary>Every archetype the generator emits. Named rather than globbed so a brain
        /// that stops being generated fails loudly instead of quietly shrinking coverage.</summary>
        public static readonly string[] Archetypes = { "Grunt", "Chaser", "Ambusher", "Caster", "Wanderer" };

        public const string CasterTuningPath = "Assets/_Prophecy/Data/CombatTuning_Caster.asset";

        public static string BrainPath(string archetype) =>
            $"Assets/_Prophecy/Data/Enemies/Brain_{archetype}.asset";

        /// <summary>
        /// Skip the calling test when NONE of the generated assets exist — the generator simply
        /// has not run on this tree. Any single asset present means it has, and from there a
        /// missing sibling is a generator regression the named assertions must report.
        /// </summary>
        public static void IgnoreIfAbsent()
        {
            foreach (var archetype in Archetypes)
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BrainPath(archetype)) != null)
                    return;

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(CasterTuningPath) != null)
                return;

            Assert.Ignore(
                "Generated enemy assets are absent — run Prophecy > Build > Generate Enemies, " +
                "then re-run.");
        }
    }
}
