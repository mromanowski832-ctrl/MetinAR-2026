<#
.SYNOPSIS
  Dependency-free unit tests for tools/qa/MetinARQa.psm1 (METINAR-011). Works on Windows PowerShell 5.1 and pwsh 7
  (no Pester required). Exit 0 = all passed.
#>
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot '..\MetinARQa.psm1') -Force -DisableNameChecking
$fx = Join-Path $PSScriptRoot 'fixtures'
$script:passed = 0; $script:failed = 0
function Assert([string]$name, [bool]$cond, [string]$info = '') {
    if ($cond) { $script:passed++; Write-Host "PASS $name" -ForegroundColor Green }
    else { $script:failed++; Write-Host "FAIL $name $info" -ForegroundColor Red }
}
function Lines([string]$f) { return @(Get-Content (Join-Path $fx $f)) }

# --- aapt badging
$b = ConvertFrom-MetinARAaptBadging -Lines (Lines 'aapt-badging.txt')
Assert 'badging.package' ($b.Package -eq 'com.metinar.game')
Assert 'badging.sdk' ($b.MinSdk -eq 31 -and $b.TargetSdk -eq 35)
Assert 'badging.debuggable' ($b.Debuggable -eq $true)
Assert 'badging.abi' (@($b.NativeCode) -join ',' -eq 'arm64-v8a')
Assert 'badging.activity' ($b.LaunchableActivity -eq 'com.unity3d.player.UnityPlayerGameActivity')
Assert 'badging.permissions' (@($b.Permissions).Count -eq 5)

# --- permission policy
$cfg = Get-Content -Raw (Join-Path $PSScriptRoot '..\qa-config.json') | ConvertFrom-Json
$p = Test-MetinARPermissionPolicy -Permissions $b.Permissions -Forbidden @($cfg.permissions.forbidden) -Allowed @($cfg.permissions.allowed)
Assert 'perm.current-build-pass' ($p.Status -eq 'PASS' -and $p.BackgroundLocationAbsent) "status=$($p.Status) unexpected=$($p.Unexpected -join ',')"
$p2 = Test-MetinARPermissionPolicy -Permissions (@($b.Permissions) + 'android.permission.ACCESS_BACKGROUND_LOCATION') -Forbidden @($cfg.permissions.forbidden) -Allowed @($cfg.permissions.allowed)
Assert 'perm.background-location-fails' ($p2.Status -eq 'FAIL' -and -not $p2.BackgroundLocationAbsent)
$p3 = Test-MetinARPermissionPolicy -Permissions (@($b.Permissions) + 'android.permission.BLUETOOTH_CONNECT') -Forbidden @($cfg.permissions.forbidden) -Allowed @($cfg.permissions.allowed)
Assert 'perm.unexpected-warns' ($p3.Status -eq 'WARN' -and @($p3.Unexpected) -contains 'android.permission.BLUETOOTH_CONNECT')

# --- Unity log summary
$u = Get-MetinARUnityLogSummary -Lines (Lines 'unity-compile.log')
Assert 'unity.errors' ($u.CompilerErrors -eq 1)
Assert 'unity.warnings-deduped' ($u.CompilerWarnings -eq 1)
Assert 'unity.exceptions' ($u.Exceptions -eq 1)
Assert 'unity.blocking' (@($u.BlockingLines) -contains 'Scripts have compiler errors.')
$clean = Get-MetinARUnityLogSummary -Lines @('Exiting batchmode successfully now!')
Assert 'unity.clean' ($clean.CompilerErrors -eq 0 -and $clean.BlockingLines.Count -eq 0)

