# Unity project setup (one time)

Goal: turn this repository into a real Unity 6 Universal 2D project, run the domain EditMode tests
in Unity, and commit the result. Written for Windows with Unity Hub and Git (Git Bash or PowerShell).

Nothing here has been done for you: the repository contains no Unity-generated files yet.

---

## 0. Before you start

- Close any other Unity Editor that has this repository open.
- Free disk space: about 10 GB for the Editor and Android modules, plus about 2 GB for the project's
  `Library/`.
- Get the repository and the engine branch onto your machine:

```bash
git clone https://github.com/heymanzee1995-cmyk/PROJECT-THE-LIFE-WE-LEFT-BEHIND.git
```

```bash
cd PROJECT-THE-LIFE-WE-LEFT-BEHIND
```

```bash
git checkout feature/phase-2-merge-engine
```

If you already have a clone, run `git fetch origin` and then `git checkout feature/phase-2-merge-engine`
and `git pull` instead.

The Unity project files will be committed **on `feature/phase-2-merge-engine`**, because that is the
branch whose tests must pass in Unity before M1 is fully accepted. Nothing gets merged into `main`
without your approval.

## 1. Choose the Unity version

Use **Unity 6.3 LTS (6000.3.x), latest patch** shown in Unity Hub.

- 6.3 LTS is supported until December 2027. Unity 6.0 LTS support ended in October 2026, so do not
  start a new project on 6000.0.
- Avoid alpha/beta builds and non-LTS update releases for this project. LTS is the stable target to
  ship on.
- Write the exact version (for example `6000.3.Nf1`) in the commit message in step 8. Everyone on the
  project must use that same version; Unity records it in `ProjectSettings/ProjectVersion.txt`.

In **Unity Hub > Installs > Install Editor**, pick the latest 6000.3 LTS and add these modules:

- **Android Build Support**, including **OpenJDK** and **Android SDK & NDK Tools**. Not needed for
  the tests, but needed from M2 onward for your phone.
- Microsoft Visual Studio Community, or your preferred IDE (optional).

## 2. Create a throwaway template project

Unity Hub creates projects in a new empty folder, so create the template somewhere **outside** the
repository first.

