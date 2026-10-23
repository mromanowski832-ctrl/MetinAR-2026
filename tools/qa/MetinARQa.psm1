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

function Find-MetinARConditionalSerializedFields {
    <#
      #31 follow-up (static, engine-free): serialized field declarations inside #if/#elif/#else regions whose condition
      mentions UNITY_EDITOR or DEVELOPMENT_BUILD in RUNTIME scripts. Such fields give the Editor and the player different
      serialization layouts for the same component, so scenes/prefabs serialized in the Editor are read with the wrong
      layout in a player (the same class of failure as CachedReader::OutOfBoundsError). Method bodies under #if are fine.
      Detects [SerializeField] / [SerializeReference] fields and public instance fields (heuristic, line based).
      Returns objects { Line, Condition, Text }.
    #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines)
    $out = New-Object System.Collections.Generic.List[object]
    $stack = New-Object System.Collections.Generic.List[string]
    $attrPending = $false
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $l = [string]$Lines[$i]
        $t = $l.Trim()
        if ($t -match '^#\s*if\s+(.+)$') { $stack.Add($Matches[1]); continue }
        if ($t -match '^#\s*elif\s+(.+)$') { if ($stack.Count) { $stack[$stack.Count - 1] = $stack[$stack.Count - 1] + ' / elif ' + $Matches[1] }; continue }
        if ($t -match '^#\s*else\b') { if ($stack.Count) { $stack[$stack.Count - 1] = $stack[$stack.Count - 1] + ' / else' }; continue }
        if ($t -match '^#\s*endif\b') { if ($stack.Count) { $stack.RemoveAt($stack.Count - 1) }; continue }
        $cond = @($stack | Where-Object { $_ -match '\bUNITY_EDITOR\b|\bDEVELOPMENT_BUILD\b' })
        $isSerializeAttr = $t -match '^\[[^\]]*\bSerialize(Field|Reference)\b[^\]]*\]'
        $attrOnly = $isSerializeAttr -and ($t -match '^\[[^\]]*\]\s*$')
        $hasDecl = $t -match '^(\[[^\]]*\]\s*)*(public|private|protected|internal)?\s*(readonly\s+)?[\w\.<>\[\],\? ]+\s+\w+\s*(=[^;]*)?;\s*(//.*)?$' -and $t -notmatch '\b(static|const|return|using|delegate|event|abstract)\b' -and $t -notmatch '=>|\(|\{'
        $publicField = $hasDecl -and $t -match '^(\[[^\]]*\]\s*)*public\s'
        $serialized = ($isSerializeAttr -and $hasDecl) -or ($attrPending -and $hasDecl) -or $publicField
        if ($cond.Count -and $serialized) { $out.Add([pscustomobject]@{ Line = $i + 1; Condition = ($cond -join ' && '); Text = $t }) }
        if ($attrOnly) { $attrPending = $true } elseif ($t.Length -gt 0 -and $t -notmatch '^\[' -and $t -notmatch '^//') { $attrPending = $false }
    }
    return $out.ToArray()
}

function Test-MetinARApkPlayerData {
    <#
      #31 / METINAR-014: consistency of Unity serialized player data inside an APK (entries directly under
      assets/bin/Data/). A file may be packed whole (sharedassets1.assets) or as 1 MB chunks (.split0..N); the player
      prefers chunks. The crashing 52a1375 APK carried sharedassets1.assets plus stale sharedassets1.assets.split0..14
      from an older Gradle export -> CachedReader::OutOfBoundsError on AR scene load.
      Same rules as AndroidPlayerDataValidator (2ec7a47, feat/fpp-hands-aura-prototype), re-implemented here so the QA
      gate does not depend on the unmerged branch: (1) whole + split of the same file, (2) chunks not contiguous from 0.
      Returns Problems (empty = consistent), WholeFiles, SplitFiles (name -> chunk count).
    #>
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Entries)
    $prefix = 'assets/bin/Data/'
    $whole = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $splits = New-Object 'System.Collections.Generic.SortedDictionary[string,System.Collections.Generic.List[int]]' ([StringComparer]::Ordinal)
    foreach ($raw in $Entries) {
        if ([string]::IsNullOrEmpty($raw)) { continue }
        $name = $raw.Replace('\', '/')
        if (-not $name.StartsWith($prefix, [StringComparison]::Ordinal)) { continue }
        $rel = $name.Substring($prefix.Length)
        if ($rel.Length -eq 0 -or $rel.Contains('/')) { continue }
        if ($rel -cmatch '^(.+)\.split([0-9]+)$') {
            $base = $Matches[1]; $idx = [int]$Matches[2]
            if (-not $splits.ContainsKey($base)) { $splits[$base] = New-Object 'System.Collections.Generic.List[int]' }
            if (-not $splits[$base].Contains($idx)) { $splits[$base].Add($idx) }
        } else {
            [void]$whole.Add($rel)
        }
    }
    $problems = New-Object System.Collections.Generic.List[string]
    $splitInfo = [ordered]@{}
    foreach ($kv in $splits.GetEnumerator()) {
        $idx = @($kv.Value | Sort-Object)
        $splitInfo[$kv.Key] = $idx.Count
        if ($whole.Contains($kv.Key)) {
            $problems.Add("$prefix$($kv.Key) exists both whole and as $($idx.Count) .split chunk(s); the player reads the chunks, which come from an older build")
        }
        for ($i = 0; $i -lt $idx.Count; $i++) {
            if ($idx[$i] -ne $i) { $problems.Add("$prefix$($kv.Key) split chunks are not contiguous: missing .split$i"); break }
        }
    }
    [pscustomobject]@{ Problems = $problems.ToArray(); WholeFiles = @($whole | Sort-Object); SplitFiles = [pscustomobject]$splitInfo }
}

function Get-MetinARZipEntryText {
    <# UTF-8 text of one zip entry, or $null when the entry is absent. #>
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Entry)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path).Path)
    try {
        $e = $zip.GetEntry($Entry)
        if ($null -eq $e) { return $null }
        $r = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
        try { return $r.ReadToEnd() } finally { $r.Dispose() }
    } finally { $zip.Dispose() }
}

