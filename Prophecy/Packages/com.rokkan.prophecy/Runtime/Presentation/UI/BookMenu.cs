using Rokkan.Prophecy.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The Bearer's own book — map, log, options — page-flipped with LB/RB, opening always
    /// on the map (spec §8.3; no page memory).
    ///
    /// <para><b>The log is a transcript, not a tracker.</b> No objectives, no checkboxes, no
    /// counters, no waypoints — entries are written by the Bearer in his own hand, in
    /// earnest, and NEVER mutate after being written. The gray-box entries below are
    /// placeholders for the shape: past tense, confident, frozen. When the real log arrives,
    /// that confidence over a darkening world is the whole horror, and nothing comments on
    /// it.</para>
    ///
    /// <para><b>The options page reveals nothing</b> (spec §9.1): no corruption readout, no
    /// bind count, no region states — those constraints bind every future edit of this page,
    /// not just this stub. The parry-window setting is listed as the accessibility control it
    /// is, gated to lit Warding Flames when those exist.</para>
    /// </summary>
    internal sealed class BookMenu : MenuRoot.MenuPanel
    {
        private readonly string[] _titles = { "THE MAP", "THE LOG", "OPTIONS" };
        private readonly string[] _pages =
        {
            "No map yet drawn.\n\nThe roads I have walked are few, and I know them.",

            "I write what I was told, and what I did.\n\n" +
            "— Crossed the gray fields at the arena's edge. A great roc barred the far " +
            "rooms; it does not pass the doorways, and I do not think it can.\n\n" +
            "— Mirefen's art carries me over still water. I crossed dry-shod and came " +
            "back the same way.",

            "Parry window — set at a lit Warding Flame.\n\n" +
            "Audio, display and remapping — anywhere, when there are settings to set.\n\n" +
            "(The gray box keeps this page honest but empty.)",
        };

        private Text _title;
        private Text _body;
        private int _page;

        public BookMenu(Transform canvas)
        {
            var panel = UiBuild.Bordered(canvas, "Book", new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(460f, 380f),
                                         UiPalette.Umber, UiPalette.Parchment, 2f);
            Root = panel.transform.parent.gameObject;

            _title = UiBuild.Label(panel.transform, "Title", new Vector2(0f, 1f),
                                   new Vector2(18f, -12f), new Vector2(300f, 24f), "", 16,
                                   UiPalette.Umber);

            _body = UiBuild.Label(panel.transform, "Body", new Vector2(0f, 1f),
                                  new Vector2(24f, -52f), new Vector2(412f, 280f), "", 14,
                                  UiPalette.Ink);

            UiBuild.Label(panel.transform, "Hints", new Vector2(0.5f, 0f), new Vector2(0f, 10f),
                          new Vector2(420f, 20f), "LB / RB  turn the page        B  close",
                          12, UiPalette.Muted, TextAnchor.MiddleCenter);

            Root.SetActive(false);
        }

        public override void Opened(CharacterSim sim)
        {
            _page = 0;   // always the map — the book has no memory of where you left it
            Draw();
        }

        public override void Tick(in MenuRoot.Frame frame)
        {
            if (frame.PageLeft && _page > 0) { _page--; Draw(); }
            if (frame.PageRight && _page < _pages.Length - 1) { _page++; Draw(); }
        }

        private void Draw()
        {
            _title.text = _titles[_page];
            _body.text = _pages[_page];
        }
    }
}
