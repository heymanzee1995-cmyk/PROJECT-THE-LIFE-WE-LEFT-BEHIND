# Unity compatibility review: Game.Domain and Game.Domain.Tests

Reviewed for Unity 6.3 LTS (6000.3.x). **Status: reviewed and checked outside Unity. NOT yet compiled
or run by Unity itself.**

## Summary

Nothing has to change in the engine code before Unity integration. One robustness fix was made
(deterministic `CopyItems` order). One correction was made to the setup instructions: the template's
URP assets must be copied too (see `UnitySetup.md`). There are two follow-ups for later milestones.

## 1. C# 9 compatibility: OK

- The harness compiles `Game.Domain` and the tests with `LangVersion 9.0`, and treats warnings as
  errors. Any newer syntax would fail the build.
- Unity 6 supports C# 9 except for a few features. Source scan results:
  - **None used:** `init` accessors and `record` types (both need `IsExternalInit`), covariant
    returns, module initializers, function pointers, `nint`/`nuint`.
  - **None used (C# 10+):** file-scoped namespaces, global usings, `required`, raw string literals.
- Used and supported: expression-bodied members, `out var`/`out _`, `is` patterns, `default`
  literals, local functions (tests), `using static` (tests), readonly structs.

## 2. .NET Standard 2.1 compatibility: OK

- `Game.Domain` is compiled in the harness against `netstandard2.1`, the same API surface as Unity's
  default *Api Compatibility Level: .NET Standard 2.1*.
- APIs used: `System.Collections.Generic`, `Array.Empty`, `HashCode.Combine` (included in Unity's
  .NET Standard 2.1 profile), `StringComparer.Ordinal`, `Math`. No LINQ, reflection, threading,
  `Task`, `dynamic`, `DateTime`, `Environment`, `Span`, `stackalloc` or `unsafe` in the domain.
- IL2CPP: no reflection or generic virtual methods, so nothing for code stripping to remove.

## 3. Unity serialization: OK for M1, two notes for later

- `EngineSnapshot`, `ItemRecord` and `ItemTypeRecord` are `[Serializable]` with public fields of
  supported types (`int`, `long`, `bool`, `string`, arrays of serializable classes). The RNG state is
  stored as `long`, not `ulong`, so any serializer handles it. No dictionaries, nested generics or
  properties.
- JsonUtility has no null array elements. A missing record would deserialize as a default object
  (`Id = 0`), which snapshot validation rejects. This is the safe behaviour.
- **Note for M4 (save system):** `EngineSnapshot.Version` has a field initializer
  (`= CurrentVersion`). If a serializer constructs the object and the JSON has no `Version` field,
  the value will read as the current version instead of "unknown". The M4 loader must read the
  version explicitly before migration and must not rely on the default. This will be covered by a
  migration test in M4.
- **Note for M2/M3 (data layer):** the config types (`ChainDefinition`, `GeneratorDefinition`,
  `BoardConfig`, `EngineConfig`) are immutable with read-only properties, so the Unity Inspector cannot
  show or edit them, by design. `Game.Data` will provide ScriptableObject authoring types and map them
  into these, running `ConfigValidator` at load.

## 4. Assembly definitions: OK

| asmdef | Key settings | Why |
|---|---|---|
| `Assets/Game/Domain/Game.Domain.asmdef` | `noEngineReferences: true`, all platforms, `autoReferenced: true` | Compiler-enforced "no UnityEngine". Included in player builds. |
| `Assets/Game/Tests/EditMode/Game.Domain.Tests.asmdef` | Editor only; references `Game.Domain`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`; `overrideReferences` with `nunit.framework.dll`; `defineConstraints: UNITY_INCLUDE_TESTS` | Standard EditMode test assembly. Never in player builds. |

- References are by assembly name, which Unity accepts. Unity may convert them to GUIDs if you edit
  the asmdef in the Inspector. Either form is fine; commit whatever Unity writes.
- Requires the Test Framework package (`com.unity.test-framework`), which normally ships with the
  templates. `UnitySetup.md` step 4 confirms it.
- `tools/DomainHarness` is outside `Assets/`, so Unity ignores it.

## 5. NUnit version: OK, with one residual risk

- Unity's test runner uses `com.unity.ext.nunit`, a custom build based on NUnit 3.5. The harness
  compile-checks the tests against NUnit 3.5.0:
  `dotnet build tools/DomainHarness/Tests/Game.Domain.Tests.csproj -p:NUnitVersion=3.5.0` succeeds.
  The tests run on NUnit 3.14, because 3.5 cannot execute on .NET 8.
- **Residual risk:** Unity's fork may differ slightly from upstream 3.5. Only running the tests in
  Unity settles this.

## 6. Deterministic behaviour across runtimes: OK

Mono (Editor), IL2CPP ARM64 (device) and CoreCLR (harness) must produce identical results for the
same seed and actions. Status of each risk:

| Risk | Status |
|---|---|
| RNG algorithm | Own SplitMix64 using integer math only, with a known-answer test (`KnownFirstValue_IsStableAcrossPlatforms`) |
| Floating point | Not used in gameplay. `double` appears only in `GetOdds()` for display |
| Dictionary/HashSet iteration order | **Fixed:** `CopyItems` was the only such iteration; it now walks cells and slots. Hash containers are used only for lookups |
| String hashing (randomised on .NET, not on Mono) | Only affects lookups, never order |
| Culture | Gameplay never formats or parses strings. Error messages only |
| Time | Caller supplies milliseconds; the domain never reads a clock |
| Static mutable state | None, so it is safe with *Enter Play Mode: no domain reload* |

## 7. Performance notes for the Editor

- The simulation tests (9 × 10,000 steps with an invariant check each step) take about 1 s on .NET 8.
  Mono in the Editor is slower; expect roughly 5–20 s on a Ryzen 5000 machine. That is fine for
  EditMode tests.
- The engine has no `Update` loop and allocates nothing in its query methods. `TryDeliver` allocates
  a small array per call (not a per-frame path).

## Must change before Unity integration

Nothing in the code. Before you open the project, the only setup point to know is that the
template's `Assets/Settings` (URP assets) must be copied along with `Packages/` and `ProjectSettings/`.
The previous README said to copy only the latter two, which would leave URP unassigned.
