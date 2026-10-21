# METINAR-018 — Audio pipeline + starter combat SFX

Issue: #42 (stage 3 of the production program #41). Branch: `feat/042-audio-pipeline-combat-sfx` from `main` `e236629`.
Owner of this area: the "Dźwięk i muzyka" bot. Unity / A31 time only through reservations in #33.

## 1. What this delivers

- Folder layout `client/Assets/MetinAR/Audio/` with `.meta` files carrying the mobile import settings.
- `Audio/LICENSES.md`: one row per audio file (origin, author, licence, source, SHA-256).
- Pure rules `client/Assets/MetinAR/Editor/Audio/AudioPipelineRules.cs` (no UnityEngine): naming, folders, import
  profiles, budgets, WAV/Ogg header reader, licence ledger parser, `.meta` reader.
- Unity `AudioImportPostprocessor.cs`: applies the same profile to NEW audio files under `Assets/MetinAR/Audio` on their
  first import only (like the METINAR-009 postprocessor). Existing `.meta` files are not rewritten.
- Licence + budget gate in `tests/AssetPipeline.Tests/AudioPipelineTests.cs`, run in CI by the existing
  `asset-pipeline-tests` workflow (`dotnet test`).
- 10 original SFX made by `tools/audio/generate_sfx.py` (procedural synthesis, Python 3 + numpy, fixed seeds).
- Not done here (by scope): no wiring into combat logic, no AudioMixer asset, no music.

## 2. Layout and naming

| Folder (under `Assets/MetinAR/Audio`) | Content | File name |
|---|---|---|
| `SFX/Combat/` | player weapon / shield sounds | `SFX_Combat_<Event>_<NN>.wav` |
| `SFX/Mobs/<MobId>/` | per-mob sounds, `<MobId>` = METINAR-009 asset id | `SFX_Mob_<MobId>_<Event>_<NN>.wav` |
| `SFX/UI/` | interface | `SFX_UI_<Event>_<NN>.wav` |
| `Music/` | music (future) | `MUS_<Name>[_<NN>].ogg` |
| `Ambience/` | loops/beds (future) | `AMB_<Name>_<NN>.ogg` |

`<NN>` numbers variants (`_01`, `_02`, ...) so runtime code can pick randomly (e.g. spider steps).

## 3. Import profiles for the Samsung Galaxy A31 (proposal)

| Category | Source | Load Type | Compression | Mono | Preload | Load in background |
|---|---|---|---|---|---|---|
| SFX (Combat, Mobs, UI) | 16-bit PCM WAV, ≤ 48 kHz | Decompress On Load | ADPCM | yes | yes | no |
| Music | Ogg Vorbis | Streaming | Vorbis q 0.50 | no | no | yes |
| Ambience | Ogg Vorbis | Compressed In Memory | Vorbis q 0.40 | no | no | yes |

Set in `defaultSettings` and in the `Android` and `iPhone` overrides of every `.meta` (Unity 6000.6 AudioImporter
layout, serializedVersion 8; the gate also reads the older numeric keys 7/4). `normalize` must be 0. PCM output is rejected by the
gate. Why ADPCM + Decompress On Load for SFX: short, frequently replayed clips; ADPCM is ~3.5× smaller than PCM in the
APK and decoding cost at load is negligible; at runtime they play from PCM with no per-play CPU decode, which matters on
the A31 while AR tracking runs. The whole starter set is ~0.34 MB as PCM in RAM.

Music must never be fully loaded: Streaming keeps one decoder per playing track. Vorbis lossy re-encode of an `.ogg`
source is accepted for music (source quality ≥ q 6 recommended).

## 4. Budgets (proposal, enforced by the test)

| Category | Max duration | Max source file | Sample rate | Channels | Bits |
|---|---|---|---|---|---|
| Combat / Mob SFX | 2.0 s | 256 KiB | 8–48 kHz | mono | 16 |
| UI SFX | 0.5 s | 64 KiB | 8–48 kHz | mono | 16 |
| Music | 240 s | 6 MiB | 8–48 kHz | ≤ 2 | Vorbis |
| Ambience | 60 s | 2 MiB | 8–48 kHz | ≤ 2 | Vorbis |
| All SFX sources together | — | 2 MiB | | | |

Runtime voice count (`AudioManager` real voices 32) is unchanged. Not measured on the A31 yet.

## 5. Licence rules

