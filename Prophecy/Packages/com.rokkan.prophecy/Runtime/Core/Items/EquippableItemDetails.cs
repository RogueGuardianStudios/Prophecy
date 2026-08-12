using Rokkan.Prophecy.Sim.Items;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>Something worn or wielded: names the slot TYPE it fits (a Ring fits either
    /// hand; the sim's Equipment resolves which). Stat payloads (a ring's modifier, a
    /// shield's guard numbers) join <see cref="ItemData"/> when the first item needs one —
    /// the seam is ready, the fields are not invented ahead of a real user.</summary>
    [CreateAssetMenu(fileName = "New Equippable", menuName = "Rokkan/Items/Equippable")]
    public sealed class EquippableItemDetails : ItemDetails
    {
        [SerializeField] private ItemSlotType _slot = ItemSlotType.Sword;

        public override ItemData ToData()
        {
            var data = Fill(new ItemData());
            data.Slot = _slot;
            return data;
        }
    }
}
