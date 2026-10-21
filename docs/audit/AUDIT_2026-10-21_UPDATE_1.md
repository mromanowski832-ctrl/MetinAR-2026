# MetinAR — aktualizacja 1 audytu z 2026-10-21 (delta, macierz bramek, sloty, droga do MVP)

| Pole | Wartość |
|---|---|
| Stan odczytu | 2026-10-21, ok. 15:00–15:15 (czas polski, Europe/Warsaw) |
| Dokument bazowy | [AUDIT_2026-10-21.md](AUDIT_2026-10-21.md) (merge `1c32343`, PR [#70](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/70)). **Pozostaje bez zmian** jako zapis stanu z 14:00–14:25. Ta aktualizacja go nie poprawia, tylko dopisuje deltę |
| `main` | `3b796a3a5df4d2982e59f11853fe05af228e71db` (PR #37, 14:58) |
| Sterowanie | [#33](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33), workflow [AUTONOMOUS_WORKFLOW.md](../AUTONOMOUS_WORKFLOW.md) |
| Wykonawca | Kierownik produkcji (podagent na boxie). Odczyt GitHuba przez `gh`, czysty klon, próbne merge lokalnie bez push |
| Czego **nie** zrobiłem | Unity, build APK i test A31 (LuzMen i telefon poza zakresem). Rozmów botów nie czytałem. Wyniki Unity/APK/A31 = deklaracje botów z linkiem (archiwalne) |

Statusy: PLAN · W REALIZACJI · DO INTEGRACJI · DO WERYFIKACJI · ZWERYFIKOWANE · BŁĄD · ZABLOKOWANE · BRAK DANYCH. Priorytety P0–P3. **Raport częściowy** w zakresie Unity/A31 (jak wyżej).

## 1. Delta od audytu

### 1.1 Decyzje

| Decyzja | Treść | Dowód |
|---|---|---|
| D1–D4 + plan audytu | przyjęte przez właściciela 14:33–14:35 | [#33 6316048203](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316048203) |
| D2 — doprecyzowanie | dłonie = wyłącznie wirtualne dłonie z mieczem na ekranie; **bez śledzenia prawdziwych dłoni kamerą** | j.w. |
| HOLD #30 | właściciel: „Na razie nie ruszaj #30”. Merge #36 i #45 (zależne od #30) czekają na osobną decyzję właściciela | [#33 6316204526](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316204526) |
| Reguła slotów | jedna procedura REQUEST → przydział → ACQUIRED → RELEASED | [#33 6316795354](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316795354) |

### 1.2 Co weszło na `main` (wszystko squash-merge przez Kierownika wg D1)

| PR | Merge SHA | Czas | Testy / CI na `main` | Uwagi |
|---|---|---|---|---|
| [#70](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/70) audyt | `1c32343` | 14:36 | qa-static, geo-core, asset-pipeline SUCCESS ([run 39600942672](https://github.com/mromanowski832-ctrl/MetinAR-2026/actions/runs/39600942672)) | tylko docs |
| [#27](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/27) serwer (D3) | `e2863e5` | 14:42 | **server-tests 102/102** ([run 39601692713](https://github.com/mromanowski832-ctrl/MetinAR-2026/actions/runs/39601692713), log: „Total tests: 102, Passed: 102”) | #22 [DONE]. Head PR `f06850f` |
| [#43](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/43) SFX walki | `4fa13ec` | 14:44 | 3 workflowy SUCCESS ([run 39602024179](https://github.com/mromanowski832-ctrl/MetinAR-2026/actions/runs/39602024179)) | Unity `AudioImportVerifier` 13/13 na `f7af1d9` (archiwalne, [#33 6316158401](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316158401)) |
| [#62](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/62) SFX zestawu | `75c75c5` | 14:49 | 3 workflowy SUCCESS ([run 39602698303](https://github.com/mromanowski832-ctrl/MetinAR-2026/actions/runs/39602698303)) | konflikt po squashu #43 rozwiązany merge commitem `31124ca`. **Sprawdziłem:** `git diff 94798b4 75c75c5 -- client tools` = pusty, czyli drzewa identyczne z wersją z Unity PASS 27/27 |
| [#37](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/37) bramki QA | `3b796a3` | 14:58 | 3 workflowy SUCCESS ([run 39604005690](https://github.com/mromanowski832-ctrl/MetinAR-2026/actions/runs/39604005690)); server-tests nie startują (filtr `server/**`) | #26 [DONE]. **Sprawdziłem:** różnica `3e9e675..3b796a3` w `client/` = tylko `Audio/**` (+2 pliki `.meta` folderów), więc bramki Unity z `3e9e675` są aktualne dla kodu poza audio |

**Testy .NET na `main` `3b796a3`, uruchomione w tej aktualizacji (box, .NET 8.0.426):** AssetPipeline.Tests 267/267, GeoCore.Tests 125/125, MetinAR.QA.Tests 34/34, MetinAR.Server.Tests 102/102. **Unity na `main`: nie uruchomione (BRAK DANYCH dla SHA `3b796a3`).**

Nowe issues: [#71](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/71) zapis (T08), [#72](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/72) socket tarczy (T16), [#73](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/73) APK QA (T19, PR [#75](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/75)), [#74](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/74) aggro (wycinek S3 #48, założył Developer).

### 1.3 Luki audytu (§0.3, §D) — co zamknięto

| Luka / problem z audytu | Stan teraz | Dowód |
|---|---|---|
| P-01 brak merge kodu | **zamknięte** (5 PR na `main`, D1) | §1.2 |
| P-04 wynik retestu A31 13:37 nieopublikowany | **zamknięte** — handoff opublikowany 14:38 | [#31 6316090802](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6316090802) |
| P-03 przyczyna „AR niedostępne” w `f194aa9` | **przyczyna opisana**; poprawka nie jest w przeglądanym kodzie → patrz 1.5 N-01 | j.w. |
| P-11 audio FAIL | **zamknięte** (verifier PASS, merge) | §1.2 |
| P-12 serwer Q1–Q3 | **zamknięte** (D3 wprowadzone, merge) | [PR #27 6316156010](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/27#issuecomment-6316156010) |
| P-14 tytuły #38, #34 | **zamknięte** (#38 [REVIEW], #34 [IN PROGRESS]) | [#33 6316052997](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316052997) |
| P-15 socket tarczy bez issue | **zamknięte** (#72) | #72 |
| P-06 brak zapisu | issue #71 istnieje, kod: PLAN | #71 |
| P-10 adb / pakiet QA | reguła poprawiona (start **i** zamknięcie Unity); PR #75 `com.metinar.game.qa` DO INTEGRACJI | [#33 6316048203](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316048203), PR #75 |
| Checklista urządzenia QA (F.5) | **zamknięte** — [DEVICE-ACCEPTANCE-LOOP-V0.md](../qa/DEVICE-ACCEPTANCE-LOOP-V0.md) na `main` od `3b796a3` (sprawdzone `git log`) | `3b796a3` |

### 1.4 Luki, które pozostały

| ID | Luka | Status |
|---|---|---|
| P-02 | pętla walki rozbita: #67 nadal CONFLICTING z `main`; próbne merge `main`+#65+#32+#67 (dziś, lokalnie) = konflikt w `ArEncounterMob.cs` i `ArEncounterSceneController.cs` | W REALIZACJI |
| P-05 | klipy V3 #36 (T03) — slot Unity od 14:58 | W REALIZACJI ([#33 6316637727](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316637727)) |
| P-07 | spawn bez reguł terenu; notka serwera do #55 bez implementacji | PLAN ([#55 6316143523](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/55#issuecomment-6316143523)) |
| P-08 | T09 (#66) otwarte; T15 sklasyfikowane w `main` (`SmokeExceptionClassifier`) | BŁĄD (T09) |
| P-13 | mappoc `ae73090` zbudowane (SHA256 w [#33 6316593381](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316593381)), spacer czeka na właściciela | DO WERYFIKACJI ([#33 6316753173](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316753173)) |
| P-17 | 1,5 m (#32) vs 2,5 m (MOBS_NPC §4) | PLAN |
| P-20 | gałęzie bez PR | bez zmian |
| §0.3 | transkrypty botów, kanał kontrolera | BRAK DANYCH |

### 1.5 Nowe błędy (wykryte po audycie)

| ID | Problem | Dowód | Wpływ | Prio | Właściciel | Warunek zamknięcia |
|---|---|---|---|---|---|---|
| N-01 | **Strażnik platformy buildu i walidator rejestracji ARCore nie są w żadnym wypchniętym kodzie.** Handoff #31 mówi „lokalnie, nie wypchnięta”. Przeszukałem wszystkie 33 gałęzie zdalne (`git grep RuntimeInitializeOnLoads\|activeBuildTarget`): brak w kodzie produkcyjnym; w PR #32 @ `8b99a85` jest tylko walidator „stale split” z `596bb67` (`AndroidPlayerDataValidator`), a `RuntimeInitializeOnLoads.json` występuje wyłącznie jako nazwa pliku w fixturze testu. Handoff PR #32 ([6316803316](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/32#issuecomment-6316803316)) tego nie wspomina | [#31 6316090802](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6316090802); `git grep` na `8b99a85` | kolejny APK zbudowany bez `-buildTarget Android` może znów nie mieć ARCore („AR niedostępne”) | **P0** dla przekazania APK | Developer | commit w PR objętym review (najlepiej #32 przed merge, inaczej osobny PR przed APK T07): odmowa buildu przy platformie ≠ Android + walidator 11 rejestracji `Unity.XR.ARCore` + testy .NET na fixture z obu APK; CI zielone; APK v0 przechodzi ten walidator; AR startuje na A31 |
| N-02 | **Błąd Kierownika:** o 15:00 przypomniałem Developerowi o „brakującym” handoffie retestu, choć był w #31 od 14:38. Audyt (§0.3, P-04, F.1) **był poprawny w chwili odczytu** (14:00–14:25, handoff przyszedł 13 min później), ale polecenie F.1 i moje późniejsze komunikaty powtórzyły nieaktualną informację | [#31 6316090802](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6316090802) (12:38Z = 14:38), korekta w [#33 6316795354](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316795354) | zbędna praca Developera ([#31 6316673102](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6316673102)), błędna informacja dla właściciela | P2 | Kierownik | korekta w #33 (jest); przed każdym „brakuje X” świeży odczyt GitHuba |
| N-03 | **Pająk przesuwa się razem z telefonem** (retest `8d0acee`). Poprawka `a58b07f` (wolny `ARAnchor` pod `TrackablesParent`, log `[ArAnchor]`) przeszła Editor smoke 16/16 na `8b99a85`, **na A31 nietestowana** | [#31 6316090802](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6316090802), [#33 6316791839](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316791839) | brak stabilnego osadzenia = v0 nie do odbioru | **P0** dla v0 | Developer | protokół §3 PASS na A31 z logiem `[ArAnchor]` |
| N-04 | `qa-config.json`: merge `a76d064` (#65) przyjął wersję z #37 i **usunął wymaganie** `MetinAR.Gameplay.Tests` z `testProjects`. Projekt jest teraz tylko „opcjonalny” (glob `tests/*/*.Tests.csproj`), więc jego zniknięcie nie da FAIL w runnerze QA. CI #65 ma osobny `gameplay-core-tests.yml`, więc w CI testy nadal biegną | `git diff 9e85ede bc1b466 -- tools/qa/qa-config.json` vs `git diff origin/main a76d064` (pusty) | utrata intencji jednej strony konfliktu (reguła §4) | P3 | Developer (integrator `qa-config.json`), QA review | `MetinAR.Gameplay.Tests` w `testProjects` po merge #65, albo zapis QA, że opcjonalność jest celowa |
| N-05 | Developer przy RELEASED zgłosił **2 procesy Unity.exe, „żaden nie jest mój”**, a ACQUIRED miały tylko modele 3D (dwa przebiegi kolejno) | [#33 6316791839](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316791839), [6316637727](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316637727) | możliwy proces bez slotu → ryzyko dla adb przy A31 | P2 | Kierownik | przed przydziałem A31 lista procesów z właścicielami w #33; 0 procesów Unity przy ACQUIRED A31 |
| N-06 | #71 nie ma w kryteriach przerwanego zapisu ani uszkodzonego pliku | [#71](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/71) | utrata postępu przy zabiciu aplikacji w trakcie zapisu | P1 | Kierownik (uzupełnienie), Developer | kryteria z §8.2 dopisane w #71 |
| N-07 | #8 [BACKLOG] „Real hand tracking” i draft PR #29 nadal otwarte, choć D2 wyklucza śledzenie dłoni kamerą | [#33 6316048203](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316048203) | ryzyko podjęcia pracy poza zakresem | P3 | Kierownik → decyzja właściciela | #8 oznaczone „poza zakresem (D2)”; zamknięcie/usunięcie tylko za zgodą |
| N-08 | sprzeczne liczby przebiegów AR smoke: audyt P-08 „3 kolejne”, audyt T09 „10 kolejnych”, #26 „two consecutive fresh/clean runs” | [AUDIT §D P-08, §E.3 T09](AUDIT_2026-10-21.md), [#26](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/26) | niejasny warunek zamknięcia #66 | P2 | QA | jedna liczba z §5.2 potwierdzona przez QA |

## 2. Najbliższy rezultat: „Pętla walki v0 na A31”

**Definicja:** jeden APK z jednego, jawnie oznaczonego commita (40 hex SHA, nazwa pliku, rozmiar, SHA256, `applicationIdentifier` = `com.metinar.game`, `versionName`), w którym na A31 działa: mapa → AR → przeciwnik stabilnie osadzony w świecie → widoczne wirtualne dłonie i miecz → ATAK → spadek HP → śmierć → **jednorazowa** nagroda (bez podwójnej przy ponownym wejściu/zapisie) → poprawny HUD po powrocie na mapę. Dłonie artystyczne (#36, T10) są osobnym etapem (D2).

### 2.1 Kryteria i stan (lista jawna; stan na najlepszym buildzie z A31 — `8d0acee`)

| # | Kryterium | Test (ID z [DEVICE-ACCEPTANCE-LOOP-V0.md](../qa/DEVICE-ACCEPTANCE-LOOP-V0.md) / §3) | `8d0acee` na A31 | Dowód |
|---|---|---|---|---|
| 1 | mapa | L1 | BRAK DANYCH (handoff nie raportuje mapy) | [#31 6316090802](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6316090802) |
| 2 | wejście w AR | L2 | ZWERYFIKOWANE (`Scanning -> ReadyToPlace -> Placed`, 29–30 fps) | j.w. |
| 3 | przeciwnik stabilnie w świecie | L3, L7, §3 A1–A6 | **BŁĄD** (jedzie z telefonem) | j.w. |
| 4 | wirtualne dłonie + miecz | L3, FPP C1 | ZWERYFIKOWANE (miecz i tabliczka PASS) | j.w. |
| 5 | ATAK | L4–L6 | BRAK DANYCH dla `8d0acee` (archiwalnie „Trafienie!” na `2ec7a47`) | j.w.; [#31 6313559173](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6313559173) |
| 6 | spadek HP | L4 | BŁĄD (oczekiwane: brak #67 w buildzie) | j.w. |
| 7 | śmierć | L9 | PLAN | — |
| 8 | jednorazowa nagroda | L10 + powtórne wejście | PLAN | — |
| 9 | HUD po powrocie | L11 | PLAN | — |

**Wynik: 2/9 kryteriów (22%) potwierdzonych na A31 w jednym buildzie.** Odbiór = 9/9 + Aura (2.2) + bramki etapu z §5.

### 2.2 Aura — zatwierdzone kryteria

| Kryterium | Źródło |
|---|---|
| AURA = gest obu rąk 1,15 s + aktywacja 0,65 s; w tym czasie brak ataku | [COMBAT_PHYSICS.md §4](../design/COMBAT_PHYSICS.md) (spec zaakceptowany 09:51) |
| efekt na ostrzu 6 s, bez bonusu statystyk i kosztu (prototyp) | `docs/METINAR-FPP-001.md` „Technical prototype values” na PR #32 `8b99a85` (na `main` po merge #32) |
| dwuręczny chwyt, niebiesko-złoty efekt, **brak obrażeń od samej AURY** (HP moba bez zmian) | [METINAR-031-FPP-ACCEPTANCE.md C5](../qa/METINAR-031-FPP-ACCEPTANCE.md) |
| spam ATAK/AURA 10 s: brak podwójnych trafień i zawieszeń | C6 i L6 |
| kadr V3: pozycja Aura hold `(0.10,-0.14,0.50)/(10,-30,-18)` | [#31 6314043034](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/31#issuecomment-6314043034) |
| dźwięk `AuraLoop` (nowa wersja, 88192 próbki) — odsłuch właściciela, jeszcze nie wykonany | [#42](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/42) |

### 2.3 Ścieżka merge od stanu bieżącego

| Krok | PR | Head teraz | Stan (sprawdzone) | Do zrobienia | Właściciel |
|---|---|---|---|---|---|
| 1 | [#65](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/65) S1 | `a76d064` | MERGEABLE/CLEAN, CI 14 SUCCESS + 1 SKIPPED; zawiera `main` | review z bramkami (§5) → squash; N-04 | Kierownik (merge), Developer (N-04) |
| 2 | [#32](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/32) FPP V3 + kotwica | `8b99a85` | MERGEABLE, CI 12 SUCCESS + 1 SKIPPED; Unity 0 error CS, AR smoke 16/16, WorldMap 15/15 (archiwalne, [#33 6316791839](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316791839)); próbne merge na `main`+#65 bez konfliktu | **N-01** (strażnik + walidator ARCore) w tym PR lub zaraz po nim; po merge #65 ponowna kompilacja Unity na końcowym SHA, jeśli zmieni się `client/` | Developer |
| 3 | [#67](https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/67) S2a + hook | `f903f02` | **CONFLICTING/DIRTY**; nie zawiera `main` ani #65 head | merge `main` (po 1–2) bez force-push; ręcznie `ArEncounterMob.cs`, `ArEncounterSceneController.cs` z zachowaniem kotwicy z `a58b07f` **i** obrażeń z #67; `FppCombat.HitDetected += Combat.HandleHitDetected`; Play Mode z prawdziwym zamachem; Unity smoke na końcowym SHA | Developer |
| 4 | APK v0 | — | — | build z SHA `main` po kroku 3 (lub gałęzi identycznej z planowanym merge); walidator N-01 + ApkAudit + `Test-ApkPlayerData`; SHA256 w handoffie | Developer; QA weryfikuje |
| 5 | A31 | — | — | slot §6, checklista L1–L15 + §3, OWN dla L3/L9/L10/L11 | Developer (wykonanie), QA (odbiór), właściciel (obserwacja) |

**Nie wchodzi do v0:** #30, #36, #45 (HOLD / decyzja właściciela), #74 aggro, #28 (osobny pakiet mappoc).

## 3. Pająk przesuwa się z telefonem — otwarty błąd odbioru (P0 dla v0)

**Rozdzielenie:** dla v0 mob **nie porusza się wcale**. Każdy ruch moba względem świata w v0 = błąd osadzenia (N-03). Celowy ruch AI (pościg, ataki) to [#74](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/74), dziecko S3 [#48](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/48). Prośba właściciela o pościg jest przypisana do #74/#48, **nie jest kryterium v0**, chyba że właściciel zdecyduje inaczej.

**Protokół na A31** (APK v0, logcat `-v threadtime`, filtr `[ArAnchor]`, nagranie ekranu). Log `[ArAnchor]` co 2 s: `kind`, `anchorState`, `session`, `cam`, `mob`, `d` (pozycje w metrach sesji AR, nie GPS). Tolerancje = **PROPOZYCJA** do potwierdzenia przez QA.

| ID | Krok | Oczekiwany wynik | Dowód |
|---|---|---|---|
| A1 | postaw pająka, stój 10 s | `kind` = kotwica świata (lub jawny fallback), `anchorState=Tracking`, `mob` stałe (dryf ≤ 0,10 m) | LOG + VID |
| A2 | obrót telefonu w miejscu ±90° (pająk wychodzi z kadru i wraca) | `cam` zmienia orientację, `mob` bez zmian (≤ 0,10 m); po powrocie pająk w tym samym miejscu podłogi | LOG + VID |
| A3 | krok w bok 1 m w lewo i w prawo | `cam` przesuwa się o ~1 m, `mob` bez zmian; pająk widziany z boku (paralaksa), nie „przyklejony” do ekranu | LOG + VID |
| A4 | podejście 1 m bliżej i odejście 2 m dalej | `d` maleje/rośnie zgodnie z ruchem (±0,2 m), `mob` bez zmian; pająk rośnie/maleje na ekranie | LOG + VID |
| A5 | utrata śledzenia: zasłoń kamerę 5 s | brak crasha; `anchorState` → Limited/None, brak ruchu moba i brak ataku (COMBAT_PHYSICS §2); dokładnie jeden mob (obserwacja #66) | LOG + VID |
| A6 | powrót śledzenia | `anchorState=Tracking` w ≤ 5 s; pająk w tym samym miejscu (≤ 0,20 m); ATAK działa | LOG + VID |

FAIL któregokolwiek = N-03 otwarte, z fragmentem logu i opisem. Wyniki A1–A6 dołącza się do raportu L3/L7.

## 4. Reguła konfliktów i odbioru wersji zintegrowanej

1. Przy konflikcie w `tools/qa/qa-config.json` i plikach `*.Tests.csproj` porównaj **obie strony** względem wspólnej bazy (`git diff <base> <ours>`, `git diff <base> <theirs>`). Zachowaj każde ustawienie i każdy plik testu potrzebny którejkolwiek stronie. Usunięcie wpisu wymaga uzasadnienia w opisie merge.
   - Przykład poprawny: `8b99a85` zachował w `MetinAR.QA.Tests.csproj` oba wpisy (`SmokeExceptionClassifier` z #37 i `AndroidPlayerDataValidator` z #32) — sprawdzone.
   - Przykład do poprawy: N-04 (`a76d064`).
2. Po integracji autor podaje listę **faktycznie uruchomionych** zestawów na końcowym SHA: nazwa, liczba testów, wynik, link do CI lub logu. Zestaw nieuruchomiony = UNVERIFIED.
3. Wyniki z wcześniejszych gałęzi (np. `8d0acee`, `3e9e675`, `f903f02`) są **dowodami historycznymi**. Odbiór wersji zintegrowanej dotyczy jej końcowego kodu i konfiguracji. Wyjątek: dowód z wcześniejszego SHA obowiązuje tylko wtedy, gdy wykazano `git diff` = pusty w obszarze, którego dotyczy bramka (jak dla #62 i #37 w §1.2).

## 5. Jedna macierz bramek — **PROPOZYCJA do potwierdzenia przez QA**

### 5.1 Macierz

| Bramka | Blokuje merge PR | Blokuje przekazanie APK | Blokuje odbiór etapu na telefonie |
|---|---|---|---|
| CI .NET (qa-static: QA.Tests, qa-tooling, repo hygiene + doc links, PR metadata; geo-core; asset-pipeline; server-tests przy `server/**`; gameplay-core po #65) na końcowym SHA | TAK (każdy PR) | TAK (SHA APK musi mieć zielone CI) | TAK |
| Komentarz review z listą bramek (D1) | TAK | — | — |
| Unity compile, 0 `error CS`, na końcowym SHA | TAK dla PR zmieniających `client/**` | TAK | TAK |
| `ArEncounterSmokeTest` (z grafiką, bez `-nographics`), 1 przebieg na końcowym SHA | TAK dla PR zmieniających `client/**`: każdy FAIL blokuje. **T09** (#66) nie blokuje tylko przy identycznej sygnaturze („mobs 0 spawns 1”, brak logu despawn), wpisanej do #66; T10 tylko jako skutek T09 w tym samym przebiegu. Inna sygnatura = nowy błąd = blokuje | TAK (ten sam warunek) | TAK: **#66 zamknięte** (§5.2) |
| T15 (wyjątki) | TAK, chyba że jedyną przyczyną jest sygnatura `KnownEditorInfrastructure` z `SmokeExceptionClassifier` (UnityEditor.Search, `METINAR-014` §1), zalogowana jako `[ArSmoke] T15 ignored known editor-infrastructure exceptions`. Zakres wyjątku: **tylko Unity Editor batchmode**, test T15 | jak w kolumnie merge | **nie dotyczy urządzenia**: każdy wyjątek `MetinAR.*`/FATAL na A31 = FAIL (L14) |
| `WorldMapPrototypeSmokeTest` | TAK dla `client/**` | TAK | TAK |
| `MetinARQaChecks` (brakujące skrypty, kolejność scen) | TAK dla `client/**` | TAK | — |
| `ArSmokeInjection` (3 tryby: metinar/unknown/searchdb) | TAK tylko dla PR zmieniających klasyfikator, smoke test lub `tools/qa/**` | NIE | NIE |
| Weryfikatory assetów i audio (`AssetPipelineValidator`, `FppArtAssetCheck`, `ItemIconsCheck`, `AudioImportVerifier`, `.meta` bez zmian) | TAK dla PR zmieniających dany obszar | TAK, jeśli asset jest w buildzie | NIE (poza odsłuchem/oceną właściciela, gdy etap tego wymaga) |
| Walidator APK: ApkAudit (pakiet, debuggable, minSdk 31, arm64-v8a, IL2CPP, brak lokalizacji w tle, lista uprawnień), `Test-ApkPlayerData`, **rejestracje ARCore (11 × `Unity.XR.ARCore` w `RuntimeInitializeOnLoads.json`)**, **strażnik platformy Android**, `applicationIdentifier` zgodny z przeznaczeniem (`com.metinar.game` / `.qa` / `.mappoc`) | NIE (PR zmieniający build musi mieć testy walidatora) | **TAK** — ARCore i strażnik dziś nie istnieją (N-01), więc ta bramka jest **PLAN** | TAK (P1–P3 checklisty) |
| Checklista urządzenia: [DEVICE-ACCEPTANCE-LOOP-V0.md](../qa/DEVICE-ACCEPTANCE-LOOP-V0.md) P1–P6, L1–L15 + §3 A1–A6 + FPP C1–C9 | NIE (PR podaje „device: UNVERIFIED”) | NIE | **TAK**: wszystkie PASS z dowodem, OWN dla L3/L9/L10/L11; ADB LOST = powtórzenie kroku, nie PASS |

Błędy produktu (znikanie moba T09, ruch pająka N-03, crash, podwójna nagroda) **nigdy** nie są wyjątkiem. Wyjątek infrastruktury zapisuje się z dokładną przyczyną, ID testu i zakresem (jak T15 wyżej).

### 5.2 Liczba przebiegów AR smoke — jedna liczba

**Propozycja: 10 kolejnych przebiegów `ArEncounterSmokeTest` bez T09 (świeży i ciepły Library, na końcowym SHA etapu) zamyka #66 i jest bramką odbioru etapu.** Do merge PR wystarcza 1 przebieg na końcowym SHA (5.1).

Uzasadnienie: zmierzona częstość T09 = 4/18 ≈ 22% ([#66](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/66)). Gdyby błąd nie był naprawiony, szansa przypadkowego „zaliczenia” wynosi: 2 przebiegi (#26) ≈ 61%, 3 przebiegi (P-08) ≈ 47%, **10 przebiegów ≈ 8%**. Liczby 2 (#26, już zamknięte) i 3 (P-08 audytu) traktuję jako zastąpione. Przy `ArSmokeInjection` „3” oznacza 3 tryby wstrzyknięcia, nie 3 przebiegi stabilności — to inna bramka.

## 6. Protokół slotów (jeden sposób)

Źródło: [#33 6316795354](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6316795354).

| Krok | Kto | Treść |
|---|---|---|
| 1. REQUEST | wykonawca | zakres, SHA, własny checkout, szacowany czas, czy potrzebny adb/A31/build Android |
| 2. Przydział | Kierownik (MANAGER QUEUE/GRANT) **albo** istniejąca reguła techniczna (np. kompilacja tylko-edytor we własnym checkoucie, gdy A31 nie jest ACQUIRED i nie trwa build Android) | kolejność wg ścieżki krytycznej |
| 3. ACQUIRED | wykonawca, **dopiero po przydziale** | start pracy |
| 4. RELEASED | wykonawca | SHA, wynik, liczba procesów Unity.exe (z rozróżnieniem własnych), wolne miejsce na C: |

Zasady stałe:
- Ścieżka krytyczna ma pierwszeństwo (teraz #65 → #32 → #67 → APK v0 → A31).
- Gdy A31 jest ACQUIRED: nikt nie **uruchamia ani nie zamyka** Unity na LuzMen; przed przydziałem A31 = 0 procesów Unity (N-05).
- Buildy Android pojedynczo, `bin/Data` wyczyszczone, ≥ 30 GB wolnego na C:.
- Każdy we własnym checkoucie; nikt nie przerywa aktywnego procesu innego bota.
- HOLD właściciela (#30) nie jest odblokowywany starą kolejką.

## 7. Odpowiedzialność (jeden właściciel za wynik końcowy)

| Zadanie | Właściciel wyniku | Podzadania / integrator | Status |
|---|---|---|---|
| #33 kolejka, #41 program, #10, #15 | Kierownik | — | W REALIZACJI (#15, #10 nieaktualne) |
| #31 / PR #32 FPP V3 + kotwica + N-01 | Developer | — | DO INTEGRACJI (N-01, N-03) |
| #46 / PR #65 S1 | Developer | — | DO INTEGRACJI |
| #47 / PR #67 S2a + S2b (A31) | Developer | QA: odbiór checklisty | W REALIZACJI (konflikt) |
| APK v0 + test A31 (T07) | Developer | QA weryfikuje; właściciel obserwuje | PLAN |
| #66 T09 | Developer | QA: 10 przebiegów (§5.2) | BŁĄD |
| #71 zapis (T08) | Developer | QA: testy uszkodzeń | PLAN |
| #73 / PR #75 APK QA + adb | QA | — | DO INTEGRACJI (CI 17 SUCCESS + 1 SKIPPED) |
| Bramka ApkAudit: rejestracje ARCore (niezależnie od C#, jak `Test-ApkPlayerData`) | QA | Developer: walidator C# w buildzie (N-01) | PLAN |
| #34 / PR #36 klipy V3 | modele 3D | Developer: T10e w integracji | W REALIZACJI; merge czeka na decyzję o #30 |
| #24 / PR #30 pająk | modele 3D | — | ZABLOKOWANE (HOLD właściciela) |
| #72 socket tarczy | modele 3D | — | PLAN (po #36) |
| #38 / PR #45 handlarz | grafik | — | ZABLOKOWANE (zależy od #30) |
| #59: PR #63 puklerz / PR #64, #69 ikony | integrator: grafik | modele 3D: #63 | DO INTEGRACJI (#69 DO WERYFIKACJI) |
| #35 import Metin2 | grafik | — | ZABLOKOWANE |
| #20 / PR #28 mapa OSM | Mapy | właściciel: spacer | DO WERYFIKACJI |
| #68 muzyka | Dźwięk | właściciel: ocena | W REALIZACJI (podglądy poza repo) |
| #48 S3 (z #74 aggro) | Developer (integrator) | Dźwięk: hooki SFX z #43; modele 3D: klipy pająka (po decyzji o #30) | PLAN |
| #49 S4 ekwipunek | Developer (integrator) | grafik: ikony/atlas; modele 3D: puklerz/socket | PLAN |
| #50 S5 sklep | Developer (integrator) | grafik: handlarz (#45) | PLAN |
| #51 S6, #52 S7, #54 S9 | Developer | — | PLAN |
| #53 S8 klasy | Developer (integrator) | modele 3D: rekwizyty i animacje FPP, osobny issue na klasę | PLAN |
| #55 S10 eksploracja i spawny | Mapy (integrator) | serwer: reguły i źródło werdyktu (opcje A/B/C, [6316143523](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/55#issuecomment-6316143523)); Developer: spawn w scenie mapy | PLAN |
| #39 tożsamość + zapis serwerowy, #56 S11 | serwer (integrator) | Developer: klient API | PLAN |
| #57 S12 balans | Developer | QA: pomiary A31 | PLAN |
| #58 S13 Napisy | Developer | Dźwięk/grafik/modele 3D: rejestry licencji | PLAN |
| #8 / PR #29 śledzenie dłoni | Kierownik (porządek) | decyzja właściciela | poza zakresem wg D2 (N-07) |

## 8. Droga od pętli v0 do MVP i pierwszego wydania

### 8.1 Etapy

| Etap | Zakres (issues) | Zależności | Kryterium zakończenia |
|---|---|---|---|
| **E2 Pętla v0** | #65, #32, #67, N-01, N-03 | — | §2: 9/9 + Aura + bramki etapu (§5) na A31 |
| **E3 Pętla trwała** | #71 zapis (8.2), #66 zamknięte, #75, #28 (po spacerze), T10 dłonie art (#36, po decyzji o #30) | E2 | restart / przerwany zapis / uszkodzony plik PASS na A31; 10 przebiegów bez T09; mapa bez skoków (OWN) |
| **E4 Walka pełna** | #48 S3 (+ #74), #51 S6, hooki SFX (#43 na `main`), pająk #30 (decyzja) | E2 (S3), E3 (zapis HP/EXP) | mob goni i atakuje, HP gracza, śmierć gracza (kara Q4), brak ataku przy utracie śledzenia; A31 ≥ 24 fps |
| **E5 Ekwipunek i ekonomia** | #49 S4, #50 S5, #54 S9, #72, #59 | E3 | łup → ekwipunek → sklep → zapis, bez duplikacji przedmiotów i talarów |
| **E6 Świat** | #55 S10 (D4 bufory ostateczne), mapa OSM zamiast prototypu, #39, #56 S11 | #28, serwer | spawny tylko w miejscach dozwolonych (testy odrzucenia), nagrody potwierdzane przez serwer |
| **E7 Klasa MVP** | #52 S7 (wojownik) | E4 | 14 umiejętności wojownika na przyciskach |
| **E8 MVP** | #57 S12, #58 S13, #68 | E4–E7 | sesja 30 min na A31 bez crasha; fps/PSS/termika w budżecie; ekran Napisy; muzyka zaakceptowana |
| **E9 Pierwsze wydanie** | #53 S8 (opcjonalnie), polityka prywatności, kanał dystrybucji | E8 + decyzje 8.3 | build release podpisany, test na urządzeniach z 8.4, **publikacja tylko przez właściciela** |

### 8.2 Zapis postępu (#71) — wymagania do dopisania

| Scenariusz | Wymaganie | Test |
|---|---|---|
| zwykły restart | profil (poziom, EXP, talary, id rozliczonych spotkań) zapisany po każdej zmianie; odczyt przy starcie | .NET round-trip; A31: zabij pająka → zamknij → uruchom → stan bez zmian |
| przerwany zapis | zapis do pliku tymczasowego, `Flush`, potem atomowa podmiana (`File.Replace`/rename) z kopią `.bak` | .NET: przerwanie między zapisem tmp a podmianą → stary profil nienaruszony; A31: `am force-stop` zaraz po nagrodzie |
| uszkodzony plik | wersja schematu + suma kontrolna; przy błędzie: odczyt `.bak`; gdy oba złe — nowy profil **z widocznym komunikatem** i logiem, kopia uszkodzonego pliku zachowana; nigdy crash, nigdy cichy reset | .NET: plik pusty, obcięty, losowe bajty, zła suma, przyszła wersja schematu |
| jednorazowa nagroda | id spotkania zapisany razem z nagrodą; ponowne wejście/odczyt nie przyznaje drugi raz | .NET + L10 |

### 8.3 Decyzje produktowe właściciela (z rekomendacją)

| # | Decyzja | Rekomendacja |
|---|---|---|
| P1 | #30 (HOLD) i zależne #36, #45 | po gotowych klipach V3: merge `main` do #30 bez force-push, potem #30 → #36 → #45 |
| P2 | czy aggro (#74) wchodzi do odbioru v0 | nie; v0 = mob stoi, aggro w E4 |
| P3 | #8 / PR #29 (śledzenie dłoni) | oznaczyć jako poza zakresem (D2); gałęzie zostają jako referencja, bez usuwania |
| P4 | zakres MVP | jeden wojownik (S7), pająk + handlarz, zapis lokalny, mapa OSM; pozostałe klasy (S8) po MVP |
| P5 | zapis w MVP: lokalny czy serwerowy | lokalny z kopią zapasową (8.2); serwer (#39/S11) w E6 |
| P6 | serwer w chmurze (koszt) | odłożyć do E6; wcześniej tylko lokalnie |
| P7 | kanał pierwszego wydania (wydatek na konto deweloperskie) | zamknięty test wewnętrzny; decyzja i publikacja wyłącznie przez właściciela |
| P8 | drugie urządzenie testowe (zakup) | co najmniej jeden dodatkowy telefon z ARCore przed E9 |
| P9 | iOS (pakiet ARKit jest w projekcie) | poza pierwszym wydaniem |
| P10 | min. odległość postawienia moba 1,5 m (kod) vs 2,5 m (spec) | przyjąć 1,5 m, jeśli test A31 (A4) potwierdzi wygodę; skorygować spec |

### 8.4 Testy platform

| Platforma | Stan |
|---|---|
| Samsung Galaxy A31, Android 12 (API 31), Mali — minimum | jedyne urządzenie z wynikami |
| Inne urządzenia Android | BRAK DANYCH |
| iOS | BRAK DANYCH (bez testów, bez buildu) |

## 9. Następny punkt kontrolny i następny raport

**Punkt kontrolny:** „Pętla walki v0 na A31” — §2 9/9 + Aura + protokół §3 + bramki etapu §5 na jednym APK z jednego SHA, z dowodami QA w #47 i #31.

**Następny raport (Aktualizacja 2) pokaże:**
- co scalono: PR → merge SHA → run CI;
- jakie konflikty rozwiązano: plik, co zachowano z każdej strony (§4);
- listę faktycznie uruchomionych zestawów na końcowym SHA z wynikami;
- status N-01 (strażnik + ARCore w przeglądanym kodzie, SHA) i N-03 (A1–A6);
- APK v0: SHA, SHA256, pakiet; wynik checklisty: PASS/FAIL/UNVERIFIED per ID;
- co nadal nie działa i następny konkretny rezultat (E3).
