# Independent QA acceptance list — #31 / PR #32 (METINAR-FPP-001)

Owner: QA (#26). This is a test checklist, not an implementation. Each item is checked against an **exact commit SHA and
APK SHA256**. A missing proof means **UNVERIFIED**. Green CI or Editor smokes never close device items (D*). Results are
reported on PR #32 / #31 in the format `ID | PASS/FAIL/UNVERIFIED | evidence (log line, video/screenshot ref, measurement)`.

Device: Samsung Galaxy A31 (Android 12, API 31), development APK built by `MetinAR.Editor.MetinARAndroidDevBuild.BuildArEncounterDevelopmentApk`.
Device use is coordinated in #33 (`LOCAL SLOT ACQUIRED/RELEASED`). Do not put pairing codes, IP addresses, serial numbers or GPS coordinates in GitHub.

## A. Build / static (LuzMen + CI)
| ID | Check | Pass criterion |
|---|---|---|
| A1 | Unity compile on the exact head | 0 `error CS` (`Invoke-MetinARQa.ps1 -Gates UnityCompile`) |
| A2 | Editor smokes (with graphics, **without** `-nographics`) | `ArEncounterSmokeTest` all PASS, including T10/T10b (attack/aura) and T15 (no unclassified exception). `WorldMapPrototypeSmokeTest` all PASS. Until #26 merges, a T15 failure whose only cause is the SearchDatabase startup signature is recorded as "known, #26" — never as PASS. |
| A3 | `-nographics` artefact | `nativeTex can not be null` from ARCameraManager appears only under `-nographics` (XR Simulation without a GPU). It is logged separately and not counted as a product exception. Smokes are run with graphics. |
| A4 | APK audit | `-Gates ApkAudit`: package `com.metinar.game`, debuggable, minSdk 31, arm64-v8a only, IL2CPP, **no ACCESS_BACKGROUND_LOCATION**, no permission outside the allow-list, SHA256 recorded |
| A5 | No missing scripts / scene order | `-Gates UnityQaChecks` PASS |

## B. Device — install and launch (A31)
| ID | Check | Pass criterion |
|---|---|---|
| B1 | Install the exact APK | `adb install -r` Success; `dumpsys package` versionName/lastUpdateTime match; APK SHA256 = handoff |
| B2 | Cold start → AR scene | starts in < 15 s; camera permission prompt handled by the owner on the device; real camera background visible |
| B3 | Logcat clean | `Invoke-MetinARDeviceSmoke.ps1`: no FATAL/ANR/native crash, no `Exception` from `MetinAR.*` during the test |

## C. Device — FPP combat behaviour (manual, owner on the device; record a screen video)
| ID | Check | Pass criterion |
|---|---|---|
| C1 | Visible hands + sword | both virtual hands/forearms and the sword are visible in FPP; the **real hand is not required or shown** |
| C2 | Correct buttons | ATAK and AURA buttons are visible, labelled, reachable with the thumb, not under the system bars |
| C3 | Attack hits a near mob | with the mob within sword reach, ATAK plays the swing and registers exactly one hit per swing (HP/feedback) |
| C4 | No hit out of range | with the mob clearly out of reach, ATAK plays the swing and **no** hit is registered |
| C5 | Aura | AURA plays the two-hand grip and leaves the blue-gold blade effect; it **does no damage by itself** (mob HP unchanged) |
| C6 | Button spam | rapid ATAK/AURA tapping for 10 s: no double hits beyond the cooldown, no stuck animation, no exception |
| C7 | Tracking loss | cover the camera / point at a blank surface for 5 s, then return: no crash, the mob stays anchored or re-anchors, input recovers |
| C8 | Pause/resume | Home → wait 10 s → return: AR session resumes, no black camera, no exception |
| C9 | Exit/return | leave the AR encounter to the map and enter again (or kill and restart the app): state is consistent, no duplicate mob/hands |

## D. Device — performance (PROTOTYPE GATE, `tools/qa/perf-budgets.json`)
| ID | Check | Pass criterion |
|---|---|---|
| D1 | FPS/frame time | `[MetinARPerf]` windows (dev APK): `CombinedArHandSword`/`ArEncounterPrototype` budget, e.g. fpsAvg ≥ 20–24, frame p95 within budget |
| D2 | Memory | PSS from `dumpsys meminfo` ≤ scene budget; no steady growth across 3 attack/aura cycles |
| D3 | Thermal (short) | thermal status < 4, battery < 45 °C during a 5-min session (the short run is not a soak test) |

Visual quality versus the concept art is **out of scope** for pass/fail; a procedural blockout is labelled as such.
