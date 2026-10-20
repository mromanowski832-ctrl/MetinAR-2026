# MetinAR QA helpers (METINAR-011).
# Compatible with Windows PowerShell 5.1 and PowerShell 7+. Keep this file ASCII-only:
# Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
Set-StrictMode -Version 2.0

# ---------------------------------------------------------------------------------------------
# Gate results
# ---------------------------------------------------------------------------------------------

function New-MetinARGateResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet('PASS', 'FAIL', 'WARN', 'SKIP', 'UNVERIFIED')][string]$Status,
        [string]$Detail = '',
        [string[]]$Evidence = @(),
        [double]$Seconds = 0
    )
    [pscustomobject]@{
        Name     = $Name
        Status   = $Status
        Detail   = $Detail
        Evidence = @($Evidence)
        Seconds  = [math]::Round($Seconds, 1)
    }
}

function Get-MetinAROverallStatus {
    param([object[]]$Results)
    $statuses = @($Results | ForEach-Object { $_.Status })
    if ($statuses -contains 'FAIL') { return 'FAIL' }
    if ($statuses.Count -gt 0 -and $statuses -notcontains 'PASS' -and $statuses -notcontains 'WARN' -and $statuses -contains 'UNVERIFIED') { return 'UNVERIFIED' }
    if ($statuses -contains 'WARN' -or $statuses -contains 'UNVERIFIED') { return 'PASS_WITH_WARNINGS' }
    if ($statuses.Count -eq 0) { return 'NOT_RUN' }
    return 'PASS'
}

# ---------------------------------------------------------------------------------------------
# Repo / config / tool discovery
# ---------------------------------------------------------------------------------------------

function Get-MetinARRepoRoot {
    param([string]$Start = $PSScriptRoot)
    $dir = (Resolve-Path $Start).Path
    while ($dir) {
        if ((Test-Path (Join-Path $dir 'tools/qa/qa-config.json')) -and (Test-Path (Join-Path $dir 'client'))) { return $dir }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    throw 'MetinAR repo root not found (expected tools/qa/qa-config.json and client/).'
}

function Read-MetinARJson {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-Content -Raw -Path $Path | ConvertFrom-Json)
}

function Find-MetinARGit {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $roots = @('C:\Program Files\Microsoft Visual Studio', 'C:\Program Files (x86)\Microsoft Visual Studio')
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $hit = Get-ChildItem -Path $r -Recurse -Filter git.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like '*TeamFoundation\Team Explorer\Git\cmd\git.exe' } | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Find-MetinARUnity {
    param([string]$Version, [string]$Override)
    if ($Override) {
        if (Test-Path $Override) { return (Resolve-Path $Override).Path }
        throw "Unity not found at $Override"
    }
    if ($env:METINAR_UNITY -and (Test-Path $env:METINAR_UNITY)) { return $env:METINAR_UNITY }
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$Version\Editor\Unity.exe",
        "/Applications/Unity/Hub/Editor/$Version/Unity.app/Contents/MacOS/Unity",
        "$HOME/Unity/Hub/Editor/$Version/Editor/Unity"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

function Find-MetinARAndroidTool {
    param([Parameter(Mandatory = $true)][ValidateSet('adb', 'aapt2', 'aapt', 'apksigner')][string]$Name, [string]$UnityPath)
    $exe = $Name
    if ($env:OS -eq 'Windows_NT') { $exe = if ($Name -eq 'apksigner') { 'apksigner.bat' } else { "$Name.exe" } }
    $sdkRoots = @()
    if ($UnityPath) {
        $editorDir = Split-Path $UnityPath -Parent
        $sdkRoots += (Join-Path $editorDir 'Data\PlaybackEngines\AndroidPlayer\SDK')
    }
    foreach ($v in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)) { if ($v) { $sdkRoots += $v } }
    foreach ($sdk in $sdkRoots) {
        if (-not (Test-Path $sdk)) { continue }
        if ($Name -eq 'adb') {
            $p = Join-Path (Join-Path $sdk 'platform-tools') $exe
            if (Test-Path $p) { return $p }
        } else {
            $bt = Join-Path $sdk 'build-tools'
            if (Test-Path $bt) {
                $hit = Get-ChildItem $bt -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } -Descending -ErrorAction SilentlyContinue |
                    ForEach-Object { Join-Path $_.FullName $exe } | Where-Object { Test-Path $_ } | Select-Object -First 1
                if ($hit) { return $hit }
            }
        }
    }
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

