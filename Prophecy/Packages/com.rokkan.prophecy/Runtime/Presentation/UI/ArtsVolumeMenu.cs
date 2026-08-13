using System.Collections.Generic;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Arts;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The working page — the Order's book, handed to the Bearer. The carousel's Arts tab.
    ///
    /// <para><b>Only KNOWN arts appear</b> (Matt: you will not start with all arts): the
    /// rows are SLOTS, dealt from <see cref="CharacterSim.KnownArts"/> in catalog order and
    /// compacted — an untaught art leaves no gap, and a loadout toggle flips the page live.
    /// An empty page says so instead of showing nothing.</para>
    ///
    /// <para><b>Press selects, hold casts (Matt, superseding spec §8.1's equip-on-select):</b>
    /// the stick moves a CURSOR that equips nothing; pressing A equips the art under it
    /// (the gilt frame moves); holding A on the equipped art sweeps a fill across its row
    /// and CASTS when the row is full — releasing early abandons the cast and the fill.
    /// One cast per hold: recasting takes a fresh press.</para>
    ///
    /// <para>The header carries the reserve bar with its cost segment, the identical widget
    /// to the HUD's, so the planning page and the play HUD speak one language. Rows already
    /// running are marked <c>running</c> with no timer — the room is the timer.</para>
    /// </summary>
    internal sealed class ArtsVolumeMenu : MenuRoot.MenuPanel
    {
        private sealed class Row
        {
            public VisualElement Highlight;
            public VisualElement Fill;
            public Label Glyph;
            public Label Name;
            public Label Description;
            public Label Pips;
            public Label Running;
        }

        private const float RowWidth = 1084f;

        private readonly FlameBarWidget _bar;
        private readonly List<Row> _rows = new List<Row>();

        /// <summary>Indexes into the sim's art table for the arts it knows, in table
        /// order — the compacted page.</summary>
        private readonly List<int> _known = new List<int>();

        private Label _empty;
        private int _cursor;
        private float _fill;
        private bool _castArmed;

        public ArtsVolumeMenu(VisualElement layer)
        {
            var panel = UiBuild.Bordered(layer, "ArtsVolume",
                                         UiPalette.Umber, UiPalette.Parchment, 3f);
            UiBuild.Centre(panel, UiBuild.MenuWidth, UiBuild.MenuHeight);
            Root = panel;

            UiBuild.Tabs(panel, 1);

            var page = UiBuild.PageFrame(panel);

            _bar = new FlameBarWidget(page, 16f, 12f, new Vector2(1076f, 22f));

            // One slot per authored art — the most the page can ever need. Which art a
            // slot shows (and whether it shows at all) is dealt each draw.
            for (int i = 0; i < ArtCatalog.DefaultTable().Length; i++)
            {
                var row = new Row();

                row.Highlight = UiBuild.Solid(page, $"Row{i}", UiPalette.Bright);
                UiBuild.Place(row.Highlight, left: 12f, top: 48f + i * 100f,
                              width: RowWidth, height: 92f);

                // The hold-to-cast fill, under the text: the cost segment's tint sweeping
                // the row is the "are you sure" the cast no longer asks in words.
                row.Fill = UiBuild.Solid(row.Highlight, "Fill", UiPalette.CostSegment);
                UiBuild.Place(row.Fill, left: 0f, top: 0f, bottom: 0f, width: 0f);

                // Left block: the art's icon with its name under it (Matt). The gray-box
                // icon is a squared glyph — two letters standing where art will stand.
                var icon = UiBuild.Bordered(row.Highlight, "Icon",
                                            UiPalette.Umber, UiPalette.ParchmentEmpty, 2f);
                UiBuild.Place(icon, left: 56f, top: 5f, width: 52f, height: 52f);

                row.Glyph = UiBuild.Text(icon, "Glyph", "", 20, UiPalette.Umber,
                                         TextAnchor.MiddleCenter);
                UiBuild.Place(row.Glyph, left: 0f, top: 0f, right: 0f, bottom: 0f);

                row.Name = UiBuild.Text(row.Highlight, "Name", "", 13,
                                        UiPalette.Ink, TextAnchor.UpperCenter);
                UiBuild.Place(row.Name, left: 12f, top: 59f, width: 140f, height: 32f);

                // Centre: the art's one-line telling.
                row.Description = UiBuild.Text(row.Highlight, "Description", "", 18,
                                               UiPalette.Ink, TextAnchor.MiddleLeft);
                UiBuild.Place(row.Description, left: 170f, top: 0f, width: 620f, height: 92f);

                row.Running = UiBuild.Text(row.Highlight, "Running", "running", 20,
                                           UiPalette.Gilt, TextAnchor.MiddleRight);
                UiBuild.Place(row.Running, right: 210f, top: 0f, width: 150f, height: 92f);

                row.Pips = UiBuild.Text(row.Highlight, "Pips", "", 20,
                                        UiPalette.HearthGold, TextAnchor.MiddleRight);
                UiBuild.Place(row.Pips, right: 20f, top: 0f, width: 170f, height: 92f);

                _rows.Add(row);
            }

            _empty = UiBuild.Text(page, "Empty", "The Order has taught nothing yet.", 24,
                                  UiPalette.Muted, TextAnchor.MiddleCenter);
            UiBuild.Place(_empty, left: 0f, right: 0f, top: 48f, height: 92f);
            _empty.style.display = DisplayStyle.None;

            var hints = UiBuild.Text(panel, "Hints",
                                     "A  select        hold A  cast        LB / RB  menu        B  close",
                                     20, UiPalette.Muted, TextAnchor.MiddleCenter);
            UiBuild.Place(hints, left: 0f, right: 0f, bottom: 8f, height: 30f);

            IsOpen = false;
        }

        public override void Opened(CharacterSim sim)
        {
            _fill = 0f;
            _castArmed = false;
            _cursor = 0;

            if (sim == null) return;

            RebuildKnown(sim);

            // Every open lands the cursor on the equipped art — the page has no memory of
            // its own, the character's slot is the memory.
            for (int i = 0; i < _known.Count; i++)
                if (sim.ArtTuning.Arts[_known[i]].Id == sim.EquippedArt)
                    _cursor = i;
        }

        public override void Tick(in MenuRoot.Frame frame)
        {
            var sim = frame.Sim;
            if (sim == null) return;

            RebuildKnown(sim);

            if (_known.Count == 0)
            {
                _fill = 0f;
                _castArmed = false;
                Draw(sim);
                return;
            }

            _cursor = Mathf.Clamp(_cursor, 0, _known.Count - 1);

            if (frame.NavY != 0)
            {
                _cursor = (_cursor - frame.NavY + _known.Count) % _known.Count;

                // Moving the cursor abandons any hold in progress.
                _fill = 0f;
                _castArmed = false;
            }

            var art = sim.ArtTuning.Arts[_known[_cursor]];

            // The press SELECTS (equips) — and arms the hold, so press-and-keep-holding
            // is the single gesture that selects and then casts. Equipping writes the slot
            // directly: it is a loadout edit, the same class of write as the pack's gear
            // boxes, and InputFrame's doc names it as sanctioned.
            if (frame.Confirm)
            {
                sim.EquippedArt = art.Id;
                _fill = 0f;
                _castArmed = true;
            }

            if (frame.ConfirmHeld && _castArmed && sim.EquippedArt == art.Id)
            {
                // The fill is purely visual, so frame time is honest here. The CAST is not:
                // it is parked as a request the sim consumes at the top of its next tick —
                // the page runs with the clock paused, and a cast applied from UI code
                // between ticks is a mutation no headless replay could reproduce.
                _fill += Time.deltaTime / Mathf.Max(0.05f, sim.ArtTuning.HoldToCastSeconds);

                if (_fill >= 1f)
                {
                    _fill = 0f;
                    _castArmed = false;   // one cast per hold; a fresh press re-arms

                    // A module-owned art (Buoyancy) casts only from its own button verb;
                    // the page cannot cast it, the same fact the sim's cast path enforces.
                    if (!art.ModuleOwned) sim.RequestCast(art.Id);
                }
            }
            else if (!frame.ConfirmHeld)
            {
                _fill = 0f;
                _castArmed = false;
            }

            Draw(sim);
        }

        private void RebuildKnown(CharacterSim sim)
        {
            _known.Clear();
            var table = sim.ArtTuning.Arts;
            for (int i = 0; i < table.Count && _known.Count < _rows.Count; i++)
                if (sim.KnownArts.Contains(table[i].Id)) _known.Add(i);
        }

        /// <summary>Cost as CASTING DIFFICULTY (Matt): pips for the share of the bar the
        /// cast will take, one pip per ~15%. The same art gets easier as the Flame rank
        /// deepens the reserve — the pips recount themselves.</summary>
        private static string Difficulty(float cost, float max)
        {
            if (cost <= 0f) return "—";

            float fraction = max > 0f ? cost / max : 1f;
            return HudController.Pips(Mathf.Max(1, Mathf.RoundToInt(fraction / 0.15f)));
        }

        private void Draw(CharacterSim sim)
        {
            _bar.Set(sim.Reserve.Current, sim.Reserve.Max, sim.ArtTuning.CostOf(sim.EquippedArt));

            for (int r = 0; r < _rows.Count; r++)
            {
                if (r >= _known.Count)
                {
                    _rows[r].Highlight.style.display = DisplayStyle.None;
                    continue;
                }

                var entry = sim.ArtTuning.Arts[_known[r]];
                bool cursor = r == _cursor;
                bool isEquipped = entry.Id == sim.EquippedArt;
                bool affordable = sim.Reserve.CanAfford(entry.Cost);
                bool running = sim.IsArtRunning(entry.Id);

                var row = _rows[r];
                row.Highlight.style.display = DisplayStyle.Flex;
                row.Highlight.style.top = 48f + r * 100f;
                row.Highlight.style.backgroundColor =
                    cursor ? UiPalette.Bright : UiPalette.Parchment;

                // The gilt frame is the equipped mark now that the cursor no longer equips.
                UiBuild.SetBorder(row.Highlight, UiPalette.Gilt, isEquipped ? 2f : 0f);

                row.Fill.style.width = isEquipped ? _fill * RowWidth : 0f;

                row.Glyph.text = entry.DisplayName.Length >= 2
                    ? entry.DisplayName.Substring(0, 2).ToUpperInvariant()
                    : entry.DisplayName.ToUpperInvariant();
                row.Name.text = entry.DisplayName;
                row.Name.style.color = affordable ? UiPalette.Ink : UiPalette.Muted;
                row.Description.text = entry.Description;
                row.Description.style.color = affordable ? UiPalette.Ink : UiPalette.Muted;
                row.Pips.text = Difficulty(entry.Cost, sim.Reserve.Max);
                row.Pips.style.color = affordable ? UiPalette.HearthGold : UiPalette.MutedPip;
                row.Running.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _empty.style.display = _known.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
