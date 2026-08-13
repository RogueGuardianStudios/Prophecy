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
        /// <summary>Derived from the enum, the way <c>StatBlock.Count</c> is — a hand-kept
        /// number here compiles cleanly while a new slot silently overflows past it.</summary>
        public static readonly int SlotCount =
            System.Enum.GetValues(typeof(EquipSlot)).Length;

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

            if (!TrySlotFor(item.Slot, out var target)) return false;
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

        /// <summary>
        /// The body slot a slot TYPE maps to. False for anything unmapped — a new slot type
        /// must refuse to equip until someone maps it, rather than quietly landing in a slot
        /// it does not belong to and surfacing as wrong gear in a playtest.
        /// </summary>
        public static bool TrySlotFor(ItemSlotType type, out EquipSlot slot)
        {
            switch (type)
            {
                case ItemSlotType.Sword: slot = EquipSlot.Sword; return true;
                case ItemSlotType.Shield: slot = EquipSlot.Shield; return true;
                case ItemSlotType.Pants: slot = EquipSlot.Pants; return true;
                case ItemSlotType.Boots: slot = EquipSlot.Boots; return true;
                case ItemSlotType.Shirt: slot = EquipSlot.Shirt; return true;
                case ItemSlotType.Gloves: slot = EquipSlot.Gloves; return true;
                case ItemSlotType.Necklace: slot = EquipSlot.Necklace; return true;
                default: slot = default; return false;
            }
        }
    }
}