- Owner decision 2026-10-21 (#42, supersedes the earlier "CC-BY not for now"):
  - `original` / `MetinAR-Owned`: made by the project.
  - `cc0` / `CC0-1.0`: download URL and author in `Source`.
  - `cc_by` / `CC-BY-x.y` (e.g. CC-BY-4.0): download URL and author in `Source`, plus one row in the **CC-BY attribution**
    table of `Audio/LICENSES.md`:
    `| File | Title | Author | Source URL | License | License URL | Attribution | Changes |`.
    Field names and order follow the credits spec (`docs/design/MODES_DATA_MODEL.md` §5, PR #44), i.e. the
    `credits.json` keys `title`, `author`, `sourceUrl`, `license`, `licenseUrl`, `attribution`, `changes`; the
    `files` list of a credits entry comes from the `File` column. Rules: all fields required (empty or `-` rejected);
    `Author` and `License` equal the ledger row; `Source URL` is an absolute http(s) URL; `License URL` is the CC deed
    `https://creativecommons.org/licenses/by/x.y/` for that version; `Attribution` is the ready-to-display TASL text and
    must name the title, the author and `CC BY x.y`; `Changes` describes the edits or says `unmodified`. Attribution
    rows without a `cc_by` ledger entry, duplicates and the outdated 6-column table (3f032cb) are rejected.
  - Excluded: CC licences with NC, ND or SA terms (CC-BY-NC, CC-BY-ND, CC-BY-SA and combinations, any version or
    spelling). Anything else is an owner decision.
- CC-BY entries must appear on an in-game credits screen. There is none yet: dependency for the spec #40.
- Format examples in fenced code blocks inside `LICENSES.md` are ignored by the parser.
- Forbidden: Metin2 / Gameforge / Ymir content, ripped game audio, paid libraries, unclear licences.
- The ledger stores the SHA-256 of each file; replacing the bytes without updating provenance fails the test.

## 6. Starter SFX (all `original`, MetinAR-Owned)

| File | Length | WAV | ADPCM est. | Use |
|---|---|---|---|---|
| `SFX/Combat/SFX_Combat_SwordSwing_01.wav` | 0.38 s | 33 560 B | ~9.3 KB | sword swing (whoosh) |
| `SFX/Combat/SFX_Combat_SwordHit_01.wav` | 0.42 s | 37 088 B | ~10.3 KB | blade hits a mob |
| `SFX/Combat/SFX_Combat_ShieldBlock_01.wav` | 0.50 s | 44 144 B | ~11.4 KB | shield block (wood + metal rim) |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Attack_01.wav` | 0.55 s | 48 554 B | ~12.4 KB | spider attack (hiss + mandibles) |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Hit_01.wav` | 0.36 s | 31 796 B | ~8.3 KB | spider hurt |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Death_01.wav` | 1.20 s | 105 884 B | ~26.7 KB | spider death |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_01..03.wav` | 0.12 s each | 10 628 B each | ~3.2 KB each | leg taps (3 variants) |
| `SFX/UI/SFX_UI_Click_01.wav` | 0.06 s | 5 336 B | ~2.1 KB | UI button |

Total sources 338 246 B; ADPCM estimate (ffmpeg `adpcm_ima_wav`) ~90 KB. Peaks normalised: hits/blocks −1 dBFS,
mob −2 dBFS, swing −3 dBFS, steps and UI click −6 dBFS (quieter by design). Unity `normalize` is off so these levels hold.

Regenerate: `python3 tools/audio/generate_sfx.py`. Check bytes: `python3 tools/audio/generate_sfx.py --check`
(bit-identical with numpy 2.5.4; other numpy versions may differ in the last bits — then update the SHA-256 in LICENSES.md
only together with the regenerated files).

## 7. Hooks for stage 5 (not wired in this issue)

| Sound | Suggested hook | Notes |
|---|---|---|
| SwordSwing | `FppCombatController.RequestAttack()` returning `true` (PR #32, `feat/fpp-hands-aura-prototype`) or the Attack clip start | 2D/near-listener (player's own weapon) |
| SwordHit | `FppCombatController.HitDetected(mob, swingId)` | once per swing (already de-duplicated by `TryRecordHit`); 3D at the mob |
| ShieldBlock | future block/parry event | no shield/block mechanic exists yet (stage 1 spec #40) |
| Spider Attack | Animation Event on `A_VenomousSpider_Attack` (PR #30) or server attack event | 3D on the spider |
| Spider Hit | `A_VenomousSpider_Hit` start / damage-applied event | 3D |
| Spider Death | `A_VenomousSpider_Death` start / mob defeated state | 3D, no overlap with Hit |
| Spider Step 01..03 | Animation Events on foot contacts of Walk/Run clips | random variant, pitch ±5 %, rate-limit (8 legs!) |
| UI Click | `HudButtonBar` / HUD button `onClick` | 2D, UI mixer group |

Suggested runtime (stage 5, not decided): a small `AudioCue` ScriptableObject (clips + volume/pitch range + spatial blend)
and an AudioMixer with groups Master / SFX / UI / Music, created in the Unity Editor (requires a #33 slot).

## 8. Verification status

- `dotnet test tests/AssetPipeline.Tests` — run on the cloud computer (see PR).
- `tools/qa/Test-RepoHygiene.ps1 -BaseRef origin/main` and `Test-DocLinks.ps1` — run on the cloud computer.
- Unity 6000.6.4f1 on LuzMen (slot in #33, separate fresh checkout `MetinAR-042-audio018`, batchmode): import + compile
  exit 0, 0 `error CS`; `AudioImportVerifier.RunBatch` (menu *MetinAR/Audio/Verify Audio Import Settings*) PASS for all
  clips: default/Android/iOS = ADPCM + Decompress On Load + preload, mono, clips 1 ch 44.1 kHz. A probe WAV without
  `.meta` got the profile from `AudioImportPostprocessor` (incl. `normalize: 0`) and was deleted again. The committed
  `.meta` files are in the layout Unity writes itself and were not rewritten by a re-import.
- AudioMixer: not added. Unity has no public API to create an `.mixer` asset from a script; it needs one manual
  Editor step (Create > Audio Mixer, groups Master/SFX/UI/Music) in a later slot.
- Listening test on the A31 speaker: **UNVERIFIED**.