# ---------------------------------------------------------------------------------------------
# Unity batch
# ---------------------------------------------------------------------------------------------

function Test-MetinARUnityProjectLocked {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)
    $lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
    if (-not (Test-Path $lock)) { return $false }
    try {
        $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $fs.Close()
        return $false
    } catch {
        return $true
    }
}

function Invoke-MetinARUnityBatch {
    param(
        [Parameter(Mandatory = $true)][string]$UnityPath,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$LogFile,
        [string]$ExecuteMethod,
        [string[]]$ExtraArgs = @(),
        [switch]$Quit,
        [switch]$NoGraphics,
        [int]$TimeoutMinutes = 30
    )
    $argList = New-Object System.Collections.Generic.List[string]
    $argList.Add('-batchmode')
    if ($NoGraphics) { $argList.Add('-nographics') }
    if ($Quit) { $argList.Add('-quit') }
    $argList.Add('-projectPath'); $argList.Add('"' + $ProjectPath + '"')
    $argList.Add('-logFile'); $argList.Add('"' + $LogFile + '"')
    if ($ExecuteMethod) { $argList.Add('-executeMethod'); $argList.Add($ExecuteMethod) }
    foreach ($a in $ExtraArgs) { $argList.Add($a) }
    New-Item -ItemType Directory -Force -Path (Split-Path $LogFile -Parent) | Out-Null
    $proc = Start-Process -FilePath $UnityPath -ArgumentList ($argList -join ' ') -PassThru -NoNewWindow
    $null = $proc.Handle # Windows PowerShell 5.1: cache the handle so ExitCode is available later
    $finished = $proc.WaitForExit($TimeoutMinutes * 60 * 1000)
    if (-not $finished) {
        try { $proc.Kill() } catch { Write-Verbose "Unity kill after timeout failed: $_" }
        return [pscustomobject]@{ ExitCode = -1; TimedOut = $true; Seconds = ($TimeoutMinutes * 60) }
    }
    $proc.WaitForExit()
    return [pscustomobject]@{ ExitCode = $proc.ExitCode; TimedOut = $false; Seconds = ($proc.ExitTime - $proc.StartTime).TotalSeconds }
}

function Get-MetinARUnityLogSummary {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)
    $errCs = New-Object System.Collections.Generic.HashSet[string]
    $warnCs = New-Object System.Collections.Generic.HashSet[string]
    $exceptions = New-Object System.Collections.Generic.List[string]
    $missingScript = 0
    $fatal = New-Object System.Collections.Generic.List[string]
    foreach ($l in $Lines) {
        if ($null -eq $l) { continue }
        if ($l -match 'error CS\d{4}') { [void]$errCs.Add($l.Trim()) ; continue }
        if ($l -match 'warning CS\d{4}') { [void]$warnCs.Add($l.Trim()); continue }
        if ($l -match '^\s*\w+(\.\w+)*Exception(:|\s|$)' -or $l -match '^(Unhandled|Uncaught) ?[Ee]xception') { $exceptions.Add($l.Trim()) }
        if ($l -match 'The referenced script .* is missing' -or $l -match 'missing script') { $missingScript++ }
        if ($l -match 'Scripts have compiler errors' -or $l -match 'Aborting batchmode due to' -or
            $l -match 'No valid Unity Editor license' -or $l -match 'Error building Player' -or
            $l -match '\[Package Manager\].*[Ee]rror' -or $l -match 'An error occurred while resolving packages') {
            $fatal.Add($l.Trim())
        }
    }
    [pscustomobject]@{
        CompilerErrors     = $errCs.Count
        CompilerWarnings   = $warnCs.Count
        CompilerErrorLines = @($errCs | Select-Object -First 20)
        Exceptions         = $exceptions.Count
        ExceptionLines     = @($exceptions | Select-Object -First 10)
        MissingScriptLogs  = $missingScript
        BlockingLines      = @($fatal | Select-Object -Unique | Select-Object -First 10)
    }
}

