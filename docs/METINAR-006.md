# METINAR-006 — AR encounter ground-placement prototype

Issue: #7. Branch: `feat/006-ar-ground-placement` (from `main` 357047f).

## Scope

A prototype AR scene that opens from the world map for the prototype test mob, scans for a horizontal ground
plane and places **one** procedural placeholder spider on it, anchored to the detected plane.

Out of scope here: combat, damage, drops, hand tracking, a map SDK, geospatial/VPS, final art and final button functions.
GPS is **not** used as the AR anchor: the world map only hands over *which* mob to show.

## Package versions (unchanged, already installed)

| Package | Version |
|---|---|
| Unity Editor | 6000.6.4f1 (Unity 6.6) |
| com.unity.xr.arfoundation | 6.6.2 (includes XR Simulation) |
| com.unity.xr.arcore | 6.6.2 |
| com.unity.xr.arkit | 6.6.2 |
| com.unity.xr.management | 4.7.0 |
| com.unity.xr.interaction.toolkit | 3.6.1 (not used by the AR scene) |
| com.unity.inputsystem | 1.20.0 |
| com.unity.render-pipelines.universal | 17.6.0 (ARBackgroundRendererFeature already present) |

XR loaders (unchanged): Android → ARCore loader, iOS → ARKit loader, Standalone/Editor → XR Simulation loader.
ARCore settings (unchanged since METINAR-002): requirement = Required, depth = Required.
Android player settings (unchanged): ARM64, IL2CPP, minSdk 31, targetSdk 36 (automatic = highest installed).
No new packages, no paid dependencies.

## Architecture

All code is in `client/Assets/MetinAR/Scripts/ArEncounter/` (namespace `MetinAR.ArEncounter`).

Pure C# (no UnityEngine, covered by `tests/GeoCore.Tests`):

- `ArEncounterRequest` — the encounter data (mob name, level, HP, aggressive, reason). `PrototypeTestSpider(reason)`
  = Trujący Pająk, Poz. 5, HP 120/120, aggressive.
- `ArEncounterHandoff` — static one-shot hand-over between scenes (`Submit` on the world map, `TryConsume` in AR).
- `ArEncounterState` / `ArSessionStatus` / `ArPlacementResult` — enums.
- `ArPlaneObservation` — what the state machine needs to know about one plane.
- `ArEncounterStateMachine` + `ArPlacementSettings` — the AR flow (below). Prototype values:
  minimum plane extent 0.3 m, session start timeout 20 s.

Unity runtime:

- `ArEncounterSceneController` — reads the AR Foundation session/plane/raycast state each frame into an
  `ArFrameSnapshot`, feeds the state machine, drives the reticle and HUD, spawns the mob, and exits.
  A `FrameSourceOverride` hook lets Play Mode tests feed synthetic frames (unsupported/timeout/tracking loss).
- `ArEncounterHud` — status text, "Postaw potwora", "Wyjdź z AR", failure panel, placeholder buttons, radar dot.
- `ArEncounterMob` — the placed enemy; name/level/HP label billboarded toward the AR camera.
- `ArEncounterLauncher` — on the world-map controller; on `EncounterRequested` it submits the request for the
  requested mob and loads the AR scene (0.75 s delay so the existing world-map toast is visible).
- `ArEncounterScenes` — scene path constants.

World-map changes (minimal): `WorldMapPrototypeController.RequestArEncounter(reason, showToast, mob)` records
`LastEncounterMob`; `MobInfoPanel` ("Wejdź do AR") and `PrototypeMob` (aggro) pass their mob. Existing behavior is unchanged.

Editor:

- `ArEncounterPrototypeSceneBuilder.BuildAll` — generates the procedural art, prefabs, AR scene, registers build
  scenes (WorldMapPrototype index 0, ArEncounterPrototype index 1) and adds the launcher to the world map.
- `ArEncounterSmokeTest.Run` — Play Mode smoke test (below).
- `MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk` — dev APK with both scenes.

## Scene: `Assets/MetinAR/Scenes/ArEncounterPrototype.unity`

- `AR Session` (+ `ARInputManager`).
- `XR Origin` → `Camera Offset` → `Main Camera` (`ARCameraManager`, `ARCameraBackground` = live camera feed,
  `TrackedPoseDriver` with the handheld-AR bindings); on the origin: `ARPlaneManager` (horizontal only,
  `ArGroundPlane.prefab`), `ARRaycastManager`, `ARAnchorManager`.
- `PlacementReticleRoot` — ground reticle shown while a target exists.
- `AR_HUD_Canvas` (portrait 1080×1920 reference) — owner visual direction, procedural dark-fantasy look:
  top-left portrait card with level/HP/MP, top-right circular radar, quest tracker, compact circular action buttons
  on the right, bottom nav bar (Ekwipunek / Umiejętności / Zadania / Menu), status line, "Postaw potwora",
  "Wyjdź z AR", failure panel, toast. The centre stays clear.
- `ArPrototypeSpider.prefab` — original primitive-built spider (~1.3 m span), blob shadow, label with name,
  "Poz. 5" and HP bar above it.

All textures/materials in `Assets/MetinAR/Art/ArEncounter` are generated procedurally by the builder.
No Metin2 or third-party proprietary assets.

**Placeholder buttons** (action I–III, nav bar, radar/quest) have no gameplay function: they only show
"Funkcja jeszcze nieprzypisana (prototyp)". Their final functions are an owner decision.

