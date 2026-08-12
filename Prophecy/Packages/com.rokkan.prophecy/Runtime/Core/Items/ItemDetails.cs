using RGS.Core;
using Rokkan.Prophecy.Sim.Items;
using UnityEngine;

namespace Rokkan.Prophecy.Core
{
    /// <summary>
    /// An item's authored identity — the asset half of the item grammar, ported from
    /// HopeFell's <c>com.rokkan.gameplay</c> (2026-08-12, see MIGRATION-HopeFell.md). The
    /// asset authors identity and payload and BAKES a plain <see cref="ItemData"/> for the
    /// sim; nothing at tick time ever touches the ScriptableObject — the same split every
    /// tuning asset already obeys. The icon stays here, presentation's to fetch by Id when
    /// the real UI wants it.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item", menuName = "Rokkan/Items/Item")]
    public class ItemDetails : ScriptableObject
    {
        [SerializeField] private SerializableGuid _id = SerializableGuid.NewGuid();
        [SerializeField] private string _displayName;
        [SerializeField, TextArea] private string _description;

        [SerializeField, Tooltip("Reserved for the real UI; the gray box draws names.")]
        private Sprite _icon;

        [SerializeField, Min(1)] private int _maxStack = 1;

        public SerializableGuid Id => _id;
        public Sprite Icon => _icon;

        public virtual ItemData ToData() => Fill(new ItemData());

        protected ItemData Fill(ItemData data)
        {
            data.Id = _id;
            data.Name = _displayName;
            data.Description = _description;
            data.MaxStack = _maxStack;
            return data;
        }
    }
}
