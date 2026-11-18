using System;

namespace Game.Domain
{
    /// <summary>
    /// Unique identity of one item instance. IDs are allocated monotonically by the engine,
    /// persisted with the save, and never reused. <see cref="None"/> (0) is never a valid item.
    /// </summary>
    public readonly struct ItemId : IEquatable<ItemId>, IComparable<ItemId>
    {
        public static readonly ItemId None = new ItemId(0);

        public readonly long Value;

        public ItemId(long value)
        {
            Value = value;
        }

        public bool IsNone => Value == 0;

        public bool Equals(ItemId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ItemId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(ItemId other) => Value.CompareTo(other.Value);
        public override string ToString() => "#" + Value;

        public static bool operator ==(ItemId a, ItemId b) => a.Value == b.Value;
        public static bool operator !=(ItemId a, ItemId b) => a.Value != b.Value;
    }
}
