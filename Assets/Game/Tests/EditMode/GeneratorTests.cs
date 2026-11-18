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
            // Nearest cells to 31 (col 3,row 4) at distance 1 are 24, 30, 32, 38: lowest index wins.
            Assert.AreEqual(24, r.Cell);
            Assert.AreEqual(r.Item, e.GetCell(24).Id);
            Assert.AreEqual(ToolboxCharges - 1, gen.GeneratorCharges);
            Assert.AreEqual(0, gen.GeneratorReadyAtMs);
            Assert.That(InvariantChecker.Check(e, ledger, true), Is.Empty);
        }

        [Test]
        public void SpendingAllCharges_StartsCooldown_ThenRefillsToFull()
        {
            MergeEngine e = NewEngine(out _);
            Item gen = e.GetCell(GeneratorCell);
            long now = 5000;
            for (int i = 0; i < ToolboxCharges; i++)
            {
                Assert.AreEqual(ActionError.None, e.TryTapGenerator(GeneratorCell, gen.Id, now).Error);
            }

            Assert.AreEqual(now + ToolboxCooldownMs, gen.GeneratorReadyAtMs);
            SpawnResult cooling = e.TryTapGenerator(GeneratorCell, gen.Id, now + 1000);
            Assert.AreEqual(ActionError.GeneratorOnCooldown, cooling.Error);
            Assert.AreEqual(ToolboxCooldownMs - 1000, cooling.CooldownRemainingMs);
            Assert.AreEqual(0, e.GetAvailableCharges(gen, now + ToolboxCooldownMs - 1));

            long ready = now + ToolboxCooldownMs;
            Assert.AreEqual(ToolboxCharges, e.GetAvailableCharges(gen, ready));
            Assert.AreEqual(0, e.GetCooldownRemainingMs(gen, ready));
            Assert.AreEqual(ActionError.None, e.TryTapGenerator(GeneratorCell, gen.Id, ready).Error);
            Assert.AreEqual(ToolboxCharges - 1, gen.GeneratorCharges);
        }

        [Test]
        public void CooldownSurvivesSnapshot()
        {
            MergeEngine e = NewEngine(out _);
            Item gen = e.GetCell(GeneratorCell);
            for (int i = 0; i < ToolboxCharges; i++)
            {
                e.TryTapGenerator(GeneratorCell, gen.Id, 0);
            }

            MergeEngine loaded = MergeEngine.FromSnapshot(e.Config, e.CreateSnapshot());
            Item g2 = loaded.GetCell(GeneratorCell);
            Assert.AreEqual(ActionError.GeneratorOnCooldown, loaded.TryTapGenerator(GeneratorCell, g2.Id, ToolboxCooldownMs - 1).Error);
            Assert.AreEqual(ActionError.None, loaded.TryTapGenerator(GeneratorCell, g2.Id, ToolboxCooldownMs).Error);
        }

        [Test]
        public void FullBoard_RefusesSpawn_WithoutSpendingChargeOrRng_AndHasResolution()
        {
            var layout = new List<InitialPlacement> { At(Toolbox, 1, GeneratorCell) };
            for (int cell = 0; cell < 56; cell++)
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
                    now += ToolboxCooldownMs;
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
