using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// Checks the merge-engine invariants. Returns human-readable violations (empty = healthy).
    /// Used by the automated tests and simulation; safe to call from a debug overlay.
    ///
    /// Checked:
    /// 1. Valid transformations: every merge consumed two same-chain, same-tier items and produced tier+1 (via ledger).
    /// 2. Provenance: board + inventory contents equal the replayed ledger live set, type for type.
    /// 3. No duplicate IDs across board and inventory.
    /// 4. No dangling references: every cell/slot item knows where it is, every known item is where it says.
    /// 5. Inventory consistency: capacity respected, no item in two places; locked cells empty.
    /// 6. Merge-value conservation for strictly doubling chains (via ledger replay).
    /// Plus: types and generator state are valid, and every ID is below the next-ID counter.
    /// </summary>
    public static class InvariantChecker
    {
        /// <param name="fullReplay">
        /// Also replay the whole ledger from scratch (O(ledger length)). The incremental live-set check
        /// always runs when a ledger is supplied.
        /// </param>
        public static List<string> Check(MergeEngine engine, Ledger ledger = null, bool fullReplay = false)
        {
            var violations = new List<string>();
            EngineConfig config = engine.Config;
            var seen = new HashSet<long>();
            int placed = 0;

            for (int i = 0; i < engine.CellCount; i++)
            {
                Item item = engine.GetCell(i);
                if (item == null)
                {
                    continue;
                }

                placed++;
                if (engine.IsLocked(i))
                {
                    violations.Add("Locked cell " + i + " contains " + item + ".");
                }

                CheckPlacedItem(engine, item, Location.Cell(i), seen, violations);
            }

            for (int s = 0; s < engine.InventoryCapacity; s++)
            {
                Item item = engine.GetSlot(s);
                if (item == null)
                {
                    continue;
                }

                placed++;
                CheckPlacedItem(engine, item, Location.Slot(s), seen, violations);
            }

            // Every placed item is unique and tracked (checked above), so equal counts prove the tracked
            // set is exactly the placed set: no item is tracked without being in a cell or slot.
            if (placed != engine.ItemCount)
            {
                violations.Add("Engine tracks " + engine.ItemCount + " items but " + placed + " are placed (orphaned items).");
            }

            var all = new List<Item>(engine.ItemCount);
            engine.CopyItems(all);

            if (ledger != null)
            {
                for (int i = 0; i < ledger.Violations.Count; i++)
                {
                    violations.Add("Ledger: " + ledger.Violations[i]);
                }

                if (ledger.LiveCount != engine.ItemCount)
                {
                    violations.Add("Ledger has " + ledger.LiveCount + " live items but engine has " + engine.ItemCount + ".");
                }

                for (int i = 0; i < all.Count; i++)
                {
                    if (!ledger.TryGetLive(all[i].Id, out ItemType type))
                    {
                        violations.Add("Item " + all[i] + " has no provenance in the ledger.");
                    }
                    else if (!type.Equals(all[i].Type))
                    {
                        violations.Add("Item " + all[i] + " is recorded in the ledger as " + type + ".");
                    }
                }

                if (fullReplay)
                {
                    CheckReplay(engine, ledger, all, violations);
                }
            }

            return violations;
        }

        private static void CheckReplay(MergeEngine engine, Ledger ledger, List<Item> all, List<string> violations)
        {
            LedgerReplay replay = ledger.Replay(engine.Config);
            violations.AddRange(replay.Violations);
            if (replay.Live.Count != all.Count)
            {
                violations.Add("Replayed ledger has " + replay.Live.Count + " live items but engine has " + all.Count + ".");
            }

            long liveValue = 0;
            for (int i = 0; i < all.Count; i++)
            {
                liveValue += engine.Config.GetMergeValue(all[i].Type);
                if (!replay.Live.TryGetValue(all[i].Id.Value, out ItemType type) || !type.Equals(all[i].Type))
                {
                    violations.Add("Replayed ledger disagrees about " + all[i] + ".");
                }
            }

            if (liveValue != replay.ExternalMergeValueBalance)
            {
                violations.Add("Merge value not conserved: live value " + liveValue + " but spawn/reward/load minus sell/delivery is " +
                               replay.ExternalMergeValueBalance + ".");
            }
        }

        private static void CheckPlacedItem(MergeEngine engine, Item item, Location at, HashSet<long> seen, List<string> violations)
        {
            if (!seen.Add(item.Id.Value))
            {
                violations.Add("Duplicate ID " + item.Id + " (found again at " + at + ").");
            }

            if (item.Location != at)
            {
                violations.Add("Item " + item.Id + " is at " + at + " but believes it is at " + item.Location + ".");
            }

            if (!engine.TryGetItem(item.Id, out Item tracked) || !ReferenceEquals(tracked, item))
            {
                violations.Add("Item " + item.Id + " at " + at + " is not tracked by the engine.");
            }

            if (item.Id.IsNone || item.Id.Value >= engine.NextItemId)
            {
                violations.Add("Item " + item.Id + " has an ID outside 1.." + (engine.NextItemId - 1) + ".");
            }

            if (!engine.Config.IsValidType(item.Type))
            {
                violations.Add("Item " + item.Id + " has invalid type " + item.Type + ".");
                return;
            }

            GeneratorDefinition gen = engine.Config.GetGenerator(item.Type);
            if (item.IsGenerator != (gen != null))
            {
                violations.Add("Item " + item.Id + " generator flag disagrees with config.");
            }

            if (gen != null && (item.GeneratorCharges < 0 || item.GeneratorCharges > gen.MaxCharges))
            {
                violations.Add("Generator " + item.Id + " has " + item.GeneratorCharges + " charges (max " + gen.MaxCharges + ").");
            }
        }
    }
}
