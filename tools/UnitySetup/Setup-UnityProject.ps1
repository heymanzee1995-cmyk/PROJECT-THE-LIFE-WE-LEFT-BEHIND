<#
.SYNOPSIS
  One-step Unity setup for THE LIFE WE LEFT BEHIND (run via SetupUnity.cmd in the repository root).

.DESCRIPTION
  1. Checks that no Unity Editor is running.
  2. Finds an installed Unity 6 editor (prefers 6000.3 LTS, otherwise the newest 6000.x).
  3. If the repository root is not yet a Unity project, creates a fresh Universal 2D project with
     Unity's own command line from Unity's template, and copies its Packages/, ProjectSettings/ and
     Assets/ into the repository root. Unity generates every file; nothing is hand-written.
  4. Offers to move stray Unity projects found inside the repository out of it (never deletes).
  5. Imports the project, builds the merge board scene, runs EditMode and PlayMode tests.
  6. Prints a summary and opens the Unity Editor on the board scene.

  It never commits, pushes or deletes anything. Logs: unity-*.log and TestResults-*.xml in the repository
  root (git-ignored).

.PARAMETER UnityExe
  Full path to Unity.exe, if auto-detection picks the wrong editor.

.PARAMETER TemplateProject
  Path to an existing, freshly created Universal 2D project to copy from, if the script cannot create one.

.PARAMETER SkipTests
  Skip running the tests (faster; not recommended).
