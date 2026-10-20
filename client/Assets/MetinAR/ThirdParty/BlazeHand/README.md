# MediaPipe hand models (ONNX) — third-party, Apache-2.0

Used by METINAR-007 on-device hand tracking (`Assets/MetinAR/Scripts/HandTracking`), run with Unity Sentis
(`com.unity.ai.inference` 2.6.1). Files are **unmodified** copies.

| File | SHA-256 |
|---|---|
| `hand_detector.onnx` (palm detector, input 1×192×192×3 RGB 0..1, outputs `Identity` 1×2016×18 boxes, `Identity_1` 1×2016×1 logits) | `c620c7c17de68a6568d0ce9e1ee1335531b7c7a6567dfd1150856e20921cbba9` |
| `hand_landmarks_detector.onnx` (hand landmarker, input 1×224×224×3 RGB 0..1, outputs `Identity` 63 landmarks, `Identity_1` presence, `Identity_2` handedness, `Identity_3` 63 world landmarks) | `e18a95135b40c732ea53d2dd6af66cbec6d3f8bf0296bb529a4c4be0e8349ec1` |

- Source: Hugging Face `unity/inference-engine-blaze-hand`, revision `1e4394f13926e0b61c4a2d8f24ce7a78319d4d21`
  (<https://huggingface.co/unity/inference-engine-blaze-hand>), repository license **Apache-2.0**.
- Original models: Google MediaPipe Hands / Hand Landmarker (palm detection + hand landmark models,
  model card "Licensed under Apache License, Version 2.0"), <https://github.com/google-ai-edge/mediapipe>.
- License text: `LICENSE-APACHE-2.0.txt` in this folder.
- Not included: the anchor table and sample code from the Unity samples repository; anchors are generated in code.
