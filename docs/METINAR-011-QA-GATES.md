# METINAR-011 — QA automation + mobile performance gates

Status: prototype QA layer. **All performance numbers below are PROTOTYPE GATES** — technical regression limits for
development builds, not final product promises. Nothing here changes gameplay, AR, hand/sword, map, backend or assets.

> METINAR-014 update: AR smoke T15 exception classification, branch-aware test discovery, `ArSmokeInjection` gate and
> multi-metric latency budgets: see [METINAR-014-QA-STABILIZATION.md](METINAR-014-QA-STABILIZATION.md).

## 1. What was added

| Path | Purpose |
|---|---|
| `tools/qa/Invoke-MetinARQa.ps1` | Unified local runner (Windows PowerShell 5.1+ / pwsh 7). PASS/FAIL per gate, Markdown + JSON report. |
| `tools/qa/Invoke-MetinARDeviceSmoke.ps1` | Physical Android smoke + performance harness over ADB, redacted logs. |
| `tools/qa/Test-RepoHygiene.ps1` | Unity-free static repo checks (local + CI). |
| `tools/qa/Test-DocLinks.ps1` | Offline check of relative Markdown links. |
| `tools/qa/Test-PrMetadata.ps1` | Lightweight PR title/body/branch check (CI, warnings only except hard errors). |
| `tools/qa/MetinARQa.psm1` | Shared helpers: Unity log/JUnit parsing, aapt badging, permission policy, redaction, logcat/dumpsys parsers, budgets. |
| `tools/qa/qa-config.json` | Expected scenes, smoke tests, build method, APK path, ABI, permission allow/forbid lists. |
| `tools/qa/perf-budgets.json` | Prototype performance budgets. |
| `tools/qa/tests/` | Dependency-free PowerShell tests + fixtures, and a fake-adb harness simulation (CI). |
| `client/Assets/MetinAR/Editor/MetinARQaChecks.cs` | Editor batch gate: missing-script scan + build scene/index check (read-only). |
| `client/Assets/MetinAR/Scripts/QA/MetinARPerfProbe.cs` | Passive perf probe, **Android/iOS development players only** (compiled out of Editor and release). |
| `client/Assets/MetinAR/Scripts/QA/FrameStatsAccumulator.cs` | Engine-free statistics + versioned `[MetinARPerf]` log-line format. |
| `tests/MetinAR.QA.Tests/` | xUnit tests for the pure QA logic (references the production source). |
| `.github/workflows/qa-static.yml` | Unity-free GitHub checks (no secrets, no licence, no paid services). |
| `.github/pull_request_template.md` | Merge-gate checklist. |
| `docs/qa/METINAR-011-sample-qa-report.md` | Sample redacted reports from a real LuzMen run. |

## 2. Quick start (Windows, repo root)

```powershell
# everything: hygiene, .NET tests, Unity compile, missing scripts/scenes, both Play Mode smokes, Android build, APK audit
powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1

# fast, no Unity
powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1 -Gates Hygiene,DotNetTests

# re-audit an existing APK without rebuilding
powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1 -Gates ApkAudit -ApkPath client\Builds\Android\MetinAR-ar-encounter-dev.apk

# physical phone (owner connects + authorizes USB debugging first)
powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARDeviceSmoke.ps1 -ApkPath client\Builds\Android\MetinAR-ar-encounter-dev.apk -SoakSeconds 60
```

Options: `-UnityPath` (or env `METINAR_UNITY`), `-SkipUnity`, `-SkipAndroid`, `-StopOnFail`, `-OutputDir`,
`-RevertUnityRewrites` (restores tracked files that Unity rewrote during the run and were clean before it).

Rules the runner enforces: Unity must not have the same project open (`Temp/UnityLockfile` locked → FAIL);
stale smoke XML / APK files are deleted before their gate so old results can never be reported as new;
outputs land in `Builds/qa/<timestamp>/` (git-ignored). Exit code 0 = no FAIL gate.

