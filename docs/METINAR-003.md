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
| `UnityLocationProvider.cs` | Wraps `Input.location` (foreground); Android permission helper | Yes |
| `PlayerLocationService.cs` | Scene façade; owns one provider; Editor → mock, device → Unity | Yes |

`GeoCoordinate.cs` and `GeoMath.cs` were **not** modified.

Branch: `feat/003-player-location` (from `main` @ `805c7fe`). No PR / no merge as part of this task.

## Architecture

```text
PlayerLocationService (MonoBehaviour)
        │
        ├── UNITY_EDITOR / explicit Mock → MockLocationProvider
        └── device / explicit Unity     → UnityLocationProvider
                                              │
                                              └── Input.location (+ Android Permission API)
```

- **Explicit selection:** `PlayerLocationService.ProviderKind` = `Auto` | `Mock` | `Unity`.
  `Auto` resolves to Mock in the Editor and Unity on player builds.
- **Not a giant singleton:** the service only wires location; no map/monster/network/AR duties.
- **Mock default coordinate:** Null Island `(0, 0)` — clearly synthetic, not a user home.
- **Unity provider:** desired accuracy **5 m**, distance filter **1 m**, init timeout default **20 s**.
  Call `Tick()` each frame (done by `PlayerLocationService.Update`).

## Real vs mock

| | Mock | Unity |
|--|------|-------|
| Use | Editor, automated tests, offline dev | Physical / mobile player builds |
| Fixes | Deterministic; `SetCoordinate` / `MoveMeters` | Live `Input.location.lastData` |
| Permissions | None | Android fine/coarse; iOS via Unity location path |
| .NET tests | Linked into `GeoCore.Tests` | Not linked (UnityEngine) |

## Permissions

- **Android:** `UnityEngine.Android.Permission` for fine location (coarse accepted if already granted).
  At most **one** request per `Start()` attempt — does not spam the permission dialog.
- **iOS:** Unity `Input.location` path only (Info.plist / Player Settings); no extra native plugin code here.
- **Denied:** state → `PermissionDenied`.
- **Location disabled in OS:** state → `LocationDisabled`.
- **Init timeout / `LocationServiceStatus.Failed`:** state → `Failed`.

## Foreground-only

`UnityLocationProvider` starts/stops `Input.location` with the app in the foreground.
No background location mode, no significant-change monitoring, no always-on GPS service.

## Accuracy and timeout

- Desired accuracy: **~5 meters**
- Minimum movement between updates: **~1 meter**
- Initialization timeout: **20 seconds** (configurable on the provider / service)
- Horizontal accuracy on `LocationFix` must be finite and `>= 0` (NaN/Infinity rejected)

## Diagnostics

Under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, `PlayerLocationService` logs state / lat / lon / accuracy
on an interval (default 2 s), **not** every frame. Release player builds do **not** continuously log
exact coordinates.

## What compiled / tested

- **.NET:** `dotnet test` on `tests/GeoCore.Tests` — **32/32 passed** (prior GeoCore **17/17** still green
  plus LocationFix + MockLocationProvider tests). Ran on Windows with `DOTNET_ROLL_FORWARD=LatestMajor`.
- **Unity:** batchmode `-nographics -quit` compile of `client/` on Windows Editor `6000.6.4f1` —
  **exit 0**, **0× `error CS`**. Log: `C:\Users\mroma\MetinAR-2026-audit-logs\metinar-003-compile.log`.
- **Android APK/AAB build:** **ANDROID BUILD: NOT VERIFIED** (PlayerSettings remain Android / ARM64 / IL2CPP;
  SDK/NDK/OpenJDK present under Unity `6000.6.4f1` AndroidPlayer; no APK produced for this ticket).
- **REAL GPS ON PHYSICAL PHONE: UNVERIFIED** — no device GPS run was performed.

## Out of scope (intentionally not invented)

Map rendering, monster spawns, networking, AR session fusion, background GPS, geofencing,
and changes to `GeoCoordinate` / `GeoMath`.
