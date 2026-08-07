using Rokkan.Prophecy.World;
using UnityEngine;

namespace Rokkan.Prophecy.Overworld
{
    /// <summary>
    /// The halo cutout's runtime half: three shader globals — the player's chest, the
    /// radius, and a fade — read by every material that can occlude (the gray-box lit
    /// shader and the ground shader's bridge decks). Attached by the grid host, so it only
    /// exists where the top-down camera does; the scene transition takes it along and
    /// OnDisable zeroes the strength, which makes the cylinder test free-exit everywhere
    /// else. Plain globals are fine here — only RenderGraph fullscreen passes refuse them,
    /// and these are ordinary geometry passes.
    ///
    /// <para><b>The hole is earned, not ambient</b> (Matt, live): the halo fades in only
    /// while <see cref="OverworldSightline"/> says the camera genuinely cannot see the
    /// player, and never while the cave reveal is running — inside a revealed room the
    /// player is already visible, and a halo there punches through the room's own walls
    /// into the void.</para>
    /// </summary>
    public sealed class HaloCutoutDriver : MonoBehaviour
    {
        [SerializeField, Tooltip("Radius of the cone's player-disc, in metres. A body's " +
                                 "width and a bit — enough to read the character and their " +
                                 "immediate footing, not enough to undress the wall. Tuned " +
                                 "down twice (1.4 → 1.0 → 0.75) chasing overcut.")]
        private float _radius = 0.75f;

        [SerializeField, Tooltip("Height above the feet the cylinder aims at — the chest, " +
                                 "so the hole frames the character rather than their shoes.")]
        private float _chestHeight = 0.9f;

        private const float FadePerSecond = 5f;
        private OverworldBuildOutput _built;
        private CaveRevealDriver _cave;
        private float _strength;

        public void Bind(OverworldBuildOutput built)
        {
            _built = built;
            _cave = GetComponent<CaveRevealDriver>();
        }

        private void Update()
        {
            var director = SceneDirector.Instance;
            var player = director != null ? director.Player : null;
            var camera = Camera.main;

            var chest = player != null
                ? player.FeetWorldPosition + Vector3.up * _chestHeight
                : Vector3.zero;

            // Chest always tracks while a player exists — a fading-out halo must die where
            // the player IS, not snap to origin the frame its reason goes away.
            bool wanted = player != null && camera != null
                          && (_cave == null || !_cave.Revealing)
                          && OverworldSightline.Blocked(
                                 _built != null ? _built.Grid : null,
                                 camera.transform.position, chest);

            _strength = Mathf.MoveTowards(_strength, wanted ? 1f : 0f,
                                          FadePerSecond * Time.deltaTime);

            if (player != null) Shader.SetGlobalVector("_HaloCentre", chest);
            Shader.SetGlobalFloat("_HaloRadius", _radius);
            Shader.SetGlobalFloat("_HaloStrength", _strength);
        }

        private void OnDisable()
        {
            _strength = 0f;
            Shader.SetGlobalFloat("_HaloStrength", 0f);
        }
    }
}
