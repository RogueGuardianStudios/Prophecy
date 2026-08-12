using Rokkan.Prophecy.Sim.Items;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>Something consumed for an effect. Heal is the one payload the sim knows so
    /// far; further verbs earn fields when something authored actually uses them.</summary>
    [CreateAssetMenu(fileName = "New Usable", menuName = "Rokkan/Items/Usable")]
    public sealed class UsableItemDetails : ItemDetails
    {
        [SerializeField, Tooltip("Quarters healed on use — 4 is one heart."), Min(0)]
        private int _healQuarters;

        public override ItemData ToData()
        {
            var data = Fill(new ItemData());
            data.Slot = ItemSlotType.Consumable;
            data.HealQuarters = _healQuarters;
            return data;
        }
    }
}
