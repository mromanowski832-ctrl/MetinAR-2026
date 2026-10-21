<#
.SYNOPSIS
  MetinAR physical Android smoke + performance harness over ADB (METINAR-011).

.DESCRIPTION
  Steps (each reported PASS / FAIL / WARN / UNVERIFIED):
    Device         exactly one authorized device (serial is never written to reports)
    Install        adb install -r -t <apk>  (never -g: runtime permissions are NOT auto-granted)
    ColdLaunch     force-stop, clear logcat, am start -W  -> launch status + TotalTime
    PermissionUi   detects an OS permission dialog; it is never tapped. The owner decides on the phone.
    Process        process alive and its activity resumed after the soak period
    Permissions    runtime permission state (granted / not granted) from dumpsys package
    Background     HOME -> process kept / not resumed -> location requests while backgrounded -> resume same pid
    Crash          crash / ANR / FATAL markers for the app in logcat
    Memory         dumpsys meminfo TOTAL PSS vs. prototype budget
    Cpu            app CPU % from dumpsys cpuinfo (informational; GPU utilisation needs vendor tools)
    Frames         SurfaceFlinger present-time FPS (display side, approximate) + gfxinfo summary
    PerfBudget     [MetinARPerf] samples (development builds) vs. tools/qa/perf-budgets.json
    Thermal        thermal status + battery temperature
  Writes Builds/qa/device-<timestamp>/device-report.md, device-report.json and logcat-redacted.txt.
  Raw logcat is never written to disk; only the redacted copy (no serial, IP, MAC, e-mail, coordinates, IDs).

  Exit codes: 0 = no FAIL, 1 = at least one FAIL, 2 = no authorized device (nothing verified).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARDeviceSmoke.ps1 -ApkPath client\Builds\Android\MetinAR-ar-encounter-dev.apk
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARDeviceSmoke.ps1 -SkipInstall -SoakSeconds 120 -BudgetScene WorldMapPrototype
#>
[CmdletBinding()]
param(
    [string]$ApkPath,
    [string]$Package,
    [string]$Serial,
    [string]$AdbPath,
    [string]$UnityPath,
    [string]$OutputDir,
    [int]$LaunchWaitSeconds = 15,
    [int]$SoakSeconds = 30,
    [int]$PermissionWaitSeconds = 0,
    [string]$BudgetScene,
    [switch]$SkipInstall,
    [switch]$SkipBackgroundTest
)
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot 'MetinARQa.psm1') -Force -DisableNameChecking
# Reports use '.' decimals regardless of the Windows locale (e.g. pl-PL).
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$repo = Get-MetinARRepoRoot -Start $PSScriptRoot
$config = Read-MetinARJson -Path (Join-Path $repo 'tools/qa/qa-config.json')
$budgets = Read-MetinARJson -Path (Join-Path $repo $config.budgetsFile)
if (-not $Package) { $Package = $config.androidBuild.expectedPackage }
if (-not $OutputDir) { $OutputDir = Join-Path $repo ('Builds/qa/device-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if (-not $AdbPath) {
    if (-not $UnityPath) { $UnityPath = Find-MetinARUnity -Version $config.unityVersion }
    $AdbPath = Find-MetinARAndroidTool -Name adb -UnityPath $UnityPath
}
if (-not $AdbPath) { throw 'adb not found (pass -AdbPath, set ANDROID_HOME, or install the Unity Android module).' }

$results = New-Object System.Collections.Generic.List[object]
$notes = New-Object System.Collections.Generic.List[string]
$secrets = New-Object System.Collections.Generic.List[string]
function Add-Result($r) {
    $results.Add($r)
    $color = switch ($r.Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'Gray' } }
    Write-Host ("[{0,-10}] {1,-13} {2}" -f $r.Status, $r.Name, (ConvertTo-MetinARRedactedText -Lines @($r.Detail) -Secrets $secrets)[0]) -ForegroundColor $color
}
function Adb {
    $a = @(); if ($Serial) { $a += @('-s', $Serial) }
    $out = & $AdbPath @($a + $args) 2>&1 | ForEach-Object { "$_" }
    return @($out)
}
function Write-Report {
    $header = [ordered]@{}
    foreach ($k in $deviceInfo.Keys) { $header[$k] = $deviceInfo[$k] }
    $header['Package'] = $Package
    $header['Date'] = (Get-Date -Format 'yyyy-MM-dd HH:mm zzz')
    $h = @{}; foreach ($k in $header.Keys) { $h[$k] = $header[$k] }
    $md = ConvertTo-MetinARMarkdownReport -Title 'MetinAR physical-device smoke report' -Results $results -Header $h -Notes $notes
    $md = (ConvertTo-MetinARRedactedText -Lines ($md -split "`n") -Secrets $secrets -KeepHex) -join "`n"
    Set-Content -Path (Join-Path $OutputDir 'device-report.md') -Value $md -Encoding UTF8
    $json = [pscustomobject]@{ overall = (Get-MetinAROverallStatus -Results $results); device = $header; results = $results; notes = $notes } | ConvertTo-Json -Depth 6
    $json = (ConvertTo-MetinARRedactedText -Lines ($json -split "`n") -Secrets $secrets -KeepHex) -join "`n"
    Set-Content -Path (Join-Path $OutputDir 'device-report.json') -Value $json -Encoding UTF8
    Write-Host ''
    Write-Host ("OVERALL: {0}   report: {1}" -f (Get-MetinAROverallStatus -Results $results), (Join-Path $OutputDir 'device-report.md'))
}
$deviceInfo = [ordered]@{}

