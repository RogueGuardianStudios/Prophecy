using System.Collections;
using Rokkan.Prophecy.Presentation;
using Rokkan.Prophecy.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// Swaps world scenes under a persistent player, and owns the order in which that happens.
    ///
    /// <para>The sequence is not arbitrary and every step has a reason:</para>
    /// <list type="number">
    ///   <item>unload the old world, so two sets of geometry never coexist in the bake</item>
    ///   <item>load the new one additively — Bootstrap must never be unloaded</item>
    ///   <item>make it the active scene, or its lighting and skybox are ignored and anything
    ///         instantiated later lands in Bootstrap instead</item>
    ///   <item>wait a frame, so the arriving scene's own Awake and Start have run</item>
    ///   <item>apply its declared movement space, which re-bakes collision with the right
    ///         axis projection</item>
    ///   <item>place the player, then snap the camera so it does not slide in from wherever it
    ///         was looking a moment ago</item>
    /// </list>
    ///
    /// <para><b>Why anything owns this at all.</b> <c>PlayerCharacterHost</c> bakes collision in
    /// its own <c>Start</c>, which is right when the world is already there and wrong the instant
    /// worlds start arriving later than the player. Rather than have the player guess when the
    /// ground exists, the thing that loads the ground says so. That is also why the Bootstrap
    /// player has its start-up bake switched off — one owner, one moment.</para>
    /// </summary>
    public sealed class SceneDirector : MonoBehaviour
    {
        [SerializeField]
        private PlayerCharacterHost _player;

        [SerializeField]
        private FollowCamera _camera;

        [SerializeField, Tooltip("Loaded on start-up, unless a world scene is already open.")]
        private string _firstWorldScene = "GrayBox_Traversal";

        public static SceneDirector Instance { get; private set; }

        private SceneDescriptor _descriptor;
        private SpawnPoint _activeSpawn;

        /// <summary>Times the player has fallen out of the world this session. Debug overlay —
        /// a gray box that keeps eating the player is a level design note, not just an annoyance.</summary>
        public int FallResetCount { get; private set; }

        /// <summary>Name of the world scene currently loaded, or empty.</summary>
        public string CurrentWorldScene { get; private set; } = string.Empty;

        /// <summary>True while a transition is in flight. Portals check this so a player standing
        /// in two overlapping triggers cannot start two loads.</summary>
        public bool IsTransitioning { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"{name}: a second SceneDirector appeared — destroying it. " +
                                 "Bootstrap should be loaded exactly once.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private IEnumerator Start()
        {
            // Pressing Play from a world scene loads Bootstrap on top (see BootstrapLoader), so
            // the world is already open and loading _firstWorldScene would be wrong — adopt what
            // is there. Iterating for a scene with a descriptor beats trusting the build order.
            var existing = FindOpenWorldScene();

            if (existing.IsValid())
                yield return Enter(existing, null);
            else if (!string.IsNullOrEmpty(_firstWorldScene))
                yield return Transition(_firstWorldScene, null);
        }

        /// <summary>Go to another world scene, arriving at <paramref name="spawnId"/>.</summary>
        public void GoTo(string sceneName, string spawnId = null)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning($"{name}: already transitioning; ignoring request for '{sceneName}'.", this);
                return;
            }

            StartCoroutine(Transition(sceneName, spawnId));
        }

        private IEnumerator Transition(string sceneName, string spawnId)
        {
            IsTransitioning = true;

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"{name}: scene '{sceneName}' is not in the build settings.", this);
                IsTransitioning = false;
                yield break;
            }

            if (!string.IsNullOrEmpty(CurrentWorldScene))
            {
                var current = SceneManager.GetSceneByName(CurrentWorldScene);
                if (current.IsValid() && current.isLoaded)
                {
                    var unload = SceneManager.UnloadSceneAsync(current);
                    while (unload != null && !unload.isDone) yield return null;
                }
            }

            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (load != null && !load.isDone) yield return null;

            yield return Enter(SceneManager.GetSceneByName(sceneName), spawnId);

            IsTransitioning = false;
        }

        private IEnumerator Enter(Scene scene, string spawnId)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"{name}: cannot enter an unloaded scene.", this);
                yield break;
            }

            SceneManager.SetActiveScene(scene);
            CurrentWorldScene = scene.name;

            // One frame, so the arriving scene's Start has run before anything is measured.
            yield return null;

            var descriptor = FindDescriptorIn(scene);

            if (descriptor == null)
            {
                Debug.LogError($"{name}: '{scene.name}' has no SceneDescriptor — cannot tell " +
                               "which movement space it is, or where the player starts.", this);
                yield break;
            }

            _descriptor = descriptor;
            _activeSpawn = descriptor.ResolveSpawn(spawnId);

            if (_player != null)
            {
                _player.ConfigureSpace(descriptor.Space);
                if (_activeSpawn != null) _player.TeleportTo(_activeSpawn.Position, _activeSpawn.Facing);
            }

            if (_camera != null) _camera.SnapToTarget();
        }

        /// <summary>
        /// Catch the player when they fall out of the world.
        ///
        /// <para>Without this, a missed jump in the gray box means stopping and restarting play
        /// mode — which is enough friction to stop anyone iterating on jump feel, the one thing
        /// this whole milestone exists to do. Respawning at the spawn point they arrived at is
        /// also Zelda II's own death rule in miniature: back to the start, everything else intact.</para>
        /// </summary>
        private void Update()
        {
            if (IsTransitioning) return;
            if (_player == null || _descriptor == null) return;
            if (!_descriptor.KillPlaneEnabled) return;

            if (_player.transform.position.y > _descriptor.KillPlaneY) return;

            FallResetCount++;

            if (_activeSpawn != null)
                _player.TeleportTo(_activeSpawn.Position, _activeSpawn.Facing);
            else
                _player.TeleportTo(Vector3.zero);

            if (_camera != null) _camera.SnapToTarget();
        }

        /// <summary>Any loaded scene other than this one carrying a <see cref="SceneDescriptor"/>.</summary>
        private Scene FindOpenWorldScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (scene == gameObject.scene) continue;
                if (FindDescriptorIn(scene) != null) return scene;
            }

            return default;
        }

        private static SceneDescriptor FindDescriptorIn(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var descriptor = roots[i].GetComponentInChildren<SceneDescriptor>(true);
                if (descriptor != null) return descriptor;
            }

            return null;
        }
    }
}
