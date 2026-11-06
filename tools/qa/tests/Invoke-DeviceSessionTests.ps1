<#
.SYNOPSIS
  Tests for the #90 (METINAR-045) device session evidence collector: tools/qa/Invoke-MetinARDeviceSession.ps1 and its
  helpers in MetinARQa.psm1. Dependency-free like Invoke-QaToolTests.ps1 (no Pester: Windows PowerShell 5.1 ships
  Pester 3.4). Part 1 (pure helpers) runs on Windows PowerShell 5.1 and pwsh 7. Part 2 runs the collector against
  tests/fake-adb.sh (Linux/macOS, CI) - a SIMULATION, never physical-device evidence. Exit 0 = all passed.
#>
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot '..\MetinARQa.psm1') -Force -DisableNameChecking
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$script:passed = 0; $script:failed = 0
function Assert([string]$name, [bool]$cond, [string]$info = '') {
    if ($cond) { $script:passed++; Write-Host "PASS $name" -ForegroundColor Green }
    else { $script:failed++; Write-Host "FAIL $name $info" -ForegroundColor Red }
}
$budgets = Get-Content -Raw (Join-Path $PSScriptRoot '..\perf-budgets.json') | ConvertFrom-Json
$target = $budgets.productTargets.ArCombatA31

# --- Part 1: pure helpers -------------------------------------------------------------------------------------------
Assert 'budgets.productTarget-present' ($null -ne $target -and $target.requireCombo -eq $true -and [double]$target.minFpsMedian -gt [double]$budgets.scenes.ArEncounterPrototype.minFpsAvg)
Assert 'budgets.decision1-thresholds' ([double]$target.minFpsMedian -eq 29.5 -and [double]$target.minFpsP5 -eq 27 -and [double]$target.maxFrameP50MedianMs -eq 33.9 -and [double]$target.maxFrameP95Ms -eq 40 -and [int]$target.maxFrameSpikesPerWindow -eq 2 -and [double]$target.maxMainP95Ms -eq 40 -and [int]$target.warmupSeconds -eq 60)
Assert 'budgets.decision2-scene-from-config' ((@($target.sceneNames) -join ',') -eq 'ArEncounterPrototype')
$collectorText = Get-Content -Raw (Join-Path $PSScriptRoot '..\Invoke-MetinARDeviceSession.ps1')
Assert 'collector.scene-not-hardcoded' ($collectorText -notmatch "'ArEncounterPrototype'" -and $collectorText -match '\[string\[\]\]\$CombatScene')
Assert 'budgets.scenes-unchanged' ($budgets.scenes.ArEncounterPrototype.minFpsAvg -eq 24 -and $budgets.scenes.ArEncounterPrototype.maxFrameP95Ms -eq 55)

$stepsDef = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot '..\etap1-steps.json') | ConvertFrom-Json
$steps = $stepsDef.steps
Assert 'steps.22+A97' (@($steps).Count -eq 23 -and (@($steps | ForEach-Object { $_.id }) -join ',') -eq (((1..22) -join ',') + ',A97'))
# A97 (#97, lead decision 04:43): mandatory step, criterion copied word for word from #97 (same text as PR #102 section 3.1).
# The expected value is the SHA-256 of the UTF-8 quote (this file must stay ASCII, so the text itself is not repeated here).
$a97 = $steps | Where-Object { [string]$_.id -ceq 'A97' } | Select-Object -First 1
$a97Hash = $null
if ($a97) { $sha = [System.Security.Cryptography.SHA256]::Create(); try { $a97Hash = (($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$a97.criterion)) | ForEach-Object { $_.ToString('x2') }) -join '') } finally { $sha.Dispose() } }
Assert 'steps.A97-verbatim-criterion' ($a97Hash -ceq 'c9c7ba1bb9be5226b1b83041e1bfdd9952bcb808ae23afdd3b7b1d7f4458fefc') "sha=$a97Hash"
Assert 'steps.A97-mandatory' ($a97.mandatory -eq $true -and (@($a97.dependsOn) -contains '#97') -and ((@($a97.relatedSteps) -join ',') -eq '8,10,11') -and $a97.evidence -match 'A31' -and $a97.evidence -match 'UNVERIFIED')
# review #100 point 5: step 8 follows #97 (spawn 1,10-3,0 m, mob approaches and stops at d_stop ~1,32 m, d_min 1,10 m)
Assert 'steps.8-matches-97' ($steps[7].criterion -match '1,32 m' -and $steps[7].criterion -match 'd_min 1,10 m' -and $steps[7].criterion -notmatch '1,5' -and (@($steps[7].dependsOn) -contains '#97'))
Assert 'steps.requiredStepIds' ((@($stepsDef.requiredStepIds | ForEach-Object { [string]$_ }) -join ',') -eq (((1..22) -join ',') + ',A97'))
Assert 'steps.fields' (@($steps | Where-Object { -not $_.step -or -not $_.criterion -or -not $_.evidence -or -not $_.fixOwner }).Count -eq 0)
Assert 'steps.patterns-compile' (@($steps | Where-Object { $_.logPattern } | Where-Object { try { [void][regex]::new($_.logPattern); $false } catch { $true } }).Count -eq 0)

