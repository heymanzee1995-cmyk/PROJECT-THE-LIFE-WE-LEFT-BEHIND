# M1 review and Unity integration preparation

Branch: `feature/phase-2-merge-engine`. M1 is provisionally accepted. **M2 has not been started.**
No branches were merged or deleted.

## 1. What was done

| Task | Status |
|---|---|
| 1. Edge-case tests (all 10 requested scenarios, plus 7 related cases) | TESTED (automated) |
| 2. Design-assumption review | Done: `Docs/DesignDecisions.md`. All three rules kept, decisions pending |
| 3. Unity compatibility inspection | Done: `Docs/UnityCompatibility.md`. Nothing blocks integration |
| 4. Unity project setup instructions | Done: `Docs/UnitySetup.md`. Nothing done in Unity on your behalf |
| Unity EditMode run | NOT VERIFIED: needs you (see `UnitySetup.md` step 6) |

## 2. Files added / changed

- **Added:** `Assets/Game/Tests/EditMode/EdgeCaseTests.cs`, `Docs/DesignDecisions.md`,
  `Docs/UnityCompatibility.md`, `Docs/UnitySetup.md`, this report.
- **Changed:**
  - `Assets/Game/Domain/Board/MergeEngine.cs`: `CopyItems` now uses a deterministic order. No
    gameplay change.
  - `Assets/Game/Domain/Diagnostics/InvariantChecker.cs`: removed a loop that this change made
    meaningless. Orphan detection is unchanged and is explained in a comment.
  - `tools/DomainHarness/Tests/Game.Domain.Tests.csproj`: added an NUnit version switch for the 3.5
    API check.
  - `.gitignore`: ignores the command-line test output files.
  - `README.md`: points to the new setup guide and corrects the old copy instructions.

## 3. Tests and results

**78 tests (was 61), 78 passed, 0 failed.** Run with `dotnet test`, NUnit 3.14, .NET 8. The build
has 0 warnings with warnings treated as errors. The explicit `ReplaySeed` helper is not counted.

The same tests compile against **NUnit 3.5.0**, the version Unity's bundled NUnit is based on.

New tests in `EdgeCaseTests` (17). The full board uses all 63 cells of a 7×9 board with no merge
available.

| # | Requested case | Tests |
|---|---|---|
| 1 | Completely full 7×9 board | `E01`: 63/63 items, no empty cell, no merge, resolution path exists, invariants hold |
| 2 | Generator with no free cell | `E02`: ten refused taps with no change to charges, cooldown, RNG, items, ledger or events. `E02b`: a depleted generator reports its cooldown first, then BoardFull |
| 3 | Reward queue on a full board | `E03`: invalid rewards refused; claims refused while full; FIFO order kept; claimed generators arrive full |
| 4 | Inventory exhaustion | `E04`: refused until expanded, then refused at max capacity. `E04b`: full board and full inventory together, still resolvable by selling |
| 5 | Selling to recover space | `E05`: frees exactly that cell, correct value, Sell ledger record; a second sell is refused |
| 6 | Generator after space frees | `E06`: spawn lands in the only free cell (far corner), IDs continue, board full again. `E06b`: freeing space by merging also works |
| 7 | Invalid and interrupted drags | `E07`: out-of-range, same cell, stale, None and future IDs, empty inventory slot. `E07b`: locked target or source, empty source. `E07c`: kill mid-drag, reload, resume the drop, then a duplicate drop is refused. `E07d`: token goes stale after the item is merged or stored |
| 8 | Save/restore a full board | `E08`: identical state, one Load record per item, still refuses spawns, and both engines continue identically afterwards |
| 9 | Restore with queued rewards | `E09`: queue order survives two reloads; reward IDs continue from the saved counter. `E09b`: corrupt queued rewards are rejected |
| 10 | Delivery after save/load | `E10`: pre-save IDs stay valid; unknown, stored and duplicate IDs are refused; delivery succeeds and survives another reload |

**Every refused operation in these tests goes through one helper.** It asserts the expected error, an
identical snapshot, no new ledger records and no events.

**Self-check:** I planted a bug where a refused full-board tap consumes RNG. 5 of the 17 new tests
failed as expected. The engine was then restored (verified: no diff).

## 4. Design-assumption review (summary)

Full analysis is in `Docs/DesignDecisions.md`. No defects were found in the three rules, and none
were changed.

