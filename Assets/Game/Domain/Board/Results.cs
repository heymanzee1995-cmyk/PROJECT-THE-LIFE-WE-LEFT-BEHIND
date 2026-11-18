using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>Why a command was refused. Every refused command leaves engine state unchanged.</summary>
    public enum ActionError
    {
        None = 0,
        InvalidCell,
        InvalidSlot,
        CellLocked,
        CellNotLocked,
        SourceEmpty,
        /// <summary>The item at the source is not the one the caller expected (stale drag / stale UI).</summary>
        StaleItem,
        SameLocation,
        TargetOccupied,
        /// <summary>Dropped onto an item it cannot merge with. Nothing changes; the item returns to its origin.</summary>
        IncompatibleTarget,
        NotAGenerator,
        GeneratorNotOnBoard,
        GeneratorOnCooldown,
        BoardFull,
        InventoryFull,
        InvalidItemType,
        UnknownItem,
        ItemNotOnBoard,
        DuplicateItem,
        NothingToDeliver,
        NoPendingReward,
        InvalidCapacity,
    }

    public enum DropOutcome
    {
        None = 0,
        Moved,
        Merged,
    }

    public readonly struct DropResult
    {
        public readonly ActionError Error;
        public readonly DropOutcome Outcome;
        /// <summary>For merges: the newly created item. Otherwise the moved item.</summary>
        public readonly ItemId ResultItem;

        public DropResult(ActionError error, DropOutcome outcome, ItemId resultItem)
        {
            Error = error;
            Outcome = outcome;
            ResultItem = resultItem;
        }

        public bool Success => Error == ActionError.None;
        public static DropResult Fail(ActionError error) => new DropResult(error, DropOutcome.None, ItemId.None);
    }

    public readonly struct SpawnResult
    {
        public readonly ActionError Error;
        public readonly ItemId Item;
        public readonly int Cell;
        /// <summary>When refused with <see cref="ActionError.GeneratorOnCooldown"/> (no charges): ms until the next charge.</summary>
        public readonly long CooldownRemainingMs;

        public SpawnResult(ActionError error, ItemId item, int cell, long cooldownRemainingMs)
        {
            Error = error;
            Item = item;
            Cell = cell;
            CooldownRemainingMs = cooldownRemainingMs;
        }

        public bool Success => Error == ActionError.None;
        public static SpawnResult Fail(ActionError error, long cooldownRemainingMs = 0) => new SpawnResult(error, ItemId.None, -1, cooldownRemainingMs);
    }

    /// <summary>Result of an inventory move, sale, reward claim or unlock.</summary>
    public readonly struct ItemResult
    {
        public readonly ActionError Error;
        public readonly ItemId Item;
        public readonly Location Location;
        /// <summary>For sales: coins the item was worth. Crediting a wallet is the economy layer's job.</summary>
        public readonly int Value;

        public ItemResult(ActionError error, ItemId item, Location location, int value)
        {
            Error = error;
            Item = item;
            Location = location;
            Value = value;
        }

        public bool Success => Error == ActionError.None;
        public static ItemResult Fail(ActionError error) => new ItemResult(error, ItemId.None, Location.Nowhere, 0);
        public static ItemResult Ok() => new ItemResult(ActionError.None, ItemId.None, Location.Nowhere, 0);
    }

    /// <summary>Why an item came into existence. Mirrors the ledger provenance rules.</summary>
    public enum CreationReason
    {
        Load = 0,
        Spawn,
        Merge,
        Reward,
    }

    /// <summary>Why an item stopped existing. Mirrors the ledger provenance rules.</summary>
    public enum RemovalReason
    {
        Merge = 0,
        Sell,
        Delivery,
    }

    /// <summary>A merge that is currently possible on the board (for hints).</summary>
    public readonly struct MergeHint
    {
        public readonly int CellA;
        public readonly int CellB;

        public MergeHint(int cellA, int cellB)
        {
            CellA = cellA;
            CellB = cellB;
        }
    }

    /// <summary>A starting-layout entry: an item of <see cref="Type"/> placed at board cell <see cref="Cell"/>.</summary>
    public readonly struct InitialPlacement
    {
        public readonly ItemType Type;
        public readonly int Cell;

        public InitialPlacement(ItemType type, int cell)
        {
            Type = type;
            Cell = cell;
        }
    }

    public sealed class SnapshotLoadException : System.Exception
    {
        public SnapshotLoadException(IReadOnlyList<string> errors)
            : base("Snapshot rejected:\n - " + string.Join("\n - ", errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }
}