Assert 'logtime.parse' ((Get-MetinARLogcatTime '10-20 04:30:18.123  4242  4242 I Unity   : x') -eq '10-20 04:30:18.123')
Assert 'logtime.none' ($null -eq (Get-MetinARLogcatTime '--------- beginning of main'))
Assert 'logtime.seconds' (((ConvertTo-MetinARLogcatSeconds '10-20 04:31:00.500') - (ConvertTo-MetinARLogcatSeconds '10-20 04:30:59.000')) -eq 1.5)

$c = New-MetinARLogcatCollector
$a1 = Add-MetinARLogcatChunk -Collector $c -Lines @('--------- beginning of main', '10-20 04:30:00.000  1  1 I A: one', '10-20 04:30:01.000  1  1 I A: two', '  continuation of two', '10-20 04:30:01.000  1  1 I A: two-b')
$a2 = Add-MetinARLogcatChunk -Collector $c -Lines @('10-20 04:30:00.000  1  1 I A: one', '10-20 04:30:01.000  1  1 I A: two', '  continuation of two', '10-20 04:30:01.000  1  1 I A: two-b', '10-20 04:30:01.000  1  1 I A: two-c', '10-20 04:30:02.000  1  1 I A: three')
Assert 'merge.first' ($a1 -eq 4) "added=$a1"
Assert 'merge.dedupe' ($a2 -eq 2 -and $c.Lines.Count -eq 6 -and $c.Gaps.Count -eq 0) "added=$a2 total=$($c.Lines.Count)"
$a3 = Add-MetinARLogcatChunk -Collector $c -Lines @('10-20 04:30:09.000  1  1 I A: after rollover')
Assert 'merge.gap-recorded' ($a3 -eq 1 -and $c.Gaps.Count -eq 1 -and $c.Gaps[0] -match 'rolled over')

$gfxLines = @('Total frames rendered: 120', 'Janky frames: 6 (5.00%)', '50th percentile: 33ms', '90th percentile: 34ms', '95th percentile: 36ms', '99th percentile: 50ms', '---PROFILEDATA---',
    'Flags,IntendedVsync,Vsync,OldestInputEvent,NewestInputEvent,HandleInputStart,AnimationStart,PerformTraversalsStart,DrawStart,SyncQueued,SyncStart,IssueDrawCommandsStart,SwapBuffers,FrameCompleted,',
    '0,1000000000,1000000000,0,0,0,0,0,0,0,0,0,0,1030000000,', '0,1033333333,1033333333,0,0,0,0,0,0,0,0,0,0,1073333333,', '1,5,5,0,0,0,0,0,0,0,0,0,0,999999999,', '---PROFILEDATA---')
$g = ConvertFrom-MetinARGfxInfo -Lines $gfxLines
Assert 'gfx.summary' ($g.TotalFrames -eq 120 -and $g.JankyPercent -eq 5 -and $g.P50Ms -eq 33 -and $g.P95Ms -eq 36 -and $g.P99Ms -eq 50)
Assert 'gfx.profiledata' ($g.ProfileFrames -eq 2 -and $g.ProfileP50Ms -eq 30 -and $g.ProfileP95Ms -eq 40) "frames=$($g.ProfileFrames) p50=$($g.ProfileP50Ms) p95=$($g.ProfileP95Ms)"
$g0 = ConvertFrom-MetinARGfxInfo -Lines @('Total frames rendered: 0', 'Janky frames: 0 (0.00%)')
Assert 'gfx.empty-unity-surface' ($g0.TotalFrames -eq 0 -and $g0.ProfileFrames -eq 0 -and $null -eq $g0.P50Ms)

Assert 'pct.nearest-rank' ((Get-MetinARPercentile -Values @(10, 20, 30, 40, 50, 60, 70, 80, 90, 100) -Percent 5) -eq 10 -and (Get-MetinARPercentile -Values @(1, 2, 3, 4) -Percent 50) -eq 2)
Assert 'pct.empty' ($null -eq (Get-MetinARPercentile -Values @() -Percent 50))

Assert 'commit.from-name' ((Get-MetinARApkCommitFromName 'C:\b\MetinAR-v0c2-ee48cb9-dev.apk') -eq 'ee48cb9' -and $null -eq (Get-MetinARApkCommitFromName 'x/MetinAR-ar-encounter-qa.apk'))
$h = Get-MetinARSerialHash 'FAKESERIAL123'
Assert 'serial.hash' ($h -match '^[0-9a-f]{12}$' -and $h -ne 'FAKESERIAL123' -and $h -eq (Get-MetinARSerialHash 'FAKESERIAL123'))
# decision 4: first 12 hex of SHA-256 over the UTF-8 serial (reference value from `printf FAKESERIAL123 | sha256sum`)
Assert 'serial.hash-known-value' ($h -ceq '3b9bbb471cfd') "h=$h"
Assert 'serial.hash-empty-null' ($null -eq (Get-MetinARSerialHash '') -and $null -eq (Get-MetinARSerialHash $null))

