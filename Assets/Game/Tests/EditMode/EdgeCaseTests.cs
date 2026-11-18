using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Game.Domain.Tests.TestConfigs;

namespace Game.Domain.Tests
{
    /// <summary>
    /// Deterministic edge cases around a completely full board, the reward queue, inventory
    /// exhaustion, recovery, drags and save/load. Every refused command is checked to leave the state
    /// unchanged: identical snapshot, no ledger records and no events.
    /// Every case runs on the default 5x8 board and on the alternative 7x9 board.
    /// </summary>
    [TestFixture("5x8")]
    [TestFixture("7x9")]
    public class EdgeCaseTests
    {
        private readonly BoardShape _shape;

        public EdgeCaseTests(string board)
        {
            _shape = board == "7x9" ? BoardShape.Alt7x9 : BoardShape.Default5x8;
        }

        // These shadow the default-board constants from TestConfigs with this fixture's board.
        private int FullCells => _shape.CellCount;
        private int GeneratorCell => _shape.GeneratorCell;
        private int[] LockedCells => _shape.LockedCells;

        private MergeEngine ShapedEngine(out Ledger ledger, params InitialPlacement[] layout) =>
            NewEngine(out ledger, 12345, layout, Default(shape: _shape));

        /// <summary>
        /// Every cell filled (no locked cells) and no merge available: a generator at the centre and
        /// alternating max-tier cups / flowers everywhere else.
        /// </summary>
        private MergeEngine FullBoard(out Ledger ledger, int inventoryCapacity = 2, int maxInventoryCapacity = 3)
        {
            var layout = new List<InitialPlacement> { At(Toolbox, 1, GeneratorCell) };
            for (int cell = 0; cell < FullCells; cell++)
            {
                if (cell != GeneratorCell)
                {
                    layout.Add(cell % 2 == 0 ? At(Cup, 6, cell) : At(Flower, 8, cell));
                }
            }

            EngineConfig config = Default(inventoryCapacity, maxInventoryCapacity, lockedCells: new int[0], shape: _shape);
            return NewEngine(out ledger, 2026, layout, config);
        }

        /// <summary>Counts every event the engine raises.</summary>
        private sealed class EventCounter
        {
            public int Count;

            public EventCounter(MergeEngine e)
            {
                e.ItemCreated += (a, b) => Count++;
                e.ItemRemoved += (a, b) => Count++;
                e.ItemMoved += (a, b, c) => Count++;
                e.ItemsMerged += (a, b, c) => Count++;
                e.CellUnlocked += a => Count++;
                e.InventoryCapacityChanged += a => Count++;
                e.RewardQueued += a => Count++;
            }
        }

        /// <summary>Runs a command that must be refused with <paramref name="expected"/> and must change nothing.</summary>
        private static void AssertRefused(MergeEngine e, Ledger ledger, ActionError expected, Func<ActionError> command)
        {
            string before = Canon(e);
            int ledgerBefore = ledger.Entries.Count;
            var events = new EventCounter(e);

            ActionError actual = command();

            Assert.AreEqual(expected, actual);
            Assert.AreEqual(before, Canon(e), "refused " + expected + " changed engine state");
            Assert.AreEqual(ledgerBefore, ledger.Entries.Count, "refused " + expected + " wrote ledger records");
            Assert.AreEqual(0, events.Count, "refused " + expected + " raised events");
        }

        private static void AssertHealthy(MergeEngine e, Ledger ledger)
        {
            List<string> v = InvariantChecker.Check(e, ledger, fullReplay: true);
            Assert.That(v, Is.Empty, string.Join("\n", v));
        }

        private static MergeEngine Reload(MergeEngine e, out Ledger ledger)
        {
            ledger = new Ledger();
            return MergeEngine.FromSnapshot(e.Config, e.CreateSnapshot(), ledger);
        }

        // 1 ------------------------------------------------------------------

