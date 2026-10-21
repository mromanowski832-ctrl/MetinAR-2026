# METINAR-019 — S1 Gameplay core: dane i formuły (#46)

Czysty C# bez zależności od Unity: assembly `MetinAR.Gameplay` (`noEngineReferences: true`) w
`client/Assets/MetinAR/Scripts/Gameplay/`. Formuły i liczby pochodzą ze specu [docs/design](design/README.md)
(PR #44, zaakceptowany przez właściciela 2026-10-21) z decyzjami właściciela z PR #61: **maks. poziom 99**
i **stopnie umiejętności** Wprawa → Biegłość → Kunszt → Szczyt (zastępują szkic „rangi 1–15”). Kod nie wprowadza
nowych zasad gry.

## Zawartość

| Plik | Co robi | Źródło w specu |
|---|---|---|
| `BalanceConfig.cs`, `BalanceRules.cs`, `ClassBalance.cs` | wszystkie liczby: klasy (wojownik, ninja, szaman, Slajter), statystyki pochodne, obrażenia, EXP, moby, rzadkość, hartowanie; `CreateDefault()` + `Validate()` | CHARACTER_PROGRESSION §1–3, COMBAT_PHYSICS §1, MOBS_NPC §2, ITEMS_COMBAT_SPEC §3–4 |
| `ExpCurve.cs` | krzywa EXP 1–99, suma EXP, poziom z sumy, kule HUD 4 × 25% (ułamkowo), mnożnik różnicy poziomów | CHARACTER_PROGRESSION §2, §2.1, §2.2 |
| `DerivedStats.cs`, `CharacterLoadout.cs`, `PrimaryStats.cs` | statystyki z poziomu, auto +1 i 2 wolnych punktów; MaxHp/MaxMp/Atk/Mag/Def/Acc/Eva/Crit/Blok/regeneracja/odporności z limitami | CHARACTER_PROGRESSION §1.2–1.4 |
| `DamageResolver.cs`, `GameplayRandom.cs` | rozstrzygnięcie trafienia w kolejności 1–9 ze specu; deterministyczny generator z ziarnem (SplitMix64) | COMBAT_PHYSICS §1, §3 |
| `MobLevelStats.cs` | HP/Atk/Def/EXP/talary zwykłego moba poz. 1–99 | MOBS_NPC §2 |
| `SkillTierRules.cs` | stopnie umiejętności `rankIndex` 1–29: koszt w punktach (1–15), min. poziom postaci, szansa/odstęp/koszt EXP prób (tylko dane), mnożniki coef/MP/odnowienie/czas; punkty umiejętności z poziomu | CLASSES_SKILLS §2, §2.1 |
| `Data/*` | import tabel CSV/JSON z `docs/design/tables` (tekst na wejściu): `exp_curve.csv`, `mob_level_stats.csv`, `rarity_tiers.csv`, `temper_levels.csv`, `skill_tiers.csv`, `classes_skills.json` | tables/README |

Zaokrąglenia jak w generatorze `generate_tables.py`: `round` = do najbliższej, remisy do parzystej
(tak samo działa `System.Math.Round`), `round5`/`round10` = wielokrotność kroku.

## Interpretacje (bez nowych zasad, do potwierdzenia w review)

- Kolejność losowań w `DamageResolver`: trafienie → blok → rozrzut → krytyk; każdy rzut tylko, gdy potrzebny.
- Riposta daje 0 obrażeń (−100%); „minimum 1” dotyczy trafień i bloków, pudło = 0.
- Zablokowany cios: ×0,4 stosowane razem z modyfikatorami (kolejność mnożenia nie zmienia wyniku).
- Limit 40% dotyczy statystyki `CritChance`; bonus słabego punktu dodawany jest do rzutu ponad nią (do 100%).
- `Acc` i `Eva` dodają premie z przedmiotów (rękawice „celność +2”, buty „unik +2” z zestawu startowego).
- Mnożniki stopni w kodzie liczone są z formuł §2 bez zaokrąglania; `skill_tiers.csv` pokazuje je zaokrąglone
  (coef/MP do 0,01). `ApplyTables` może podmienić je na wartości z tabeli.

## Poza zakresem S1 (pytania dla kolejnych etapów, nie blokują S1)

- **Unik (`Eva`) i celność moba** nie są zdefiniowane w specu (tabela mobów ma tylko HP/Atk/Def). `DamageResolver`
  przyjmuje `Eva` celu jako wejście; S2 musi ustalić wartość dla mobów (pytanie do kierownika produkcji/właściciela).
- **Obrażenia hybrydowe Slajtera** (`scaling: hybrid`, `coef` + `magCoef`): spec nie mówi, czy część Mag liczy się
  z połową `Def` (magiczne) czy z pełną. Do rozstrzygnięcia przy S8; S1 nie implementuje umiejętności.
- Zaokrąglenie nagród z mnożnikami rangi moba i różnicy poziomów (EXP/talary) — S2.
- Próby podniesienia stopnia (losowanie, odstęp, koszt EXP) — po stronie serwera, S7/S11; S1 trzyma tylko dane.
- `sanity_warrior_ttk.csv` zakłada Kunszt III na poz. 70 i Kunszt V na poz. 80, a `skill_tiers.csv` wymaga
  poz. 72 i 84 — rozbieżność tabel zgłoszona w #41 (S1 sprawdza tylko formuły obrażeń z tych wierszy).

## Weryfikacja

- `dotnet test tests/MetinAR.Gameplay.Tests/MetinAR.Gameplay.Tests.csproj` (CI: `.github/workflows/gameplay-core-tests.yml`).
  Testy kompilują produkcyjne pliki (C# 9, jak Unity) i porównują wyniki z tabelami `docs/design/tables/*`
  (poziomy 1–99 po merge'u PR #61).
- Kompilacja Unity 6000.6.4f1 w batchmode na LuzMen (wynik w PR).