function Get-MetinARJUnitSummary {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    [xml]$x = Get-Content -Raw -Path $Path
    $suite = $x.testsuite
    $failed = @()
    foreach ($tc in @($suite.testcase)) {
        if ($null -ne $tc -and $tc.SelectSingleNode('failure')) {
            $failed += ($tc.name + ': ' + $tc.SelectSingleNode('failure').GetAttribute('message'))
        }
    }
    [pscustomobject]@{
        Name     = [string]$suite.name
        Tests    = [int]$suite.tests
        Failures = [int]$suite.failures
        Failed   = $failed
    }
}

# ---------------------------------------------------------------------------------------------
# APK inspection (pure parsers + wrappers)
# ---------------------------------------------------------------------------------------------

function ConvertFrom-MetinARAaptBadging {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Lines)
    $info = [ordered]@{
        Package = $null; VersionName = $null; VersionCode = $null; MinSdk = $null; TargetSdk = $null
        Debuggable = $false; NativeCode = @(); LaunchableActivity = $null; Permissions = @()
    }
    $perms = New-Object System.Collections.Generic.List[string]
    foreach ($l in $Lines) {
        if ($l -match "^package: name='([^']+)'") {
            $info.Package = $Matches[1]
            if ($l -match "versionCode='([^']*)'") { $info.VersionCode = $Matches[1] }
            if ($l -match "versionName='([^']*)'") { $info.VersionName = $Matches[1] }
        } elseif ($l -match "^(minSdkVersion|sdkVersion):'(\d+)'") { $info.MinSdk = [int]$Matches[2] }
        elseif ($l -match "^targetSdkVersion:'(\d+)'") { $info.TargetSdk = [int]$Matches[1] }
        elseif ($l -match '^application-debuggable') { $info.Debuggable = $true }
        elseif ($l -match '^native-code:\s*(.*)$') { $info.NativeCode = @([regex]::Matches($Matches[1], "'([^']+)'") | ForEach-Object { $_.Groups[1].Value }) }
        elseif ($l -match "^launchable-activity: name='([^']+)'") { $info.LaunchableActivity = $Matches[1] }
        elseif ($l -match "^uses-permission(-sdk-23)?: name='([^']+)'") { if (-not $perms.Contains($Matches[2])) { $perms.Add($Matches[2]) } }
    }
    $info.Permissions = @($perms)
    return [pscustomobject]$info
}

function Test-MetinARPermissionPolicy {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Permissions,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Forbidden,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Allowed
    )
    $bad = @($Permissions | Where-Object { $Forbidden -contains $_ })
    $unexpected = @($Permissions | Where-Object { ($Forbidden -notcontains $_) -and ($Allowed -notcontains $_) })
    $status = 'PASS'
    if ($unexpected.Count -gt 0) { $status = 'WARN' }
    if ($bad.Count -gt 0) { $status = 'FAIL' }
    [pscustomobject]@{
        Status                   = $status
        Forbidden                = $bad
        Unexpected               = $unexpected
        BackgroundLocationAbsent = ($Permissions -notcontains 'android.permission.ACCESS_BACKGROUND_LOCATION')
    }
}

function Get-MetinARZipEntries {
    param([Parameter(Mandatory = $true)][string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path).Path)
    try { return @($zip.Entries | ForEach-Object { $_.FullName }) } finally { $zip.Dispose() }
}

