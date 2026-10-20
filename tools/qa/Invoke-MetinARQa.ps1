<#
.SYNOPSIS
  MetinAR unified local verification runner (METINAR-011). Prints PASS/FAIL per gate and writes a Markdown + JSON report.

.DESCRIPTION
  Gates (run in this order, select with -Gates):
    Hygiene        static repo checks (tools/qa/Test-RepoHygiene.ps1)
    DotNetTests    every test project in qa-config.json (dotnet test, Release)
    UnityCompile   Unity batch import/compile: exit 0 and 0 'error CS'
    UnityQaChecks  missing-script scan (build scenes + prefabs) and expected build scenes / indices
    WorldMapSmoke  existing world-map Play Mode smoke (JUnit XML)
    ArSmoke        existing AR encounter Play Mode smoke (XR Simulation, JUnit XML)
    AndroidBuild   Android ARM64 / IL2CPP development APK
    ApkAudit       APK size + SHA-256, ABI, IL2CPP, debuggable, minSdk, manifest permission audit
                   (FAIL if ACCESS_BACKGROUND_LOCATION or any other forbidden permission is declared)
  A WarningsSummary row summarises compiler warnings / exceptions across all Unity logs that ran.

  Unity must not have the same project open (the runner checks Temp/UnityLockfile).
  Outputs go to Builds/qa/<timestamp>/ (git-ignored). Exit code 0 = no FAIL gate.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1 -Gates Hygiene,DotNetTests
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1 -SkipAndroid -ApkPath client\Builds\Android\MetinAR-ar-encounter-dev.apk
#>
[CmdletBinding()]
param(
    # Comma-separated or array: All, Hygiene, DotNetTests, UnityCompile, UnityQaChecks, WorldMapSmoke, ArSmoke, AndroidBuild, ApkAudit
    [string[]]$Gates = @('All'),
    [string]$UnityPath,
    [string]$OutputDir,
    [string]$ApkPath,
    [switch]$SkipUnity,
    [switch]$SkipAndroid,
    [switch]$StopOnFail,
    [switch]$RevertUnityRewrites
)

# 'Continue': Windows PowerShell 5.1 turns native stderr into error records; gates decide PASS/FAIL explicitly.
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot 'MetinARQa.psm1') -Force -DisableNameChecking

$repo = Get-MetinARRepoRoot -Start $PSScriptRoot
$config = Read-MetinARJson -Path (Join-Path $repo 'tools/qa/qa-config.json')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $OutputDir) { $OutputDir = Join-Path $repo ("Builds/qa/" + $stamp) }
New-Item -ItemType Directory -Force -Path (Join-Path $OutputDir 'logs') | Out-Null
$project = Join-Path $repo $config.unityProject

$all = @('Hygiene', 'DotNetTests', 'UnityCompile', 'UnityQaChecks', 'WorldMapSmoke', 'ArSmoke', 'AndroidBuild', 'ApkAudit')
$Gates = @($Gates | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$unknown = @($Gates | Where-Object { $_ -ne 'All' -and $all -notcontains $_ })
if ($unknown.Count) { Write-Host "Unknown gate(s): $($unknown -join ', '). Valid: All, $($all -join ', ')" -ForegroundColor Red; exit 2 }
$selected = if ($Gates -contains 'All') { $all } else { @($all | Where-Object { $Gates -contains $_ }) }
$unityGates = @('UnityCompile', 'UnityQaChecks', 'WorldMapSmoke', 'ArSmoke', 'AndroidBuild')

$results = New-Object System.Collections.Generic.List[object]
$unityLogs = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]

