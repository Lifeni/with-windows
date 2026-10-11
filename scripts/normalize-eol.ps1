# Normalize working-tree line endings to CRLF for paths that .gitattributes marks as `eol=crlf`.
#
# Why: files written by tools (apply_patch, generators) default to LF, while this repo requires CRLF for
# *.cs / *.xaml / *.csproj / *.props / *.md. Git keeps LF in the index either way, so the only effects are the
# repeated "LF will be replaced by CRLF the next time Git touches it" notice and a worktree that differs from
# what a fresh checkout produces. Run this script to align the worktree.
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts/normalize-eol.ps1
#
# NOTE: kept ASCII-only on purpose. Windows PowerShell 5.1 reads .ps1 as ANSI when there is no BOM, so
# non-ASCII text here would be mangled (the Chinese explanation lives in AGENTS.md instead).

$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

# .gitattributes declares `* text=auto eol=crlf`, so every text file should be CRLF in the worktree.
# Binary files are detected by content (a NUL byte) rather than by calling `git check-attr`, because
# Windows PowerShell 5.1 handled that output differently and silently matched nothing.
$binaryExtensions = @('.png', '.ico', '.pfx', '.dll', '.exe', '.zip', '.woff', '.woff2')

$converted = 0
foreach ($file in (git ls-files)) {
    if ($binaryExtensions -contains [System.IO.Path]::GetExtension($file).ToLowerInvariant()) { continue }

    $path = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }

    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -eq 0) { continue }
    if ($bytes -contains 0) { continue }   # a NUL byte means binary

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $normalized = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"

    if ($text -cne $normalized) {
        [System.IO.File]::WriteAllText($path, $normalized, (New-Object System.Text.UTF8Encoding($hasBom)))
        $converted++
    }
}

Write-Host "Normalized $converted file(s) to CRLF. The git index is unchanged, so git status should stay clean."
