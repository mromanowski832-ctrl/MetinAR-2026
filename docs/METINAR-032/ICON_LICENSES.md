# METINAR-032 — rejestr licencji ikon przedmiotów (zestaw startowy + partia 2)

Issue #59 · spec `docs/design/ITEMS_COMBAT_SPEC.md` §7 (pozycje) i §8 (budżet: 256² PNG, ASTC) · folder `client/Assets/MetinAR/Textures/ItemIcons/`.

Wszystkie ikony to **praca własna** dla MetinAR: rysunek 2D wygenerowany skryptem projektu
[`art-source/ItemIcons/make_icons.py`](../../art-source/ItemIcons/make_icons.py) (Python + numpy + scipy + Pillow).
Nie użyto zewnętrznych obrazów, zdjęć, fontów, pędzli, paczek ikon, treści AI ani plików z innych gier (w tym Metin2).
Atrybucja nie jest wymagana (brak treści CC-BY). Rekord w `ASSET_SOURCE.txt`: `origin: original`, `license: MetinAR-Owned`.

| Item id | Nazwa (gra) | Plik | Źródło | Autor | Licencja | Atrybucja | SHA-256 (PNG) |
|---|---|---|---|---|---|---|---|
| `starter_jerkin` | Kaftan Wędrowca | `T_ItemIcons_StarterJerkin_BC.png` | oryginał, `make_icons.py` → `starter_jerkin()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `715b42d567a2bd6e…` |
| `starter_boots` | Buty Traktowe | `T_ItemIcons_StarterBoots_BC.png` | oryginał, `make_icons.py` → `starter_boots()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `fda34320d6e08904…` |
| `starter_buckler` | Lipowy Puklerz | `T_ItemIcons_StarterBuckler_BC.png` | oryginał, `make_icons.py` → `starter_buckler()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `03bab4a858d7d90f…` |
| `amber_earring` | Bursztynowy Kolczyk | `T_ItemIcons_AmberEarring_BC.png` | oryginał, `make_icons.py` → `amber_earring()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `4b6437ea6fb94024…` |
| `starter_gloves` | Rękawice Rekruta | `T_ItemIcons_StarterGloves_BC.png` | oryginał, `make_icons.py` → `starter_gloves()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `202f4004dc42bc97…` |
| `starter_sword` | Miecz Rekruta | `T_ItemIcons_StarterSword_BC.png` | oryginał, `make_icons.py` → `starter_sword()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `d2c202e3514518d1…` |
| `potion_hp_s` | Mała Mikstura Życia | `T_ItemIcons_PotionHpS_BC.png` | oryginał, `make_icons.py` → `potion_hp_s()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `cd3e9937672bf3c6…` |
| `antidote` | Antidotum | `T_ItemIcons_Antidote_BC.png` | oryginał, `make_icons.py` → `antidote()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `e7d9417131e04387…` |
| `ore_shard` | Odłamek Rudy | `T_ItemIcons_OreShard_BC.png` | oryginał, `make_icons.py` → `ore_shard()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `814091bd2cde5564…` |

Reprodukcja: `python art-source/ItemIcons/make_icons.py <out_dir>` daje bajtowo identyczne PNG (sprawdzone dwoma przebiegami; numpy 2.5.4, scipy 1.18.1, Pillow 13.0.0).
Puklerz (`starter_buckler`): ikona narysowana niezależnie od mesha od modeli 3D (#59), więc nie ma między nimi zależności.

## Partia 2 (gałąź `feat/032b-item-icons-batch2`)

Pozostałe identyfikatory przedmiotów z `docs/design/tables/` (`items_starter.csv`, `shop_starter_merchant.csv`,
`skill_tier_items.csv` = ITEMS_COMBAT_SPEC §4.1). `treatise_proficiency_<classId>` rozwinięty na 4 klasy z `classes_skills.json`
(`warrior`, `ninja`, `shaman`, `slajter`). Ta sama metoda, ten sam skrypt, te same zasady: praca własna, MetinAR-Owned, bez
zewnętrznych obrazów, fontów, pędzli, paczek ikon, treści AI ani plików z innych gier (w tym Metin2). Atrybucja nie dotyczy.
Emblematy na okładkach traktatów (miecz, gwiazda, błyskawica, półksiężyc) i wszystkie kształty to własne proste figury geometryczne.

| Item id | Nazwa (gra) | Plik | Tabela | Źródło | Autor | Licencja | Atrybucja | SHA-256 (PNG) |
|---|---|---|---|---|---|---|---|---|
| `spider_silk` | Jedwab Pajęczy | `T_ItemIcons_SpiderSilk_BC.png` | items_starter.csv | oryginał, `make_icons.py` → `spider_silk()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `89c6a6a6489ffe19…` |
| `potion_hp_m` | Mikstura Życia | `T_ItemIcons_PotionHpM_BC.png` | shop_starter_merchant.csv | oryginał, `make_icons.py` → `potion_hp_m()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `8f384d9d4bb3a852…` |
| `potion_mp_s` | Mała Mikstura Many | `T_ItemIcons_PotionMpS_BC.png` | shop_starter_merchant.csv | oryginał, `make_icons.py` → `potion_mp_s()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `370fa351aa2441d0…` |
| `smith_oil` | Olej Kowalski | `T_ItemIcons_SmithOil_BC.png` | shop_starter_merchant.csv | oryginał, `make_icons.py` → `smith_oil()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `a84d9df8549b92fe…` |
| `forget_draught` | Napar Zapomnienia | `T_ItemIcons_ForgetDraught_BC.png` | shop_starter_merchant.csv | oryginał, `make_icons.py` → `forget_draught()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `0e1dee65e9966132…` |
| `bag_expansion` | Sakwa +8 miejsc | `T_ItemIcons_BagExpansion_BC.png` | shop_starter_merchant.csv | oryginał, `make_icons.py` → `bag_expansion()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `05e7fabd4571d88d…` |
| `treatise_proficiency_warrior` | Traktat Biegłości (Wojownik) | `T_ItemIcons_TreatiseProficiencyWarrior_BC.png` | skill_tier_items.csv / shop (§4.1), `<classId>` = `warrior` | oryginał, `make_icons.py` → `_treatise("warrior")` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `562b20ea10f4996e…` |
| `treatise_proficiency_ninja` | Traktat Biegłości (Ninja) | `T_ItemIcons_TreatiseProficiencyNinja_BC.png` | skill_tier_items.csv / shop (§4.1), `<classId>` = `ninja` | oryginał, `make_icons.py` → `_treatise("ninja")` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `e477b3e9fa403a30…` |
| `treatise_proficiency_shaman` | Traktat Biegłości (Szaman) | `T_ItemIcons_TreatiseProficiencyShaman_BC.png` | skill_tier_items.csv / shop (§4.1), `<classId>` = `shaman` | oryginał, `make_icons.py` → `_treatise("shaman")` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `6f8f1e0cedc5153d…` |
| `treatise_proficiency_slajter` | Traktat Biegłości (Slajter) | `T_ItemIcons_TreatiseProficiencySlajter_BC.png` | skill_tier_items.csv / shop (§4.1), `<classId>` = `slajter` | oryginał, `make_icons.py` → `_treatise("slajter")` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `48f74077fecd82c2…` |
| `spark_artistry` | Iskra Kunsztu | `T_ItemIcons_SparkArtistry_BC.png` | skill_tier_items.csv (§4.1) | oryginał, `make_icons.py` → `spark_artistry()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `71085754e6197201…` |
| `seal_apex` | Pieczęć Szczytu | `T_ItemIcons_SealApex_BC.png` | skill_tier_items.csv (§4.1) | oryginał, `make_icons.py` → `seal_apex()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `56d311fa20d68882…` |

Podgląd partii 2: [contact_sheet_batch2.png](contact_sheet_batch2.png). Partia 1 przy regeneracji pozostaje bajtowo identyczna
(sprawdzone 2026-10-21; numpy 2.5.4, scipy 1.18.1, Pillow 12.3.0).