function Test-MetinARApkArCore {
    <#
      METINAR-037 / audit UPDATE_1 N-01 (independent of the C# build validator): an Android APK can start ARCore only when
      the Unity.XR.ARCore [RuntimeInitializeOnLoadMethod] registrations (session, camera, planes, ...) are listed in
      assets/bin/Data/RuntimeInitializeOnLoads.json. They are missing when the APK was built while the editor's ACTIVE build
      target was not Android (#31 f194aa9: "Failed to load session subsystem" -> AR unavailable). The JSON is parsed as JSON
      (records under "root"), the rule (assembly, required entries, required Class.Method list) comes from qa-config.json.
      Returns { Status, Found, Required, AssemblyRecords, Missing, Problems }.
    #>
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Entries,
        [AllowNull()][AllowEmptyString()][string]$RuntimeInitializeJson,
        [Parameter(Mandatory = $true)]$Rule
    )
    $names = @{}
    foreach ($n in @($Entries)) { if ($n) { $names[("$n" -replace '\\', '/')] = $true } }
    $problems = New-Object System.Collections.Generic.List[string]
    foreach ($req in @($Rule.requiredEntries)) { if (-not $names.ContainsKey($req)) { $problems.Add("missing $req") } }
    $required = @($Rule.requiredRegistrations)
    $found = 0; $asmRecords = 0; $missing = @()
    $records = $null
    if (-not $names.ContainsKey($Rule.runtimeInitializeEntry) -or [string]::IsNullOrEmpty($RuntimeInitializeJson)) {
        $problems.Add("missing $($Rule.runtimeInitializeEntry)")
        $missing = $required
    } else {
        try { $records = @((ConvertFrom-Json -InputObject $RuntimeInitializeJson -ErrorAction Stop).root) } catch { $records = $null }
        if ($null -eq $records) {
            $problems.Add("$($Rule.runtimeInitializeEntry) is not valid JSON with a 'root' array")
            $missing = $required
        } else {
            $have = @{}
            foreach ($r in $records) {
                if ($null -eq $r -or -not $r.PSObject.Properties['assemblyName']) { continue }
                if ([string]$r.assemblyName -cne [string]$Rule.assembly) { continue }
                $asmRecords++
                $have[("{0}.{1}" -f $r.className, $r.methodName)] = $true
            }
            foreach ($q in $required) { if ($have.ContainsKey($q)) { $found++ } else { $missing += $q } }
            if ($missing.Count) {
                $problems.Add(("{0}: {1}/{2} {3} registrations (missing: {4}) - ARCore subsystems will not load ('AR unavailable'); build with the active target Android (-buildTarget Android)" -f $Rule.runtimeInitializeEntry, $found, $required.Count, $Rule.assembly, ($missing -join ', ')))
            }
        }
    }
    [pscustomobject]@{
        Status          = $(if ($problems.Count) { 'FAIL' } else { 'PASS' })
        Found           = $found
        Required        = $required.Count
        AssemblyRecords = $asmRecords
        Missing         = @($missing)
        Problems        = @($problems)
    }
}

function Resolve-MetinARExpectedPackages {
    <# Which applicationIdentifier ApkAudit accepts: -ExpectedPackage pins one id; -ApkPurpose (qa/game/mappoc) maps through
       androidBuild.packagesByPurpose; otherwise the runner's own build (expectedPackage) or, for an external -ApkPath, the
       auditAcceptedPackages list. #>
    param([Parameter(Mandatory = $true)]$AndroidBuild, [string]$ExpectedPackage, [string]$ApkPurpose, [switch]$ExternalApk)
    if ($ExpectedPackage) { return @($ExpectedPackage) }
    if ($ApkPurpose) {
        $map = $AndroidBuild.packagesByPurpose
        if ($null -eq $map -or -not $map.PSObject.Properties[$ApkPurpose]) { throw "unknown -ApkPurpose '$ApkPurpose' (qa-config androidBuild.packagesByPurpose)" }
        return @([string]$map.$ApkPurpose)
    }
    if ($ExternalApk) { return @($AndroidBuild.auditAcceptedPackages) }
    return @([string]$AndroidBuild.expectedPackage)
}

function Get-MetinARApkReport {
    param([Parameter(Mandatory = $true)][string]$ApkPath, [string]$AaptPath, $ArCoreRule)
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
        PlayerData     = Test-MetinARApkPlayerData -Entries @($entries)
        ArCore         = $(if ($ArCoreRule) { Test-MetinARApkArCore -Entries @($entries) -RuntimeInitializeJson (Get-MetinARZipEntryText -Path $ApkPath -Entry $ArCoreRule.runtimeInitializeEntry) -Rule $ArCoreRule } else { $null })
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
      processes do not fail the gate. Unity error/exception lines from the app are reported as WARN, except:
      a Unity exception whose message or stack (the continuation lines of the same pid/tid/Unity tag) contains a
      MetinAR. frame is a product exception -> MetinARExceptions -> FAIL (lead decision 2026-10-21 15:28, #66/#33;
      the Editor-only T15 classifier never applies on the device).
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
    $metinarExceptions = New-Object System.Collections.Generic.List[string]
    $pkg = if ($Package) { [regex]::Escape($Package) } else { $null }
    # Unity exception block: header '<Type>Exception: ...' on a Unity line, then the stack on the following Unity lines
    # of the same pid/tid. Ends at '(Filename: ...)', an empty message, another header or a line of another thread/tag.
    $block = $null
    $closeBlock = {
        if ($null -ne $block -and $block.MetinAR) { $metinarExceptions.Add(($block.Header + ' | ' + $block.Frame)) }
    }
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

        # --- MetinAR exception blocks
        $unityKey = $null; $msg = $null
        if ($l -match '^\d\d-\d\d \S+\s+(\d+)\s+(\d+) [VDIWEFA] Unity\s*: ?(.*)$') { $unityKey = $Matches[1] + '/' + $Matches[2]; $msg = $Matches[3] }
        elseif ($l -match '^[VDIWEFA]/Unity\s*\(\s*(\d+)\): ?(.*)$') { $unityKey = $Matches[1]; $msg = $Matches[2] }
        $isHeader = $ours -and $null -ne $unityKey -and $msg -cmatch '^\s*(Rethrow as )?([A-Za-z_][\w.]*\.)?\w*Exception\b'
        $continues = $null -ne $block -and $unityKey -eq $block.Key -and -not $isHeader -and
                     $msg.Trim().Length -gt 0 -and $msg -notmatch '^\s*\(Filename:'
        if ($continues -or ($isHeader -and $null -ne $block -and $unityKey -eq $block.Key -and $msg -cmatch '^\s*Rethrow as ')) {
            if (-not $block.MetinAR -and $msg -cmatch '(?<![\w.])MetinAR\.[A-Za-z_]') { $block.MetinAR = $true; $block.Frame = $msg.Trim() }
            continue
        }
        & $closeBlock
        $block = $null
        if ($isHeader) {
            $block = @{ Key = $unityKey; Header = $l; MetinAR = $false; Frame = '' }
            if ($msg -cmatch '(?<![\w.])MetinAR\.[A-Za-z_]') { $block.MetinAR = $true; $block.Frame = $msg.Trim() }
        }
    }
    & $closeBlock
    $status = 'PASS'
    if ($unityErrors.Count -gt 0) { $status = 'WARN' }
    if ($crash.Count -gt 0 -or $anr.Count -gt 0 -or $fatal.Count -gt 0 -or $metinarExceptions.Count -gt 0) { $status = 'FAIL' }
    [pscustomobject]@{
        Status      = $status
        Crashes     = @($crash | Select-Object -First 20)
        Anrs        = @($anr | Select-Object -First 20)
        FatalLines  = @($fatal | Select-Object -First 20)
        UnityErrors = @($unityErrors | Select-Object -First 20)
        MetinARExceptions = @($metinarExceptions | Select-Object -First 20)
    }
}

