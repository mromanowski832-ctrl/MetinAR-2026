# METINAR-FPP-001 — animated first-person hands / sword / Aura

Issue: #31. Owner decision: 2026-10-21 (Europe/Warsaw).

## Accepted direction

The owner accepted a first-person view with virtual hands and a sword over the real AR camera background.
The enemy remains placed in AR world space. The right hand holds the sword. Attack is a button-driven swing.
Aura brings the left hand to the hilt, grips with both hands, plays an energy activation, and leaves an effect on the blade.
Showing a real hand to the camera is not required.

This replaces the previous real-hand/no-FPP requirement for the current prototype.
Issue #8 and PR #29 are deferred history, not completed features. No existing hand-tracking code or evidence is deleted.
The change does not approve copying Metin2 assets, animation files, shaders or effects.

## Initial implementation scope

Two virtual hands/forearms, one sword, one attack, one Aura activation, and an observable hit event against the existing AR spider.
Original procedural blockout art is an implementation starting point; its visual quality must be reviewed on device.
Durations, reach and hit volumes are technical prototype values, not approved final game balance.
No final damage, progression, resource costs, drops or server authority is introduced.

## Completion evidence

Do not mark this feature DONE from source, a passing pure test suite or a successful APK build alone.
Required: production-source unit tests, Unity 6000.6.4f1 compilation, AR/world-map regression, exact-SHA ARM64/IL2CPP build,
and physical A31 observation of hands, attack, two-hand Aura, valid hit versus miss, pause/resume and measured performance.
iOS remains UNVERIFIED until separately tested.

## Status

Implementation in progress on `feat/fpp-hands-aura-prototype`.
Unity, APK, device, visual and performance gates: NOT RUN in this environment.

## Implementation details

- `FppCombatTimeline`: attack windup/active/recovery, Aura gesture/expiry, input gating, one hit per target per swing.
- `FppHitGeometry`: blade segment versus local target body box; invalid data fails closed.
- `FppHandsView`: original procedural closed gloves/forearms, sword, two-hand hilt gesture, timed energy ribbons and activation ring.
- `FppCombatController`: created by the existing AR scene controller; hands attach to the AR camera, mob stays in world space.
- First two existing HUD actions become ATAK and AURA. Other actions/navigation keep their existing placeholder behavior.
- No model inference, new packages, shader search, permissions, scene YAML or device settings added.
- Materials clone the existing procedural spider's Abdomen and Eye0 references, keeping their shaders included in the build.
- Hit detection uses the same sword pose as rendering, sampling the crossed active interval at <=10 ms increments.
- Hits emit `HitDetected(mob, swingId)`, increment `HitCount`, and show "Trafienie!". HP/damage remain unchanged intentionally.
- Pause, focus loss, tracking loss, disable and exit cancel pending actions/Aura.

### Technical prototype values

Attack: 0.72 s; active hit window: 0.18–0.42 s.
Aura gesture: 1.15 s; activation: 0.65 s; blade effect: 6 s. No stat bonus or cost.
Blade extends from local y=0.13 to 0.94 m on the grip.
Target volume covers the existing procedural spider body (not legs/label), with ~2.5 cm tolerance.
Frame gap >0.25 s cancels pending actions; camera jump >0.25 m or >35 degrees per frame also cancels.
These thresholds are conservative technical starting points and require phone measurement.

## Verification and handoff

Local environment: no Unity Editor, .NET SDK or ADB device. No local Unity compile/APK/physical claims.
25 new pure test cases are added to the existing GeoCore.Tests project and reference production source.
The existing geo-core-tests GitHub workflow will execute them after push; record its actual result before merge.
The AR smoke now exercises Attack/Aura button wiring, one near hit, distant miss, spam prevention,
left-hand hilt pose, Aura no-damage, tracking loss and scene-exit cleanup.
The smoke uses synthetic session status and target repositioning for deterministic checks. It does NOT establish physical AR correctness.

