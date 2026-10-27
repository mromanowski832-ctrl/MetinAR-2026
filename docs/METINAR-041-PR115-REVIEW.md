# PR #115 — przegląd integracji i odbiór Unity/A31

Przegląd z 2026-10-27. Refs #115, #86, #33, #92. Punkt wyjścia:
`ba7cb4123767fda87dfa1e8008089808107bbea9`, gałąź `feat/086-art-menu-integration-20261024`.
Poprawki są na osobnej gałęzi `fix/115-art-menu-scene-integration-20261027`.
Nie zmieniają scen, obliczeń walki, modeli ani 305 przypiętych importów. **DRAFT / DO NOT MERGE.**

## Potwierdzone problemy w kodzie i poprawki

| Problem | Skutek i poprawka |
| --- | --- |
| Viewport katalogu miał `RectMask2D`, ale nie miał grafiki przyjmującej raycast. Teksty mają `raycastTarget=false`. | Gest rozpoczęty na tekście lub przerwie trafiał w modalne tło, którego przodkiem nie jest `ScrollRect`. Viewport dostaje przezroczysty `Image` przyjmujący gesty. Przyciski pozostają nad nim. |
| `Open` zerował pozycję zawartości, ale zostawiał prędkość `ScrollRect`. | Bezwładność katalogu mogła przechodzić na następny ekran. Zmiana strony wywołuje `StopMovement`. |
| NPC uruchamiał `Talk` przed sprawdzeniem, czy `ShowMerchant` przyjmie rozmowę. | Odrzucone zdarzenie kliknięcia przy otwartym menu mogło pozostawić `Talk` bez zarejestrowanego rozmówcy, którego menu przywróci do `Idle`. `ShowMerchant` zwraca wynik przyjęcia, a NPC uruchamia `Talk` tylko po sukcesie. |

To ustalenia z przepływu kodu. Odtworzenie i potwierdzenie napraw w uruchomionym Unity pozostaje **UNVERIFIED**.

## Co sprawdzono w repozytorium

- Katalog `Resources/MetinARPresentation.asset` odwołuje się do rzeczywistych rootów prefabów dłoni,
  handlarza, puklerza i miecza oraz do 21 ikon zgodnych z identyfikatorami tabel.
- Rozszerzony skrypt przechodzi także zależności zagnieżdżonego miecza i kontrolerów animacji.
  Sprawdza istnienie plików pod GUID oraz deklaracje fileID w prefabach, materiałach i kontrolerach.
  Nie wykrył zerwanych referencji w tym zakresie.
- Wszystkie **305 plików importowanych zachowują oryginalne hashe Git**. Pająk #30 nie został dodany ani zmieniony.
- `WorldMapPrototype.unity` ma kamerę z `PhysicsRaycaster`, system zdarzeń i istniejący `PrototypeNpc`.
  `GameFrontend` dołącza model handlarza przy ładowaniu sceny, także przed aktywacją NPC po pierwszej lokalizacji.
- W `ArEncounterPrototype` istniejący `FppCombatController` tworzy `FppHandsView`; jego `Build` pobiera katalog
  i dołącza `FppModelPresenter`. Nie trzeba dopisywać drugiej sceny ani ręcznie stawiać osobnego modelu dłoni.
- Nazwy trzech klipów i ich zakresy w `.fbx.meta` odpowiadają konsumentowi: Idle 2 s, Attack 0,72 s,
  AuraActivate 1,15 s. Kontroler handlarza zawiera Idle/Talk. To kontrola danych, nie dowód odtwarzania klipów.
- W importowanym prefabie dłoni miecz jest zagnieżdżony w sockecie z jednolitą skalą 0,75,
  zgodną z obecnym kontraktem FPP. Faktyczne położenie po imporcie sprawdza Unity co 10 ms.

Statyczny skrypt **nie rozstrzyga**, czy Unity poprawnie wygeneruje subasset fileID dla FBX,
powiąże kości/krzywe, wyświetli materiały albo zachowa wydajność na telefonie.

## Testy i poprawione bramki

Uruchomione lokalnie na poprawionych źródłach:

| Test | Wynik |
| --- | --- |
| `python3 tools/assets/check_presentation_integration.py` | PASS: 2145 kontroli, 315 GUID, 21 przedmiotów, 305 importów; pełne bajty |
| `python3 tools/assets/test_presentation_dependencies.py -v` | PASS: 4 testy; rzeczywisty graf oraz celowo zerwany GUID, brak pliku FBX i błędny fileID materiału |
| `python3 art-source/ItemIcons/check_icons.py` | PASS: 108 kontroli |
| `git diff --check` | PASS |
| Unity / Play Mode / APK / fizyczny A31 | **UNVERIFIED — brak Unity i urządzenia w środowisku przeglądu** |

