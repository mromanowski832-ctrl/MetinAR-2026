# ETAP I: protokół odbioru na A31 (22 kroki + krok obowiązkowy A97), 30 FPS w walce, regresja

Właściciel: QA (#26). Zadanie: #90 (METINAR-045). Wykonawca przebiegu: MetinAR Developer z właścicielem przy telefonie. QA weryfikuje dowody.

Źródła:
- Główna specyfikacja (PR #96, `2bb0a72e`): [§18 pętla 22 kroków](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2bb0a72eae0ab9cdb440b022d52927d30f358d1e/docs/design/OWNER_SPEC_2026-10-23.md#L727-L756), [§15 30 FPS](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2bb0a72eae0ab9cdb440b022d52927d30f358d1e/docs/design/OWNER_SPEC_2026-10-23.md#L642-L656), [§20 DoD](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2bb0a72eae0ab9cdb440b022d52927d30f358d1e/docs/design/OWNER_SPEC_2026-10-23.md#L849-L859).
- Luki i kolejność: [SPEC_GAP §3 (PR #84)](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/docs/owner-spec-2026-10-23/docs/audit/SPEC_GAP_2026-10-23.md).
- Zasady sesji urządzenia: [DEVICE-ACCEPTANCE-LOOP-V0.md §1](DEVICE-ACCEPTANCE-LOOP-V0.md). Arkusz sesji v0: [A31-SESSION-PROTOCOL-V0.md (PR #79)](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/docs/qa-a31-session-protocol-v0/docs/qa/A31-SESSION-PROTOCOL-V0.md).
- Narzędzia (PR #100, baza main):
  - [`tools/qa/Invoke-MetinARDeviceSession.ps1`](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/chore/090-a31-session-evidence/tools/qa/Invoke-MetinARDeviceSession.ps1) (kolektor dowodów),
  - [`tools/qa/etap1-steps.json`](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/chore/090-a31-session-evidence/tools/qa/etap1-steps.json) (te same 22 kroki w formie maszynowej),
  - [`tools/qa/perf-budgets.json`](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/chore/090-a31-session-evidence/tools/qa/perf-budgets.json) (`productTargets.ArCombatA31`).

> **Decyzje leada 2026-10-23 04:28 (#90) są wprowadzone** (rozdział 9): progi ArCombatA31 zatwierdzone, scena z konfiguracji, format `[Combo] step=N`, `serialSha256`, harness `Assert`, nagrania na telefonie z listą do ręcznego usunięcia, zasada T09 i prób fizycznych. Dodany obowiązkowy krok A97 z #97 (walka bez chodzenia).
>
> **Poprawki z review leada 2026-10-23 04:52 (#102, #100):** krok 5 tylko w trybie mapy (kamera wyłączona), krok 8 i miejsce pomiaru zgodne z #97 (podejście i zatrzymanie potwora w ≈ 1,32 m), mediana `frameP50Ms` ≤ 33,9 ms, czyszczenie bufora logcat opcjonalne (`-ClearLogcat`), A97 podpowiadany automatycznie przez kolektor z `etap1-steps.json` (#90).
>
> **Status dokumentu:** projekt do przeglądu. Ten dokument nie zawiera jeszcze żadnego wyniku z telefonu. **Wszystkie kroki: UNVERIFIED.**
> Specyfikacja: „Nie deklarować 30 FPS na A31 na podstawie testów jednostkowych lub symulacji” (§15). Zielone CI nie jest dowodem działania na telefonie (§16 p. 8).

## 1. Zasady

Obowiązują bez zmian zasady z [DEVICE-ACCEPTANCE-LOOP-V0.md §1](DEVICE-ACCEPTANCE-LOOP-V0.md):
- **Tożsamość buildu:** jeden raport dotyczy jednego APK. Raport podaje commit (40 hex), nazwę pliku, bajty, **SHA-256**, `applicationIdentifier` i `versionName`. Przebudowa APK oznacza nowy przebieg.
- **UNVERIFIED:** krok bez dowodu z telefonu to UNVERIFIED, nigdy PASS. Przebieg częściowy raportujemy jako częściowy.
- **Sloty (#33):** `LOCAL SLOT ACQUIRED` dla A31 przed pierwszym poleceniem adb i `RELEASED` po ostatnim. W tym czasie nikt nie uruchamia ani nie zamyka Unity na LuzMen. **Nigdy `adb kill-server`.**
- **Zdrowie adb:** `adb devices` przed każdym krokiem. Zerwanie połączenia zapisujemy jako `ADB LOST hh:mm–hh:mm`, a nie jako FAIL produktu.
- **Prywatność:** w GitHubie nie ma numeru seryjnego, IP, kodów parowania ani współrzędnych GPS. Kolektor zapisuje tylko `serialSha256` = pierwsze 12 znaków hex SHA-256 z numeru seryjnego (UTF-8); **surowego numeru seryjnego nie zapisuje nigdzie** (decyzja 4). Logi są redagowane.

Dodatkowo dla ETAPU I:
- **Jeden spójny build integracyjny** (§16 p. 8): wszystkie 22 kroki na jednym APK z jednego SHA. Krok przetestowany na innym APK jest dla tego przebiegu UNVERIFIED.
- **PASS kroku wymaga dowodu z tego samego przebiegu:** wiersz logu albo znacznik czasu w nagraniu, a dla kroków oznaczonych OWN także słów właściciela.
- **Próby fizyczne na A31 (decyzja 7):** każda nieudana próba kroku jest raportowana jako **FAIL tej próby** (numer próby, godzina, wideo, przyczyna). Nieudanych prób nie wolno pomijać ani nadpisywać późniejszą udaną. Krok dostaje PASS **tylko z nagraniem udanej próby**. Bez nagrania udanej próby krok jest UNVERIFIED, nawet jeśli log wygląda dobrze.
- Wskazówka z logu w podsumowaniu kolektora (`steps22[].logHint`) **nie jest** PASS. To tylko podpowiedź, gdzie szukać na nagraniu.

Oznaczenia dowodów:

| Kod | Znaczenie |
|---|---|
| LOG | wiersz z `logcat-filtered.txt` sesji (znacznik czasu + treść) |
| VID | plik nagrania i czas `mm:ss` (screenrecord z kolektora albo nagranie z zewnątrz) |
| IMG | zrzut ekranu |
| MEAS | wartość zmierzona przez kolektor (manifest/podsumowanie) |
| OWN | słowa właściciela (cytat, godzina) |

## 2. Nagłówek przebiegu (wypełnić raz)

| Pole | Wartość |
|---|---|
| Session ID | `A31-RRRRMMDD-NN` (folder `Builds/qa/sessions/<SessionId>/`) |
| Commit gry (40 hex) | `____` (= `-GameCommit`; zgodny z `session-manifest.json` → `gameCommit`) |
| APK: plik / bajty / SHA-256 | `____` / `____` / `____` (manifest → `apk`; `Get-FileHash` tuż przed instalacją musi się zgadzać, inaczej STOP) |
| `applicationIdentifier` / `versionName` | `com.metinar.game` / `____` |
| ApkAudit + Player data | `[PASS]`, ARCore **11/11** (link do komentarza QA) |
| SHA narzędzia QA | `____` (manifest → `qaTool.gitHead`, osobno od commitu gry) |
| Urządzenie | Samsung Galaxy A31, Android `__`, `serialSha256` `____` |
| Slot #33 | ACQUIRED `<link>` … RELEASED `<link>` |
| Stan startowy | thermal `__`, bateria `__` °C / `__` %, ładowanie tak/nie (manifest → pierwsza próbka) |
| Nagranie | `video/…mp4` + SHA-256 (manifest → `videos`), albo plik zewnętrzny; dla A97 także nagranie stóp lub podłogi albo drugie nagranie z boku |
| Pliki na telefonie do usunięcia | lista `deviceCleanup` z `session-summary.md` (rozdział 6.3) |
| Wykonawca / obserwator | `____` / właściciel |

## 3. 22 kroki: definicja

Ta sama lista jest w `tools/qa/etap1-steps.json`, a kolektor kopiuje ją do `session-summary.md`. „Zależy od” to kod, którego **jeszcze nie ma na main** (szczegóły w rozdziale 5). Dopóki ten kod nie trafi do buildu integracyjnego, krok jest UNVERIFIED z przyczyną „brak funkcji w buildzie”.

| # | Krok (§18) | Kryterium PASS | Dowód | Sygnał w logu (wskazówka) | Zależy od | Kto naprawia |
|---|---|---|---|---|---|---|
| 1 | Uruchamiam grę | zimny start z launchera, mapa < 15 s (`device.maxColdLaunchMs`), brak FATAL/ANR | LOG + VID + MEAS TotalTime | `[MetinARPerf] v=1 event=start` | – | #31 (start) |
| 2 | Wczytuję postać Wojownika | po starcie wczytana zapisana postać Wojownika (klasa, poziom), bez tworzenia nowej przy każdym starcie | VID + LOG zapisu/wczytania | brak (do zdefiniowania w #71) | #71 | #71 save/load |
| 3 | Widzę mapę GPS | mapa rzeczywistej okolicy z pozycją gracza z GPS (nie mapa prototypowa) | VID + IMG (współrzędne zamaskowane) | `[MetinARPerf] … scene=WorldMapPrototype` | #87, PR #28 | #87 mapa/GPS |
| 4 | Otrzymuję informację o pobliskim potworze | potwór na mapie, panel z nazwą, poziomem i odległością, komunikat zbliżenia | VID + IMG | brak | #87 | #87 mapa/GPS |
| 5 | Podchodzę do niego | odległość w panelu maleje przy chodzeniu (prawdziwy GPS, nie symulacja); kamera wyłączona / tylko tryb mapy | VID (spacer) + IMG odległości przed i po | brak | #87 | #87 mapa/GPS |
| 6 | Wybieram rozpoczęcie walki | przycisk „Wejdź do AR” w zasięgu startuje spotkanie | LOG + VID | `[WorldMapPrototype] AR ENCOUNTER REQUESTED` | – | #87 / #31 |
| 7 | Włącza się kamera AR | obraz z kamery, stan Starting → Scanning, uprawnienie kamery obsłużone | LOG + VID | `[ArEncounter] state … -> Scanning` | – | #31 AR |
| 8 | Pająk stoi stabilnie na rzeczywistym podłożu | jeden pająk na podłodze, postawiony w 1,10–3,0 m; gdy jest poza zasięgiem ostrza, sam podchodzi i zatrzymuje się w d_stop ≈ 1,32 m (± 0,05 m), nigdy bliżej niż d_min 1,10 m – gracz nie podchodzi (#97); S3-A1…A6 z PR #79 PASS (dryf ≤ 0,10 m, powrót ≤ 0,20 m) | LOG `[ArAnchor]` + VID (z boku albo z miarką: odległość zatrzymania) + OWN; build dev z #97 / PR #104: `[ArReach] swing pitch=… dist=N.NN m` | `[ArEncounter] spawned` / `[ArAnchor]` | PR #80, #97 | #31 AR (PR #80) |
| 9 | Widzę animowane dłonie i miecz | dłonie i miecz FPP widoczne i animowane (idle i atak), bez dziur i migotania | VID + OWN | brak | PR #36, #91 | #91 dłonie |
| 10 | Wykonuję trzyciosowe combo | 3 kolejne ATAK dają cios 1, 2 i 3 (finisher) | LOG + VID | dokładnie `[Combo] step=N` (N = 1, 2, 3; decyzja 3) | **#82** | #82 combo |
| 11 | Przeciwnik traci HP | każdy trafiony cios to jedna linia `[ArDamage] swing=`, HP maleje, etykieta trafienia | LOG + VID | `[ArDamage] swing=` | PR #80 | #31 walka (PR #80) |
| 12 | Aktywuję Aurę | AURA uruchamia Aurę; sama Aura nie zadaje obrażeń (AU1–AU3) | VID | brak | #93 | #93 Aura |
| 13 | Widzę animację dwóch rąk i energii | gest obu dłoni i energia na ostrzu (AU1, AU2) | VID + OWN | brak | #93, #91 | #93 / #91 |
| 14 | Zabijam przeciwnika | HP 0, „Pokonany”, dokładnie jedna linia `defeated`, dalsze ATAK bez obrażeń | LOG + VID | `[ArDamage] defeated` | – | #31 walka |
| 15 | Otrzymuję EXP, talary i przedmiot | nagroda raz: EXP i talary w logu i UI oraz **przedmiot** | LOG + VID + OWN | `defeated … exp=+X talary=+Y` | #49 (przedmiot) | #49 łup |
| 16 | Przedmiot pojawia się w ekwipunku | zdobyty przedmiot widoczny w ekwipunku | VID + IMG | brak | #49 | #49 ekwipunek |
| 17 | Mogę założyć zdobyty przedmiot | założenie zmienia ekwipunek i statystyki (i model, jeśli dotyczy) | VID + IMG | brak | #49, #86 | #49 / #86 |
| 18 | Mogę skorzystać ze sklepu NPC | sklep NPC: zakup albo sprzedaż zmienia talary i ekwipunek | VID + IMG talarów przed i po | brak | #50, PR #45 | #50 sklep |
| 19 | Wracam na mapę | „Wróć do mapy” wraca na mapę; HUD pokazuje nowy EXP i talary | VID + IMG HUD + LOG | brak | #89, #88 | #89 / #88 |
| 20 | Zamykam aplikację | zamknięcie z przełącznika aplikacji; stan zapisany przed zamknięciem | VID | brak | #71 | #71 save/load |
| 21 | Uruchamiam ją ponownie | zimny start bez awarii (jak krok 1) | VID + LOG + MEAS TotalTime | (drugi `event=start`) | – | #31 (start) |
| 22 | Mój poziom, przedmioty i talary nadal istnieją | po restarcie ten sam poziom, EXP, talary i przedmioty co przed zamknięciem | VID + IMG przed i po | brak | #71 | #71 save/load |

### 3.1 Krok obowiązkowy A97: walka bez chodzenia (#97)

Osobny, **obowiązkowy** krok odbioru ETAPU I (decyzja leada 04:28). Nie zastępuje kroków 8, 10 i 11, tylko je łączy w jeden scenariusz bez ruchu gracza. Jest w `tools/qa/etap1-steps.json` jako krok `A97` (`mandatory: true`, poza 22 krokami §18) i kolektor uzupełnia go automatycznie: wskazówka z logu w tabeli kroków, pole `stepA97` i werdykt `stageClosure` w podsumowaniu sesji (#90, PR #100). Wynik PASS/FAIL nadal zatwierdza człowiek po obejrzeniu nagrania (UNVERIFIED do przeglądu), a karta (rozdział 4) przepisuje go z podsumowania.

Kryterium akceptacji z #97 (cytat dosłowny):

> „stoję nieruchomo w bezpiecznym miejscu, rozpoczynam walkę AR, potwór podchodzi w zasięg, wykonuję combo, zadaję obrażenia i mogę zakończyć walkę bez fizycznego chodzenia”

| Pole | Wartość |
|---|---|
| Kryterium PASS | jak w cytacie wyżej, w jednej ciągłej próbie, bez fizycznego chodzenia gracza |
| Dowód (wymagany) | fizyczny test na Samsung A31; **wideo pokazujące stopy lub podłogę albo drugie nagranie z boku** (VID) + LOG walki (`[ArDamage] swing=`, `[Combo] step=N`) + OWN |
| Co nie wystarcza | testy jednostkowe, testy w edytorze i CI. **Bez wideo wynik jest UNVERIFIED** |
| Raport | w #31 / #90 z commitem i SHA-256 APK |
| Zależy od | PR #80, #82 (gałąź `feat/097-ar-no-walk`) |
| Powiązane kroki | 8, 10, 11 (#97 je blokuje) |
| Kto naprawia | MetinAR Developer (#97) |

## 4. 22 kroki: karta przebiegu (wypełnić po sesji)

Kolumny „Commit” i „APK SHA-256” wpisujemy dla każdego kroku. Zwykle `=` (jak w nagłówku). Inna wartość oznacza, że krok nie należy do tego przebiegu (UNVERIFIED). „Wynik” to PASS, FAIL albo UNVERIFIED. FAIL wymaga linku do błędu, UNVERIFIED wymaga przyczyny.

Kolumna „Próby” zawiera **wszystkie** próby fizyczne kroku, np. `1: FAIL 12:04 (wideo 02:10, pająk zniknął, #66); 2: PASS 12:07 (wideo 05:31)`. Każda nieudana próba to FAIL tej próby. PASS kroku jest możliwy tylko z nagraniem udanej próby (decyzja 7).

| # | Commit | APK SHA-256 | Wynik | Próby (nr: wynik, godz., wideo) | Log (stamp + wiersz) | Wideo (plik, mm:ss) | Problem | Kto naprawia |
|---|---|---|---|---|---|---|---|---|
| 1 | = | = | UNVERIFIED | | | | | #31 |
| 2 | = | = | UNVERIFIED | | | | brak funkcji (#71) | #71 |
| 3 | = | = | UNVERIFIED | | | | brak funkcji (#87) | #87 |
| 4 | = | = | UNVERIFIED | | | | brak funkcji (#87) | #87 |
| 5 | = | = | UNVERIFIED | | | | brak funkcji (#87) | #87 |
| 6 | = | = | UNVERIFIED | | | | | #87 / #31 |
| 7 | = | = | UNVERIFIED | | | | | #31 |
| 8 | = | = | UNVERIFIED | | | | PR #80 nie na main | #31 |
| 9 | = | = | UNVERIFIED | | | | | #91 |
| 10 | = | = | UNVERIFIED | | | | brak funkcji (#82) | #82 |
| 11 | = | = | UNVERIFIED | | | | PR #80 nie na main | #31 |
| 12 | = | = | UNVERIFIED | | | | brak funkcji (#93) | #93 |
| 13 | = | = | UNVERIFIED | | | | brak funkcji (#93) | #93 / #91 |
| 14 | = | = | UNVERIFIED | | | | | #31 |
| 15 | = | = | UNVERIFIED | | | | przedmiot: brak funkcji (#49) | #49 |
| 16 | = | = | UNVERIFIED | | | | brak funkcji (#49) | #49 |
| 17 | = | = | UNVERIFIED | | | | brak funkcji (#49, #86) | #49 / #86 |
| 18 | = | = | UNVERIFIED | | | | brak funkcji (#50) | #50 |
| 19 | = | = | UNVERIFIED | | | | | #89 / #88 |
| 20 | = | = | UNVERIFIED | | | | brak zapisu (#71) | #71 |
| 21 | = | = | UNVERIFIED | | | | | #31 |
| 22 | = | = | UNVERIFIED | | | | brak zapisu (#71) | #71 |
| **A97** | = | = | UNVERIFIED | | | | #97 otwarte; zależy od PR #80, #82; bez wideo stóp/podłogi = UNVERIFIED | #97 Developer |

Podsumowanie: `<n> PASS, <n> FAIL, <n> UNVERIFIED` oraz liczba nieudanych prób fizycznych. ETAP I jest zamknięty tylko przy **22/22 PASS i PASS kroku A97 na jednym APK** (§18: „Dopóki ten scenariusz nie działa na fizycznym telefonie, nie mamy ukończonego pierwszego grywalnego wycinka”) i przy PASS bramki 30 FPS z rozdziału 6.

## 5. Co zależy od kodu, którego jeszcze nie ma

Stan na 2026-10-23, main `e1bdf80`. Bez tego kodu kroki nie mogą dostać PASS, a pomiar 30 FPS w walce z combo nie może się odbyć.

| Funkcja | Kroki | Gdzie jest teraz | Skutek dla protokołu |
|---|---|---|---|
| Combo 3 ciosów i log dokładnie `[Combo] step=N` (decyzja 3; lead przypisuje format Developerowi w #82) | 10, A97; **pomiar 30 FPS** | tylko #82 (`feat/082-warrior-combo` @ `eb68369`), nie na main | kolektor daje `CombatFps30 = NO DATA` („no [Combo] lines”) zamiast PASS albo FAIL |
| Zapis i wczytanie postaci | 2, 20, 22 | #71 | brak sygnału w logu; wzorzec dopisać do `etap1-steps.json`, gdy #71 ustali format |
| Mapa z prawdziwym GPS, informacja o potworze, podejście | 3–5 | #87, PR #28 | dziś jest mapa prototypowa; krok 3 ma tylko wskazówkę `scene=WorldMapPrototype` |
| Stabilna kotwica i walka (retest wg #31) | 8, 11, A97 | PR #80 (draft, kandydat-2 `ee48cb9`) | na main brak `[ArAnchor]` i poprawki T10d |
| Profesjonalne dłonie i miecz | 9, 13 | PR #36, #91 | ocena tylko z wideo i od właściciela |
| Aura (gest i energia) | 12, 13 | #93 / #91 | bez logu; AU1–AU5 z PR #79 |
| Przedmiot z łupu, ekwipunek, zakładanie | 15 (przedmiot), 16, 17 | #49, #86 | dziś nagroda to tylko EXP i talary |
| Sklep NPC | 18 | #50, PR #45 | – |
| Powrót na mapę z HUD postępu | 19 | #89, #88 | – |
| Walka bez chodzenia (potwór podchodzi sam) | A97; blokuje 8, 10, 11 | #97 (`feat/097-ar-no-walk`), zależy od PR #80 i #82 | bez tego A97 = UNVERIFIED |
| Scena walki produktu | pomiar 30 FPS | na razie `ArEncounterPrototype` (decyzja 2); zmiana nazwy przy integracji | kolektor bierze scenę z `productTargets.ArCombatA31.sceneNames` albo z `-CombatScene`; nazwy nie ma na sztywno w kodzie |

## 6. Metoda: 30 FPS na A31 w typowej walce (z combo)

Cel z §15: **„Stabilne 30 FPS na A31 podczas typowej walki”**, a do tego brak awarii, brak blokowania głównego wątku, brak narastania pamięci, stabilna kamera AR i kotwica oraz poprawny powrót po minimalizacji.

**Konflikt K3:** obecne 24 fps / p95 55 ms w `perf-budgets.json` → `scenes.ArEncounterPrototype` (i 20 fps / 66 ms w COMBAT_PHYSICS L105) to **bramka regresji prototypu**, nie cel produktu. Kolektor raportuje je osobno jako `PrototypeGate:<scena>`. Cel produktu to `productTargets.ArCombatA31`.

### 6.1 Warunki wstępne
- Build **development lub QA** z sondą `[MetinARPerf]`. Sonda działa tylko z `DEVELOPMENT_BUILD` na Androidzie (`MetinARPerfProbe.cs`), więc na buildzie release pomiar daje NO DATA. Okno sondy ma 5 s (`MetinARPerfProbe.WindowSeconds`).
- W buildzie jest combo (#82) z logiem dokładnie `[Combo] step=N` i walka z PR #80 (`[ArDamage] swing=`).
- **Zimny start:** telefon przez 10 min bez gry i bez ładowania, `thermal status ≤ 1`, bateria ≤ 35 °C, poziom ≥ 50%. Jasność stała (np. 50%, bez auto), tryb oszczędzania energii wyłączony, inne aplikacje zamknięte. Wartości zapisuje pierwsza próbka kolektora. Gorący start daje ostrzeżenie (WARN) w pozycji Thermal i przebieg trzeba powtórzyć.
- Miejsce: dobrze oświetlona podłoga z teksturą, pająk postawiony w 1,10–3,0 m; w walce podchodzi i zatrzymuje się w ≈ 1,32 m (#97, krok 8). Gracz stoi w miejscu.

### 6.2 Przebieg
1. Slot #33 ACQUIRED. `adb devices` pokazuje dokładnie A31 jako `device`.
2. Uruchom kolektor (domyślnie próbka co 30 s, nagranie w 4 segmentach po 170 s, razem około 11 min; screenrecord ma limit 180 s na plik):
   ```
   powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARDeviceSession.ps1 -ApkPath <apk> -SessionId A31-RRRRMMDD-NN -GameCommit <40-hex> -DurationSeconds 600 -ScreenRecordSegments 4
   ```
   **Bufor logcat:** domyślnie kolektor **nie** czyści bufora logów telefonu. Przed startem czyta zegar telefonu i odrzuca na komputerze starsze linie (`session-manifest.json` → `logcat.sinceDeviceTime`, `droppedOlderLines`). `-ClearLogcat` wykonuje `adb logcat -c` przed startem. **Ryzyko:** to czyści cały bufor logów urządzenia (wszystkie aplikacje, także nieskopiowane crashe z wcześniejszych uruchomień), więc używać tylko wtedy, gdy nic wcześniejszego z telefonu nie jest już potrzebne.

   Scena walki pochodzi z `perf-budgets.json` → `productTargets.ArCombatA31.sceneNames` (domyślnie `ArEncounterPrototype`). Po zmianie nazwy przy integracji: zmienić tam albo dodać `-CombatScene <nazwa>`.
3. **Rozgrzewka 60 s** (`warmupSeconds`): mapa → wejście do AR → umieszczenie pająka. Okna z tego czasu nie są liczone.
4. **Walka typowa, co najmniej 8 min:** seria combo 3 ciosów co około 5 s z krótkimi przerwami. Co 1–2 min Aura. Pająk ma się odradzać albo trzeba wchodzić w kolejne spotkania. Kamera porusza się naturalnie (kroki 8 i 11). Bez minimalizacji w czasie pomiaru.
5. Po pomiarze, poza oknem FPS: Home na 10 s i powrót (L8, „powrót po minimalizacji”). Osobno można uruchomić `Invoke-MetinARDeviceSmoke.ps1`, które ma sprawdzenie Background.
6. Kolektor kończy się sam. Slot #33 RELEASED.

### 6.3 Co zbierać (kolektor, folder sesji)

| Plik | Zawartość |
|---|---|
| `session-manifest.json` | APK SHA-256, commit gry, SHA narzędzia QA, `serialSha256`, model, Android, API, znaczniki czasu, adb checks i luki, pliki z SHA-256 |
| `samples.jsonl` | co 30 s: PSS (`dumpsys meminfo`), `gfxinfo framestats` (percentyle i PROFILEDATA), SurfaceFlinger `--latency`, thermal status, temperatura i poziom baterii, ładowanie |
| `logcat-filtered.txt` / `logcat-full-redacted.txt` | logcat z pid aplikacji i tagami MetinAR / pełny (zredagowany) |
| `video/*.mp4` | segmenty screenrecord z SHA-256 (pliki zostają też na telefonie w `/sdcard/metinar-<SessionId>-NN.mp4`) |
| `deviceCleanup` (manifest, podsumowanie) | **lista plików do ręcznego usunięcia z telefonu**: ścieżka na telefonie, rozmiar i SHA-256 ściągniętej kopii, SHA-256 na telefonie (odczyt `sha256sum`), `copyVerified` i działanie |
| `session-summary.json` / `.md` | pozycje CombatFps30, PrototypeGate, Memory, Thermal, Crash, ColdLaunch, FramesDisplay i 22 kroki; **każdy werdykt UNVERIFIED** do przeglądu nagrania |

**Nagrania na telefonie (decyzja 6):** kolektor niczego nie usuwa. Plik z listy `deviceCleanup` usuwa ręcznie właściciel albo posiadacz slotu A31, i to dopiero wtedy, gdy SHA-256 kopii zgadza się z SHA-256 na telefonie (`copyVerified = true`, działanie „may be deleted by hand”). Przy „DO NOT DELETE yet” albo „no local copy” pliku nie usuwamy.

`gfxinfo` i SurfaceFlinger są **informacyjne**. Unity rysuje na SurfaceView, więc `gfxinfo` często pokazuje 0 klatek, a SurfaceFlinger mierzy kompozycję ekranu (60 Hz), a nie klatki gry. Werdykt 30 FPS opiera się na oknach `[MetinARPerf]`.

### 6.4 Ocena (`Test-MetinARCombatFps`, progi `productTargets.ArCombatA31`, ZATWIERDZONE przez leada 2026-10-23 04:28, decyzja 1)

Liczone są tylko okna `[MetinARPerf]` scen walki z konfiguracji (`sceneNames`, dziś `ArEncounterPrototype`), które:
- kończą się po rozgrzewce (60 s od początku pierwszego okna tej sceny),
- zawierają w swoim zakresie czasu (koniec − 5 s, koniec] linię walki (`[ArDamage] swing=` albo `[Combo] step=N`) **oraz** linię combo. Combo liczy się tylko w dokładnym formacie `[Combo] step=N` (N ≥ 1, wielkość liter ma znaczenie); np. `[combo] step=1`, `[Combo] step=` czy `step=0` nie są dowodem combo.

Okno bez dowodu walki w logu nie jest liczone.

| Metryka | Próg | Uzasadnienie |
|---|---|---|
| liczba okien walki z combo | ≥ 3 (#90); zalecane ≥ 18 (90 s) | kryterium #90 |
| mediana `fpsAvg` | ≥ 29,5 | „stabilne 30” (decyzja 1) |
| p5 `fpsAvg` (najgorsze 5% okien) | ≥ 27,0 | „stabilne”: bez długich spadków |
| mediana `frameP50Ms` | ≤ 33,9 ms | 1000 / 29,5 ≈ 33,9 ms, spójne z medianą fps ≥ 29,5 (review leada 04:52; poprzednie 34,5 ms ≈ 29,0 FPS było z nią sprzeczne) |
| `frameP95Ms` w każdym oknie | ≤ 40 ms | bez częstych klatek > 1,2 × budżetu |
| `frameSpikes` (> 50 ms) w każdym oknie | ≤ 2 | bez szarpnięć przy ciosie |
| `mainP95Ms` w każdym oknie | ≤ 40 ms | §15 „brak blokowania głównego wątku” |
| PSS: wzrost od pierwszej do ostatniej próbki | < 10%, szczyt ≤ 850 MB (≥ 3 próbki) | §15 „brak narastających wycieków” |
| thermal na starcie / w trakcie | ≤ 1 / < 4; bateria < 45 °C | warunki porównywalne między buildami |
| Crash / ANR / wyjątek `MetinAR.*` | 0 | §15 „brak awarii” |

Wynik pomiaru to `measured = PASS / FAIL / NO DATA`. **Werdykt kroku i bramki pozostaje UNVERIFIED**, dopóki człowiek nie potwierdzi na nagraniu z tego samego zakresu czasu, że trwała typowa walka z combo i że pająk i kamera były stabilne. Dopiero wtedy QA wpisuje PASS albo FAIL z linkiem do sesji.

## 7. Regresja po każdym kandydacie

Kolejność: najpierw tanie bramki, potem urządzenie. Kandydat bez zielonych bramek A–C nie dostaje slotu A31.

| # | Bramka | Kryterium | Uwagi |
|---|---|---|---|
| A | CI `qa-static` (dotnet-tests, qa-tooling, repo-checks, pr-metadata) | wszystko zielone | nie jest dowodem na telefonie |
| B | Unity (LuzMen, slot #33): QA checks, WorldMap smoke, **ArSmoke** (dziś 20/20 na `ee48cb9`, w tym T10d, T10e, T12, T12b) | wszystkie PASS | jedna instancja Unity na raz; nie w czasie slotu A31 |
| C | APK: ApkAudit + `Test-ApkPlayerData.ps1` | `[PASS]`, ARCore 11/11, właściwy `applicationIdentifier`, brak `ACCESS_BACKGROUND_LOCATION` | APK z 0/11 (np. `e18df69`) jest zabroniony |
| D | A31: [A31-SESSION-PROTOCOL-V0 (PR #79)](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/docs/qa-a31-session-protocol-v0/docs/qa/A31-SESSION-PROTOCOL-V0.md): P1–P6, L1–L15, S3-A1…A6, AU1–AU5 | każda pozycja z dowodem albo UNVERIFIED | pętla v0 to podzbiór 22 kroków (6, 7, 8, 11, 12, 14, 15, 19) |
| E | A31: kolektor sesji (rozdział 6) | `CombatFps30` + Memory + Thermal + Crash, przegląd nagrania | po merge'u #82; wcześniej NO DATA |
| F | A31: 22 kroki + A97 (rozdziały 3, 3.1 i 4) | 22/22 PASS + A97 PASS na jednym APK, każda nieudana próba zaraportowana | tylko na buildzie integracyjnym |

**Flake T09 (#66)** w ArSmoke: „placed mob disappears (mobs 0 spawns 1)”, 4 z 18 przebiegów; T10 pada wtórnie. Zasada dla regresji (**zatwierdzona przez leada, decyzja 7**). Testy automatyczne: jeden rerun, raportujemy oba wyniki, dwa pady z rzędu oznaczają FAIL:
- Jeśli padają **tylko** T09 z komunikatem `duplicates: mobs 0 spawns 1` i T10 wtórnie, to raport brzmi „ArSmoke 18/20 + T09 flake #66” z linkiem do logu. Nie jest to cicho PASS.
- Przebieg powtarzamy raz na tym samym SHA i zapisujemy oba wyniki.
- Dwa pady T09 z rzędu albo inny komunikat T09 oznaczają **FAIL** kandydata.
- **Kroki fizyczne na A31 nie mają reruna „na czysto”:** każda nieudana próba to FAIL tej próby w kolumnie „Próby” (rozdział 4). Krok przechodzi tylko z nagraniem udanej próby. Ten sam objaw (mob znika po umieszczeniu) w L7 / S3-A5 to FAIL próby z linkiem do #66.
- Zasada obowiązuje, dopóki #66 nie zostanie naprawione. Potem T09 musi przechodzić 20/20.

## 8. Raportowanie

- Raport przebiegu trafia jako komentarz w #90 (i w zadaniu kandydata). Zawiera nagłówek z rozdziału 2, kartę z rozdziału 4, wynik pomiaru z `session-summary.md`, SHA-256 plików z manifestu i linki ACQUIRED/RELEASED z #33.
- Logi i nagrania dołączamy tylko po redakcji. Serial występuje wyłącznie jako `serialSha256` (12 hex).
- Raport zawiera listę `deviceCleanup` (ścieżka, rozmiar, SHA-256 kopii). Usunięcie plików z telefonu zgłasza właściciel albo posiadacz slotu A31 po weryfikacji SHA.
- Raport kroku A97 idzie też do #31 / #90 z commitem i SHA-256 APK.
- FAIL oznacza błąd z krokami odtworzenia, przypisany do właściciela z kolumny „Kto naprawia”.

## 9. Decyzje leada 2026-10-23 04:28 (#90)

| # | Decyzja | Gdzie w protokole i narzędziach |
|---|---|---|
| D1 | Progi `ArCombatA31` zatwierdzone: mediana fps ≥ 29,5, p5 ≥ 27, frame p50 ≤ 33,9 ms (korekta z review 04:52), p95 ≤ 40 ms, spikes ≤ 2, main p95 ≤ 40 ms, rozgrzewka 60 s | 6.4; `perf-budgets.json` (PR #100) |
| D2 | Scena: na razie `ArEncounterPrototype`; kolektor bierze scenę z konfiguracji (`sceneNames` albo `-CombatScene`), nazwa zmieni się przy integracji | 5, 6.2; PR #100 |
| D3 | Akceptowany jest dokładnie format `[Combo] step=N` (lead przypisuje go Developerowi w #82) | 3 (krok 10), 6.4; PR #100 |
| D4 | Tylko `serialSha256` (12 hex), nigdy surowy serial | 1, 8; PR #100 |
| D5 | Zostaje harness `Assert`; Pester 3.4 nie jest używany | PR #100 (testy) |
| D6 | Nagrania zostają na telefonie, kolektor niczego nie usuwa; raport ma listę do ręcznego usunięcia po weryfikacji SHA (ścieżka, rozmiar, SHA-256 kopii); usuwa właściciel lub posiadacz slotu A31 | 2, 6.3, 8; PR #100 |
| D7 | T09: testy automatyczne – jeden rerun, oba wyniki, dwa pady z rzędu = FAIL. Kroki fizyczne A31 – każda nieudana próba = FAIL tej próby, krok bez nagrania udanej próby nie przechodzi | 1, 4, 7 |
| #97 | Kryterium z #97 jako osobny obowiązkowy krok A97 (walka bez chodzenia) | 3.1, 4, 5, 7 (F) |

Merge (D1 z wcześniejszych ustaleń): lead merguje #75, potem #100, a #102 osobno. QA nic nie merguje.
