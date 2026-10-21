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
function Run([string]$mode, [int]$expectedExit, [scriptblock]$check, [string[]]$extra = @('-SkipInstall'), [string]$package = 'com.metinar.game.qa', [string]$label = $mode) {
    $out = Join-Path ([IO.Path]::GetTempPath()) ("metinar-devsim-" + $label + "-" + [guid]::NewGuid().ToString('N').Substring(0, 6))
    $env:FAKE_ADB_MODE = $mode
    $env:FAKE_ADB_STATE = Join-Path $out 'state'
    $env:FAKE_ADB_PACKAGE = $package
    $env:FAKE_ADB_LOST_AFTER = '3'
    New-Item -ItemType Directory -Force $out | Out-Null
    & pwsh -NoProfile -File $harness -AdbPath $fake -LaunchWaitSeconds 0 -SoakSeconds 0 -AdbReconnectSeconds 3 -OutputDir $out @extra *> (Join-Path $out 'console.txt')
    $code = $LASTEXITCODE
    $ok = ($code -eq $expectedExit) -and (& $check $out)
    Write-Host ("{0} device-sim {1} exit={2} (expected {3}) -> {4}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $label, $code, $expectedExit, $out)
    if (-not $ok) { $script:failed++; Get-Content (Join-Path $out 'console.txt') | Write-Host }
}
function ReportOf([string]$o) { return (Get-Content -Raw (Join-Path $o 'device-report.md')) }
Run 'nodevice' 2 { param($o) (Get-Content -Raw (Join-Path $o 'device-report.md')) -match '\*\*Overall:\*\* UNVERIFIED' }
Run 'ok' 0 {
    param($o)
    $md = Get-Content -Raw (Join-Path $o 'device-report.md'); $log = Get-Content -Raw (Join-Path $o 'logcat-redacted.txt')
    ($md -match '\| PerfBudget \| \*\*PASS\*\*') -and ($md -match '\| Background \| \*\*PASS\*\*') -and ($md -match 'TotalTime=2345') -and
    ($md -notmatch 'FAKESERIAL123') -and ($log -notmatch 'FAKESERIAL123') -and ($log -notmatch '52\.2297') -and ($log -notmatch '192\.168\.1\.23')
}
Run 'crash' 1 { param($o) (Get-Content -Raw (Join-Path $o 'device-report.md')) -match '\| Crash \| \*\*FAIL\*\*' }
# METINAR-037 (#73): adb health is checked before every step.
Run 'ok' 0 { param($o) (ReportOf $o) -match 'adb devices checked \d+ times' -and (ReportOf $o) -notmatch '\| Adb \|' } -label 'adbchecks'
Run 'adblost' 3 {
    param($o)
    $md = ReportOf $o
    ($md -match '\| Adb \| \*\*FAIL\*\*') -and ($md -match "lost before step 'PermissionUi'") -and ($md -match '\| Crash \| \*\*UNVERIFIED\*\*') -and
    ($md -match '\| Memory \| \*\*UNVERIFIED\*\*') -and ($md -match 'Logcat/host gap') -and ($md -notmatch 'FAKESERIAL123') -and ($md -notmatch '\| Crash \| \*\*PASS\*\*')
}
Run 'adbblip' 0 { param($o) $md = ReportOf $o; ($md -match "adb gap before step 'PermissionUi'") -and ($md -match '\| Crash \| \*\*PASS\*\*') -and ($md -notmatch '\| Adb \|') }
# Install guard: never replace the installed game (protected package) with another build unless explicitly allowed.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$apkDir = Join-Path ([IO.Path]::GetTempPath()) ("metinar-devsim-apk-" + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Force $apkDir | Out-Null
$apk = Join-Path $apkDir 'fake.apk'
$fs = [IO.File]::Open($apk, [IO.FileMode]::Create)
$za = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($n in @('assets/bin/Data/globalgamemanagers', 'assets/bin/Data/data.unity3d', 'lib/arm64-v8a/libil2cpp.so')) { $e = $za.CreateEntry($n); $w = New-Object IO.StreamWriter($e.Open()); $w.Write('x'); $w.Dispose() }
$za.Dispose(); $fs.Dispose()
Run 'ok' 1 { param($o) (ReportOf $o) -match '\| Install \| \*\*FAIL\*\* \| [^|]*\| APK not installed: would replace the installed com\.metinar\.game' } @('-ApkPath', $apk, '-Package', 'com.metinar.game') 'com.metinar.game' 'guard-game'
Run 'ok' 0 { param($o) (ReportOf $o) -match '\| Install \| \*\*PASS\*\*' -and (ReportOf $o) -match 'replacing installed com\.metinar\.game' } @('-ApkPath', $apk, '-Package', 'com.metinar.game', '-AllowReplaceInstalled') 'com.metinar.game' 'guard-allowed'
Run 'ok' 0 { param($o) (ReportOf $o) -match '\| Install \| \*\*PASS\*\*' } @('-ApkPath', $apk) 'com.metinar.game.qa' 'qa-install'
Remove-Item -Recurse -Force $apkDir -ErrorAction SilentlyContinue
Write-Host "device simulation: $(if ($failed) { "$failed FAILED" } else { 'all passed' })"
if ($failed) { exit 1 } else { exit 0 }
