# METINAR-007 — Real hand tracking + AR sword: backend selection (spike)

Issue: #8. Branch: `feat/007-hand-sword-ar` (from `main` 8557a3e, METINAR-006 merged).

This document is written **before** any hand-tracking dependency is added (milestone 1 of the controller's
order). Implementation, test results and limitations are appended below as later milestones complete.

## Requirements (from the issue / controller)

- Detect the player's **real** hand in the live phone camera; the sword follows that hand (no camera-fixed/FPP sword).
- Inference **on-device only**; no camera frame leaves the phone; no cloud API.
- Free/open, **clearly commercial-compatible** license, actively maintained; no paid dependency.
- Unity 6000.6.4f1, AR Foundation 6.6.2 / ARCore 6.6.2, Android ARM64 + IL2CPP (minSdk 31, target 36 unchanged).
- No new privacy permission beyond what strictly required (the AR scene already uses `CAMERA`).
- Must not destabilise the METINAR-006 AR scene (ARCameraBackground keeps owning the camera feed).

## Facts about the current stack

- AR Foundation / ARCore on Android **do not provide hand or finger landmarks** (ARCore has no hand-tracking
  subsystem; AR Foundation's XR Hands path is for OpenXR headsets, not phone cameras). A separate computer-vision
  model is required.
- AR Foundation exposes the camera image on the CPU via `ARCameraManager.TryAcquireLatestCpuImage` →
  `XRCpuImage.Convert` (RGBA32). This is the standard, supported way to read the AR camera frame without taking the
  camera away from ARCore. It needs no extra permission (same `CAMERA` permission ARCore already requests).

## Candidates evaluated

| Option | What it is | License | Maintenance (checked 2026-10-20) | Unity 6.6 + AR Foundation + Android IL2CPP risk |
|---|---|---|---|---|
| **A. Unity Sentis (`com.unity.ai.inference`) + MediaPipe hand models (ONNX)** | First-party Unity neural-network runtime; runs ONNX models on-device (GPU compute or Burst CPU). Models: Google MediaPipe palm detector + 21-point hand landmarker, converted to ONNX and published by Unity. | Sentis: Unity package under the Unity Terms of Service (same basis as the Unity Editor/AR Foundation the project already uses; free, no runtime fee for the package). Models: **Apache-2.0** (Google model card + HF repo license). | Sentis 2.6.1 released 2026-04-02, "released for Unity 6000.6" in the Unity 6.6 manual. Model repo updated 2025-05-21. | **Lowest.** Pure managed + Burst/compute, no native plugin, no Gradle/AAR changes, IL2CPP supported, same package registry. Camera frames come from AR Foundation's CPU image. |
| B. homuler/MediaPipeUnityPlugin | Unity wrapper around the MediaPipe C++ framework (native `libmediapipe_c.so`). | MIT (+ Apache-2.0 MediaPipe, many native third-party notices). | Latest release v0.16.3 (2025-11-08), last push 2026-01-10 (~9 months idle), single maintainer, 54 open issues. | High. Large prebuilt native binary per ABI, own camera/texture pipeline, integration with AR Foundation camera frames is custom work, Unity 6.6 not explicitly supported. |
| C. Native MediaPipe Tasks Vision (Android AAR `com.google.mediapipe:tasks-vision`) via JNI bridge | Google's official on-device Hand Landmarker for Android. | Apache-2.0. | Actively maintained by Google (MediaPipe v1.1.1, 2026-10-14). | Medium-high. Needs Gradle dependency + Java/Kotlin bridge + passing ARCore CPU images through JNI; Android-only (iOS would need a second bridge). |
| D. Raw TFLite runtime / ONNX Runtime native plugin | Own native inference runtime + same models. | Apache-2.0 / MIT. | Maintained. | Medium-high. Extra native libraries and bindings for both platforms; no advantage over Sentis for two small models. |
| E. Cloud hand-tracking API | — | — | — | **Rejected**: sends camera frames off-device. |

## Decision

**Option A — Unity Sentis + MediaPipe BlazePalm/BlazeHand landmark models (ONNX), all inference on-device.**

Exact dependency set to be added in the next commit (nothing else):

| Item | Exact version / revision | License | Source |
|---|---|---|---|
| `com.unity.ai.inference` (display name "Sentis") | **2.6.1** | Unity Terms of Service (Unity package; free) | Unity package registry; docs `https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6` |
| its dependencies (resolved by Unity): `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics`, `com.unity.dt.app-ui`, `com.unity.nuget.newtonsoft-json` | as resolved by UPM for 6000.6.4f1 (recorded in `packages-lock.json`) | Unity packages (Unity Companion License / Unity ToS; Newtonsoft.Json MIT) | Unity package registry |
| `hand_detector.onnx` (MediaPipe palm detector, 192×192 input, 2016 anchors) | HF `unity/inference-engine-blaze-hand` @ `1e4394f13926e0b61c4a2d8f24ce7a78319d4d21`, SHA-256 `c620c7c17de68a6568d0ce9e1ee1335531b7c7a6567dfd1150856e20921cbba9` | **Apache-2.0** | `https://huggingface.co/unity/inference-engine-blaze-hand` |
| `hand_landmarks_detector.onnx` (MediaPipe hand landmarker, 224×224 input, 21 landmarks + presence + handedness + world landmarks) | same revision, SHA-256 `e18a95135b40c732ea53d2dd6af66cbec6d3f8bf0296bb529a4c4be0e8349ec1` | **Apache-2.0** | same |

The model files are committed unmodified under `client/Assets/MetinAR/ThirdParty/BlazeHand/` together with the
Apache-2.0 license text and a README with source, revision and hashes.

**Not used:** Unity's `inference-engine-samples` repository code (its license is the Unity ToS *experimental/evaluation*
terms, not an open-source license). The pre/post-processing (anchors, box decoding, ROI, affine crop, landmark
projection) is written from scratch in this project from the published MediaPipe model description; the anchor
table is generated in code (SSD anchors: 192 px input, strides 8/16/16/16, 2016 anchors), not copied.

### Why not B/C/D

- B adds a large native binary with a stale release line and a separate camera pipeline that would compete with
  AR Foundation for frames — exactly the "destabilise the AR scene" risk the controller flagged.
- C/D are viable and well-licensed but need native bridges per platform; they stay as the **fallback** if Sentis
  performance on the owner's phone is unacceptable (same models, same pre/post-processing).

### Privacy / permissions

- No new Android permission: the AR scene already requests `CAMERA` (ARCore). Sentis needs no permission.
- Frames are converted in memory, used for inference and discarded; nothing is stored, logged as image data or
  sent over the network. No `INTERNET`-based processing is involved.
- `ACCESS_BACKGROUND_LOCATION` stays absent (checked in the APK manifest for this issue).

### Owner-visible notes

- Sentis is a first-party Unity package covered by the Unity Terms of Service — the same terms the project already
  accepts for the Unity Editor and AR Foundation. It is free and has no separate runtime fee. If the owner wants
  only OSI-licensed code in the runtime, option C (MediaPipe Tasks, Apache-2.0) is the documented fallback.
- MediaPipe's model card describes the models as trained on a limited dataset; performance can vary with lighting,
  motion blur and skin tone (model-card limitation, not something this project can fix).
