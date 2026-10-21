# METINAR-FPP-ART-001 — First-person hands/forearms, sword and Idle/Attack/AuraActivate clips

Issue: #34 (3D models team). Branch `feat/fpp-art-hands-sword` from `main` e236629. Consumer: #31 / PR #32
(`feat/fpp-hands-aura-prototype`, FppHandsView / FppCombatTimeline).
**Clips baked from contract V3 `v3-a31-retest`** (#31 issuecomment-6314043034 = PR #32 head 8d0acee `FppViewFraming`
= `FppPoseContract.RetestV3` on `integration/fpp-art-preview`). V1 (PR #32 52a1375, clips up to cd3d02c) and the
earlier proposal rest (0.06,−0.15,0.44) are obsolete. After this change the art preview can set
`FppArtPreviewConfig.BakedContractId = "v3-a31-retest"` (that file is on the integration branch, not changed here).
Status: assets + editor tooling delivered for **isolated preview**. No integration into #31 code, no scene changes.
The hands are virtual on-screen hands holding the sword; there is no camera hand tracking.
Phone (Galaxy A31) and performance: **UNVERIFIED**.

## 0. What is and is not in this change

Delivered:

- `art-source/FppHandsSword/*.py`: Blender 4.2.3 scripts that generate everything (geometry, UVs, rig, weights,
  clips, textures, exports, preview renders, measurement report). No `.blend` is committed; it is rebuilt.
- `client/Assets/MetinAR/Characters/Player/FppArms/`: `SK_Player_FppArms.fbx`, `A_FppArms_{Idle,Attack,AuraActivate}.fbx`,
  `T_FppArms_{BC,N,MS}.png`, `M_FppArms.mat`, `AC_FppArms.controller`, `PF_Player_FppArms.prefab`, `ASSET_SOURCE.txt`.
- `client/Assets/MetinAR/Weapons/FppSword/`: `SM_Weapon_FppSword.fbx`, `T_FppSword_{BC,N,MS,E}.png`, `M_FppSword.mat`,
  `PF_Weapon_FppSword.prefab`, `ASSET_SOURCE.txt`.
- `client/Assets/MetinAR/Editor/FppArt/`: `FppArtAssetBuilder` (materials, controller, prefabs) and
  `FppArtAssetCheck` (read-only numeric check + preview screenshots).
- METINAR-009 pipeline extension: new `Player` category (section 5) + tests.

Not changed: combat scenes, `FppCombatController`/`FppHandsView` (PR #32), geo (`GeoCoordinate`/`GeoMath`), map,
server, the #24 spider (PR #30), Android permissions, Unity packages, `Prefabs/`, `Art/`.
No marketplace, scanned, AI-generated or unclear-licence content. The owner's reference image was **not** used as a
model, texture or trace source (visual direction only: dark/gold gauntlets, blue-gold Aura).

## 1. Asset contract (coordinate space, sockets)

| Item | Value |
|---|---|
| Units | metres, 1 Unity unit = 1 m, FBX Convert Units on, Scale Factor 1 |
| Space of the arms prefab | **AR-camera local**: origin = camera, +X right, +Y up, +Z forward (put `PF_Player_FppArms` under the AR camera with identity local transform) |
| Authoring space | Blender x = Unity x, Blender y = Unity z, Blender z = Unity y (Unity's Bake Axis Conversion maps it back; verified numerically in Unity, section 7) |
| Attack left-hand dodge | #36 MANAGER DECISION 2026-10-21T13:33Z (option 1): during Attack only, the left hand position is `LEFT_REST + (−0.08,−0.03,−0.04)·w(t)`, w = SmoothStep in 0.18→0.30 s, 1 until 0.48 s, SmoothStep out 0.48→0.70 s (back at rest from 0.70 s); rotation stays E(0,0,12). Minimum drawn-hilt (pommel…guard) to left-socket distance over the whole Attack: **74.8 mm at 0.42 s** (required ≥ 50 mm; without the dodge 5.7 mm). All V3 values (rest, Aura, left rest) unchanged. The code side (`FppPoseContract.LeftHandPose` on #31) needs the same term to match the clips |
| Contract | V3 `v3-a31-retest`: rest `RightGrip (0.09,−0.17,0.46)` Euler(15,−30,−22); AuraActivate hold `(0.10,−0.14,0.50)` Euler(10,−30,−18); left hand rest `(−0.08,−0.23,0.42)` Euler(0,0,12) (unchanged); sword scale **0.75** around the grip; windup/end, timings, hit window unchanged |
| Right grip | bone `B_Socket_Sword` = `RightGrip` frame (unscaled); hilt axis = local +Y, blade towards +Y |
| Left grip | bone `B_Socket_GripL` = `LeftHand` frame (rest above; on the hilt at `RightGrip·(0,−0.09,0)` = model (0,−0.12,0) × 0.75) |
| Sword scale | the sword **model** (`SM_Weapon_FppSword`, `PF_Weapon_FppSword`, its sockets) stays at scale 1 (model units below); `PF_Player_FppArms` nests it under `B_Socket_Sword` with uniform local scale 0.75, so in the RightGrip frame every sword point is model × 0.75 (hands stay life-size) |
| Sword prefab pivot | `Socket_GripR` = (0,0,0) = right-hand grip centre, identity rotation |
| Sword sockets (sword-model local, scale 1) | `Socket_GripR` (0,0,0) · `Socket_GripL` (0,-0.12,0) · `Socket_FxBladeBase` (0,0.13,0) · `Socket_FxBladeTip` (0,0.94,0) · `Socket_FxAuraBurst` (0,0.38,0) · `Socket_FxGuardGem` (0,0.116,0) |
| Blade | model y 0.13 … 0.94; in the RightGrip frame 0.0975 … 0.705 (= `FppPoseContract.HitBladeBase/HitBladeTip`, the code-driven hit segment, 25 % shorter reach than V1) |
| Hilt | model radius 16.5 mm, y −0.172 … 0.104; guard at y 0.116 (±0.11 m wide); pommel at y −0.195 (drawn: radius 12.4 mm, y −0.129 … 0.078, guard ±0.0825 m) |
| Nesting | `PF_Player_FppArms` contains `PF_Weapon_FppSword` under `B_Socket_Sword` with identity local position/rotation and local scale 0.75 |

### 1.1 Bones (29, Generic rig)

`B_Root`; per side (`_R`, `_L`): `B_UpperArm`, `B_Forearm`, `B_Hand`, `B_Index1/2`, `B_Middle1/2`, `B_Ring1/2`,
`B_Pinky1/2`, `B_Thumb1/2`; sockets `B_Socket_Sword` (child of `B_Hand_R`) and `B_Socket_GripL` (child of `B_Hand_L`).
Shoulders (upper-arm heads, camera-local): R (0.16, −0.295, 0.025), L (−0.16, −0.295, −0.10). Upper arm 0.30 m,
forearm 0.285 m. The arms are solved with an analytic two-bone IK so the hand frames follow the contract exactly
(wrist gap 0 mm). PR #32's procedural arm roots `(±0.28, −0.46, 0.19)` are not used by this rig.

### 1.2 Clips (baked, 100 fps, one clip per file, every bone keyed every frame)

| State / clip | Length | Loop | Content (times = PR #32 FppCombatTimeline) |
|---|---|---|---|
| `Idle` / `A_FppArms_Idle` | 2.00 s (201 keys) | yes | rest pose ± ≤4 mm / 1.2° sway; first and last frame are exactly the rest pose |
| `Attack` / `A_FppArms_Attack` | 0.72 s (73 keys) | no | rest → windup (0.25,−0.08,0.48) E(0,−10,−65) at 0.18 s → cut to (−0.15,−0.22,0.50) E(70,30,60) at 0.42 s (hit window 0.18–0.42) → return to rest at 0.72 s |
| `AuraActivate` / `A_FppArms_AuraActivate` | 1.15 s (116 keys) | no | sword to (0.10,−0.14,0.50) E(10,−30,−18) over 0–0.25 s; left hand reaches the hilt over 0–0.35 s (fingers open, close in the last 20 % of the reach); two-hand hold incl. burst at 0.65 s; release 0.90–1.15 s |

Easing is `Mathf.SmoothStep`, rotations `Quaternion.Euler`/`Slerp` exactly as in `FppPoseContract`. Because the clips reproduce
the code's pose functions, #31 can either keep driving the sockets from code or play these clips; both put the
sword in the same place. `AC_FppArms` has the three states (Idle default) and **no parameters/transitions**:
driving them from the combat timeline belongs to #31. `FppArtAssetBuilder` updates an existing `AC_FppArms.controller`
in place (same GUID and state fileIDs; it no longer deletes and recreates it), so a rebuild on unchanged clips leaves
the checkout clean.

Left-hand fingers: in Attack (the end of the cut sweeps the hilt past the resting left hand) and
during the Aura reach/release the left fingers would intersect the hilt. `fpp_anim.fit_left_fingers` searches a
per-frame finger opening/closing that removes all intersections and smooths it (hold ±6 frames + 7-tap box).

## 2. Visual description

Original design: dark leather gloves with stitched seams, dark-metal back-of-hand plates and finger plates,
gold knuckle bar, dark-metal vambraces with gold chevron inlay and gold rims, dark cloth upper sleeves.
Sword: brushed steel lens-profile blade with a fuller carrying blue emissive runes, up-swept dark crossguard with
gold bands, gold centre block and pommel with blue gems, dark leather grip wrap.

## 3. Budgets — actual numbers (Blender export; Unity numbers in section 7)

| Asset | Tris | Verts | Bones | Materials | Textures | Budget |
|---|---|---|---|---|---|---|
| `SK_Player_FppArms` | 4 932 | 2 588 | 29 | 1 (`M_FppArms`, URP Lit) | BC / N / MS, 1024² | Player (proposal): ≤10 000 tris, ≤40 bones, ≤2 materials, 1024² (2048² justified), 1 LOD |
| `SM_Weapon_FppSword` | 1 452 | 765 | 0 | 1 (`M_FppSword`, URP Lit + emission) | BC / N / MS / E, 1024² | Weapon (METINAR-009): ≤3 000 tris, 0 bones, 1 material, 1024² |

MS = metallic in R, smoothness in A (URP `_MetallicGlossMap`). Import: ASTC 6×6 (BC/MS/E), ASTC 4×4 (N), mipmaps on,
Read/Write off (METINAR-009 postprocessor). Renderers: shadows off (camera-attached, no ground), arms
`updateWhenOffscreen = true`, Animator `AlwaysAnimate`, no root motion.

## 4. Why these FPP limits (proposal)

METINAR-009 had no first-person category, and the files would otherwise sit outside the pipeline folders and be
**skipped silently** by the validator. FPP arms are a single instance, always on screen at 0.3–0.9 m from the camera,
never LOD-switched (constant screen size) and drawn on top of AR camera tracking + the mob. 10k tris / 40 bones /
2 materials is the mob envelope without LODs; this asset uses half of it. To be confirmed by A31 measurements
(METINAR-009 section 10) — **UNVERIFIED**.

## 5. Pipeline change (METINAR-009 extension)

- `AssetPipelineRules`: `PlayerFolder = Assets/MetinAR/Characters/Player` added to `PipelineFolders`; category
  `Player` (token `Player`, per-asset folder); `SK_/SM_Player_<Id>`, `PF_Player_<Id>` names; budget above;
  required states `Idle`, `Attack`, `AuraActivate` (Idle loops via the existing looping list).
- `AssetPipelineValidator`: required-state check includes Player; ground-pivot warning skipped for Player (pivot is
  the camera origin); Player `SK_` models must be Generic with Preserve Hierarchy. Model bounds use mesh bounds
  (same change as PR #30, identical hunk, so the two PRs merge cleanly on that line).
- `MetinARAssetImportPostprocessor`: Player models/clips get Preserve Hierarchy on first import (clip curve paths
  `Armature/B_Root/...` must match the model's).
- `tests/AssetPipeline.Tests`: Player naming/category/budget/state tests.

## 6. Findings for #31 on contract V3 (contract, not the asset)

**6.1 Attack: the right hilt swept through the resting left hand (resolved).** With V3 alone the hilt axis passed
5.7 mm from the left-hand socket at t = 0.38 s (Unity check of `22d2b1a`: 527 hand vertices inside the hilt proxy).
Decision on #36 (13:33Z, option 1): the left hand dodges during Attack (section 1). Now: 74.8 mm minimum at 0.42 s,
0 penetrating vertices in every clip (Blender and Unity). The procedural/code-driven view (#31) has to add the same
dodge term in `LeftHandPose` to stay identical to the clips:
`leftRest(t) = LeftRest + (−0.08,−0.03,−0.04) · w(t)` for Attack, w as above (`fpp_contract.left_dodge_weight`).

**6.2 Grip gap (fixed, P3, art only).** The hands are life-size and were shaped around the scale-1 hilt (16.5 mm); the
drawn hilt is 12.4 mm. `fpp_model.GRIP_TIGHTEN` (= 16.5 mm × 0.25 = 4.1 mm) moves palm, knuckles, fingers and thumb
radially towards the hilt axis. No contract value changes (the socket frames are the contract). Minimum hand–hilt
clearance now R 3.8 mm / L 2.1 mm (was 7.2 / 5.7 with the 0.75 sword, 3.1 / 2.8 in V1).

**6.3 Framing.** With the estimated A31 portrait FOV (69.4° vertical, an estimate, not measured on the device) the V3
rest pose has the grip, guard, blade and left hand in the lower part of the frame (renders in
`docs/METINAR-FPP-ART-001/renders/`).

## 7. Verification

**V3 regeneration:** results of the V3 clips are in section 7.0 (added with the Unity run). The tables below 7.0
are the V1 run (`a5d7e27`/`cd3d02c`) and are kept as history.

### 7.0 V3 (contract `v3-a31-retest`) — first run, before the left-hand dodge (history)

These are the results of `22d2b1a`, before the dodge. The final run is in 7.0b.

Unity 6000.6.4f1 on LuzMen, batch mode. Each run used a **new clean clone** from GitHub with a fresh Library
(first import). Run 1: `C:\Users\mroma\MetinAR-FPPART-V3` @ `3092949`; its only output was the expected
`PF_Player_FppArms.prefab` change (nested sword scale 0.75), committed as `22d2b1a`. **Run 2 (tested SHA `22d2b1a`)**:
`C:\Users\mroma\MetinAR-FPPART-V3B`. The commit after it only adds this section and the reports/screenshots below.

| Gate (run 2, `22d2b1a`) | Result | Evidence |
|---|---|---|
| Blender build (`fpp_build.py -- full`, box) | sockets vs contract 0 mm / 0°; IK wrist gap 0 mm; Idle/Aura 0 hand vertices in the sword proxies; **Attack 591** (section 6.1); grip clearance R 7.2 mm / L 5.7 mm | `blender_build_report.json` |
| `dotnet test tests/AssetPipeline.Tests` (box) | PASS 179/179 (incl. 10 new V3 contract/idempotency tests) | — |
| Unity compile | exit 0, 0 `error CS` in all step logs | `unity-batch-summary.txt` |
| `FppArtAssetBuilder.BuildBatch` ×2 | exit 0 both times; `git status` **clean** after the 1st and 2nd build and at the end (idempotent: `AC_FppArms.controller` and prefabs unchanged) | `unity-batch-summary.txt` |
| `FppArtAssetCheck.RunBatch` | **errors = 1**: `Attack: hand vertices inside sword proxies = 527 over 37 sampled frames` (contract V3 overlap, section 6.1). Everything else is OK: B_Socket_Sword ≤ 0.52 mm / 0.00°, B_Socket_GripL ≤ 0.57 mm / 0.00°; nested sword scale 0.75; sockets in the RightGrip frame = contract (GripL (0,−0.09,0), BladeBase 0.0975, BladeTip 0.705, AuraBurst 0.285) 0.00 mm; Idle/Aura 0 penetrations; 28 screenshots | `unity-asset-check.txt` |
| `AssetPipelineValidator.RunBatch` | PASS: files=26 errors=0 warnings=0 | `unity-asset-validation.txt` |
| `MetinARQaChecks.Run` | PASS: scenes=2 prefabs=4 missingScripts=0 failures=0 | log |
| `AssetPipelineSelfTest.Run` | PASS 14/14 | log |
| Visibility (estimated A31 portrait, 69.4° v) | Idle: grip 201/201, blade tip 66/201, left hand 201/201; Attack: grip 55/73, tip 24/73, left 73/73; Aura: grip 116/116, tip 0/116, left 116/116 | `unity-asset-check.txt` |
| APK / A31 / FPS | **NOT RUN, UNVERIFIED** | — |

The check screenshots now do a warm-up render after `BakeMesh`. In run 1 the first shot of each pose showed the
previous proxy mesh, so the Idle portrait/grip shots had no hands. The V1 screenshots had the same artefact.

Unity 6000.6.4f1 on LuzMen, separate checkout `C:\Users\mroma\MetinAR-FPPART` (local clone + Library copied
from `MetinAR-009-verify`; nothing in other checkouts touched), batch mode, tested SHA `a5d7e27`
(the following commit only adds the Unity-generated `.meta`/material/controller/prefab files and these reports).

| Gate | Result | Evidence |
|---|---|---|
| Blender build (`fpp_build.py -- full`) | PASS: 0 hand vertices in the sword proxies in any frame; grip clearance R 3.1 mm / L 2.8 mm; sockets vs PR #32 0 mm / 0°; IK wrist gap 0 mm | `METINAR-FPP-ART-001/blender_build_report.json` |
| `dotnet test tests/AssetPipeline.Tests` (box) | PASS 149/149 | PR description |
| Unity compile | PASS: 0 `error CS` (standalone `-quit` compile on `1d7547b`; scripts recompiled on `a5d7e27` at the start of the build step; 0 `error CS` in all step logs) | `unity-batch-summary.txt` |
| `FppArtAssetBuilder.BuildBatch` | PASS (exit 0) | prefabs/materials/controller committed |
| `FppArtAssetCheck.RunBatch` | PASS: errors = 0 (1 WARN = framing, section 6) | `unity-asset-check.txt` |
| — sockets in Unity vs PR #32 (every 10 ms) | B_Socket_Sword ≤ 0.60 mm / 0.00°, B_Socket_GripL ≤ 0.69 mm / 0.00° | ″ |
| — sword sockets | all 6 within 0.00 mm of the contract, frames = RightGrip frame (0.00°) | ″ |
| — hand/sword intersection on the skinned mesh | 0 vertices in all sampled frames; min grip clearance 3.1 mm (Idle/Attack), 2.9 mm (Aura) | ″ |
| — materials/textures/missing scripts | URP Lit, all maps assigned, 0 missing scripts, shadows off, UV0 present | ″ |
| `AssetPipelineValidator.RunBatch` | PASS: files=26 errors=0 warnings=0 (Player files included, not skipped) | `unity-asset-validation.txt` |
| `MetinARQaChecks.Run` | PASS: scenes=2 prefabs=4 (incl. both new prefabs) missingScripts=0 | `unity-qa-checks.json` |
| Unity screenshots | 28 written (portrait/landscape FPP, side, grip) | `METINAR-FPP-ART-001/unity/`, `checkpoint-03-*.jpg` |
| APK / A31 / FPS / memory | **NOT RUN — UNVERIFIED** (integration and device test belong to #31 / QA #26) | — |

Unity-measured mesh numbers: `SK_Player_FppArms` 4 932 tris, 4 277 vertices after import (UV/normal splits),
1 sub-mesh, 29 bones; `SM_Weapon_FppSword` 1 452 tris, 1 132 vertices, 1 sub-mesh.

Found and fixed by the Unity check (first run): Unity's FBX import maps a node's local axes as vectors and flips
local Z, so the socket frames came in rotated +90° about X (positions were exact). The socket bones/empties now
carry `NODE_FIX` (`fpp_contract.py`); the clips additionally use tighter compression tolerances (0.05) because the
default ones moved the left socket by ~4 mm.

## 8. Reproduce

```
blender -b --factory-startup --python art-source/FppHandsSword/fpp_build.py -- full <out_dir>
# copy <out_dir>/Assets/** into client/Assets/MetinAR/, then in Unity 6000.6.4f1:
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.FppArt.FppArtAssetBuilder.BuildBatch
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.FppArt.FppArtAssetCheck.RunBatch
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.AssetPipeline.AssetPipelineValidator.RunBatch
```

## 9. Known limitations / UNVERIFIED

- Phone (A31) appearance, FPS and memory: UNVERIFIED (no APK built here; integration is #31).
- Hand/sword intersection is checked against conservative sword proxies (cylinder hilt, box guard/blade, sphere-ish
  pommel), hand vertices only; arm-vs-arm and arm-vs-camera-near-plane are reported, not enforced (nearest vertex
  ≈0.29 m from the camera).
- Finger opening in Attack/Aura is an automatic fit, not hand-keyed animation; it reads as a reflexive opening.
- The FOV numbers in section 6 are estimates.
- The Attack left-hand dodge (section 6.1) lives in the clips; the code-driven view needs the same term (#31).
- The Player budget is a proposal (owner decision).

## 10. Owner / #31 decisions

1. Accept the `Player` category and its budget (section 4) or set different limits.
2. #31: keep code-driven sockets or play the baked clips (both match).
3. #31: add the Attack left-hand dodge to `FppPoseContract.LeftHandPose` (section 6.1).
