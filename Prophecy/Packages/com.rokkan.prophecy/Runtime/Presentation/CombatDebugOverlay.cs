using System.Collections.Generic;
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
        private Block _block;
        private DodgeStep _dodge;
        private HitReact _hitReact;
        private TrainingAttacker[] _attackers;
        private CharacterAnimator _animator;
        private ArenaStations _stations;
        private Material _lines;

        private readonly StringBuilder _text = new StringBuilder(512);
        private readonly List<int> _candidates = new List<int>();
        private readonly List<DebugVolume> _volumes = new List<DebugVolume>();

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

            if (_host == null) _host = FindThePlayerHost();
            if (_director == null) _director = FindAnyObjectByType<CombatDirector>();

            ResolveToggle();
        }

        /// <summary>
        /// The PLAYER's host, never an enemy's. Every enemy also carries a
        /// <see cref="PlayerCharacterHost"/>, so a bare FindAnyObjectByType has no defined
        /// winner — and this overlay once reported a grunt's full health while the player was
        /// being ground to death beside it. A host with an <see cref="EnemyBrainHost"/>
        /// alongside is a body with a brain: an enemy.
        /// </summary>
        private static PlayerCharacterHost FindThePlayerHost()
        {
            var hosts = FindObjectsByType<PlayerCharacterHost>(FindObjectsSortMode.None);
            for (int i = 0; i < hosts.Length; i++)
                if (hosts[i].GetComponent<EnemyBrainHost>() == null)
                    return hosts[i];
            return null;
        }

        private void Start()
        {
            ResolveModules();
        }

        private void ResolveModules()
        {
            if (_host == null || _host.Sim == null) return;

            _attack = _host.Sim.Get<AttackModule>();
            _block = _host.Sim.Get<Block>();
            _dodge = _host.Sim.Get<DodgeStep>();
            _hitReact = _host.Sim.Get<HitReact>();
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
            if (_attack == null) ResolveModules();

            // Re-found when the arena changes, because the attackers belong to the scene rather
            // than to Bootstrap and a transition replaces the whole set.
            if (!ReferenceEquals(_director, CombatDirector.Instance))
            {
                _director = CombatDirector.Instance;
                _attackers = FindObjectsByType<TrainingAttacker>(FindObjectsSortMode.None);
                _animator = FindAnyObjectByType<CharacterAnimator>();
                _stations = FindAnyObjectByType<ArenaStations>();
            }
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

            AppendDefence(sim);

            if (_stations != null && _stations.Stations.Count > 0)
            {
                _text.AppendLine();
                _text.AppendLine("<b>warp</b>  (number keys)");

                var list = _stations.Stations;
                for (int i = 0; i < list.Count && i < 10; i++)
                {
                    int key = (i + 1) % 10;
                    _text.AppendLine($"  [{key}] {list[i].Label}{(i == _stations.LastStation ? "  <-" : "")}");
                }
            }

            if (_director != null)
            {
                _text.AppendLine();
                AppendBodyState();

                _text.AppendLine($"targets  {_director.Hurtboxes.Count} live" +
                                 $"   in the air {_director.Projectiles.Count}");

                var combatants = _director.Combatants;
                long paceTick = _host != null && _host.Sim != null ? _host.Sim.CurrentTick : 0;
                for (int i = 0; i < combatants.Count && i < 7; i++)
                {
                    var c = combatants[i];
                    if (c == null) continue;

                    // The pacing column: whose turn it is, and how far off the next one is.
                    string pace = "";
                    if (_director.State.Attacks.TryDescribe(c.CombatId, paceTick, out var token))
                        pace = token.InFlight ? "   IN-FLIGHT"
                             : token.HoldsToken ? "   TOKEN"
                             : token.TicksUntilEligible > 0 ? $"   beat {token.TicksUntilEligible}t"
                             : "   ready";

                    _text.AppendLine($"  #{c.CombatId} {c.name,-18} {c.Health,4}/{c.MaxHealth}" +
                                     $"{(c.IsAlive ? "" : "   DOWN")}{pace}");
                }

                var log = _director.HitLog;
                if (log.Count > 0)
                {
                    _text.AppendLine();
                    _text.AppendLine("last hits (tick / who / dmg / outcome)");

                    for (int i = log.Count - 1; i >= 0 && i >= log.Count - 5; i--)
                    {
                        var entry = log[i];
                        _text.AppendLine(
                            $"  {entry.Hit.Tick,6}  #{entry.Hit.AttackerId}->#{entry.Hit.TargetId}  " +
                            $"{entry.Result.DamageApplied,3}  " +
                            $"{Describe(entry.Result.Outcome)}  {entry.Hit.AttackId}");
                    }
                }
            }
        }

        /// <summary>
        /// The defensive picture, which is the half that is hardest to infer from watching. Whether
        /// the guard answers the height coming at you, whether the parry window is open right now,
        /// and how many ticks of hit-stun are left are each the difference between "the system is
        /// broken" and "you were early".
        /// </summary>
        private void AppendDefence(CharacterSim sim)
        {
            _text.AppendLine();
            _text.AppendLine($"health   {sim.Vitals.Health}/{sim.Vitals.MaxHealth}" +
                             $"{(sim.Vitals.IsAlive ? "" : "   DEAD")}");

            if (_block != null)
            {
                if (!_block.IsGuarding)
                {
                    _text.AppendLine($"guard    down   (parry window {_combatParryWindow(_block)} ticks)");
                }
                else
                {
                    int up = _block.GuardTicks(sim.CurrentTick);
                    _text.AppendLine($"guard    <b>UP</b> {up}t answering {_block.Guarding}" +
                                     $"{(_block.ParryWindowOpen(sim.CurrentTick) ? "   <b>PARRY</b>" : "")}");
                }
            }

            if (_dodge != null)
            {
                if (!_dodge.IsDodging)
                {
                    _text.AppendLine("dodge    ready");
                }
                else
                {
                    var timeline = _dodge.Timeline;
                    _text.AppendLine($"dodge    {timeline.CurrentPhase} {timeline.ElapsedTicks}/" +
                                     $"{timeline.Definition.TotalTicks}  {(_dodge.Direction < 0 ? "<-" : "->")}" +
                                     $"{(_dodge.WindowOpen ? "   <b>INTANGIBLE</b>" : "")}");
                }
            }

            if (_hitReact != null)
            {
                int stun = _hitReact.TicksRemaining(sim.CurrentTick);
                int iframes = _hitReact.InvulnerableTicksRemaining(sim.CurrentTick);

                if (stun > 0) _text.AppendLine($"stunned  {stun,2} ticks   ({_hitReact.Cause})");
                if (iframes > 0) _text.AppendLine($"iframes  {iframes,2} ticks");
            }
        }

        private static int _combatParryWindow(Block block) => block.ParryWindow.Duration;

        private static string Describe(HitOutcome outcome)
        {
            switch (outcome)
            {
                case HitOutcome.Landed: return "hit    ";
                case HitOutcome.Blocked: return "BLOCK  ";
                case HitOutcome.Parried: return "PARRY  ";
                case HitOutcome.Invulnerable: return "iframe ";
                default: return "ignored";
            }
        }

        // ---------------------------------------------------------------- strip

        /// <summary>
        /// One cell per authored tick: phases on the top row, windows on the bottom, playhead
        /// through both. This is the frame data, drawn.
        /// </summary>
        /// <summary>
        /// What the body is doing, and what it was doing before that.
        ///
        /// <para>The history is the useful half. A pose that is wrong for one or two frames cannot
        /// be caught by pausing and cannot fail a test — but it leaves a trace here, and a state
        /// that lasted 0.02 s between two that lasted a second is unmistakable once written down.</para>
        /// </summary>
        private void AppendBodyState()
        {
            if (_animator == null) return;

            _text.Append($"body     <b>{_animator.Current}</b>");

            var history = _animator.History;
            if (history != null && history.Count > 0)
            {
                _text.Append("   was:");

                int shown = Mathf.Min(4, history.Count);
                for (int i = 0; i < shown; i++)
                {
                    // Flag anything that lasted less than about two frames at 60 fps — that is the
                    // shape of a flicker, and it is the thing being hunted.
                    bool blink = history[i].Seconds < 0.034f;
                    string mark = blink ? "!" : "";
                    _text.Append($" {mark}{history[i].State}({history[i].Seconds * 1000f:F0}ms)");
                }
            }

            _text.AppendLine();
        }

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
        /// because rotation is the reason the resolver does separating-axis at all and an
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

            DrawSimVolumes(space, depth);
            DrawIncomingVolumes(space, depth);
            DrawProjectiles(space, depth);

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// The attacks aimed at <i>you</i>, which is the half that matters once defence exists.
        /// Drawn dim through the wind-up and bright the tick they become dangerous, so the
        /// telegraph is something you can watch rather than something you time by ear.
        /// </summary>
        private void DrawIncomingVolumes(MovementSpace space, float depth)
        {
            if (_attackers == null) return;

            for (int a = 0; a < _attackers.Length; a++)
            {
                var attacker = _attackers[a];
                if (attacker == null || !attacker.IsSwinging) continue;

                var definition = attacker.Attack;
                if (definition?.HitBoxes == null) continue;

                var timeline = attacker.Timeline;
                var feet = SpaceMapping.ToPlane(attacker.transform.position, space);

                // The attacker's own frozen facing, not "which side is the player on". Those agree
                // until you walk through an attacker mid-swing, at which point the drawn box used
                // to mirror-flip while the real one stayed put — the overlay lying about the exact
                // thing it exists to be truthful about.
                int facing = attacker.ResolvedFacing;

                for (int i = 0; i < definition.HitBoxes.Length; i++)
                {
                    var box = definition.HitBoxes[i];
                    bool live = timeline.IsHitBoxLive(i);

                    DrawBox(box.ResolveCentre(feet, facing), box.HalfExtents,
                            box.ResolveRotation(facing),
                            live ? _liveBoxColour : _armedBoxColour, space, depth);
                }
            }
        }

        /// <summary>
        /// Every simulated body's volumes — the player's and every planning enemy's — through
        /// one seam.
        ///
        /// <para><b>Each private draw path this replaces was written only after its attack
        /// shipped invisible.</b> The thrusts run no timeline, so the dive's box was undrawable
        /// for two milestones; a planning enemy attacks through the ordinary
        /// <c>AttackModule</c> but was found through no path at all, so the volume that
        /// actually reached the player was the one volume the viewer could not draw. The lesson
        /// is the same each time: the overlay must not KNOW the modules, or the next one is
        /// invisible until someone notices. A box-owning module implements
        /// <see cref="IDebugVolumeSource"/> and appears here with no overlay edit.</para>
        /// </summary>
        private void DrawSimVolumes(MovementSpace space, float depth)
        {
            DrawVolumesOf(_host.Sim, space, depth);

            if (_director == null) return;

            var combatants = _director.Combatants;
            for (int c = 0; c < combatants.Count; c++)
            {
                var combatant = combatants[c];
                if (combatant == null || !combatant.IsSimulated) continue;

                var sim = combatant.SimHost.Sim;
                if (sim == _host.Sim) continue;          // already drawn, rays and all

                DrawVolumesOf(sim, space, depth);
            }
        }

        private void DrawVolumesOf(CharacterSim sim, MovementSpace space, float depth)
        {
            if (sim == null) return;

            _volumes.Clear();

            var modules = sim.Modules;
            for (int m = 0; m < modules.Count; m++)
                if (modules[m] is IDebugVolumeSource source)
                    source.CollectDebugVolumes(sim.State, _volumes);

            var state = sim.State;

            for (int i = 0; i < _volumes.Count; i++)
            {
                var volume = _volumes[i];

                DrawBox(volume.Centre, volume.HalfExtents, volume.RotationDegrees,
                        volume.Live ? _liveBoxColour : _armedBoxColour, space, depth);

                // The cover ray is traced from the body, not the box — seeing that line start at
                // the chest is what makes the rule obvious rather than something to be told.
                //
                // Only to targets the swing could actually reach. Drawing one to every hurtbox in
                // the level was a line per body per frame, and past a dozen of them the picture it
                // produced was a fan of noise rather than the one ray being explained — which is
                // also why only the inspected body's swings get rays at all.
                if (volume.Live && volume.StoppedByGeometry &&
                    sim == _host.Sim && _director != null)
                {
                    var origin = new Vector2(state.Position.x, state.Position.y + state.BodySize.y * 0.5f);
                    var targets = _director.Hurtboxes;

                    var reach = HitResolver.BoundingHalfExtents(volume.HalfExtents, volume.RotationDegrees);
                    int found = targets.Query(volume.Centre.x - reach.x, volume.Centre.x + reach.x, _candidates);

                    for (int t = 0; t < found; t++)
                        DrawLine(origin, targets[_candidates[t]].Centre, _coverRayColour, space, depth);
                }
            }
        }

        /// <summary>
        /// Everything in the air. Drawn in the live colour because a projectile has no wind-up —
        /// it is dangerous for its whole existence, and showing it dim would be a lie about the
        /// one thing the player needs to judge.
        /// </summary>
        private void DrawProjectiles(MovementSpace space, float depth)
        {
            if (_director == null) return;

            var live = _director.Projectiles;

            for (int i = 0; i < live.Count; i++)
                DrawBox(live[i].Position, live[i].HalfExtents, 0f, _liveBoxColour, space, depth);
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
