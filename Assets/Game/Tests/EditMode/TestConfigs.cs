using System.Collections.Generic;
using System.Text;

namespace Game.Domain.Tests
{
    /// <summary>Board geometry used by test content.</summary>
    public sealed class BoardShape
    {
        /// <summary>Approved default board: 5 columns x 8 rows, bottom row locked.</summary>
        public static readonly BoardShape Default5x8 = new BoardShape("5x8", 5, 8, 17, new[] { 35, 36, 37, 38, 39 });

        /// <summary>Alternative test board: the original 7 x 9, bottom row locked.</summary>
        public static readonly BoardShape Alt7x9 = new BoardShape("7x9", 7, 9, 31, new[] { 56, 57, 58, 59, 60, 61, 62 });

        public BoardShape(string name, int columns, int rows, int generatorCell, int[] lockedCells)
        {
            Name = name;
            Columns = columns;
            Rows = rows;
            GeneratorCell = generatorCell;
            LockedCells = lockedCells;
        }

        public string Name { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int CellCount => Columns * Rows;
        /// <summary>Centre cell, where the default layout places the toolbox generator.</summary>
        public int GeneratorCell { get; }
        public int[] LockedCells { get; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Test content. These values exist only for tests; real content comes from data assets.
    /// The default board is the approved 5x8 (bottom row locked); 7x9 is kept as an alternative.
    /// </summary>
    internal static class TestConfigs
    {
        public const string Toolbox = "toolbox"; // generator chain, 3 tiers
        public const string Crate = "crate";     // single-tier generator, slow
        public const string Flower = "flower";   // 8 tiers, strictly doubling
        public const string Cup = "cup";         // 6 tiers, not doubling

        // Default board geometry (5x8). Use BoardShape.Alt7x9 for the alternative board.
        public const int Columns = 5;
        public const int Rows = 8;
        public const int GeneratorCell = 17;     // column 2, row 3 (centre)
        public const int CrateCell = 0;
        public const long ToolboxCooldownMs = 60000;
        public const int ToolboxCharges = 6;

        public static readonly int[] LockedCells = { 35, 36, 37, 38, 39 };

        public static ItemType T(string chain, int tier) => new ItemType(chain, tier);

        public static EngineConfig Default(int inventoryCapacity = 4, int maxInventoryCapacity = 8, int[] lockedCells = null,
            BoardShape shape = null)
        {
            shape = shape ?? BoardShape.Default5x8;
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
            var board = new BoardConfig(shape.Columns, shape.Rows, inventoryCapacity, maxInventoryCapacity, lockedCells ?? shape.LockedCells);
            return new EngineConfig(board, new[] { toolbox, crate, flower, cup });
        }

        public static List<InitialPlacement> DefaultLayout(BoardShape shape = null) => new List<InitialPlacement>
        {
            new InitialPlacement(T(Toolbox, 1), (shape ?? BoardShape.Default5x8).GeneratorCell),
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
