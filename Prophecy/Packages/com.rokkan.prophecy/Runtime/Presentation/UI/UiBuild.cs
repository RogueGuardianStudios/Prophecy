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

        /// <summary>The one menu footprint — 90% of the design space, leaving a 5% margin of
        /// world on every side (Matt: "only a 10% non-menu border").</summary>
        public const float MenuWidth = 1920f * 0.9f;
        public const float MenuHeight = 1080f * 0.9f;

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
