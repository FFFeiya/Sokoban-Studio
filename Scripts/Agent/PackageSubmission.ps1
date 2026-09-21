[CmdletBinding()]
param(
    [string]$DistDir = "Dist",
    [string]$BuildDir = "Build",
    [string]$ProjectPath
)

# Packages the submission artifacts into $DistDir:
#   - Sokoban-Studio-Source.zip : the tracked tree only (git archive HEAD), so Library/Temp/
#     Logs/UserSettings/Build/obj/.git and every untracked file are excluded automatically.
#   - Sokoban-Studio-Windows-x64.zip  : the contents of the player build directory, when one exists.
# Uses only built-in PowerShell (Compress-Archive / System.IO.Compression). No modules.

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Resolve-ProjectPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ProjectPath $Path))
}

function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1MB) { return ("{0:N2} MB" -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ("{0:N2} KB" -f ($Bytes / 1KB)) }
    return "$Bytes B"
}

function Write-ZipReport([string]$Path, [string]$Label) {
    $info = Get-Item -LiteralPath $Path
    Write-Host ("[{0}] {1}" -f $Label, $info.FullName)
    Write-Host ("[{0}] size: {1} ({2} bytes)" -f $Label, (Format-Size $info.Length), $info.Length)
}

$DistPath = Resolve-ProjectPath $DistDir
$BuildPath = Resolve-ProjectPath $BuildDir

Write-Host "Project: $ProjectPath"
Write-Host "Dist: $DistPath"
Write-Host "Build: $BuildPath"

New-Item -ItemType Directory -Force -Path $DistPath | Out-Null

# --- Source zip: tracked tree only ---------------------------------------------------------
if (-not (Test-Path (Join-Path $ProjectPath ".git"))) {
    Fail "Not a git repository: missing .git under $ProjectPath"
}

$SourceZip = Join-Path $DistPath "Sokoban-Studio-Source.zip"
if (Test-Path $SourceZip) { Remove-Item $SourceZip -Force }

Write-Host "[Source] archiving tracked tree (git archive HEAD)..."
& git -C $ProjectPath archive --format=zip -o $SourceZip HEAD
if ($LASTEXITCODE -ne 0) {
    Fail "[Source] git archive failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $SourceZip)) {
    Fail "[Source] git archive reported success but $SourceZip was not produced"
}

Write-ZipReport $SourceZip "Source"

# --- Release zip: player build directory, when present --------------------------------------
$BuildItems = @()
if (Test-Path $BuildPath) {
    $BuildItems = @(Get-ChildItem -Path $BuildPath -Force -ErrorAction SilentlyContinue)
}

if ($BuildItems.Count -eq 0) {
    Write-Host "[Build] skipped: $BuildPath does not exist or is empty. Run the Release build before packaging."
} else {
    $BuildZip = Join-Path $DistPath "Sokoban-Studio-Windows-x64.zip"
    if (Test-Path $BuildZip) { Remove-Item $BuildZip -Force }

    Write-Host "[Build] compressing $($BuildItems.Count) item(s)..."
    Compress-Archive -Path "$BuildPath\*" -DestinationPath $BuildZip -Force
    if (-not (Test-Path $BuildZip)) {
        Fail "[Build] Compress-Archive did not produce $BuildZip"
    }

    Write-ZipReport $BuildZip "Build"
}

Write-Host "[Package] dist contents:"
Get-ChildItem -Path $DistPath -File | ForEach-Object { Write-Host ("  {0} ({1})" -f $_.Name, (Format-Size $_.Length)) }

Write-Host "[Package] PASS"
exit 0
