# METINAR-017 — Waluta i ekonomia

> **Status: ZAAKCEPTOWANE przez właściciela 2026-10-21 (#40; maks. poziom 99, pozostałe propozycje domyślne). Bez implementacji.**
> Wszystkie ceny i przychody to **proponowane wartości startowe do balansowania**. Indeks: [README.md](README.md).

## 1. Waluta

- Jedna waluta w grze, roboczo **talary** (`Currency.Taler`, ikona: istniejąca obracająca się moneta w HUD).
  Nazwa do decyzji (Q7: talary / złoto / srebrniki / inna).
- **Bez waluty premium i bez płatności** w tej specyfikacji. Monetyzacja to osobna decyzja właściciela, poza zakresem.
- Licznik talarów: `long`, bez ułamków. Docelowo saldo trzyma serwer (#39).

## 2. Źródła talarów

| Źródło | Formuła (propozycja) | Przykład poz. 5 |
|---|---|---|
| Mob zwykły | `round(4 + 2 × m)` ±30% (tabela: [tables/mob_level_stats.csv](tables/mob_level_stats.csv)) | 10–18 |
| Mob elitarny / strażnik | ×3 / ×10 | 42 / 140 |
| Zadanie dzienne | `30 × L^0.9` | ~130 |
| Sprzedaż przedmiotów NPC | 20% ceny kupna × mnożnik rzadkości ([tables/rarity_tiers.csv](tables/rarity_tiers.csv)) | 5–60 |
| Skrzynka odkrycia (nowa komórka mapy, maks. 5/dobę) | `10 × L` | 50 |

Cel: **~450 talarów / h na poziomie 5**. Przychód rośnie razem z talarami z moba (`4 + 2 × m`): ok. ×9 na poziomie 60
i ×14 na poziomie 99 względem poziomu 5. To wolniej niż ceny sprzętu (`iL^1.35`: ok. ×56 dla iL 99 względem iL 5)
i hartowania, żeby ujścia działały także w końcówce gry (poziomy 60–99).

## 3. Ujścia talarów (sinks)

| Ujście | Formuła / cena | Uwagi |
|---|---|---|
| Mikstury i antidotum | stałe ceny NPC ([tables/shop_starter_merchant.csv](tables/shop_starter_merchant.csv)) | główne ujście wczesne |
| Hartowanie przedmiotów | `round5(5 × iL × t^1.6)` + Odłamki Rudy | główne ujście późne ([ITEMS_COMBAT_SPEC.md](ITEMS_COMBAT_SPEC.md) §4) |
| Przekucie affiksu | `round5(40 × iL)` + 3 Odłamki | losuje 1 affiks od nowa |
| Trening umiejętności | `round5(8 × unlockLevel × rank^1.4)` za punkt | Wprawa 1–14 i awans do Biegłości I (`rank` 1–15), [CLASSES_SKILLS.md](CLASSES_SKILLS.md) §2 |
| Traktat Biegłości (u trenera) | `round5(60 × L)`, min. 1 200 | od poz. 20, limit zakupu 2/dobę, skup 0; Iskra Kunsztu i Pieczęć Szczytu nie są sprzedawane |
| Próba podniesienia stopnia (ujście EXP, nie talarów) | 3% / 6% / 10% EXP do następnego poziomu | Traktat / Iskra / Pieczęć, [CLASSES_SKILLS.md](CLASSES_SKILLS.md) §2.1 |
| Napar Zapomnienia (reset statystyk) | `1500 × max(1, L/10)` | poz. 99: 14 850 |
| Sakwa (+8 miejsc ekwipunku) | 2 000, każda kolejna ×2 | maks. 56 miejsc |
| Zmiana ścieżki | `500 × L` + zadanie | Q11; poz. 99: 49 500 |
| Sprzęt zwykły u NPC | `round5(20 × iL^1.35 × slotFactor)` | slotFactor: broń 1,2; zbroja 1,0; reszta 0,6; formuła od iL 5, ceny zestawu startowego (iL 1) ustalone ręcznie w §5 |

**Bez naprawy sprzętu**, bo proponujemy brak wytrzymałości przedmiotów (Q5). Brak szybkiej podróży, bo mapa to
świat rzeczywisty.

## 4. Równowaga

- Docelowo ujścia zjadają **70–80%** przychodu, a ok. 20–30% zostaje jako oszczędności na hartowanie.
- Ceny NPC rosną z poziomem przedmiotu (`iL^1.35`) wolniej niż przychód z mobów razem z zadaniami, ale hartowanie
  rośnie szybciej (`t^1.6`), więc wysokie poziomy hartowania pozostają celem długoterminowym.
- **Brak handlu między graczami w v1** (Q8). Handel i rynek wymagają serwera, ochrony przed duplikacją i moderacji.
- Telemetria do balansu (później, za zgodą właściciela): przychód/h, wydatki wg kategorii, saldo wg poziomu.

## 5. Sklep Wędrownego Handlarza (zestaw startowy)

Wędrowny Handlarz (#38, stała lokalizacja GPS NPC) sprzedaje tylko przedmioty **zwykłe** i usługi.
Tabela: [tables/shop_starter_merchant.csv](tables/shop_starter_merchant.csv).

| Przedmiot | Cena kupna | Skup |
|---|---|---|
| Mała Mikstura Życia (+80 HP) | 25 | 5 |
| Mała Mikstura Many (+50 MP) | 25 | 5 |
| Antidotum | 40 | 8 |
| Miecz Rekruta | 120 | 24 |
| Lipowy Puklerz | 90 | 18 |
| Kaftan Wędrowca | 110 | 22 |
| Buty Traktowe | 70 | 14 |
| Rękawice Rekruta | 60 | 12 |
| Olej Kowalski | 400 | 80 |
| Sakwa +8 | 2 000 | — |

Interakcja ze sklepem tylko w zasięgu NPC na mapie (propozycja 30 m, z bramką dokładności GPS jak dla aggro).
