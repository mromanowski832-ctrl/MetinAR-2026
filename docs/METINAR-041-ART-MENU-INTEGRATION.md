# Integracja istniejącej grafiki i menu — 2026-10-24

Refs #86, #34, #38, #59. To jest **gałąź integracyjna do review**, nie ukończenie tych zadań ani gotowa gra.

## Problem i rezultat w kodzie

Na `main` modele i ikony z osobnych PR-ów nie są dostępne w jednym drzewie projektu, a `FppHandsView`
buduje zastępcze siatki. Ta gałąź zbiera istniejące oryginalne zasoby, łączy je katalogiem referencji
Unity i dodaje ich konsumentów w grze. Nie nadpisuje gałęzi autorów i nie scala ich PR-ów.

- `Resources/MetinARPresentation.asset`: jawne referencje do dłoni, handlarza, puklerza i 21 ikon.
  Identyfikatory pochodzą z istniejących tabel. Brak zdalnego pobierania assetów podczas gry.
- `FppModelPresenter`: podłącza model dłoni i miecza do istniejącego `FppHandsView`.
  Klipy Idle / Attack / AuraActivate są próbkowane tym samym czasem co dotychczasowa walka.
  Długości ataku i Aury oraz pozycje obu końców ostrza są kontrolowane względem kodu trafień.
  Przy rozbieżności > 2 cm nowa grafika jest odrzucana z błędem; dotychczasowa reprezentacja pozostaje.
  Taki fallback **nie przechodzi odbioru wizualnego**. Nie zmieniono obrażeń, zasięgu ani hitboxa.
- `NpcModelPresenter`: zastępuje ciało istniejącego Wędrownego Handlarza na mapie modelem z #45,
  zachowując pozycję GPS, etykietę i tożsamość obiektu. Dotknięcie otwiera rozmowę i klip Talk;
  koniec rozmowy przywraca Idle. Kupno i sprzedaż pozostają jawnie niedostępne.
- `GameFrontend`: menu w istniejącej scenie mapy, start/powrót do świata, ustawienia głośności,
  ograniczenie animacji menu i katalog 21 przedmiotów. Menu nie przedstawia katalogu jako plecaka.
  Głośność jest stosowana do `AudioListener` i zapisywana lokalnie; katalog nie zmienia ekonomii.
  Menu wstrzymuje zachowania mobów/ruchu testowego i interakcję z HUD-em, potem je przywraca.
  Nie zmienia `Time.timeScale`, uprawnień, obsługi lokalizacji, nazw ani indeksów scen.
  Powrót z AR nie otwiera ponownie ekranu startowego. Menu można przywołać przyciskiem menu mapy.
- Istniejący `WorldMapPrototypeSmokeTest` uwzględnia teraz rozpoczęcie gry przyciskiem menu,
  obecność modelu handlarza oraz nowe działające ekrany menu/ustawień i pauzę/wznowienie mapy.
  Nie pomija menu ani nie zakłada, że stare panele atrap nadal są jego odbiorcami.

Obsługa menu obejmuje `WorldMapPrototype` oraz przyszłe `WorldMapOsmPoc`. Zamiana modelu NPC jest
podłączona do istniejącego `PrototypeNpc` w `WorldMapPrototype`; nie jest deklaracją integracji
z markerami gałęzi #98. Zapis postaci i EQ, założenie drugiego miecza, sklep, pełne combo i końcowa
Aura z nowej specyfikacji nie są ukończone przez tę zmianę.

## Źródła włączone bez zmian

Pełne ścieżki i hashe 305 importowanych plików są w
[`presentation-imports.json`](../tools/assets/presentation-imports.json). CI porównuje każdy plik bajt po bajcie
z zapisanym hashem Git. Zachowano źródła DCC, tekstury, FBX, animacje, `.meta`, manifesty licencji
i wcześniejsze raporty. **Wcześniejsze raporty dotyczą ich własnych SHA, nie tej integracji.**

| Zestaw | PR / przypięty SHA | Zawartość |
| --- | --- | --- |
| Dłonie i miecz FPP | #36 / `e377a75de46d02261bce3ceb9adbb399505d9328` | model, rig, trzy klipy, tekstury, narzędzia i źródła |
| Wędrowny Handlarz | #45 / `d33c716e2b70875f5f9c110b29a6d3df5f091ee7` | model, trzy LOD, sześć klipów, tekstury i źródła |
| Lipowy Puklerz | #63 / `1dd895a36877f454d3619aaa4805ebbc4be63e77` | model, tekstury, prefab i źródła; bez implementacji bloku |
| Ikony, obie partie | #69 / `586c16d9661062eec0dca890d1147580b919acfa` | 21 ikon, postprocessor i źródła z #64/#69 |

Rozszerzenie pipeline o kategorię Player oraz jego testy i korekty importu pochodzą z #36.
Zastosowano jego różnicę do aktualnego `main`; zachowano późniejszy rejestr pochodzenia z `main`.
Nie podnoszono limitów ani nie dodawano zależności/płatnych usług.

