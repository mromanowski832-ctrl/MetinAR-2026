# METINAR-032 — Starter-kit item icons (original, procedural 2D)

Issue: #59 (icons part; the `starter_buckler` mesh belongs to the 3D models agent). Branch: `feat/032-starter-item-icons` (from `main` 6a49039).
Spec: [docs/design/ITEMS_COMBAT_SPEC.md](design/ITEMS_COMBAT_SPEC.md) §7 (items) and §8 (budget: item icons 256² PNG, atlases 2048², ASTC).
Licence register: [docs/METINAR-032/ICON_LICENSES.md](METINAR-032/ICON_LICENSES.md).

## 1. Deliverables

| Item id (`items_starter.csv`) | Name (game) | File in `client/Assets/MetinAR/Textures/ItemIcons/` |
|---|---|---|
| `starter_jerkin` | Kaftan Wędrowca | `T_ItemIcons_StarterJerkin_BC.png` |
| `starter_boots` | Buty Traktowe | `T_ItemIcons_StarterBoots_BC.png` |
| `starter_buckler` | Lipowy Puklerz | `T_ItemIcons_StarterBuckler_BC.png` |
| `amber_earring` | Bursztynowy Kolczyk | `T_ItemIcons_AmberEarring_BC.png` |
| `starter_gloves` | Rękawice Rekruta | `T_ItemIcons_StarterGloves_BC.png` |
| `starter_sword` | Miecz Rekruta | `T_ItemIcons_StarterSword_BC.png` |
| `potion_hp_s` | Mała Mikstura Życia | `T_ItemIcons_PotionHpS_BC.png` |
| `antidote` | Antidotum | `T_ItemIcons_Antidote_BC.png` |
| `ore_shard` | Odłamek Rudy | `T_ItemIcons_OreShard_BC.png` |

Preview: [docs/METINAR-032/contact_sheet.png](METINAR-032/contact_sheet.png).

- **Format:** 256×256 RGBA PNG, transparent background, sRGB, about 2 px dark outline, soft drop shadow, top-left key light.
  The sword and gloves follow the FPP assets from PR #36 (steel blade with blue runes, dark leather with a brass zig-zag bracer).
- **Folder and naming:** a shared texture set `Textures/ItemIcons/` with `ASSET_SOURCE.txt`. Names follow the METINAR-009
  texture pattern `T_<SetId>_<Part>_BC.png`, with `Part` being the PascalCase item id. The existing validator accepts them
  as-is, so no pipeline rule was changed.
- **Import settings** (`Editor/ItemIcons/ItemIconImportPostprocessor.cs`, first import only, order after the METINAR-009 postprocessor):
  - Sprite (Single), sRGB, alpha is transparency, Read/Write off, clamp, bilinear, max 256.
  - ASTC 4×4 overrides on Android and iOS (outlines stay crisp; 64 KB per icon).
  - Mipmaps stay **on**: icons are shown smaller than 256 px in inventory slots, and the METINAR-009 validator warns when mipmaps are off.
- **Atlas:** not committed. The 9 icons together are 9 × 256² and fit a single 1024² page, well under the 2048² limit. A Sprite Atlas
  belongs to the inventory UI integration, because the shared-texture rules of METINAR-009 accept only `T_` textures in this folder.

## 2. Source and reproduction

```bash
python art-source/ItemIcons/make_icons.py <out_dir> [--masters]
```

`icon_lib.py` is a small 2D painter: polygons, ellipses and strokes are rasterised on a 1024² master and shaded with a bevel/height
model and seeded value noise. The master is then downsampled to 256² (premultiplied-alpha Lanczos). The output is deterministic:
two runs give byte-identical PNGs. No external image, font, brush or icon pack is loaded. The contact sheet's label font
(DejaVu Sans) is only used for the preview, never in the icons.

## 3. Verification

- `MetinAR.Editor.ItemIcons.ItemIconsCheck.Run` (Unity batch) checks for every icon:
  - texture import, 256² source with alpha, Sprite/sRGB/alpha settings;
  - ASTC overrides on Android and iOS, the sprite loads;
  - transparent corners and border, 15–75 % coverage.
  It also checks `ASSET_SOURCE.txt`, runs the METINAR-009 validator on the folder (0 errors, 0 warnings) and renders a preview
  of the imported sprites. Results are in `docs/METINAR-032/unity/`.
- Result (Unity 6000.6.4f1, URP, real batch run on the local Windows machine; clean checkout of `96555a4` plus the final
  sword PNG and check script, byte-identical to this branch): **57 checks, 0 failures**
  ([unity-icon-check.txt](METINAR-032/unity/unity-icon-check.txt)). METINAR-009 validator on the whole project: **errors=0, warnings=0**
  ([report](METINAR-032/unity/metinar-009-asset-validation.txt)). `MetinARQaChecks`: PASS (0 missing scripts). Import + compile: 0 `error CS`.
- Unity preview of the imported (ASTC) sprites: [unity_icons_grid.png](METINAR-032/unity/unity_icons_grid.png) (about 340 px per slot),
  [unity_icons_small.png](METINAR-032/unity/unity_icons_small.png) (about 120 px per slot, phone inventory scale).
- `AssetPipelineValidator.RunBatch` on the whole project: report in `docs/METINAR-032/unity/`.

## 4. Known limitations

- No Sprite Atlas yet (see above). It comes with the inventory UI integration.
- Icons are not wired to item data or UI. `InventoryPanel` still uses the prototype visuals.
- Physical device: UNVERIFIED (no Android build in this issue).
