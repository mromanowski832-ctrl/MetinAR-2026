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