1. Unity Hub > **Projects > New project**.
2. Editor version: the 6000.3 LTS you just installed.
3. Template: **Universal 2D** (it may be labelled "2D (URP)"). Download the template if Hub asks.
4. Project name: `LifeTemplate`. Location: a scratch folder, for example `C:\Temp\`.
5. Leave "Connect to Unity Cloud" / Version Control **off**. The repository uses GitHub.
6. Click **Create project** and wait for the Editor to finish importing.
7. Confirm URP is active: **Edit > Project Settings > Graphics > Default Render Pipeline** shows a
   Universal Render Pipeline asset (not "None").
8. **Close the Editor** (File > Exit). Files must not be copied while Unity has them open.

## 3. Copy the template into the repository

Copy these from `C:\Temp\LifeTemplate\` into the repository root:

| Copy | Why |
|---|---|
| `Packages\` (whole folder, including `packages-lock.json`) | Package list: URP, 2D, Input System, Test Framework… |
| `ProjectSettings\` (whole folder) | Project settings, including the Graphics settings that point at the URP asset |
| **Everything inside `Assets\`, including every `.meta` file** | `Assets\Settings\` holds the URP pipeline and 2D renderer assets that `ProjectSettings` references by GUID. Without them URP silently falls back to unassigned. Also scenes and input actions |

Do **not** copy `Library\`, `Temp\`, `Logs\`, `UserSettings\`, `obj\`, `*.csproj` or `*.sln`. They
are generated per machine and are git-ignored.

The repository already has `Assets\Game\`. The template has no `Game` folder, so nothing clashes.
If Explorer asks to merge `Assets`, choose merge, not replace.

PowerShell equivalent, run from the repository root (adjust the source path):

```powershell
$src = "C:\Temp\LifeTemplate"
robocopy "$src\Packages" ".\Packages" /E
robocopy "$src\ProjectSettings" ".\ProjectSettings" /E
robocopy "$src\Assets" ".\Assets" /E
```

(robocopy exit codes 0–7 mean success.)

## 4. Open the repository in Unity

1. Unity Hub > **Projects > Add > Add project from disk** and select the repository root folder.
2. Open it with the same 6000.3 LTS version. The first import takes a few minutes.
3. Unity now generates `.meta` files for `Assets/Game/**`, including the existing `.asmdef` files.
4. Check the **Console** (Window > General > Console): it must show **no compile errors**. Note any
   warnings from `Game.*` assemblies and report them. None are expected.
5. **Window > Package Manager > In Project:** confirm both packages are listed; if one is missing, use
   **+ > Install package by name…**:
   - **Test Framework** (`com.unity.test-framework`)
   - **Input System** (`com.unity.inputsystem`). Unity 6 templates normally include it. The board reads
     touch and mouse through it.
6. **Project Settings > Player > Other Settings > Active Input Handling:** `Input System Package (New)`
   or `Both`. If Unity asks to restart after changing it, let it.
7. Confirm the editor settings (these are the Unity 6 defaults, but check them):
   - **Project Settings > Editor > Version Control > Mode:** `Visible Meta Files`.
   - **Project Settings > Editor > Asset Serialization > Mode:** `Force Text`.
   - **Project Settings > Player > Other Settings > Api Compatibility Level:** `.NET Standard 2.1`.
8. Recommended for faster iteration on your machine: **Project Settings > Editor > Enter Play Mode
   Settings**, set to *Do not reload Domain or Scene*. The domain code has no static state, so this
   is safe.

## 5. Check the domain assemblies

The assemblies already exist in the repository. No need to create them, only confirm that Unity
reads them correctly:

1. In the Project window, select `Assets/Game/Domain/Game.Domain.asmdef`. In the Inspector:
   **No Engine References** is ticked, and Platforms are all (Any Platform).
2. Select `Assets/Game/Tests/EditMode/Game.Domain.Tests.asmdef`. In the Inspector:
   - Platforms: **Editor** only.
   - Assembly Definition References: `Game.Domain`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`.
   - Override References ticked, with `nunit.framework.dll`.
   - Define Constraints: `UNITY_INCLUDE_TESTS`.
3. The other assemblies, for reference:

| asmdef | Platforms | References |
|---|---|---|
| `Assets/Game/Data/Game.Data.asmdef` | Any; **No Engine References** ticked | `Game.Domain` |
| `Assets/Game/Presentation/Game.Presentation.asmdef` | Any | `Game.Domain`, `Game.Data`, `Unity.InputSystem` |
| `Assets/Game/Editor/Game.Editor.asmdef` | Editor only | `Game.Presentation` |
| `Assets/Game/Tests/EditModePresentation/Game.Presentation.Tests.asmdef` | Editor only | Game assemblies plus test runner |
| `Assets/Game/Tests/PlayMode/Game.PlayMode.Tests.asmdef` | Any (PlayMode) | Game assemblies plus test runner |

4. Do not press Apply unless you change something. If Unity rewrites the files (for example to
   GUID references), that's fine: commit what it writes.

Optional guard check: add `using UnityEngine;` at the top of any file in `Assets/Game/Domain`. It
must fail to compile, which proves the engine-reference guard works. Remove the line again.

## 6. Run the EditMode tests

**In the Editor:** Window > General > **Test Runner** > **EditMode** tab > **Run All**.

Expected result (this is the acceptance gate for M2):

- **149 tests listed: 148 passed, 0 failed, 1 not run.** These come from two assemblies:
  `Game.Domain.Tests` (102 plus `ReplaySeed`) and `Game.Presentation.Tests` (46).
- The one not run is `SimulationTests.ReplaySeed`. It is marked `[Explicit]` (a manual replay
  helper), so Run All skips it on purpose. Depending on the Test Runner version it shows as
  "not run", "skipped" or "ignored". Any of these is correct. Any other skipped test is not expected:
  report it.
- The `RandomActions_PreserveAllInvariants` entry expands into 8 seed cases (5 on the default 5×8
  board, 3 on the 7×9 alternative). `EdgeCaseTests` appears twice, as `EdgeCaseTests("5x8")` and
  `EdgeCaseTests("7x9")`, with 17 tests each. All of these count towards the 148.
- History: 78 (M1), then 95 (5×8 default), then 148 (M2: approved rule changes, content, layout,
  gesture and presenter tests).

**PlayMode tests** (Test Runner > **PlayMode** tab > Run All): **3 passed.**

- `BoardSmokeTests` (2): the board loads at 5×8; merge, bounce-back, spawn, store and sell keep the
  views in sync with the engine.
- `BoardAllocationTests` (1): dragging allocates **no managed memory per frame**. This uses Unity's
  GC allocation recorder and is the automated part of the M2 "no per-frame allocations" criterion.

For PlayMode from the command line, run the same command as below with `-testPlatform PlayMode`.
- The Console shows **no compile errors**.
- The simulation tests take the longest (roughly 5–20 s in total). The Editor is busy until they finish.

Please send me: your exact Unity version, the pass/fail/skip counts, and any Console warnings that
mention `Game.Domain`.

**From the command line** (useful later, and what I'll use for verification once I have access).
Close the Editor first, then run in PowerShell from the repository root:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.Nf1\Editor\Unity.exe" `
  -batchmode -projectPath "$PWD" `
  -runTests -testPlatform EditMode `
  -testResults "$PWD\TestResults-EditMode.xml" `
  -logFile "$PWD\unity-editmode.log"
echo "Exit code: $LASTEXITCODE"
```

Replace `6000.3.Nf1` with your installed version. Exit code `0` means all passed, `2` means some
tests failed, `3` means the run itself failed (see the log). Don't add `-quit`: `-runTests` exits by
itself. Both output files are git-ignored.

If anything fails, send me the Test Runner failure text (it includes the seed and step for simulation
failures) or the XML/log file.

## 7. Check what git sees

```bash
git status
```

You should see only these new or changed paths:

- `Assets/` items from the template, each with its `.meta` file
- New `.meta` files under `Assets/Game/`: one per folder, `.cs` file and `.asmdef`
- `Packages/manifest.json`, `Packages/packages-lock.json`
- `ProjectSettings/*.asset` and `ProjectSettings/ProjectVersion.txt`

You must **not** see `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.csproj` (outside
`tools/DomainHarness`) or `*.sln`. If you do, stop and tell me: the `.gitignore` needs fixing.

Every file and folder under `Assets/` must have a matching `.meta`. A missing `.meta` breaks
references for everyone else. Quick check from Git Bash (it should print nothing):

```bash
find Assets -mindepth 1 ! -name '*.meta' | while read -r p; do [ -e "$p.meta" ] || echo "missing meta: $p"; done
```

## 8. Commit and push

```bash
git add Assets Packages ProjectSettings
```

```bash
git commit -m "Add Unity 6.3 LTS Universal 2D project files and .meta files (Unity 6000.3.Nf1). EditMode 148 passed + 1 explicit not run; PlayMode 3 passed."
```

```bash
git push origin feature/phase-2-merge-engine
```

Put your real Unity version and the real test result into the message.

## 9. Play the M2 board

1. **Tools > The Life We Left Behind > Create Merge Board Scene.** This builds
   `Assets/Game/Scenes/MergeBoard.unity` and an unlit placeholder material
   (`Assets/Game/Presentation/Placeholder/SpriteUnlit.mat`), and puts the scene first in the build list.
   Nothing about the scene is hand-written; Unity creates it.
2. Set the Game view to a phone resolution, for example 1080×2400 portrait, or use the Device
   Simulator (Window > General > Device Simulator) with a common Android phone.
3. Press **Play**. With the mouse: drag to move and merge, click a toolbox to spawn. The top-left text
   reports the measured **cell size in dp**.
4. Commit the scene, the material and their `.meta` files.

## 10. Android build for your phone

1. **File > Build Profiles:** switch the active platform to **Android** (re-imports once).
2. **Player settings:** Default Orientation **Portrait**; Scripting Backend **IL2CPP**; Target
   Architectures **ARM64** only.
3. For the device checks, tick **Development Build** so the cell-size report and the Profiler work.
   Connect your phone over USB with USB debugging on, then **Build And Run**.
4. Follow the device checklist in `Docs/MilestoneReports/M2-playable-board.md`.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Pink/magenta sprites or "No render pipeline asset" | `Assets/Settings` wasn't copied with its `.meta` files. Repeat step 3 for `Assets` |
| `The type or namespace name 'NUnit' could not be found` | Test Framework package missing. Step 4.5 |
| Test Runner shows no tests | The test asmdef isn't Editor-only, or `UNITY_INCLUDE_TESTS` is missing. Step 5.2. Check the Console for compile errors first |
| `Multiple Unity instances cannot open the same project` | Close the Editor before running the command line tests |
| `The type or namespace name 'InputSystem' does not exist` | Input System package missing. Step 4.5 |
| Board does not react to touch or mouse | Active Input Handling is set to the old Input Manager only. Step 4.6 |
| Sprites look black or very dark | The scene has no unlit material assigned. Run the Create Merge Board Scene menu, which assigns one |
| Text missing on items | Report it with a screenshot. The placeholder text uses Unity's built-in `LegacyRuntime.ttf` font |
| Hub refuses the folder | Select the repository root (the folder containing `Assets`, `Packages` and `ProjectSettings`) |
