using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// Pure C# merge board: cells, inventory, items, generators and pending rewards.
    ///
    /// Rules every command follows:
    /// <list type="bullet">
    /// <item>All validation happens before any mutation. A refused command returns an error and leaves state untouched.</item>
    /// <item>Events are raised only after the state change has fully completed.</item>
    /// <item>Item IDs are allocated monotonically and never reused, including across save/load.</item>
    /// <item>Drag is a presentation concern. The engine only sees the final drop, which names the item the
    /// caller expects at the source, so a stale or interrupted drag can never duplicate or lose an item.</item>
    /// </list>
    /// Time is passed in by the caller (milliseconds) so cooldowns are testable and deterministic.
    /// </summary>
    public sealed class MergeEngine
    {
        private readonly EngineConfig _config;
        private readonly SeededRandom _rng;
        private readonly Ledger _ledger;
        private readonly Item[] _cells;
        private readonly bool[] _locked;
        private Item[] _inventory;
        private readonly Dictionary<long, Item> _items = new Dictionary<long, Item>();
        private readonly List<ItemType> _pendingRewards = new List<ItemType>();
        private readonly Dictionary<ItemType, int> _hintScratch = new Dictionary<ItemType, int>();
        private readonly HashSet<long> _deliveryScratch = new HashSet<long>();
        private long _nextId;

        public event Action<Item, CreationReason> ItemCreated;
        public event Action<Item, RemovalReason> ItemRemoved;
        /// <summary>(item, from, to). Raised for moves, both halves of a swap, and inventory transfers.</summary>
        public event Action<Item, Location, Location> ItemMoved;
        /// <summary>(consumed source, consumed target, result). Raised after the removal/creation events.</summary>
        public event Action<ItemId, ItemId, Item> ItemsMerged;
        public event Action<int> CellUnlocked;
        public event Action<int> InventoryCapacityChanged;
        public event Action<ItemType> RewardQueued;

        private MergeEngine(EngineConfig config, ulong rngState, int inventoryCapacity, Ledger ledger)
        {
            _config = config;
            _rng = new SeededRandom(rngState);
            _ledger = ledger;
            _cells = new Item[config.Board.CellCount];
            _locked = new bool[config.Board.CellCount];
            _inventory = new Item[inventoryCapacity];
            _nextId = 1;
        }

        // ------------------------------------------------------------------ construction

        /// <summary>Start a fresh board with a starting layout. Throws on invalid config or layout.</summary>
        public static MergeEngine CreateNew(EngineConfig config, ulong seed, IReadOnlyList<InitialPlacement> layout,
            Ledger ledger = null)
        {
            ConfigValidator.ThrowIfInvalid(config);
            var engine = new MergeEngine(config, seed, config.Board.InventoryCapacity, ledger);
            for (int i = 0; i < config.Board.InitiallyLockedCells.Count; i++)
            {
                engine._locked[config.Board.InitiallyLockedCells[i]] = true;
            }

            var errors = new List<string>();
            if (layout != null)
            {
                for (int i = 0; i < layout.Count; i++)
                {
                    InitialPlacement p = layout[i];
                    if (!engine.IsValidCell(p.Cell))
                    {
                        errors.Add("Layout entry " + i + ": cell " + p.Cell + " is outside the board.");
                    }
                    else if (engine._locked[p.Cell])
                    {
                        errors.Add("Layout entry " + i + ": cell " + p.Cell + " is locked.");
                    }
                    else if (engine._cells[p.Cell] != null)
                    {
                        errors.Add("Layout entry " + i + ": cell " + p.Cell + " is already used.");
                    }
                    else if (!config.IsValidType(p.Type))
                    {
                        errors.Add("Layout entry " + i + ": " + p.Type + " is not a valid item type.");
                    }
                    else
                    {
                        Item item = engine.AllocateItem(p.Type);
                        engine.Attach(item, Location.Cell(p.Cell));
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException("Invalid starting layout:\n - " + string.Join("\n - ", errors), nameof(layout));
            }

            if (ledger != null)
            {
                engine.RecordAllAsLoaded();
            }

            return engine;
        }

        /// <summary>
        /// Rebuild an engine from a snapshot. Returns false (and lists every problem) if the snapshot is
        /// inconsistent with itself or with the config, so the caller can fall back to a backup.
        /// </summary>
        public static bool TryFromSnapshot(EngineConfig config, EngineSnapshot snapshot, out MergeEngine engine,
            out List<string> errors, Ledger ledger = null)
        {
            engine = null;
            errors = ConfigValidator.Validate(config);
            if (errors.Count > 0)
            {
                return false;
            }

            ValidateSnapshot(config, snapshot, errors);
            if (errors.Count > 0)
            {
                return false;
            }

            var result = new MergeEngine(config, unchecked((ulong)snapshot.RngState), snapshot.InventoryCapacity, ledger);
            result._nextId = snapshot.NextItemId;
            for (int i = 0; i < result._locked.Length; i++)
            {
                result._locked[i] = snapshot.LockedCells[i];
            }

            for (int i = 0; i < snapshot.Items.Length; i++)
            {
                ItemRecord r = snapshot.Items[i];
                var type = new ItemType(r.ChainId, r.Tier);
                var item = new Item(new ItemId(r.Id), type, config.GetGenerator(type) != null)
                {
                    GeneratorCharges = r.GeneratorCharges,
                    GeneratorReadyAtMs = r.GeneratorReadyAtMs,
                };
                result.Attach(item, new Location((LocationArea)r.Area, r.Index));
            }

            for (int i = 0; i < snapshot.PendingRewards.Length; i++)
            {
                result._pendingRewards.Add(new ItemType(snapshot.PendingRewards[i].ChainId, snapshot.PendingRewards[i].Tier));
            }

            if (ledger != null)
            {
                result.RecordAllAsLoaded();
            }

            engine = result;
            return true;
        }

        public static MergeEngine FromSnapshot(EngineConfig config, EngineSnapshot snapshot, Ledger ledger = null)
        {
            if (!TryFromSnapshot(config, snapshot, out MergeEngine engine, out List<string> errors, ledger))
            {
                throw new SnapshotLoadException(errors);
            }

            return engine;
        }

        private static void ValidateSnapshot(EngineConfig config, EngineSnapshot s, List<string> errors)
        {
            if (s == null)
            {
                errors.Add("Snapshot is null.");
                return;
            }

            if (s.Version != EngineSnapshot.CurrentVersion)
            {
                errors.Add("Unsupported snapshot version " + s.Version + " (expected " + EngineSnapshot.CurrentVersion + "; migrate first).");
                return;
            }

            BoardConfig board = config.Board;
            if (s.Columns != board.Columns || s.Rows != board.Rows)
            {
                errors.Add("Snapshot board is " + s.Columns + "x" + s.Rows + " but config is " + board.Columns + "x" + board.Rows + ".");
                return;
            }

            if (s.LockedCells == null || s.LockedCells.Length != board.CellCount)
            {
                errors.Add("Snapshot locked-cell table has the wrong length.");
                return;
            }

            if (s.InventoryCapacity < 0 || s.InventoryCapacity > board.MaxInventoryCapacity)
            {
                errors.Add("Snapshot inventory capacity " + s.InventoryCapacity + " is outside 0.." + board.MaxInventoryCapacity + ".");
                return;
            }

            if (s.NextItemId < 1)
            {
                errors.Add("Snapshot next item ID must be >= 1 (was " + s.NextItemId + ").");
            }

            if (s.Items == null || s.PendingRewards == null)
            {
                errors.Add("Snapshot item or reward list is missing.");
                return;
            }

            var ids = new HashSet<long>();
            var usedCells = new HashSet<int>();
            var usedSlots = new HashSet<int>();
            for (int i = 0; i < s.Items.Length; i++)
            {
                ItemRecord r = s.Items[i];
                if (r == null)
                {
                    errors.Add("Item record " + i + " is null.");
                    continue;
                }

                string label = "Item " + r.Id;
                if (r.Id < 1 || r.Id >= s.NextItemId)
                {
                    errors.Add(label + ": ID must be in 1.." + (s.NextItemId - 1) + ".");
                }

                if (!ids.Add(r.Id))
                {
                    errors.Add(label + ": duplicate ID.");
                }

                var type = new ItemType(r.ChainId, r.Tier);
                if (!config.IsValidType(type))
                {
                    errors.Add(label + ": unknown type " + type + ".");
                    continue;
                }

                if (r.Area == (int)LocationArea.Board)
                {
                    if (r.Index < 0 || r.Index >= board.CellCount)
                    {
                        errors.Add(label + ": cell " + r.Index + " is outside the board.");
                    }
                    else if (s.LockedCells[r.Index])
                    {
                        errors.Add(label + ": cell " + r.Index + " is locked.");
                    }
                    else if (!usedCells.Add(r.Index))
                    {
                        errors.Add(label + ": cell " + r.Index + " holds more than one item.");
                    }
                }
                else if (r.Area == (int)LocationArea.Inventory)
                {
                    if (r.Index < 0 || r.Index >= s.InventoryCapacity)
                    {
                        errors.Add(label + ": inventory slot " + r.Index + " is outside capacity " + s.InventoryCapacity + ".");
                    }
                    else if (!usedSlots.Add(r.Index))
                    {
                        errors.Add(label + ": inventory slot " + r.Index + " holds more than one item.");
                    }
                }
                else
                {
                    errors.Add(label + ": invalid area " + r.Area + ".");
                }

                GeneratorDefinition gen = config.GetGenerator(type);
                if (gen == null)
                {
                    if (r.GeneratorCharges != 0 || r.GeneratorReadyAtMs != 0)
                    {
                        errors.Add(label + ": non-generator item has generator state.");
                    }
                }
                else if (r.GeneratorCharges < 0 || r.GeneratorCharges > gen.MaxCharges || r.GeneratorReadyAtMs < 0)
                {
                    errors.Add(label + ": generator state out of range (charges " + r.GeneratorCharges + ", readyAt " + r.GeneratorReadyAtMs + ").");
                }
            }

            for (int i = 0; i < s.PendingRewards.Length; i++)
            {
                ItemTypeRecord r = s.PendingRewards[i];
                if (r == null || !config.IsValidType(new ItemType(r.ChainId, r.Tier)))
                {
                    errors.Add("Pending reward " + i + " is not a valid item type.");
                }
            }
        }

        // ------------------------------------------------------------------ queries

        public EngineConfig Config => _config;
        public int Columns => _config.Board.Columns;
        public int Rows => _config.Board.Rows;
        public int CellCount => _cells.Length;
        public int InventoryCapacity => _inventory.Length;
        public int ItemCount => _items.Count;
        public long NextItemId => _nextId;
        public ulong RngState => _rng.State;
        public int PendingRewardCount => _pendingRewards.Count;

        public bool IsValidCell(int cell) => cell >= 0 && cell < _cells.Length;
        public bool IsValidSlot(int slot) => slot >= 0 && slot < _inventory.Length;
        public int CellIndex(int column, int row) => row * Columns + column;
        public int ColumnOf(int cell) => cell % Columns;
        public int RowOf(int cell) => cell / Columns;

        public Item GetCell(int cell) => IsValidCell(cell) ? _cells[cell] : null;
        public bool IsLocked(int cell) => IsValidCell(cell) && _locked[cell];
        public Item GetSlot(int slot) => IsValidSlot(slot) ? _inventory[slot] : null;
        public ItemType GetPendingReward(int index) => _pendingRewards[index];

        public bool TryGetItem(ItemId id, out Item item) => _items.TryGetValue(id.Value, out item);

        /// <summary>Copies every live item into <paramref name="buffer"/> (cleared first). No other allocation.</summary>
        public void CopyItems(List<Item> buffer)
        {
            buffer.Clear();
            foreach (KeyValuePair<long, Item> pair in _items)
            {
                buffer.Add(pair.Value);
            }
        }

        public int CountEmptyCells()
        {
            int count = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_locked[i] && _cells[i] == null)
                {
                    count++;
                }
            }

            return count;
        }

        public bool IsBoardFull => CountEmptyCells() == 0;

        public int CountFreeSlots()
        {
            int count = 0;
            for (int i = 0; i < _inventory.Length; i++)
            {
                if (_inventory[i] == null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Charges a generator can spend right now, accounting for a finished cooldown.</summary>
        public int GetAvailableCharges(Item generator, long nowMs)
        {
            GeneratorDefinition def = generator == null ? null : _config.GetGenerator(generator.Type);
            if (def == null)
            {
                return 0;
            }

            if (generator.GeneratorCharges == 0 && nowMs >= generator.GeneratorReadyAtMs)
            {
                return def.MaxCharges;
            }

            return generator.GeneratorCharges;
        }

        /// <summary>Ms until a depleted generator refills; 0 if it has charges available.</summary>
        public long GetCooldownRemainingMs(Item generator, long nowMs)
        {
            if (GetAvailableCharges(generator, nowMs) > 0)
            {
                return 0;
            }

            return Math.Max(0, generator.GeneratorReadyAtMs - nowMs);
        }

        /// <summary>Finds a pair of mergeable items on the board, lowest cell indices first. For hints.</summary>
        public bool TryFindMerge(out MergeHint hint)
        {
            _hintScratch.Clear();
            for (int i = 0; i < _cells.Length; i++)
            {
                Item item = _cells[i];
                if (item == null || !_config.CanMerge(item.Type, item.Type))
                {
                    continue;
                }

                if (_hintScratch.TryGetValue(item.Type, out int other))
                {
                    hint = new MergeHint(other, i);
                    _hintScratch.Clear();
                    return true;
                }

                _hintScratch.Add(item.Type, i);
            }

            _hintScratch.Clear();
            hint = default;
            return false;
        }

        /// <summary>
        /// True if the player has at least one way to free a board cell: a merge, a free inventory slot,
        /// or an item to sell. Every item is sellable (value may be 0), so this only fails on an empty board.
        /// Used to prove a full board is never a soft-lock.
        /// </summary>
        public bool HasBoardFullResolution() => TryFindMerge(out _) || HasAnyBoardItem();

        private bool HasAnyBoardItem()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ commands: board

        /// <summary>
        /// Drop the item at <paramref name="fromCell"/> onto <paramref name="toCell"/>: moves into an empty
        /// cell, merges with an identical mergeable item, otherwise swaps. <paramref name="expected"/> must be
        /// the item the player picked up; if it no longer sits there the drop is refused as stale.
        /// </summary>
        public DropResult TryDrop(int fromCell, ItemId expected, int toCell)
        {
            if (!IsValidCell(fromCell) || !IsValidCell(toCell))
            {
                return DropResult.Fail(ActionError.InvalidCell);
            }

            if (fromCell == toCell)
            {
                return DropResult.Fail(ActionError.SameLocation);
            }

            if (_locked[toCell] || _locked[fromCell])
            {
                return DropResult.Fail(ActionError.CellLocked);
            }

            Item source = _cells[fromCell];
            if (source == null)
            {
                return DropResult.Fail(ActionError.SourceEmpty);
            }

            if (source.Id != expected)
            {
                return DropResult.Fail(ActionError.StaleItem);
            }

            Item target = _cells[toCell];
            Location from = Location.Cell(fromCell);
            Location to = Location.Cell(toCell);

            if (target == null)
            {
                _cells[fromCell] = null;
                _cells[toCell] = source;
                source.Location = to;
                ItemMoved?.Invoke(source, from, to);
                return new DropResult(ActionError.None, DropOutcome.Moved, source.Id);
            }

            if (_config.CanMerge(source.Type, target.Type))
            {
                Item result = AllocateItem(source.Type.NextTier);
                Detach(source);
                Detach(target);
                Attach(result, to);
                _ledger?.RecordMerge(source.Id, source.Type, target.Id, target.Type, result.Id, result.Type);

                ItemRemoved?.Invoke(source, RemovalReason.Merge);
                ItemRemoved?.Invoke(target, RemovalReason.Merge);
                ItemCreated?.Invoke(result, CreationReason.Merge);
                ItemsMerged?.Invoke(source.Id, target.Id, result);
                return new DropResult(ActionError.None, DropOutcome.Merged, result.Id);
            }

            _cells[fromCell] = target;
            _cells[toCell] = source;
            source.Location = to;
            target.Location = from;
            ItemMoved?.Invoke(source, from, to);
            ItemMoved?.Invoke(target, to, from);
            return new DropResult(ActionError.None, DropOutcome.Swapped, source.Id);
        }

        /// <summary>
        /// Tap a generator on the board. Spends one charge and spawns a rolled item into the nearest empty
        /// unlocked cell. Refused, with no charge spent and no RNG consumed, if the board is full or the
        /// generator is cooling down.
        /// </summary>
        public SpawnResult TryTapGenerator(int cell, ItemId expected, long nowMs)
        {
            if (!IsValidCell(cell))
            {
                return SpawnResult.Fail(ActionError.InvalidCell);
            }

            Item generator = _cells[cell];
            if (generator == null)
            {
                return SpawnResult.Fail(ActionError.SourceEmpty);
            }

            if (generator.Id != expected)
            {
                return SpawnResult.Fail(ActionError.StaleItem);
            }

            GeneratorDefinition def = _config.GetGenerator(generator.Type);
            if (def == null)
            {
                return SpawnResult.Fail(ActionError.NotAGenerator);
            }

            int charges = GetAvailableCharges(generator, nowMs);
            if (charges <= 0)
            {
                return SpawnResult.Fail(ActionError.GeneratorOnCooldown, GetCooldownRemainingMs(generator, nowMs));
            }

            int targetCell = FindNearestEmptyCell(cell);
            if (targetCell < 0)
            {
                return SpawnResult.Fail(ActionError.BoardFull);
            }

            ItemType rolled = def.Roll(_rng);
            Item spawned = AllocateItem(rolled);
            Attach(spawned, Location.Cell(targetCell));
            generator.GeneratorCharges = charges - 1;
            generator.GeneratorReadyAtMs = generator.GeneratorCharges == 0 ? nowMs + def.CooldownMs : 0;
            _ledger?.RecordCreated(CreationReason.Spawn, spawned.Id, spawned.Type);

            ItemCreated?.Invoke(spawned, CreationReason.Spawn);
            return new SpawnResult(ActionError.None, spawned.Id, targetCell, 0);
        }

        public ItemResult TryUnlockCell(int cell)
        {
            if (!IsValidCell(cell))
            {
                return ItemResult.Fail(ActionError.InvalidCell);
            }

            if (!_locked[cell])
            {
                return ItemResult.Fail(ActionError.CellNotLocked);
            }

            _locked[cell] = false;
            CellUnlocked?.Invoke(cell);
            return new ItemResult(ActionError.None, ItemId.None, Location.Cell(cell), 0);
        }

        // ------------------------------------------------------------------ commands: inventory

        /// <summary>Move a board item into the first free inventory slot.</summary>
        public ItemResult TryMoveToInventory(int cell, ItemId expected)
        {
            if (!IsValidCell(cell))
            {
                return ItemResult.Fail(ActionError.InvalidCell);
            }

            Item item = _cells[cell];
            if (item == null)
            {
                return ItemResult.Fail(ActionError.SourceEmpty);
            }

            if (item.Id != expected)
            {
                return ItemResult.Fail(ActionError.StaleItem);
            }

            int slot = FindFreeSlot();
            if (slot < 0)
            {
                return ItemResult.Fail(ActionError.InventoryFull);
            }

            Location from = item.Location;
            Location to = Location.Slot(slot);
            _cells[cell] = null;
            _inventory[slot] = item;
            item.Location = to;
            ItemMoved?.Invoke(item, from, to);
            return new ItemResult(ActionError.None, item.Id, to, 0);
        }

        /// <summary>Move an inventory item onto an empty, unlocked board cell.</summary>
        public ItemResult TryMoveFromInventory(int slot, ItemId expected, int cell)
        {
            if (!IsValidSlot(slot))
            {
                return ItemResult.Fail(ActionError.InvalidSlot);
            }

            if (!IsValidCell(cell))
            {
                return ItemResult.Fail(ActionError.InvalidCell);
            }

            Item item = _inventory[slot];
            if (item == null)
            {
                return ItemResult.Fail(ActionError.SourceEmpty);
            }

            if (item.Id != expected)
            {
                return ItemResult.Fail(ActionError.StaleItem);
            }

            if (_locked[cell])
            {
                return ItemResult.Fail(ActionError.CellLocked);
            }

            if (_cells[cell] != null)
            {
                return ItemResult.Fail(ActionError.TargetOccupied);
            }

            Location from = item.Location;
            Location to = Location.Cell(cell);
            _inventory[slot] = null;
            _cells[cell] = item;
            item.Location = to;
            ItemMoved?.Invoke(item, from, to);
            return new ItemResult(ActionError.None, item.Id, to, 0);
        }

        /// <summary>Expansion hook: grow inventory to <paramref name="newCapacity"/> (must grow, up to the configured max).</summary>
        public ItemResult TryExpandInventory(int newCapacity)
        {
            if (newCapacity <= _inventory.Length || newCapacity > _config.Board.MaxInventoryCapacity)
            {
                return ItemResult.Fail(ActionError.InvalidCapacity);
            }

            var grown = new Item[newCapacity];
            Array.Copy(_inventory, grown, _inventory.Length);
            _inventory = grown;
            InventoryCapacityChanged?.Invoke(newCapacity);
            return ItemResult.Ok();
        }

        // ------------------------------------------------------------------ commands: removal

        /// <summary>
        /// Sell (or trash, when the configured value is 0) the item at <paramref name="location"/>.
        /// Returns the coin value; crediting it is the economy layer's responsibility.
        /// </summary>
        public ItemResult TrySell(Location location, ItemId expected)
        {
            Item item;
            if (location.IsBoard)
            {
                if (!IsValidCell(location.Index))
                {
                    return ItemResult.Fail(ActionError.InvalidCell);
                }

                item = _cells[location.Index];
            }
            else if (location.IsInventory)
            {
                if (!IsValidSlot(location.Index))
                {
                    return ItemResult.Fail(ActionError.InvalidSlot);
                }

                item = _inventory[location.Index];
            }
            else
            {
                return ItemResult.Fail(ActionError.InvalidCell);
            }

            if (item == null)
            {
                return ItemResult.Fail(ActionError.SourceEmpty);
            }

            if (item.Id != expected)
            {
                return ItemResult.Fail(ActionError.StaleItem);
            }

            int value = _config.GetSellValue(item.Type);
            Detach(item);
            _ledger?.RecordRemoved(RemovalReason.Sell, item.Id, item.Type);
            ItemRemoved?.Invoke(item, RemovalReason.Sell);
            return new ItemResult(ActionError.None, item.Id, location, value);
        }

        /// <summary>
        /// Remove a set of board items as one atomic delivery (all or none). Checking that they satisfy an
        /// order is the order system's job; this only guarantees the removal is consistent.
        /// </summary>
        public ItemResult TryDeliver(IReadOnlyList<ItemId> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return ItemResult.Fail(ActionError.NothingToDeliver);
            }

            _deliveryScratch.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                if (!_items.TryGetValue(ids[i].Value, out Item item))
                {
                    _deliveryScratch.Clear();
                    return ItemResult.Fail(ActionError.UnknownItem);
                }

                if (!item.Location.IsBoard)
                {
                    _deliveryScratch.Clear();
                    return ItemResult.Fail(ActionError.ItemNotOnBoard);
                }

                if (!_deliveryScratch.Add(ids[i].Value))
                {
                    _deliveryScratch.Clear();
                    return ItemResult.Fail(ActionError.DuplicateItem);
                }
            }

            _deliveryScratch.Clear();
            var removed = new Item[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                removed[i] = _items[ids[i].Value];
                Detach(removed[i]);
                _ledger?.RecordRemoved(RemovalReason.Delivery, removed[i].Id, removed[i].Type);
            }

            for (int i = 0; i < removed.Length; i++)
            {
                ItemRemoved?.Invoke(removed[i], RemovalReason.Delivery);
            }

            return ItemResult.Ok();
        }

        // ------------------------------------------------------------------ commands: rewards

        /// <summary>
        /// Queue a reward item. Rewards wait in a queue (no ID yet) until there is room on the board,
        /// so a reward is never lost because the board happened to be full.
        /// </summary>
        public ActionError EnqueueReward(ItemType type)
        {
            if (!_config.IsValidType(type))
            {
                return ActionError.InvalidItemType;
            }

            _pendingRewards.Add(type);
            RewardQueued?.Invoke(type);
            return ActionError.None;
        }

        /// <summary>Place the oldest pending reward into the first empty unlocked cell (row-major).</summary>
        public ItemResult TryClaimReward()
        {
            if (_pendingRewards.Count == 0)
            {
                return ItemResult.Fail(ActionError.NoPendingReward);
            }

            int cell = FindFirstEmptyCell();
            if (cell < 0)
            {
                return ItemResult.Fail(ActionError.BoardFull);
            }

            ItemType type = _pendingRewards[0];
            _pendingRewards.RemoveAt(0);
            Item item = AllocateItem(type);
            Attach(item, Location.Cell(cell));
            _ledger?.RecordCreated(CreationReason.Reward, item.Id, item.Type);
            ItemCreated?.Invoke(item, CreationReason.Reward);
            return new ItemResult(ActionError.None, item.Id, item.Location, 0);
        }

        // ------------------------------------------------------------------ snapshot

        public EngineSnapshot CreateSnapshot()
        {
            var snapshot = new EngineSnapshot
            {
                Version = EngineSnapshot.CurrentVersion,
                Columns = Columns,
                Rows = Rows,
                LockedCells = (bool[])_locked.Clone(),
                InventoryCapacity = _inventory.Length,
                NextItemId = _nextId,
                RngState = unchecked((long)_rng.State),
                Items = new ItemRecord[_items.Count],
                PendingRewards = new ItemTypeRecord[_pendingRewards.Count],
            };

            // Deterministic order: board cells, then inventory slots.
            int n = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != null)
                {
                    snapshot.Items[n++] = ToRecord(_cells[i]);
                }
            }

            for (int i = 0; i < _inventory.Length; i++)
            {
                if (_inventory[i] != null)
                {
                    snapshot.Items[n++] = ToRecord(_inventory[i]);
                }
            }

            for (int i = 0; i < _pendingRewards.Count; i++)
            {
                snapshot.PendingRewards[i] = new ItemTypeRecord { ChainId = _pendingRewards[i].ChainId, Tier = _pendingRewards[i].Tier };
            }

            return snapshot;
        }

        private static ItemRecord ToRecord(Item item) => new ItemRecord
        {
            Id = item.Id.Value,
            ChainId = item.Type.ChainId,
            Tier = item.Type.Tier,
            Area = (int)item.Location.Area,
            Index = item.Location.Index,
            GeneratorCharges = item.GeneratorCharges,
            GeneratorReadyAtMs = item.GeneratorReadyAtMs,
        };

        // ------------------------------------------------------------------ internals

        private Item AllocateItem(ItemType type)
        {
            GeneratorDefinition def = _config.GetGenerator(type);
            var item = new Item(new ItemId(_nextId++), type, def != null);
            if (def != null)
            {
                item.GeneratorCharges = def.MaxCharges;
                item.GeneratorReadyAtMs = 0;
            }

            return item;
        }

        private void Attach(Item item, Location location)
        {
            _items.Add(item.Id.Value, item);
            if (location.IsBoard)
            {
                _cells[location.Index] = item;
            }
            else
            {
                _inventory[location.Index] = item;
            }

            item.Location = location;
        }

        private void Detach(Item item)
        {
            if (item.Location.IsBoard)
            {
                _cells[item.Location.Index] = null;
            }
            else if (item.Location.IsInventory)
            {
                _inventory[item.Location.Index] = null;
            }

            _items.Remove(item.Id.Value);
            item.Location = Location.Nowhere;
        }

        private void RecordAllAsLoaded()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != null)
                {
                    _ledger.RecordCreated(CreationReason.Load, _cells[i].Id, _cells[i].Type);
                }
            }

            for (int i = 0; i < _inventory.Length; i++)
            {
                if (_inventory[i] != null)
                {
                    _ledger.RecordCreated(CreationReason.Load, _inventory[i].Id, _inventory[i].Type);
                }
            }
        }

        /// <summary>Nearest empty unlocked cell by Manhattan distance; ties go to the lowest index.</summary>
        private int FindNearestEmptyCell(int origin)
        {
            int ox = ColumnOf(origin), oy = RowOf(origin);
            int best = -1, bestDistance = int.MaxValue;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_locked[i] || _cells[i] != null)
                {
                    continue;
                }

                int d = Math.Abs(ColumnOf(i) - ox) + Math.Abs(RowOf(i) - oy);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        private int FindFirstEmptyCell()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_locked[i] && _cells[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _inventory.Length; i++)
            {
                if (_inventory[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
