using System.Collections.Generic;

namespace Game.Domain
{
    public enum LedgerDirection
    {
        Created = 0,
        Removed = 1,
    }

    /// <summary>One provenance record. Merges produce three entries sharing an <see cref="OperationId"/>.</summary>
    public readonly struct LedgerEntry
    {
        public readonly long Sequence;
        public readonly long OperationId;
        public readonly LedgerDirection Direction;
        public readonly ItemId Item;
        public readonly ItemType Type;
        /// <summary>Set for creations.</summary>
        public readonly CreationReason Created;
        /// <summary>Set for removals.</summary>
        public readonly RemovalReason Removed;

        public LedgerEntry(long sequence, long operationId, LedgerDirection direction, ItemId item, ItemType type,
            CreationReason created, RemovalReason removed)
        {
            Sequence = sequence;
            OperationId = operationId;
            Direction = direction;
            Item = item;
            Type = type;
            Created = created;
            Removed = removed;
        }

        public bool IsMerge => Direction == LedgerDirection.Created ? Created == CreationReason.Merge : Removed == RemovalReason.Merge;

        public override string ToString() =>
            "[" + Sequence + " op" + OperationId + "] " + Direction + " " + Item + " " + Type + " (" +
            (Direction == LedgerDirection.Created ? Created.ToString() : Removed.ToString()) + ")";
    }

    /// <summary>Outcome of replaying a ledger from scratch.</summary>
    public sealed class LedgerReplay
    {
        public readonly Dictionary<long, ItemType> Live = new Dictionary<long, ItemType>();
        public readonly List<string> Violations = new List<string>();

        /// <summary>Merge value added by non-merge creations minus value removed by non-merge removals.</summary>
        public long ExternalMergeValueBalance;
    }

    /// <summary>
    /// Append-only provenance log. Every item that exists must have been created by a recorded load,
    /// spawn, merge or reward; every removed item must have been removed by a recorded merge, sale or
    /// delivery. The ledger validates each record as it arrives and can also be replayed from scratch.
    ///
    /// It is optional: the engine runs without one. Tests, simulations and debug builds attach one.
    /// Memory grows with every action, so release builds should not keep one for a whole session.
    /// </summary>
    public sealed class Ledger
    {
        private readonly List<LedgerEntry> _entries = new List<LedgerEntry>();
        private readonly Dictionary<long, ItemType> _live = new Dictionary<long, ItemType>();
        private readonly HashSet<long> _everCreated = new HashSet<long>();
        private readonly List<string> _violations = new List<string>();
        private long _nextOperation = 1;

        public IReadOnlyList<LedgerEntry> Entries => _entries;
        public IReadOnlyList<string> Violations => _violations;
        public int LiveCount => _live.Count;

        public bool TryGetLive(ItemId id, out ItemType type) => _live.TryGetValue(id.Value, out type);

        public void RecordCreated(CreationReason reason, ItemId id, ItemType type)
        {
            if (reason == CreationReason.Merge)
            {
                _violations.Add("Merge creation " + id + " recorded without its consumed items.");
            }

            Create(_nextOperation++, reason, id, type);
        }

        public void RecordRemoved(RemovalReason reason, ItemId id, ItemType type)
        {
            if (reason == RemovalReason.Merge)
            {
                _violations.Add("Merge removal " + id + " recorded without a merge result.");
            }

            Remove(_nextOperation++, reason, id, type);
        }

        public void RecordMerge(ItemId a, ItemType typeA, ItemId b, ItemType typeB, ItemId result, ItemType resultType)
        {
            long op = _nextOperation++;
            CheckMergeShape(op, a, typeA, b, typeB, result, resultType, _violations);
            Remove(op, RemovalReason.Merge, a, typeA);
            Remove(op, RemovalReason.Merge, b, typeB);
            Create(op, CreationReason.Merge, result, resultType);
        }

