using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The few strokes the gray-box UI is drawn with, on UI Toolkit: solid rects, bordered
    /// rects, and plain labels, built in code at runtime.
    ///
    /// <para><b>Why code-built and not UXML/USS.</b> The same reason the uGUI version built
    /// its tree in Awake: one source of truth, reviewable in a diff, nothing for a regenerated
    /// scene to clobber. UI Toolkit makes the deal better — borders are native styles instead
    /// of nested rects, and the whole tree lives under one <see cref="UIDocument"/> whose
    /// <c>PanelSettings</c> the scene builder generates.</para>
    ///
    /// <para>Everything defaults to <see cref="PickingMode.Ignore"/>: the HUD is a readout and
    /// the menus read the action map directly, so no element anywhere wants pointer events —
    /// and a HUD that silently ate a click would be a bug worn as a default.</para>
    /// </summary>
    internal static class UiBuild
    {
        /// <summary>A full-bleed container — the layer each controller owns inside the shared
        /// document root. Replaces any previous layer of the same name, so a domain reload or
        /// a re-enable rebuilds instead of stacking.</summary>
        public static VisualElement Layer(VisualElement root, string name)
        {
            root.Q(name)?.RemoveFromHierarchy();

            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.top = 0f;
            layer.style.right = 0f;
            layer.style.bottom = 0f;
            root.Add(layer);
            return layer;
        }

        /// <summary>A solid rect, absolutely positioned by whichever edges the caller sets.</summary>
        public static VisualElement Solid(VisualElement parent, string name, Color color)
        {
            var element = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            element.style.backgroundColor = color;
            parent.Add(element);
            return element;
        }

        /// <summary>A rect with a native border — one element where uGUI needed two.</summary>
        public static VisualElement Bordered(VisualElement parent, string name,
                                             Color border, Color ground, float borderWidth)
        {
            var element = Solid(parent, name, ground);
            SetBorder(element, border, borderWidth);
            return element;
        }

        public static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
        }

        /// <summary>Pin edges and size in one call. Pass null to leave an edge unset — the
        /// pattern is the anchor: left+top is a top-left panel, right+bottom a bottom-right one.</summary>
        public static void Place(VisualElement element, float? left = null, float? top = null,
                                 float? right = null, float? bottom = null,
                                 float? width = null, float? height = null)
        {
            if (left.HasValue) element.style.left = left.Value;
            if (top.HasValue) element.style.top = top.Value;
            if (right.HasValue) element.style.right = right.Value;
            if (bottom.HasValue) element.style.bottom = bottom.Value;
            if (width.HasValue) element.style.width = width.Value;
            if (height.HasValue) element.style.height = height.Value;
        }

        /// <summary>The one menu footprint — the pack sheet set the proportions (its column
        /// widths derive from the item square, its height from six visible bag rows) and
        /// every tabbed menu wears them (Matt: resize the other screens to this panel).</summary>
        public const float MenuWidth = 1152f;
        public const float MenuHeight = 974f;

        /// <summary>The carousel order of the tabbed menus (Matt: the menus are tabs,
        /// options last).</summary>
        public static readonly string[] MenuTabs = { "Pack", "Arts", "Book", "Options" };

        /// <summary>The page frame under the tab rail — one bordered field the page's
        /// content sits in, so the body reads apart from the header (Matt). The pack skips
        /// it: its own windows already are the frame.</summary>
        public static VisualElement PageFrame(VisualElement panel)
        {
            var frame = Bordered(panel, "Page", UiPalette.Umber, UiPalette.Parchment, 2f);
            Place(frame, left: 20f, top: 58f, right: 20f, bottom: 44f);
            return frame;
        }

        /// <summary>The tab rail every menu wears where its title used to be — the active
        /// page bright, its neighbours one LB/RB press away around the carousel. The four
        /// tabs split the whole header between them (Matt).</summary>
        public static void Tabs(VisualElement panel, int active)
        {
            const float margin = 24f;
            const float gap = 8f;
            float width = (MenuWidth - margin * 2f - gap * (MenuTabs.Length - 1))
                          / MenuTabs.Length;

            for (int i = 0; i < MenuTabs.Length; i++)
            {
                bool current = i == active;

                var tab = Bordered(panel, "Tab" + i,
                                   current ? UiPalette.Gilt : UiPalette.Umber,
                                   current ? UiPalette.Bright : UiPalette.ParchmentEmpty,
                                   current ? 2f : 1.5f);
                Place(tab, left: margin + i * (width + gap), top: 10f,
                      width: width, height: 40f);

                var label = Text(tab, "Label", MenuTabs[i], 20,
                                 current ? UiPalette.Ink : UiPalette.Muted,
                                 TextAnchor.MiddleCenter);
                Place(label, left: 0f, top: 0f, right: 0f, bottom: 0f);
            }
        }

        /// <summary>THE item square (Matt: "standardize the sizes") — loadout slots, carousel
        /// cards and bag cells are all this size. Capped by the bag's six-visible-rows
        /// minimum; raise it only with that window's height.</summary>
        public const float ItemSquare = 76f;

        /// <summary>Centre on the screen — the menus' one placement.</summary>
        public static void Centre(VisualElement element, float width, float height)
        {
            element.style.left = Length.Percent(50f);
            element.style.top = Length.Percent(50f);
            element.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
            element.style.width = width;
            element.style.height = height;
        }

        /// <summary>Plain text. The font comes from the generated runtime theme.</summary>
        public static Label Text(VisualElement parent, string name, string content,
                                 int fontSize, Color color,
                                 TextAnchor align = TextAnchor.UpperLeft)
        {
            var label = new Label(content) { name = name, pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityTextAlign = align;
            label.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(label);
            return label;
        }

        private static Font _serif;
        private static bool _serifSearched;

        /// <summary>Set a serifed face on <paramref name="label"/> — a roman numeral I needs
        /// its cap and foot or it reads as a lowercase l (Matt). Borrowed from the OS for the
        /// gray box; shipping wants a bundled font asset (Release-Checklist).</summary>
        public static void Serif(Label label)
        {
            if (!_serifSearched)
            {
                _serifSearched = true;

                var installed = new System.Collections.Generic.HashSet<string>(
                    Font.GetOSInstalledFontNames());

                foreach (var name in new[] { "Georgia", "Times New Roman", "Cambria" })
                {
                    if (!installed.Contains(name)) continue;
                    _serif = Font.CreateDynamicFontFromOSFont(name, 32);
                    break;
                }

                if (_serif == null)
                    Debug.LogWarning("[Prophecy] No OS serif font found — rank numerals stay sans.");
            }

            if (_serif != null)
                label.style.unityFontDefinition = FontDefinition.FromFont(_serif);
        }

        /// <summary>Roman numerals for the pack sheet — the Flame RANK is a numeral, never a
        /// bar (spec §2.2), and a stat page reads better in the book's own hand.</summary>
        public static string Roman(int value)
        {
            switch (value)
            {
                case 1: return "I";
                case 2: return "II";
                case 3: return "III";
                case 4: return "IV";
                case 5: return "V";
                case 6: return "VI";
                case 7: return "VII";
                case 8: return "VIII";
                default: return value.ToString();
            }
        }
    }
}