        [Test]
        public void E01_CompletelyFullBoard_IsConsistentAndHasNoMerge()
        {
            MergeEngine e = FullBoard(out Ledger ledger);

            Assert.AreEqual(FullCells, e.CellCount);
            Assert.AreEqual(FullCells, e.ItemCount);
            Assert.AreEqual(0, e.CountEmptyCells());
            Assert.IsTrue(e.IsBoardFull);
            Assert.IsFalse(e.TryFindMerge(out _), "test board must offer no merge");
            Assert.IsTrue(e.HasBoardFullResolution(), "a full board must still offer a way out");
            for (int cell = 0; cell < FullCells; cell++)
            {
                Assert.IsNotNull(e.GetCell(cell), "cell " + cell + " empty");
                Assert.IsFalse(e.IsLocked(cell));
            }

            AssertHealthy(e, ledger);
        }

        // 2 ------------------------------------------------------------------

        [Test]
        public void E02_GeneratorOnFullBoard_IsRefusedRepeatedly_WithoutSpendingAnything()
        {
            MergeEngine e = FullBoard(out Ledger ledger);
            Item gen = e.GetCell(GeneratorCell);
            ulong rng = e.RngState;

            for (int i = 0; i < 10; i++)
            {
                AssertRefused(e, ledger, ActionError.BoardFull, () => e.TryTapGenerator(GeneratorCell, gen.Id, i * 1000L).Error);
            }

            Assert.AreEqual(ToolboxCharges, gen.GeneratorCharges, "no charge spent");
            Assert.AreEqual(0, gen.GeneratorReadyAtMs, "no cooldown started");
            Assert.AreEqual(rng, e.RngState, "no RNG consumed");
        }

        [Test]
        public void E02b_DepletedGeneratorOnFullBoard_ReportsCooldownFirst()
        {
            // Cooldown is checked before board space, so the player sees the more useful message.
            MergeEngine e = FullBoard(out Ledger ledger);
            Item gen = e.GetCell(GeneratorCell);
            e.TrySell(Location.Cell(0), e.GetCell(0).Id); // cell 0 becomes the only free cell
            for (int i = 0; i < ToolboxCharges; i++)
            {
                e.TryTapGenerator(GeneratorCell, gen.Id, 0);
                ItemId spawned = e.GetCell(0) != null ? e.GetCell(0).Id : ItemId.None;
                if (i < ToolboxCharges - 1)
                {
                    e.TrySell(Location.Cell(0), spawned);
                }
            }

            Assert.IsTrue(e.IsBoardFull);
            AssertRefused(e, ledger, ActionError.GeneratorOnCooldown, () => e.TryTapGenerator(GeneratorCell, gen.Id, 1).Error);
            AssertRefused(e, ledger, ActionError.BoardFull, () => e.TryTapGenerator(GeneratorCell, gen.Id, ToolboxCooldownMs).Error);
        }

        // 3 ------------------------------------------------------------------

        [Test]
        public void E03_RewardQueue_OnFullBoard_HoldsRewardsInOrder_AndClaimsAsSpaceFrees()
        {
            MergeEngine e = FullBoard(out Ledger ledger);
            Assert.AreEqual(ActionError.None, e.EnqueueReward(T(Flower, 3)));
            Assert.AreEqual(ActionError.None, e.EnqueueReward(T(Toolbox, 2)));
            Assert.AreEqual(ActionError.None, e.EnqueueReward(T(Cup, 1)));
            AssertRefused(e, ledger, ActionError.InvalidItemType, () => e.EnqueueReward(T("ghost", 1)));
            AssertRefused(e, ledger, ActionError.InvalidItemType, () => e.EnqueueReward(T(Flower, 9)));

            for (int i = 0; i < 3; i++)
            {
                AssertRefused(e, ledger, ActionError.BoardFull, () => e.TryClaimReward().Error);
            }

            Assert.AreEqual(3, e.PendingRewardCount);

            // Free two cells, claim two rewards in FIFO order into the first empty cells.
            e.TrySell(Location.Cell(10), e.GetCell(10).Id);
            e.TrySell(Location.Cell(4), e.GetCell(4).Id);
            ItemResult first = e.TryClaimReward();
            ItemResult second = e.TryClaimReward();
            Assert.AreEqual(Location.Cell(4), first.Location);
            Assert.AreEqual(Location.Cell(10), second.Location);
            Assert.AreEqual(T(Flower, 3), e.GetCell(4).Type);
            Assert.AreEqual(T(Toolbox, 2), e.GetCell(10).Type);
            Assert.AreEqual(10, e.GetCell(10).GeneratorCharges);

            Assert.AreEqual(1, e.PendingRewardCount);
            Assert.AreEqual(T(Cup, 1), e.GetPendingReward(0));
            AssertRefused(e, ledger, ActionError.BoardFull, () => e.TryClaimReward().Error);
            AssertHealthy(e, ledger);
        }