# synthetic combat timeline: 30 windows of 5 s from 04:30:00; combat (+combo) lines in every window after 04:31:00
function New-Timeline([double]$fps = 29.6, [double]$p95 = 35, [switch]$NoCombo, [switch]$NoCombat, [string]$ComboText = '[Combo] step=1 weapon=1H hit=yes') {
    $l = New-Object System.Collections.Generic.List[string]
    for ($s = 5; $s -le 150; $s += 5) {
        $t = ([datetime]'2000-10-20 04:30:00').AddSeconds($s)
        if (-not $NoCombat -and $s -gt 60) {
            $l.Add(('{0:MM-dd HH:mm:ss}.500  4242  4260 I Unity   : [ArDamage] swing={1} outcome=Hit' -f $t.AddSeconds(-3), $s))
            if (-not $NoCombo) { $l.Add(('{0:MM-dd HH:mm:ss}.600  4242  4260 I Unity   : {2} swing={1}' -f $t.AddSeconds(-3), $s, $ComboText)) }
        }
        $f = if ($s -gt 60) { $fps } else { 29.6 }; $q = if ($s -gt 60) { $p95 } else { 35 }
        $l.Add(('{0:MM-dd HH:mm:ss}.000  4242  4260 I Unity   : [MetinARPerf] v=1 scene=ArEncounterPrototype windowS=5 frames=148 fpsAvg={1} frameP50Ms=33.3 frameP95Ms={2} frameSpikes=0 mainP95Ms=30 totalUsedMB=420' -f $t, $f, $q))
    }
    return , $l.ToArray()
}
$tl = New-Timeline
$ts = Get-MetinARTimedPerfSamples -Lines $tl
Assert 'timed.samples' ($ts.Count -eq 30 -and $ts[0].ts -eq '10-20 04:30:05.000' -and ($ts[1].t - $ts[0].t) -eq 5)
$r = Test-MetinARCombatFps -Lines $tl -Target $target
Assert 'combat.pass' ($r.Measured -eq 'PASS' -and $r.Windows -eq 18 -and $r.FpsMedian -eq 29.6 -and $r.FpsP5 -eq 29.6) "m=$($r.Measured) w=$($r.Windows) $($r.Reason)"
# decision 1: median >= 29.5 (29.4 fails on the median alone, 29.5 passes)
$r294 = Test-MetinARCombatFps -Lines (New-Timeline -fps 29.4) -Target $target
Assert 'combat.median-29.4-fails' ($r294.Measured -eq 'FAIL' -and @($r294.Checks | Where-Object { $_.Status -eq 'FAIL' }).Count -eq 1) "m=$($r294.Measured)"
Assert 'combat.median-29.5-passes' ((Test-MetinARCombatFps -Lines (New-Timeline -fps 29.5) -Target $target).Measured -eq 'PASS')
# review #100 point 3: frame p50 median <= 33.9 ms (1000/29.5); 34.0 and the old 34.5 fail on p50 alone
foreach ($p50 in @('33.9', '34.0', '34.5')) {
    $rp = Test-MetinARCombatFps -Lines @($tl | ForEach-Object { $_ -replace 'frameP50Ms=33\.3', ('frameP50Ms=' + $p50) }) -Target $target
    $want = if ($p50 -eq '33.9') { 'PASS' } else { 'FAIL' }
    Assert ("combat.p50-{0}-{1}" -f $p50, $want.ToLowerInvariant()) ($rp.Measured -eq $want -and @($rp.Checks | Where-Object { $_.Status -eq 'FAIL' }).Count -eq $(if ($want -eq 'FAIL') { 1 } else { 0 })) "m=$($rp.Measured)"
}
Assert 'budgets.p50-consistent-with-fps-median' ([Math]::Abs(1000 / [double]$target.minFpsMedian - [double]$target.maxFrameP50MedianMs) -lt 0.05)
# decision 3: exactly `[Combo] step=N` (N >= 1, case-sensitive); anything else is not combo evidence
foreach ($bad in @('[Combo] step=', '[Combo] step=x', '[combo] step=1', '[Combo] stepped=1', '[Combo]step=1', '[Combo] step=0', '[COMBO] step=2')) {
    $rb = Test-MetinARCombatFps -Lines (New-Timeline -ComboText $bad) -Target $target
    Assert ("combo.format-rejects '{0}'" -f $bad) ($rb.Measured -eq 'NO DATA' -and $rb.ComboLines -eq 0) "m=$($rb.Measured) combo=$($rb.ComboLines)"
}
Assert 'combo.format-accepts-step=12' ((Test-MetinARCombatFps -Lines (New-Timeline -ComboText '[Combo] step=12') -Target $target).Windows -eq 18)
Assert 'steps.combo-pattern-exact' (('[Combo] step=3' -match $steps[9].logPattern) -and -not ('[Combo] step=30' -match $steps[9].logPattern))
# decision 2: the scene list comes from the target configuration
$tOther = ($target | ConvertTo-Json -Depth 6 | ConvertFrom-Json); $tOther.sceneNames = @('ArCombat')
$ro = Test-MetinARCombatFps -Lines $tl -Target $tOther
Assert 'scene.config-drives-selection' ($ro.Measured -eq 'NO DATA' -and $ro.SceneWindows -eq 0 -and $ro.Scenes -eq 'ArCombat')
$rl = Test-MetinARCombatFps -Lines (New-Timeline -fps 24 -p95 60) -Target $target
Assert 'combat.low-fails' ($rl.Measured -eq 'FAIL' -and @($rl.Checks | Where-Object { $_.Status -eq 'FAIL' }).Count -ge 2)
$rn = Test-MetinARCombatFps -Lines (New-Timeline -NoCombo) -Target $target
Assert 'combat.no-combo-no-data' ($rn.Measured -eq 'NO DATA' -and $rn.Windows -eq 0 -and $rn.Reason -match '#82')
$rc = Test-MetinARCombatFps -Lines (New-Timeline -NoCombat) -Target $target
Assert 'combat.no-combat-no-data' ($rc.Measured -eq 'NO DATA' -and $rc.Reason -match 'combat|combo')
$rw = Test-MetinARCombatFps -Lines $tl -Target $target -WarmupSeconds 140
Assert 'combat.warmup-excludes' ($rw.Windows -eq 2 -and $rw.Measured -eq 'FAIL') "w=$($rw.Windows) m=$($rw.Measured)"
$tl2 = @($tl | Where-Object { $_ -notmatch '04:30:(0[5-9]|[1-5]\d)\.000.*MetinARPerf' })
$rs = Test-MetinARCombatFps -Lines @($tl | ForEach-Object { $_ -replace 'scene=ArEncounterPrototype', 'scene=WorldMapPrototype' }) -Target $target
Assert 'combat.other-scene-no-data' ($rs.Measured -eq 'NO DATA' -and $rs.SceneWindows -eq 0)
Assert 'combat.subset-still-measured' ((Test-MetinARCombatFps -Lines $tl2 -Target $target).Windows -ge 1)

