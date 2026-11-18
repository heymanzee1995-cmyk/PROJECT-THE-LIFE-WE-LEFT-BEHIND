# M2: Unity integration and playable board (milestone report)

Phase 2, branch `feature/phase-2-merge-engine`. Not merged; no Phase 3 features (energy, coins wallet,
orders, story, saving, monetisation) were built.

**Overall status:** everything that can be built and checked outside Unity is done and passing. The
Unity-side acceptance (project committed, tests in Unity's runner, playable in the Editor, profiler and
device checks) is **pending you**, because the Unity project is not in the repository yet and I cannot
run the Unity Editor in my environment.

## 1. What was implemented

| Item | Status |
|---|---|
| **Decision 1:** incompatible drop is refused (`IncompatibleTarget`), nothing changes, item bounces back | TESTED (automated) |
| **Decision 2:** incremental recharge, one charge per configurable interval; timer stops at full; no double restore after save/load; backwards clock restores nothing | TESTED (automated) |
| **Resolution 3:** stored item dropped on a board item is rejected and returns to its slot | TESTED (automated) |
| Content data: `MergeContent.json` (approved **5×8** board, bottom row locked, 2 generators, 3 item chains, starting layout) loaded and validated by `Game.Data` | TESTED (automated) |
| Portrait layout and cell-size maths, with safe area and reserved HUD space | TESTED (automated) |
| Tap-versus-drag gesture | TESTED (automated) |
| Presenter: drop and tap outcomes, hover highlights, item info, sell guard for generators | TESTED (automated) |
| Zero allocations on the per-frame drag path (pure C# part) | TESTED (automated, .NET harness) |
| Unity board (`BoardScreen`): placeholder tiles, pooled item views, drag with lift, hover highlight, merge pop, bounce-back with shake, spawn fly-out, inventory row, sell zone, generator charges and countdown, idle merge hint, messages, safe-area and resize relayout, cancel on pause or focus loss, cell-size report | IMPLEMENTED (untested in Unity). Compiles against Unity's engine API (see 3c) |
| Scene builder menu (Tools > The Life We Left Behind > Create Merge Board Scene) | IMPLEMENTED (untested). Needs the Unity Editor |
| PlayMode smoke and allocation tests | IMPLEMENTED (untested in Unity). C# compiles |
| Unity project committed; tests pass in Unity's runner | NOT STARTED: needs you (`Docs/UnitySetup.md`) |
| Playable in the Editor, profiler evidence, device feel | NOT STARTED: needs you (section 6) |

### How it plays (M2 scope)

- 5×8 board. The bottom row is locked and shown as locked; unlocking comes with later progression.
- **Tap a toolbox or the attic trunk** to spawn an item into the nearest free space. Each generator
  shows its charges; when empty it shows the time to the next charge.
- **Drag** an item:
  - onto an identical item to merge it into the next level;
  - onto an empty space to move it;
  - onto anything else to bounce back with a subtle shake and "Those don't match."
- **Storage row** (4 slots) at the bottom: drop board items there; drag them back onto empty spaces.
  Dropping a stored item onto a board item returns it to its slot.
- **Sell** square at the bottom right: drop an item to remove it. It shows the coin value, but there
  is no wallet until M3. Generators cannot be sold.
- After about 8 seconds idle, an available merge gently pulses as a hint.

## 2. Files added and changed

**Engine (changed):**
- `Domain/Board/MergeEngine.cs`, `Results.cs`, `Item.cs`
- `Domain/Config/GeneratorDefinition.cs`, `ConfigValidator.cs`
- `Domain/Persistence/EngineSnapshot.cs`

**Data (new, `Game.Data`, no engine references):**
- `Data/MergeContentData.cs`, `MergeContentMapper.cs`, `ItemCatalog.cs`
- `Content/Resources/MergeContent.json`

**Presentation (new, `Game.Presentation`):**
- Pure logic: `Presentation/Logic/BoardLayout.cs`, `DragGesture.cs`, `BoardPresenter.cs`, `TimeText.cs`, `TextWrap.cs`
- Unity: `Presentation/Unity/BoardScreen.cs`, `ItemView.cs`, `Tweener.cs`, `PlaceholderArt.cs`, `PointerInput.cs`

**Editor (new):** `Editor/BoardSceneBuilder.cs`

**Tests:**
- New: `Tests/EditModePresentation/*` (content, layout, gesture/time/wrap, presenter, allocation)
  and `Tests/PlayMode/BoardSmokeTests.cs`
- Updated: `Tests/EditMode/*`, with the swap and cooldown tests rewritten to the approved rules, not deleted

**Harness:** `tools/DomainHarness/Data/`, `tools/DomainHarness/PresentationLogic/`, test project references

**Docs:** this report, `UnitySetup.md` (packages, input setting, expected counts, scene, Android),
`ApprovedChanges.md`, `DesignDecisions.md`, `Architecture.md`, `README.md`

## 3. Tests and results

### 3a. .NET harness: 150 passed, 0 failed

Run: `dotnet test tools/DomainHarness/Tests/Game.Domain.Tests.csproj`. Builds have 0 warnings with
warnings treated as errors. The tests also compile against the NUnit 3.5 API that Unity's NUnit is
based on.

| Area | Tests | Notes |
|---|---|---|
| Engine (`Game.Domain.Tests`) | 102 | Includes the 8-seed × 10,000-action simulation, which now asserts "no swap ever" and fills the 5×8 board 4–29 times per seed |
| Content | 6 | Real JSON file: 5×8 board, playable start with a merge available, odds sum to 1, names and colours; 16 malformed-content cases |
| Layout | 11 | Cell sizes match `VisionReference.md` within 1 dp; every cell hit-tests to itself; slots and sell zone don't overlap; notch handling; fallback DPI; 7×9 |
| Gesture, time, wrap | 17 | |
| Presenter | 12 | Every drop and tap outcome; every refusal leaves the state byte-for-byte unchanged |
| Allocations (harness only) | 2 | 5,000 drag frames: **0 bytes**. Generator badge refresh: **0 bytes** steady state |

### 3b. Expected in Unity (not yet run)

- **EditMode:** 149 listed, 148 passed, 1 not run (`ReplaySeed`, explicit by design).
- **PlayMode:** 3 passed. The allocation test asserts `Is.Not.AllocatingGCMemory()` across a full
  drag sweep.

### 3c. Unity API compile check (my environment, not committed)

- The runtime presentation code and the PlayMode tests compile with warnings as errors against Unity
  **2021.3** engine reference assemblies, plus small local stubs for the Input System and test-runner
  types.
- This proves there are no C# or API typos. It does **not** prove Unity 6.3 compiles it.
- The Editor script (`BoardSceneBuilder.cs`) could not be checked; no UnityEditor assemblies were
  available.

## 4. What I could NOT verify

- Compilation inside Unity 6.3, and the tests running in Unity's Test Runner.
- Anything visual or felt: animation timing, colours, text legibility, how the drag feels, whether
  the hint helps.
- Placeholder **text rendering**: `TextMesh` with the built-in `LegacyRuntime.ttf` font, under URP 2D.
  Expected to work, but not seen.
- **Sprite lighting:** the scene builder assigns an unlit URP sprite material. If the shader name
  differs in your URP version, sprites may render dark (the builder logs a warning).
- Real touch on a phone, frame rate, memory, load time, safe area on a notched device.
- Profiler evidence of zero GC allocations while dragging, on device (the automated PlayMode test
  covers the Editor).
- Measured cell size in dp on your phone (the on-screen report shows it).

## 5. Known issues and risks

- **Gate not met:** the Unity project is not committed. The milestone cannot be accepted until
  `Docs/UnitySetup.md` steps 1–10 are done and the tests pass in Unity.
- **No saving yet (M4):** every launch starts a fresh board.
- **Wall-clock recharge:** changing the device clock speeds up or slows down recharge. The server-time
  check is planned for Services.
- **Compact 16:9 phones:** cells are about 37 dp, below the 48 dp comfort target. The cell-size report
  flags it ("BELOW 48 dp").
- **Placeholder UI:** text uses `TextMesh` (world space), not uGUI. The real HUD on uGUI with split
  canvases arrives with the top bar in M3.
- **Presentation-only rule:** generators can't be dropped on Sell. The engine still allows selling
  them; this is a guard against trashing a generator by accident. **Needs your approval.**
- **Sell has no confirmation.** A high-level item can be sold with one drop (no wallet yet). Revisit
  with M3.
- **Not included in M2:** no audio or haptics, no locked-cell unlock trigger, no inventory expansion
  button (the engine hook exists).
- The debug ledger runs in Editor and development builds only and grows during a session. Fine for
  testing; it is off in release builds.

## 6. Manual checklist for you

### In the Editor (after `UnitySetup.md` steps 1–9)

1. Console has no errors. Test Runner: EditMode 148 passed and 1 not run; PlayMode 3 passed.
2. Play `MergeBoard` at 1080×2400 (or a Device Simulator phone). You see a 5×8 board, the locked
   bottom row, a storage row with 4 slots and a Sell square. Item names are readable.
3. The cell-size report (top left) shows a dp value. Note it.
4. Drag one Seed Packet onto the other: they merge into a Sprout with a pop.
5. Drag the Sprout onto the Old Toolbox: it bounces back, the toolbox wobbles, and "Those don't match."
   appears.
6. Tap the toolbox 10 times: items fly out. On the 11th tap it wobbles and shows a countdown.
7. Drag an item onto Storage, then back onto an empty space. Drag a stored item onto a board item: it
   returns to its slot.
8. Drag an item onto Sell: it disappears with "Sold … for N coins." Drag the toolbox onto Sell: it
   bounces back.
9. Fill the board with spawns, then tap the toolbox: "The board is full…" appears.
10. **Profiler** (Window > Analysis > Profiler, CPU module, Hierarchy view, sort by GC Alloc): while
    dragging an item around, frames show **0 B GC Alloc** from `BoardScreen`. Take a screenshot as evidence.

### On your Android phone (development build, `UnitySetup.md` step 10)

11. Note the phone model and the reported cell size in dp. Can you hit cells comfortably with a thumb?
12. Drag, merge, bounce, spawn, store and sell all work by touch. The item stays visible above your finger.
13. Mid-drag, press Home, then return: the item is back in place and nothing is duplicated or lost.
14. Rotate or resize (split screen if available): the layout stays portrait and correct.
15. On a notched phone, nothing sits under the notch or gesture bar.
16. Profiler connected to the device: no GC allocations while dragging; note the frame rate.
17. Tell me anything that feels slow, small, confusing or unpleasant.

## 7. Proposed next step

1. You complete `UnitySetup.md` and send:
   - the Unity version;
   - the EditMode and PlayMode counts;
   - any Console warnings;
   - the cell size on your phone;
   - the Profiler screenshot;
   - your notes on the checklist.
2. I fix anything found, and mark the verified items. Only you can confirm VERIFIED (on device).
3. After your approval, M2 is complete. Next would be Phase 3 (economy and orders) on
   `feature/phase-3-economy-orders`, starting with a plan for your approval.
