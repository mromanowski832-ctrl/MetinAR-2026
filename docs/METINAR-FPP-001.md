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