function Get-MetinARApkReport {
    param([Parameter(Mandatory = $true)][string]$ApkPath, [string]$AaptPath)
    $item = Get-Item $ApkPath
    $hash = (Get-FileHash -Algorithm SHA256 -Path $ApkPath).Hash.ToLowerInvariant()
    $entries = Get-MetinARZipEntries -Path $ApkPath
    $abis = @($entries | Where-Object { $_ -match '^lib/([^/]+)/.+\.so$' } | ForEach-Object { ($_ -split '/')[1] } | Select-Object -Unique)
    $badging = $null
    if ($AaptPath) {
        $out = & $AaptPath dump badging $ApkPath 2>&1 | ForEach-Object { "$_" }
        $badging = ConvertFrom-MetinARAaptBadging -Lines @($out)
    }
    [pscustomobject]@{
        Path           = $item.FullName
        SizeBytes      = $item.Length
        SizeMB         = [math]::Round($item.Length / 1MB, 2)
        Sha256         = $hash
        Abis           = $abis
        HasIl2Cpp      = [bool](@($entries | Where-Object { $_ -match '^lib/[^/]+/libil2cpp\.so$' }).Count)
        HasGlobalMetadata = [bool](@($entries | Where-Object { $_ -match 'global-metadata\.dat$' }).Count)
        HasMonoAssemblies = [bool](@($entries | Where-Object { $_ -match '^assets/bin/Data/Managed/.+\.dll$' }).Count)
        Badging        = $badging
    }
}

# ---------------------------------------------------------------------------------------------
# Privacy redaction (logcat, dumpsys, Unity logs before they leave the machine)
# ---------------------------------------------------------------------------------------------

function ConvertTo-MetinARRedactedText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines,
        [string[]]$Secrets = @(),
        # Keep long hex strings (SHA-256, git SHAs) - for QA reports that contain no device identifiers.
        [switch]$KeepHex
    )
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines) {
        if ($null -eq $line) { continue }
        $t = $line
        foreach ($s in $Secrets) { if ($s -and $s.Length -ge 3) { $t = $t.Replace($s, '<redacted-id>') } }
        # e-mail addresses
        $t = [regex]::Replace($t, '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}', '<email>')
        # MAC addresses
        $t = [regex]::Replace($t, '\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b', '<mac>')
        # IPv4 (keep 0.0.0.0 and 127.0.0.1 readable)
        $t = [regex]::Replace($t, '\b(?!0\.0\.0\.0\b)(?!127\.0\.0\.1\b)(\d{1,3}\.){3}\d{1,3}(:\d+)?\b', '<ip>')
        # IPv6 (full 8-group form or compressed '::' form; logcat hh:mm:ss times are not touched)
        $t = [regex]::Replace($t, '\b([0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}\b', '<ip6>')
        $t = [regex]::Replace($t, '(?<![:\w])([0-9A-Fa-f]{1,4}:){1,6}:([0-9A-Fa-f]{1,4}(:[0-9A-Fa-f]{1,4})*)?(?![:\w])', '<ip6>')
        # Coordinates: keyed values (lat=, latitude:, lon=, lng=, longitude:)
        $t = [regex]::Replace($t, '(?i)\b(lat|latitude|lon|lng|long|longitude)(\s*[=:]\s*)-?\d{1,3}\.\d+', '$1$2<coord>')
        # Coordinates: bare decimal pairs with >= 3 decimals, e.g. "52.22977,21.01178" or "52.2297 21.0122"
        $t = [regex]::Replace($t, '-?\b\d{1,3}\.\d{3,}\s*[,; ]\s*-?\d{1,3}\.\d{3,}\b', '<coord-pair>')
        # IMEI / 15-digit identifiers
        if (-not $KeepHex) { $t = [regex]::Replace($t, '\b\d{15}\b', '<imei>') }
        # Android ID / 16-hex identifiers and long hex tokens (>= 16)
        if (-not $KeepHex) { $t = [regex]::Replace($t, '\b[0-9a-fA-F]{16,}\b', '<hex-id>') }
        # serial=..., ro.serialno, android_id=...
        $t = [regex]::Replace($t, '(?i)\b(serial(no)?|android_id|device_id|ro\.serialno|ro\.boot\.serialno)(\s*[=:\]]\s*\[?)[A-Za-z0-9._-]+', '$1$3<redacted-id>')
        # Local user paths
        $t = [regex]::Replace($t, '(?i)([A-Z]:\\Users\\)[^\\/\s"]+', '$1<user>')
        $t = [regex]::Replace($t, '(/home/|/Users/)[^/\s"]+', '$1<user>')
        $out.Add($t)
    }
    return , $out.ToArray()
}

