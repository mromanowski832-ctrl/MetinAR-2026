<#
.SYNOPSIS
  Lightweight pull-request metadata check for GitHub Actions (METINAR-011). Reads the pull_request event payload.
  FAIL: PR targets a branch other than main from main itself, or the body is empty.
  WARN (annotation only): no METINAR-<n> in the title, no linked issue (#n), missing Verification / Known limitations
  sections, or no explicit physical-device statement (VERIFIED / UNVERIFIED / NOT REQUIRED).
.EXAMPLE
  pwsh -File tools/qa/Test-PrMetadata.ps1 -EventPath $env:GITHUB_EVENT_PATH
#>
[CmdletBinding()]
param([string]$EventPath = $env:GITHUB_EVENT_PATH, [string]$Title, [string]$Body, [string]$HeadRef, [string]$BaseRef)
$ErrorActionPreference = 'Continue'
if ($EventPath -and (Test-Path $EventPath)) {
    $ev = Get-Content -Raw $EventPath | ConvertFrom-Json
    if ($ev.PSObject.Properties['pull_request']) {
        $pr = $ev.pull_request
        $Title = $pr.title; $Body = [string]$pr.body; $HeadRef = $pr.head.ref; $BaseRef = $pr.base.ref
    }
}
if (-not $Title) { Write-Host 'No pull_request payload; nothing to check.'; exit 0 }
$fail = @(); $warn = @()
if ($HeadRef -eq 'main') { $fail += 'head branch is main (work must happen on an issue branch)' }
if ($BaseRef -and $BaseRef -ne 'main') { $warn += "base branch is $BaseRef (expected main)" }
if (-not $Body -or -not $Body.Trim()) { $fail += 'PR body is empty (use .github/pull_request_template.md)' } else {
    if ($Body -notmatch '#\d+') { $warn += 'no linked issue (#<n>) in the PR body' }
    if ($Body -notmatch '(?i)(merge gate|verification)') { $warn += 'no Merge gate / Verification section' }
    if ($Body -notmatch '(?i)known limitations') { $warn += 'no Known limitations section' }
    if ($Body -notmatch '(?i)(UNVERIFIED|VERIFIED|NOT REQUIRED)') { $warn += 'no explicit physical-device status (VERIFIED / UNVERIFIED / NOT REQUIRED)' }
    if ($Body -match '(?i)ACCESS_BACKGROUND_LOCATION\s*[:=]?\s*PRESENT') { $fail += 'PR body reports ACCESS_BACKGROUND_LOCATION PRESENT' }
}
if ($Title -notmatch 'METINAR-\d+') { $warn += 'title does not reference METINAR-<n>' }
foreach ($w in $warn) { Write-Host "::warning title=PR metadata::$w" }
foreach ($f in $fail) { Write-Host "::error title=PR metadata::$f" }
Write-Host ("PR metadata: {0} error(s), {1} warning(s) for '{2}' ({3} -> {4})" -f $fail.Count, $warn.Count, $Title, $HeadRef, $BaseRef)
if ($fail.Count) { exit 1 } else { exit 0 }