$m1 = Test-MetinARPssTrend -SamplesMB @(400, 404, 406) -MaxPssMB 850
$m2 = Test-MetinARPssTrend -SamplesMB @(400, 430, 460) -MaxPssMB 850
$m3 = Test-MetinARPssTrend -SamplesMB @(400, 900) -MaxPssMB 850
$m4 = Test-MetinARPssTrend -SamplesMB @(400) -MaxPssMB 850
Assert 'pss.stable-pass' ($m1.Measured -eq 'PASS' -and $m1.GrowthPercent -eq 1.5)
Assert 'pss.growth-fail' ($m2.Measured -eq 'FAIL' -and $m2.Reason -match 'growth')
Assert 'pss.peak-fail' ($m3.Measured -eq 'FAIL' -and $m3.Reason -match 'peak')
Assert 'pss.too-few-no-data' ($m4.Measured -eq 'NO DATA')

$ev = Get-MetinARStepLogEvidence -Steps @($steps) -Lines $tl
Assert 'steps.hint-A97' (($ev | Where-Object { [string]$_.id -ceq 'A97' }).logLines -eq 36)
Assert 'steps.hints' ($ev.Count -eq 23 -and ($ev | Where-Object { $_.id -eq 11 }).logLines -eq 18 -and $null -eq ($ev | Where-Object { $_.id -eq 2 }).logLines -and ($ev | Where-Object { $_.id -eq 14 }).logLines -eq 0)

