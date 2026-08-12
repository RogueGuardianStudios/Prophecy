using System;
using RGS.Core;

namespace Rokkan.Prophecy.Sim.Items
{
    /// <summary>
    /// The bag: fixed slots holding stacks, plain C#, headless. Adds are ALL-OR-NOTHING —
    /// a pickup either fits entirely (topping up existing piles first, then opening new
    /// ones) or is refused whole, so "did it fit" is one honest bool and no pile is left
    /// half-taken on the ground.
    /// </summary>
    public sealed class Inventory
    {
        /// <summary>Matches the pack grid's cell count. The grid's FIRST cell mirrors the
        /// tuned <c>Flasks</c> system rather than a bag slot, so the last bag slot only
        /// shows once the UI grows a row — a knowing trade, not a bug.</summary>
        public const int DefaultCapacity = 64;

        private readonly ItemStack[] _slots;

        public Inventory(int capacity = DefaultCapacity)
        {
            _slots = new ItemStack[Math.Max(1, capacity)];
        }

        public int Capacity => _slots.Length;

        /// <summary>The stack in a slot, or null for an empty one.</summary>
        public ItemStack this[int index] => _slots[index];

        public int CountOf(SerializableGuid id)
        {
            int count = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null && _slots[i].Item.Id == id) count += _slots[i].Quantity;
            return count;
        }

        /// <summary>Everything fits or nothing is taken.</summary>
        public bool TryAdd(ItemData item, int quantity = 1)
        {
            if (item == null || quantity < 1) return false;

            int room = 0;
            for (int i = 0; i < _slots.Length && room < quantity; i++)
            {
                if (_slots[i] == null) room += item.MaxStack;
                else if (_slots[i].Item.Id == item.Id) room += item.MaxStack - _slots[i].Quantity;
            }

            if (room < quantity) return false;

            for (int i = 0; i < _slots.Length && quantity > 0; i++)
            {
                if (_slots[i] == null || _slots[i].Item.Id != item.Id) continue;

                int take = Math.Min(item.MaxStack - _slots[i].Quantity, quantity);
                _slots[i].Quantity += take;
                quantity -= take;
            }

            for (int i = 0; i < _slots.Length && quantity > 0; i++)
            {
                if (_slots[i] != null) continue;

                int take = Math.Min(item.MaxStack, quantity);
                _slots[i] = new ItemStack(item, take);
                quantity -= take;
            }

            return true;
        }

        /// <summary>Everything asked for is removed or nothing is — the mirror of TryAdd.</summary>
        public bool TryRemove(SerializableGuid id, int quantity = 1)
        {
            if (quantity < 1 || CountOf(id) < quantity) return false;

            for (int i = 0; i < _slots.Length && quantity > 0; i++)
            {
                if (_slots[i] == null || _slots[i].Item.Id != id) continue;

                int take = Math.Min(_slots[i].Quantity, quantity);
                _slots[i].Quantity -= take;
                quantity -= take;

                if (_slots[i].Quantity == 0) _slots[i] = null;
            }

            return true;
        }
    }
}