function Get-MetinARTestProjects {
    <#
      METINAR-014 branch-aware test discovery. Returns one object per test project:
        Path (repo-relative, forward slashes), Required (listed in testProjects), Exists.
      Required projects are always returned (a missing one is a FAIL for the caller). Optional projects come from
      optionalTestProjectGlobs ('dir/*/file' patterns, '*' matches one path segment) and only when they exist on the
      current branch, so MapCore.Tests (008B), hand-tracking tests (007) or server tests are picked up when merged
      without being required before then. Duplicates are removed; order: required first, then optional sorted.
    #>
    param([Parameter(Mandatory = $true)][string]$RepoRoot, [Parameter(Mandatory = $true)]$Config)
    $out = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    foreach ($p in @(Get-MetinARProp $Config 'testProjects')) {
        if (-not $p) { continue }
        $rel = ([string]$p).Replace('\', '/')
        if ($seen.ContainsKey($rel.ToLowerInvariant())) { continue }
        $seen[$rel.ToLowerInvariant()] = $true
        $out.Add([pscustomobject]@{ Path = $rel; Required = $true; Exists = (Test-Path -LiteralPath (Join-Path $RepoRoot $rel)) })
    }
    $optional = New-Object System.Collections.Generic.List[string]
    foreach ($g in @(Get-MetinARProp $Config 'optionalTestProjectGlobs')) {
        if (-not $g) { continue }
        $parts = ([string]$g).Replace('\', '/').Split('/')
        $current = @($RepoRoot)
        for ($i = 0; $i -lt $parts.Count; $i++) {
            $last = ($i -eq $parts.Count - 1)
            $next = @()
            foreach ($dir in $current) {
                if (-not (Test-Path -LiteralPath $dir -PathType Container)) { continue }
                if ($parts[$i] -match '[\*\?]') {
                    $items = if ($last) { @(Get-ChildItem -LiteralPath $dir -File -Filter $parts[$i] -ErrorAction SilentlyContinue) }
                             else { @(Get-ChildItem -LiteralPath $dir -Directory -Filter $parts[$i] -ErrorAction SilentlyContinue) }
                    $next += @($items | ForEach-Object { $_.FullName })
                } else {
                    $cand = Join-Path $dir $parts[$i]
                    if (($last -and (Test-Path -LiteralPath $cand -PathType Leaf)) -or (-not $last -and (Test-Path -LiteralPath $cand -PathType Container))) { $next += $cand }
                }
            }
            $current = $next
        }
        $rootFull = (Resolve-Path -LiteralPath $RepoRoot).Path.TrimEnd('\', '/')
        foreach ($f in $current) {
            $rel = $f.Substring($rootFull.Length).TrimStart('\', '/').Replace('\', '/')
            if (-not $seen.ContainsKey($rel.ToLowerInvariant())) { $seen[$rel.ToLowerInvariant()] = $true; $optional.Add($rel) }
        }
    }
    foreach ($rel in @($optional | Sort-Object)) { $out.Add([pscustomobject]@{ Path = $rel; Required = $false; Exists = $true }) }
    return $out.ToArray()
}

function Get-MetinARSmokeInjectionVerdict {
    <#
      METINAR-014 negative gate check for the AR smoke: given the injection mode and the JUnit summary of that run,
      decide whether the gate behaved correctly. metinar/unknown must make T15 fail (and nothing else may fail);
      searchdb (the known Unity Search startup signature) must leave the whole smoke green.
    #>
    param([Parameter(Mandatory = $true)][string]$Mode, $JUnit)
    if ($null -eq $JUnit) { return [pscustomobject]@{ Mode = $Mode; Ok = $false; Detail = 'no JUnit results' } }
    $failed = @($JUnit.Failed)
    $t15 = @($failed | Where-Object { $_ -match '^T15_' })
    $other = @($failed | Where-Object { $_ -notmatch '^T15_' })
    switch ($Mode) {
        { $_ -eq 'metinar' -or $_ -eq 'unknown' } {
            $ok = ($t15.Count -eq 1 -and $other.Count -eq 0 -and $t15[0] -match 'injected')
            return [pscustomobject]@{ Mode = $Mode; Ok = $ok; Detail = $(if ($ok) { 'T15 FAILED as expected' } else { "expected only T15 to fail on the injected exception; failed: $($failed -join ' | ')" }) }
        }
        'searchdb' {
            $ok = ($failed.Count -eq 0 -and $JUnit.Tests -gt 0)
            return [pscustomobject]@{ Mode = $Mode; Ok = $ok; Detail = $(if ($ok) { 'known signature classified as noise, smoke PASS' } else { "expected PASS; failed: $($failed -join ' | ')" }) }
        }
        default { return [pscustomobject]@{ Mode = $Mode; Ok = $false; Detail = "unknown injection mode '$Mode'" } }
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
    # METINAR-014: any number of latency metrics, e.g. lat.hand.inference, lat.map.tile.decode, lat.map.tile.mesh.
    # Optional entries (required=false) are skipped when the build never reports them (feature not on this branch).
    foreach ($m in @(Get-MetinARProp $Budget 'latencyMetrics')) {
        if ($null -eq $m) { continue }
        $name = [string](Get-MetinARProp $m 'metric')
        if (-not $name) { continue }
        $key = if ($name.StartsWith('lat.')) { $name } else { 'lat.' + $name }
        $present = (@(Values "$key.avgMs").Count + @(Values "$key.p95Ms").Count) -gt 0
        if (-not $present -and (Get-MetinARProp $m 'required') -eq $false) { continue }
        Add-Check "$key.avgMs (worst window)" '<=' (Get-MetinARProp $m 'maxAvgMs') (Values "$key.avgMs")
        Add-Check "$key.p95Ms (worst window)" '<=' (Get-MetinARProp $m 'maxP95Ms') (Values "$key.p95Ms")
    }
    $st = @($checks | ForEach-Object { $_.Status })
    $status = 'PASS'
    if ($st.Count -eq 0 -or $st -contains 'UNVERIFIED') { $status = 'UNVERIFIED' }   # no measurable metric is never a PASS
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

# --- METINAR-037 (#73): adb connection state, install guard, package-scoped permissions --------------------------------

function ConvertFrom-MetinARAdbDevices {
    <# Parses `adb devices` output. State: device | offline | unauthorized | missing | multiple (no -Serial and
       more than one attached). Daemon start-up chatter and the header are ignored. Serials are returned for
       redaction only and must never be written to a report. #>
    param(
        [AllowEmptyCollection()][string[]]$Lines = @(),
        [string]$Serial
    )
    $entries = @()
    foreach ($l in @($Lines)) {
        if ("$l" -match '^(\S+)\s+(device|offline|unauthorized|recovery|sideload|bootloader|no permissions.*)$') {
            $entries += [pscustomobject]@{ Serial = $Matches[1]; State = $Matches[2] }
        }
    }
    $pool = @($entries)
    if ($Serial) { $pool = @($entries | Where-Object { $_.Serial -eq $Serial }) }
    $ready = @($pool | Where-Object { $_.State -eq 'device' })
    $state = 'missing'
    if ($ready.Count -gt 1) { $state = 'multiple' }
    elseif ($ready.Count -eq 1) { $state = 'device' }
    elseif ($pool.Count -ge 1) { $state = [string]$pool[0].State }
    if ($state -like 'no permissions*') { $state = 'unauthorized' }
    [pscustomobject]@{
        State   = $state
        Count   = @($entries).Count
        Serials = @(@($entries) | ForEach-Object { $_.Serial })
    }
}

function Test-MetinARLogcatCapture {
    <#
      Decides whether an `adb logcat -d` dump can be used as crash evidence. Kind:
        Ok              - adb exit 0, non-empty dump with at least one line of the app (pid in -Pids or the package name);
        Lost            - adb reports the device gone (see Test-MetinARAdbOutputLost);
        CollectionError - adb exited non-zero, the dump is empty / whitespace only, or it has no line of the app.
      Anything but Ok must never be reported as "no crash" (PASS): the caller reports UNVERIFIED (or FAIL when the
      partial dump still shows a crash).
    #>
    param(
        [AllowNull()][AllowEmptyCollection()][string[]]$Lines = @(),
        [AllowNull()][object]$ExitCode = 0,
        [string]$Package,
        [int[]]$Pids = @()
    )
    $all = @($Lines | Where-Object { $null -ne $_ })
    if (Test-MetinARAdbOutputLost -Lines $all) { return [pscustomobject]@{ Kind = 'Lost'; Reason = 'adb reports the device is gone' } }
    if ($null -eq $ExitCode -or "$ExitCode" -notmatch '^-?\d+$') { return [pscustomobject]@{ Kind = 'CollectionError'; Reason = 'adb logcat exit code unknown' } }
    if ([int]"$ExitCode" -ne 0) { return [pscustomobject]@{ Kind = 'CollectionError'; Reason = ("adb logcat exited with code {0}" -f $ExitCode) } }
    $content = @($all | Where-Object { "$_".Trim().Length -gt 0 })
    if ($content.Count -eq 0) { return [pscustomobject]@{ Kind = 'CollectionError'; Reason = 'empty logcat dump' } }
    $pkg = if ($Package) { [regex]::Escape($Package) } else { $null }
    $app = @($content | Where-Object {
        $l = "$_"; $hit = $false
        if ($l -match '^\d\d-\d\d \S+\s+(\d+)\s' -and $Pids -contains [int]$Matches[1]) { $hit = $true }
        elseif ($l -match '^[VDIWEFA]/[^(]*\(\s*(\d+)\)' -and $Pids -contains [int]$Matches[1]) { $hit = $true }
        if (-not $hit -and $pkg -and $l -match ('(?<![\w.])' + $pkg + '(?![\w.])')) { $hit = $true }
        $hit
    })
    if ($app.Count -eq 0) { return [pscustomobject]@{ Kind = 'CollectionError'; Reason = ("logcat dump ({0} lines) has no line of {1} (pids {2})" -f $content.Count, $Package, ($Pids -join ',')) } }
    return [pscustomobject]@{ Kind = 'Ok'; Reason = ("{0} lines, {1} of the app" -f $content.Count, $app.Count) }
}

function Test-MetinARAdbOutputLost {
    <# True when adb output shows the connection to the device is gone (a command that silently returns nothing
       useful, e.g. an empty logcat dump, would otherwise look like a clean run). #>
    param([AllowEmptyCollection()][string[]]$Lines = @())
    return [bool](@($Lines | Where-Object { "$_" -match "^(adb(\.exe)?: )?error: (device ('[^']*' )?not found|no devices/emulators found|device offline|closed|protocol fault)" }).Count)
}

function Test-MetinARInstallGuard {
    <# Refuses to install over a protected package that is already on the phone (e.g. the game build another bot
       is testing), unless explicitly allowed. QA builds use their own applicationIdentifier (com.metinar.game.qa). #>
    param(
        [string]$Package,
        [AllowEmptyCollection()][string[]]$ProtectedPackages = @(),
        [AllowEmptyCollection()][string[]]$InstalledPackages = @(),
        [switch]$AllowReplace
    )
    $isProtected = [bool]($Package -and (@($ProtectedPackages) -ccontains $Package))
    $isInstalled = [bool]($Package -and (@($InstalledPackages) -ccontains $Package))
    $blocked = $isProtected -and $isInstalled -and -not $AllowReplace
    [pscustomobject]@{
        Status    = $(if ($blocked) { 'FAIL' } else { 'PASS' })
        Protected = $isProtected
        Installed = $isInstalled
        Reason    = $(if ($blocked) { "would replace the installed $Package (protected; another build may be under test). Use the QA APK (own applicationIdentifier) or pass -AllowReplaceInstalled after a note in #33" } elseif ($isProtected -and $isInstalled) { "replacing installed $Package (-AllowReplaceInstalled)" } else { $null })
    }
}

function Resolve-MetinARPermissionAllowList {
    <# Expands '{package}' in allow-list entries (AndroidX adds <applicationId>.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION). #>
    param([AllowEmptyCollection()][string[]]$Allowed = @(), [string]$Package)
    return @($Allowed | ForEach-Object { if ($Package) { "$_".Replace('{package}', $Package) } else { "$_" } })
}

# ---------------------------------------------------------------------------------------------
# #90 (METINAR-045) device session evidence: pure helpers for Invoke-MetinARDeviceSession.ps1
# ---------------------------------------------------------------------------------------------

function Get-MetinARLogcatTime {
    <# Returns the 'MM-dd HH:mm:ss.fff' prefix of a `logcat -v threadtime` line, or $null. #>
    param([AllowNull()][AllowEmptyString()][string]$Line)
    if ($Line -and $Line -match '^(\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\s') { return $Matches[1] }
    return $null
}

function ConvertTo-MetinARLogcatSeconds {
    <# Seconds since the start of a leap reference year for a logcat 'MM-dd HH:mm:ss.fff' stamp (differences only). #>
    param([Parameter(Mandatory = $true)][string]$Stamp)
    $d = [datetime]::ParseExact('2000-' + $Stamp, 'yyyy-MM-dd HH:mm:ss.fff', [System.Globalization.CultureInfo]::InvariantCulture)
    return ($d - [datetime]'2000-01-01').TotalSeconds
}

function New-MetinARLogcatCollector {
    <# Accumulates repeated `adb logcat -d` dumps without duplicates (the in-memory text is redacted by the caller). #>
    [pscustomobject]@{ Lines = (New-Object System.Collections.Generic.List[string]); LastStamp = $null; LastStampLines = (New-Object 'System.Collections.Generic.HashSet[string]'); Dumps = 0; Gaps = (New-Object System.Collections.Generic.List[string]) }
}

function Add-MetinARLogcatChunk {
    <#
      Merges one `adb logcat -d -v threadtime` dump into the collector. Lines at or before the last stamp already seen
      are skipped (lines with exactly that stamp are compared as text). When the oldest stamp in the dump is newer than
      the last stamp seen, the ring buffer rolled over between two dumps: a possible gap is recorded (never hidden).
      Returns the number of new lines.
    #>
    param([Parameter(Mandatory = $true)]$Collector, [AllowEmptyCollection()][AllowNull()][string[]]$Lines = @())
    $Collector.Dumps++
    $first = $null
    foreach ($l in @($Lines)) { $t = Get-MetinARLogcatTime $l; if ($t) { $first = $t; break } }
    if ($Collector.LastStamp -and $first -and [string]::CompareOrdinal($first, $Collector.LastStamp) -gt 0) {
        $Collector.Gaps.Add(("possible logcat gap: dump {0} starts at {1}, previous dump ended at {2} (ring buffer rolled over)" -f $Collector.Dumps, $first, $Collector.LastStamp))
    }
    $added = 0
    $newStamp = $Collector.LastStamp
    $newSet = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($s in $Collector.LastStampLines) { [void]$newSet.Add($s) }
    $take = $false
    foreach ($l in @($Lines)) {
        if ($null -eq $l) { continue }
        $t = Get-MetinARLogcatTime $l
        if (-not $t) {
            # continuation / '--------- beginning of' lines follow the decision of the last stamped line
            if ($take -and -not $l.StartsWith('--------- ')) { $Collector.Lines.Add($l); $added++ }
            continue
        }
        $cmp = if ($Collector.LastStamp) { [string]::CompareOrdinal($t, $Collector.LastStamp) } else { 1 }
        if ($cmp -lt 0) { $take = $false; continue }
        if ($cmp -eq 0 -and $Collector.LastStampLines.Contains($l)) { $take = $false; continue }
        $take = $true
        $Collector.Lines.Add($l); $added++
        if (-not $newStamp -or [string]::CompareOrdinal($t, $newStamp) -gt 0) { $newStamp = $t; $newSet.Clear() }
        if ($t -eq $newStamp) { [void]$newSet.Add($l) }
    }
    $Collector.LastStamp = $newStamp
    $Collector.LastStampLines = $newSet
    return $added
}

function ConvertFrom-MetinARGfxInfo {
    <#
      Parses `dumpsys gfxinfo <pkg> [framestats]`: the HWUI summary (frames, janky, percentiles) and, when present,
      the ---PROFILEDATA--- rows (frame time = FrameCompleted - IntendedVsync, rows with Flags=0 only).
      Unity renders to its own surface, so HWUI data is often empty for the game; that is reported, not hidden.
    #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines)
    $r = [ordered]@{ TotalFrames = $null; JankyFrames = $null; JankyPercent = $null; P50Ms = $null; P90Ms = $null; P95Ms = $null; P99Ms = $null; ProfileFrames = 0; ProfileP50Ms = $null; ProfileP95Ms = $null }
    $hdr = $null; $durations = New-Object System.Collections.Generic.List[double]; $inProfile = $false
    foreach ($l in @($Lines)) {
        if ($null -eq $l) { continue }
        $t = $l.Trim()
        if ($null -eq $r.TotalFrames -and $t -match '^Total frames rendered:\s*(\d+)') { $r.TotalFrames = [long]$Matches[1] }
        elseif ($null -eq $r.JankyFrames -and $t -match '^Janky frames:\s*(\d+)\s*\(([\d.]+)%\)') { $r.JankyFrames = [long]$Matches[1]; $r.JankyPercent = [double]::Parse($Matches[2], [System.Globalization.CultureInfo]::InvariantCulture) }
        elseif ($null -eq $r.P50Ms -and $t -match '^50th percentile:\s*(\d+)ms') { $r.P50Ms = [double]$Matches[1] }
        elseif ($null -eq $r.P90Ms -and $t -match '^90th percentile:\s*(\d+)ms') { $r.P90Ms = [double]$Matches[1] }
        elseif ($null -eq $r.P95Ms -and $t -match '^95th percentile:\s*(\d+)ms') { $r.P95Ms = [double]$Matches[1] }
        elseif ($null -eq $r.P99Ms -and $t -match '^99th percentile:\s*(\d+)ms') { $r.P99Ms = [double]$Matches[1] }
        elseif ($t -eq '---PROFILEDATA---') { $inProfile = -not $inProfile; $hdr = $null }
        elseif ($inProfile) {
            $cols = $t.TrimEnd(',') -split ','
            if ($cols[0] -eq 'Flags') { $hdr = @{}; for ($i = 0; $i -lt $cols.Count; $i++) { $hdr[$cols[$i]] = $i }; continue }
            if ($null -eq $hdr -or -not $hdr.ContainsKey('IntendedVsync') -or -not $hdr.ContainsKey('FrameCompleted')) { continue }
            if ($cols.Count -le [math]::Max($hdr['IntendedVsync'], $hdr['FrameCompleted']) -or $cols[0] -ne '0') { continue }
            [double]$a = 0; [double]$b = 0
            if ([double]::TryParse($cols[$hdr['IntendedVsync']], [ref]$a) -and [double]::TryParse($cols[$hdr['FrameCompleted']], [ref]$b) -and $b -gt $a -and $a -gt 0) { $durations.Add(($b - $a) / 1e6) }
        }
    }
    if ($durations.Count) {
        $r.ProfileFrames = $durations.Count
        $r.ProfileP50Ms = [math]::Round((Get-MetinARPercentile -Values $durations.ToArray() -Percent 50), 2)
        $r.ProfileP95Ms = [math]::Round((Get-MetinARPercentile -Values $durations.ToArray() -Percent 95), 2)
    }
    return [pscustomobject]$r
}

function Get-MetinARPercentile {
    <# Nearest-rank percentile (no interpolation), $null for no values. #>
    param([AllowEmptyCollection()][double[]]$Values = @(), [Parameter(Mandatory = $true)][double]$Percent)
    $v = @($Values | Sort-Object)
    if ($v.Count -eq 0) { return $null }
    $rank = [math]::Ceiling(($Percent / 100.0) * $v.Count)
    if ($rank -lt 1) { $rank = 1 }
    return [double]$v[[int]$rank - 1]
}

function Get-MetinARTimedPerfSamples {
    <# Like Get-MetinARPerfSamples, plus 'ts' (logcat stamp) and 't' (seconds, for differences) on every window. #>
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines)
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($l in @($Lines)) {
        $ts = Get-MetinARLogcatTime $l
        if (-not $ts) { continue }
        $s = Get-MetinARPerfSamples -Lines @($l)
        if ($s.Count -eq 0) { continue }
        $s[0] | Add-Member -NotePropertyName ts -NotePropertyValue $ts -Force
        $s[0] | Add-Member -NotePropertyName t -NotePropertyValue (ConvertTo-MetinARLogcatSeconds $ts) -Force
        $out.Add($s[0])
    }
    return , $out.ToArray()
}

function Test-MetinARCombatFps {
    <#
      #90 / spec section 15: "stable 30 FPS on the A31 in typical combat". Selects the [MetinARPerf] windows of the target scene(s)
      that end after the warm-up (counted from the start of the first window of that scene) and that contain a combat
      log line (-CombatPattern) - and, with requireCombo, a combo line (-ComboPattern) - inside the window
      (end - windowS, end]. Checks median and p5 of fpsAvg, the median of frameP50Ms, the worst frameP95Ms, spikes and
      main-thread p95 against the target. A window is never counted as combat without a log line that proves it.
      Measured: PASS / FAIL / NO DATA. This is an automatic measurement; the session verdict stays UNVERIFIED until a
      human has reviewed the video of the same time range.
    #>
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines,
        [Parameter(Mandatory = $true)]$Target,
        [string]$CombatPattern = '\[ArDamage\] swing=|\[Combo\] step=[1-9][0-9]*(?=\s|$)',
        [string]$ComboPattern = '\[Combo\] step=[1-9][0-9]*(?=\s|$)',
        [AllowNull()][object]$WarmupSeconds = $null
    )
    $scenes = @(Get-MetinARProp $Target 'sceneNames')
    $requireCombo = (Get-MetinARProp $Target 'requireCombo') -ne $false
    $warm = if ($null -ne $WarmupSeconds) { [double]$WarmupSeconds } elseif ($null -ne (Get-MetinARProp $Target 'warmupSeconds')) { [double](Get-MetinARProp $Target 'warmupSeconds') } else { 60.0 }
    $all = Get-MetinARTimedPerfSamples -Lines $Lines
    $sceneSamples = @($all | Where-Object { $scenes -contains [string]$_.scene })
    $combatT = New-Object System.Collections.Generic.List[double]; $comboT = New-Object System.Collections.Generic.List[double]
    foreach ($l in @($Lines)) {
        $ts = Get-MetinARLogcatTime $l
        if (-not $ts) { continue }
        if ($l -cmatch $CombatPattern) { $combatT.Add((ConvertTo-MetinARLogcatSeconds $ts)) }
        if ($ComboPattern -and $l -cmatch $ComboPattern) { $comboT.Add((ConvertTo-MetinARLogcatSeconds $ts)) }
    }
    $result = [ordered]@{ Name = 'CombatFps'; Measured = 'NO DATA'; Scenes = ($scenes -join ','); SceneWindows = $sceneSamples.Count; WarmupSeconds = $warm
        CombatLines = $combatT.Count; ComboLines = $comboT.Count; Windows = 0; FpsMedian = $null; FpsP5 = $null; FrameP50MedianMs = $null; FrameP95WorstMs = $null
        SpikesWorst = $null; MainP95WorstMs = $null; Checks = @(); Reason = ''; WindowStamps = @() }
    if ($sceneSamples.Count -eq 0) { $result.Reason = "no [MetinARPerf] windows for scene(s) $($result.Scenes) (release build, probe missing or AR not entered)"; return [pscustomobject]$result }
    $first = $sceneSamples[0]
    $firstLen = if ($null -ne (Get-MetinARProp $first 'windowS')) { [double]$first.windowS } else { 5.0 }
    $warmEnd = ($first.t - $firstLen) + $warm
    $picked = New-Object System.Collections.Generic.List[object]
    foreach ($s in $sceneSamples) {
        $len = if ($null -ne (Get-MetinARProp $s 'windowS')) { [double]$s.windowS } else { 5.0 }
        $start = $s.t - $len
        if ($start -lt $warmEnd) { continue }
        $inCombat = @($combatT | Where-Object { $_ -gt $start -and $_ -le $s.t }).Count -gt 0
        $inCombo = @($comboT | Where-Object { $_ -gt $start -and $_ -le $s.t }).Count -gt 0
        if ($inCombat -and (-not $requireCombo -or $inCombo)) { $picked.Add($s) }
    }
    $result.Windows = $picked.Count
    $result.WindowStamps = @($picked | ForEach-Object { $_.ts })
    if ($picked.Count -eq 0) {
        $why = if ($requireCombo -and $comboT.Count -eq 0) { 'no combo log line (' + $ComboPattern + ') in the log: combo is not in this build (#82) or was not used' }
               elseif ($combatT.Count -eq 0) { 'no combat log line (' + $CombatPattern + ') in the log: no hits recorded' }
               else { 'no combat window after the warm-up' }
        $result.Reason = $why
        return [pscustomobject]$result
    }
    function Vals([string]$k) { return [double[]]@($picked | ForEach-Object { Get-MetinARProp $_ $k } | Where-Object { $null -ne $_ -and $_ -isnot [string] }) }
    $fps = Vals 'fpsAvg'; $p50 = Vals 'frameP50Ms'; $p95 = Vals 'frameP95Ms'; $sp = Vals 'frameSpikes'; $mp = Vals 'mainP95Ms'
    $result.FpsMedian = Get-MetinARPercentile -Values $fps -Percent 50
    $result.FpsP5 = Get-MetinARPercentile -Values $fps -Percent 5
    $result.FrameP50MedianMs = Get-MetinARPercentile -Values $p50 -Percent 50
    if ($p95.Count) { $result.FrameP95WorstMs = ($p95 | Measure-Object -Maximum).Maximum }
    if ($sp.Count) { $result.SpikesWorst = ($sp | Measure-Object -Maximum).Maximum }
    if ($mp.Count) { $result.MainP95WorstMs = ($mp | Measure-Object -Maximum).Maximum }
    $checks = New-Object System.Collections.Generic.List[object]
    function Chk([string]$metric, [string]$op, $limit, $observed) {
        if ($null -eq $limit) { return }
        if ($null -eq $observed) { $checks.Add([pscustomobject]@{ Metric = $metric; Limit = "$op $limit"; Observed = 'n/a'; Status = 'NO DATA' }); return }
        $ok = if ($op -eq '>=') { [double]$observed -ge [double]$limit } else { [double]$observed -le [double]$limit }
        $checks.Add([pscustomobject]@{ Metric = $metric; Limit = "$op $limit"; Observed = [math]::Round([double]$observed, 2); Status = $(if ($ok) { 'PASS' } else { 'FAIL' }) })
    }
    Chk 'combat windows' '>=' (Get-MetinARProp $Target 'minWindows') $picked.Count
    Chk 'fpsAvg median' '>=' (Get-MetinARProp $Target 'minFpsMedian') $result.FpsMedian
    Chk 'fpsAvg p5' '>=' (Get-MetinARProp $Target 'minFpsP5') $result.FpsP5
    Chk 'frameP50Ms median' '<=' (Get-MetinARProp $Target 'maxFrameP50MedianMs') $result.FrameP50MedianMs
    Chk 'frameP95Ms worst window' '<=' (Get-MetinARProp $Target 'maxFrameP95Ms') $result.FrameP95WorstMs
    Chk 'frameSpikes>50ms worst window' '<=' (Get-MetinARProp $Target 'maxFrameSpikesPerWindow') $result.SpikesWorst
    Chk 'mainP95Ms worst window' '<=' (Get-MetinARProp $Target 'maxMainP95Ms') $result.MainP95WorstMs
    $st = @($checks | ForEach-Object { $_.Status })
    $result.Checks = $checks.ToArray()
    $result.Measured = if ($st -contains 'FAIL') { 'FAIL' } elseif ($st -contains 'NO DATA' -or $st.Count -eq 0) { 'NO DATA' } else { 'PASS' }
    return [pscustomobject]$result
}

function Test-MetinARPssTrend {
    <# PSS samples (MB, in time order): peak vs budget and growth first -> last (leak indicator). Needs >= MinSamples. #>
    param([AllowEmptyCollection()][double[]]$SamplesMB = @(), [AllowNull()][object]$MaxPssMB = $null, [double]$MaxGrowthPercent = 10, [int]$MinSamples = 3)
    $v = @($SamplesMB | Where-Object { $null -ne $_ -and $_ -gt 0 })
    $r = [ordered]@{ Name = 'Memory'; Measured = 'NO DATA'; Samples = $v.Count; FirstMB = $null; LastMB = $null; PeakMB = $null; GrowthPercent = $null; MaxPssMB = $MaxPssMB; MaxGrowthPercent = $MaxGrowthPercent; Reason = '' }
    if ($v.Count -eq 0) { $r.Reason = 'no dumpsys meminfo TOTAL PSS sample'; return [pscustomobject]$r }
    $r.FirstMB = $v[0]; $r.LastMB = $v[$v.Count - 1]; $r.PeakMB = ($v | Measure-Object -Maximum).Maximum
    $r.GrowthPercent = [math]::Round((($r.LastMB - $r.FirstMB) / $r.FirstMB) * 100, 1)
    $fail = @()
    if ($null -ne $MaxPssMB -and $r.PeakMB -gt [double]$MaxPssMB) { $fail += ("peak {0} MB > {1} MB" -f $r.PeakMB, $MaxPssMB) }
    if ($v.Count -ge $MinSamples -and $r.GrowthPercent -ge $MaxGrowthPercent) { $fail += ("growth {0}% >= {1}%" -f $r.GrowthPercent, $MaxGrowthPercent) }
    if ($fail.Count) { $r.Measured = 'FAIL'; $r.Reason = ($fail -join '; ') }
    elseif ($v.Count -lt $MinSamples) { $r.Measured = 'NO DATA'; $r.Reason = ("{0} sample(s), need >= {1} for the growth check" -f $v.Count, $MinSamples) }
    else { $r.Measured = 'PASS' }
    return [pscustomobject]$r
}

function Get-MetinARApkCommitFromName {
    <# Game commit from an APK file name such as MetinAR-v0c2-ee48cb9-dev.apk (7-40 hex between '-' and '-'/'.apk'). #>
    param([string]$Name)
    if ($Name -and ([IO.Path]::GetFileName($Name)) -match '-([0-9a-f]{7,40})(?=-|\.apk$)') { return $Matches[1] }
    return $null
}

function Get-MetinARSerialHash {
    <#
      Short SHA-256 of a device serial: identifies the same phone across sessions without writing the serial.
      #90 decision 4: only this value (first 12 lowercase hex characters of SHA-256 over the UTF-8 serial) is ever
      written; the raw serial never reaches a file.
    #>
    param([string]$Serial)
    if (-not $Serial) { return $null }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $h = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Serial)) } finally { $sha.Dispose() }
    return ((($h | ForEach-Object { $_.ToString('x2') }) -join '').Substring(0, 12))
}