Testy zależności dołączono do istniejącego workflow Asset Pipeline. Wynik .NET/QA/pozostałego CI
należy czytać dla **dokładnego aktualnego SHA** w zakładce Checks nowego PR; nie przenosić historycznego PASS z #115.

Rozszerzenia testów Unity, przygotowane do uruchomienia:

- `PresentationIntegrationCheck`: missing scripts w całych hierarchiach czterech prefabów,
  rzeczywiste klipy Idle/Talk handlarza, Attack/Aura/Idle także po przestawieniu kamery,
  połączenia scrollowania i wyzerowanie bezwładności.
- `WorldMapPrototypeSmokeTest`: raycast nad tekstem katalogu i rzeczywisty drag przez `ScrollRect`,
  przejście strony przy nadanej prędkości, kliknięcie produkcyjnego collidera NPC,
  przyjęta/odrzucona rozmowa, Talk → Idle oraz zachowanie pauzy/wznowienia mapy.
- `ArEncounterSmokeTest`: wejście przez prawdziwy przycisk startu przed testami AR,
  wymagany widoczny `FppModelPresenter` oraz brak ponownego ekranu startowego po powrocie.
  Błędy z prefiksem `[MetinAR Art]` blokują smoke; proceduralny fallback nie zalicza odbioru.

## Kolejność odbioru na LuzMen

1. Zweryfikować stan #33. Ostatni odczyt zawierał niezwolniony slot Unity Mapy i raport offline LuzMen.
   Potrzebny jest faktyczny RELEASED albo bezpieczne przekazanie przez kierownika po sprawdzeniu procesu Unity
   i checkoutu. Ten dokument nie przejmuje slotu i nie rezerwuje terminu A31.
2. Użyć osobnego, czystego checkoutu poprawionej gałęzi. Zapisać `git rev-parse HEAD` i `git status --short`.
   Nie nadpisywać checkoutów ani APK testów #80/#104. Sprawdzić wolne miejsce i wersję Unity **6000.6.4f1**.
3. Import i kompilacja: exit 0, zero `error CS`, brak blokujących błędów pakietów. Nie uruchamiać builderów
   regenerujących przypięte modele/prefaby przed odtworzeniem problemu — najpierw testować zapisane artefakty.
4. Uruchomić poniższe istniejące metody, każdą w osobnym procesie Unity z `-batchmode -projectPath client
   -executeMethod <metoda> -logFile <osobny plik>`. Metody same kończą batch z kodem wyniku.

| Metoda | Wymagany rezultat |
| --- | --- |
| `MetinAR.Editor.Presentation.PresentationIntegrationCheck.Run` | PASS, zgodność ostrza ≤ 2 cm w każdej próbce |
| `MetinAR.Editor.FppArt.FppArtAssetCheck.RunBatch` | PASS; przejrzeć także ostrzeżenia kadrowania |
| `MetinAR.Editor.WanderingMerchant.WanderingMerchantAssetCheck.Run` | PASS: rig, animacje, LOD, collider i materiały |
| `MetinAR.Editor.Weapons.StarterBucklerAssetCheck.Run` | PASS: prefab puklerza i jego materiały |
| `MetinAR.Editor.ItemIcons.ItemIconsCheck.Run` | PASS: 21 rzeczywistych sprite'ów |
| `MetinAR.Editor.AssetPipeline.AssetPipelineValidator.RunBatch` | 0 błędów; ostrzeżenia ocenić i zapisać |
| `MetinAR.Editor.AssetPipeline.AssetPipelineSelfTest.Run` | PASS |

5. Następnie użyć istniejącego runnera z katalogu repozytorium:

```powershell
& .\tools\qa\Invoke-MetinARQa.ps1 -Gates DotNetTests,UnityCompile,UnityQaChecks,WorldMapSmoke,ArSmoke -StopOnFail
```

Wymagane nowe wyniki XML obu smoke testów i zero missing scripts. W Editorze obejrzeć model dłoni/miecza
w Idle/Attack/Aura, chwyt lewej dłoni, materiał i punkty ostrza; handlarza, etykietę i przejścia Talk/Idle;
21 ikon, przewijanie po tekście i przerwach, suwak, polskie glify oraz zamknięcie menu bez wznowienia AR w tle.
Zapisać wynik i zrzuty dla tego samego SHA. Historyczne rendery z #36/#45 nie są dowodem tej integracji.

6. Po zaliczeniu bramek Editor zbudować QA APK i przeprowadzić audit:

