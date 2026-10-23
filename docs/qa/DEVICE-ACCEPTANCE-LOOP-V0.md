# Device acceptance checklist — combat loop v0 (#47 S2b) and FPP (#31)

Owner: QA (#26). This is the device test list for audit task **T07** (`docs/audit/AUDIT_2026-10-21.md`, §E.3, stage E2):
map → AR → place → ATAK until death → reward → back to the map, in **one APK built from one SHA**. The static / Editor
part for #31 is in [METINAR-031-FPP-ACCEPTANCE.md](METINAR-031-FPP-ACCEPTANCE.md) (sections A–D). This file is the
device run sheet that closes S2b and the device items of #31. The executor is the MetinAR Developer, with the owner at
the phone. QA verifies.

## 1. Rules

- **Exact build identity:** every report starts with the commit SHA (40 hex), the APK file name, its size in bytes and
  its **SHA256**, plus `applicationIdentifier` and `versionName`. One report covers one APK. If the APK is rebuilt, the
  run starts again.
- **UNVERIFIED rule:** an item without its evidence (section 3) is **UNVERIFIED**, never PASS. Green CI, Editor smokes,
  Play Mode tests and "looked fine" without a log line or recording do not close device items. A partial run is
  reported as partial. Items that weren't run are listed as UNVERIFIED with the reason.
- **Slots (#33):** `LOCAL SLOT ACQUIRED` for A31 before the first adb command and `RELEASED` after the last one. While
  A31 is ACQUIRED, nobody **starts or closes** Unity on LuzMen, because both kill the adb server. **Never**
  `adb kill-server` / adb restart.
- **adb health:** before every device step run `adb devices` and confirm the phone shows as `device` (not `offline`
  / `unauthorized` / missing). If the connection drops, the step is recorded as **ADB LOST** with the time range
  (logcat gap), not as FAIL or PASS of the product. `Invoke-MetinARDeviceSmoke.ps1` does this automatically once PR #75 (#73) is merged. Until then, check by hand.
- **Privacy:** do not put pairing codes, IP addresses, device serials or GPS coordinates in GitHub. Mask them in
  logs (`<serial>`, `<ip>`) before attaching.
- **Package:** the APK under test must not overwrite another team's install without a note in #33. QA builds use their
  own `applicationIdentifier` (#73). The game APK is `com.metinar.game`.

## 2. Pre-flight (P)

| ID | Check | Pass criterion | Evidence |
|---|---|---|---|
| P1 | APK identity | SHA256 of the file on LuzMen = SHA256 in the handoff comment | `Get-FileHash` output |
| P2 | Player data | `tools/qa/Test-ApkPlayerData.ps1 -ApkPath <apk>` → `[PASS]` | console line |
| P3 | ApkAudit | `-Gates ApkAudit`: `applicationIdentifier` matches the purpose (game `com.metinar.game`, QA `com.metinar.game.qa`), debuggable, minSdk 31, arm64-v8a, no `ACCESS_BACKGROUND_LOCATION`, **11/11 `Unity.XR.ARCore` registrations** in `assets/bin/Data/RuntimeInitializeOnLoads.json` (0/11 = "AR unavailable", e.g. QA APK `e18df69`). Until PR #75 merges, ApkAudit does not count the ARCore registrations. Count the records with `"assemblyName":"Unity.XR.ARCore"` in that file by hand. | report line (+ the count) |
| P4 | adb | `adb devices` shows exactly the A31 as `device` | line with the serial masked |
| P5 | Install | `adb install -r` → `Success`; `dumpsys package <id>` versionName matches | two lines |
| P6 | Logcat start | `adb logcat -c` then capture `-v threadtime` to a file for the whole run | file name + start time |

## 3. Device run (L = loop v0 / S2b, F = FPP #31)

Evidence codes: **LOG** = exact logcat line(s); **VID** = screen recording (file name + timestamp); **IMG** =
screenshot; **MEAS** = measured number with its source command; **OWN** = owner's confirmation (quote + time).

| ID | Area | Steps | Pass criterion | Evidence |
|---|---|---|---|---|
| L1 | Cold start + map | launch from the launcher | world map in < 15 s; no FATAL/ANR | LOG + VID |
| L2 | Enter AR | open the encounter from the map panel | camera permission handled; real camera image; state reaches `Scanning` | LOG `[ArEncounter] state Starting -> Scanning` |
| L3 | **Visibility** | scan the floor, place the mob | exactly one mob, standing on the floor, fully in frame at 1–3 m; virtual hands + sword visible (F, D2: no real hand tracking) | IMG + LOG `spawned ... anchored=True` |
| L4 | **Near hit** | stand within sword reach, ATAK | one swing = at most one hit (`swing=<n>` once); mob HP drops; HP label updates | LOG `[ArDamage] swing=` + VID |
| L5 | **Far hit (negative)** | step back clearly out of reach (≥ 3 m), ATAK ×3 | swing animation plays, **no** `[ArDamage] swing=` line, HP unchanged | LOG (absence over the time range) + VID |
| L6 | **Button spam** | tap ATAK/AURA as fast as possible for 10 s | no double hit per `swingId`; no stuck animation; AURA does no damage by itself; no exception | LOG + VID |
| L7 | **Tracking loss** | cover the camera / point at a blank wall for 5 s, then return | no crash; the mob stays anchored or re-anchors; still exactly one mob (watch #66: mob must not vanish); input works | LOG + VID |
| L8 | **Pause/resume** | Home, wait 10 s, return | AR session resumes; no black camera; mob HP unchanged; no exception | LOG + VID |
| L9 | **HP to 0** | keep hitting until death | HP reaches 0; label „Pokonany”; body disappears after ~1 s; exactly one `defeated` line; further ATAK does no damage | LOG `[ArDamage] defeated ...` + VID |
| L10 | **Reward** | read the reward | reward granted **once**: for warrior lvl 1 vs spider lvl 5: **+101 EXP**, **10–18 talary** (METINAR-020 doc); no second reward on re-entry | LOG `exp=+101 talary=+N` |
| L11 | **Exit/return** | exit to the map, open the map HUD, enter AR again | map HUD shows `Lv`, EXP orbs 101/160 = 2.525 of 4, talary = reward; re-entry gives a fresh, consistent encounter, no duplicate mob/hands | IMG of map HUD + LOG |
| L12 | **FPS** | whole AR part (L3–L9), ≥ 3 perf windows | `[MetinARPerf] scene=ArEncounterPrototype`: fpsAvg ≥ 24, frame p95 ≤ 55 ms (`tools/qa/perf-budgets.json`, PROTOTYPE GATE); map ≥ 28 fps | LOG `[MetinARPerf]` lines + MEAS |
| L13 | **PSS memory** | `adb shell dumpsys meminfo <id>` after L3, after L9 and after L11 | TOTAL PSS ≤ 850 MB in AR (≤ 600 MB on the map); growth across the 3 samples < 10% (no leak per encounter) | MEAS (3 values, MB) |
| L14 | **Logcat** | review the whole capture | no `FATAL`, `ANR`, native crash, `Exception` from `MetinAR.*`; `[ArDamage] hit ignored` only when expected; gaps listed as ADB LOST. The harness `Crash` gate FAILs only on crash/ANR/`F` lines. Unity errors and exceptions (including `MetinAR.*`) appear only as WARN `unityErrors=<n>`, so read each one by hand. The Editor T15 `KnownEditorInfrastructure` exception does not apply on the device. | LOG summary (counts) + the file |
| L15 | Thermal (short) | `dumpsys thermalservice` / battery at the end | thermal status < 4; battery < 45 °C during ≤ 5 min | MEAS |

`Invoke-MetinARDeviceSmoke.ps1` covers P4–P6, L12–L14 automatically (perf parsing, PSS, crash scan). L3–L11 need the
owner at the phone and a recording.

Stage acceptance (audit UPDATE_1 §5.1) also includes the spider world-anchoring tests from audit UPDATE_1 §3 (its
A1–A6). In reports, write them as `S3-A1`…`S3-A6` so they are not confused with the Editor items A1–A6 in
[METINAR-031-FPP-ACCEPTANCE.md](METINAR-031-FPP-ACCEPTANCE.md).

## 4. Report format (comment on the PR/issue under test, and on #26)

```text
QA DEVICE ACCEPTANCE — loop v0 / #31
Commit: <40-hex SHA>   Branch: <name>
APK: <file> <bytes> B  SHA256 <64 hex>  package <applicationIdentifier>  versionName <v>
Device: Samsung Galaxy A31, Android <ver> (no serial)   Slot: #33 <ACQUIRED link> .. <RELEASED link>
Logcat: <file name>, <start>–<end> Europe/Warsaw, gaps: <none | ADB LOST hh:mm–hh:mm>
ID | PASS/FAIL/UNVERIFIED | evidence
P1 | PASS | Get-FileHash ... = <sha>
...
L5 | PASS | 12:41:05–12:41:20 no "[ArDamage] swing=" lines; VID loop-01.mp4 00:52
L13 | PASS | PSS 512 / 530 / 541 MB
Summary: <n> PASS, <n> FAIL, <n> UNVERIFIED. Known issues: #66 (T09 mob disappears) ...
```

Only "all L items PASS with evidence + owner confirmation (OWN) for L3, L9, L10, L11" closes S2b on A31. One FAIL keeps
the issue open with a linked bug.
