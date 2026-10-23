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
# METINAR-040 (#85): AudioService haptics add android.permission.VIBRATE (normal level, approved); ApkAudit must pass it.
$p4 = Test-MetinARPermissionPolicy -Permissions (@($b.Permissions | Where-Object { $_ -ne 'android.permission.VIBRATE' }) + 'android.permission.VIBRATE') -Forbidden @($cfg.permissions.forbidden) -Allowed @($cfg.permissions.allowed)
Assert 'perm.vibrate-allowed' ($p4.Status -eq 'PASS' -and @($p4.Unexpected).Count -eq 0 -and @($p4.Forbidden).Count -eq 0) "status=$($p4.Status) unexpected=$($p4.Unexpected -join ',')"

# --- Unity log summary
$u = Get-MetinARUnityLogSummary -Lines (Lines 'unity-compile.log.txt')
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
$cpu = ConvertFrom-MetinARCpuInfo -Package 'com.metinar.game' -Lines @('Load: 7.1 / 6.9 / 6.5', '  112% 4242/com.metinar.game: 98% user + 14% kernel / faults: 10 minor', '  3.1% 1500/system_server: 2% user + 1.1% kernel')
Assert 'cpu.parse' ($cpu.CpuPercent -eq 112 -and $cpu.UserPercent -eq 98 -and $cpu.KernelPercent -eq 14)
Assert 'cpu.absent' ($null -eq (ConvertFrom-MetinARCpuInfo -Package 'x.y' -Lines @('  1% 1/init: 1% user')).CpuPercent)
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
Assert 'overall.unverified-only' ((Get-MetinAROverallStatus -Results @((New-MetinARGateResult -Name 'Device' -Status 'UNVERIFIED'))) -eq 'UNVERIFIED')
$md = ConvertTo-MetinARMarkdownReport -Title 'T' -Results $res -Header @{ Head = 'abc' }
Assert 'report.table' ($md -match '\| B \| \*\*UNVERIFIED\*\* \|' -and $md -match 'no / device' -and $md -match '### B')

# --- METINAR-014: branch-aware test discovery
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("metinar-qa-disc-" + [guid]::NewGuid().ToString('N'))
try {
    foreach ($d in @('tests/GeoCore.Tests', 'tests/MapCore.Tests', 'tests/Helpers', 'server/tests/MetinAR.Server.Tests', 'client/Tests/HandTracking.Tests')) { New-Item -ItemType Directory -Force -Path (Join-Path $tmp $d) | Out-Null }
    foreach ($f in @('tests/GeoCore.Tests/GeoCore.Tests.csproj', 'tests/MapCore.Tests/MapCore.Tests.csproj', 'tests/Helpers/Helpers.csproj', 'server/tests/MetinAR.Server.Tests/MetinAR.Server.Tests.csproj', 'client/Tests/HandTracking.Tests/HandTracking.Tests.csproj')) { Set-Content -Path (Join-Path $tmp $f) -Value '<Project />' }
    $cfgD = [pscustomobject]@{ testProjects = @('tests/GeoCore.Tests/GeoCore.Tests.csproj', 'tests/Gone.Tests/Gone.Tests.csproj'); optionalTestProjectGlobs = @('tests/*/*.Tests.csproj', 'server/tests/*/*.Tests.csproj', 'client/Tests/*/*.Tests.csproj', 'nothere/*/*.Tests.csproj') }
    $disc = @(Get-MetinARTestProjects -RepoRoot $tmp -Config $cfgD)
    $paths = @($disc | ForEach-Object { $_.Path })
    Assert 'discover.required-first' ($disc[0].Path -eq 'tests/GeoCore.Tests/GeoCore.Tests.csproj' -and $disc[0].Required -and $disc[0].Exists) ($paths -join ',')
    Assert 'discover.required-missing-kept' (@($disc | Where-Object { $_.Path -eq 'tests/Gone.Tests/Gone.Tests.csproj' -and $_.Required -and -not $_.Exists }).Count -eq 1)
    Assert 'discover.optional-found' (($paths -contains 'tests/MapCore.Tests/MapCore.Tests.csproj') -and ($paths -contains 'server/tests/MetinAR.Server.Tests/MetinAR.Server.Tests.csproj') -and ($paths -contains 'client/Tests/HandTracking.Tests/HandTracking.Tests.csproj')) ($paths -join ',')
    Assert 'discover.no-duplicates' (@($paths | Where-Object { $_ -eq 'tests/GeoCore.Tests/GeoCore.Tests.csproj' }).Count -eq 1)
    Assert 'discover.pattern-only' (-not ($paths -contains 'tests/Helpers/Helpers.csproj'))
    Assert 'discover.count' ($disc.Count -eq 5) ($paths -join ',')
    $none = @(Get-MetinARTestProjects -RepoRoot $tmp -Config ([pscustomobject]@{ testProjects = @('tests/GeoCore.Tests/GeoCore.Tests.csproj') }))
    Assert 'discover.no-globs' ($none.Count -eq 1)
} finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
$repoCfg = Read-MetinARJson -Path (Join-Path $PSScriptRoot '../qa-config.json')
$repoDisc = @(Get-MetinARTestProjects -RepoRoot (Join-Path $PSScriptRoot '../../..') -Config $repoCfg)
Assert 'discover.repo-required-exist' (@($repoDisc | Where-Object { $_.Required -and -not $_.Exists }).Count -eq 0) (($repoDisc | ForEach-Object { "$($_.Path)=$($_.Exists)" }) -join ',')

