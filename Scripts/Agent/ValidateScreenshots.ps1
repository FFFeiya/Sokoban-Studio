# ValidateScreenshots.ps1
#
# Mechanical PNG screenshot QA. Pure PowerShell + System.Drawing (no OCR, no visual AI).
#
# Detects, for each listed file:
#   1. missing
#   2. byte size <= -MinBytes
#   3. not a decodable PNG
#   4. dimensions below -MinWidth / -MinHeight
#   5. transparency ratio below -NonTransparentRatio
#   6. near-black  (max luminance < 10) / near-uniform/blank (max-min < -MinLuminanceRange)
#   7. too few distinct colors (< -MinUniqueColors)
#   8. byte-identical duplicates (SHA256) -> FAIL "duplicate of <other>";
#      near-duplicate warning (8x8 grayscale average perceptual hash, hamming <= 2) -> WARNING only.
#
# Prints one "[Validate] <file> PASS|FAIL: <reason>" line per file (plus optional WARNING lines).
# Exit code 0 only when EVERY listed file PASSes AND at least one file was listed.
#
# Usage:
#   .\Scripts\Agent\ValidateScreenshots.ps1
#   .\Scripts\Agent\ValidateScreenshots.ps1 -Paths @('Docs\Screenshots\runtime-gameplay.png')
#   .\Scripts\Agent\ValidateScreenshots.ps1 -Paths @('C:\shots\a.png','C:\shots\b.png') -MinBytes 0

