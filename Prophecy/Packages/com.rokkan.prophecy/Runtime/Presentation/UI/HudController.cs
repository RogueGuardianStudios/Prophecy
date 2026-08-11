using System.Collections.Generic;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Arts;
using Rokkan.Prophecy.World;
using UnityEngine;
using UnityEngine.UI;

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
    /// sim/presentation split, applied to interface. It builds its own widget tree in Awake
    /// (see <see cref="UiBuild"/> for why) and finds the player through
    /// <see cref="SceneDirector"/> whenever one exists; with no player it simply shows a full
    /// bar of nothing, which makes the UI scene loadable in isolation.</para>
    ///
    /// <para>The corrupting parchment (spec §5) is deliberately absent: panels are flat
    /// parchment color, <c>_Corruption</c> is 0 until the binding economy exists. The palette
    /// and layout already obey the rules the shader will inherit — no red anywhere but the
    /// hearts, no green at all, damage in Shadow Mid.</para>
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        private const float HeartSize = 26f;
        private const float HeartGap = 5f;
        private const int DamageFlashTicks = 30;
        private const float LowHealthFraction = 0.25f;

        private CharacterSim _sim;

        private FlameBarWidget _flameBar;
        private RectTransform _seamFill;
        private float _seamWidth;

        private RectTransform _heartRow;
        private readonly List<RectTransform> _heartFills = new List<RectTransform>();
        private int _builtHearts = -1;

        private RectTransform _flaskRow;
        private readonly List<Image> _flaskFills = new List<Image>();
        private int _builtFlasks = -1;

        private Image _emblemBorder;
        private Image _emblemGround;
        private Image _emblemShape;
        private Text _emblemLetter;
        private Text _costPips;

        private Image _dangerBleed;

        private void Awake()
        {
            var canvas = UiBuild.Canvas(transform, "HudCanvas", sortingOrder: 10);
            var root = canvas.transform;

            BuildDangerBleed(root);
            BuildTopLeft(root);
            BuildBottomLeft(root);
            BuildBottomRight(root);
        }

        // ------------------------------------------------------------------ build

        private void BuildTopLeft(Transform root)
        {
            var anchor = new Vector2(0f, 1f);
            var panel = UiBuild.Solid(root, "TopLeft", anchor, new Vector2(16f, -16f),
                                      new Vector2(252f, 96f), UiPalette.Parchment);

            // 1. The Resolve seam — the panel's header border, the one element grounded on
            //    shadow. Structural at rest: no pulse, no numerals; it sits full until spent.
            var seam = UiBuild.Solid(panel.transform, "ResolveSeam", anchor, Vector2.zero,
                                     new Vector2(252f, 5f), UiPalette.ShadowBase);
            _seamWidth = 252f;
            _seamFill = UiBuild.Solid(seam.transform, "Fill", new Vector2(0f, 0.5f),
                                      Vector2.zero, new Vector2(0f, 5f),
                                      UiPalette.PaleGold).rectTransform;

            // 2. The heart row — cells are (re)built against the sim's heart count.
            _heartRow = UiBuild.Rect(panel.transform, "Hearts", anchor,
                                     new Vector2(10f, -13f), new Vector2(232f, HeartSize));

            // 3. The reserve bar.
            _flameBar = new FlameBarWidget(panel.transform, new Vector2(10f, -47f),
                                           new Vector2(232f, 13f));
        }

        private void BuildBottomLeft(Transform root)
        {
            var anchor = new Vector2(0f, 0f);
            var panel = UiBuild.Solid(root, "BottomLeft", anchor, new Vector2(16f, 16f),
                                      new Vector2(160f, 44f), UiPalette.Parchment);

            _flaskRow = UiBuild.Rect(panel.transform, "Flasks", new Vector2(0f, 0.5f),
                                     new Vector2(10f, 0f), new Vector2(140f, 30f));
        }

        private void BuildBottomRight(Transform root)
        {
            var anchor = new Vector2(1f, 0f);
            var panel = UiBuild.Solid(root, "BottomRight", anchor, new Vector2(-16f, 16f),
                                      new Vector2(84f, 104f), UiPalette.Parchment);

            // Emblem plate: border drawn by the outer image, ground by the inner, both
            // recolored per state. The shape is a diamond — identical in all four states;
            // only ground, border and tint may change (spec §6).
            _emblemBorder = UiBuild.Solid(panel.transform, "EmblemPlate", new Vector2(0.5f, 1f),
                                          new Vector2(0f, -8f), new Vector2(64f, 64f),
                                          UiPalette.Umber);
            _emblemGround = UiBuild.Fill(_emblemBorder.transform, "Ground", UiPalette.Parchment, 1f);

            _emblemShape = UiBuild.Solid(_emblemGround.transform, "Shape", new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(30f, 30f),
                                         UiPalette.HearthGold);
            _emblemShape.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            _emblemLetter = UiBuild.Label(_emblemGround.transform, "Letter",
                                          new Vector2(0.5f, 0.5f), Vector2.zero,
                                          new Vector2(40f, 30f), "", 20, UiPalette.Parchment,
                                          TextAnchor.MiddleCenter);

            _costPips = UiBuild.Label(panel.transform, "CostPips", new Vector2(0.5f, 0f),
                                      new Vector2(0f, 6f), new Vector2(80f, 20f), "", 14,
                                      UiPalette.HearthGold, TextAnchor.MiddleCenter);
        }

        private void BuildDangerBleed(Transform root)
        {
            _dangerBleed = UiBuild.Fill(root, "DangerBleed", Color.clear);
            _dangerBleed.sprite = EdgeBleedSprite();
            _dangerBleed.type = Image.Type.Sliced;
        }

        /// <summary>A frame that is opaque at the screen edge and clear in the middle — the
        /// danger bleed's shape, generated so the gray box ships no texture assets.</summary>
        private static Sprite EdgeBleedSprite()
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

            return Sprite.Create(texture, new Rect(0, 0, size, size),
                                 new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect,
                                 new Vector4(border, border, border, border));
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

            _seamFill.sizeDelta = new Vector2(_seamWidth * fraction, _seamFill.sizeDelta.y);
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
                _heartFills[i].sizeDelta = new Vector2(_heartFills[i].sizeDelta.x, height);
            }
        }

        private void RebuildHearts(int count)
        {
            for (int i = _heartRow.childCount - 1; i >= 0; i--)
                Destroy(_heartRow.GetChild(i).gameObject);
            _heartFills.Clear();

            for (int i = 0; i < count; i++)
            {
                var cell = UiBuild.Solid(_heartRow, $"Heart{i}", new Vector2(0f, 0.5f),
                                         new Vector2(i * (HeartSize + HeartGap), 0f),
                                         new Vector2(HeartSize, HeartSize),
                                         UiPalette.ParchmentEmpty);

                var fill = UiBuild.Solid(cell.transform, "Fill", new Vector2(0.5f, 0f),
                                         new Vector2(0f, 2f),
                                         new Vector2(HeartSize - 4f, 0f),
                                         UiPalette.FestivalRed);
                _heartFills.Add(fill.rectTransform);
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
                _flaskFills[i].color = i < flasks.Filled
                    ? UiPalette.FestivalRed
                    : UiPalette.Parchment;
        }

        private void RebuildFlasks(int capacity)
        {
            for (int i = _flaskRow.childCount - 1; i >= 0; i--)
                Destroy(_flaskRow.GetChild(i).gameObject);
            _flaskFills.Clear();

            // The row always shows every slot — capacity never shrinks; what the darkening
            // world takes away is the refill, and an empty outline says that without a word
            // of explanation (spec §4.3).
            for (int i = 0; i < capacity; i++)
            {
                var fill = UiBuild.Bordered(_flaskRow, $"Flask{i}", new Vector2(0f, 0.5f),
                                            new Vector2(i * 26f, 0f), new Vector2(20f, 28f),
                                            UiPalette.Umber, UiPalette.Parchment, 1f);
                _flaskFills.Add(fill);
            }

            _builtFlasks = capacity;
        }

        private void DrawEmblem()
        {
            var art = _sim.EquippedArt;

            if (art == ArtId.None)
            {
                _emblemShape.enabled = false;
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

            _emblemShape.enabled = true;

            if (locked)
            {
                // The one dark panel in the whole HUD: every other art lights the page, GfP
                // puts it out. No countdown, no progress ring — locked means "you cannot
                // stop it", not "it is running out" (spec §6).
                Style(UiPalette.Gilt, UiPalette.Umber, UiPalette.PaleGold, UiPalette.PaleGold);
            }
            else if (running)
            {
                // Running inverts the panel brighter; it does not glow (spec §6).
                Style(UiPalette.Gilt, UiPalette.Bright, UiPalette.HearthGold, UiPalette.HearthGold);
            }
            else if (!affordable)
            {
                // Muted, never red — an unaffordable spell is not an error (spec §6).
                Style(UiPalette.Umber, UiPalette.Parchment, UiPalette.Muted, UiPalette.MutedPip);
            }
            else
            {
                Style(UiPalette.Umber, UiPalette.Parchment, UiPalette.HearthGold, UiPalette.HearthGold);
            }

            _emblemLetter.text = entry.DisplayName.Substring(0, 1);
            _costPips.text = Pips(entry.Cost);
        }

        private void Style(Color border, Color ground, Color emblem, Color pips)
        {
            _emblemBorder.color = border;
            _emblemGround.color = ground;
            _emblemShape.color = emblem;
            _emblemLetter.color = ground;
            _costPips.color = pips;
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
            var color = UiPalette.ShadowMid;
            color.a = alpha;

            _dangerBleed.color = color;
            _dangerBleed.enabled = alpha > 0.001f;
        }
    }
}
