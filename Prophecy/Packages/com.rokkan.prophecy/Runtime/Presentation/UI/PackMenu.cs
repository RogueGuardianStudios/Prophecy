using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;
using UnityEngine.UI;

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
        private Text _might;
        private Text _flame;
        private Text _heart;
        private Text _resolve;
        private Text _goods;

        public PackMenu(Transform canvas)
        {
            var panel = UiBuild.Bordered(canvas, "Pack", new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(420f, 360f),
                                         UiPalette.Umber, UiPalette.Parchment, 2f);
            Root = panel.transform.parent.gameObject;

            UiBuild.Label(panel.transform, "Title", new Vector2(0f, 1f), new Vector2(18f, -12f),
                          new Vector2(300f, 24f), "THE PACK", 16, UiPalette.Umber);

            UiBuild.Label(panel.transform, "MightName", new Vector2(0f, 1f),
                          new Vector2(24f, -52f), new Vector2(120f, 22f), "Might", 15,
                          UiPalette.Ink);
            _might = UiBuild.Label(panel.transform, "MightValue", new Vector2(0f, 1f),
                                   new Vector2(170f, -52f), new Vector2(120f, 22f), "", 17,
                                   UiPalette.Steel);

            UiBuild.Label(panel.transform, "FlameName", new Vector2(0f, 1f),
                          new Vector2(24f, -82f), new Vector2(120f, 22f), "Flame", 15,
                          UiPalette.Ink);
            _flame = UiBuild.Label(panel.transform, "FlameValue", new Vector2(0f, 1f),
                                   new Vector2(170f, -82f), new Vector2(120f, 22f), "", 17,
                                   UiPalette.HearthGold);

            UiBuild.Label(panel.transform, "HeartName", new Vector2(0f, 1f),
                          new Vector2(24f, -112f), new Vector2(120f, 22f), "Heart", 15,
                          UiPalette.Ink);
            _heart = UiBuild.Label(panel.transform, "HeartValue", new Vector2(0f, 1f),
                                   new Vector2(170f, -112f), new Vector2(120f, 22f), "", 17,
                                   UiPalette.FestivalRed);

            UiBuild.Label(panel.transform, "ResolveName", new Vector2(0f, 1f),
                          new Vector2(24f, -142f), new Vector2(120f, 22f), "Resolve", 15,
                          UiPalette.Ink);
            _resolve = UiBuild.Label(panel.transform, "ResolveValue", new Vector2(0f, 1f),
                                     new Vector2(170f, -142f), new Vector2(160f, 22f), "", 15,
                                     UiPalette.Ink);

            _goods = UiBuild.Label(panel.transform, "Goods", new Vector2(0f, 1f),
                                   new Vector2(24f, -190f), new Vector2(372f, 130f), "", 14,
                                   UiPalette.Ink);

            UiBuild.Label(panel.transform, "Hints", new Vector2(0.5f, 0f), new Vector2(0f, 10f),
                          new Vector2(380f, 20f), "B  close", 12, UiPalette.Muted,
                          TextAnchor.MiddleCenter);

            Root.SetActive(false);
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