        // 4 ------------------------------------------------------------------

        [Test]
        public void E04_InventoryExhaustion_RefusesUntilExpanded_ThenRefusesAtMaxCapacity()
        {
            MergeEngine e = FullBoard(out Ledger ledger, inventoryCapacity: 2, maxInventoryCapacity: 3);
            Assert.AreEqual(ActionError.None, e.TryMoveToInventory(0, e.GetCell(0).Id).Error);
            Assert.AreEqual(ActionError.None, e.TryMoveToInventory(1, e.GetCell(1).Id).Error);
            Assert.AreEqual(0, e.CountFreeSlots());

            AssertRefused(e, ledger, ActionError.InventoryFull, () => e.TryMoveToInventory(2, e.GetCell(2).Id).Error);
            AssertRefused(e, ledger, ActionError.InvalidCapacity, () => e.TryExpandInventory(2).Error);
            AssertRefused(e, ledger, ActionError.InvalidCapacity, () => e.TryExpandInventory(4).Error);

            Assert.AreEqual(ActionError.None, e.TryExpandInventory(3).Error);
            Assert.AreEqual(ActionError.None, e.TryMoveToInventory(2, e.GetCell(2).Id).Error);
            AssertRefused(e, ledger, ActionError.InventoryFull, () => e.TryMoveToInventory(3, e.GetCell(3).Id).Error);
            AssertRefused(e, ledger, ActionError.InvalidCapacity, () => e.TryExpandInventory(4).Error);

            // Moving an item back out frees exactly one slot.
            Assert.AreEqual(ActionError.None, e.TryMoveFromInventory(0, e.GetSlot(0).Id, 0).Error);
            Assert.AreEqual(1, e.CountFreeSlots());
            AssertHealthy(e, ledger);
        }

        [Test]
        public void E04b_FullBoardAndFullInventory_StillResolvableBySelling()
        {
            MergeEngine e = FullBoard(out Ledger ledger, inventoryCapacity: 1, maxInventoryCapacity: 1);
            e.TryMoveToInventory(0, e.GetCell(0).Id);
            e.TryMoveFromInventory(0, e.GetSlot(0).Id, 0);
            e.TryMoveToInventory(0, e.GetCell(0).Id);
            e.EnqueueReward(T(Cup, 1));
            // Cell 0 is the board's only hole: a reward fills it, so board and inventory are both full.
            e.TryClaimReward();
            Assert.IsTrue(e.IsBoardFull);
            Assert.AreEqual(0, e.CountFreeSlots());
            Item gen = e.GetCell(GeneratorCell);

            AssertRefused(e, ledger, ActionError.BoardFull, () => e.TryTapGenerator(GeneratorCell, gen.Id, 0).Error);
            AssertRefused(e, ledger, ActionError.InventoryFull, () => e.TryMoveToInventory(5, e.GetCell(5).Id).Error);
            AssertRefused(e, ledger, ActionError.TargetOccupied, () => e.TryMoveFromInventory(0, e.GetSlot(0).Id, 5).Error);
            Assert.IsTrue(e.HasBoardFullResolution());

            Assert.AreEqual(ActionError.None, e.TrySell(Location.Slot(0), e.GetSlot(0).Id).Error);
            Assert.AreEqual(ActionError.None, e.TrySell(Location.Cell(5), e.GetCell(5).Id).Error);
            Assert.AreEqual(ActionError.None, e.TryTapGenerator(GeneratorCell, gen.Id, 0).Error);
            AssertHealthy(e, ledger);
        }

