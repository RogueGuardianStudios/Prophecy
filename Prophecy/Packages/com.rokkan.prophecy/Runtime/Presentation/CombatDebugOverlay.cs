using System.Text;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rokkan.Prophecy.Presentation
{
    /// <summary>
    /// F2 overlay for combat: the attack timeline drawn a tick at a time, and the volumes drawn
    /// where they actually are.
    ///
    /// <para><b>Combat timing is invisible, and that is the entire problem.</b> A swing that misses
    /// can miss because the hit window had not opened, because it had already closed, because the
    /// box was short, or because a wall was in the way — and all four look identical at speed.
    /// Movement had the same issue and the F1 overlay solved it by putting coyote and buffer
    /// counters on screen. This is that, for frame data: the phase strip shows what the sim thinks
    /// is happening on the current tick, and the boxes show what it is asking the world.</para>
    ///
    /// <para>The strip is one cell per authored tick, so it is literally the frame data. Standing
    /// in front of a dummy and watching which cell the playhead is in when a hit registers is how
    /// startup and active lengths get set — and reading a cancel window that never opens explains a
    /// combo that will not link far faster than guessing at it will.</para>
    ///
    /// <para>Volumes are drawn with <c>GL</c> rather than gizmos on purpose. Gizmos need the Game
    /// view's toggle switched on and vanish in a build; a hit box you can only see sometimes is
    /// worse than useless when the question is why a hit did not land.</para>
    /// </summary>
    public sealed class CombatDebugOverlay : MonoBehaviour
    {
        [SerializeField] private PlayerCharacterHost _host;
        [SerializeField] private CombatDirector _director;

        [Header("Toggle")]
        [SerializeField, Tooltip("Optional. Uses the Debug map's ToggleCombatOverlay action; falls back to F2.")]
        private InputActionAsset _actions;

        [SerializeField] private string _debugMapName = "Debug";
        [SerializeField] private string _toggleActionName = "ToggleCombatOverlay";
        [SerializeField] private bool _visibleOnStart = true;

        [Header("Layout")]
        [SerializeField] private Vector2 _origin = new Vector2(12f, 300f);
        [SerializeField] private float _width = 420f;

        [Header("Volumes")]
        [SerializeField] private bool _drawVolumes = true;
        [SerializeField] private Color _hurtboxColour = new Color(0.3f, 0.75f, 1f, 0.9f);
        [SerializeField] private Color _armedBoxColour = new Color(1f, 0.75f, 0.2f, 0.35f);
        [SerializeField] private Color _liveBoxColour = new Color(1f, 0.25f, 0.15f, 1f);
        [SerializeField] private Color _coverRayColour = new Color(0.6f, 1f, 0.5f, 0.8f);

        private InputAction _toggle;
        private bool _visible;

        private GUIStyle _style;
        private Texture2D _background;
        private Texture2D _cell;

        private AttackModule _attack;
        private Material _lines;

        private readonly StringBuilder _text = new StringBuilder(512);

        // Phase and window colours, shared by the strip and the volume drawing so the same thing
        // is never two colours in two places.
        private static readonly Color Startup = new Color(0.95f, 0.75f, 0.25f);
        private static readonly Color Active = new Color(0.95f, 0.30f, 0.20f);
        private static readonly Color Recovery = new Color(0.35f, 0.45f, 0.60f);
        private static readonly Color HitWindow = new Color(1.00f, 0.90f, 0.30f);
        private static readonly Color CancelWindow = new Color(0.35f, 0.90f, 0.45f);
        private static readonly Color IFrameWindow = new Color(0.35f, 0.85f, 0.95f);
        private static readonly Color ParryWindow = new Color(0.85f, 0.45f, 0.95f);
        private static readonly Color Playhead = Color.white;

        private void Awake()
        {
            _visible = _visibleOnStart;

            if (_host == null) _host = FindAnyObjectByType<PlayerCharacterHost>();
            if (_director == null) _director = FindAnyObjectByType<CombatDirector>();

            ResolveToggle();
        }

        private void Start()
        {
            if (_host != null && _host.Sim != null) _attack = _host.Sim.Get<AttackModule>();
        }

        private void OnDestroy()
        {
            if (_background != null) Destroy(_background);
            if (_cell != null) Destroy(_cell);
            if (_lines != null) Destroy(_lines);
        }

        private void Update()
        {
            if (WasTogglePressed()) _visible = !_visible;

            // Late resolution: the host builds its sim in Awake, but a scene loaded additively can
            // put the host in after this component. Cheap, and it stops the overlay being blank
            // for the one setup most likely to need it.
            if (_attack == null && _host != null && _host.Sim != null)
                _attack = _host.Sim.Get<AttackModule>();

            if (_director == null) _director = CombatDirector.Instance;
        }

        // ---------------------------------------------------------------- text

        private void OnGUI()
        {
            if (!_visible || _host == null || _host.Sim == null) return;

            EnsureStyle();
            BuildText();

            var content = new GUIContent(_text.ToString());
            float textHeight = _style.CalcHeight(content, _width);
            float stripHeight = _attack != null && _attack.IsAttacking ? 74f : 0f;

            var box = new Rect(_origin.x, _origin.y, _width, textHeight + stripHeight);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(_origin.x, _origin.y, _width, textHeight), content, _style);

            if (stripHeight > 0f)
                DrawTimelineStrip(new Rect(_origin.x + 10f, _origin.y + textHeight, _width - 20f, stripHeight - 10f));
        }

        private void BuildText()
        {
            var sim = _host.Sim;

            _text.Clear();
            _text.AppendLine("<b>PROPHECY — combat</b>   [F2]");

            if (_attack == null)
            {
                _text.AppendLine("no AttackModule on this character");
                return;
            }

            var timeline = _attack.Runner != null ? _attack.Runner.Timeline : null;

            if (!_attack.IsAttacking || timeline == null)
            {
                _text.AppendLine($"stance   {sim.State.Stance}   idle");
                int buffered = _attack.Runner != null ? _attack.Runner.BufferedTicksRemaining(sim.CurrentTick) : 0;
                _text.AppendLine($"buffer   {buffered,2} ticks");
            }
            else
            {
                var definition = _attack.Current;

                _text.AppendLine($"attack   <b>{definition.Id}</b>   chain {_attack.Runner.ChainDepth}");
                _text.AppendLine($"phase    {timeline.CurrentPhase}   tick {timeline.ElapsedTicks}/{definition.TotalTicks}");
                _text.AppendLine($"frames   {definition.StartupTicks} startup / {definition.ActiveTicks} active / " +
                                 $"{definition.RecoveryTicks} recovery");
                _text.AppendLine($"windows  cancel {timeline.CancelRange}   iframe {timeline.IFrameRange}   " +
                                 $"parry {timeline.ParryRange}");
                _text.AppendLine($"buffer   {_attack.Runner.BufferedTicksRemaining(sim.CurrentTick),2} ticks" +
                                 $"   {(timeline.IsCancellable ? "<b>CANCELLABLE</b>" : "committed")}");
            }

            if (_director != null)
            {
                _text.AppendLine();
                _text.AppendLine($"targets  {_director.Hurtboxes.Count} live");

                var combatants = _director.Combatants;
                for (int i = 0; i < combatants.Count && i < 6; i++)
                {
                    var c = combatants[i];
                    if (c == null) continue;

                    _text.AppendLine($"  #{c.CombatId} {c.name,-18} {c.Health,4}/{c.MaxHealth}" +
                                     $"{(c.IsAlive ? "" : "   DOWN")}");
                }

                var log = _director.HitLog;
                if (log.Count > 0)
                {
                    _text.AppendLine();
                    _text.AppendLine("last hits (tick / target / dmg / attack)");

                    for (int i = log.Count - 1; i >= 0 && i >= log.Count - 4; i--)
                        _text.AppendLine($"  {log[i].Tick,6}  #{log[i].TargetId}  {log[i].Damage,3}  {log[i].AttackId}");
                }
            }
        }

        // ---------------------------------------------------------------- strip

        /// <summary>
        /// One cell per authored tick: phases on the top row, windows on the bottom, playhead
        /// through both. This is the frame data, drawn.
        /// </summary>
        private void DrawTimelineStrip(Rect area)
        {
            var definition = _attack.Current;
            var timeline = _attack.Runner.Timeline;
            if (definition == null || definition.TotalTicks <= 0) return;

            int total = definition.TotalTicks;
            float cellWidth = area.width / total;
            float rowHeight = area.height * 0.4f;
            float gap = area.height * 0.12f;

            for (int tick = 0; tick < total; tick++)
            {
                float x = area.x + tick * cellWidth;

                var phase = definition.StartupRange.Contains(tick) ? Startup
                          : definition.ActiveRange.Contains(tick) ? Active
                          : Recovery;

                Fill(new Rect(x, area.y, cellWidth - 1f, rowHeight), phase);

                // Windows share the lower row. Hit windows are the move's identity so they win the
                // pixel when two overlap; the rest are survival timing and can be read from the
                // text when they collide.
                Color? window = null;

                if (definition.HitBoxes != null)
                {
                    for (int b = 0; b < definition.HitBoxes.Length; b++)
                        if (definition.HitBoxes[b].Window.Contains(tick)) window = HitWindow;
                }

                if (window == null && timeline.CancelRange.Contains(tick)) window = CancelWindow;
                if (window == null && timeline.IFrameRange.Contains(tick)) window = IFrameWindow;
                if (window == null && timeline.ParryRange.Contains(tick)) window = ParryWindow;

                if (window.HasValue)
                    Fill(new Rect(x, area.y + rowHeight + gap, cellWidth - 1f, rowHeight), window.Value);
            }

            int elapsed = timeline.ElapsedTicks;
            if (elapsed >= 0 && elapsed < total)
            {
                float x = area.x + elapsed * cellWidth;
                Fill(new Rect(x, area.y - 2f, Mathf.Max(2f, cellWidth - 1f), area.height + 4f),
                     new Color(Playhead.r, Playhead.g, Playhead.b, 0.55f));
            }
        }

        private void Fill(Rect rect, Color colour)
        {
            var previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, _cell);
            GUI.color = previous;
        }

        // ---------------------------------------------------------------- volumes

        /// <summary>
        /// Draws hurtboxes, the armed attack's boxes, and the cover ray, in world space and in the
        /// Game view. Rotated boxes are drawn as four transformed corners rather than an AABB,
        /// because rotation is the reason the resolver uses ImmediatePhysics at all and an
        /// axis-aligned outline would misrepresent every diagonal swing.
        /// </summary>
        private void OnRenderObject()
        {
            if (!_drawVolumes || !_visible || _host == null || _host.Sim == null) return;

            EnsureLineMaterial();
            _lines.SetPass(0);

            GL.PushMatrix();
            GL.Begin(GL.LINES);

            var space = _director != null ? _director.Space : _host.Space;
            float depth = _host.RailDepth;

            if (_director != null)
            {
                var boxes = _director.Hurtboxes;
                for (int i = 0; i < boxes.Count; i++)
                    DrawBox(boxes[i].Centre, boxes[i].HalfExtents, boxes[i].RotationDegrees,
                            _hurtboxColour, space, depth);
            }

            DrawAttackVolumes(space, depth);

            GL.End();
            GL.PopMatrix();
        }

        private void DrawAttackVolumes(MovementSpace space, float depth)
        {
            if (_attack == null || !_attack.IsAttacking) return;

            var definition = _attack.Current;
            var timeline = _attack.Runner.Timeline;
            if (definition?.HitBoxes == null) return;

            var state = _host.Sim.State;

            for (int i = 0; i < definition.HitBoxes.Length; i++)
            {
                var box = definition.HitBoxes[i];
                bool live = timeline.IsHitBoxLive(i);

                var centre = box.ResolveCentre(state.Position, state.Facing);
                float rotation = box.ResolveRotation(state.Facing);

                DrawBox(centre, box.HalfExtents, rotation,
                        live ? _liveBoxColour : _armedBoxColour, space, depth);

                // The cover ray is traced from the body, not the box — seeing that line start at
                // the chest is what makes the rule obvious rather than something to be told.
                if (live && box.StoppedByGeometry && _director != null)
                {
                    var origin = new Vector2(state.Position.x, state.Position.y + state.BodySize.y * 0.5f);
                    var targets = _director.Hurtboxes;

                    for (int t = 0; t < targets.Count; t++)
                        DrawLine(origin, targets[t].Centre, _coverRayColour, space, depth);
                }
            }
        }

        private void DrawBox(Vector2 centre, Vector2 halfExtents, float rotationDegrees,
                             Color colour, MovementSpace space, float depth)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);

            var x = new Vector2(cos, sin) * halfExtents.x;
            var y = new Vector2(-sin, cos) * halfExtents.y;

            var a = centre - x - y;
            var b = centre + x - y;
            var c = centre + x + y;
            var d = centre - x + y;

            DrawLine(a, b, colour, space, depth);
            DrawLine(b, c, colour, space, depth);
            DrawLine(c, d, colour, space, depth);
            DrawLine(d, a, colour, space, depth);
        }

        private static void DrawLine(Vector2 from, Vector2 to, Color colour, MovementSpace space, float depth)
        {
            GL.Color(colour);
            GL.Vertex(SpaceMapping.ToWorld(from, space, depth));
            GL.Vertex(SpaceMapping.ToWorld(to, space, depth));
        }

        // ---------------------------------------------------------------- plumbing

        private void EnsureLineMaterial()
        {
            if (_lines != null) return;

            _lines = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            _lines.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lines.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lines.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

            // Depth writes off, depth test always: a hit box behind gray-box geometry still has to
            // be visible, because "was the box even there" is the question being asked.
            _lines.SetInt("_ZWrite", 0);
            _lines.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void EnsureStyle()
        {
            if (_style != null) return;

            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            _background.Apply();

            _cell = new Texture2D(1, 1);
            _cell.SetPixel(0, 0, Color.white);
            _cell.Apply();

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

        private bool WasTogglePressed()
        {
            if (_toggle != null) return _toggle.WasPressedThisFrame();

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.f2Key.wasPressedThisFrame;
        }
    }
}
