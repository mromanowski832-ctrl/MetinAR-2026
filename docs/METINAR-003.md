# METINAR-003 — Player location provider

## What was implemented

Game-facing location acquisition under `MetinAR.Location` (folder
`client/Assets/MetinAR/Scripts/Location/`):

| File | Role | UnityEngine? |
|------|------|--------------|
| `LocationFix.cs` | Immutable fix: `GeoCoordinate`, horizontal accuracy (m), timestamp (s) | No |
| `LocationProviderState.cs` | `Stopped`, `Starting`, `Running`, `PermissionDenied`, `LocationDisabled`, `Failed` | No |
| `ILocationProvider.cs` | `State`, `TryGetLatestFix`, `Start`, `Stop` | No |
| `MockLocationProvider.cs` | Deterministic mock; `SetCoordinate` / `MoveMeters` via `GeoMath.OffsetMeters` | No |
| `UnityLocationProvider.cs` | Thin Unity adapter (`Input.location`, Android `Permission`) over `DeviceLocationProviderCore` | Yes |
| `PlayerLocationService.cs` | Scene façade; owns one provider; Auto selection by platform; app pause/resume | Yes |
| `DeviceLocationProviderCore.cs` | **All** device permission + lifecycle decisions (METINAR-004B corrections) | No |
| `IDeviceLocationPlatform.cs` | Seam over device APIs (+ `DeviceLocationServiceStatus`, `DeviceLocationSample`) | No |
| `LocationPermissionRequest.cs` | One FINE+COARSE request; thread-safe aggregation of per-permission callbacks | No |
| `LocationPermissionLevel.cs` | `Unknown` / `None` / `Approximate` / `Precise` | No |
| `ISuspendableLocationProvider.cs` | `Suspend` / `Resume` for application pause | No |
| `ForegroundLocationLifecycle.cs` | Foreground-only policy used by `PlayerLocationService` | No |

`GeoCoordinate.cs` and `GeoMath.cs` were **not** modified.

Branch: `feat/003-player-location`. No PR / no merge as part of this task.

## Architecture

```text
PlayerLocationService (MonoBehaviour)
        │
        ├── UNITY_EDITOR              → MockLocationProvider
        ├── UNITY_ANDROID || UNITY_IOS → UnityLocationProvider
        ├── other platforms (Auto)    → MockLocationProvider
        └── explicit ProviderKind.Unity → UnityLocationProvider (diagnostics OK)
                                              │
                                              └── Input.location (+ Android PermissionCallbacks)
```

- **Explicit selection:** `PlayerLocationService.ProviderKind` = `Auto` | `Mock` | `Unity`.
- **Not a giant singleton:** the service only wires location; no map/monster/network/AR duties.
- **Mock default coordinate:** Null Island `(0, 0)` — clearly synthetic, not a user home.
- **Unity provider:** desired accuracy **5 m**, distance filter **1 m**, init timeout default **20 s**.
  Call `Tick()` each frame (done by `PlayerLocationService.Update`).

## Corrected Android permission + lifecycle flow (METINAR-004B corrections, PR #11 review 5565064786)

This section **supersedes** the METINAR-003A permission flow below (kept for history).

### R1 — one runtime request for FINE + COARSE

`UnityLocationProvider` is now an adapter; decisions are in pure `DeviceLocationProviderCore`.

1. `Start()`: if FINE **or** COARSE is already granted → no prompt; if `Input.location.isEnabledByUser`
   is false → `LocationDisabled`, else `Input.location.Start(5 m, 1 m)` once and arm the 20 s init timeout.
2. Otherwise exactly **one** `Permission.RequestUserPermissions(new[] { FINE, COARSE }, callbacks)`.
   The `PermissionCallbacks` object is stored on the `LocationPermissionRequest` and kept referenced until
   the aggregate result is resolved.
3. Callbacks (`PermissionGranted`, `PermissionDenied`, `PermissionDeniedAndDontAskAgain`,
   `PermissionRequestDismissed`) only **record** per-permission results (thread-safe). The core evaluates
   them on the main thread in `Tick()` / `Resume()`.
4. The **first** grant (fine or coarse, either callback order) starts acquisition **once**. A fine denial
   never cancels a coarse grant and does not detach the callbacks; the request stays pending until the
   other result arrives.
