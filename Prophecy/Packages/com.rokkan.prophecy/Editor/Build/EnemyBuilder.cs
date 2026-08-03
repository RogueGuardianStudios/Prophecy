using System.Collections.Generic;
using RGS.GOAP.Core;
using GoapGuid = RGS.GOAP.Core.Internal.SerializableGuid;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Goap;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using UnityEditor;
using UnityEngine;

namespace Rokkan.Prophecy.Editor
{
    /// <summary>
    /// Builds a capsule enemy: the GOAP brain, its sensors and beliefs, and a prefab that runs it.
    ///
    /// <para><b>Authored from script on purpose.</b> The GOAP Hub window is a debugger first, and
    /// the package was written to be driven programmatically — so generating a brain in code is the
    /// intended workflow rather than a way round the tool, and exercising it is part of proving the
    /// asset is game-ready (HANDOFF decision 36).</para>
    ///
    /// <para>Idempotent, like every other generator here: re-running replaces its own output rather
    /// than stacking a second brain beside the first. Retune a number, regenerate, and the enemy
    /// stays honest.</para>
    /// </summary>
    public static class EnemyBuilder
    {
        private const string Folder = "Assets/_Prophecy/Data/Enemies";
        private const string PrefabPath = "Assets/_Prophecy/Prefabs/Enemy_Capsule.prefab";

        // Blackboard key names. Sensors write these; beliefs read them.
        private const string KeyHasTarget = ProphecyTargetSensor.HasTarget;
        private const string KeyCanSee = ProphecyTargetSensor.CanSeeTarget;
        private const string KeyDistanceX = ProphecyTargetSensor.TargetDistanceX;
        private const string KeyBlocked = ProphecyTerrainSensor.BlockedAhead;

