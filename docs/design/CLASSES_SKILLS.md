# METINAR-017 — Klasy, ścieżki i umiejętności

> **Status: ZAAKCEPTOWANE przez właściciela 2026-10-21 (#40; maks. poziom 99, pozostałe propozycje domyślne). Bez implementacji.**
> Wszystkie koszty, czasy odnowienia i współczynniki to **proponowane wartości startowe do balansowania**.
> Klasy ustalił właściciel: **wojownik, szaman, ninja, Slajter** (Slajter zastępuje „surę”, rola bez zmian).
> Nazwy ścieżek i umiejętności, opisy i liczby są **własne**. Nie są tłumaczeniem ani przeróbką nazw z Metin2.
> Mechaniki gatunku (atak obszarowy, doskok, wzmocnienie, osłabienie, leczenie, pocisk) są dozwolone.
> Dane maszynowe: [tables/classes_skills.json](tables/classes_skills.json). Indeks: [README.md](README.md).

## 1. Przegląd

| Klasa | Rola | Ścieżka A | Ścieżka B | Broń | Druga ręka |
|---|---|---|---|---|---|
| Wojownik | wręcz, wytrzymałość | **Bastion** (`bulwark`) — tarcza, blok, kontry | **Rozłam** (`breaker`) — broń dwuręczna, obszar | miecz 1H / miecz lub topór 2H | tarcza (tylko Bastion) |
| Ninja | zwinny zabójca / łucznik | **Cień** (`shade`) — sztylety, krytyki, ukrycie | **Pustułka** (`kestrel`) — łuk, celowanie kamerą, pułapki | para sztyletów / łuk | kołczan (łuk) |
| Szaman | wsparcie, leczenie, magia | **Korzeń** (`rootward`) — leczenie, osłony, oczyszczanie | **Nawałnica** (`tempest`) — wiatr i pioruny | różdżka runiczna | totem |
| Slajter | wojownik-mag mroku | **Popiół** (`ashblade`) — zaklęte ostrze, kradzież życia | **Zmierzch** (`duskcaller`) — klątwy, czary z dystansu, przyzwanie | miecz 1H | fokus mroku |

Każda klasa ma **2 umiejętności klasowe** (poziom 1 i 5) i **6 umiejętności ścieżki** (poziomy 10, 12, 15, 20, 26, 34).
Razem 14 na klasę, 56 w grze.

## 2. Zasady umiejętności

- **Wybór ścieżki:** poziom 10, u NPC-trenera (w prototypie: Wędrowny Handlarz). Zmiana ścieżki: zadanie + opłata
  (Q11). Po zmianie punkty umiejętności ścieżki wracają do puli.
- **Punkty umiejętności:** 1 punkt za każdy awans od poziomu 2 (98 punktów do poziomu 99). Ranga 1–10 dostępna
  od odblokowania; **rangi mistrzowskie 11–15** wymagają poziomu postaci 60 / 68 / 76 / 84 / 92 (ranga 11 / 12 / 13 / 14 / 15).
  8 dostępnych umiejętności × 15 rang = 120 punktów, więc na poziomie 99 da się w pełni rozwinąć ok. 6–7 z 8 i gracz
  dalej wybiera styl. Poziomy odblokowania umiejętności (1, 5, 10, 12, 15, 20, 26, 34) i wybór ścieżki (10) bez zmian.
- **Skalowanie z rangą** (dla każdej umiejętności):
  - współczynnik obrażeń/leczenia: `coef × (1 + 0,08 × (rank − 1))` (ranga 10 = ×1,72, ranga 15 = ×2,12),
  - koszt MP: `mpCost × (1 + 0,06 × (rank − 1))`,
  - odnowienie: `cooldown × (1 − 0,02 × (rank − 1))` (ranga 15 = ×0,72),
  - czas trwania efektów: `+3%` na rangę.
- **Opłata treningowa** za każdą rangę (ujście talarów): `round5(8 × unlockLevel × rank^1.4)`.
- **Obrażenia umiejętności:** `Atk` lub `Mag` × współczynnik, dalej jak w formule obrażeń ([COMBAT_PHYSICS.md](COMBAT_PHYSICS.md) §1).
- **Typy obrażeń:** fizyczne, magiczne-żywioł (szaman), mrok (Slajter), trucizna (DoT).
- **Kontrola (ogłuszenie, unieruchomienie):** maleje przy powtórzeniu na tym samym celu (100% → 50% → 25%, reset po 15 s).
  Strażnicy (bossowie) mają połowę czasu kontroli.

## 3. Sterowanie umiejętnościami w AR/FPP

Istniejący prototyp FPP (#31) ma przyciski **ATAK** i **AURA** (dwie ręce na rękojeści + efekt na ostrzu).
Propozycja układu przycisków w AR:

| Przycisk | Działanie |
|---|---|
| ATAK | atak podstawowy bronią (tap), przytrzymanie = umiejętność ładowana klasy |
| AURA | **gest obu rąk** = wybrane przez gracza wzmocnienie klasy (pole `aura_gesture` w tabeli); domyślnie pierwsze wzmocnienie ścieżki |
| Umiejętność 1, 2 | dwa sloty na umiejętności aktywne, przypisywane w panelu postaci |
| BLOK / UNIK | wojownik z tarczą: przytrzymaj BLOK; ninja: przesunięcie palcem w bok; pozostali: krótki unik palcem z dłuższym odnowieniem |
| Mikstury 1, 2 | szybkie sloty `quick_1`, `quick_2` |

Gesty, które wymagają kamery (celowanie łukiem, stuknięcie w podłoże AR), działają tylko po wykryciu płaszczyzny.
**Żadna umiejętność nie wymaga chodzenia ani cofania się w AR** (bezpieczeństwo w realnym otoczeniu); „doskok”
i „odskok” przesuwają moba lub kamerę wirtualnie.

Uwaga o nazwie przycisku AURA: słowo „aura” jest ogólne, ale umiejętność „aura miecza” to nazwa znana z Metin2.
Dlatego żadna umiejętność w tej specyfikacji nie nazywa się „aura”; przycisk zostaje etykietą gestu (Q14).

Dodatkowe sterowania wymagają nowych animacji FPP (łuk, różdżka z totemem, sztylety w obu rękach, fokus mroku).
Obecne ręce i miecz z #36 pasują do wojownika i Slajtera — patrz plan etapów w [OPEN_QUESTIONS_PLAN.md](OPEN_QUESTIONS_PLAN.md).

## 4. Tabele umiejętności

## Wojownik (`warrior`) — walka wręcz, wytrzymałość

Broń: `sword_1h`, `sword_2h`, `axe_2h`; druga ręka: `shield`. Wybór ścieżki na poziomie 10.

### Umiejętności klasowe (wspólne dla obu ścieżek)

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Ciężkie Cięcie | `warrior_heavy_slash` | melee | single | 6 MP | 4 s | — | 160% Atk | przytrzymaj ATAK 0,6 s: mocniejsze cięcie | przytrzymaj ATAK |
| 5 | Wypad | `warrior_lunge` | dash | single | 10 MP | 10 s | — | 120% Atk | doskok do celu i zachwianie 0,6 s | przycisk umiejętności |

### Ścieżka Bastionu (`bulwark`) — obrona z tarczą, kontry

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Ściana Tarczy | `bulwark_shield_wall` | buff | self | 18 MP | 25 s | 8 s | — | +20% szansy bloku, −15% otrzymywanych obrażeń | AURA (gest obu rąk) |
| 12 | Riposta | `bulwark_riposte` | melee | single | 8 MP | 6 s | — | 180% Atk | BLOK w oknie 0,25 s przed trafieniem moba: zero obrażeń i kontra | BLOK w oknie czasu |
| 15 | Uderzenie Tarczą | `bulwark_shield_bash` | control | single | 14 MP | 12 s | 1.5 s | 120% Atk | ogłuszenie 1,5 s | przycisk umiejętności |
| 20 | Zaparcie o Ziemię | `bulwark_earthbrace` | aoe | circle_3m | 22 MP | 18 s | 4 s | 140% Atk | uderzenie w podłoże AR, spowolnienie wrogów o 30% | przycisk umiejętności |
| 26 | Żelazna Skóra | `bulwark_iron_hide` | buff | self | 30 MP | 45 s | 20 s | +25% Def | +25% obrony (skaluje się z rangą) | AURA (gest obu rąk) |
| 34 | Ostatni Szaniec | `bulwark_last_stand` | passive | self | 0 | 120 s | 4 s | 20% maks. HP | gdy HP < 25%: leczy 20% maks. HP i −50% obrażeń na 4 s | automatycznie |

### Ścieżka Rozłamu (`breaker`) — broń dwuręczna, obrażenia obszarowe

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Szeroki Łuk | `breaker_wide_arc` | aoe | cone_120_2.5m | 12 MP | 6 s | — | 130% Atk | poziome cięcie we wszystkich przed graczem | przycisk umiejętności |
| 12 | Gniew Kowadła | `breaker_anvil_fury` | melee | single | 16 MP | 10 s | — | 220% Atk | cios z góry, +15% szansy kryt.; zamach 1,0 s | przycisk umiejętności |
| 15 | Krwawy Rytm | `breaker_blood_rhythm` | buff | self | 20 MP | 30 s | 15 s | — | każde trafienie w ciągu 2 s: +4% szybkości ataku, maks. 5 stosów | AURA (gest obu rąk) |
| 20 | Rozpruwacz | `breaker_sunder` | debuff | single | 14 MP | 14 s | 8 s | 110% Atk | −20% obrony celu | przycisk umiejętności |
| 26 | Zapamiętanie | `breaker_frenzy` | buff | self | 25 MP | 40 s | 12 s | — | +25% zadawanych, +15% otrzymywanych obrażeń | AURA (gest obu rąk) |
| 34 | Wstrząs Ostrza | `breaker_quakeblade` | melee | single | 35 MP | 25 s | — | 300% Atk | dobicie: +50% obrażeń gdy cel ma < 30% HP | przycisk umiejętności |

## Ninja (`ninja`) — zwinny zabójca / łucznik

Broń: `dagger_pair`, `bow`; druga ręka: `quiver`. Wybór ścieżki na poziomie 10.

### Umiejętności klasowe (wspólne dla obu ścieżek)

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Zwód | `ninja_quick_feint` | dodge | self | 5 MP | 5 s | 0.4 s | — | przesunięcie palcem w bok: unik 100% przez 0,4 s | przesunięcie palcem |
| 5 | Gwiazdka | `ninja_throwing_star` | projectile | single | 6 MP | 3 s | — | 90% Atk | rzut z dystansu, przerywa zamach moba z szansą 30% | przycisk umiejętności |

### Ścieżka Cienia (`shade`) — sztylety, krytyki, ukrycie

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Cios w Splot | `shade_nerve_strike` | melee | single | 10 MP | 6 s | — | 150% Atk | +20% szansy kryt.; +30% obrażeń w oknie odsłonięcia po ataku moba | przycisk umiejętności |
| 12 | Zasłona Mgły | `shade_mist_veil` | buff | self | 18 MP | 25 s | 3 s | — | mob traci cel na 3 s; następny cios zawsze krytyczny | AURA (gest obu rąk) |
| 15 | Jad na Klindze | `shade_venom_edge` | buff | self | 16 MP | 30 s | 20 s | 30% Atk | trafienia nakładają truciznę: 30% Atk/s przez 5 s | AURA (gest obu rąk) |
| 20 | Taniec Klingi | `shade_blade_dance` | melee | single | 20 MP | 12 s | — | 60% Atk ×4 | 4 szybkie cięcia; dodatkowe +10% za każde trafienie w rytm stuknięć | stuknięcia w rytm |
| 26 | Piętno | `shade_death_mark` | debuff | single | 14 MP | 20 s | 10 s | — | cel otrzymuje +15% obrażeń krytycznych od gracza | przycisk umiejętności |
| 34 | Wyrok Cienia | `shade_shadow_execution` | melee | single | 30 MP | 30 s | — | 280% Atk | +100% obrażeń na celu ogłuszonym lub z Piętnem | przycisk umiejętności |

### Ścieżka Pustułki (`kestrel`) — łuk, celowanie kamerą, pułapki

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Strzał z Przyłożenia | `kestrel_aimed_shot` | projectile | single | 10 MP | 5 s | — | 200% Atk | naciąg 1,0 s; +25% gdy celownik na słabym punkcie | przytrzymaj + celuj kamerą |
| 12 | Wachlarz Strzał | `kestrel_arrow_fan` | aoe | cone_40_8m | 14 MP | 8 s | — | 80% Atk ×3 | 3 strzały w stożku | przycisk umiejętności |
| 15 | Wnyki | `kestrel_snare` | control | ground | 16 MP | 18 s | 3 s | 100% Atk | pułapka na wykrytej płaszczyźnie AR: unieruchomienie 3 s | stuknij w podłoże AR |
| 20 | Oko Pustułki | `kestrel_eye` | buff | self | 18 MP | 35 s | 15 s | — | +15 celności, +10% szansy kryt. | AURA (gest obu rąk) |
| 26 | Grot Zgnilizny | `kestrel_rot_arrow` | projectile | single | 15 MP | 12 s | 6 s | 60% Atk | trafienie + trucizna 25% Atk/s przez 6 s | przycisk umiejętności |
| 34 | Odlot | `kestrel_wing_retreat` | projectile | single | 20 MP | 20 s | 1 s | 180% Atk | wirtualny odskok (mob cofa się o 1 m) + strzał, unik 1 s | przycisk umiejętności |

## Szaman (`shaman`) — wsparcie, leczenie, magia żywiołów

Broń: `rune_wand`; druga ręka: `totem`. Wybór ścieżki na poziomie 10.

### Umiejętności klasowe (wspólne dla obu ścieżek)

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Dotyk Odnowy | `shaman_mending_touch` | heal | self | 10 MP | 6 s | — | 120% Mag | leczenie 120% Mag + 4% maks. HP | przycisk umiejętności |
| 5 | Świetlik | `shaman_firefly` | projectile | single | 6 MP | 2 s | — | 110% Mag | podstawowy pocisk magiczny | zastępuje ATAK |

### Ścieżka Korzenia (`rootward`) — leczenie, osłony, oczyszczanie

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Zielony Krąg | `rootward_greenring` | heal | ground_circle_2m | 24 MP | 30 s | 6 s | 8% maks. HP | krąg na podłożu AR: 8% maks. HP/s przez 6 s | stuknij w podłoże AR |
| 12 | Kora | `rootward_barkskin` | shield | self | 20 MP | 20 s | 10 s | 150% Mag | osłona pochłania 150% Mag obrażeń | AURA (gest obu rąk) |
| 15 | Oczyszczenie | `rootward_purge` | cleanse | self | 12 MP | 12 s | — | 50% Mag | zdejmuje truciznę i osłabienia, małe leczenie | przycisk umiejętności |
| 20 | Ciernie | `rootward_thornvines` | control | single | 16 MP | 16 s | 2.5 s | 80% Mag | unieruchomienie 2,5 s | przycisk umiejętności |
| 26 | Dar Wiosny | `rootward_spring_gift` | buff | self | 22 MP | 60 s | 120 s | 15% | +15% maks. HP i regeneracja 1% HP/s poza walką | AURA (gest obu rąk) |
| 34 | Drugi Oddech | `rootward_second_breath` | buff | self | 40 MP | 180 s | 30 s | 30% maks. HP | przez 30 s śmiertelny cios zostawia 1 HP i leczy 30% maks. HP | przycisk umiejętności |

### Ścieżka Nawałnicy (`tempest`) — ofensywna magia wiatru i piorunów

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Grom z Dłoni | `tempest_palm_thunder` | projectile | single_chain1 | 14 MP | 4 s | — | 160% Mag | przeskakuje na 1 dodatkowy cel (60%) | przycisk umiejętności |
| 12 | Podmuch | `tempest_gale` | control | cone_60_4m | 12 MP | 10 s | — | 90% Mag | odepchnięcie 1 m i przerwanie zamachu | przycisk umiejętności |
| 15 | Naładowanie | `tempest_static_charge` | buff | self | 16 MP | 25 s | — | 30% | 3 następne czary +30% | AURA (gest obu rąk) |
| 20 | Kula Burzy | `tempest_storm_orb` | aoe | circle_2m | 22 MP | 14 s | 3 s | 50% Mag ×6 | wolna kula, trafia co 0,5 s przez 3 s | przycisk umiejętności |
| 26 | Wiatr w Plecy | `tempest_tailwind` | buff | self | 18 MP | 40 s | 20 s | 15% | +15% szybkości rzucania i ataku | AURA (gest obu rąk) |
| 34 | Oko Cyklonu | `tempest_eye_of_storm` | aoe | circle_4m | 45 MP | 45 s | 2 s | 350% Mag | 350% Mag w 2 s i ogłuszenie 1 s | przycisk umiejętności |

## Slajter (`slajter`) — wojownik-mag mrocznej magii

Broń: `sword_1h`; druga ręka: `dark_focus`. Wybór ścieżki na poziomie 10.

### Umiejętności klasowe (wspólne dla obu ścieżek)

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Mroczne Pchnięcie | `slajter_gloom_thrust` | melee | single | 8 MP | 4 s | — | 120% Atk + 40% Mag | 120% Atk + 40% Mag jako obrażenia mroku | przycisk umiejętności |
| 5 | Wysysanie | `slajter_siphon` | drain | single | 12 MP | 8 s | — | 60% Mag | leczy 50% zadanych obrażeń | przycisk umiejętności |

### Ścieżka Popiołu (`ashblade`) — zaklęte ostrze, walka wręcz, kradzież życia

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Żar Klingi | `ashblade_ember_edge` | buff | self | 18 MP | 30 s | 20 s | 30% Mag | każde trafienie +30% Mag jako mrok | AURA (gest obu rąk) |
| 12 | Szept Trwogi | `ashblade_dread_whisper` | debuff | single | 12 MP | 16 s | 8 s | — | −20% ataku celu | przycisk umiejętności |
| 15 | Cena Krwi | `ashblade_blood_pact` | buff | self | 10% HP | 30 s | 10 s | 15% | płacisz 10% HP: +20% maks. MP natychmiast i +15% obrażeń | przycisk umiejętności |
| 20 | Skok Zjawy | `ashblade_wraith_leap` | dash | single | 20 MP | 14 s | 0.5 s | 170% Atk | doskok, niewrażliwość 0,5 s | przycisk umiejętności |
| 26 | Rozdarcie | `ashblade_rend` | melee | single | 18 MP | 12 s | 6 s | 100% Atk + 30% Mag | trafienie + mrok 30% Mag/s przez 6 s | przycisk umiejętności |
| 34 | Popielna Zawierucha | `ashblade_ashstorm` | aoe | circle_3m | 40 MP | 35 s | — | 160% Atk + 100% Mag | obszar 3 m wokół gracza | przycisk umiejętności |

### Ścieżka Zmierzchu (`duskcaller`) — mroczne czary z dystansu, klątwy, przyzwanie

| Poz. | Nazwa | Id | Rodzaj | Cel | Koszt (r1) | Odnowienie (r1) | Czas | Skalowanie (r1) | Działanie | Sterowanie AR/FPP |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 | Pocisk Zmierzchu | `duskcaller_dusk_bolt` | projectile | single | 12 MP | 3 s | — | 170% Mag | pocisk mroku | przycisk umiejętności |
| 12 | Urok Słabości | `duskcaller_hex` | debuff | single | 14 MP | 18 s | 12 s | — | −20% obrony, −15% odporności na mrok | przycisk umiejętności |
| 15 | Przymierze Zmroku | `duskcaller_dusk_pact` | buff | self | 20 MP | 40 s | 20 s | 20% | +20% Mag, podwójna regeneracja MP | AURA (gest obu rąk) |
| 20 | Okowy Mroku | `duskcaller_gloom_chains` | control | single | 16 MP | 15 s | 2 s | 90% Mag | unieruchomienie 2 s | przycisk umiejętności |
| 26 | Głodny Cień | `duskcaller_hungering_shade` | summon | single | 30 MP | 40 s | 8 s | 40% Mag | przyzwany cień zadaje 40% Mag/s przez 8 s | przycisk umiejętności |
| 34 | Zaćmienie | `duskcaller_eclipse` | projectile | single | 45 MP | 45 s | — | 400% Mag | rzucanie 1,5 s, przyciemnia scenę AR na czas czaru | przycisk umiejętności |
