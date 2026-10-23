# Konflikty dokumentów ze specyfikacją właściciela 2026-10-23

Stan: 2026-10-23 (Europe/Warsaw). Podstawa: [OWNER_SPEC_2026-10-23_STRESZCZENIE.md](OWNER_SPEC_2026-10-23_STRESZCZENIE.md)
(streszczenie kierownika; przy rozbieżności rozstrzyga tekst dosłowny właściciela, gdy zostanie dodany).
Porównanie z `main` @ `e1bdf80`. Ten plik **tylko wskazuje** miejsca i potrzebną zmianę. Dokumentów źródłowych tu nie zmieniamy.
Zasada ze specyfikacji: „Nowsza, jednoznaczna decyzja właściciela wygrywa, a dokumenty się aktualizuje”.

Oznaczenia: **K** = sprzeczność (spec wygrywa, dokument do poprawy), **D** = dokument niepełny względem specu (do uzupełnienia),
**N** = rozbieżność nazwy do sprawdzenia w tekście dosłownym (bez zmiany teraz).

## 1. docs/PROJECT_BRIEF.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| L17–18 | „The phone tracks the player's real hand. A virtual sword is attached to the tracked hand.” | Tryb B: wirtualne dłonie FPP, „brak śledzenia prawdziwej dłoni” | K | zastąpić opisem wirtualnych dłoni FPP (1H: miecz w prawej, tarcza/gesty w lewej; 2H: obie dłonie) |
| L37 | pętla „…AR encounter becomes available” bez combo/nagród/zapisu | pętla etapu 1: śmierć, nagrody, ekwipunek, trwały zapis, dźwięki | D | dopisać pełną pętlę etapu 1 i odnośnik do testu 26 kroków |
| L41 | „Build from scratch.” | „Nie zaczynamy od zera.” / „Zachowaj istniejący poprawny kod.” | K | usunąć; wpisać zasadę zachowania istniejącego kodu |
| całość | brak trybów A/B, zapisu, audio, PvE, zakazu płatności | sekcje Wizja, Architektura | D | dodać skrót wizji i odnośnik do specyfikacji 2026-10-23 |

Powiązane issue: **#8** (METINAR-007 „Real hand tracking”, [BACKLOG]) i PR **#29** („Fix Galaxy A31 hand tracking”) dotyczą
odrzuconego kierunku. Potrzebna zmiana: oznaczyć jako zastąpione przez spec (decyzja kierownika; bez zamykania w tym PR).

