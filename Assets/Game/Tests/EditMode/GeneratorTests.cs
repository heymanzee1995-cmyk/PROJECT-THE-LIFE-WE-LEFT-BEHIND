using System.Collections.Generic;
using NUnit.Framework;
using static Game.Domain.Tests.TestConfigs;

namespace Game.Domain.Tests
{
    public class GeneratorTests
    {
        [Test]
        public void Tap_SpawnsIntoNearestEmptyCell_AndSpendsOneCharge()
        {
            MergeEngine e = NewEngine(out Ledger ledger);
            Item gen = e.GetCell(GeneratorCell);
            SpawnResult r = e.TryTapGenerator(GeneratorCell, gen.Id, 1000);

            Assert.AreEqual(ActionError.None, r.Error);
            // Nearest cells to 17 (col 2,row 3) at distance 1 are 12, 16, 18, 22: lowest index wins.
            Assert.AreEqual(12, r.Cell);
            Assert.AreEqual(r.Item, e.GetCell(12).Id);
            Assert.AreEqual(ToolboxCharges - 1, gen.GeneratorCharges);
            Assert.AreEqual(1000, gen.GeneratorRechargeStartMs, "spending from full starts the recharge timer");
            Assert.That(InvariantChecker.Check(e, ledger, true), Is.Empty);
        }

        [Test]
        public void Recharge_RestoresOneChargePerInterval_AndStopsWhenFull()
        {
            MergeEngine e = NewEngine(out _);
            Item gen = e.GetCell(GeneratorCell);
            const long now = 5000;
            const long T = ToolboxRechargeMs;
            for (int i = 0; i < ToolboxCharges; i++)
            {
                Assert.AreEqual(ActionError.None, e.TryTapGenerator(GeneratorCell, gen.Id, now).Error);
            }

            // Timer started at the first spend (from full); later spends did not reset it.
            Assert.AreEqual(now, gen.GeneratorRechargeStartMs);
            SpawnResult empty = e.TryTapGenerator(GeneratorCell, gen.Id, now + 1000);
            Assert.AreEqual(ActionError.GeneratorOnCooldown, empty.Error);
            Assert.AreEqual(T - 1000, empty.CooldownRemainingMs, "time to the next single charge");

            Assert.AreEqual(0, e.GetAvailableCharges(gen, now + T - 1), "0.99 intervals restores nothing");
            Assert.AreEqual(1, e.GetAvailableCharges(gen, now + T), "one interval restores one charge");
            Assert.AreEqual(2, e.GetAvailableCharges(gen, now + T * 5 / 2), "2.5 intervals restore two");
            Assert.AreEqual(T / 2, e.GetNextChargeInMs(gen, now + T * 5 / 2), "half an interval of progress kept");
            Assert.AreEqual(T / 2 + 3 * T, e.GetFullRechargeInMs(gen, now + T * 5 / 2));

            Assert.AreEqual(ToolboxCharges, e.GetAvailableCharges(gen, now + T * 100), "a long absence stops at max");
            Assert.AreEqual(0, e.GetNextChargeInMs(gen, now + T * 100), "timer stopped when full");
            Assert.AreEqual(0, e.GetFullRechargeInMs(gen, now + T * 100));

            // Spending one restored charge keeps the running timer (start advanced by whole intervals).
            Assert.AreEqual(ActionError.None, e.TryTapGenerator(GeneratorCell, gen.Id, now + T * 5 / 2).Error);
            Assert.AreEqual(1, gen.GeneratorCharges);
            Assert.AreEqual(now + 2 * T, gen.GeneratorRechargeStartMs);
            Assert.AreEqual(T / 2, e.GetNextChargeInMs(gen, now + T * 5 / 2));
        }

        [Test]
        public void SpendingBelowFull_DoesNotResetTheTimer()
        {
            MergeEngine e = NewEngine(out _);
            Item gen = e.GetCell(GeneratorCell);
            e.TryTapGenerator(GeneratorCell, gen.Id, 0);
            e.TryTapGenerator(GeneratorCell, gen.Id, ToolboxRechargeMs / 2);
            Assert.AreEqual(0, gen.GeneratorRechargeStartMs);
            Assert.AreEqual(ToolboxCharges - 1, e.GetAvailableCharges(gen, ToolboxRechargeMs), "one charge back at T, not at 1.5T");
        }

