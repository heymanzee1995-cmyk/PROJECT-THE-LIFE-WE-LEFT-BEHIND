# Approved gameplay changes: implementation plan

Approved on 2026-11-18; open points resolved the same day (see the end of this file).
**Status (M2):**
- Decision 1 (bounce back): engine TESTED (automated); Unity feedback IMPLEMENTED (untested).
- Decision 2 (incremental recharge): engine TESTED (automated); countdown IMPLEMENTED (untested).
- Resolution 3 (stored item onto a board item is rejected): TESTED (automated).
- Decision 3 and resolution 2 (delivery from board and inventory, board first): NOT STARTED (M3).
- Resolution 1 (save-format conditions): NOT STARTED (save system).

Implementation note for Decision 2: whether the timer is running is decided by `charges < max`,
not by a zero start time. A timer that starts at time 0 is therefore valid. The M1 code still implements the old
rules (swap, full refill, board-only delivery), unchanged, so the tests verified in Unity (95 since the 5×8 default board) match
the code you'll verify.

Gate: none of this starts until the Unity project is committed and all 78 EditMode tests pass in
Unity. Each change is then made on its milestone's branch, with tests, and reported. None is made
silently.

## Where each change lands

| Decision | Engine (Game.Domain) | Presentation / feature | Milestone |
|---|---|---|---|
| 1. Bounce back on incompatible drop | `TryDrop` refuses instead of swapping | Bounce-back animation and "invalid merge" feedback | **M2** (first domain step, before the board view) |
| 2. Incremental recharge | New recharge model, config field, snapshot field, queries | Charge count and countdown on generators | **M2** (domain first; M3 tunes values alongside energy) |
| 3. Delivery from board and inventory | `TryDeliver` accepts inventory items | Order eligibility and auto-selection in the order system | **M3** (with orders) |

Order inside M2: (1) domain changes for Decisions 1 and 2, with tests, then (2) the board view. The
view is then never built against rules that are about to change.

---

## Decision 1: incompatible drop returns the item (M2)

### Engine change

- `TryDrop(from, expected, to)` onto an occupied cell whose item cannot merge with the dragged one
  (different chain, different tier, or both at max tier) returns a **refusal**:
  `ActionError.IncompatibleTarget` (new value).
- Like every refusal it leaves state unchanged: no position change, no items created or destroyed,
  no ledger records, no events. Energy is not touched (drops never cost energy).
- `DropOutcome.Swapped` is removed. Drops onto empty cells (move) and valid merges are unchanged.
- Inventory to board onto an occupied cell refuses with `TargetOccupied` (already the engine's
  behaviour). It is **never a merge**. See resolution 3 below for the approved feedback.

### Presentation (M2)

- Every refused drop (`IncompatibleTarget`, `CellLocked`, `InvalidCell`, `TargetOccupied`, outside
  the board) animates the item back to its origin cell.
- `IncompatibleTarget` additionally plays subtle "invalid merge" feedback (for example a short shake
  of both items with an optional soft sound and light haptic, respecting the settings toggles).
  Final look is designed in M2.
- Because the domain never moved the item, the animation is cosmetic. Killing the app mid-bounce
  cannot duplicate or lose anything.

### Consequence to be aware of

