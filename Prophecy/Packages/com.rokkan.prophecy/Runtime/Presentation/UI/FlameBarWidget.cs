using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The Flame reserve bar with its cost preview — Hearth Gold on shadowed parchment, and
    /// the equipped art's cost as a faded segment at the leading edge of the fill (spec §4.2).
    /// The segment IS the affordability read: what a cast will take, before taking it, and as
    /// casts land it redraws further left so stacking stays legible without numbers.
    ///
    /// <para>One widget class on purpose: the HUD and the arts volume header must speak one
    /// language (spec §8.1), and one implementation cannot drift into two dialects.</para>
    ///
    /// <para>Never labelled. The reserve does not carry the word "Flame" anywhere in UI —
    /// the bar is self-evident (spec §2.2).</para>
    /// </summary>
    public sealed class FlameBarWidget
    {
        private readonly VisualElement _fill;
        private readonly VisualElement _segment;
        private readonly VisualElement _segmentRule;
        private readonly float _width;

        /// <summary>Build into <paramref name="parent"/> at (<paramref name="left"/>,
        /// <paramref name="top"/>), with a thin dark umber border per spec §4.1.</summary>
        public FlameBarWidget(VisualElement parent, float left, float top, Vector2 size)
        {
            _width = size.x - 2f;
            float height = size.y - 2f;

            var ground = UiBuild.Bordered(parent, "FlameBar",
                                          UiPalette.Umber, UiPalette.ParchmentEmpty, 1f);
            UiBuild.Place(ground, left: left, top: top, width: size.x, height: size.y);

            _fill = UiBuild.Solid(ground, "Fill", UiPalette.HearthGold);
            UiBuild.Place(_fill, left: 0f, top: 0f, width: 0f, height: height);

            _segment = UiBuild.Solid(ground, "CostSegment", UiPalette.CostSegment);
            UiBuild.Place(_segment, left: 0f, top: 0f, width: 0f, height: height);

            // The 1px umber rule separating spend-preview from keep (spec §4.2).
            _segmentRule = UiBuild.Solid(ground, "CostRule", UiPalette.Umber);
            UiBuild.Place(_segmentRule, left: 0f, top: 0f, width: 1f, height: height);
        }

        public void Set(float current, float max, float cost)
        {
            float fraction = max <= 0f ? 0f : Mathf.Clamp01(current / max);
            _fill.style.width = _width * fraction;

            // The segment sits at the leading edge of the FILLED portion: what this cast would
            // take. An unaffordable cost shades the whole fill — everything you have, still
            // not enough — which is the same sentence the muted emblem is saying.
            float segment = max <= 0f ? 0f : Mathf.Clamp01(Mathf.Min(cost, current) / max);
            bool visible = segment > 0.0001f;

            _segment.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _segmentRule.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible) return;

            float left = _width * (fraction - segment);
            _segment.style.left = left;
            _segment.style.width = _width * segment;
            _segmentRule.style.left = left;
        }
    }
}
