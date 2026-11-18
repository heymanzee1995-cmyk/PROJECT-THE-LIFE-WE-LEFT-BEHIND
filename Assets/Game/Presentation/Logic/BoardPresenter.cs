using Game.Data;
using Game.Domain;

namespace Game.Presentation
{
    /// <summary>Where the pointer is during a drag or at release.</summary>
    public enum DropZone
    {
        None = 0,
        Cell,
        Inventory,
        Sell,
    }

    public readonly struct PointerTarget
    {
        public readonly DropZone Zone;
        /// <summary>Board cell for <see cref="DropZone.Cell"/>; otherwise unused.</summary>
        public readonly int Cell;

        public PointerTarget(DropZone zone, int cell)
        {
            Zone = zone;
            Cell = cell;
        }

        public static readonly PointerTarget Nothing = new PointerTarget(DropZone.None, -1);
        public static PointerTarget AtCell(int cell) => new PointerTarget(DropZone.Cell, cell);
        public static readonly PointerTarget Inventory = new PointerTarget(DropZone.Inventory, -1);
        public static readonly PointerTarget Sell = new PointerTarget(DropZone.Sell, -1);
    }

    /// <summary>Highlight to show under a dragged item.</summary>
    public enum HoverKind
    {
        None = 0,
        /// <summary>Over its own origin: dropping returns it.</summary>
        Return,
        Move,
        Merge,
        /// <summary>The drop would be rejected (incompatible item, locked cell, full storage, unsellable).</summary>
        Invalid,
        Store,
        Sell,
    }

    public enum InteractionKind
    {
        None = 0,
        Moved,
        Merged,
        Stored,
        Retrieved,
        Sold,
        /// <summary>Drop rejected: the dragged item returns to where it came from. Nothing changed.</summary>
        Bounced,
        Spawned,
        /// <summary>Tapped generator has no charges; <see cref="InteractionResult.WaitMs"/> until the next.</summary>
        GeneratorEmpty,
        /// <summary>Tapped generator could not spawn because no cell is free.</summary>
        BoardFull,
        /// <summary>Tap on an ordinary item: show its details.</summary>
        Info,
    }

    /// <summary>What the view should show after a drop or tap. All state changes already happened in the engine.</summary>
    public readonly struct InteractionResult
    {
        public readonly InteractionKind Kind;
        public readonly ActionError Error;
        /// <summary>Play the subtle "invalid" shake on the target (and dragged item).</summary>
        public readonly bool InvalidFeedback;
        /// <summary>Cell the feedback refers to (merge/spawn/target), or -1.</summary>
        public readonly int Cell;
        public readonly ItemId Item;
        public readonly int Value;
        public readonly long WaitMs;
        /// <summary>Short player-facing text, or null for none.</summary>
        public readonly string Message;

        public InteractionResult(InteractionKind kind, ActionError error, bool invalidFeedback, int cell, ItemId item, int value,
            long waitMs, string message)
        {
            Kind = kind;
            Error = error;
            InvalidFeedback = invalidFeedback;
            Cell = cell;
            Item = item;
            Value = value;
            WaitMs = waitMs;
            Message = message;
        }

        public static InteractionResult Bounce(ActionError error, bool invalid, int cell, string message) =>
            new InteractionResult(InteractionKind.Bounced, error, invalid, cell, ItemId.None, 0, 0, message);
    }

    /// <summary>Placeholder player-facing text (English). Localisation comes later.</summary>
    public static class Messages
    {
        public const string NoMatch = "Those don't match.";
        public const string Locked = "That space is still locked.";
        public const string StorageFull = "Storage is full.";
        public const string NeedEmptyCell = "Place stored items on an empty space.";
        public const string CannotSellGenerator = "Generators can't be sold.";
        public const string BoardFull = "The board is full. Merge, store or sell something to make room.";
        public const string NextChargePrefix = "Recharging. Next in ";
    }