        /// <summary>Recompute the live set and all checks from the raw entries, independent of incremental state.</summary>
        public LedgerReplay Replay(EngineConfig config)
        {
            var replay = new LedgerReplay();
            var everCreated = new HashSet<long>();
            int i = 0;
            while (i < _entries.Count)
            {
                LedgerEntry e = _entries[i];
                if (e.IsMerge)
                {
                    if (i + 2 >= _entries.Count)
                    {
                        replay.Violations.Add("Replay: truncated merge group at op" + e.OperationId + ".");
                        break;
                    }

                    LedgerEntry r1 = _entries[i], r2 = _entries[i + 1], c = _entries[i + 2];
                    bool grouped = r1.OperationId == r2.OperationId && r2.OperationId == c.OperationId &&
                                   r1.Direction == LedgerDirection.Removed && r2.Direction == LedgerDirection.Removed &&
                                   c.Direction == LedgerDirection.Created && r2.IsMerge && c.IsMerge;
                    if (!grouped)
                    {
                        replay.Violations.Add("Replay: malformed merge group at op" + e.OperationId + ".");
                        i++;
                        continue;
                    }

                    CheckMergeShape(e.OperationId, r1.Item, r1.Type, r2.Item, r2.Type, c.Item, c.Type, replay.Violations);
                    if (config != null && !config.CanMerge(r1.Type, r2.Type))
                    {
                        replay.Violations.Add("Replay op" + e.OperationId + ": " + r1.Type + " cannot merge under the config.");
                    }

                    ReplayRemove(replay, r1);
                    ReplayRemove(replay, r2);
                    ReplayCreate(replay, everCreated, c);
                    i += 3;
                    continue;
                }

                long value = config == null ? 0 : config.GetMergeValue(e.Type);
                if (e.Direction == LedgerDirection.Created)
                {
                    ReplayCreate(replay, everCreated, e);
                    replay.ExternalMergeValueBalance += value;
                }
                else
                {
                    ReplayRemove(replay, e);
                    replay.ExternalMergeValueBalance -= value;
                }

                i++;
            }

            return replay;
        }

        private static void ReplayCreate(LedgerReplay replay, HashSet<long> everCreated, LedgerEntry e)
        {
            if (!everCreated.Add(e.Item.Value))
            {
                replay.Violations.Add("Replay: " + e.Item + " created twice (seq " + e.Sequence + ").");
                return;
            }

            replay.Live[e.Item.Value] = e.Type;
        }

        private static void ReplayRemove(LedgerReplay replay, LedgerEntry e)
        {
            if (!replay.Live.TryGetValue(e.Item.Value, out ItemType type))
            {
                replay.Violations.Add("Replay: " + e.Item + " removed but not live (seq " + e.Sequence + ").");
                return;
            }

            if (!type.Equals(e.Type))
            {
                replay.Violations.Add("Replay: " + e.Item + " removed as " + e.Type + " but was " + type + ".");
            }

            replay.Live.Remove(e.Item.Value);
        }

        private static void CheckMergeShape(long op, ItemId a, ItemType typeA, ItemId b, ItemType typeB, ItemId result,
            ItemType resultType, List<string> violations)
        {
            if (a == b)
            {
                violations.Add("Merge op" + op + " consumed the same item twice: " + a + ".");
            }

            if (result == a || result == b)
            {
                violations.Add("Merge op" + op + " reused a consumed ID for its result: " + result + ".");
            }

            if (!typeA.Equals(typeB))
            {
                violations.Add("Merge op" + op + " consumed different types: " + typeA + " and " + typeB + ".");
            }

            if (!resultType.Equals(typeA.NextTier))
            {
                violations.Add("Merge op" + op + " produced " + resultType + " from " + typeA + " (expected " + typeA.NextTier + ").");
            }
        }

        private void Create(long op, CreationReason reason, ItemId id, ItemType type)
        {
            if (id.IsNone)
            {
                _violations.Add("Created an item with no ID (" + type + ").");
            }

            if (!_everCreated.Add(id.Value))
            {
                _violations.Add("ID " + id + " was created more than once (IDs must never be reused).");
            }

            _live[id.Value] = type;
            _entries.Add(new LedgerEntry(_entries.Count, op, LedgerDirection.Created, id, type, reason, default));
        }

        private void Remove(long op, RemovalReason reason, ItemId id, ItemType type)
        {
            if (!_live.TryGetValue(id.Value, out ItemType liveType))
            {
                _violations.Add("Removed " + id + " (" + reason + ") but it is not live.");
            }
            else if (!liveType.Equals(type))
            {
                _violations.Add("Removed " + id + " as " + type + " but it was created as " + liveType + ".");
            }

            _live.Remove(id.Value);
            _entries.Add(new LedgerEntry(_entries.Count, op, LedgerDirection.Removed, id, type, default, reason));
        }
    }
}
