# The Life We Left Behind

An original narrative merge-2 game for Android (Unity 6, URP 2D, portrait). Emma Williams returns to
Willowbrook to restore her late grandmother's home and uncovers a family mystery.

This is an original game. It does not copy any other game's characters, art, branding, dialogue, UI
layouts, names or storylines.

## Status

| Phase | Branch | Status |
|---|---|---|
| Merge engine (pure C#) | `feature/phase-2-merge-engine` | TESTED (automated), awaiting review |
| UI prototype / board view | `feature/phase-1-ui-prototype` | NOT STARTED |

The engine was built before the UI prototype (agreed order), so the UI shows real rules and not
throwaway logic. See `Docs/MilestoneReports/` for each milestone's report.

## Layout

```
Assets/Game/Domain/          Game.Domain assembly: pure C#, no UnityEngine (noEngineReferences)
Assets/Game/Tests/EditMode/  Game.Domain.Tests: NUnit EditMode tests + randomised simulation
tools/DomainHarness/         .NET projects that compile and run the same files outside Unity
Docs/                        Architecture, milestone reports
```

## Opening in Unity (first time)

The repo has no Unity project files yet. To create them:

1. In Unity Hub create a new **Universal 2D** project with Unity 6 in an empty folder.
2. Close Unity. Copy that project's `Packages/` and `ProjectSettings/` folders into this repo root.
3. Open this repo folder in Unity Hub (*Add > Add project from disk*).
4. Run **Window > General > Test Runner > EditMode > Run All**.
5. Commit the generated `.meta` files, `Packages/` and `ProjectSettings/`.

Recommended for fast iteration on a modest machine: *Project Settings > Editor > Enter Play Mode
Options*, set to "Do not reload Domain or Scene".

## Running the domain tests without Unity

Requires the .NET 8 SDK.

```bash
dotnet test tools/DomainHarness/Tests/Game.Domain.Tests.csproj
```

The harness compiles `Game.Domain` with C# 9 against .NET Standard 2.1, which matches Unity 6's
limits, and treats warnings as errors.
