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
    /// </summary>
    public sealed class HaloCutoutDriver : MonoBehaviour
    {
        [SerializeField, Tooltip("Radius of the see-through cylinder, in metres. A body's " +
                                 "width and a bit — enough to read the character and their " +
                                 "immediate footing, not enough to undress the wall.")]
        private float _radius = 1.4f;

        [SerializeField, Tooltip("Height above the feet the cylinder aims at — the chest, " +
                                 "so the hole frames the character rather than their shoes.")]
        private float _chestHeight = 0.9f;

        private const float FadePerSecond = 5f;
        private float _strength;

        private void Update()
        {
            var director = SceneDirector.Instance;
            var player = director != null ? director.Player : null;

            _strength = Mathf.MoveTowards(_strength, player != null ? 1f : 0f,
                                          FadePerSecond * Time.deltaTime);

            if (player != null)
                Shader.SetGlobalVector("_HaloCentre",
                    player.FeetWorldPosition + Vector3.up * _chestHeight);
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