Required on the Unity machine:
1. Open the exact PR head in Unity 6000.6.4f1; import/compile with zero C# errors.
2. Run `MetinAR.Editor.ArEncounterSmokeTest.Run` and `MetinAR.Editor.WorldMapPrototypeSmokeTest.Run`.
3. Build with existing `MetinAR.Editor.MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk`.
4. Record SHA, APK hash, permissions audit and exact test results.
5. On A31: place the spider; observe both virtual hands, sword and background; tap ATAK with blade crossing the body and then outside its reach;
   tap AURA and observe left hand at hilt, activation and continued blade effect; spam taps, pause/resume, lose tracking, exit/re-enter.
6. Measure scene FPS/frame time and memory during combined camera + mob + hands + Aura, and check logs for exceptions.
7. Record visual feedback: glove silhouette, hand/sword alignment, phone aspect ratio/cropping and occlusion. These are unverified.

## Known limitations

This is original procedural blockout art with code-driven poses, not polished rigged character animation.
No custom viewmodel camera is introduced: hands use normal AR-camera depth, so close virtual geometry can occlude them.
The target volume is specific to the current procedural spider. Integrating production mobs requires explicit collider/target data.
Temporal blade sampling is a bounded approximation; camera motion within a normal frame is not continuously swept.
A long stall cancels the action instead of applying an unseen delayed hit.
There is no final combat balance, HP damage, multiplayer validation, audio, haptics, resource cost or copied Metin2 content.
Physical Android/iOS quality and performance remain UNVERIFIED.


## A31 crash on entering AR (52a1375 dev APK) — build artifact, not FPP code

Symptom: tapping AR closed the game (native SIGTRAP in `Loading.Preload`, `CachedReader::OutOfBoundsError`,
"different serialization layout" errors and `sharedassets1.assets is corrupted`).

Root cause: the APK contained `assets/bin/Data/sharedassets1.assets` from the new build **and** stale
`sharedassets1.assets.split0..14` from an older build (dated 2026-10-20, containing the #8 sword/hand-tracking
assets). They came from the Gradle export folder in a `Library` copied from another checkout; the new build did not
overwrite or delete them. The Android player prefers split chunks, so it read the old AR-scene data against the new
`level1`/`globalgamemanagers` and crashed. The AR scene YAML and the FPP components (added at runtime via
`AddComponent`, no serialized fields) were not the cause; no runtime MetinAR script has serialized fields under
`#if UNITY_EDITOR`.

Fix: `MetinARAndroidDevBuild` deletes `Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets/bin/Data`
before building and, after the build, fails (exit 1) if the APK contains a serialized file both whole and split or
with non-contiguous chunks (`AndroidPlayerDataValidator`, tested in `tests/MetinAR.QA.Tests`).

## A31 retest fixes (APK 2ec7a47, owner retest 2026-10-21)

