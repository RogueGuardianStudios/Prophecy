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
        private const string SlotFolder = Folder + "/Slots";
        private const string PrefabPath = "Assets/_Prophecy/Prefabs/Enemy_Capsule.prefab";

        // Blackboard key names. Sensors write these; beliefs read them.
        private const string KeyHasTarget = ProphecyTargetSensor.HasTarget;
        private const string KeyCanSee = ProphecyTargetSensor.CanSeeTarget;
        private const string KeyDistanceX = ProphecyTargetSensor.TargetDistanceX;
        private const string KeyDirection = ProphecyTargetSensor.TargetDirection;
        private const string KeyBlocked = ProphecyTerrainSensor.BlockedAhead;

        /// <summary>
        /// What kind of enemy this is — which is entirely a question of which actions it owns.
        ///
        /// <para><b>"Dumb" does not mean "no planner".</b> A chaser that only ever closes still has
        /// a decision to make (chase, or go back to patrolling) and makes it the same way the grunt
        /// does. The difference between the simplest enemy here and the most capable is the size of
        /// its action set, not the machinery underneath — which is what keeps one brain format,
        /// one sensor set and one debugging story across the whole roster.</para>
        /// </summary>
        public enum Archetype
        {
            /// <summary>Patrols, closes, and fights at range zero. The full loop.</summary>
            Grunt,

            /// <summary>Closes and body-checks. No swing at all — contact is the attack.</summary>
            Chaser,

            /// <summary>Lies still until something comes close, then commits to one lunge.</summary>
            Ambusher,

            /// <summary>Keeps its distance and throws things. Retreat is how it fights.</summary>
            Caster,

            /// <summary>The overworld's menace: roams the plane in two axes, weighted toward the
            /// player. Zelda II's map blobs. Touch is its only threat, like the chaser — and the
            /// touch is where a real encounter trigger will land later.</summary>
            Wanderer,
        }

        [MenuItem("Prophecy/Build/Generate Enemies", priority = 40)]
        public static void Generate()
        {
            EnsureFolder();

            var boolCheck = CreateOrReplace<GoapBeliefBoolCheck>($"{Folder}/Belief_BoolCheck.asset");
            var compare = CreateOrReplace<GoapBeliefSimpleCompare>($"{Folder}/Belief_Compare.asset");

            var targetSensor = CreateOrReplace<ProphecyTargetSensor>($"{Folder}/Sensor_Target.asset");
            var terrainSensor = CreateOrReplace<ProphecyTerrainSensor>($"{Folder}/Sensor_Terrain.asset");

            var kit = new StrategyKit
            {
                Patrol = CreateOrReplace<PatrolStrategy>($"{Folder}/Action_Patrol.asset"),
                Pursue = CreateOrReplace<PursueStrategy>($"{Folder}/Action_Pursue.asset"),
                Swing = CreateOrReplace<SwingStrategy>($"{Folder}/Action_Swing.asset"),
                Lurk = CreateOrReplace<LurkStrategy>($"{Folder}/Action_Lurk.asset"),
                Spring = CreateOrReplace<SpringStrategy>($"{Folder}/Action_Spring.asset"),
                KeepDistance = CreateOrReplace<KeepDistanceStrategy>($"{Folder}/Action_KeepDistance.asset"),
                Roam = CreateOrReplace<RoamStrategy>($"{Folder}/Action_Roam.asset"),
            };

            var built = new System.Text.StringBuilder("[Prophecy] Enemies generated.");

            foreach (Archetype archetype in System.Enum.GetValues(typeof(Archetype)))
            {
                var brain = BuildBrain(archetype, $"{Folder}/Brain_{archetype}.asset",
                                       boolCheck, compare, targetSensor, terrainSensor, kit);

                BuildPrefab(archetype, brain);
                built.Append($"\n  {archetype,-9} {PrefabPathFor(archetype)}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(built.ToString());
        }

        /// <summary>The shared strategy assets, passed round rather than reloaded per archetype.</summary>
        private struct StrategyKit
        {
            public PatrolStrategy Patrol;
            public PursueStrategy Pursue;
            public SwingStrategy Swing;
            public LurkStrategy Lurk;
            public SpringStrategy Spring;
            public KeepDistanceStrategy KeepDistance;
            public RoamStrategy Roam;
        }

        private static string PrefabPathFor(Archetype archetype) =>
            archetype == Archetype.Grunt
                ? PrefabPath                                            // the name the arena already uses
                : $"Assets/_Prophecy/Prefabs/Enemy_{archetype}.prefab";

        // ---------------------------------------------------------------- brain

        private static GoapBrainSO BuildBrain(Archetype archetype, string brainPath,
                                              GoapBeliefBoolCheck boolCheck,
                                              GoapBeliefSimpleCompare compare,
                                              ProphecyTargetSensor targetSensor,
                                              ProphecyTerrainSensor terrainSensor,
                                              StrategyKit kit)
        {
            var patrol = kit.Patrol;
            var pursue = kit.Pursue;
            var swing = kit.Swing;

            var brain = CreateOrReplace<GoapBrainSO>(brainPath);

            brain.Keys.Clear();
            brain.KeyManifest.Clear();
            brain.States.Clear();
            brain.Sensors.Clear();
            brain.Transitions.Clear();

            var hasTarget = AddKey(brain, KeyHasTarget, GoapKeyType.Boolean);
            var canSee = AddKey(brain, KeyCanSee, GoapKeyType.Boolean);
            var distanceX = AddKey(brain, KeyDistanceX, GoapKeyType.Float);
            var blocked = AddKey(brain, KeyBlocked, GoapKeyType.Boolean);

            // Written by the target sensor and unused by any belief here, but declared anyway: a
            // sensor output with no key is the same silent no-op as an unmapped one, and a mapping
            // that exists is one less thing to rediscover when something does want a facing.
            AddKey(brain, KeyDirection, GoapKeyType.Integer);

            // Planner-space keys. Nothing writes these — they exist so an action can advertise a
            // world state a goal wants, which is how A* finds its way from "patrolling" to "swung".
            var struck = AddKey(brain, "TargetStruck", GoapKeyType.Boolean);
            var rammedKey = AddKey(brain, "Rammed", GoapKeyType.Boolean);
            var patrolled = AddKey(brain, "Patrolling", GoapKeyType.Boolean);

            brain.Sensors.Add(targetSensor);
            brain.Sensors.Add(terrainSensor);

            // Closing distance sits INSIDE the swing's reach, and that gap is the whole point.
            // Pursue succeeds at StopWithin and Swing fails beyond Reach, so with the two equal the
            // pursuit halts exactly on the boundary and any drift outward — the target backing off,
            // a tick of separation — fails the swing before it starts. The planner then replans,
            // closes, and fails again: an enemy that walks up to you and never attacks.
            //
            // SwingReach is used twice on purpose: once as the planner's precondition (the
            // WithinReach belief) and once as the strategy's own check. They must agree, or the
            // planner commits to a swing the strategy then refuses.
            const float SwingReach = 1.4f;
            const float CloseTo = 0.9f;

            // An ambusher's trigger. Wide enough that walking past sets it off, short enough that
            // it reads as a trap you stepped into rather than something that saw you coming.
            const float SpringRange = 3.5f;

            // Where a caster wants to stand, and the furthest it will still take a shot from.
            const float CasterRange = 6f;
            const float CasterMaxRange = 12f;

            // ---- beliefs
            var seesTarget = Bool(boolCheck, canSee, "SeesTarget");
            var hasAnyTarget = Bool(boolCheck, hasTarget, "HasTarget");
            var withinReach = Compare(compare, distanceX, ComparisonType.LessThanOrEqual, SwingReach, "WithinReach");
            var wasStruck = Bool(boolCheck, struck, "TargetStruck");
            var isPatrolling = Bool(boolCheck, patrolled, "Patrolling");
            var hasRammed = Bool(boolCheck, rammedKey, "Rammed");

            // ---- actions. Preconditions are what must hold; effects are what the planner may
            //      assume afterwards. The chain patrol -> pursue -> swing falls out of these.
            var patrolAction = Action("Patrol", patrol, cost: 5f,
                effects: new[] { On(isPatrolling) },
                settings: new PatrolSettings { StartDirection = 1 });

            // Pursue asserts the SENSOR's belief, not a planner-private stand-in for it.
            //
            // It used to advertise a separate "InReach" key and Swing required both that and
            // WithinReach. But WithinReach is written by the target sensor and no action can
            // produce it, so A* could only reach StrikeTarget when the target already happened to
            // be inside 1.4 m — walking there was not something the planner knew how to do. The
            // enemy could therefore only plan an attack against a target that had walked up to it.
            //
            // Closing the distance is exactly what Pursue does, so it says so. StopWithin sits
            // inside SwingReach, which is what makes the claim true when the action succeeds.
            var pursueAction = Action("Pursue", pursue, cost: 2f,
                preconditions: new[] { On(seesTarget) },
                effects: new[] { On(withinReach) },
                settings: new PursueSettings { StopWithin = CloseTo });

            var swingAction = Action("Swing", swing, cost: 1f,
                preconditions: new[] { On(withinReach) },
                effects: new[] { On(wasStruck) },
                settings: new SwingSettings { Reach = SwingReach, RecoveryTicks = 45 });

            // Springing range is wider than a swing's, and NOTHING can make it true — deliberately.
            // An ambusher is defined by only being dangerous when something has already come close,
            // so "get closer" must not be a step the planner can take to enable its attack. This is
            // the one place the unreachable-precondition shape is the design rather than the bug it
            // was for the grunt.
            var withinSpring = Compare(compare, distanceX, ComparisonType.LessThanOrEqual,
                                       SpringRange, "WithinSpringRange");

            // A caster's opposite: far enough away to be worth shooting from.
            var atRange = Compare(compare, distanceX, ComparisonType.GreaterThanOrEqual,
                                  CasterRange, "AtRange");

            var lurkAction = Action("Lurk", kit.Lurk, cost: 5f,
                effects: new[] { On(isPatrolling) },
                settings: new LurkSettings { WatchTarget = true });

            var springAction = Action("Spring", kit.Spring, cost: 1f,
                preconditions: new[] { On(seesTarget), On(withinSpring) },
                effects: new[] { On(wasStruck) },
                settings: new SpringSettings { LungeTicks = 20, Leap = true });

            var keepDistanceAction = Action("KeepDistance", kit.KeepDistance, cost: 2f,
                preconditions: new[] { On(seesTarget) },
                effects: new[] { On(atRange) },
                settings: new KeepDistanceSettings { PreferredRange = CasterRange, Hysteresis = 1.5f });

            // Firing is a swing with a longer arm: same action, same button, and the projectile
            // comes from the attack's own Spawns rather than from anything the planner knows about.
            var fireAction = Action("Fire", swing, cost: 1f,
                preconditions: new[] { On(seesTarget), On(atRange) },
                effects: new[] { On(wasStruck) },
                settings: new SwingSettings { Reach = CasterMaxRange, RecoveryTicks = 60 });

            // ---- goals. Attacking outranks idling, and is only considered when there is something
            //      to attack — so an enemy with nothing in sight plans its idle instead of failing
            //      to plan at all.
            var killGoal = Goal("StrikeTarget", priority: 10f,
                desired: new[] { On(wasStruck) },
                validity: new[] { On(hasAnyTarget) });

            // The ambusher's version also requires the target to be CLOSE, not merely to exist.
            //
            // Its spring is deliberately gated on a range nothing can close, so with a target in
            // sight but out of range the goal is valid and unreachable — and the planner reports a
            // failure five times, enters fallback, waits out a cooldown, and does it again. Making
            // the trigger a validity condition means the goal is simply not considered until you
            // are near, and it plans its lurk cleanly instead. A goal that cannot be reached should
            // not be offered.
            var springGoal = Goal("StrikeTarget", priority: 10f,
                desired: new[] { On(wasStruck) },
                validity: new[] { On(hasAnyTarget), On(withinSpring) });

            var patrolGoal = Goal("Wander", priority: 1f,
                desired: new[] { On(isPatrolling) });

            // A chaser has no attack at all — closing IS the whole behaviour, and contact damage on
            // its body does the rest.
            //
            // It wants "Rammed", which nothing in the world ever makes true, rather than the
            // sensor's WithinReach. That distinction is not pedantry: a goal whose desired state is
            // ALREADY true needs no actions to reach, so the planner returns a zero-step plan,
            // completes it the same frame, and replans — every frame, forever. The chaser stood
            // jittering on top of its target while filling the log at about a megabyte a second.
            //
            // A goal only an action can satisfy keeps a plan alive. This is the same lesson as the
            // grunt's, from the other side: a goal must be reachable but not already reached.
            var closeGoal = Goal("CloseIn", priority: 10f,
                desired: new[] { On(hasRammed) },
                validity: new[] { On(hasAnyTarget) });

            // Closes and then holds, rather than completing. HoldPosition is what stops it walking
            // straight through the target and back again — characters do not collide in the sim, so
            // "keep closing" is not pressure, it is an oscillation that lands contact damage from
            // both sides. It stops just short and lets the contact interval do the work.
            var chargeAction = Action("Pursue", pursue, cost: 2f,
                preconditions: new[] { On(seesTarget) },
                effects: new[] { On(hasRammed) },
                settings: new PursueSettings { StopWithin = 0.7f, HoldPosition = true });

            // The overworld's entire vocabulary: roam the plane, weighted toward whatever can be
            // seen. It advertises the idle bit and never completes — like the chaser's held
            // pursuit, wandering is what the creature does, not a task with an end. No
            // precondition: a wanderer with nothing in sight still wanders, which is the point.
            var roamAction = Action("Roam", kit.Roam, cost: 5f,
                effects: new[] { On(isPatrolling) },
                settings: new RoamSettings { RepickTicks = 90, BiasTowardTarget = 0.55f, MoveScale = 0.75f });

            var state = new GoapBehavioralState
            {
                Name = archetype.ToString(),
                StateId = GoapGuid.NewGuid(),
            };

            switch (archetype)
            {
                case Archetype.Grunt:
                    state.Actions.Add(patrolAction);
                    state.Actions.Add(pursueAction);
                    state.Actions.Add(swingAction);
                    state.Goals.Add(killGoal);
                    state.Goals.Add(patrolGoal);
                    break;

                case Archetype.Chaser:
                    state.Actions.Add(patrolAction);
                    state.Actions.Add(chargeAction);
                    state.Goals.Add(closeGoal);
                    state.Goals.Add(patrolGoal);
                    break;

                case Archetype.Ambusher:
                    state.Actions.Add(lurkAction);
                    state.Actions.Add(springAction);
                    state.Goals.Add(springGoal);
                    state.Goals.Add(patrolGoal);
                    break;

                case Archetype.Caster:
                    state.Actions.Add(lurkAction);
                    state.Actions.Add(keepDistanceAction);
                    state.Actions.Add(fireAction);
                    state.Goals.Add(killGoal);
                    state.Goals.Add(patrolGoal);
                    break;

                // One action, one goal. The bias toward the player lives inside Roam, so menace
                // needs no second goal to switch to — the simplest brain in the roster, and the
                // proof that "dumb is a smaller action set" scales all the way down.
                case Archetype.Wanderer:
                    state.Actions.Add(roamAction);
                    state.Goals.Add(patrolGoal);
                    break;
            }

            // Every belief the state's own actions and goals reference, whether or not this
            // archetype uses it — GatherBeliefs walks conditions, so an unused one costs a bit index
            // and nothing else, while a missing one is a plan that cannot be found.
            state.Beliefs.Add(seesTarget);
            state.Beliefs.Add(hasAnyTarget);
            state.Beliefs.Add(withinReach);
            state.Beliefs.Add(wasStruck);
            state.Beliefs.Add(isPatrolling);
            state.Beliefs.Add(hasRammed);

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

            // A key on its own is unreachable. A sensor names its outputs in strings, and the only
            // route from that string to this KeyId runs
            //
            //     output name -> slot asset -> brain key manifest -> KeyId
            //
            // with every hop failing silently: an unmapped name resolves to Guid.Empty and
            // GoapSensorSO.WriteBool returns without writing. Skip the manifest and the enemy still
            // starts, still senses, still reports a valid brain — and its blackboard stays empty
            // forever, so every belief reads false and the planner never finds a goal to want.
            brain.KeyManifest.Add(new GoapBrainSO.BrainKeyMapping { Slot = EnsureSlot(name, type), KeyId = key.KeyId });
            return key.KeyId;
        }

        /// <summary>
        /// The shared identity a sensor output binds to, loaded rather than replaced.
        ///
        /// <para>Slots are referenced by the prefab's sensor mappings, so regenerating must reuse
        /// the same asset — replacing it would leave those mappings pointing at a deleted object
        /// and silently unbind every sensor again.</para>
        /// </summary>
        private static GoapSlotSO EnsureSlot(string name, GoapKeyType type)
        {
            string path = $"{SlotFolder}/Slot_{name}.asset";
            var slot = AssetDatabase.LoadAssetAtPath<GoapSlotSO>(path);

            if (slot == null)
            {
                slot = ScriptableObject.CreateInstance<GoapSlotSO>();
                AssetDatabase.CreateAsset(slot, path);
            }

            slot.DataType = type;
            EditorUtility.SetDirty(slot);
            return slot;
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
                                                 NodeCondition[] effects = null,
                                                 RGS.GOAP.Core.Strategies.BaseStrategySettings settings = null)
        {
            var action = new GoapActionInstance
            {
                InstanceId = GoapGuid.NewGuid(),
                Name = name,
                Strategy = strategy,
                Cost = cost,

                // Left null this reads as "use the defaults", but the defaults it falls back to are
                // literals inside each strategy's OnUpdate — not the values on the settings class.
                // So the authored numbers, and their tooltips explaining how they relate, were
                // dead text. Assigning a real instance is what makes them the tuning surface.
                Settings = settings,
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

        private static void BuildPrefab(Archetype archetype, GoapBrainSO brain)
        {
            string prefabPath = PrefabPathFor(archetype);
            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(prefabPath));

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

                // Explicit, because "no loadout" means every module ON — so an enemy left empty
                // silently owns the player's whole moveset. That is how the ambusher's spring
                // became a double jump.
                Wire(host, "_loadout", EnemyLoadoutBuilder.Generate());
                // The enemy's own moveset, not the player's. Same damage and reach, four times the
                // wind-up — see EnemyTuningBuilder for why sharing the player's frame data made the
                // attack unanswerable rather than merely fast.
                Wire(host, "_combatTuning", archetype == Archetype.Caster
                    ? EnemyTuningBuilder.GenerateCaster()
                    : EnemyTuningBuilder.Generate());
                // TRUE, unlike the player. The player lives in Bootstrap and its geometry arrives
                // with a scene load, so baking at Start would find nothing; an enemy lives IN the
                // scene it fights in, and its floor exists by the time Start runs. With this off
                // the enemy's CollisionWorld stays empty, every ground probe fails, and a patrol
                // believes it is standing on the edge of a cliff forever.
                Wire(host, "_bakeOnStart", true);

                Wire(view, "_host", host);
                Wire(view, "_body", body.transform);
                Wire(view, "_resizeProxyOnCrouch", true);   // it is a capsule; squashing reads fine

                Wire(combatant, "_team", 2);

                // A chaser has no attack action at all, so its body has to be the weapon — without
                // contact damage it is a harmless thing that jogs at you. Everything else is only
                // dangerous when it commits to a swing, which is what makes closing on them a
                // decision rather than a punishment. The wanderer shares the rule: touch is its
                // whole threat, and today's chip damage is the placeholder where the overworld
                // encounter trigger will land.
                bool touchIsTheWeapon = archetype == Archetype.Chaser || archetype == Archetype.Wanderer;
                Wire(combatant, "_contactDamage", touchIsTheWeapon ? 8 : 0);

                // Without this the grunt is a DUMMY: BuildHurtbox falls back to a box on its rest
                // position, so the capsule walks off and leaves its hittable volume at the spawn
                // point. Nothing reports it — a dummy that does not move is the normal case.
                Wire(combatant, "_simHost", host);

                // Its attacks are all Defeats:None — block, parry and i-frames every one of them.
                // Without a visible wind-up that is a defence you cannot time, so the enemy reads
                // as unblockable whether it is or not.
                var telegraph = root.AddComponent<AttackTelegraph>();
                Wire(telegraph, "_host", host);
                Wire(telegraph, "_combatant", combatant);

                Wire(brainHost, "_host", host);
                Wire(brainHost, "_team", 2);

                // Lookout eyes, wanderer only. The spawner places it 14 m from the player —
                // outside the default 12 m sight — so without this it is born blind to the one
                // thing it exists to menace, drifts at random, and is retired without ever
                // bending toward anyone. Everyone else keeps the default: a lane enemy that sees
                // 20 m aggros half the arena.
                if (archetype == Archetype.Wanderer)
                    Wire(brainHost, "_tuning.SightRange", 20f);

                AttachGoap(root, brain);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
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

            // OFF now that the roster plans. This logs every plan and every action completion, and
            // an enemy replanning each frame turned that into ~1 MB/s and an 87 MB Editor.log —
            // which buries the one line worth reading. Turn it on per-agent in the inspector when
            // a specific enemy is misbehaving.
            Wire(agent, "_debugPlanSearch", false);

            // A diagnostic, not a feature. "The enemy is not planning" has several causes that look
            // identical from outside; this says which.
            root.AddComponent<GoapTraceProbe>();

            // SensorController.InitializeFromBrain does fill this list at Awake — but only with the
            // sensor asset, leaving OutputMappings empty, and an unmapped output name resolves to
            // Guid.Empty and is dropped without a word. The mappings are the wiring, and they have
            // to be authored; nothing infers them from the fact that a sensor declares an output.
            var targetSensor = Load<ProphecyTargetSensor>($"{Folder}/Sensor_Target.asset");
            var terrainSensor = Load<ProphecyTerrainSensor>($"{Folder}/Sensor_Terrain.asset");

            sensors.Sensors.Clear();
            sensors.Sensors.Add(MapSensor(targetSensor,
                (ProphecyTargetSensor.HasTarget, GoapKeyType.Boolean),
                (ProphecyTargetSensor.CanSeeTarget, GoapKeyType.Boolean),
                (ProphecyTargetSensor.TargetDistanceX, GoapKeyType.Float),
                (ProphecyTargetSensor.TargetDirection, GoapKeyType.Integer)));

            sensors.Sensors.Add(MapSensor(terrainSensor,
                (ProphecyTerrainSensor.BlockedAhead, GoapKeyType.Boolean)));
        }

        /// <summary>
        /// Binds a sensor's declared outputs to the slots the brain maps, which is the step that
        /// makes its writes land anywhere.
        /// </summary>
        private static GoapSensorLocalSettings MapSensor(GoapSensorSO sensor,
                                                         params (string Name, GoapKeyType Type)[] outputs)
        {
            // Plain GoapSensorLocalSettings only because neither Prophecy sensor overrides
            // GetSettingsType. If one ever does, SensorController's upgrade pass will swap this
            // instance for the right subclass — and the mappings would go with it.
            var settings = new GoapSensorLocalSettings { Sensor = sensor, UpdateInterval = 0.1f };

            foreach (var (name, type) in outputs)
            {
                settings.OutputMappings.Add(new SensorOutputMapping
                {
                    OutputName = name,
                    ExpectedType = type,
                    TargetSlot = EnsureSlot(name, type),
                });
            }

            return settings;
        }

        // ---------------------------------------------------------------- plumbing

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Prophecy/Data"))
                AssetDatabase.CreateFolder("Assets/_Prophecy", "Data");

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Prophecy/Data", "Enemies");

            if (!AssetDatabase.IsValidFolder(SlotFolder))
                AssetDatabase.CreateFolder(Folder, "Slots");
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
                default:
                    Debug.LogWarning($"[Prophecy] cannot wire '{field}': unsupported type " +
                                     $"{value.GetType().Name}.");
                    break;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
