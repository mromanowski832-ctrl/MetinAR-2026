<#
.SYNOPSIS
  Static, Unity-free repository checks for MetinAR (METINAR-011). Runs locally and in GitHub Actions.

.DESCRIPTION
  - Unity version pin (ProjectVersion.txt) matches tools/qa/qa-config.json
  - no tracked Unity caches / build outputs / APK / AAB / keystores
  - every tracked client/Assets file has a tracked .meta, and no orphan .meta files
  - enabled build scenes exist and match the expected list/order from qa-config.json
  - no ACCESS_BACKGROUND_LOCATION in Android manifests / Unity C# / gradle / project settings
  - no obvious secrets (private keys, GitHub/AWS/Google tokens, keystore passwords)
  - no very large tracked files
  - PowerShell tooling is ASCII-only (Windows PowerShell 5.1 compatibility)
  - test projects only reference production source files that exist
  - optional (-BaseRef): guarded files (GeoCoordinate.cs / GeoMath.cs) unchanged vs the base ref

.EXAMPLE
  pwsh -File tools/qa/Test-RepoHygiene.ps1
  pwsh -File tools/qa/Test-RepoHygiene.ps1 -BaseRef origin/main
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$BaseRef,
    [switch]$PassThru
)
$ErrorActionPreference = 'Continue'
Import-Module (Join-Path $PSScriptRoot 'MetinARQa.psm1') -Force -DisableNameChecking
if (-not $RepoRoot) { $RepoRoot = Get-MetinARRepoRoot -Start $PSScriptRoot }
$config = Read-MetinARJson -Path (Join-Path $RepoRoot 'tools/qa/qa-config.json')
$git = Find-MetinARGit
$checks = New-Object System.Collections.Generic.List[object]
function Add([string]$name, [string]$status, [string]$detail) { $checks.Add([pscustomobject]@{ Name = $name; Status = $status; Detail = $detail }) }