# ---------------------------------------------------------------------------------------------
# Device log / dumpsys parsers (pure)
# ---------------------------------------------------------------------------------------------

function Find-MetinARLogcatProblems {
    <#
      Detects crash / ANR / fatal markers for the app in `adb logcat -v threadtime` output.
      With -Package (and optionally -Pids) only markers that belong to the app are counted, so unrelated system
      processes do not fail the gate. Unity error/exception lines from the app are reported as WARN.
    #>
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines,
        [string]$Package,
        [int[]]$Pids = @()
    )
    $crash = New-Object System.Collections.Generic.List[string]
    $anr = New-Object System.Collections.Generic.List[string]
    $fatal = New-Object System.Collections.Generic.List[string]
    $unityErrors = New-Object System.Collections.Generic.List[string]
    $pkg = if ($Package) { [regex]::Escape($Package) } else { $null }
    foreach ($l in $Lines) {
        if ($null -eq $l) { continue }
        $linePid = $null; $level = $null
        if ($l -match '^\d\d-\d\d \S+\s+(\d+)\s+\d+ ([VDIWEFA]) ') { $linePid = [int]$Matches[1]; $level = $Matches[2] }
        elseif ($l -match '^([VDIWEFA])/[^(]*\(\s*(\d+)\)') { $level = $Matches[1]; $linePid = [int]$Matches[2] }
        $ours = ($null -ne $linePid -and $Pids -contains $linePid) -or ($pkg -and $l -match $pkg)
        if (-not $pkg) { $ours = $true }

        $isCrash = $false
        if ($pkg) {
            if ($l -match ('Process: ' + $pkg + ', PID') -or $l -match ('>>> ' + $pkg + ' <<<') -or
                ($l -match '\bam_crash\b' -and $l -match $pkg)) { $isCrash = $true }
        } elseif ($l -match 'FATAL EXCEPTION' -or $l -match '\bam_crash\b' -or $l -match '\*\*\* \*\*\* \*\*\*') { $isCrash = $true }
        if ($ours -and ($l -match 'FATAL EXCEPTION' -or $l -match 'Fatal signal \d+' -or $l -match 'Abort message:' -or
                        $l -match '\bCRASH\b')) { $isCrash = $true }
        if ($isCrash) { $crash.Add($l) }

        if (($pkg -and ($l -match ('ANR in ' + $pkg) -or ($l -match '\bam_anr\b' -and $l -match $pkg))) -or
            (-not $pkg -and ($l -match '\bANR in\b' -or $l -match '\bam_anr\b'))) { $anr.Add($l) }

        if ($level -eq 'F' -and $ours) { $fatal.Add($l) }
        if ($ours -and ($l -match ' E Unity\s*:' -or $l -match '^E/Unity' -or $l -match 'Unity\s*:\s*\w*Exception')) { $unityErrors.Add($l) }
    }
    $status = 'PASS'
    if ($unityErrors.Count -gt 0) { $status = 'WARN' }
    if ($crash.Count -gt 0 -or $anr.Count -gt 0 -or $fatal.Count -gt 0) { $status = 'FAIL' }
    [pscustomobject]@{
        Status      = $status
        Crashes     = @($crash | Select-Object -First 20)
        Anrs        = @($anr | Select-Object -First 20)
        FatalLines  = @($fatal | Select-Object -First 20)
        UnityErrors = @($unityErrors | Select-Object -First 20)
    }
}

