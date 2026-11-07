# Walka AR bez chodzenia — analiza techniczna (2026-10-23)

Stan: 2026-10-23 04:10 (Europe/Warsaw). Podstawa: [DECISIONS D3](DECISIONS_2026-10-23.md), zadanie #97 (https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/97).
Kod: `main` @ `e1bdf80` i PR #80 @ `ee48cb9` (kandydat 2, tag `v0-candidate-2`). Liczby z kodu i prefabu; obliczenia offline (Python),
**bez pomiaru na A31** — wartości do potwierdzenia testem fizycznym.

## 1. Liczby w kodzie i w dokumentach

| Wielkość | `main` @ e1bdf80 | PR #80 @ ee48cb9 | Dokumenty | Sprzeczność |
|---|---|---|---|---|
| Odległość postawienia (kamera → potwór, poziomo) | min 1,5 m, bez maksimum: [main ArEncounterFraming.cs:13](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterFraming.cs#L13) | 1,5–3,0 m: [#80 ArEncounterFraming.cs:22–25](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterFraming.cs#L22-L25) | 2,5 m: [main MOBS_NPC.md:67](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/MOBS_NPC.md#L67) | tak — trzy różne wartości |
| Ostrze | 0,13–0,94 m w układzie rękojeści × `SwordScale` 0,75 = 0,10–0,71 m: [main FppViewFraming.cs:16](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/FppViewFraming.cs#L16), [main FppViewFraming.cs:27–31](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/FppViewFraming.cs#L27-L31) | jw. | „od y = 0,13 do 0,94 m”: [main COMBAT_PHYSICS.md:38](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/COMBAT_PHYSICS.md#L38); „zasięg ostrza FPP ~0,94 m”: [main MOBS_NPC.md:68](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/MOBS_NPC.md#L68) | tak — dokumenty pomijają skalę 0,75 |
| Zasięg trafienia od kamery | trafienie tylko przez dosłowną bryłę ciała ([main FppCombatController.cs:109–118](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/FppCombatController.cs#L109-L118)); na A31 0 trafień (L4 FAIL) — zasięgu w praktyce brak | **1,21 m** (najdalszy punkt ostrza w oknie trafienia, [#80 FppCombatController.cs:131–149](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/FppCombatController.cs#L131-L149); log Unity `bladeReach=1.21`, T10e, opis PR #80); komentarz „about 1.25–1.4 m … to the spider centre”: [#80 FppHitGeometry.cs:20–28](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/FppHitGeometry.cs#L20-L28) | „dystans walki 1,2 m”: [main MOBS_NPC.md:68](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/MOBS_NPC.md#L68) | zgodne co do rzędu wielkości |
| Bryła trafienia pająka (lokalnie, przed skalą) | x ±0,285, z −0,605…+0,345: [main FppCombatController.cs:118](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/FppCombatController.cs#L118) | to samo + kolumna do y 3,5: [main FppHitGeometry.cs:17–https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/FppHitGeometry.cs#L17-Lhttps://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/), [main FppHitGeometry.cs:31–https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/FppHitGeometry.cs#L31-Lhttps://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/) | „`CombatHitVolume` w prefabie”: [main COMBAT_PHYSICS.md:40](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/COMBAT_PHYSICS.md#L40) | prefab nie ma colliderów; bryła jest w kodzie |
| Skala potwora | 0,9: [main ArEncounterFraming.cs:22](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterFraming.cs#L22) | 0,9: [main ArEncounterFraming.cs:34–https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterFraming.cs#L34-Lhttps://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/) | — | — |
| Orientacja | potwór zwrócony do kamery (lokalne +z → kamera): [#80 ArEncounterSceneController.cs:842–850](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterSceneController.cs#L842-L850) | | | |
| FOV kamery A31 | szacunek 69,4° pionowo, ekran 540×1200: [main FppViewFraming.cs:34–36](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/client/Assets/MetinAR/Scripts/ArEncounter/FppViewFraming.cs#L34-L36) („Not a device measurement”) | jw. | — | brak pomiaru |
| Wysokość telefonu | „about 1.2–1.5 m above the floor”: [#80 FppHitGeometry.cs:21–22](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/FppHitGeometry.cs#L21-L22) | | | |

Wymiary wizualne pająka (prefab `ArPrototypeSpider.prefab`, prymitywy bez paska HP i cienia, × 0,9): rozstaw nóg **1,18 m**,
wysokość **0,55 m**, głębokość 1,11 m (komentarz w kodzie: „~1,12 m leg span”). Bryła trafienia × 0,9: przód **f = 0,31 m** przed
środkiem, tył 0,545 m, połowa szerokości 0,26 m.

## 2. Wzór na pozycję docelową (standoff)

```
d_stop = R_h − m + f
```
- `d_stop` — pozioma odległość kamera → środek potwora, w której potwór się zatrzymuje;
- `R_h` — poziomy zasięg ostrza z kamery, z tej samej geometrii co trafienie (zasada 8). Przy poziomym telefonie 1,21 m;
  przy pochyleniu θ w przybliżeniu `1,21·cos θ` (30° → 1,05 m) — dokładną wartość daje test na `SwordPose` dla θ = 0…40°;
- `m` — margines na pochylenie i dryf śledzenia: **0,20 m** (pokrywa 30° pochylenia: 0,16 m, plus 0,04 m dryfu);
- `f` — odległość przedniej ściany bryły trafienia od środka: 0,345 × 0,9 = **0,31 m**.

**d_stop = 1,21 − 0,20 + 0,31 = 1,32 m**; przednia ściana bryły jest wtedy 1,01 m od kamery, w zasięgu ostrza (1,21 m poziomo, ≈ 1,05 m przy 30°).

Ograniczenie widoczności (zasada 6): potwór nie może zasłaniać całego ekranu. Przyjęte kryterium: prostokąt otaczający potwora na
ekranie ≤ **60 %** powierzchni ekranu i ≤ 60 % wysokości. Założenia: FOV 69,4° pionowo (szacunek z kodu, nie pomiar), ekran 540×1200
(poziomy FOV 34,6°), telefon 1,2–1,5 m nad podłożem, celowanie w środek potwora.

| d (m) | rozstaw nóg / szerokość ekranu | wysokość / wysokość ekranu | pokrycie ekranu | korpus (bryła trafienia) / szerokość |
|---|---|---|---|---|
| 1,10 | 1,77–2,29 | 0,53–0,60 | 53–60 % | 0,66–0,80 |
| 1,32 | 1,57–1,91 | 0,48–0,51 | 48–51 % | 0,59–0,68 |
| 2,00 | 1,10–1,21 | 0,33 | 33 % | — |
| ≈ 2,2–2,35 | 1,00 (cały rozstaw nóg mieści się w szerokości) | | | |

`d_min` = **1,10 m** (najmniejsza odległość, przy której pokrycie ≤ 60 % dla telefonu na 1,2 m).

**Okno zgodności:** zasięg wymaga d ≤ ≈ 1,36 m (przy 30°) i ≤ 1,52 m (poziomo); widoczność wymaga d ≥ 1,10 m. Okno **1,10–1,36 m
istnieje**, `d_stop` = 1,32 m jest w środku.

**Sprzeczność do zgłoszenia:** przy żadnej odległości w zasięgu cały pająk nie mieści się na ekranie w poziomie. Na 1,32 m nogi
wychodzą poza boki ekranu (1,6–1,9 × szerokość), choć korpus się mieści (0,6–0,7 × szerokość), a ekran nie jest zasłonięty (≈ 50 %).
Pełny rozstaw nóg mieści się dopiero od ≈ 2,2–2,35 m, czyli poza zasięgiem ostrza. FOV kamery to sprzęt, więc go nie zmienimy.
Gdyby właściciel wymagał całego pająka na ekranie podczas walki, trzeba zmienić **model lub skalę**: `MobVisualScale` ≤ ≈ 0,6
(rozstaw ≤ ≈ 0,78 m) przy 1,32 m. Druga możliwość to dłuższy zasięg broni, ale zasada 8 wiąże zasięg z geometrią ostrza.
Skala 0,9 wynika z retestu właściciela przy odległościach 1,5–3 m ([#80 ArEncounterFraming.cs:30–33](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterFraming.cs#L30-L33)). **Decyzja właściciela.**

## 3. Zalecana konfiguracja (wartości PROTOTYPE, w jednym obiekcie konfiguracji, bez stałych w kodzie walki)

| Parametr | Wartość | Uzasadnienie |
|---|---|---|
| `standoffMeters` (d_stop) | 1,32 m, liczone z `R_h` w czasie działania (nie stała) | wzór z §2 |
| `minDistanceMeters` (d_min) | 1,10 m | zasada 6; bliżej → potwór cofa się animowanie do d_stop |
| `approachStartMeters` | d_stop + 0,15 = 1,47 m | histereza: brak drgań na granicy |
| `stopToleranceMeters` | ± 0,05 m | |
| `approachSpeed` | 0,6 m/s (maks. 0,8), rozpędzanie i hamowanie 0,2 s | podejście z 3,0 m ≈ 3 s, płynnie (zasada 5) |
| `turnSpeed` | 180°/s | |
| maks. przesunięcie na klatkę | `approachSpeed_max × min(dt, 0,05 s)` ≤ 0,04 m | test „bez teleportu” |
| Postawienie początkowe | dowolnie w 1,10–3,0 m; powyżej d_stop + 0,15 potwór podchodzi sam | zasady 2–4 |
| Utrata śledzenia | ruch i walka wstrzymane; po powrocie potwór w tej samej odległości i kierunku względem kamery co przed utratą (przycięte do d_min…3,0 m), HP i postęp bez zmian | zasada 7. Obecna relokacja z #80 bierze nowy punkt z celownika ([#80 ArEncounterSceneController.cs:435–461](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/ArEncounterSceneController.cs#L435-L461)) — do zmiany |
| Komunikaty HUD AR | bez „Podejdź”, „Odsuń się”, „Podejdź bliżej”; przy stawianiu tylko „wyceluj bliżej/dalej w podłoże” | zasada 2 |

## 4. Gałąź implementacji

Nowa gałąź `feat/097-ar-no-walk` od `ee48cb9` (head PR #80). PR jest piętrowy: baza `fix/031-v0-p0-hit-tracking`, a po scaleniu #80 baza `main`.
Uzasadnienie: zasady 7 i 8 wymagają poprawek z #80 (kolumna trafienia P0-1, wstrzymanie i ukrycie przy utracie śledzenia z zachowaniem HP,
`BladeReachMeters` z geometrii ostrza). To te same pliki, więc gałąź od `main` dublowałaby #80 i dawała konflikty.
Gałąź, PR #80, `ee48cb9` i tag `v0-candidate-2` **pozostają bez zmian**. Kandydat 2 służy do sprawdzenia wcześniejszych poprawek (#31).
