# Architecture

## Assemblies

| Assembly | Contents | Unity references |
|---|---|---|
| `Game.Domain` | Board, items, chains, merge rules, generators, inventory, rewards, ledger, snapshot DTO, invariant checker | None (`noEngineReferences: true`) |
| `Game.Domain.Tests` | EditMode NUnit tests, randomised simulation | Test runner only, Editor-only |
| `Game.Presentation` | Views, input, pooling, animation (M2) | Not yet created |
| `Game.Data` | ScriptableObjects / JSON mapped into domain config (M2/M3) | Not yet created |
| `Game.Services` | Save files, analytics, cloud, ads, IAP behind interfaces (later) | Not yet created |

## Merge engine (`MergeEngine`)

- **Commands return results.** Every `Try*` command validates fully before it changes anything. A
  refused command returns an `ActionError` and leaves the state unchanged (tested byte-for-byte).
- **Events after the fact.** `ItemCreated`, `ItemRemoved`, `ItemMoved`, `ItemsMerged`, `CellUnlocked`,
  `InventoryCapacityChanged` and `RewardQueued` fire only after a change has fully completed.
  Presentation reacts to them; it never mutates domain objects (setters are `internal`).
- **Drag is presentation-only.** The engine sees only the final drop: `TryDrop(from, expectedItemId, to)`.
  If the app is killed mid-drag, nothing in the domain has moved, so nothing can be duplicated or
  lost. A drop with a stale token (the item has changed) is refused with `StaleItem`.
- **Drop rules:** empty target, move; identical type below max tier, merge into tier+1 at the target
  with a new ID; anything else, swap.
- **Generators** are chain tiers with a `GeneratorDefinition` (charges, cooldown, weighted output
  table). A tap spends one charge and spawns into the nearest empty unlocked cell (Manhattan distance,
  then lowest index). At 0 charges the generator cools down, then refills to full. A refused tap
  spends no charge and consumes no RNG. Merged or rewarded generators start full.
- **Time** is passed in as milliseconds by the caller. The domain never reads a clock.
- **Randomness** goes only through `SeededRandom` (SplitMix64), whose state is in the snapshot.
- **Odds disclosure:** `GeneratorDefinition.GetOdds()` returns exact probabilities.
- **Board-full:** spawns and reward claims are refused with `BoardFull`. Every item can be sold (value
  may be 0), so `HasBoardFullResolution()` is true whenever any item is on the board. Rewards wait in
  a queue (without an ID) until there is room, so they are never lost.
- **IDs** are allocated monotonically from `NextItemId`, which is persisted, and never reused.

## Provenance ledger (`Ledger`)

Optional, append-only log. Creations: Load, Spawn, Merge, Reward. Removals: Merge, Sell, Delivery.
Each merge is one operation with two removals and one creation. The ledger checks every record as it
arrives (no ID created twice, nothing removed unless it is live, merge shape valid) and can replay
itself from scratch. Its memory grows with play, so it is meant for tests, simulations and debug
builds, not for whole release sessions.

## Invariants (`InvariantChecker`)

1. Valid transformation of every merge (same chain and tier in, tier+1 out).
2. Provenance: board plus inventory equals the ledger's live set, type for type, both incrementally
   and on full replay.
3. No duplicate IDs.
4. No dangling references (cell, item location and engine index all agree; no orphans).
5. Inventory consistency (capacity respected, nothing in two places); locked cells empty.
6. Merge-value conservation for `strictlyDoubling` chains.

Item-count conservation is deliberately **not** an invariant, because merges reduce the count.

## Configuration

`EngineConfig` = `BoardConfig` + `ChainDefinition[]`. These are plain immutable C# objects with no
defaults in the logic. `ConfigValidator` lists every problem in readable form, and the engine refuses
to start on an invalid config. The data layer (later) maps ScriptableObjects or JSON into these types.

## Snapshot

`EngineSnapshot` is a serializer-friendly DTO (public fields, `[Serializable]`) carrying a version
number. `TryFromSnapshot` validates it fully and returns every error, so the save system (M4) can fall
back to a backup. File I/O, atomic writes and migrations are M4.
