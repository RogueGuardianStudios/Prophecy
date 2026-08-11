using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The pack — the character sheet and the goods, mostly a read-only record of what the
    /// player has taken; the page does not comment on that (spec §8.2). Stats live here in
    /// their spec colors: Might a steel numeral, Flame rank a gold ROMAN numeral (the rank
    /// carries the word; the reserve is a bar and never rendered as one here), Heart a red
    /// numeral, and Resolve as plain progress.
    ///
    /// <para>The Resolve rite (spending a banked level) belongs on this page and arrives with
    /// the Resolve economy — until something awards Resolve there is nothing to spend.</para>
    /// </summary>
    internal sealed class PackMenu : MenuRoot.MenuPanel
    {
        private Label _might;
        private Label _flame;
        private Label _heart;
        private Label _resolve;
        private Label _goods;

        public PackMenu(VisualElement layer)
        {
            var panel = UiBuild.Bordered(layer, "Pack",
                                         UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Centre(panel, 420f, 360f);
            Root = panel;

            var title = UiBuild.Text(panel, "Title", "THE PACK", 16, UiPalette.Umber);
            UiBuild.Place(title, left: 18f, top: 12f, width: 300f, height: 24f);

            StatRow(panel, "Might", 52f, UiPalette.Steel, out _might);
            StatRow(panel, "Flame", 82f, UiPalette.HearthGold, out _flame);
            StatRow(panel, "Heart", 112f, UiPalette.FestivalRed, out _heart);
            StatRow(panel, "Resolve", 142f, UiPalette.Ink, out _resolve);

            _goods = UiBuild.Text(panel, "Goods", "", 14, UiPalette.Ink);
            UiBuild.Place(_goods, left: 24f, top: 190f, width: 372f, height: 130f);

            var hints = UiBuild.Text(panel, "Hints", "B  close", 12, UiPalette.Muted,
                                     TextAnchor.MiddleCenter);
            UiBuild.Place(hints, left: 0f, right: 0f, bottom: 10f, height: 20f);

            IsOpen = false;
        }

        private static void StatRow(VisualElement panel, string statName, float top,
                                    Color valueColor, out Label value)
        {
            var name = UiBuild.Text(panel, statName + "Name", statName, 15, UiPalette.Ink);
            UiBuild.Place(name, left: 24f, top: top, width: 120f, height: 22f);

            value = UiBuild.Text(panel, statName + "Value", "", 17, valueColor);
            UiBuild.Place(value, left: 170f, top: top, width: 200f, height: 22f);
        }

        public override void Opened(CharacterSim sim) => Draw(sim);

        public override void Tick(in MenuRoot.Frame frame) => Draw(frame.Sim);

        private void Draw(CharacterSim sim)
        {
            if (sim == null) return;

            var stats = sim.Stats;

            _might.text = stats.LevelOf(StatKind.Might).ToString();
            _flame.text = UiBuild.Roman(stats.LevelOf(StatKind.Flame));
            _heart.text = stats.LevelOf(StatKind.Heart).ToString();

            int cost = stats.Tuning.ResolveForNextLevel(stats.TotalLevels);
            _resolve.text = stats.UnspentLevels > 0
                ? "a rite is owed"
                : $"{stats.Resolve} of {cost}";

            _goods.text =
                $"Flasks — {sim.Flasks.Filled} of {sim.Flasks.Capacity}\n" +
                "\n" +
                "The rest of the pack is empty.\n" +
                "It will not stay that way.";
        }
    }
}