[CmdletBinding()]
param(
    # Repo-relative or absolute PNG paths. Defaults to the 10 standard screenshots under Docs\Screenshots.
    [string[]]$Paths,

    [int]$MinWidth = 640,
    [int]$MinHeight = 360,
    [long]$MinBytes = 10240,
    [double]$NonTransparentRatio = 0.9,
    [double]$MinLuminanceRange = 30,
    [int]$MinUniqueColors = 16
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$standardNames = @(
    'runtime-main-menu',
    'runtime-level-select',
    'runtime-gameplay',
    'runtime-multigroup',
    'runtime-complete',
    'editor-dashboard',
    'editor-level-editor',
    'editor-validation',
    'editor-analyzer',
    'editor-solution-preview'
)

if ($null -eq $Paths) {
    $Paths = @()
    foreach ($n in $standardNames) { $Paths += ('Docs\Screenshots\' + $n + '.png') }
}

function Get-PhashDistance {
    param([string]$a, [string]$b)
    if ([string]::IsNullOrEmpty($a) -or [string]::IsNullOrEmpty($b)) { return 999 }
    if ($a.Length -ne $b.Length) { return 999 }
    $d = 0
    for ($i = 0; $i -lt $a.Length; $i++) { if ($a[$i] -ne $b[$i]) { $d++ } }
    return $d
}

function Get-ImageRecord {
    param(
        [string]$InputPath,
        [string]$RepoRoot,
        [int]$MinWidth,
        [int]$MinHeight,
        [long]$MinBytes,
        [double]$NonTransparentRatio,
        [double]$MinLuminanceRange,
        [int]$MinUniqueColors
    )

    $rec = @{
        Display    = $InputPath
        Full       = $InputPath
        Exists     = $false
        FailReason = $null
        Sha        = $null
        Phash      = $null
        Decoded    = $false
    }

    $full = $InputPath
    if (-not [System.IO.Path]::IsPathRooted($full)) {
        $full = Join-Path $RepoRoot $InputPath
    }
    $rec.Full = $full

    # 1. Exists
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        $rec.FailReason = 'missing'
        return $rec
    }
    $rec.Exists = $true

    # 8a. SHA256 over raw bytes (used later for byte-identical duplicate detection).
    try {
        $bytes = [System.IO.File]::ReadAllBytes($full)
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha.ComputeHash($bytes)
        } finally {
            $sha.Dispose()
        }
        $rec.Sha = ([System.BitConverter]::ToString($hash)).Replace('-', '')
    } catch {
        $rec.FailReason = ('cannot read file bytes: ' + $_.Exception.Message)
        return $rec
    }

    # 2. Byte size
    if ($bytes.Length -le $MinBytes) {
        $rec.FailReason = ('too small ({0} bytes <= {1})' -f $bytes.Length, $MinBytes)
        return $rec
    }

    # 3. Decodable PNG
    $bmp = $null
    try {
        $bmp = [System.Drawing.Bitmap]::FromFile($full)
    } catch {
        $rec.FailReason = 'not a decodable PNG'
        return $rec
    }

    try {
        # 4. Dimensions
        if ($bmp.Width -lt $MinWidth -or $bmp.Height -lt $MinHeight) {
            $rec.FailReason = ('dimensions {0}x{1} below {2}x{3}' -f $bmp.Width, $bmp.Height, $MinWidth, $MinHeight)
            return $rec
        }

        $w = $bmp.Width
        $h = $bmp.Height

        # Read all pixels once via LockBits + Marshal.Copy (GetPixel would be far too slow at 1920x1080).
        $rect = [System.Drawing.Rectangle]::new(0, 0, $w, $h)
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $stride = [Math]::Abs($data.Stride)
            $bufLen = $stride * $h
            $buffer = [byte[]]::new($bufLen)
            [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $bufLen)
        } finally {
            $bmp.UnlockBits($data)
        }

        # Analyse sampled pixels (every 4th in x and y) for speed.
        $total = 0
        $opaque = 0
        $minL = 255.0
        $maxL = 0.0
        $colors = [System.Collections.Generic.HashSet[int]]::new()
        $pacc = [double[]]::new(64)
        $pcnt = [int[]]::new(64)

        for ($y = 0; $y -lt $h; $y += 4) {
            $row = $y * $stride
            $cy = [int][Math]::Floor($y * 8 / $h)
            if ($cy -gt 7) { $cy = 7 }
            for ($x = 0; $x -lt $w; $x += 4) {
                $o = $row + ($x * 4)
                # Format32bppArgb memory layout is BGRA (little-endian ARGB).
                $b = $buffer[$o]
                $g = $buffer[$o + 1]
                $r = $buffer[$o + 2]
                $a = $buffer[$o + 3]

                $total++
                if ($a -gt 128) { $opaque++ }

                $lum = (0.299 * $r) + (0.587 * $g) + (0.114 * $b)
                if ($lum -lt $minL) { $minL = $lum }
                if ($lum -gt $maxL) { $maxL = $lum }

                [void]$colors.Add((($r -shl 16) -bor ($g -shl 8) -bor $b))

                $cx = [int][Math]::Floor($x * 8 / $w)
                if ($cx -gt 7) { $cx = 7 }
                $bi = ($cy * 8) + $cx
                $pacc[$bi] += $lum
                $pcnt[$bi]++
            }
        }

        # 5. Non-transparent ratio
        $ratio = 0.0
        if ($total -gt 0) { $ratio = $opaque / [double]$total }
        if ($ratio -lt $NonTransparentRatio) {
            $rec.FailReason = ('transparent/blank alpha (opaque ratio {0:0.###} < {1:0.###})' -f $ratio, $NonTransparentRatio)
            return $rec
        }

        # 6. Luminance
        if ($maxL -lt 10) {
            $rec.FailReason = 'near-black'
            return $rec
        }
        if (($maxL - $minL) -lt $MinLuminanceRange) {
            $rec.FailReason = 'near-uniform/blank'
            return $rec
        }

        # 7. Unique colors
        if ($colors.Count -lt $MinUniqueColors) {
            $rec.FailReason = ('too few colors ({0} < {1})' -f $colors.Count, $MinUniqueColors)
            return $rec
        }

        # 8b. Perceptual hash: 8x8 grayscale average-bit hash (for near-duplicate WARNING only).
        $cells = [double[]]::new(64)
        $cellSum = 0.0
        for ($i = 0; $i -lt 64; $i++) {
            if ($pcnt[$i] -gt 0) { $cells[$i] = $pacc[$i] / [double]$pcnt[$i] } else { $cells[$i] = 0.0 }
            $cellSum += $cells[$i]
        }
        $avgCell = $cellSum / 64.0
        $sb = [System.Text.StringBuilder]::new(64)
        for ($i = 0; $i -lt 64; $i++) {
            if ($cells[$i] -gt $avgCell) { [void]$sb.Append('1') } else { [void]$sb.Append('0') }
        }
        $rec.Phash = $sb.ToString()
        $rec.Decoded = $true

        return $rec
    } finally {
        if ($null -ne $bmp) { $bmp.Dispose() }
    }
}

