using System.Collections.Generic;
using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Abilities;
using Rokkan.Prophecy.Sim.Arts;
using UnityEngine;
using UnityEngine.UI;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The working page — the Order's book, handed to the Bearer, and the only screen with a
    /// performance requirement (spec §8.1): two casts before a bad door must be one open, two
    /// actions, one close. So: no animation yet, selection IS equipping, and A casts from the
    /// page through the same <see cref="CastArt.Cast"/> the RB button uses — playing a cast
    /// from the volume and from the pad are the same act in the sim.
    ///
    /// <para>The header carries the reserve bar with its cost segment, the identical widget
    /// to the HUD's, so the planning page and the play HUD speak one language. Rows already
    /// running are marked <c>running</c> with no timer — the room is the timer.</para>
    /// </summary>
    internal sealed class ArtsVolumeMenu : MenuRoot.MenuPanel
    {
        private sealed class Row
        {
            public Image Highlight;
            public Text Name;
            public Text Pips;
            public Text Running;
        }

        private readonly FlameBarWidget _bar;
        private readonly List<Row> _rows = new List<Row>();
        private int _selected;

        public ArtsVolumeMenu(Transform canvas)
        {
            var panel = UiBuild.Bordered(canvas, "ArtsVolume", new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(420f, 470f),
                                         UiPalette.Umber, UiPalette.Parchment, 2f);
            Root = panel.transform.parent.gameObject;

            UiBuild.Label(panel.transform, "Title", new Vector2(0f, 1f), new Vector2(18f, -12f),
                          new Vector2(300f, 24f), "THE ORDER'S VOLUME", 16, UiPalette.Umber);

            _bar = new FlameBarWidget(panel.transform, new Vector2(18f, -44f),
                                      new Vector2(380f, 13f));

            var arts = ArtCatalog.All;
            for (int i = 0; i < arts.Length; i++)
            {
                var row = new Row();
                float y = -76f - i * 44f;

                row.Highlight = UiBuild.Solid(panel.transform, $"Row{i}", new Vector2(0f, 1f),
                                              new Vector2(10f, y), new Vector2(396f, 40f),
                                              UiPalette.Bright);

                row.Name = UiBuild.Label(row.Highlight.transform, "Name", new Vector2(0f, 0.5f),
                                         new Vector2(10f, 0f), new Vector2(250f, 30f),
                                         arts[i].DisplayName, 15, UiPalette.Ink,
                                         TextAnchor.MiddleLeft);

                row.Pips = UiBuild.Label(row.Highlight.transform, "Pips", new Vector2(1f, 0.5f),
                                         new Vector2(-12f, 0f), new Vector2(90f, 30f),
                                         HudController.Pips(arts[i].Cost), 12,
                                         UiPalette.HearthGold, TextAnchor.MiddleRight);

                row.Running = UiBuild.Label(row.Highlight.transform, "Running",
                                            new Vector2(1f, 0.5f), new Vector2(-108f, 0f),
                                            new Vector2(80f, 30f), "running", 12,
                                            UiPalette.Gilt, TextAnchor.MiddleRight);

                _rows.Add(row);
            }

            UiBuild.Label(panel.transform, "Hints", new Vector2(0.5f, 0f), new Vector2(0f, 10f),
                          new Vector2(380f, 20f), "A  cast        B  close", 12,
                          UiPalette.Muted, TextAnchor.MiddleCenter);

            Root.SetActive(false);
        }

        public override void Opened(CharacterSim sim)
        {
            // Every open lands on the equipped art — the page has no memory of its own, the
            // character's slot is the memory.
            _selected = 0;

            if (sim != null)
            {
                for (int i = 0; i < ArtCatalog.All.Length; i++)
                    if (ArtCatalog.All[i].Id == sim.EquippedArt)
                        _selected = i;
            }
        }

        public override void Tick(in MenuRoot.Frame frame)
        {
            var sim = frame.Sim;
            if (sim == null) return;

            if (frame.NavY != 0)
            {
                int count = ArtCatalog.All.Length;
                _selected = (_selected - frame.NavY + count) % count;

                // Equip on selection (spec §8.1): landing on a row IS equipping it, so
                // closing the volume without casting still leaves the art in the slot.
                sim.EquippedArt = ArtCatalog.All[_selected].Id;
            }

            if (frame.Confirm)
            {
                var id = ArtCatalog.All[_selected].Id;
                sim.EquippedArt = id;

                // Buoyancy's cast is its module's water toggle and runs on the world clock;
                // from the paused page the cast is the toggle flip the button would do.
                // Everything else goes through the one cast path.
                if (id != ArtId.Buoyancy) CastArt.Cast(sim, id, sim.CurrentTick);
            }

            Draw(sim);
        }

        private void Draw(CharacterSim sim)
        {
            var equipped = ArtCatalog.Find(sim.EquippedArt);
            _bar.Set(sim.Reserve.Current, sim.Reserve.Max, equipped.Cost);

            var buoyancy = sim.Get<Buoyancy>();

            for (int i = 0; i < _rows.Count; i++)
            {
                var entry = ArtCatalog.All[i];
                bool selected = i == _selected;
                bool affordable = sim.Reserve.CanAfford(entry.Cost);
                bool running = entry.Id == ArtId.Buoyancy
                    ? buoyancy != null && buoyancy.FloatOn
                    : sim.ActiveArts.Contains(entry.Id);

                var highlight = _rows[i].Highlight;
                highlight.color = selected ? UiPalette.Bright : UiPalette.Parchment;

                _rows[i].Name.color = affordable ? UiPalette.Ink : UiPalette.Muted;
                _rows[i].Pips.color = affordable ? UiPalette.HearthGold : UiPalette.MutedPip;
                _rows[i].Running.gameObject.SetActive(running);
            }
        }
    }
}
