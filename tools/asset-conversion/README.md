# tools/asset-conversion — METINAR-IMPORT-001 (#35)

Conversion toolchain for client model sets (Granny `.gr2` + `.msm/.msa/.mse` scripts + `.dds` textures)
into the Unity contract of `docs/METINAR-009-ASSET-PIPELINE.md`. **No game assets, archives, pack keys,
listings with player data, share links or tokens are ever committed here.** Tests use synthetic data only.

Status (2026-10-21): toolchain + synthetic end-to-end test on the box. No original asset converted
(licence scope not yet verified, and the client assets are inside encrypted packs — see `docs/METINAR-IMPORT-001.md`).

## Toolchain (Linux box, user space)

| Tool | Version | Source | Licence | Verified |
|---|---|---|---|---|
| Blender (headless) | 4.2.9 LTS | download.blender.org, SHA256 checked against the official `.sha256` | GPL | runs `-b` |
| io_scene_gr2 ("Granny2 Native") | 0.1.61 | github.com/Rasetsuu/blendergranny release asset (SHA256 pinned in `setup_box_tools.sh`) | MIT, pure Python, no Granny DLL/SDK | synthetic only; young project (May 2026), animation export/import WIP |
| Pillow | ≥ 10 (13.0 used) | PyPI | MIT-CMU | DDS DXT1/DXT5 read, PNG write |

Not used: Noesis (Windows-only), `granny2.dll` / `gr2tool.exe` or anything shipped with a game client
(we never load libraries from the package), EterNexus (targets `.eix/.epk`; this client uses a different,
encrypted `MCOZ` pack format — see doc).

`scripts/setup_box_tools.sh` downloads and verifies everything into `$TOOLS_DIR` (default `~/tools`) and prints the env vars.

## Scripts

| Script | Runs in | Purpose |
|---|---|---|
| `gr2_probe.py` | Python | read-only GR2 header probe: magic, version 6/7, sections, compression (raw/Oodle0/Oodle1/BitKnit) |
| `m2script.py` | Python | parser for `.msm/.msa/.mse`-style text scripts → JSON (+ referenced files) |
| `dds_to_png.py` | Python+Pillow | DDS/TGA → PNG, reports format, alpha used / binary alpha (→ URP alpha clipping) |
| `build_manifest.py` | Python | manifest CSV/JSON: relative path, SHA256, real type by magic, duplicates by content, name-collision variants, script references and missing dependencies |
| `blender_gr2_to_fbx.py` | Blender | model `.gr2` + clip `.gr2` files → `SK_<Cat>_<Id>.fbx` + `A_<Id>_<State>.fbx` with the FBX settings of METINAR-009 §3.1, `convert_report.json` |
| `blender_verify_fbx.py` | Blender | re-imports FBX: 1 armature, skin weights ⊆ bones, armature binding, triangles, height, pivot at ground, animated curves |
| `blender_compare_clip.py` | Blender | deformation fidelity: per-frame nearest-vertex distance between a reference FBX and a converted FBX |
| `blender_make_synthetic.py` | Blender | builds the synthetic test set (3-bone rig, weighted mesh, 3 clips, generated texture, `.msm/.msa`) |
| `run_synthetic_e2e.sh` | bash | full chain on synthetic data: make → manifest → probe → DDS→PNG → GR2→FBX → verify → fidelity |
| `unity/Editor/LicensedSetBuilder.cs` | Unity Editor | preview-project builder: importer settings, clips (loop per state, no root motion for mobs), URP Lit material, `AC_<Id>` controller, `PF_<Cat>_<Id>` prefab per §3.7. **NOT_RUN** |

```bash
eval "$(TOOLS_DIR=~/tools tools/asset-conversion/scripts/setup_box_tools.sh | tail -3)"
WORK=/tmp/e2e tools/asset-conversion/scripts/run_synthetic_e2e.sh
python3 -m pytest tools/asset-conversion/tests -q
```

Metin2 data is Z-up in centimetres: use `--scale 0.01` in `blender_gr2_to_fbx.py` (to be confirmed on the first real file).

## Known limits (synthetic test, 2026-10-21)

- Structure PASS: mesh (316 tris), 3 bones, weights, UVs, material slot, 3 clips with correct length (31/21/25 frames @30 fps), pivot at ground, DXT5 alpha preserved as binary alpha.
- FBX step is lossless (pose before export == pose after re-import).
- **GR2 animation fidelity NOT proven**: the synthetic `.gr2` files are written by the add-on's WIP exporter;
  the round trip deforms the mesh 0.12–0.24 m away from the Blender reference. Whether the error is in the
  WIP exporter, the importer, or the hand-written bind data cannot be decided without a Granny-authored
  reference file. First real set must be checked visually against the original (screenshot/video) before scaling.
- Clip FBX re-import starts at frame 2 (Blender FBX import offset); clip length is unchanged.
