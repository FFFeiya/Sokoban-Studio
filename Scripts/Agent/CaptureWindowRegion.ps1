# CaptureWindowRegion.ps1
#
# Capture a specific top-level (or child) Windows window by title into a PNG using Win32.
# This is the E2 (PrintWindow) / E3 (CopyFromScreen) fallback used to capture Unity EditorWindows,
# which Unity's ScreenCapture APIs cannot capture.
#
# Behaviour:
#   - makes the process Per-Monitor DPI Aware V2 (SetProcessDpiAwarenessContext(-4));
#   - finds the HWND by title (Prefix or Exact) over top-level windows, then their children;
#   - uses GetWindowRect for the authoritative PHYSICAL rect (never a DPI-scaled logical position);
#   - Auto: try PrintWindow (PW_RENDERFULLCONTENT=2); if the result is blank/black/near-uniform,
#     fall back to CopyFromScreen (foreground + topmost + settle, then raster the window rect).
#   - CopyFromScreen refuses to run if the window rect is not fully inside the virtual screen
#     (it never widens the capture to the whole desktop).
#
# Prints: [CaptureWindowRegion] method=PrintWindow|CopyFromScreen ok=<bool> size=<WxH> hwnd=<hex>
# Exit 0 on success, non-zero on failure.
#
# Usage:
#   .\Scripts\Agent\CaptureWindowRegion.ps1 -WindowTitle '[CAPTURE]' -OutputPath 'C:\...\editor-dashboard.png'
#   .\Scripts\Agent\CaptureWindowRegion.ps1 -WindowTitle 'Untitled' -OutputPath '.\n.png' -TitleMatch Prefix -Method CopyFromScreen
#
[CmdletBinding()]
param(
    # Title (or title prefix) of the target window.
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$WindowTitle,

    # Destination PNG path. Parent directory is created if missing.
    [Parameter(Mandatory = $true, Position = 1)]
    [string]$OutputPath,

    # Prefix: match windows whose title starts with -WindowTitle. Exact: full title equality.
    [ValidateSet('Prefix', 'Exact')]
    [string]$TitleMatch = 'Prefix',

    # Auto: PrintWindow then CopyFromScreen fallback. Otherwise force a single method.
    [ValidateSet('Auto', 'PrintWindow', 'CopyFromScreen')]
    [string]$Method = 'Auto'
)

$ErrorActionPreference = 'Stop'

# --- reporting state (used by the failure reporter too) ---
$script:methodUsed = $Method
$script:hwndHex = ''

function Write-CaptureLine([bool]$ok, [string]$size) {
    Write-Host "[CaptureWindowRegion] method=$($script:methodUsed) ok=$ok size=$size hwnd=$($script:hwndHex)"
}

function Exit-Fail([string]$m) {
    [Console]::Error.WriteLine("[CaptureWindowRegion] ERROR: $m")
    Write-CaptureLine $false ''
    exit 2
}

# --- load GDI+ / WinForms assemblies ---
try {
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
} catch {
    Exit-Fail "Could not load System.Drawing / System.Windows.Forms: $($_.Exception.Message)"
}

# --- Win32 P/Invoke (exact signatures) ---
if (-not ('W' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(int v); // -4 = Per-Monitor V2
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags); // flags 2 = PW_RENDERFULLCONTENT
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr hdc);
  public delegate bool EnumProc(IntPtr h, IntPtr lp);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
}

# --- Win32 constants ---
$HWND_TOPMOST   = [IntPtr](-1)
$SWP_NOMOVE     = 0x2
$SWP_SHOWWINDOW = 0x40
$SW_RESTORE     = 9
$PW_RENDERFULLCONTENT = 2

# 1. DPI awareness first (Per-Monitor V2). Ignore failure (awareness may already be fixed at start).
try { [void][W]::SetProcessDpiAwarenessContext(-4) } catch { }

# --- title matching (evaluated from the enum callbacks) ---
$script:FindTitle = $WindowTitle
$script:FindExact = ($TitleMatch -eq 'Exact')

function Test-TitleMatchPS([string]$t) {
    if ($script:FindExact) { return ($t -eq $script:FindTitle) }
    return $t.StartsWith($script:FindTitle, [System.StringComparison]::Ordinal)
}