5. Denial is final (`PermissionDenied`) only when **neither** is granted: both denied, or the dialog was
   dismissed with nothing granted (no stuck `Starting`, no automatic re-prompt).
6. The init timeout stays **disarmed** while the permission decision is pending; it is armed only when
   `Input.location.Start` is actually called.
7. `Stop()` cancels the attempt: late results are recorded but never start acquisition. If the old
   dialog is still outstanding, a new `Start()` waits for it instead of issuing a second prompt; a result
   that already resolved for a cancelled attempt is not applied to the new attempt.
8. Accuracy: 5 m is the **desired** accuracy passed to Unity, not a guarantee. The granted level is
   exposed as `UnityLocationProvider.PermissionLevel` / `PlayerLocationService.PermissionLevel`
   (`Approximate` for coarse-only). Consumers must use `LocationFix.HorizontalAccuracyMeters`.

### R2 — foreground-only lifecycle

`PlayerLocationService.OnApplicationPause(bool)` → `ForegroundLocationLifecycle`:

- **Pause:** Unity provider → `Suspend()`: `Input.location.Stop()`, cached fix invalidated
  (`TryGetLatestFix` = false), timeout disarmed; an in-flight permission request is **kept** (on Android the
  permission dialog itself pauses the app). Mock provider → `Stop()` (coordinate kept).
- **Resume:** restarts only if acquisition was requested before the pause, the component is still
  enabled and permission still allows it. Never re-prompts: a pending dialog is awaited, a grant received
  while paused starts acquisition once in the foreground, a denial received while paused stays `PermissionDenied`.
- **Never restarted by resume:** explicit `StopProvider()`, disabled component (`OnDisable`),
  `PermissionDenied`; also `LocationDisabled` / `Failed` (an explicit `StartProvider()` retries).
- A `StartProvider()` while paused is deferred to resume (no background prompt or service).
- The world origin (`WorldMapOrigin`) is **not** reset on resume.
- No background permission, no background service, no native iOS plugin.

### Verification layers (kept separate)

| Layer | What | Result |
|-------|------|--------|
| Logic tests (.NET 8, no Unity) | `tests/GeoCore.Tests` links the production pure files; fake implements only the project's `IDeviceLocationPlatform` seam (no fake UnityEngine) | see METINAR-004.md |
| Editor tests (Unity 6000.6.4f1) | batch compile + Play Mode smoke runner incl. pause/resume cases M/N/O (mock provider in Editor) | see METINAR-004.md |
| Android APK build | ARM64 IL2CPP development APK; compiles the Android-only `RequestUserPermissions` path; merged-manifest check | see METINAR-004.md |
| Physical device | real permission dialog, approximate choice, background/resume on a phone | **UNVERIFIED** |

## Android permission flow (METINAR-003A, superseded)

Order of operations in `UnityLocationProvider.Start()` on Android player builds:

1. Check runtime permission first (`FineLocation` **or** `CoarseLocation` already granted).
2. **If granted:** check `Input.location.isEnabledByUser`.
   - Location service off → `LocationDisabled`.
   - Else → `Input.location.Start(5 m, 1 m)` and arm the 20 s init timeout.
3. **If not granted:** `Permission.RequestUserPermission(FineLocation, PermissionCallbacks)`.
   - Keep the `PermissionCallbacks` instance as a **field** while the dialog is pending (avoid GC).
   - At most **one** system permission request per `Start()` attempt.
   - `PermissionGranted` → re-verify Fine/Coarse → verify location available → begin `Input.location`.
   - `PermissionDenied` / `PermissionDeniedAndDontAskAgain` / `PermissionRequestDismissed` → `PermissionDenied`.
4. **Init timeout (20 s)** starts **only** when `Input.location.Start` is actually called —
   **not** while waiting for the permission dialog.

Previous bug: checking `isEnabledByUser` *before* requesting Android permission made first-time
users land in `LocationDisabled` without ever seeing the OS prompt.

## iOS permission flow (METINAR-003A)

- Do **not** early-return on `!Input.location.isEnabledByUser` before the first
  `Input.location.Start` — on iOS that flag stays false until the OS prompt is answered,
  so gating here would block the permission dialog forever.
- Call `Input.location.Start` via the Unity path; poll `LocationServiceStatus` in `Tick`.
- **Limitation:** denied vs global location-off may **not** be separable without native APIs.
  No native plugin is used; both paths may surface as `Failed` (service failed / timeout).
