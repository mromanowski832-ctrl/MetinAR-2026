# METINAR-FPP-ART-001 — First-person hands/forearms, sword and Idle/Attack/AuraActivate clips

Issue: #34 (3D models team). Branch `feat/fpp-art-hands-sword` from `main` e236629. Consumer: #31 / PR #32
(`feat/fpp-hands-aura-prototype`, FppHandsView / FppCombatTimeline, head 52a1375 at the time of writing).
Status: assets + editor tooling delivered for **isolated preview**. No integration into #31 code, no scene changes.
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
| Right grip | bone `B_Socket_Sword` = PR #32 `RightGrip` frame; hilt axis = local +Y, blade towards +Y |
| Left grip | bone `B_Socket_GripL` = PR #32 `LeftHand` frame (rest `(-0.19,-0.25,0.43)`, Euler(0,0,12); on the hilt at `RightGrip·(0,-0.12,0)`) |
| Sword prefab pivot | `Socket_GripR` = (0,0,0) = right-hand grip centre, identity rotation |
| Sword sockets (sword-local) | `Socket_GripR` (0,0,0) · `Socket_GripL` (0,-0.12,0) · `Socket_FxBladeBase` (0,0.13,0) · `Socket_FxBladeTip` (0,0.94,0) · `Socket_FxAuraBurst` (0,0.38,0) · `Socket_FxGuardGem` (0,0.116,0) |
| Blade | y 0.13 … 0.94 (= PR #32 `BladeBase`/`BladeTip`, the code-driven hit segment) |
| Hilt | radius 16.5 mm, y −0.172 … 0.104; guard at y 0.116 (±0.11 m wide); pommel at y −0.195 |
| Nesting | `PF_Player_FppArms` contains `PF_Weapon_FppSword` under `B_Socket_Sword` with identity local transform |

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
| `AuraActivate` / `A_FppArms_AuraActivate` | 1.15 s (116 keys) | no | sword to (0.02,−0.18,0.52) E(12,0,0) over 0–0.25 s; left hand reaches the hilt over 0–0.35 s (fingers open, close in the last 20 % of the reach); two-hand hold incl. burst at 0.65 s; release 0.90–1.15 s |

Easing is `Mathf.SmoothStep`, rotations `Quaternion.Euler`/`Slerp` exactly as in PR #32. Because the clips reproduce
the code's pose functions, #31 can either keep driving the sockets from code or play these clips; both put the
sword in the same place. `AC_FppArms` has the three states (Idle default) and **no parameters/transitions**:
driving them from the combat timeline belongs to #31.

Left-hand fingers: in Attack (≈0.38–0.48 s, the end of the cut sweeps the hilt past the resting left hand) and
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

## 6. Finding for #31: framing of the rest pose (contract, not the asset)

With the estimated Galaxy A31 rear-camera field of view (≈69.4° vertical in portrait, ≈34.6° horizontal; an
**estimate**, not measured on the device) the PR #32 rest pose `RightGrip (0.18,−0.19,0.46)` puts the grip, guard,
blade and left hand **outside the portrait frame**; in landscape only the guard is visible. The sword enters the
frame only mid-swing and during the Aura hold (renders in `docs/METINAR-FPP-ART-001/`). Suggested check on the
device: measure the AR camera FOV and/or move the rest pose towards the centre, e.g. `(0.09,−0.17,0.42)`.
This change does **not** alter the contract; if #31 changes it, re-run `fpp_build.py` (it reads `fpp_contract.py`).

## 7. Verification

See the gates table in the PR. Logs: `docs/METINAR-FPP-ART-001/unity-*.txt`, Blender report
`docs/METINAR-FPP-ART-001/blender_build_report.json`.

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
- The Player budget is a proposal (owner decision).

## 10. Owner / #31 decisions

1. Accept the `Player` category and its budget (section 4) or set different limits.
2. #31: keep code-driven sockets or play the baked clips (both match); framing of the rest pose (section 6).