# review #100 point 4: logcat buffer kept by default, lines before the launch dropped on the host by device time
Assert 'logcat.device-date' ((ConvertFrom-MetinARDeviceDate @('10-20_04:29:58')) -eq '10-20 04:29:58.000' -and $null -eq (ConvertFrom-MetinARDeviceDate @('date: bad format')) -and $null -eq (ConvertFrom-MetinARDeviceDate @()))
$since = Select-MetinARLogcatSince -Lines @('--------- beginning of main', '10-20 04:10:00.000  3999  3999 E AndroidRuntime: FATAL EXCEPTION: main', '10-20 04:29:58.000  1 1 I x: at', '10-20 04:30:01.000  1 1 I x: after') -Since '10-20 04:29:58.000'
Assert 'logcat.since-drops-older' ($since.Count -eq 3 -and ($since -join "`n") -notmatch '3999' -and $since[0] -match 'beginning')
Assert 'logcat.since-empty-keeps-all' ((Select-MetinARLogcatSince -Lines @('10-20 04:10:00.000  1 1 I x: a', 'b') -Since '').Count -eq 2)
Assert 'collector.logcat-clear-only-behind-switch' ($collectorText -match '\[switch\]\$ClearLogcat' -and ([regex]::Matches($collectorText, "logcat '-c'")).Count -eq 1 -and $collectorText -match "if \(\`$ClearLogcat\) \{\s*#[^\n]*\n\s*Adb logcat '-c'" -and $collectorText -match 'RISK: it wipes the whole device log buffer')

