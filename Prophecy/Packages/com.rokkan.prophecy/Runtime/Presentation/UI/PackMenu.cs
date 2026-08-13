using Rokkan.Prophecy.Sim;
using Rokkan.Prophecy.Sim.Combat;
using Rokkan.Prophecy.Sim.Items;
using Rokkan.Prophecy.Sim.Stats;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rokkan.Prophecy.Presentation.UI
{
    /// <summary>
    /// The pack — the full sheet, still a read-only record of what the player has taken; the
    /// page does not comment on that (spec §8.2). Two columns. LEFT, full height: the
    /// LOADOUT — the filmed player beside his worn slots as item boxes arranged in his shape
    /// (boots at the bottom, pants above, shirt above that, hands holding sword and shield;
    /// empty slots show the dash they are). RIGHT, five item squares wide: STATS (the
    /// Resolve seam as a bar in its colors, the three ranks under it — Might steel, Flame
    /// gold, Heart red; every rank is a serifed "Rank N" so numeral I cannot read as l),
    /// then CONSUMABLES — LOCKED to five slots: the flask permanent in the first (Matt),
    /// four open beside it, D-pad Left/Right to cycle when anything fills them — then
    /// ITEMS: the bag pure and whole as a scrolling grid of square cells, six rows in view.
    ///
    /// <para>The sheet's footprint became THE menu footprint (UiBuild.MenuWidth/Height):
    /// its column widths are DERIVED (five squares plus scroller), six bag rows must stay
    /// in view, and every other tab was resized to match (Matt). The title gave way to the
    /// tab rail — this is the carousel's first page.</para>
    ///
    /// <para>The Resolve rite (spending a banked level) belongs by the stats and arrives
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

        /// <summary>Five across (Matt) — the right column's width is derived from this.</summary>
        private const int InventoryColumns = 5;

        /// <summary>13 rows × 5 = 65 cells for the bag's 64 slots (the last cell stays
        /// empty). Six rows fit the viewport; the rest is what the scroll is for.</summary>
        private const int InventoryRows = 13;

        /// <summary>One grid step: the standard square plus its gap.</summary>
        private const float Stride = 84f;

        private readonly Label[] _worn = new Label[Boxes.Length];
        private readonly Label[] _bagName = new Label[InventoryColumns * InventoryRows];
        private readonly Label[] _bagCount = new Label[InventoryColumns * InventoryRows];
        private Image _portrait;
        private VisualElement _figure;
        private PlayerPortrait _rig;
        private Label _might;
        private Label _flame;
        private Label _heart;
        private Label _resolve;
        private VisualElement _resolveFill;
        private ScrollView _inventoryScroll;
        private VisualElement _flaskFill;
        private Label _flaskCount;

        public PackMenu(VisualElement layer)
        {
            var panel = UiBuild.Bordered(layer, "Pack",
                                         UiPalette.Umber, UiPalette.Parchment, 3f);
            UiBuild.Centre(panel, UiBuild.MenuWidth, UiBuild.MenuHeight);
            Root = panel;

            UiBuild.Tabs(panel, 0);

            BuildLoadout(panel);
            BuildStats(panel);
            BuildCarousel(panel);
            BuildInventory(panel);

            var hints = UiBuild.Text(panel, "Hints", "LB / RB  menu        B  close", 20,
                                     UiPalette.Muted, TextAnchor.MiddleCenter);
            UiBuild.Place(hints, left: 0f, right: 0f, bottom: 8f, height: 30f);

            IsOpen = false;
        }

        /// <summary>Full height on the left — top lined up with STATS, bottom with ITEMS.</summary>
        private void BuildLoadout(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Loadout",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 20f, top: 60f, width: 640f, height: 868f);

            var caption = UiBuild.Text(region, "Caption", "LOADOUT", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 14f, top: 8f, width: 300f, height: 24f);

            // Split down the middle: the Bearer stands on the left, his slots on the right.
            var divide = UiBuild.Solid(region, "Divide", UiPalette.Umber);
            UiBuild.Place(divide, left: 348f, top: 36f, width: 2f, height: 824f);

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
            UiBuild.Place(_portrait, left: 0f, top: 36f, width: 346f, bottom: 0f);
            region.Add(_portrait);

            // Spread over the half's height (Matt: "space them out a bit") — a body is
            // taller than it is wide, and so is this arrangement of it.
            for (int i = 0; i < Boxes.Length; i++)
            {
                float x = 358f + Boxes[i].Column * 100f;
                float y = 218f + Boxes[i].Row * 130f;

                var box = UiBuild.Bordered(region, "Box" + i,
                                           UiPalette.Umber, UiPalette.ParchmentEmpty, 2f);
                UiBuild.Place(box, left: x, top: y,
                              width: UiBuild.ItemSquare, height: UiBuild.ItemSquare);

                var slot = UiBuild.Text(box, "Slot", Boxes[i].Label, 13,
                                        UiPalette.Muted, TextAnchor.UpperCenter);
                UiBuild.Place(slot, left: 0f, top: 3f, right: 0f, height: 16f);

                _worn[i] = UiBuild.Text(box, "Worn", "", 13,
                                        UiPalette.Ink, TextAnchor.MiddleCenter);
                UiBuild.Place(_worn[i], left: 3f, top: 19f, right: 3f, bottom: 3f);
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

            FigurePart(_figure, "Head", 133f, 130f, 80f, 80f);
            FigurePart(_figure, "Torso", 93f, 218f, 160f, 250f);
            FigurePart(_figure, "ArmL", 41f, 222f, 44f, 220f);
            FigurePart(_figure, "ArmR", 261f, 222f, 44f, 220f);
            FigurePart(_figure, "LegL", 103f, 476f, 64f, 250f);
            FigurePart(_figure, "LegR", 179f, 476f, 64f, 250f);
            FigurePart(_figure, "FootL", 85f, 730f, 88f, 30f);
            FigurePart(_figure, "FootR", 179f, 730f, 88f, 30f);
        }

        private static void FigurePart(VisualElement region, string name,
                                       float left, float top, float width, float height)
        {
            var part = UiBuild.Bordered(region, name, UiPalette.Umber, UiPalette.Muted, 2f);
            UiBuild.Place(part, left: left, top: top, width: width, height: height);
        }

        /// <summary>The stats window at the column's top: the Resolve seam as a BAR in the
        /// seam's own colors, the three ranks in single rows under it.</summary>
        private void BuildStats(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Stats",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 676f, top: 60f, width: 456f, height: 180f);

            var caption = UiBuild.Text(region, "Caption", "STATS", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 14f, top: 8f, width: 200f, height: 22f);

            var resolveName = UiBuild.Text(region, "ResolveName", "Resolve", 18, UiPalette.Muted);
            UiBuild.Place(resolveName, left: 16f, top: 32f, width: 150f, height: 22f);

            _resolve = UiBuild.Text(region, "ResolveValue", "", 14, UiPalette.Muted,
                                    TextAnchor.MiddleRight);
            UiBuild.Place(_resolve, right: 16f, top: 32f, width: 200f, height: 22f);

            var ground = UiBuild.Bordered(region, "ResolveGround",
                                          UiPalette.Umber, UiPalette.ShadowBase, 1f);
            UiBuild.Place(ground, left: 16f, top: 56f, width: 424f, height: 18f);

            _resolveFill = UiBuild.Solid(ground, "ResolveFill", UiPalette.PaleGold);
            UiBuild.Place(_resolveFill, left: 1f, top: 1f, width: 0f, height: 14f);

            StatRow(region, "Might", 82f, UiPalette.Steel, out _might);
            StatRow(region, "Flame", 114f, UiPalette.HearthGold, out _flame);
            StatRow(region, "Heart", 146f, UiPalette.FestivalRed, out _heart);
        }

        /// <summary>One line per stat in the narrow column: muted name left, serifed
        /// "Rank N" right — the serif keeps Rank I's numeral from reading as an l.</summary>
        private static void StatRow(VisualElement region, string statName, float top,
                                    Color valueColor, out Label value)
        {
            var name = UiBuild.Text(region, statName + "Name", statName, 18,
                                    UiPalette.Muted, TextAnchor.MiddleLeft);
            UiBuild.Place(name, left: 16f, top: top, width: 140f, height: 28f);

            value = UiBuild.Text(region, statName + "Value", "", 24, valueColor,
                                 TextAnchor.MiddleRight);
            UiBuild.Place(value, right: 16f, top: top, width: 260f, height: 28f);
            UiBuild.Serif(value);
        }

        /// <summary>The consumables rail between stats and the bag — LOCKED to five slots:
        /// the flask PERMANENT in the first (its bottle and count live here now, nowhere
        /// else), four open slots beside it. D-pad Left/Right cycles the selection when
        /// anything fills them.</summary>
        private void BuildCarousel(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Consumables",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 676f, top: 252f, width: 456f, height: 124f);

            var caption = UiBuild.Text(region, "Caption", "CONSUMABLES", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 14f, top: 8f, width: 250f, height: 22f);

            // The turn hint lives in the header — the five slots span the column.
            var turnLeft = UiBuild.Text(region, "TurnL", "<", 18, UiPalette.Muted,
                                        TextAnchor.MiddleCenter);
            UiBuild.Place(turnLeft, right: 44f, top: 8f, width: 20f, height: 22f);

            var turnRight = UiBuild.Text(region, "TurnR", ">", 18, UiPalette.Muted,
                                         TextAnchor.MiddleCenter);
            UiBuild.Place(turnRight, right: 16f, top: 8f, width: 20f, height: 22f);

            for (int i = 0; i < 5; i++)
            {
                bool flaskSlot = i == 0;
                float x = 20f + i * Stride;

                var card = UiBuild.Bordered(region, "Card" + i,
                                            flaskSlot ? UiPalette.Gilt : UiPalette.Umber,
                                            flaskSlot ? UiPalette.Bright : UiPalette.ParchmentEmpty,
                                            flaskSlot ? 2f : 1.5f);
                UiBuild.Place(card, left: x, top: 38f,
                              width: UiBuild.ItemSquare, height: UiBuild.ItemSquare);

                if (flaskSlot) BuildFlaskCard(card);
            }
        }

        /// <summary>The bag at the column's bottom, its floor flush with the loadout's:
        /// square cells in a vertical scroll, six rows in view. Scrolling is the scroll
        /// view's; driving it from the pad arrives with item selection.</summary>
        private void BuildInventory(VisualElement panel)
        {
            var region = UiBuild.Bordered(panel, "Inventory",
                                          UiPalette.Umber, UiPalette.Parchment, 2f);
            UiBuild.Place(region, left: 676f, top: 388f, width: 456f, height: 540f);

            var caption = UiBuild.Text(region, "Caption", "ITEMS", 18, UiPalette.Muted);
            UiBuild.Place(caption, left: 14f, top: 8f, width: 200f, height: 22f);

            _inventoryScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "Scroll",
                pickingMode = PickingMode.Ignore,
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            _inventoryScroll.style.position = Position.Absolute;
            UiBuild.Place(_inventoryScroll, left: 0f, top: 32f, right: 0f, bottom: 8f);
            region.Add(_inventoryScroll);

            var grid = new VisualElement { name = "Grid", pickingMode = PickingMode.Ignore };
            grid.style.height = 4f + InventoryRows * Stride - 8f + 4f;
            _inventoryScroll.Add(grid);

            for (int i = 0; i < InventoryColumns * InventoryRows; i++)
            {
                int column = i % InventoryColumns;
                int row = i / InventoryColumns;

                var cell = UiBuild.Bordered(grid, "Cell" + i,
                                            UiPalette.Umber, UiPalette.ParchmentEmpty, 1.5f);
                UiBuild.Place(cell, left: 12f + column * Stride, top: 4f + row * Stride,
                              width: UiBuild.ItemSquare, height: UiBuild.ItemSquare);

                _bagName[i] = UiBuild.Text(cell, "Name", "", 13,
                                           UiPalette.Ink, TextAnchor.MiddleCenter);
                UiBuild.Place(_bagName[i], left: 3f, top: 3f, right: 3f, bottom: 18f);

                _bagCount[i] = UiBuild.Text(cell, "Count", "", 13, UiPalette.Ink,
                                            TextAnchor.MiddleRight);
                UiBuild.Place(_bagCount[i], right: 4f, bottom: 2f, width: 40f, height: 14f);
            }
        }

        /// <summary>A gray-box bottle in the flask's permanent carousel slot: neck, body,
        /// and a liquid fill whose level is the strength — the fraction of a heart one
        /// drink restores.</summary>
        private void BuildFlaskCard(VisualElement cell)
        {
            var neck = UiBuild.Bordered(cell, "FlaskNeck",
                                        UiPalette.Umber, UiPalette.ParchmentEmpty, 2f);
            UiBuild.Place(neck, left: 32f, top: 5f, width: 12f, height: 10f);

            var body = UiBuild.Bordered(cell, "FlaskBody",
                                        UiPalette.Umber, UiPalette.ParchmentEmpty, 2f);
            UiBuild.Place(body, left: 23f, top: 15f, width: 30f, height: 44f);

            _flaskFill = UiBuild.Solid(body, "Liquid", UiPalette.FestivalRed);
            UiBuild.Place(_flaskFill, left: 2f, right: 2f, bottom: 2f, height: 0f);

            _flaskCount = UiBuild.Text(cell, "Count", "", 13, UiPalette.Ink,
                                       TextAnchor.MiddleRight);
            UiBuild.Place(_flaskCount, right: 4f, bottom: 2f, width: 40f, height: 14f);
        }

        public override void Opened(CharacterSim sim)
        {
            bool film = false;

            if (sim != null)
            {
                _rig = PlayerPortrait.Acquire();
                var texture = _rig.Open();

                // Filming only counts when a double actually stood on the stage. A body the
                // portrait could not resolve films empty parchment, and hiding the gray-box
                // figure behind THAT is a blank sheet, not a portrait — the figure is the
                // fallback for every failure, not just a missing player.
                film = _rig.HasDouble;
                if (film) _portrait.image = texture;
                else _rig.Close();
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
                var worn = sim.Worn[Boxes[i].Slot];
                _worn[i].text = worn != null ? worn.Name : "—";
                _worn[i].style.color = worn != null ? UiPalette.Ink : UiPalette.MutedPip;
            }

            var stats = sim.Stats;

            _might.text = "Rank " + UiBuild.Roman(stats.LevelOf(StatKind.Might));
            _flame.text = "Rank " + UiBuild.Roman(stats.LevelOf(StatKind.Flame));
            _heart.text = "Rank " + UiBuild.Roman(stats.LevelOf(StatKind.Heart));

            // The seam: progress toward the next rite. The FILL rule lives in one place —
            // HudController.SeamFraction — so this bar and the HUD's seam can never disagree
            // on screen about the same sim; only the label is this page's own.
            int cost = stats.Tuning.ResolveForNextLevel(stats.TotalLevels);
            bool owed = stats.UnspentLevels > 0;

            _resolve.text = owed ? "a rite is owed" : $"{stats.Resolve} of {cost}";
            _resolveFill.style.width = HudController.SeamFraction(stats) * 422f;

            int filled = sim.Flasks.Filled;

            // Strength as liquid level: the fraction of a heart one drink restores — full
            // bottle today, and honest the day a weaker or stronger flask exists.
            float strength = Mathf.Clamp01((float)Flasks.HealQuarters / Vitals.QuartersPerHeart);
            _flaskFill.style.height = strength * 36f;
            _flaskFill.style.backgroundColor =
                filled > 0 ? UiPalette.FestivalRed : UiPalette.MutedPip;

            _flaskCount.text = $"×{filled}";
            _flaskCount.style.color = filled > 0 ? UiPalette.Ink : UiPalette.Muted;

            // The bag, pure and whole: cell i is bag slot i (the flask lives in the
            // carousel now, nowhere else); the 65th cell outlives the 64-slot bag and
            // stays honestly empty.
            for (int cell = 0; cell < _bagName.Length; cell++)
            {
                var stack = cell < sim.Bag.Capacity ? sim.Bag[cell] : null;

                _bagName[cell].text = stack != null ? stack.Item.Name : "";
                _bagCount[cell].text = stack != null && stack.Quantity > 1
                    ? $"×{stack.Quantity}"
                    : "";
            }
        }
    }
}
