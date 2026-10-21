# METINAR-014 — AR smoke gate stabilization + branch-aware QA regression

Role: QA/tests/perf. There are no gameplay changes and no AR/hand/map/backend/model behaviour changes. The only AR-owned file touched is
`client/Assets/MetinAR/Editor/ArEncounterSmokeTest.cs` (an Editor-only smoke test): its T15 exception check now classifies exceptions.
Base document: [METINAR-011-QA-GATES.md](METINAR-011-QA-GATES.md).

## 1. AR smoke flake (T15) — narrow classification

**Reproducer.** On a fresh `client/Library`, Unity 6000.6.4f1 starts `SearchInit.IndexationOnStartup` from an
editor delay-call and throws, outside any MetinAR code:

```
ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.
  at System.Collections.Generic.List`1[T].get_Item (System.Int32 index)
  at UnityEditor.Search.SearchDatabase+<EnumerateAll>d__78.MoveNext ()
  at System.Linq.Enumerable.First[TSource] (...)
  at UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase ()
  at UnityEditor.Search.SearchInit.IndexationOnStartup ()
  at UnityEditor.EditorApplication.Internal_CallDelayFunctions ()
```

The exception arrived while the smoke was listening, so T15 (`no exceptions logged`) failed even though no MetinAR code was involved.
This was reproduced on clean main 8557a3e during METINAR-011.

**Classification.** `client/Assets/MetinAR/Editor/SmokeExceptionClassifier.cs` puts each exception into one of three kinds:

| Kind | Rule | T15 |
|---|---|---|
| `MetinAR` | any frame in namespace `MetinAR.*` or any frame location under `Assets/MetinAR/` (message text and project folder name are ignored) | **FAIL** |
| `KnownEditorInfrastructure` | message starts `ArgumentOutOfRangeException: Index was out of range`, frames include **both** `UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase` and `UnityEditor.Search.SearchInit.IndexationOnStartup`, and **every** frame is on a short allow-list (List/Linq/SearchDatabase/IndexationOnStartup/EditorApplication delay-call/StackTraceUtility) | logged as `[ArSmoke] T15 ignored known editor-infrastructure exceptions (...)` |
| `Unknown` | everything else (default) | **FAIL** |

There is no pattern-based suppression. A new Unity or package exception fails until someone reproduces it and adds a separate signature with tests.

**Regression tests** (`tests/MetinAR.QA.Tests/SmokeExceptionClassifierTests.cs`, which run in CI via qa-static):
- the verbatim signature is classified as noise, in both Mono and Unity frame formats;
- an injected MetinAR exception fails, including a MetinAR frame inside the Search signature;
- an injected arbitrary Unity/runtime exception fails;
- near misses fail: the same message without Search frames, a different exception type, an extra unlisted frame, or only one required frame;
- null or empty input fails.

**Injected-exception gate (Unity).** `-Gates ArSmokeInjection` is explicit-only and not part of `All`. It runs the AR smoke three times with
`METINAR_SMOKE_INJECT` set:
- `metinar`: a real exception is thrown in MetinAR code and logged with `Debug.LogException`. T15 must FAIL, and no other test may fail.
- `unknown`: a synthetic `NullReferenceException` from `UnityEngine.XR.ARFoundation.ARSession:Update`. T15 must FAIL.
- `searchdb`: a synthetic copy of the known signature. The smoke must PASS.

The hook is inert when the variable is unset. After the gate runs, the AR results XML is deleted so a later `ArSmoke` cannot read an injected result.

```powershell
.\tools\qa\Invoke-MetinARQa.ps1 -Gates ArSmokeInjection -OutputDir Builds\qa\inject
```

## 2. Branch-aware QA runner

`qa-config.json`:
- `testProjects` lists the required projects: GeoCore.Tests and MetinAR.QA.Tests. A missing required project is a FAIL.
- `optionalTestProjectGlobs` (`tests/*/*.Tests.csproj`, `server/tests/*/*.Tests.csproj`, `client/Tests/*/*.Tests.csproj`) picks up
  whatever exists on the branch under test. Today that is AssetPipeline.Tests and MetinAR.Server.Tests. MapCore.Tests (METINAR-008B) and hand-tracking
  tests (METINAR-007) join automatically when they merge, and no unmerged branch is required. As a check, discovery run against
  `origin/feat/008b-osm-map-poc` returned `tests/MapCore.Tests/MapCore.Tests.csproj`.

`Get-MetinARTestProjects` is shared by the `DotNetTests` gate and Hygiene `TestSourceLinks`. The report lists the discovered projects.

## 3. Generic perf hooks

- `MetinARPerfProbe.Measure(metric)` is a disposable timing scope that reports through `RecordLatency`. It does nothing (no clock read, no allocation)
  outside development players. Well-known names: `MetinARPerfProbe.Metrics.HandInference` (`hand.inference`), `MapTileDecode`
  (`map.tile.decode`), `MapTileMesh` (`map.tile.mesh`). Feature owners wrap their own code, e.g.
  `using (MetinARPerfProbe.Measure(MetinARPerfProbe.Metrics.MapTileDecode)) { Decode(tile); }`. This issue does not touch any feature code.
- `perf-budgets.json` `latencyMetrics[]`: `{metric, maxAvgMs, maxP95Ms, required}`. `required:false` metrics are skipped until a build
  reports them. A budget with nothing measurable is `UNVERIFIED`, never PASS. `anyScene:true` (HandTracking) evaluates over samples from every
  scene. The legacy single `latencyMetric` schema is still accepted.
- Scene FPS and memory budgets are unchanged (`WorldMapPrototype`, `ArEncounterPrototype`, `CombinedArHandSword`).

## 4. Notes from PR #32 (FPP) and LuzMen

- PR #32 reported T15 failing on `SearchDatabase+<EnumerateAll>.MoveNext` ← `SearchInit.IndexationOnStartup`. That is the same signature
  as above (the full frame chain includes `GetDefaultSearchDatabase`). This branch classifies it; PR #32 gets the fix only after #26 merges.
- `ArgumentException: nativeTex can not be null` (ARCameraManager, ~12,700×) appears only when the AR smoke is started with `-nographics`
  (XR Simulation without a graphics device). The QA runner starts `WorldMapSmoke`, `ArSmoke` and `ArSmokeInjection` **with** graphics.
  These exceptions are *not* classified as noise, so a `-nographics` smoke run still fails T15 by design.
- `ArSmokeInjection` is scheduled before `AndroidBuild` in a multi-gate run (the Android build switches the Library target).
- An Android build failure caused by a full disk (`No space left on device`) is reported in the gate evidence.
- The #31 device acceptance checklist is in [qa/METINAR-031-FPP-ACCEPTANCE.md](qa/METINAR-031-FPP-ACCEPTANCE.md).

## 5. APK player-data validation (#31 crash follow-up)

The 52a1375 FPP APK crashed on AR scene load (`CachedReader::OutOfBoundsError`). It shipped `assets/bin/Data/sharedassets1.assets`
together with stale `sharedassets1.assets.split0..14` from an older Gradle export, and the player reads the chunks.

- `Test-MetinARApkPlayerData` (MetinARQa.psm1) is engine-free and uses the same rules as `AndroidPlayerDataValidator` from
  `feat/fpp-hands-aura-prototype` @ 2ec7a47. It fails when a file exists both whole and split, or when the chunks are not contiguous from `.split0`.
  It is reimplemented in PowerShell, so the QA gate does not depend on the unmerged branch and the two branches do not conflict. Once #32 merges,
  the C# validator in `MetinARAndroidDevBuild` fails the build, and this QA check independently audits the finished APK.
- It is wired into:
  - `ApkAudit` (`PLAYER DATA:` problems → FAIL, plus a layout summary in the evidence), which runs for every Android build in the QA runner;
  - `Invoke-MetinARDeviceSmoke.ps1`, which refuses to install an inconsistent APK;
  - the standalone `tools/qa/Test-ApkPlayerData.ps1 -ApkPath <apk>`, which needs no Unity, adb or phone.
- Tests in `Invoke-QaToolTests.ps1` (CI `qa-static`, no phone) cover:
  - the exact 52a1375 crash layout is rejected;
  - the clean layout passes;
  - a missing chunk is detected, as are chunks that don't start at 0;
  - backslashes are normalised and subfolders are ignored;
  - end-to-end checks over real zip files, including the CLI exit codes.

Hygiene, also static and run in CI:
- `EditorOnlySerializedFields` fails on serialized fields (`[SerializeField]`/`[SerializeReference]`/public fields) inside
  `#if`/`#else` regions that mention `UNITY_EDITOR` or `DEVELOPMENT_BUILD` in runtime scripts, because those make the Editor and player serialization layouts differ.
- `ManagedStripping` reports the Android stripping level, link.xml and `[SerializeReference]` usage. It warns when SerializeReference meets Low/Medium/High
  stripping with no link.xml. Detecting which scene types were actually stripped needs a build report and is out of scope.

## 6. Verification

See the AGENT READY FOR REVIEW comment on issue #26 for the run evidence.
