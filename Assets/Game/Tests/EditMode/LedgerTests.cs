using NUnit.Framework;
using static Game.Domain.Tests.TestConfigs;

namespace Game.Domain.Tests
{
    /// <summary>The ledger must catch provenance errors on its own, independent of the engine.</summary>
    public class LedgerTests
    {
        private static readonly ItemType F1 = T(Flower, 1);
        private static readonly ItemType F2 = T(Flower, 2);

        [Test]
        public void DetectsReusedId()
        {
            var l = new Ledger();
            l.RecordCreated(CreationReason.Spawn, new ItemId(1), F1);
            l.RecordRemoved(RemovalReason.Sell, new ItemId(1), F1);
            l.RecordCreated(CreationReason.Spawn, new ItemId(1), F1);
            Assert.That(l.Violations, Has.Some.Contains("more than once"));
            Assert.That(l.Replay(Default()).Violations, Has.Some.Contains("created twice"));
        }

        [Test]
        public void DetectsRemovalOfUnknownItem()
        {
            var l = new Ledger();
            l.RecordRemoved(RemovalReason.Delivery, new ItemId(5), F1);
            Assert.That(l.Violations, Has.Some.Contains("not live"));
            Assert.That(l.Replay(Default()).Violations, Has.Some.Contains("not live"));
        }

        [Test]
        public void DetectsInvalidMergeShapes()
        {
            var l = new Ledger();
            l.RecordCreated(CreationReason.Spawn, new ItemId(1), F1);
            l.RecordCreated(CreationReason.Spawn, new ItemId(2), F2);
            l.RecordMerge(new ItemId(1), F1, new ItemId(2), F2, new ItemId(3), F2);
            Assert.That(l.Violations, Has.Some.Contains("different types"));

            var l2 = new Ledger();
            l2.RecordCreated(CreationReason.Spawn, new ItemId(1), F1);
            l2.RecordCreated(CreationReason.Spawn, new ItemId(2), F1);
            l2.RecordMerge(new ItemId(1), F1, new ItemId(2), F1, new ItemId(3), T(Flower, 3));
            Assert.That(l2.Violations, Has.Some.Contains("expected flower:2"));

            var l3 = new Ledger();
            l3.RecordCreated(CreationReason.Spawn, new ItemId(1), F1);
            l3.RecordMerge(new ItemId(1), F1, new ItemId(1), F1, new ItemId(2), F2);
            Assert.That(l3.Violations, Has.Some.Contains("same item twice"));
        }

        [Test]
        public void DetectsMergeRecordedAsPlainCreate()
        {
            var l = new Ledger();
            l.RecordCreated(CreationReason.Merge, new ItemId(1), F2);
            Assert.That(l.Violations, Has.Some.Contains("without its consumed items"));
        }

        [Test]
        public void ValidHistory_ReplaysCleanly_AndConservesMergeValue()
        {
            var l = new Ledger();
            l.RecordCreated(CreationReason.Load, new ItemId(1), F1);
            l.RecordCreated(CreationReason.Spawn, new ItemId(2), F1);
            l.RecordMerge(new ItemId(1), F1, new ItemId(2), F1, new ItemId(3), F2);
            l.RecordCreated(CreationReason.Reward, new ItemId(4), T(Cup, 2));
            l.RecordRemoved(RemovalReason.Delivery, new ItemId(4), T(Cup, 2));
            Assert.That(l.Violations, Is.Empty);
            LedgerReplay replay = l.Replay(Default());
            Assert.That(replay.Violations, Is.Empty);
            Assert.AreEqual(1, replay.Live.Count);
            Assert.AreEqual(F2, replay.Live[3]);
            Assert.AreEqual(2, replay.ExternalMergeValueBalance, "two flower:1 (value 1 each) in = one flower:2 (value 2) live");
        }

        [Test]
        public void Checker_FlagsEngineThatDisagreesWithLedger()
        {
            MergeEngine e = NewEngine(out Ledger ledger);
            // Simulate a lost ledger record: an extra unexplained creation in the ledger only.
            ledger.RecordCreated(CreationReason.Spawn, new ItemId(500), F1);
            Assert.That(InvariantChecker.Check(e, ledger), Has.Some.Contains("live items but engine has"));
        }
    }
}
