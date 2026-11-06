<#
.SYNOPSIS
  MetinAR device session evidence collector for the A31 acceptance protocol (METINAR-045, #90).

.DESCRIPTION
  One command per session: takes an APK and a session id and writes everything into one session folder
  (default Builds/qa/sessions/<SessionId>/, git-ignored; an existing non-empty folder is refused, nothing is overwritten):

    session-manifest.json   build identity (APK name/bytes/SHA-256/package/versionName, game commit and its source),
                            QA tool identity (git HEAD of this checkout + dirty flag - kept separate from the game commit),
                            device (model, manufacturer, Android/API/ABI, serialSha256 - the serial itself is never written),
                            host, ISO timestamps with offset, settings, adb health checks and gaps, list of files
    samples.jsonl           one JSON line per sample: pid, TOTAL PSS, gfxinfo framestats summary, SurfaceFlinger latency,
                            thermal status, battery level/temperature, new logcat lines
    logcat-full-redacted.txt   every collected logcat line (main,system,crash,events), redacted
    logcat-filtered.txt     app pid/package + AndroidRuntime/DEBUG/libc/ActivityManager/Unity/[MetinARPerf]/[ArDamage]/
                            [ArEncounter]/[ArAnchor]/[Combo]/[WorldMapPrototype], redacted
    video/*.mp4             optional `screenrecord` segments (-ScreenRecordSegments), pulled at the end with SHA-256;
                            camera feed - stays local, never upload to GitHub
    session-summary.json/.md   measurements against tools/qa/perf-budgets.json (productTargets.<TargetName> for the
                            combat FPS, scenes{} prototype gates as regression) and the 22 steps of owner spec section 18
                            plus the mandatory step A97 (#97) from tools/qa/etap1-steps.json with log hints, and
                            stageClosure: ETAP I closes only with PASS of all 22 steps AND A97 (Get-MetinARStageClosure).

  VERDICT RULE (#90): every item has `measured` (PASS / FAIL / WARN / NO DATA) from the tool and `verdict` =
  UNVERIFIED with reviewRequired=true. Only a human who has watched the video of the same time range may change a
  verdict to PASS/FAIL (fill `reviewer`). The overall verdict of the session is always UNVERIFIED when written.

  adb: `adb devices` is checked before every step and every sample (a short drop is waited out for
  -AdbReconnectSeconds and recorded as a gap). adb is never restarted: no kill-server, no start-server, no reconnect.
  On LuzMen this needs an ACQUIRED slot in #33; starting or closing Unity kills the adb server.
  Install uses the same guards as Invoke-MetinARDeviceSmoke.ps1 (protected packages, player data, ARCore
  registrations), never -g (permissions are not auto-granted). Nothing is deleted on the device: screenrecord files
  stay in /sdcard and the report lists them (device path, size, SHA-256 of the pulled copy, device-side SHA-256 check)
  for manual removal by the owner or the A31 slot holder after SHA verification (#90 decision 6).
  The combat scene comes from perf-budgets.json productTargets.<TargetName>.sceneNames (default ArEncounterPrototype)
  or -CombatScene; it is never hardcoded (#90 decision 2). Combo lines must be exactly `[Combo] step=N` (decision 3).
  The device serial is written only as serialSha256 = first 12 hex of SHA-256 (decision 4).
  Raw logcat is never written to disk; only the redacted copies.

  LOGCAT BUFFER (review #100 point 4): by default the device log buffer is NOT cleared. Before the launch the collector
  reads the device clock (`adb shell date`) and keeps only lines stamped from that moment (Select-MetinARLogcatSince);
  older lines are dropped on the host and counted in manifest.logcat.droppedOlderLines. If the device clock cannot be
  read, nothing is filtered and the report says so (earlier runs may then show up in the crash check).
  -ClearLogcat runs `adb logcat -c` before the launch, as before. RISK: it wipes the whole device log buffer
  (main/system/crash/events of ALL apps), so crash traces or logs of an earlier run that nobody has copied yet are
  lost for good. Use it only on the A31 slot when nothing earlier on the phone still needs its log.

  Exit codes: 0 = no measured FAIL (verdict still UNVERIFIED), 1 = at least one measured FAIL,
              2 = no authorized device (nothing collected), 3 = adb connection lost (partial session written),
              4 = logcat collection error (empty/failed dump or no line of the app).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARDeviceSession.ps1 -ApkPath Builds\MetinAR-v0c2-ee48cb9-dev.apk -SessionId A31-2026-10-24-01 -DurationSeconds 900 -ScreenRecordSegments 5
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARDeviceSession.ps1 -SkipInstall -SessionId A31-retest-02 -GameCommit ee48cb9
#>
[CmdletBinding()]
param(
    [string]$ApkPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')][string]$SessionId,
    [string]$Package,
    [string]$Serial,
    [string]$AdbPath,
    [string]$UnityPath,
    [string]$OutputRoot,
    [string]$GameCommit,
    [ValidateRange(0, 14400)][int]$DurationSeconds = 600,
    [ValidateRange(1, 600)][int]$SampleIntervalSeconds = 30,
    [ValidateRange(0, 600)][int]$WarmupSeconds = 60,
    [ValidateRange(0, 120)][int]$LaunchWaitSeconds = 10,
    [ValidateRange(0, 20)][int]$ScreenRecordSegments = 0,
    [ValidateRange(5, 180)][int]$ScreenRecordSeconds = 170,
    [int]$AdbReconnectSeconds = 20,
    [string]$TargetName = 'ArCombatA31',
    [string[]]$CombatScene,
    [string]$CombatPattern = '\[ArDamage\] swing=|\[Combo\] step=[1-9][0-9]*(?=\s|$)',
    [string]$ComboPattern = '\[Combo\] step=[1-9][0-9]*(?=\s|$)',
    [switch]$SkipInstall,
    [switch]$SkipLaunch,
    [switch]$AllowReplaceInstalled,
    [switch]$ClearLogcat
)
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot 'MetinARQa.psm1') -Force -DisableNameChecking
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$repo = Get-MetinARRepoRoot -Start $PSScriptRoot
$config = Read-MetinARJson -Path (Join-Path $repo 'tools/qa/qa-config.json')
$budgets = Read-MetinARJson -Path (Join-Path $repo $config.budgetsFile)
$stepsDef = (Get-Content -Raw -Encoding UTF8 -Path (Join-Path $PSScriptRoot 'etap1-steps.json')) | ConvertFrom-Json
if (-not $Package) { $Package = $config.androidBuild.expectedPackage }
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'Builds/qa/sessions' }
$sessionDir = Join-Path $OutputRoot $SessionId
if ((Test-Path $sessionDir) -and @(Get-ChildItem -Force -Path $sessionDir).Count -gt 0) {
    Write-Error "Session folder already exists and is not empty: $sessionDir - choose a new -SessionId (nothing is overwritten)."
    exit 5
}
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
if (-not $AdbPath) {
    if (-not $UnityPath) { $UnityPath = Find-MetinARUnity -Version $config.unityVersion }
    $AdbPath = Find-MetinARAndroidTool -Name adb -UnityPath $UnityPath
}
if (-not $AdbPath) { throw 'adb not found (pass -AdbPath, set ANDROID_HOME, or install the Unity Android module).' }

function Get-IsoNow { return (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffzzz') }
$secrets = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]
$stepLog = New-Object System.Collections.Generic.List[object]
$adbGaps = New-Object System.Collections.Generic.List[string]
$sampleRows = New-Object System.Collections.Generic.List[object]
$collector = New-MetinARLogcatCollector
$pidsSeen = New-Object System.Collections.Generic.List[int]
$videos = New-Object System.Collections.Generic.List[object]
$script:adbChecks = 0
$script:adbLost = $null
$script:logcatErrors = 0
$script:lastLogcatExit = $null
$script:launch = [ordered]@{ component = $null; totalTimeMs = $null; ok = $null; pid = $null }
$script:install = [ordered]@{ status = 'NOT RUN'; detail = '' }
$startedAt = Get-IsoNow

function Note([string]$Text) { $notes.Add($Text); Write-Host "[note] $Text" -ForegroundColor Yellow }
function Step([string]$Name, [string]$Status, [string]$Detail) {
    $stepLog.Add([pscustomobject]@{ name = $Name; at = (Get-IsoNow); status = $Status; detail = $Detail })
    Write-Host ("[{0,-10}] {1,-12} {2}" -f $Status, $Name, (ConvertTo-MetinARRedactedText -Lines @($Detail) -Secrets $secrets)[0])
}
function Adb {
    $a = @(); if ($Serial) { $a += @('-s', $Serial) }
    $out = & $AdbPath @($a + $args) 2>&1 | ForEach-Object { "$_" }
    $script:AdbExit = $LASTEXITCODE
    return @($out)
}
function Assert-AdbReady([string]$Name) {
    # `adb devices` before every step; never kill-server / start-server / reconnect.
    $script:adbChecks++
    $deadline = (Get-Date).AddSeconds([math]::Max(0, $AdbReconnectSeconds))
    $lostAt = $null; $lostState = $null
    while ($true) {
        $st = ConvertFrom-MetinARAdbDevices -Lines @(& $AdbPath devices 2>&1 | ForEach-Object { "$_" }) -Serial $Serial
        if ($st.State -eq 'device') {
            if ($lostAt) {
                $gap = ("adb gap before '{0}': state={1} from {2:HH:mm:ss} to {3:HH:mm:ss} ({4:N0} s); logcat lines and samples in this window may be missing" -f $Name, $lostState, $lostAt, (Get-Date), ((Get-Date) - $lostAt).TotalSeconds)
                $adbGaps.Add($gap); Note $gap
            }
            return
        }
        if (-not $lostAt) { $lostAt = Get-Date; $lostState = $st.State; Write-Host ("[adb] device state '{0}' before '{1}' - waiting up to {2} s (adb is not restarted)" -f $st.State, $Name, $AdbReconnectSeconds) -ForegroundColor Yellow }
        if ((Get-Date) -ge $deadline) {
            $script:adbLost = ("adb connection lost before '{0}': state={1} since {2:HH:mm:ss}; not restarted (no kill-server). Check the cable / whether Unity was started or closed (#33)." -f $Name, $lostState, $lostAt)
            $adbGaps.Add($script:adbLost)
            throw [System.OperationCanceledException]::new($script:adbLost)
        }
        Start-Sleep -Seconds 2
    }
}
function Get-AppPid {
    $p = ((Adb shell pidof $Package) | Select-Object -First 1)
    if ($p -and "$p".Trim() -match '^(\d+)') { $n = [int]$Matches[1]; if (-not $pidsSeen.Contains($n)) { $pidsSeen.Add($n) }; return $n }
    return $null
}
function Get-LogcatChunk {
    $script:AdbExit = $null
    $lines = @(Adb logcat '-d' '-v' threadtime '-b' 'main,system,crash,events')
    $script:lastLogcatExit = $script:AdbExit
    if ($script:AdbExit -ne 0) { $script:logcatErrors++; Note ("logcat dump failed (exit {0}) - lines of this interval may be missing" -f $script:AdbExit); return 0 }
    if ($manifest.logcat.sinceDeviceTime) {
        $kept = Select-MetinARLogcatSince -Lines $lines -Since $manifest.logcat.sinceDeviceTime
        $manifest.logcat.droppedOlderLines = [Math]::Max([int]$manifest.logcat.droppedOlderLines, $lines.Count - $kept.Count)
        $lines = $kept
    }
    return (Add-MetinARLogcatChunk -Collector $collector -Lines $lines)
}
function Get-FileSha256([string]$Path) { return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant() }

# --- screenrecord (optional, local adb client process; device files are not deleted) -------------------------------
$script:rec = $null
$script:recIndex = 0
function Start-NextRecording {
    if ($script:recIndex -ge $ScreenRecordSegments) { return }
    $script:recIndex++
    $remote = ('/sdcard/metinar-{0}-{1:D2}.mp4' -f $SessionId, $script:recIndex)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $AdbPath
    $sArg = if ($Serial) { '-s "' + $Serial + '" ' } else { '' }
    $psi.Arguments = $sArg + 'shell screenrecord --time-limit ' + $ScreenRecordSeconds + ' ' + $remote
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $script:rec = [pscustomobject]@{ Proc = $proc; Remote = $remote; Index = $script:recIndex; StartedAt = (Get-IsoNow); Started = (Get-Date) }
    $videos.Add([pscustomobject]@{ index = $script:recIndex; remote = $remote; startedAt = $script:rec.StartedAt; endedAt = $null; file = $null; bytes = $null; sha256 = $null; pulled = $false
            deviceBytes = $null; deviceSha256 = $null; copyVerified = $false; note = '' })
    Write-Host ("[video] segment {0} started ({1} s max)" -f $script:recIndex, $ScreenRecordSeconds)
}
function Complete-Recording([switch]$Wait) {
    if (-not $script:rec) { return }
    $p = $script:rec.Proc
    if ($Wait -and -not $p.HasExited) {
        $left = $ScreenRecordSeconds + 15 - ((Get-Date) - $script:rec.Started).TotalSeconds
        if ($left -gt 0) { [void]$p.WaitForExit([int]($left * 1000)) }
    }
    if (-not $p.HasExited) { return }
    $v = $videos[$videos.Count - 1]
    $v.endedAt = Get-IsoNow
    $script:rec = $null
}
function Stop-RecordingAtEnd {
    if (-not $script:rec) { return }
    # Let the segment reach its --time-limit so the mp4 is finalized; only the local adb client is stopped as last resort.
    Complete-Recording -Wait
    if ($script:rec) {
        try { $script:rec.Proc.Kill() } catch { Write-Verbose 'screenrecord client already exited' }
        $v = $videos[$videos.Count - 1]; $v.endedAt = Get-IsoNow; $v.note = 'local adb client stopped after time limit + 15 s; the file may be truncated'
        $script:rec = $null
    }
}

$manifest = [ordered]@{
    schema = 1; kind = 'MetinAR device session (#90 / METINAR-045)'; sessionId = $SessionId
    verdictPolicy = 'All device results are UNVERIFIED until a human has reviewed the video of the same time range. Never PASS from simulation or unit tests (owner spec section 15).'
    apk = [ordered]@{ name = $null; bytes = $null; sha256 = $null; package = $Package; versionName = $null; versionCode = $null }
    gameCommit = [ordered]@{ sha = $null; source = $null }
    qaTool = [ordered]@{ script = 'tools/qa/Invoke-MetinARDeviceSession.ps1'; gitHead = $null; dirty = $null; note = 'QA tool commit (this checkout) - not the game commit' }
    device = [ordered]@{ serialSha256 = $null; model = $null; manufacturer = $null; android = $null; sdk = $null; abi = $null }
    host = [ordered]@{ os = ('{0} {1}' -f [System.Environment]::OSVersion.Platform, [System.Environment]::OSVersion.Version.ToString(2)); powershell = $PSVersionTable.PSVersion.ToString() }
    timestamps = [ordered]@{ started = $startedAt; launched = $null; samplingStarted = $null; samplingEnded = $null; finished = $null }
    settings = [ordered]@{ durationSeconds = $DurationSeconds; sampleIntervalSeconds = $SampleIntervalSeconds; warmupSeconds = $WarmupSeconds; launchWaitSeconds = $LaunchWaitSeconds
        screenRecordSegments = $ScreenRecordSegments; screenRecordSeconds = $ScreenRecordSeconds; adbReconnectSeconds = $AdbReconnectSeconds; target = $TargetName
        combatPattern = $CombatPattern; comboPattern = $ComboPattern; skipInstall = [bool]$SkipInstall; skipLaunch = [bool]$SkipLaunch; clearLogcat = [bool]$ClearLogcat }
    logcat = [ordered]@{ cleared = $false; sinceDeviceTime = $null; droppedOlderLines = 0; note = $null }
    adb = [ordered]@{ checks = 0; gaps = @(); lost = $null; killServer = 'never' }
    install = $null; launch = $null; steps = @(); videos = @(); deviceCleanup = @(); files = @(); notes = @()
}

# --- build identity (before touching the device) ---------------------------------------------------------------
if ($ApkPath) {
    if (Test-Path $ApkPath) {
        $fi = Get-Item $ApkPath
        $manifest.apk.name = $fi.Name; $manifest.apk.bytes = $fi.Length; $manifest.apk.sha256 = Get-FileSha256 $fi.FullName
        $aapt = Find-MetinARAndroidTool -Name aapt -UnityPath $UnityPath
        if ($aapt) {
            $b = ConvertFrom-MetinARAaptBadging -Lines @(& $aapt dump badging $fi.FullName 2>&1 | ForEach-Object { "$_" })
            if ($b.Package) { $Package = $b.Package; $manifest.apk.package = $b.Package; $manifest.apk.versionName = $b.VersionName; $manifest.apk.versionCode = $b.VersionCode; $script:activity = $b.LaunchableActivity }
        } else { Note 'aapt not found: package/versionName not read from the APK (package from qa-config)' }
    } else { Note "APK not found: $ApkPath" }
}
if ($GameCommit) { $manifest.gameCommit.sha = $GameCommit; $manifest.gameCommit.source = '-GameCommit' }
elseif ($ApkPath -and ($c = Get-MetinARApkCommitFromName -Name $ApkPath)) { $manifest.gameCommit.sha = $c; $manifest.gameCommit.source = 'APK file name' }
else { $manifest.gameCommit.source = 'unknown - pass -GameCommit (40-hex SHA of the build commit)'; Note 'game commit unknown: pass -GameCommit' }
$git = Find-MetinARGit
if ($git) {
    $manifest.qaTool.gitHead = ((& $git -C $repo rev-parse HEAD 2>$null) | Select-Object -First 1)
    $manifest.qaTool.dirty = (@(& $git -C $repo status --porcelain -- tools/qa 2>$null).Count -gt 0)
}

function Write-Json($Object, [string]$Name) {
    $json = $Object | ConvertTo-Json -Depth 8
    $json = (ConvertTo-MetinARRedactedText -Lines ($json -split "`n") -Secrets $secrets -KeepHex) -join "`n"
    Set-Content -Path (Join-Path $sessionDir $Name) -Value $json -Encoding UTF8
}

$exitCode = 0
$deviceOk = $false
try {
    # --- Device ------------------------------------------------------------------------------------------------
    $devLines = @(& $AdbPath devices 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match '^\S+\s+(device|unauthorized|offline)$' })
    foreach ($l in $devLines) { $secrets.Add(($l -split '\s+')[0]) }
    if ($Serial) { $secrets.Add($Serial); $devLines = @($devLines | Where-Object { $_ -like "$Serial*" }) }
    $ready = @($devLines | Where-Object { $_ -match '\sdevice$' })
    if ($ready.Count -ne 1) {
        $why = if ($ready.Count -gt 1) { "$($ready.Count) devices connected; pass -Serial" } elseif (@($devLines | Where-Object { $_ -match 'unauthorized' }).Count) { 'device connected but USB debugging not authorized' } else { 'no device connected (adb devices empty)' }
        Step 'Device' 'UNVERIFIED' $why
        Note 'Nothing was collected on a physical device in this run.'
        $exitCode = 2
    } else {
        $deviceOk = $true
        if (-not $Serial) { $Serial = ($ready[0] -split '\s+')[0]; $secrets.Add($Serial) }
        $manifest.device.serialSha256 = Get-MetinARSerialHash -Serial $Serial
        $manifest.device.model = "$((Adb shell getprop ro.product.model) | Select-Object -First 1)".Trim()
        $manifest.device.manufacturer = "$((Adb shell getprop ro.product.manufacturer) | Select-Object -First 1)".Trim()
        $manifest.device.android = "$((Adb shell getprop ro.build.version.release) | Select-Object -First 1)".Trim()
        $manifest.device.sdk = "$((Adb shell getprop ro.build.version.sdk) | Select-Object -First 1)".Trim()
        $manifest.device.abi = "$((Adb shell getprop ro.product.cpu.abi) | Select-Object -First 1)".Trim()
        Step 'Device' 'OK' ("{0} {1}, Android {2} (API {3}), {4}" -f $manifest.device.manufacturer, $manifest.device.model, $manifest.device.android, $manifest.device.sdk, $manifest.device.abi)
    }

    if ($deviceOk) {
        # --- Install (same guards as Invoke-MetinARDeviceSmoke.ps1) ------------------------------------------------
        Assert-AdbReady 'Install'
        if ($SkipInstall) { $script:install.status = 'SKIP'; $script:install.detail = 'using the installed build (-SkipInstall); build identity of the installed app is not proven by this manifest' }
        elseif (-not $ApkPath -or -not (Test-Path $ApkPath)) { $script:install.status = 'FAIL'; $script:install.detail = 'no -ApkPath (or use -SkipInstall)' }
        else {
            $guard = Test-MetinARInstallGuard -Package $Package -ProtectedPackages @($config.androidBuild.protectedPackages) `
                -InstalledPackages @((Adb shell pm list packages $Package) | ForEach-Object { "$_".Trim() -replace '^package:', '' }) -AllowReplace:$AllowReplaceInstalled
            $entries = @(Get-MetinARZipEntries -Path $ApkPath)
            $pd = Test-MetinARApkPlayerData -Entries $entries
            $arc = Test-MetinARApkArCore -Entries $entries -Rule $config.androidBuild.arCore -RuntimeInitializeJson (Get-MetinARZipEntryText -Path $ApkPath -Entry $config.androidBuild.arCore.runtimeInitializeEntry)
            if ($guard.Status -eq 'FAIL') { $script:install.status = 'FAIL'; $script:install.detail = 'not installed: ' + $guard.Reason }
            elseif (@($pd.Problems).Count) { $script:install.status = 'FAIL'; $script:install.detail = 'APK rejected before install: inconsistent player data (stale .split chunks)' }
            elseif ($arc.Status -eq 'FAIL') { $script:install.status = 'FAIL'; $script:install.detail = ("APK rejected before install: ARCore registrations {0}/{1}" -f $arc.Found, $arc.Required) }
            else {
                if ($guard.Reason) { Note $guard.Reason }
                $out = Adb install '-r' '-t' $ApkPath
                $ok = ($out -join ' ') -match 'Success'
                $script:install.status = $(if ($ok) { 'OK' } else { 'FAIL' }); $script:install.detail = (($out | Select-Object -Last 1) + ' (runtime permissions not auto-granted)')
            }
        }
        Step 'Install' $script:install.status $script:install.detail
        if ($script:install.status -eq 'FAIL') { throw [System.InvalidOperationException]::new('install failed') }

        # --- Launch ------------------------------------------------------------------------------------------------
        Assert-AdbReady 'Launch'
        $installed = @(Adb shell pm list packages $Package) -contains "package:$Package"
        if (-not $installed) { Step 'Launch' 'FAIL' "$Package is not installed"; throw [System.InvalidOperationException]::new('not installed') }
        $act = $script:activity
        if (-not $act) {
            $resolved = @(Adb shell cmd package resolve-activity '--brief' $Package) | Where-Object { $_ -match '/' } | Select-Object -Last 1
            if ($resolved) { $act = ($resolved.Trim() -split '/', 2)[1] }
        }
        $component = if ($act) { if ($act.StartsWith('.')) { "$Package/$Package$act" } elseif ($act.Contains('/')) { $act } else { "$Package/$act" } } else { $null }
        $script:launch.component = $component
        if ($SkipLaunch) {
            Step 'Launch' 'SKIP' 'app not restarted (-SkipLaunch); logcat is not cleared, earlier lines may be included'
            $manifest.logcat.note = '-SkipLaunch: buffer not cleared and not filtered; earlier lines may be included'
        } else {
            Adb shell am force-stop $Package | Out-Null
            if ($ClearLogcat) {
                # -ClearLogcat only: wipes the whole device log buffer (all apps) - see LOGCAT BUFFER in the help
                Adb logcat '-c' | Out-Null
                $manifest.logcat.cleared = $true
                $manifest.logcat.note = '-ClearLogcat: device log buffer wiped with adb logcat -c before the launch'
            } else {
                $manifest.logcat.sinceDeviceTime = ConvertFrom-MetinARDeviceDate -Lines @(Adb shell date '+%m-%d_%H:%M:%S')
                $manifest.logcat.note = if ($manifest.logcat.sinceDeviceTime) { 'buffer not cleared (default); lines before the launch dropped on the host' } else { 'buffer not cleared and device clock unreadable: earlier lines may be included' }
                if (-not $manifest.logcat.sinceDeviceTime) { Note 'device clock not read: logcat lines from before the launch may be included (use -ClearLogcat only if the device log may be wiped)' }
            }
            $manifest.timestamps.launched = Get-IsoNow
            $launchOut = if ($component) { Adb shell am start '-W' '-n' $component } else { Adb shell monkey '-p' $Package '-c' android.intent.category.LAUNCHER 1 }
            foreach ($l in $launchOut) { if ($l -match 'TotalTime:\s*(\d+)') { $script:launch.totalTimeMs = [int]$Matches[1] } }
            $script:launch.ok = -not (($launchOut -join ' ') -match 'Error|Exception')
            Step 'Launch' $(if ($script:launch.ok) { 'OK' } else { 'FAIL' }) ("component={0} TotalTime={1} ms" -f $component, $script:launch.totalTimeMs)
        }
        Start-Sleep -Seconds $LaunchWaitSeconds
        $script:launch.pid = Get-AppPid

        # --- Sampling loop -----------------------------------------------------------------------------------------
        if ($ScreenRecordSegments -gt 0) { Assert-AdbReady 'ScreenRecord'; Start-NextRecording }
        $manifest.timestamps.samplingStarted = Get-IsoNow
        $t0 = Get-Date
        $n = 0
        $layer = $null
        do {
            $n++
            $tick = Get-Date
            Assert-AdbReady ("Sample {0}" -f $n)
            $appPid = Get-AppPid
            $mem = ConvertFrom-MetinARMeminfo -Lines (Adb shell dumpsys meminfo $Package)
            $gfx = ConvertFrom-MetinARGfxInfo -Lines (Adb shell dumpsys gfxinfo $Package framestats)
            Adb shell dumpsys gfxinfo $Package reset | Out-Null
            if (-not $layer) { $layer = (Adb shell dumpsys SurfaceFlinger '--list') | Where-Object { $_ -match [regex]::Escape($Package) -and $_ -match 'SurfaceView' -and $_ -notmatch 'Background' } | Select-Object -First 1 }
            $sf = $null
            if ($layer) { $sf = ConvertFrom-MetinARSurfaceFlingerLatency -Lines (Adb shell dumpsys SurfaceFlinger '--latency' ("'" + $layer.Trim() + "'")) }
            $th = ConvertFrom-MetinARThermal -ThermalLines (Adb shell dumpsys thermalservice) -BatteryLines (Adb shell dumpsys battery)
            $newLines = Get-LogcatChunk
            $row = [ordered]@{ n = $n; at = (Get-IsoNow); elapsedS = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1); pidAlive = [bool]$appPid
                pssMB = $(if ($mem.TotalPssKB) { [math]::Round($mem.TotalPssKB / 1024, 1) } else { $null })
                gfx = $gfx
                surfaceFlinger = $(if ($sf -and $sf.Frames -gt 0) { [ordered]@{ frames = $sf.Frames; fpsAvg = $sf.FpsAvg; frameAvgMs = $sf.FrameAvgMs; frameMaxMs = $sf.FrameMaxMs; janks = $sf.Janks; refreshMs = $sf.RefreshMs } } else { $null })
                thermalStatus = $th.ThermalStatus; batteryLevel = $th.BatteryLevel; batteryTempC = $th.BatteryTempC; newLogcatLines = $newLines }
            $sampleRows.Add([pscustomobject]$row)
            Add-Content -Path (Join-Path $sessionDir 'samples.jsonl') -Value (([pscustomobject]$row | ConvertTo-Json -Depth 5 -Compress)) -Encoding UTF8
            Write-Host ("[sample {0}] t={1}s pid={2} pss={3}MB thermal={4} batt={5}C logcat+{6}" -f $n, $row.elapsedS, [bool]$appPid, $row.pssMB, $th.ThermalStatus, $th.BatteryTempC, $newLines)
            if (-not $appPid) { Note ("sample {0}: app process not running" -f $n) }
            if ($script:rec) { Complete-Recording }
            if (-not $script:rec -and $ScreenRecordSegments -gt 0 -and $script:recIndex -lt $ScreenRecordSegments -and ((Get-Date) - $t0).TotalSeconds -lt $DurationSeconds) { Assert-AdbReady 'ScreenRecord'; Start-NextRecording }
            $elapsed = ((Get-Date) - $t0).TotalSeconds
            if ($elapsed -ge $DurationSeconds) { break }
            $sleep = $SampleIntervalSeconds - ((Get-Date) - $tick).TotalSeconds
            $sleep = [math]::Min($sleep, $DurationSeconds - $elapsed)
            if ($sleep -gt 0) { Start-Sleep -Milliseconds ([int]($sleep * 1000)) }
        } while ($true)
        $manifest.timestamps.samplingEnded = Get-IsoNow

        # --- videos: finish and pull (never deleted on the device) ---------------------------------------------------
        if ($ScreenRecordSegments -gt 0) {
            Stop-RecordingAtEnd
            Assert-AdbReady 'Pull video'
            $vdir = Join-Path $sessionDir 'video'; New-Item -ItemType Directory -Force -Path $vdir | Out-Null
            foreach ($v in $videos) {
                $dest = Join-Path $vdir ([IO.Path]::GetFileName($v.remote))
                $out = Adb pull $v.remote $dest
                if ($script:AdbExit -eq 0 -and (Test-Path $dest)) {
                    $v.file = 'video/' + [IO.Path]::GetFileName($dest); $v.bytes = (Get-Item $dest).Length; $v.sha256 = Get-FileSha256 $dest; $v.pulled = $true
                    # read-only check of the copy against the file on the phone (size + SHA-256); nothing is deleted here
                    $sz = @(Adb shell stat -c '%s' $v.remote) | Where-Object { $_ -match '^\s*\d+\s*$' } | Select-Object -First 1
                    if ($sz) { $v.deviceBytes = [long]$sz.Trim() }
                    $dh = @(Adb shell sha256sum $v.remote) | Where-Object { $_ -match '^\s*([0-9a-fA-F]{64})\b' } | Select-Object -First 1
                    if ($dh -and $dh -match '^\s*([0-9a-fA-F]{64})\b') { $v.deviceSha256 = $Matches[1].ToLowerInvariant() }
                    $v.copyVerified = ($null -ne $v.deviceSha256) -and ($v.deviceSha256 -eq $v.sha256) -and ($null -eq $v.deviceBytes -or $v.deviceBytes -eq $v.bytes)
                } else { $v.note = ('pull failed: ' + (($out | Select-Object -Last 1))).Trim() }
            }
            Note ("screenrecord files stay on the phone in /sdcard (metinar-{0}-NN.mp4); the harness deletes nothing - see 'deviceCleanup' for the manual removal list" -f $SessionId)
            Step 'Video' 'OK' ("{0}/{1} segment(s) pulled" -f @($videos | Where-Object { $_.pulled }).Count, $videos.Count)
        }
        # final logcat dump
        Assert-AdbReady 'Logcat'
        [void](Get-LogcatChunk)
        [void](Get-AppPid)
        Step 'Logcat' 'OK' ("{0} dump(s), {1} line(s), {2} possible gap(s)" -f $collector.Dumps, $collector.Lines.Count, $collector.Gaps.Count)
    }
} catch [System.OperationCanceledException] {
    Step 'Adb' 'FAIL' $script:adbLost
    $exitCode = 3
} catch [System.InvalidOperationException] {
    $exitCode = 1
} finally {
    if ($script:rec) { try { if (-not $script:rec.Proc.HasExited) { $script:rec.Proc.Kill() } } catch { Write-Verbose 'recording client already exited' }; $script:rec = $null }
}

# --- Analysis (pure, also on a partial session) --------------------------------------------------------------------
$all = $collector.Lines.ToArray()
$appPattern = 'AndroidRuntime|DEBUG\s*:|libc\s*:|ActivityManager|ActivityTaskManager|Unity\s*:|\[MetinARPerf\]|\[ArDamage\]|\[ArEncounter\]|\[ArAnchor\]|\[Combo\]|\[WorldMapPrototype\]'
$filtered = @($all | Where-Object {
    $l = $_; $mine = $false
    if ($l -match '^\d\d-\d\d \S+\s+(\d+)\s') { $mine = $pidsSeen.Contains([int]$Matches[1]) }
    $mine -or $l -match [regex]::Escape($Package) -or $l -match $appPattern
})
if ($deviceOk) {
    Set-Content -Path (Join-Path $sessionDir 'logcat-full-redacted.txt') -Value (ConvertTo-MetinARRedactedText -Lines $all -Secrets $secrets) -Encoding UTF8
    Set-Content -Path (Join-Path $sessionDir 'logcat-filtered.txt') -Value (ConvertTo-MetinARRedactedText -Lines $filtered -Secrets $secrets) -Encoding UTF8
}
foreach ($g in $collector.Gaps) { $adbGaps.Add($g) }

$items = New-Object System.Collections.Generic.List[object]
function Add-SessionItem([string]$Name, [string]$Measured, [string]$Detail, $Data) {
    $items.Add([pscustomobject][ordered]@{ name = $Name; measured = $Measured; verdict = 'UNVERIFIED'; reviewRequired = $true; reviewer = $null; detail = $Detail; data = $Data })
}
$target = $null
if ($budgets.PSObject.Properties['productTargets']) { $target = Get-MetinARProp $budgets.productTargets $TargetName }
if ($target -and $CombatScene) {
    # #90 decision 2: the combat scene comes from configuration; -CombatScene overrides productTargets.<TargetName>.sceneNames
    $target = ($target | ConvertTo-Json -Depth 6 | ConvertFrom-Json)
    $target.sceneNames = @($CombatScene)
}
$combatScenes = @(); if ($target) { $combatScenes = @(Get-MetinARProp $target 'sceneNames') }
$manifest.settings['combatScenes'] = $combatScenes
function Get-SceneBudget([string]$Scene) {
    $b = Get-MetinARProp $budgets.scenes $Scene
    if ($null -eq $b) { foreach ($prop in $budgets.scenes.PSObject.Properties) { $names = Get-MetinARProp $prop.Value 'sceneNames'; if ($names -and @($names) -contains $Scene) { $b = $prop.Value } } }
    return $b
}

if ($deviceOk) {
    # Crash / ANR / exceptions
    $pidsArr = [int[]]$pidsSeen.ToArray()
    $capture = Test-MetinARLogcatCapture -Lines $all -ExitCode $script:lastLogcatExit -Package $Package -Pids $pidsArr
    $problems = Find-MetinARLogcatProblems -Lines $all -Package $Package -Pids $pidsArr
    $cm = $problems.Status
    if ($capture.Kind -ne 'Ok' -or $script:logcatErrors -gt 0) {
        if ($cm -ne 'FAIL') { $cm = 'NO DATA' }
        Note ("logcat collection problem ({0}; failed dumps {1}): crash state is not proven" -f $capture.Reason, $script:logcatErrors)
        if ($exitCode -eq 0 -and $capture.Kind -ne 'Ok') { $exitCode = 4 }
    }
    if ($cm -eq 'UNVERIFIED') { $cm = 'NO DATA' }
    $crashLines = @(@($problems.Crashes) + @($problems.Anrs) + @($problems.FatalLines) + @($problems.MetinARExceptions) | Where-Object { $_ } | Select-Object -First 10)
    $crashEvidence = @()
    if ($crashLines.Count) { $crashEvidence = @(ConvertTo-MetinARRedactedText -Lines $crashLines -Secrets $secrets) }
    Add-SessionItem 'Crash' $cm ("crash={0} anr={1} fatal={2} metinarExceptions={3} unityErrors={4}" -f @($problems.Crashes).Count, @($problems.Anrs).Count, @($problems.FatalLines).Count, @($problems.MetinARExceptions).Count, @($problems.UnityErrors).Count) `
        ([ordered]@{ evidence = $crashEvidence })

    # Cold launch
    if ($null -ne $script:launch.totalTimeMs) {
        $maxL = [int]$budgets.device.maxColdLaunchMs
        Add-SessionItem 'ColdLaunch' $(if (-not $script:launch.ok) { 'FAIL' } elseif ($script:launch.totalTimeMs -gt $maxL) { 'WARN' } else { 'PASS' }) ("am start -W TotalTime={0} ms (budget {1} ms)" -f $script:launch.totalTimeMs, $maxL) $null
    } else { Add-SessionItem 'ColdLaunch' 'NO DATA' 'no am start -W TotalTime (-SkipLaunch or launch not reached)' $null }

    # Combat FPS (product target, spec section 15)
    if ($target) {
        $fps = Test-MetinARCombatFps -Lines $all -Target $target -CombatPattern $CombatPattern -ComboPattern $ComboPattern -WarmupSeconds $WarmupSeconds
        $chk = @($fps.Checks | ForEach-Object { "{0} {1}: {2} {3}" -f $_.Metric, $_.Limit, $_.Observed, $_.Status }) -join '; '
        Add-SessionItem 'CombatFps30' $fps.Measured ("target {0}: {1} combat window(s) after {2} s warm-up (combat lines {3}, combo lines {4}); {5}{6}" -f $TargetName, $fps.Windows, $fps.WarmupSeconds, $fps.CombatLines, $fps.ComboLines, $chk, $(if ($fps.Reason) { ' - ' + $fps.Reason } else { '' })) $fps
    } else { Add-SessionItem 'CombatFps30' 'NO DATA' "productTargets.$TargetName missing in perf-budgets.json" $null }

    # Prototype regression gates (scenes{})
    $samples = Get-MetinARPerfSamples -Lines $all
    foreach ($sc in @($samples | ForEach-Object { $_.scene } | Select-Object -Unique)) {
        $b = Get-SceneBudget $sc
        if ($null -eq $b) { continue }
        $r = Test-MetinARPerfBudget -Samples @($samples | Where-Object { $_.scene -eq $sc }) -Budget $b -BudgetName $sc
        $m = if ($r.Status -eq 'UNVERIFIED') { 'NO DATA' } else { $r.Status }
        Add-SessionItem ("PrototypeGate:{0}" -f $sc) $m ((@($r.Checks | ForEach-Object { "{0} {1}: {2} {3}" -f $_.Metric, $_.Limit, $_.Observed, $_.Status }) -join '; ') + ' (prototype regression gate, not the product target)') $r
    }
    if ($samples.Count -eq 0) { Add-SessionItem 'PrototypeGates' 'NO DATA' 'no [MetinARPerf] lines (release build or probe missing)' $null }

    # Memory
    $pssVals = [double[]]@($sampleRows | ForEach-Object { $_.pssMB } | Where-Object { $null -ne $_ })
    $maxPss = $null
    foreach ($cs in $combatScenes) { $arBudget = Get-SceneBudget $cs; if ($arBudget -and $null -ne (Get-MetinARProp $arBudget 'maxPssMB')) { $maxPss = Get-MetinARProp $arBudget 'maxPssMB'; break } }
    $growth = 10; if ($target -and $null -ne (Get-MetinARProp $target 'maxPssGrowthPercent')) { $growth = [double](Get-MetinARProp $target 'maxPssGrowthPercent') }
    $memR = Test-MetinARPssTrend -SamplesMB $pssVals -MaxPssMB $maxPss -MaxGrowthPercent $growth
    Add-SessionItem 'Memory' $memR.Measured ("PSS first={0} last={1} peak={2} MB (budget {3} MB), growth {4}% (limit < {5}%), {6} sample(s){7}" -f $memR.FirstMB, $memR.LastMB, $memR.PeakMB, $maxPss, $memR.GrowthPercent, $growth, $memR.Samples, $(if ($memR.Reason) { ' - ' + $memR.Reason } else { '' })) $memR

    # Thermal / battery (validity of the FPS run)
    $thS = @($sampleRows | Where-Object { $null -ne $_.thermalStatus })
    $btS = @($sampleRows | Where-Object { $null -ne $_.batteryTempC })
    if ($thS.Count -or $btS.Count) {
        $maxTh = if ($thS.Count) { ($thS | ForEach-Object { [int]$_.thermalStatus } | Measure-Object -Maximum).Maximum } else { $null }
        $maxBt = if ($btS.Count) { ($btS | ForEach-Object { [double]$_.batteryTempC } | Measure-Object -Maximum).Maximum } else { $null }
        $startTh = if ($thS.Count) { $thS[0].thermalStatus } else { $null }; $startBt = if ($btS.Count) { $btS[0].batteryTempC } else { $null }
        $m = 'PASS'; $why = @()
        if (($null -ne $maxTh -and $maxTh -ge [int]$budgets.device.failThermalStatus) -or ($null -ne $maxBt -and $maxBt -ge [double]$budgets.device.failBatteryTempC)) { $m = 'FAIL'; $why += 'throttling/overheating: the FPS run is not valid' }
        elseif (($null -ne $maxTh -and $maxTh -ge [int]$budgets.device.warnThermalStatus) -or ($null -ne $maxBt -and $maxBt -ge [double]$budgets.device.warnBatteryTempC)) { $m = 'WARN'; $why += 'warm device' }
        if ($target) {
            $sT = Get-MetinARProp $target 'maxStartThermalStatus'; $sB = Get-MetinARProp $target 'maxStartBatteryTempC'
            if (($null -ne $sT -and $null -ne $startTh -and [int]$startTh -gt [int]$sT) -or ($null -ne $sB -and $null -ne $startBt -and [double]$startBt -gt [double]$sB)) { if ($m -eq 'PASS') { $m = 'WARN' }; $why += 'session did not start cold (protocol: cool down before the FPS run)' }
        }
        Add-SessionItem 'Thermal' $m ("start thermal={0} battery={1} C; max thermal={2} battery={3} C {4}" -f $startTh, $startBt, $maxTh, $maxBt, ($why -join '; ')) $null
    } else { Add-SessionItem 'Thermal' 'NO DATA' 'no thermalservice / battery sample' $null }

    # Frames (display side; informational)
    $sfRows = @($sampleRows | Where-Object { $_.surfaceFlinger })
    $gfxRows = @($sampleRows | Where-Object { $_.gfx -and $_.gfx.TotalFrames })
    $fd = @()
    if ($sfRows.Count) { $fd += ("SurfaceFlinger fpsAvg min={0} max={1} over {2} sample(s)" -f ($sfRows | ForEach-Object { $_.surfaceFlinger.fpsAvg } | Measure-Object -Minimum).Minimum, ($sfRows | ForEach-Object { $_.surfaceFlinger.fpsAvg } | Measure-Object -Maximum).Maximum, $sfRows.Count) }
    if ($gfxRows.Count) { $fd += ("gfxinfo frames={0} janky max={1}%" -f ($gfxRows | ForEach-Object { $_.gfx.TotalFrames } | Measure-Object -Sum).Sum, ($gfxRows | ForEach-Object { $_.gfx.JankyPercent } | Measure-Object -Maximum).Maximum) }
    Add-SessionItem 'FramesDisplay' $(if ($fd.Count) { 'PASS' } else { 'NO DATA' }) ((($fd -join '; ') + ' (informational: the 30 FPS verdict uses [MetinARPerf] combat windows)').Trim()) $null
}

# 22 steps of owner spec section 18 + mandatory A97 (#97, lead decision 04:43) - always UNVERIFIED, log lines are hints only
$hints = Get-MetinARStepLogEvidence -Steps @($stepsDef.steps) -Lines $all
$stepsAll = @()
foreach ($s in @($stepsDef.steps)) {
    $h = $hints | Where-Object { [string]$_.id -eq [string]$s.id } | Select-Object -First 1
    $stepsAll += [pscustomobject][ordered]@{ id = $s.id; mandatory = $true; step = $s.step; criterion = $s.criterion; evidence = $s.evidence; dependsOn = $s.dependsOn; fixOwner = $s.fixOwner
        logHint = $(if ($null -eq $h.logLines) { 'no log signal defined - video only' } elseif ($h.logLines -gt 0) { "{0} log line(s), first {1}" -f $h.logLines, $h.firstStamp } else { 'no matching log line' })
        verdict = 'UNVERIFIED'; reviewer = $null; videoRef = $null; attempts = @(); problem = $null }
}
$steps22 = @($stepsAll | Where-Object { [string]$_.id -match '^\d+$' })
$stepA97 = $stepsAll | Where-Object { [string]$_.id -ceq 'A97' } | Select-Object -First 1
$stageClosure = Get-MetinARStageClosure -StepResults $stepsAll -Definition $stepsDef
if ($null -eq $stepA97) { $notes.Add('step A97 (#97) missing from etap1-steps.json: stage closure INVALID DEFINITION') }

$measuredAll = @($items | ForEach-Object { $_.measured })
$measuredOverall = if ($exitCode -eq 2) { 'NO DATA' } elseif ($measuredAll -contains 'FAIL' -or $exitCode -eq 3) { 'FAIL' } elseif ($measuredAll -contains 'WARN') { 'WARN' } elseif ($measuredAll -contains 'NO DATA' -or $measuredAll.Count -eq 0) { 'NO DATA' } else { 'PASS' }
if ($exitCode -eq 0 -and $measuredAll -contains 'FAIL') { $exitCode = 1 }

# --- manifest + summary -------------------------------------------------------------------------------------------
$manifest.timestamps.finished = Get-IsoNow
$manifest.adb.checks = $script:adbChecks; $manifest.adb.gaps = $adbGaps.ToArray(); $manifest.adb.lost = $script:adbLost
$manifest.install = $script:install; $manifest.launch = $script:launch
$manifest.steps = $stepLog.ToArray(); $manifest.videos = $videos.ToArray()
$manifest.notes = $notes.ToArray()

# #90 decision 6: files left on the phone, for manual removal by the owner / A31 slot holder after SHA verification
$deviceCleanup = @($videos | ForEach-Object {
    [ordered]@{ devicePath = $_.remote; bytes = $_.bytes; sha256 = $_.sha256; localCopy = $_.file; deviceBytes = $_.deviceBytes; deviceSha256 = $_.deviceSha256
        copyVerified = [bool]$_.copyVerified
        action = $(if ($_.copyVerified) { 'may be deleted by hand after review (copy verified: SHA-256 on the phone = pulled copy)' }
                   elseif ($_.pulled) { 'DO NOT DELETE yet: the SHA-256 of the copy was not confirmed on the phone - verify by hand first' }
                   else { 'DO NOT DELETE: no local copy' }) } })
$manifest.deviceCleanup = $deviceCleanup
$summary = [ordered]@{
    schema = 1; sessionId = $SessionId; verdict = 'UNVERIFIED'; measuredOverall = $measuredOverall; exitCode = $exitCode; stageClosure = $stageClosure
    rule = 'measured = automatic measurement; verdict stays UNVERIFIED until a human reviews the video and fills reviewer. Never declare 30 FPS from simulation or unit tests.'
    build = [ordered]@{ apk = $manifest.apk.name; apkSha256 = $manifest.apk.sha256; gameCommit = $manifest.gameCommit.sha; qaToolCommit = $manifest.qaTool.gitHead }
    device = [ordered]@{ model = $manifest.device.model; android = $manifest.device.android; serialSha256 = $manifest.device.serialSha256 }
    items = $items.ToArray(); steps = $stepsAll; steps22 = $steps22; stepA97 = $stepA97; deviceCleanup = $deviceCleanup; adbGaps = $adbGaps.ToArray(); notes = $notes.ToArray()
}
Write-Json $summary 'session-summary.json'

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# Sesja urzadzenia MetinAR: $SessionId")
$md.Add('')
$md.Add('**Werdykt: UNVERIFIED** (wymaga przegladu nagrania przez czlowieka; pomiar automatyczny: **' + $measuredOverall + '**, exit ' + $exitCode + ')')
$md.Add('')
$md.Add('| Pole | Wartosc |'); $md.Add('|---|---|')
$md.Add("| APK | $($manifest.apk.name) |"); $md.Add("| APK SHA-256 | $($manifest.apk.sha256) |"); $md.Add("| Pakiet / versionName | $($manifest.apk.package) / $($manifest.apk.versionName) |")
$md.Add("| Commit gry | $($manifest.gameCommit.sha) ($($manifest.gameCommit.source)) |"); $md.Add("| Commit narzedzia QA | $($manifest.qaTool.gitHead) (dirty=$($manifest.qaTool.dirty)) |")
$md.Add("| Urzadzenie | $($manifest.device.manufacturer) $($manifest.device.model), Android $($manifest.device.android) (API $($manifest.device.sdk)), serialSha256 $($manifest.device.serialSha256) |")
$md.Add("| Start / koniec | $($manifest.timestamps.started) / $($manifest.timestamps.finished) |")
$md.Add("| Probki / adb checks / luki | $($sampleRows.Count) / $($script:adbChecks) / $($adbGaps.Count) |")
$md.Add(("| Bufor logcat | {0}; od {1}; odrzucone starsze linie {2} |" -f $(if ($manifest.logcat.cleared) { "wyczyszczony (-ClearLogcat)" } else { "nie czyszczony (domyslnie)" }), $manifest.logcat.sinceDeviceTime, $manifest.logcat.droppedOlderLines))
$md.Add('')
$md.Add('## Pomiary'); $md.Add('')
$md.Add('| Pozycja | Pomiar | Werdykt | Szczegoly |'); $md.Add('|---|---|---|---|')
foreach ($i in $items) { $md.Add(("| {0} | {1} | {2} | {3} |" -f $i.name, $i.measured, $i.verdict, ($i.detail -replace '\|', '/'))) }
$md.Add('')
$md.Add('## Zamkniecie ETAPU I'); $md.Add('')
$md.Add(('**{0}**: PASS {1}/{2} wymaganych krokow (22 kroki spec section 18 + obowiazkowy A97 z #97). A97: {3}.' -f $stageClosure.verdict, $stageClosure.passed, $stageClosure.requiredCount, $stageClosure.a97))
$md.Add('ETAP I jest zamkniety tylko przy PASS (recenzent + nagranie udanej proby) wszystkich 22 krokow i A97 na jednym APK. A97 nie jest wpisem recznym.')
$md.Add('')
$md.Add('## 22 kroki (spec section 18) + A97 (#97, obowiazkowy) - do uzupelnienia po przegladzie nagrania'); $md.Add('')
$md.Add('| # | Krok | Werdykt | Wskazowka z logu | Zalezy od | Wlasciciel poprawki |'); $md.Add('|---|---|---|---|---|---|')
foreach ($s in $stepsAll) { $md.Add(("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $s.id, $s.step, $s.verdict, ($s.logHint -replace '\|', '/'), (@($s.dependsOn) -join ', '), $s.fixOwner)) }
if ($stepA97) { $md.Add(''); $md.Add('**A97 - kryterium z #97 (doslownie):** ' + $stepA97.criterion); $md.Add(''); $md.Add('Dowod: ' + $stepA97.evidence) }
if ($videos.Count) { $md.Add(''); $md.Add('## Nagrania (lokalnie, nie wysylac na GitHub)'); $md.Add(''); foreach ($v in $videos) { $md.Add(("- seg {0}: {1} -> {2}, {3} B, sha256 {4} {5}" -f $v.index, $v.startedAt, $v.file, $v.bytes, $v.sha256, $v.note)) } }
if ($deviceCleanup.Count) {
    $md.Add(''); $md.Add('## Pliki do recznego usuniecia z telefonu (po weryfikacji SHA-256)'); $md.Add('')
    $md.Add('Skrypt niczego nie usuwa. Usuwa wlasciciel albo osoba trzymajaca slot A31 (#33), dopiero gdy SHA-256 kopii jest potwierdzony.'); $md.Add('')
    $md.Add('| Sciezka na telefonie | Rozmiar kopii (B) | SHA-256 kopii | SHA-256 na telefonie | Kopia zweryfikowana | Dzialanie |'); $md.Add('|---|---|---|---|---|---|')
    foreach ($c in $deviceCleanup) { $md.Add(("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $c.devicePath, $c.bytes, $c.sha256, $c.deviceSha256, $(if ($c.copyVerified) { 'tak' } else { 'NIE' }), $c.action)) }
}
if ($adbGaps.Count -or $notes.Count) { $md.Add(''); $md.Add('## Luki i uwagi'); $md.Add(''); foreach ($g in $adbGaps) { $md.Add("- $g") }; foreach ($nt in $notes) { $md.Add("- $nt") } }
$mdText = (ConvertTo-MetinARRedactedText -Lines $md.ToArray() -Secrets $secrets -KeepHex)
Set-Content -Path (Join-Path $sessionDir 'session-summary.md') -Value $mdText -Encoding UTF8

$manifest.files = @(Get-ChildItem -Path $sessionDir -Recurse -File | Where-Object { $_.Name -ne 'session-manifest.json' } | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($sessionDir.Length).TrimStart('\', '/') -replace '\\', '/'; bytes = $_.Length; sha256 = Get-FileSha256 $_.FullName } })
Write-Json $manifest 'session-manifest.json'

Write-Host ''
Write-Host ("SESSION {0}: verdict UNVERIFIED, measured {1}, exit {2} -> {3}" -f $SessionId, $measuredOverall, $exitCode, $sessionDir)
exit $exitCode