        [MenuItem("Prophecy/Build/Generate Capsule Enemy", priority = 40)]
        public static void Generate()
        {
            EnsureFolder();

            var boolCheck = CreateOrReplace<GoapBeliefBoolCheck>($"{Folder}/Belief_BoolCheck.asset");
            var compare = CreateOrReplace<GoapBeliefSimpleCompare>($"{Folder}/Belief_Compare.asset");

            var targetSensor = CreateOrReplace<ProphecyTargetSensor>($"{Folder}/Sensor_Target.asset");
            var terrainSensor = CreateOrReplace<ProphecyTerrainSensor>($"{Folder}/Sensor_Terrain.asset");

            var patrol = CreateOrReplace<PatrolStrategy>($"{Folder}/Action_Patrol.asset");
            var pursue = CreateOrReplace<PursueStrategy>($"{Folder}/Action_Pursue.asset");
            var swing = CreateOrReplace<SwingStrategy>($"{Folder}/Action_Swing.asset");

            var brain = BuildBrain(brainPath: $"{Folder}/Brain_CapsuleGrunt.asset",
                                   boolCheck, compare,
                                   targetSensor, terrainSensor,
                                   patrol, pursue, swing);

            BuildPrefab(brain);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Prophecy] Capsule enemy generated.\n  brain : {AssetDatabase.GetAssetPath(brain)}" +
                      $"\n  prefab: {PrefabPath}");
        }

        // ---------------------------------------------------------------- brain

        private static GoapBrainSO BuildBrain(string brainPath,
                                              GoapBeliefBoolCheck boolCheck,
                                              GoapBeliefSimpleCompare compare,
                                              ProphecyTargetSensor targetSensor,
                                              ProphecyTerrainSensor terrainSensor,
                                              PatrolStrategy patrol, PursueStrategy pursue,
                                              SwingStrategy swing)
        {
            var brain = CreateOrReplace<GoapBrainSO>(brainPath);

            brain.Keys.Clear();
            brain.States.Clear();
            brain.Sensors.Clear();
            brain.Transitions.Clear();

            var hasTarget = AddKey(brain, KeyHasTarget, GoapKeyType.Boolean);
            var canSee = AddKey(brain, KeyCanSee, GoapKeyType.Boolean);
            var distanceX = AddKey(brain, KeyDistanceX, GoapKeyType.Float);
            var blocked = AddKey(brain, KeyBlocked, GoapKeyType.Boolean);

            // Planner-space keys. Nothing writes these — they exist so an action can advertise a
            // world state a goal wants, which is how A* finds its way from "patrolling" to "swung".
            var inReach = AddKey(brain, "InReach", GoapKeyType.Boolean);
            var struck = AddKey(brain, "TargetStruck", GoapKeyType.Boolean);
            var patrolled = AddKey(brain, "Patrolling", GoapKeyType.Boolean);

            brain.Sensors.Add(targetSensor);
            brain.Sensors.Add(terrainSensor);

            // ---- beliefs
            var seesTarget = Bool(boolCheck, canSee, "SeesTarget");
            var hasAnyTarget = Bool(boolCheck, hasTarget, "HasTarget");
            var withinReach = Compare(compare, distanceX, ComparisonType.LessThanOrEqual, 1.4f, "WithinReach");
            var wasStruck = Bool(boolCheck, struck, "TargetStruck");
            var isPatrolling = Bool(boolCheck, patrolled, "Patrolling");
            var reachedTarget = Bool(boolCheck, inReach, "InReach");

            // ---- actions. Preconditions are what must hold; effects are what the planner may
            //      assume afterwards. The chain patrol -> pursue -> swing falls out of these.
            var patrolAction = Action("Patrol", patrol, cost: 5f,
                effects: new[] { On(isPatrolling) });

            var pursueAction = Action("Pursue", pursue, cost: 2f,
                preconditions: new[] { On(seesTarget) },
                effects: new[] { On(reachedTarget) });

            var swingAction = Action("Swing", swing, cost: 1f,
                preconditions: new[] { On(reachedTarget), On(withinReach) },
                effects: new[] { On(wasStruck) });

            // ---- goals. Striking outranks wandering, and is only considered when there is
            //      something to strike — so an enemy with nothing in sight plans a patrol instead
            //      of failing to plan at all.
            var killGoal = Goal("StrikeTarget", priority: 10f,
                desired: new[] { On(wasStruck) },
                validity: new[] { On(hasAnyTarget) });

            var patrolGoal = Goal("Wander", priority: 1f,
                desired: new[] { On(isPatrolling) });

            var state = new GoapBehavioralState
            {
                Name = "Grunt",
                StateId = GoapGuid.NewGuid(),
            };

            state.Actions.Add(patrolAction);
            state.Actions.Add(pursueAction);
            state.Actions.Add(swingAction);
            state.Goals.Add(killGoal);
            state.Goals.Add(patrolGoal);

            state.Beliefs.Add(seesTarget);
            state.Beliefs.Add(hasAnyTarget);
            state.Beliefs.Add(withinReach);
            state.Beliefs.Add(wasStruck);
            state.Beliefs.Add(isPatrolling);
            state.Beliefs.Add(reachedTarget);

            brain.States.Add(state);
            brain.DefaultStateId = state.StateId;

            EditorUtility.SetDirty(brain);
            return brain;
        }

        // ---------------------------------------------------------------- helpers

        private static GoapGuid AddKey(GoapBrainSO brain, string name, GoapKeyType type)
        {
            var key = new GoapKey { Name = name, Type = type, KeyId = GoapGuid.NewGuid() };
            brain.Keys.Add(key);
            return key.KeyId;
        }

        private static GoapBeliefInstance Bool(GoapBeliefBoolCheck so, GoapGuid key, string name)
        {
            var instance = GoapBeliefInstance.Create(so);
            instance.name = name;
            instance.DefaultSettings = new BoolCheckSettings { TargetKeyId = key, Invert = false };
            return instance;
        }

        private static GoapBeliefInstance Compare(GoapBeliefSimpleCompare so, GoapGuid key,
                                                  ComparisonType comparison, float value, string name)
        {
            var instance = GoapBeliefInstance.Create(so);
            instance.name = name;
            instance.DefaultSettings = new SimpleCompareSettings
            {
                TargetKeyId = key,
                Comparison = comparison,
                UseConstantValue = true,
                ConstantValue = value,
            };
            return instance;
        }

        private static NodeCondition On(GoapBeliefInstance belief, bool value = true) =>
            new NodeCondition { Belief = belief, TargetValue = value };

        private static GoapActionInstance Action(string name, RGS.GOAP.Core.Strategies.BaseGoapActionStrategy strategy,
                                                 float cost,
                                                 NodeCondition[] preconditions = null,
                                                 NodeCondition[] effects = null)
        {
            var action = new GoapActionInstance
            {
                InstanceId = GoapGuid.NewGuid(),
                Name = name,
                Strategy = strategy,
                Cost = cost,
            };

            if (preconditions != null) action.Preconditions.AddRange(preconditions);
            if (effects != null) action.Effects.AddRange(effects);

            return action;
        }

        private static GoapGoalInstance Goal(string name, float priority,
                                             NodeCondition[] desired,
                                             NodeCondition[] validity = null)
        {
            var goal = new GoapGoalInstance
            {
                InstanceId = GoapGuid.NewGuid(),
                Name = name,
                Priority = priority,
            };

            goal.DesiredState.AddRange(desired);
            if (validity != null) goal.ValidityConditions.AddRange(validity);

            return goal;
        }

        // ---------------------------------------------------------------- prefab

        private static void BuildPrefab(GoapBrainSO brain)
        {
            var root = new GameObject("Enemy_Capsule");

            try
            {
                // A capsule, for now. The same proxy the player wore until it had a model, and for
                // the same reason: the interesting thing here is the behaviour, not the silhouette.
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
                Object.DestroyImmediate(body.GetComponent<Collider>());   // the sim owns collision

                var host = root.AddComponent<PlayerCharacterHost>();
                var view = root.AddComponent<CharacterView>();
                var combatant = root.AddComponent<Combatant>();
                var brainHost = root.AddComponent<EnemyBrainHost>();

                Wire(host, "_tuning", Load<MovementTuning>("Assets/_Prophecy/Data/MovementTuning.asset"));
                Wire(host, "_combatTuning", Load<CombatTuning>("Assets/_Prophecy/Data/CombatTuning.asset"));
                Wire(host, "_bakeOnStart", false);   // a SceneDirector owns the world

                Wire(view, "_host", host);
                Wire(view, "_body", body.transform);
                Wire(view, "_resizeProxyOnCrouch", true);   // it is a capsule; squashing reads fine

                Wire(combatant, "_team", 2);
                Wire(combatant, "_contactDamage", 0);

                Wire(brainHost, "_host", host);
                Wire(brainHost, "_team", 2);

                AttachGoap(root, brain);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Add the GOAP agent, its context and its sensor controller.
        ///
        /// <para>Added by reflection over the component types rather than referenced directly,
        /// because this assembly must not take a hard dependency on the GOAP package — the editor
        /// assembly is shared with generators that have nothing to do with AI.</para>
        /// </summary>
        private static void AttachGoap(GameObject root, GoapBrainSO brain)
        {
            var context = root.AddComponent<GoapAgentContext>();
            var sensors = root.AddComponent<SensorController>();
            var agent = root.AddComponent<GoapAgent>();

            Wire(agent, "_brain", brain);
            Wire(context, "Agent", agent);

            // The sensor list is authored per agent, not on the brain, so an enemy can carry a
            // subset. Both of ours are wanted.
            var list = new List<GoapSensorSO>(brain.Sensors);
            Wire(sensors, "Sensors", list);
        }

        // ---------------------------------------------------------------- plumbing

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Prophecy/Data"))
                AssetDatabase.CreateFolder("Assets/_Prophecy", "Data");

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Prophecy/Data", "Enemies");
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        private static T CreateOrReplace<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        /// <summary>Set a private serialized field by name, so generators need no public setters.</summary>
        private static void Wire(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogWarning($"[Prophecy] {target.GetType().Name} has no field '{field}'.");
                return;
            }

            switch (value)
            {
                case null: property.objectReferenceValue = null; break;
                case bool b: property.boolValue = b; break;
                case int i: property.intValue = i; break;
                case float f: property.floatValue = f; break;
                case Object o: property.objectReferenceValue = o; break;
                case IList<GoapSensorSO> list:
                    property.arraySize = list.Count;
                    for (int n = 0; n < list.Count; n++)
                        property.GetArrayElementAtIndex(n).objectReferenceValue = list[n];
                    break;
                default:
                    Debug.LogWarning($"[Prophecy] cannot wire '{field}': unsupported type " +
                                     $"{value.GetType().Name}.");
                    break;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
