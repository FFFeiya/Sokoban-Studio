# CaptureSubmissionMedia.ps1
#
# Single-entry orchestrator for the FULLY AUTOMATED screenshot pipeline (spec §46).
# One command drives: Preflight -> Request -> Unity launch/connect -> Editor-window servicing
# -> Validate -> Manifest -> Gallery -> Markdown markers -> summary.
#
# This script NEVER asks a human to take a screenshot. EditorEditorWindows are captured by the
# Win32 fallback (CaptureWindowRegion.ps1); runtime scenarios are captured inside Unity by the
# separate editor bridge. If the environment cannot capture pixels we fail with
# CAPTURE_ENVIRONMENT_BLOCKED and a non-zero exit (spec §29/§48).
#
# File protocol (all under Temp\AgentCapture\, gitignored; the Unity bridge reads/writes these):
#   request.json               (this script writes) { requestId, scenarios[], runtimeResolution{width,height}, overwrite }
#   bridge-ready.json          (bridge writes)      { ready:true, requestId }
#   result.json                (bridge writes)      { requestId, status:"complete"|"partial", outputs[{scenario,path,captureMethod,width,height,validation}], failed[] }
#     - outputs[].path is ABSOLUTE (Path.GetFullPath(ProjectRoot/Docs/Screenshots/<file>)); it is normalized to
#       repo-relative only for manifest/gallery display and passed verbatim to ValidateScreenshots.ps1.
#     - status is "complete" when nothing failed, "partial" when any scenario failed; BOTH are terminal.
#     - outputs[].width/height may be 0; the manifest Resolution is read from the actual PNG instead.
#   editor-capture-request.json(bridge writes)      { scenario, windowTitle, outputPath }   (outputPath is ABSOLUTE)
#   editor-capture-done.json   (this script writes) { scenario, ok, method, outputPath }
#   log.txt                    (this script appends) one line per scenario attempt
#   (also mirrored to Logs/Agent/capture-submission.log, spec §45)
#
# Usage:
#   .\Scripts\Agent\CaptureSubmissionMedia.ps1 -Target All
#   .\Scripts\Agent\CaptureSubmissionMedia.ps1 -Target Runtime
#   .\Scripts\Agent\CaptureSubmissionMedia.ps1 -Target Editor -UpdateMarkdown
#   .\Scripts\Agent\CaptureSubmissionMedia.ps1 -Target All -Validate        # re-validate existing PNGs only
#   .\Scripts\Agent\CaptureSubmissionMedia.ps1 -Target All -NoLaunch -Validate   # offline: no Unity launch/connect
#
# Verification of the offline parts (no Unity editor required):
#   [System.Management.Automation.PSParser]::Tokenize((Get-Content -Raw '.\Scripts\Agent\CaptureSubmissionMedia.ps1'), [ref]$null)
#   # inject a fake Temp\AgentCapture\result.json + synthetic PNGs, then:
#   .\Scripts\Agent\CaptureSubmissionMedia.ps1 -Target All -NoLaunch -Validate
#   # preflight failure path (bogus Unity path):
#   .\Scripts\Agent\CaptureSubmissionMedia.ps1 -Target All -UnityPath 'C:\no\such\Unity.exe'
#
[CmdletBinding()]
param(
    # Which scenario family to run.
    [ValidateSet('Runtime', 'Editor', 'All')]
    [string]$Target = 'All',

    # Force a README marker update even when not every target validated PASS.
    [switch]$UpdateMarkdown,

    # Validate-only: re-validate existing PNGs; never capture, never launch/connect Unity.
    [switch]$Validate,

    # Skip Unity launch/connect (offline testing of the post-capture steps).
    [switch]$NoLaunch,

    # Unity editor executable (overridable for testing).
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.51f1\Editor\Unity.exe',

    # Unity project path (overridable for testing). Defaults to the repository root.
    [string]$ProjectPath,

    # Cold-start budget for the editor bridge (seconds).
    [int]$BridgeReadyTimeoutSec = 300,

    # Overall capture budget once the bridge is ready (seconds).
    [int]$MainLoopTimeoutSec = 600,

    # Budget for servicing a single editor-capture-request before it is marked failed (seconds).
    [int]$PerScenarioTimeoutSec = 90
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Root paths. Repo root is fixed from this script's location; -ProjectPath only
# affects Unity launch/matching and the project-exists preflight (it equals the
# repo root by default). All generated files land under the repo root.
# ---------------------------------------------------------------------------
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = $RepoRoot }