## 3. Gate catalogue

| Gate | Command (inside the runner) | PASS when | Where |
|---|---|---|---|
| Hygiene | `Test-RepoHygiene.ps1` | Unity pin = `6000.6.4f1`; no tracked caches/APK/AAB/keystores; every `client/Assets` file has a `.meta` and no orphan `.meta`; build scenes match config; no `ACCESS_BACKGROUND_LOCATION` in client source/manifests/settings; no secrets/tokens/keystore passwords; no file > 50 MB; PowerShell ASCII-only; test `Compile` links resolve; (CI) `GeoCoordinate`/`GeoMath` unchanged vs base | CI + local |
| DotNetTests | `dotnet test <project> -c Release` for every project in `qa-config.json` | all exit 0 (English output forced; on SDK-10-only machines `DOTNET_ROLL_FORWARD=Major`) | CI + local |
| UnityCompile | `Unity -batchmode -nographics -quit -projectPath client -logFile …` | exit 0, 0 `error CS`, no blocking package/licence/compile lines | LuzMen |
| UnityQaChecks | `-executeMethod MetinAR.Editor.MetinARQaChecks.Run -metinarQaResults <json> -metinarQaExpectedScenes "<a;b>" -metinarQaPrefabRoots Assets/MetinAR` | 0 missing scripts in enabled build scenes and `Assets/MetinAR` prefabs; enabled scenes exactly = expected list in order (index 0 WorldMap, 1 AR) | LuzMen |
| WorldMapSmoke | `-executeMethod MetinAR.Editor.WorldMapPrototypeSmokeTest.Run` | exit 0, JUnit `failures=0`, tests > 0 | LuzMen |
| ArSmoke | `-executeMethod MetinAR.Editor.ArEncounterSmokeTest.Run` (XR Simulation) | exit 0, JUnit `failures=0`, tests > 0 | LuzMen |
| AndroidBuild | `-nographics -executeMethod MetinAR.Editor.MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk` | exit 0, APK exists, 0 `error CS` | LuzMen |
| ApkAudit | zip listing + `aapt dump badging` | package `com.metinar.game`; debuggable dev build; minSdk ≥ 31; `arm64-v8a` only; `libil2cpp.so` + `global-metadata.dat`, no Mono DLLs; **no forbidden permission (incl. `ACCESS_BACKGROUND_LOCATION`)**; size ≤ budget. Unlisted permissions → WARN for review. Reports size + SHA-256 | LuzMen |
| WarningsSummary | aggregate of all Unity logs of the run | 0 `error CS` (warnings / exceptions → WARN) | LuzMen |

Configuration lives in `tools/qa/qa-config.json`: when METINAR-007 adds a scene or a new build method, update
`expectedBuildScenes` / `androidBuild` there in the same PR — the QA scripts need no code change.

## 4. GitHub checks (no Unity licence, no secrets)

`qa-static.yml` (push + pull_request, `contents: read`):

- **dotnet-tests** — `tests/MetinAR.QA.Tests` (the existing `geo-core-tests` workflow keeps running `GeoCore.Tests`).
- **qa-tooling** — parse every `.ps1/.psm1`; PSScriptAnalyzer (errors fail, warnings annotate);
  `tools/qa/tests/Invoke-QaToolTests.ps1`; `Invoke-DeviceSmokeSimulation.ps1` (harness against `fake-adb.sh`
  — logic/redaction/exit-code test only, **never device evidence**).
- **repo-checks** — `Test-RepoHygiene.ps1 -BaseRef origin/<base>` and `Test-DocLinks.ps1`.
- **pr-metadata** — `Test-PrMetadata.ps1`: errors for head = `main`, empty body, or a body reporting
  `ACCESS_BACKGROUND_LOCATION PRESENT`; warnings for missing `METINAR-<n>`, linked issue, merge-gate/verification,
  known limitations, or physical-device status.

