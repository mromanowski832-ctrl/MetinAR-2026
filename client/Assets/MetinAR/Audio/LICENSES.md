# MetinAR audio — licence and provenance ledger (METINAR-018)

Every audio file under `client/Assets/MetinAR/Audio` must have exactly one row in the table below.
The test `tests/AssetPipeline.Tests` (`AudioRepositoryTests`) fails when a file has no row, a row has no file,
the SHA-256 does not match the bytes, the licence is not allowed or the file breaks its budget / import profile.

Rules (see `docs/METINAR-018-AUDIO-PIPELINE.md`):

- `origin`: `original` (made by the project, licence `MetinAR-Owned`), `cc0` (licence `CC0-1.0`, source must contain the
  download URL and the author), or `third_party` (open licence such as `CC-BY-4.0`, URL + author + attribution required).
- Allowed licences: `MetinAR-Owned`, `CC0-1.0`, `CC-BY-4.0`. Any other licence needs an owner decision first.
- Forbidden: anything from Metin2 / Gameforge / Ymir, paid sound libraries, ripped game audio, unclear licences.
- `File` is relative to `client/Assets/MetinAR/Audio`.

## Ledger

| File | Origin | Author | License | Source | SHA-256 |
|---|---|---|---|---|---|
| `SFX/Combat/SFX_Combat_ShieldBlock_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` shield_block(), seed 1803 | `cde91634837be8e33828e24dfc10bf8efb70abdaddb496a17eb354f58cab4f3e` |
| `SFX/Combat/SFX_Combat_SwordHit_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` sword_hit(), seed 1802 | `814ca6b61cdddc14ea329784ff6788e022e0dceef32637504d599ea1118bb793` |
| `SFX/Combat/SFX_Combat_SwordSwing_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` sword_swing(), seed 1801 | `1978df6dfb8aafb8778e628840d23808aa39868e87cfd06b732c11482b0c123d` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Attack_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_attack(), seed 1804 | `372f331af71bdaa77687ce6cc887ad5374f002e2c83a9f31cf3ad9829aa2463e` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Death_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_death(), seed 1806 | `808a2d75c59a04c3297bb1decac364777923534fc0034e2742ea019aca141377` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Hit_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_hit(), seed 1805 | `c1c35a74fbb1aaa095c38a1b6394e865e3834cb003e0ac1a9b193bdd3eb4e4df` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_step(), seed 1807 | `fd712d1890690ce0e46ee70374d589d2c3b4e4f27b7761b0f444199fd913fecb` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_02.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_step(), seed 1808 | `d6573903d641756da066617f8d3a646f5c943ceeb7e3d4191ac07da39255803d` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_03.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_step(), seed 1809 | `8d75ec0df76ac2e47d28c8c8ee405ee6ac720dbfc713993340c1579cb09b9821` |
| `SFX/UI/SFX_UI_Click_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` ui_click(), seed 1810 | `51e851c96b5c09e3e615912efa7a561cf0d0be3019ab55a5838fa999a23c6017` |

## Notes on the original SFX (METINAR-018)

- Generated 2026-10-21 entirely from oscillators, filtered pseudo-random noise and envelopes with Python 3 + numpy
  (`tools/audio/generate_sfx.py`, fixed seeds). No recordings, samples, sound libraries, presets or AI audio were used.
- Reproduce: `python3 tools/audio/generate_sfx.py`; verify: `python3 tools/audio/generate_sfx.py --check`.
- Source format: 16-bit PCM mono WAV, 44.1 kHz. Unity compresses on import (ADPCM, Decompress On Load; see the `.meta` files).