# --- Device ---------------------------------------------------------------------------------
$devLines = @(& $AdbPath devices 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match '^\S+\s+(device|unauthorized|offline)$' })
foreach ($l in $devLines) { $secrets.Add(($l -split '\s+')[0]) }
if ($Serial) { $secrets.Add($Serial); $devLines = @($devLines | Where-Object { $_ -like "$Serial*" }) }
$ready = @($devLines | Where-Object { $_ -match '\sdevice$' })
$unauth = @($devLines | Where-Object { $_ -match '\sunauthorized$' })
if ($ready.Count -ne 1) {
    $why = if ($ready.Count -gt 1) { "$($ready.Count) devices connected; pass -Serial" } elseif ($unauth.Count) { 'device connected but USB debugging not authorized - owner must accept the prompt on the phone' } else { 'no device connected (adb devices empty)' }
    Add-Result (New-MetinARGateResult -Name 'Device' -Status 'UNVERIFIED' -Detail $why)
    $notes.Add('Nothing was verified on a physical device in this run.')
    Write-Report
    exit 2
}
if (-not $Serial) { $Serial = ($ready[0] -split '\s+')[0]; $secrets.Add($Serial) }
$deviceInfo['Model'] = (Adb shell getprop ro.product.model | Select-Object -First 1).Trim()
$deviceInfo['Android'] = (Adb shell getprop ro.build.version.release | Select-Object -First 1).Trim()
$deviceInfo['API'] = (Adb shell getprop ro.build.version.sdk | Select-Object -First 1).Trim()
$deviceInfo['ABI'] = (Adb shell getprop ro.product.cpu.abi | Select-Object -First 1).Trim()
Add-Result (New-MetinARGateResult -Name 'Device' -Status 'PASS' -Detail ("1 authorized device: {0}, Android {1} (API {2}), {3}" -f $deviceInfo.Model, $deviceInfo.Android, $deviceInfo.API, $deviceInfo.ABI))

