using Rokkan.Prophecy.Sim.Items;

namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>The places a thing can be worn or wielded — the pack sheet's loadout boxes,
    /// in the order they read down the page.</summary>
    public enum EquipSlot
    {
        Sword,
        Shield,
        Pants,
        Boots,
        Shirt,
        Gloves,
        Necklace,
        RingLeft,
        RingRight,
    }

    /// <summary>
    /// What the Bearer wears and wields — nine slots holding plain <see cref="ItemData"/>.
    /// Headless on purpose: worn things will change what the tick does (a worn ring's stat
    /// modifier, a shield's guard numbers), so the state lives with the sim and the pack
    /// sheet only reads it. Seeding belongs to the character factories, not this container.
    /// </summary>
    public sealed class Equipment
    {
        public const int SlotCount = 9;

        private readonly ItemData[] _worn = new ItemData[SlotCount];

        /// <summary>The worn item, or null for an empty slot. Settable directly — factories
        /// and tests place gear without the courtesy checks.</summary>
        public ItemData this[EquipSlot slot]
        {
            get => _worn[(int)slot];
            set => _worn[(int)slot] = value;
        }

        /// <summary>Equip into the item's own slot type — a Ring takes the first free hand.
        /// False when the item is not equippable or every candidate slot is occupied;
        /// swapping is the caller's two-step (unequip, then equip).</summary>
        public bool TryEquip(ItemData item, out EquipSlot slot)
        {
            slot = default;
            if (item == null || !item.IsEquippable) return false;

            if (item.Slot == ItemSlotType.Ring)
            {
                if (this[EquipSlot.RingLeft] == null) slot = EquipSlot.RingLeft;
                else if (this[EquipSlot.RingRight] == null) slot = EquipSlot.RingRight;
                else return false;

                this[slot] = item;
                return true;
            }

            var target = SlotFor(item.Slot);
            if (this[target] != null) return false;

            this[target] = item;
            slot = target;
            return true;
        }

        /// <summary>Empty a slot and hand back what was in it (null if nothing was).</summary>
        public ItemData Unequip(EquipSlot slot)
        {
            var item = this[slot];
            this[slot] = null;
            return item;
        }

        private static EquipSlot SlotFor(ItemSlotType type)
        {
            switch (type)
            {
                case ItemSlotType.Sword: return EquipSlot.Sword;
                case ItemSlotType.Shield: return EquipSlot.Shield;
                case ItemSlotType.Pants: return EquipSlot.Pants;
                case ItemSlotType.Boots: return EquipSlot.Boots;
                case ItemSlotType.Shirt: return EquipSlot.Shirt;
                case ItemSlotType.Gloves: return EquipSlot.Gloves;
                default: return EquipSlot.Necklace;
            }
        }
    }
}
