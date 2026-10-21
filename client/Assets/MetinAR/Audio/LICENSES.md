# MetinAR audio — licence and provenance ledger (METINAR-018)

Every audio file under `client/Assets/MetinAR/Audio` must have exactly one row in the table below.
The test `tests/AssetPipeline.Tests` (`AudioRepositoryTests`) fails when a file has no row, a row has no file,
the SHA-256 does not match the bytes, the licence is not allowed or the file breaks its budget / import profile.

Rules (see `docs/METINAR-018-AUDIO-PIPELINE.md`, owner decision 2026-10-21 in #42):

- `origin` / licence:
  - `original` → `MetinAR-Owned` (made by the project).
  - `cc0` → `CC0-1.0`; `Source` must contain the author and the download URL.
  - `cc_by` → `CC-BY-x.y` (e.g. `CC-BY-4.0`, `CC-BY-3.0`); `Source` must contain the author and the download URL, and the
    file needs one row in the **CC-BY attribution** table below (all fields required, none empty or `-`).
- Excluded: any CC licence with NC, ND or SA terms (`CC-BY-NC-*`, `CC-BY-ND-*`, `CC-BY-SA-*` and combinations such as
  `CC-BY-NC-SA-*`), anything from Metin2 / Gameforge / Ymir, paid sound libraries, ripped game audio, unclear licences.
  Any other licence needs an owner decision first.
- CC-BY rows must later appear on the in-game credits screen (no screen yet: dependency recorded for spec #40).
- `File` is relative to `client/Assets/MetinAR/Audio`. The SHA-256 is of the file bytes as committed.

Entry format (example only — fenced blocks are ignored by the test):

```text
| File | Origin | Author | License | Source | SHA-256 |
| `SFX/Combat/SFX_Combat_SwordHit_02.wav` | cc_by | Jane Doe | CC-BY-4.0 | Jane Doe, https://example.org/sounds/123 | `<64 hex>` |

| File | Title | Author | Source URL | License | License URL | Attribution | Changes |
| `SFX/Combat/SFX_Combat_SwordHit_02.wav` | Steel Hit | Jane Doe | https://example.org/sounds/123 | CC-BY-4.0 | https://creativecommons.org/licenses/by/4.0/ | "Steel Hit" by Jane Doe (https://example.org/sounds/123), licensed under CC BY 4.0; trimmed and converted to mono | trimmed to 0.4 s, mono, peak -1 dBFS |
```

Attribution fields (names and order = credits spec `docs/design/MODES_DATA_MODEL.md` section 5 / PR #44, read by the
`credits.json` generator of #40: `title`, `author`, `sourceUrl`, `license`, `licenseUrl`, `attribution`, `changes`):

- `Title`: title of the original work.
- `Author`: exactly as in the ledger row.
- `Source URL`: absolute http(s) link to the original.
- `License`: licence version, same as the ledger row (`CC-BY-x.y`).
- `License URL`: the licence deed, `https://creativecommons.org/licenses/by/x.y/`.
- `Attribution`: ready-to-display TASL text, shown on the credits screen as is. It must name the title, the author
  and the licence (`CC BY x.y`).
- `Changes`: what we changed (trim, fades, pitch, mix…) or `unmodified`.

All fields are required; empty or `-` fails the test. A `|` inside a cell is not allowed.

## Ledger

| File | Origin | Author | License | Source | SHA-256 |
|---|---|---|---|---|---|
| `SFX/Combat/SFX_Combat_AuraLoop_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` aura_loop(), seed 1811 | `0babcc79b81216c8e0ac7db913595a25cdddd6aa478ea13bd636c0702df268e7` |
| `SFX/Combat/SFX_Combat_AuraStart_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` aura_start(), seeds 1811, 1812 | `ede507271e05ad1b0c766c71583ecd8ceef396761852f04638cfb1cd6b720978` |
| `SFX/Combat/SFX_Combat_AuraStop_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` aura_stop(), seed 1813 | `d1b6b04d9f783345acf24f26d04ffcb748ebfec6835d23b96b7e9f1c9436a614` |
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
| `SFX/Item/SFX_Item_EquipMetal_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` equip_metal(), seed 3301 | `4f703f745f605eaee99a4257ea6d471c305985dc1231e815b44985ca2b97e606` |
| `SFX/Item/SFX_Item_UnequipMetal_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` unequip_metal(), seed 3302 | `d84825a5c54defe8f54eff694f7a425ec7295ccdac894fde85ba4f254c07843a` |
| `SFX/Item/SFX_Item_EquipLeather_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` equip_leather(), seed 3303 | `de03f079bacaaf006529c48d1d5cfb8df8cd59060c1d879a3e12581307cbde5a` |
| `SFX/Item/SFX_Item_UnequipLeather_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` unequip_leather(), seed 3304 | `8283b6e897920b6a27abb3dabcc41f3a629d1c5f7d4d326c84dbffaf94caa4a9` |
| `SFX/Item/SFX_Item_Potion_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` potion_drink(), seed 3305 | `ba149c8f158db40dc67537ee31d4e39b6253c3c3ed7775def24a12a430af6a0f` |
| `SFX/Item/SFX_Item_Antidote_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` antidote_drink(), seed 3306 | `e79c6974f7fcbc680ecb321010cd05ef243e02d5c68a9fb9b3f9dd714f74baf7` |
| `SFX/Loot/SFX_Loot_Common_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` loot_pickup(), seed 3307 | `d8a924d99fd3e4416b8f32774da08c6b0cee4cbd7ad07842f6cc21e3ae0a7a72` |
| `SFX/UI/SFX_UI_CoinPickup_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` coin_pickup(), seed 3308 | `ae71bc247fc5d6fdcb39461b5715211f845f33a628ad4bc0335d9e08d13f7fc3` |
| `SFX/UI/SFX_UI_LevelUp_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` level_up(), seed 3309 | `223020866d532d65588aa558014b2c716e03e7263726cdf42c045b391bc8f7aa` |
| `SFX/UI/SFX_UI_ShopOpen_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` shop_open(), seed 3310 | `200df3b3653ef58518127d0da992f247c6ce0cc1c16c26ee0e0f46b550cfd961` |
| `SFX/UI/SFX_UI_Buy_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` buy(), seed 3311 | `1d99642b8ad4e85b27281a16799e5cc3aa3399de1bbf079b43ed06331f66cde9` |
| `SFX/UI/SFX_UI_Sell_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` sell(), seed 3312 | `f281f910069cdbc1cb95fa21a81a6935bf74801a3079d6fa75ef2654085f65c7` |
| `SFX/UI/SFX_UI_Denied_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` denied(), no random source (deterministic tones) | `844c672a2a9cf39149c5a11734eded3c24ea0b70e992103ab241a49c437cc114` |
| `SFX/Status/SFX_Status_PoisonTick_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` poison_tick(), seed 3314 | `a4afa135fed0f5e968b500dae607d151b619afd40eaaa7c721c9610d25aa1c54` |

## CC-BY attribution

No CC-BY files yet. Add a table with the header
`| File | Title | Author | Source URL | License | License URL | Attribution | Changes |` here.

## Notes on the original SFX (METINAR-018)

- Generated 2026-10-21 entirely from oscillators, filtered pseudo-random noise and envelopes with Python 3 + numpy
  (`tools/audio/generate_sfx.py`, fixed seeds). No recordings, samples, sound libraries, presets or AI audio were used.
- Reproduce: `python3 tools/audio/generate_sfx.py`; verify: `python3 tools/audio/generate_sfx.py --check`.
- Source format: 16-bit PCM mono WAV, 44.1 kHz. Unity compresses on import (ADPCM, Decompress On Load; see the `.meta` files).
