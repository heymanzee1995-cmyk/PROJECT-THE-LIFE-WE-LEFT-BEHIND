using System.Collections.Generic;
using Game.Data;
using Game.Domain;
using NUnit.Framework;

namespace Game.Presentation.Tests
{
    public class ContentTests
    {
        [Test]
        public void ShippedContentFile_IsValid_AndUsesTheApproved5x8Board()
        {
            MergeContent content = ContentFile.LoadValid();
            BoardConfig board = content.Config.Board;
            Assert.AreEqual(5, board.Columns);
            Assert.AreEqual(8, board.Rows);
            CollectionAssert.AreEquivalent(new[] { 35, 36, 37, 38, 39 }, board.InitiallyLockedCells);
            Assert.AreEqual(4, board.InventoryCapacity);
            Assert.AreEqual(8, board.MaxInventoryCapacity);
        }

        [Test]
        public void ShippedContent_StartsAPlayableBoard()
        {
            MergeContent content = ContentFile.LoadValid();
            MergeEngine engine = MergeEngine.CreateNew(content.Config, 1, content.StartingLayout, new Ledger());
            Assert.AreEqual(content.StartingLayout.Count, engine.ItemCount);
            Assert.IsTrue(engine.TryFindMerge(out _), "the first session should open with a merge available");
            bool hasGenerator = false;
            for (int i = 0; i < engine.CellCount; i++)
            {
                hasGenerator |= engine.GetCell(i) != null && engine.GetCell(i).IsGenerator;
            }

            Assert.IsTrue(hasGenerator);
        }

        [Test]
        public void ShippedContent_GeneratorOddsSumToOne_AndEveryTierHasAName()
        {
            MergeContent content = ContentFile.LoadValid();
            foreach (ChainDefinition chain in content.Config.Chains)
            {
                for (int tier = 1; tier <= chain.MaxTier; tier++)
                {
                    string name = content.Catalog.GetName(new ItemType(chain.Id, tier));
                    Assert.AreNotEqual(chain.Id + " " + tier, name, "missing display name for " + chain.Id + ":" + tier);
                    GeneratorDefinition gen = chain.GetGenerator(tier);
                    if (gen != null)
                    {
                        double sum = 0;
                        foreach (OutputOdds o in gen.GetOdds())
                        {
                            sum += o.Probability;
                        }

                        Assert.AreEqual(1.0, sum, 1e-9);
                    }
                }

                Assert.IsNotNull(content.Catalog.GetColor(chain.Id), "missing colour for " + chain.Id);
            }
        }

        private static MergeContentData Minimal() => new MergeContentData
        {
            version = 1,
            board = new BoardData { columns = 5, rows = 8, inventoryCapacity = 2, maxInventoryCapacity = 4, lockedCells = new[] { 39 } },
            chains = new[]
            {
                new ChainData
                {
                    id = "gen", color = "#112233", maxTier = 1, sellValues = new[] { 0 }, tierNames = new[] { "Gen" },
                    generators = new[]
                    {
                        new GeneratorTierData
                        {
                            tier = 1, maxCharges = 3, rechargeIntervalMs = 1000,
                            outputs = new[] { new OutputData { chainId = "a", tier = 1, weight = 1 } },
                        },
                    },
                },
                new ChainData { id = "a", maxTier = 2, sellValues = new[] { 1, 2 } },
            },
            startingLayout = new[] { new LayoutEntryData { chainId = "gen", tier = 1, cell = 0 } },
        };

        private static void AssertRejected(MergeContentData data, string fragment)
        {
            Assert.IsFalse(MergeContentMapper.TryBuild(data, out MergeContent content, out List<string> errors));
            Assert.IsNull(content);
            Assert.That(errors.Exists(e => e.Contains(fragment)), "expected '" + fragment + "' in:\n" + string.Join("\n", errors));
        }

        [Test]
        public void MinimalContent_IsValid()
        {
            Assert.IsTrue(MergeContentMapper.TryBuild(Minimal(), out MergeContent content, out List<string> errors), string.Join("\n", errors));
            Assert.AreEqual("a 2", content.Catalog.GetName(new ItemType("a", 2)), "placeholder name when none given");
            Assert.AreEqual("Gen", content.Catalog.GetName(new ItemType("gen", 1)));
        }

        [Test]
        public void BadContent_IsRejectedWithClearErrors()
        {
            Assert.IsFalse(MergeContentMapper.TryBuild(null, out _, out _));

            MergeContentData d = Minimal(); d.version = 0; AssertRejected(d, "no 'version'");
            d = Minimal(); d.version = 7; AssertRejected(d, "Unsupported content version");
            d = Minimal(); d.board = null; AssertRejected(d, "no 'board'");
            d = Minimal(); d.chains = null; AssertRejected(d, "no 'chains'");
            d = Minimal(); d.board.columns = 0; AssertRejected(d, "columns");
            d = Minimal(); d.chains[0].generators[0].tier = 2; AssertRejected(d, "generator tier 2 is outside");
            d = Minimal(); d.chains[0].generators = new[] { d.chains[0].generators[0], d.chains[0].generators[0] }; AssertRejected(d, "defined twice");
            d = Minimal(); d.chains[0].generators[0].rechargeIntervalMs = 0; AssertRejected(d, "recharge interval");
            d = Minimal(); d.chains[0].generators[0].outputs[0].chainId = "ghost"; AssertRejected(d, "not a valid chain/tier");
            d = Minimal(); d.chains[0].tierNames = new[] { "A", "B" }; AssertRejected(d, "tier names");
            d = Minimal(); d.chains[0].color = "red"; AssertRejected(d, "#RRGGBB");
            d = Minimal(); d.chains[1].sellValues = new[] { 1 }; AssertRejected(d, "sell values");
            d = Minimal(); d.startingLayout[0].cell = 39; AssertRejected(d, "is locked");
            d = Minimal(); d.startingLayout[0].cell = 40; AssertRejected(d, "outside the board");
            d = Minimal(); d.startingLayout[0].tier = 9; AssertRejected(d, "not a valid item");
            d = Minimal(); d.startingLayout = new[] { d.startingLayout[0], d.startingLayout[0] }; AssertRejected(d, "used twice");
        }

        [Test]
        public void ContentText_RoundTripsThroughTheSerializer()
        {
            // Guards against field-name typos between the JSON file and MergeContentData.
            MergeContentData data = ContentFile.Parse(ContentFile.ReadText());
            Assert.AreEqual(MergeContentData.SupportedVersion, data.version);
            Assert.IsNotNull(data.board);
            Assert.That(data.chains.Length, Is.GreaterThanOrEqualTo(3));
            Assert.IsNotNull(data.chains[0].generators);
            Assert.That(data.chains[0].generators[0].rechargeIntervalMs, Is.GreaterThan(0));
            Assert.That(data.startingLayout.Length, Is.GreaterThan(0));
        }
    }
}
