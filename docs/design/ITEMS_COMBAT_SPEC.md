# METINAR-017 — Specyfikacja systemów gry: przedmioty, ekwipunek, walka

> **Status: PROPOZYCJA do akceptacji właściciela. Issue #40. Bez implementacji.**
> Każda liczba w tym dokumencie i w plikach powiązanych to **proponowana wartość startowa do balansowania**.
> Nic tu nie jest finalnym balansem, dopóki właściciel tego nie zaakceptuje (AUTONOMOUS_WORKFLOW §9).

## 0. Zasady i spis plików

**Oryginalność (decyzja właściciela, wiążąca):** gra ma się kojarzyć z gatunkiem (role klas, 2 ścieżki na klasę,
umiejętności o podobnej funkcji, sloty ekwipunku, rzadkość, ulepszanie, drop, EXP i waluta), ale **wszystko jest
własne**: nazwy, opisy, liczby, tabele, formuły, modele, ikony i dźwięki. Nie kopiujemy ani nie przerabiamy
„literka w literkę” niczego z Metin2 i nie używamy żadnych jego assetów (#35 pozostaje `[BLOCKED]`).
Celowe różnice od typowych rozwiązań gatunku: maks. poziom 60, 4 statystyki z własnymi formułami, obrona z malejącymi
zyskami, hartowanie +1…+10 **bez niszczenia przedmiotu**, brak wytrzymałości, osobiste moby i łup, EXP za spacer
z limitem dobowym, sterowanie umiejętnościami gestami AR.

| Plik | Zakres (punkty zlecenia) |
|---|---|
| [CHARACTER_PROGRESSION.md](CHARACTER_PROGRESSION.md) | 1) statystyki i formuły, 2) poziomy, krzywa EXP, źródła EXP, ochrona GPS |
| [ECONOMY.md](ECONOMY.md) | 3) talary: źródła, ujścia, ceny NPC, równowaga |
| [CLASSES_SKILLS.md](CLASSES_SKILLS.md) | 4) 4 klasy × 2 ścieżki, 56 umiejętności, punkty i rangi |
| **ten plik** | 5) przedmioty i ekwipunek, drop; zestaw startowy; budżety assetów |
| [MOBS_NPC.md](MOBS_NPC.md) | 6) moby i NPC, rangi, AI, odrodzenia w strefach GPS |
| [COMBAT_PHYSICS.md](COMBAT_PHYSICS.md) | 7) obrażenia, trafienie/pudło/krytyk/blok, hitboxy, reakcje, wejście, budżet A31, dźwięki |
| [MODES_DATA_MODEL.md](MODES_DATA_MODEL.md) | 8) AR/FPP vs mapa GPS, 9) model danych i rola serwera |
| [OPEN_QUESTIONS_PLAN.md](OPEN_QUESTIONS_PLAN.md) | 10) pytania do właściciela i plan etapów |
| [tables/](tables/README.md) | tabele CSV/JSON (szkice danych) + generator |

## 1. Sloty ekwipunku

10 slotów ekwipunku + 2 szybkie sloty na mikstury ([tables/item_slots.csv](tables/item_slots.csv)):

| Slot (`EquipSlot`) | Nazwa | Widać w FPP/AR? | Dlaczego |
|---|---|---|---|
| `Weapon` | Broń | **tak** (model 3D w dłoni) | główny element walki AR |
| `OffHand` | Druga ręka: tarcza / kołczan / totem / fokus | **tak** | tarcza do BLOKU, lewa ręka w geście AURA |
| `Helmet` | Hełm | nie (ikona, przyszły awatar mapy) | w FPP nie widać głowy |
| `Armor` | Zbroja | nie | jw. |
| `Gloves` | Rękawice | **tak** (wariant tekstury rąk FPP) | w FPP widać dłonie — rękawice to najtańszy widoczny ekwipunek |
| `Boots` | Buty | nie | ikona |
| `Earrings` | Kolczyki | nie | biżuteria, tylko ikona (tanie w produkcji) |
| `Necklace` | Naszyjnik | nie | jw. |
| `Bracelet` | Bransoleta | opcjonalnie (prosty mesh na nadgarstku) | widoczna przy zamachu |
| `Ring` | Pierścień | nie | slot dodatkowy — decyzja Q19 |
| `Quick1`, `Quick2` | Szybkie sloty | przyciski w AR | mikstury w walce |

