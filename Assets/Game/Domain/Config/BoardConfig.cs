using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>Board and storage dimensions. Values come from content data; nothing here has a default.</summary>
    public sealed class BoardConfig
    {
        private readonly int[] _initiallyLockedCells;

        public BoardConfig(int columns, int rows, int inventoryCapacity, int maxInventoryCapacity,
            IReadOnlyList<int> initiallyLockedCells = null)
        {
            Columns = columns;
            Rows = rows;
            InventoryCapacity = inventoryCapacity;
            MaxInventoryCapacity = maxInventoryCapacity;
            if (initiallyLockedCells == null)
            {
                _initiallyLockedCells = Array.Empty<int>();
            }
            else
            {
                _initiallyLockedCells = new int[initiallyLockedCells.Count];
                for (int i = 0; i < _initiallyLockedCells.Length; i++)
                {
                    _initiallyLockedCells[i] = initiallyLockedCells[i];
                }
            }
        }

        public int Columns { get; }
        public int Rows { get; }
        public int CellCount => Columns * Rows;

        /// <summary>Starting number of inventory slots.</summary>
        public int InventoryCapacity { get; }

        /// <summary>Upper bound reachable via inventory expansion.</summary>
        public int MaxInventoryCapacity { get; }

        public IReadOnlyList<int> InitiallyLockedCells => _initiallyLockedCells;
    }
}