# ETAP I closing verdict: 22 steps AND A97 required (lead decision 04:43, no manual side entry)
function New-StepResults([string]$verdict = 'UNVERIFIED', [string]$a97Verdict = $verdict, [switch]$NoA97Video, [switch]$DropA97) {
    $o = @()
    foreach ($st in $steps) {
        $isA = ([string]$st.id -ceq 'A97')
        if ($isA -and $DropA97) { continue }
        $v = if ($isA) { $a97Verdict } else { $verdict }
        $o += [pscustomobject]@{ id = $st.id; verdict = $v; reviewer = 'QA'; videoRef = $(if ($isA -and $NoA97Video) { $null } else { 'vid-01.mp4 01:00' }) }
    }
    return , $o
}
$c0 = Get-MetinARStageClosure -StepResults (New-StepResults) -Definition $stepsDef
Assert 'closure.unverified-not-closed' ($c0.verdict -eq 'NOT CLOSED' -and -not $c0.closed -and $c0.requiredCount -eq 23 -and @($c0.open).Count -eq 23 -and $c0.a97 -eq 'UNVERIFIED')
$c1 = Get-MetinARStageClosure -StepResults (New-StepResults 'PASS' 'UNVERIFIED') -Definition $stepsDef
Assert 'closure.22-pass-without-A97-not-closed' ($c1.verdict -eq 'NOT CLOSED' -and $c1.passed -eq 22 -and (@($c1.open) -join ',') -eq 'A97') "v=$($c1.verdict) open=$(@($c1.open) -join ',')"
$c2 = Get-MetinARStageClosure -StepResults (New-StepResults 'PASS') -Definition $stepsDef
Assert 'closure.22+A97-pass-closed' ($c2.verdict -eq 'CLOSED' -and $c2.closed -and $c2.passed -eq 23 -and $c2.a97 -eq 'PASS')
$c3 = Get-MetinARStageClosure -StepResults (New-StepResults 'PASS' -NoA97Video) -Definition $stepsDef
Assert 'closure.A97-pass-needs-video' (-not $c3.closed -and (@($c3.open) -join ',') -eq 'A97' -and (@($c3.reasons) -join ';') -match 'videoRef')
$c4 = Get-MetinARStageClosure -StepResults (New-StepResults 'PASS' 'FAIL') -Definition $stepsDef
Assert 'closure.A97-fail' (-not $c4.closed -and (@($c4.failed) -join ',') -eq 'A97' -and $c4.a97 -eq 'FAIL')
$c5 = Get-MetinARStageClosure -StepResults (New-StepResults 'PASS' -DropA97) -Definition $stepsDef
Assert 'closure.A97-result-missing' (-not $c5.closed -and (@($c5.open) -join ',') -eq 'A97')
$defNoA97 = ($stepsDef | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
$defNoA97.steps = @($defNoA97.steps | Where-Object { [string]$_.id -cne 'A97' }); $defNoA97.requiredStepIds = @(1..22)
$c6 = Get-MetinARStageClosure -StepResults (New-StepResults 'PASS') -Definition $defNoA97
Assert 'closure.definition-cannot-drop-A97' ($c6.verdict -eq 'INVALID DEFINITION' -and -not $c6.closed -and (@($c6.missingDefinition) -join ',') -eq 'A97' -and $c6.requiredCount -eq 23)

# --- Part 2: collector against fake adb (Linux/macOS only) -----------------------------------------------------------
$isWin = ($PSVersionTable.PSEdition -ne 'Core') -or $IsWindows
if ($isWin) {
    Write-Host 'SKIP collector simulation (needs bash for tests/fake-adb.sh; runs in CI on ubuntu)'
} else {
    $collector = Join-Path $PSScriptRoot '..\Invoke-MetinARDeviceSession.ps1'
    $fake = Join-Path $PSScriptRoot 'fake-adb.sh'
    function RunSession([string]$mode, [int]$expected, [scriptblock]$check, [string[]]$extra = @('-SkipInstall'), [string]$label = $mode) {
        $root = Join-Path ([IO.Path]::GetTempPath()) ('metinar-session-' + $label + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
        New-Item -ItemType Directory -Force $root | Out-Null
        $env:FAKE_ADB_MODE = $mode; $env:FAKE_ADB_STATE = Join-Path $root 'state'; $env:FAKE_ADB_PACKAGE = 'com.metinar.game.qa'; $env:FAKE_ADB_LOST_AFTER = '3'
        & pwsh -NoProfile -File $collector -AdbPath $fake -SessionId "SIM-$label" -OutputRoot $root -DurationSeconds 0 -SampleIntervalSeconds 1 -LaunchWaitSeconds 0 -AdbReconnectSeconds 3 -WarmupSeconds 60 @extra *> (Join-Path $root 'console.txt')
        $code = $LASTEXITCODE
        $dir = Join-Path $root "SIM-$label"
        $ok = $false
        try { $ok = ($code -eq $expected) -and [bool](& $check $dir) } catch { Write-Host "check threw: $_" }
        Assert ("session-sim.{0} exit={1} (expected {2})" -f $label, $code, $expected) $ok $dir
        if (-not $ok) { Get-Content (Join-Path $root 'console.txt') | Write-Host }
        return $dir
    }
    function J([string]$d, [string]$f) { return (Get-Content -Raw -Encoding UTF8 (Join-Path $d $f) | ConvertFrom-Json) }
    function AllText([string]$d) { return ((Get-ChildItem -Recurse -File $d | ForEach-Object { Get-Content -Raw $_.FullName }) -join "`n") }
    function ItemOf($s, [string]$n) { return ($s.items | Where-Object { $_.name -eq $n } | Select-Object -First 1) }

    RunSession 'nodevice' 2 { param($d) $s = J $d 'session-summary.json'; $s.verdict -eq 'UNVERIFIED' -and $s.measuredOverall -eq 'NO DATA' -and (Test-Path (Join-Path $d 'session-manifest.json')) } | Out-Null
    RunSession 'session' 0 {
        param($d)
        $m = J $d 'session-manifest.json'; $s = J $d 'session-summary.json'; $all = AllText $d
        $fps = ItemOf $s 'CombatFps30'
        $md = Get-Content -Raw -Encoding UTF8 (Join-Path $d 'session-summary.md')
        $okManifest = ($m.sessionId -eq 'SIM-session') -and ($m.device.model -eq 'FakePhone') -and ($m.device.manufacturer -eq 'FakeVendor') -and ($m.device.android -eq '12') -and
            ($m.device.serialSha256 -match '^[0-9a-f]{12}$') -and ((Get-Content -Raw (Join-Path $d 'session-manifest.json')) -match '"started":\s*"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}[+-]\d\d:\d\d"') -and ($m.host.os -notmatch '<') -and ($m.qaTool.gitHead -match '^[0-9a-f]{40}$') -and
            ($m.adb.checks -ge 4) -and ($m.adb.killServer -eq 'never') -and (@($m.files | Where-Object { $_.path -eq 'logcat-filtered.txt' -and $_.sha256 -match '^[0-9a-f]{64}$' }).Count -eq 1)
        $okSummary = ($s.verdict -eq 'UNVERIFIED') -and ($s.measuredOverall -ne 'FAIL') -and (@($s.items | Where-Object { $_.verdict -ne 'UNVERIFIED' -or $_.reviewRequired -ne $true }).Count -eq 0) -and
            ($fps.measured -eq 'PASS') -and ($fps.data.Windows -eq 18) -and (@($s.steps22).Count -eq 22) -and (@($s.steps).Count -eq 23) -and (@($s.steps | Where-Object { $_.verdict -ne 'UNVERIFIED' }).Count -eq 0) -and
            ([string]$s.stepA97.id -ceq 'A97') -and ($s.stepA97.criterion -ceq $a97.criterion) -and ($s.stepA97.mandatory -eq $true) -and ($s.stepA97.logHint -match '^38 log line') -and
            ($s.stageClosure.verdict -eq 'NOT CLOSED') -and ($s.stageClosure.requiredCount -eq 23) -and (@($s.stageClosure.required) -contains 'A97') -and ($s.stageClosure.a97 -eq 'UNVERIFIED') -and
            ($md -match 'Zamkniecie ETAPU I') -and ($md -match 'nie czyszczony \(domyslnie\)') -and ($md -match '\| A97 \|') -and ($md -match 'NOT CLOSED') -and ($md.Contains([string]$a97.criterion)) -and
            ((ItemOf $s 'Crash').measured -eq 'PASS') -and ((ItemOf $s 'ColdLaunch').detail -match 'TotalTime=2345')
        $filtered = Get-Content -Raw (Join-Path $d 'logcat-filtered.txt')
        $okLogcat = ($m.settings.clearLogcat -eq $false) -and ($m.logcat.cleared -eq $false) -and ($m.logcat.sinceDeviceTime -eq '10-20 04:29:58.000') -and ($m.logcat.droppedOlderLines -eq 2) -and
            -not (Test-Path (Join-Path (Split-Path $d) 'state.cleared')) -and ((Get-Content -Raw (Join-Path $d 'logcat-full-redacted.txt')) -notmatch 'PID: 3999')
        $okLog = $okLogcat -and ([regex]::Matches($filtered, 'scene=ArEncounterPrototype').Count -eq 30) -and ($filtered -match '\[Combo\] step=') -and (Test-Path (Join-Path $d 'logcat-full-redacted.txt')) -and (@(Get-Content (Join-Path $d 'samples.jsonl')).Count -eq 1)
        $okPrivacy = ($all -notmatch 'FAKESERIAL123') -and ($all -notmatch '52\.2297') -and ($all -notmatch '21\.0117')
        if (-not ($okManifest -and $okSummary -and $okLog -and $okPrivacy)) { Write-Host "manifest=$okManifest summary=$okSummary log=$okLog privacy=$okPrivacy fps=$($fps.measured)/$($fps.detail)" }
        $okManifest -and $okSummary -and $okLog -and $okPrivacy
    } | Out-Null
    # -ClearLogcat: `adb logcat -c` is sent before the launch (fake records it), no time filter, the earlier crash is gone
    RunSession 'session' 0 {
        param($d)
        $m = J $d 'session-manifest.json'; $s = J $d 'session-summary.json'
        $md = Get-Content -Raw -Encoding UTF8 (Join-Path $d 'session-summary.md')
        ($m.settings.clearLogcat -eq $true) -and ($m.logcat.cleared -eq $true) -and ($null -eq $m.logcat.sinceDeviceTime) -and ($m.logcat.droppedOlderLines -eq 0) -and
            (Test-Path (Join-Path (Split-Path $d) 'state.cleared')) -and ((ItemOf $s 'Crash').measured -eq 'PASS') -and ($md -match 'wyczyszczony \(-ClearLogcat\)') -and
            ((Get-Content -Raw (Join-Path $d 'logcat-full-redacted.txt')) -notmatch 'PID: 3999')
    } @('-SkipInstall', '-ClearLogcat') 'session-clearlogcat' | Out-Null
    RunSession 'sessionlow' 1 { param($d) $s = J $d 'session-summary.json'; ((ItemOf $s 'CombatFps30').measured -eq 'FAIL') -and $s.verdict -eq 'UNVERIFIED' -and $s.measuredOverall -eq 'FAIL' } | Out-Null
    RunSession 'sessionnocombo' 0 { param($d) $s = J $d 'session-summary.json'; $f = ItemOf $s 'CombatFps30'; ($f.measured -eq 'NO DATA') -and ($f.detail -match '#82') -and $s.measuredOverall -eq 'NO DATA' } | Out-Null
    RunSession 'adblost' 3 { param($d) $m = J $d 'session-manifest.json'; $s = J $d 'session-summary.json'; ($m.adb.lost -match "before 'Sample 1'") -and $s.verdict -eq 'UNVERIFIED' -and $s.measuredOverall -eq 'FAIL' -and ((AllText $d) -notmatch 'FAKESERIAL123') } | Out-Null
    RunSession 'session' 0 { param($d) $m = J $d 'session-manifest.json'; $v = @($m.videos); ($v.Count -eq 1) -and $v[0].pulled -and ($v[0].sha256 -match '^[0-9a-f]{64}$') -and (Test-Path (Join-Path $d 'video/metinar-SIM-video-01.mp4')) -and ((AllText $d) -match 'deletes nothing') } @('-SkipInstall', '-ScreenRecordSegments', '1', '-ScreenRecordSeconds', '5') 'video' | Out-Null
    # decision 6: manual-removal list (device path, size, SHA-256 of the pulled copy, device-side SHA-256 check)
    RunSession 'session' 0 {
        param($d)
        $s = J $d 'session-summary.json'; $m = J $d 'session-manifest.json'; $c = @($s.deviceCleanup)
        $local = Join-Path $d 'video/metinar-SIM-cleanup-01.mp4'; $md = Get-Content -Raw -Encoding UTF8 (Join-Path $d 'session-summary.md')
        $ok = ($c.Count -eq 1) -and ($c[0].devicePath -eq '/sdcard/metinar-SIM-cleanup-01.mp4') -and ($c[0].bytes -eq 8) -and ($c[0].sha256 -eq (Get-FileHash -Algorithm SHA256 $local).Hash.ToLowerInvariant()) -and
            ($c[0].deviceSha256 -eq $c[0].sha256) -and ($c[0].deviceBytes -eq 8) -and ($c[0].copyVerified -eq $true) -and ($c[0].action -match 'by hand') -and (@($m.deviceCleanup).Count -eq 1) -and
            ($md -match 'Pliki do recznego usuniecia') -and ($md -match '/sdcard/metinar-SIM-cleanup-01\.mp4') -and ($md -match $c[0].sha256)
        if (-not $ok) { Write-Host ($c | ConvertTo-Json -Depth 4) }
        $ok
    } @('-SkipInstall', '-ScreenRecordSegments', '1', '-ScreenRecordSeconds', '5') 'cleanup' | Out-Null
    $env:FAKE_ADB_VIDEO_SHA_BAD = '1'
    RunSession 'session' 0 { param($d) $c = @((J $d 'session-summary.json').deviceCleanup); ($c.Count -eq 1) -and ($c[0].copyVerified -eq $false) -and ($c[0].action -match 'DO NOT DELETE') } @('-SkipInstall', '-ScreenRecordSegments', '1', '-ScreenRecordSeconds', '5') 'cleanupbad' | Out-Null
    Remove-Item Env:\FAKE_ADB_VIDEO_SHA_BAD
    # decision 2: -CombatScene overrides the configured scene list
    RunSession 'session' 0 { param($d) $m = J $d 'session-manifest.json'; $f = ItemOf (J $d 'session-summary.json') 'CombatFps30'; ((@($m.settings.combatScenes) -join ',') -eq 'ArCombat') -and ($f.measured -eq 'NO DATA') -and ($f.detail -match 'ArCombat') } @('-SkipInstall', '-CombatScene', 'ArCombat') 'scene' | Out-Null
    # decision 4: explicit -Serial - only the 12-hex hash is written, the raw serial nowhere
    RunSession 'session' 0 { param($d) $m = J $d 'session-manifest.json'; ($m.device.serialSha256 -ceq '3b9bbb471cfd') -and ((AllText $d) -notmatch 'FAKESERIAL123') -and ((@($m.settings.combatScenes) -join ',') -eq 'ArEncounterPrototype') } @('-SkipInstall', '-Serial', 'FAKESERIAL123') 'serial' | Out-Null

    # an existing non-empty session folder is never overwritten
    $root = Join-Path ([IO.Path]::GetTempPath()) ('metinar-session-exists-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
    New-Item -ItemType Directory -Force (Join-Path $root 'SIM-exists') | Out-Null
    Set-Content -Path (Join-Path $root 'SIM-exists/keep.txt') -Value 'keep'
    $env:FAKE_ADB_MODE = 'session'; $env:FAKE_ADB_STATE = Join-Path $root 'state'
    & pwsh -NoProfile -File $collector -AdbPath $fake -SessionId 'SIM-exists' -OutputRoot $root -DurationSeconds 0 -SkipInstall *> (Join-Path $root 'console.txt')
    Assert 'session-sim.existing-folder-refused' ($LASTEXITCODE -eq 5 -and @(Get-ChildItem (Join-Path $root 'SIM-exists')).Count -eq 1)

    # install path with the same guards as the smoke harness (synthetic APK named like a build: commit from the name)
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $apkDir = Join-Path ([IO.Path]::GetTempPath()) ('metinar-session-apk-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
    New-Item -ItemType Directory -Force $apkDir | Out-Null
    $apk = Join-Path $apkDir 'MetinAR-v0c9-abc1234-dev.apk'
    $fs = [IO.File]::Open($apk, [IO.FileMode]::Create)
    $za = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    $files = [ordered]@{ 'assets/bin/Data/globalgamemanagers' = 'x'; 'assets/bin/Data/data.unity3d' = 'x'; 'lib/arm64-v8a/libil2cpp.so' = 'x'; 'lib/arm64-v8a/libUnityARCore.so' = 'x'
        'assets/bin/Data/UnitySubsystems/UnityARCore/UnitySubsystemsManifest.json' = '{}'; 'assets/bin/Data/RuntimeInitializeOnLoads.json' = (Get-Content -Raw (Join-Path $PSScriptRoot 'fixtures/runtime-init-synthetic-arcore-ok.json')) }
    foreach ($n in $files.Keys) { $e = $za.CreateEntry($n); $w = New-Object IO.StreamWriter($e.Open()); $w.Write([string]$files[$n]); $w.Dispose() }
    $za.Dispose(); $fs.Dispose()
    $sha = (Get-FileHash -Algorithm SHA256 $apk).Hash.ToLowerInvariant()
    RunSession 'session' 0 { param($d) $m = J $d 'session-manifest.json'; ($m.apk.sha256 -eq $sha) -and ($m.apk.name -eq 'MetinAR-v0c9-abc1234-dev.apk') -and ($m.gameCommit.sha -eq 'abc1234') -and ($m.gameCommit.source -eq 'APK file name') -and ($m.install.status -eq 'OK') } @('-ApkPath', $apk) 'install' | Out-Null
}

Write-Host ''
Write-Host "Device session tests: $script:passed passed, $script:failed failed"
if ($script:failed -gt 0) { exit 1 } else { exit 0 }
