using System.Collections.Generic;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Arts;
using Rokkan.Prophecy.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The play HUD: three corner panels and nothing else (spec §4). Top-left is the body —
    /// Resolve seam, hearts, Flame reserve. Bottom-left is the flask row. Bottom-right is the
    /// equipped art. Top-right is EMPTY and stays empty — it is reserved, and no minimap
    /// exists, ever (a minimap would let the player read the Darkening as data instead of
    /// feeling it).
    ///
    /// <para>The corner layout mirrors the controller on purpose: left-hand inputs (LB guard,
    /// D-pad consumables) drive the left panels, the right hand's RB cast drives the right
    /// panel. Preserve that mapping in any future change (spec §4).</para>
    ///
    /// <para><b>Pure read.</b> This class draws what the sim says and decides nothing — the
    /// sim/presentation split, applied to interface. It builds its element tree into the
    /// shared <see cref="UIDocument"/> in OnEnable (see <see cref="UiBuild"/> for why the
    /// tree is code) and finds the player through <see cref="SceneDirector"/> whenever one
    /// exists; with no player it simply shows a full bar of nothing, which keeps the UI scene
    /// loadable in isolation.</para>
    ///
    /// <para>The corrupting parchment (spec §5) is deliberately absent: panels are flat
    /// parchment color, <c>_Corruption</c> is 0 until the binding economy exists. Note for
    /// that day: UI Toolkit has no per-element custom material, so the corruption pass will
    /// write into the parchment texture (or the panel's render target) rather than ride a
    /// material property — the palette and layout already obey the rules the effect will
    /// inherit.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        private const float HeartSize = 26f;
        private const float HeartGap = 5f;
        private const int DamageFlashTicks = 30;
        private const float LowHealthFraction = 0.25f;

        private CharacterSim _sim;

        private FlameBarWidget _flameBar;
        private VisualElement _seamFill;
        private float _seamWidth;

        private VisualElement _heartRow;
        private readonly List<VisualElement> _heartFills = new List<VisualElement>();
        private int _builtHearts = -1;

        private VisualElement _flaskRow;
        private readonly List<VisualElement> _flaskFills = new List<VisualElement>();
        private int _builtFlasks = -1;

        private VisualElement _emblemPlate;
        private VisualElement _emblemShape;
        private Label _emblemLetter;
        private Label _costPips;

        private VisualElement _dangerBleed;

        private void OnEnable()
        {
            // The document's root is shared with MenuRoot; each controller owns one named
            // layer inside it, replaced wholesale on re-enable.
            var root = GetComponent<UIDocument>().rootVisualElement;
            var layer = UiBuild.Layer(root, "HudLayer");
            layer.SendToBack();   // menus, whenever they exist, draw over the HUD

            BuildDangerBleed(layer);
            BuildTopLeft(layer);
            BuildBottomLeft(layer);
            BuildBottomRight(layer);

            _builtHearts = -1;
            _builtFlasks = -1;
            _sim = null;
        }

        // ------------------------------------------------------------------ build

        private void BuildTopLeft(VisualElement layer)
        {
            var panel = UiBuild.Solid(layer, "TopLeft", UiPalette.Parchment);
            UiBuild.Place(panel, left: 16f, top: 16f, width: 252f, height: 96f);

            // 1. The Resolve seam — the panel's header border, the one element grounded on
            //    shadow. Structural at rest: no pulse, no numerals; it sits full until spent.
            var seam = UiBuild.Solid(panel, "ResolveSeam", UiPalette.ShadowBase);
            UiBuild.Place(seam, left: 0f, top: 0f, width: 252f, height: 5f);
            _seamWidth = 252f;

            _seamFill = UiBuild.Solid(seam, "Fill", UiPalette.PaleGold);
            UiBuild.Place(_seamFill, left: 0f, top: 0f, width: 0f, height: 5f);

            // 2. The heart row — cells are (re)built against the sim's heart count.
            _heartRow = UiBuild.Solid(panel, "Hearts", Color.clear);
            UiBuild.Place(_heartRow, left: 10f, top: 13f, width: 232f, height: HeartSize);

            // 3. The reserve bar.
            _flameBar = new FlameBarWidget(panel, 10f, 47f, new Vector2(232f, 13f));
        }

        private void BuildBottomLeft(VisualElement layer)
        {
            var panel = UiBuild.Solid(layer, "BottomLeft", UiPalette.Parchment);
            UiBuild.Place(panel, left: 16f, bottom: 16f, width: 160f, height: 44f);

            _flaskRow = UiBuild.Solid(panel, "Flasks", Color.clear);
            UiBuild.Place(_flaskRow, left: 10f, top: 8f, width: 140f, height: 28f);
        }

        private void BuildBottomRight(VisualElement layer)
        {
            var panel = UiBuild.Solid(layer, "BottomRight", UiPalette.Parchment);
            UiBuild.Place(panel, right: 16f, bottom: 16f, width: 84f, height: 104f);

            // Emblem plate: a native border and a ground, recolored per state. The shape is
            // a diamond — identical in all four states; only ground, border and tint may
            // change (spec §6).
            _emblemPlate = UiBuild.Bordered(panel, "EmblemPlate",
                                            UiPalette.Umber, UiPalette.Parchment, 1f);
            UiBuild.Place(_emblemPlate, left: 10f, top: 8f, width: 64f, height: 64f);

            _emblemShape = UiBuild.Solid(_emblemPlate, "Shape", UiPalette.HearthGold);
            UiBuild.Place(_emblemShape, left: 16f, top: 16f, width: 30f, height: 30f);
            _emblemShape.style.rotate = new Rotate(Angle.Degrees(45f));

            _emblemLetter = UiBuild.Text(_emblemPlate, "Letter", "", 20, UiPalette.Parchment,
                                         TextAnchor.MiddleCenter);
            UiBuild.Place(_emblemLetter, left: 0f, top: 0f, right: 0f, bottom: 0f);

            _costPips = UiBuild.Text(panel, "CostPips", "", 14, UiPalette.HearthGold,
                                     TextAnchor.MiddleCenter);
            UiBuild.Place(_costPips, left: 0f, right: 0f, bottom: 6f, height: 20f);
        }

        private void BuildDangerBleed(VisualElement layer)
        {
            _dangerBleed = UiBuild.Solid(layer, "DangerBleed", Color.clear);
            UiBuild.Place(_dangerBleed, left: 0f, top: 0f, right: 0f, bottom: 0f);

            var texture = EdgeBleedTexture();
            _dangerBleed.style.backgroundImage = new StyleBackground(texture);
            _dangerBleed.style.unitySliceLeft = 40;
            _dangerBleed.style.unitySliceRight = 40;
            _dangerBleed.style.unitySliceTop = 40;
            _dangerBleed.style.unitySliceBottom = 40;
            _dangerBleed.style.display = DisplayStyle.None;
        }

        /// <summary>A frame that is opaque at the screen edge and clear in the middle — the
        /// danger bleed's shape, generated so the gray box ships no texture assets. White,
        /// tinted at draw time so the alpha ramp is authored once.</summary>
        private static Texture2D EdgeBleedTexture()
        {
            const int size = 96;
            const float border = 40f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / border));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // ------------------------------------------------------------------ read

        private void LateUpdate()
        {
            if (_sim == null)
            {
                var director = SceneDirector.Instance;
                _sim = director != null && director.Player != null ? director.Player.Sim : null;
                if (_sim == null) return;
            }

            DrawSeam();
            DrawHearts();
            DrawFlame();
            DrawFlasks();
            DrawEmblem();
            DrawDanger();
        }

        private void DrawSeam()
        {
            var stats = _sim.Stats;
            int cost = stats.Tuning.ResolveForNextLevel(stats.TotalLevels);

            // A banked level shows as a full seam — it sits full until spent, and it does not
            // flash, prompt or otherwise nudge (spec §2.3). The gain animation arrives with
            // the Resolve economy.
            float fraction = stats.UnspentLevels > 0
                ? 1f
                : Mathf.Clamp01(stats.Resolve / (float)Mathf.Max(1, cost));

            _seamFill.style.width = _seamWidth * fraction;
        }

        private void DrawHearts()
        {
            var vitals = _sim.Vitals;

            if (vitals.HeartCount != _builtHearts) RebuildHearts(vitals.HeartCount);

            for (int i = 0; i < _heartFills.Count; i++)
            {
                int quarters = Mathf.Clamp(
                    vitals.Health - i * Sim.Combat.Vitals.QuartersPerHeart,
                    0, Sim.Combat.Vitals.QuartersPerHeart);

                // Drain is top-down — a vessel emptying, not a bar depleting (spec §4.1) —
                // so what remains sits at the BOTTOM of the cell and the top empties first.
                float height = (HeartSize - 4f) * quarters / Sim.Combat.Vitals.QuartersPerHeart;
                _heartFills[i].style.height = height;
            }
        }

        private void RebuildHearts(int count)
        {
            _heartRow.Clear();
            _heartFills.Clear();

            for (int i = 0; i < count; i++)
            {
                var cell = UiBuild.Solid(_heartRow, $"Heart{i}", UiPalette.ParchmentEmpty);
                UiBuild.Place(cell, left: i * (HeartSize + HeartGap), top: 0f,
                              width: HeartSize, height: HeartSize);

                var fill = UiBuild.Solid(cell, "Fill", UiPalette.FestivalRed);
                UiBuild.Place(fill, left: 2f, right: 2f, bottom: 2f, height: 0f);
                _heartFills.Add(fill);
            }

            _builtHearts = count;
        }

        private void DrawFlame()
        {
            float cost = _sim.EquippedArt == ArtId.None
                ? 0f
                : ArtCatalog.Find(_sim.EquippedArt).Cost;

            _flameBar.Set(_sim.Reserve.Current, _sim.Reserve.Max, cost);
        }

        private void DrawFlasks()
        {
            var flasks = _sim.Flasks;

            if (flasks.Capacity != _builtFlasks) RebuildFlasks(flasks.Capacity);

            for (int i = 0; i < _flaskFills.Count; i++)
                _flaskFills[i].style.backgroundColor = i < flasks.Filled
                    ? UiPalette.FestivalRed
                    : UiPalette.Parchment;
        }

        private void RebuildFlasks(int capacity)
        {
            _flaskRow.Clear();
            _flaskFills.Clear();

            // The row always shows every slot — capacity never shrinks; what the darkening
            // world takes away is the refill, and an empty outline says that without a word
            // of explanation (spec §4.3).
            for (int i = 0; i < capacity; i++)
            {
                var pip = UiBuild.Bordered(_flaskRow, $"Flask{i}",
                                           UiPalette.Umber, UiPalette.Parchment, 1f);
                UiBuild.Place(pip, left: i * 26f, top: 0f, width: 20f, height: 28f);
                _flaskFills.Add(pip);
            }

            _builtFlasks = capacity;
        }

        private void DrawEmblem()
        {
            var art = _sim.EquippedArt;

            if (art == ArtId.None)
            {
                _emblemShape.style.display = DisplayStyle.None;
                _emblemLetter.text = "";
                _costPips.text = "";
                return;
            }

            var entry = ArtCatalog.Find(art);
            bool running = _sim.ActiveArts.Contains(art);

            // Buoyancy's running state lives in its module — the armed toggle is the art
            // being "on", wet or dry.
            if (art == ArtId.Buoyancy)
            {
                var buoyancy = _sim.Get<Buoyancy>();
                running = buoyancy != null && buoyancy.FloatOn;
            }

            bool locked = _sim.ActiveArts.Contains(ArtId.GluttonForPunishment);
            bool affordable = _sim.Reserve.CanAfford(entry.Cost);

            _emblemShape.style.display = DisplayStyle.Flex;

            if (locked)
            {
                // The one dark panel in the whole HUD: every other art lights the page, GfP
                // puts it out. No countdown, no progress ring — locked means "you cannot
                // stop it", not "it is running out" (spec §6).
                Style(UiPalette.Gilt, 2f, UiPalette.Umber, UiPalette.PaleGold, UiPalette.PaleGold);
            }
            else if (running)
            {
                // Running inverts the panel brighter; it does not glow (spec §6).
                Style(UiPalette.Gilt, 2f, UiPalette.Bright, UiPalette.HearthGold, UiPalette.HearthGold);
            }
            else if (!affordable)
            {
                // Muted, never red — an unaffordable spell is not an error (spec §6).
                Style(UiPalette.Umber, 1f, UiPalette.Parchment, UiPalette.Muted, UiPalette.MutedPip);
            }
            else
            {
                Style(UiPalette.Umber, 1f, UiPalette.Parchment, UiPalette.HearthGold, UiPalette.HearthGold);
            }

            _emblemLetter.text = entry.DisplayName.Substring(0, 1);
            _costPips.text = Pips(entry.Cost);
        }

        private void Style(Color border, float borderWidth, Color ground, Color emblem, Color pips)
        {
            UiBuild.SetBorder(_emblemPlate, border, borderWidth);
            _emblemPlate.style.backgroundColor = ground;
            _emblemShape.style.backgroundColor = emblem;
            _emblemLetter.style.color = ground;
            _costPips.style.color = pips;
        }

        /// <summary>Cost as pips — redundant with the bar's segment on purpose: the bar
        /// answers "can I afford it", the pips answer "what does it cost" (spec §6).</summary>
        public static string Pips(float cost)
        {
            int count = Mathf.CeilToInt(cost);
            if (count <= 0) return "—";

            var pips = new System.Text.StringBuilder(count * 2);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) pips.Append(' ');
                pips.Append('●');
            }

            return pips.ToString();
        }

        private void DrawDanger()
        {
            var vitals = _sim.Vitals;

            // A fresh hit flashes the edges; low health holds them lit. Shadow Mid, never
            // red — the player and the world die the same color (spec §7).
            float sinceHit = vitals.LastDamagedTick == long.MinValue
                ? float.MaxValue
                : _sim.CurrentTick - vitals.LastDamagedTick;

            float flash = Mathf.Clamp01(1f - sinceHit / DamageFlashTicks) * 0.85f;
            float lowHold = vitals.IsAlive && vitals.Fraction <= LowHealthFraction ? 0.45f : 0f;

            float alpha = Mathf.Max(flash, lowHold);
            var tint = UiPalette.ShadowMid;
            tint.a = alpha;

            _dangerBleed.style.unityBackgroundImageTintColor = tint;
            _dangerBleed.style.display = alpha > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
