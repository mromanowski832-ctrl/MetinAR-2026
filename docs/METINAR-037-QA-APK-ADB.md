# METINAR-037 — separate QA APK package and adb procedure (#73, audit T19)

Owner: QA (#26). Source: audit `docs/audit/AUDIT_2026-10-21.md` §E.3 T19, problem P-10. Verified by: MetinAR Developer.

## 1. QA APK identity

| | Game (developer builds) | QA build |
|---|---|---|
| Build method | `MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk` | `MetinARAndroidDevBuild.BuildArEncounterQaApk` |
| applicationIdentifier | `com.metinar.game` | **`com.metinar.game.qa`** (`QaApkVariant.ApplicationIdentifier`) |
| Launcher label | MetinAR | **MetinAR QA** |
| APK | `client/Builds/Android/MetinAR-ar-encounter-dev.apk` | `client/Builds/Android/MetinAR-ar-encounter-qa.apk` |
| Scenes | WorldMapPrototype (startup) + ArEncounterPrototype | same |

- The QA build changes only `applicationIdentifier` and `productName`. Both are captured before the build and restored in
  a `finally` block (also after a failed build or an exception), then saved, so `ProjectSettings.asset` stays as committed
  (the same idea as `MapPocP8AndroidBuild`, #28). The log shows `QA variant id=...` before the build and
  `settings restored id=com.metinar.game ...` after it. `RESTORE MISMATCH` makes the build fail.
- `QaApkVariant.SideBySideProblem` refuses an id equal to the game id or one that isn't a valid Android id
  (unit-tested in `tests/MetinAR.QA.Tests/QaApkVariantTests.cs`).
- `tools/qa/qa-config.json` → `androidBuild`: `method`/`apk`/`expectedPackage` point at the QA variant. `gamePackage` is
  the game id. `auditAcceptedPackages` lists what ApkAudit accepts for `-ApkPath` (QA or game APK; `-ExpectedPackage`
  pins one). `protectedPackages` are the ids the device harness never replaces without an explicit switch. The
  permission allow-list uses `{package}.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` (AndroidX adds it per applicationId).
- Other test APKs use their own ids too: `com.metinar.game.mappoc` (Mapy, #28), `com.metinar.audiotest` (Dźwięk).

## 1a. ApkAudit: ARCore registrations, platform guard, package by purpose (audit UPDATE_1 N-01)

- **Why:** the QA APK `e18df69` passed the old ApkAudit (it contains `Unity.XR.ARCore.dll`, `libUnityARCore.so` and the
  subsystems manifest), yet `assets/bin/Data/RuntimeInitializeOnLoads.json` holds **0/11** `Unity.XR.ARCore`
  registrations. The editor's active target was not Android, and the app shows "AR unavailable".
- **Check:** `Test-MetinARApkArCore` (in `MetinARQa.psm1`, written independently of the C# validator) parses that JSON.
  It counts the `className.methodName` records listed in `qa-config.json` → `androidBuild.arCore.requiredRegistrations`
  (11, from the working APK 8d0acee) whose `assemblyName` is exactly `Unity.XR.ARCore`. It also requires
  `requiredEntries`. Missing or invalid JSON, any missing registration or a missing entry = **FAIL**
  (`ARCORE:` problem in ApkAudit). The device harness refuses to install such an APK.
- **Platform guard:** the runner starts the AndroidBuild gate with `-buildTarget Android` (`androidBuild.unityBuildTarget`).
  `BuildArEncounterQaApk` fails if `EditorUserBuildSettings.activeBuildTarget` is not Android.
- **applicationIdentifier:** `-ApkPurpose qa|game|mappoc` (`androidBuild.packagesByPurpose`) pins the expected id for
  `-ApkPath`, e.g. a release/game APK must be `com.metinar.game` and a QA APK `com.metinar.game.qa`. Without it an
  external APK may be either (`auditAcceptedPackages`), and our own build must be `.qa`. `-ExpectedPackage` overrides both.
- Fixtures: `tools/qa/tests/fixtures/runtime-init-e18df69-no-arcore.json` (the real e18df69 file) and
  `runtime-init-synthetic-arcore-ok.json` (the same file plus the 11 records).

## 2. Device harness (`tools/qa/Invoke-MetinARDeviceSmoke.ps1`)

- **`adb devices` before every step** (Install, ColdLaunch, PermissionUi, Process, Permissions, Memory, Background,
  Crash/logcat). The phone must be listed as `device`.
  - Short drop: waits up to `-AdbReconnectSeconds` (default 20 s) by polling `adb devices`. **adb is never restarted;
    no `kill-server`.** The gap is written to the report notes as `adb gap before step ... from .. to ..`. Logcat lines in
    that window may be missing.
  - Lasting loss: `Adb` = **FAIL** with the step and time. Every step not yet run = **UNVERIFIED**. Note
    `Logcat/host gap`. **Exit code 3**. The run never reads a failed or empty logcat dump as "no crash".
- **Install guard:** if the APK's package is in `protectedPackages` and already installed (e.g. the game build another
  bot is testing), Install = FAIL `would replace the installed com.metinar.game`. Override only with
  `-AllowReplaceInstalled` after a note in #33. The QA APK (`com.metinar.game.qa`) installs next to the game.
- **ARCore guard:** an APK failing the ARCore check is never installed (`APK rejected before install: ARCore
  registrations n/11`). There is no override.
- Covered without a phone by `tools/qa/tests/Invoke-DeviceSmokeSimulation.ps1` (fake adb: `adblost`, `adbblip`,
  guard on/off, QA install) and the unit tests in `tools/qa/tests/Invoke-QaToolTests.ps1`. A simulation is never
  device evidence.

## 3. LuzMen rule (from #33, MANAGER DECISION 2026-10-21 14:35)

Starting **or closing** Unity on LuzMen kills the adb server, so the phone drops out and logcat breaks. While A31 is
`LOCAL SLOT ACQUIRED`, nobody starts or closes Unity on LuzMen (batchmode included). If the harness reports `Adb` FAIL
or an adb gap, first check whether a Unity process started or ended at that time. Don't restart adb yourself.

## 4. Verification status

See #73 / its PR for the exact SHAs. Box/CI checks (unit tests, simulation, hygiene) run without Unity or a phone.
Unity compile, the QA APK build and the side-by-side install on A31 need LuzMen slots in #33. Until they run they are
**UNVERIFIED**.
