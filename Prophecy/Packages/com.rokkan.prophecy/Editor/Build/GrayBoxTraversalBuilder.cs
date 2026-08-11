using System.Collections.Generic;
using System.IO;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Generates <c>GrayBox_Traversal</c> — the traversal course — with every dimension derived
    /// from <see cref="MovementTuning"/>.
    ///
    /// <para><b>This is the one piece of level geometry that must be generated.</b> A hand-placed
    /// ledge is a number frozen at the moment someone dragged it. Change the jump height a week
    /// later and the ledge does not move — it just quietly becomes the wrong test, still clearable
    /// or newly impossible, and nothing says so. Here a ledge at 90% of jump height <i>stays</i> at
    /// 90% of jump height. Retune, regenerate, and the course still asks the question it was built
    /// to ask.</para>
    ///
    /// <para>That makes this file the living dictionary of level dimensions: the maximum jump
    /// distance is computed here from the same numbers the sim integrates, so the answer to "how
    /// far can the player jump" has exactly one source.</para>
    ///
    /// <para>The arithmetic is closed-form ballistics and therefore slightly conservative — it
    /// ignores the apex gravity scale, which buys a little extra hang and a little extra distance.
    /// Erring short is the right direction: every gap it authors is comfortably clearable, and the
    /// hardest one still demands a proper run-up.</para>
    /// </summary>
    public static class GrayBoxTraversalBuilder
    {
        public const string ScenePath = "Assets/_Prophecy/Scenes/GrayBox_Traversal.unity";

        /// <summary>Scene name portals in other scenes target. Derived so it cannot drift.</summary>
        public static string SceneName => Path.GetFileNameWithoutExtension(ScenePath);

        /// <summary>The mid-level arrival spawn. Named here so the overworld's return portal and
        /// this builder agree by construction rather than by convention.</summary>
        public const string CentreSpawnId = "centre";

        private const float Depth = 3f;      // Z thickness, so the side-on camera sees solid boxes
        private const float GroundThickness = 2f;

        /// <summary>Walkable ground-level floor spans, recorded as they are built. What the centre
        /// spawn and the portals stand on — placing either by eye instead would put it over a gap
        /// the moment the course's proportions retune.</summary>
        private static readonly List<Vector2> FloorSpans = new List<Vector2>();

        /// <summary>The lowest walkable floor any section built — the pool basin, today. The
        /// camera floor derives from it, or descending below ground level outruns the frame.</summary>
        private static float _lowestFloorY;

        [MenuItem("Prophecy/Build/Generate GrayBox_Traversal", priority = 40)]
        public static void Generate()
        {
            var tuning = LoadTuning();
            if (tuning == null) return;

            var metrics = Metrics.From(tuning);

            // Refuse rather than prompt. A modal "save your changes?" dialog would deadlock this
            // when it is driven from the command line or a tool, and losing someone's unsaved
            // scene to a generator is a far worse outcome than being told to save first.
            if (HasUnsavedChanges()) return;

            // Remember what was open so it can be put back. An untitled scene has no path and
            // cannot be restored — but it also has nothing in it worth restoring.
            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = false;
            for (int i = 0; setup != null && i < setup.Length; i++)
                if (!string.IsNullOrEmpty(setup[i].path)) canRestore = true;

            // Single, not Additive: Unity refuses to create an additive scene while an untitled
            // one is open, which is the state a freshly opened editor is always in.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildContents(tuning, metrics);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.Refresh();
            BuildSettings.EnsureInBuildSettings(ScenePath);

            if (canRestore) EditorSceneManager.RestoreSceneManagerSetup(setup);

            Debug.Log($"[Prophecy] Generated {ScenePath}\n{metrics}");
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
                Debug.LogError($"[Prophecy] Gray box generation failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ derived numbers

        /// <summary>
        /// What the tuning implies about what the player can physically do. Every dimension in the
        /// course is a fraction of one of these, so the course can never author a gap the movement
        /// cannot clear.
        /// </summary>
        private readonly struct Metrics
        {
            public readonly float JumpHeight;
            public readonly float RiseSeconds;
            public readonly float FallSeconds;
            public readonly float AirSeconds;
            public readonly float RunJumpDistance;
            public readonly float WalkJumpDistance;
            public readonly float CrouchClearance;
            public readonly float StandClearance;

            private Metrics(MovementTuningData t)
            {
                JumpHeight = t.JumpHeight;

                // Closed form: rise until gravity cancels the launch, then fall back the same height.
                RiseSeconds = t.JumpVelocity / t.RiseGravity;
                FallSeconds = Mathf.Sqrt(2f * t.JumpHeight / t.FallGravity);
                AirSeconds = RiseSeconds + FallSeconds;

                RunJumpDistance = t.RunSpeed * AirSeconds;
                WalkJumpDistance = t.WalkSpeed * AirSeconds;

                CrouchClearance = t.CrouchHeight;
                StandClearance = t.StandHeight;
            }

            public static Metrics From(MovementTuning asset) => new Metrics(asset.Data);

            public override string ToString() =>
                $"  jump height      {JumpHeight:F2} m\n" +
                $"  airtime          {AirSeconds:F3} s  (rise {RiseSeconds:F3} + fall {FallSeconds:F3})\n" +
                $"  jump distance    {RunJumpDistance:F2} m running / {WalkJumpDistance:F2} m walking\n" +
                $"  clearance        {StandClearance:F2} m standing / {CrouchClearance:F2} m crouched";
        }

        // ------------------------------------------------------------------ construction

        private static void BuildContents(MovementTuning tuning, Metrics m)
        {
            FloorSpans.Clear();
            _lowestFloorY = 0f;

            var geometry = new GameObject("Geometry").transform;
            var markers = new GameObject("Markers").transform;

            CreateLighting();

            const float start = -10f;
            float cursor = start;

            // --- flat run-up: long enough to reach top speed and feel the acceleration curve
            cursor = Ground(geometry, "Ground_RunUp", cursor, 22f);

            // --- step ledges at fractions of the jump height. The 0.9 step is the honest test:
            //     clearable, but only just, so a jump that is silently short shows up here.
            cursor = Steps(geometry, m, cursor, new[] { 0.35f, 0.65f, 0.9f });

            // --- gap jumps at fractions of the running jump distance
            cursor = Gaps(geometry, m, cursor, new[] { 0.55f, 0.75f, 0.9f });

            // --- crouch tunnel: passable crouched, refused standing
            cursor = CrouchTunnel(geometry, tuning, cursor);

            // --- one-way platform: jump up through it, land on top
            cursor = OneWay(geometry, m, cursor);

            // --- the pool: chest-deep shelf, head-under basin, a step back out
            cursor = WaterBasin(geometry, tuning, cursor);

            // --- the door: everything west of here (the pool included) is room 1, the rest
            //     room 2. Crossing it is the room system's whole demo — the walk-through
            //     drives the camera slide and room-scoped state (Buoyancy's float) drops.
            //
            //     The door stands in the middle of its own LANDING PAD (Matt's rule: a
            //     committed crossing must deliver onto flat ground, never into a drop) — a
            //     dedicated flat segment, so both exits land on level floor with room to
            //     take the pad back.
            float padStart = cursor;
            cursor = Ground(geometry, "Ground_DoorPad", cursor, 7f);
            float doorX = padStart + 3.5f;
            CreateRoomDoor(geometry, doorX);

            // --- a chimney that can only be climbed by wall jumping
            cursor = WallJumpChimney(geometry, m, cursor);

            // --- a ledge too high to land on, low enough to catch
            cursor = LedgeChallenge(geometry, m, cursor);

            // --- a ladder bolted to the wall it exists to get you over
            cursor = LadderShaft(geometry, cursor);

            // --- a rope hanging through the platform it is tied to
            cursor = RopeSection(geometry, cursor);

            // --- a wall to run into, so wall-stop is visible rather than inferred
            float end = Wall(geometry, cursor);

            // Markers go last, because where they stand is derived from what was built: the spawn
            // sits on real floor nearest the level's midpoint, and the portals bracket the course.
            float spawnX = SpawnX(start, end);
            CreateDescriptorAndSpawn(markers, tuning, spawnX, spawnX < doorX ? 1 : 2);
            CreatePortals(markers, start, end);
            CreateRoomBounds(markers, tuning, start, doorX, end);
        }

        /// <summary>
        /// Where the player starts: on solid ground, as close to the middle of the course as the
        /// floors allow.
        ///
        /// <para>The naive answer — the arithmetic midpoint — lands wherever it lands, and half
        /// the course is gaps, steps and shafts. So the midpoint is snapped into the nearest
        /// recorded ground-level floor span, which keeps "the middle of the level" true after any
        /// retune reshuffles what is underfoot there.</para>
        /// </summary>
        private static float SpawnX(float start, float end)
        {
            const float margin = 1f;
            float mid = (start + end) * 0.5f;

            float bestX = start + margin;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < FloorSpans.Count; i++)
            {
                var span = FloorSpans[i];
                if (span.y - span.x < margin * 2f) continue;   // too narrow to stand in

                float x = Mathf.Clamp(mid, span.x + margin, span.y - margin);
                float distance = Mathf.Abs(x - mid);

                if (distance >= bestDistance) continue;

                bestDistance = distance;
                bestX = x;
            }

            return bestX;
        }

        /// <summary>
        /// A portal at each end of the course, both leading to the overworld.
        ///
        /// <para>West sits on the run-up's edge; east on the last stretch of floor, in front of
        /// the end wall. Both return to WHERE THE PLAYER LEFT the overworld (the @return spawn):
        /// this scene is the encounter arena, and Zelda II's rule is that the fight interrupts
        /// the journey — you resume your map spot, your terrace, your bridge deck. The earlier
        /// door model (different ends, different overworld doors) lost the player's position on
        /// every encounter, and lost their SURFACE once bridges and caves existed.</para>
        /// </summary>
        private static void CreatePortals(Transform parent, float start, float end)
        {
            PortalAt(parent, "Portal_West", start + 1.6f,
                     GrayBoxOverworldBuilder.SceneName, SceneDirector.ReturnSpawnId);

            // The end wall occupies the last two metres; the portal stands on the floor before it.
            PortalAt(parent, "Portal_East", end - 4f,
                     GrayBoxOverworldBuilder.SceneName, SceneDirector.ReturnSpawnId);
        }

        /// <summary>
        /// A portal the side-scroll camera can read: an upright slab in the portal colour, with
        /// the volume sized a touch past it. No collider — the sim must not bake a wall across
        /// the course, and the portal tests the player's feet itself.
        /// </summary>
        private static void PortalAt(Transform parent, string name, float x,
                                     string targetScene, string targetSpawnId)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.localScale = new Vector3(1.4f, 2.4f, 0.4f);
            slab.transform.position = new Vector3(x, 1.2f, 0f);

            Object.DestroyImmediate(slab.GetComponent<Collider>());
            slab.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Portal();

            var portal = slab.AddComponent<Portal>();
            SetPrivate(portal, "_targetScene", targetScene);
            SetPrivate(portal, "_targetSpawnId", targetSpawnId);
            SetPrivate(portal, "_halfExtents", new Vector3(0.9f, 1.4f, 1.5f));
        }

        private static void CreateLighting()
        {
            var light = new GameObject("Directional Light");
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var component = light.AddComponent<Light>();
            component.type = LightType.Directional;
            component.intensity = 1.1f;
            component.shadows = LightShadows.Soft;
        }

        /// <summary>
        /// The door between rooms 1 and 2: a thin trigger spanning the full jumpable height,
        /// because a crossing the volume misses is a room change the sim never sees — plus a
        /// VISIBLE frame, because a door the player cannot see is a seam, not a door (Matt's
        /// first look found no door anywhere). Two amber posts flank the lane in front and
        /// behind, a lintel crosses overhead, all collider-stripped: the frame is vocabulary,
        /// never a wall.
        /// </summary>
        private static void CreateRoomDoor(Transform parent, float x) =>
            GrayBoxDoors.Doorway(parent, "Door_PoolToEast", x, 1, 2, Depth);

        /// <summary>
        /// Each room's framing limits. Room 1 owns the pool, so its camera floor reaches the
        /// basin; room 2 is ordinary ground, so its floor sits one lane below it. Both
        /// horizontal clamps STOP at the door: neither screen shows past it until the
        /// committed walk-through pans one into the other.
        /// </summary>
        private static void CreateRoomBounds(Transform markers, MovementTuning tuning,
                                             float start, float doorX, float end)
        {
            GrayBoxDoors.Bounds(markers, 1, _lowestFloorY - tuning.Data.LaneHeight, 40f,
                                start - 6f, doorX);
            GrayBoxDoors.Bounds(markers, 2, -tuning.Data.LaneHeight, 40f, doorX, end + 6f);
        }

        private static void CreateDescriptorAndSpawn(Transform markers, MovementTuning tuning,
                                                     float spawnX, int spawnRoom)
        {
            var spawnObject = new GameObject("Spawn_" + CentreSpawnId);
            spawnObject.transform.SetParent(markers, false);
            spawnObject.transform.position = new Vector3(spawnX, 0f, 0f);

            var spawn = spawnObject.AddComponent<SpawnPoint>();
            SetPrivate(spawn, "_id", CentreSpawnId);
            SetPrivate(spawn, "_facing", 1);
            SetPrivate(spawn, "_room", spawnRoom);

            var descriptorObject = new GameObject("SceneDescriptor");
            descriptorObject.transform.SetParent(markers, false);

            var descriptor = descriptorObject.AddComponent<SceneDescriptor>();
            SetPrivate(descriptor, "_space", MovementSpace.SideScroll);
            SetPrivate(descriptor, "_defaultSpawn", spawn);
            SetPrivate(descriptor, "_displayName", "Gray Box — Traversal");

            // Comfortably below the lowest floor. Missing a gap should cost a couple of seconds,
            // not a play-mode restart — otherwise nobody iterates on jump feel, which is the whole
            // reason this course exists.
            SetPrivate(descriptor, "_killPlaneEnabled", true);
            SetPrivate(descriptor, "_killPlaneY", -25f);

            // One lane below the LOWEST floor any section built — the pool basin dug below
            // ground level, and a camera floor pinned to ground level stranded the sinking
            // player at the frame's bottom edge (the camera "not following" a descent). The
            // clamp only matters when descending, so ground-level play frames exactly as
            // before.
            SetPrivate(descriptor, "_useCameraBounds", true);
            SetPrivate(descriptor, "_cameraFloorY", _lowestFloorY - tuning.Data.LaneHeight);
            SetPrivate(descriptor, "_cameraCeilingY", 40f);
        }

        /// <summary>Flat ground from <paramref name="startX"/>, returning the X it ends at.</summary>
        private static float Ground(Transform parent, string name, float startX, float length,
                                    float topY = 0f)
        {
            Box(parent, name,
                new Vector2(startX, topY - GroundThickness),
                new Vector2(startX + length, topY));

            // Only ground-level floor counts as somewhere to place a marker — a raised walkway is
            // walkable but arriving on one would skip the course it belongs to.
            if (topY == 0f) FloorSpans.Add(new Vector2(startX, startX + length));

            return startX + length;
        }

        private static float Steps(Transform parent, Metrics m, float startX, float[] heightFractions)
        {
            const float tread = 3f;
            float x = startX;

            for (int i = 0; i < heightFractions.Length; i++)
            {
                float top = m.JumpHeight * heightFractions[i];

                Box(parent, $"Step_{Mathf.RoundToInt(heightFractions[i] * 100f)}pc",
                    new Vector2(x, -GroundThickness),
                    new Vector2(x + tread, top));

                x += tread;
            }

            // A landing shelf at the top so the last step is a place to stand, not a ledge to fall off.
            float shelfTop = m.JumpHeight * heightFractions[heightFractions.Length - 1];
            Box(parent, "Step_Shelf",
                new Vector2(x, -GroundThickness),
                new Vector2(x + 4f, shelfTop));

            return x + 4f;
        }

        private static float Gaps(Transform parent, Metrics m, float startX, float[] distanceFractions)
        {
            const float platform = 4f;
            const float top = 0f;   // deliberately flat: these gaps test distance, not height
            float x = startX;

            for (int i = 0; i < distanceFractions.Length; i++)
            {
                float gap = m.RunJumpDistance * distanceFractions[i];
                x += gap;

                Box(parent, $"Gap_{Mathf.RoundToInt(distanceFractions[i] * 100f)}pc_Landing",
                    new Vector2(x, top - GroundThickness),
                    new Vector2(x + platform, top));

                x += platform;
            }

            return x;
        }

        private static float CrouchTunnel(Transform parent, MovementTuning tuning, float startX)
        {
            const float length = 6f;
            float gapHeight = tuning.Data.CrouchHeight + 0.15f;

            Ground(parent, "Tunnel_Floor", startX, length);

            Box(parent, "Tunnel_Ceiling",
                new Vector2(startX, gapHeight),
                new Vector2(startX + length, gapHeight + 2f));

            return startX + length;
        }

        /// <summary>
        /// The two kinds of platform, side by side, so the difference is something you can feel
        /// rather than read.
        ///
        /// <para>The first is pass-through: jump up onto it, hold down, drop back off. The second
        /// is strictly one-way — you can still get up onto it, but holding down does nothing, the
        /// way the sealed floor of an area you climbed into should behave.</para>
        /// </summary>
        private static float OneWay(Transform parent, Metrics m, float startX)
        {
            const float length = 14f;
            float x = Ground(parent, "OneWay_Floor", startX, length);

            float platformY = m.JumpHeight * 0.6f;

            var droppable = Box(parent, "Platform_PassThrough",
                new Vector2(startX + 2f, platformY),
                new Vector2(startX + 6f, platformY + 0.3f));
            var droppableComponent = droppable.gameObject.AddComponent<OneWayPlatform>();
            SetPrivate(droppableComponent, "_allowDropThrough", true);

            var sealedPlatform = Box(parent, "Platform_OneWayOnly",
                new Vector2(startX + 8f, platformY),
                new Vector2(startX + 12f, platformY + 0.3f));
            var sealedComponent = sealedPlatform.gameObject.AddComponent<OneWayPlatform>();
            SetPrivate(sealedComponent, "_allowDropThrough", false);

            return x;
        }

        /// <summary>
        /// A rope hanging from a pass-through platform.
        ///
        /// <para>Anchoring is the point. The rope is tied to a platform that can be passed
        /// through, so climbing it leads somewhere — up through the platform and onto it — and
        /// coming back down is a matter of dropping through or mounting the rope downward. A rope
        /// hanging from solid ceiling would be a ride to nowhere.</para>
        ///
        /// <para>It reaches down to within standing height, so it can be grabbed from the floor
        /// rather than only from above.</para>
        /// </summary>
        private static float RopeSection(Transform parent, float startX)
        {
            const float length = 14f;
            const float platformY = 6f;

            float x = Ground(parent, "Rope_Floor", startX, length);

            var platform = Box(parent, "Rope_Platform",
                new Vector2(startX + 2f, platformY),
                new Vector2(startX + 12f, platformY + 0.3f));

            var platformComponent = platform.gameObject.AddComponent<OneWayPlatform>();
            SetPrivate(platformComponent, "_allowDropThrough", true);

            float ropeX = startX + 6f;

            // Runs a little past the platform's surface on purpose: climbing off the top has to
            // leave the feet ABOVE the platform, or the character dismounts just underneath it
            // and falls straight back down.
            var rope = Box(parent, "Rope",
                new Vector2(ropeX, 1.2f),
                new Vector2(ropeX + 1.2f, platformY + 0.9f));

            var ropeCollider = rope.GetComponent<Collider>();
            if (ropeCollider != null) ropeCollider.isTrigger = true;

            var ropeVolume = rope.gameObject.AddComponent<LadderVolume>();
            SetPrivate(ropeVolume, "_kind", ClimbableKind.Rope);

            return x;
        }

        /// <summary>
        /// A chimney with no way up except wall jumping.
        ///
        /// <para>The near wall floats, leaving a gap to walk in under, so the player arrives
        /// <i>inside</i> the shaft rather than having to clear it first. The far wall then blocks
        /// forward progress entirely — the only exit is upward, alternating between the two faces.
        /// Making it a gate rather than a shortcut is the point: if wall jumping is broken, this
        /// section is impassable and you find out immediately.</para>
        /// </summary>
        /// <summary>
        /// The water dictionary entry: one pool, two depths, both derived from the body.
        ///
        /// <para>The shallow shelf is chest-deep — head out, breath safe, a slow trudge that
        /// teaches the drag. The basin is head-under at twice the stand height, which is where
        /// the breath timer becomes the hazard; the way out is a mid-depth step whose rises fit
        /// the water-boosted jump, or Mirefen's Buoyancy fired from the floor. The waterline is
        /// FLUSH with the deck, so the water-walk steps onto land without a seam.</para>
        ///
        /// <para>The visible water is a backdrop slab BEHIND the play plane plus a thin surface
        /// strip — an opaque box on the plane would hide the swimmer inside it. The sim's water
        /// is the invisible trigger, exactly as ladders are.</para>
        /// </summary>
        private static float WaterBasin(Transform parent, MovementTuning tuning, float startX)
        {
            float stand = tuning.Data.StandHeight;

            float shallowDepth = stand * 0.66f;       // 1.19 at 1.8: chest-deep
            float deepDepth = stand * 2.1f;           // 3.78 at 1.8: head well under
            float stepDepth = stand;                  // the way out, one boosted jump at a time

            const float shallowLength = 5f;
            const float deepLength = 7f;
            const float stepLength = 2f;

            // FLUSH with the deck, deliberately (Matt): the water-walk must step straight
            // onto land where they meet. A waterline even 0.1 below the bank is a lip the
            // sweep treats as a wall, and the art ends at an invisible kerb.
            const float surfaceY = 0f;

            float x = startX;
            float length = shallowLength + deepLength + stepLength;

            // The shelf. Its box reaches down to the basin floor so its far face is the
            // basin's near wall — a thinner shelf would leave a slit into the void below.
            Box(parent, "Water_ShallowShelf",
                new Vector2(x, -deepDepth - GroundThickness),
                new Vector2(x + shallowLength, -shallowDepth));

            Box(parent, "Water_BasinFloor",
                new Vector2(x + shallowLength, -deepDepth - GroundThickness),
                new Vector2(x + shallowLength + deepLength, -deepDepth));

            _lowestFloorY = Mathf.Min(_lowestFloorY, -deepDepth);

            Box(parent, "Water_ExitStep",
                new Vector2(x + shallowLength + deepLength, -deepDepth - GroundThickness),
                new Vector2(x + length, -stepDepth));

            // The water itself: a trigger spanning the whole basin up to the waterline. No
            // renderer — the sim's water, like the sim's ladders, is a region, not a thing.
            var trigger = new GameObject("Water_Volume");
            trigger.transform.SetParent(parent, false);
            trigger.transform.position = new Vector3(x + length * 0.5f,
                                                     (surfaceY - deepDepth) * 0.5f, 0f);
            var box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(length, deepDepth + surfaceY, Depth);   // floor to waterline
            trigger.AddComponent<WaterVolume>();

            // What the water looks like: a slab behind the lane and a strip at the surface.
            var backdrop = Box(parent, "Water_Backdrop",
                               new Vector2(x, -deepDepth),
                               new Vector2(x + length, surfaceY));
            backdrop.localScale = new Vector3(backdrop.localScale.x, backdrop.localScale.y, 0.6f);
            backdrop.position = new Vector3(backdrop.position.x, backdrop.position.y,
                                            Depth * 0.5f + 0.35f);
            StripCollider(backdrop);
            backdrop.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Water();

            var strip = Box(parent, "Water_Surface",
                            new Vector2(x, surfaceY - 0.04f),
                            new Vector2(x + length, surfaceY));
            StripCollider(strip);
            strip.GetComponent<MeshRenderer>().sharedMaterial = GrayBoxMaterials.Water();

            return x + length;
        }

        /// <summary>Visual-only boxes must not bake: a collider on the backdrop is a wall
        /// behind the lane, and one on the surface strip is a lid over the pool.</summary>
        private static void StripCollider(Transform visual)
        {
            var collider = visual.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }

        private static float WallJumpChimney(Transform parent, Metrics m, float startX)
        {
            const float shaftHeight = 10f;
            const float gap = 2.4f;            // wide enough for the body, tight enough to cross
            const float exitLength = 8f;

            float nearWallX = startX + 3f;
            float farWallX = nearWallX + 1f + gap;

            Ground(parent, "Chimney_Floor", startX, farWallX + 1f - startX);

            // Floats above head height so the player can walk in beneath it.
            Box(parent, "Chimney_NearWall",
                new Vector2(nearWallX, m.StandClearance + 0.7f),
                new Vector2(nearWallX + 1f, shaftHeight));

            Box(parent, "Chimney_FarWall",
                new Vector2(farWallX, 0f),
                new Vector2(farWallX + 1f, shaftHeight));

            // Land on top of the far wall and carry on from there.
            Box(parent, "Chimney_Exit",
                new Vector2(farWallX, shaftHeight),
                new Vector2(farWallX + exitLength, shaftHeight + 0.4f));

            return farWallX + exitLength;
        }

        /// <summary>
        /// A shelf placed in the band that is grabbable but not landable.
        ///
        /// <para>Its top sits above the jump apex — so it cannot simply be jumped onto — but
        /// within reach of the hands on the way down, which is precisely the window a ledge grab
        /// exists to fill. Deriving both bounds from the same numbers the sim integrates means the
        /// section stays exactly that awkward after a retune, instead of quietly becoming a step.</para>
        /// </summary>
        private static float LedgeChallenge(Transform parent, Metrics m, float startX)
        {
            const float shelfLength = 9f;

            float runUp = Ground(parent, "Ledge_Approach", startX, 10f);

            // Above the apex (unlandable), below apex + the reach band (grabbable).
            float apex = m.JumpHeight;
            float shelfTop = apex + 1.3f;

            Box(parent, "Ledge_Shelf",
                new Vector2(runUp, shelfTop - 6f),
                new Vector2(runUp + shelfLength, shelfTop));

            return runUp + shelfLength;
        }

        /// <summary>
        /// A ladder to a platform placed deliberately out of jumping range.
        ///
        /// <para>The volume is a <b>trigger</b>, which is what keeps it climbable rather than
        /// solid — a ladder you collide with is a wall across the corridor. It also runs slightly
        /// past the platform's surface so climbing off the top leaves the feet above it rather
        /// than inside it.</para>
        /// </summary>
        /// <summary>
        /// A ladder fixed against a wall, which is the only way a ladder should ever exist.
        ///
        /// <para>The wall is what the ladder is <i>for</i>: it blocks the route at ground level,
        /// and the ladder is the way over it. Building the pair together means the section cannot
        /// end up with a ladder floating in open air — which still works mechanically and is
        /// exactly the kind of thing that survives to a build because nothing about it errors.</para>
        /// </summary>
        private static float LadderShaft(Transform parent, float startX)
        {
            const float wallHeight = 8f;
            const float ladderWidth = 1.2f;

            float floorEnd = Ground(parent, "Ladder_Floor", startX, 18f);

            float wallX = startX + 4f;
            float ladderX = wallX - ladderWidth;

            // The wall the ladder is bolted to, and the reason it is there at all.
            Box(parent, "Ladder_Wall",
                new Vector2(wallX, 0f),
                new Vector2(wallX + 1f, wallHeight));

            // Walkway on top, reached by the climb.
            Box(parent, "Ladder_Walkway",
                new Vector2(wallX, wallHeight),
                new Vector2(wallX + 11f, wallHeight + 0.4f));

            // Past the walkway surface, so climbing off the top puts the feet on it.
            var ladder = Box(parent, "Ladder",
                new Vector2(ladderX, 0f),
                new Vector2(ladderX + ladderWidth, wallHeight + 1f));

            var collider = ladder.GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;

            var volume = ladder.gameObject.AddComponent<LadderVolume>();
            SetPrivate(volume, "_kind", ClimbableKind.Ladder);

            return floorEnd;
        }

        private static float Wall(Transform parent, float startX)
        {
            Ground(parent, "Ground_End", startX, 8f);

            Box(parent, "Wall_End",
                new Vector2(startX + 8f, 0f),
                new Vector2(startX + 10f, 6f));

            return startX + 10f;
        }

        /// <summary>An axis-aligned box spanning min..max in the XY play plane.</summary>
        private static Transform Box(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);

            var size = max - min;
            cube.transform.localScale = new Vector3(size.x, size.y, Depth);
            cube.transform.position = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);

            return cube.transform;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>True (and complains) if any open scene has unsaved edits.</summary>
        private static bool HasUnsavedChanges()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;

                Debug.LogError($"[Prophecy] '{(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name)}' " +
                               "has unsaved changes. Save or discard them, then run this again — " +
                               "generating replaces the open scene.");
                return true;
            }

            return false;
        }

        private static MovementTuning LoadTuning()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MovementTuning>(ProphecyAssetBootstrap.MovementTuningPath);

            if (asset == null)
                Debug.LogError($"[Prophecy] No MovementTuning at {ProphecyAssetBootstrap.MovementTuningPath}. " +
                               "Run Prophecy > Build > Create Missing Data Assets first — the course " +
                               "is derived from it and cannot be built without it.");

            return asset;
        }

        /// <summary>
        /// Writes a private <c>[SerializeField]</c>. The alternative is making every world
        /// component's fields public purely so a generator can reach them, which would trade a
        /// contained bit of editor reflection for a permanently looser runtime API.
        /// </summary>
        private static void SetPrivate(Object target, string fieldName, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[Prophecy] {target.GetType().Name} has no serialized field '{fieldName}'.");
                return;
            }

            switch (value)
            {
                case string s: property.stringValue = s; break;
                case int i: property.intValue = i; break;
                case float f: property.floatValue = f; break;
                case bool b: property.boolValue = b; break;
                // enumValueFlag rather than enumValueIndex: it carries the declared value, which
                // is what a [Flags] enum like MovementSpace needs and what a plain one still gets right.
                case System.Enum e: property.enumValueFlag = System.Convert.ToInt32(e); break;
                case Vector3 v: property.vector3Value = v; break;
                case Object o: property.objectReferenceValue = o; break;
                default:
                    Debug.LogError($"[Prophecy] Unsupported field type for '{fieldName}'.");
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
