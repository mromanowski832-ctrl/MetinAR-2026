# METINAR-011 — sample redacted QA reports

Real outputs from `LuzMen` (Windows 11, pl-PL locale, Unity 6000.6.4f1), isolated clone of `chore/011-qa-gates`,
2026-10-20 (UTC+2). Local user names were redacted by the runner (`C:\Users\<user>\…`). Logs stay in `Builds/qa/`
(git-ignored) and are not committed.

## 1. `Invoke-MetinARQa.ps1` (head `278e46a`)

```text
[PASS      ] Hygiene         9 checks, 0 fail, 0 warn
[PASS      ] DotNetTests     143/143 passed
[PASS      ] UnityCompile    exit=0 errorCS=0 warningCS=35 timedOut=False
[PASS      ] UnityQaChecks   missingScripts=0 scenes=2 prefabs=2 sceneOrder=OK
[PASS      ] WorldMapSmoke   15 tests, 0 failures, exit=0
[FAIL      ] ArSmoke         15 tests, 1 failures, exit=1
[PASS      ] ApkAudit        48.86 MB, perms=5, bgLocation=ABSENT
[WARN      ] WarningsSummary errorCS=0 warningCS=35 exceptions=2 (all Unity logs)

OVERALL: FAIL   report: C:\Users\<user>\MetinAR-011\Builds\qa\run2\qa-report.md
```

| Gate | Status | Time (s) | Detail |
|---|---|---:|---|
| Hygiene | **PASS** | 8.5 | 9 checks, 0 fail, 0 warn |
| DotNetTests | **PASS** | 17.4 | 143/143 passed (GeoCore 125 + QA 18) |
| UnityCompile | **PASS** | 137.4 | exit=0 errorCS=0 warningCS=35 |
| UnityQaChecks | **PASS** | 51.5 | missingScripts=0 scenes=2 prefabs=2 sceneOrder=OK |
| WorldMapSmoke | **PASS** | 124.4 | 15 tests, 0 failures |
| ArSmoke | **FAIL** | 86.4 | 15 tests, 1 failure (T15, see below) |
| AndroidBuild | **PASS** (run1, head `3925ae1`) | 1123.6 | exit=0, `MetinAR-ar-encounter-dev.apk` |
| ApkAudit | **PASS** | 0.9 | 48.86 MB, perms=5, bgLocation=ABSENT |
| WarningsSummary | **WARN** | 0 | errorCS=0 warningCS=35 exceptions=2 |

ApkAudit evidence (APK built in run1 from `3925ae1`; `client/` is byte-identical at `278e46a`):

```text
size: 51232700 bytes (48.86 MB)
sha256: 4acb82b1995dfc2838ba9fdd1ad352eb0d8bb943f41933c76b6b20da170187ce
abis: arm64-v8a  il2cpp: True  global-metadata: True
package: com.metinar.game  version: 1.0.0 (1)  minSdk: 31  targetSdk: 36  debuggable: True
launchable-activity: com.unity3d.player.UnityPlayerGameActivity
permissions: android.permission.INTERNET, android.permission.ACCESS_FINE_LOCATION, android.permission.ACCESS_COARSE_LOCATION, android.permission.CAMERA, com.metinar.game.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION
ACCESS_BACKGROUND_LOCATION: ABSENT
```

ArSmoke finding (pre-existing, reproduced on clean `main` `8557a3e` in the same fresh clone — exit 1, same failure):

```text
T15_NoExceptionsLogged: exceptions: ArgumentOutOfRangeException: Index was out of range.
  at UnityEditor.Search.SearchDatabase+<EnumerateAll>d__78.MoveNext ()
  at UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase ()
  at UnityEditor.Search.SearchInit.IndexationOnStartup ()
```

The exception comes from Unity's Editor search indexing on a freshly imported `Library`, not from MetinAR code; T14 and
all functional AR tests (T01–T14) passed. The QA runner reports it honestly as FAIL; fixing it means changing the AR
smoke test's exception filter (AR-owned test code), so it is left for an owner/controller decision.

Notes printed by the runner: tests ran with `DOTNET_ROLL_FORWARD=Major` (LuzMen has only the .NET 10 runtime);
Unity rewrote `InteractableMat.mat` and `LiberationSans SDF - Fallback.asset` during the run and `-RevertUnityRewrites`
restored them.

## 2. `Invoke-MetinARDeviceSmoke.ps1` (no phone connected)

```text
[UNVERIFIED] Device        no device connected (adb devices empty)
OVERALL: UNVERIFIED   (exit 2)
```

## 3. Device harness against the fake adb (CI simulation — NOT device evidence)

What a full report looks like (`tools/qa/tests/Invoke-DeviceSmokeSimulation.ps1`, synthetic data, serial/IP/coordinates
in the fake logcat are redacted):

| Gate | Status | Detail |
|---|---|---|
| Device | **PASS** | 1 authorized device: FakePhone, Android 12 (API 31), arm64-v8a |
| ColdLaunch | **PASS** | TotalTime=2345 ms (budget 15000 ms) |
| PermissionUi | **PASS** | no OS permission dialog in focus |
| Process | **PASS** | alive=True resumed=True |
| Permissions | **PASS** | ACCESS_FINE_LOCATION=granted, CAMERA=denied/unset |
| Background | **PASS** | background: alive=True resumed=False locationEntries=0; resume: resumed=True samePid=True |
| Crash | **PASS** | crash=0 anr=0 fatal=0 unityErrors=0 |
| Memory | **PASS** | TOTAL PSS=305 MB (budget 600 MB, WorldMapPrototype) |
| Cpu | **PASS** | CPU 45% (user 40% + kernel 5%) |
| Frames | **PASS** | SurfaceFlinger: 60 frames, avg 60 fps, janks=0 |
| PerfBudget | **PASS** | PROTOTYPE GATE: WorldMapPrototype=PASS (2 windows) |
| Thermal | **PASS** | thermalStatus=0 batteryTemp=31.2 C |

```text
10-20 04:30:29.000  4242  4242 I Unity   : GPS fix lat=<coord> lon=<coord> from <ip> serial <redacted-id>
```
