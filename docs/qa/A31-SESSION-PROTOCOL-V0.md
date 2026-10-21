# A31 session protocol — loop v0 (owner's test, T07)

Fill in during the session. Every item: **PASS / FAIL / UNVERIFIED** + evidence. No evidence = UNVERIFIED (never PASS).
Source of items: [DEVICE-ACCEPTANCE-LOOP-V0.md](DEVICE-ACCEPTANCE-LOOP-V0.md) L1–L11 (+ L12–L15 automatic),
audit UPDATE_1 §3 (S3-A1…S3-A6, tolerances still a PROPOSAL) and §2.2 (Aura). QA (#26) verifies the evidence afterwards.

## Header

| Field | Value |
|---|---|
| **APK under test** | **v0 candidate** `MetinAR-v0-9368783-dev.apk`, package **`com.metinar.game`** |
| Commit | tag `v0-candidate-1` → `9368783` (#67 head; on `main` as squash `e1bdf80`, same client/server/tools code) |
| APK SHA256 (expected) | `a88ebbfe8de507985221a8512df1e8dd38a5423e7b9a95ddd934434953fc4803` (QA audit #33 6318408518). `Get-FileHash` right before install: `________` (must match, otherwise STOP) |
| APK bytes / versionName | 95,120,357 B / 1.0.0 (1) |
| ApkAudit #75 on this APK | **PASS** (QA, 16:2x): ARCore 11/11, package `com.metinar.game`, player data consistent, no `ACCESS_BACKGROUND_LOCATION`. Binding to `9368783`: code confirmed (build log + checkout HEAD); asset tree differs from the commit in 6 Unity re-serialized settings files |
| Device | Samsung Galaxy A31, Android `__` (no serial in GitHub) |
| Slot | #33 A31 ACQUIRED `<link>` … RELEASED `<link>`; no Unity start/close on LuzMen in between |
| Logcat | `adb logcat -c`, then `-v threadtime` to `<file>`, start `__:__` – end `__:__` Europe/Warsaw; gaps: `none / ADB LOST hh:mm–hh:mm` |
| Screen recording | `<file>` (timestamps below refer to it) |
| Tester / observer | executor: `____`, owner: `____` |

**Which APK:** the session uses the **v0 candidate** above. The QA APK from PR #75 (`com.metinar.game.qa`, "MetinAR QA",
commit `3b5c9c1`) is built from `main` **before** #67 (no damage/reward), so it **cannot** be used for L4–L11. It only
serves the #73 checks and installs **next to** `com.metinar.game` (different applicationId; it does not replace the game).
If it is installed during the session, open the right icon ("MetinAR" vs "MetinAR QA") and note it here: `____`.
APK `e18df69` is banned (0/11 ARCore).

## Pre-flight

| ID | Check | Pass criterion | Result | Evidence |
|---|---|---|---|---|
| P1 | APK identity | SHA256 of the file on LuzMen = header value | | `Get-FileHash` output |
| P2 | Player data | `Test-ApkPlayerData.ps1` → `[PASS]` (player data + ARCore 11/11) | PASS (QA audit) | #33 6318408518 |
| P3 | ApkAudit | `applicationIdentifier` = `com.metinar.game`, debuggable, minSdk 31, arm64-v8a, no `ACCESS_BACKGROUND_LOCATION`, 11/11 `Unity.XR.ARCore` registrations | PASS (QA audit) | #33 6318408518 |
| P4 | `adb devices` | exactly the A31 as `device` | | (serial masked) |
| P5 | install | `adb install -r` → `Success`; `dumpsys package com.metinar.game` versionName matches | | |
| P6 | logcat start | capture running before L1 | | |

## Loop v0

| ID | Steps | Pass criterion | Result | Evidence (log line / video time / owner quote) |
|---|---|---|---|---|
| L1 | launch from the launcher | world map < 15 s; no FATAL/ANR | | |
| L2 | open the encounter from the map | camera permission handled; real camera image; `[ArEncounter] state Starting -> Scanning` | | |
| L3 | scan floor, place mob | exactly one mob on the floor, fully in frame at 1–3 m; hands + sword visible; `[ArEncounter] spawned ...` + `[ArDamage] bound ...` | | **OWN** required |
| L4 | within reach, ATAK | one `[ArDamage] swing=<n>` per swing, HP drops, label updates | | |
| L5 | step back ≥ 3 m, ATAK ×3 | animation plays, **no** `[ArDamage] swing=` in that time range, HP unchanged | | time range: |
| L6 | spam ATAK/AURA 10 s | no double hit per swing id, nothing stuck, AURA alone does no damage, no exception | | |
| L7 | cover camera / blank wall 5 s, return | no crash; mob stays anchored or re-anchors; still exactly one mob (#66); input works | | |
| L8 | Home, 10 s, return | AR resumes, no black camera, HP unchanged, no exception | | |
| L9 | hit until death | HP 0, „Pokonany”, body gone after ~1 s, exactly **one** `[ArDamage] defeated ...` line, further ATAK no damage | | **OWN** required |
| L10 | read reward | once: `exp=+101 talary=+N` with N in **10–18** (warrior lvl 1 vs spider lvl 5); no second reward on re-entry | | **OWN** required |
| L11 | exit to map, map HUD, enter AR again | HUD `Lv`, EXP 101/160 (2.525 of 4 orbs), talary = reward; fresh consistent encounter, no duplicate mob/hands | | **OWN** required |

## Spider anchoring (audit UPDATE_1 §3, N-03) — logcat filter `[ArAnchor]` (every 2 s: kind, anchorState, session, cam, mob, d)

In v0 the mob must **not** move at all. Tolerances are the audit's PROPOSAL; record the measured values.

| ID | Steps | Pass criterion | Result | Measured (`mob` drift, `d`) / evidence |
|---|---|---|---|---|
| S3-A1 | place spider, stand 10 s | `kind` = world anchor (or explicit fallback, note which), `anchorState=Tracking`, `mob` drift ≤ 0.10 m | | |
| S3-A2 | rotate phone in place ±90° | `cam` orientation changes, `mob` ≤ 0.10 m; spider back on the same floor spot | | |
| S3-A3 | step 1 m left and right | `cam` moves ~1 m, `mob` unchanged; parallax visible, not stuck to the screen | | |
| S3-A4 | 1 m closer, then 2 m further | `d` follows (±0.2 m), `mob` unchanged; spider size changes on screen | | |
| S3-A5 | cover camera 5 s | no crash; `anchorState` → Limited/None; no mob movement, no attack; exactly one mob | | |
| S3-A6 | uncover | `anchorState=Tracking` within ≤ 5 s; spider at the same spot (≤ 0.20 m); ATAK works | | |

## Aura (audit UPDATE_1 §2.2)

| ID | Steps | Pass criterion | Result | Evidence |
|---|---|---|---|---|
| AU1 | tap AURA | two-hand gesture ~1.15 s + activation ~0.65 s; no attack during it | | VID time |
| AU2 | watch the blade | blue-gold effect on the blade ~6 s; no stat bonus/cost (prototype) | | VID time |
| AU3 | AURA near the mob | **no damage** from AURA itself: no `[ArDamage] swing=` line, HP unchanged | | time range |
| AU4 | spam (= L6) | no double hits, nothing stuck | | |
| AU5 | sound `AuraLoop` | owner's listening verdict (#42) | | **OWN** quote |

## Automatic / after the session

| ID | Pass criterion | Result | Evidence |
|---|---|---|---|
| L12 | whole AR part (L3–L9), ≥ 3 perf windows: `[MetinARPerf] scene=ArEncounterPrototype` fpsAvg ≥ 24, frame p95 ≤ 55 ms; map ≥ 28 fps (PROTOTYPE GATE) | | |
| L13 | `dumpsys meminfo com.metinar.game` after L3, L9, L11: TOTAL PSS ≤ 850 MB in AR (≤ 600 MB on the map), growth < 10% | | 3 values, MB |
| L14 | no FATAL/ANR/native crash, no `Exception` from `MetinAR.*` (= FAIL; read by hand until #75 merges); other Unity exceptions listed; `[ArDamage] hit ignored` only when expected; gaps listed as ADB LOST | | counts + file |
| L15 | `dumpsys thermalservice` / battery at the end: thermal status < 4; battery < 45 °C during ≤ 5 min | | |

## Summary

`<n> PASS, <n> FAIL, <n> UNVERIFIED`. FAIL items get a bug link (S3-A* FAIL → N-03; mob vanished → #66).
S2b closes only with all L items PASS + OWN for L3, L9, L10, L11; v0 acceptance also needs S3-A1…A6 and AU1–AU5 (audit UPDATE_1 §2, §5.1).
