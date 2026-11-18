using System;

namespace Game.Domain
{
    /// <summary>
    /// Plain, serializer-friendly copy of the full merge engine state. File I/O, atomic writes and
    /// migrations belong to the save system (later milestone); this type is only the data contract.
    /// Public fields keep it compatible with Unity's JsonUtility as well as other serializers.
    /// </summary>
    [Serializable]
    public sealed class EngineSnapshot
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public int Columns;
        public int Rows;
        public bool[] LockedCells = Array.Empty<bool>();
        public int InventoryCapacity;
        /// <summary>Next ID to allocate. Every stored item ID must be lower than this.</summary>
        public long NextItemId = 1;
        /// <summary>SeededRandom state, stored as signed bits so every serializer handles it.</summary>
        public long RngState;
        public ItemRecord[] Items = Array.Empty<ItemRecord>();
        public ItemTypeRecord[] PendingRewards = Array.Empty<ItemTypeRecord>();
    }

    [Serializable]
    public sealed class ItemRecord
    {
        public long Id;
        public string ChainId;
        public int Tier;
        /// <summary>1 = board, 2 = inventory (see <see cref="LocationArea"/>).</summary>
        public int Area;
        public int Index;
        public int GeneratorCharges;
        public long GeneratorRechargeStartMs;
    }

    [Serializable]
    public sealed class ItemTypeRecord
    {
        public string ChainId;
        public int Tier;
    }
}
