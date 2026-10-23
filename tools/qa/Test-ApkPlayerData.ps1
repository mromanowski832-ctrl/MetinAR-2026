<#
.SYNOPSIS
    Standalone, engine-free APK player-data check (#31 / METINAR-014). No Unity, adb or phone needed.
.DESCRIPTION
    Fails when assets/bin/Data contains a serialized file both whole and as .split chunks (stale Gradle export, the
    52a1375 crash) or when .split chunks are not contiguous from 0. Same rules as ApkAudit and as
    AndroidPlayerDataValidator (feat/fpp-hands-aura-prototype, 2ec7a47).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\qa\Test-ApkPlayerData.ps1 -ApkPath client\Builds\Android\MetinAR-ar-encounter-dev.apk
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string[]]$ApkPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MetinARQa.psm1') -Force -DisableNameChecking
$failed = 0
foreach ($p in @($ApkPath | ForEach-Object { $_ -split ',' } | Where-Object { $_ })) {
    if (-not (Test-Path $p)) { Write-Host "[FAIL] $p : not found"; $failed++; continue }
    $hash = (Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()
    $r = Test-MetinARApkPlayerData -Entries @(Get-MetinARZipEntries -Path $p)
    $split = (@($r.SplitFiles.PSObject.Properties) | ForEach-Object { "$($_.Name) x$($_.Value)" }) -join ', '
    if (@($r.Problems).Count) {
        $failed++
        Write-Host "[FAIL] $p sha256=$hash"
        foreach ($x in $r.Problems) { Write-Host "       $x" }
    } else {
        Write-Host "[PASS] $p sha256=$hash whole=$(@($r.WholeFiles).Count) split: $split"
    }
}
if ($failed) { exit 1 } else { exit 0 }
