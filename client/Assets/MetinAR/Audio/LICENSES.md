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
| `SFX/Combat/SFX_Combat_AuraLoop_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` aura_loop(), seed 1811 | `1cf9e77bfb3382b1973046b50a84df06bfec0dcdfc29b4747ee5e116e5606cd9` |
| `SFX/Combat/SFX_Combat_AuraStart_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` aura_start(), seeds 1811, 1812 | `68aeab5ea51f0bc0c074d46b2305321a932e79494c8ff0381ce822ab19391324` |
| `SFX/Combat/SFX_Combat_AuraStop_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` aura_stop(), seed 1813 | `f6fead8bed10b099ec55a236064f6239b55e97b912665b0fedb82d3f91e2d5b0` |
| `SFX/Combat/SFX_Combat_ShieldBlock_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` shield_block(), seed 1803 | `f673fa8a1e5967fc17f7676dfd61dbf07a56af46b47290fefb0d2a6fd1203b50` |
| `SFX/Combat/SFX_Combat_SwordHit_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` sword_hit(), seed 1802 | `b8b94666a93f1c71396f0a0bf78c30aa92a88d0dafe8186fe9afcaf11e5590fc` |
| `SFX/Combat/SFX_Combat_SwordSwing_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` sword_swing(), seed 1801 | `f2a4b650efeb260e593bbb09c1bdda1d8a5858c090b7a20e87def0d1142f21d4` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Attack_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_attack(), seed 1804 | `1331c408ed60eb70ad27b46334edf2aac6d8c42964485c656fba1e6054ad54f6` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Death_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_death(), seed 1806 | `7ab813557c1bfeb09e607db4220654d45cef7c8bc49980d4a25b7067b19e7ce1` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Hit_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_hit(), seed 1805 | `94a95e3be279f5e9ab8a2a28d94f6354d2c4da677f1d7342276a3a3677500c01` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_step(), seed 1807 | `ddf9d21838bd1c0cafb394a1ec1565e73e8d18410b13e11349152dbc93d0d400` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_02.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_step(), seed 1808 | `3845eb97893cdc36fe6de8aae50fcba4b7dc69cf6bd4076489e4e275047198a6` |
| `SFX/Mobs/VenomousSpider/SFX_Mob_VenomousSpider_Step_03.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` spider_step(), seed 1809 | `0e41c5e248b3777553f608b73fea7a92f4325452666af69d4aad6f34d3aae3bd` |
| `SFX/UI/SFX_UI_Click_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` ui_click(), seed 1810 | `aa76b1b194203d390c98d91fdfdb5e95db070668996999008fb1360b34932e73` |
| `SFX/Item/SFX_Item_EquipMetal_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` equip_metal(), seed 3301 | `4f703f745f605eaee99a4257ea6d471c305985dc1231e815b44985ca2b97e606` |
| `SFX/Item/SFX_Item_UnequipMetal_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` unequip_metal(), seed 3302 | `d84825a5c54defe8f54eff694f7a425ec7295ccdac894fde85ba4f254c07843a` |
| `SFX/Item/SFX_Item_EquipLeather_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` equip_leather(), seed 3303 | `de03f079bacaaf006529c48d1d5cfb8df8cd59060c1d879a3e12581307cbde5a` |
| `SFX/Item/SFX_Item_UnequipLeather_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` unequip_leather(), seed 3304 | `8283b6e897920b6a27abb3dabcc41f3a629d1c5f7d4d326c84dbffaf94caa4a9` |
| `SFX/Item/SFX_Item_Potion_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` potion_drink(), seed 3305 | `ba149c8f158db40dc67537ee31d4e39b6253c3c3ed7775def24a12a430af6a0f` |
| `SFX/Item/SFX_Item_Antidote_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` antidote_drink(), seed 3306 | `e79c6974f7fcbc680ecb321010cd05ef243e02d5c68a9fb9b3f9dd714f74baf7` |
| `SFX/Loot/SFX_Loot_Common_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` loot_pickup(), seed 3307 | `d8a924d99fd3e4416b8f32774da08c6b0cee4cbd7ad07842f6cc21e3ae0a7a72` |
| `SFX/UI/SFX_UI_CoinPickup_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` coin_pickup(), seed 3308 | `ae71bc247fc5d6fdcb39461b5715211f845f33a628ad4bc0335d9e08d13f7fc3` |
| `SFX/Stinger/SFX_Stinger_LevelUp_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` level_up(), seed 3309 | `223020866d532d65588aa558014b2c716e03e7263726cdf42c045b391bc8f7aa` |
| `SFX/UI/SFX_UI_ShopOpen_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` shop_open(), seed 3310 | `7106cf8516802133cb45b4123a33c9b7a0cbef3e5d92bc00f6584011b5122e4b` |
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