#>
param(
    [string]$UnityExe = "",
    [string]$TemplateProject = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

function Say([string]$text) { Write-Host $text -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "  OK  $text" -ForegroundColor Green }
function Warn([string]$text) { Write-Host "  !!  $text" -ForegroundColor Yellow }
function Fail([string]$text) {
    Write-Host ""
    Write-Host "STOPPED: $text" -ForegroundColor Red
    Write-Host "Send a screenshot of this window to Claude." -ForegroundColor Red
    exit 1
}

function Show-LogErrors([string]$log) {
    if (Test-Path $log) {
        Write-Host "---- errors from $log ----" -ForegroundColor Yellow
        Select-String -Path $log -Pattern "error CS|Exception|\[Board\]|Error|Aborting" |
            Select-Object -Last 25 | ForEach-Object { Write-Host $_.Line }
        Write-Host "---- end ----" -ForegroundColor Yellow
    }
}

function Run-Unity([string]$exe, [string[]]$arguments, [string]$log) {
    $all = $arguments + @("-logFile", "`"$log`"")
    $p = Start-Process -FilePath $exe -ArgumentList $all -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}

function Read-TestResults([string]$xmlPath) {
    if (-not (Test-Path $xmlPath)) { return $null }
    [xml]$x = Get-Content $xmlPath
    $run = $x.'test-run'
    return [pscustomobject]@{
        Total   = [int]$run.total
        Passed  = [int]$run.passed
        Failed  = [int]$run.failed
        Skipped = [int]$run.skipped + [int]$run.inconclusive
    }
}

Write-Host ""
Say "THE LIFE WE LEFT BEHIND - Unity setup"
Say "Repository: $Repo"
Write-Host ""

# ---------------------------------------------------------------- 1. Unity must be closed
Say "[1/6] Checking that Unity is closed"
$running = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
if ($running) { Fail "Unity is running. Close every Unity Editor window (File > Exit), then run SetupUnity.cmd again." }
Ok "No Unity Editor running"

# ---------------------------------------------------------------- 2. Find the editor
Say "[2/6] Finding a Unity 6 editor"
if ($UnityExe -eq "") {
    $roots = @("C:\Program Files\Unity\Hub\Editor", "D:\Program Files\Unity\Hub\Editor", "$env:ProgramFiles\Unity\Hub\Editor")
    $found = @()
    foreach ($r in $roots | Select-Object -Unique) {
        if (Test-Path $r) {
            $found += Get-ChildItem $r -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Editor\Unity.exe") }
        }
    }

    $existingVersionFile = Join-Path $Repo "ProjectSettings\ProjectVersion.txt"
    $wanted = $null
    if (Test-Path $existingVersionFile) {
        $line = Select-String -Path $existingVersionFile -Pattern "m_EditorVersion:\s*(\S+)" | Select-Object -First 1
        if ($line) { $wanted = $line.Matches[0].Groups[1].Value }
    }

    $pick = $null
    if ($wanted) { $pick = $found | Where-Object { $_.Name -eq $wanted } | Select-Object -First 1 }
    if (-not $pick) { $pick = $found | Where-Object { $_.Name -like "6000.3.*" } | Sort-Object { [version]($_.Name -replace '[a-z].*$', '') } -Descending | Select-Object -First 1 }
    if (-not $pick) { $pick = $found | Where-Object { $_.Name -like "6000.*" } | Sort-Object { [version]($_.Name -replace '[a-z].*$', '') } -Descending | Select-Object -First 1 }
    if (-not $pick) { Fail "No Unity 6 editor found under 'Unity\Hub\Editor'. Install Unity 6000.3 LTS from Unity Hub (Installs > Install Editor), or run: SetupUnity.cmd -UnityExe `"C:\path\to\Unity.exe`"" }
    $UnityExe = Join-Path $pick.FullName "Editor\Unity.exe"
    if ($wanted -and $pick.Name -ne $wanted) { Warn "This project was made with $wanted, which is not installed. Using $($pick.Name)." }
    if ($pick.Name -notlike "6000.3.*") { Warn "Using $($pick.Name). The recommended version is 6000.3 LTS (see Docs/UnitySetup.md)." }
}
if (-not (Test-Path $UnityExe)) { Fail "Unity.exe not found at $UnityExe" }
Ok "Using $UnityExe"
$EditorDir = Split-Path $UnityExe -Parent

# ---------------------------------------------------------------- 3. Stray projects inside the repo
Say "[3/6] Looking for stray Unity projects inside the repository"
$stray = Get-ChildItem $Repo -Directory | Where-Object {
    $_.Name -notin @("Assets", "Packages", "ProjectSettings", "Library", "Temp", "Logs", "UserSettings", "Docs", "tools", ".git") -and
    (Test-Path (Join-Path $_.FullName "ProjectSettings\ProjectVersion.txt"))
}
foreach ($s in $stray) {
    Warn "Found a separate Unity project inside the repository: '$($s.Name)'"
    $answer = Read-Host "      Move it out to C:\Users\$env:USERNAME\UnityScratch so it is not mixed into the game? (y/n)"
    if ($answer -match '^[yY]') {
        $dest = Join-Path "C:\Users\$env:USERNAME\UnityScratch" $s.Name
        if (Test-Path $dest) { $dest = $dest + "-" + (Get-Date -Format "yyyyMMddHHmmss") }
        New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        Move-Item -Path $s.FullName -Destination $dest
        Ok "Moved to $dest (nothing was deleted)"
    } else {
        Warn "Left in place. Do not commit it."
    }
}
if (-not $stray) { Ok "None found" }

# ---------------------------------------------------------------- 4. Make the repo root a Unity project
Say "[4/6] Making the repository a Unity project"
$isProject = (Test-Path (Join-Path $Repo "ProjectSettings\ProjectVersion.txt")) -and (Test-Path (Join-Path $Repo "Packages\manifest.json"))
if ($isProject) {
    Ok "Already a Unity project (ProjectSettings and Packages present)"
} else {
    if ($TemplateProject -eq "") {
        $templateRoots = @(
            (Join-Path $EditorDir "Data\Resources\PackageManager\ProjectTemplates"),
            (Join-Path $env:APPDATA "UnityHub\Templates"),
            (Join-Path $env:LOCALAPPDATA "UnityHub\Templates")
        )
        $tgz = $null
        foreach ($t in $templateRoots) {
            if (Test-Path $t) {
                $tgz = Get-ChildItem $t -Recurse -Filter "com.unity.template.universal-2d*.tgz" -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if ($tgz) { break }
            }
        }

        if (-not $tgz) {
            Fail ("Could not find Unity's 'Universal 2D' template on this PC. In Unity Hub: Projects > New project > choose the " +
                  "'Universal 2D' template (download it if asked) > create it anywhere OUTSIDE this folder, close Unity, then run: " +
                  "SetupUnity.cmd -TemplateProject `"C:\path\to\that\project`"")
        }

        $TemplateProject = Join-Path $env:TEMP ("LifeTemplate-" + (Get-Date -Format "yyyyMMddHHmmss"))
        Say "      Creating a fresh Universal 2D project with Unity (a few minutes)..."
        $code = Run-Unity $UnityExe @("-batchmode", "-quit", "-createProject", "`"$TemplateProject`"", "-cloneFromTemplate", "`"$($tgz.FullName)`"") (Join-Path $Repo "unity-create.log")
        if (-not (Test-Path (Join-Path $TemplateProject "Packages\manifest.json"))) {
            Show-LogErrors (Join-Path $Repo "unity-create.log")
            Fail ("Unity could not create the template project (exit code $code). Create it in Unity Hub instead " +
                  "('Universal 2D' template, outside this folder), close Unity, then run: SetupUnity.cmd -TemplateProject `"C:\path\to\it`"")
        }
        Ok "Template project created at $TemplateProject"
    }

    $manifestPath = Join-Path $TemplateProject "Packages\manifest.json"
    if (-not (Test-Path $manifestPath)) { Fail "$TemplateProject is not a Unity project (no Packages\manifest.json)." }
    $manifest = Get-Content $manifestPath -Raw
    if ($manifest -notmatch "com\.unity\.render-pipelines\.universal") { Fail "$TemplateProject does not use URP. Use the 'Universal 2D' template." }
    if ($manifest -notmatch "com\.unity\.feature\.2d") { Warn "The template has no 2D feature set; it may be the 3D template. Continuing, but 'Universal 2D' is the intended one." }

    foreach ($folder in @("Packages", "ProjectSettings", "Assets")) {
        & robocopy (Join-Path $TemplateProject $folder) (Join-Path $Repo $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { Fail "Copying $folder failed (robocopy code $LASTEXITCODE)." }
        Ok "Copied $folder"
    }
}

# Packages and settings the board needs.
$manifest = Get-Content (Join-Path $Repo "Packages\manifest.json") -Raw
if ($manifest -notmatch "com\.unity\.inputsystem") { Warn "Input System package missing: in Unity, Window > Package Manager > + > Install package by name > com.unity.inputsystem" }
if ($manifest -notmatch "com\.unity\.test-framework") { Warn "Test Framework package missing: install com.unity.test-framework the same way" }
$settingsPath = Join-Path $Repo "ProjectSettings\ProjectSettings.asset"
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath -Raw
    if ($settings -match "activeInputHandler: 0") {
        ($settings -replace "activeInputHandler: 0", "activeInputHandler: 2") | Set-Content $settingsPath -NoNewline
        Ok "Active Input Handling set to 'Both' (the board reads the Input System)"
    }
}

# ---------------------------------------------------------------- 5. Import, build the scene, run tests
Say "[5/6] Importing the project and building the merge board scene (first import takes several minutes)..."
$code = Run-Unity $UnityExe @("-batchmode", "-quit", "-projectPath", "`"$Repo`"", "-executeMethod", "Game.Editor.BoardSceneBuilder.CreateSceneBatch") (Join-Path $Repo "unity-setup.log")
if ($code -ne 0 -or -not (Test-Path (Join-Path $Repo "Assets\Game\Scenes\MergeBoard.unity"))) {
    Show-LogErrors (Join-Path $Repo "unity-setup.log")
    Fail "Import or scene creation failed (exit code $code). The full log is unity-setup.log in the repository folder."
}
Ok "Compiled without errors and created Assets/Game/Scenes/MergeBoard.unity"

$edit = $null
$play = $null
if (-not $SkipTests) {
    Say "      Running EditMode tests..."
    $null = Run-Unity $UnityExe @("-batchmode", "-projectPath", "`"$Repo`"", "-runTests", "-testPlatform", "EditMode", "-testResults", "`"$Repo\TestResults-EditMode.xml`"") (Join-Path $Repo "unity-editmode.log")
    $edit = Read-TestResults (Join-Path $Repo "TestResults-EditMode.xml")
    Say "      Running PlayMode tests..."
    $null = Run-Unity $UnityExe @("-batchmode", "-projectPath", "`"$Repo`"", "-runTests", "-testPlatform", "PlayMode", "-testResults", "`"$Repo\TestResults-PlayMode.xml`"") (Join-Path $Repo "unity-playmode.log")
    $play = Read-TestResults (Join-Path $Repo "TestResults-PlayMode.xml")
}

# ---------------------------------------------------------------- 6. Summary and open the Editor
Write-Host ""
Say "[6/6] Summary"
Write-Host "  Unity:    $UnityExe"
if ($edit) {
    $colour = "Green"
    if ($edit.Failed -gt 0) { $colour = "Red" }
    Write-Host ("  EditMode: {0} passed, {1} failed, {2} not run   (expected 148 passed, 0 failed, 1 not run)" -f $edit.Passed, $edit.Failed, $edit.Skipped) -ForegroundColor $colour
} elseif (-not $SkipTests) {
    Write-Host "  EditMode: no results (see unity-editmode.log)" -ForegroundColor Red
}
if ($play) {
    $colour = "Green"
    if ($play.Failed -gt 0) { $colour = "Red" }
    Write-Host ("  PlayMode: {0} passed, {1} failed, {2} not run   (expected 3 passed)" -f $play.Passed, $play.Failed, $play.Skipped) -ForegroundColor $colour
} elseif (-not $SkipTests) {
    Write-Host "  PlayMode: no results (see unity-playmode.log)" -ForegroundColor Red
}
Write-Host "  Git:      nothing was committed. Run 'git status' to review."
Write-Host ""

New-Item -ItemType File -Force -Path (Join-Path $Repo "Library\Game.OpenMergeBoard.flag") | Out-Null
Say "Opening Unity on the merge board scene. When it is open: set the Game view to a portrait size (e.g. 1080x2400) and press Play."
Start-Process -FilePath $UnityExe -ArgumentList @("-projectPath", "`"$Repo`"")
Write-Host ""
Write-Host "Send Claude a screenshot of this window and of Unity." -ForegroundColor Cyan
