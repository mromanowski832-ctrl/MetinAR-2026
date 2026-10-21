# METINAR-033 — Starter kit SFX

Issue: #60 (program #41, stage A2). Branch: `feat/060-starter-kit-sfx` from `main` `6a49039`.
Owner of this area: the "Dźwięk i muzyka" bot. Spec: `docs/design/` (PR #44): [COMBAT_PHYSICS.md](design/COMBAT_PHYSICS.md)
section 5 (sound events), [ITEMS_COMBAT_SPEC.md](design/ITEMS_COMBAT_SPEC.md) (SFX ≤ 1.5 s, ≤ 40 KB),
[ECONOMY.md](design/ECONOMY.md) (talers, shop).

**Depends on PR #43 (METINAR-018 audio pipeline).** `main` did not have the pipeline yet, so this branch contains a merge
commit of `feat/042-audio-pipeline-combat-sfx` at `8dc9e3e`. Merge PR #43 first, then this PR; after that the diff of this
PR is only the METINAR-033 work. Pipeline, ledger and gates: [METINAR-018-AUDIO-PIPELINE.md](METINAR-018-AUDIO-PIPELINE.md).

## 1. What this delivers

- 14 original one-shot SFX from `tools/audio/generate_sfx.py` (procedural synthesis, numpy, fixed seeds 3301–3314;
  `SFX_UI_Denied_01` uses no random source). No recordings, samples, libraries or AI audio; licence `MetinAR-Owned`,
  one row each in `Audio/LICENSES.md` with SHA-256.
- New pipeline folders `SFX/Item`, `SFX/Loot`, `SFX/Status` (same SFX import profile as Combat/UI).
- `.meta` files in the Unity 6000.6 AudioImporter layout (serializedVersion 8, `Android` / `iPhone` overrides):
  ADPCM + Decompress On Load, force mono, preload, `normalize: 0`.
- Gates (`tests/AssetPipeline.Tests`): new folders and names, UI/Item/Loot/Status budget 1.5 s, starter-kit set complete
  and one-shot, poison tick ≤ 0.5 s (shorter than the 1 s DoT interval), every one-shot SFX ≤ 1.5 s and ≤ 40 KB ADPCM
  estimate (spec).
- Not done here (by scope): no wiring into game code, no Unity import run (see section 4).

## 2. Files

| File (under `Audio/SFX`) | Event | Length | WAV (16-bit mono 44.1 kHz) | ADPCM est. | Peak |
|---|---|---|---|---|---|
| `Item/SFX_Item_EquipMetal_01.wav` | equip metal item (sword, buckler) | 0.45 s | 39 734 B | ~10.0 KB | −3 dBFS |
| `Item/SFX_Item_UnequipMetal_01.wav` | unequip metal item | 0.40 s | 35 324 B | ~8.9 KB | −5 dBFS |
| `Item/SFX_Item_EquipLeather_01.wav` | equip leather / cloth item (jerkin, boots, gloves) | 0.40 s | 35 324 B | ~8.9 KB | −4 dBFS |
| `Item/SFX_Item_UnequipLeather_01.wav` | unequip leather / cloth item | 0.35 s | 30 914 B | ~7.8 KB | −5 dBFS |
| `Item/SFX_Item_Potion_01.wav` | drink health / mana potion | 0.90 s | 79 424 B | ~20.0 KB | −3 dBFS |
| `Item/SFX_Item_Antidote_01.wav` | drink antidote (poison removed) | 0.90 s | 79 424 B | ~20.0 KB | −3 dBFS |
| `Loot/SFX_Loot_Common_01.wav` | pick up an item | 0.35 s | 30 914 B | ~7.8 KB | −4 dBFS |
| `UI/SFX_UI_CoinPickup_01.wav` | pick up talers | 0.40 s | 35 324 B | ~8.9 KB | −3 dBFS |
| `UI/SFX_UI_LevelUp_01.wav` | level up | 1.40 s | 123 524 B | ~31.1 KB | −6 dBFS |
| `UI/SFX_UI_ShopOpen_01.wav` | open the merchant shop | 0.70 s | 61 784 B | ~15.6 KB | −6 dBFS |
| `UI/SFX_UI_Buy_01.wav` | purchase confirmed | 0.45 s | 39 734 B | ~10.0 KB | −3 dBFS |
| `UI/SFX_UI_Sell_01.wav` | sale confirmed | 0.45 s | 39 734 B | ~10.0 KB | −3 dBFS |
| `UI/SFX_UI_Denied_01.wav` | refusal (not enough talers, bag full) | 0.35 s | 30 914 B | ~7.8 KB | −14 dBFS (dense tone) |
| `Status/SFX_Status_PoisonTick_01.wav` | one poison DoT tick (spider) | 0.25 s | 22 094 B | ~5.6 KB | −8 dBFS |

Starter kit: 684 166 B source, ~172 KB as ADPCM (estimate: 4 bits per sample + block headers, not a Unity build
measurement). All 27 SFX together: 1 357 704 B source (budget 2 MiB), ~342 KB ADPCM, ~1.36 MB PCM in RAM after
Decompress On Load (spec: audio memory ≤ 8 MB).

Levels: loudest 100 ms window of every clip between about −20 and −14 dBFS RMS (same range as the combat set); the poison
tick is quieter on purpose because it repeats every second; Denied has a low peak because it is a dense square-ish tone.
Checked by stats and spectrograms only; listening on the A31 is pending (slot request in #33 for #43).

## 3. Hooks for the Developer (not wired here)

| Sound | Trigger (proposal) | Notes |
|---|---|---|
| `SFX_Item_EquipMetal_01` / `SFX_Item_UnequipMetal_01` | item moved into / out of an equipment slot, item material metal (`starter_sword`, `starter_buckler`) | one sound per slot change, not per stat refresh |
| `SFX_Item_EquipLeather_01` / `SFX_Item_UnequipLeather_01` | same, material leather / cloth (`starter_jerkin`, `starter_boots`, `starter_gloves`) | jewellery (`amber_earring`): use Loot for now (question) |
| `SFX_Item_Potion_01` | `potion_hp_s`, `potion_hp_m`, `potion_mp_s` consumed (quick slot) | starts with the 2 s heal-over-time |
| `SFX_Item_Antidote_01` | `antidote` consumed (poison removed) | stop any pending poison tick |
| `SFX_Loot_Common_01` | item added to the bag from a drop | rarity variants `SFX_Loot_<Rarity>_01` later (spec section 5) |
| `SFX_UI_CoinPickup_01` | talers credited from a drop / reward | rate-limit: at most one per 150 ms |
| `SFX_UI_LevelUp_01` | character level increases | once per level, even if several levels at once (play once) |
| `SFX_UI_ShopOpen_01` | merchant shop panel opened (`shop_starter_merchant`) | not on every tab change |
| `SFX_UI_Buy_01` / `SFX_UI_Sell_01` | purchase / sale accepted | after the balance changed |
| `SFX_UI_Denied_01` | purchase refused (not enough talers), bag full, item cannot be equipped | replaces Click for refused actions |
| `SFX_Status_PoisonTick_01` | each poison DoT tick on the player (every 1 s, COMBAT_PHYSICS section 2) | one-shot per tick, pitch ±5 % optional; no loop |

All are 2D UI-style one-shots except PoisonTick (player status; 2D as well). Route them to the SFX / UI groups of
`Audio/Mixers/MetinARMixer.mixer` (Item, Loot, Status → SFX; UI → UI).

## 4. Verification and limits

- `dotnet test tests/AssetPipeline.Tests -c Release`, `Test-RepoHygiene.ps1`, `Test-DocLinks.ps1`,
  `python3 tools/audio/generate_sfx.py --check` (bit-identical regeneration of all 27 files).
- Unity not run: the new `.meta` files are copies of the profile verified on LuzMen for PR #43 (Unity 6000.6.4f1,
  AudioImportVerifier PASS) with new GUIDs; the pipeline code change is pure C# (no UnityEngine). Risk: a new folder
  `.meta` or category could behave differently on first import; to be checked in the next Unity slot (#33).
- Physical device (A31 listening and loudness): **UNVERIFIED**.
