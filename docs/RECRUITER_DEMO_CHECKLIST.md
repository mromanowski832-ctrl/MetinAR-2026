# MetinAR — honest mobile demo checklist

**Status:** recording and physical-device validation **not yet attached**. Use this to prepare verifiable portfolio evidence, not to claim that the complete game works.

## Goal and safety

Record a short, unedited (or clearly labelled cuts) screen capture from a real Android device showing the features that actually pass. Do not walk near roads, rails or private properties while using the phone. Test in a safe stationary position where possible. Avoid recording faces, residential addresses, precise home coordinates, developer tokens or unrelated notifications.

## Before recording

- [ ] Record repository commit SHA and active branch.
- [ ] Record Unity Editor version (currently pinned in [ProjectVersion.txt](../client/ProjectSettings/ProjectVersion.txt)).
- [ ] Connect and authorize a physical Android phone; record device model and Android version without personal identifiers.
- [ ] Run the Windows QA gates described in [METINAR-011](METINAR-011-QA-GATES.md):
  `powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1`
- [ ] For a built QA APK, run the actual physical-device harness according to [METINAR-037](METINAR-037-QA-APK-ADB.md). Example from repo root:
  `powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARDeviceSmoke.ps1 -ApkPath client\Builds\Android\MetinAR-ar-encounter-qa.apk -SoakSeconds 60`
- [ ] Save the generated QA report, sanitized logs, screenshot and APK hash where the QA tooling specifies. Never upload location history or secrets.

## Demo sequence (show only verified behavior)

1. **App start:** launch the actual APK and show the startup screen.
2. **World map / GPS:** show permission handling and distance display, if functioning on this device.
3. **Encounter availability:** show a test encounter being offered by proximity, if the pipeline works.
4. **AR scene:** show real-camera world tracking and any placed enemy, if ARCore initializes and placement works.
5. **Combat:** show input, hit / miss and HP changes, if functioning in the device build.
6. **Result:** show encounter completion / reward only if reproducible.

If a step fails, stop claiming the full feature; capture the failure as a separate bug report with exact reproduction steps. A Unity Editor preview, mocked ADB run or a slide animation is **not** evidence that the APK worked on a phone.

## Evidence log to publish alongside the video

| Field | Actual observation |
| --- | --- |
| Date and Git SHA | Pending |
| Android model / version | Pending |
| Unity version | Pending |
| QA APK SHA-256 | Pending |
| QA runner result | Pending |
| Real ADB harness result | Pending |
| ARCore registration check | Pending |
| Tested gameplay steps | Pending |
| Known failures | Pending |
| Video URL | Pending |

Keep the evidence and dates aligned with the exact commit tested. Do not substitute previous CI successes or another build's logs for current evidence.
