# Design decisions under review

Three gameplay rules in the M1 engine were my assumptions, not specified rules. None is a code defect.
All three stay as implemented until you decide otherwise; changing any of them is a deliberate
gameplay change with its own tests, never a silent one.

Status: **all three KEPT, awaiting your decision.**

---

## 1. A non-matching drop swaps the two items

**Rule:** dropping item A onto a different item B (different chain, different tier, or both at max
tier) exchanges their cells. Drops onto empty cells move; identical items below max tier merge.

**Consequences**

- **Works on a full board.** Swapping needs no free cell, so players can always rearrange a packed
  board to line up merges. A "bounce back" rule would give the same result there, but a "push to the
  nearest free cell" rule would fail on a full board.
- **A mis-drop rearranges the board.** A drop that lands one cell off moves the target item to where
  the dragged item came from. With 7 columns on a phone, cells are small (to be measured in M2), so
  this will happen. Players can undo it with one more drag, but it can feel unpredictable.
- **Identical max-tier items swap.** The board looks unchanged, but two move events fire. M2 must
  animate this as a "can't merge" response, or players will think the drop was ignored.
- **Generators swap like items.** A generator can be moved by dropping onto it. This is intended:
  generators should be movable.
- **No hidden state.** IDs are unchanged and nothing is created or destroyed, so the ledger and
  invariants are unaffected (covered by the simulation).

**Alternatives if you want to change it:** bounce back (the dragged item returns, nothing else moves),
or swap only when the target is adjacent. Either is a small change in `MergeEngine.TryDrop` plus tests.

**My recommendation:** keep swap, and make the M2 feedback clear (a visible "can't merge" wobble on
max-tier swaps). Revisit after you've played it on your phone.

---

## 2. Generators refill all charges at once after a cooldown

**Rule:** a generator holds `MaxCharges`. Each tap spends one. When it reaches **zero**, it cools down
for `CooldownMs` and then refills to full. While it has any charges left, it does not recharge.

**Consequences**

- **"Stranded charge" trap (the main risk).** A player who leaves a generator at 1 charge and returns
  the next day still finds 1 charge, because the cooldown only starts at zero. Players may read this as
  a bug. Mitigation, if kept: show "spend the last charge to start the refill" in the UI.
- **Encourages bursts.** Play becomes "tap until empty, wait, repeat". Combined with energy (M3) there
  are two clocks gating the same action. Energy should be the main pacing limit, and generator
  cooldown a secondary rhythm, or players feel double-taxed.
- **Merging or rewarding a generator gives full charges.** Merging two depleted generators yields a
  full one, skipping both cooldowns. This is generous but requires two same-tier generators, so I
  consider it acceptable, maybe even a nice reward. Tell me if it should carry the cooldown over.
- **Clock changes.** The refill time is an absolute timestamp. Moving the device clock forward
  refills instantly; moving it back delays the refill. The server-time check is planned for Services.
- **Persistence is simple.** Only two numbers per generator (charges and ready-at time), so saves stay
  small and migrations stay easy.

**Alternative:** trickle recharge, one charge per interval, which starts as soon as a charge is
spent. It removes the stranded-charge trap and is common in the genre. The cost is a small engine
change, new config fields, new tests and a snapshot field (a migration once saves exist in M4, so
cheaper to decide now).

**My recommendation:** decide before M3 balancing. I lean toward trickle recharge for fairness, which
fits the "fairer energy" goal in the master instructions, but it is your call.

---

## 3. Deliveries use board items only, not inventory items

**Rule:** `TryDeliver` removes a set of items that must all be on the board. An item in the inventory
must first be moved back onto the board.

**Consequences**

- **Friction when the board is full.** If the order item is in storage and the board is full, the
  player must sell or merge to free a cell, move the item out, then deliver: three steps where one
  would do. It is **never a soft-lock**, because selling always frees a cell (covered by the tests).
- **The orders UI (M3) must match the rule.** If an order shows "ready" because an inventory item
  matches, delivery will fail with `ItemNotOnBoard`. The order check and the delivery command must use
  the same rule.
- **Board stays the focus.** Players see what they deliver, and storage stays storage. Some players
  will treat inventory as a staging area for orders and find this annoying.
- **No invariant impact.** Delivery is atomic either way: all items removed or none.

**Alternative:** let deliveries take matching items from the inventory as well, either automatically
or by player choice. It is a small engine change (allow inventory locations in `TryDeliver`) plus
tests, and the M3 order check would use the same rule.

**My recommendation:** allow inventory deliveries when building orders in M3. It removes friction
with no fairness downside. I have not changed it: it needs your approval.

---

## Defects found during this review

- **Fixed:** `MergeEngine.CopyItems` returned items in `Dictionary` order, which is not guaranteed to
  match between Mono, IL2CPP and .NET. No gameplay depended on that order, but a future caller could
  have. It now returns board cells in index order, then inventory slots. **Gameplay rules are
  unchanged.**
- **Guidance, not a defect:** engine events fire synchronously, after the state change completes. A
  listener that issues another engine command from inside an event handler will run before the
  remaining events of the first command fire. M2 presentation code must queue follow-up commands, not
  call them from inside handlers. This will be documented as a rule in M2.