        // 5 ------------------------------------------------------------------

        [Test]
        public void E05_SellingOnFullBoard_FreesExactlyThatCell()
        {
            MergeEngine e = FullBoard(out Ledger ledger);
            Item victim = e.GetCell(9);
            ItemId victimId = victim.Id;
            int expectedValue = Default().GetSellValue(victim.Type);
            var removed = new List<RemovalReason>();
            e.ItemRemoved += (item, reason) => removed.Add(reason);

            ItemResult r = e.TrySell(Location.Cell(9), victimId);

            Assert.AreEqual(ActionError.None, r.Error);
            Assert.AreEqual(expectedValue, r.Value);
            Assert.AreEqual(1, e.CountEmptyCells());
            Assert.IsNull(e.GetCell(9));
            Assert.IsFalse(e.TryGetItem(victimId, out _));
            CollectionAssert.AreEqual(new[] { RemovalReason.Sell }, removed);
            LedgerEntry last = ledger.Entries[ledger.Entries.Count - 1];
            Assert.AreEqual(LedgerDirection.Removed, last.Direction);
            Assert.AreEqual(RemovalReason.Sell, last.Removed);
            Assert.AreEqual(victimId, last.Item);

            // Selling the same item again (double tap / stale UI) is refused.
            AssertRefused(e, ledger, ActionError.SourceEmpty, () => e.TrySell(Location.Cell(9), victimId).Error);
            AssertHealthy(e, ledger);
        }

        // 6 ------------------------------------------------------------------