if (-not $git) {
    Add 'git' 'FAIL' 'git not found (on Windows the Visual Studio bundled git is searched too)'
} else {
    $files = @(& $git -C $RepoRoot -c core.quotepath=off ls-files --cached --others --exclude-standard)

    # 1. Unity version pin
    $pv = Join-Path $RepoRoot 'client/ProjectSettings/ProjectVersion.txt'
    $ver = if (Test-Path $pv) { ((Get-Content $pv | Where-Object { $_ -match '^m_EditorVersion:' }) -replace '^m_EditorVersion:\s*', '').Trim() } else { '' }
    Add 'UnityVersionPin' $(if ($ver -eq $config.unityVersion) { 'PASS' } else { 'FAIL' }) "ProjectVersion=$ver expected=$($config.unityVersion)"

    # 2. generated folders / binaries
    $gen = @($files | Where-Object {
        $_ -match '^(client/)?(Library|Temp|Logs|obj|UserSettings|Builds?|\.vs)/' -or $_ -match '\.(apk|aab|keystore|jks|p12|mobileprovision)$'
    })
    Add 'NoGeneratedFiles' $(if ($gen.Count) { 'FAIL' } else { 'PASS' }) $(if ($gen.Count) { "tracked: $(($gen | Select-Object -First 10) -join ', ')" } else { 'no tracked caches/builds/APK/keystores' })

    # 2b. tracked or staged local credential files (templates are deliberately permitted)
    $secretFiles = @($files | Where-Object {
        $name = [IO.Path]::GetFileName($_)
        $looksLikeEnv = $name -match '(?i)(^\.env($|\.)|\.env($|\.))' -and $name -notmatch '(?i)\.env\.(example|sample|template)
    $assetSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($f in $files) { if ($f.StartsWith('client/Assets/')) { [void]$assetSet.Add($f) } }
    $dirs = New-Object System.Collections.Generic.HashSet[string]
    $missingMeta = New-Object System.Collections.Generic.List[string]
    $orphanMeta = New-Object System.Collections.Generic.List[string]
    foreach ($f in $assetSet) {
        $rel = $f.Substring('client/Assets/'.Length)
        $parts = $rel -split '/'
        for ($i = 1; $i -lt $parts.Count; $i++) { [void]$dirs.Add('client/Assets/' + (($parts[0..($i - 1)]) -join '/')) }
        if ($f.EndsWith('.meta')) {
            $target = $f.Substring(0, $f.Length - 5)
            if (-not $assetSet.Contains($target) -and -not (@($assetSet | Where-Object { $_.StartsWith($target + '/') } | Select-Object -First 1).Count)) { $orphanMeta.Add($f) }
        } elseif (-not $assetSet.Contains($f + '.meta')) { $missingMeta.Add($f) }
    }
    foreach ($d in $dirs) { if (-not $assetSet.Contains($d + '.meta')) { $missingMeta.Add($d + '/') } }
    $metaDetail = "assets=$($assetSet.Count) missingMeta=$($missingMeta.Count) orphanMeta=$($orphanMeta.Count)"
    if ($missingMeta.Count) { $metaDetail += " e.g. $(($missingMeta | Select-Object -First 5) -join ', ')" }
    if ($orphanMeta.Count) { $metaDetail += " orphan e.g. $(($orphanMeta | Select-Object -First 5) -join ', ')" }
    Add 'MetaFiles' $(if ($missingMeta.Count -or $orphanMeta.Count) { 'FAIL' } else { 'PASS' }) $metaDetail

    # 4. build scenes (static read of EditorBuildSettings.asset)
    $ebs = Join-Path $RepoRoot 'client/ProjectSettings/EditorBuildSettings.asset'
    $enabled = @(); $cur = $null
    foreach ($l in (Get-Content $ebs)) {
        if ($l -match '^\s*- enabled: (\d)') { $cur = ($Matches[1] -eq '1') }
        elseif ($l -match '^\s*path: (.+)$' -and $null -ne $cur) { if ($cur) { $enabled += $Matches[1].Trim() }; $cur = $null }
    }
    $expected = @($config.expectedBuildScenes)
    $sceneProblems = @()
    for ($i = 0; $i -lt [math]::Max($expected.Count, $enabled.Count); $i++) {
        $e = if ($i -lt $expected.Count) { $expected[$i] } else { '<none>' }
        $a = if ($i -lt $enabled.Count) { $enabled[$i] } else { '<none>' }
        if ($e -ne $a) { $sceneProblems += "index $i expected $e got $a" }
    }
    foreach ($s in $enabled) { if (-not $assetSet.Contains('client/' + $s)) { $sceneProblems += "scene not tracked: $s" } }
    Add 'BuildScenes' $(if ($sceneProblems.Count) { 'FAIL' } else { 'PASS' }) $(if ($sceneProblems.Count) { $sceneProblems -join '; ' } else { (@(for ($i = 0; $i -lt $enabled.Count; $i++) { "$i=$($enabled[$i])" }) -join ', ') })

    # 5. background location
    $bg = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(cs|xml|gradle|kts|asset|json|properties)$' -and $_ -notmatch '^tools/qa/' -and $_ -notmatch '^tests/' })) {
        $p = Join-Path $RepoRoot $f
        if ((Test-Path $p) -and (Select-String -Path $p -Pattern 'ACCESS_BACKGROUND_LOCATION' -SimpleMatch -Quiet)) { $bg += $f }
    }
    Add 'NoBackgroundLocation' $(if ($bg.Count) { 'FAIL' } else { 'PASS' }) $(if ($bg.Count) { "found in: $($bg -join ', ')" } else { 'ACCESS_BACKGROUND_LOCATION not referenced by client source/manifests/settings' })

    # 6. secrets
    $secretPatterns = @(
        '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
        '\bgh[pousr]_[A-Za-z0-9]{30,}',
        '\bgithub_pat_[A-Za-z0-9_]{40,}',
        '\bAKIA[0-9A-Z]{16}\b',
        '\bAIza[0-9A-Za-z_-]{35}\b',
        '\bxox[abpr]-[A-Za-z0-9-]{10,}',
        '\bsk-(proj-)?[A-Za-z0-9_-]{24,}',
        '\b(sk|rk)_live_[A-Za-z0-9]{20,}',
        '-----BEGIN PGP PRIVATE KEY BLOCK-----',
        '^\s*AndroidKeystorePass:\s*\S+',
        '^\s*AndroidKeyaliasPass:\s*\S+'
    )
    $hits = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(cs|json|ya?ml|xml|asset|txt|md|ps1|psm1|gradle|properties|env|cfg|ini)$' })) {
        $p = Join-Path $RepoRoot $f
        if (-not (Test-Path $p)) { continue }
        if ((Get-Item -Force $p).Length -gt 2MB) { continue }
        foreach ($pat in $secretPatterns) {
            $m = Select-String -Path $p -Pattern $pat -Quiet
            if ($m -and $f -ne 'tools/qa/Test-RepoHygiene.ps1') { $hits += "$f ($pat)"; break }
        }
    }
    Add 'NoSecrets' $(if ($hits.Count) { 'FAIL' } else { 'PASS' }) $(if ($hits.Count) { "possible secrets: $($hits -join '; ')" } else { 'no private keys / tokens / keystore passwords found' })

    # 7. large files
    $big = @(); $warnBig = @()
    foreach ($f in $files) {
        $p = Join-Path $RepoRoot $f
        if (-not (Test-Path $p -PathType Leaf)) { continue }
        $len = (Get-Item -Force $p).Length
        if ($len -gt 50MB) { $big += "$f ($([math]::Round($len / 1MB, 1)) MB)" } elseif ($len -gt 10MB) { $warnBig += "$f ($([math]::Round($len / 1MB, 1)) MB)" }
    }
    $st = if ($big.Count) { 'FAIL' } elseif ($warnBig.Count) { 'WARN' } else { 'PASS' }
    Add 'LargeFiles' $st $(if ($big.Count -or $warnBig.Count) { (@($big) + @($warnBig)) -join ', ' } else { 'no tracked file > 10 MB' })

    # 8. PowerShell ASCII-only
    $nonAscii = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(ps1|psm1|psd1)$' })) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $RepoRoot $f))
        foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii += $f; break } }
    }
    Add 'PowerShellAscii' $(if ($nonAscii.Count) { 'FAIL' } else { 'PASS' }) $(if ($nonAscii.Count) { "non-ASCII bytes in: $($nonAscii -join ', ')" } else { 'all PowerShell files ASCII-only' })

    # 8b. (#31 follow-up) runtime serialization layout must not depend on UNITY_EDITOR / DEVELOPMENT_BUILD
    $condFields = @()
    $runtimeCs = @($files | Where-Object { $_ -match '^client/Assets/MetinAR/.+\.cs$' -and $_ -notmatch '/Editor/' -and $_ -notmatch '/Tests?/' })
    foreach ($f in $runtimeCs) {
        foreach ($hit in @(Find-MetinARConditionalSerializedFields -Lines @(Get-Content -LiteralPath (Join-Path $RepoRoot $f)))) { $condFields += "$($f):$($hit.Line) [$($hit.Condition)] $($hit.Text)" }
    }
    Add 'EditorOnlySerializedFields' $(if ($condFields.Count) { 'FAIL' } else { 'PASS' }) $(if ($condFields.Count) { $condFields -join '; ' } else { "$($runtimeCs.Count) runtime scripts, no serialized field under #if UNITY_EDITOR/DEVELOPMENT_BUILD" })

    # 8c. (#31 follow-up) managed stripping vs [SerializeReference] / link.xml (static report; types actually stripped need a build report)
    $psFile = Join-Path $RepoRoot 'client/ProjectSettings/ProjectSettings.asset'
    $stripLine = if (Test-Path $psFile) { @(Get-Content $psFile | Where-Object { $_ -match '^\s*managedStrippingLevel:' }) | Select-Object -First 1 } else { $null }
    $stripIdx = if (Test-Path $psFile) { @(Get-Content $psFile).IndexOf($stripLine) } else { -1 }
    $stripAndroid = 'default (Minimal for IL2CPP)'
    if ($stripLine -and $stripLine -notmatch '\{\}') {
        $after = @(Get-Content $psFile)[($stripIdx + 1)..($stripIdx + 8)]
        $a = @($after | Where-Object { $_ -match '^\s+Android:\s*(\d+)' }) | Select-Object -First 1
        if ($a -match 'Android:\s*(\d+)') { $stripAndroid = @('Disabled', 'Low', 'Medium', 'High', 'Minimal')[[int]$Matches[1]] }
    }
    $linkXml = @($files | Where-Object { $_ -match '^client/Assets/.*link\.xml$' })
    $serRef = @()
    foreach ($f in $runtimeCs) { $serRef += @(Select-String -LiteralPath (Join-Path $RepoRoot $f) -Pattern '\[SerializeReference\b' | ForEach-Object { "$($f):$($_.LineNumber)" }) }
    $stripRisk = ($serRef.Count -gt 0 -and $linkXml.Count -eq 0 -and $stripAndroid -match 'Low|Medium|High')
    Add 'ManagedStripping' $(if ($stripRisk) { 'WARN' } else { 'PASS' }) ("Android stripping={0}; link.xml={1}; [SerializeReference] in runtime={2}{3}" -f $stripAndroid, $(if ($linkXml.Count) { $linkXml -join ',' } else { 'none' }), $serRef.Count, $(if ($stripRisk) { " -> SerializeReference types may be stripped; add link.xml preserve entries: $($serRef -join ', ')" } else { '' }))

    # 9. test projects reference existing production source
    $badRefs = @()
    $testProjects = @(Get-MetinARTestProjects -RepoRoot $RepoRoot -Config $config | ForEach-Object { $_.Path })
    foreach ($proj in $testProjects) {
        $pp = Join-Path $RepoRoot $proj
        if (-not (Test-Path $pp)) { $badRefs += "missing project $proj"; continue }
        [xml]$x = Get-Content -Raw $pp
        foreach ($c in @($x.SelectNodes('//Compile[@Include]'))) {
            $inc = $c.GetAttribute('Include')
            $abs = Join-Path (Split-Path $pp -Parent) $inc
            if (-not (Test-Path $abs)) { $badRefs += "$proj -> $inc" }
        }
    }
    Add 'TestSourceLinks' $(if ($badRefs.Count) { 'FAIL' } else { 'PASS' }) $(if ($badRefs.Count) { $badRefs -join '; ' } else { "$($testProjects.Count) test projects (required + discovered), all Compile links resolve" })

    # 10. guarded files vs base
    if ($BaseRef) {
        $guarded = @('client/Assets/MetinAR/Scripts/Location/GeoCoordinate.cs', 'client/Assets/MetinAR/Scripts/Location/GeoMath.cs')
        $changed = @(& $git -C $RepoRoot diff --name-only "$BaseRef...HEAD" -- $guarded 2>$null)
        Add 'GuardedFiles' $(if ($changed.Count) { 'WARN' } else { 'PASS' }) $(if ($changed.Count) { "changed vs ${BaseRef}: $($changed -join ', ') (needs a dedicated reviewed issue)" } else { "GeoCoordinate/GeoMath unchanged vs $BaseRef" })
    }
}

