using System;

namespace Game.Domain
{
    public enum LocationArea
    {
        None = 0,
        Board = 1,
        Inventory = 2,
    }

    /// <summary>Where an item lives: a board cell index (row-major) or an inventory slot index.</summary>
    public readonly struct Location : IEquatable<Location>
    {
        public static readonly Location Nowhere = new Location(LocationArea.None, -1);

        public readonly LocationArea Area;
        public readonly int Index;

        public Location(LocationArea area, int index)
        {
            Area = area;
            Index = index;
        }

        public static Location Cell(int index) => new Location(LocationArea.Board, index);
        public static Location Slot(int index) => new Location(LocationArea.Inventory, index);

        public bool IsBoard => Area == LocationArea.Board;
        public bool IsInventory => Area == LocationArea.Inventory;

        public bool Equals(Location other) => Area == other.Area && Index == other.Index;
        public override bool Equals(object obj) => obj is Location other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Area, Index);
        public override string ToString() => Area + "[" + Index + "]";

        public static bool operator ==(Location a, Location b) => a.Equals(b);
        public static bool operator !=(Location a, Location b) => !a.Equals(b);
    }
}
