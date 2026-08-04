using System.Collections.Generic;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// Zelda II's overworld menace: things pop up around the player and wander, weighted toward
    /// them. This spawns and retires the wanderers; what a wanderer <i>does</i> is its GOAP
    /// brain's business.
    ///
    /// <para><b>World furniture, like the portal and the kill plane.</b> Spawning lives in
    /// presentation because it is the overworld's dressing, not a combat outcome — a wanderer,
    /// once spawned, is a full simulated character under all the usual rules. When encounters
    /// become real (touching one should carry you to a side-scroll battle, per the design bible's
    /// dual structure), the contact that today deals chip damage becomes the trigger; this class
    /// is where that hook will live.</para>
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

        private readonly List<GameObject> _alive = new List<GameObject>();
        private float _nextSpawnAt;

        private void Update()
        {
            var director = SceneDirector.Instance;
            var player = director != null ? director.Player : null;
            if (player == null || director.IsTransitioning) return;

            var feet = SpaceMapping.ToWorld(player.CurrentPosition, player.Space, player.RailDepth);

            Retire(feet);

            if (Time.time < _nextSpawnAt) return;
            _nextSpawnAt = Time.time + Mathf.Max(0.5f, _spawnEverySeconds);

            if (_wandererPrefab == null || _alive.Count >= _maxAlive) return;

            Spawn(feet);
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