        [Test]
        public void RechargeProgressSurvivesSnapshot()
        {
            MergeEngine e = NewEngine(out _);
            Item gen = e.GetCell(GeneratorCell);
            for (int i = 0; i < ToolboxCharges; i++)
            {
                e.TryTapGenerator(GeneratorCell, gen.Id, 0);
            }

            MergeEngine loaded = MergeEngine.FromSnapshot(e.Config, e.CreateSnapshot());
            Item g2 = loaded.GetCell(GeneratorCell);
            Assert.AreEqual(ActionError.GeneratorOnCooldown, loaded.TryTapGenerator(GeneratorCell, g2.Id, ToolboxRechargeMs - 1).Error);
            Assert.AreEqual(ActionError.None, loaded.TryTapGenerator(GeneratorCell, g2.Id, ToolboxRechargeMs).Error);
            Assert.AreEqual(ActionError.GeneratorOnCooldown, loaded.TryTapGenerator(GeneratorCell, g2.Id, ToolboxRechargeMs).Error,
                "the one restored charge was spent; the next comes one interval later");
        }

        [Test]
        public void SaveLoad_NeverRestoresAChargeTwice()
        {
            const long T = ToolboxRechargeMs;
            MergeEngine reference = NewEngine(out _);
            Item refGen = reference.GetCell(GeneratorCell);
            for (int i = 0; i < 4; i++)
            {
                reference.TryTapGenerator(GeneratorCell, refGen.Id, 0);
            }

            MergeEngine e = MergeEngine.FromSnapshot(reference.Config, reference.CreateSnapshot());
            long[] times = { T / 3, T / 3, T, T + 1, T + 1, 2 * T + T / 2, 2 * T + T / 2, 10 * T };
            foreach (long t in times)
            {
                // Reload repeatedly, including several times at the same moment.
                e = MergeEngine.FromSnapshot(e.Config, e.CreateSnapshot());
                Item g = e.GetCell(GeneratorCell);
                Assert.AreEqual(reference.GetAvailableCharges(refGen, t), e.GetAvailableCharges(g, t), "charges at t=" + t);
                Assert.AreEqual(reference.GetNextChargeInMs(refGen, t), e.GetNextChargeInMs(g, t), "next charge at t=" + t);
            }

            // Tapping at a restored moment and reloading still matches the single engine.
            reference.TryTapGenerator(GeneratorCell, refGen.Id, 2 * T + T / 2);
            Item g2 = e.GetCell(GeneratorCell);
            e.TryTapGenerator(GeneratorCell, g2.Id, 2 * T + T / 2);
            e = MergeEngine.FromSnapshot(e.Config, e.CreateSnapshot());
            Assert.AreEqual(Canon(reference), Canon(e));
        }

        [Test]
        public void ClockGoingBackwards_RestoresNothing_AndKeepsTheTimer()
        {
            MergeEngine e = NewEngine(out _);
            Item gen = e.GetCell(GeneratorCell);
            const long start = 1000000;
            for (int i = 0; i < ToolboxCharges; i++)
            {
                e.TryTapGenerator(GeneratorCell, gen.Id, start);
            }

            Assert.AreEqual(0, e.GetAvailableCharges(gen, start - 500000));
            Assert.AreEqual(ActionError.GeneratorOnCooldown, e.TryTapGenerator(GeneratorCell, gen.Id, start - 500000).Error);
            Assert.AreEqual(start, gen.GeneratorRechargeStartMs);
            Assert.AreEqual(1, e.GetAvailableCharges(gen, start + ToolboxRechargeMs));
        }

        [Test]
        public void RefusedTapAtZeroCharges_ChangesNothing()
        {
            MergeEngine e = NewEngine(out Ledger ledger);
            Item gen = e.GetCell(GeneratorCell);
            for (int i = 0; i < ToolboxCharges; i++)
            {
                e.TryTapGenerator(GeneratorCell, gen.Id, 0);
            }

            string before = Canon(e);
            int ledgerBefore = ledger.Entries.Count;
            int events = 0;
            e.ItemCreated += (a, b) => events++;
            Assert.AreEqual(ActionError.GeneratorOnCooldown, e.TryTapGenerator(GeneratorCell, gen.Id, ToolboxRechargeMs / 2).Error);
            Assert.AreEqual(before, Canon(e));
            Assert.AreEqual(ledgerBefore, ledger.Entries.Count);
            Assert.AreEqual(0, events);
        }