Ograniczenia: broń dwuręczna i łuk blokują tarczę (łuk używa kołczanu). Tarcza tylko dla wojownika. Typy zbroi:
ciężka (wojownik), lekka (ninja, Slajter), szata (szaman) — wpływ tylko na wartości, bez kar.

## 2. Typy przedmiotów

| Kategoria (`ItemCategory`) | Przykłady | Stos |
|---|---|---|
| `Equipment` | broń, zbroja, biżuteria | 1 |
| `Consumable` | mikstury, antidotum, napary | 50 |
| `Material` | Odłamek Rudy, Jedwab Pajęczy, Olej Kowalski | 99 (olej 20) |
| `Quest` | przedmioty zadań | 20, nie do sprzedania |
| `Service` | sakwa, reset | — (natychmiastowe) |

Każdy przedmiot ma `itemLevel` (iL), wymagany poziom = `max(1, iL − 2)` i wymaganą klasę dla broni/drugiej ręki.

## 3. Rzadkość i losowane statystyki

5 poziomów rzadkości ([tables/rarity_tiers.csv](tables/rarity_tiers.csv)):

| Rzadkość | Kolor | Affiksy | Mnożnik statystyk bazowych | Waga dropu (mob zwykły) |
|---|---|---|---|---|
| Zwykły (`Common`) | szary | 0 | ×1,00 | 70 |
| Niezwykły (`Uncommon`) | zielony | 1 | ×1,06 | 22 |
| Rzadki (`Rare`) | niebieski | 2 | ×1,12 | 6,5 |
| Epicki (`Epic`) | fioletowy | 3 | ×1,20 | 1,3 |
| Legendarny (`Legendary`) | pomarańczowy | 3 + efekt unikalny | ×1,30 | 0,2 |

- **Statystyki bazowe** wynikają z typu i iL (np. `WeaponAtk = 4 + 1,6 × iL`, obrona zbroi `2 + 0,9 × iL`).
- **Affiksy** losowane z puli dozwolonej dla slotu ([tables/affixes.csv](tables/affixes.csv)), wartość:
  `lerp(valueAtIL1, valueAtIL60, (iL−1)/59) × U(0,6; 1,0)`. Ten sam affiks nie powtarza się na przedmiocie.
- Legendarne efekty (np. „co 5. trafienie wywołuje falę”) dopiero po włączeniu serwera — łatwo je nadużyć.

## 4. Ulepszanie: hartowanie i przekucie

**Hartowanie** (`Temper`) u Kowala, poziomy **+1…+10** ([tables/temper_levels.csv](tables/temper_levels.csv)):

| Poziom | +% statystyk bazowych | Szansa | Porażka |
|---|---|---|---|
| +1…+2 | 6–12 | 100% | — |
| +3…+5 | 18–30 | 95–80% | tracisz tylko materiały |
| +6…+10 | 36–60 | 70–30% | spadek o 1 poziom (Olej Kowalski chroni) |

**Przedmiot nigdy nie jest niszczony.** Koszt: Odłamki Rudy + talary `round5(5 × iL × t^1.6)`.
**Przekucie** (`Reforge`): losuje jeden affiks od nowa za talary i 3 Odłamki. Gniazda/kamienie: poza v1 (Q18).
**Wytrzymałości brak** (propozycja, Q5): brak naprawy, brak irytacji przy spacerach.

## 5. Ekwipunek (inwentarz)

- Start: **24 miejsca** (siatka 4 × 6 na pionowym ekranie). Dziś prototyp `InventoryPanel` ma 16 (Q17).
- Rozszerzenie: Sakwa +8 (talary), maks. **56**.
- Stosy: wg §2. Automatyczne łączenie stosów przy podniesieniu. Sortowanie: kategoria → rzadkość → iL.
- Pełny ekwipunek: łup trafia do **skrytki łupu** (8 miejsc, 24 h), żeby nic nie przepadło w AR.
- Przedmioty mogą być oznaczane jako „zablokowane” (ochrona przed sprzedażą).

## 6. Drop: ramy tabel łupu