# --- METINAR-014: multiple latency metrics
$latSamples = @(
    [pscustomobject]@{ scene = 'WorldMapPrototype'; fpsAvg = 60.0 },
    [pscustomobject]@{ scene = 'WorldMapPrototype'; fpsAvg = 59.0; 'lat.map.tile.decode.avgMs' = 12.0; 'lat.map.tile.decode.p95Ms' = 95.0 },
    [pscustomobject]@{ scene = 'WorldMapPrototype'; fpsAvg = 58.0; 'lat.map.tile.decode.avgMs' = 10.0; 'lat.map.tile.decode.p95Ms' = 40.0 }
)
$latBudget = [pscustomobject]@{ minFpsAvg = 28; latencyMetrics = @(
    [pscustomobject]@{ metric = 'map.tile.decode'; maxAvgMs = 30; maxP95Ms = 80; required = $false },
    [pscustomobject]@{ metric = 'map.tile.mesh'; maxAvgMs = 20; maxP95Ms = 50; required = $false }) }
$lr = Test-MetinARPerfBudget -Samples $latSamples -Budget $latBudget -BudgetName 'WorldMapPrototype'
Assert 'lat.multi.fail-on-p95' ($lr.Status -eq 'FAIL' -and @($lr.Checks | Where-Object { $_.Metric -like 'lat.map.tile.decode.p95Ms*' -and $_.Status -eq 'FAIL' -and $_.Observed -eq 95 }).Count -eq 1) "$($lr.Status)"
Assert 'lat.optional-absent-skipped' (@($lr.Checks | Where-Object { $_.Metric -like 'lat.map.tile.mesh*' }).Count -eq 0)
$latBudget.latencyMetrics[1].required = $true
$lr2 = Test-MetinARPerfBudget -Samples $latSamples -Budget $latBudget -BudgetName 'WorldMapPrototype'
Assert 'lat.required-absent-unverified' (@($lr2.Checks | Where-Object { $_.Metric -like 'lat.map.tile.mesh*' -and $_.Status -eq 'UNVERIFIED' }).Count -eq 2)
$handBudget = [pscustomobject]@{ anyScene = $true; latencyMetrics = @([pscustomobject]@{ metric = 'lat.hand.inference'; maxAvgMs = 45; maxP95Ms = 70 }) }
$hr = Test-MetinARPerfBudget -Samples @([pscustomobject]@{ scene = 'A' }, [pscustomobject]@{ scene = 'B'; 'lat.hand.inference.avgMs' = 30.0; 'lat.hand.inference.p95Ms' = 60.0 }) -Budget $handBudget -BudgetName 'HandTracking'
Assert 'lat.hand-pass' ($hr.Status -eq 'PASS' -and @($hr.Checks).Count -eq 2) "$($hr.Status)"
$legacy = Test-MetinARPerfBudget -Samples @([pscustomobject]@{ scene = 'X'; 'lat.hand.inference.avgMs' = 50.0; 'lat.hand.inference.p95Ms' = 60.0 }) -Budget ([pscustomobject]@{ latencyMetric = 'lat.hand.inference'; maxLatencyAvgMs = 45; maxLatencyP95Ms = 70 })
Assert 'lat.legacy-schema' ($legacy.Status -eq 'FAIL')
$budgetFile = Read-MetinARJson -Path (Join-Path $PSScriptRoot '../perf-budgets.json')
Assert 'lat.budget-file-metrics' ((@($budgetFile.scenes.WorldMapPrototype.latencyMetrics | ForEach-Object { $_.metric }) -join ',') -eq 'map.tile.decode,map.tile.mesh' -and $budgetFile.scenes.HandTracking.latencyMetrics[0].metric -eq 'hand.inference')

