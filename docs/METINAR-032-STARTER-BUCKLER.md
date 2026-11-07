# METINAR-032 — `starter_buckler` (Lipowy Puklerz): FPP shield mesh

Issue #59 (mesh part only; icons are delivered separately by the grafik agent). Spec: `docs/design/ITEMS_COMBAT_SPEC.md`
§7 (starter set) and §8 (budgets, row "Shield / quiver / totem / focus (FPP)"). Original asset, `MetinAR-Owned`.

## Result

| | Value | Budget §8 |
|---|---|---|
| Triangles | **1 368** (1 mesh, 1 sub-mesh) | ≤ 1 500 |
| Bones | 0 (static `SM_`) | 0 |
| Materials | 1 (`M_StarterBuckler`, URP Lit) | 1 |
| Textures | `T_StarterBuckler_BC` / `_N` / `_MS`, **512²**, ASTC 6×6 (BC, MS) / 4×4 (N) | 512² |
| LOD | none (1 mesh) | — |
| Size | Ø 0.362 m (planks Ø 0.34 m + iron rim), depth 0.124 m (grip to boss top) | — |

Look: linden (*lipa*) planks with dark seams, a faded ochre painted ring, edge wear; dark hammered iron rim,
boss and 8 rivets; leather-wrapped grip. It matches the PR #36 FPP set (dark iron, leather, warm wood/gold accents).

Renders (Blender Cycles): `docs/METINAR-032/renders/`; Unity URP screenshots: `docs/METINAR-032/unity/`.

## Files

```
art-source/StarterBuckler/buckler_build.py     build entry: mesh, textures, FBX, renders, build_report.json
art-source/StarterBuckler/buckler_model.py     procedural lathe/tube geometry with fixed UV atlas, sockets
art-source/StarterBuckler/buckler_texture.py   numpy painter in UV space (no bake), normal map from height
client/Assets/MetinAR/Weapons/StarterBuckler/  ASSET_SOURCE.txt, Models/SM_Weapon_StarterBuckler.fbx,
                                               Textures/T_StarterBuckler_{BC,N,MS}.png, Materials/M_StarterBuckler.mat,
                                               Prefabs/PF_Weapon_StarterBuckler.prefab (+ .meta for everything)
client/Assets/MetinAR/Editor/StarterBuckler/   StarterBucklerAssetBuilder.cs (material + prefab, import settings),
                                               StarterBucklerAssetCheck.cs (batch verification + screenshots)
```

Rebuild: `blender -b --factory-startup --python art-source/StarterBuckler/buckler_build.py -- <out_dir>` (Blender 4.2.3 LTS),
copy `Models/` and `Textures/` into the asset folder, then in Unity *MetinAR → Weapons → Build Starter Buckler*
(batch: `-executeMethod MetinAR.Editor.Weapons.StarterBucklerAssetBuilder.BuildBatch`). Textures are byte-identical
between runs; the FBX differs only in its header timestamp.

## Frame, pivot and sockets (integration contract)

- **Pivot = centre of the grip** (where the left fist closes). **Handle along +Y**, **front of the shield towards +Z**,
  +X to the right when looking at the front. This is the same grip-frame convention as the hand frames of PR #32/#36
  (fist wraps +Y, forward +Z), so the prefab is meant to be parented with identity local transform to a left-hand grip
  frame.
- Authoring follows METINAR-009 §3.1 (front faces Blender −Y); the build turns mesh and sockets 180° about Z for export,
  because Unity's FBX import with *Bake Axis Conversion* maps Blender +Y to Unity +Z (finding of METINAR-013). The
  asset check verifies +Z in Unity (normals of the boss top: (0, 0, 1.000)).
- Import: no rig, no animation, no extracted materials, Read/Write off, *Preserve Hierarchy* on (set by the builder).
  Unity keeps the FBX root node and puts the mesh node under it with a +90° X node rotation (normal FBX axis
  conversion); all checks are done in model-root space.
- Sockets (empty nodes, model-root space, identity rotation, verified ≤ 0.5°):
  - `Socket_Grip` (0, 0, 0): attach point (= pivot).
  - `Socket_FxBlock` (0, 0, 0.110): front of the boss, for a block spark / sound position.

### Dependency on PR #36 (not copied)

This branch is **based on `main` only**; PR #36 (`feat/fpp-art-hands-sword`) is neither changed nor copied. Holding
the buckler in the FPP hands needs, in the integration issue, either:
- the existing left-hand socket bone `B_Socket_GripL` of `PF_Player_FppArms` (PR #36), whose frame is the PR #32
  `LeftHand` frame. It is the sword's two-hand grip, so a left-hand **shield** pose or a dedicated `B_Socket_OffHand`
  bone and a shield-hold clip are a design/animation decision for that issue; or
- a code-driven placement in `FppHandsView` (PR #32) using the same convention.

Block / Aura gestures with the shield (ITEMS_COMBAT_SPEC §1: "left hand in the AURA gesture") are not part of this mesh
task.

## Validator / pipeline

- Category: `Weapons/StarterBuckler/` = the existing METINAR-009 **"Weapon / hand prop"** category on `main`
  (≤ 3 000 tris, 0 bones, 1 material, ≤ 1024², 1 LOD). The §8 shield budget is tighter and lies inside it, so
  **no validator rule was added or changed** (no overlap with the Player rules of PR #36). The tighter §8 limits
  (≤ 1 500 tris, 512²) are enforced by `StarterBucklerAssetCheck` (C03, C10).
- The weapon category has no ground-pivot rule (pivot at the grip), no LODGroup and no `Anchor_Label` requirement.

## Verification

See the PR for the full gate table (Unity 6000.6.4f1 on a clean checkout of the final SHA: compile, builder,
asset check, AssetPipelineValidator, QA missing scripts, AssetPipelineSelfTest; `dotnet test tests/AssetPipeline.Tests`;
CI). Raw report: `docs/METINAR-032/unity-asset-check.txt`.