    /// <summary>
    /// Turns player gestures into engine commands and describes the outcome for the view. Pure C#: the
    /// MonoBehaviours only feed it pointer positions and render the result. Never mutates engine state
    /// except through engine commands, so a refused action changes nothing.
    /// </summary>
    public sealed class BoardPresenter
    {
        private readonly MergeEngine _engine;
        private readonly ItemCatalog _catalog;

        public BoardPresenter(MergeEngine engine, ItemCatalog catalog)
        {
            _engine = engine;
            _catalog = catalog;
        }

        public MergeEngine Engine => _engine;

        /// <summary>Highlight for the current drag position. Allocation-free (called every frame while dragging).</summary>
        public HoverKind Hover(Location from, ItemId id, PointerTarget target)
        {
            Item source = Find(from, id);
            if (source == null)
            {
                return HoverKind.None;
            }

            switch (target.Zone)
            {
                case DropZone.Cell:
                    if (from.IsBoard && target.Cell == from.Index)
                    {
                        return HoverKind.Return;
                    }

                    if (_engine.IsLocked(target.Cell))
                    {
                        return HoverKind.Invalid;
                    }

                    Item occupant = _engine.GetCell(target.Cell);
                    if (occupant == null)
                    {
                        return HoverKind.Move;
                    }

                    return from.IsBoard && _engine.Config.CanMerge(source.Type, occupant.Type) ? HoverKind.Merge : HoverKind.Invalid;

                case DropZone.Inventory:
                    if (from.IsInventory)
                    {
                        return HoverKind.Return;
                    }

                    return _engine.CountFreeSlots() > 0 ? HoverKind.Store : HoverKind.Invalid;

                case DropZone.Sell:
                    return source.IsGenerator ? HoverKind.Invalid : HoverKind.Sell;

                default:
                    return HoverKind.None;
            }
        }

        /// <summary>Complete a drag of item <paramref name="id"/> picked up at <paramref name="from"/>.</summary>
        public InteractionResult Drop(Location from, ItemId id, PointerTarget target)
        {
            switch (target.Zone)
            {
                case DropZone.Cell:
                    return from.IsBoard ? DropBoardOnCell(from.Index, id, target.Cell) : DropSlotOnCell(from.Index, id, target.Cell);
                case DropZone.Inventory:
                    if (from.IsInventory)
                    {
                        return InteractionResult.Bounce(ActionError.SameLocation, false, -1, null);
                    }

                    ItemResult stored = _engine.TryMoveToInventory(from.Index, id);
                    return stored.Success
                        ? new InteractionResult(InteractionKind.Stored, ActionError.None, false, -1, id, 0, 0, null)
                        : InteractionResult.Bounce(stored.Error, stored.Error == ActionError.InventoryFull, -1,
                            stored.Error == ActionError.InventoryFull ? Messages.StorageFull : null);
                case DropZone.Sell:
                    return SellAt(from, id);
                default:
                    return InteractionResult.Bounce(ActionError.None, false, -1, null);
            }
        }

        /// <summary>Tap on the item <paramref name="id"/> at <paramref name="at"/>: use a generator or show item info.</summary>
        public InteractionResult Tap(Location at, ItemId id, long nowMs)
        {
            Item item = Find(at, id);
            if (item == null)
            {
                return new InteractionResult(InteractionKind.None, ActionError.StaleItem, false, -1, id, 0, 0, null);
            }

            if (item.IsGenerator && at.IsBoard)
            {
                SpawnResult spawn = _engine.TryTapGenerator(at.Index, id, nowMs);
                switch (spawn.Error)
                {
                    case ActionError.None:
                        return new InteractionResult(InteractionKind.Spawned, ActionError.None, false, spawn.Cell, spawn.Item, 0, 0, null);
                    case ActionError.BoardFull:
                        return new InteractionResult(InteractionKind.BoardFull, spawn.Error, true, at.Index, id, 0, 0, Messages.BoardFull);
                    case ActionError.GeneratorOnCooldown:
                        return new InteractionResult(InteractionKind.GeneratorEmpty, spawn.Error, true, at.Index, id, 0,
                            spawn.CooldownRemainingMs, Messages.NextChargePrefix + TimeText.Format(spawn.CooldownRemainingMs));
                    default:
                        return new InteractionResult(InteractionKind.None, spawn.Error, false, at.Index, id, 0, 0, null);
                }
            }

            return new InteractionResult(InteractionKind.Info, ActionError.None, false, at.IsBoard ? at.Index : -1, id, 0, 0,
                Describe(item, nowMs));
        }

