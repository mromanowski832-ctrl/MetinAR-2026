# METINAR-032 — rejestr licencji ikon przedmiotów (zestaw startowy)

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
| `starter_sword` | Miecz Rekruta | `T_ItemIcons_StarterSword_BC.png` | oryginał, `make_icons.py` → `starter_sword()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `c998217c66388d41…` |
| `potion_hp_s` | Mała Mikstura Życia | `T_ItemIcons_PotionHpS_BC.png` | oryginał, `make_icons.py` → `potion_hp_s()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `cd3e9937672bf3c6…` |
| `antidote` | Antidotum | `T_ItemIcons_Antidote_BC.png` | oryginał, `make_icons.py` → `antidote()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `e7d9417131e04387…` |
| `ore_shard` | Odłamek Rudy | `T_ItemIcons_OreShard_BC.png` | oryginał, `make_icons.py` → `ore_shard()` | MetinAR project (agent „grafik” dla właściciela) | MetinAR-Owned | nie dotyczy | `814091bd2cde5564…` |

Reprodukcja: `python art-source/ItemIcons/make_icons.py <out_dir>` daje bajtowo identyczne PNG (sprawdzone dwoma przebiegami; numpy 2.5.4, scipy 1.18.1, Pillow 13.0.0).
Puklerz (`starter_buckler`): ikona narysowana niezależnie od mesha od modeli 3D (#59), więc nie ma między nimi zależności.