- Łup **osobisty** (każdy gracz losuje swój), wypada po śmierci moba w AR, zbierany automatycznie po walce.
- `DropTable` = talary (zakres) + lista **niezależnych rzutów**; każdy rzut ma szansę i pulę z wagami.
- Przedmiot z puli dostaje iL = poziom moba ±1 i rzadkość z tabeli wag rangi moba.
- Modyfikatory: `gold_find`, przyszłe wydarzenia; limit dzienny legendarnych na gracza (serwer).
- Przykład: [tables/drop_table_venomous_spider.json](tables/drop_table_venomous_spider.json).
- Docelowo **losowanie po stronie serwera** (#22/#39). W prototypie lokalnie z ziarnem losowania do testów.

## 7. Zestaw startowy (pierwszy pionowy wycinek)

Cel: przetestować cały łańcuch **mapa → aggro → AR → walka → śmierć moba → łup → ekwipunek → założenie → sklep**.

| Pozycja | Id | Nazwa | Dane | Asset |
|---|---|---|---|---|
| Broń | `starter_sword` | Miecz Rekruta | miecz 1H, iL 1, Atk 6 | istniejący miecz FPP (#36) |
| Zbroja | `starter_jerkin` | Kaftan Wędrowca | Def 4, +10 HP | ikona |
| Buty | `starter_boots` | Buty Traktowe | Def 2, unik +2 | ikona |
| Tarcza | `starter_buckler` | Lipowy Puklerz | Def 3, blok 8% | ikona + prosty mesh na lewą rękę (≤ 1,5k tris) |
| Biżuteria | `amber_earring` | Bursztynowy Kolczyk | odporność na truciznę +5% | ikona |
| Rękawice | `starter_gloves` | Rękawice Rekruta | Def 1, celność +2 | obecne rękawice FPP (#36) |
| Mob | `mob_venomous_spider` | Trujący Pająk | poz. 5, HP 120, aggro 12 m | #30 |
| NPC | `npc_wandering_merchant` | Wędrowny Handlarz | sklep | #38 |
| Zużywalne | `potion_hp_s`, `antidote` | Mała Mikstura Życia, Antidotum | | ikony |
| Materiał | `ore_shard` | Odłamek Rudy | do hartowania | ikona |

Wycinek obejmuje tylko wojownika (pasuje do istniejących rąk i miecza FPP). Inne klasy w kolejnych etapach.

## 8. Budżety techniczne assetów (per kategoria)

Zgodne z METINAR-009 §1 i decyzją kontrolera w PR #36 (kategoria Player) oraz #38 (NPC v1). Bez podnoszenia limitów
bez pomiaru na A31.

| Kategoria | Trójkąty LOD0 | Kości | Materiały | Tekstury | LOD | Źródło |
|---|---|---|---|---|---|---|
| Mob zwykły | ≤ 8 000 (cel 4–6k) | ≤ 40 | ≤ 2 (cel 1) | 1024² | 3 | METINAR-009 |
| Strażnik (boss) | ≤ 20 000 | ≤ 75 | ≤ 3 | 2048² | 3 | METINAR-009 |
| NPC (v1) | ≤ 10 000 | ≤ 40 | ≤ 2 | 1024² | 1 | #38 / PR #36 |
| Ręce FPP (Player) | ≤ 10 000 łącznie | ≤ 40 | ≤ 2 | 1024² | 1 | PR #36 |
| Broń / rekwizyt ręki | ≤ 3 000 | 0 | 1 | 512²–1024² | opc. | METINAR-009 |
| Tarcza / kołczan / totem / fokus (FPP) | ≤ 1 500 (propozycja) | 0 | 1 | 512² | — | propozycja |
| Rękawice | bez nowego mesha: wariant tekstury rąk FPP | — | — | 1024² | — | propozycja |
| Zbroja, hełm, buty, biżuteria | tylko ikona 256² (atlas 2048²) | — | — | — | — | propozycja; awatar mapy później |
| Ikony przedmiotów i umiejętności | 256² PNG, atlasy 2048², ASTC | — | — | — | — | propozycja |
| SFX | mono, ≤ 1,5 s, ≤ 40 KB (Vorbis) | | | | | PR #43 |
| Muzyka | stereo, strumieniowana, ≤ 3 MB / utwór, maks. 2 w pamięci | | | | | propozycja |
| Pamięć audio łącznie | ≤ 8 MB | | | | | propozycja |

Licencje: tylko własne lub CC0/otwarte z dokumentacją (`ASSET_SOURCE.txt`, METINAR-009 §5). Płatne źródła: decyzja właściciela.
