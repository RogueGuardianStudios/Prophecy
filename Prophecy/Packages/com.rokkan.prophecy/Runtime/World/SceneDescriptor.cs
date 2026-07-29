using Rokkan.Prophecy.Sim;
using UnityEngine;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// What a world scene declares about itself: which movement space it is played in, and where
    /// the player starts.
    ///
    /// <para><b>This is what lets the player be persistent.</b> The player rig lives in Bootstrap
    /// and is never unloaded, which keeps all its wiring inside one scene where the editor can
    /// validate it. But a persistent player would otherwise have to hard-code whether it is in a
    /// side-scroll area or the top-down overworld — and those are genuinely different: the sim
    /// plane maps to world XY in one and XZ in the other, and the collision bake projects
    /// differently for each.</para>
    ///
    /// <para>So the <i>player</i> persists and the <i>configuration</i> is per-scene. Each world
    /// scene states its own terms and <see cref="SceneDirector"/> applies them on arrival. Adding
    /// an overworld later needs no change to the player at all.</para>
    /// </summary>
    public sealed class SceneDescriptor : MonoBehaviour
    {
        [SerializeField, Tooltip("Side-scroll action area, or top-down overworld.")]
        private MovementSpace _space = MovementSpace.SideScroll;

        [SerializeField, Tooltip("Used when a transition names no spawn. Falls back to any spawn in the scene.")]
        private SpawnPoint _defaultSpawn;

        [SerializeField, Tooltip("For the debug overlay and save slots.")]
        private string _displayName;

        [Header("Bounds")]
        [SerializeField, Tooltip("Falling below this height respawns the player. Set it under the " +
                                 "lowest floor, not under the lowest pit you meant to be lethal.")]
        private float _killPlaneY = -25f;

        [SerializeField, Tooltip("Turn off for scenes with no falling — the overworld, interiors.")]
        private bool _killPlaneEnabled = true;

        public MovementSpace Space => _space;

        /// <summary>Height below which the player is considered lost. See <see cref="SceneDirector"/>.</summary>
        public float KillPlaneY => _killPlaneY;

        public bool KillPlaneEnabled => _killPlaneEnabled;

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? gameObject.scene.name : _displayName;

        /// <summary>
        /// The spawn matching <paramref name="id"/>, else the authored default, else any spawn in
        /// the scene. Degrading rather than failing is deliberate: a mistyped portal id should
        /// drop the player somewhere sane and complain, not leave them at the origin under the
        /// floor with no clue why.
        /// </summary>
        public SpawnPoint ResolveSpawn(string id = null)
        {
            var spawns = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude);

            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < spawns.Length; i++)
                {
                    if (spawns[i].gameObject.scene != gameObject.scene) continue;
                    if (spawns[i].Id == id) return spawns[i];
                }

                Debug.LogWarning($"{DisplayName}: no SpawnPoint with id '{id}'. Using the default.", this);
            }

            if (_defaultSpawn != null) return _defaultSpawn;

            for (int i = 0; i < spawns.Length; i++)
                if (spawns[i].gameObject.scene == gameObject.scene) return spawns[i];

            Debug.LogError($"{DisplayName}: no SpawnPoint at all — the player has nowhere to arrive.", this);
            return null;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_killPlaneEnabled) return;

            // Drawn because an invisible respawn threshold is the kind of number people set once
            // and then spend an afternoon wondering about.
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);

            var centre = new Vector3(transform.position.x, _killPlaneY, transform.position.z);
            Gizmos.DrawWireCube(centre, new Vector3(400f, 0.05f, 20f));
        }
    }
}
