using NUnit.Framework;

namespace Game.Domain.Tests
{
    /// <summary>
    /// M1 acceptance: at least 10,000 seeded random actions per seed, every invariant checked after
    /// every step. A failure message prints the seed and step; paste them into <see cref="ReplaySeed"/>.
    /// </summary>
    public class SimulationTests
    {
        private const int Steps = 10000;

        /// <summary>Five seeds on the default 5x8 board, three on the alternative 7x9 board.</summary>
        private static readonly object[] Cases =
        {
            new object[] { 1UL, "5x8" },
            new object[] { 2UL, "5x8" },
            new object[] { 3UL, "5x8" },
            new object[] { 1337UL, "5x8" },
            new object[] { 20261118UL, "5x8" },
            new object[] { 0xDEADBEEFUL, "7x9" },
            new object[] { 987654321UL, "7x9" },
            new object[] { 42UL, "7x9" },
        };

        internal static SimulationStats RunOn(string board, ulong seed, int steps)
        {
            BoardShape shape = board == "7x9" ? BoardShape.Alt7x9 : BoardShape.Default5x8;
            return MergeSimulation.Run(seed, steps, TestConfigs.Default(shape: shape), TestConfigs.DefaultLayout(shape));
        }

        [Test, TestCaseSource(nameof(Cases))]
        public void RandomActions_PreserveAllInvariants(ulong seed, string board)
        {
            SimulationStats stats = RunOn(board, seed, Steps);
            TestContext.WriteLine("seed " + seed + " on " + board + ": " + stats);

            // Guard against a simulation that "passes" by doing nothing interesting.
            Assert.That(stats.Merges, Is.GreaterThan(300), "too few merges exercised");
            Assert.That(stats.Count("tap"), Is.GreaterThan(800), "too few spawns exercised");
            Assert.That(stats.Count("sell"), Is.GreaterThan(100));
            Assert.That(stats.Count("deliver"), Is.GreaterThan(50));
            Assert.That(stats.Count("toInv"), Is.GreaterThan(50));
            Assert.That(stats.Count("fromInv"), Is.GreaterThan(50));
            Assert.That(stats.Count("claimReward"), Is.GreaterThan(50));
            Assert.That(stats.Reloads, Is.GreaterThan(200));
            Assert.That(stats.InterruptedDrags, Is.GreaterThan(100));
        }

        [Test]
        public void SameSeed_IsFullyReproducible()
        {
            SimulationStats a = MergeSimulation.Run(77, 3000);
            SimulationStats b = MergeSimulation.Run(77, 3000);
            Assert.AreEqual(a.ToString(), b.ToString());
        }

        [Test]
        public void TightBoard_HitsBoardFullAndNeverSoftLocks()
        {
            // Small 3x3 board, no inventory: the board fills constantly, exercising board-full handling.
            var config = new EngineConfig(new BoardConfig(3, 3, 0, 2), TestConfigs.Default().Chains);
            var layout = new[] { TestConfigs.At(TestConfigs.Toolbox, 1, 4), TestConfigs.At(TestConfigs.Crate, 1, 0) };
            SimulationStats stats = MergeSimulation.Run(5, Steps, config, layout);
            TestContext.WriteLine(stats.ToString());
            Assert.That(stats.BoardFullRefusals, Is.GreaterThan(50));
        }

        /// <summary>Paste a failing seed/step here to replay locally.</summary>
        [Test, Explicit("Manual replay of a failing simulation seed.")]
        public void ReplaySeed()
        {
            RunOn("5x8", 1UL, Steps);
        }
    }
}