        [Test]
        public void E06_GeneratorSucceedsOnceSpaceIsFreed_FillingThatCell_ThenRefusesAgain()
        {
            MergeEngine e = FullBoard(out Ledger ledger);
            Item gen = e.GetCell(GeneratorCell);
            AssertRefused(e, ledger, ActionError.BoardFull, () => e.TryTapGenerator(GeneratorCell, gen.Id, 0).Error);

            int corner = FullCells - 1;
            e.TrySell(Location.Cell(corner), e.GetCell(corner).Id); // far corner: the only empty cell
            long nextId = e.NextItemId;
            SpawnResult r = e.TryTapGenerator(GeneratorCell, gen.Id, 0);

            Assert.AreEqual(ActionError.None, r.Error);
            Assert.AreEqual(corner, r.Cell, "spawn must use the only free cell, however far");
            Assert.AreEqual(nextId, r.Item.Value);
            Assert.AreEqual(ToolboxCharges - 1, gen.GeneratorCharges);
            Assert.IsTrue(e.IsBoardFull);
            AssertRefused(e, ledger, ActionError.BoardFull, () => e.TryTapGenerator(GeneratorCell, gen.Id, 0).Error);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void E06b_MergingOnFullBoard_FreesACellForTheGenerator()
        {
            MergeEngine e = FullBoard(out Ledger ledger);
            // Replace two cells with mergeable flowers via sell + reward.
            e.TrySell(Location.Cell(1), e.GetCell(1).Id);
            e.TrySell(Location.Cell(3), e.GetCell(3).Id);
            e.EnqueueReward(T(Flower, 1));
            e.EnqueueReward(T(Flower, 1));
            e.TryClaimReward();
            e.TryClaimReward();
            Assert.IsTrue(e.IsBoardFull);
            Assert.IsTrue(e.TryFindMerge(out MergeHint hint));
            Assert.AreEqual(1, hint.CellA);
            Assert.AreEqual(3, hint.CellB);

            Assert.AreEqual(DropOutcome.Merged, e.TryDrop(1, e.GetCell(1).Id, 3).Outcome);
            Assert.AreEqual(1, e.CountEmptyCells());
            Item gen = e.GetCell(GeneratorCell);
            Assert.AreEqual(1, e.TryTapGenerator(GeneratorCell, gen.Id, 0).Cell);
            AssertHealthy(e, ledger);
        }

        // 7 ------------------------------------------------------------------

        [Test]
        public void E07_InvalidDrops_OnFullBoard_AreAllRefusedUnchanged()
        {
            MergeEngine e = FullBoard(out Ledger ledger);
            ItemId a = e.GetCell(10).Id;
            ItemId b = e.GetCell(11).Id;

            AssertRefused(e, ledger, ActionError.InvalidCell, () => e.TryDrop(-1, a, 11).Error);
            AssertRefused(e, ledger, ActionError.InvalidCell, () => e.TryDrop(10, a, FullCells).Error);
            AssertRefused(e, ledger, ActionError.InvalidCell, () => e.TryDrop(int.MaxValue, a, 0).Error);
            AssertRefused(e, ledger, ActionError.SameLocation, () => e.TryDrop(10, a, 10).Error);
            AssertRefused(e, ledger, ActionError.StaleItem, () => e.TryDrop(10, b, 11).Error);
            AssertRefused(e, ledger, ActionError.StaleItem, () => e.TryDrop(10, ItemId.None, 11).Error);
            AssertRefused(e, ledger, ActionError.StaleItem, () => e.TryDrop(10, new ItemId(e.NextItemId), 11).Error);
            AssertRefused(e, ledger, ActionError.SourceEmpty, () => e.TryMoveFromInventory(0, ItemId.None, 0).Error);
            AssertRefused(e, ledger, ActionError.InvalidSlot, () => e.TryMoveFromInventory(-1, a, 0).Error);
        }

        [Test]
        public void E07b_DropOntoLockedCell_AndFromEmptyCell_AreRefusedUnchanged()
        {
            MergeEngine e = ShapedEngine(out Ledger ledger, At(Cup, 1, 10));
            ItemId id = e.GetCell(10).Id;
            AssertRefused(e, ledger, ActionError.CellLocked, () => e.TryDrop(10, id, LockedCells[0]).Error);
            AssertRefused(e, ledger, ActionError.CellLocked, () => e.TryDrop(LockedCells[1], id, 10).Error);
            AssertRefused(e, ledger, ActionError.SourceEmpty, () => e.TryDrop(11, id, 12).Error);
        }

        [Test]
        public void E07c_InterruptedDrag_KillAndReload_ThenCompleteDrop()
        {
            MergeEngine e = FullBoard(out _);
            e.TrySell(Location.Cell(2), e.GetCell(2).Id);
            ItemId token = e.GetCell(10).Id;   // player picks up cell 10...
            string atPickup = Canon(e);         // ...app is paused (autosave) and killed mid-drag.

            MergeEngine reloaded = Reload(e, out Ledger ledger);
            Assert.AreEqual(atPickup, Canon(reloaded), "reload must restore the exact pre-drag state");
            Assert.AreEqual(token, reloaded.GetCell(10).Id, "item is still at its origin, exactly once");
            Assert.AreEqual(FullCells - 1, reloaded.ItemCount);

            // The resumed drop with the pre-kill token is a normal drop.
            DropResult r = reloaded.TryDrop(10, token, 2);
            Assert.AreEqual(DropOutcome.Moved, r.Outcome);
            Assert.AreEqual(token, reloaded.GetCell(2).Id);
            Assert.IsNull(reloaded.GetCell(10));

            // Replaying the same drop (duplicate input event) is refused unchanged.
            AssertRefused(reloaded, ledger, ActionError.SourceEmpty, () => reloaded.TryDrop(10, token, 2).Error);
            AssertHealthy(reloaded, ledger);
        }

        [Test]
        public void E07d_DragTokenGoesStaleAfterItemIsMergedOrMoved()
        {
            MergeEngine e = ShapedEngine(out Ledger ledger, At(Flower, 1, 10), At(Flower, 1, 11), At(Cup, 1, 20));
            ItemId held = e.GetCell(10).Id;
            ItemId cup = e.GetCell(20).Id;

            // Another path merges the held item away before the drop arrives.
            e.TryDrop(11, e.GetCell(11).Id, 10);
            Assert.AreNotEqual(held, e.GetCell(10).Id);
            AssertRefused(e, ledger, ActionError.StaleItem, () => e.TryDrop(10, held, 30).Error);

            // A held item moved to inventory: board drop from its old cell is refused, as is a sell there.
            e.TryMoveToInventory(20, cup);
            AssertRefused(e, ledger, ActionError.SourceEmpty, () => e.TryDrop(20, cup, 21).Error);
            AssertRefused(e, ledger, ActionError.SourceEmpty, () => e.TrySell(Location.Cell(20), cup).Error);
            AssertRefused(e, ledger, ActionError.StaleItem, () => e.TryMoveFromInventory(0, held, 21).Error);
            AssertHealthy(e, ledger);
        }

        // 8 ------------------------------------------------------------------

        [Test]
        public void E08_SaveAndRestore_FullBoard()
        {
            MergeEngine e = FullBoard(out _);
            Item gen = e.GetCell(GeneratorCell);
            e.TrySell(Location.Cell(0), e.GetCell(0).Id);
            e.TryTapGenerator(GeneratorCell, gen.Id, 500); // fills cell 0 again, spends a charge
            Assert.IsTrue(e.IsBoardFull);

            MergeEngine loaded = Reload(e, out Ledger ledger);

            Assert.AreEqual(Canon(e), Canon(loaded));
            Assert.IsTrue(loaded.IsBoardFull);
            Assert.AreEqual(FullCells, loaded.ItemCount);
            Assert.AreEqual(FullCells, ledger.Entries.Count, "one Load record per item");
            Assert.AreEqual(e.NextItemId, loaded.NextItemId);
            Assert.AreEqual(e.RngState, loaded.RngState);
            Item gen2 = loaded.GetCell(GeneratorCell);
            Assert.AreEqual(ToolboxCharges - 1, gen2.GeneratorCharges);
            AssertRefused(loaded, ledger, ActionError.BoardFull, () => loaded.TryTapGenerator(GeneratorCell, gen2.Id, 600).Error);
            AssertHealthy(loaded, ledger);

            // Both engines continue identically after the same recovery action.
            e.TrySell(Location.Cell(23), e.GetCell(23).Id);
            loaded.TrySell(Location.Cell(23), loaded.GetCell(23).Id);
            SpawnResult ra = e.TryTapGenerator(GeneratorCell, gen.Id, 700);
            SpawnResult rb = loaded.TryTapGenerator(GeneratorCell, gen2.Id, 700);
            Assert.AreEqual(ra.Item, rb.Item);
            Assert.AreEqual(Canon(e), Canon(loaded));
        }

        // 9 ------------------------------------------------------------------

        [Test]
        public void E09_RestoreBoardWithQueuedRewards_PreservesQueueAndOrder()
        {
            MergeEngine e = FullBoard(out _);
            e.EnqueueReward(T(Toolbox, 3));
            e.EnqueueReward(T(Cup, 2));
            e.EnqueueReward(T(Flower, 5));

            MergeEngine loaded = Reload(e, out Ledger ledger);

            Assert.AreEqual(3, loaded.PendingRewardCount);
            Assert.AreEqual(T(Toolbox, 3), loaded.GetPendingReward(0));
            Assert.AreEqual(T(Cup, 2), loaded.GetPendingReward(1));
            Assert.AreEqual(T(Flower, 5), loaded.GetPendingReward(2));
            AssertRefused(loaded, ledger, ActionError.BoardFull, () => loaded.TryClaimReward().Error);

            loaded.TrySell(Location.Cell(8), loaded.GetCell(8).Id);
            long nextId = loaded.NextItemId;
            ItemResult claim = loaded.TryClaimReward();
            Assert.AreEqual(ActionError.None, claim.Error);
            Assert.AreEqual(nextId, claim.Item.Value, "reward IDs continue from the saved counter");
            Item gen = loaded.GetCell(8);
            Assert.AreEqual(T(Toolbox, 3), gen.Type);
            Assert.AreEqual(14, gen.GeneratorCharges, "generator reward arrives full");
            Assert.AreEqual(2, loaded.PendingRewardCount);

            // A second save/load in the middle of the queue keeps the rest.
            MergeEngine again = Reload(loaded, out Ledger ledger2);
            Assert.AreEqual(T(Cup, 2), again.GetPendingReward(0));
            Assert.AreEqual(T(Flower, 5), again.GetPendingReward(1));
            AssertHealthy(again, ledger2);
        }

        [Test]
        public void E09b_SnapshotWithInvalidQueuedReward_IsRejected()
        {
            MergeEngine e = FullBoard(out _);
            e.EnqueueReward(T(Cup, 2));
            EngineSnapshot s = e.CreateSnapshot();
            s.PendingRewards[0].Tier = 99;
            Assert.IsFalse(MergeEngine.TryFromSnapshot(e.Config, s, out _, out List<string> errors));
            Assert.That(errors.Exists(x => x.Contains("Pending reward")));
            s.PendingRewards = new ItemTypeRecord[] { null };
            Assert.IsFalse(MergeEngine.TryFromSnapshot(e.Config, s, out _, out _));
        }

        // 10 -----------------------------------------------------------------

        [Test]
        public void E10_DeliverItemsAfterSaveLoad()
        {
            MergeEngine e = FullBoard(out _);
            ItemId cupId = e.GetCell(20).Id;    // cup:6
            ItemId flowerId = e.GetCell(21).Id; // flower:8
            ItemId storedId = e.GetCell(22).Id;
            ItemId soldId = e.GetCell(23).Id;
            e.TryMoveToInventory(22, storedId);
            e.TrySell(Location.Cell(23), soldId);

            MergeEngine loaded = Reload(e, out Ledger ledger);

            // IDs taken before the save are still valid after it.
            Assert.IsTrue(loaded.TryGetItem(cupId, out _));
            Assert.IsTrue(loaded.TryGetItem(flowerId, out _));

            AssertRefused(loaded, ledger, ActionError.UnknownItem, () => loaded.TryDeliver(new[] { cupId, soldId }).Error);
            AssertRefused(loaded, ledger, ActionError.ItemNotOnBoard, () => loaded.TryDeliver(new[] { cupId, storedId }).Error);
            AssertRefused(loaded, ledger, ActionError.DuplicateItem, () => loaded.TryDeliver(new[] { cupId, cupId }).Error);

            var removed = new List<ItemId>();
            loaded.ItemRemoved += (item, reason) =>
            {
                Assert.AreEqual(RemovalReason.Delivery, reason);
                removed.Add(item.Id);
            };
            Assert.AreEqual(ActionError.None, loaded.TryDeliver(new[] { cupId, flowerId }).Error);
            CollectionAssert.AreEqual(new[] { cupId, flowerId }, removed);
            Assert.IsNull(loaded.GetCell(20));
            Assert.IsNull(loaded.GetCell(21));
            AssertRefused(loaded, ledger, ActionError.UnknownItem, () => loaded.TryDeliver(new[] { cupId }).Error);
            AssertHealthy(loaded, ledger);

            // And the delivery survives another save/load.
            MergeEngine again = Reload(loaded, out Ledger ledger2);
            Assert.IsFalse(again.TryGetItem(cupId, out _));
            Assert.AreEqual(Canon(loaded), Canon(again));
            AssertHealthy(again, ledger2);
        }
    }
}
