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
  `CC-BY-NC-SA-*`), anything from Metin2 / Gameforge / Webzen / Ymir, paid sound libraries, ripped game audio, unclear licences.
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
| `SFX/Item/SFX_Item_EquipMetal_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` equip_metal(), seed 3301 | `64965b86bf173c08e747fc4d616805acb9749ac2fc945961e2cc4dce9d20ff9b` |
| `SFX/Item/SFX_Item_UnequipMetal_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` unequip_metal(), seed 3302 | `9cbfeb7c5348a98db4bcbe18290ab5c3aad94d511b4e4112f87e2eb76328a5c8` |
| `SFX/Item/SFX_Item_EquipLeather_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` equip_leather(), seed 3303 | `0ae19749622bbbf1d11fdbc8dce16db96be8cc0d771b88a54ff1d495bec9a256` |
| `SFX/Item/SFX_Item_UnequipLeather_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` unequip_leather(), seed 3304 | `f806f12754ea0288de9c3765442b1d4697f8d6dec57c78619cc46daa4911652d` |
| `SFX/Item/SFX_Item_Potion_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` potion_drink(), seed 3305 | `34560aa913728d7616297f0231efbfb2d414e1d0950b9c9c7338ad7f30c1d19c` |
| `SFX/Item/SFX_Item_Antidote_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` antidote_drink(), seed 3306 | `5b28945620400329976ec56a512ff03ab673c3a654a6b90c71c157f00256599c` |
| `SFX/Loot/SFX_Loot_Common_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` loot_pickup(), seed 3307 | `64d1a38f8b38968648cdfe6726aaa4c327e3dd2d86a36c38a8f48d6c932df752` |
| `SFX/UI/SFX_UI_CoinPickup_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` coin_pickup(), seed 3308 | `6afc278a9f076d919f2e9d0ddf06a0422f96105329b194a20fe0df55e2aa50c9` |
| `SFX/Stinger/SFX_Stinger_LevelUp_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` level_up(), seed 3309 | `7b6b32aac21ed5f4156c709a7f6a1559fe12695cddc8e24ddfeafabae1f89460` |
| `SFX/UI/SFX_UI_ShopOpen_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` shop_open(), seed 3310 | `5b2ee8c151cefc4f03acb678a4342720a1d4e2af76536eb95d97a6ae1a161348` |
| `SFX/UI/SFX_UI_Buy_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` buy(), seed 3311 | `9d95398d33b0a4ef814319e7738e3d7840faa63b85f0aa69da13879bb0109791` |
| `SFX/UI/SFX_UI_Sell_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` sell(), seed 3312 | `5b2f476757923db61e73f4d6ad2d1f3095d66fb94783f9d80c276120223988b2` |
| `SFX/UI/SFX_UI_Denied_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` denied(), no random source (deterministic tones) | `ad33762ff793108843d8a95a055fa55d0e859b708f2dffd9219f69d9f90202cd` |
| `SFX/Status/SFX_Status_PoisonTick_01.wav` | original | MetinAR project (Grok Bot — Dźwięk i muzyka, for the owner Michał Romanowski) | MetinAR-Owned | procedural synthesis, `tools/audio/generate_sfx.py` poison_tick(), seed 3314 | `ec784a106e385bb64c1942bc4dfa73697eeedf521777939bb6c3af1a3d42d33f` |

## CC-BY attribution

No CC-BY files yet. Add a table with the header
`| File | Title | Author | Source URL | License | License URL | Attribution | Changes |` here.

## Notes on the original SFX (METINAR-018)

- Generated 2026-10-21 entirely from oscillators, filtered pseudo-random noise and envelopes with Python 3 + numpy
  (`tools/audio/generate_sfx.py`, fixed seeds). No recordings, samples, sound libraries, presets or AI audio were used.
- Reproduce: `python3 tools/audio/generate_sfx.py`; verify: `python3 tools/audio/generate_sfx.py --check`.
- Source format: 16-bit PCM mono WAV, 44.1 kHz. Unity compresses on import (ADPCM, Decompress On Load; see the `.meta` files).