## Budżety istniejących modeli

Liczby poniżej pochodzą z raportów autorów przypiętych assetów. Nowy pomiar zintegrowanej sceny
na A31 jest **UNVERIFIED**.

| Asset | Trójkąty LOD0 / LOD1 / LOD2 | Kości | Materiały | Tekstury |
| --- | --- | --- | --- | --- |
| Dłonie FPP #36 | 4932 / — / — | 29 | 1 | 1024² |
| Miecz FPP #36 | 1452 / — / — | 0 | 1 | 1024² |
| Handlarz #45 | 3192 / 1581 / 777 | 25 | 1 | 1024² |
| Puklerz #63 | 1368 / — / — | 0 | 1 | 512² |
| Ikony #69 | nie dotyczy | — | — | 21 × 256² RGBA |

Wymóg LOD dla każdego modelu, dwa miecze 1H i wariant opancerzonych rękawic z #86/#91 nadal wymagają
osobnego dokończenia. Tu są używane stabilne zasoby #36, a nie nowsze, niezweryfikowane klipy #106.

## Pająk: istniejąca blokada

Nie skopiowano, nie zmieniono i nie wdrożono zasobów #30. Obowiązuje zapisany w #92 `HOLD #30`.
Istniejący proceduralny przeciwnik w scenach pozostaje. Sam ogólny cel ukończenia gry nie został
potraktowany jako wyraźne odwołanie wcześniejszego HOLD. Pozostałe modele mobów i Kowal nie są tu tworzone.

## Weryfikacja tej integracji

| Bramka | Stan w środowisku przygotowania |
| --- | --- |
| Skrypt ikon #69, PNG / alfa / import / pokrycie tabel | **PASS: 108 kontroli**; lokalny snapshot ma 109 `.meta`, nie cały projekt |
| Referencje katalogu, 21 id, prefab fileID, pochodzenie, lokalne GUID | **PASS: tryb `--metadata-only`** na lokalnym snapshotcie |
| Dokładne bajty wszystkich 305 importów oraz pełne GUID | Uruchamiane przez CI, bez `--metadata-only`; sprawdzić wynik konkretnego commita |
| Testy .NET, QA i hygiene | Uruchamiane przez GitHub Actions; nie deklarować PASS przed wynikiem |
| Unity `6000.6.4f1`: import i kompilacja | **UNVERIFIED** — Unity nie jest dostępne w tym środowisku |
| Rzeczywiste próbkowanie klipów, materiały, widgety i missing scripts | **UNVERIFIED** — przygotowany `PresentationIntegrationCheck` trzeba uruchomić w Unity |
| Play Mode mapa → AR → powrót; pause/resume; menu/NPC | **UNVERIFIED** |
| APK ARM64/IL2CPP, manifest i instalacja | **UNVERIFIED** |
| A31: wygląd, dotyk, polskie glify, FPS/pamięć/termika | **UNVERIFIED** |

Pierwszy commit integracji `30c2b793898e69600962bd2d62f056dbb3a69184` przeszedł na GitHubie:
pełną kontrolę **2011 sprawdzeń / 305 importów / 315 GUID**, Asset Pipeline **366 testów**, GeoCore
i Gameplay Core. To wynik tamtego SHA, nie bramek Unity; po korektach smoke testu trzeba sprawdzić nowy head.

Kontrole dostępne bez Unity:

```bash
python3 tools/assets/check_presentation_integration.py
python3 art-source/ItemIcons/check_icons.py
dotnet test tests/AssetPipeline.Tests/AssetPipeline.Tests.csproj -c Release
```

Pierwszy skrypt wymaga standardowej biblioteki Python. Skrypt ikon wymaga istniejących zależności
generatora, w tym Pillow. `--metadata-only` jest wyłącznie do lokalnego, niepełnego snapshotu.

Na komputerze z Unity, po imporcie pełnej gałęzi:

```text
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.Presentation.PresentationIntegrationCheck.Run
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.FppArt.FppArtAssetCheck.RunBatch
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.WanderingMerchant.WanderingMerchantAssetCheck.Run
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.AssetPipeline.AssetPipelineValidator.RunBatch
Unity -batchmode -projectPath client -executeMethod MetinAR.Editor.MetinARQaChecks.Run
```

Nazwy metod sprawdzono w źródłach. Następnie wymagane są istniejące smoke testy scen, build i odbiór na telefonie wg
[`AUTONOMOUS_WORKFLOW.md`](AUTONOMOUS_WORKFLOW.md) oraz
[`METINAR-011-QA-GATES.md`](METINAR-011-QA-GATES.md).

**Rekomendacja: DRAFT / DO NOT MERGE do przejścia bramek Unity, Android i urządzenia.**
Fallback starej grafiki, brak pełnego EQ/sklepu i HOLD pająka uniemożliwiają uznanie całej gry za ukończoną.
