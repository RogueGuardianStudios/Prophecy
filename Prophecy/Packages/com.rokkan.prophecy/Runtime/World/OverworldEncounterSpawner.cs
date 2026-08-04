using System.Collections.Generic;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// Zelda II's overworld menace: things pop up around the player and wander, weighted toward
    /// them — and touching one carries you into the side-scroll world. This spawns the wanderers,
    /// retires the distant, and springs the encounter; what a wanderer <i>does</i> between those
    /// moments is its GOAP brain's business.
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
    /// <para><b>Spawns land just outside the camera's frame.</b> Nearer, and things materialise
    /// on screen, which reads as a bug rather than an ambient world; farther, and the player
    /// never meets them. Retirement is the same ring from the other side: past the far radius a
    /// wanderer is walking scenery nobody can see, so it is recycled into the budget.</para>
    /// </summary>
    public sealed class OverworldEncounterSpawner : MonoBehaviour
    {
        [SerializeField, Tooltip("What wanders. Expected to carry a PlayerCharacterHost and a " +
                                 "GOAP brain — the Wanderer capsule.")]
        private GameObject _wandererPrefab;

        [SerializeField, Tooltip("How many may exist at once. Zelda II fields two or three.")]
        private int _maxAlive = 3;

        [SerializeField, Tooltip("Seconds between spawn attempts. An attempt does nothing while " +
                                 "the field is full.")]
        private float _spawnEverySeconds = 4f;

        [SerializeField, Tooltip("Distance from the player a wanderer appears at, in metres. " +
                                 "Just past what the overworld camera shows.")]
        private float _spawnRadius = 14f;

        [SerializeField, Tooltip("Distance beyond which a wanderer is retired and its slot " +
                                 "returned to the budget.")]
        private float _despawnRadius = 30f;

        [SerializeField, Tooltip("Half extents of the plain, in metres. Spawns are clamped " +
                                 "inside; the builder sizes this from the ground it generated.")]
        private Vector2 _plainHalfExtents = new Vector2(30f, 20f);

        [SerializeField, Tooltip("Margin kept from the plain's edge, so nothing appears " +
                                 "standing in the rim.")]
        private float _edgeMargin = 2f;

        [Header("The encounter")]
        [SerializeField, Tooltip("Side-scroll scene a touch carries the player to.")]
        private string _encounterScene;

        [SerializeField, Tooltip("SpawnPoint id to arrive at over there.")]
        private string _encounterSpawnId = "default";

        [SerializeField, Tooltip("How close a wanderer must get, in metres on the plane. About " +
                                 "a body's width — touching, not near.")]
        private float _touchRadius = 0.9f;

        private readonly List<GameObject> _alive = new List<GameObject>();
        private float _nextSpawnAt;

        private void Update()
        {
            var director = SceneDirector.Instance;
            var player = director != null ? director.Player : null;
            if (player == null || director.IsTransitioning) return;

            var feet = SpaceMapping.ToWorld(player.CurrentPosition, player.Space, player.RailDepth);

            if (TouchSpringsTheEncounter(director, feet)) return;

            Retire(feet);

            if (Time.time < _nextSpawnAt) return;
            _nextSpawnAt = Time.time + Mathf.Max(0.5f, _spawnEverySeconds);

            if (_wandererPrefab == null || _alive.Count >= _maxAlive) return;

            Spawn(feet);
        }

        /// <summary>
        /// A wanderer within touching distance carries the player off to the side-scroll world.
        /// One transition per frame at most — the first touch wins and the scene swap takes this
        /// whole component with it.
        /// </summary>
        private bool TouchSpringsTheEncounter(SceneDirector director, Vector3 playerFeet)
        {
            if (string.IsNullOrEmpty(_encounterScene)) return false;

            for (int i = 0; i < _alive.Count; i++)
            {
                var wanderer = _alive[i];
                if (wanderer == null) continue;

                var delta = wanderer.transform.position - playerFeet;
                delta.y = 0f;   // height is the railed axis up here; a touch is a plane question

                if (delta.sqrMagnitude > _touchRadius * _touchRadius) continue;

                director.GoTo(_encounterScene, _encounterSpawnId);
                return true;
            }

            return false;
        }

        private void Spawn(Vector3 playerFeet)
        {
            // A random bearing, clamped into the plain. If the player hugs an edge the clamp
            // pulls the point inward, which can land it on screen — accepted for the gray box,
            // where watching one appear is honestly a feature.
            float bearing = Random.Range(0f, Mathf.PI * 2f);
            var offset = new Vector3(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing)) * _spawnRadius;
            var position = playerFeet + offset;

            position.x = Mathf.Clamp(position.x,
                                     -_plainHalfExtents.x + _edgeMargin,
                                     _plainHalfExtents.x - _edgeMargin);
            position.z = Mathf.Clamp(position.z,
                                     -_plainHalfExtents.y + _edgeMargin,
                                     _plainHalfExtents.y - _edgeMargin);
            position.y = 0f;

            var wanderer = Instantiate(_wandererPrefab, position, Quaternion.identity);
            wanderer.name = _wandererPrefab.name;

            // The prefab is authored for side-scroll, because every other scene is. The spawner
            // is the thing that knows which space this world plays in.
            var host = wanderer.GetComponent<PlayerCharacterHost>();
            if (host != null) host.ConfigureSpace(MovementSpace.TopDown);

            _alive.Add(wanderer);
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
            var centre = transform.position;
            Gizmos.DrawWireCube(new Vector3(0f, 0.1f, 0f),
                                new Vector3(_plainHalfExtents.x * 2f, 0.1f, _plainHalfExtents.y * 2f));
            Gizmos.DrawWireSphere(centre, _spawnRadius);
        }
    }
}
