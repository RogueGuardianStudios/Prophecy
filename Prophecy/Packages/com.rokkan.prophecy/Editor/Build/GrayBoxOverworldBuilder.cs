using System.IO;
using Rokkan.Prophecy.Core;
using Rokkan.Prophecy.Overworld;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.World;
using UnityEditor;
using UnityEngine;
using static Rokkan.Prophecy.Editor.Build.GrayBoxSceneScaffold;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Generates <c>GrayBox_Overworld</c> — the first top-down scene, and deliberately almost
    /// nothing: a plain, a portal back, and the camera that knows how to shoot it.
    ///
    /// <para><b>What this scene is for.</b> The overworld proper is a design problem (regions,
    /// darkening, encounters) that cannot be worked on until the <i>mode</i> works: arriving from
    /// a side-scroll scene, moving on the XZ plane under a fixed three-quarter camera, and leaving
    /// again. This is the smallest world in which that whole loop is real. Everything on the plain
    /// earns its place by exercising part of the loop; nothing here is level design.</para>
    ///
    /// <para>Known, recorded gap: top-down has no collision (<c>CharacterSim.Integrate</c> skips
    /// the sweep), so the plain's edge is a suggestion. The kill plane is off — there is nothing
    /// to fall off — so walking into the void is safe, just featureless.</para>
    /// </summary>
    public static class GrayBoxOverworldBuilder
    {
        public const string ScenePath = "Assets/_Prophecy/Scenes/GrayBox_Overworld.unity";

        /// <summary>Scene name portals target. Derived so it cannot drift from the asset.</summary>
        public static string SceneName => Path.GetFileNameWithoutExtension(ScenePath);

        // Arrival spawns, flanking the portal slab. Leaving the traversal course by different
        // ends arrives at different sides of it — the beginnings of the level occupying a place
        // on the map rather than being a menu entry.
        public const string WestSpawnId = "west";
        public const string EastSpawnId = "east";

        // The overworld's authored assets, named once — the map tool loads the same files, and
        // a re-typed path there degrades a rename to per-run warnings instead of one edit here.
        public const string MapPath = "Assets/_Prophecy/Data/OverworldMap.asset";
        public const string BiomePalettePath = "Assets/_Prophecy/Data/OverworldBiomePalette.asset";

        // The plain. Sized in metres like everything else; roomy enough that the camera's frame
        // fits well inside it from the centre, small enough that the portal is never out of sight.
        private const float PlainWidth = 64f;    // X
        private const float PlainDepth = 44f;    // Z

        [MenuItem("Prophecy/Build/Generate GrayBox_Overworld", priority = 41)]
        public static void Generate()
        {
            var tuning = LoadMovementTuning();
            if (tuning == null) return;

            if (GenerateScene(ScenePath, () => { BuildContents(tuning); return true; }))
                Debug.Log($"[Prophecy] Generated {ScenePath}");
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
                Debug.LogError($"[Prophecy] Overworld generation failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ construction

        private static void BuildContents(MovementTuning tuning)
        {
            var geometry = new GameObject("Geometry").transform;
            var markers = new GameObject("Markers").transform;

            CreateLighting();
            var map = CreateStalbergGround();

            // The host sits at the scene origin and its transform is the map's bottom-left
            // corner, so the world spans 0..BoundsSize and cell (x,z) IS world (x,z). The
            // furniture is authored around the map's CENTRE — derived, so a bounds retune
            // regenerates it back into the middle of wherever the middle now is.
            var centre = Rokkan.Prophecy.Overworld.OverworldWorldBuilder.MapCentre(map, Vector3.zero);

            CreateReturnPortal(geometry, centre);
            CreateDescriptorAndSpawns(markers, centre);
            CreateCamera(tuning);
            CreateCombatDirector();
            CreateEncounters(markers);
            CreateCameraCut(markers, centre);
        }

        /// <summary>
        /// The ground: the hand-authored map, compiled at load into the discrete tile grid and
        /// rendered from the 17-piece tile set. The map asset is created here only if missing —
        /// its whole point is to be hand-edited afterwards, so a regenerate must never flatten
        /// someone's authoring. The starter layout is an island: one big landmass with limbs,
        /// sized to keep the existing furniture (spawns, cube, spawner ring) on dry land.
        /// </summary>
        private static Rokkan.Prophecy.Overworld.OverworldMap CreateStalbergGround()
        {
            var map = AssetDatabase.LoadAssetAtPath<Rokkan.Prophecy.Overworld.OverworldMap>(MapPath);

            if (map == null)
            {
                map = ScriptableObject.CreateInstance<Rokkan.Prophecy.Overworld.OverworldMap>();
                map.Seed = 7;
                map.Spacing = 3f;
                map.Jitter = 0.35f;
                map.BoundsSize = new Vector2(PlainWidth + 32f, PlainDepth + 28f);
                map.Regions = new[]
                {
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "Heartland", Centre = Vector2.zero, Size = new Vector2(PlainWidth, PlainDepth) },
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "West Reach", Centre = new Vector2(-PlainWidth * 0.55f, 8f), Size = new Vector2(24f, 18f), RotationDegrees = 15f },
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "East Cape", Centre = new Vector2(PlainWidth * 0.55f, -6f), Size = new Vector2(20f, 16f), RotationDegrees = -20f },
                    new Rokkan.Prophecy.Overworld.AuthoredRegion
                        { Name = "North Spur", Centre = new Vector2(10f, PlainDepth * 0.55f), Size = new Vector2(18f, 14f) },
                };
                AssetDatabase.CreateAsset(map, MapPath);
            }

            var tiles = AssetDatabase.LoadAssetAtPath<Rokkan.Prophecy.Overworld.OverworldTileSet>(
                OverworldTileBuilder.TileSetPath);
            if (tiles == null)
                Debug.LogError("[Prophecy] No OverworldTileSet asset — run Prophecy > Build > " +
                               "Generate Overworld Tiles first, then regenerate the overworld.");

            var host = new GameObject("OverworldGrid");
            var component = host.AddComponent<Rokkan.Prophecy.Overworld.OverworldGridHost>();

            SetPrivate(component, "_map", map);
            SetPrivate(component, "_tiles", tiles);

            // The biome palette is optional — no asset simply means gray-box everywhere.
            var biomes = AssetDatabase.LoadAssetAtPath<Rokkan.Prophecy.Overworld.OverworldBiomePalette>(
                BiomePalettePath);
            if (biomes != null) SetPrivate(component, "_biomes", biomes);

            return map;
        }

        // The flat plain-and-rim ground this scene started with is gone: the ground is now the
        // Stålberg grid, built at load by OverworldGridHost from the hand-authored map asset.
        // The collider discipline survives inside the host — placed tiles are stripped, because
        // top-down has no collision and a floor baked into the XZ projection occludes every line
        // of sight (the lesson the plain taught).

        /// <summary>
        /// The slab that carries you to the fight — the shared portal recipe, standing proud
        /// of the plain at the map's centre. Targets the combat tester's fixed spawn; the
        /// tester's own portal comes back via <c>@return</c>, so the round trip lands where
        /// the player left.
        /// </summary>
        private static void CreateReturnPortal(Transform parent, Vector3 centre) =>
            PortalSlab(parent, "Portal_CombatTester", centre,
                       GrayBoxCombatTesterBuilder.SceneName, GrayBoxCombatTesterBuilder.DefaultSpawnId);

        private static void CreateDescriptorAndSpawns(Transform markers, Vector3 centre)
        {
            var west = Spawn(markers, WestSpawnId, centre + new Vector3(-4f, 0f, 0f), facing: 1);
            Spawn(markers, EastSpawnId, centre + new Vector3(4f, 0f, 0f), facing: -1);

            var descriptorObject = new GameObject("SceneDescriptor");
            descriptorObject.transform.SetParent(markers, false);

            var descriptor = descriptorObject.AddComponent<SceneDescriptor>();
            SetPrivate(descriptor, "_space", MovementSpace.TopDown);
            SetPrivate(descriptor, "_defaultSpawn", west);
            SetPrivate(descriptor, "_displayName", "Gray Box — Overworld");

            // Nothing to fall off, and nothing for the side-scroll camera bounds to mean.
            SetPrivate(descriptor, "_killPlaneEnabled", false);
            SetPrivate(descriptor, "_useCameraBounds", false);
        }

        private static SpawnPoint Spawn(Transform parent, string id, Vector3 position, int facing)
        {
            var spawnObject = new GameObject("Spawn_" + id);
            spawnObject.transform.SetParent(parent, false);
            spawnObject.transform.position = position;

            var spawn = spawnObject.AddComponent<SpawnPoint>();
            SetPrivate(spawn, "_id", id);
            SetPrivate(spawn, "_facing", facing);

            return spawn;
        }

        /// <summary>
        /// The scene's own camera, out-ranking the Bootstrap lane rig while this scene is loaded.
        /// Priority is the whole handover mechanism, and the rig owns it — adding the one
        /// component is the entire setup, which is what keeps this assembly free of Cinemachine.
        /// RequireComponent supplies the CinemachineCamera; the rig adds its own composer.
        /// </summary>
        private static void CreateCamera(MovementTuning tuning)
        {
            var rig = new GameObject("OverworldCamera");

            var component = rig.AddComponent<OverworldCameraRig>();
            SetPrivate(component, "_tuning", tuning);
        }

        /// <summary>
        /// The fight, such as it is. Not for combat — nothing here swings — but perception runs
        /// through the combat registry: a wanderer finds the player by querying the fight's
        /// hurtboxes, and contact (its only threat) resolves through the same OnHit as everything
        /// else. No director, and every wanderer is blind.
        /// </summary>
        private static void CreateCombatDirector()
        {
            var director = new GameObject("CombatDirector");
            var component = director.AddComponent<CombatDirector>();
            SetPrivate(component, "_space", MovementSpace.TopDown);
        }

        /// <summary>
        /// Cutaway style 3's demo: crossing the Eastwater Bridge eases the camera into a
        /// low, yawed river shot — the deck, the channel, the falls — and hands back on
        /// stepping off. The zone self-assembles its Cinemachine shot from the pose numbers
        /// (this assembly deliberately knows nothing of Cinemachine, same as with the rigs);
        /// the yaw is the rig doc's sanctioned exception: an authored set-piece.
        /// </summary>
        private static void CreateCameraCut(Transform markers, Vector3 centre)
        {
            var zone = new GameObject("CameraCut_EastwaterBridge");
            zone.transform.SetParent(markers, false);
            zone.transform.position = centre + new Vector3(12.7f, 0f, -4.5f);

            var cut = zone.AddComponent<CameraCutZone>();
            SetPrivate(cut, "_halfExtents", new Vector3(3.2f, 2f, 1.6f));
            SetPrivate(cut, "_blendSeconds", 0.7f);
            SetPrivate(cut, "_shotLocalPosition", new Vector3(-7f, 4.5f, -9f));
            SetPrivate(cut, "_shotEulerAngles", new Vector3(24f, 38f, 0f));
            SetPrivate(cut, "_shotFieldOfView", 38f);
        }

        /// <summary>The Zelda II layer: wanderers popping up around the player.</summary>
        private static void CreateEncounters(Transform markers)
        {
            var spawnerObject = new GameObject("OverworldEncounters");
            spawnerObject.transform.SetParent(markers, false);

            var spawner = spawnerObject.AddComponent<OverworldEncounterSpawner>();

            // ON again (2026-08-05): safety became an authored property. The spawner reads the
            // player's province — WHAT wanders, how often, how many — and the provinces around
            // the arrival spawn have empty tables, so walking the map to judge coasts stays
            // undisturbed. The menace exists exactly where a province table says it does.
            SetPrivate(spawner, "_gridHost", Object.FindAnyObjectByType<OverworldGridHost>());

            // WHERE a contact carries you is the contact cell's business: road cells give the
            // safe crossing, provinces name their own sections, and both point at the traversal
            // course until real battle scenes exist. Re-point these strings and the province
            // assets when they do.
            SetPrivate(spawner, "_roadScene", GrayBoxTraversalBuilder.SceneName);
            SetPrivate(spawner, "_roadSpawnId", GrayBoxTraversalBuilder.CentreSpawnId);
            SetPrivate(spawner, "_fallbackScene", GrayBoxTraversalBuilder.SceneName);
            SetPrivate(spawner, "_fallbackSpawnId", GrayBoxTraversalBuilder.CentreSpawnId);
        }
    }
}