Cloud Unity builds were deliberately not added (they need Unity activation secrets).

## 5. Physical Android smoke harness

`Invoke-MetinARDeviceSmoke.ps1` uses the Unity-bundled `adb`. It requires exactly one authorized device
(otherwise exit 2 and everything `UNVERIFIED`; with an `unauthorized` device it asks the owner to accept the USB
debugging prompt — it never accepts it).

| Step | What it does |
|---|---|
| Install / update | `adb install -r -t` — **never `-g`**, runtime permissions are not auto-granted |
| Cold launch | `am force-stop`, `logcat -c`, `am start -W` → status + `TotalTime` (budget 15 s) |
| Permission UI | detects `permissioncontroller` in focus; **never taps it**. `-PermissionWaitSeconds N` waits for the owner to decide on the phone |
| Process state | `pidof` + resumed activity after `-LaunchWaitSeconds` + `-SoakSeconds` |
| Permissions | runtime grant state (FINE/COARSE/CAMERA…) from `dumpsys package`; FAIL if background location is granted |
| Foreground/background | HOME → still alive, not resumed, `dumpsys location` entries for the app while backgrounded (WARN) → relaunch → resumed with the same pid |
| Crash / ANR / FATAL | `logcat -d -v threadtime -b main,system,crash,events`; only markers that belong to the app's pid/package count |
| Memory | `dumpsys meminfo` TOTAL PSS / native / graphics vs scene budget |
| CPU / GPU | `dumpsys cpuinfo` app CPU % (informational); GPU utilisation needs vendor tools/root → UNVERIFIED |
| Frames | `dumpsys SurfaceFlinger --latency` on the Unity SurfaceView (display-side FPS / janks, approximate) + `gfxinfo` |
| Perf budget | parses `[MetinARPerf]` lines and checks `perf-budgets.json` (first window skipped as warm-up) |
| Thermal / battery | `dumpsys thermalservice` status + `dumpsys battery` temperature/level |

Privacy: raw logcat is never written to disk. `logcat-redacted.txt` keeps only app / crash / ActivityManager lines
and redacts device serials, IPv4/IPv6, MAC, e-mails, keyed and bare coordinate pairs (`lat=`, `52.22977,21.01178`),
IMEI/15-digit IDs, Android IDs / long hex tokens and local user names in paths. Reports contain the phone model and
Android version only. Do not commit raw logs, screenshots with the status bar location, or bug reports.

### Owner checklist (cannot be automated)

1. Connect the phone, accept USB debugging, run the harness with `-PermissionWaitSeconds 120`.
2. On the phone answer the location prompt (test both *Allow while using* and *Deny* on separate runs) and the camera
   prompt when entering AR (both *Allow* and *Deny*).
3. Outdoors: GPS fix appears, mob distance changes while walking, poor-accuracy indoors does not trigger aggro.
4. AR: floor detected, spider placed once, stays anchored while walking around it; exit returns to the map.
5. Lock/unlock and app switch during map and AR; no crash, GPS stops in background (status bar location icon off).
6. Note device model, Android version, result per row of the regression matrix (section 7).

## 6. Performance gates (PROTOTYPE GATE)

Measured by `MetinARPerfProbe` in development players: one `[MetinARPerf] v=1` line every 5 s with frame time
(avg / p50 / p95 / max), frame spikes > 50 ms, main-thread time (`ProfilerRecorder "Main Thread"`, avg / p95 / max /
spikes), `Total Used`, `GC Reserved`, `System Used` memory, plus latency metrics reported through
`MetinARPerfProbe.RecordLatency(name, ms)`. Pause/resume resets the window so background time is not a spike.
Overhead: one frame-time sample per frame and a log line every 5 s; nothing runs in release builds or the Editor.