# --- METINAR-014: AR smoke exception injection verdicts
$jOk = [pscustomobject]@{ Tests = 15; Failures = 0; Failed = @() }
$jT15 = [pscustomobject]@{ Tests = 15; Failures = 1; Failed = @('T15_NoExceptionsLogged: exceptions: InvalidOperationException: Injected project-code smoke exception') }
$jOther = [pscustomobject]@{ Tests = 15; Failures = 2; Failed = @('T03_Foo: x', 'T15_NoExceptionsLogged: exceptions: injected') }
Assert 'inject.metinar-fail-ok' (Get-MetinARSmokeInjectionVerdict -Mode 'metinar' -JUnit $jT15).Ok
Assert 'inject.metinar-pass-wrong' (-not (Get-MetinARSmokeInjectionVerdict -Mode 'metinar' -JUnit $jOk).Ok)
Assert 'inject.unknown-other-fail-wrong' (-not (Get-MetinARSmokeInjectionVerdict -Mode 'unknown' -JUnit $jOther).Ok)
Assert 'inject.searchdb-pass-ok' (Get-MetinARSmokeInjectionVerdict -Mode 'searchdb' -JUnit $jOk).Ok
Assert 'inject.searchdb-fail-wrong' (-not (Get-MetinARSmokeInjectionVerdict -Mode 'searchdb' -JUnit $jT15).Ok)
Assert 'inject.no-results' (-not (Get-MetinARSmokeInjectionVerdict -Mode 'unknown' -JUnit $null).Ok)

# --- #31 / METINAR-014: APK player data (stale .split chunks)
$D = 'assets/bin/Data/'
$crash52 = @('AndroidManifest.xml', 'lib/arm64-v8a/libil2cpp.so', "$($D)boot.config", "$($D)globalgamemanagers") +
    @(0..3 | ForEach-Object { "$($D)globalgamemanagers.assets.split$_" }) + @("$($D)level0", "$($D)level1", "$($D)sharedassets0.assets.split0", "$($D)sharedassets0.assets.split1", "$($D)sharedassets1.assets") +
    @(0..14 | ForEach-Object { "$($D)sharedassets1.assets.split$_" }) + @("$($D)Managed/Metadata/global-metadata.dat")
$pd = Test-MetinARApkPlayerData -Entries $crash52
Assert 'playerdata.crash-52a1375-rejected' (@($pd.Problems).Count -eq 1 -and $pd.Problems[0] -match 'sharedassets1\.assets exists both whole and as 15 \.split') "$($pd.Problems -join ' | ')"
$clean = @($crash52 | Where-Object { $_ -notmatch 'sharedassets1\.assets\.split' })
$pdc = Test-MetinARApkPlayerData -Entries $clean
Assert 'playerdata.clean-e18df69-layout-ok' (@($pdc.Problems).Count -eq 0 -and $pdc.SplitFiles.'globalgamemanagers.assets' -eq 4 -and $pdc.SplitFiles.'sharedassets0.assets' -eq 2)
Assert 'playerdata.missing-chunk' ((@((Test-MetinARApkPlayerData -Entries @("$($D)a.assets.split0", "$($D)a.assets.split2")).Problems) -join '|') -match 'missing \.split1')
Assert 'playerdata.not-from-zero' ((@((Test-MetinARApkPlayerData -Entries @("$($D)a.assets.split1")).Problems) -join '|') -match 'missing \.split0')
Assert 'playerdata.backslash' (@((Test-MetinARApkPlayerData -Entries @('assets\bin\Data\x.assets', 'assets\bin\Data\x.assets.split0')).Problems).Count -eq 1)
Assert 'playerdata.subfolders-ignored' (@((Test-MetinARApkPlayerData -Entries @("$($D)Managed/x.split0", "$($D)Resources/y.assets", "$($D)Resources/y.assets.split0", 'other/z.assets', 'other/z.assets.split0')).Problems).Count -eq 0)
Assert 'playerdata.empty' (@((Test-MetinARApkPlayerData -Entries @()).Problems).Count -eq 0)
# end-to-end over real zip files (what ApkAudit / Test-ApkPlayerData.ps1 / the device harness read)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zdir = Join-Path ([IO.Path]::GetTempPath()) ("metinar-qa-apk-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $zdir | Out-Null
function New-FakeApk([string]$path, [string[]]$names) {
    $fs = [IO.File]::Open($path, [IO.FileMode]::Create)
    $za = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    foreach ($n in $names) { $e = $za.CreateEntry($n); $w = New-Object IO.StreamWriter($e.Open()); $w.Write('x'); $w.Dispose() }
    $za.Dispose(); $fs.Dispose()
}
try {
    $bad = Join-Path $zdir 'bad.apk'; $good = Join-Path $zdir 'good.apk'
    New-FakeApk $bad $crash52; New-FakeApk $good $clean
    $rb = Get-MetinARApkReport -ApkPath $bad
    Assert 'playerdata.apkreport-bad' (@($rb.PlayerData.Problems).Count -eq 1 -and $rb.HasIl2Cpp)
    Assert 'playerdata.apkreport-good' (@((Get-MetinARApkReport -ApkPath $good).PlayerData.Problems).Count -eq 0)
    $tool = Join-Path $PSScriptRoot '../Test-ApkPlayerData.ps1'
    $psExe = (Get-Process -Id $PID).Path
    & $psExe -NoProfile -File $tool -ApkPath $good *> $null; $codeGood = $LASTEXITCODE
    & $psExe -NoProfile -File $tool -ApkPath $bad *> $null; $codeBad = $LASTEXITCODE
    Assert 'playerdata.cli-exit-codes' ($codeGood -eq 0 -and $codeBad -eq 1) "good=$codeGood bad=$codeBad"
} finally { Remove-Item -Recurse -Force $zdir -ErrorAction SilentlyContinue }