| Rule | Main consequence | Recommendation (needs your decision) |
|---|---|---|
| Non-matching drop swaps | Works on a full board, but mis-drops rearrange items; identical max-tier swaps look like nothing happened | Keep it; give clear "can't merge" feedback in M2; revisit after playing on your phone |
| Generators refill all charges at once, only when empty | **Stranded-charge trap:** one leftover charge never recharges. Also two clocks (energy plus cooldown) | Decide before M3. I lean toward a trickle recharge (one charge per interval) for fairness |
| Deliveries use board items only | Extra steps when the order item is in storage and the board is full (never a soft-lock); the M3 order UI must use the same rule | Allow inventory deliveries in M3 |

## 5. Remaining edge cases (not yet covered)

- **Content updates versus old saves (important for M4).** A snapshot is rejected outright if a
  content update removes a chain or tier, lowers a generator's max charges, or changes the board size.
  That would lose the player's board. M4 migrations must transform these saves, not just reject them.
- **Clock going backwards or extreme timestamps.** Not explicitly tested. A backwards clock only
  delays a refill. `nowMs + CooldownMs` could overflow near `long.MaxValue`, which is unrealistic,
  but there is no guard.
- **Re-entrant commands from event handlers.** Not tested. M2 rule: queue them, don't nest them.
- **Mid-command process kill.** Commands are synchronous and in-memory. Durability depends on the
  M4 atomic save design.
- **Ledger memory in long sessions.** The ledger is debug/test only; this needs a decision in M2 for
  debug builds.
- **Special items** (wildcards, bonus items, bubbles) and **order-aware hints** do not exist yet, so
  they are untested.

## 6. Unity compatibility findings (summary)

Full review is in `Docs/UnityCompatibility.md`.

- **C# 9:** OK. No unsupported features (records/init, covariant returns, module initializers,
  function pointers, nint) and no C# 10+ syntax.
- **.NET Standard 2.1:** OK. Compiled against netstandard2.1. No reflection, threading, LINQ, Task or
  dynamic in the domain.
- **Serialization:** OK for M1. M4 must read the snapshot `Version` explicitly (a field initializer
  could mask a missing version). The data layer will need ScriptableObject authoring types for config.
- **Assembly definitions:** OK as written. Depends on the Test Framework package.
- **Determinism:** OK after the `CopyItems` fix. Integer-only gameplay, own RNG with a known-answer
  test, no clock or static state.
- **Must change before integration:** nothing in the code. The setup instructions now copy the
  template's `Assets/Settings` (URP assets), which my earlier README omitted.

## 7. Exact Unity setup steps

See **`Docs/UnitySetup.md`**. In short:

1. Install **Unity 6.3 LTS (6000.3.x, latest patch)** with Android Build Support (OpenJDK, SDK &
   NDK). Unity 6.0 LTS support ended in October 2026, so don't use it.
2. Create a throwaway **Universal 2D** project outside the repo, check URP is active, then close it.
3. Copy its `Packages/`, `ProjectSettings/` and **all of `Assets/` with the `.meta` files** into the
   repo root. Don't copy `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `.csproj` or `.sln` files.
4. Open the repo in Hub. Check the Console has no errors, Test Framework is installed, meta files are
   visible, serialization is Force Text, and the API level is .NET Standard 2.1.
5. Check the two asmdefs in the Inspector. There is nothing to create; they already exist.
6. Test Runner > EditMode > Run All. **Expect 78 passed.**
7. `git status`: only Assets, Packages and ProjectSettings with `.meta` files, and no Library.
   Run the missing-meta check.
8. Commit on `feature/phase-2-merge-engine`, with your Unity version and the test result in the
   message, and push.

## 8. M2 readiness assessment

**Not ready to start. Waiting on you.**

| Gate | Status |
|---|---|
| Domain engine tested outside Unity | Done (78/78) |
| Unity project files committed | **Pending:** `UnitySetup.md` steps 1–8 |
| Domain tests pass in Unity's EditMode runner | **Pending:** step 6 |
| Engine API covers M2 needs (drop, tap, inventory, sell, board-full, hints, events) | Yes |
| Decision: swap versus bounce on a non-matching drop | **Needed before M2**, because it shapes M2's drop animation and feedback |
| Decisions: generator recharge model, inventory deliveries | Needed before M3, not M2 |

Once you push the Unity project and report the EditMode result, I'll pull it, check the commit
(meta files, no Library, the URP assets present), and then propose the M2 plan for your approval
before writing any code.