| Scenario | min avg FPS (worst 5 s window) | max frame p95 | max spikes > 50 ms / window | max main-thread p95 | max Unity total used | max PSS |
|---|---:|---:|---:|---:|---:|---:|
| World map (`WorldMapPrototype`) | 28 | 45 ms | 3 | 33 ms | 450 MB | 600 MB |
| AR encounter (`ArEncounterPrototype`) | 24 | 55 ms | 4 | 40 ms | 650 MB | 850 MB |
| Hand-tracking inference (METINAR-007) | — | — | — | — | — | — |
| Combined AR + hand + sword | 20 | 66 ms | 6 | 50 ms | 800 MB | 1000 MB |

| Other gate | Limit |
|---|---|
| Hand inference latency `lat.hand.inference` | avg ≤ 45 ms, p95 ≤ 70 ms (combined scene: ≤ 50 / ≤ 80 ms) |
| Development APK size | WARN > 110 MB, FAIL > 150 MB |
| Cold launch (`am start -W` TotalTime) | WARN > 15 s |
| Thermal status | WARN ≥ 2 (moderate), FAIL ≥ 4 (critical) |
| Battery temperature | WARN ≥ 40 °C, FAIL ≥ 45 °C |

Notes:

- Targets are conservative for a mid-range API 31 phone and a 30 fps-class AR prototype; they catch regressions,
  they are not quality targets. Tighten them once real baselines exist.
- **Hand-tracking latency is UNVERIFIED until METINAR-007 exposes it.** Integration for 007 (no QA change needed):
  `MetinAR.QA.MetinARPerfProbe.RecordLatency("hand.inference", ms);` around the inference call. The combined scene
  name is configured in `perf-budgets.json` (`CombinedArHandSword.sceneNames`); update it when 007 lands.
- Thermal / battery: a 1–2 minute smoke cannot show throttling. AR + camera + inference is the highest-risk combination;
  a 10-minute soak (`-SoakSeconds 600`) on the baseline phone is required before calling the combined scene stable.
- GPU pressure is not measurable without vendor tools; use Android GPU Inspector / Snapdragon Profiler / Arm
  Performance Studio manually when frame p95 fails but main-thread p95 passes (GPU-bound).

## 7. Regression matrix

Status today: everything physical is **UNVERIFIED** (no phone connected during METINAR-011).

| # | Area | Case | Expected | Method | Status |
|---|---|---|---|---|---|
| R1 | Platform | Android API 31 baseline phone (used since 003) | installs, cold launch < 15 s, no crash | device harness | UNVERIFIED |
| R2 | Platform | Android API 33 / 34 / 35 | same as R1; runtime-permission UI per version | device harness + checklist | UNVERIFIED (no device) |
| R3 | Platform | iOS | build + run | — | UNVERIFIED (no hardware / no iOS pipeline) |
| R4 | Location | permission allowed (precise) | fix, map follows player | checklist 2–3 | UNVERIFIED |
| R5 | Location | allowed approximate only | approximate state, no 5 m accuracy claim | checklist 2 | UNVERIFIED |
| R6 | Location | denied / dismissed | clear denied state, no re-prompt loop, no crash | checklist 2; pure tests cover logic | logic PASS (pure tests) / device UNVERIFIED |
| R7 | Location | poor GPS accuracy / jitter | no false aggro | pure tests (`AggroConfidenceGate`) + outdoor check | logic PASS / device UNVERIFIED |
| R8 | Camera | allowed when entering AR | camera feed, scanning | checklist 4 | UNVERIFIED |
| R9 | Camera | denied | no crash, a clear failure state and a working way back to the map (exact UX owned by the AR issue) | checklist 2 | UNVERIFIED |
| R10 | AR | ARCore supported | plane → single placement → anchored | AR smoke (XR Simulation) + checklist 4 | Editor PASS / device UNVERIFIED |
| R11 | AR | ARCore unsupported / missing | Unsupported panel, no crash | AR smoke T13 + device without ARCore | Editor PASS / device UNVERIFIED |
| R12 | Lifecycle | pause/resume on map | GPS suspended in background, resumes, same pid | device harness Background + pure tests | logic PASS / device UNVERIFIED |
| R13 | Lifecycle | pause/resume in AR | session recovers, mob not duplicated | checklist 5 | UNVERIFIED |
| R14 | Scenes | map → AR → map transitions | correct scene indices, hand-off consumed once | AR smoke T03/T11, UnityQaChecks | Editor PASS |
| R15 | Network | no network / airplane mode | prototype works offline (no backend yet); map provider / backend (008/010) must define offline behaviour | checklist | UNVERIFIED; re-evaluate when 008/010 land |
| R16 | Privacy | manifest | no `ACCESS_BACKGROUND_LOCATION`, no unlisted sensitive permission | ApkAudit + Hygiene | see report |
| R17 | Performance | world map / AR / combined budgets | section 6 | device harness PerfBudget | UNVERIFIED |
| R18 | Hand tracking | inference latency, combined AR + hand + sword | section 6 | device harness after 007 | UNVERIFIED (007 not merged) |