$ScreensDir     = Join-Path $RepoRoot 'Docs\Screenshots'
$CaptureDir     = Join-Path $RepoRoot 'Temp\AgentCapture'
$ManifestPath   = Join-Path $ScreensDir 'SCREENSHOT_MANIFEST.md'
$GalleryPath    = Join-Path $ScreensDir 'GALLERY.md'
$RequestPath    = Join-Path $CaptureDir 'request.json'
$ResultPath     = Join-Path $CaptureDir 'result.json'
$BridgeReadyPath= Join-Path $CaptureDir 'bridge-ready.json'
$EditorReqPath  = Join-Path $CaptureDir 'editor-capture-request.json'
$EditorDonePath = Join-Path $CaptureDir 'editor-capture-done.json'
$LogPath        = Join-Path $CaptureDir 'log.txt'
$AgentLogDir    = Join-Path $RepoRoot 'Logs\Agent'
$UnityLogPath   = Join-Path $AgentLogDir 'capture-unity.log'
$SessionLogPath = Join-Path $AgentLogDir 'capture-submission.log'
$LockFile       = Join-Path $ProjectPath 'Temp\UnityLockfile'

$CaptureWindowScript = Join-Path $PSScriptRoot 'CaptureWindowRegion.ps1'
$ValidateScript      = Join-Path $PSScriptRoot 'ValidateScreenshots.ps1'
$MarkdownScript      = Join-Path $PSScriptRoot 'UpdateMarkdownScreenshots.ps1'

# ---------------------------------------------------------------------------
# Stable scenario -> filename mapping (spec §31) + manifest metadata (spec §43).
# UsedIn = the README marker key(s) from UpdateMarkdownScreenshots.ps1's $ScreenshotMap.
# ---------------------------------------------------------------------------
$ScenarioDefs = @(
    [pscustomobject]@{ Scenario = 'RuntimeMainMenu';       File = 'runtime-main-menu.png';      Family = 'Runtime'; Caption = '主菜单';          Level = '-';                      State = 'menu';                UsedIn = @('RUNTIME_MAIN_MENU') }
    [pscustomobject]@{ Scenario = 'RuntimeLevelSelect';    File = 'runtime-level-select.png';   Family = 'Runtime'; Caption = '关卡选择';        Level = '-';                      State = 'menu';                UsedIn = @('RUNTIME_LEVEL_SELECT') }
    [pscustomobject]@{ Scenario = 'RuntimeGameplay';       File = 'runtime-gameplay.png';       Family = 'Runtime'; Caption = '游戏实机';        Level = 'Level07';                State = 'replay mid-state';    UsedIn = @('HERO_RUNTIME') }
    [pscustomobject]@{ Scenario = 'RuntimeMultiGroup';     File = 'runtime-multigroup.png';     Family = 'Runtime'; Caption = '多组压力板与门';  Level = 'temp multi-group fixture'; State = 'mid-state';         UsedIn = @('RUNTIME_MULTIGROUP') }
    [pscustomobject]@{ Scenario = 'RuntimeComplete';       File = 'runtime-complete.png';       Family = 'Runtime'; Caption = '关卡完成';        Level = 'Level07';                State = 'replay to completion'; UsedIn = @('RUNTIME_COMPLETE') }
    [pscustomobject]@{ Scenario = 'EditorDashboard';       File = 'editor-dashboard.png';       Family = 'Editor';  Caption = '内容总览';        Level = '-';                      State = 'analysis overview';   UsedIn = @('HERO_DASHBOARD') }
    [pscustomobject]@{ Scenario = 'EditorLevelEditor';     File = 'editor-level-editor.png';    Family = 'Editor';  Caption = '关卡编辑器';      Level = 'Level07';                State = 'loaded level';        UsedIn = @('EDITOR_LEVEL_EDITOR') }
    [pscustomobject]@{ Scenario = 'EditorValidation';      File = 'editor-validation.png';      Family = 'Editor';  Caption = '校验与死锁警告';  Level = 'temp deadlock fixture';  State = 'validation warning';  UsedIn = @('EDITOR_VALIDATION') }
    [pscustomobject]@{ Scenario = 'EditorAnalyzer';        File = 'editor-analyzer.png';        Family = 'Editor';  Caption = '分析器';          Level = 'Level07';                State = 'analysis result';     UsedIn = @('EDITOR_ANALYZER') }
    [pscustomobject]@{ Scenario = 'EditorSolutionPreview'; File = 'editor-solution-preview.png';Family = 'Editor';  Caption = '解法预览';        Level = 'Level07';                State = 'solution step';       UsedIn = @('HERO_EDITOR', 'EDITOR_SOLUTION_PREVIEW') }
)

