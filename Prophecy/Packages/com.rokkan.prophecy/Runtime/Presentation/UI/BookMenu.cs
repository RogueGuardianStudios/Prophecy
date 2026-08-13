using Rokkan.Prophecy.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The Bearer's own book — map and log — page-flipped with the stick's left/right (the
    /// shoulders turn the tab carousel now), opening always on the map (spec §8.3; no page
    /// memory). Options moved to its own tab, the carousel's last.
    ///
    /// <para><b>The log is a transcript, not a tracker.</b> No objectives, no checkboxes, no
    /// counters, no waypoints — entries are written by the Bearer in his own hand, in
    /// earnest, and NEVER mutate after being written. The gray-box entries below are
    /// placeholders for the shape: past tense, confident, frozen. When the real log arrives,
    /// that confidence over a darkening world is the whole horror, and nothing comments on
    /// it.</para>
    ///
    /// </summary>
    internal sealed class BookMenu : MenuRoot.MenuPanel
    {
        private readonly string[] _titles = { "THE MAP", "THE LOG" };
        private readonly string[] _pages =
        {
            "No map yet drawn.\n\nThe roads I have walked are few, and I know them.",

            "I write what I was told, and what I did.\n\n" +
            "— Crossed the gray fields at the arena's edge. A great roc barred the far " +
            "rooms; it does not pass the doorways, and I do not think it can.\n\n" +
            "— Mirefen's art carries me over still water. I crossed dry-shod and came " +
            "back the same way.",
        };

        private Label _title;
        private Label _body;
        private int _page;

        public BookMenu(VisualElement layer)
        {
            var panel = UiBuild.Bordered(layer, "Book",
                                         UiPalette.Umber, UiPalette.Parchment, 3f);
            UiBuild.Centre(panel, UiBuild.MenuWidth, UiBuild.MenuHeight);
            Root = panel;

            UiBuild.Tabs(panel, 2);

            var page = UiBuild.PageFrame(panel);

            _title = UiBuild.Text(page, "Title", "", 24, UiPalette.Umber);
            UiBuild.Place(_title, left: 16f, top: 12f, width: 400f, height: 32f);

            _body = UiBuild.Text(page, "Body", "", 24, UiPalette.Ink);
            UiBuild.Place(_body, left: 28f, top: 56f, width: 1050f, height: 760f);

            var hints = UiBuild.Text(panel, "Hints",
                                     "Left / Right  turn the page        LB / RB  menu        B  close",
                                     20, UiPalette.Muted, TextAnchor.MiddleCenter);
            UiBuild.Place(hints, left: 0f, right: 0f, bottom: 8f, height: 30f);

            IsOpen = false;
        }

        public override void Opened(CharacterSim sim)
        {
            _page = 0;   // always the map — the book has no memory of where you left it
            Draw();
        }

        public override void Tick(in MenuRoot.Frame frame)
        {
            // The stick turns the book's own pages; the shoulders belong to the tab rail.
            if (frame.NavX < 0 && _page > 0) { _page--; Draw(); }
            if (frame.NavX > 0 && _page < _pages.Length - 1) { _page++; Draw(); }
        }

        private void Draw()
        {
            _title.text = _titles[_page];
            _body.text = _pages[_page];
        }
    }
}
