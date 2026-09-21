# UpdateMarkdownScreenshots.ps1
#
# Idempotent, marker-region screenshot inserter for Markdown docs (README.md and friends).
# It updates ONLY the text between a <!-- AUTO:<KEY>:START --> ... <!-- AUTO:<KEY>:END --> pair,
# replacing it with a single relative-path image line. Nothing outside a marker region is ever
# touched, so unrelated prose is safe across repeated runs.
#
# Contract (SOKOBAN_SCREENSHOT_TO_MARKDOWN_PIPELINE_REQUIREMENTS.md):
#   - relative paths only (absolute paths are forbidden);
#   - fail non-zero when a declared image file is missing, or a present marker is malformed;
#   - idempotent: running twice produces byte-identical output and touches the file only on change;
#   - does not rewrite content outside the markers.
#
# Usage:
#   .\Scripts\Agent\UpdateMarkdownScreenshots.ps1                  # README.md (default)
#   .\Scripts\Agent\UpdateMarkdownScreenshots.ps1 -Targets README.md,Docs\SHOWCASE.md
#   .\Scripts\Agent\UpdateMarkdownScreenshots.ps1 -Check           # verify only, no writes
#
[CmdletBinding()]
param(
    # Repo root. Defaults to two levels up from this script (Scripts/Agent/).
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,

    # Markdown files (repo-relative) to update.
    [string[]]$Targets = @('README.md'),

    # Verify-only mode: report what would change / what is broken, write nothing, exit 0 on clean.
    [switch]$Check
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error "[UpdateMarkdownScreenshots] $Message"
    exit 1
}

# Single source of truth for every screenshot we may embed. Key = marker suffix; Image = repo-relative
# PNG path; Caption = alt text. Markers NOT present in a target file are skipped (they are for other
# documents), so one map serves every Markdown doc. Keep in sync with SCREENSHOT_MANIFEST.md.
$ScreenshotMap = [ordered]@{
    'HERO_RUNTIME'             = @{ Image = 'Docs/Screenshots/runtime-gameplay.png';           Caption = '游戏实机' }
    'HERO_DASHBOARD'           = @{ Image = 'Docs/Screenshots/editor-dashboard.png';           Caption = '内容总览' }
    'HERO_EDITOR'              = @{ Image = 'Docs/Screenshots/editor-solution-preview.png';     Caption = '关卡编辑器与解法预览' }
    'RUNTIME_MAIN_MENU'        = @{ Image = 'Docs/Screenshots/runtime-main-menu.png';          Caption = '主菜单' }
    'RUNTIME_LEVEL_SELECT'     = @{ Image = 'Docs/Screenshots/runtime-level-select.png';       Caption = '关卡选择' }
    'RUNTIME_MULTIGROUP'       = @{ Image = 'Docs/Screenshots/runtime-multigroup.png';         Caption = '多组压力板与门' }
    'RUNTIME_COMPLETE'         = @{ Image = 'Docs/Screenshots/runtime-complete.png';           Caption = '关卡完成' }
    'EDITOR_LEVEL_EDITOR'      = @{ Image = 'Docs/Screenshots/editor-level-editor.png';        Caption = '关卡编辑器' }
    'EDITOR_VALIDATION'        = @{ Image = 'Docs/Screenshots/editor-validation.png';          Caption = '校验与死锁警告' }
    'EDITOR_ANALYZER'          = @{ Image = 'Docs/Screenshots/editor-analyzer.png';            Caption = '分析器' }
    'EDITOR_SOLUTION_PREVIEW'  = @{ Image = 'Docs/Screenshots/editor-solution-preview.png';    Caption = '解法预览' }
}

# Reject any absolute path in the map at startup (defense in depth against the "no absolute paths" rule).
foreach ($entry in $ScreenshotMap.Values) {
    if ([System.IO.Path]::IsPathRooted($entry.Image)) {
        Fail "absolute image path is forbidden: $($entry.Image)"
    }
}

foreach ($target in $Targets) {
    $absTarget = Join-Path $RepoRoot $target
    if (-not (Test-Path -LiteralPath $absTarget -PathType Leaf)) {
        Fail "target not found: $target"
    }

    $text = [System.IO.File]::ReadAllText($absTarget)
    $changed = $false

    foreach ($key in $ScreenshotMap.Keys) {
        $image   = $ScreenshotMap[$key].Image
        $caption = $ScreenshotMap[$key].Caption
        $startMarker = "<!-- AUTO:${key}:START -->"
        $endMarker   = "<!-- AUTO:${key}:END -->"

        $startIdx = $text.IndexOf($startMarker, [System.StringComparison]::Ordinal)
        if ($startIdx -lt 0) {
            # Marker absent from this document: not an error (it belongs to another doc).
            continue
        }

        $endIdx = $text.IndexOf($endMarker, $startIdx + $startMarker.Length, [System.StringComparison]::Ordinal)
        if ($endIdx -lt 0) {
            Fail "missing end marker for $startMarker in $target"
        }

        $absImage = Join-Path $RepoRoot $image
        if (-not (Test-Path -LiteralPath $absImage -PathType Leaf)) {
            Fail "image missing: $image (declared by ${startMarker} in ${target})"
        }

        # Relative path + alt text (alt reuses the caption; no absolute paths).
        $block = "$startMarker`r`n![$caption]($image)`r`n$endMarker"

        $newText = $text.Substring(0, $startIdx) + $block + $text.Substring($endIdx + $endMarker.Length)
        if ($newText -ceq $text) {
            Write-Host "[UpdateMarkdownScreenshots] $target : $key already up to date"
        }
        else {
            if ($Check) {
                Write-Host "[UpdateMarkdownScreenshots] $target : $key would be updated"
            }
            else {
                Write-Host "[UpdateMarkdownScreenshots] $target : $key updated"
            }
            $changed = $true
        }
        $text = $newText
    }

    if (-not $Check -and $changed) {
        # Normalize a single trailing newline and write UTF-8 without BOM (Windows PowerShell 5.1 safe).
        $text = $text.TrimEnd("`r", "`n") + "`r`n"
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($absTarget, $text, $utf8NoBom)
    }
}

Write-Host "[UpdateMarkdownScreenshots] PASS"
exit 0
