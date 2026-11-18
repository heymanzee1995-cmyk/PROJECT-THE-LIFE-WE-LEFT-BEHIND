using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Game.Domain.Tests.TestConfigs;

namespace Game.Domain.Tests
{
    public class InventorySellDeliverRewardTests
    {
        private static void AssertHealthy(MergeEngine e, Ledger ledger)
        {
            List<string> v = InvariantChecker.Check(e, ledger, true);
            Assert.That(v, Is.Empty, string.Join("\n", v));
        }

        [Test]
        public void MoveToInventory_AndBack()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Cup, 3, 10));
            ItemId id = e.GetCell(10).Id;

            ItemResult toInv = e.TryMoveToInventory(10, id);
            Assert.AreEqual(ActionError.None, toInv.Error);
            Assert.AreEqual(Location.Slot(0), toInv.Location);
            Assert.IsNull(e.GetCell(10));
            Assert.AreEqual(id, e.GetSlot(0).Id);
            AssertHealthy(e, ledger);

            ItemResult back = e.TryMoveFromInventory(0, id, 20);
            Assert.AreEqual(ActionError.None, back.Error);
            Assert.IsNull(e.GetSlot(0));
            Assert.AreEqual(id, e.GetCell(20).Id);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void InventoryFull_IsRefused()
        {
            var config = Default(inventoryCapacity: 2, maxInventoryCapacity: 3);
            MergeEngine e = NewEngine(out Ledger ledger, 1, new[] { At(Cup, 1, 1), At(Cup, 2, 2), At(Cup, 3, 3) }, config);
            e.TryMoveToInventory(1, e.GetCell(1).Id);
            e.TryMoveToInventory(2, e.GetCell(2).Id);
            string before = Canon(e);
            Assert.AreEqual(ActionError.InventoryFull, e.TryMoveToInventory(3, e.GetCell(3).Id).Error);
            Assert.AreEqual(before, Canon(e));

            Assert.AreEqual(ActionError.None, e.TryExpandInventory(3).Error);
            Assert.AreEqual(3, e.InventoryCapacity);
            Assert.AreEqual(ActionError.None, e.TryMoveToInventory(3, e.GetCell(3).Id).Error);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void ExpandInventory_RejectsShrinkAndOverMax()
        {
            MergeEngine e = NewEngine(out _); // 4 of max 8
            Assert.AreEqual(ActionError.InvalidCapacity, e.TryExpandInventory(4).Error);
            Assert.AreEqual(ActionError.InvalidCapacity, e.TryExpandInventory(2).Error);
            Assert.AreEqual(ActionError.InvalidCapacity, e.TryExpandInventory(9).Error);
            Assert.AreEqual(4, e.InventoryCapacity);
        }

        [Test]
        public void MoveFromInventory_RefusesOccupiedLockedAndStale()
        {
            MergeEngine e = EngineWith(out _, At(Cup, 1, 1), At(Cup, 2, 2));
            ItemId id = e.GetCell(1).Id;
            e.TryMoveToInventory(1, id);
            string before = Canon(e);
            Assert.AreEqual(ActionError.TargetOccupied, e.TryMoveFromInventory(0, id, 2).Error);
            Assert.AreEqual(ActionError.CellLocked, e.TryMoveFromInventory(0, id, LockedCells[0]).Error);
            Assert.AreEqual(ActionError.StaleItem, e.TryMoveFromInventory(0, new ItemId(77), 5).Error);
            Assert.AreEqual(ActionError.SourceEmpty, e.TryMoveFromInventory(1, id, 5).Error);
            Assert.AreEqual(ActionError.InvalidSlot, e.TryMoveFromInventory(99, id, 5).Error);
            Assert.AreEqual(ActionError.InvalidCell, e.TryMoveFromInventory(0, id, 99).Error);
            Assert.AreEqual(before, Canon(e));
        }

        [Test]
        public void InventoryItemDroppedOnBoardItem_IsRejected_WithNoChangeAnywhere()
        {
            // Approved: no direct merging from inventory; the item returns to its slot.
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 2, 1), At(Flower, 2, 2), At(Cup, 3, 3), At(Toolbox, 1, 4));
            ItemId stored = e.GetCell(1).Id;
            e.TryMoveToInventory(1, stored);
            int[] targets = { 2, 3, 4 }; // identical mergeable item, different item, generator
            foreach (int cell in targets)
            {
                string before = Canon(e);
                int ledgerBefore = ledger.Entries.Count;
                int events = 0;
                Action<Item, Location, Location> onMove = (a, b, c) => events++;
                Action<Item, CreationReason> onCreate = (a, b) => events++;
                Action<Item, RemovalReason> onRemove = (a, b) => events++;
                e.ItemMoved += onMove;
                e.ItemCreated += onCreate;
                e.ItemRemoved += onRemove;

                ItemResult r = e.TryMoveFromInventory(0, stored, cell);

                e.ItemMoved -= onMove;
                e.ItemCreated -= onCreate;
                e.ItemRemoved -= onRemove;
                Assert.AreEqual(ActionError.TargetOccupied, r.Error, "target cell " + cell);
                Assert.AreEqual(stored, e.GetSlot(0).Id, "item stays in its inventory slot");
                Assert.AreEqual(before, Canon(e), "board and inventory unchanged");
                Assert.AreEqual(ledgerBefore, ledger.Entries.Count);
                Assert.AreEqual(0, events);
            }

            AssertHealthy(e, ledger);
        }

        [Test]
        public void Sell_RemovesItemAndReturnsConfiguredValue_FromBoardOrInventory()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 4, 1), At(Cup, 5, 2), At(Crate, 1, 3));
            RemovalReason? reason = null;
            e.ItemRemoved += (item, r) => reason = r;

            ItemResult sold = e.TrySell(Location.Cell(1), e.GetCell(1).Id);
            Assert.AreEqual(8, sold.Value);
            Assert.AreEqual(RemovalReason.Sell, reason);
            Assert.IsNull(e.GetCell(1));

            ItemId cup = e.GetCell(2).Id;
            e.TryMoveToInventory(2, cup);
            Assert.AreEqual(8, e.TrySell(Location.Slot(0), cup).Value);

            // Value 0 = trash.
            Assert.AreEqual(0, e.TrySell(Location.Cell(3), e.GetCell(3).Id).Value);
            Assert.AreEqual(0, e.ItemCount);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void Sell_RefusesBadInput()
        {
            MergeEngine e = EngineWith(out _, At(Cup, 1, 1));
            string before = Canon(e);
            Assert.AreEqual(ActionError.SourceEmpty, e.TrySell(Location.Cell(2), ItemId.None).Error);
            Assert.AreEqual(ActionError.StaleItem, e.TrySell(Location.Cell(1), new ItemId(42)).Error);
            Assert.AreEqual(ActionError.InvalidCell, e.TrySell(Location.Cell(500), ItemId.None).Error);
            Assert.AreEqual(ActionError.InvalidSlot, e.TrySell(Location.Slot(500), ItemId.None).Error);
            Assert.AreEqual(ActionError.InvalidCell, e.TrySell(Location.Nowhere, ItemId.None).Error);
            Assert.AreEqual(before, Canon(e));
        }

        [Test]
        public void Deliver_RemovesAllOrNothing()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Cup, 1, 1), At(Flower, 2, 2), At(Cup, 3, 3));
            ItemId a = e.GetCell(1).Id, b = e.GetCell(2).Id, c = e.GetCell(3).Id;
            e.TryMoveToInventory(3, c);
            string before = Canon(e);

            Assert.AreEqual(ActionError.UnknownItem, e.TryDeliver(new[] { a, new ItemId(999) }).Error);
            Assert.AreEqual(ActionError.DuplicateItem, e.TryDeliver(new[] { a, a }).Error);
            Assert.AreEqual(ActionError.ItemNotOnBoard, e.TryDeliver(new[] { a, c }).Error);
            Assert.AreEqual(ActionError.NothingToDeliver, e.TryDeliver(new ItemId[0]).Error);
            Assert.AreEqual(before, Canon(e), "a refused delivery must not remove anything");

            Assert.AreEqual(ActionError.None, e.TryDeliver(new[] { a, b }).Error);
            Assert.AreEqual(1, e.ItemCount);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void Rewards_QueueWhenBoardFull_AndAreNeverLost()
        {
            var layout = new List<InitialPlacement>();
            for (int cell = 0; cell < LockedCells[0]; cell++)
            {
                layout.Add(At(cell % 2 == 0 ? Cup : Flower, 6, cell));
            }

            MergeEngine e = NewEngine(out Ledger ledger, 1, layout);
            Assert.AreEqual(ActionError.InvalidItemType, e.EnqueueReward(T("nope", 1)));
            Assert.AreEqual(ActionError.None, e.EnqueueReward(T(Toolbox, 2)));
            Assert.AreEqual(ActionError.None, e.EnqueueReward(T(Flower, 1)));
            string before = Canon(e);
            Assert.AreEqual(ActionError.BoardFull, e.TryClaimReward().Error);
            Assert.AreEqual(before, Canon(e));
            Assert.AreEqual(2, e.PendingRewardCount);

            e.TrySell(Location.Cell(20), e.GetCell(20).Id);
            ItemResult claimed = e.TryClaimReward();
            Assert.AreEqual(ActionError.None, claimed.Error);
            Assert.AreEqual(Location.Cell(20), claimed.Location);
            Item gen = e.GetCell(20);
            Assert.AreEqual(T(Toolbox, 2), gen.Type);
            Assert.AreEqual(10, gen.GeneratorCharges, "reward generators start full");
            Assert.AreEqual(1, e.PendingRewardCount);
            AssertHealthy(e, ledger);

            MergeEngine empty = NewEngine(out _);
            Assert.AreEqual(ActionError.NoPendingReward, empty.TryClaimReward().Error);
        }

        [Test]
        public void UnlockCell_MakesItUsable()
        {
            MergeEngine e = EngineWith(out _, At(Cup, 1, 1));
            int cell = LockedCells[2];
            Assert.IsTrue(e.IsLocked(cell));
            Assert.AreEqual(ActionError.CellLocked, e.TryDrop(1, e.GetCell(1).Id, cell).Error);
            Assert.AreEqual(ActionError.None, e.TryUnlockCell(cell).Error);
            Assert.AreEqual(ActionError.CellNotLocked, e.TryUnlockCell(cell).Error);
            Assert.AreEqual(ActionError.InvalidCell, e.TryUnlockCell(-1).Error);
            Assert.AreEqual(DropOutcome.Moved, e.TryDrop(1, e.GetCell(1).Id, cell).Outcome);
        }
    }
}
