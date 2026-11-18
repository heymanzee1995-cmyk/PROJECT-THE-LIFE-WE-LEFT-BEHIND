using System.Collections.Generic;
using NUnit.Framework;
using static Game.Domain.Tests.TestConfigs;

namespace Game.Domain.Tests
{
    public class MergeTests
    {
        private static void AssertHealthy(MergeEngine engine, Ledger ledger)
        {
            List<string> v = InvariantChecker.Check(engine, ledger, fullReplay: true);
            Assert.That(v, Is.Empty, string.Join("\n", v));
        }

        [Test]
        public void MergingTwoIdenticalItems_ProducesOneNextTierItemWithNewId()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 2, 10), At(Flower, 2, 11));
            ItemId a = e.GetCell(10).Id, b = e.GetCell(11).Id;

            DropResult r = e.TryDrop(10, a, 11);

            Assert.AreEqual(ActionError.None, r.Error);
            Assert.AreEqual(DropOutcome.Merged, r.Outcome);
            Assert.IsNull(e.GetCell(10));
            Item result = e.GetCell(11);
            Assert.AreEqual(T(Flower, 3), result.Type);
            Assert.AreEqual(r.ResultItem, result.Id);
            Assert.AreNotEqual(a, result.Id);
            Assert.AreNotEqual(b, result.Id);
            Assert.That(result.Id.Value, Is.GreaterThan(System.Math.Max(a.Value, b.Value)));
            Assert.IsFalse(e.TryGetItem(a, out _));
            Assert.IsFalse(e.TryGetItem(b, out _));
            Assert.AreEqual(1, e.ItemCount);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void MergedIds_AreNeverReused()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 1, 1), At(Flower, 1, 2), At(Flower, 1, 3), At(Flower, 1, 4));
            var seen = new HashSet<long> { 1, 2, 3, 4 };
            e.ItemCreated += (item, _) => Assert.IsTrue(seen.Add(item.Id.Value), "reused " + item.Id);

            e.TryDrop(1, e.GetCell(1).Id, 2);
            e.TryDrop(3, e.GetCell(3).Id, 4);
            e.TryDrop(2, e.GetCell(2).Id, 4);

            Assert.AreEqual(T(Flower, 3), e.GetCell(4).Type);
            Assert.AreEqual(7, seen.Count);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void DifferentTiers_Swap()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 1, 10), At(Flower, 2, 11));
            ItemId a = e.GetCell(10).Id, b = e.GetCell(11).Id;
            DropResult r = e.TryDrop(10, a, 11);
            Assert.AreEqual(DropOutcome.Swapped, r.Outcome);
            Assert.AreEqual(b, e.GetCell(10).Id);
            Assert.AreEqual(a, e.GetCell(11).Id);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void DifferentChains_Swap()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 1, 10), At(Cup, 1, 11));
            Assert.AreEqual(DropOutcome.Swapped, e.TryDrop(10, e.GetCell(10).Id, 11).Outcome);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void MaxTierItems_DoNotMerge()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Cup, 6, 10), At(Cup, 6, 11));
            Assert.AreEqual(DropOutcome.Swapped, e.TryDrop(10, e.GetCell(10).Id, 11).Outcome);
            Assert.AreEqual(2, e.ItemCount);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void DropOnEmptyCell_Moves()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Cup, 1, 10));
            ItemId id = e.GetCell(10).Id;
            Location from = default, to = default;
            e.ItemMoved += (item, f, t) => { from = f; to = t; };
            DropResult r = e.TryDrop(10, id, 20);
            Assert.AreEqual(DropOutcome.Moved, r.Outcome);
            Assert.AreEqual(id, e.GetCell(20).Id);
            Assert.AreEqual(Location.Cell(10), from);
            Assert.AreEqual(Location.Cell(20), to);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void GeneratorsMerge_IntoFreshNextTierGenerator()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Toolbox, 1, 10), At(Toolbox, 1, 11));
            e.TryTapGenerator(10, e.GetCell(10).Id, 0); // spend a charge on one of them
            DropResult r = e.TryDrop(10, e.GetCell(10).Id, 11);
            Assert.AreEqual(DropOutcome.Merged, r.Outcome);
            Item gen = e.GetCell(11);
            Assert.IsTrue(gen.IsGenerator);
            Assert.AreEqual(T(Toolbox, 2), gen.Type);
            Assert.AreEqual(10, gen.GeneratorCharges);
            AssertHealthy(e, ledger);
        }

        [Test]
        public void MergeEvents_FireAfterStateIsConsistent()
        {
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 1, 10), At(Flower, 1, 11));
            var order = new List<string>();
            e.ItemRemoved += (item, reason) =>
            {
                order.Add("removed " + reason);
                Assert.IsFalse(e.TryGetItem(item.Id, out _));
            };
            e.ItemCreated += (item, reason) =>
            {
                order.Add("created " + reason);
                Assert.AreSame(item, e.GetCell(11));
            };
            e.ItemsMerged += (a, b, result) => order.Add("merged");
            e.TryDrop(10, e.GetCell(10).Id, 11);
            CollectionAssert.AreEqual(new[] { "removed Merge", "removed Merge", "created Merge", "merged" }, order);
        }

        [Test]
        public void RefusedDrops_ReturnErrorAndLeaveStateUnchanged()
        {
            var locked = new List<int>(LockedCells);
            MergeEngine e = EngineWith(out Ledger ledger, At(Flower, 1, 10), At(Flower, 1, 11));
            ItemId id = e.GetCell(10).Id;
            string before = Canon(e);
            int ledgerCount = ledger.Entries.Count;
            bool anyEvent = false;
            e.ItemMoved += (a, b, c) => anyEvent = true;
            e.ItemCreated += (a, b) => anyEvent = true;
            e.ItemRemoved += (a, b) => anyEvent = true;

            Assert.AreEqual(ActionError.InvalidCell, e.TryDrop(-1, id, 11).Error);
            Assert.AreEqual(ActionError.InvalidCell, e.TryDrop(10, id, 63).Error);
            Assert.AreEqual(ActionError.SameLocation, e.TryDrop(10, id, 10).Error);
            Assert.AreEqual(ActionError.CellLocked, e.TryDrop(10, id, locked[0]).Error);
            Assert.AreEqual(ActionError.SourceEmpty, e.TryDrop(12, id, 11).Error);
            Assert.AreEqual(ActionError.StaleItem, e.TryDrop(10, new ItemId(999), 11).Error);
            Assert.AreEqual(ActionError.StaleItem, e.TryDrop(10, e.GetCell(11).Id, 11 + 1).Error);

            Assert.AreEqual(before, Canon(e));
            Assert.AreEqual(ledgerCount, ledger.Entries.Count);
            Assert.IsFalse(anyEvent);
        }

        [Test]
        public void Hint_FindsAvailableMerge_AndIgnoresMaxTier()
        {
            MergeEngine e = EngineWith(out _, At(Cup, 6, 1), At(Cup, 6, 2), At(Flower, 3, 5), At(Cup, 2, 7), At(Flower, 3, 40));
            Assert.IsTrue(e.TryFindMerge(out MergeHint hint));
            Assert.AreEqual(5, hint.CellA);
            Assert.AreEqual(40, hint.CellB);

            MergeEngine none = EngineWith(out _, At(Cup, 6, 1), At(Cup, 6, 2), At(Flower, 3, 5));
            Assert.IsFalse(none.TryFindMerge(out _));
        }
    }
}