With no swap, a **full board with no merge cannot be rearranged**. The player must store, sell or
merge first. This is not a soft-lock (selling is always possible, and it's tested), but the board-full
message in M2 should point to those options.

### Existing tests that encode the old rule (rewritten in M2, not deleted)

Each keeps its scenario and gets the new expected outcome. The test count does not drop.

| Test | Today | After |
|---|---|---|
| `MergeTests.DifferentTiers_Swap` | asserts `Swapped`, items exchanged | refused `IncompatibleTarget`, both items unmoved, state unchanged; renamed `DifferentTiers_BounceBack` |
| `MergeTests.DifferentChains_Swap` | asserts `Swapped` | as above; renamed `DifferentChains_BounceBack` |
| `MergeTests.MaxTierItems_DoNotMerge` | asserts `Swapped` | asserts `IncompatibleTarget`, unchanged |
| `MergeSimulation.Drop` | accepts the swap outcome | expects `IncompatibleTarget` for non-mergeable occupied targets and checks no state change |

### New tests (M2)

- Incompatible drop leaves snapshot, ledger and events untouched: different tier, different chain,
  max tier, generator onto item, item onto generator.
- Two identical generators still merge; a generator onto a different generator bounces.
- Full board with no merge: every board-to-board drop is refused; sell or store frees a cell; then a
  move works.
- Simulation: no swap outcome ever appears, and every incompatible drop is checked for no state change.

---

## Decision 2: incremental recharge (M2)

### Rule

- A generator restores **one charge per `RechargeIntervalMs`**, up to `MaxCharges`.
- The timer runs only while the generator is below full, and stops at full.
- It starts when a charge is spent from full. If more charges are spent while it's running, the
  running timer keeps going (spending does not reset it).

### Engine change

- **Config:** `GeneratorDefinition.CooldownMs` is replaced by **`RechargeIntervalMs`** (validated
  `> 0`; 0 is refused because it would mean infinite charges). It remains data-driven.
- **State:** `Item.GeneratorReadyAtMs` is replaced by **`GeneratorRechargeStartMs`**: when the charge
  currently recharging started, or 0 when full.
- **Settling** (internal, deterministic) at a command time `now`:
  `restored = clamp((now - start) / interval, 0, max - charges)`;
  `charges += restored`; `start += restored * interval`; if full, the timer stops (`start = 0`).
  - The start time advances by whole intervals, not to `now`, so partial progress is kept.
  - Restoring is computed from saved values, never accumulated, so **save/load cannot restore a
    charge twice**. Reloading any number of times at the same `now` gives the same result.
  - A clock set backwards (`now < start`) restores nothing and does not move the timer.
- **Tap:** settle, then if `charges == 0` refuse with `GeneratorOnCooldown`, otherwise spend one
  charge. If the generator was full, start the timer at `now`. A refused tap still changes nothing:
  settling is computed, not stored, unless the tap succeeds. This keeps the "refusal leaves state
  unchanged" guarantee.
- **Queries for the UI (no state change):** `GetAvailableCharges(item, now)`,
  `GetNextChargeInMs(item, now)` (0 when full) and `GetFullRechargeInMs(item, now)`.
  `SpawnResult.CooldownRemainingMs` becomes the time to the next charge.
- **Merged and rewarded generators** start full with the timer stopped (unchanged).
- **Snapshot:** `ItemRecord.GeneratorReadyAtMs` becomes `GeneratorRechargeStartMs`. Validation:
  `0 <= charges <= max`; `start == 0` exactly when `charges == max`; `start >= 0`.
  - Version: **stays at version 1** (approved), with the save-format conditions in resolution 1 below.

### Existing tests that encode the old rule (rewritten in M2, not deleted)

| Test | After |
|---|---|
| `GeneratorTests.SpendingAllCharges_StartsCooldown_ThenRefillsToFull` | becomes `Recharge_RestoresOneChargePerInterval_AndStopsWhenFull` |
| `GeneratorTests.CooldownSurvivesSnapshot` | becomes `RechargeProgressSurvivesSnapshot` |
| `GeneratorTests.Tap_SpawnsIntoNearestEmptyCell_AndSpendsOneCharge` | asserts the timer starts at tap time instead of `ReadyAt == 0` |
| `GeneratorTests.Rolls_FollowConfiguredWeights` | waits one interval per empty generator instead of a cooldown |
| `EdgeCaseTests.E02` | asserts "timer not started" instead of "no cooldown started" |
| `EdgeCaseTests.E02b` | refuses at 0 charges, then is checked again one interval later (one charge, then BoardFull) |
| `ConfigValidatorTests.RejectsBadGenerators` | checks that `RechargeIntervalMs <= 0` is rejected |
| `SnapshotTests.CorruptSnapshots_AreRejectedWithClearErrors` | adds the cases "timer set while full" and "timer missing while below full" |
| `TestConfigs` | `ToolboxCooldownMs` becomes `ToolboxRechargeMs`, and `Canon` uses the new field |
| `MergeSimulation` | tap check uses the next-charge time; snapshot deep copy uses the new field |

### New tests (M2)

- One charge per interval: after 0.99 intervals nothing is restored, after 1 interval one charge,
  after 2.5 intervals two (with half an interval of progress kept).
- Stops at full: a long absence restores exactly up to max, and the timer reads 0.
- Spending from full starts the timer; spending below full does not reset it.
- **No duplicate restoration:** save mid-interval, then reload repeatedly at the same time and at
  later times. Charges always equal the single-engine result.
- Clock backwards: nothing restored, no exception, the timer keeps its start.
- Remaining-time queries: next charge and full recharge, at several times.
- Refused tap at 0 charges changes nothing (snapshot, ledger, events).
- Simulation: compares every generator's charges against an independent re-calculation after every
  reload.

---

## Decision 3: delivery from board and inventory (M3)

### Engine change

- `TryDeliver(ids)` accepts items located on the board **or** in the inventory. All other rules
  stay: ids must exist and be distinct, the list must be non-empty, and removal is all or nothing.
- Each removed item gets one `Delivery` removal record in the ledger, whichever area it came from.
- `ActionError.ItemNotOnBoard` is no longer returned for inventory items. It is kept only if M3 adds
  a location that can't deliver.

### Order system (M3)

- Order requirements (type × count) come from data.
- **Eligibility and delivery use the same rule:** an order is "ready" only if board plus inventory
  can satisfy it, and delivery uses exactly that rule.
- **Auto-selection (approved):** eligible **board** items first, lowest cell index first; then
  eligible **inventory** items, lowest slot first. See resolution 2 below.
- If the requirements can't be met at the moment of delivery (for example, an item was sold in
  between), nothing is removed and the order stays open.
