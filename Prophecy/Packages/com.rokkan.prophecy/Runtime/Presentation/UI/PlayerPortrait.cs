using Rokkan.Animation;
using Rokkan.Prophecy.World;
using UnityEngine;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The pack sheet's portrait: a STUNT DOUBLE of the player's body, filmed on a private
    /// stage into a render texture. The double is re-cloned from the live body every time
    /// the sheet opens, so it wears exactly what the player wears — same model, same
    /// materials, and later the same equipment visuals — with nothing to sync; but unlike
    /// filming the player directly it stands in a controlled spot, in the IDLE pose (Matt:
    /// the mid-swing freeze the live film showed was the problem), borrowed from the same
    /// <see cref="BodyAnimationSet"/> the world animates with.
    ///
    /// <para>The stage sits far above any authored world so nothing walks into frame, and
    /// the double lives on the <c>PlayerBody</c> layer — the only layer the portrait camera
    /// renders — so even a stray skybox object could not photobomb. The backdrop clears to
    /// parchment and the film sits on the pack's page as if drawn there. The live player is
    /// never touched.</para>
    ///
    /// <para>Created on demand and kept across scene transitions; the camera and the double
    /// exist only while a menu is actually showing the film, so the cost when closed is
    /// zero.</para>
    /// </summary>
    public sealed class PlayerPortrait : MonoBehaviour
    {
        public const string LayerName = "PlayerBody";

        private const int Width = 512;
        private const int Height = 640;
        private const float Fov = 28f;

        /// <summary>How much air the frame keeps around the body's bounds.</summary>
        private const float Padding = 1.18f;

        /// <summary>An empty spot far above any authored world.</summary>
        private static readonly Vector3 Stage = new Vector3(0f, 500f, 0f);

        private Camera _camera;
        private RenderTexture _texture;
        private GameObject _double;
        private AnimationSystem _animation;
        private AnimationClip _idleClip;
        private bool _idleLoop;
        private float _standHeight = 1.8f;
        private bool _warnedNoLayer;

        public static PlayerPortrait Acquire()
        {
            var rig = FindAnyObjectByType<PlayerPortrait>(FindObjectsInactive.Include);
            if (rig != null) return rig;

            var host = new GameObject("PlayerPortrait");
            host.transform.position = Stage;
            DontDestroyOnLoad(host);
            return host.AddComponent<PlayerPortrait>();
        }

        /// <summary>Clone the player as they stand right now, pose the double, start
        /// filming, and hand back the film.</summary>
        public RenderTexture Open()
        {
            if (_camera == null) Build();

            RebuildDouble();
            _camera.enabled = true;
            Frame();
            return _texture;
        }

        public void Close()
        {
            if (_camera != null) _camera.enabled = false;
            DestroyDouble();
        }

        private void Build()
        {
            _texture = new RenderTexture(Width, Height, 24) { name = "PlayerPortrait" };

            // The camera gets its OWN child object. On the rig root it would drag the stage
            // — and the double parented to it — along with every reframe, and the body ends
            // up permanently behind the lens filming parchment.
            var film = new GameObject("Film");
            film.transform.SetParent(transform, false);

            _camera = film.AddComponent<Camera>();
            _camera.targetTexture = _texture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = UiPalette.Parchment;
            _camera.fieldOfView = Fov;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
            _camera.enabled = false;

            int layer = LayerMask.NameToLayer(LayerName);
            _camera.cullingMask = layer >= 0 ? 1 << layer : ~0;
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                _texture.Release();
                _texture = null;
            }
        }

        /// <summary>A fresh copy of whatever body the world is showing, stood on the stage.
        /// Fresh every open, so a changed loadout is already worn.</summary>
        private void RebuildDouble()
        {
            DestroyDouble();

            var director = SceneDirector.Instance;
            var player = director != null ? director.Player : null;
            if (player == null) return;

            var body = ResolveBody(player.transform);

            // The fallback of last resort is the host root itself, and that one must NOT be
            // cloned: a copied host would carry the sim, the input capture, everything.
            if (body == player.transform) return;

            _double = Instantiate(body.gameObject);
            _double.name = "PortraitDouble";
            _double.transform.SetParent(transform, false);
            _double.transform.localPosition = Vector3.zero;
            _double.transform.localRotation = Quaternion.identity;

            // The frame is a fixed window sized off the sim's own stand height — the model
            // was scaled to exactly this by its installer. Renderer bounds are deliberately
            // NOT consulted: a freshly cloned skinned mesh reports bind-pose bounds from
            // wherever its root bone happens to be, and the first framing cropped the body
            // at the knees believing them.
            if (player.Sim != null && player.Sim.State.StandSize.y > 0f)
                _standHeight = player.Sim.State.StandSize.y;

            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0)
            {
                SetLayerRecursively(_double.transform, layer);
            }
            else if (!_warnedNoLayer)
            {
                _warnedNoLayer = true;
                Debug.LogWarning($"[Prophecy] No '{LayerName}' layer in the project — the " +
                                 "portrait cannot isolate its stage and films everything.");
            }

            // The double borrows the player's own animation set for its pose, through the
            // clone's own AnimationSystem (a capsule proxy double has none — it just stands).
            _animation = _double.GetComponentInChildren<AnimationSystem>();
            _idleClip = null;

            var animator = player.GetComponentInChildren<CharacterAnimator>();
            var set = animator != null ? animator.Set : null;
            if (set != null && set.TryGet(BodyState.Idle, out var entry))
            {
                _idleClip = entry.Clip;
                _idleLoop = entry.Loop;
            }
        }

        private void DestroyDouble()
        {
            if (_double != null) Destroy(_double);
            _double = null;
            _animation = null;
            _idleClip = null;
        }

        private void LateUpdate()
        {
            if (_camera == null || !_camera.enabled) return;

            // Idempotent by design — asserting idle every frame costs nothing and holds the
            // pose no matter what state the body was cloned in.
            if (_animation != null && _idleClip != null)
                _animation.PlayState(_idleClip, _idleLoop, 1f, 0f);

            Frame();
        }

        private void Frame()
        {
            if (_double == null) return;

            // Face the double from its front: a fixed window centred on the standing body
            // (feet at the stage origin), far enough back that the height fits with air.
            var centre = transform.position + Vector3.up * (_standHeight * 0.52f);
            float distance = _standHeight * Padding * 0.5f
                             / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            var forward = _double.transform.forward;

            _camera.transform.position = centre + forward * distance;
            _camera.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        }

        /// <summary>The visual the world is actually showing: the installed hero model,
        /// else the capsule proxy, else the host itself (which is never cloned).</summary>
        private static Transform ResolveBody(Transform player)
        {
            var body = player.Find("HeroModel");
            if (body == null) body = player.Find("Body");
            return body != null ? body : player;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
