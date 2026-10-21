# METINAR-017 — Walka i fizyka

> **Status: PROPOZYCJA do akceptacji właściciela (#40). Bez implementacji.**
> Liczby to **proponowane wartości startowe do balansowania**. Czasy FPP pochodzą z prototypu #31 (wartości techniczne,
> nie balans). Indeks: [README.md](README.md).

## 1. Formuła obrażeń

Kolejność rozstrzygania jednego trafienia (`DamageResolver`, czysty C# bez zależności od Unity, łatwy do testów):

1. **Trafienie / pudło**
   `HitChance = clamp(0,92 + (Acc − Eva_celu) × 0,004 − max(0, L_celu − L_atak) × 0,02, 0,40, 0,99)`.
   W AR/FPP geometria ma pierwszeństwo: jeśli ostrze **fizycznie nie przecięło** bryły trafienia, to pudło bez rzutu.
   Rzut `HitChance` dotyczy tylko trafień geometrycznych (symuluje unik moba). Umiejętności z kontrolą tłumu nie pudłują.
2. **Blok** (tylko cel z tarczą i atak od przodu): `BlockChance` (limit 35%), zablokowany cios = −60% obrażeń,
   **Riposta** w oknie czasu = −100%.
3. **Surowe obrażenia:** `Raw = (Atk lub Mag) × coef + flat`; atak podstawowy `coef = 1,0`.
4. **Rozrzut:** `× U(0,92; 1,08)`.
5. **Krytyk:** szansa `CritChance` (+ bonus słabego punktu), mnożnik `CritMult` (limit 2,2).
6. **Obrona (malejące zyski):** `Mitigation = Def / (Def + 50 + 8 × L_atakującego)`; obrażenia × `(1 − Mitigation)`.
   Dla obrażeń magicznych używamy połowy `Def` + odporności.
7. **Odporności:** × `(1 − Res_typ)`, limit 50%.
8. **Modyfikatory:** wzmocnienia/osłabienia (sumowane w grupie, mnożone między grupami), bonus vs rodzina moba.
9. **Minimum 1**, zaokrąglenie do liczby całkowitej.

Sprawdzenie skali (wojownik, sprzęt bez rzadkości do poz. 10): mob na tym samym poziomie pada po
**4–6 trafieniach na poz. 1–10** i **8–10 na poz. 30–60** ([tables/sanity_warrior_ttk.csv](tables/sanity_warrior_ttk.csv)).
Cel czasu walki: zwykły 6–10 s, elitarny 20–30 s, strażnik 60–90 s, żeby nie męczyć ręki trzymającej telefon.

Trucizna i inne DoT: tik co 1 s, bez krytyków, bez rzutu na trafienie, ten sam typ nie sumuje się (odświeża czas).

## 2. Bryły trafienia (hitboxy) i kolizja miecza w AR/FPP

Zgodnie z prototypem FPP (#31, `FppHitGeometry`, `FppCombatTimeline`):

- **Ostrze** = odcinek od `y = 0,13` do `0,94 m` w układzie rękojeści, pozycja z tej samej pozy co render.
- **Okno trafienia** ataku podstawowego 0,18–0,42 s z 0,72 s zamachu; próbkowanie co ≤ 10 ms; **1 trafienie na cel na zamach**.
- **Mob** dostaje dane `CombatHitVolume` w prefabie zamiast stałej bryły pająka:
  - `Body`: 1–3 prostopadłościany/kapsuły (tolerancja 2,5 cm jak dziś),
  - `WeakPoint`: opcjonalna kula (bonus do krytyka),
  - `Ground anchor`: punkt kontaktu z płaszczyzną AR.
  Dane trzymane w prefabie moba (pipeline METINAR-009 dostanie kontrolę ich obecności).
- **Pociski i czary** (łuk, różdżka, fokus): kierunek = środek ekranu / oś kamery; **pomoc celowania** do 8° w stronę
  środka `Body`; test kulą o promieniu 0,10 m wzdłuż toru. Pociski mają prędkość 12–25 m/s (widoczny lot).
- **Obszarowe** (stożek, okrąg): test geometryczny na płaszczyźnie AR względem pozycji kamery.
- **Ataki moba** nie używają fizyki Unity: trafienie gracza rozstrzyga zdarzenie animacji `Attack` + stan gracza
  (blok, unik, niewrażliwość). Gracz nie ma bryły kolizji — jest „kamerą”.
- Utrata śledzenia AR, przerwa klatki > 0,25 s lub skok kamery > 0,25 m / 35° anuluje oczekujące akcje (jak dziś);
  ataki moba w tym czasie też są wstrzymane (gracz nie traci HP, gdy AR nie działa).

## 3. Reakcje na trafienie i „ragdoll-light”

Pełny ragdoll fizyczny jest **za drogi na A31**. Propozycja:

| Zdarzenie | Reakcja |
|---|---|
| Trafienie zwykłe | klip `Hit` (addytywny, jeśli możliwy) + błysk materiału 80 ms + liczba obrażeń |
| Krytyk | jw. + odrzut proceduralny 0,05–0,15 m wzdłuż kierunku ciosu, wygaszany w 0,25 s, mob zostaje na płaszczyźnie |
| Odrzut z umiejętności | 0,3–1,0 m, krzywa wygaszania, ograniczenie do obszaru wykrytego podłoża |
| Ogłuszenie | klip `Stun` (opcjonalny w pipeline) lub zamrożenie Idle + efekt nad głową |
| Śmierć | klip `Death` → 1 s rozpuszczenia (dissolve) → wypadnięcie łupu jako ikona/promień światła |

Wszystko liczone w kodzie (bez `Rigidbody`), deterministycznie, żeby serwer mógł kiedyś odtworzyć wynik.

## 4. Czas wejścia dotykowego i AR

| Wejście | Zasada |
|---|---|
| Tap ATAK | zaczyna zamach; tap w trakcie zamachu jest **buforowany**, jeśli padł w ostatnich 0,15 s fazy powrotu, inaczej ignorowany (anty-spam jak w #31) |
| Przytrzymanie ATAK | ≥ 0,6 s = umiejętność ładowana klasy; puszczenie wcześniej = zwykły atak |
| AURA | gest obu rąk 1,15 s + aktywacja 0,65 s (wartości z #31); w tym czasie brak ataku |
| BLOK | przytrzymanie; Riposta, jeśli BLOK zaczął się ≤ 0,25 s przed klatką trafienia moba |
| Unik (swipe) | poziomy ruch palca ≥ 12% szerokości ekranu w ≤ 0,25 s |
| Umiejętności | tap = rzut natychmiast lub tryb celowania (stuknięcie w podłoże / przytrzymanie dla łuku) |
| Globalne odnowienie | 0,5 s między umiejętnościami (nie dotyczy ataku podstawowego i mikstur) |

Haptyka (krótkie drgnięcie przy trafieniu / bloku) — propozycja, do sprawdzenia na A31.

## 5. Zdarzenia dźwiękowe

Nazwy zgodne z PR #43 (pipeline audio). Istnieją: `SFX_Combat_SwordSwing_01`, `SFX_Combat_SwordHit_01`,
`SFX_Combat_ShieldBlock_01`, `SFX_Mob_VenomousSpider_Attack_01/Hit_01/Death_01/Step_01..03`, `SFX_UI_Click_01`.
Potrzebne nowe (dla bota Dźwięk i muzyka, po akceptacji):

| Zdarzenie gry | Plik (nazwa robocza) |
|---|---|
| Pudło / świst bez trafienia | `SFX_Combat_Whiff_01` |
| Krytyk | `SFX_Combat_Crit_01` |
| Riposta | `SFX_Combat_Parry_01` |
| Unik | `SFX_Combat_Dodge_01` |
| Telegraf moba (ogólny) | `SFX_Mob_Telegraph_01` |
| Rzucanie czaru: żywioł / mrok / leczenie | `SFX_Skill_CastNature_01`, `SFX_Skill_CastDark_01`, `SFX_Skill_Heal_01` |
| Strzał z łuku / trafienie strzałą | `SFX_Combat_BowRelease_01`, `SFX_Combat_ArrowHit_01` |
| Trucizna tik | `SFX_Status_PoisonTick_01` |
| Awans | `SFX_UI_LevelUp_01` |
| Podniesienie talarów | `SFX_UI_CoinPickup_01` |
| Łup wg rzadkości | `SFX_Loot_Common_01` … `SFX_Loot_Legendary_01` |
| Hartowanie udane / nieudane | `SFX_Smith_TemperSuccess_01`, `SFX_Smith_TemperFail_01` |
| Mikstura | `SFX_Item_Potion_01` |

## 6. Budżet wydajności Samsung Galaxy A31 (walka AR)

Odwołanie: `tools/qa/perf-budgets.json` (scena łączona AR + ręce + miecz: min. 20 FPS, p95 klatki ≤ 66 ms,
main thread p95 ≤ 50 ms). Propozycja limitów zawartości walki:

| Pozycja | Limit v1 |
|---|---|
| Moby w spotkaniu AR | **1** (v1), docelowo maks. 3 tylko po pomiarze |
| Skinned meshe aktywne | mob(y) + ręce FPP (maks. 4) |
| Logika walki (AI, rozstrzyganie, statusy) | ≤ 1,5 ms / klatkę main thread |
| Cząsteczki | ≤ 300 żywych, ≤ 4 systemy naraz, bez kolizji cząsteczek, bez świateł w cząsteczkach |
| Cienie | tylko „blob” pod mobem, bez cieni w czasie rzeczywistym |
| Głosy audio | ≤ 12 jednocześnie, SFX mono |
| Liczby obrażeń / UI świata | pula obiektów, maks. 8 naraz, bez alokacji w walce |
| Fizyka Unity | brak `Rigidbody` w walce; `FixedUpdate` bez logiki walki |
| Alokacje GC w walce | 0 B na klatkę w stanie ustalonym (cel) |

Pomiar wydajności na A31 jest wymagany przed zwiększeniem limitów. Wszystko powyżej: **UNVERIFIED** do testu na telefonie.
