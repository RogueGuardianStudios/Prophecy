using Rokkan.Prophecy.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The options page, the carousel's last tab — graduated out of the Book (Matt: four
    /// tabs, options last).
    ///
    /// <para><b>This page reveals nothing</b> (spec §9.1): no corruption readout, no bind
    /// count, no region states — those constraints bind every future edit of this page, not
    /// just this stub. The parry-window setting is listed as the accessibility control it
    /// is, gated to lit Warding Flames when those exist.</para>
    /// </summary>
    internal sealed class OptionsMenu : MenuRoot.MenuPanel
    {
        public OptionsMenu(VisualElement layer)
        {
            var panel = UiBuild.Bordered(layer, "Options",
                                         UiPalette.Umber, UiPalette.Parchment, 3f);
            UiBuild.Centre(panel, UiBuild.MenuWidth, UiBuild.MenuHeight);
            Root = panel;

            UiBuild.Tabs(panel, 3);

            var page = UiBuild.PageFrame(panel);

            var body = UiBuild.Text(page, "Body",
                "Parry window — set at a lit Warding Flame.\n\n" +
                "Audio, display and remapping — anywhere, when there are settings to set.\n\n" +
                "(The gray box keeps this page honest but empty.)",
                24, UiPalette.Ink);
            UiBuild.Place(body, left: 28f, top: 20f, width: 1050f, height: 800f);

            var hints = UiBuild.Text(panel, "Hints", "LB / RB  menu        B  close", 20,
                                     UiPalette.Muted, TextAnchor.MiddleCenter);
            UiBuild.Place(hints, left: 0f, right: 0f, bottom: 8f, height: 30f);

            IsOpen = false;
        }

        public override void Opened(CharacterSim sim) { }

        public override void Tick(in MenuRoot.Frame frame) { }
    }
}
