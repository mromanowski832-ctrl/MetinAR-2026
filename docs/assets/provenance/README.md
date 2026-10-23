# Asset provenance register (METINAR-041 E1, #86)

Every **content file** under `client/Assets/MetinAR` must have exactly **one** licence/provenance source, and no source
may name Metin2, Webzen, Gameforge or YMIR. The pure test `AssetProvenanceRepositoryTests` in
`tests/AssetPipeline.Tests` (CI: `asset-pipeline-tests.yml`) fails the build otherwise. Rules live in
`client/Assets/MetinAR/Editor/AssetPipeline/AssetProvenanceRegister.cs`.

## Scope

Content = models (`.fbx .obj .blend …`), images (`.png .tga .psd .exr …`), audio, fonts, video and Unity-serialized
content (`.mat .anim .controller .prefab .unity .mixer .shadergraph …`); see `AssetProvenanceRegister.ContentExtensions`.
Code (`.cs`, `.asmdef`), `.meta`, docs (`.md`, `.txt`) and `.asset` settings/data are out of scope.
Every file path in scope (also code) is still scanned for the forbidden names.

## The three accepted sources (one per file)

| Source | Covers | Rules |
|---|---|---|
| `ASSET_SOURCE.txt` (METINAR-009 §11) | every content file in its folder and sub-folders (nearest one wins) | `AssetSourceManifest` |
| `client/Assets/MetinAR/Audio/LICENSES.md` | audio files listed in its ledger table (METINAR-018) | read with the audio parser `AudioLicenseLedger.Parse` (header-based, both tables); its `ParseErrors` fail the register; every field of the ledger **and** CC-BY tables is scanned for forbidden names |
| `docs/assets/provenance/*.csv` (this folder) | single loose files that have no `ASSET_SOURCE.txt` folder | below |

A file covered by two sources (e.g. a CSV row inside an `ASSET_SOURCE.txt` folder) is an error, so a folder is
registered in exactly one place. Prefer `ASSET_SOURCE.txt` for per-asset folders (`Characters/…`, `Weapons/…`,
`Textures/ItemIcons/`); use CSV rows for loose generated prototype files.

## CSV fragments

One fragment per PR or asset set (e.g. `main-prototype-art.csv`, `pr-045-merchant.csv`), so parallel PRs never edit the
same file. Lines starting with `#` are comments. Header (exact):

```
path,sha256,asset_id,origin,author,license,commercial_use,created_with,date_added,source_url,notes
```

- `path`: relative to `client/Assets/MetinAR`, `/` separators; must exist; one row per file across all fragments.
- `sha256`: lowercase SHA-256 of the file. May be empty only for Unity-serialized assets that editor scripts re-save
  (`.mat .prefab .unity .mixer .anim …`); images, models, audio and fonts always carry it, so a replaced binary must be
  re-registered.
- Remaining columns follow the `ASSET_SOURCE.txt` rules (origins `original` / `commissioned` / `third_party` /
  `generated`, accepted licences, `commercial_use: yes`). Third-party rows need `source_url` and the extra keys go into
  an `ASSET_SOURCE.txt` folder instead (the CSV has no `license_file` column), so third-party assets always live in a
  per-asset folder with the licence text.

Generate rows with the current hashes:

```
python tools/assets/provenance_rows.py --asset-id ArEncounter --date 2026-10-20 \
  --created-with "Unity editor script ..." client/Assets/MetinAR/Art/ArEncounter/*.png >> docs/assets/provenance/<fragment>.csv
dotnet test tests/AssetPipeline.Tests --filter AssetProvenance
```

## Licences: graphics vs audio (agreed with METINAR-040, PR #99 review)

- One logical record for audio and graphics: `path, sha256, origin, author, license, source_url` plus, for CC-BY, the
  credits fields `title, license_url, attribution, changes` (names/order as the credits spec, MODES_DATA_MODEL §5).
  Audio keeps its two Markdown tables (`Audio/LICENSES.md`, unchanged); graphics use `ASSET_SOURCE.txt` / this CSV.
- **Two separate, deliberate owner decisions** (controller confirmation 2026-10-23 04:39, #99); neither register
  copies the other's rule:
  - **Graphics** (owner decision 2026-10-21; METINAR-009 §11, MODES_DATA_MODEL §5, ITEMS_COMBAT_SPEC): `MetinAR-Owned`,
    `CC0-1.0`, **`CC-BY-4.0` only** (with attribution), `MIT`, `Apache-2.0`, `BSD-3-Clause`. SA/GPL only with
    `owner_approved: yes` in an `ASSET_SOURCE.txt` (the CSV has no such column, so SA is always rejected there).
  - **Audio** (owner decision #42, 2026-10-21 05:02; METINAR-018, `Audio/LICENSES.md`): `MetinAR-Owned`, `CC0-1.0`,
    **`CC-BY-x.y` (any version)** with authors and a full CC-BY attribution row. **SA always rejected.**
  - NC/ND are always rejected in both. Pinned by `GraphicsAndAudioLicencePoliciesAreSeparateOwnerDecisions`.
- CC-BY columns (`title, license_url, attribution, changes`) will be appended to the CSV in that order when the first
  CC-BY graphic lands (credits generator #40 then reads one attribution schema). Until then third-party/CC-BY graphics
  go into an `ASSET_SOURCE.txt` folder with `license_file` and `attribution`.

## Open PRs at the time of E1

#36, #45, #63, #64 and #69 already ship `ASSET_SOURCE.txt` folders and pass the check on their branches. Anything they add
outside such a folder goes into their own fragment (`pr-<n>-<topic>.csv`) when they merge `origin/main`.
