<#
.SYNOPSIS
  Runs tools/qa/Invoke-MetinARDeviceSmoke.ps1 against tests/fake-adb.sh (Linux/macOS, CI). Checks the harness logic,
  redaction and exit codes. This is a SIMULATION, never physical-device evidence.
#>
$ErrorActionPreference = 'Continue'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent | Split-Path -Parent
$harness = Join-Path $PSScriptRoot '..\Invoke-MetinARDeviceSmoke.ps1'
$fake = Join-Path $PSScriptRoot 'fake-adb.sh'
$failed = 0
function Run([string]$mode, [int]$expectedExit, [scriptblock]$check) {
    $out = Join-Path ([IO.Path]::GetTempPath()) ("metinar-devsim-" + $mode + "-" + [guid]::NewGuid().ToString('N').Substring(0, 6))
    $env:FAKE_ADB_MODE = $mode
    $env:FAKE_ADB_STATE = Join-Path $out 'state'
    New-Item -ItemType Directory -Force $out | Out-Null
    & pwsh -NoProfile -File $harness -AdbPath $fake -SkipInstall -LaunchWaitSeconds 0 -SoakSeconds 0 -OutputDir $out *> (Join-Path $out 'console.txt')
    $code = $LASTEXITCODE
    $ok = ($code -eq $expectedExit) -and (& $check $out)
    Write-Host ("{0} device-sim mode={1} exit={2} (expected {3}) -> {4}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $mode, $code, $expectedExit, $out)
    if (-not $ok) { $script:failed++; Get-Content (Join-Path $out 'console.txt') | Write-Host }
}
Run 'nodevice' 2 { param($o) (Get-Content -Raw (Join-Path $o 'device-report.md')) -match '\*\*Overall:\*\* UNVERIFIED' }
Run 'ok' 0 {
    param($o)
    $md = Get-Content -Raw (Join-Path $o 'device-report.md'); $log = Get-Content -Raw (Join-Path $o 'logcat-redacted.txt')
    ($md -match '\| PerfBudget \| \*\*PASS\*\*') -and ($md -match '\| Background \| \*\*PASS\*\*') -and ($md -match 'TotalTime=2345') -and
    ($md -notmatch 'FAKESERIAL123') -and ($log -notmatch 'FAKESERIAL123') -and ($log -notmatch '52\.2297') -and ($log -notmatch '192\.168\.1\.23')
}
Run 'crash' 1 { param($o) (Get-Content -Raw (Join-Path $o 'device-report.md')) -match '\| Crash \| \*\*FAIL\*\*' }
Write-Host "device simulation: $(if ($failed) { "$failed FAILED" } else { 'all passed' })"
if ($failed) { exit 1 } else { exit 0 }
