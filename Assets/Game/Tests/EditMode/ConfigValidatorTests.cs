using System.Collections.Generic;
using NUnit.Framework;
using static Game.Domain.Tests.TestConfigs;

namespace Game.Domain.Tests
{
    public class ConfigValidatorTests
    {
        private static BoardConfig Board() => new BoardConfig(7, 9, 4, 8);

        private static ChainDefinition Plain(string id = "a", int maxTier = 3) => new ChainDefinition(id, maxTier, Values(maxTier));

        private static int[] Values(int n)
        {
            var v = new int[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = i + 1;
            }

            return v;
        }

        private static List<string> Errors(BoardConfig board, params ChainDefinition[] chains) =>
            ConfigValidator.Validate(new EngineConfig(board, chains));

        private static void AssertError(List<string> errors, string fragment)
        {
            Assert.That(errors.Exists(e => e.Contains(fragment)), "expected an error containing '" + fragment + "' but got:\n" + string.Join("\n", errors));
        }

        [Test]
        public void DefaultTestConfig_IsValid()
        {
            CollectionAssert.IsEmpty(ConfigValidator.Validate(Default()));
        }

        [Test]
        public void RejectsBadBoardDimensions()
        {
            AssertError(Errors(new BoardConfig(0, 9, 4, 8), Plain()), "columns");
            AssertError(Errors(new BoardConfig(7, 99, 4, 8), Plain()), "rows");
        }

        [Test]
        public void RejectsBadInventoryCapacity()
        {
            AssertError(Errors(new BoardConfig(7, 9, -1, 8), Plain()), "Inventory capacity");
            AssertError(Errors(new BoardConfig(7, 9, 6, 4), Plain()), "Max inventory capacity");
        }

        [Test]
        public void RejectsBadLockedCells()
        {
            AssertError(Errors(new BoardConfig(2, 1, 0, 0, new[] { 5 }), Plain()), "outside the board");
            AssertError(Errors(new BoardConfig(2, 1, 0, 0, new[] { 0, 0 }), Plain()), "listed twice");
            AssertError(Errors(new BoardConfig(2, 1, 0, 0, new[] { 0, 1 }), Plain()), "start unlocked");
        }

        [Test]
        public void RejectsBadChains()
        {
            AssertError(Errors(Board()), "At least one item chain");
            AssertError(Errors(Board(), Plain("a"), Plain("a")), "more than once");
            AssertError(Errors(Board(), Plain("")), "empty id");
            AssertError(Errors(Board(), new ChainDefinition("a", 0, new int[0])), "max tier");
            AssertError(Errors(Board(), new ChainDefinition("a", 3, new[] { 1, 2 })), "sell values");
            AssertError(Errors(Board(), new ChainDefinition("a", 2, new[] { 1, -1 })), ">= 0");
            AssertError(Errors(Board(), new ChainDefinition("a", 31, Values(31), null, true)), "doubling");
        }

        [Test]
        public void RejectsBadGenerators()
        {
            var target = Plain("item");
            AssertError(Errors(Board(), target, Gen(new GeneratorDefinition(0, 10, new[] { new WeightedOutput(T("item", 1), 1) }))), "max charges");
            AssertError(Errors(Board(), target, Gen(new GeneratorDefinition(1, -5, new[] { new WeightedOutput(T("item", 1), 1) }))), "cooldown");
            AssertError(Errors(Board(), target, Gen(new GeneratorDefinition(1, 10, new WeightedOutput[0]))), "output table is empty");
            AssertError(Errors(Board(), target, Gen(new GeneratorDefinition(1, 10, new[] { new WeightedOutput(T("item", 1), 0) }))), "positive weight");
            AssertError(Errors(Board(), target, Gen(new GeneratorDefinition(1, 10, new[] { new WeightedOutput(T("nope", 1), 1) }))), "not a valid chain/tier");
            AssertError(Errors(Board(), target, Gen(new GeneratorDefinition(1, 10, new[] { new WeightedOutput(T("item", 9), 1) }))), "not a valid chain/tier");
            AssertError(Errors(Board(), target, new ChainDefinition("g", 2, new[] { 0, 0 }, new GeneratorDefinition[1])), "generator tier table");
        }

        private static ChainDefinition Gen(GeneratorDefinition def) => new ChainDefinition("gen", 1, new[] { 0 }, new[] { def });

        [Test]
        public void EngineRefusesInvalidConfig()
        {
            var bad = new EngineConfig(new BoardConfig(0, 0, 0, 0), new ChainDefinition[0]);
            Assert.Throws<InvalidConfigException>(() => MergeEngine.CreateNew(bad, 1, null));
        }

        [Test]
        public void EngineRefusesInvalidLayout()
        {
            var config = Default();
            Assert.Throws<System.ArgumentException>(() => MergeEngine.CreateNew(config, 1, new[] { At(Flower, 1, 999) }));
            Assert.Throws<System.ArgumentException>(() => MergeEngine.CreateNew(config, 1, new[] { At(Flower, 1, LockedCells[0]) }));
            Assert.Throws<System.ArgumentException>(() => MergeEngine.CreateNew(config, 1, new[] { At(Flower, 1, 3), At(Cup, 1, 3) }));
            Assert.Throws<System.ArgumentException>(() => MergeEngine.CreateNew(config, 1, new[] { At(Flower, 99, 3) }));
        }

        [Test]
        public void GeneratorOdds_AreExactAndSumToOne()
        {
            GeneratorDefinition gen = Default().GetGenerator(T(Toolbox, 2));
            List<OutputOdds> odds = gen.GetOdds();
            Assert.AreEqual(3, odds.Count);
            Assert.AreEqual(0.5, odds[0].Probability, 1e-12);
            Assert.AreEqual(0.2, odds[1].Probability, 1e-12);
            Assert.AreEqual(0.3, odds[2].Probability, 1e-12);
        }
    }
}