        [Test]
        public void FullBoard_RefusesSpawn_WithoutSpendingChargeOrRng_AndHasResolution()
        {
            var layout = new List<InitialPlacement> { At(Toolbox, 1, GeneratorCell) };
            for (int cell = 0; cell < LockedCells[0]; cell++)
            {
                if (cell != GeneratorCell)
                {
                    // Alternate chains so no merge exists: the only resolution is inventory/sell.
                    layout.Add(cell % 2 == 0 ? At(Cup, 6, cell) : At(Flower, 8, cell));
                }
            }

            MergeEngine e = NewEngine(out _, 1, layout, Default(inventoryCapacity: 0, maxInventoryCapacity: 0));
            Assert.IsTrue(e.IsBoardFull);
            Item gen = e.GetCell(GeneratorCell);
            string before = Canon(e);

            SpawnResult r = e.TryTapGenerator(GeneratorCell, gen.Id, 0);

            Assert.AreEqual(ActionError.BoardFull, r.Error);
            Assert.AreEqual(before, Canon(e), "a refused spawn must not change charges, RNG or items");
            Assert.IsFalse(e.TryFindMerge(out _));
            Assert.IsTrue(e.HasBoardFullResolution(), "selling must always be available");

            // Resolve by selling one item, then spawning works.
            Assert.AreEqual(ActionError.None, e.TrySell(Location.Cell(0), e.GetCell(0).Id).Error);
            Assert.AreEqual(ActionError.None, e.TryTapGenerator(GeneratorCell, gen.Id, 0).Error);
        }

        [Test]
        public void TappingNonGenerator_OrWrongCell_IsRefused()
        {
            MergeEngine e = EngineWith(out _, At(Cup, 1, 5), At(Toolbox, 1, 6));
            string before = Canon(e);
            Assert.AreEqual(ActionError.NotAGenerator, e.TryTapGenerator(5, e.GetCell(5).Id, 0).Error);
            Assert.AreEqual(ActionError.SourceEmpty, e.TryTapGenerator(7, ItemId.None, 0).Error);
            Assert.AreEqual(ActionError.InvalidCell, e.TryTapGenerator(-3, ItemId.None, 0).Error);
            Assert.AreEqual(ActionError.StaleItem, e.TryTapGenerator(6, e.GetCell(5).Id, 0).Error);
            Assert.AreEqual(before, Canon(e));
        }

        [Test]
        public void GeneratorInInventory_KeepsStateButCannotBeTapped()
        {
            MergeEngine e = NewEngine(out _);
            Item gen = e.GetCell(GeneratorCell);
            e.TryTapGenerator(GeneratorCell, gen.Id, 0);
            Assert.AreEqual(ActionError.None, e.TryMoveToInventory(GeneratorCell, gen.Id).Error);
            Assert.AreEqual(ActionError.SourceEmpty, e.TryTapGenerator(GeneratorCell, gen.Id, 0).Error);
            Assert.AreEqual(ToolboxCharges - 1, gen.GeneratorCharges);
        }

        [Test]
        public void Rolls_FollowConfiguredWeights()
        {
            // 60% flower / 40% cup at tier 1. Spawn many times, emptying the board as we go.
            MergeEngine e = NewEngine(out _, 777, null, Default());
            Item gen = e.GetCell(GeneratorCell);
            int flowers = 0, total = 0;
            long now = 0;
            while (total < 6000)
            {
                SpawnResult r = e.TryTapGenerator(GeneratorCell, gen.Id, now);
                if (r.Error == ActionError.GeneratorOnCooldown)
                {
                    now += ToolboxRechargeMs;
                    continue;
                }

                Assert.AreEqual(ActionError.None, r.Error);
                Item spawned = e.GetCell(r.Cell);
                if (spawned.Type.ChainId == Flower)
                {
                    flowers++;
                }

                total++;
                e.TrySell(Location.Cell(r.Cell), spawned.Id);
            }

            Assert.AreEqual(0.6, (double)flowers / total, 0.03);
        }

        [Test]
        public void SameSeedAndActions_GiveIdenticalResults()
        {
            string Run()
            {
                MergeEngine e = NewEngine(out _, 2024);
                Item gen = e.GetCell(GeneratorCell);
                for (int i = 0; i < ToolboxCharges; i++)
                {
                    e.TryTapGenerator(GeneratorCell, gen.Id, 0);
                }

                return Canon(e);
            }

            Assert.AreEqual(Run(), Run());
        }
    }
}