# --- redaction
$r = ConvertTo-MetinARRedactedText -Lines @(
    '10-20 04:30:29.000  4242  4242 I Unity   : GPS fix lat=52.229770 lon=21.011780 acc=5 from 192.168.1.23 device serial=R5CT12345AB',
    'pos 52.22977,21.01178 mail owner@example.com mac aa:bb:cc:dd:ee:ff',
    'C:\Users\mroma\MetinAR-2026\client and /home/someone/x',
    'android_id=9774d56d682e549c imei 356938035643809 ipv6 fe80::1ff:fe23:4567:890a',
    'device R5CT12345AB attached; localhost 127.0.0.1 kept') -Secrets @('R5CT12345AB')
$all = $r -join "`n"
Assert 'redact.time-kept' ($r[0].StartsWith('10-20 04:30:29.000')) $r[0]
Assert 'redact.coords' ($all -notmatch '52\.2297' -and $all -notmatch '21\.0117') $all
Assert 'redact.ip' ($all -notmatch '192\.168\.1\.23' -and $all -match '127\.0\.0\.1')
Assert 'redact.serial' ($all -notmatch 'R5CT12345AB')
Assert 'redact.email' ($all -notmatch 'owner@example\.com')
Assert 'redact.mac' ($all -notmatch 'aa:bb:cc:dd:ee:ff')
Assert 'redact.userpath' ($all -notmatch 'mroma' -and $all -notmatch 'someone')
Assert 'redact.ids' ($all -notmatch '9774d56d682e549c' -and $all -notmatch '356938035643809' -and $all -notmatch 'fe80::1ff')
$keep = ConvertTo-MetinARRedactedText -Lines @('sha256: 3f5a0c0e1b2d4f6a8c9e0b1d2f3a4c5e6f708192a3b4c5d6e7f8091a2b3c4d5e') -KeepHex
Assert 'redact.keephex' ($keep[0] -match '3f5a0c0e1b2d4f6a')

# --- logcat problems
$lc = Find-MetinARLogcatProblems -Lines (Lines 'logcat-crash.txt') -Package 'com.metinar.game' -Pids @(4242)
Assert 'logcat.crash' ($lc.Status -eq 'FAIL' -and $lc.Crashes.Count -ge 2)
Assert 'logcat.anr' ($lc.Anrs.Count -eq 1)
Assert 'logcat.fatal-other-pid-ignored' ($lc.FatalLines.Count -eq 0)
Assert 'logcat.unity-error' ($lc.UnityErrors.Count -eq 1)
$ok = Find-MetinARLogcatProblems -Lines (Lines 'logcat-clean.txt') -Package 'com.metinar.game' -Pids @(4242)
Assert 'logcat.clean-ignores-other-apps' ($ok.Status -eq 'PASS') "crash=$($ok.Crashes -join '|') anr=$($ok.Anrs -join '|') fatal=$($ok.FatalLines -join '|')"

# --- perf samples + budgets
$s = Get-MetinARPerfSamples -Lines (Lines 'logcat-crash.txt')
Assert 'perf.parse-count' ($s.Count -eq 2)
Assert 'perf.parse-values' ($s[0].scene -eq 'WorldMapPrototype' -and $s[0].fpsAvg -eq 30 -and $s[1].frameSpikes -eq 2 -and $s[0].totalUsedMB -eq 300.5)
$budgets = Get-Content -Raw (Join-Path $PSScriptRoot '..\perf-budgets.json') | ConvertFrom-Json
$bud = Test-MetinARPerfBudget -Samples $s -Budget $budgets.scenes.WorldMapPrototype -BudgetName 'WorldMapPrototype'
Assert 'perf.budget-fails-low-fps' ($bud.Status -eq 'FAIL' -and $bud.Windows -eq 1) "status=$($bud.Status)"
$fps = @($bud.Checks | Where-Object { $_.Metric -like 'fpsAvg*' })[0]
Assert 'perf.budget-fps-observed' ($fps.Observed -eq 24 -and $fps.Status -eq 'FAIL')
$good = Test-MetinARPerfBudget -Samples @($s[0], $s[0]) -Budget $budgets.scenes.WorldMapPrototype -BudgetName 'WorldMapPrototype'
Assert 'perf.budget-pass' ($good.Status -eq 'PASS') (($good.Checks | ForEach-Object { "$($_.Metric)=$($_.Status)" }) -join ',')
$none = Test-MetinARPerfBudget -Samples @() -Budget $budgets.scenes.WorldMapPrototype
Assert 'perf.budget-no-samples-unverified' ($none.Status -eq 'UNVERIFIED')
$hand = Test-MetinARPerfBudget -Samples @($s[0], $s[0]) -Budget $budgets.scenes.HandTracking -BudgetName 'HandTracking'
Assert 'perf.hand-latency-unverified-until-007' ($hand.Status -eq 'UNVERIFIED')

