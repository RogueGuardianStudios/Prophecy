using NUnit.Framework;
using RGS.Core;
using Rokkan.Prophecy.Sim.Combat;
using Rokkan.Prophecy.Sim.Items;

namespace Rokkan.Prophecy.Tests
{
    /// <summary>
    /// The item grammar's sim half: the bag's all-or-nothing stacking and the equipment's
    /// slot resolution. Grammar ported from HopeFell 2026-08-12; these rules are the
    /// deliberate divergences — state headless, adds atomic — so they get pinned.
    /// </summary>
    public sealed class ItemSystemTests
    {
        private static ItemData Pebble(int maxStack = 5) => new ItemData
        {
            Id = SerializableGuid.NewGuid(),
            Name = "pebble",
            MaxStack = maxStack,
        };

        private static ItemData Ring(string name = "a dull ring") => new ItemData
        {
            Id = SerializableGuid.NewGuid(),
            Name = name,
            Slot = ItemSlotType.Ring,
        };

        // ---------------------------------------------------------------- the bag

        [Test]
        public void AddTopsUpExistingPilesBeforeOpeningNewOnes()
        {
            var bag = new Inventory(4);
            var pebble = Pebble(maxStack: 5);

            Assert.IsTrue(bag.TryAdd(pebble, 3));
            Assert.IsTrue(bag.TryAdd(pebble, 4));

            Assert.AreEqual(5, bag[0].Quantity, "the first pile fills to its max");
            Assert.AreEqual(2, bag[1].Quantity, "the overflow opens exactly one new pile");
            Assert.IsNull(bag[2]);
            Assert.AreEqual(7, bag.CountOf(pebble.Id));
        }

        [Test]
        public void AddIsAllOrNothing()
        {
            var bag = new Inventory(1);
            var pebble = Pebble(maxStack: 5);

            Assert.IsTrue(bag.TryAdd(pebble, 4));
            Assert.IsFalse(bag.TryAdd(pebble, 2), "two do not fit in the one remaining space");
            Assert.AreEqual(4, bag.CountOf(pebble.Id), "and the refused add took NOTHING");
        }

        [Test]
        public void DifferentItemsNeverShareAPile()
        {
            var bag = new Inventory(2);
            var pebbleA = Pebble();
            var pebbleB = Pebble();   // same name, different identity

            Assert.IsTrue(bag.TryAdd(pebbleA));
            Assert.IsTrue(bag.TryAdd(pebbleB));

            Assert.AreEqual(1, bag.CountOf(pebbleA.Id));
            Assert.AreEqual(1, bag.CountOf(pebbleB.Id));
            Assert.IsNotNull(bag[1], "the second item opened its own pile");
        }

        [Test]
        public void RemoveIsAllOrNothingAndFreesEmptiedSlots()
        {
            var bag = new Inventory(2);
            var pebble = Pebble(maxStack: 5);
            bag.TryAdd(pebble, 7);

            Assert.IsFalse(bag.TryRemove(pebble.Id, 8), "more than is held is refused whole");
            Assert.AreEqual(7, bag.CountOf(pebble.Id));

            Assert.IsTrue(bag.TryRemove(pebble.Id, 6));
            Assert.AreEqual(1, bag.CountOf(pebble.Id));
            Assert.IsNull(bag[0], "the drained pile's slot is free again");
        }

        // ---------------------------------------------------------------- the worn slots

        [Test]
        public void RingsTakeTheFirstFreeHand()
        {
            var worn = new Equipment();

            Assert.IsTrue(worn.TryEquip(Ring("left"), out var first));
            Assert.AreEqual(EquipSlot.RingLeft, first);

            Assert.IsTrue(worn.TryEquip(Ring("right"), out var second));
            Assert.AreEqual(EquipSlot.RingRight, second);

            Assert.IsFalse(worn.TryEquip(Ring("third"), out _), "no third hand exists");
        }

        [Test]
        public void EquipRefusesAnOccupiedSlotAndUnequipFreesIt()
        {
            var worn = new Equipment();
            var sword = new ItemData { Id = SerializableGuid.NewGuid(), Name = "a plain sword", Slot = ItemSlotType.Sword };
            var better = new ItemData { Id = SerializableGuid.NewGuid(), Name = "a better sword", Slot = ItemSlotType.Sword };

            Assert.IsTrue(worn.TryEquip(sword, out _));
            Assert.IsFalse(worn.TryEquip(better, out _), "swapping is a deliberate two-step");

            Assert.AreSame(sword, worn.Unequip(EquipSlot.Sword), "unequip hands back what was worn");
            Assert.IsTrue(worn.TryEquip(better, out _));
            Assert.AreSame(better, worn[EquipSlot.Sword]);
        }

        [Test]
        public void OnlyEquippablesEquip()
        {
            var worn = new Equipment();

            Assert.IsFalse(worn.TryEquip(Pebble(), out _), "a slotless item has nowhere to go");
            Assert.IsFalse(worn.TryEquip(new ItemData
            {
                Id = SerializableGuid.NewGuid(),
                Name = "a flask",
                Slot = ItemSlotType.Consumable,
            }, out _), "consumables ride the carousel, not the body");
        }
    }
}
