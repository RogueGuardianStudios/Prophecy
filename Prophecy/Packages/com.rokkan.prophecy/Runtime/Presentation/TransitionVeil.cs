using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// A black cover over the whole screen while a world transition is in flight.
    ///
    /// <para><b>Why the screen must be covered at all.</b> A transition is several frames of
    /// half-built world: the old scene is gone, the new one has loaded but its Start has not run,
    /// the player is still standing at the old scene's coordinates, and the camera has not been
    /// told where to look. Every one of those frames renders <i>something</i>, and whatever it is,
    /// it is wrong — the fix is not to order the work so perfectly that no wrong frame exists, it
    /// is to not show the work. Zelda II cuts to black between the map and a side view; so does
    /// this.</para>
    ///
    /// <para>IMGUI rather than a UGUI canvas, deliberately: it is one opaque rect, it draws above
    /// the scene without a camera, and it adds no package dependency to the runtime assembly for
    /// what is functionally a curtain.</para>
    /// </summary>
    public sealed class TransitionVeil : MonoBehaviour
    {
        private Texture2D _black;
        private bool _visible;

        /// <summary>Create one, parented to whatever owns transitions.</summary>
        public static TransitionVeil Create(Transform parent)
        {
            var veil = new GameObject("TransitionVeil").AddComponent<TransitionVeil>();
            veil.transform.SetParent(parent, false);
            return veil;
        }

        public bool Visible => _visible;

        public void Show() => _visible = true;

        public void Hide() => _visible = false;

        private void Awake()
        {
            _black = new Texture2D(1, 1, TextureFormat.RGB24, false);
            _black.SetPixel(0, 0, Color.black);
            _black.Apply();
        }

        private void OnDestroy()
        {
            if (_black != null) Destroy(_black);
        }

        private void OnGUI()
        {
            if (!_visible) return;

            // Far in front of every other IMGUI drawer — the F1/F2 overlays included. A debug
            // overlay peeking through the curtain would defeat the point of having one.
            GUI.depth = int.MinValue;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _black);
        }
    }
}