function Get-MetinARPerfSamples {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines)
    $samples = New-Object System.Collections.Generic.List[object]
    foreach ($l in $Lines) {
        if ($null -eq $l) { continue }
        $idx = $l.IndexOf('[MetinARPerf] v=1 ')
        if ($idx -lt 0) { continue }
        $payload = $l.Substring($idx + '[MetinARPerf] v=1 '.Length).Trim()
        if ($payload -match '^event=') { continue }
        $h = [ordered]@{}
        foreach ($tok in ($payload -split '\s+')) {
            $kv = $tok -split '=', 2
            if ($kv.Count -ne 2) { continue }
            $num = 0.0
            if ([double]::TryParse($kv[1], [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$num)) {
                $h[$kv[0]] = $num
            } else {
                $h[$kv[0]] = $kv[1]
            }
        }
        if ($h.Contains('scene')) { $samples.Add([pscustomobject]$h) }
    }
    return , $samples.ToArray()
}

function Get-MetinARProp {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $p = $Object.PSObject.Properties[$Name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Test-MetinARPerfBudget {
    <#
      Evaluates [MetinARPerf] samples of one scene against a scene budget from perf-budgets.json.
      The first sample (warm-up window) is skipped when more than one sample exists.
      Returns PASS / FAIL / UNVERIFIED (no samples).
    #>
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Samples,
        [Parameter(Mandatory = $true)]$Budget,
        [string]$BudgetName = 'scene'
    )
    $checks = New-Object System.Collections.Generic.List[object]
    $use = @($Samples)
    if ($use.Count -gt 1) { $use = @($use | Select-Object -Skip 1) }
    if ($use.Count -eq 0) {
        return [pscustomobject]@{ Name = $BudgetName; Status = 'UNVERIFIED'; Windows = 0; Checks = @(); Detail = 'no [MetinARPerf] samples' }
    }

    function Add-Check([string]$metric, [string]$op, $limit, [double[]]$values) {
        if ($null -eq $limit) { return }
        $values = @($values | Where-Object { $null -ne $_ })
        if ($values.Count -eq 0) {
            $checks.Add([pscustomobject]@{ Metric = $metric; Limit = "$op $limit"; Observed = 'n/a'; Status = 'UNVERIFIED' })
            return
        }
        $observed = if ($op -eq '>=') { ($values | Measure-Object -Minimum).Minimum } else { ($values | Measure-Object -Maximum).Maximum }
        $ok = if ($op -eq '>=') { $observed -ge [double]$limit } else { $observed -le [double]$limit }
        $checks.Add([pscustomobject]@{ Metric = $metric; Limit = "$op $limit"; Observed = [math]::Round($observed, 2); Status = $(if ($ok) { 'PASS' } else { 'FAIL' }) })
    }
    function Values([string]$key) { return [double[]]@($use | ForEach-Object { Get-MetinARProp $_ $key } | Where-Object { $null -ne $_ -and $_ -isnot [string] }) }

    Add-Check 'fpsAvg (worst window)' '>=' (Get-MetinARProp $Budget 'minFpsAvg') (Values 'fpsAvg')
    Add-Check 'frameP95Ms (worst window)' '<=' (Get-MetinARProp $Budget 'maxFrameP95Ms') (Values 'frameP95Ms')
    Add-Check 'frameSpikes>50ms per window' '<=' (Get-MetinARProp $Budget 'maxFrameSpikesPerWindow') (Values 'frameSpikes')
    Add-Check 'mainP95Ms (worst window)' '<=' (Get-MetinARProp $Budget 'maxMainP95Ms') (Values 'mainP95Ms')
    Add-Check 'totalUsedMB (peak)' '<=' (Get-MetinARProp $Budget 'maxTotalUsedMB') (Values 'totalUsedMB')
    $lat = Get-MetinARProp $Budget 'latencyMetric'
    if ($lat) {
        Add-Check "$lat.avgMs (worst window)" '<=' (Get-MetinARProp $Budget 'maxLatencyAvgMs') (Values "$lat.avgMs")
        Add-Check "$lat.p95Ms (worst window)" '<=' (Get-MetinARProp $Budget 'maxLatencyP95Ms') (Values "$lat.p95Ms")
    }
    $st = @($checks | ForEach-Object { $_.Status })
    $status = 'PASS'
    if ($st -contains 'UNVERIFIED') { $status = 'UNVERIFIED' }
    if ($st -contains 'FAIL') { $status = 'FAIL' }
    [pscustomobject]@{ Name = $BudgetName; Status = $status; Windows = $use.Count; Checks = $checks.ToArray(); Detail = '' }
}

function ConvertFrom-MetinARMeminfo {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines)
    $r = [ordered]@{ TotalPssKB = $null; TotalRssKB = $null; JavaHeapKB = $null; NativeHeapKB = $null; GraphicsKB = $null }
    foreach ($l in $Lines) {
        if ($l -match '^\s*TOTAL PSS:\s*(\d+)') { $r.TotalPssKB = [long]$Matches[1]; if ($l -match 'TOTAL RSS:\s*(\d+)') { $r.TotalRssKB = [long]$Matches[1] } }
        elseif ($l -match '^\s*TOTAL\s+(\d+)\s' -and $null -eq $r.TotalPssKB) { $r.TotalPssKB = [long]$Matches[1] }
        elseif ($l -match '^\s*Java Heap:\s*(\d+)') { $r.JavaHeapKB = [long]$Matches[1] }
        elseif ($l -match '^\s*Native Heap:\s*(\d+)') { $r.NativeHeapKB = [long]$Matches[1] }
        elseif ($l -match '^\s*Graphics:\s*(\d+)') { $r.GraphicsKB = [long]$Matches[1] }
    }
    return [pscustomobject]$r
}