function Write-Gate($r) {
    $color = switch ($r.Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'Gray' } }
    Write-Host ("[{0,-10}] {1,-15} {2}" -f $r.Status, $r.Name, $r.Detail) -ForegroundColor $color
}
function Add-Result($r) {
    $results.Add($r); Write-Gate $r
    if ($StopOnFail -and $r.Status -eq 'FAIL') { throw "STOP_ON_FAIL:$($r.Name)" }
}
function Rel([string]$p) { if ($p -and $p.StartsWith($repo)) { return $p.Substring($repo.Length).TrimStart('\', '/') } return $p }

# --- git state (to detect files Unity rewrites) ---
$git = Find-MetinARGit
function Get-DirtyTracked {
    if (-not $git) { return @() }
    $lines = & $git -C $repo status --porcelain --untracked-files=no 2>$null
    return @($lines | ForEach-Object { "$_".Substring(3).Trim('"') })
}
$dirtyBefore = Get-DirtyTracked
$head = if ($git) { (& $git -C $repo rev-parse HEAD 2>$null) } else { 'unknown' }
$branch = if ($git) { (& $git -C $repo rev-parse --abbrev-ref HEAD 2>$null) } else { 'unknown' }
if ($dirtyBefore.Count -gt 0) { $notes.Add("Working tree had tracked modifications before the run: $($dirtyBefore -join ', ')") }

$unity = $null
$needUnity = @($selected | Where-Object { $unityGates -contains $_ }).Count -gt 0 -and -not $SkipUnity
if ($needUnity) {
    $unity = Find-MetinARUnity -Version $config.unityVersion -Override $UnityPath
    if (-not $unity) { $notes.Add("Unity $($config.unityVersion) not found (set -UnityPath or METINAR_UNITY).") }
}

function Invoke-UnityGate([string]$name, [string]$method, [string[]]$extra, [switch]$quit, [switch]$noGraphics, [int]$timeout) {
    if ($SkipUnity) { return (New-MetinARGateResult -Name $name -Status 'SKIP' -Detail 'skipped (-SkipUnity)') }
    if (-not $unity) { return (New-MetinARGateResult -Name $name -Status 'FAIL' -Detail "Unity $($config.unityVersion) not found") }
    if (Test-MetinARUnityProjectLocked -ProjectPath $project) {
        return (New-MetinARGateResult -Name $name -Status 'FAIL' -Detail 'project is open in another Unity instance (Temp/UnityLockfile locked)')
    }
    $log = Join-Path $OutputDir ("logs/unity-" + $name + ".log")
    $run = Invoke-MetinARUnityBatch -UnityPath $unity -ProjectPath $project -LogFile $log -ExecuteMethod $method -ExtraArgs $extra -Quit:$quit -NoGraphics:$noGraphics -TimeoutMinutes $timeout
    $unityLogs.Add($log)
    $lines = if (Test-Path $log) { @(Get-Content -Path $log) } else { @() }
    $sum = Get-MetinARUnityLogSummary -Lines $lines
    return [pscustomobject]@{ Run = $run; Summary = $sum; Log = $log; Lines = $lines }
}

try {
    foreach ($gate in $selected) {
        $t0 = Get-Date
        switch ($gate) {
            'Hygiene' {
                $out = & (Join-Path $PSScriptRoot 'Test-RepoHygiene.ps1') -RepoRoot $repo -PassThru
                $fails = @($out | Where-Object { $_.Status -eq 'FAIL' })
                $warns = @($out | Where-Object { $_.Status -eq 'WARN' })
                $st = if ($fails.Count) { 'FAIL' } elseif ($warns.Count) { 'WARN' } else { 'PASS' }
                Add-Result (New-MetinARGateResult -Name 'Hygiene' -Status $st -Detail ("{0} checks, {1} fail, {2} warn" -f @($out).Count, $fails.Count, $warns.Count) `
                    -Evidence @($out | ForEach-Object { "$($_.Status) $($_.Name): $($_.Detail)" }) -Seconds ((Get-Date) - $t0).TotalSeconds)
            }
            'DotNetTests' {
                $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
                if (-not $dotnet) { Add-Result (New-MetinARGateResult -Name 'DotNetTests' -Status 'FAIL' -Detail 'dotnet not found'); break }
                $runtimes = & dotnet --list-runtimes 2>$null
                $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
                if (-not ($runtimes -match 'Microsoft\.NETCore\.App 8\.')) {
                    $env:DOTNET_ROLL_FORWARD = 'Major'
                    $notes.Add('No .NET 8 runtime found; tests ran with DOTNET_ROLL_FORWARD=Major on a newer runtime.')
                }
                $ev = @(); $failed = $false; $total = 0; $passed = 0
                foreach ($proj in $config.testProjects) {
                    $log = Join-Path $OutputDir ("logs/dotnet-" + [IO.Path]::GetFileNameWithoutExtension($proj) + ".log")
                    & dotnet test (Join-Path $repo $proj) --configuration Release --logger 'console;verbosity=normal' *> $log
                    $code = $LASTEXITCODE
                    $content = @(Get-Content $log)
                    $line = @($content | Where-Object { $_ -match '(Passed|Failed)!\s+-\s+Failed:' }) | Select-Object -Last 1
                    if ($line -match 'Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+\d+, Total:\s+(\d+)') { $total += [int]$Matches[3]; $passed += [int]$Matches[2] }
                    else {
                        # console logger with normal verbosity: "Total tests: N" / "Passed: N" / "Failed: N"
                        $tt = @($content | Where-Object { $_ -match '^\s*Total tests:\s*(\d+)' }) | Select-Object -Last 1
                        $pp = @($content | Where-Object { $_ -match '^\s*Passed:\s*(\d+)\s*$' }) | Select-Object -Last 1
                        $ff = @($content | Where-Object { $_ -match '^\s*Failed:\s*(\d+)\s*$' }) | Select-Object -Last 1
                        $tn = if ($tt -match '(\d+)') { [int]$Matches[1] } else { 0 }
                        $pn = if ($pp -match '(\d+)') { [int]$Matches[1] } else { 0 }
                        $fn = if ($ff -match '(\d+)') { [int]$Matches[1] } else { 0 }
                        $total += $tn; $passed += $pn
                        $line = "total=$tn passed=$pn failed=$fn"
                    }
                    if ($total -eq 0) { $failed = $true }
                    $ev += "$proj exit=$code :: $line"
                    if ($code -ne 0) { $failed = $true }
                }
                Add-Result (New-MetinARGateResult -Name 'DotNetTests' -Status $(if ($failed) { 'FAIL' } else { 'PASS' }) -Detail "$passed/$total passed" -Evidence $ev -Seconds ((Get-Date) - $t0).TotalSeconds)
            }
            'UnityCompile' {
                $u = Invoke-UnityGate 'UnityCompile' $null @() -quit -noGraphics 60
                if ($u -isnot [pscustomobject] -or -not $u.PSObject.Properties['Run']) { Add-Result $u; break }
                $ok = ($u.Run.ExitCode -eq 0 -and $u.Summary.CompilerErrors -eq 0 -and $u.Summary.BlockingLines.Count -eq 0)
                Add-Result (New-MetinARGateResult -Name 'UnityCompile' -Status $(if ($ok) { 'PASS' } else { 'FAIL' }) `
                    -Detail ("exit={0} errorCS={1} warningCS={2} timedOut={3}" -f $u.Run.ExitCode, $u.Summary.CompilerErrors, $u.Summary.CompilerWarnings, $u.Run.TimedOut) `
                    -Evidence (@($u.Summary.CompilerErrorLines) + @($u.Summary.BlockingLines) + @("log: " + (Rel $u.Log))) -Seconds $u.Run.Seconds)
            }
            'UnityQaChecks' {
                $json = Join-Path $OutputDir 'unity-qa-checks.json'
                if (Test-Path $json) { Remove-Item $json -Force }
                $extra = @('-metinarQaResults', ('"' + $json + '"'), '-metinarQaExpectedScenes', ('"' + ($config.expectedBuildScenes -join ';') + '"'),
                           '-metinarQaPrefabRoots', ('"' + ($config.missingScriptPrefabRoots -join ';') + '"'))
                $u = Invoke-UnityGate 'UnityQaChecks' 'MetinAR.Editor.MetinARQaChecks.Run' $extra -noGraphics 30
                if ($u -isnot [pscustomobject] -or -not $u.PSObject.Properties['Run']) { Add-Result $u; break }
                if (-not (Test-Path $json)) {
                    Add-Result (New-MetinARGateResult -Name 'UnityQaChecks' -Status 'FAIL' -Detail "no results file (exit=$($u.Run.ExitCode))" -Evidence @("log: " + (Rel $u.Log)) -Seconds $u.Run.Seconds); break
                }
                $r = Read-MetinARJson -Path $json
                $enabled = @($r.buildScenes | Where-Object { $_.enabled } | ForEach-Object { $_.path })
                $ev = @()
                for ($i = 0; $i -lt $enabled.Count; $i++) { $ev += "build index $i = $($enabled[$i])" }
                $ev += "prefabs scanned: $($r.prefabsScanned); missing scripts total: $($r.totalMissingScripts)"
                $ev += @($r.failures)
                $st = if ($r.result -eq 'PASS' -and $u.Run.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }
                Add-Result (New-MetinARGateResult -Name 'UnityQaChecks' -Status $st -Detail ("missingScripts={0} scenes={1} prefabs={2} sceneOrder={3}" -f $r.totalMissingScripts, $enabled.Count, $r.prefabsScanned, $(if (@($r.failures | Where-Object { $_ -match 'build index|extra enabled' }).Count) { 'MISMATCH' } else { 'OK' })) -Evidence $ev -Seconds $u.Run.Seconds)
            }
            { $_ -eq 'WorldMapSmoke' -or $_ -eq 'ArSmoke' } {
                $cfg = @($config.smokeTests | Where-Object { $_.name -eq $gate })[0]
                $xmlPath = Join-Path $repo $cfg.results
                if (Test-Path $xmlPath) { Remove-Item $xmlPath -Force }
                $u = Invoke-UnityGate $gate $cfg.method @() ([int]$cfg.timeoutMinutes)
                if ($u -isnot [pscustomobject] -or -not $u.PSObject.Properties['Run']) { Add-Result $u; break }
                $j = Get-MetinARJUnitSummary -Path $xmlPath
                if ($null -eq $j) {
                    Add-Result (New-MetinARGateResult -Name $gate -Status 'FAIL' -Detail "no JUnit results (exit=$($u.Run.ExitCode), timedOut=$($u.Run.TimedOut))" -Evidence @("log: " + (Rel $u.Log)) -Seconds $u.Run.Seconds); break
                }
                Copy-Item $xmlPath (Join-Path $OutputDir (Split-Path $xmlPath -Leaf)) -Force
                $ok = ($u.Run.ExitCode -eq 0 -and $j.Failures -eq 0 -and $j.Tests -gt 0)
                Add-Result (New-MetinARGateResult -Name $gate -Status $(if ($ok) { 'PASS' } else { 'FAIL' }) -Detail ("{0} tests, {1} failures, exit={2}" -f $j.Tests, $j.Failures, $u.Run.ExitCode) `
                    -Evidence (@($j.Failed) + @("results: " + (Rel $xmlPath), "log: " + (Rel $u.Log))) -Seconds $u.Run.Seconds)
            }
            'AndroidBuild' {
                if ($SkipAndroid) { Add-Result (New-MetinARGateResult -Name 'AndroidBuild' -Status 'SKIP' -Detail 'skipped (-SkipAndroid)'); break }
                $apk = Join-Path $repo $config.androidBuild.apk
                if (Test-Path $apk) { Remove-Item $apk -Force }
                $u = Invoke-UnityGate 'AndroidBuild' $config.androidBuild.method @() -noGraphics ([int]$config.androidBuild.timeoutMinutes)
                if ($u -isnot [pscustomobject] -or -not $u.PSObject.Properties['Run']) { Add-Result $u; break }
                $okLine = @($u.Lines | Where-Object { $_ -match '\[MetinARAndroidDevBuild\] (SUCCESS|FAILED|scenes=)' })
                $ok = ($u.Run.ExitCode -eq 0 -and (Test-Path $apk) -and $u.Summary.CompilerErrors -eq 0)
                Add-Result (New-MetinARGateResult -Name 'AndroidBuild' -Status $(if ($ok) { 'PASS' } else { 'FAIL' }) -Detail ("exit={0} apk={1} method={2}" -f $u.Run.ExitCode, $(if (Test-Path $apk) { Rel $apk } else { 'missing' }), $config.androidBuild.method) `
                    -Evidence (@($okLine | ForEach-Object { ConvertTo-MetinARRedactedText -Lines @($_) }) + @($u.Summary.BlockingLines) + @("log: " + (Rel $u.Log))) -Seconds $u.Run.Seconds)
            }
            'ApkAudit' {
                $apk = if ($ApkPath) { (Resolve-Path $ApkPath).Path } else { Join-Path $repo $config.androidBuild.apk }
                if (-not (Test-Path $apk)) { Add-Result (New-MetinARGateResult -Name 'ApkAudit' -Status 'FAIL' -Detail "APK not found: $(Rel $apk)"); break }
                $aapt = Find-MetinARAndroidTool -Name aapt -UnityPath $unity
                if (-not $aapt) { $aapt = Find-MetinARAndroidTool -Name aapt2 -UnityPath $unity }
                $rep = Get-MetinARApkReport -ApkPath $apk -AaptPath $aapt
                $budgets = Read-MetinARJson -Path (Join-Path $repo $config.budgetsFile)
                $problems = @(); $warnings = @()
                $b = $rep.Badging
                if ($null -eq $b -or -not $b.Package) { $problems += 'aapt badging unavailable (cannot audit manifest)' }
                else {
                    if ($b.Package -ne $config.androidBuild.expectedPackage) { $problems += "package $($b.Package) != $($config.androidBuild.expectedPackage)" }
                    if ($config.androidBuild.requireDebuggable -and -not $b.Debuggable) { $problems += 'not a development (debuggable) build' }
                    if ($b.MinSdk -lt [int]$config.androidBuild.minSdk) { $problems += "minSdk $($b.MinSdk) < $($config.androidBuild.minSdk)" }
                    $perm = Test-MetinARPermissionPolicy -Permissions $b.Permissions -Forbidden @($config.permissions.forbidden) -Allowed @($config.permissions.allowed)
                    if ($perm.Forbidden.Count) { $problems += "FORBIDDEN permissions: $($perm.Forbidden -join ', ')" }
                    if ($perm.Unexpected.Count) { $warnings += "permissions not in allow-list (review): $($perm.Unexpected -join ', ')" }
                }
                if ($rep.Abis -notcontains $config.androidBuild.requiredAbi) { $problems += "required ABI $($config.androidBuild.requiredAbi) missing" }
                $badAbi = @($rep.Abis | Where-Object { @($config.androidBuild.forbiddenAbis) -contains $_ })
                if ($badAbi.Count) { $problems += "unexpected ABIs: $($badAbi -join ', ')" }
                if (-not $rep.HasIl2Cpp -or -not $rep.HasGlobalMetadata) { $problems += 'IL2CPP markers missing (libil2cpp.so / global-metadata.dat)' }
                if ($rep.HasMonoAssemblies) { $problems += 'Mono managed assemblies present (expected IL2CPP)' }
                if ($rep.SizeMB -gt [double]$budgets.apk.maxDevApkMB) { $problems += "APK $($rep.SizeMB) MB > prototype budget $($budgets.apk.maxDevApkMB) MB" }
                elseif ($rep.SizeMB -gt [double]$budgets.apk.warnDevApkMB) { $warnings += "APK $($rep.SizeMB) MB > warn level $($budgets.apk.warnDevApkMB) MB" }
                $st = if ($problems.Count) { 'FAIL' } elseif ($warnings.Count) { 'WARN' } else { 'PASS' }
                $ev = @(
                    "apk: $(Rel $rep.Path)",
                    "size: $($rep.SizeBytes) bytes ($($rep.SizeMB) MB)",
                    "sha256: $($rep.Sha256)",
                    "abis: $($rep.Abis -join ',')  il2cpp: $($rep.HasIl2Cpp)  global-metadata: $($rep.HasGlobalMetadata)"
                )
                if ($b) {
                    $ev += "package: $($b.Package)  version: $($b.VersionName) ($($b.VersionCode))  minSdk: $($b.MinSdk)  targetSdk: $($b.TargetSdk)  debuggable: $($b.Debuggable)"
                    $ev += "launchable-activity: $($b.LaunchableActivity)"
                    $ev += "permissions: $($b.Permissions -join ', ')"
                    $ev += "ACCESS_BACKGROUND_LOCATION: $(if ($b.Permissions -contains 'android.permission.ACCESS_BACKGROUND_LOCATION') { 'PRESENT' } else { 'ABSENT' })"
                }
                $ev += $problems; $ev += $warnings
                Add-Result (New-MetinARGateResult -Name 'ApkAudit' -Status $st -Detail ("{0} MB, perms={1}, bgLocation={2}" -f $rep.SizeMB, $(if ($b) { @($b.Permissions).Count } else { '?' }), $(if ($b -and $b.Permissions -contains 'android.permission.ACCESS_BACKGROUND_LOCATION') { 'PRESENT' } else { 'ABSENT' })) -Evidence $ev -Seconds ((Get-Date) - $t0).TotalSeconds)
                $rep | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $OutputDir 'apk-report.json') -Encoding UTF8
            }
        }
    }
} catch {
    if ("$_" -like 'STOP_ON_FAIL:*') { $notes.Add("Stopped after first failing gate ($("$_".Substring(13))).") } else { throw }
}

# --- warnings / errors summary across Unity logs ---
if ($unityLogs.Count -gt 0) {
    $agg = Get-MetinARUnityLogSummary -Lines @($unityLogs | Where-Object { Test-Path $_ } | ForEach-Object { Get-Content $_ })
    $st = if ($agg.CompilerErrors -gt 0) { 'FAIL' } elseif ($agg.Exceptions -gt 0 -or $agg.CompilerWarnings -gt 0) { 'WARN' } else { 'PASS' }
    $ev = @("unique warning CS lines: $($agg.CompilerWarnings)", "unique error CS lines: $($agg.CompilerErrors)", "exception lines: $($agg.Exceptions)") + @($agg.ExceptionLines)
    $results.Add((New-MetinARGateResult -Name 'WarningsSummary' -Status $st -Detail ("errorCS={0} warningCS={1} exceptions={2} (all Unity logs)" -f $agg.CompilerErrors, $agg.CompilerWarnings, $agg.Exceptions) -Evidence (ConvertTo-MetinARRedactedText -Lines $ev)))
    Write-Gate $results[$results.Count - 1]
}

# --- files Unity rewrote ---
$dirtyAfter = Get-DirtyTracked
$rewritten = @($dirtyAfter | Where-Object { $dirtyBefore -notcontains $_ })
if ($rewritten.Count -gt 0) {
    if ($RevertUnityRewrites -and $git) {
        & $git -C $repo checkout -- @($rewritten) 2>$null
        $notes.Add("Reverted tracked files rewritten by Unity during the run: $($rewritten -join ', ')")
    } else {
        $notes.Add("Unity rewrote tracked files during the run (review or rerun with -RevertUnityRewrites): $($rewritten -join ', ')")
    }
}
$notes.Add('Build / compile / editor smoke results are not physical-device evidence. GPS, camera, AR, hand tracking and performance stay UNVERIFIED until run on a phone (tools/qa/Invoke-MetinARDeviceSmoke.ps1).')

$overall = Get-MetinAROverallStatus -Results $results
$header = @{
    'Branch'   = $branch
    'Head'     = $head
    'Unity'    = $(if ($unity) { $config.unityVersion } else { 'not used' })
    'Host'     = $(if ($env:OS -eq 'Windows_NT') { 'Windows' } else { [Environment]::OSVersion.Platform })
    'Date'     = (Get-Date -Format 'yyyy-MM-dd HH:mm zzz')
}
$md = ConvertTo-MetinARMarkdownReport -Title 'MetinAR QA report' -Results $results -Header $header -Notes $notes
# Redact local user paths / e-mails / IPs; keep SHA-256 and git SHAs readable.
$md = (ConvertTo-MetinARRedactedText -Lines ($md -split "`n") -KeepHex) -join "`n"
Set-Content -Path (Join-Path $OutputDir 'qa-report.md') -Value $md -Encoding UTF8
[pscustomobject]@{ overall = $overall; branch = $branch; head = $head; results = $results; notes = $notes } | ConvertTo-Json -Depth 6 |
    Set-Content -Path (Join-Path $OutputDir 'qa-report.json') -Encoding UTF8

Write-Host ''
Write-Host ("OVERALL: {0}   report: {1}" -f $overall, (Join-Path $OutputDir 'qa-report.md'))
if ($overall -eq 'FAIL') { exit 1 } else { exit 0 }
