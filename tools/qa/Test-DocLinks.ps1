<#
.SYNOPSIS
  Checks that relative links in repository Markdown files point to existing files (METINAR-011). Offline; external
  http(s) links are not fetched.
.EXAMPLE
  pwsh -File tools/qa/Test-DocLinks.ps1
#>
[CmdletBinding()]
param([string]$RepoRoot, [switch]$PassThru)
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot 'MetinARQa.psm1') -Force -DisableNameChecking
if (-not $RepoRoot) { $RepoRoot = Get-MetinARRepoRoot -Start $PSScriptRoot }
$git = Find-MetinARGit
$md = @(& $git -C $RepoRoot -c core.quotepath=off ls-files --cached --others --exclude-standard -- '*.md' |
    Where-Object { $_ -notmatch '^client/(Assets|Packages)/' })
$broken = New-Object System.Collections.Generic.List[string]
$count = 0
foreach ($f in $md) {
    $path = Join-Path $RepoRoot $f
    if (-not (Test-Path $path)) { continue }
    $inFence = $false
    $n = 0
    foreach ($line in (Get-Content $path)) {
        $n++
        if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        foreach ($m in [regex]::Matches($line, '\[[^\]]*\]\(([^)\s]+)(\s+"[^"]*")?\)')) {
            $target = $m.Groups[1].Value
            if ($target -match '^(https?:|mailto:|#)' -or $target -match '^<') { continue }
            $count++
            $clean = ($target -split '#')[0]
            if (-not $clean) { continue }
            $clean = [uri]::UnescapeDataString($clean)
            $resolved = if ($clean.StartsWith('/')) { Join-Path $RepoRoot $clean.TrimStart('/') } else { Join-Path (Split-Path $path -Parent) $clean }
            if (-not (Test-Path $resolved)) { $broken.Add("${f}:${n} -> $target") }
        }
    }
}
$result = [pscustomobject]@{ Name = 'DocLinks'; Status = $(if ($broken.Count) { 'FAIL' } else { 'PASS' }); Detail = "$($md.Count) markdown files, $count relative links, $($broken.Count) broken"; Broken = $broken.ToArray() }
if ($PassThru) { return $result }
Write-Host ("[{0}] {1}" -f $result.Status, $result.Detail)
foreach ($b in $broken) { Write-Host "  broken: $b" }
if ($broken.Count) { exit 1 } else { exit 0 }