# --- Install -------------------------------------------------------------------------------
$activity = $null
if ($ApkPath) {
    $aapt = Find-MetinARAndroidTool -Name aapt -UnityPath $UnityPath
    if ($aapt) {
        $badging = ConvertFrom-MetinARAaptBadging -Lines @(& $aapt dump badging $ApkPath 2>&1 | ForEach-Object { "$_" })
        if ($badging.Package) { $Package = $badging.Package; $activity = $badging.LaunchableActivity }
    }
}
if (-not $SkipInstall) {
    if (-not $ApkPath -or -not (Test-Path $ApkPath)) {
        Add-Result (New-MetinARGateResult -Name 'Install' -Status 'FAIL' -Detail 'no -ApkPath given (or use -SkipInstall)')
    } elseif (@((Test-MetinARApkPlayerData -Entries @(Get-MetinARZipEntries -Path $ApkPath)).Problems).Count) {
        # #31: never install an APK whose player data mixes whole files with stale .split chunks (crashes on scene load).
        $pd = Test-MetinARApkPlayerData -Entries @(Get-MetinARZipEntries -Path $ApkPath)
        Add-Result (New-MetinARGateResult -Name 'Install' -Status 'FAIL' -Detail 'APK rejected before install: inconsistent player data (stale .split chunks)' -Evidence @($pd.Problems))
    } else {
        $t0 = Get-Date
        $out = Adb install '-r' '-t' $ApkPath
        $ok = ($out -join ' ') -match 'Success'
        Add-Result (New-MetinARGateResult -Name 'Install' -Status $(if ($ok) { 'PASS' } else { 'FAIL' }) -Detail (($out | Select-Object -Last 1) + ' (runtime permissions not auto-granted)') -Seconds ((Get-Date) - $t0).TotalSeconds)
    }
} else {
    Add-Result (New-MetinARGateResult -Name 'Install' -Status 'SKIP' -Detail 'using the already installed build')
}
$installed = @(Adb shell pm list packages $Package) -contains "package:$Package"
if (-not $installed) {
    Add-Result (New-MetinARGateResult -Name 'ColdLaunch' -Status 'FAIL' -Detail "$Package is not installed")
    Write-Report; exit 1
}
if (-not $activity) {
    $resolved = @(Adb shell cmd package resolve-activity '--brief' $Package) | Where-Object { $_ -match '/' } | Select-Object -Last 1
    if ($resolved) { $activity = ($resolved.Trim() -split '/', 2)[1] }
}
$component = if ($activity) { if ($activity.StartsWith('.')) { "$Package/$Package$activity" } elseif ($activity.Contains('/')) { $activity } else { "$Package/$activity" } } else { $null }

# --- Cold launch ---------------------------------------------------------------------------
Adb shell am force-stop $Package | Out-Null
Adb logcat '-c' | Out-Null
Start-Sleep -Seconds 1
$launch = if ($component) { Adb shell am start '-W' '-n' $component } else { Adb shell monkey '-p' $Package '-c' android.intent.category.LAUNCHER 1 }
$total = $null
foreach ($l in $launch) { if ($l -match 'TotalTime:\s*(\d+)') { $total = [int]$Matches[1] } }
$launchOk = -not (($launch -join ' ') -match 'Error|Exception')
$maxLaunch = [int]$budgets.device.maxColdLaunchMs
$st = if (-not $launchOk) { 'FAIL' } elseif ($null -ne $total -and $total -gt $maxLaunch) { 'WARN' } else { 'PASS' }
Add-Result (New-MetinARGateResult -Name 'ColdLaunch' -Status $st -Detail ("component={0} TotalTime={1} ms (budget {2} ms)" -f $component, $total, $maxLaunch))
Start-Sleep -Seconds $LaunchWaitSeconds

# --- Permission UI (never tapped) ----------------------------------------------------------
function Get-Focus { return ((Adb shell dumpsys window) | Where-Object { $_ -match 'mCurrentFocus|mFocusedApp' } | Select-Object -First 1) }
$focus = Get-Focus
$dialog = $focus -match 'permissioncontroller|GrantPermissionsActivity'
if ($dialog -and $PermissionWaitSeconds -gt 0) {
    Write-Host "OS permission dialog is showing on the phone. Owner: choose Allow / Deny on the device. Waiting up to $PermissionWaitSeconds s..." -ForegroundColor Yellow
    $deadline = (Get-Date).AddSeconds($PermissionWaitSeconds)
    while ((Get-Date) -lt $deadline -and $dialog) { Start-Sleep -Seconds 3; $focus = Get-Focus; $dialog = $focus -match 'permissioncontroller|GrantPermissionsActivity' }
}
Add-Result (New-MetinARGateResult -Name 'PermissionUi' -Status $(if ($dialog) { 'UNVERIFIED' } else { 'PASS' }) -Detail $(if ($dialog) { 'OS permission dialog still showing - not tapped by the harness; owner decision required on the phone' } else { 'no OS permission dialog in focus' }))

# --- Soak ----------------------------------------------------------------------------------
Start-Sleep -Seconds $SoakSeconds
$appPid = ((Adb shell pidof $Package) | Select-Object -First 1)
$appPid = if ($appPid) { $appPid.Trim() } else { '' }
$resumed = ((Adb shell dumpsys activity activities) | Where-Object { $_ -match '(mResumedActivity|topResumedActivity|ResumedActivity)' -and $_ -match [regex]::Escape($Package) }).Count -gt 0
Add-Result (New-MetinARGateResult -Name 'Process' -Status $(if ($appPid -and ($resumed -or $dialog)) { 'PASS' } elseif ($appPid) { 'WARN' } else { 'FAIL' }) -Detail ("alive={0} resumed={1} after {2} s" -f [bool]$appPid, $resumed, ($LaunchWaitSeconds + $SoakSeconds)))

