# Tabele szkicowe METINAR-017 (#40)

**Status: ZAAKCEPTOWANE przez właściciela 2026-10-21 (maks. poziom 99). Wartości to wartości startowe do balansowania.**
Format danych (kolumny, nazwy pól) jest szkicem pod przyszły import do `ScriptableObject` i serwera, a nie kontraktem kodu.

| Plik | Zawartość |
|---|---|
| [generate_tables.py](generate_tables.py) | generator `exp_curve.csv`, `mob_level_stats.csv`, `sanity_warrior_ttk.csv` z formuł (Python 3, bez zależności) |
| [exp_curve.csv](exp_curve.csv) | EXP do następnego poziomu 1–99, suma, ekwiwalent zabić, EXP na kulę HUD |
| [mob_level_stats.csv](mob_level_stats.csv) | HP/Atk/Def/EXP/talary zwykłego moba wg poziomu 1–99 |
| [sanity_warrior_ttk.csv](sanity_warrior_ttk.csv) | kontrola skali: ile trafień wojownika na moba tego samego poziomu (1–99) |
| [classes_skills.json](classes_skills.json) | 4 klasy, 8 ścieżek, 56 umiejętności, reguły rang (1–15, mistrzowskie od poz. 60) |
| [item_slots.csv](item_slots.csv) | sloty ekwipunku i widoczność w FPP |
| [rarity_tiers.csv](rarity_tiers.csv) | rzadkości, affiksy, wagi dropu |
| [affixes.csv](affixes.csv) | pula losowanych statystyk (`valueAtIL1` → `valueAtIL99`, edytowana ręcznie, nie generowana) |
| [temper_levels.csv](temper_levels.csv) | hartowanie +1…+10 |
| [items_starter.csv](items_starter.csv) | definicje przedmiotów zestawu startowego |
| [shop_starter_merchant.csv](shop_starter_merchant.csv) | oferta Wędrownego Handlarza |
| [drop_table_venomous_spider.json](drop_table_venomous_spider.json) | tabela łupu Trującego Pająka |

Odtworzenie tabel generowanych: `python3 docs/design/tables/generate_tables.py` (wypisuje też podsumowanie tempa:
suma EXP, ekwiwalenty zabić i godziny do poziomów 10…99).