## 8. Merge gate (for every future PR)

The PR template (`.github/pull_request_template.md`) carries this table; every row must be PASS, NOT REQUIRED or an
explicit UNVERIFIED with a reason:

1. **Exact head SHA** the evidence was produced on (re-run after every push).
2. **Compile** — Unity batch exit 0, 0 `error CS`.
3. **Tests** — all pure test projects + both Play Mode smokes when runtime/scene code changed; GitHub checks green.
4. **Build** — Android ARM64/IL2CPP dev APK, size + SHA-256 when mobile runtime changed.
5. **Permissions** — ApkAudit: no `ACCESS_BACKGROUND_LOCATION`, no new permission without an approved issue.
6. **Privacy** — no serials, IPs, exact coordinates, keys or personal paths in commits, logs, screenshots.
7. **Physical-device evidence** — harness report + owner checklist, or `UNVERIFIED`.
8. **Known limitations** listed honestly.
9. **Recommendation** — MERGE / DO NOT MERGE with blocking items first.

## 9. Evidence from METINAR-011 (LuzMen, 2026-10-20)

Full outputs: [docs/qa/METINAR-011-sample-qa-report.md](qa/METINAR-011-sample-qa-report.md).

- Hygiene PASS; DotNetTests 143/143 (125 GeoCore + 18 QA); UnityCompile exit 0, 0 `error CS`; UnityQaChecks 0
  missing scripts, index 0 WorldMap / 1 AR; WorldMapSmoke 15/15; AndroidBuild PASS; ApkAudit PASS (48.86 MB,
  arm64-v8a IL2CPP, debuggable, 5 permissions, **no `ACCESS_BACKGROUND_LOCATION`**).
- **ArSmoke 14/15 — FAIL on T15** (Editor-internal `UnityEditor.Search.SearchDatabase` exception on a fresh
  `Library`). Reproduced on clean `main`, so it is pre-existing and not caused by this issue; left for an owner /
  controller decision because the fix belongs in the AR smoke test.
- Physical device: **UNVERIFIED** (no phone connected).

## 10. Known limitations of the QA layer

- Unity gates run only on Windows `LuzMen` (no licence-free cloud Unity). The runner needs the project closed in
  other Unity instances; agents working in parallel should use separate clones.
- The Play Mode smokes are the existing editor runners (no Unity Test Framework package); their results paths are
  fixed (`Builds/metinar-004a-smoke-results.xml`, `Builds/metinar-006-ar-smoke-results.xml`).
- `MetinARQaChecks` scans enabled build scenes and `Assets/MetinAR` prefabs, not third-party sample folders.
- SurfaceFlinger FPS is display-side and approximate; Unity frame/main-thread numbers come from the probe and need a
  development build.
- `dumpsys location` output differs between Android versions; background-location detection is a WARN heuristic.
- PSScriptAnalyzer is installed from PowerShell Gallery in CI (free, MIT); locally the runner needs no modules.
