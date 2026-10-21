# METINAR-017 — Postać: statystyki, poziomy, EXP

> **Status: ZAAKCEPTOWANE przez właściciela 2026-10-21 (#40; maks. poziom 99, pozostałe propozycje domyślne). Bez implementacji.**
> Każda liczba i formuła w tym pliku to **proponowana wartość startowa do balansowania**, a nie finalny balans.
> Wszystkie nazwy, formuły i tabele są własne projektu MetinAR. Indeks: [README.md](README.md).

## 1. Statystyki

### 1.1 Statystyki główne (rozdawane punktami)

| Id (kod) | Nazwa PL | Za co odpowiada |
|---|---|---|
| `Might` | Moc | atak fizyczny broni wręcz, część obrażeń hybrydowych Slajtera |
| `Finesse` | Zwinność | celność, szansa krytyczna, unik, atak łukiem i sztyletami (zamiast Mocy) |
| `Spirit` | Duch | moc magii, maks. MP, siła leczenia |
| `Vigor` | Krzepa | maks. HP, obrona, odporności |

**Celowo nie ma statystyki szybkości ruchu.** Na mapie GPS gracz chodzi naprawdę, a w AR walczy w miejscu.
Szybkość ataku jest zmieniana tylko przez umiejętności i przedmioty, w wąskich granicach (patrz §1.4).

### 1.2 Wartości startowe i przyrost na poziom

Każdy awans daje **automatyczny przyrost klasowy** (+1 w dwóch statystykach klasy) oraz **2 wolne punkty** do rozdania.

| Klasa (`ClassId`) | Moc | Zwinność | Duch | Krzepa | Auto +1 / poziom | HP / poziom | MP / poziom |
|---|---|---|---|---|---|---|---|
| Wojownik (`warrior`) | 6 | 3 | 2 | 5 | Moc, Krzepa | 14 | 4 |
| Ninja (`ninja`) | 3 | 6 | 2 | 4 | Zwinność, Krzepa | 9 | 5 |
| Szaman (`shaman`) | 2 | 3 | 6 | 4 | Duch, Krzepa | 8 | 9 |
| Slajter (`slajter`) | 5 | 2 | 5 | 4 | Moc, Duch | 11 | 7 |

Reset punktów: przedmiot „Napar Zapomnienia” u Wędrownego Handlarza ([ECONOMY.md](ECONOMY.md)).

### 1.3 Statystyki pochodne (formuły)

`L` = poziom postaci, `iL` = poziom przedmiotu.

| Id | Nazwa | Formuła (propozycja) |
|---|---|---|
| `MaxHp` | Maks. HP | `60 + L × hpPerLevel(klasa) + Vigor × 6 + przedmioty` |
| `MaxMp` | Maks. MP | `20 + L × mpPerLevel(klasa) + Spirit × 5 + przedmioty` |
| `Atk` | Atak | `WeaponAtk × (1 + temper%) + ScalingStat × 0.8 + L × 0.5`; `ScalingStat` = Moc (miecze, topory), Zwinność (sztylety, łuk) |
| `Mag` | Moc magii | `WeaponMag × (1 + temper%) + Spirit × 1.0 + L × 0.5` |
| `Def` | Obrona | `Σ obrona przedmiotów + Vigor × 0.8` |
| `Acc` | Celność | `70 + Finesse × 1.0 + L` |
| `Eva` | Unik | `5 + Finesse × 0.5 + przedmioty` |
| `CritChance` | Szansa kryt. | `3% + Finesse × 0.15% + przedmioty`, **limit 40%** |
| `CritMult` | Mnożnik kryt. | `1.6 + przedmioty`, **limit 2.2** |
| `BlockChance` | Blok | tylko z tarczą: `tarcza + affiksy`, **limit 35%** |
| `HpRegen` | Regeneracja | poza walką 2% MaxHp / 5 s; w walce 0 + przedmioty |
| `MpRegen` | Regeneracja MP | `1 + Spirit × 0.05` MP/s w walce, ×3 poza walką |
| `Res<Typ>` | Odporności | trucizna, mrok, żywioły: z przedmiotów, **limit 50%** |

Broń: `WeaponAtk(iL) = 4 + 1.6 × iL` (zwykła), mnożona przez rzadkość ([ITEMS_COMBAT_SPEC.md](ITEMS_COMBAT_SPEC.md) §3).

### 1.4 Szybkość ataku

Bazowy zamach to 0,72 s z prototypu FPP (#31). Modyfikator szybkości ataku jest ograniczony do **0,80–1,25×**,
żeby okno trafienia (0,18–0,42 s) i animacje zostały czytelne i żeby szybkie ataki nie męczyły ręki w AR.

## 2. Poziomy i krzywa EXP

- **Maks. poziom: 99** (decyzja właściciela 2026-10-21, wcześniej proponowane 60; [OPEN_QUESTIONS_PLAN.md](OPEN_QUESTIONS_PLAN.md), Q1).
- Formuła: `ExpToNext(L) = round10( MobExp(L) × K(L) )`, gdzie `MobExp(m) = round(20 + 10 × m^1.15)`,
  `K(L) = 4 + 1.2 × L` to liczba „ekwiwalentów zabicia moba na tym samym poziomie”, licząc wszystkie źródła EXP.
- Pełna tabela: [tables/exp_curve.csv](tables/exp_curve.csv) (generator: [tables/generate_tables.py](tables/generate_tables.py)).

| Poziom | EXP do następnego | Suma EXP na starcie poziomu | Ekwiwalent zabić |
|---|---|---|---|
| 1 | 160 | 0 | 5,2 |
| 5 | 840 | 1 460 | 10 |
| 10 | 2 580 | 8 720 | 16 |
| 20 | 9 320 | 61 230 | 28 |
| 30 | 20 800 | 202 180 | 40 |
| 40 | 37 230 | 479 840 | 52 |
| 60 | 85 800 | 1 649 930 | 76 |
| 80 | 156 400 | 3 998 640 | 100 |
| 98 | 239 430 | 7 491 640 | 121,6 |
| 99 | — | 7 731 070 | — |

Razem ok. **6 210 ekwiwalentów zabicia** i **7 731 070 EXP** do poziomu 99. Przy założeniu ~25 ekwiwalentów na godzinę
aktywnej gry (walki + zadania + spacer) daje to **~250 h gry** (ok. 8 miesięcy przy ~1 h dziennie).
Formuła jest ta sama co przy limicie 60, więc tempo poziomów 1–60 się nie zmienia:

| Odcinek | Ekwiwalenty zabić | Czas gry (≈) |
|---|---|---|
| 1 → 10 | 90 | 3–4 h |
| 1 → 30 | 640 | ~25 h |
| 1 → 60 | 2 360 | ~95 h |
| 60 → 80 | 1 750 | ~70 h |
| 80 → 99 | 2 100 | ~85 h |
| **1 → 99** | **6 210** | **~250 h** |

Poziomy 60–99 to długa końcówka gry (~155 h): rangi mistrzowskie umiejętności ([CLASSES_SKILLS.md](CLASSES_SKILLS.md) §2),
przedmioty iL 60–99 i hartowanie +6…+10. Tempo Q2 (poz. 10 w ~3–4 h) zaakceptowane bez zmian.

### 2.1 Wskaźnik EXP w HUD (4 × 25%)

Istniejący HUD ma 4 kule EXP. Każda kula = 25% bieżącego poziomu (`expPerHudOrb` w tabeli). Kula częściowo
wypełniona pokazuje postęp proporcjonalnie (dziś `_filledExpSegments` 0–4; potrzebne wypełnienie ułamkowe).

### 2.2 Modyfikator różnicy poziomów (EXP z mobów)

| Mob względem gracza | Mnożnik EXP |
|---|---|
| ≥ 5 poziomów wyżej | ×1,25 (limit) |
| 1–4 wyżej | +5% za poziom |
| równy do 5 niżej | ×1,0 |
| > 5 niżej | −15% za każdy poziom ponad 5, minimum ×0,10 |

### 2.3 Śmierć

Propozycja: **bez utraty poziomu**. Utrata 2% EXP bieżącego poziomu (nie schodzi poniżej 0 na poziomie),
mob wraca do pełnego HP, gracz wraca na mapę GPS z 30% HP. Opcje: Q4.

## 3. Źródła EXP

| Źródło | Udział docelowy | Zasada (propozycja) |
|---|---|---|
| Moby | ~50% | `MobExp(m) × mnożnik rangi` (zwykły ×1, elitarny ×3, strażnik ×10) × modyfikator §2.2 |
| Zadania | ~30% | zadanie dzienne = 2 ekwiwalenty zabicia, fabularne 4–8, dawane przez NPC |
| Eksploracja GPS | ~20% | dystans i odkryte komórki mapy, z limitami i walidacją (§4) |

### 3.1 Eksploracja GPS

- **Dystans:** za każde zweryfikowane 250 m marszu `0,6 × MobExp(L)`. Limit EXP z dystansu: **8 km na dobę**
  (po limicie dystans dalej liczy się do zadań, ale nie daje EXP). Ograniczenie chroni zdrowie i utrudnia oszustwa.
- **Odkrycie komórki:** pierwsza wizyta w komórce mapy (~150–200 m, siatka typu S2/geohash, wybór techniczny
  przy implementacji) = 1 ekwiwalent zabicia, maks. 30 nowych komórek na dobę.
- Tylko podczas używania aplikacji (lokalizacja tylko na pierwszym planie, bez `ACCESS_BACKGROUND_LOCATION`).

## 4. GPS: ochrona przed oszustwami (wymaga serwera, nie przesądzamy Q1)

Klient może liczyć podgląd, ale EXP/talary za dystans przyznaje docelowo serwer (#22, #39).

1. **Prędkość:** odcinek liczy się przy 0,4–3,0 m/s (chód/trucht) po wygładzeniu; 3–7 m/s bez nagrody;
   > 7 m/s (pojazd) nagroda wstrzymana na 2 min.
2. **Teleport:** skok dający > 50 m/s między fixami = odcinek odrzucony, licznik podejrzeń +1.
3. **Dokładność:** tylko fixy z dokładnością ≤ 20 m; dystans liczony jak w `AggroConfidenceGate`
   (pewność > pojedynczy fix, odrzucanie szumu w miejscu — „dryf” stojącego telefonu nie daje dystansu).
4. **Mock location:** fix oznaczony przez system jako pozorowany = brak nagród za ruch i spotkania.
5. **Rytm fixów:** zbyt regularne odstępy/idealnie proste linie = flaga do przeglądu (bez automatycznego bana).
6. **Limity dobowe** jak w §3.1 oraz limit spotkań AR na godzinę na serwerze.
7. **Prywatność:** na serwer idą podsumowania odcinków (dystans, czas, ocena jakości), a nie surowa trasa;
   żadnych dokładnych współrzędnych w logach, issue ani PR.
8. Opcjonalnie krokomierz jako drugi sygnał — wymaga nowego uprawnienia (`ACTIVITY_RECOGNITION`), więc to
   decyzja właściciela (Q10).
