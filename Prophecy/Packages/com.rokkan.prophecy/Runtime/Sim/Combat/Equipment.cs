namespace Rokkan.Prophecy.Sim.Combat
{
    /// <summary>The places a thing can be worn or wielded — the pack sheet's loadout rows,
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
    /// What the Bearer wears and wields. Gray-box thin on purpose: a slot holds a display
    /// name or nothing, because no item economy exists yet to hold anything richer — when
    /// items become real (stats, effects, identity), this is the seam they replace, and the
    /// slots and the sheet survive the upgrade.
    /// </summary>
    public sealed class Equipment
    {
        public const int SlotCount = 9;

        private readonly string[] _worn = new string[SlotCount];

        public Equipment()
        {
            // The sim already swings and guards, so the sheet says so from the first boot;
            // everything else arrives with the item economy.
            _worn[(int)EquipSlot.Sword] = "a plain sword";
            _worn[(int)EquipSlot.Shield] = "a plain shield";
        }

        /// <summary>The worn item's display name, or null for an empty slot.</summary>
        public string this[EquipSlot slot]
        {
            get => _worn[(int)slot];
            set => _worn[(int)slot] = string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
