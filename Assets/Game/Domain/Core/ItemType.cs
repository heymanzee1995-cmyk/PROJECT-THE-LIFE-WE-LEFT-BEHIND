using System;

namespace Game.Domain
{
    /// <summary>What an item is: a tier within a chain. Two items merge only if their types are equal.</summary>
    public readonly struct ItemType : IEquatable<ItemType>
    {
        public readonly string ChainId;
        public readonly int Tier;

        public ItemType(string chainId, int tier)
        {
            ChainId = chainId;
            Tier = tier;
        }

        public ItemType NextTier => new ItemType(ChainId, Tier + 1);

        public bool Equals(ItemType other) => Tier == other.Tier && string.Equals(ChainId, other.ChainId, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ItemType other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ChainId == null ? 0 : StringComparer.Ordinal.GetHashCode(ChainId), Tier);
        public override string ToString() => ChainId + ":" + Tier;

        public static bool operator ==(ItemType a, ItemType b) => a.Equals(b);
        public static bool operator !=(ItemType a, ItemType b) => !a.Equals(b);
    }
}