# --- dumpsys parsers
$m = ConvertFrom-MetinARMeminfo -Lines (Lines 'meminfo.txt')
Assert 'meminfo.pss' ($m.TotalPssKB -eq 312345 -and $m.NativeHeapKB -eq 81920 -and $m.GraphicsKB -eq 120000)
$sf = ConvertFrom-MetinARSurfaceFlingerLatency -Lines (Lines 'surfaceflinger-latency.txt')
Assert 'sf.frames' ($sf.Frames -eq 3 -and $sf.Janks -eq 1 -and $sf.RefreshMs -eq 16.67) "frames=$($sf.Frames) janks=$($sf.Janks) refresh=$($sf.RefreshMs)"
Assert 'sf.fps' ([math]::Abs($sf.FpsAvg - 36) -lt 0.5) "fps=$($sf.FpsAvg)"
$gp = ConvertFrom-MetinARGrantedPermissions -Lines (Lines 'dumpsys-package.txt')
Assert 'perm.runtime-state' ($gp.'android.permission.ACCESS_FINE_LOCATION' -eq $true -and $gp.'android.permission.CAMERA' -eq $false)
$th = ConvertFrom-MetinARThermal -ThermalLines @('IsStatusOverride: false', 'Thermal Status: 2') -BatteryLines @('  level: 81', '  temperature: 345')
Assert 'thermal.parse' ($th.ThermalStatus -eq 2 -and $th.BatteryLevel -eq 81 -and $th.BatteryTempC -eq 34.5)

# --- JUnit
$j = Get-MetinARJUnitSummary -Path (Join-Path $fx 'junit.xml')
Assert 'junit.counts' ($j.Tests -eq 3 -and $j.Failures -eq 1 -and $j.Name -eq 'ArEncounterSmokeTest')
Assert 'junit.failed-names' (@($j.Failed).Count -eq 1 -and $j.Failed[0] -eq 'T02_Missing: 2 missing scripts') "$($j.Failed -join '|')"
Assert 'junit.missing-file' ($null -eq (Get-MetinARJUnitSummary -Path (Join-Path $fx 'nope.xml')))

# --- report + overall
$res = @(
    (New-MetinARGateResult -Name 'A' -Status 'PASS' -Detail 'ok'),
    (New-MetinARGateResult -Name 'B' -Status 'UNVERIFIED' -Detail 'no | device' -Evidence @('line1'))
)
Assert 'overall.warnings' ((Get-MetinAROverallStatus -Results $res) -eq 'PASS_WITH_WARNINGS')
Assert 'overall.fail' ((Get-MetinAROverallStatus -Results (@($res) + (New-MetinARGateResult -Name 'C' -Status 'FAIL'))) -eq 'FAIL')
$md = ConvertTo-MetinARMarkdownReport -Title 'T' -Results $res -Header @{ Head = 'abc' }
Assert 'report.table' ($md -match '\| B \| \*\*UNVERIFIED\*\* \|' -and $md -match 'no / device' -and $md -match '### B')

Write-Host ''
Write-Host "QA tool tests: $script:passed passed, $script:failed failed"
if ($script:failed -gt 0) { exit 1 } else { exit 0 }
