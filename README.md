# The Life We Left Behind

An original narrative merge-2 game for Android (Unity 6, URP 2D, portrait). Emma Williams returns to
Willowbrook to restore her late grandmother's home and uncovers a family mystery.

This is an original game. It does not copy any other game's characters, art, branding, dialogue, UI
layouts, names or storylines.

## Status

| Phase | Branch | Status |
|---|---|---|
| Merge engine (pure C#) | `feature/phase-2-merge-engine` | TESTED (automated) outside Unity; provisionally accepted; Unity EditMode run pending |
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

The repo has no Unity-generated files yet. Follow **`Docs/UnitySetup.md`** (Unity 6.3 LTS,
Universal 2D template). Note that the template's `Assets/Settings` URP assets must be copied along
with `Packages/` and `ProjectSettings/`.

## Running the domain tests without Unity

Requires the .NET 8 SDK.

```bash
dotnet test tools/DomainHarness/Tests/Game.Domain.Tests.csproj
```

API check against NUnit 3.5 (the version Unity's bundled NUnit is based on; compile only):

```bash
dotnet build tools/DomainHarness/Tests/Game.Domain.Tests.csproj -p:NUnitVersion=3.5.0
```

The harness compiles `Game.Domain` with C# 9 against .NET Standard 2.1, which matches Unity 6's
limits, and treats warnings as errors.