if ($PassThru) { return $checks.ToArray() }
foreach ($c in $checks) {
    $color = switch ($c.Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'Gray' } }
    Write-Host ("[{0,-4}] {1,-20} {2}" -f $c.Status, $c.Name, $c.Detail) -ForegroundColor $color
}
if (@($checks | Where-Object { $_.Status -eq 'FAIL' }).Count) { exit 1 } else { exit 0 }

        $looksLikeKey = $name -match '(?i)\.(pem|p8|p12|jks|keystore|mobileprovision|key)
    $assetSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($f in $files) { if ($f.StartsWith('client/Assets/')) { [void]$assetSet.Add($f) } }
    $dirs = New-Object System.Collections.Generic.HashSet[string]
    $missingMeta = New-Object System.Collections.Generic.List[string]
    $orphanMeta = New-Object System.Collections.Generic.List[string]
    foreach ($f in $assetSet) {
        $rel = $f.Substring('client/Assets/'.Length)
        $parts = $rel -split '/'
        for ($i = 1; $i -lt $parts.Count; $i++) { [void]$dirs.Add('client/Assets/' + (($parts[0..($i - 1)]) -join '/')) }
        if ($f.EndsWith('.meta')) {
            $target = $f.Substring(0, $f.Length - 5)
            if (-not $assetSet.Contains($target) -and -not (@($assetSet | Where-Object { $_.StartsWith($target + '/') } | Select-Object -First 1).Count)) { $orphanMeta.Add($f) }
        } elseif (-not $assetSet.Contains($f + '.meta')) { $missingMeta.Add($f) }
    }
    foreach ($d in $dirs) { if (-not $assetSet.Contains($d + '.meta')) { $missingMeta.Add($d + '/') } }
    $metaDetail = "assets=$($assetSet.Count) missingMeta=$($missingMeta.Count) orphanMeta=$($orphanMeta.Count)"
    if ($missingMeta.Count) { $metaDetail += " e.g. $(($missingMeta | Select-Object -First 5) -join ', ')" }
    if ($orphanMeta.Count) { $metaDetail += " orphan e.g. $(($orphanMeta | Select-Object -First 5) -join ', ')" }
    Add 'MetaFiles' $(if ($missingMeta.Count -or $orphanMeta.Count) { 'FAIL' } else { 'PASS' }) $metaDetail

    # 4. build scenes (static read of EditorBuildSettings.asset)
    $ebs = Join-Path $RepoRoot 'client/ProjectSettings/EditorBuildSettings.asset'
    $enabled = @(); $cur = $null
    foreach ($l in (Get-Content $ebs)) {
        if ($l -match '^\s*- enabled: (\d)') { $cur = ($Matches[1] -eq '1') }
        elseif ($l -match '^\s*path: (.+)$' -and $null -ne $cur) { if ($cur) { $enabled += $Matches[1].Trim() }; $cur = $null }
    }
    $expected = @($config.expectedBuildScenes)
    $sceneProblems = @()
    for ($i = 0; $i -lt [math]::Max($expected.Count, $enabled.Count); $i++) {
        $e = if ($i -lt $expected.Count) { $expected[$i] } else { '<none>' }
        $a = if ($i -lt $enabled.Count) { $enabled[$i] } else { '<none>' }
        if ($e -ne $a) { $sceneProblems += "index $i expected $e got $a" }
    }
    foreach ($s in $enabled) { if (-not $assetSet.Contains('client/' + $s)) { $sceneProblems += "scene not tracked: $s" } }
    Add 'BuildScenes' $(if ($sceneProblems.Count) { 'FAIL' } else { 'PASS' }) $(if ($sceneProblems.Count) { $sceneProblems -join '; ' } else { (@(for ($i = 0; $i -lt $enabled.Count; $i++) { "$i=$($enabled[$i])" }) -join ', ') })

    # 5. background location
    $bg = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(cs|xml|gradle|kts|asset|json|properties)$' -and $_ -notmatch '^tools/qa/' -and $_ -notmatch '^tests/' })) {
        $p = Join-Path $RepoRoot $f
        if ((Test-Path $p) -and (Select-String -Path $p -Pattern 'ACCESS_BACKGROUND_LOCATION' -SimpleMatch -Quiet)) { $bg += $f }
    }
    Add 'NoBackgroundLocation' $(if ($bg.Count) { 'FAIL' } else { 'PASS' }) $(if ($bg.Count) { "found in: $($bg -join ', ')" } else { 'ACCESS_BACKGROUND_LOCATION not referenced by client source/manifests/settings' })

    # 6. secrets
    $secretPatterns = @(
        '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
        '\bgh[pousr]_[A-Za-z0-9]{30,}',
        '\bgithub_pat_[A-Za-z0-9_]{40,}',
        '\bAKIA[0-9A-Z]{16}\b',
        '\bAIza[0-9A-Za-z_-]{35}\b',
        '\bxox[abpr]-[A-Za-z0-9-]{10,}',
        '^\s*AndroidKeystorePass:\s*\S+',
        '^\s*AndroidKeyaliasPass:\s*\S+'
    )
    $hits = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(cs|json|ya?ml|xml|asset|txt|md|ps1|psm1|gradle|properties|env|cfg|ini)$' })) {
        $p = Join-Path $RepoRoot $f
        if (-not (Test-Path $p)) { continue }
        if ((Get-Item -Force $p).Length -gt 2MB) { continue }
        foreach ($pat in $secretPatterns) {
            $m = Select-String -Path $p -Pattern $pat -Quiet
            if ($m -and $f -ne 'tools/qa/Test-RepoHygiene.ps1') { $hits += "$f ($pat)"; break }
        }
    }
    Add 'NoSecrets' $(if ($hits.Count) { 'FAIL' } else { 'PASS' }) $(if ($hits.Count) { "possible secrets: $($hits -join '; ')" } else { 'no private keys / tokens / keystore passwords found' })

    # 7. large files
    $big = @(); $warnBig = @()
    foreach ($f in $files) {
        $p = Join-Path $RepoRoot $f
        if (-not (Test-Path $p -PathType Leaf)) { continue }
        $len = (Get-Item -Force $p).Length
        if ($len -gt 50MB) { $big += "$f ($([math]::Round($len / 1MB, 1)) MB)" } elseif ($len -gt 10MB) { $warnBig += "$f ($([math]::Round($len / 1MB, 1)) MB)" }
    }
    $st = if ($big.Count) { 'FAIL' } elseif ($warnBig.Count) { 'WARN' } else { 'PASS' }
    Add 'LargeFiles' $st $(if ($big.Count -or $warnBig.Count) { (@($big) + @($warnBig)) -join ', ' } else { 'no tracked file > 10 MB' })

    # 8. PowerShell ASCII-only
    $nonAscii = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(ps1|psm1|psd1)$' })) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $RepoRoot $f))
        foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii += $f; break } }
    }
    Add 'PowerShellAscii' $(if ($nonAscii.Count) { 'FAIL' } else { 'PASS' }) $(if ($nonAscii.Count) { "non-ASCII bytes in: $($nonAscii -join ', ')" } else { 'all PowerShell files ASCII-only' })

    # 8b. (#31 follow-up) runtime serialization layout must not depend on UNITY_EDITOR / DEVELOPMENT_BUILD
    $condFields = @()
    $runtimeCs = @($files | Where-Object { $_ -match '^client/Assets/MetinAR/.+\.cs$' -and $_ -notmatch '/Editor/' -and $_ -notmatch '/Tests?/' })
    foreach ($f in $runtimeCs) {
        foreach ($hit in @(Find-MetinARConditionalSerializedFields -Lines @(Get-Content -LiteralPath (Join-Path $RepoRoot $f)))) { $condFields += "$($f):$($hit.Line) [$($hit.Condition)] $($hit.Text)" }
    }
    Add 'EditorOnlySerializedFields' $(if ($condFields.Count) { 'FAIL' } else { 'PASS' }) $(if ($condFields.Count) { $condFields -join '; ' } else { "$($runtimeCs.Count) runtime scripts, no serialized field under #if UNITY_EDITOR/DEVELOPMENT_BUILD" })

    # 8c. (#31 follow-up) managed stripping vs [SerializeReference] / link.xml (static report; types actually stripped need a build report)
    $psFile = Join-Path $RepoRoot 'client/ProjectSettings/ProjectSettings.asset'
    $stripLine = if (Test-Path $psFile) { @(Get-Content $psFile | Where-Object { $_ -match '^\s*managedStrippingLevel:' }) | Select-Object -First 1 } else { $null }
    $stripIdx = if (Test-Path $psFile) { @(Get-Content $psFile).IndexOf($stripLine) } else { -1 }
    $stripAndroid = 'default (Minimal for IL2CPP)'
    if ($stripLine -and $stripLine -notmatch '\{\}') {
        $after = @(Get-Content $psFile)[($stripIdx + 1)..($stripIdx + 8)]
        $a = @($after | Where-Object { $_ -match '^\s+Android:\s*(\d+)' }) | Select-Object -First 1
        if ($a -match 'Android:\s*(\d+)') { $stripAndroid = @('Disabled', 'Low', 'Medium', 'High', 'Minimal')[[int]$Matches[1]] }
    }
    $linkXml = @($files | Where-Object { $_ -match '^client/Assets/.*link\.xml$' })
    $serRef = @()
    foreach ($f in $runtimeCs) { $serRef += @(Select-String -LiteralPath (Join-Path $RepoRoot $f) -Pattern '\[SerializeReference\b' | ForEach-Object { "$($f):$($_.LineNumber)" }) }
    $stripRisk = ($serRef.Count -gt 0 -and $linkXml.Count -eq 0 -and $stripAndroid -match 'Low|Medium|High')
    Add 'ManagedStripping' $(if ($stripRisk) { 'WARN' } else { 'PASS' }) ("Android stripping={0}; link.xml={1}; [SerializeReference] in runtime={2}{3}" -f $stripAndroid, $(if ($linkXml.Count) { $linkXml -join ',' } else { 'none' }), $serRef.Count, $(if ($stripRisk) { " -> SerializeReference types may be stripped; add link.xml preserve entries: $($serRef -join ', ')" } else { '' }))

    # 9. test projects reference existing production source
    $badRefs = @()
    $testProjects = @(Get-MetinARTestProjects -RepoRoot $RepoRoot -Config $config | ForEach-Object { $_.Path })
    foreach ($proj in $testProjects) {
        $pp = Join-Path $RepoRoot $proj
        if (-not (Test-Path $pp)) { $badRefs += "missing project $proj"; continue }
        [xml]$x = Get-Content -Raw $pp
        foreach ($c in @($x.SelectNodes('//Compile[@Include]'))) {
            $inc = $c.GetAttribute('Include')
            $abs = Join-Path (Split-Path $pp -Parent) $inc
            if (-not (Test-Path $abs)) { $badRefs += "$proj -> $inc" }
        }
    }
    Add 'TestSourceLinks' $(if ($badRefs.Count) { 'FAIL' } else { 'PASS' }) $(if ($badRefs.Count) { $badRefs -join '; ' } else { "$($testProjects.Count) test projects (required + discovered), all Compile links resolve" })

    # 10. guarded files vs base
    if ($BaseRef) {
        $guarded = @('client/Assets/MetinAR/Scripts/Location/GeoCoordinate.cs', 'client/Assets/MetinAR/Scripts/Location/GeoMath.cs')
        $changed = @(& $git -C $RepoRoot diff --name-only "$BaseRef...HEAD" -- $guarded 2>$null)
        Add 'GuardedFiles' $(if ($changed.Count) { 'WARN' } else { 'PASS' }) $(if ($changed.Count) { "changed vs ${BaseRef}: $($changed -join ', ') (needs a dedicated reviewed issue)" } else { "GeoCoordinate/GeoMath unchanged vs $BaseRef" })
    }
}

