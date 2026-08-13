using System.Collections.Generic;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.World;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// Zelda II's overworld menace: things pop up around the player and wander, weighted toward
    /// them — and touching one carries you into the side-scroll world. This spawns the wanderers,
    /// retires the distant, and springs the encounter; what a wanderer <i>does</i> between those
    /// moments is its GOAP brain's business.
    ///
    /// <para><b>The provinces do the tuning.</b> This component owns no wanderer prefab, no
    /// cadence, no cap: the PLAYER's cell's <see cref="OverworldProvince"/> supplies all three,
    /// so crossing a border changes the menace without a scene change. No province, or a
    /// province with an empty table, is a safe place — the clock ticks and nothing comes.
    /// Contact is the mirror image, resolved from the CONTACT cell: a road gives the safe
    /// crossing, anything else the province's own section
    /// (<see cref="OverworldEncounterRules.ResolveContact"/>). It lives in the overworld
    /// assembly now, because provinces are a grid question — the seam that keeps the sim off
    /// the grid was never meant to keep the overworld's own presentation off it.</para>
    ///
    /// <para><b>The touch is a walking portal, and it is resolved like one.</b> Same feet-in-a-
    /// volume test the <see cref="Portal"/> does, presentation-side, because an encounter is a
    /// scene transition and not a combat outcome — the wanderer deals no damage, exactly as
    /// Zelda II's map blobs deal none. Routing it through the combat contact system was the
    /// earlier placeholder, and it had the wrong grammar: chip damage punishes the touch, where
    /// an encounter <i>answers</i> it.</para>
    ///
    /// <para><b>No arming rule needed, unlike the portal.</b> A portal pair can spawn you inside
    /// its partner's volume; an encounter cannot — the transition unloads this scene, wanderers
    /// die with it, and a fresh overworld starts empty with the first spawn seconds away and a
    /// ring away. The geometry that made ping-pong possible for portals does not exist here.</para>
    ///
    /// <para><b>Spawns land just outside the camera's frame, on ground the grid approves.</b>
    /// Nearer, and things materialise on screen, which reads as a bug rather than an ambient
    /// world; farther, and the player never meets them. A few bearings are tried against
    /// <see cref="OverworldEncounterRules.CanHostAWanderer"/> — all refused (player hugging a
    /// coast, deep in greeble country) simply skips the beat. Retirement is the same ring from
    /// the other side: past the far radius a wanderer is walking scenery nobody can see, so it
    /// is recycled into the budget.</para>
    /// </summary>
    public sealed class OverworldEncounterSpawner : MonoBehaviour
    {
        [SerializeField, Tooltip("The compiled world this spawner reads provinces and ground " +
                                 "from. Found in the scene when left empty.")]
        private OverworldGridHost _gridHost;

        [SerializeField, Tooltip("Distance from the player a wanderer appears at, in metres. " +
                                 "Just past what the overworld camera shows.")]
        private float _spawnRadius = 14f;

        [SerializeField, Tooltip("Distance beyond which a wanderer is retired and its slot " +
                                 "returned to the budget.")]
        private float _despawnRadius = 30f;

        [Header("The contact")]
        [SerializeField, Tooltip("Scene a contact on a ROAD cell carries the player to — the " +
                                 "Zelda II safe crossing, whatever province the road runs through.")]
        private string _roadScene;

        [SerializeField, Tooltip("SpawnPoint id to arrive at for the road crossing.")]
        private string _roadSpawnId = "default";

        [SerializeField, Tooltip("Scene for a contact outside any province, or in a province " +
                                 "with no scene of its own. Empty = such contacts do nothing.")]
        private string _fallbackScene;

        [SerializeField, Tooltip("SpawnPoint id for the fallback scene.")]
        private string _fallbackSpawnId = "default";

        [SerializeField, Tooltip("How close a wanderer must get, in metres on the plane. About " +
                                 "a body's width — touching, not near.")]
        private float _touchRadius = 0.9f;

        [SerializeField, Tooltip("A touch only counts on the same floor: the feet must be within " +
                                 "this height of the wanderer. Half a terrace step, so a body on " +
                                 "a bridge deck is out of reach of the road below it.")]
        private float _touchHeightTolerance = 1f;

        /// <summary>Spawn-clock beat while outside any province — how often "is this still a
        /// safe place?" is re-asked, NOT a spawn rate: wilderness never spawns. Short, so a
        /// border crossing is noticed promptly.</summary>
        private const float WildernessRecheckSeconds = 2f;

        /// <summary>Bearings tried per spawn beat before conceding it.</summary>
        private const int SpawnTries = 8;

        private readonly List<GameObject> _alive = new List<GameObject>();
        private float _nextSpawnAt;

        private void Start()
        {
            // One interval of grace before the first spawn can land. Without it the first
            // Update spawned on frame one — biased toward the arrival spawn — and it caught
            // the still-orienting player about three seconds after every entry. The overworld
            // read as broken when it was actually being lethal.
            _nextSpawnAt = Time.time + Mathf.Max(WildernessRecheckSeconds, CadenceHere());
        }

        private void Update()
        {
            // The player from the locator; the director keeps only the question that is
            // genuinely scene-flow's — whether a transition is in flight.
            var player = Presentation.PlayerLocator.Current;
            var director = SceneDirector.Instance;
            if (player == null || (director != null && director.IsTransitioning)) return;

            var grid = Grid();
            if (grid == null) return;

            var feet = player.FeetWorldPosition;

            if (TouchSpringsTheEncounter(director, grid, feet)) return;

            Retire(feet);

            if (Time.time < _nextSpawnAt) return;

            var province = ProvinceUnder(grid, feet);
            _nextSpawnAt = Time.time + (province != null
                ? Mathf.Max(0.5f, province.SpawnEverySeconds)
                : WildernessRecheckSeconds);

            if (province == null || _alive.Count >= province.MaxAlive) return;

            var prefab = OverworldEncounterRules.PickWanderer(province.Wanderers, Random.value);
            if (prefab == null) return;   // the safe-province case, not a failure

            Spawn(grid, feet, prefab);
        }

        private OverworldTileGrid Grid()
        {
            if (_gridHost == null)
                _gridHost = FindAnyObjectByType<OverworldGridHost>();
            return _gridHost != null ? _gridHost.Grid : null;
        }

        private float CadenceHere()
        {
            var player = Presentation.PlayerLocator.Current;
            var grid = Grid();
            if (player == null || grid == null) return WildernessRecheckSeconds;

            var province = ProvinceUnder(grid, player.FeetWorldPosition);
            return province != null ? province.SpawnEverySeconds : WildernessRecheckSeconds;
        }

        private static OverworldProvince ProvinceUnder(OverworldTileGrid grid, Vector3 feet)
        {
            return grid.TryCellAt(new Vector2(feet.x, feet.z), out int x, out int z)
                ? grid.ProvinceAt(x, z)
                : null;
        }

        /// <summary>
        /// A wanderer within touching distance carries the player off to the side-scroll world.
        /// One transition per frame at most — the first touch wins and the scene swap takes this
        /// whole component with it.
        /// </summary>
        private bool TouchSpringsTheEncounter(SceneDirector director, OverworldTileGrid grid,
                                              Vector3 playerFeet)
        {
            for (int i = 0; i < _alive.Count; i++)
            {
                var wanderer = _alive[i];
                if (wanderer == null) continue;

                var delta = wanderer.transform.position - playerFeet;

                // Mostly a plane question — but not entirely, since layers arrived: a body on a
                // bridge deck is not touching the wanderer walking beneath it. Half a terrace
                // step separates "same floor" from "different floor".
                if (Mathf.Abs(delta.y) > _touchHeightTolerance) continue;
                delta.y = 0f;

                if (delta.sqrMagnitude > _touchRadius * _touchRadius) continue;

                // WHERE from the ground under the player's feet, not the wanderer's — the
                // player is the one being carried, and their footing is what "on a road" means.
                if (!grid.TryCellAt(new Vector2(playerFeet.x, playerFeet.z), out int x, out int z))
                    continue;

                OverworldEncounterRules.ResolveContact(grid, x, z,
                                                       _roadScene, _roadSpawnId,
                                                       _fallbackScene, _fallbackSpawnId,
                                                       out var scene, out var spawnId);
                if (string.IsNullOrEmpty(scene)) continue;

                director.GoTo(scene, spawnId);
                return true;
            }

            return false;
        }

        private void Spawn(OverworldTileGrid grid, Vector3 playerFeet, GameObject prefab)
        {
            // A random bearing per try, snapped to its cell and put to the grid: plain ground
            // or nothing. No clamping toward the player — a refused ring simply skips the beat,
            // which is Zelda II's rhythm anyway (the menace is a pressure, not a metronome).
            for (int attempt = 0; attempt < SpawnTries; attempt++)
            {
                float bearing = Random.Range(0f, Mathf.PI * 2f);
                var probe = new Vector2(playerFeet.x + Mathf.Cos(bearing) * _spawnRadius,
                                        playerFeet.z + Mathf.Sin(bearing) * _spawnRadius);

                if (!grid.TryCellAt(probe, out int x, out int z)) continue;
                if (!OverworldEncounterRules.CanHostAWanderer(grid, x, z)) continue;

                var centre = grid.CellCentre(x, z);
                var position = new Vector3(centre.x,
                                           grid.SurfaceHeight(x, z, centre),
                                           centre.y);

                var wanderer = Instantiate(prefab, position, Quaternion.identity);
                wanderer.name = prefab.name;

                // The composition — top-down space, the steering oracle — is the overworld's
                // knowledge, not this spawner's, so it lives where every spawn path finds it.
                OverworldWandererSetup.Apply(wanderer);

                _alive.Add(wanderer);
                return;
            }
        }

        /// <summary>Retire the distant and forget the destroyed, keeping the budget honest.</summary>
        private void Retire(Vector3 playerFeet)
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                var wanderer = _alive[i];

                if (wanderer == null)
                {
                    _alive.RemoveAt(i);
                    continue;
                }

                var delta = wanderer.transform.position - playerFeet;
                delta.y = 0f;

                if (delta.sqrMagnitude < _despawnRadius * _despawnRadius) continue;

                Destroy(wanderer);
                _alive.RemoveAt(i);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, _spawnRadius);
        }
    }
}
