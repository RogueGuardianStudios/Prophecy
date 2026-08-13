using RGS.Core;

namespace Rokkan.Prophecy.Sim.Items
{
    /// <summary>Where an item can go. One TYPE per item — a Ring fits either hand's slot,
    /// which the equipping resolves; Consumable rides the carousel, not the body.</summary>
    public enum ItemSlotType
    {
        None,
        Sword,
        Shield,
        Shirt,
        Pants,
        Boots,
        Gloves,
        Necklace,
        Ring,
        Consumable,
    }

    /// <summary>
    /// The sim's whole knowledge of an item: plain data, no asset in sight. The grammar is
    /// HopeFell's (<c>com.rokkan.gameplay</c>'s ItemDetails family — GUID identity, typed
    /// payloads, stacking), ported 2026-08-12 with one deliberate inversion: there the state
    /// lived in MonoBehaviours; here the authoring asset (<c>ItemDetails</c> in Core) BAKES
    /// this object and the sim owns everything that changes at runtime, because what is worn
    /// and carried must be able to change what the tick does, headlessly. See
    /// MIGRATION-HopeFell.md.
    /// </summary>
    public sealed class ItemData
    {
        public SerializableGuid Id;
        public string Name;
        public string Description;
        public ItemSlotType Slot = ItemSlotType.None;
        public int MaxStack = 1;

        /// <summary>Usable payload: quarters healed on use. 0 = not usable.</summary>
        public int HealQuarters;

        public bool IsEquippable => Slot != ItemSlotType.None && Slot != ItemSlotType.Consumable;
    }

    /// <summary>A pile of one item in a bag slot.</summary>
    public sealed class ItemStack
    {
        public ItemStack(ItemData item, int quantity)
        {
            Item = item;
            Quantity = quantity;
        }

        public ItemData Item { get; }
        public int Quantity { get; set; }
    }
}