if ($PassThru) { return $checks.ToArray() }
foreach ($c in $checks) {
    $color = switch ($c.Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'Gray' } }
    Write-Host ("[{0,-4}] {1,-20} {2}" -f $c.Status, $c.Name, $c.Detail) -ForegroundColor $color
}
if (@($checks | Where-Object { $_.Status -eq 'FAIL' }).Count) { exit 1 } else { exit 0 }

        $looksLikeEnv -or $looksLikeKey
    })
    Add 'NoSensitiveFiles' $(if ($secretFiles.Count) { 'FAIL' } else { 'PASS' }) $(if ($secretFiles.Count) { "blocked filenames: $(($secretFiles | Select-Object -First 12) -join ', ')" } else { 'no tracked/staged credential files or backup dotenv files' })

    # 3. .meta consistency under client/Assets
    $assetSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($f in $files) { if ($f.StartsWith('client/Assets/')) { [void]$assetSet.Add($f) } }
    $dirs = New-Object System.Collections.Generic.HashSet[string]
    $missingMeta = New-Object System.Collections.Generic.List[string]
    $orphanMeta = New-Object System.Collections.Generic.List[string]
    foreach ($f in $assetSet) {
        $rel = $f.Substring('client/Assets/'.Length)
        $parts = $rel -split '/'
        for ($i = 1; $i -lt $parts.Count; $i++) { [void]$dirs.Add('client/Assets/' + (($parts[0..($i - 1)]) -join '/')) }
        if ($f.EndsWith('.meta')) {
            $target = $f.Substring(0, $f.Length - 5)
            if (-not $assetSet.Contains($target) -and -not (@($assetSet | Where-Object { $_.StartsWith($target + '/') } | Select-Object -First 1).Count)) { $orphanMeta.Add($f) }
        } elseif (-not $assetSet.Contains($f + '.meta')) { $missingMeta.Add($f) }
    }
    foreach ($d in $dirs) { if (-not $assetSet.Contains($d + '.meta')) { $missingMeta.Add($d + '/') } }
    $metaDetail = "assets=$($assetSet.Count) missingMeta=$($missingMeta.Count) orphanMeta=$($orphanMeta.Count)"
    if ($missingMeta.Count) { $metaDetail += " e.g. $(($missingMeta | Select-Object -First 5) -join ', ')" }
    if ($orphanMeta.Count) { $metaDetail += " orphan e.g. $(($orphanMeta | Select-Object -First 5) -join ', ')" }
    Add 'MetaFiles' $(if ($missingMeta.Count -or $orphanMeta.Count) { 'FAIL' } else { 'PASS' }) $metaDetail

    # 4. build scenes (static read of EditorBuildSettings.asset)
    $ebs = Join-Path $RepoRoot 'client/ProjectSettings/EditorBuildSettings.asset'
    $enabled = @(); $cur = $null
    foreach ($l in (Get-Content $ebs)) {
        if ($l -match '^\s*- enabled: (\d)') { $cur = ($Matches[1] -eq '1') }
        elseif ($l -match '^\s*path: (.+)$' -and $null -ne $cur) { if ($cur) { $enabled += $Matches[1].Trim() }; $cur = $null }
    }
    $expected = @($config.expectedBuildScenes)
    $sceneProblems = @()
    for ($i = 0; $i -lt [math]::Max($expected.Count, $enabled.Count); $i++) {
        $e = if ($i -lt $expected.Count) { $expected[$i] } else { '<none>' }
        $a = if ($i -lt $enabled.Count) { $enabled[$i] } else { '<none>' }
        if ($e -ne $a) { $sceneProblems += "index $i expected $e got $a" }
    }
    foreach ($s in $enabled) { if (-not $assetSet.Contains('client/' + $s)) { $sceneProblems += "scene not tracked: $s" } }
    Add 'BuildScenes' $(if ($sceneProblems.Count) { 'FAIL' } else { 'PASS' }) $(if ($sceneProblems.Count) { $sceneProblems -join '; ' } else { (@(for ($i = 0; $i -lt $enabled.Count; $i++) { "$i=$($enabled[$i])" }) -join ', ') })

    # 5. background location
    $bg = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(cs|xml|gradle|kts|asset|json|properties)$' -and $_ -notmatch '^tools/qa/' -and $_ -notmatch '^tests/' })) {
        $p = Join-Path $RepoRoot $f
        if ((Test-Path $p) -and (Select-String -Path $p -Pattern 'ACCESS_BACKGROUND_LOCATION' -SimpleMatch -Quiet)) { $bg += $f }
    }
    Add 'NoBackgroundLocation' $(if ($bg.Count) { 'FAIL' } else { 'PASS' }) $(if ($bg.Count) { "found in: $($bg -join ', ')" } else { 'ACCESS_BACKGROUND_LOCATION not referenced by client source/manifests/settings' })

    # 6. secrets
    $secretPatterns = @(
        '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
        '\bgh[pousr]_[A-Za-z0-9]{30,}',
        '\bgithub_pat_[A-Za-z0-9_]{40,}',
        '\bAKIA[0-9A-Z]{16}\b',
        '\bAIza[0-9A-Za-z_-]{35}\b',
        '\bxox[abpr]-[A-Za-z0-9-]{10,}',
        '^\s*AndroidKeystorePass:\s*\S+',
        '^\s*AndroidKeyaliasPass:\s*\S+'
    )
    $hits = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(cs|json|ya?ml|xml|asset|txt|md|ps1|psm1|gradle|properties|env|cfg|ini)$' })) {
        $p = Join-Path $RepoRoot $f
        if (-not (Test-Path $p)) { continue }
        if ((Get-Item -Force $p).Length -gt 2MB) { continue }
        foreach ($pat in $secretPatterns) {
            $m = Select-String -Path $p -Pattern $pat -Quiet
            if ($m -and $f -ne 'tools/qa/Test-RepoHygiene.ps1') { $hits += "$f ($pat)"; break }
        }
    }
    Add 'NoSecrets' $(if ($hits.Count) { 'FAIL' } else { 'PASS' }) $(if ($hits.Count) { "possible secrets: $($hits -join '; ')" } else { 'no private keys / tokens / keystore passwords found' })

    # 7. large files
    $big = @(); $warnBig = @()
    foreach ($f in $files) {
        $p = Join-Path $RepoRoot $f
        if (-not (Test-Path $p -PathType Leaf)) { continue }
        $len = (Get-Item -Force $p).Length
        if ($len -gt 50MB) { $big += "$f ($([math]::Round($len / 1MB, 1)) MB)" } elseif ($len -gt 10MB) { $warnBig += "$f ($([math]::Round($len / 1MB, 1)) MB)" }
    }
    $st = if ($big.Count) { 'FAIL' } elseif ($warnBig.Count) { 'WARN' } else { 'PASS' }
    Add 'LargeFiles' $st $(if ($big.Count -or $warnBig.Count) { (@($big) + @($warnBig)) -join ', ' } else { 'no tracked file > 10 MB' })

    # 8. PowerShell ASCII-only
    $nonAscii = @()
    foreach ($f in ($files | Where-Object { $_ -match '\.(ps1|psm1|psd1)$' })) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $RepoRoot $f))
        foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii += $f; break } }
    }
    Add 'PowerShellAscii' $(if ($nonAscii.Count) { 'FAIL' } else { 'PASS' }) $(if ($nonAscii.Count) { "non-ASCII bytes in: $($nonAscii -join ', ')" } else { 'all PowerShell files ASCII-only' })

    # 8b. (#31 follow-up) runtime serialization layout must not depend on UNITY_EDITOR / DEVELOPMENT_BUILD
    $condFields = @()
    $runtimeCs = @($files | Where-Object { $_ -match '^client/Assets/MetinAR/.+\.cs$' -and $_ -notmatch '/Editor/' -and $_ -notmatch '/Tests?/' })
    foreach ($f in $runtimeCs) {
        foreach ($hit in @(Find-MetinARConditionalSerializedFields -Lines @(Get-Content -LiteralPath (Join-Path $RepoRoot $f)))) { $condFields += "$($f):$($hit.Line) [$($hit.Condition)] $($hit.Text)" }
    }
    Add 'EditorOnlySerializedFields' $(if ($condFields.Count) { 'FAIL' } else { 'PASS' }) $(if ($condFields.Count) { $condFields -join '; ' } else { "$($runtimeCs.Count) runtime scripts, no serialized field under #if UNITY_EDITOR/DEVELOPMENT_BUILD" })

    # 8c. (#31 follow-up) managed stripping vs [SerializeReference] / link.xml (static report; types actually stripped need a build report)
    $psFile = Join-Path $RepoRoot 'client/ProjectSettings/ProjectSettings.asset'
    $stripLine = if (Test-Path $psFile) { @(Get-Content $psFile | Where-Object { $_ -match '^\s*managedStrippingLevel:' }) | Select-Object -First 1 } else { $null }
    $stripIdx = if (Test-Path $psFile) { @(Get-Content $psFile).IndexOf($stripLine) } else { -1 }
    $stripAndroid = 'default (Minimal for IL2CPP)'
    if ($stripLine -and $stripLine -notmatch '\{\}') {
        $after = @(Get-Content $psFile)[($stripIdx + 1)..($stripIdx + 8)]
        $a = @($after | Where-Object { $_ -match '^\s+Android:\s*(\d+)' }) | Select-Object -First 1
        if ($a -match 'Android:\s*(\d+)') { $stripAndroid = @('Disabled', 'Low', 'Medium', 'High', 'Minimal')[[int]$Matches[1]] }
    }
    $linkXml = @($files | Where-Object { $_ -match '^client/Assets/.*link\.xml$' })
    $serRef = @()
    foreach ($f in $runtimeCs) { $serRef += @(Select-String -LiteralPath (Join-Path $RepoRoot $f) -Pattern '\[SerializeReference\b' | ForEach-Object { "$($f):$($_.LineNumber)" }) }
    $stripRisk = ($serRef.Count -gt 0 -and $linkXml.Count -eq 0 -and $stripAndroid -match 'Low|Medium|High')
    Add 'ManagedStripping' $(if ($stripRisk) { 'WARN' } else { 'PASS' }) ("Android stripping={0}; link.xml={1}; [SerializeReference] in runtime={2}{3}" -f $stripAndroid, $(if ($linkXml.Count) { $linkXml -join ',' } else { 'none' }), $serRef.Count, $(if ($stripRisk) { " -> SerializeReference types may be stripped; add link.xml preserve entries: $($serRef -join ', ')" } else { '' }))

    # 9. test projects reference existing production source
    $badRefs = @()
    $testProjects = @(Get-MetinARTestProjects -RepoRoot $RepoRoot -Config $config | ForEach-Object { $_.Path })
    foreach ($proj in $testProjects) {
        $pp = Join-Path $RepoRoot $proj
        if (-not (Test-Path $pp)) { $badRefs += "missing project $proj"; continue }
        [xml]$x = Get-Content -Raw $pp
        foreach ($c in @($x.SelectNodes('//Compile[@Include]'))) {
            $inc = $c.GetAttribute('Include')
            $abs = Join-Path (Split-Path $pp -Parent) $inc
            if (-not (Test-Path $abs)) { $badRefs += "$proj -> $inc" }
        }
    }
    Add 'TestSourceLinks' $(if ($badRefs.Count) { 'FAIL' } else { 'PASS' }) $(if ($badRefs.Count) { $badRefs -join '; ' } else { "$($testProjects.Count) test projects (required + discovered), all Compile links resolve" })

    # 10. guarded files vs base
    if ($BaseRef) {
        $guarded = @('client/Assets/MetinAR/Scripts/Location/GeoCoordinate.cs', 'client/Assets/MetinAR/Scripts/Location/GeoMath.cs')
        $changed = @(& $git -C $RepoRoot diff --name-only "$BaseRef...HEAD" -- $guarded 2>$null)
        Add 'GuardedFiles' $(if ($changed.Count) { 'WARN' } else { 'PASS' }) $(if ($changed.Count) { "changed vs ${BaseRef}: $($changed -join ', ') (needs a dedicated reviewed issue)" } else { "GeoCoordinate/GeoMath unchanged vs $BaseRef" })
    }
}

if ($PassThru) { return $checks.ToArray() }
foreach ($c in $checks) {
    $color = switch ($c.Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'Gray' } }
    Write-Host ("[{0,-4}] {1,-20} {2}" -f $c.Status, $c.Name, $c.Detail) -ForegroundColor $color
}
if (@($checks | Where-Object { $_.Status -eq 'FAIL' }).Count) { exit 1 } else { exit 0 }