        /// <summary>One-line description used by the item info text.</summary>
        public string Describe(Item item, long nowMs)
        {
            ChainDefinition chain = _engine.Config.GetChain(item.Type.ChainId);
            string text = _catalog.GetName(item.Type) + "  ·  level " + item.Type.Tier + " of " + chain.MaxTier;
            if (item.IsGenerator)
            {
                return text + "  ·  " + _engine.GetAvailableCharges(item, nowMs) + " charges";
            }

            return text + "  ·  sells for " + chain.GetSellValue(item.Type.Tier);
        }

        private InteractionResult DropBoardOnCell(int fromCell, ItemId id, int toCell)
        {
            if (toCell == fromCell)
            {
                return InteractionResult.Bounce(ActionError.SameLocation, false, toCell, null);
            }

            DropResult r = _engine.TryDrop(fromCell, id, toCell);
            if (r.Success)
            {
                return new InteractionResult(r.Outcome == DropOutcome.Merged ? InteractionKind.Merged : InteractionKind.Moved,
                    ActionError.None, false, toCell, r.ResultItem, 0, 0, null);
            }

            switch (r.Error)
            {
                case ActionError.IncompatibleTarget:
                    return InteractionResult.Bounce(r.Error, true, toCell, Messages.NoMatch);
                case ActionError.CellLocked:
                    return InteractionResult.Bounce(r.Error, true, toCell, Messages.Locked);
                default:
                    return InteractionResult.Bounce(r.Error, false, toCell, null);
            }
        }

        private InteractionResult DropSlotOnCell(int slot, ItemId id, int toCell)
        {
            ItemResult r = _engine.TryMoveFromInventory(slot, id, toCell);
            if (r.Success)
            {
                return new InteractionResult(InteractionKind.Retrieved, ActionError.None, false, toCell, id, 0, 0, null);
            }

            switch (r.Error)
            {
                case ActionError.TargetOccupied:
                    // Approved: no direct merging from inventory. The item returns to its slot.
                    return InteractionResult.Bounce(r.Error, true, toCell, Messages.NeedEmptyCell);
                case ActionError.CellLocked:
                    return InteractionResult.Bounce(r.Error, true, toCell, Messages.Locked);
                default:
                    return InteractionResult.Bounce(r.Error, false, toCell, null);
            }
        }

        private InteractionResult SellAt(Location from, ItemId id)
        {
            Item item = Find(from, id);
            if (item == null)
            {
                return InteractionResult.Bounce(ActionError.StaleItem, false, -1, null);
            }

            if (item.IsGenerator)
            {
                // Presentation guard: generators are kept out of the sell zone so they cannot be trashed by accident.
                return InteractionResult.Bounce(ActionError.None, true, -1, Messages.CannotSellGenerator);
            }

            string name = _catalog.GetName(item.Type);
            ItemResult r = _engine.TrySell(from, id);
            return r.Success
                ? new InteractionResult(InteractionKind.Sold, ActionError.None, false, -1, id, r.Value, 0,
                    "Sold " + name + " for " + r.Value + " coins.")
                : InteractionResult.Bounce(r.Error, false, -1, null);
        }

        private Item Find(Location at, ItemId id)
        {
            Item item = at.IsBoard ? _engine.GetCell(at.Index) : at.IsInventory ? _engine.GetSlot(at.Index) : null;
            return item != null && item.Id == id ? item : null;
        }
    }
}