function ConvertFrom-MetinARSurfaceFlingerLatency {
    <#
      Parses `dumpsys SurfaceFlinger --latency <layer>` output: first line = refresh period (ns), then up to 128 rows
      "desiredPresent actualPresent frameReady" (ns). Uses actualPresent deltas. Approximate, display-side only.
    #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines)
    $refreshNs = $null
    $times = New-Object System.Collections.Generic.List[double]
    foreach ($l in $Lines) {
        $t = $l.Trim()
        if (-not $t) { continue }
        $parts = $t -split '\s+'
        if ($null -eq $refreshNs -and $parts.Count -eq 1) { [double]$tmp = 0; if ([double]::TryParse($parts[0], [ref]$tmp)) { $refreshNs = $tmp }; continue }
        if ($parts.Count -ge 3) {
            [double]$a = 0
            if ([double]::TryParse($parts[1], [ref]$a) -and $a -gt 0 -and $a -lt 9.2e18) { $times.Add($a) }
        }
    }
    $deltas = New-Object System.Collections.Generic.List[double]
    for ($i = 1; $i -lt $times.Count; $i++) { $d = ($times[$i] - $times[$i - 1]) / 1e6; if ($d -gt 0) { $deltas.Add($d) } }
    if ($deltas.Count -eq 0) {
        return [pscustomobject]@{ RefreshMs = $(if ($refreshNs) { [math]::Round($refreshNs / 1e6, 2) } else { $null }); Frames = 0; FpsAvg = $null; FrameAvgMs = $null; FrameMaxMs = $null; Janks = 0 }
    }
    $avg = ($deltas | Measure-Object -Average).Average
    $max = ($deltas | Measure-Object -Maximum).Maximum
    $refreshMs = if ($refreshNs) { $refreshNs / 1e6 } else { 16.67 }
    $janks = @($deltas | Where-Object { $_ -gt (2 * $refreshMs) }).Count
    [pscustomobject]@{
        RefreshMs  = [math]::Round($refreshMs, 2)
        Frames     = $deltas.Count
        FpsAvg     = [math]::Round(1000 / $avg, 1)
        FrameAvgMs = [math]::Round($avg, 2)
        FrameMaxMs = [math]::Round($max, 2)
        Janks      = $janks
    }
}

