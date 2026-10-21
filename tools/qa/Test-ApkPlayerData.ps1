<#
.SYNOPSIS
    Standalone, engine-free APK player-data check (#31 / METINAR-014). No Unity, adb or phone needed.
.DESCRIPTION
    Also fails when the APK lacks any of the 11 Unity.XR.ARCore registrations in RuntimeInitializeOnLoads.json
    (qa-config androidBuild.arCore; the e18df69 "AR unavailable" build, audit UPDATE_1 N-01).
    Fails when assets/bin/Data contains a serialized file both whole and as .split chunks (stale Gradle export, the
    52a1375 crash) or when .split chunks are not contiguous from 0. Same rules as ApkAudit and as
    AndroidPlayerDataValidator (feat/fpp-hands-aura-prototype, 2ec7a47).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\qa\Test-ApkPlayerData.ps1 -ApkPath client\Builds\Android\MetinAR-ar-encounter-qa.apk
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string[]]$ApkPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MetinARQa.psm1') -Force -DisableNameChecking
$arRule = (Get-Content -Raw (Join-Path $PSScriptRoot 'qa-config.json') | ConvertFrom-Json).androidBuild.arCore
$failed = 0
foreach ($p in @($ApkPath | ForEach-Object { $_ -split ',' } | Where-Object { $_ })) {
    if (-not (Test-Path $p)) { Write-Host "[FAIL] $p : not found"; $failed++; continue }
    $hash = (Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()
    $entries = @(Get-MetinARZipEntries -Path $p)
    $r = Test-MetinARApkPlayerData -Entries $entries
    $ar = Test-MetinARApkArCore -Entries $entries -RuntimeInitializeJson (Get-MetinARZipEntryText -Path $p -Entry 'assets/bin/Data/RuntimeInitializeOnLoads.json') -Rule $arRule
    $split = (@($r.SplitFiles.PSObject.Properties) | ForEach-Object { "$($_.Name) x$($_.Value)" }) -join ', '
    $problems = @(@($r.Problems) + @($ar.Problems | ForEach-Object { "ARCORE: $_" }))
    if ($problems.Count) {
        $failed++
        Write-Host "[FAIL] $p sha256=$hash"
        foreach ($x in $problems) { Write-Host "       $x" }
    } else {
        Write-Host "[PASS] $p sha256=$hash whole=$(@($r.WholeFiles).Count) split: $split ARCore $($ar.Found)/$($ar.Required)"
    }
}
if ($failed) { exit 1 } else { exit 0 }
