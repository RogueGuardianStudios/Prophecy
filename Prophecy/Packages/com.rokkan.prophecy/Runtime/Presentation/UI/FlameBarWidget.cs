using UnityEngine;
using UnityEngine.UI;

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
        private readonly RectTransform _fill;
        private readonly RectTransform _segment;
        private readonly RectTransform _segmentRule;
        private readonly float _width;

        /// <summary>Build into <paramref name="parent"/> at <paramref name="offset"/> from the
        /// top-left, with a thin dark umber border per spec §4.1.</summary>
        public FlameBarWidget(Transform parent, Vector2 offset, Vector2 size)
        {
            _width = size.x - 2f;
            float height = size.y - 2f;

            var ground = UiBuild.Bordered(parent, "FlameBar", new Vector2(0f, 1f), offset, size,
                                          UiPalette.Umber, UiPalette.ParchmentEmpty, 1f);

            _fill = UiBuild.Solid(ground.transform, "Fill", new Vector2(0f, 0.5f),
                                  Vector2.zero, new Vector2(0f, height),
                                  UiPalette.HearthGold).rectTransform;

            _segment = UiBuild.Solid(ground.transform, "CostSegment", new Vector2(0f, 0.5f),
                                     Vector2.zero, new Vector2(0f, height),
                                     UiPalette.CostSegment).rectTransform;

            // The 1px umber rule separating spend-preview from keep (spec §4.2).
            _segmentRule = UiBuild.Solid(ground.transform, "CostRule", new Vector2(0f, 0.5f),
                                         Vector2.zero, new Vector2(1f, height),
                                         UiPalette.Umber).rectTransform;
        }

        public void Set(float current, float max, float cost)
        {
            float fraction = max <= 0f ? 0f : Mathf.Clamp01(current / max);
            _fill.sizeDelta = new Vector2(_width * fraction, _fill.sizeDelta.y);

            // The segment sits at the leading edge of the FILLED portion: what this cast would
            // take. An unaffordable cost shades the whole fill — everything you have, still
            // not enough — which is the same sentence the muted emblem is saying.
            float segment = max <= 0f ? 0f : Mathf.Clamp01(Mathf.Min(cost, current) / max);
            bool visible = segment > 0.0001f;

            _segment.gameObject.SetActive(visible);
            _segmentRule.gameObject.SetActive(visible);

            if (!visible) return;

            float left = _width * (fraction - segment);
            _segment.anchoredPosition = new Vector2(left, 0f);
            _segment.sizeDelta = new Vector2(_width * segment, _segment.sizeDelta.y);
            _segmentRule.anchoredPosition = new Vector2(left, 0f);
        }
    }
}