$pkgDump = Adb shell dumpsys package $Package
$perm = ConvertFrom-MetinARGrantedPermissions -Lines $pkgDump
$permText = @($perm.PSObject.Properties | Where-Object { $_.Name -match 'LOCATION|CAMERA' } | ForEach-Object { "$($_.Name -replace '^android\.permission\.', '')=$(if ($_.Value) { 'granted' } else { 'denied/unset' })" }) -join ', '
$bgGranted = ($perm.PSObject.Properties['android.permission.ACCESS_BACKGROUND_LOCATION'] -and $perm.'android.permission.ACCESS_BACKGROUND_LOCATION')
Add-Result (New-MetinARGateResult -Name 'Permissions' -Status $(if ($bgGranted) { 'FAIL' } else { 'PASS' }) -Detail $(if ($permText) { $permText } else { 'no runtime location/camera permission entries' }))

$mem = ConvertFrom-MetinARMeminfo -Lines (Adb shell dumpsys meminfo $Package)
$gfx = Adb shell dumpsys gfxinfo $Package
$gfxTotal = ($gfx | Where-Object { $_ -match 'Total frames rendered' } | Select-Object -First 1)
$gfxJank = ($gfx | Where-Object { $_ -match 'Janky frames' } | Select-Object -First 1)
$layer = (Adb shell dumpsys SurfaceFlinger '--list') | Where-Object { $_ -match [regex]::Escape($Package) -and $_ -match 'SurfaceView' -and $_ -notmatch 'Background' } | Select-Object -First 1
$sf = $null
if ($layer) { $sf = ConvertFrom-MetinARSurfaceFlingerLatency -Lines (Adb shell dumpsys SurfaceFlinger '--latency' ("'" + $layer.Trim() + "'")) }
$cpu = ConvertFrom-MetinARCpuInfo -Lines (Adb shell dumpsys cpuinfo) -Package $Package
$thermal = ConvertFrom-MetinARThermal -ThermalLines (Adb shell dumpsys thermalservice) -BatteryLines (Adb shell dumpsys battery)

# --- Background / resume -------------------------------------------------------------------
if (-not $SkipBackgroundTest -and -not $dialog -and $appPid) {
    Adb shell input keyevent KEYCODE_HOME | Out-Null
    Start-Sleep -Seconds 6
    $bgPid = ((Adb shell pidof $Package) | Select-Object -First 1)
    $stillResumed = ((Adb shell dumpsys activity activities) | Where-Object { $_ -match '(mResumedActivity|topResumedActivity)' -and $_ -match [regex]::Escape($Package) }).Count -gt 0
    $locLines = @((Adb shell dumpsys location) | Where-Object { $_ -match [regex]::Escape($Package) -and $_ -match '(?i)request|listener|active' })
    if ($component) { Adb shell am start '-n' $component | Out-Null } else { Adb shell monkey '-p' $Package '-c' android.intent.category.LAUNCHER 1 | Out-Null }
    Start-Sleep -Seconds 6
    $rePid = ((Adb shell pidof $Package) | Select-Object -First 1)
    $reResumed = ((Adb shell dumpsys activity activities) | Where-Object { $_ -match '(mResumedActivity|topResumedActivity)' -and $_ -match [regex]::Escape($Package) }).Count -gt 0
    $samePid = ($rePid -and $rePid.Trim() -eq $appPid)
    $st = 'PASS'
    if (-not $reResumed -or -not $rePid) { $st = 'FAIL' } elseif (-not $samePid) { $st = 'WARN' }
    if ($locLines.Count -gt 0) { $st = 'WARN' }
    Add-Result (New-MetinARGateResult -Name 'Background' -Status $st -Detail ("background: alive={0} resumed={1} locationEntries={2}; resume: resumed={3} samePid={4}" -f [bool]$bgPid, $stillResumed, $locLines.Count, $reResumed, $samePid) -Evidence (ConvertTo-MetinARRedactedText -Lines $locLines -Secrets $secrets))
    if ($locLines.Count -gt 0) { $notes.Add('dumpsys location lists the app while backgrounded - review against the foreground-only GPS rule (entries may be stale/inactive history).') }
} else {
    Add-Result (New-MetinARGateResult -Name 'Background' -Status $(if ($SkipBackgroundTest) { 'SKIP' } else { 'UNVERIFIED' }) -Detail $(if ($dialog) { 'skipped: permission dialog in focus' } elseif (-not $appPid) { 'skipped: process not running' } else { 'skipped (-SkipBackgroundTest)' }))
}

