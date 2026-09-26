[CmdletBinding()]
param(
    [ValidateSet("Fast", "Gate", "Release")]
    [string]$Tier = "Fast",
    [string]$ProjectPath
)

$ErrorActionPreference = "Stop"
$ExpectedVersion = "2022.3.51f1"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$LogDir = Join-Path $ProjectPath "Logs\Verify"
$ResultDir = Join-Path $LogDir "TestResults"
$BuildDir = Join-Path $ProjectPath "Build"
New-Item -ItemType Directory -Force -Path $LogDir, $ResultDir | Out-Null

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Find-UnityEditor {
    if ($env:UNITY_EXE -and (Test-Path $env:UNITY_EXE)) {
        return (Resolve-Path $env:UNITY_EXE).Path
    }

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$ExpectedVersion\Editor\Unity.exe",
        "C:\Program Files (x86)\Unity\Hub\Editor\$ExpectedVersion\Editor\Unity.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    Fail "Unity $ExpectedVersion not found. Install it in Unity Hub or set UNITY_EXE."
}

function Assert-ProjectVersion {
    $versionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path $versionFile)) {
        Fail "Not a Unity project: missing ProjectSettings\ProjectVersion.txt"
    }

    $text = Get-Content $versionFile -Raw
    if ($text -notmatch [regex]::Escape($ExpectedVersion)) {
        Fail "Unity version mismatch. Expected $ExpectedVersion. ProjectVersion.txt: $($text.Trim())"
    }
}

function Invoke-Unity([string[]]$Arguments, [string]$Label) {
    Write-Host "[$Label] launching Unity..."
    # Use Start-Process -Wait: Unity 2022's parent process exits immediately on
    # Windows while the child finishes the work, so $LASTEXITCODE is unreliable.
    $proc = Start-Process -FilePath $script:UnityExe -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    $code = $proc.ExitCode
    Write-Host "[$Label] exit code: $code"
    return $code
}

function Assert-NoCompilerFailure([string]$LogPath, [string]$Label) {
    if (-not (Test-Path $LogPath)) {
        Fail "[$Label] expected log was not produced: $LogPath"
    }

    $patterns = @(
        'error CS\d+',
        'Compilation failed',
        'Scripts have compiler errors',
        'compile errors'
    )

    $matches = Select-String -Path $LogPath -Pattern $patterns -AllMatches
    if ($matches) {
        Write-Host "[$Label] compiler failure evidence:"
        $matches | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line }
        Fail "[$Label] compiler errors detected. See $LogPath"
    }
}

function Assert-TestResult([string]$ResultPath, [string]$Label) {
    if (-not (Test-Path $ResultPath)) {
        Fail "[$Label] test result XML missing: $ResultPath"
    }

    try {
        [xml]$xml = Get-Content $ResultPath -Raw
        $root = $xml.DocumentElement
        $result = [string]$root.result
        $failed = 0
        if ($root.HasAttribute("failed")) {
            $failed = [int]$root.failed
        }

        Write-Host "[$Label] result=$result total=$($root.total) passed=$($root.passed) failed=$failed"

        if ($failed -gt 0 -or ($result -and $result -notin @("Passed", "Inconclusive"))) {
            Fail "[$Label] tests did not pass. See $ResultPath"
        }
    } catch {
        Fail "[$Label] could not parse test result XML: $($_.Exception.Message)"
    }
}

function Run-Fast {
    Assert-ProjectVersion

    $log = Join-Path $LogDir "fast-compile.log"
    $args = @(
        "-batchmode",
        "-quit",
        "-projectPath", $ProjectPath,
        "-logFile", $log
    )

    $code = Invoke-Unity $args "Fast"
    if ($code -ne 0) { Fail "[Fast] Unity failed. See $log" }
    Assert-NoCompilerFailure $log "Fast"
    Write-Host "[Fast] PASS"
}

function Run-TestPlatform([string]$Platform) {
    $stamp = $Platform.ToLowerInvariant()
    $log = Join-Path $LogDir ("gate-" + $stamp + ".log")
    $xml = Join-Path $ResultDir ($stamp + ".xml")

    if (Test-Path $xml) { Remove-Item $xml -Force }

    $args = @(
        "-batchmode",
        "-projectPath", $ProjectPath,
        "-runTests",
        "-testPlatform", $Platform,
        "-testResults", $xml,
        "-logFile", $log
    )

    $code = Invoke-Unity $args ("Gate-" + $Platform)
    if ($code -ne 0) { Fail "[Gate-$Platform] Unity returned $code. See $log" }
    Assert-NoCompilerFailure $log ("Gate-" + $Platform)
    Assert-TestResult $xml ("Gate-" + $Platform)
}

function Has-PlayModeTests {
    $assets = Join-Path $ProjectPath "Assets"
    if (-not (Test-Path $assets)) { return $false }
    $dirs = Get-ChildItem -Path $assets -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "PlayMode" }
    return ($null -ne $dirs -and @($dirs).Count -gt 0)
}

function Run-Gate {
    Run-Fast
    Run-TestPlatform "EditMode"

    if (Has-PlayModeTests) {
        Run-TestPlatform "PlayMode"
    } else {
        Write-Host "[Gate] No PlayMode test directory detected; PlayMode CLI test run skipped."
    }

    Write-Host "[Gate] PASS"
}

function Run-Release {
    Run-Gate

    New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
    $exe = Join-Path $BuildDir "Sokoban-Studio.exe"
    $log = Join-Path $LogDir "release-build.log"

    $args = @(
        "-batchmode",
        "-quit",
        "-projectPath", $ProjectPath,
        "-buildTarget", "Win64",
        "-buildWindows64Player", $exe,
        "-logFile", $log
    )

    $code = Invoke-Unity $args "Release"
    if ($code -ne 0) { Fail "[Release] build failed. See $log" }
    Assert-NoCompilerFailure $log "Release"

    if (-not (Test-Path $exe)) {
        Fail "[Release] Unity returned success but build artifact is missing: $exe"
    }

    Write-Host "[Release] PASS"
    Write-Host "[Release] artifact: $exe"
}

$script:UnityExe = Find-UnityEditor
Write-Host "Unity: $script:UnityExe"
Write-Host "Project: $ProjectPath"
Write-Host "Tier: $Tier"

switch ($Tier) {
    "Fast" { Run-Fast }
    "Gate" { Run-Gate }
    "Release" { Run-Release }
}

exit 0