## Scene flow

1. World map: "Wejdź do AR" in the mob panel, or aggro of the aggressive mob, raises `EncounterRequested`.
2. `ArEncounterLauncher` submits an `ArEncounterRequest` for that mob and loads `ArEncounterPrototype`.
3. AR scene consumes the request (falls back to the prototype spider if opened directly), checks availability, scans.
4. Player taps "Postaw potwora" → one spider is spawned on the plane and attached to an `ARAnchor` on it.
5. "Wyjdź z AR" (or the failure panel's back button) clears the hand-off, resets the AR session and loads the world map.

## AR states

| State | Meaning | UI |
|---|---|---|
| Starting | availability check / session not running yet | "Uruchamianie AR..." |
| Scanning | session running, no suitable horizontal plane | "Skanuj teren…", place button disabled |
| ReadyToPlace | a horizontal, upward-facing, tracking plane ≥ 0.3 × 0.3 m, not subsumed | reticle + enabled "Postaw potwora" |
| Placed | exactly one mob spawned; further taps return `AlreadyPlaced` | plane visuals hidden; "Szukam otoczenia…" when tracking degrades (mob hidden, combat paused; ARSession reset after 15 s, #31); „Podejdź – x,x m” while out of attack reach (v0 walk-up, #31 18:58) |
| Unsupported | device has no AR support (terminal, no crash) | "AR niedostępne" panel + back to map |
| Failed | session did not start within 20 s or reported an error; recovers if the session starts | "Błąd sesji AR" panel + back to map |

Placement target: the centre-screen raycast (viewport points 0.5/0.5, 0.5/0.4, 0.5/0.3) against plane polygons,
falling back to the centre of the nearest suitable plane.

**Prototype decision, needs owner confirmation:** placement is "aim + confirm button" (not tap-anywhere).
Tap-to-place versus confirm is marked as a prototype choice.

## Tests

Pure (.NET 8, `dotnet test tests/GeoCore.Tests`): 125 passed, 0 failed — 103 existing (GeoCoordinate/GeoMath and
earlier tests untouched) + 22 new in `ArEncounterStateMachineTests.cs` (state machine, request validation, hand-off).

Unity 6000.6.4f1 batch compile (LuzMen): exit 0, 0 `error CS`.

AR Play Mode smoke (`-batchmode -executeMethod MetinAR.Editor.ArEncounterSmokeTest.Run`, XR Simulation loader +
synthetic frames), JUnit XML at `Builds/metinar-006-ar-smoke-results.xml`: 15 tests, 0 failures.

| Test | Covers |
|---|---|
| T01 | world map has the launcher; build scenes: WorldMap index 0, AR index 1 |
| T02 | 0 missing scripts in both scenes and both prefabs |
| T03 | real `Button.onClick` "Wejdź do AR" → request handed off → AR scene opens |
| T04 | AR scene consumed the request; AR Session, XR Origin, camera, managers, HUD present |
| T05 | before ground, nothing can be placed |
| T06 | XR Simulation session runs and scans |
| T07 | simulated scan sweep (XR Simulation camera tilted toward the floor) → real simulated plane detected → ReadyToPlace |
| T08 | "Postaw potwora" spawns exactly one anchored spider on the ground plane, label correct |
| T09 | repeated taps never duplicate; position stays stable |
| T10 | placeholder buttons only show the notice and change nothing |
| T11 | "Wyjdź z AR" returns to the world map |
| T12 | synthetic frames: scanning → ready → placed, single spawn, tracking-limited stays Placed |
| T13 | unsupported: clear, terminal, no crash, back button works |
| T14 | start timeout → Failed → recovery |
| T15 | 0 exceptions / errors logged |

XR Simulation only rescans its environment while the simulated device camera moves (> 2.5 cm or > 4°), exactly like a
real phone, so T07 moves the simulation camera (`SimulationCameraPoseProvider` transform) in a slow sweep.
In the simulated run the placement target came from the nearest-plane fallback, not the centre-screen ray.

World-map smoke (`MetinAR.Editor.WorldMapPrototypeSmokeTest.Run`): 15 tests, 0 failures; tests E/F now also check
the hand-off request (panel and aggro) without loading the AR scene.

Android: `MetinAR.Editor.MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk` →
`client/Builds/Android/MetinAR-ar-encounter-dev.apk` (development build, ARM64/IL2CPP, both scenes).
See the PR for size, SHA-256 and the manifest check.

## Physical-device limitations

- **Physical AR is UNVERIFIED.** No phone was connected (`adb devices` empty); nothing in this issue was observed on a device.
- Real plane detection quality, the camera feed, anchor stability, label legibility outdoors and HUD readability
  on a real screen are untested.
- ARCore is set to *Required* and depth to *Required* (unchanged project settings): on a device without ARCore /
  Google Play Services for AR the app should show the Unsupported state; whether the owner's test phone supports
  ARCore is not verified here.
- XR Simulation and synthetic frames are not a substitute for a phone test.

## Owner checks on a phone

1. Install the dev APK, open the world map, tap the spider → "Wejdź do AR"; grant camera permission.
2. Point at the floor and move the phone slowly; the reticle should appear on the ground and "Postaw potwora" enable.
3. Tap it once: one spider on the ground with name, "Poz. 5" and HP 120/120 above it; tap again → no duplicate.
4. Walk around it: it should stay on the same spot of the floor.
5. "Wyjdź z AR" returns to the world map.