# --- Logcat (redacted) ---------------------------------------------------------------------
$raw = Adb logcat '-d' '-v' threadtime '-b' 'main,system,crash,events'
$pids = @(); foreach ($p in @($appPid, $rePid)) { if ($p -and "$p".Trim() -match '^\d+$') { $pids += [int]"$p".Trim() } }
$problems = Find-MetinARLogcatProblems -Lines $raw -Package $Package -Pids $pids
$appLines = @($raw | Where-Object {
    $l = $_; $mine = $false
    if ($l -match '^\d\d-\d\d \S+\s+(\d+)\s') { $mine = $pids -contains [int]$Matches[1] }
    $mine -or $l -match [regex]::Escape($Package) -or $l -match 'AndroidRuntime|DEBUG\s*:|libc\s*:|ActivityManager|ActivityTaskManager|\[MetinARPerf\]'
})
$redacted = ConvertTo-MetinARRedactedText -Lines $appLines -Secrets $secrets
Set-Content -Path (Join-Path $OutputDir 'logcat-redacted.txt') -Value $redacted -Encoding UTF8
$raw = $null
$ev = @() + @($problems.Crashes) + @($problems.Anrs) + @($problems.FatalLines) + @($problems.UnityErrors | Select-Object -First 5)
Add-Result (New-MetinARGateResult -Name 'Crash' -Status $problems.Status -Detail ("crash={0} anr={1} fatal={2} unityErrors={3} (redacted app log: {4} lines)" -f $problems.Crashes.Count, $problems.Anrs.Count, $problems.FatalLines.Count, $problems.UnityErrors.Count, $redacted.Count) -Evidence (ConvertTo-MetinARRedactedText -Lines $ev -Secrets $secrets))

# --- Memory / frames / perf budgets / thermal ----------------------------------------------
$samples = Get-MetinARPerfSamples -Lines $redacted
$scenes = @($samples | ForEach-Object { $_.scene } | Select-Object -Unique)
if (-not $BudgetScene) { $BudgetScene = if ($scenes.Count) { $scenes[0] } else { $config.expectedBuildScenes[0] -replace '^.*/', '' -replace '\.unity$', '' } }
$sceneBudget = Get-MetinARProp $budgets.scenes $BudgetScene
$pssMB = if ($mem.TotalPssKB) { [math]::Round($mem.TotalPssKB / 1024, 1) } else { $null }
$maxPss = if ($sceneBudget) { Get-MetinARProp $sceneBudget 'maxPssMB' } else { $null }
$st = if ($null -eq $pssMB) { 'UNVERIFIED' } elseif ($maxPss -and $pssMB -gt [double]$maxPss) { 'FAIL' } else { 'PASS' }
Add-Result (New-MetinARGateResult -Name 'Memory' -Status $st -Detail ("TOTAL PSS={0} MB (budget {1} MB, {2}); native={3} KB graphics={4} KB" -f $pssMB, $maxPss, $BudgetScene, $mem.NativeHeapKB, $mem.GraphicsKB))

$frameDetail = if ($sf -and $sf.Frames -gt 0) { "SurfaceFlinger: {0} frames, avg {1} fps, avg {2} ms, max {3} ms, janks(>2 vsync)={4}, refresh {5} ms" -f $sf.Frames, $sf.FpsAvg, $sf.FrameAvgMs, $sf.FrameMaxMs, $sf.Janks, $sf.RefreshMs } else { 'SurfaceFlinger latency unavailable for the Unity layer' }
if ($gfxTotal) { $frameDetail += "; gfxinfo: $($gfxTotal.Trim()) $(if ($gfxJank) { $gfxJank.Trim() })" }
$cpuDetail = if ($null -ne $cpu.CpuPercent) { "CPU {0}% (user {1}% + kernel {2}%, dumpsys cpuinfo recent window, 100% = one core)" -f $cpu.CpuPercent, $cpu.UserPercent, $cpu.KernelPercent } else { 'app not listed in dumpsys cpuinfo' }
Add-Result (New-MetinARGateResult -Name 'Cpu' -Status $(if ($null -ne $cpu.CpuPercent) { 'PASS' } else { 'UNVERIFIED' }) -Detail ($cpuDetail + '; GPU utilisation is not exposed without root/vendor tools (UNVERIFIED)'))
Add-Result (New-MetinARGateResult -Name 'Frames' -Status $(if ($sf -and $sf.Frames -gt 0) { 'PASS' } else { 'UNVERIFIED' }) -Detail ($frameDetail + ' (informational; budgets use [MetinARPerf])'))

