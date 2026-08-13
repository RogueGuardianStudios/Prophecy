using UnityEngine;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// The animated curtain over a world transition: fade to black while the old world is still
    /// visible, hold black while the swap happens, fade up on the new one.
    ///
    /// <para><b>Why the screen must be covered at all.</b> A transition is several frames of
    /// half-built world — the old scene gone, the player at stale coordinates, a camera that has
    /// not been told where to look. The fix is not ordering the work so perfectly that no wrong
    /// frame exists; it is not showing the work. And the cover must be an <i>animation</i> rather
    /// than a hard pop: an instant cut reads as a glitch, where a beat of fade reads as the game
    /// deliberately carrying you somewhere. Zelda II spends real frames on its transitions; so
    /// does this.</para>
    ///
    /// <para><b>The veil owns its pacing; the director obeys it.</b> <c>SceneDirector</c> asks
    /// for cover, waits for <see cref="IsOpaque"/> before moving anything, does the swap while
    /// <see cref="HoldSatisfied"/> counts down, and only then asks for the reveal. The load
    /// happens <i>inside</i> the hold, so a slow load stretches the black rather than the
    /// fade.</para>
    ///
    /// <para>IMGUI, one alpha-tinted rect: no camera, no canvas, no package dependency. If the
    /// effect ever grows into a proper wipe or an iris, <see cref="OnGUI"/> is the one method
    /// that changes — the phase machine and the director's contract stay.</para>
    /// </summary>
    public sealed class TransitionVeil : MonoBehaviour
    {
        private enum Phase { Clear, Covering, Covered, Uncovering }

        [SerializeField, Tooltip("Seconds to fade the world out. The 'you are being carried " +
                                 "away' beat — long enough to read as deliberate.")]
        private float _coverSeconds = 0.4f;

        [SerializeField, Tooltip("Minimum seconds at full black, so a fast load never strobes. " +
                                 "A slow load simply extends it.")]
        private float _holdSeconds = 0.25f;

        [SerializeField, Tooltip("Seconds to fade the new world in.")]
        private float _uncoverSeconds = 0.4f;

        private Texture2D _black;
        private Phase _phase = Phase.Clear;
        private float _alpha;
        private float _coveredSince;

        /// <summary>Create one, parented to whatever owns transitions.</summary>
        public static TransitionVeil Create(Transform parent)
        {
            var veil = new GameObject("TransitionVeil").AddComponent<TransitionVeil>();
            veil.transform.SetParent(parent, false);
            return veil;
        }

        /// <summary>Fully black — safe to tear the world down behind it.</summary>
        public bool IsOpaque => _alpha >= 1f;

        /// <summary>Black, and has been for the authored minimum. The cue to begin the reveal.</summary>
        public bool HoldSatisfied =>
            _phase == Phase.Covered && Time.unscaledTime - _coveredSince >= _holdSeconds;

        /// <summary>Nothing drawn. The transition is, visually, over.</summary>
        public bool IsClear => _phase == Phase.Clear;

        /// <summary>For the overlay and for tests. 0 clear, 1 black.</summary>
        public float Alpha => _alpha;

        /// <summary>Start fading the world out. Idempotent while already covering or covered.</summary>
        public void BeginCover()
        {
            if (_phase == Phase.Covering || _phase == Phase.Covered) return;
            _phase = _coverSeconds <= 0f ? Phase.Covered : Phase.Covering;
            if (_phase == Phase.Covered) { _alpha = 1f; _coveredSince = Time.unscaledTime; }
        }

        /// <summary>Instantly black — the adopt path, where there is no old world to fade out.</summary>
        public void SnapCovered()
        {
            _phase = Phase.Covered;
            _alpha = 1f;
            _coveredSince = Time.unscaledTime;
        }

        /// <summary>Start fading the new world in, from wherever the alpha currently is — error
        /// paths call this mid-cover, and a stuck curtain is worse than a short fade.</summary>
        public void BeginUncover()
        {
            if (_phase == Phase.Clear || _phase == Phase.Uncovering) return;
            _phase = _uncoverSeconds <= 0f ? Phase.Clear : Phase.Uncovering;
            if (_phase == Phase.Clear) _alpha = 0f;
        }

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

        private void Update()
        {
            // Unscaled on purpose: whatever a future slow-motion effect does to the clock, the
            // curtain moves at its authored pace — a transition is outside the world it hides.
            switch (_phase)
            {
                case Phase.Covering:
                    _alpha = Mathf.MoveTowards(_alpha, 1f, Time.unscaledDeltaTime / _coverSeconds);
                    if (_alpha >= 1f)
                    {
                        _phase = Phase.Covered;
                        _coveredSince = Time.unscaledTime;
                    }
                    break;

                case Phase.Uncovering:
                    _alpha = Mathf.MoveTowards(_alpha, 0f, Time.unscaledDeltaTime / _uncoverSeconds);
                    if (_alpha <= 0f) _phase = Phase.Clear;
                    break;
            }
        }

        private void OnGUI()
        {
            if (_alpha <= 0f) return;

            // Far in front of every other IMGUI drawer — the F1/F2 overlays included. A debug
            // overlay peeking through the curtain would defeat the point of having one.
            GUI.depth = int.MinValue;

            var tint = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _black);
            GUI.color = tint;
        }
    }
}