Findings 1, 3 and 4 of the retest (2 = real hands via #36 on `integration/fpp-art-preview`; 5 = HP damage is #47).
Projection checks use the ESTIMATED A31 portrait camera (69.4° vertical, 540×1200 ≈ 34.6° horizontal) and a
60–75° band; the physical FOV is still UNVERIFIED. Engine-free rules: `FppViewFraming`, `ArEncounterFraming`
(tests: `tests/GeoCore.Tests/FppFramingTests.cs`).

1. **Sword too big / too close.** The full-size sword (blade 0.94 m, guard 0.25 m) at ~0.5 m from a 35° wide portrait
   frame cannot stay out of the centre: the old Aura hold put the blade on the centre line with the guard across
   ~75 % of the width. Changes:
   - sword model (hilt, guard, pommel, blade, Aura ribbons) drawn at `SwordScale = 0.75` around the grip; hands stay
     life-size. The hit sweep samples the same scaled blade (`BladeBase/BladeTip` = 0.0975/0.705 m), so visible and
     tracked blades still match; the blade's reach is 25 % shorter.
   - rest `(0.09, -0.17, 0.46)` / Euler `(15, -30, -22)`: fist at NDC ≈ (0.63, -0.53), guard x 0.16..1.12 (≈ 48 % of the
     width, below the centre line), blade 100 % in frame up the right side, never closer than 0.70 NDC to the centre.
     The −30° yaw turns the guard towards the target (edge forward), which is what shrinks its on-screen width.
   - Aura hold `(0.10, -0.14, 0.50)` / `(10, -30, -18)`: also lower-right, blade ≥ 0.73 NDC from the centre.
   - #34 proposal `(0.06, -0.15, 0.44)` / `(15, 0, -14)` was not taken as is: at full scale its guard spans
     x −0.22..1.46 (84 % of the width, across the centre). Windup/end of the swing, timings, cancel and one-hit rule
     are unchanged; the swing still crosses the centre for its 0.24 s active window, which is intended.
   - left hand rest `(-0.08, -0.23, 0.42)` / `(0, 0, 12)` (was `(-0.19, -0.25, 0.43)`, off-screen): lower-left of the
     frame, NDC ≈ (-0.61, -0.79), so both hands are visible at rest; on the hilt during Aura at `LeftGripOffset = -0.09`
     (scaled hilt).
3. **Spider too close.** Placement targets closer than 1.5 m along the ground are skipped (reticle points 0.5/0.6/0.7/
   0.4/0.3 of the screen height, then the plane-centre fallback). If only closer ground is found, the place button
   stays disabled and the status says „Za blisko. Odsuń się albo wyceluj dalej w podłoże (min. 1,5 m).”. The spawned
   spider is drawn at visual scale 0.75 (leg span ≈ 0.93 m), so at 1.5 m it fits the estimated portrait width.
   Level 5, HP 120 and the 12 m aggro are not touched (they come from the request). Melee still needs the player
   to walk up to it; up close it will again fill most of the screen.
4. **Nameplate huge up close.** `ArEncounterMob.ClampLabel` keeps the nameplate at ≤ 9° angular width (HP bar about a
   quarter, name about a third of the estimated portrait width) when near and at its authored size when far, and
   lowers it out of the top HUD band (viewport y > 0.80) without pushing it onto the spider's body.

Editor checks: `ArEncounterSmokeTest` T10b (sword scale, rest pose, spider scale/stats, nameplate scale and HUD band)
and T12 (too-close synthetic target rejected with the hint, far target placed). The FPP hit tests run on the scaled
blade.

## Second owner retest (feat `8d0acee`, A31, 2026-10-21 ~14:00 Europe/Warsaw)

Owner findings and what this change does (damage itself is S2a #47 / PR #67):

| Finding | Change |
|---|---|
| Spider now too small | `ArEncounterFraming.MobVisualScale` 0.75 → **0.9** (between the first build, 1.0 = filled the screen, and 0.75). Visual only; level/HP unchanged; nameplate clamp unchanged. |
| Spider moves forward together with the phone | The mob is no longer held by a plane-attached anchor. It is put in session space (`XROrigin.TrackablesParent`, never under the camera), then a **free world `ARAnchor`** is requested with `ARAnchorManager.TryAddAnchorAsync(pose)` and the mob is re-parented to it (fallbacks: plane anchor, then session space). `[ArAnchor]` log every 2 s: anchor kind and tracking state, session state, camera and mob positions, ground distance — so the next device log shows whether the camera position is tracked while the mob stays at its spot. Root cause on the device is **UNVERIFIED** until that log is read. |

### `[ArAnchor]` log for the A31 v0 test (open acceptance bug: spider moves with the phone)

One line every 2 s and **immediately** on every session-tracking change (`event=session SessionTracking->SessionInitializing`
and back), anchor tracking change or anchor-kind change (`pending -> world`). Fields: `kind`, `anchorState`, `session`,
`cam` position, `camYaw` / `camPitch`, `mob` world position, `anchor` and `origin` positions, `parent`, and the
`ArAnchorProbe` sample against the placement baseline: `yaw` (rotation since placement), `side` (sidestep, m),
`closer` (+ closer / − farther, m), `d` / `d0` (ground distance now / at placement), `drift` (monster world drift, m),
`camMove`, `verdict` = `STAYS` / `DRIFT` / `FOLLOWS_CAMERA`, plus `maxDrift` and `followsCam=n/total`.

A31 script: place the spider, then (1) rotate in place ±90°, (2) sidestep 0.5 m left and right, (3) walk 1 m closer,
(4) walk 1 m back, (5) cover the camera for ~3 s (tracking loss) and uncover it (recovery). Pass = every line
`verdict=STAYS`, `followsCam=0/…`, `maxDrift` < 0.10 m, and `d` changing with `closer`. Editor smoke T09 asserts the
probe saw no follow and drift < 0.05 m. Device result: **UNVERIFIED** until that log is read.

#66 (placed mob disappears, `mobs 0 spawns 1`): AR Foundation destroys a removed anchor's GameObject together with the
monster parented to it (`ARTrackableManager`, `destroyOnRemoval = true`), and XR Simulation removes a plane-attached
anchor when its plane is merged away. The monster's anchor now has `destroyOnRemoval = false`; on removal the monster
is moved to session space at the same spot, the loss is logged as
`[ArAnchor] event=anchorRemoved kind=<world|plane> id=… action=RehomeAndReanchor … mobKept=true` and a new world anchor
is requested (at most 3 times). Any destruction of the monster is logged as
`[ArAnchor] event=mobDestroyed verdict=UNEXPECTED|expected …`. Editor smoke T09b removes the anchor and asserts the
monster survives and is re-anchored.

Owner request "an attacked mob must become aggressive, run to the player and attack" is **not** in this PR (audit plan
D1–D4, 2026-10-21): it is a separate `[READY]` issue, implemented after #67. Hands stay virtual on-screen hands with a
sword; there is no tracking of the player's real hands through the AR camera.

## Integration APK f194aa9: "AR niedostępne" on A31 — build-environment regression (fixed in the build script)

- Device log (A31, 13:42 Europe/Warsaw): `E Unity: Failed to load session subsystem.` from `ARCoreLoader.Initialize`, then
  `No active XRSessionSubsystem`, and `[ArEncounter] Starting -> Unsupported`. ARCore 1.57 is installed; feat `8d0acee`
  works on the same phone.
- APK diff f194aa9 vs 8d0acee: identical `Packages/`, `ProjectSettings/`, `Assets/XR/` and identical native
  `UnitySubsystems/UnityARCore/UnitySubsystemsManifest.json` (in both it declares only `inputs` — ARCore's session,
  camera, plane, raycast and anchor descriptors are **C#-registered**, never in that manifest). The difference is
  `assets/bin/Data/RuntimeInitializeOnLoads.json`: f194aa9 has **none** of the 11 `Unity.XR.ARCore` registrations
  (`ARCoreSessionSubsystem.RegisterDescriptor`, `ARCoreCameraSubsystem.Register`, ...); 8d0acee has all of them.
  f194aa9 also lacked one small Java dex (App UI activity).
- Cause: the integration checkout (`C:\Users\mroma\MetinAR-FPP-INTEG`, Library created from scratch) was built with
  `-executeMethod ...BuildArEncounterDevelopmentApk` **without `-buildTarget Android`**, so the editor's active target was
  Standalone. The build log shows it (`ARCore requires Gradle ... Cannot get path to the Gradle launcher unless the active
  build platform is Android`); the 8d0acee checkout already had Android active. Not caused by the #36 hands or by the merge.
- Fix (integration preview): `MetinARAndroidDevBuild` refuses to build when the active target is not Android, and after the
  build `ArCorePlayerDataValidator` rejects any APK whose `RuntimeInitializeOnLoads.json` lacks the ARCore session /
  camera / plane / raycast / anchor registrations (QA tests use the real JSON of both APKs as fixtures). Builds run with
  `-buildTarget Android`. The guard and the validator also live on PR #32 (the v0 APK branch); the validator requires
  all 11 `Unity.XR.ARCore` registrations and the build log prints `ARCore registrations 11/11`.