$budgetResults = @()
foreach ($sc in $scenes) {
    $b = Get-MetinARProp $budgets.scenes $sc
    if ($null -eq $b) {
        foreach ($prop in $budgets.scenes.PSObject.Properties) { $names = Get-MetinARProp $prop.Value 'sceneNames'; if ($names -and @($names) -contains $sc) { $b = $prop.Value } }
    }
    if ($null -eq $b) { $notes.Add("No perf budget defined for scene '$sc'."); continue }
    $budgetResults += Test-MetinARPerfBudget -Samples @($samples | Where-Object { $_.scene -eq $sc }) -Budget $b -BudgetName $sc
}
# METINAR-014: scene-independent budgets (e.g. hand.inference latency) run over all samples; optional metrics that the
# build never reported produce no checks and are listed as a note instead of a fake PASS.
foreach ($prop in $budgets.scenes.PSObject.Properties) {
    if ((Get-MetinARProp $prop.Value 'anyScene') -ne $true -or @($samples).Count -eq 0) { continue }
    $br = Test-MetinARPerfBudget -Samples @($samples) -Budget $prop.Value -BudgetName $prop.Name
    if (@($br.Checks).Count -eq 0) { $notes.Add("Perf budget '$($prop.Name)': metric not reported by this build (feature not on this branch or not exercised)."); continue }
    $budgetResults += $br
}
if ($budgetResults.Count -eq 0) {
    Add-Result (New-MetinARGateResult -Name 'PerfBudget' -Status 'UNVERIFIED' -Detail 'no [MetinARPerf] samples (release build, probe missing, or soak too short)')
} else {
    $ev = @(); foreach ($br in $budgetResults) { foreach ($c in $br.Checks) { $ev += ("{0} {1}: {2} observed={3} limit {4}" -f $br.Name, $c.Status, $c.Metric, $c.Observed, $c.Limit) } }
    $st = if (@($budgetResults | Where-Object { $_.Status -eq 'FAIL' }).Count) { 'FAIL' } elseif (@($budgetResults | Where-Object { $_.Status -eq 'UNVERIFIED' }).Count) { 'WARN' } else { 'PASS' }
    Add-Result (New-MetinARGateResult -Name 'PerfBudget' -Status $st -Detail ("PROTOTYPE GATE: {0}" -f (($budgetResults | ForEach-Object { "$($_.Name)=$($_.Status) ($($_.Windows) windows)" }) -join ', ')) -Evidence $ev)
}

$d = $budgets.device
$st = 'PASS'
if (($null -ne $thermal.ThermalStatus -and $thermal.ThermalStatus -ge [int]$d.failThermalStatus) -or ($null -ne $thermal.BatteryTempC -and $thermal.BatteryTempC -ge [double]$d.failBatteryTempC)) { $st = 'FAIL' }
elseif (($null -ne $thermal.ThermalStatus -and $thermal.ThermalStatus -ge [int]$d.warnThermalStatus) -or ($null -ne $thermal.BatteryTempC -and $thermal.BatteryTempC -ge [double]$d.warnBatteryTempC)) { $st = 'WARN' }
if ($null -eq $thermal.ThermalStatus -and $null -eq $thermal.BatteryTempC) { $st = 'UNVERIFIED' }
Add-Result (New-MetinARGateResult -Name 'Thermal' -Status $st -Detail ("thermalStatus={0} batteryTemp={1} C batteryLevel={2}% (short run; not a sustained thermal test)" -f $thermal.ThermalStatus, $thermal.BatteryTempC, $thermal.BatteryLevel))

$notes.Add('A short scripted run is a smoke check, not proof that GPS accuracy, AR tracking quality, hand tracking or long-session thermals are acceptable; those need the owner checklist in docs/METINAR-011-QA-GATES.md.')
Write-Report
if (@($results | Where-Object { $_.Status -eq 'FAIL' }).Count) { exit 1 } else { exit 0 }
