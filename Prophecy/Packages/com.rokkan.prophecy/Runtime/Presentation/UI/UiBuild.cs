using UnityEngine;
using UnityEngine.UI;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The few strokes the gray-box UI is drawn with: solid rects, bordered rects, and plain
    /// text. Built in code at runtime — the <see cref="TransitionVeil"/> precedent, scaled up.
    ///
    /// <para><b>Why runtime-built and not a hand-authored canvas.</b> A serialized uGUI
    /// hierarchy is exactly the fragile cross-referenced YAML this project refuses to
    /// hand-edit, and a builder that authors it into the scene asset would need to wire dozens
    /// of component references to survive regeneration. Building the tree in Awake keeps the
    /// whole layout in reviewable C#, keeps the generated UI scene down to two components, and
    /// costs a few milliseconds once per session.</para>
    /// </summary>
    internal static class UiBuild
    {
        private static Font _font;

        /// <summary>The built-in font, so the gray box ships zero UI assets.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>A screen-space canvas scaled against 1920×1080 design space.</summary>
        public static Canvas Canvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>A rect anchored by the same point it is positioned from.</summary>
        public static RectTransform Rect(Transform parent, string name, Vector2 anchor,
                                         Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>A solid rect. The workhorse.</summary>
        public static Image Solid(Transform parent, string name, Vector2 anchor,
                                  Vector2 offset, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, anchor, offset, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A rect that fills its parent, inset on every side.</summary>
        public static Image Fill(Transform parent, string name, Color color, float inset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A bordered rect: outer solid drawn in the border color, inner fill inset
        /// by the border width. Returns the inner image; the border recolors via its parent.</summary>
        public static Image Bordered(Transform parent, string name, Vector2 anchor,
                                     Vector2 offset, Vector2 size,
                                     Color border, Color ground, float borderWidth)
        {
            var outer = Solid(parent, name, anchor, offset, size, border);
            return Fill(outer.transform, "Ground", ground, borderWidth);
        }

        /// <summary>Plain text, sized to its rect.</summary>
        public static Text Label(Transform parent, string name, Vector2 anchor, Vector2 offset,
                                 Vector2 size, string content, int fontSize, Color color,
                                 TextAnchor align = TextAnchor.UpperLeft)
        {
            var rect = Rect(parent, name, anchor, offset, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.color = color;
            text.text = content;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
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