try {
    $records = @()
    foreach ($p in $Paths) {
        try {
            $records += (Get-ImageRecord -InputPath $p -RepoRoot $RepoRoot `
                    -MinWidth $MinWidth -MinHeight $MinHeight -MinBytes $MinBytes `
                    -NonTransparentRatio $NonTransparentRatio -MinLuminanceRange $MinLuminanceRange `
                    -MinUniqueColors $MinUniqueColors)
        } catch {
            $records += @{
                Display    = $p
                Full       = $p
                Exists     = $false
                FailReason = ('error: ' + $_.Exception.Message)
                Sha        = $null
                Phash      = $null
                Decoded    = $false
            }
        }
    }

    if ($records.Count -eq 0) {
        Write-Host '[Validate] (none) FAIL: no files provided'
        exit 1
    }

    # 8. Byte-identical duplicate detection (SHA256). Applies to every existing file.
    $bySha = @{}
    for ($i = 0; $i -lt $records.Count; $i++) {
        $sha = $records[$i].Sha
        if ($null -eq $sha) { continue }
        if (-not $bySha.ContainsKey($sha)) { $bySha[$sha] = @() }
        $bySha[$sha] = @($bySha[$sha]) + $i
    }
    foreach ($sha in @($bySha.Keys)) {
        $idx = @($bySha[$sha])
        if ($idx.Count -lt 2) { continue }
        for ($a = 0; $a -lt $idx.Count; $a++) {
            $i = $idx[$a]
            $j = $idx[0]
            if ($j -eq $i) { $j = $idx[1] }
            $dupMsg = 'duplicate of ' + $records[$j].Display
            if ($null -eq $records[$i].FailReason) {
                $records[$i].FailReason = $dupMsg
            } else {
                $records[$i].FailReason = $records[$i].FailReason + '; ' + $dupMsg
            }
        }
    }

    # Report: exactly one [Validate] <file> PASS|FAIL line per input file, in input order.
    $failCount = 0
    foreach ($rec in $records) {
        if ($null -eq $rec.FailReason) {
            Write-Host ('[Validate] ' + $rec.Display + ' PASS')
        } else {
            $failCount++
            Write-Host ('[Validate] ' + $rec.Display + ' FAIL: ' + $rec.FailReason)
        }
    }

    # Near-duplicate WARNING (non-fatal): only meaningful between files that otherwise PASS.
    for ($i = 0; $i -lt $records.Count; $i++) {
        for ($j = $i + 1; $j -lt $records.Count; $j++) {
            $ri = $records[$i]
            $rj = $records[$j]
            if ($null -ne $ri.FailReason -or $null -ne $rj.FailReason) { continue }
            if (-not $ri.Decoded -or -not $rj.Decoded) { continue }
            $d = Get-PhashDistance -a $ri.Phash -b $rj.Phash
            if ($d -le 2) {
                Write-Host ('[Validate] ' + $ri.Display + ' WARNING: near-duplicate of ' + $rj.Display + ' (phash hamming=' + $d + ')')
                Write-Host ('[Validate] ' + $rj.Display + ' WARNING: near-duplicate of ' + $ri.Display + ' (phash hamming=' + $d + ')')
            }
        }
    }

    if ($failCount -gt 0) {
        Write-Host ('[Validate] summary: {0}/{1} file(s) failed' -f $failCount, $records.Count)
        exit 1
    }

    Write-Host ('[Validate] summary: all {0} file(s) passed' -f $records.Count)
    exit 0
} catch {
    Write-Host ('[Validate] (fatal) FAIL: ' + $_.Exception.Message)
    exit 1
}