# --- #31 follow-up: serialized fields under #if UNITY_EDITOR / DEVELOPMENT_BUILD
$cs = @(
    'public class A : MonoBehaviour {',
    '    [SerializeField] private float _ok = 1f;',
    '    public int Visible;',
    '#if UNITY_EDITOR',
    '    [SerializeField] private string _editorNote;',
    '    public GameObject EditorOnlyRef;',
    '    private void OnValidate() { _ok = 2f; }',
    '    public static int Counter;',
    '    public bool Prop => true;',
    '    public int Auto { get; set; }',
    '#endif',
    '#if UNITY_ANDROID',
    '    [SerializeField] private int _androidOnly;',
    '#endif',
    '#if !UNITY_EDITOR && DEVELOPMENT_BUILD',
    '    [SerializeField]',
    '    private bool _devFlag;',
    '#else',
    '    [SerializeReference] private object _poly;',
    '#endif',
    '    void Update() {',
    '#if UNITY_EDITOR || DEVELOPMENT_BUILD',
    '        int local = 3;',
    '        Debug.Log(local);',
    '#endif',
    '    }',
    '}')
$cf = @(Find-MetinARConditionalSerializedFields -Lines $cs)
$cfl = @($cf | ForEach-Object { $_.Line })
Assert 'condfields.detects-editor-fields' (($cfl -contains 5) -and ($cfl -contains 6)) ($cfl -join ',')
Assert 'condfields.attr-on-previous-line' ($cfl -contains 17) ($cfl -join ',')
Assert 'condfields.else-branch-counts' ($cfl -contains 19) ($cfl -join ',')
Assert 'condfields.ignores-methods-static-unrelated-locals' (-not ($cfl -contains 7 -or $cfl -contains 8 -or $cfl -contains 9 -or $cfl -contains 10 -or $cfl -contains 13 -or $cfl -contains 23 -or $cfl -contains 2 -or $cfl -contains 3)) ($cfl -join ',')
Assert 'condfields.count' ($cf.Count -eq 4) ($cfl -join ',')
$hyg = @(& (Join-Path $PSScriptRoot '../Test-RepoHygiene.ps1') -RepoRoot (Join-Path $PSScriptRoot '../../..') -PassThru)
Assert 'condfields.repo-clean' (@($hyg | Where-Object { $_.Name -eq 'EditorOnlySerializedFields' -and $_.Status -eq 'PASS' }).Count -eq 1) (($hyg | Where-Object { $_.Name -match 'EditorOnly|ManagedStripping' } | ForEach-Object { "$($_.Name)=$($_.Status): $($_.Detail)" }) -join ' || ')
Assert 'stripping.reported' (@($hyg | Where-Object { $_.Name -eq 'ManagedStripping' -and $_.Detail -match 'Android stripping=' }).Count -eq 1)

Write-Host ''
Write-Host "QA tool tests: $script:passed passed, $script:failed failed"
if ($script:failed -gt 0) { exit 1 } else { exit 0 }
