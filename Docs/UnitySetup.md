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
3. Unity now generates `.meta` files for `Assets/Game/**`, including the two existing `.asmdef` files.
4. Check the **Console** (Window > General > Console): it must show **no compile errors**. Note any
   warnings from `Game.Domain` / `Game.Domain.Tests` and report them. None are expected.
5. **Window > Package Manager > In Project:** confirm **Test Framework** (`com.unity.test-framework`)
   is listed. If not: **+ > Install package by name…**, enter `com.unity.test-framework`, and install.
6. Confirm the editor settings (these are the Unity 6 defaults, but check them):
   - **Project Settings > Editor > Version Control > Mode:** `Visible Meta Files`.
   - **Project Settings > Editor > Asset Serialization > Mode:** `Force Text`.
   - **Project Settings > Player > Other Settings > Api Compatibility Level:** `.NET Standard 2.1`.
7. Recommended for faster iteration on your machine: **Project Settings > Editor > Enter Play Mode
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
3. Do not press Apply unless you change something. If Unity rewrites the files (for example to
   GUID references), that's fine: commit what it writes.

Optional guard check: add `using UnityEngine;` at the top of any file in `Assets/Game/Domain`. It
must fail to compile, which proves the engine-reference guard works. Remove the line again.

## 6. Run the EditMode tests

**In the Editor:** Window > General > **Test Runner** > **EditMode** tab > **Run All**.

Expected result:

- **78 tests passed, 0 failed.**
- `ReplaySeed` is marked Explicit and is **not** run by Run All. That is intended.
- The simulation tests take the longest (roughly 5–20 s in total). The Editor is busy until they finish.

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
git commit -m "Add Unity 6.3 LTS Universal 2D project files and .meta files (Unity 6000.3.Nf1). Domain EditMode tests: 78 passed in Unity."
```

```bash
git push origin feature/phase-2-merge-engine
```

Put your real Unity version and the real test result into the message.

## 9. Optional now, needed for M2

- **File > Build Profiles** (Unity 6): switch the active platform to **Android**. This re-imports
  assets once; doing it now avoids a long wait at M2.
- **Player settings for later:** portrait only, IL2CPP, ARM64. These are M2 tasks; you don't need to
  set them yet.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Pink/magenta sprites or "No render pipeline asset" | `Assets/Settings` wasn't copied with its `.meta` files. Repeat step 3 for `Assets` |
| `The type or namespace name 'NUnit' could not be found` | Test Framework package missing. Step 4.5 |
| Test Runner shows no tests | The test asmdef isn't Editor-only, or `UNITY_INCLUDE_TESTS` is missing. Step 5.2. Check the Console for compile errors first |
| `Multiple Unity instances cannot open the same project` | Close the Editor before running the command line tests |
| Hub refuses the folder | Select the repository root (the folder containing `Assets`, `Packages` and `ProjectSettings`) |