- Info.plist / Player Settings: `locationUsageDescription` is set in ProjectSettings.

## Mock timestamp (METINAR-003A)

- `SetTimestampSeconds(t)` sets the **exact** timestamp `t` with **no** unexpected increment on that call.
- Coordinate / accuracy / `MoveMeters` updates while running still advance the mock clock by **1 s**
  (documented movement time step).
- Covered by pure .NET tests in `MockLocationProviderTests`.

## Real vs mock

| | Mock | Unity |
|--|------|-------|
| Use | Editor, automated tests, offline / non-mobile | Android / iOS player builds |
| Fixes | Deterministic; `SetCoordinate` / `MoveMeters` | Live `Input.location.lastData` |
| Permissions | None | Android FINE + COARSE in one request; iOS via Unity location path |
| .NET tests | Linked into `GeoCore.Tests` | Adapter not linked (UnityEngine); its decision core `DeviceLocationProviderCore` is linked and tested |

## Permissions

- **Android:** `UnityEngine.Android.Permission.RequestUserPermissions` for FINE + COARSE in one request
  (METINAR-004B corrections); either grant is enough. One request per `Start()` attempt.
- **iOS:** Unity `Input.location` path only (Info.plist / Player Settings); no extra native plugin.
- **Denied:** state → `PermissionDenied`.
- **Location disabled in OS (Android, after permission granted):** state → `LocationDisabled`.
- **Init timeout / `LocationServiceStatus.Failed`:** state → `Failed`.

## Foreground-only

`UnityLocationProvider` starts/stops `Input.location` with the app in the foreground; since the
METINAR-004B corrections this is enforced through application pause/resume (see R2 above).
No background location mode, no significant-change monitoring, no always-on GPS service.
APK must **not** declare `ACCESS_BACKGROUND_LOCATION`.

## Accuracy and timeout

- Desired accuracy: **~5 meters** (requested, not guaranteed; approximate permission is far coarser)
- Minimum movement between updates: **~1 meter**
- Initialization timeout: **20 seconds**, armed only at `Input.location.Start`
- Horizontal accuracy on `LocationFix` must be finite and `>= 0` (NaN/Infinity rejected)

## Diagnostics

Under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, `PlayerLocationService` logs state / lat / lon / accuracy
on an interval (default 2 s), **not** every frame. Release player builds do **not** continuously log
exact coordinates.

## What compiled / tested (METINAR-003A)

- **.NET:** `dotnet test` on `tests/GeoCore.Tests` — **34/34 passed** (mock exact-timestamp +
  movement-advances-timestamp tests added; prior GeoCore + LocationFix + Mock tests still green).
  Ran on Windows with `DOTNET_ROLL_FORWARD=LatestMajor`.
- **Unity:** batchmode `-nographics -quit` compile of `client/` on Windows Editor `6000.6.4f1` —
  **exit 0**, **0× `error CS`**. Log: `C:\Users\mroma\MetinAR-2026-audit-logs\metinar-003a-compile.log`.
- **Android DEVELOPMENT APK:** **SUCCEEDED**
  - Path: `C:\Users\mroma\MetinAR-2026\client\Builds\Android\MetinAR-dev.apk`
  - Size: **52105579** bytes (~49.7 MiB)
  - ARM64 IL2CPP development (`BuildOptions.Development`); no release signing; no publish
  - Helper: `MetinAR.Editor.MetinARAndroidDevBuild.BuildDevelopmentApk`
  - Build log: `C:\Users\mroma\MetinAR-2026-audit-logs\metinar-003a-android-build.log`
  - Manifest permissions (`aapt dump permissions`):
    - `ACCESS_FINE_LOCATION` — **present**
    - `ACCESS_COARSE_LOCATION` — present (Unity companion)
    - `CAMERA` — **present** (AR)
    - `ACCESS_BACKGROUND_LOCATION` — **ABSENT** (confirmed)
- **REAL GPS ON PHYSICAL PHONE: UNVERIFIED** — no device GPS run was performed.

## Out of scope (intentionally not invented)

Map rendering, monster spawns, networking, AR session fusion, background GPS, geofencing,
and changes to `GeoCoordinate` / `GeoMath`.
