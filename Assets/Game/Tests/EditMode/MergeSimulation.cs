using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Domain.Tests
{
    /// <summary>Thrown when the simulation finds a broken invariant. The message contains everything needed to replay.</summary>
    public sealed class SimulationFailure : Exception
    {
        public SimulationFailure(string message) : base(message)
        {
        }
    }

    public sealed class SimulationStats
    {
        public readonly Dictionary<string, int> Succeeded = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Refused = new Dictionary<string, int>();
        public int Merges;
        public int BoardFullRefusals;
        public int Reloads;
        public int InterruptedDrags;
        public int MaxItemsSeen;
        public long FinalNextId;

        public int Count(string action) => Succeeded.TryGetValue(action, out int n) ? n : 0;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("merges=").Append(Merges).Append(" boardFull=").Append(BoardFullRefusals).Append(" reloads=").Append(Reloads)
              .Append(" interruptedDrags=").Append(InterruptedDrags).Append(" maxItems=").Append(MaxItemsSeen)
              .Append(" nextId=").Append(FinalNextId).Append('\n');
            foreach (KeyValuePair<string, int> pair in Succeeded)
            {
                sb.Append("  ").Append(pair.Key).Append(": ok=").Append(pair.Value)
                  .Append(" refused=").Append(Refused.TryGetValue(pair.Key, out int r) ? r : 0).Append('\n');
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Seeded random-action soak test for the merge engine. Each step performs one random command
    /// (spawn, drag/merge, inventory moves, sell, deliver, rewards, time, save/load, interrupted and
    /// stale drags, unlocks, garbage input) and then checks every invariant. A refused command must
    /// leave the state byte-for-byte unchanged. On failure the exception message names the seed and
    /// step so the run can be replayed with <see cref="Run"/>.
    /// </summary>
    public sealed class MergeSimulation
    {
        private const int FullReplayEvery = 250;
        private const int HistoryLength = 25;

        private readonly ulong _seed;
        private readonly EngineConfig _config;
        private readonly SeededRandom _driver;
        private readonly HashSet<long> _everAllocated = new HashSet<long>();
        private readonly List<Item> _scratch = new List<Item>();
        private readonly Queue<string> _history = new Queue<string>();
        private readonly SimulationStats _stats = new SimulationStats();
        private MergeEngine _engine;
        private Ledger _ledger;
        private long _now;
        private int _step;

        private readonly IReadOnlyList<InitialPlacement> _layout;

        private MergeSimulation(ulong seed, EngineConfig config, IReadOnlyList<InitialPlacement> layout)
        {
            _seed = seed;
            _config = config;
            _layout = layout;
            _driver = new SeededRandom(seed ^ 0xA5A5A5A55A5A5A5AUL);
        }

        public static SimulationStats Run(ulong seed, int steps, EngineConfig config = null,
            IReadOnlyList<InitialPlacement> layout = null)
        {
            var sim = new MergeSimulation(seed, config ?? TestConfigs.Default(), layout ?? TestConfigs.DefaultLayout());
            sim.Execute(steps);
            return sim._stats;
        }

        private void Execute(int steps)
        {
            _ledger = new Ledger();
            Attach(MergeEngine.CreateNew(_config, _seed, _layout, _ledger));
            for (int i = 0; i < _engine.ItemCount; i++)
            {
                _everAllocated.Add(i + 1);
            }

            CheckInvariants(true, "initial");
            for (_step = 0; _step < steps; _step++)
            {
                string before = TestConfigs.Canon(_engine);
                string action;
                bool changed;
                try
                {
                    changed = PerformRandomAction(out action);
                }
                catch (SimulationFailure)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw Failure("unexpected exception: " + ex);
                }

                Remember(action);
                if (!changed && TestConfigs.Canon(_engine) != before)
                {
                    throw Failure("action reported no state change but state differs: " + action);
                }

                CheckInvariants(_step % FullReplayEvery == 0, action);
                _stats.MaxItemsSeen = Math.Max(_stats.MaxItemsSeen, _engine.ItemCount);
            }

            CheckInvariants(true, "final");
            _stats.FinalNextId = _engine.NextItemId;
        }

        private void Attach(MergeEngine engine)
        {
            _engine = engine;
            engine.ItemCreated += (item, reason) =>
            {
                if (!_everAllocated.Add(item.Id.Value))
                {
                    throw Failure("ID " + item.Id + " was allocated twice (" + reason + ").");
                }
            };
        }

        // ------------------------------------------------------------------ action selection

        private bool PerformRandomAction(out string action)
        {
            int roll = _driver.NextInt(100);
            if (roll < 26) return TapGenerator(out action);
            if (roll < 50) return Drop(out action);
            if (roll < 54) return ToInventory(out action);
            if (roll < 58) return FromInventory(out action);
            if (roll < 65) return Sell(out action);
            if (roll < 70) return Deliver(out action);
            if (roll < 73) return EnqueueReward(out action);
            if (roll < 77) return ClaimReward(out action);
            if (roll < 83) return AdvanceTime(out action);
            if (roll < 86) return SaveLoad(out action);
            if (roll < 89) return InterruptedDrag(out action);
            if (roll < 92) return StaleDrag(out action);
            if (roll < 93) return Unlock(out action);
            if (roll < 94) return Expand(out action);
            return Garbage(out action);
        }

        // ------------------------------------------------------------------ actions
        // Each returns true if the engine state may have changed (i.e. the command succeeded).

        private bool TapGenerator(out string action)
        {
            int cell = _driver.NextInt(10) == 0 ? RandomCellAny() : RandomBoardCell(item => item.IsGenerator);
            if (cell < 0)
            {
                action = "tap: no generator";
                return false;
            }

            Item gen = _engine.GetCell(cell);
            ItemId expected = gen == null ? ItemId.None : gen.Id;
            int before = _engine.ItemCount;
            int emptyBefore = _engine.CountEmptyCells();
            SpawnResult r = _engine.TryTapGenerator(cell, expected, _now);
            action = "tap cell " + cell + " -> " + r.Error + (r.Success ? " spawned " + r.Item + "@" + r.Cell : "");
            Tally("tap", r.Success);

            switch (r.Error)
            {
                case ActionError.None:
                    Expect(_engine.ItemCount == before + 1, "spawn must add exactly one item");
                    Expect(_engine.GetCell(r.Cell) != null && _engine.GetCell(r.Cell).Id == r.Item, "spawned item not at reported cell");
                    Expect(emptyBefore > 0, "spawn succeeded on a full board");
                    return true;
                case ActionError.BoardFull:
                    _stats.BoardFullRefusals++;
                    Expect(emptyBefore == 0, "BoardFull reported with empty cells available");
                    Expect(_engine.HasBoardFullResolution(), "board full with no resolution path (soft-lock)");
                    return false;
                case ActionError.GeneratorOnCooldown:
                    Expect(r.CooldownRemainingMs > 0, "cooldown refusal with no remaining time");
                    return false;
                case ActionError.InvalidCell:
                    Expect(!_engine.IsValidCell(cell), "valid cell refused as InvalidCell");
                    return false;
                case ActionError.NotAGenerator:
                case ActionError.SourceEmpty:
                    Expect(gen == null || !gen.IsGenerator, "valid generator refused as " + r.Error);
                    return false;
                default:
                    throw Failure("unexpected tap error " + r.Error);
            }
        }

        private bool Drop(out string action)
        {
            int from = RandomBoardCell(item => true);
            if (from < 0)
            {
                action = "drop: empty board";
                return false;
            }

            Item source = _engine.GetCell(from);
            int to;
            int pick = _driver.NextInt(10);
            if (pick < 6)
            {
                to = RandomBoardCell(item => item != source && item.Type.Equals(source.Type));
                if (to < 0)
                {
                    to = RandomCellAny();
                }
            }
            else if (pick < 9)
            {
                to = _driver.NextInt(_engine.CellCount);
            }
            else
            {
                to = RandomCellAny();
            }

            Item target = _engine.GetCell(to);
            ItemType sourceType = source.Type;
            ItemId sourceId = source.Id;
            ItemId targetId = target == null ? ItemId.None : target.Id;
            int before = _engine.ItemCount;
            DropResult r = _engine.TryDrop(from, sourceId, to);
            action = "drop " + sourceId + " " + from + "->" + to + " -> " + r.Error + " " + r.Outcome;
            Tally("drop", r.Success);
            if (!r.Success)
            {
                Expect(r.Error == ActionError.InvalidCell || r.Error == ActionError.SameLocation || r.Error == ActionError.CellLocked,
                    "unexpected drop refusal " + r.Error);
                return false;
            }

            if (r.Outcome == DropOutcome.Merged)
            {
                _stats.Merges++;
                Expect(_engine.ItemCount == before - 1, "merge must reduce item count by exactly one");
                Expect(!_engine.TryGetItem(sourceId, out _) && !_engine.TryGetItem(targetId, out _), "merge sources still exist");
                Item result = _engine.GetCell(to);
                Expect(result != null && result.Id == r.ResultItem, "merge result missing from target cell");
                Expect(result.Type.Equals(sourceType.NextTier), "merge produced wrong type");
                Expect(_engine.GetCell(from) == null, "merge source cell not cleared");
            }
            else
            {
                Expect(_engine.ItemCount == before, "move/swap changed item count");
                Expect(_engine.GetCell(to).Id == sourceId, "moved item not at target");
                Expect(r.Outcome == DropOutcome.Moved ? _engine.GetCell(from) == null : _engine.GetCell(from).Id == targetId,
                    "source cell wrong after " + r.Outcome);
            }

            return true;
        }

        private bool ToInventory(out string action)
        {
            int cell = RandomBoardCell(item => true);
            if (cell < 0)
            {
                action = "toInv: empty board";
                return false;
            }

            ItemId id = _engine.GetCell(cell).Id;
            int free = _engine.CountFreeSlots();
            ItemResult r = _engine.TryMoveToInventory(cell, id);
            action = "toInv " + id + " from " + cell + " -> " + r.Error;
            Tally("toInv", r.Success);
            Expect(r.Success == (free > 0), "inventory move result disagrees with free slots");
            return r.Success;
        }

        private bool FromInventory(out string action)
        {
            int slot = RandomSlot();
            if (slot < 0)
            {
                action = "fromInv: inventory empty";
                return false;
            }

            ItemId id = _engine.GetSlot(slot).Id;
            int cell = _driver.NextInt(3) == 0 ? _driver.NextInt(_engine.CellCount) : RandomEmptyCell();
            if (cell < 0)
            {
                cell = _driver.NextInt(_engine.CellCount);
            }

            ItemResult r = _engine.TryMoveFromInventory(slot, id, cell);
            action = "fromInv " + id + " slot " + slot + "->" + cell + " -> " + r.Error;
            Tally("fromInv", r.Success);
            if (!r.Success)
            {
                Expect(r.Error == ActionError.TargetOccupied || r.Error == ActionError.CellLocked, "unexpected refusal " + r.Error);
            }

            return r.Success;
        }

        private bool Sell(out string action)
        {
            Location loc;
            if (_driver.NextInt(4) == 0 && RandomSlot() >= 0)
            {
                loc = Location.Slot(RandomSlot());
            }
            else
            {
                int cell = RandomBoardCell(item => true);
                if (cell < 0)
                {
                    action = "sell: nothing";
                    return false;
                }

                loc = Location.Cell(cell);
            }

            Item item = loc.IsBoard ? _engine.GetCell(loc.Index) : _engine.GetSlot(loc.Index);
            if (item.IsGenerator && CountGenerators() <= 2)
            {
                action = "sell: kept last generators";
                return false;
            }

            string described = item.ToString();
            ItemResult r = _engine.TrySell(loc, item.Id);
            action = "sell " + described + " -> " + r.Error + " value " + r.Value;
            Tally("sell", r.Success);
            Expect(r.Success, "valid sale refused: " + r.Error);
            Expect(r.Value == _config.GetSellValue(item.Type), "sale returned wrong value");
            Expect(!_engine.TryGetItem(item.Id, out _), "sold item still exists");
            return true;
        }

        private bool Deliver(out string action)
        {
            var ids = new List<ItemId>();
            int wanted = 1 + _driver.NextInt(3);
            for (int i = 0; i < wanted * 3 && ids.Count < wanted; i++)
            {
                int cell = RandomBoardCell(item => !item.IsGenerator);
                if (cell >= 0 && !ids.Contains(_engine.GetCell(cell).Id))
                {
                    ids.Add(_engine.GetCell(cell).Id);
                }
            }

            int corrupt = _driver.NextInt(6);
            if (corrupt == 0 && ids.Count > 0)
            {
                ids.Add(ids[0]); // duplicate
            }
            else if (corrupt == 1)
            {
                ids.Add(new ItemId(_engine.NextItemId + 100)); // unknown
            }
            else if (corrupt == 2 && RandomSlot() >= 0)
            {
                ids.Add(_engine.GetSlot(RandomSlot()).Id); // in inventory, not on board
            }

            int before = _engine.ItemCount;
            ItemResult r = _engine.TryDeliver(ids);
            action = "deliver [" + string.Join(",", ids) + "] -> " + r.Error;
            Tally("deliver", r.Success);
            if (r.Success)
            {
                Expect(_engine.ItemCount == before - ids.Count, "delivery removed the wrong number of items");
                foreach (ItemId id in ids)
                {
                    Expect(!_engine.TryGetItem(id, out _), "delivered item still exists");
                }
            }
            else
            {
                Expect(_engine.ItemCount == before, "refused delivery removed items");
            }

            return r.Success;
        }

        private bool EnqueueReward(out string action)
        {
            ItemType type;
            int pick = _driver.NextInt(20);
            if (pick == 0)
            {
                type = new ItemType("ghost", 1);
            }
            else if (pick < 4)
            {
                type = new ItemType(TestConfigs.Toolbox, 1 + _driver.NextInt(3));
            }
            else
            {
                ChainDefinition chain = _config.Chains[2 + _driver.NextInt(2)];
                type = new ItemType(chain.Id, 1 + _driver.NextInt(chain.MaxTier));
            }

            ActionError error = _engine.EnqueueReward(type);
            action = "enqueueReward " + type + " -> " + error;
            Tally("enqueueReward", error == ActionError.None);
            Expect((error == ActionError.None) == _config.IsValidType(type), "reward validation wrong");
            return error == ActionError.None;
        }

        private bool ClaimReward(out string action)
        {
            int pending = _engine.PendingRewardCount;
            bool hasRoom = _engine.CountEmptyCells() > 0;
            ItemResult r = _engine.TryClaimReward();
            action = "claimReward -> " + r.Error + (r.Success ? " " + r.Item : "");
            Tally("claimReward", r.Success);
            if (pending == 0)
            {
                Expect(r.Error == ActionError.NoPendingReward, "claimed from an empty reward queue");
            }
            else
            {
                Expect(r.Success == hasRoom, "reward claim result disagrees with free cells");
                Expect(_engine.PendingRewardCount == (r.Success ? pending - 1 : pending), "reward queue count wrong");
            }

            return r.Success;
        }

        private bool AdvanceTime(out string action)
        {
            long dt = _driver.NextInt(3) == 0 ? _driver.NextInt(150000) : _driver.NextInt(5000);
            _now += dt;
            action = "time +" + dt + "ms -> " + _now;
            Tally("time", true);
            return false;
        }

        /// <summary>Autosave, kill the process, reload from the save: nothing may change.</summary>
        private bool SaveLoad(out string action)
        {
            string before = TestConfigs.Canon(_engine);
            EngineSnapshot snapshot = DeepCopy(_engine.CreateSnapshot());
            Reload(snapshot);
            action = "save/load";
            Tally("saveLoad", true);
            Expect(TestConfigs.Canon(_engine) == before, "state changed across save/load");
            return false;
        }

        /// <summary>
        /// Player picks up an item, the app is paused (autosave) and killed mid-drag, then the game
        /// reloads and the drop completes using the pre-kill drag token. The item must still be at its
        /// origin exactly once, and the drop must behave like a normal drop.
        /// </summary>
        private bool InterruptedDrag(out string action)
        {
            int from = RandomBoardCell(item => true);
            if (from < 0)
            {
                action = "interruptedDrag: empty board";
                return false;
            }

            ItemId token = _engine.GetCell(from).Id;
            int count = _engine.ItemCount;
            Reload(DeepCopy(_engine.CreateSnapshot()));
            _stats.InterruptedDrags++;
            Expect(_engine.ItemCount == count, "item count changed across interrupted drag");
            Expect(_engine.GetCell(from) != null && _engine.GetCell(from).Id == token, "dragged item not back at origin after reload");

            int to = _driver.NextInt(_engine.CellCount);
            DropResult r = _engine.TryDrop(from, token, to);
            action = "interruptedDrag " + token + " " + from + "->" + to + " -> " + r.Error + " " + r.Outcome;
            Tally("interruptedDrag", r.Success);
            Expect(r.Error != ActionError.StaleItem, "valid drag token rejected after reload");
            if (r.Outcome == DropOutcome.Merged)
            {
                _stats.Merges++;
            }

            return r.Success;
        }

        /// <summary>
        /// A drag token goes stale (the picked-up item was sold/merged by another path first). Dropping
        /// with the stale token must be refused without touching state.
        /// </summary>
        private bool StaleDrag(out string action)
        {
            int from = RandomBoardCell(item => !item.IsGenerator);
            if (from < 0)
            {
                action = "staleDrag: nothing";
                return false;
            }

            ItemId token = _engine.GetCell(from).Id;
            _engine.TrySell(Location.Cell(from), token);
            bool refill = _driver.NextInt(2) == 0 && _engine.PendingRewardCount == 0;
            if (refill)
            {
                // Put a different item in the same cell so the stale token points at an occupied cell.
                _engine.EnqueueReward(new ItemType(TestConfigs.Cup, 1));
                if (_engine.TryClaimReward().Location != Location.Cell(from))
                {
                    refill = false;
                }
            }

            string before = TestConfigs.Canon(_engine);
            DropResult r = _engine.TryDrop(from, token, _driver.NextInt(_engine.CellCount));
            action = "staleDrag " + token + " from " + from + " -> " + r.Error;
            Tally("staleDrag", r.Success);
            Expect(!r.Success, "stale drag token was accepted");
            Expect(TestConfigs.Canon(_engine) == before, "stale drag changed state");
            return true; // the sale (and refill) before the drop did change state
        }

        private bool Unlock(out string action)
        {
            int cell = _driver.NextInt(_engine.CellCount);
            bool wasLocked = _engine.IsLocked(cell);
            ItemResult r = _engine.TryUnlockCell(cell);
            action = "unlock " + cell + " -> " + r.Error;
            Tally("unlock", r.Success);
            Expect(r.Success == wasLocked, "unlock result disagrees with lock state");
            return r.Success;
        }

        private bool Expand(out string action)
        {
            int target = _engine.InventoryCapacity + _driver.NextInt(3) - 1;
            int before = _engine.InventoryCapacity;
            ItemResult r = _engine.TryExpandInventory(target);
            action = "expandInv " + before + "->" + target + " -> " + r.Error;
            Tally("expandInv", r.Success);
            Expect(r.Success == (target > before && target <= _config.Board.MaxInventoryCapacity), "expand result wrong");
            return r.Success;
        }

        /// <summary>Random out-of-range / wrong-ID input to random commands. All must be refused with no change.</summary>
        private bool Garbage(out string action)
        {
            int bad = _driver.NextInt(2) == 0 ? -1 - _driver.NextInt(5) : _engine.CellCount + _driver.NextInt(5);
            var wrongId = new ItemId(_engine.NextItemId + _driver.NextInt(1000));
            int which = _driver.NextInt(7);
            ActionError error;
            switch (which)
            {
                case 0: error = _engine.TryDrop(bad, wrongId, 0).Error; break;
                case 1: error = _engine.TryTapGenerator(bad, wrongId, _now).Error; break;
                case 2: error = _engine.TryMoveToInventory(bad, wrongId).Error; break;
                case 3: error = _engine.TryMoveFromInventory(bad, wrongId, 0).Error; break;
                case 4: error = _engine.TrySell(Location.Cell(bad), wrongId).Error; break;
                case 5: error = _engine.TryUnlockCell(bad).Error; break;
                default:
                    int cell = RandomBoardCell(item => true);
                    error = cell < 0 ? ActionError.SourceEmpty : _engine.TryDrop(cell, wrongId, (cell + 1) % _engine.CellCount).Error;
                    break;
            }

            action = "garbage#" + which + " arg " + bad + " -> " + error;
            Tally("garbage", error == ActionError.None);
            Expect(error != ActionError.None, "garbage input was accepted");
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private void Reload(EngineSnapshot snapshot)
        {
            var ledger = new Ledger();
            if (!MergeEngine.TryFromSnapshot(_config, snapshot, out MergeEngine loaded, out List<string> errors, ledger))
            {
                throw Failure("engine rejected its own snapshot:\n" + string.Join("\n", errors));
            }

            _ledger = ledger;
            Attach(loaded);
            _stats.Reloads++;
        }

        /// <summary>Deep-copies a snapshot so the reloaded engine shares no objects with the old one.</summary>
        private static EngineSnapshot DeepCopy(EngineSnapshot s)
        {
            var copy = new EngineSnapshot
            {
                Version = s.Version,
                Columns = s.Columns,
                Rows = s.Rows,
                LockedCells = (bool[])s.LockedCells.Clone(),
                InventoryCapacity = s.InventoryCapacity,
                NextItemId = s.NextItemId,
                RngState = s.RngState,
                Items = new ItemRecord[s.Items.Length],
                PendingRewards = new ItemTypeRecord[s.PendingRewards.Length],
            };
            for (int i = 0; i < s.Items.Length; i++)
            {
                ItemRecord r = s.Items[i];
                copy.Items[i] = new ItemRecord
                {
                    Id = r.Id, ChainId = r.ChainId, Tier = r.Tier, Area = r.Area, Index = r.Index,
                    GeneratorCharges = r.GeneratorCharges, GeneratorReadyAtMs = r.GeneratorReadyAtMs,
                };
            }

            for (int i = 0; i < s.PendingRewards.Length; i++)
            {
                copy.PendingRewards[i] = new ItemTypeRecord { ChainId = s.PendingRewards[i].ChainId, Tier = s.PendingRewards[i].Tier };
            }

            return copy;
        }

        private void CheckInvariants(bool fullReplay, string action)
        {
            List<string> violations = InvariantChecker.Check(_engine, _ledger, fullReplay);
            if (violations.Count > 0)
            {
                throw Failure("invariant violated after '" + action + "':\n  " + string.Join("\n  ", violations));
            }
        }

        private int RandomBoardCell(Predicate<Item> filter)
        {
            int start = _driver.NextInt(_engine.CellCount);
            for (int k = 0; k < _engine.CellCount; k++)
            {
                int cell = (start + k) % _engine.CellCount;
                Item item = _engine.GetCell(cell);
                if (item != null && filter(item))
                {
                    return cell;
                }
            }

            return -1;
        }

        private int RandomEmptyCell()
        {
            int start = _driver.NextInt(_engine.CellCount);
            for (int k = 0; k < _engine.CellCount; k++)
            {
                int cell = (start + k) % _engine.CellCount;
                if (!_engine.IsLocked(cell) && _engine.GetCell(cell) == null)
                {
                    return cell;
                }
            }

            return -1;
        }

        private int RandomSlot()
        {
            if (_engine.InventoryCapacity == 0)
            {
                return -1;
            }

            int start = _driver.NextInt(_engine.InventoryCapacity);
            for (int k = 0; k < _engine.InventoryCapacity; k++)
            {
                int slot = (start + k) % _engine.InventoryCapacity;
                if (_engine.GetSlot(slot) != null)
                {
                    return slot;
                }
            }

            return -1;
        }

        /// <summary>Any cell index including out-of-range and locked ones.</summary>
        private int RandomCellAny() => _driver.NextInt(-2, _engine.CellCount + 2);

        private int CountGenerators()
        {
            _engine.CopyItems(_scratch);
            int n = 0;
            foreach (Item item in _scratch)
            {
                if (item.IsGenerator)
                {
                    n++;
                }
            }

            return n;
        }

        private void Tally(string action, bool success)
        {
            Dictionary<string, int> table = success ? _stats.Succeeded : _stats.Refused;
            table[action] = (table.TryGetValue(action, out int n) ? n : 0) + 1;
            if (!_stats.Succeeded.ContainsKey(action))
            {
                _stats.Succeeded[action] = 0;
            }
        }

        private void Remember(string action)
        {
            _history.Enqueue(_step + ": " + action);
            while (_history.Count > HistoryLength)
            {
                _history.Dequeue();
            }
        }

        private void Expect(bool condition, string message)
        {
            if (!condition)
            {
                throw Failure(message);
            }
        }

        private SimulationFailure Failure(string message) =>
            new SimulationFailure("Merge simulation FAILED. seed=" + _seed + " step=" + _step +
                                  "\nReplay: MergeSimulation.Run(" + _seed + "UL, " + (_step + 1) + ")\n" + message +
                                  "\nLast actions:\n  " + string.Join("\n  ", _history));
    }
}
