using System.Collections.Generic;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using Rokkan.Prophecy.World;
using UnityEditor;
using UnityEngine;
using static Rokkan.Prophecy.Editor.Build.GrayBoxSceneScaffold;
using Object = UnityEngine.Object;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Generates <c>GrayBox_Arena</c> — the combat demo — with every distance derived from the
    /// attack that has to cover it.
    ///
    /// <para><b>Same rule as the traversal course, applied to reach instead of jump height.</b> A
    /// hand-placed dummy is a distance frozen at the moment someone dragged it. Shorten the
    /// standing slash a week later and the dummy does not move; it just quietly stops being
    /// reachable, and the first explanation anyone reaches for is that the hit detection broke.
    /// Here a dummy at 90% of reach <i>stays</i> at 90% of reach.</para>
    ///
    /// <para><b>The layout is a set of questions, not a room.</b> Each station isolates one thing
    /// that is otherwise invisible: whether stance actually picks the attack, whether cover
    /// actually stops a hit, whether the chain window is wide enough to link. A dummy that both
    /// attacks reach proves nothing about stance, so the arena has a squat one the high slash sails
    /// over and a raised one the low thrust passes under.</para>
    ///
    /// <para>Play it by opening the scene and pressing Play — <see cref="BootstrapLoader"/> pulls
    /// Bootstrap in on top and the <see cref="SceneDirector"/> adopts what is already open.</para>
    /// </summary>
    public static class GrayBoxArenaBuilder
    {
        public const string ScenePath = "Assets/_Prophecy/Scenes/GrayBox_Arena.unity";

        /// <summary>Warp entries, recorded as the stations are built — the FloorSpans pattern,
        /// because a hand-typed table of coordinates drifts the first time a station moves and
        /// nobody moves the table.</summary>
        private static readonly List<(string Label, Vector3 Position)> _stationIndex =
            new List<(string, Vector3)>();

        /// <summary>Warps land this far west of the station they name. Materialising inside
        /// the thing you came to hit reads as a glitch, and a stride or two of approach is
        /// what makes the reach stripe legible on arrival.</summary>
        private const float StationApproachMetres = 2f;

        /// <summary>Planner stations get a longer runway. These enemies move and aggro on
        /// sight, so a warp that lands at melee range starts the fight before the player has
        /// oriented — the approach leaves the enemy visible but the first move the player's.</summary>
        private const float PlannerApproachMetres = 7f;

        /// <summary>
        /// Authored combat ids, handed out in build order.
        ///
        /// <para>They have to be stable across machines — a host naming "entity 12" to a client
        /// needs both to agree which one that is — so they are written into the scene rather than
        /// counted at runtime. Starting at 10 leaves room below for anything Bootstrap owns; the
        /// player is 1.</para>
        /// </summary>
        private static int _nextAuthoredId;

        [MenuItem("Prophecy/Build/Generate GrayBox_Arena", priority = 41)]
        public static void Generate()
        {
            var tuning = LoadMovementTuning();
            if (tuning == null) return;

            var combat = LoadCombatTuning();
            if (combat == null) return;

            var reach = Reach.From(tuning.Data, combat.Data);
            string malformed = combat.Data.Validate();
            if (malformed != null)
            {
                Debug.LogError("[Prophecy] The moveset does not validate, so the arena would be " +
                               $"built around numbers that cannot work:\n{malformed}");
                return;
            }

            if (GenerateScene(ScenePath, () => { BuildContents(tuning, combat, reach); return true; }))
                Debug.Log($"[Prophecy] Generated {ScenePath}\n{reach}");
        }

        /// <summary><c>-executeMethod</c> target.</summary>
        public static void GenerateFromCommandLine()
        {
            try
            {
                Generate();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Prophecy] Arena generation failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ metrics

        /// <summary>
        /// What the moveset can actually touch, read off the authored hit boxes rather than
        /// guessed. This is the arena's half of the living dictionary: the answer to "how far can
        /// the player reach, and how high" has one source, and it is the same data the sim swings.
        /// </summary>
        private readonly struct Reach
        {
            public readonly float Stand;
            public readonly float StandNear;
            public readonly float StandLow;
            public readonly float StandHigh;
            public readonly float Crouch;
            public readonly float CrouchLow;
            public readonly float CrouchHigh;
            public readonly float BodyHalfWidth;

            private Reach(MovementTuningData movement, CombatTuningData combat)
            {
                BodyHalfWidth = movement.BodyWidth * 0.5f;

                AttackReach.Measure(combat.ForStance(Stance.Stand),
                                    out Stand, out StandNear, out StandLow, out StandHigh);
                AttackReach.Measure(combat.ForStance(Stance.Crouch),
                                    out Crouch, out _, out CrouchLow, out CrouchHigh);
            }

            public static Reach From(MovementTuningData movement, CombatTuningData combat) =>
                new Reach(movement, combat);

            public override string ToString() =>
                $"  standing slash   reach {StandNear:F2}..{Stand:F2} m, covers y {StandLow:F2}..{StandHigh:F2}\n" +
                $"  crouching thrust reach {Crouch:F2} m, covers y {CrouchLow:F2}..{CrouchHigh:F2}";
        }

        // ------------------------------------------------------------------ construction

        private static void BuildContents(MovementTuning tuning, CombatTuning combat, Reach reach)
        {
            _nextAuthoredId = 10;
            _stationIndex.Clear();

            var geometry = new GameObject("Geometry").transform;
            var targets = new GameObject("Targets").transform;
            var markers = new GameObject("Markers").transform;

            CreateLighting();
            CreateCombatDirector();
            CreateDescriptorAndSpawn(markers, tuning);

            float cursor = -12f;

            // Runs to x=180. The dummy stations end around 105, but the planner stations past it
            // patrol, and a patrol needs floor on both sides of where it starts — an enemy spawned
            // on the last metre of ground walks off it before it has decided anything.
            cursor = Ground(geometry, "Ground", cursor, 192f);

            // Station 1 — the basic hit. A full-body dummy both attacks reach, so the first swing
            // in the arena always connects and "is combat on at all" is never the question.
            Station(targets, "1_Basic", -2f, reach,
                    size: new Vector2(0.9f, 1.8f), centreY: 0.9f, health: 12);

            // Station 2 — stance. Squat enough that the standing slash passes over it, so the low
            // thrust is the only answer. Sized from the attack, not eyeballed.
            Station(targets, "2_Squat", 6f, reach,
                    size: new Vector2(0.9f, reach.StandLow - 0.1f),
                    centreY: (reach.StandLow - 0.1f) * 0.5f, health: 8,
                    indexLabel: "2 Squat / 3 Raised");

            // Station 3 — the mirror. Raised above the crouching thrust's ceiling, so only the
            // standing slash reaches. Two stations that each refuse one answer are what make stance
            // legible; one dummy that accepts both proves nothing.
            float raisedHeight = 0.7f;
            float raisedCentre = reach.CrouchHigh + 0.15f + raisedHeight * 0.5f;
            Station(targets, "3_Raised", 13f, reach,
                    size: new Vector2(0.9f, raisedHeight), centreY: raisedCentre, health: 8,
                    post: raisedCentre - raisedHeight * 0.5f);

            // Station 4 — cover. The pillar sits inside the hit box's span but across the line from
            // the attacker's chest, so the volume overlaps and the hit is refused anyway. That is
            // the whole StoppedByGeometry rule, made visible in one step.
            CoverStation(geometry, targets, 22f, reach);

            // Station 5 — the chain. Enough health to survive the opener, so the follow-up has
            // something to land on and the cancel window can actually be felt.
            Station(targets, "5_Chain", 32f, reach,
                    size: new Vector2(0.9f, 1.8f), centreY: 0.9f, health: 48,
                    indexLabel: "5 Chain");

            // Station 6 — height. A dummy on a ledge one lane up: reachable from the platform,
            // not from the floor, so vertical spacing gets checked against real reach too.
            LedgeStation(geometry, targets, tuning, 41f, reach);

            // Stations 7-9 — the things that swing back. Each demands exactly one answer, because a
            // telegraph you can survive two ways teaches nothing about either.
            Telegraph(targets, "7_High", 52f, AttackHeight.High, DefensiveAnswer.None, delay: 0,
                      damage: 2, tuning: tuning, indexLabel: "7 High telegraph");

            Telegraph(targets, "8_Low", 60f, AttackHeight.Low, DefensiveAnswer.None, delay: 30,
                      damage: 2, tuning: tuning, indexLabel: "8 Low telegraph");

            // No guard answers this one, but a parry and a dodge both do — which is the shape that
            // makes a telegraph worth reading rather than just worth holding a button through.
            Telegraph(targets, "9_Unblockable", 68f, AttackHeight.Any,
                      DefensiveAnswer.Block, delay: 60,
                      damage: 4, tuning: tuning, indexLabel: "9 Unblockable");

            // Station 10 — the down-thrust. A launch ledge and a row of targets under it, spaced so
            // one bounce carries into the next. Nothing else in the arena asks whether the dive is
            // worth doing rather than merely working. Built in course order, so the warp list and
            // the authored combat ids both read west to east.
            PogoStation(geometry, targets, tuning, 78f, reach);

            // Stations 11-12 — things that are not a sword. A bolt that travels, and a
            // shockwave that stays where it lands and grows. Each authors a different set of
            // answers, so "which of my four buttons is this one for" becomes a question the
            // arena can actually ask.
            Caster(targets, "11_Bolt", 90f, tuning, projectile: true,
                   indexLabel: "11 Bolt / 12 Shockwave");
            Caster(targets, "12_Shockwave", 100f, tuning, projectile: false);

            // Station 13 — the first thing in the arena that decides for itself. Everything above
            // is a dummy on a timer; this one plans. Placed at the far end with clear floor either
            // side of it, because the first question to ask of a patrol is whether it turns round
            // before it walks off, and that needs an edge to find.
            Grunt(targets, 112f);

            // Stations 14–16 — the rest of the roster, each alone so its one idea is legible.
            // Spaced further apart than the dummy stations: these move, and two planners inside
            // each other's 12 m sight range would be answering questions about each other rather
            // than about the player.
            Planner(targets, EnemyBuilder.Archetype.Chaser, "14_Chaser", 130f,
                    indexLabel: "14 Chaser (body-checks)");
            Planner(targets, EnemyBuilder.Archetype.Ambusher, "15_Ambusher", 148f,
                    indexLabel: "15 Ambusher (springs)");
            Planner(targets, EnemyBuilder.Archetype.Caster, "16_Caster", 166f,
                    indexLabel: "16 Caster (keeps away)");

            // A wall to stop the run-out, so nobody walks into the void wondering where the arena went.
            Box(geometry, "Wall_East", new Vector2(cursor - 2f, 0f), new Vector2(cursor, 8f));

            CreateStationIndex(markers);
        }

        /// <summary>Registers a warp entry at the moment its station is built, with the same
        /// coordinates the station was built from.</summary>
        private static void Index(string label, float x, float y = 0f) =>
            _stationIndex.Add((label, new Vector3(x, y, 0f)));

        /// <summary>
        /// One dummy on the floor, with a marker strip on the ground showing where the player has
        /// to stand for the attack to connect. The strip is the reach, drawn.
        /// </summary>
        private static void Station(Transform parent, string name, float x, Reach reach,
                                    Vector2 size, float centreY, int health, float post = 0f,
                                    string indexLabel = null)
        {
            if (indexLabel != null) Index(indexLabel, x - StationApproachMetres);

            // The root sits on the floor and every height is measured from there. Raising the root
            // to the top of a pedestal instead would add the pedestal to the hurtbox offset as
            // well, putting the volume a pedestal higher than the box you can see.
            var root = new GameObject($"Dummy_{name}");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(size.x, size.y, 0.9f);
            body.transform.localPosition = new Vector3(0f, centreY, 0f);

            // The dummy must not be something the player collides with, or they can never get close
            // enough to hit it. Hurtboxes are the sim's business; this collider is not.
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var combatant = root.AddComponent<Combatant>();
            SetPrivate(combatant, "_combatId", _nextAuthoredId++);
            SetPrivate(combatant, "_team", 2);
            SetPrivate(combatant, "_size", size);
            SetPrivate(combatant, "_offset", new Vector2(0f, centreY));
            SetPrivate(combatant, "_maxHealth", health);

            if (post > 0f)
                Box(root.transform, "Post", new Vector2(x - 0.35f, 0f), new Vector2(x + 0.35f, post));

            ReachStripe(root.transform, x, size.x * 0.5f, reach);
        }

        /// <summary>
        /// Paints the band of foot positions from which the standing slash connects with this
        /// dummy — the near limit is where the box's far edge just reaches, the far limit is where
        /// its near edge has passed through. Reach is one number; where you can stand is two, and
        /// the difference is the thing that is actually hard to feel.
        /// </summary>
        private static void ReachStripe(Transform parent, float x, float targetHalfWidth, Reach reach)
        {
            float nearest = x - targetHalfWidth - reach.Stand;
            float furthest = x + targetHalfWidth - reach.StandNear;

            if (furthest <= nearest) return;

            Marker(parent, "ReachBand", nearest, furthest);
        }

        /// <summary>
        /// A dummy that swings back on a fixed rhythm, demanding one specific answer.
        ///
        /// <para>The wind-up is long on purpose — a telegraph you cannot read is just damage on a
        /// timer, and the number that decides whether a fight is fair is how many ticks the player
        /// gets between "it is starting" and "it is too late". The hit box is sized and placed from
        /// the height it is asking about, so a low sweep really does pass under a standing guard
        /// and a high one really does sail over a crouch.</para>
        ///
        /// <para>Each attacker opens on a different delay so three of them in one arena do not
        /// fire in unison and turn a reading exercise into a scramble.</para>
        /// </summary>
        private static void Telegraph(Transform parent, string name, float x, AttackHeight height,
                                      DefensiveAnswer defeats, int delay, int damage,
                                      MovementTuning tuning, string indexLabel = null)
        {
            if (indexLabel != null) Index(indexLabel, x - StationApproachMetres);

            var root = new GameObject($"Attacker_{name}");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.9f, 1.8f, 0.9f);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var combatant = root.AddComponent<Combatant>();
            SetPrivate(combatant, "_combatId", _nextAuthoredId++);
            SetPrivate(combatant, "_team", 2);
            SetPrivate(combatant, "_size", new Vector2(0.9f, 1.8f));
            SetPrivate(combatant, "_offset", new Vector2(0f, 0.9f));
            SetPrivate(combatant, "_maxHealth", 200);

            // Anything that fights back hurts to stand inside. Walking through an enemy to get
            // behind it should cost something, or spacing is not a decision.
            SetPrivate(combatant, "_contactDamage", 6);

            // Heights measured against the player's own body, so a low sweep is genuinely below
            // what a standing guard covers rather than nominally so.
            float stand = tuning.Data.StandHeight;
            float centreY = height == AttackHeight.Low ? stand * 0.20f : stand * 0.65f;
            float halfY = height == AttackHeight.Low ? stand * 0.18f : stand * 0.28f;

            var attacker = root.AddComponent<TrainingAttacker>();
            SetPrivate(attacker, "_restTicks", 110);
            SetPrivate(attacker, "_openingDelayTicks", delay);
            SetPrivate(attacker, "_faceNearestTarget", true);

            // The window is the phases, restated: the box opens when the wind-up ends and
            // closes when the active phase does. Authored once, so retiming the telegraph
            // cannot leave the box open during a phase it no longer matches.
            const int startupTicks = 34;
            const int activeTicks = 6;

            var serialized = new SerializedObject(attacker);
            var definition = serialized.FindProperty("_attack");

            definition.FindPropertyRelative("Id").stringValue = $"telegraph_{name}";
            definition.FindPropertyRelative("StartupTicks").intValue = startupTicks;
            definition.FindPropertyRelative("ActiveTicks").intValue = activeTicks;
            definition.FindPropertyRelative("RecoveryTicks").intValue = 26;

            var boxes = definition.FindPropertyRelative("HitBoxes");
            boxes.arraySize = 1;

            var box = boxes.GetArrayElementAtIndex(0);
            box.FindPropertyRelative("OpenTick").intValue = startupTicks;
            box.FindPropertyRelative("CloseTick").intValue = startupTicks + activeTicks;
            box.FindPropertyRelative("Offset").vector2Value = new Vector2(1.05f, centreY);
            box.FindPropertyRelative("HalfExtents").vector2Value = new Vector2(0.75f, halfY);
            box.FindPropertyRelative("RotationDegrees").floatValue = 0f;
            box.FindPropertyRelative("Damage").intValue = damage;
            box.FindPropertyRelative("Height").enumValueFlag = (int)height;
            box.FindPropertyRelative("Defeats").enumValueFlag = (int)defeats;
            box.FindPropertyRelative("StoppedByGeometry").boolValue = false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A ledge to leap from and a row of targets beneath it.
        ///
        /// <para>Spacing is the question this station asks. The dive is vertical, so crossing the
        /// row means the bounce has to carry far enough sideways to reach the next target before
        /// gravity wins — which makes the gap a direct readout of whether
        /// <c>DownThrustBounceHeight</c> and air control are in proportion. A row you cannot chain
        /// is not broken, it is a number that wants changing, and this is where that shows.</para>
        /// </summary>
        private static void PogoStation(Transform geometry, Transform targets, MovementTuning tuning,
                                        float x, Reach reach)
        {
            float ledgeTop = tuning.Data.LaneHeight;

            // The warp lands ON the ledge, a couple of strides in from its edge — the station
            // is the dive, and the dive starts up here.
            Index("10 Pogo (on the ledge)", x + StationApproachMetres, ledgeTop);

            Box(geometry, "Pogo_Ledge", new Vector2(x, 0f), new Vector2(x + 5f, ledgeTop));

            // Generous health: the point is bouncing repeatedly, and a target that dies on the
            // second hit ends the exercise before it has taught anything.
            for (int i = 0; i < 3; i++)
            {
                Station(targets, $"10_Pogo_{i + 1}", x + 8f + i * 3.2f, reach,
                        size: new Vector2(0.9f, 1.2f), centreY: 0.6f, health: 80);
            }
        }

        /// <summary>
        /// A dummy that throws something instead of swinging it.
        ///
        /// <para>The bolt travels and is stopped by geometry, so it can be walked out of and hidden
        /// from. The shockwave stands still and grows, so it cannot be out-ranged once it has
        /// landed, only answered. Between them they cover the two things a volume can be, and the
        /// answers each accepts are deliberately different so neither is a re-skin of the sword
        /// stations.</para>
        /// </summary>
        /// <summary>
        /// Drop the GOAP capsule in. Instantiated from its prefab rather than assembled here, so
        /// the arena and the enemy cannot drift apart — regenerate either and the other still
        /// holds.
        /// </summary>
        private static void Grunt(Transform parent, float x) =>
            Planner(parent, EnemyBuilder.Archetype.Grunt, "13_Grunt", x,
                    indexLabel: "13 Grunt (it plans)");

        /// <summary>
        /// Drops one planner-driven enemy in, by archetype — the prefab path comes from
        /// <see cref="EnemyBuilder"/>, the generator that writes it, so the two cannot drift.
        ///
        /// <para>Each archetype gets its own station so they can be read one at a time. A chaser
        /// and a grunt standing together are hard to tell apart until one of them stops to swing,
        /// which is the moment the difference matters and the worst moment to be guessing which is
        /// which.</para>
        /// </summary>
        private static void Planner(Transform parent, EnemyBuilder.Archetype archetype,
                                    string stationName, float x, string indexLabel = null)
        {
            if (indexLabel != null) Index(indexLabel, x - PlannerApproachMetres);

            string path = EnemyBuilder.PrefabPathFor(archetype);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogWarning($"[Prophecy] No enemy prefab at {path}. Run " +
                                 "Prophecy > Build > Generate Enemies first; the arena will " +
                                 $"build without {stationName}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = stationName;
            instance.transform.position = new Vector3(x, 0f, 0f);
        }

        private static void Caster(Transform parent, string name, float x, MovementTuning tuning,
                                   bool projectile, string indexLabel = null)
        {
            if (indexLabel != null) Index(indexLabel, x - StationApproachMetres);

            var root = new GameObject($"Caster_{name}");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.9f, 1.8f, 0.9f);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var combatant = root.AddComponent<Combatant>();
            SetPrivate(combatant, "_combatId", _nextAuthoredId++);
            SetPrivate(combatant, "_team", 2);
            SetPrivate(combatant, "_size", new Vector2(0.9f, 1.8f));
            SetPrivate(combatant, "_offset", new Vector2(0f, 0.9f));
            SetPrivate(combatant, "_maxHealth", 200);

            SetPrivate(combatant, "_contactDamage", 6);

            var attacker = root.AddComponent<TrainingAttacker>();
            SetPrivate(attacker, "_restTicks", 120);
            SetPrivate(attacker, "_openingDelayTicks", projectile ? 0 : 45);
            SetPrivate(attacker, "_faceNearestTarget", true);

            // The projectile leaves the hand the tick the wind-up ends — authored once, so
            // retiming the cast cannot leave the launch inside a phase it no longer matches.
            const int startupTicks = 40;
            const int activeTicks = 4;

            var serialized = new SerializedObject(attacker);
            var definition = serialized.FindProperty("_attack");

            definition.FindPropertyRelative("Id").stringValue = $"cast_{name}";
            definition.FindPropertyRelative("StartupTicks").intValue = startupTicks;
            definition.FindPropertyRelative("ActiveTicks").intValue = activeTicks;
            definition.FindPropertyRelative("RecoveryTicks").intValue = 24;

            // The cast has no hit box of its own: everything it does, it does at range.
            definition.FindPropertyRelative("HitBoxes").arraySize = 0;

            var spawns = definition.FindPropertyRelative("Spawns");
            spawns.arraySize = 1;

            var spawn = spawns.GetArrayElementAtIndex(0);
            spawn.FindPropertyRelative("Tick").intValue = startupTicks;

            var shot = spawn.FindPropertyRelative("Projectile");
            float stand = tuning.Data.StandHeight;

            if (projectile)
            {
                // Chest height, flat, and stopped by a wall, so the cover station's grate is an
                // answer to this one too.
                shot.FindPropertyRelative("Id").stringValue = "bolt";
                shot.FindPropertyRelative("SpawnOffset").vector2Value = new Vector2(0.8f, stand * 0.55f);
                shot.FindPropertyRelative("Speed").floatValue = 9f;
                shot.FindPropertyRelative("Gravity").floatValue = 0f;
                shot.FindPropertyRelative("HalfExtents").vector2Value = new Vector2(0.22f, 0.22f);
                shot.FindPropertyRelative("GrowthPerSecond").vector2Value = Vector2.zero;
                shot.FindPropertyRelative("Damage").intValue = 10;
                shot.FindPropertyRelative("LifetimeTicks").intValue = 180;
                shot.FindPropertyRelative("StoppedByGeometry").boolValue = true;
                shot.FindPropertyRelative("MaxTargets").intValue = 1;

                // Defeats nothing: every answer works. The honest one, and the baseline the
                // other reads against.
                shot.FindPropertyRelative("Defeats").enumValueFlag = (int)DefensiveAnswer.None;
                shot.FindPropertyRelative("Height").enumValueFlag = (int)AttackHeight.High;
            }
            else
            {
                // Lands at the caster's feet and spreads. No guard turns a floor that is on fire:
                // being elsewhere is the answer, so i-frames work and blocking does not.
                shot.FindPropertyRelative("Id").stringValue = "shockwave";
                shot.FindPropertyRelative("SpawnOffset").vector2Value = new Vector2(-1.6f, 0.35f);
                shot.FindPropertyRelative("Speed").floatValue = 0f;
                shot.FindPropertyRelative("Gravity").floatValue = 0f;
                shot.FindPropertyRelative("HalfExtents").vector2Value = new Vector2(0.5f, 0.35f);
                shot.FindPropertyRelative("GrowthPerSecond").vector2Value = new Vector2(3.2f, 0f);
                shot.FindPropertyRelative("Damage").intValue = 14;
                shot.FindPropertyRelative("LifetimeTicks").intValue = 45;
                shot.FindPropertyRelative("StoppedByGeometry").boolValue = false;
                shot.FindPropertyRelative("MaxTargets").intValue = 0;

                // A floor that is on fire turns no shield and cannot be caught on a blade.
                // Being elsewhere is the answer, so only i-frames survive it.
                shot.FindPropertyRelative("Defeats").enumValueFlag =
                    (int)(DefensiveAnswer.Block | DefensiveAnswer.Parry);
                shot.FindPropertyRelative("Height").enumValueFlag = (int)AttackHeight.Low;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The warp list, serialized from the entries the stations recorded as they were built.
        /// Ninety metres of arena with the stations that fight back at the far end means a long
        /// walk before every attempt at a parry, which is exactly the friction that stops anyone
        /// iterating — and a warp list authored as a second set of coordinates drifts the moment
        /// a station moves, which is why the positions here are the build's own.
        /// </summary>
        private static void CreateStationIndex(Transform markers)
        {
            var go = new GameObject("ArenaStations");
            go.transform.SetParent(markers, false);

            var stations = go.AddComponent<ArenaStations>();
            var serialized = new SerializedObject(stations);
            var list = serialized.FindProperty("_stations");

            list.arraySize = _stationIndex.Count;

            for (int i = 0; i < _stationIndex.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Label").stringValue = _stationIndex[i].Label;
                element.FindPropertyRelative("Position").vector3Value = _stationIndex[i].Position;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CoverStation(Transform geometry, Transform targets, float x, Reach reach)
        {
            // A grate thin enough to be obviously not a wall, tall enough to cross the chest-height
            // line the cover ray is traced along.
            Box(geometry, "Cover_Grate", new Vector2(x, 0f), new Vector2(x + 0.15f, 2.2f));

            Station(targets, "4_Cover", x + 0.65f, reach,
                    size: new Vector2(0.9f, 1.8f), centreY: 0.9f, health: 12,
                    indexLabel: "4 Cover");
        }

        private static void LedgeStation(Transform geometry, Transform targets, MovementTuning tuning,
                                         float x, Reach reach)
        {
            // The warp stops short of the ledge itself, on the floor: the station's question is
            // "can you reach what stands a half-lane up", and it is asked from down here.
            Index("6 Ledge", x - StationApproachMetres);

            float top = tuning.Data.LaneHeight * 0.5f;

            Box(geometry, "Ledge", new Vector2(x, 0f), new Vector2(x + 8f, top));

            Station(targets, "6_Ledge", x + 4f, reach,
                    size: new Vector2(0.9f, 1.8f), centreY: top + 0.9f, health: 12);
        }

        // ------------------------------------------------------------------ scene furniture

        /// <summary>
        /// The arena owns the combat world, not Bootstrap. The player persists across scenes and
        /// the fight does not — a director in Bootstrap would keep hurtboxes from an arena that had
        /// already been unloaded, which is exactly the bug the per-scene lifetime avoids.
        /// </summary>
        private static void CreateCombatDirector()
        {
            var director = new GameObject("CombatDirector");
            var component = director.AddComponent<CombatDirector>();
            SetPrivate(component, "_space", MovementSpace.SideScroll);

            // Projectiles are simulated whether or not anything draws them, and until now nothing
            // did outside the F2 overlay — so a caster firing and a caster failing to fire looked
            // identical, and the bolt that hit you came from nowhere.
            var view = director.AddComponent<ProjectileView>();
            SetPrivate(view, "_director", component);
        }

        private static void CreateDescriptorAndSpawn(Transform markers, MovementTuning tuning)
        {
            const float spawnX = -8f;

            Index("Spawn", spawnX);

            var spawnObject = new GameObject("Spawn_default");
            spawnObject.transform.SetParent(markers, false);
            spawnObject.transform.position = new Vector3(spawnX, 0f, 0f);

            var spawn = spawnObject.AddComponent<SpawnPoint>();
            SetPrivate(spawn, "_id", "default");
            SetPrivate(spawn, "_facing", 1);

            var descriptorObject = new GameObject("SceneDescriptor");
            descriptorObject.transform.SetParent(markers, false);

            var descriptor = descriptorObject.AddComponent<SceneDescriptor>();
            SetPrivate(descriptor, "_space", MovementSpace.SideScroll);
            SetPrivate(descriptor, "_defaultSpawn", spawn);
            SetPrivate(descriptor, "_displayName", "Gray Box — Arena");

            SetPrivate(descriptor, "_killPlaneEnabled", true);
            SetPrivate(descriptor, "_killPlaneY", -25f);

            SetPrivate(descriptor, "_useCameraBounds", true);
            SetPrivate(descriptor, "_cameraFloorY", -tuning.Data.LaneHeight);
            SetPrivate(descriptor, "_cameraCeilingY", 30f);
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>A flat strip painted on the floor. Non-colliding — it is a label, not geometry.</summary>
        private static void Marker(Transform parent, string name, float minX, float maxX)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localScale = new Vector3(maxX - minX, 0.04f, Depth * 0.6f);
            quad.transform.position = new Vector3((minX + maxX) * 0.5f, 0.02f, 0f);

            Object.DestroyImmediate(quad.GetComponent<Collider>());
        }
    }
}