function ConvertFrom-MetinARGrantedPermissions {
    <# Parses runtime permission state for the package section of `dumpsys package <pkg>`. #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines)
    $map = [ordered]@{}
    foreach ($l in $Lines) {
        if ($l -match '^\s*(android\.permission\.[A-Z_]+|[a-z0-9_.]+\.permission\.[A-Za-z0-9_]+): granted=(true|false)') {
            $map[$Matches[1]] = ($Matches[2] -eq 'true')
        }
    }
    return [pscustomobject]$map
}

function ConvertFrom-MetinARCpuInfo {
    <# Parses the app's line from `dumpsys cpuinfo` (recent-window CPU %, may exceed 100 on multi-core). #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines, [Parameter(Mandatory = $true)][string]$Package)
    foreach ($l in $Lines) {
        if ($l -match ('^\s*([\d.]+)%\s+\d+/' + [regex]::Escape($Package) + ':\s*(.*)$')) {
            $total = [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
            $rest = $Matches[2]
            $user = if ($rest -match '([\d.]+)% user') { [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture) } else { $null }
            $kernel = if ($rest -match '([\d.]+)% kernel') { [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture) } else { $null }
            return [pscustomobject]@{ CpuPercent = $total; UserPercent = $user; KernelPercent = $kernel }
        }
    }
    return [pscustomobject]@{ CpuPercent = $null; UserPercent = $null; KernelPercent = $null }
}

function ConvertFrom-MetinARThermal {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$ThermalLines, [AllowEmptyString()][AllowEmptyCollection()][string[]]$BatteryLines = @())
    $status = $null; $level = $null; $tempC = $null
    foreach ($l in $ThermalLines) { if ($l -match 'Thermal Status:\s*(\d+)') { $status = [int]$Matches[1]; break } }
    foreach ($l in $BatteryLines) {
        if ($l -match '^\s*level:\s*(\d+)') { $level = [int]$Matches[1] }
        elseif ($l -match '^\s*temperature:\s*(\d+)') { $tempC = [double]$Matches[1] / 10 }
    }
    [pscustomobject]@{ ThermalStatus = $status; BatteryLevel = $level; BatteryTempC = $tempC }
}

# ---------------------------------------------------------------------------------------------
# Reports
# ---------------------------------------------------------------------------------------------

function ConvertTo-MetinARMarkdownReport {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Results,
        [hashtable]$Header = @{},
        [string[]]$Notes = @()
    )
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# $Title")
    [void]$sb.AppendLine('')
    foreach ($k in ($Header.Keys | Sort-Object)) { [void]$sb.AppendLine("- **$($k):** $($Header[$k])") }
    [void]$sb.AppendLine("- **Overall:** $(Get-MetinAROverallStatus -Results $Results)")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Gate | Status | Time (s) | Detail |')
    [void]$sb.AppendLine('|---|---|---:|---|')
    foreach ($r in $Results) {
        $d = ($r.Detail -replace '\|', '/' -replace "`r?`n", ' ')
        [void]$sb.AppendLine("| $($r.Name) | **$($r.Status)** | $($r.Seconds) | $d |")
    }
    $withEvidence = @($Results | Where-Object { $_.Evidence.Count -gt 0 })
    if ($withEvidence.Count -gt 0) {
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('## Evidence')
        foreach ($r in $withEvidence) {
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine("### $($r.Name)")
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine('```text')
            foreach ($e in $r.Evidence) { [void]$sb.AppendLine($e) }
            [void]$sb.AppendLine('```')
        }
    }
    if ($Notes.Count -gt 0) {
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('## Notes')
        foreach ($n in $Notes) { [void]$sb.AppendLine("- $n") }
    }
    return $sb.ToString()
}

Export-ModuleMember -Function *-MetinAR*
