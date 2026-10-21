# METINAR-IMPORT-001 — licensed client assets: conversion pipeline (#35)

Branch `tools/licensed-client-asset-import`. Coordination #33. Status: **preparation only** — no original model,
texture or animation has been converted or added to the project.

## 1. Licence status (explicit exception, not yet verified)

The owner states (#35, 2026-10-21 01:08Z) that his licence covers the original Metin2 models, textures and
animations and their use in MetinAR. The document has not been independently verified. The required scope is listed
in #35 (comment 01:10Z): parties, subject (models/skeletons/animations/textures), right to modify/convert,
use in a separate commercial mobile game, processing by contractors/tools, term/territory, signature.
Until that is confirmed: **no export of original assets into the game**. Provenance concern recorded by the controller
and the executor: the package on LuzMen matches a publicly posted dump of the Arezzo.pl private server (server code,
DB dumps with account/player data), i.e. it is not by itself proof of a licensed source.

Conflict to resolve before integration: `AssetSourceManifest`/validator of METINAR-009 rejects any `ASSET_SOURCE.txt`
mentioning Metin2/Gameforge/Ymir and has no `licensed` origin. A licensed exception needs an owner/controller decision
and a separate change of that validator — not done here.

## 2. Source facts (read-only, 2026-10-21)

- `Desktop\METIN2_CLIENT_PC\Arezzo_client.7z` on LuzMen: 2 489 706 277 B,
  SHA256 `07AE272A342A8E185331F3C1D1A24D1ACA888D17E73F4F4EDCB6991392BDBBBF`.
  Integrity: Windows built-in `tar.exe` (bsdtar 3.8.9 / libarchive 3.8.9) full decompression to `NUL` → exit 0, no errors
  (7-Zip is not installed; nothing was installed or written).
- Listing: 316 entries, 298 files, 2 940 953 486 B unpacked. Identical file count to the already-extracted `Arezzo/` folder.
- **No loose `.gr2/.dds/.msm/.msa/.mse/.eix/.epk` and no `npclist`/`mob_proto`/`item_proto`** in the archive or in the
  extracted `Arezzo/`. `Client/` and `Packer/` on the Desktop and in OneDrive contain only empty folders (0 files).
- Graphic client data lives in 23 pack pairs `*.data`/`*.idx` (2 800 859 392 B), e.g. `pack/entity/potwory2` (301.7 MB),
  `potwory` (112.8 MB), `npc` (84.5 MB), `npc2`, `postacie`, `postacie2`, `kostiumy`, `itemy` (30.7 MB), `ikony`, `etc`,
  `season`, `patch/patch_0x`, `maps/{mapy,obiekty,tekstury}`, `property`, `root`, `sound`. `pack/Index` is a plain list.
- Pack format: both `.idx` and `.data` blocks start with `MCOZ` (Metin2 LZO + XTEA block: compressed **and encrypted**),
  not the classic `EPKD` `.eix/.epk`. The file names inside (where spider/blacksmith/sword sets live, counts of
  `.gr2/.dds/...`) **cannot be listed without the pack keys**. The keys are not something this task may extract:
  they would come from the client binary (VMProtect present) or the package's `PackMakerLite.json`, and the brief forbids
  bypassing protection or running tools from the package. Expected mapping by name only: mobs → `entity/potwory*`,
  NPC/blacksmith → `entity/npc*`, weapons → `itemy` / `postacie*`.
- `granny2.dll` in the client: FileVersion 2.11.8.0 (read from file metadata, not loaded) → expected GR2 file format v7, 32-bit.

## 3. What unblocks the first real set

1. Licence scope confirmation (section 1).
2. Unpacked source files from a legitimate channel: the licensor/owner supplies the unpacked `ymir work` folders
   (the `Packer/*/ymir work` tree exists but is empty) or the official pack tool/keys under the licence.
3. Then: `build_manifest.py` → `gr2_probe.py` on the spider set → `blender_gr2_to_fbx.py --scale 0.01` →
   `blender_verify_fbx.py` → visual comparison with the original → Unity preview (LuzMen slot via #33) with
   `LicensedSetBuilder`.

## 4. Pipeline and test results

See `tools/asset-conversion/README.md`. Synthetic end-to-end: structure PASS, GR2 animation fidelity UNVERIFIED
(0.12–0.24 m deviation on WIP-exporter-generated test files), Unity NOT_RUN.