$Selected = @($ScenarioDefs | Where-Object { $Target -eq 'All' -or $_.Family -eq $Target })
if ($Selected.Count -eq 0) { Write-Host "PREFLIGHT FAIL: no scenarios selected for -Target $Target"; exit 1 }

# ---------------------------------------------------------------------------
# small helpers
# ---------------------------------------------------------------------------
function Write-Utf8NoBom([string]$Path, [string]$Text) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $enc)
}

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $txt = [System.IO.File]::ReadAllText($Path)
        if ([string]::IsNullOrWhiteSpace($txt)) { return $null }
        return ($txt | ConvertFrom-Json)
    } catch { return $null }
}

function Write-CaptureLog([string]$Line) {
    $full = ((Get-Date).ToString('o')) + ' | ' + $Line
    foreach ($p in @($LogPath, $SessionLogPath)) {
        try {
            $d = Split-Path -Parent $p
            if ($d -and -not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
            Add-Content -LiteralPath $p -Value $full -Encoding utf8
        } catch { }
    }
}

function Fail-Preflight([string]$Message) {
    Write-Host "PREFLIGHT FAIL: $Message"
    exit 1
}

function Get-Sha256Hex([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '-' }
    try {
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $hash = $sha.ComputeHash($bytes) } finally { $sha.Dispose() }
        return ([System.BitConverter]::ToString($hash)).Replace('-', '')
    } catch { return '-' }
}

function Get-PngSize([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
        $img = [System.Drawing.Image]::FromFile($Path)
        try { return @{ Width = $img.Width; Height = $img.Height } } finally { $img.Dispose() }
    } catch { return $null }
}

# An absolute path is used verbatim; a relative path is resolved against the repo root.
function Resolve-AbsPath([string]$Path) {
    if (-not [System.IO.Path]::IsPathRooted($Path)) { return (Join-Path $RepoRoot $Path) }
    return $Path
}

