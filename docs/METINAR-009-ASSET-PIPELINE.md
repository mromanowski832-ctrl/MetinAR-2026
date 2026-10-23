# METINAR-009 — Mobile 3D asset pipeline + first production mob plan

Issue: #16 (supersedes the research notes of backlog issue #15, whose Blacksmith requirements are preserved here).
Branch: `feat/009-mobile-asset-pipeline` (from `main` 8557a3e). Runs in parallel with METINAR-007 (#8) and METINAR-008 (#9).

## 0. Scope and status of this document

This document defines how original 3D characters, weapons, animations, materials and textures enter the Unity
project so the procedural placeholder spider (METINAR-006) and future NPCs can be replaced by production assets.

What is delivered in this issue:

- this document (budgets, folders, naming, import rules, prefab template, animation states, LOD plan,
  spider and Blacksmith production briefs, performance test plan, licence/source tracking);
- the empty pipeline folders under `client/Assets/MetinAR` with a `README.md` each;
- editor tooling under `client/Assets/MetinAR/Editor/AssetPipeline/` (import defaults, validator, self-test);
- pure tests `tests/AssetPipeline.Tests` and a CI workflow `asset-pipeline-tests`.

What is **not** changed: hand tracking / sword / combat code, AR encounter logic and scenes, GPS/location core
(`GeoCoordinate`, `GeoMath`), world-map code, `Prefabs/ArPrototypeSpider.prefab`, existing art in `Art/`.
No packages were added. No third-party asset was added.

**Every number below is a proposal** derived from common mobile practice for a phone that is also running
AR camera tracking. None of them has been measured on the owner's phones yet (section 10 describes how).
Permanent visual identity, final sizes, the final mob/NPC roster and anything listed in section 12 are owner decisions.

## 1. Mobile budgets (proposal)

AR costs a large part of the frame before any game content is drawn (camera image, tracking, plane detection,
later the METINAR-007 hand-tracking inference). Character budgets are therefore conservative.

### 1.1 Per asset

| Category | LOD0 triangles | LOD1 | LOD2 | Bones (max) | Materials / sub-meshes | Texture set | Notes |
|---|---|---|---|---|---|---|---|
| Regular mob | ≤ 8 000 (target 4–6k) | ≤ 60 % of LOD0 (target 50 %) | ≤ 30 % (target 25 %) | 40 | ≤ 2 (target 1) | 1024² (2048² only with written justification) | spider target ≈ 5–6k |
| Boss | ≤ 20 000 | ≤ 60 % | ≤ 30 % | 75 | ≤ 3 | 2048² | one boss on screen at a time |
| NPC | 10 000–20 000 (Blacksmith target from #15) | ≤ 60 % | ≤ 30 % | 75 (humanoid) | ≤ 3 (body, clothing/hair, prop) | 2048² max | humanoid rig |
| Weapon / hand prop | ≤ 3 000 | optional | — | 0 (static) | 1 | 512²–1024² | attached to a socket/hand bone |
| Player FPP arms (METINAR-FPP-ART-001) | ≤ 10 000 | — (no LOD: constant screen size) | — | 40 | ≤ 2 | 1024² (2048² justified) | `Characters/Player/<Id>/`, camera-local pivot, shadows off; see docs/METINAR-FPP-ART-001.md |

- Skin weights: max 4 bones per vertex (Unity *Standard* skin weights).
- Blend shapes: none unless an issue requires facial animation.
- One UV set for the colour textures (UV0). A second UV channel only if a baked-AO/lightmap workflow is approved.
- The 2K exception for a mob is recorded in `ASSET_SOURCE.txt` as `texture_2k_justification: <reason>`;
  the importer and validator then allow 2048.

### 1.2 Per AR encounter frame (proposal)

| Item | Budget |
|---|---|
| Visible characters | 1 regular mob now (METINAR-006 spawns exactly one); plan for up to 3 mobs or 1 boss |
| Character triangles on screen | ≤ 30 000 total |
| Character draw calls (SetPass) | ≤ 15 (+ HUD) |
| Character texture memory | ≤ 24 MB (ASTC) |
| Frame rate | stable 30 fps minimum on the owner's test Android phone; 60 fps where the device allows |
| Thermal | no throttling-driven drop below 30 fps within a 10 min session |

### 1.3 Textures and materials

| Map | Suffix | Colour space | Contents |
|---|---|---|---|
| Base colour | `_BC` | sRGB | hand-painted albedo; alpha only if the material uses alpha clipping |
| Normal | `_N` | linear, *Normal map* type | tangent space, OpenGL convention (+Y), MikkTSpace |
| Metallic / smoothness | `_MS` | linear | R = metallic, A = smoothness (URP Lit "Metallic Alpha"), G/B unused |
| Ambient occlusion | `_AO` | linear | optional, only if not baked into `_BC` |
| Emission | `_E` | sRGB | optional (glowing eyes, venom markings) |
| Mask | `_M` | linear | optional packed mask for a custom shader (needs an approved shader) |

Texture sizes must be powers of two.

### 1.4 Animation counts and clip naming

| Category | Required clips | Optional clips |
|---|---|---|
| Regular mob / boss | Idle, Walk, Attack, Hit, Death | Run, Spawn, Aggro, Attack02, Stun |
| NPC | Idle, Walk, TurnLeft, TurnRight, Talk, Interact | Gesture, Hit, Death, plus NPC-specific work loops (Blacksmith: Hammering) |
| Weapon | none (animated by the holder) | — |

Clip files: `A_<AssetId>_<State>[_NN].fbx`, **one clip per file**, clip name inside Unity = file name
(e.g. `A_VenomousSpider_Attack_02`). Authoring frame rate 30 fps. Looping by default: Idle, Walk, Run, Talk,
Hammering; everything else plays once. Mob clips are **in place** (no root motion); movement is driven by code.
Whether NPC walk uses root motion is an implementation choice for the NPC integration issue.

## 2. Folders and naming

### 2.1 Folders under `Assets/MetinAR`

```
Assets/MetinAR/
  Characters/
    Mobs/<MobId>/            regular mobs            e.g. Mobs/VenomousSpider/
    Bosses/<BossId>/         bosses
    NPC/<NpcId>/             NPCs (humanoid)         e.g. NPC/Blacksmith/
    Player/<Id>/             first-person player arms (METINAR-FPP-ART-001)  e.g. Player/FppArms/
  Weapons/<WeaponId>/        weapons + hand props    e.g. Weapons/BlacksmithHammer/
  Animations/<SetId>/        animation sets shared by several characters
  Materials/[<SetId>/]       shared URP materials
  Textures/<SetId>/          shared textures (trim sheets, shadow blobs)
  Prefabs/                   existing prototype prefabs (unchanged); production prefabs live in their asset folder
  Art/                       existing procedural prototype art (unchanged, outside the pipeline)
```

Inside every per-asset folder:

```
<AssetId>/
  ASSET_SOURCE.txt     mandatory provenance/licence record (section 11)
  README.md            optional notes
  Models/              SK_/SM_ FBX files
  Animations/          A_ clip FBX, AC_ animator controller, AM_ avatar masks
  Textures/            T_ PNG/TGA
  Materials/           M_ URP materials
  Prefabs/             PF_ prefabs and prefab variants
```

No other sub-folders and no deeper nesting (the validator enforces this).
DCC working files (`.blend`, `.spp`, `.psd`, `.ma/.mb`, `.max`, `.ztl`, `.kra`, `.xcf`) never go inside `Assets/`:
Unity would need the DCC application installed to import them, and they bloat the repository. Keep them in the
owner's art-source storage (location is an owner decision, section 12) and commit exported FBX/PNG only.

### 2.2 Names

Ids are PascalCase ASCII (`VenomousSpider`, `Blacksmith`, `BlacksmithHammer`) — no Polish diacritics, spaces or
underscores in ids. Display names ("Trujący Pająk", "Kowal") are game data, not asset names.

| Asset | Pattern | Example |
|---|---|---|
| Skinned model | `SK_<Mob|Boss|Npc|Weapon|Player>_<Id>[_Part].fbx` | `SK_Mob_VenomousSpider.fbx` |
| Static model | `SM_<Mob|Boss|Npc|Weapon|Player>_<Id>[_Part].fbx` | `SM_Weapon_BlacksmithHammer.fbx` |
| LOD meshes (inside the FBX) | `<ModelName>_LOD0`, `_LOD1`, `_LOD2` | `SK_Mob_VenomousSpider_LOD1` |
| Animation clip file | `A_<Id>_<State>[_NN].fbx` | `A_Blacksmith_Hammering.fbx` |
| Animator controller | `AC_<Id>.controller` | `AC_VenomousSpider.controller` |
| Avatar mask | `AM_<Id>[_Part].mask` | `AM_Blacksmith_UpperBody.mask` |
| Texture | `T_<Id>[_Part]_<BC|N|MS|AO|E|M>.png/.tga` | `T_VenomousSpider_Eyes_E.png` |
| Material | `M_<Id>[_Part].mat` | `M_VenomousSpider.mat` |
| Prefab | `PF_<Mob|Boss|Npc|Weapon|Player>_<Id>[_Variant].prefab` | `PF_Mob_VenomousSpider.prefab` |
| Bones | `B_<Name>[_L|_R][_NN]` (generic rigs); humanoid rigs keep the DCC/Rigify names mapped by Unity's avatar | `B_LegL_01_Upper` |
| Sockets (empty transforms) | `Socket_<Name>` | `Socket_HandR`, `Socket_Mouth` |
| Anchors (prefab children) | `Anchor_<Name>` | `Anchor_Label` |

The model token must match the folder (a `SK_Npc_` file inside `Characters/Mobs/` is an error) and the id must
match the asset folder.

## 3. Unity import rules

`MetinARAssetImportPostprocessor` applies these defaults automatically to **new** files inside the pipeline folders
(only on the first import, so a deliberate Inspector override is kept). `AssetPipelineValidator` reports deviations.

### 3.1 FBX scale and orientation

- Units: metres. 1 Unity unit = 1 m. Model at real AR size (section 8/9).
- Blender export (FBX): Scale 1.0, *Apply Scalings: FBX All*, Forward **-Z**, Up **Y**, *Apply Unit* on,
  *Use Space Transform* on, *Add Leaf Bones* off, *Bake Animation* on for clip files, armature *Primary Bone Axis* Y.
  The character faces Blender **-Y** (the Front view looks at its face). Apply all object transforms (Ctrl+A) before
  export; the armature and mesh objects have identity transforms.
- Unity: Scale Factor 1, *Convert Units* on, *Bake Axis Conversion* on → the model's front faces **+Z**, up is **+Y**
  (same as the METINAR-006 placeholder spider, whose pivot is on the ground facing +Z).
- Pivot: ground contact point in the centre of the footprint (mobs, NPCs); grip point for weapons/props.
- The validator flags a mesh whose lowest point is not within ±2 cm of the pivot height and sizes outside 5 cm–10 m
  (usual sign of a centimetre/metre export mistake).

### 3.2 Rig

| Asset | Rig | Avatar |
|---|---|---|
| Humanoid NPC (`Characters/NPC`) | **Humanoid** | *Create From This Model* on `SK_Npc_<Id>.fbx`; clip files use *Copy From Other Avatar* (that model) |
| Shared humanoid animation sets (`Animations/`) | Humanoid | Copy From Other Avatar |
| Mob / boss (spider, beasts) | **Generic** | root bone `B_Root` at the pivot; clip files reuse the model's skeleton |
| Weapon / prop | None | static mesh, parented to a socket |

Humanoid requires a standard humanoid skeleton (hips, spine, chest, neck, head, arms, legs; fingers optional).
Non-humanoid creatures must not be forced into Humanoid.
`Optimize Game Objects` stays off until socket/bone access needs are known (combat/hand-sword integration).

### 3.3 Mesh

| Setting | Value | Why |
|---|---|---|
| Read/Write | **off** | no CPU copy of the mesh in builds (halves mesh memory); colliders are primitives, not mesh colliders |
| Mesh Compression | **Off** (Low allowed after a visual check) | compression only reduces build size and can produce visible artefacts on skinned joints |
| Optimize Mesh | polygons + vertices on | GPU vertex cache order |
| Normals | Import | painted/baked normals from the DCC |
| Tangents | Calculate Mikktspace | matches Blender/Substance bakes |
| Blend shapes, cameras, lights, constraints | off | not used |
| Generate colliders | off | colliders are authored in the prefab |
| Skin weights | Standard (4 bones) | mobile skinning cost |
| Weld vertices | on | |

### 3.4 Materials (URP)

- Material creation from the FBX is **off** (*Material Creation Mode: None*); materials are authored `.mat` files in
  the asset's `Materials/` folder.
- Shader: **URP Lit** for the Base Colour / Normal / Metallic-Smoothness workflow (#15 requirement).
  *Workflow Mode* Metallic, *Smoothness Source* Metallic Alpha, *Surface* Opaque. Alpha clipping only where needed
  (e.g. hair cards). No transparent characters (overdraw + sorting problems on top of the AR background).
- URP Simple Lit or Unlit are allowed for distant LOD2 or very stylised parts if it looks right.
- Custom Shader Graphs only after an approved issue (name them `Shader Graphs/MetinAR...`); the validator rejects
  non-URP shaders.
- GPU instancing on for mob materials (several identical mobs).
- Hand-painted look: lighting/AO may be painted into `_BC`; keep metallic mostly 0 and smoothness low except metal
  (hammer head, fangs) so AR lighting does not make the paint look plastic.

### 3.5 Textures

| Setting | Value |
|---|---|
| Max size | category budget (1024 mob/weapon, 2048 boss/NPC; 2048 for a mob with justification) |
| Android override | **ASTC 6×6** for `_BC`, `_MS`, `_AO`, `_E`, `_M`; **ASTC 4×4** for `_N` |
| iOS override | same ASTC formats |
| Mipmaps | on |
| Read/Write | off |
| sRGB | on only for `_BC` and `_E` |
| Normal maps | Texture Type *Normal map* |
| Compression quality | Normal |

ASTC is supported by the GPUs of ARCore/ARKit-class phones (the project already requires Android 12 / API 31+);
an older non-ASTC GPU would get a decompressed fallback, which is acceptable because such devices are unlikely to
pass the ARCore *Required* check anyway. Not verified on the owner's device yet.

### 3.6 Animation

- Animation Type as in 3.2; *Import Animation* only on `A_` clip files (mesh files carry no takes).
- *Anim. Compression* **Optimal**, error tolerances at Unity defaults (0.5° rotation / 0.5 % position / 0.5 % scale);
  tighten per clip only if the feet slide or fingers jitter.
- *Resample Curves* on.
- One clip per file, clip name = file name, *Loop Time* per section 1.4 (set automatically on first import).
- Root motion: off for mobs (*Bake Into Pose* for root rotation/position Y), NPC decision in the integration issue.
- Animation events (e.g. the frame where the spider's attack "lands") are recorded in the clip brief and added in
  Unity on the clip import settings, not baked as extra curves.

### 3.7 Prefab hierarchy template

The pipeline produces **visual prefabs**. Gameplay components (the current `ArEncounterMob` label/HP logic, future
combat hit handling from METINAR-007) are added by the integration issue as a prefab variant or by the scene builder,
so art can be replaced without touching gameplay code.

```
PF_Mob_<Id>                      root: position 0, rotation 0, scale 1 (validator: scale must be 1)
├── Model                        LODGroup (LOD0/1/2 renderers below)
│   ├── SK_Mob_<Id> (FBX)        Animator (AC_<Id>), the imported hierarchy
│   │   ├── B_Root ...           skeleton
│   │   ├── SK_Mob_<Id>_LOD0     SkinnedMeshRenderer
│   │   ├── SK_Mob_<Id>_LOD1
│   │   └── SK_Mob_<Id>_LOD2
├── Colliders
│   └── Body                     CapsuleCollider or BoxCollider (primitive only, no MeshCollider)
├── Anchors
│   ├── Anchor_Label             world-space name/level/HP label position (above the highest point)
│   └── Anchor_Fx_<Name>         optional effect points
└── ContactShadow                blob-shadow quad on the ground (as in the METINAR-006 spider)
```

NPC: same, with `PF_Npc_<Id>`, Humanoid `Animator`, `Socket_HandR` (or the humanoid right-hand bone) for a held prop,
and `Anchor_Label` / `Anchor_Interact` instead of mob hit colliders. Weapons: `PF_Weapon_<Id>` → `Model` (MeshRenderer)
with the pivot at the grip.

Validator checks on prefabs: no missing scripts, root scale 1, LODGroup present for characters, a primitive collider
on mobs/bosses (warning), `Anchor_Label` present (warning), no MeshColliders (warning).

## 4. Animation state list

| State | Mob/boss | NPC | Loop | Notes |
|---|---|---|---|---|
| Idle | required | required | yes | 2–4 s, subtle; must not drift off the pivot |
| Walk | required | required | yes | in place for mobs; ~1 s cycle |
| Run | optional | — | yes | |
| Attack | required | — | no | clear wind-up before the hit frame (readable on a phone) |
| Hit | required | optional | no | 0.3–0.5 s, returns to Idle pose |
| Death | required | optional | no | ends lying on the ground, no ground penetration, last frame held |
| Spawn / Aggro / Stun / Attack02 | optional | — | no | only if an issue asks for them |
| TurnLeft / TurnRight | — | required | no | 90° in place |
| Talk | — | required | yes | talking loop |
| Gesture | — | optional | no | #15 "Talk/Gesture" |
| Interact | — | required | no | player interaction / hand-over |
| Hammering | — | Blacksmith | yes | NPC-specific work loop |

Player FPP arms (`Player` category, METINAR-FPP-ART-001): required `Idle` (loop), `Attack`, `AuraActivate`; timings follow #31 FppCombatTimeline.

The Animator Controller (`AC_<Id>`) and its parameters/transitions are **not** designed here: they depend on the
combat (METINAR-007) and NPC interaction systems that do not exist yet. The pipeline only guarantees the clips.

## 5. LOD plan

| Level | Triangles | Use | Screen-relative transition height (proposal) |
|---|---|---|---|
| LOD0 | 100 % | close AR view (≈ 0.5–3 m) | > 0.25 |
| LOD1 | ~50 % (≤ 60 %) | 3–8 m, several mobs | 0.25 → 0.08 |
| LOD2 | ~25 % (≤ 30 %) | far / map preview / thumbnails | 0.08 → 0.01 |
| Culled | — | | < 0.01 |

- LODs are separate meshes inside the same FBX named `_LOD0/_LOD1/_LOD2` (Unity creates the LODGroup on import).
- All LODs share the same skeleton, materials and UVs; LOD2 may drop finger/fang/eye detail bones only if those
  bones carry no animation that matters at that distance.
- Silhouette must survive LOD1 (legs of the spider stay 8 distinct legs).
- Transition heights must be tuned on a device: in AR the player usually stands 1–3 m from the mob, so LOD0 will be
  on screen most of the time and the LOD0 budget is the one that really matters.

## 6. Collider and ground-contact rules

- Primitive colliders only (Capsule/Box/Sphere) on the `Colliders` child; no MeshCollider on characters.
- Collider covers the body mass (the part a sword should hit), not the thin legs.
- The prefab pivot (y = 0) is the ground: the ground plane from METINAR-006 places the root on the detected plane.
- Idle/Walk keep feet on y = 0 (± 1 cm); Death ends on the ground, not below it.
- A soft blob shadow (`ContactShadow`) stays because AR lighting estimation/shadows are not implemented.
- Collider layer, trigger vs solid, and hit reaction are combat decisions (METINAR-007 / later combat issue).

## 7. Legal / free creation sources and tooling

**Never** use Metin2 (or any other commercial game's) models, textures, animations, music, sounds, UI, logos or
ripped/fan-extracted files, and do not trace or photobash their screenshots. "Inspired by the genre" = original work.

Recommended free tools (licence of the tool does not restrict the content you make with it):

| Purpose | Tool | Licence / cost |
|---|---|---|
| Modelling, sculpting, retopology, UVs, rigging (Rigify), animation, baking, FBX export | Blender | GPL (free); your output is yours |
| Hand-painted textures | Krita, GIMP | GPL (free) |
| Procedural materials / texture painting | Material Maker | MIT (free) |
| Auto-retopology | Instant Meshes | BSD (free) |
| Texture packing (MS/mask channels) | Krita/GIMP channel tools or a small Blender compositor setup | free |
| Unity side | Unity 6000.6.4f1 + URP (already in the project) | — |

Acceptable external **content** sources (each still needs an `ASSET_SOURCE.txt`):

| Source type | Status |
|---|---|
| CC0 libraries (e.g. Poly Haven textures/HDRIs, Kenney, Quaternius) | allowed — check each item's page, keep the licence text |
| CC-BY-4.0 items | allowed with the exact attribution line in the manifest (credits screen needed later) |
| MIT / Apache-2.0 / BSD-3-Clause content | allowed |
| CC-BY-SA, GPL-licensed content | **owner decision** (share-alike obligations) |
| Unity Asset Store items (even free) | **owner decision**: Asset Store EULA allows use in a game but not redistribution of the source files; acceptable only while the repo stays private and with a recorded owner approval |
| Mixamo (auto-rig/animations) | **owner decision**: free with an Adobe account, governed by Adobe terms, not an open licence |
| AI 3D/texture generators | **owner decision** per tool: output ownership depends on the tool's terms; recorded with `generator_terms_url` + `owner_approved: yes` |
| Paid assets or commissions | **owner decision + purchase approval**; commissions need a rights transfer (`rights_transfer`) |
| NC / ND / "editorial" / "personal use" / unknown licence | **never** |

## 8. First production mob: Venomous Spider (replacement of the placeholder)

Replaces the METINAR-006 procedural `ArPrototypeSpider` (in-game name "Trujący Pająk", PROJECT_BRIEF "Venomous Spider").
Asset id: `VenomousSpider`, folder `Assets/MetinAR/Characters/Mobs/VenomousSpider/`.

### 8.1 Look and silhouette

- Original dark-fantasy MMORPG, hand-painted look; readable at 1–5 m on a phone screen over a real-world camera image.
- Silhouette must read as a spider from above and from the side: clearly separated cephalothorax and larger abdomen,
  8 long jointed legs, visible fangs/chelicerae, eye cluster. Exaggerated proportions (bigger abdomen, thicker legs
  than a real spider) for readability on a small screen.
- Colour reading kept from the placeholder as a **starting point**: dark purple/black body, toxic-green venom
  markings on the abdomen, red glowing eyes (emission), pale fangs. Final palette = owner decision.
- High value contrast between body and markings so it reads on grass, asphalt and indoor floors.

### 8.2 Scale for AR

- Leg span ≈ 1.3 m and body top ≈ 0.6 m above the ground — same as the placeholder, so the existing
  label height (≈ 0.95 m) and AR placement minimum (0.3 m plane) keep working. Final size = owner decision.
- Pivot at the ground in the centre of the footprint, facing +Z.

### 8.3 Topology and UVs

- LOD0 target 5–6k triangles (budget 8k); LOD1 ≈ 2.5–3k; LOD2 ≈ 1.2–1.5k.
- Quads in deforming areas, 2–3 edge loops at each leg joint, legs as 6–8-sided tubes, no hidden interior faces,
  no n-gons in the export, no overlapping UVs except deliberate mirroring (left/right legs may share UV space;
  the abdomen pattern must not be mirrored if it is asymmetric).
- One material, one texture set: `T_VenomousSpider_BC`, `_N`, `_MS` (1024²) + optional `_E` (eyes/markings).
  Texel density consistent across body and legs.

### 8.4 Rig (Generic) — ≈ 30–34 bones (budget 40)

```
B_Root (ground, pivot)
└── B_Body (cephalothorax)
    ├── B_Abdomen ── B_Abdomen_Tip (optional)
    ├── B_Head ── B_Fang_L, B_Fang_R
    └── B_Leg<L|R>_<01..04>_Coxa / _Upper / _Lower      (8 legs × 3 = 24)
```

IK controls stay in Blender and are baked into the exported clips. `Socket_Mouth` (attack/venom effect point) under `B_Head`.

### 8.5 Animations

| Clip | Length (proposal) | Notes |
|---|---|---|
| `A_VenomousSpider_Idle` | 2–3 s loop | breathing abdomen, small leg shifts, feet planted |
| `A_VenomousSpider_Walk` | ~1 s loop, in place | alternating tetrapod gait (L1-R2-L3-R4 / R1-L2-R3-L4) |
| `A_VenomousSpider_Attack` | ~1.0–1.2 s | rear up + fang strike; wind-up ≥ 0.4 s; "hit" frame documented for an animation event |
| `A_VenomousSpider_Hit` | 0.3–0.5 s | recoil, returns to idle pose |
| `A_VenomousSpider_Death` | 1.5–2.5 s | legs curl, body settles on the ground, last frame held |

Attack timing/damage values are **not** decided here (combat issue).

### 8.6 Ground contact and colliders

- Feet tips at y = 0 in Idle; no foot sliding in Walk (in place); Death pose on the ground.
- `Colliders/Body`: one CapsuleCollider (or Box) along Z covering cephalothorax + abdomen,
  centre ≈ (0, 0.35, −0.05) m, radius ≈ 0.25 m, length ≈ 0.85 m (to be fitted to the final mesh). No leg colliders.
- `ContactShadow` blob quad ≈ 1.5 × 1.4 m as in the placeholder.
- `Anchor_Label` ≈ 0.95 m above the pivot (current label height).

### 8.7 Deliverables and acceptance

- `Models/SK_Mob_VenomousSpider.fbx` (LOD0/1/2 inside), 5 clip FBX files, `Textures/` set, `Materials/M_VenomousSpider.mat`
  (URP Lit), `Prefabs/PF_Mob_VenomousSpider.prefab` following 3.7, `ASSET_SOURCE.txt`.
- `MetinAR > Asset Pipeline > Validate` (or the batch method) reports **0 errors**.
- Performance test (section 10) on the owner's Android phone and an iPhone if available.
- Owner visual approval on a device screenshot/video in AR.
- Integration into the AR encounter (replacing `ArPrototypeSpider` in the METINAR-006 scene builder, wiring
  `ArEncounterMob` to `Anchor_Label`) is a **separate follow-up issue** so it does not collide with METINAR-007.

### 8.8 Production steps (who does what)

1. Owner confirms the open decisions in section 12 that block the spider (author/source, palette, size).
2. Artist (owner or approved person) produces the asset in Blender/Krita following 8.1–8.6.
3. Agent imports into a branch, runs the validator, builds the visual prefab, captures Play Mode screenshots on LuzMen.
4. Agent opens the integration issue/PR; physical-device check by the owner.

The agent does not create the final production model itself in this issue: no original artwork exists yet and its
authorship/source is an owner decision.

## 9. Blacksmith (Kowal) NPC production brief

Preserves the #15 requirements. Asset id `Blacksmith`, folder `Assets/MetinAR/Characters/NPC/Blacksmith/`;
hammer as a separate asset `Assets/MetinAR/Weapons/BlacksmithHammer/`.

| Item | Requirement |
|---|---|
| Look | original dark-fantasy MMORPG, hand-painted; readable at 2–10 m (NPCs have fixed GPS spots and may be seen from further away) |
| Triangles | LOD0 10–20k; LOD1 ≈ 50 %; LOD2 ≈ 25 % |
| Rig | Unity **Humanoid** (standard humanoid bones; Rigify or equivalent), ≤ 75 bones, 4 weights/vertex |
| UVs | clean, non-overlapping (except deliberate mirroring), consistent texel density |
| Textures | up to 2K: Base Color / Normal / Metallic-Smoothness (`T_Blacksmith_BC/_N/_MS`), optional `_E` (forge glow) |
| Materials | ≤ 3 (e.g. body/skin, clothing/apron, hair) — URP Lit |
| Hammer | **separate object** `SM_Weapon_BlacksmithHammer.fbx` (≤ 3k tris, 1 material, 512–1024² textures), pivot at the grip, attached at runtime to `Socket_HandR` / the right-hand bone |
| Animations | Idle, Hammering (loop), Walk, TurnLeft, TurnRight, Talk (+ optional Gesture), Interact; optional Hit, Death |
| LODs | LOD0/1/2 |
| Delivery | FBX + separate textures (no embedded media) |
| Scale | real human scale ≈ 1.75–1.85 m (final height = owner decision); pivot between the feet, facing +Z |
| Prefab | `PF_Npc_Blacksmith` per 3.7 with `Anchor_Label`, `Anchor_Interact`, `Socket_HandR` |
| Interaction, dialogue, shop/crafting | **not designed here** — owner decisions |

An anvil/forge prop is not in the #15 list; if wanted it becomes a separate asset (owner decision).

## 10. Performance test plan (Android / iOS)

Not run in this issue (no production asset and no phone connected). To run for the first production asset:

1. **Build**: development build with *Autoconnect Profiler* (Android: existing `MetinARAndroidDevBuild` path, ARM64/IL2CPP;
   iOS: Xcode development build on a Mac — no Mac/iPhone setup exists yet in this project).
2. **Test scene**: an isolated test scene (not the live AR scene) with the asset on a plane at 1 m, 3 m and 8 m,
   variants with 1, 3 and 5 instances, each playing Idle/Walk/Attack; then the same asset inside the AR encounter
   after integration.
3. **Devices**: the owner's Android phone (model to be recorded) and, if available, an iPhone. Record model, OS version,
   battery level, ambient temperature.
4. **Measure** (Unity Profiler + Frame Debugger; Android GPU Inspector or Xcode Instruments optionally):
   frame time (median, 95th percentile), fps, CPU main/render thread, GPU time if available, SetPass calls,
   triangles, skinned mesh count, texture memory, total memory, and fps after 10 minutes (thermal).
5. **Pass criteria** (proposal): budgets in section 1 respected; ≥ 30 fps median and 95th percentile frame time ≤ 40 ms
   with 3 mobs in AR on the owner's Android phone; no fps drop below 30 after 10 minutes; no visible LOD popping at
   1–3 m; textures readable (no ASTC artefacts on the face/markings).
6. **Report**: numbers + screenshots in the PR; anything not measured stays **UNVERIFIED**.

Compilation, the editor validator and Play Mode in the Editor are **not** a substitute for this device test.

## 11. Licence / source tracking

Every per-asset folder and every shared set folder contains `ASSET_SOURCE.txt` (`key: value` lines, `#` comments).
The validator (and the pure tests) enforce it.

Required keys: `asset_id` (= folder id), `origin`, `author`, `license`, `commercial_use` (must be `yes`),
`created_with`, `date_added`.

| `origin` | Meaning | Extra required keys | `license` |
|---|---|---|---|
| `original` | made for MetinAR by the owner/team | — | `MetinAR-Owned` |
| `commissioned` | made by a hired artist | `rights_transfer` (contract/invoice reference) | `MetinAR-Owned` |
| `third_party` | from a library/marketplace | `source_url`, `license_file` (committed licence text), `attribution` for CC-BY | `CC0-1.0`, `CC-BY-4.0`, `MIT`, `Apache-2.0`, `BSD-3-Clause`; SA/GPL/Asset Store/Mixamo/ToS only with `owner_approved: yes` |
| `generated` | AI or online generator output | `generator_terms_url`, `owner_approved: yes` | the tool's terms |

Optional keys: `modifications`, `notes`, `texture_2k_justification`, `owner_approved`.
Always rejected: NC/ND/editorial/personal/unknown licences, `commercial_use` ≠ yes, any reference to Metin2,
Webzen, Gameforge or Ymir in `source_url`, `author`, `created_with`, `notes` or `license`.

Example:

```
# Assets/MetinAR/Characters/Mobs/VenomousSpider/ASSET_SOURCE.txt
asset_id: VenomousSpider
origin: original
author: <artist name>
license: MetinAR-Owned
commercial_use: yes
created_with: Blender 4.x, Krita 5.x
date_added: 2026-10-20
modifications: n/a
notes: replaces the METINAR-006 procedural placeholder
```

Generated vs third-party vs original is therefore always visible per folder; existing procedural prototype art in
`Art/` was generated by the project's own editor scripts (METINAR-004/006) and stays outside the pipeline.

**Per-asset register (METINAR-041 E1, #86).** Every content file under `client/Assets/MetinAR` needs exactly one
source: its nearest `ASSET_SOURCE.txt`, a row in `Audio/LICENSES.md`, or a row in a CSV fragment in
[`docs/assets/provenance/`](assets/provenance/README.md) (used for the loose prototype art above). Metin2, Webzen,
Gameforge and YMIR are rejected in every source and in file paths. Checked by `AssetProvenanceRepositoryTests`.

## 12. Open owner decisions

1. Who creates the spider and Blacksmith (owner, a hired artist, approved free source, an AI tool)? Budget for any
   commission/purchase.
2. Final spider palette, exact size in AR (proposal: 1.3 m span, same as the placeholder) and level of "cuteness vs horror".
3. Blacksmith height, look (age, clothing, hair), and whether an anvil/forge prop is wanted.
4. Whether Unity Asset Store, Mixamo, CC-BY-SA/GPL or AI-generated content may be used at all (default: no).
5. Where DCC source files (`.blend`, paint files) are stored (outside the repo, or a separate repo with Git LFS — LFS is
   not installed/used today).
6. Target devices for the performance pass criteria (owner's Android phone model; is an iPhone available? iOS
   builds also need a Mac/Xcode, not set up).
7. Confirmation of the budgets in section 1 after the first device measurement.
8. Whether mobs keep the floating name/level/HP label above them in the final art direction.

## 13. Tooling delivered

All under `client/Assets/MetinAR/Editor/AssetPipeline/` (Editor-only; no runtime code, no scene/prefab changes):

| File | Role |
|---|---|
| `AssetPipelineRules.cs` | pure C#: folders, naming patterns, texture roles, budgets, LOD/size checks, required states |
| `AssetSourceManifest.cs` | pure C#: `ASSET_SOURCE.txt` parser and licence/provenance validation |
| `MetinARAssetImportPostprocessor.cs` | first-import defaults for models, clips and textures inside pipeline folders only |
| `AssetPipelineValidator.cs` | menu `MetinAR > Asset Pipeline > Validate`; batch `MetinAR.Editor.AssetPipeline.AssetPipelineValidator.RunBatch` (report `client/Builds/metinar-009-asset-validation.txt`, exit 1 on errors) |
| `AssetPipelineSelfTest.cs` | batch `MetinAR.Editor.AssetPipeline.AssetPipelineSelfTest.Run`: imports generated temporary textures in a throw-away pipeline folder and outside it, checks the applied settings and validator verdicts, deletes them (report `client/Builds/metinar-009-asset-selftest.txt`) |

Tests: `dotnet test tests/AssetPipeline.Tests/AssetPipeline.Tests.csproj` (CI workflow `asset-pipeline-tests`).
FBX-specific checks (triangle counts, bones, ground pivot, clip count/naming, avatar setup) are implemented but can
only be exercised once a real FBX exists; the self-test covers textures and provenance only.

## 14. Verification

See the PR / issue `AGENT READY FOR REVIEW` comment for the exact results of this branch.

