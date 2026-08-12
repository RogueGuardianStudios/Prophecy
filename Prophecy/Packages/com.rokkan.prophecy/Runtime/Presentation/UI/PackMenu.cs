using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The pack — the full sheet, still a read-only record of what the player has taken; the
    /// page does not comment on that (spec §8.2). Four zones: the LOADOUT top-left (the
    /// filmed player on the left of the split, his worn slots as item boxes arranged in his
    /// shape on the right — boots at the bottom, pants above, shirt above that, hands
    /// holding sword and shield; empty slots show the dash they are), the CONSUMABLES
    /// carousel under it (the centre card is the equipped consumable; D-pad Left/Right will
    /// turn it when there is more than one thing to cycle), the STATS window top-right —
    /// Resolve as a BAR in the seam's colors above the three numerals (Might steel, Flame
    /// rank a gold ROMAN numeral — the rank carries the word; the reserve is a bar and
    /// never rendered as one here — Heart red) — and the ITEMS window under it, its bottom
    /// flush with the carousel's: a scrolling grid of square cells, mostly empty outlines
    /// until the item economy fills them.
    ///
    /// <para>The Resolve rite (spending a banked level) belongs under the stats and arrives
    /// with the Resolve economy — until something awards Resolve there is nothing to spend.</para>
    /// </summary>
    internal sealed class PackMenu : MenuRoot.MenuPanel
    {
        /// <summary>The slot boxes, placed in the rough shape of a person (Matt): boots at the
        /// bottom, pants above, shirt above that, necklace at the throat; the hands hold sword
        /// and shield at chest height, gloves below the sword hand, rings on the other side.</summary>
        private static readonly (EquipSlot Slot, string Label, int Column, int Row)[] Boxes =
        {
            (EquipSlot.Necklace,  "Necklace", 1, 0),
            (EquipSlot.Sword,     "Sword",    0, 1),
            (EquipSlot.Shirt,     "Shirt",    1, 1),
            (EquipSlot.Shield,    "Shield",   2, 1),
            (EquipSlot.Gloves,    "Gloves",   0, 2),
            (EquipSlot.Pants,     "Pants",    1, 2),
            (EquipSlot.RingLeft,  "Ring",     2, 2),
            (EquipSlot.Boots,     "Boots",    1, 3),
            (EquipSlot.RingRight, "Ring",     2, 3),
        };

        private const int InventoryColumns = 9;

        /// <summary>Rows in the grid, not on the screen — six fit the viewport and the
        /// rest are what the scroll is for.</summary>
        private const int InventoryRows = 8;

        private readonly Label[] _worn = new Label[Boxes.Length];
        private Image _portrait;
        private VisualElement _figure;
        private PlayerPortrait _rig;
        private Label _might;
        private Label _flame;
        private Label _heart;
        private Label _resolve;
        private VisualElement _resolveFill;
        private Label _consumable;
        private ScrollView _inventoryScroll;
        private VisualElement _flaskFill;
        private Label _flaskCount;

        public PackMenu(VisualElement layer)
        {
            var panel = UiBuild.Bordered(layer, "Pack",
                                         UiPalette.Umber, UiPalette.Parchment, 3f);
            UiBuild.Centre(panel, UiBuild.MenuWidth, UiBuild.MenuHeight);
            Root = panel;

            var title = UiBuild.Text(panel, "Title", "THE PACK", 30, UiPalette.Umber);
            UiBuild.Place(title, left: 28f, top: 20f, width: 500f, height: 40f);

            BuildLoadout(panel);
            BuildCarousel(panel);
            BuildStats(panel);
            BuildInventory(panel);

            var hints = UiBuild.Text(panel, "Hints", "B  close", 20, UiPalette.Muted,
                                     TextAnchor.MiddleCenter);
            UiBuild.Place(hints, left: 0f, right: 0f, bottom: 14f, height: 30f);

            IsOpen = false;
        }

        private void BuildLoadout(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Loadout",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 28f, top: 76f, width: 824f, height: 560f);

            var caption = UiBuild.Text(region, "Caption", "LOADOUT", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 16f, top: 10f, width: 300f, height: 26f);

            // Split down the middle: the Bearer stands on the left, his slots on the right.
            var divide = UiBuild.Solid(region, "Divide", UiPalette.Umber);
            UiBuild.Place(divide, left: 411f, top: 44f, width: 2f, height: 502f);

            BuildFigure(region);

            // The live film of the player, over the figure's spot; the rect figure stays
            // underneath as the fallback for a sheet opened with no player to film.
            _portrait = new Image
            {
                name = "Portrait",
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit,
            };
            _portrait.style.position = Position.Absolute;
            _portrait.style.display = DisplayStyle.None;
            UiBuild.Place(_portrait, left: 0f, top: 44f, width: 409f, bottom: 0f);
            region.Add(_portrait);

            for (int i = 0; i < Boxes.Length; i++)
            {
                float x = 470f + Boxes[i].Column * 102f;
                float y = 103f + Boxes[i].Row * 102f;

                var box = UiBuild.Bordered(region, "Box" + i,
                                           UiPalette.Umber, UiPalette.ParchmentEmpty, 2f);
                UiBuild.Place(box, left: x, top: y, width: 92f, height: 92f);

                var slot = UiBuild.Text(box, "Slot", Boxes[i].Label, 13,
                                        UiPalette.Muted, TextAnchor.UpperCenter);
                UiBuild.Place(slot, left: 0f, top: 4f, right: 0f, height: 18f);

                _worn[i] = UiBuild.Text(box, "Worn", "", 16,
                                        UiPalette.Ink, TextAnchor.MiddleCenter);
                UiBuild.Place(_worn[i], left: 4f, top: 22f, right: 4f, bottom: 4f);
            }
        }

        /// <summary>The gray-box Bearer — plain rects in the palette's muted tone, standing
        /// where the portrait films. Only shown when there is no player to film.</summary>
        private void BuildFigure(VisualElement region)
        {
            _figure = new VisualElement { name = "Figure", pickingMode = PickingMode.Ignore };
            _figure.style.position = Position.Absolute;
            UiBuild.Place(_figure, left: 0f, top: 0f, right: 0f, bottom: 0f);
            region.Add(_figure);

            FigurePart(_figure, "Head", 174f, 80f, 64f, 64f);
            FigurePart(_figure, "Torso", 142f, 152f, 128f, 170f);
            FigurePart(_figure, "ArmL", 100f, 156f, 34f, 150f);
            FigurePart(_figure, "ArmR", 278f, 156f, 34f, 150f);
            FigurePart(_figure, "LegL", 154f, 330f, 50f, 170f);
            FigurePart(_figure, "LegR", 208f, 330f, 50f, 170f);
            FigurePart(_figure, "FootL", 144f, 504f, 60f, 26f);
            FigurePart(_figure, "FootR", 208f, 504f, 60f, 26f);
        }

        private static void FigurePart(VisualElement region, string name,
                                       float left, float top, float width, float height)
        {
            var part = UiBuild.Bordered(region, name, UiPalette.Umber, UiPalette.Muted, 2f);
            UiBuild.Place(part, left: left, top: top, width: width, height: height);
        }

        /// <summary>The consumables carousel under the loadout: five cards, the centre one
        /// the equipped consumable. Flasks are the only consumable the sim owns, so the
        /// neighbours are honestly empty until there is something to cycle onto them.</summary>
        private void BuildCarousel(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Consumables",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 28f, top: 660f, width: 824f, height: 252f);

            var caption = UiBuild.Text(region, "Caption", "CONSUMABLES", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 16f, top: 10f, width: 300f, height: 26f);

            for (int i = 0; i < 5; i++)
            {
                bool centre = i == 2;
                float x = 88f + i * 132f;

                var card = UiBuild.Bordered(region, "Card" + i,
                                            centre ? UiPalette.Gilt : UiPalette.Umber,
                                            centre ? UiPalette.Bright : UiPalette.ParchmentEmpty,
                                            centre ? 2f : 1.5f);
                UiBuild.Place(card, left: x, top: 88f, width: 120f, height: 120f);

                if (centre)
                {
                    _consumable = UiBuild.Text(card, "Consumable", "", 20,
                                               UiPalette.Ink, TextAnchor.MiddleCenter);
                    UiBuild.Place(_consumable, left: 0f, top: 0f, right: 0f, bottom: 0f);
                }
            }

            var turnLeft = UiBuild.Text(region, "TurnL", "<", 24, UiPalette.Muted,
                                        TextAnchor.MiddleCenter);
            UiBuild.Place(turnLeft, left: 28f, top: 88f, width: 40f, height: 120f);

            var turnRight = UiBuild.Text(region, "TurnR", ">", 24, UiPalette.Muted,
                                         TextAnchor.MiddleCenter);
            UiBuild.Place(turnRight, right: 28f, top: 88f, width: 40f, height: 120f);
        }

        /// <summary>The stats window: the Resolve seam as a BAR across the top — Flame Core
        /// gold on the shadow ground, the HUD seam's own colors — with the three numerals
        /// abreast under it.</summary>
        private void BuildStats(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Stats",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 876f, top: 76f, width: 824f, height: 220f);

            var caption = UiBuild.Text(region, "Caption", "STATS", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 16f, top: 10f, width: 200f, height: 26f);

            var resolveName = UiBuild.Text(region, "ResolveName", "Resolve", 20, UiPalette.Muted);
            UiBuild.Place(resolveName, left: 20f, top: 44f, width: 200f, height: 24f);

            _resolve = UiBuild.Text(region, "ResolveValue", "", 16, UiPalette.Muted,
                                    TextAnchor.MiddleRight);
            UiBuild.Place(_resolve, right: 20f, top: 44f, width: 240f, height: 24f);

            var ground = UiBuild.Bordered(region, "ResolveGround",
                                          UiPalette.Umber, UiPalette.ShadowBase, 1f);
            UiBuild.Place(ground, left: 20f, top: 72f, width: 780f, height: 22f);

            _resolveFill = UiBuild.Solid(ground, "ResolveFill", UiPalette.PaleGold);
            UiBuild.Place(_resolveFill, left: 1f, top: 1f, width: 0f, height: 18f);

            StatBlock(region, "Might", 0, UiPalette.Steel, out _might);
            StatBlock(region, "Flame", 1, UiPalette.HearthGold, out _flame);
            StatBlock(region, "Heart", 2, UiPalette.FestivalRed, out _heart);
        }

        /// <summary>The three numerals under the seam: small muted name, big value.</summary>
        private static void StatBlock(VisualElement region, string statName, int index,
                                      Color valueColor, out Label value)
        {
            float left = 20f + index * 260f;

            var name = UiBuild.Text(region, statName + "Name", statName, 20, UiPalette.Muted);
            UiBuild.Place(name, left: left, top: 110f, width: 240f, height: 24f);

            value = UiBuild.Text(region, statName + "Value", "", 32, valueColor);
            UiBuild.Place(value, left: left, top: 136f, width: 240f, height: 56f);
        }

        /// <summary>The items window, its bottom flush with the carousel's: square cells in
        /// a vertical scroll, six rows in view. Scrolling is the scroll view's; driving it
        /// from the pad arrives with item selection.</summary>
        private void BuildInventory(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Inventory",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 876f, top: 320f, width: 824f, height: 592f);

            var caption = UiBuild.Text(region, "Caption", "ITEMS", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 16f, top: 10f, width: 300f, height: 26f);

            _inventoryScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "Scroll",
                pickingMode = PickingMode.Ignore,
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            _inventoryScroll.style.position = Position.Absolute;
            UiBuild.Place(_inventoryScroll, left: 0f, top: 44f, right: 0f, bottom: 12f);
            region.Add(_inventoryScroll);

            var grid = new VisualElement { name = "Grid", pickingMode = PickingMode.Ignore };
            grid.style.height = 4f + InventoryRows * 88f - 8f + 4f;
            _inventoryScroll.Add(grid);

            for (int i = 0; i < InventoryColumns * InventoryRows; i++)
            {
                int column = i % InventoryColumns;
                int row = i / InventoryColumns;

                var cell = UiBuild.Bordered(grid, "Cell" + i,
                                            UiPalette.Umber, UiPalette.ParchmentEmpty, 1.5f);
                UiBuild.Place(cell, left: 11f + column * 88f, top: 4f + row * 88f,
                              width: 80f, height: 80f);

                // The flask lives in the bag too (Matt): the first cell mirrors the
                // carousel — a bottle whose liquid level notes the strength, count in
                // the corner.
                if (i == 0) BuildFlaskCell(cell);
            }
        }

        /// <summary>A gray-box bottle: neck, body, and a liquid fill whose level is the
        /// strength — the fraction of a heart one drink restores.</summary>
        private void BuildFlaskCell(VisualElement cell)
        {
            var neck = UiBuild.Bordered(cell, "FlaskNeck",
                                        UiPalette.Umber, UiPalette.ParchmentEmpty, 2f);
            UiBuild.Place(neck, left: 34f, top: 6f, width: 12f, height: 10f);

            var body = UiBuild.Bordered(cell, "FlaskBody",
                                        UiPalette.Umber, UiPalette.ParchmentEmpty, 2f);
            UiBuild.Place(body, left: 24f, top: 16f, width: 32f, height: 46f);

            _flaskFill = UiBuild.Solid(body, "Liquid", UiPalette.FestivalRed);
            UiBuild.Place(_flaskFill, left: 2f, right: 2f, bottom: 2f, height: 0f);

            _flaskCount = UiBuild.Text(cell, "Count", "", 14, UiPalette.Ink,
                                       TextAnchor.MiddleRight);
            UiBuild.Place(_flaskCount, right: 4f, bottom: 2f, width: 40f, height: 16f);
        }

        public override void Opened(CharacterSim sim)
        {
            bool film = sim != null;

            if (film)
            {
                _rig = PlayerPortrait.Acquire();
                _portrait.image = _rig.Open();
            }

            _portrait.style.display = film ? DisplayStyle.Flex : DisplayStyle.None;
            _figure.style.display = film ? DisplayStyle.None : DisplayStyle.Flex;

            _inventoryScroll.scrollOffset = Vector2.zero;

            Draw(sim);
        }

        public override void Closed()
        {
            if (_rig != null) _rig.Close();
        }

        public override void Tick(in MenuRoot.Frame frame) => Draw(frame.Sim);

        private void Draw(CharacterSim sim)
        {
            if (sim == null) return;

            for (int i = 0; i < Boxes.Length; i++)
            {
                string worn = sim.Worn[Boxes[i].Slot];
                _worn[i].text = worn ?? "—";
                _worn[i].style.color = worn != null ? UiPalette.Ink : UiPalette.MutedPip;
            }

            var stats = sim.Stats;

            _might.text = stats.LevelOf(StatKind.Might).ToString();
            _flame.text = UiBuild.Roman(stats.LevelOf(StatKind.Flame));
            _heart.text = stats.LevelOf(StatKind.Heart).ToString();

            // The seam: progress toward the next rite. A banked level shows full — the bar
            // has nothing left to ask for until the rite spends it.
            int cost = stats.Tuning.ResolveForNextLevel(stats.TotalLevels);
            bool owed = stats.UnspentLevels > 0;

            _resolve.text = owed ? "a rite is owed" : $"{stats.Resolve} of {cost}";

            float fraction = owed
                ? 1f
                : cost > 0 ? Mathf.Clamp01((float)stats.Resolve / cost) : 0f;
            _resolveFill.style.width = fraction * 778f;

            int filled = sim.Flasks.Filled;

            _consumable.text = $"Flask\n×{filled}";
            _consumable.style.color = filled > 0 ? UiPalette.Ink : UiPalette.Muted;

            // Strength as liquid level: the fraction of a heart one drink restores — full
            // bottle today, and honest the day a weaker or stronger flask exists.
            float strength = Mathf.Clamp01((float)Flasks.HealQuarters / Vitals.QuartersPerHeart);
            _flaskFill.style.height = strength * 54f;
            _flaskFill.style.backgroundColor =
                filled > 0 ? UiPalette.FestivalRed : UiPalette.MutedPip;

            _flaskCount.text = $"×{filled}";
            _flaskCount.style.color = filled > 0 ? UiPalette.Ink : UiPalette.Muted;
        }
    }
}