function Get-WindowTitle([IntPtr]$h) {
    $len = [W]::GetWindowTextLength($h)
    if ($len -le 0) { return '' }
    $sb = [System.Text.StringBuilder]::new([int]($len + 1))
    [void][W]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

# --- HWND discovery: top-level visible, top-level any, then children of titled top-levels ---
$script:topVisible   = [System.Collections.Generic.List[IntPtr]]::new()
$script:topAny       = [System.Collections.Generic.List[IntPtr]]::new()
$script:childVisible = [System.Collections.Generic.List[IntPtr]]::new()
$script:childAny     = [System.Collections.Generic.List[IntPtr]]::new()
$script:titledTops   = [System.Collections.Generic.List[IntPtr]]::new()

$topCb = [W+EnumProc]{
    param([IntPtr]$h, [IntPtr]$lp)
    $t = Get-WindowTitle $h
    if ($t.Length -gt 0) {
        [void]$script:titledTops.Add($h)
        if (Test-TitleMatchPS $t) {
            if ([W]::IsWindowVisible($h)) { [void]$script:topVisible.Add($h) }
            else { [void]$script:topAny.Add($h) }
        }
    }
    return $true
}

$childCb = [W+EnumProc]{
    param([IntPtr]$h, [IntPtr]$lp)
    $t = Get-WindowTitle $h
    if ($t.Length -gt 0 -and (Test-TitleMatchPS $t)) {
        if ([W]::IsWindowVisible($h)) { [void]$script:childVisible.Add($h) }
        else { [void]$script:childAny.Add($h) }
    }
    return $true
}

[void][W]::EnumWindows($topCb, [IntPtr]::Zero)

if ($script:topVisible.Count -eq 0 -and $script:topAny.Count -eq 0) {
    foreach ($parent in $script:titledTops) {
        [void][W]::EnumChildWindows($parent, $childCb, [IntPtr]::Zero)
    }
}

$hwnd = [IntPtr]::Zero
if     ($script:topVisible.Count   -gt 0) { $hwnd = $script:topVisible[0] }
elseif ($script:topAny.Count       -gt 0) { $hwnd = $script:topAny[0] }
elseif ($script:childVisible.Count -gt 0) { $hwnd = $script:childVisible[0] }
elseif ($script:childAny.Count     -gt 0) { $hwnd = $script:childAny[0] }

if ($hwnd.ToInt64() -eq 0) {
    Exit-Fail "no window found matching title '$WindowTitle' (TitleMatch=$TitleMatch)"
}
$script:hwndHex = ('0x{0:X}' -f $hwnd.ToInt64())

# --- authoritative physical rect ---
function Get-PhysRect([IntPtr]$h) {
    $r = New-Object 'W+RECT'
    if (-not [W]::GetWindowRect($h, [ref]$r)) { return $null }
    return $r
}

$rect0 = Get-PhysRect $hwnd
if ($null -eq $rect0) { Exit-Fail "GetWindowRect failed for $($script:hwndHex)" }
$w0 = $rect0.Right - $rect0.Left
$h0 = $rect0.Bottom - $rect0.Top
if ($w0 -le 0 -or $h0 -le 0) { Exit-Fail "window rect is empty (${w0}x${h0}) for $($script:hwndHex)" }

# --- blank / uniform detection on a sampled grid ---
function Get-BitmapStat([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    if ($w -le 0 -or $h -le 0) { return [pscustomobject]@{ Count = 0; Distinct = 0; Mean = 0.0; Max = 0.0; Stdev = 0.0 } }
    $stepX = [Math]::Max(1, [int]($w / 40))
    $stepY = [Math]::Max(1, [int]($h / 40))
    $colors = [System.Collections.Generic.HashSet[int]]::new()
    $sum = 0.0; $sumSq = 0.0; $n = 0; $max = 0.0
    for ($y = 0; $y -lt $h; $y += $stepY) {
        for ($x = 0; $x -lt $w; $x += $stepX) {
            $c = $bmp.GetPixel($x, $y)
            $lum = 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B
            $sum += $lum; $sumSq += $lum * $lum; $n++
            if ($lum -gt $max) { $max = $lum }
            [void]$colors.Add((($c.R -shl 16) -bor ($c.G -shl 8) -bor $c.B))
        }
    }
    $mean = if ($n -gt 0) { $sum / $n } else { 0.0 }
    $var = if ($n -gt 0) { [Math]::Max(0.0, ($sumSq / $n) - ($mean * $mean)) } else { 0.0 }
    return [pscustomobject]@{
        Count    = $n
        Distinct = $colors.Count
        Mean     = $mean
        Max      = $max
        Stdev    = [Math]::Sqrt($var)
    }
}

function Test-BitmapBlank($stat) {
    if ($stat.Count -le 0) { return $true }                                 # nothing sampled
    if ($stat.Max -lt 12.0) { return $true }                                # near-black
    if ($stat.Distinct -le 2 -and $stat.Stdev -lt 3.0) { return $true }     # near-uniform
    return $false
}

function Invoke-PrintWindow([IntPtr]$h, [int]$w, [int]$ht) {
    $bmp = [System.Drawing.Bitmap]::new($w, $ht)
    $g = $null; $hdc = [IntPtr]::Zero; $ok = $false
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc()
        $ok = [W]::PrintWindow($h, $hdc, [uint32]$PW_RENDERFULLCONTENT)
    } finally {
        if ($hdc.ToInt64() -ne 0 -and $null -ne $g) { $g.ReleaseHdc($hdc) }
        if ($null -ne $g) { $g.Dispose() }
    }
    return [pscustomobject]@{ Ok = $ok; Bitmap = $bmp }
}

function Invoke-CopyFromScreen([IntPtr]$h) {
    [void][W]::ShowWindow($h, $SW_RESTORE)
    [void][W]::SetForegroundWindow($h)

    # Raise to front WITHOUT changing size: pass the current size and SWP_NOMOVE.
    # (cx=cy=0 without SWP_NOSIZE would shrink the window to its minimum track size.)
    $pre = Get-PhysRect $h
    if ($null -eq $pre) { return [pscustomobject]@{ Ok = $false; Bitmap = $null; Reason = 'GetWindowRect failed (pre)' } }
    $pw = $pre.Right - $pre.Left
    $ph = $pre.Bottom - $pre.Top
    if ($pw -le 0 -or $ph -le 0) { return [pscustomobject]@{ Ok = $false; Bitmap = $null; Reason = "empty rect (${pw}x${ph})" } }
    [void][W]::SetWindowPos($h, $HWND_TOPMOST, 0, 0, $pw, $ph, [uint32]($SWP_NOMOVE -bor $SWP_SHOWWINDOW))
    Start-Sleep -Milliseconds 400

    $r = Get-PhysRect $h
    if ($null -eq $r) { return [pscustomobject]@{ Ok = $false; Bitmap = $null; Reason = 'GetWindowRect failed' } }
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    if ($w -le 0 -or $ht -le 0) { return [pscustomobject]@{ Ok = $false; Bitmap = $null; Reason = "empty rect (${w}x${ht})" } }

    $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
    if ($r.Left -lt $vs.Left -or $r.Top -lt $vs.Top -or $r.Right -gt $vs.Right -or $r.Bottom -gt $vs.Bottom) {
        $msg = "rect L=$($r.Left) T=$($r.Top) R=$($r.Right) B=$($r.Bottom) is outside virtual screen " +
               "L=$($vs.Left) T=$($vs.Top) R=$($vs.Right) B=$($vs.Bottom)"
        return [pscustomobject]@{ Ok = $false; Bitmap = $null; Reason = $msg }
    }

    $bmp = [System.Drawing.Bitmap]::new($w, $ht)
    $g = $null
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($r.Left, $r.Top, 0, 0, [System.Drawing.Size]::new($w, $ht))
    } finally {
        if ($null -ne $g) { $g.Dispose() }
    }
    return [pscustomobject]@{ Ok = $true; Bitmap = $bmp; Reason = $null }
}

