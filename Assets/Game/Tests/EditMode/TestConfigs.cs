using System.Collections.Generic;
using System.Text;

namespace Game.Domain.Tests
{
    /// <summary>
    /// Test content. These values exist only for tests; real content comes from data assets.
    /// Board is the spec default 7x9 with the bottom row locked.
    /// </summary>
    internal static class TestConfigs
    {
        public const string Toolbox = "toolbox"; // generator chain, 3 tiers
        public const string Crate = "crate";     // single-tier generator, slow
        public const string Flower = "flower";   // 8 tiers, strictly doubling
        public const string Cup = "cup";         // 6 tiers, not doubling

        public const int Columns = 7;
        public const int Rows = 9;
        public const int GeneratorCell = 31;     // column 3, row 4 (centre)
        public const int CrateCell = 0;
        public const long ToolboxCooldownMs = 60000;
        public const int ToolboxCharges = 6;

        public static readonly int[] LockedCells = { 56, 57, 58, 59, 60, 61, 62 };

        public static ItemType T(string chain, int tier) => new ItemType(chain, tier);

        public static EngineConfig Default(int inventoryCapacity = 4, int maxInventoryCapacity = 8, int[] lockedCells = null)
        {
            var toolbox = new ChainDefinition(Toolbox, 3, new[] { 5, 10, 20 }, new[]
            {
                new GeneratorDefinition(ToolboxCharges, ToolboxCooldownMs, new[]
                {
                    new WeightedOutput(T(Flower, 1), 60),
                    new WeightedOutput(T(Cup, 1), 40),
                }),
                new GeneratorDefinition(10, 45000, new[]
                {
                    new WeightedOutput(T(Flower, 1), 50),
                    new WeightedOutput(T(Flower, 2), 20),
                    new WeightedOutput(T(Cup, 1), 30),
                }),
                new GeneratorDefinition(14, 30000, new[]
                {
                    new WeightedOutput(T(Flower, 2), 50),
                    new WeightedOutput(T(Cup, 2), 50),
                }),
            });
            var crate = new ChainDefinition(Crate, 1, new[] { 0 }, new[]
            {
                new GeneratorDefinition(3, 120000, new[] { new WeightedOutput(T(Cup, 1), 1) }),
            });
            var flower = new ChainDefinition(Flower, 8, new[] { 1, 2, 4, 8, 16, 32, 64, 128 }, null, strictlyDoubling: true);
            var cup = new ChainDefinition(Cup, 6, new[] { 1, 2, 3, 5, 8, 13 });
            var board = new BoardConfig(Columns, Rows, inventoryCapacity, maxInventoryCapacity, lockedCells ?? LockedCells);
            return new EngineConfig(board, new[] { toolbox, crate, flower, cup });
        }

        public static List<InitialPlacement> DefaultLayout() => new List<InitialPlacement>
        {
            new InitialPlacement(T(Toolbox, 1), GeneratorCell),
            new InitialPlacement(T(Crate, 1), CrateCell),
        };

        public static MergeEngine NewEngine(out Ledger ledger, ulong seed = 12345, IReadOnlyList<InitialPlacement> layout = null,
            EngineConfig config = null)
        {
            ledger = new Ledger();
            return MergeEngine.CreateNew(config ?? Default(), seed, layout ?? DefaultLayout(), ledger);
        }

        /// <summary>Canonical text of a snapshot; equal text means equal state.</summary>
        public static string Canon(EngineSnapshot s)
        {
            var sb = new StringBuilder(1024);
            sb.Append('v').Append(s.Version).Append(' ').Append(s.Columns).Append('x').Append(s.Rows)
              .Append(" inv=").Append(s.InventoryCapacity).Append(" next=").Append(s.NextItemId)
              .Append(" rng=").Append(s.RngState).Append(" locked=");
            for (int i = 0; i < s.LockedCells.Length; i++)
            {
                sb.Append(s.LockedCells[i] ? '1' : '0');
            }

            sb.Append(" items=");
            for (int i = 0; i < s.Items.Length; i++)
            {
                ItemRecord r = s.Items[i];
                sb.Append(r.Id).Append(':').Append(r.ChainId).Append(':').Append(r.Tier).Append('@').Append(r.Area).Append('/')
                  .Append(r.Index).Append('g').Append(r.GeneratorCharges).Append('/').Append(r.GeneratorReadyAtMs).Append(';');
            }

            sb.Append(" rewards=");
            for (int i = 0; i < s.PendingRewards.Length; i++)
            {
                sb.Append(s.PendingRewards[i].ChainId).Append(':').Append(s.PendingRewards[i].Tier).Append(';');
            }

            return sb.ToString();
        }

        public static string Canon(MergeEngine engine) => Canon(engine.CreateSnapshot());

        /// <summary>Place an item of <paramref name="type"/> at a cell by building a custom layout engine.</summary>
        public static MergeEngine EngineWith(out Ledger ledger, params InitialPlacement[] layout) =>
            NewEngine(out ledger, 12345, layout);

        public static InitialPlacement At(string chain, int tier, int cell) => new InitialPlacement(T(chain, tier), cell);
    }
}