- Generators are only deliverable if an order explicitly requires them. Otherwise they are never
  auto-selected.

### Existing tests that encode the old rule (rewritten in M3, not deleted)

| Test | After |
|---|---|
| `InventorySellDeliverRewardTests.Deliver_RemovesAllOrNothing` | the inventory item becomes a valid delivery; the all-or-nothing case uses an unknown or duplicate ID instead |
| `EdgeCaseTests.E10_DeliverItemsAfterSaveLoad` | a stored item delivered after reload succeeds and leaves the inventory consistent |
| `MergeSimulation.Deliver` | inventory items join the valid set; the corrupt-input case becomes "sold item" |

### New tests (M3)

- Mixed delivery (board plus inventory) removes exactly those items; board and inventory counts and
  slots stay consistent; ledger has one `Delivery` record each.
- Failure anywhere in the list removes nothing, from either area.
- After delivery, the freed inventory slot and board cell are reusable.
- Order "ready" check and delivery agree in randomised tests.
- Save/load between "order ready" and "deliver".

---

## Resolutions (approved 2026-11-18)

### Resolution 1: save format stays at version 1, with conditions

- The version is **explicitly stored** in every save. `EngineSnapshot.Version` is already serialized.
- The loader **validates** it. `TryFromSnapshot` already rejects any version other than the current one.
- **A missing version must never be read as the current version.** Today `EngineSnapshot.Version`
  has the field initializer `= CurrentVersion`. A serializer that constructs the object and then
  finds no `Version` in the file would silently read version 1. **Required fix (save-system work,
  not done yet):** the save loader reads the version field from the raw file first and treats
  "absent" as a corrupt save. A test feeds a save with the version removed and expects a rejection.
  The field default should also become 0 (invalid), so the engine-level check catches it too.
- **Before any save file is written to a player's device:** version migrations (a chain of
  `vN → vN+1` steps, each with its own test), backup recovery (temp-file write then rename, one
  backup kept, fall back to it on a failed load) and corrupted-save handling (truncated, malformed
  or invalid content is rejected with a reason, never a crash).
- No save files exist yet. Snapshots are only in memory, so there is no risk until the save system
  is built.

### Resolution 2: delivery picks board items first, then inventory

- **Selection order:** eligible board items (lowest cell index first), then eligible inventory items
  (lowest slot first). Deterministic, so the same state always selects the same items.
- **Engine shape (M3):**
  - **`PlanDelivery(requirements)`** is a read-only query that returns either the exact item IDs
    that would be consumed (with their locations) or "cannot fulfil" with what's missing. It changes
    nothing.
  - **`TryDeliver(ids)`** removes exactly those IDs, all or nothing. The order system calls it with
    the planned IDs only after the player confirms.
- **Preview before confirming (M3 UI):** the order panel shows which items will be consumed,
  highlighting the board cells and inventory slots, before the Deliver confirmation.
- **Atomic:** if anything changed between preview and confirmation (an item sold, merged or moved),
  `TryDeliver` refuses, **nothing is removed**, and the UI re-plans and shows the new preview.
- **New tests (M3):**
  - Board-first priority, including a mix of board and inventory.
  - Inventory is used only for the shortfall.
  - Deterministic choice among equal items.
  - "Cannot fulfil" removes nothing.
  - A stale plan (state changed after planning) is refused with nothing removed.
  - Board and inventory counts stay consistent; one `Delivery` ledger record per item.

### Resolution 3: inventory item dropped onto a board item is rejected

- **No direct merge from the inventory** (for now). Dropping an inventory item onto any occupied
  board cell, even an identical mergeable item, is refused with `TargetOccupied`.
- Effects: the item **returns to its original inventory slot**; board unchanged; inventory unchanged;
  no energy consumed; no ledger record; no events. The engine already behaves this way today.
- **M2 presentation:** return animation to the slot plus the same subtle invalid-action feedback as
  Decision 1.
- **New tests (M2):** an inventory item onto an identical mergeable board item, onto a different
  item, and onto a generator. Each is refused, the snapshot is identical and no ledger records or
  events occur. Energy is checked once it exists (M3). The existing
  `MoveFromInventory_RefusesOccupiedLockedAndStale` already covers the snapshot part for one case.
- Revisit direct inventory merging after the core board interactions have been validated on a device.

## Summary of what changes where

| Area | M2 (after Unity verification and plan approval) | M3 | Save-system work (before saves ship) |
|---|---|---|---|
| Engine | Bounce-back refusal; incremental recharge; inventory-drop tests | `PlanDelivery`, inventory delivery | Raw-file version check, version default 0, migrations, backup, corruption handling |
| UI | Bounce and invalid feedback; charge countdown | Delivery preview and confirm | — |
| Tests | Rewrite 3 swap tests, 8 cooldown tests and the simulation parts; add new ones | Rewrite 2 delivery tests and the simulation part; add new ones | Migration test per step; missing-version; corrupt and backup recovery |