# --- choose capture path ---
$finalBmp = $null
$printWindowBlank = $false

if ($Method -eq 'PrintWindow' -or $Method -eq 'Auto') {
    $pwRes = Invoke-PrintWindow $hwnd $w0 $h0
    if ($pwRes.Ok) {
        $st = Get-BitmapStat $pwRes.Bitmap
        if (-not (Test-BitmapBlank $st)) {
            $finalBmp = $pwRes.Bitmap
            $script:methodUsed = 'PrintWindow'
        } else {
            $printWindowBlank = $true
            $pwRes.Bitmap.Dispose()
        }
    } else {
        $printWindowBlank = $true
        $pwRes.Bitmap.Dispose()
    }
    if ($printWindowBlank -and $Method -eq 'PrintWindow') {
        Exit-Fail "PrintWindow produced a blank/black image (window '$WindowTitle')"
    }
}

if ($null -eq $finalBmp -and ($Method -eq 'CopyFromScreen' -or $Method -eq 'Auto')) {
    $csRes = Invoke-CopyFromScreen $hwnd
    if (-not $csRes.Ok) { Exit-Fail "CopyFromScreen failed: $($csRes.Reason)" }
    $st2 = Get-BitmapStat $csRes.Bitmap
    if (Test-BitmapBlank $st2) {
        $csRes.Bitmap.Dispose()
        Exit-Fail "CopyFromScreen produced a blank image (window '$WindowTitle')"
    }
    $finalBmp = $csRes.Bitmap
    $script:methodUsed = 'CopyFromScreen'
}

if ($null -eq $finalBmp) { Exit-Fail 'no capture method succeeded' }

# --- save ---
$finalW = $finalBmp.Width
$finalH = $finalBmp.Height
try {
    $dir = Split-Path -Parent $OutputPath
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $finalBmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $finalBmp.Dispose()
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf) -or -not ((Get-Item -LiteralPath $OutputPath).Length -gt 0)) {
    Exit-Fail "PNG was not written to $OutputPath"
}

Write-CaptureLine $true "${finalW}x${finalH}"
exit 0
