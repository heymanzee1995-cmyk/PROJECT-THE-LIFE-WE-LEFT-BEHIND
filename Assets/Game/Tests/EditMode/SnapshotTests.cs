using System.Collections.Generic;
using NUnit.Framework;
using static Game.Domain.Tests.TestConfigs;

namespace Game.Domain.Tests
{
    public class SnapshotTests
    {
        private static MergeEngine BusyEngine()
        {
            MergeEngine e = NewEngine(out _, 31337);
            Item gen = e.GetCell(GeneratorCell);
            for (int i = 0; i < 4; i++)
            {
                e.TryTapGenerator(GeneratorCell, gen.Id, 1000);
            }

            e.TryMoveToInventory(12, e.GetCell(12).Id); // first spawned item
            e.TryUnlockCell(LockedCells[0]);
            e.EnqueueReward(T(Cup, 2));
            return e;
        }

        [Test]
        public void RoundTrip_PreservesEverything()
        {
            MergeEngine e = BusyEngine();
            var ledger = new Ledger();
            MergeEngine loaded = MergeEngine.FromSnapshot(e.Config, e.CreateSnapshot(), ledger);
            Assert.AreEqual(Canon(e), Canon(loaded));
            Assert.AreEqual(e.RngState, loaded.RngState);
            Assert.That(InvariantChecker.Check(loaded, ledger, true), Is.Empty);
            Assert.AreEqual(loaded.ItemCount, ledger.Entries.Count, "every loaded item gets a Load provenance record");
        }

        [Test]
        public void AfterLoad_BehaviourContinuesIdentically_AndIdsKeepIncreasing()
        {
            MergeEngine a = BusyEngine();
            MergeEngine b = MergeEngine.FromSnapshot(a.Config, a.CreateSnapshot());
            long next = a.NextItemId;
            SpawnResult ra = a.TryTapGenerator(GeneratorCell, a.GetCell(GeneratorCell).Id, 2000);
            SpawnResult rb = b.TryTapGenerator(GeneratorCell, b.GetCell(GeneratorCell).Id, 2000);
            Assert.AreEqual(next, ra.Item.Value);
            Assert.AreEqual(ra.Item, rb.Item);
            Assert.AreEqual(Canon(a), Canon(b));
        }

        private static void AssertRejected(System.Action<EngineSnapshot> corrupt, string fragment)
        {
            MergeEngine e = BusyEngine();
            EngineSnapshot s = e.CreateSnapshot();
            corrupt(s);
            bool ok = MergeEngine.TryFromSnapshot(e.Config, s, out MergeEngine loaded, out List<string> errors);
            Assert.IsFalse(ok);
            Assert.IsNull(loaded);
            Assert.That(errors.Exists(x => x.Contains(fragment)), "expected '" + fragment + "' in:\n" + string.Join("\n", errors));
        }

        [Test]
        public void CorruptSnapshots_AreRejectedWithClearErrors()
        {
            AssertRejected(s => s.Version = 99, "version");
            AssertRejected(s => s.Columns = 8, "config is 5x8");
            AssertRejected(s => s.LockedCells = new bool[3], "locked-cell table");
            AssertRejected(s => s.InventoryCapacity = 50, "inventory capacity");
            AssertRejected(s => s.Items[1].Id = s.Items[0].Id, "duplicate ID");
            AssertRejected(s => s.Items[0].Id = s.NextItemId, "ID must be in");
            AssertRejected(s => s.NextItemId = 1, "ID must be in");
            AssertRejected(s => s.Items[0].ChainId = "ghost", "unknown type");
            AssertRejected(s => s.Items[0].Tier = 0, "unknown type");
            AssertRejected(s => s.Items[1].Index = s.Items[0].Index, "more than one item");
            AssertRejected(s => s.Items[0].Index = LockedCells[3], "locked");
            AssertRejected(s => s.Items[0].Area = 7, "invalid area");
            AssertRejected(s => FindInventory(s).Index = 40, "outside capacity");
            AssertRejected(s => FindGenerator(s).GeneratorCharges = 99, "generator state");
            AssertRejected(s => FindNonGenerator(s).GeneratorCharges = 1, "non-generator");
            AssertRejected(s => s.PendingRewards[0].ChainId = "ghost", "Pending reward");
            AssertRejected(s => s.Items = null, "missing");
        }

        [Test]
        public void NullSnapshot_IsRejected()
        {
            Assert.IsFalse(MergeEngine.TryFromSnapshot(Default(), null, out _, out List<string> errors));
            Assert.That(errors, Is.Not.Empty);
            Assert.Throws<SnapshotLoadException>(() => MergeEngine.FromSnapshot(Default(), null));
        }

        private static ItemRecord FindInventory(EngineSnapshot s) => System.Array.Find(s.Items, r => r.Area == (int)LocationArea.Inventory);
        private static ItemRecord FindGenerator(EngineSnapshot s) => System.Array.Find(s.Items, r => r.ChainId == Toolbox);
        private static ItemRecord FindNonGenerator(EngineSnapshot s) => System.Array.Find(s.Items, r => r.ChainId == Flower || r.ChainId == Cup);
    }
}