function Get-MetinARStepLogEvidence {
    <#
      For each step of tools/qa/etap1-steps.json with a logPattern: number of matching log lines and the first stamp.
      This is a hint for the reviewer, never a PASS: every step stays UNVERIFIED until a human checks the video.
    #>
    param([Parameter(Mandatory = $true)][object[]]$Steps, [AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines = @())
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($s in $Steps) {
        $pat = [string](Get-MetinARProp $s 'logPattern')
        $count = $null; $firstTs = $null
        if ($pat) {
            $hits = @($Lines | Where-Object { $_ -cmatch $pat })
            $count = $hits.Count
            if ($hits.Count) { $firstTs = Get-MetinARLogcatTime $hits[0] }
        }
        $out.Add([pscustomobject]@{ id = $s.id; logPattern = $(if ($pat) { $pat } else { $null }); logLines = $count; firstStamp = $firstTs })
    }
    return , $out.ToArray()
}

function Select-MetinARLogcatSince {
    <#
      Keeps logcat lines stamped at or after -Since ('MM-dd HH:mm:ss.fff', device clock); lines without a threadtime
      stamp ('--------- beginning of main') are kept. Used by Invoke-MetinARDeviceSession.ps1 when the device log buffer
      is NOT cleared (default since review #100): lines of earlier runs are dropped on the host instead of wiping the
      device buffer with `adb logcat -c`. Empty -Since returns the lines unchanged.
    #>
    param([AllowEmptyCollection()][string[]]$Lines = @(), [string]$Since)
    if (-not $Since) { return , @($Lines) }
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($l in @($Lines)) {
        $t = Get-MetinARLogcatTime $l
        if ($null -eq $t -or [string]::CompareOrdinal([string]$t, $Since) -ge 0) { $out.Add($l) }
    }
    return , $out.ToArray()
}

function ConvertFrom-MetinARDeviceDate {
    # `adb shell date +%m-%d_%H:%M:%S` -> 'MM-dd HH:mm:ss.000' (null when the output is not in that format)
    param([AllowEmptyCollection()][string[]]$Lines = @())
    foreach ($l in @($Lines)) { if ("$l".Trim() -cmatch '^(\d\d-\d\d)_(\d\d:\d\d:\d\d)$') { return ('{0} {1}.000' -f $Matches[1], $Matches[2]) } }
    return $null
}

function Get-MetinARStageClosure {
    <#
      Closing verdict of ETAP I (#90, lead decision 2026-10-23 04:43): CLOSED only when every required step has
      verdict PASS with a reviewer and a videoRef (recording of the successful attempt). The 22 steps of spec
      section 18 and the mandatory step A97 (#97, combat without walking) are a hard minimum: a step definition
      file cannot drop them (extra requiredStepIds are added on top). A97 is never a manual side entry.
      Results come from session-summary.json -> steps after human review; at collection time all are UNVERIFIED.
    #>
    param([AllowEmptyCollection()][object[]]$StepResults = @(), [Parameter(Mandatory = $true)][object]$Definition)
    $required = New-Object System.Collections.Generic.List[string]
    foreach ($i in 1..22) { $required.Add([string]$i) }
    $required.Add('A97')
    foreach ($c in @(Get-MetinARProp $Definition 'requiredStepIds')) { if ($null -ne $c -and -not $required.Contains([string]$c)) { $required.Add([string]$c) } }
    $defIds = @(@(Get-MetinARProp $Definition 'steps') | ForEach-Object { [string]$_.id })
    $missingDef = @($required | Where-Object { $defIds -notcontains $_ })
    $byId = @{}
    foreach ($r in @($StepResults)) { if ($null -ne $r) { $byId[[string]$r.id] = $r } }
    $passed = New-Object System.Collections.Generic.List[string]
    $failed = New-Object System.Collections.Generic.List[string]
    $open = New-Object System.Collections.Generic.List[string]
    $reasons = New-Object System.Collections.Generic.List[string]
    foreach ($id in $required) {
        $r = $byId[$id]
        if ($null -eq $r) { $open.Add($id); $reasons.Add("${id}: no result"); continue }
        $v = [string](Get-MetinARProp $r 'verdict')
        if ($v -ceq 'PASS') {
            if (-not [string](Get-MetinARProp $r 'videoRef')) { $open.Add($id); $reasons.Add("${id}: PASS without videoRef (recording of the successful attempt)") }
            elseif (-not [string](Get-MetinARProp $r 'reviewer')) { $open.Add($id); $reasons.Add("${id}: PASS without reviewer") }
            else { $passed.Add($id) }
        } elseif ($v -ceq 'FAIL') { $failed.Add($id); $reasons.Add("${id}: FAIL") }
        else { $open.Add($id); $reasons.Add(("{0}: {1}" -f $id, $(if ($v) { $v } else { 'no verdict' }))) }
    }
    $closed = ($missingDef.Count -eq 0) -and ($passed.Count -eq $required.Count)
    $a97 = if ($passed.Contains('A97')) { 'PASS' } elseif ($failed.Contains('A97')) { 'FAIL' } else { 'UNVERIFIED' }
    return [pscustomobject][ordered]@{
        verdict = $(if ($missingDef.Count) { 'INVALID DEFINITION' } elseif ($closed) { 'CLOSED' } else { 'NOT CLOSED' })
        closed = $closed; required = $required.ToArray(); requiredCount = $required.Count
        passed = $passed.Count; failed = $failed.ToArray(); open = $open.ToArray(); missingDefinition = $missingDef
        a97 = $a97; reasons = $reasons.ToArray()
        rule = 'ETAP I closes only with PASS (reviewer + videoRef) of all 22 steps of spec section 18 AND the mandatory step A97 (#97) on one APK'
    }
}

Export-ModuleMember -Function *-MetinAR*