```powershell
& .\tools\qa\Invoke-MetinARQa.ps1 -Gates AndroidBuild,ApkAudit -StopOnFail
```

Runner uruchamia build z **`-buildTarget Android`**. Oczekiwany plik:
`client/Builds/Android/MetinAR-ar-encounter-qa.apk`, pakiet `com.metinar.game.qa`.
Wymagane ARM64/IL2CPP, rejestracje ARCore, audit manifestu i SHA256 APK. Brak `ACCESS_BACKGROUND_LOCATION`.
Nie zastępować wariantu gry `com.metinar.game`. Sprawdzić różnice plików po Unity i udokumentować nieoczekiwane zapisy.

## Odbiór na Samsungu Galaxy A31

Po zakończeniu procesów Unity uzyskać oddzielny slot A31 w #33. Podczas testu urządzenia nie uruchamiać
ani nie zamykać Unity na LuzMen: istniejący protokół odnotowuje wtedy zerwanie ADB.
Telefon musi być widoczny jako `device`; monity USB/kamery/lokalizacji wymagają rzeczywistej obsługi właściciela.

```powershell
& .\tools\qa\Invoke-MetinARDeviceSmoke.ps1 `
  -ApkPath .\client\Builds\Android\MetinAR-ar-encounter-qa.apk `
  -Package com.metinar.game.qa -PermissionWaitSeconds 120 -SoakSeconds 600 `
  -BudgetScene ArEncounterPrototype
```

Podczas 10 minut zebrać nagranie i wykonać:

| Czynność | Oczekiwany wynik |
| --- | --- |
| Zimny start → Rozpocznij przygodę | Jedno menu; powrót do działającej mapy, bez zasłoniętego HUD-u |
| Katalog: gest po tekście, przerwie, ikonie i przycisku; zmiana strony po szybkim przewinięciu | Dostępne wszystkie 21 pozycji; brak odziedziczonego rozpędu; polskie znaki i ikony czytelne |
| Ustawienia → suwak i ograniczenie ruchu → ponowne uruchomienie | Wartości zachowane; prawidłowe działanie suwaka i ograniczenia animacji |
| Kliknięcie modelu handlarza → rozmowa → katalog/zakończenie | Klikalny produkcyjny collider; Talk tylko w rozmowie, potem Idle; bez transakcji |
| Mapa → AR → podłoże → postawienie istniejącego moba | Rzeczywista kamera i jeden zakotwiczony mob; brak zmian w HOLD #30 |
| Idle → ATAK → AURA; szybkie naciskanie; utrata i odzyskanie tracking | Zaimportowane dłonie i miecz; poprawny chwyt i ostrze; zero komunikatów odrzucenia grafiki, brak kapsuł jako zastępstwa |
| Wyjście AR → mapa → menu → ponowne wejście AR | Brak ponownego ekranu startowego, powielonych dłoni/NPC i zablokowanej mapy |
| Blokada ekranu i przełączenie aplikacji, także przy odmowie uprawnień | Brak crash/ANR; działający powrót i poprawny cykl GPS/kamery |

Przy walce stać w miejscu; nie wymagać chodzenia w celu dosięgnięcia moba. Ten PR nie zawiera poprawek
podchodzenia z #104, dlatego brak możliwości takiego testu należy zapisać jako blokadę tej wersji,
a nie obchodzić ruchem gracza. Integracja #104 wymaga osobnego, jawnego przetestowania wspólnego drzewa.

Zapisać model/Android, kod SHA, SHA256 APK, wyniki każdej czynności, nagranie, zredagowany logcat,
średni FPS, p50/p95 czasu klatki, pamięć i termikę. Harness ma istniejące progi techniczne w
`tools/qa/perf-budgets.json`; jego PASS sam nie dowodzi stałych 30 FPS ani odbioru wizualnego.
Sprawdzić osobno także mapę (`-SkipInstall -BudgetScene WorldMapPrototype`). Nie publikować surowych logów z danymi urządzenia/lokalizacji.

## Ograniczenia i decyzja o scaleniu

Brak Unity/APK/A31 = **DO NOT MERGE**. Nawet zielone testy Python/.NET nie zamykają tych bramek.
Pozostaje stabilny rig #36; nowszy #106 (`5b62e079d7c2f11076350d6bda7839a2116d70f6`) ma osobne
combo/blok/Aurę i nie został tu skopiowany. Nie ukończono ekwipunku, sklepu, zapisu postaci,
wariantów broni/rękawic ani wszystkich wymagań LOD. **HOLD #30 pozostaje bez zmian.**
