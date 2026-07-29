using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rokkan.Prophecy.World
{
    /// <summary>
    /// Makes pressing Play work from any scene.
    ///
    /// <para>In a multi-scene setup the game only functions with Bootstrap loaded — it holds the
    /// clock, the player and the director. Without this, opening a world scene and hitting Play
    /// gives you scenery, no character, and no error explaining why. The workaround everyone
    /// invents is "always open Bootstrap first", which is a rule that gets forgotten several times
    /// a day and costs a few seconds every time.</para>
    ///
    /// <para>So Bootstrap loads itself on top of whatever is open, and
    /// <see cref="SceneDirector"/> adopts the scene that was already there rather than replacing
    /// it. Iterate on a level, press Play, and you are standing in it.</para>
    ///
    /// <para>Editor-only by design. In a build Bootstrap is scene zero and this would be a
    /// redundant load — worse, a silent one if the build order were ever wrong.</para>
    /// </summary>
    public static class BootstrapLoader
    {
        public const string SceneName = "Bootstrap";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapLoaded()
        {
            if (!Application.isEditor) return;

            if (SceneManager.GetSceneByName(SceneName).isLoaded) return;

            if (!Application.CanStreamedLevelBeLoaded(SceneName))
            {
                Debug.LogWarning($"[Prophecy] '{SceneName}' is not in the build settings, so it " +
                                 "cannot be auto-loaded. Play from Bootstrap, or add it to the " +
                                 "scene list.");
                return;
            }

            SceneManager.LoadScene(SceneName, LoadSceneMode.Additive);
        }
    }
}