## 2. docs/METINAR-FPP-001.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| L18 | „one sword, one attack, one Aura activation” | combo 1H i 2H po 3 ciosy, przytrzymanie = umiejętność ładowana | K | oznaczyć dokument jako prototyp etapu 1; combo opisane w #82 / METINAR-039 |
| L19, L39, L78 | proceduralny blockout „to review on device” | „Bryły prymitywne nigdy nie przechodzą odbioru wizualnego” | K | wpisać, że blockout nie przejdzie odbioru wizualnego; finalne dłonie i miecz: #34/#36 → ETAP 2 |
| L83 | „no … audio, haptics” | audio podpięte do zdarzeń, wibracje z wyłącznikiem | K | przenieść do „poza zakresem prototypu”, wskazać issue audio etapu 1 |
| L124–126 | lewa dłoń w spoczynku, bez tarczy | 1H: lewa dłoń z tarczą i gestami | D | dopisać pozę lewej dłoni z tarczą (socket #72) |
| L9 | Aura: dłoń do rękojeści, chwyt, energia, efekt | 7 kroków, bez przenikania, niebiesko-złoty efekt, powrót do postawy, bez obrażeń | D | rozpisać 7 kroków i kryteria odbioru |

## 3. docs/design/CHARACTER_PROGRESSION.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| L58–59 | jeden zamach 0,72 s, jedno okno trafienia 0,18–0,42 s | każdy cios combo ma własne okno trafienia; czasy i mnożniki w konfiguracji | K | zamienić na tabelę per cios (1H/2H × cios 1–3) w konfiguracji, wartości z #82 |
| — | brak rozróżnienia 1H/2H | 2H: wolniej, szerszy łuk, własny zasięg i obrażenia | D | dopisać parametry 2H |

## 4. docs/design/COMBAT_PHYSICS.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| §2 | jedno okno ataku podstawowego | trafienie tylko z rzeczywistej pozycji ostrza, maks. 1 trafienie na zamach na cel, okno per cios | D | dopisać okna per cios i regułę „1 trafienie / zamach / cel” |
| §4 L71 | tap buforowany tylko w ostatnich 0,15 s powrotu, inaczej ignorowany | „Tapnięcie w oknie kontynuuje combo, brak tapnięcia wraca do ciosu 1” | K | zastąpić oknem kontynuacji combo (czas z konfiguracji) |
| §4 L72 | przytrzymanie ≥ 0,6 s = umiejętność ładowana | „…jeśli jest odblokowana i przypisana, bez ciosów combo” | D | dopisać warunek odblokowania/przypisania i brak ciosów combo |
| §5 | lista SFX bez dźwięków per cios combo | „Każdy cios ma własną animację, dźwięk…” | D | dodać zdarzenia SFX cios 1/2/3 × 1H/2H, Aura (impuls), blok tarczą |
| §6 L105 | „min. 20 FPS, p95 klatki ≤ 66 ms” (dodatkowo niezgodne z `perf-budgets.json`: 24 / 55 ms) | „30 FPS na A31 podczas walki, łącznie z combo” | K | ustawić cel produktu 30 FPS w walce z combo; bramkę prototypu opisać osobno |

## 5. tools/qa/perf-budgets.json i docs/qa/DEVICE-ACCEPTANCE-LOOP-V0.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| `perf-budgets.json` L3, L37–40 | „PROTOTYPE GATE”, `ArEncounterPrototype`: `minFpsAvg` 24, `maxFrameP95Ms` 55 | 30 FPS w walce na A31 z combo | K | dodać bramkę produktu (np. `ArCombat`, `minFpsAvg` 30) mierzoną podczas combo; obecną zostawić jako regresyjną. Uwaga: A31 na kandydacie 1 miał 29,3–29,9 fps w AR bez combo (#31, komentarz 6320633550) |
| DEVICE-ACCEPTANCE L57 (L12) | fpsAvg ≥ 24, p95 ≤ 55 ms | 30 FPS | K | jak wyżej |
| DEVICE-ACCEPTANCE całość | pętla v0 L1–L15 + F | test 26 kroków; dowód: commit, ID buildu, PASS/FAIL, logi, wideo/zrzuty, problemy i kto naprawia | D | dodać protokół 26 kroków (nowy plik QA, issue ETAP 1); zasady z §1 (UNVERIFIED, slot, prywatność) zachować — są zgodne ze specem |
| DEVICE-ACCEPTANCE §1 | „UNVERIFIED rule” | „Nie oznaczaj testu jako PASS… wyłącznie na symulatorze lub w testach czystego C#” | zgodne | bez zmian |

## 6. docs/design/ITEMS_COMBAT_SPEC.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| L39 | `Gloves` = wariant tekstury rąk FPP | „opancerzone rękawice”; zmiana ekwipunku zmienia model | K | rękawice jako osobny model (mesh) z wariantami; tekstura sama nie wystarcza |
| L134, L139 | starter: „istniejący miecz FPP (#36)”, „obecne rękawice FPP (#36)” | pełny miecz (ostrze, jelec, rękojeść, głowica), finalne modele | D | wskazać finalne modele z ETAP 2 (#34/#36) |
| L149–157 | NPC v1 ≤ 10 000 trójkątów, 1024², 1 LOD; ręce FPP 1 LOD; broń LOD opcjonalny | „Każdy model wymaga … LOD”; Kowal 10–20 tys., 2K, LOD0/1/2 | K | NPC: Kowal 10–20k / 2048² / LOD0–2 (zgodne z METINAR-009 §1.1); LOD wymagany dla każdego modelu (dla dłoni FPP i broni: decyzja techniczna grafika, ile poziomów) |
| L141 | „Wędrowny Handlarz” | streszczenie: „Wędrowny Kupiec” | N | sprawdzić w tekście dosłownym; do tego czasu bez zmiany (ta sama nazwa w MOBS_NPC L93, #38, #45, #50) |

## 7. docs/design/MOBS_NPC.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| L11 | Trujący Pająk: Idle, Walk, Attack, Hit, Death (+ Aggro) | „6 animacji” | D | ustalić listę 6 klipów (przydział ETAP 1/2: idle, ruch, zamach/telegraf, atak, trafienie, śmierć) |
| L94, L98 | Kowal: hartowanie, przekucie; animacje NPC z pipeline'u | „pełny NPC 3D z animacjami, ulepszanie +1 do +10 bez niszczenia przy porażce” | D | dopisać wymóg pełnego modelu 3D z animacjami i budżet Kowala (10–20k, 2K, LOD0–2); +1…+10 bez niszczenia jest już w ITEMS_COMBAT_SPEC L14, L84 (zgodne) |
| L67 | spawn 2,5 m od kamery | — (spec nie podaje odległości) | zgodne | bez zmian |
| L87 | „Spawny ustala serwer (#22), klient tylko wyświetla” | wykluczenia z buforami, ostrożna walidacja | zgodne | bez zmian; w etapie 1 klient bez serwera (patrz #55) |

## 8. docs/design/CLASSES_SKILLS.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| L14 | „tarcza (tylko Bastion)” | 1H: „lewa ma tarczę i wykonuje gesty”; zestaw startowy ma już puklerz (ITEMS §7) | K | tarcza dla każdego wojownika z bronią 1H; Bastion daje umiejętności tarczy |
| §3 L79 | AURA = gest obu rąk z wybranym wzmocnieniem klasy | Aura: 7 kroków, niebiesko-złoty efekt, bez obrażeń przy aktywacji | D | dopisać sekwencję wizualną; wzmocnienie klasy pozostaje skutkiem (bez obrażeń) |
| klasy, ścieżki, 56 umiejętności, stopnie | 4 klasy × 2 ścieżki, 14 umiejętności, Wprawa→Szczyt | to samo | zgodne | bez zmian |

## 9. docs/design/MODES_DATA_MODEL.md, ECONOMY.md, OPEN_QUESTIONS_PLAN.md, design/README.md

| Miejsce | Obecnie | Spec | Typ | Potrzebna zmiana |
|---|---|---|---|---|
| MODES L32 | `ExpCurve` `expToNext[1..59]` | maks. poziom 99 | K | `[1..99]` (kod `ExpCurve` ma już poziomy 1–99, OPEN_QUESTIONS L47) |
| MODES L57–58 | „W trybie offline/prototypu profil może być zapisany lokalnie (JSON), oznaczony jako niezweryfikowany” | „Zapis OBOWIĄZKOWY… zamknięcie, restart, ponowne uruchomienie telefonu i ponowne logowanie” | K | zapis lokalny obowiązkowy w etapie 1 + synchronizacja później (#71, #39); przy ponownym logowaniu stan z serwera |
| MODES §4 | serwer „do decyzji” dla nagród/łupu | „Nagrody nie mogą opierać się wyłącznie na deklaracjach klienta” | K | kolumna „Docelowo serwer” dla wyniku spotkania, EXP, talarów, łupu = rozstrzygnięte (tak) |
| MODES §1 | tryby mapy/AR | tryb A: obrót, zoom, bez joysticka, symulacja oddzielona od nagród produkcyjnych | D | dopisać obrót/zoom mapy i flagę „symulacja = bez nagród produkcyjnych” |
| ECONOMY L9 | nazwa waluty „do decyzji (Q7)” | waluta: talary (spec, test kroku 18) | K | wpisać „talary (rozstrzygnięte)” |
| OPEN_QUESTIONS §2 L41–58 | plan S1–S13 „propozycje” | etapy 1–6 | D | dodać mapowanie S1–S13 → etapy 1–6 (np. S2–S4 + zapis + audio = etap 1; S7 + combo = etap 2; S6/S9 = etap 3; S10/S11 = etap 4; S8 = etap 5) |
| design/README | lista dokumentów | nowa specyfikacja | D | dodać odnośnik do specyfikacji 2026-10-23 i tego pliku |

## 10. Inne dokumenty (zgodne, informacyjnie)

- `docs/METINAR-009-ASSET-PIPELINE.md` §1.1–1.2: NPC 10–20k / 2048², 30 fps w AR — **zgodne** ze specem; foldery
  `Characters`, `Weapons`, `Animations`, `Materials`, `Textures` istnieją w `client/Assets/MetinAR/` na `main`.
- `docs/AUTONOMOUS_WORKFLOW.md` L100, L107, L248 (bez `ACCESS_BACKGROUND_LOCATION`, dowód z urządzenia, GPS tylko na pierwszym planie) — **zgodne**.
