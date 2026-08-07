using UnityEngine;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// The combat tester's supply line: keeps one enemy alive at its post. When the current
    /// one dies (its object is destroyed), a short beat passes and a fresh copy stands up in
    /// the same spot — so iterating on combat AI is fight, adjust, fight, without leaving
    /// play mode or reloading the scene.
    ///
    /// <para>Deliberately dumber than the overworld's encounter spawner: no provinces, no
    /// sightlines, no budget. A tester wants a metronome, not a menace.</para>
    /// </summary>
    public sealed class CombatTesterRespawner : MonoBehaviour
    {
        [SerializeField, Tooltip("What stands at this post. The tester wires the grunt.")]
        private GameObject _prefab;

        [SerializeField, Tooltip("Beat between a death and the replacement standing up — " +
                                 "long enough to read the kill, short enough to stay in " +
                                 "the flow of testing.")]
        private float _respawnSeconds = 2.5f;

        private GameObject _alive;
        private float _spawnAt;

        private void Start()
        {
            // First spawn almost immediately — the beat is for deaths, not arrivals.
            _spawnAt = Time.time + 0.25f;
        }

        private void Update()
        {
            if (_alive != null || _prefab == null) return;

            // Just died (or first frame): arm the timer once, then wait it out.
            if (_spawnAt <= 0f) _spawnAt = Time.time + _respawnSeconds;
            if (Time.time < _spawnAt) return;

            _alive = Instantiate(_prefab, transform.position, Quaternion.identity);
            _alive.name = _prefab.name;
            _spawnAt = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.7f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.45f);
        }
    }
}
