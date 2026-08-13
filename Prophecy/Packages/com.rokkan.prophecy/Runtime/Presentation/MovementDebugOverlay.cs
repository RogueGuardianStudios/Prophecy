using System.Text;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// F1 overlay showing what the sim actually thinks is happening.
    ///
    /// <para>Tuning movement by feel alone is guesswork the moment two numbers interact. When a
    /// jump feels wrong, the useful question is whether coyote credit was still open, whether the
    /// buffered press expired, or whether a lock ate the input — and none of that is visible from
    /// watching a capsule. This puts the sim's own answers on screen next to the thing they
    /// describe.</para>
    ///
    /// <para>The timers are the point. Coyote and buffer windows are the two mechanics most
    /// responsible for a jump feeling right, and they are entirely invisible: a player cannot tell
    /// a window that is too short from input that was dropped. Watching the counters while
    /// deliberately jumping late off a ledge is how those two numbers get set.</para>
    ///
    /// <para>IMGUI on purpose. It needs no canvas, no prefab and no layout pass, it cannot be
    /// broken by a scene someone forgot to set up, and it costs nothing when hidden. A gray-box
    /// diagnostic should not itself need building.</para>
    /// </summary>
    public sealed class MovementDebugOverlay : MonoBehaviour
    {
        [SerializeField]
        private PlayerCharacterHost _host;

        [Header("Toggle")]
        [SerializeField, Tooltip("Optional. Uses the Debug map's ToggleOverlay action; falls back to F1.")]
        private InputActionAsset _actions;

        [SerializeField] private string _debugMapName = "Debug";
        [SerializeField] private string _toggleActionName = "ToggleOverlay";
        [SerializeField] private bool _visibleOnStart = true;

        [Header("Layout")]
        [SerializeField] private Vector2 _origin = new Vector2(12f, 12f);
        [SerializeField] private float _width = 340f;

        private InputAction _toggle;
        private bool _visible;
        private GUIStyle _style;
        private Texture2D _background;

        private Jump _jump;
        private GroundMove _groundMove;
        private FallLand _fallLand;
        private DownThrust _downThrust;
        private Swim _swim;
        private Buoyancy _buoyancy;

        private readonly StringBuilder _text = new StringBuilder(512);

        private void Awake()
        {
            _visible = _visibleOnStart;
            if (_host == null) _host = FindAnyObjectByType<PlayerCharacterHost>();

            ResolveToggle();
        }

        private void Start()
        {
            if (_host == null || _host.Sim == null) return;

            _jump = _host.Sim.Get<Jump>();
            _groundMove = _host.Sim.Get<GroundMove>();
            _fallLand = _host.Sim.Get<FallLand>();
            _downThrust = _host.Sim.Get<DownThrust>();
            _swim = _host.Sim.Get<Swim>();
            _buoyancy = _host.Sim.Get<Buoyancy>();
        }

        private void OnDestroy()
        {
            if (_background != null) Destroy(_background);
        }

        private void Update()
        {
            if (WasTogglePressed()) _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible || _host == null || _host.Sim == null) return;

            EnsureStyle();
            BuildText();

            var content = new GUIContent(_text.ToString());
            float height = _style.CalcHeight(content, _width);

            GUI.Box(new Rect(_origin.x, _origin.y, _width, height), GUIContent.none);
            GUI.Label(new Rect(_origin.x, _origin.y, _width, height), content, _style);
        }

        private void BuildText()
        {
            var sim = _host.Sim;
            var state = sim.State;

            _text.Clear();
            _text.AppendLine("<b>PROPHECY — movement</b>   [F1]");
            _text.AppendLine($"tick {sim.CurrentTick}    alpha {(_host.Clock != null ? _host.Clock.InterpolationAlpha : 0f):F2}    solids {_host.BakedSolidCount}");
            _text.AppendLine($"space {_host.Space}");
            _text.AppendLine();

            _text.AppendLine($"stance   {state.Stance}{(state.Grounded ? "  GROUNDED" : "  airborne")}");
            _text.AppendLine($"room     {state.Room}");
            _text.AppendLine($"pos      ({state.Position.x,7:F2}, {state.Position.y,7:F2})   facing {(state.Facing < 0 ? "<-" : "->")}");
            _text.AppendLine($"vel      ({state.Velocity.x,7:F2}, {state.Velocity.y,7:F2})   speed {state.Velocity.magnitude:F2}");

            if (_groundMove != null)
                _text.AppendLine($"gait     {(_groundMove.IsRunning ? "RUN" : "walk")}");

            _text.AppendLine();

            if (_jump != null)
            {
                // The two windows that decide whether a jump feels fair.
                _text.AppendLine($"coyote   {_jump.CoyoteTicksRemaining(state, sim.CurrentTick),2} ticks");
                _text.AppendLine($"buffer   {_jump.BufferedTicksRemaining(sim.CurrentTick),2} ticks");
                _text.AppendLine($"rising   {(_jump.IsRising ? "yes" : "no")}");
            }

            if (_fallLand != null)
                _text.AppendLine($"airborne {_fallLand.AirborneTicks,2} ticks   impact {_fallLand.LastImpactSpeed:F1} m/s");

            if (_downThrust != null && _downThrust.IsActive)
                _text.AppendLine("DOWN-THRUST");

            if (_swim != null && _swim.InWater)
            {
                string breath = _swim.Drowning
                    ? "DROWNING"
                    : _swim.HeadUnder ? $"breath {_swim.BreathFraction:P0}" : "breathing";
                string floating = _buoyancy != null && _buoyancy.FloatOn ? "  FLOAT" : "";
                _text.AppendLine($"water    depth {_swim.Depth:F2}   {breath}{floating}");
            }

            _text.AppendLine();

            var actionLock = sim.CurrentLock;
            if (!actionLock.IsHeld)
            {
                _text.AppendLine("lock     none");
            }
            else
            {
                string owner = actionLock.Owner is AbilityModule module ? module.Name : actionLock.Owner.GetType().Name;
                _text.AppendLine($"lock     {owner}  p{actionLock.Priority}");
                _text.AppendLine($"         suppress {actionLock.Flags}");
                _text.AppendLine($"         cancel window {(actionLock.CancelWindowOpen ? "OPEN" : "shut")}");
            }
        }

        private void EnsureStyle()
        {
            if (_style != null) return;

            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            _background.Apply();

            _style = new GUIStyle(GUI.skin.label)
            {
                font = Font.CreateDynamicFontFromOSFont("Consolas", 13),
                fontSize = 13,
                richText = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                padding = new RectOffset(10, 10, 8, 8),
            };
            _style.normal.textColor = new Color(0.85f, 0.95f, 1f);
            _style.normal.background = _background;
        }

        private void ResolveToggle()
        {
            if (_actions == null) return;

            var map = _actions.FindActionMap(_debugMapName, throwIfNotFound: false);
            if (map == null) return;

            _toggle = map.FindAction(_toggleActionName, throwIfNotFound: false);
            if (_toggle != null) _toggle.Enable();
        }

        /// <summary>Prefer the bound action; fall back to the keyboard so the overlay still works
        /// in a scene where the input asset was never wired up — which is exactly the scene most
        /// likely to need debugging.</summary>
        private bool WasTogglePressed()
        {
            if (_toggle != null) return _toggle.WasPressedThisFrame();

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.f1Key.wasPressedThisFrame;
        }
    }
}