# Normalize a path for manifest/gallery display: strip the repo-root prefix, forward slashes.
function ConvertTo-RepoRelative([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $Path }
    if (-not [System.IO.Path]::IsPathRooted($Path)) { return ($Path -replace '\\', '/') }
    try {
        $full = [System.IO.Path]::GetFullPath($Path)
        $root = $RepoRoot.TrimEnd('\', '/')
        if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
            $sub = $full.Substring($root.Length).TrimStart('\', '/')
            return ($sub -replace '\\', '/')
        }
    } catch { }
    return ($Path -replace '\\', '/')
}

# Parse the single "[CaptureWindowRegion] method=X ok=bool size=WxH hwnd=hex" reporting line.
function Get-CaptureReport([string]$Text) {
    $m = [regex]::Match($Text, '\[CaptureWindowRegion\]\s+method=(\S*)\s+ok=(\w+)\s+size=(\S*)\s+hwnd=(\S*)')
    if (-not $m.Success) { return @{ Found = $false; Method = ''; Ok = $false; Size = ''; Hwnd = '' } }
    return @{
        Found  = $true
        Method = $m.Groups[1].Value
        Ok     = ($m.Groups[2].Value -eq 'true')
        Size   = $m.Groups[3].Value
        Hwnd   = $m.Groups[4].Value
    }
}

# Case-insensitive per-file PASS/FAIL extraction from ValidateScreenshots output (spec §34-§37).
function Get-ValidationStatus([string]$Text, [string]$Path) {
    if ($Text.Contains("[Validate] $Path FAIL")) { return 'FAIL' }
    if ($Text.Contains("[Validate] $Path PASS")) { return 'PASS' }
    return 'FAIL'
}

# ---------------------------------------------------------------------------
# Unity process discovery / ownership
# ---------------------------------------------------------------------------
function Find-ProjectUnityProcess {
    try {
        $procs = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction Stop
    } catch { return $null }
    $want = $ProjectPath.Replace('\', '/').TrimEnd('/').ToLowerInvariant()
    foreach ($p in @($procs)) {
        $cmd = $p.CommandLine
        if ([string]::IsNullOrWhiteSpace($cmd)) { continue }
        $ncmd = $cmd.Replace('\', '/').ToLowerInvariant()
        if ($ncmd.Contains('-projectpath') -and $ncmd.Contains($want)) { return $p }
    }
    return $null
}

# ---------------------------------------------------------------------------
# Editor-window servicing (E2/E3): call the Win32 capture helper for one request.
# ---------------------------------------------------------------------------
function Invoke-EditorCapture([object]$Request) {
    $scenario   = [string]$Request.scenario
    $windowTitle= [string]$Request.windowTitle
    $outputPath = [string]$Request.outputPath
    $start = Get-Date
    $code = 1
    $text = ''
    $method = ''
    $ok = $false

    try {
        if (-not (Test-Path -LiteralPath $CaptureWindowScript -PathType Leaf)) {
            throw "CaptureWindowRegion.ps1 not found at $CaptureWindowScript"
        }
        $raw = & $CaptureWindowScript -WindowTitle $windowTitle -OutputPath $outputPath -TitleMatch Exact -Method Auto *>&1
        $code = $LASTEXITCODE
        $text = ($raw | Out-String)
    } catch {
        $text = "exception: $($_.Exception.Message)"
        $code = 1
    }

    $report = Get-CaptureReport $text
    if ($report.Found) { $method = $report.Method }
    # ok = exit 0 AND a non-blank method was reported.
    $ok = ($code -eq 0) -and ($report.Found) -and (-not [string]::IsNullOrWhiteSpace($method))

    $done = [ordered]@{ scenario = $scenario; ok = $ok; method = $method; outputPath = $outputPath }
    Write-Utf8NoBom $EditorDonePath ($done | ConvertTo-Json -Depth 4)

    $resultText = 'fail'; if ($ok) { $resultText = 'ok' }
    $dur = [Math]::Round(((Get-Date) - $start).TotalSeconds, 1)
    Write-CaptureLog ("scenario=$scenario attempt=1 method=$method result=$resultText validation=- duration=${dur}s")

    return @{ Ok = $ok; Method = $method; Exit = $code }
}

function Invoke-BridgeWait([int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $j = Read-JsonFile $BridgeReadyPath
        if ($j -and $j.ready -eq $true) { return $true }
        Start-Sleep -Milliseconds 1000
    }
    return $false
}

function Invoke-MainLoop {
    $deadline = (Get-Date).AddSeconds($MainLoopTimeoutSec)
    $reqFirstSeen = $null
    $reqKey = $null
    $servicedReqKey = $null
    while ((Get-Date) -lt $deadline) {
        # --- service one editor-capture request (only when done-marker is absent) ---
        if ((Test-Path -LiteralPath $EditorReqPath -PathType Leaf) -and (-not (Test-Path -LiteralPath $EditorDonePath -PathType Leaf))) {
            $reqItem = Get-Item -LiteralPath $EditorReqPath
            $key = $reqItem.LastWriteTimeUtc.Ticks.ToString() + ':' + $reqItem.Length.ToString()

            # Guard against double-servicing: the bridge deletes editor-capture-done.json at the
            # START of the next editor scenario, which briefly re-exposes the still-present previous
            # request. Without this we re-capture the old window, write a stale done marker, and
            # deadlock on the real next request.
            if ($key -ne $servicedReqKey) {
                if ($reqKey -ne $key) { $reqKey = $key; $reqFirstSeen = Get-Date }
                $elapsed = ((Get-Date) - $reqFirstSeen).TotalSeconds

                if ($elapsed -gt $PerScenarioTimeoutSec) {
                    $req = Read-JsonFile $EditorReqPath
                    $sc = '-'; if ($req -and $req.scenario) { $sc = [string]$req.scenario }
                    $op = '-'; if ($req -and $req.outputPath) { $op = [string]$req.outputPath }
                    Write-CaptureLog ("scenario=$sc attempt=1 method=- result=per-scenario-timeout validation=- duration=$([Math]::Round($elapsed,1))s")
                    $done = [ordered]@{ scenario = $sc; ok = $false; method = 'per-scenario-timeout'; outputPath = $op }
                    Write-Utf8NoBom $EditorDonePath ($done | ConvertTo-Json -Depth 4)
                    $servicedReqKey = $key
                }
                else {
                    $req = Read-JsonFile $EditorReqPath
                    if ($req -and $req.windowTitle -and $req.outputPath) {
                        [void](Invoke-EditorCapture $req)
                        $servicedReqKey = $key
                    }
                }
            }
        }

        # --- termination: result.json with a terminal status ---
        # The bridge writes "complete" when nothing failed and "partial" when any scenario failed;
        # both are terminal. Do NOT wait only for "complete" (that would hang the full budget).
        $res = Read-JsonFile $ResultPath
        if ($res -and $res.status) {
            $st = [string]$res.status
            if ($st -eq 'complete' -or $st -eq 'partial') { return $res }
            if ($st -match '(?i)fail|error|cancel') { return $res }
        }

        Start-Sleep -Milliseconds 1000
    }
    return (Read-JsonFile $ResultPath)
}

# ===========================================================================
# main
# ===========================================================================
$ownedByCaptureScript = $false
$launchedProc = $null
$exitCode = 1

try {
    # ---- 1. preflight -----------------------------------------------------
    $osIsWindows = $false
    try { if ($PSVersionTable.Platform -eq 'Win32NT') { $osIsWindows = $true } } catch { }
    if (-not $osIsWindows -and $env:OS -eq 'Windows_NT') { $osIsWindows = $true }
    if (-not $osIsWindows) { Fail-Preflight "this pipeline requires Windows (Platform=$($PSVersionTable.Platform), OS=$env:OS)" }

    if (-not $Validate) {
        if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
            Fail-Preflight "Unity Editor not found at '$UnityPath' (expected 2022.3.51f1)"
        }
    }

    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) {
        Fail-Preflight "project path not found: '$ProjectPath'"
    }

    if (-not $Validate -and (Test-Path -LiteralPath $LockFile)) {
        Fail-Preflight "Unity project lock present: $LockFile (close the Unity editor for this project first)"
    }

    try {
        New-Item -ItemType Directory -Force -Path $ScreensDir | Out-Null
        New-Item -ItemType Directory -Force -Path $CaptureDir | Out-Null
        $probe = Join-Path $ScreensDir ('.write_probe_' + [Guid]::NewGuid().ToString('N') + '.tmp')
        [System.IO.File]::WriteAllText($probe, 'x')
        Remove-Item -LiteralPath $probe -Force
    } catch {
        Fail-Preflight "Docs/Screenshots is not writable: $($_.Exception.Message)"
    }

    if (-not $Validate) {
        $screenOk = $false
        try {
            Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
            $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
            if ($vs.Width -gt 0 -and $vs.Height -gt 0) { $screenOk = $true }
        } catch { }
        if (-not $screenOk) { Write-Host 'CAPTURE_ENVIRONMENT_BLOCKED'; exit 1 }
    }
    $preflightMode = 'capture'; if ($Validate) { $preflightMode = 'validate-only' }
    Write-Host "[CaptureSubmissionMedia] preflight OK (target=$Target, mode=$preflightMode)"

    # ---- 2..5. capture path ----------------------------------------------
    $requestId = '-'
    if (-not $Validate) {
        # clean stale signals, then write request.json
        foreach ($f in @($BridgeReadyPath, $ResultPath, $EditorReqPath, $EditorDonePath)) {
            if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
        }
        $requestId = 'submission-' + (Get-Date).ToString('yyyyMMdd-HHmmss')
        $reqObj = [ordered]@{
            requestId         = $requestId
            scenarios         = @($Selected | ForEach-Object { $_.Scenario })
            runtimeResolution = [ordered]@{ width = 1920; height = 1080 }
            overwrite         = $true
        }
        Write-Utf8NoBom $RequestPath ($reqObj | ConvertTo-Json -Depth 5)
        Write-Host "[CaptureSubmissionMedia] wrote request.json (requestId=$requestId, scenarios=$($Selected.Count))"
        Write-CaptureLog ("request requestId=$requestId scenarios=$($Selected.Count) target=$Target")

        if (-not $NoLaunch) {
            # 3. launch or connect
            $existing = Find-ProjectUnityProcess
            if ($existing) {
                $ownedByCaptureScript = $false
                Write-Host "[CaptureSubmissionMedia] connected to existing Unity editor pid=$($existing.ProcessId) (ownedByCaptureScript=false; will NOT close)"
            } else {
                New-Item -ItemType Directory -Force -Path $AgentLogDir | Out-Null
                Write-Host "[CaptureSubmissionMedia] launching visible Unity: $UnityPath -projectPath $ProjectPath"
                $launchedProc = Start-Process -FilePath $UnityPath `
                    -ArgumentList @('-projectPath', $ProjectPath, '-logFile', $UnityLogPath) -PassThru
                $ownedByCaptureScript = $true
                Write-CaptureLog ("launch unity pid=$($launchedProc.Id) logFile=$UnityLogPath")
            }

            # 4. wait bridge-ready
            $ready = Invoke-BridgeWait $BridgeReadyTimeoutSec
            if (-not $ready) {
                Write-Host "[CaptureSubmissionMedia] bridge did not become ready within ${BridgeReadyTimeoutSec}s"
                Write-CaptureLog "bridge-ready timeout (${BridgeReadyTimeoutSec}s)"
                Write-Host 'CAPTURE_ENVIRONMENT_BLOCKED'
                exit 1
            }
            Write-Host '[Capture] Unity bridge ready'
            Write-CaptureLog 'bridge-ready ok'

            # Re-write request.json AFTER the bridge is ready: Unity's project-open can clear
            # Temp\AgentCapture\request.json written before launch, and the bridge's Tick re-polls
            # request.json every ~1-2s, so this second write is the one that actually gets picked up.
            Write-Utf8NoBom $RequestPath ($reqObj | ConvertTo-Json -Depth 5)
            Write-CaptureLog ("re-wrote request.json after bridge-ready requestId=$requestId")

            # 5. main loop
            $resultObj = Invoke-MainLoop
            if ($null -eq $resultObj) {
                Write-Host "[CaptureSubmissionMedia] no result.json within ${MainLoopTimeoutSec}s"
                Write-CaptureLog "result timeout (${MainLoopTimeoutSec}s)"
                Write-Host 'CAPTURE_ENVIRONMENT_BLOCKED'
                exit 1
            }
        } else {
            # -NoLaunch: no Unity. Read whatever result.json exists (offline post-capture path).
            $resultObj = Read-JsonFile $ResultPath
        }
    } else {
        $resultObj = Read-JsonFile $ResultPath
    }

    # ---- 6. build records, validate --------------------------------------
    $resultOutputs = @{}
    $failedSet = @{}
    if ($resultObj) {
        if ($resultObj.requestId) { $requestId = [string]$resultObj.requestId }
        foreach ($o in @($resultObj.outputs)) {
            if ($o -and $o.scenario) { $resultOutputs[[string]$o.scenario] = $o }
        }
        foreach ($f in @($resultObj.failed)) { if ($f) { $failedSet[[string]$f] = $true } }
    }
    $resultComplete = ($resultObj -and ([string]$resultObj.status) -eq 'complete')
    $proceed = $Validate -or ($null -ne $resultObj)

    $records = New-Object System.Collections.Generic.List[object]
    foreach ($def in $Selected) {
        # Default (no result.json entry): the canonical repo-relative output path.
        $rawPath = 'Docs/Screenshots/' + $def.File
        $method = '-'
        if ($resultOutputs.ContainsKey($def.Scenario)) {
            $o = $resultOutputs[$def.Scenario]
            if ($o.path) { $rawPath = [string]$o.path }   # bridge writes ABSOLUTE paths
            if ($o.captureMethod) { $method = [string]$o.captureMethod }
        }
        $records.Add([pscustomobject]@{
            Def        = $def
            RelPath    = (ConvertTo-RepoRelative $rawPath)   # manifest/gallery display
            ValPath    = $rawPath                            # what ValidateScreenshots gets (abs or rel)
            Method     = $method
            Validation = 'FAIL'
            Resolution = '-'
            Sha        = '-'
            CapturedAt = (Get-Date).ToString('o')
        })
    }

    $passCount = 0
    if ($proceed) {
        $paths = @($records | ForEach-Object { $_.ValPath })
        $vraw = & $ValidateScript -Paths $paths *>&1
        $vcode = $LASTEXITCODE
        $vtext = ($vraw | Out-String)
        Write-Host '[CaptureSubmissionMedia] ValidateScreenshots.ps1 output:'
        Write-Host $vtext.TrimEnd()

        foreach ($r in $records) {
            if ($failedSet.ContainsKey($r.Def.Scenario)) {
                $r.Validation = 'FAIL'
            } else {
                $r.Validation = Get-ValidationStatus $vtext $r.ValPath
            }
            if ($r.Validation -eq 'PASS') { $passCount++ }

            $abs = Resolve-AbsPath $r.ValPath
            $r.Sha = Get-Sha256Hex $abs

            # Resolution ALWAYS comes from the actual PNG (result.json width/height may be 0).
            $size = Get-PngSize $abs
            if ($null -ne $size) { $r.Resolution = ('{0}x{1}' -f $size.Width, $size.Height) }
        }
    } else {
        foreach ($r in $records) { $r.Validation = 'FAIL' }
    }

    $total = $records.Count
    $allPass = ($total -gt 0) -and ($passCount -eq $total)

    $modeText = 'capture'; if ($Validate) { $modeText = 'validate-only' }

    # ---- 7. manifest -----------------------------------------------------
    if ($proceed) {
        $generatedAt = (Get-Date).ToString('o')
        $ml = New-Object System.Collections.Generic.List[string]
        $ml.Add('# SCREENSHOT_MANIFEST.md — 提交截图清单（自动生成）')
        $ml.Add('')
        $ml.Add('自动生成 by `Scripts/Agent/CaptureSubmissionMedia.ps1`；请勿手工编辑。')
        $ml.Add('')
        $ml.Add(("- Target: {0}" -f $Target))
        $ml.Add(("- Mode: {0}" -f $modeText))
        $ml.Add(("- Request ID: {0}" -f $requestId))
        $ml.Add(("- Generated: {0}" -f $generatedAt))
        $ml.Add('')
        $ml.Add('## 总览')
        $ml.Add('')
        $ml.Add('| Scenario | Method | Resolution | Validation |')
        $ml.Add('|---|---|---|---|')
        foreach ($r in $records) {
            $ml.Add(("| {0} | {1} | {2} | {3} |" -f $r.Def.Scenario, $r.Method, $r.Resolution, $r.Validation))
        }
        $ml.Add('')
        $ml.Add('## 明细')
        foreach ($r in $records) {
            $usedIn = ($r.Def.UsedIn -join ', ')
            $ml.Add('')
            $ml.Add(("### {0}" -f $r.Def.Scenario))
            $ml.Add('')
            $ml.Add(("- Scenario: {0}" -f $r.Def.Scenario))
            $ml.Add(("- Path: {0}" -f $r.RelPath))
            $ml.Add(("- Capture Method: {0}" -f $r.Method))
            $ml.Add(("- Source Level: {0}" -f $r.Def.Level))
            $ml.Add(("- Source State: {0}" -f $r.Def.State))
            $ml.Add(("- Resolution: {0}" -f $r.Resolution))
            $ml.Add(("- SHA256: {0}" -f $r.Sha))
            $ml.Add(("- Validation: {0}" -f $r.Validation))
            $ml.Add(("- Captured At: {0}" -f $r.CapturedAt))
            $ml.Add(("- Used In: {0}" -f $usedIn))
        }
        Write-Utf8NoBom $ManifestPath (($ml -join "`r`n") + "`r`n")
        Write-Host "[CaptureSubmissionMedia] wrote manifest: $ManifestPath"

        # ---- 8. gallery --------------------------------------------------
        $gl = New-Object System.Collections.Generic.List[string]
        $gl.Add('# Screenshot Gallery — 截图总览')
        $gl.Add('')
        $gl.Add('自动生成 by `Scripts/Agent/CaptureSubmissionMedia.ps1`；共 10 张稳定文件名截图。')
        $gl.Add('')
        $gl.Add(("- Generated: {0}" -f $generatedAt))
        foreach ($family in @('Runtime', 'Editor')) {
            $gl.Add('')
            $gl.Add(("## {0}" -f $family))
            $gl.Add('')
            $gl.Add('| # | Preview | File | Caption |')
            $gl.Add('|---|---|---|---|')
            $n = 0
            foreach ($def in @($ScenarioDefs | Where-Object { $_.Family -eq $family })) {
                $n++
                $gl.Add(("| {0} | ![{1}]({2}) | {2} | {1} |" -f $n, $def.Caption, $def.File))
            }
        }
        Write-Utf8NoBom $GalleryPath (($gl -join "`r`n") + "`r`n")
        Write-Host "[CaptureSubmissionMedia] wrote gallery: $GalleryPath"
    }

    # ---- 9. README markers (only after capture success + all PASS, or forced) ----
    $captureSucceeded = (-not $Validate) -and $resultComplete
    $doMarkdown = (($captureSucceeded -and $allPass) -or $UpdateMarkdown)
    if ($doMarkdown) {
        $mraw = & $MarkdownScript -Targets @('README.md') *>&1
        $mcode = $LASTEXITCODE
        Write-Host ($mraw | Out-String).TrimEnd()
        if ($mcode -ne 0) {
            Write-Host "[CaptureSubmissionMedia] UpdateMarkdownScreenshots exit=$mcode (README markers not updated)"
            Write-CaptureLog "markdown update exit=$mcode"
        } else {
            Write-Host '[CaptureSubmissionMedia] README markers updated'
            Write-CaptureLog 'markdown update ok'
        }
    }

    # ---- 10. summary -----------------------------------------------------
    Write-Host ''
    $i = 0
    foreach ($r in $records) {
        $i++
        Write-Host ("[{0}/{1}] {2}" -f $i, $total, $r.Def.Scenario)
        Write-Host ("  method: {0}" -f $r.Method)
        Write-Host ("  validation: {0}" -f $r.Validation)
    }
    Write-Host ''
    Write-Host ("{0} / {1} PASS" -f $passCount, $total)

    $pipelinePass = $false
    if ($proceed) {
        if ($Validate) { $pipelinePass = $allPass }
        else { $pipelinePass = ($resultComplete -and $allPass) }
    }

    Write-CaptureLog ("summary target=$Target mode=$modeText pass=$passCount/$total pipelinePass=$pipelinePass")

    if ($pipelinePass) {
        Write-Host 'SCREENSHOT_MANIFEST updated'
        Write-Host 'GALLERY updated'
        Write-Host ''
        Write-Host 'CAPTURE PIPELINE: PASS'
        $exitCode = 0
    } else {
        if ($failedSet.Count -gt 0) {
            Write-Host ("failed: {0}" -f (@($failedSet.Keys) -join ', '))
        }
        Write-Host ''
        Write-Host 'CAPTURE PIPELINE: FAIL'
        $exitCode = 1
    }
}
finally {
    # 11. close Unity ONLY if this script launched it.
    if ($ownedByCaptureScript -and $null -ne $launchedProc) {
        try {
            Stop-Process -Id $launchedProc.Id -Force -ErrorAction SilentlyContinue
            Write-Host "[CaptureSubmissionMedia] closed capture-owned Unity pid=$($launchedProc.Id)"
            Write-CaptureLog "closed capture-owned unity pid=$($launchedProc.Id)"
        } catch { }
    }
}

exit $exitCode
