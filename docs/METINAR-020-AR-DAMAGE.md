# METINAR-020 — S2a Obrażenia w AR: wojownik vs pająk (#47)

Zakres **S2a** wg MANAGER DECISION w #47 (2026-10-21 09:37Z): `HitDetected` z FPP → `DamageResolver` → HP moba →
śmierć → EXP i talary lokalnie; HUD 4 × 25% z ułamkiem; testy jednostkowe i test Play Mode z symulowanym trafieniem.
**S2b (test na A31) nie jest częścią tej zmiany** — wszystko, co dotyczy telefonu, jest `UNVERIFIED`.
Gałąź od `feat/046-gameplay-core` (PR #65, S1). Liczby: spec `docs/design/` (PR #44) i tabele `docs/design/tables/`.

## Przepływ

```text
FppCombatController.HitDetected(mob, swingId)   (#31, PR #32 — podpięcie po merge, patrz niżej)
  → ArEncounterCombat.HandleHitDetected(mob, swingId)          Unity, dodawany w ArEncounterSceneController.Awake
    → EncounterCombatSession.RegisterHit(swingId)              czysty C#, MetinAR.Gameplay.Combat
       → DamageResolver.Resolve (COMBAT_PHYSICS §1: trafienie → blok → rozrzut → krytyk → Def → odporność → min 1)
       → HP moba; 0 HP = śmierć → MobRewardCalculator (EXP + talary) → PlayerProgress (raz)
    → ArEncounterMob.ShowHp / ShowDefeated (etykieta „Pokonany”, ciało znika po ~1 s), toast w HUD AR, log [ArDamage]
  → LocalPlayerSession.Changed → WorldMapHud: „Lv N”, 4 kule EXP (wypełnienie ułamkowe od dołu), licznik talarów
```

| Plik | Co robi |
|---|---|
| `Scripts/Gameplay/Combat/EncounterCombatSession.cs` | jedna walka: 1 trafienie na zamach (`swingId`), HP, śmierć, nagroda dokładnie raz, statystyki atakującego z bieżącego poziomu |
| `Scripts/Gameplay/Combat/MobCombatProfile.cs` | moby z formuł MOBS_NPC §2 + mnożniki rang; Trujący Pająk poz. 5: HP 120, Atk 24, Def 15, słaby punkt +15 pp |
| `Scripts/Gameplay/Combat/MobRewardCalculator.cs` | EXP i talary za zabicie (zasady zaokrągleń niżej) |
| `Scripts/Gameplay/Combat/PlayerProgress.cs` | suma EXP → poziom (maks. 99), talary (`long`), kule HUD 0…4 ułamkowo |
| `Scripts/Gameplay/Combat/StarterLoadout.cs` | atakujący S2: wojownik z Mieczem Rekruta (`atk=6`, `items_starter.csv`), bez rozdanych punktów (S6) |
| `Scripts/Progression/LocalPlayerSession.cs` | postęp gracza w pamięci na czas sesji aplikacji (bez zapisu na dysk i bez wysyłania; serwer w S11/#39) |
| `Scripts/ArEncounter/ArEncounterCombat.cs` | most Unity: zdarzenie trafienia → sesja → etykieta moba, toast, log |
| `Scripts/ArEncounter/ArEncounterMob.cs` | `ShowHp`, `ShowDefeated` |
| `Scripts/UI/WorldMapHud.cs` | poziom, kule EXP z ułamkiem, licznik talarów (tworzony w runtime na monecie, bez zmiany sceny) |
| `Editor/ArDamageSimulatedHitTest.cs` | test Play Mode z symulowanymi trafieniami (wynik `Builds/metinar-020-ar-damage-results.xml`) |

Przykład (sprawdzony w testach): wojownik poz. 1 (Atk 11,3, Acc 74) vs pająk poz. 5: szansa trafienia 99%,
`Mitigation = 15/73`, zwykły cios 9 → **14 ciosów** bez krytyków (~10 s przy zamachu 0,72 s; cel specu dla zwykłego
moba 6–10 s), nagroda **101 EXP** (84 × 1,20 za +4 poziomy) i **10–18 talarów**; w HUD 101/160 = **2,525 z 4 kul**.

## Decyzje S2 w otwartych pytaniach z PR #65 (zasada „domyślne propozycje specu”, opis w #41)

1. **Unik/celność mobów:** tabela mobów ich nie ma, więc mob używa formuł postaci (CHARACTER_PROGRESSION §1.3) z zerowymi
   statystykami głównymi i bez przedmiotów: `Eva = 5`, `Acc = 70 + m` (`MobRules.Evasion`, `AccuracyBase`,
   `AccuracyPerLevel`). Szansa trafienia gracza zostaje na limicie 99% dla mobów do +1 poziomu (strefy −2…+1, Q12),
   tak jak zakłada `sanity_warrior_ttk.csv` (TTK bez pudeł). `Acc` moba użyją ataki moba w S3.
2. **Zaokrąglanie nagród:** jedno zaokrąglenie na końcu, jak w generatorze tabel (do najbliższej, remis do parzystej):
   `EXP = max(1, round(MobExp(m) × ranga × różnica poziomów))`; talary = losowa liczba całkowita z przedziału tabeli
   `goldMin…goldMax` (włącznie, równy rozkład) × ranga, `max(1, round(...))`. Modyfikator różnicy poziomów dotyczy tylko
   EXP (§2.2 „EXP z mobów”), ranga — EXP i talarów (×1/×3/×10; przykład ECONOMY §2: elitarny poz. 5 = 42).
   HP/Atk rang: `round5(HP × mnożnik)`, `round(Atk × mnożnik)`.
3. **Obrona Slajtera (część Mag w obrażeniach hybrydowych):** do implementacji w S8. Domyślnie wg reguły specu dla
   obrażeń magicznych: część `Mag × magCoef` liczona z połową `Def`, część fizyczna z pełnym `Def`, suma i jedno
   zaokrąglenie. S2 tego nie koduje.
4. **Kunszt a TTK:** obowiązuje `skill_tiers.csv` (zatwierdzone stopnie, Kunszt III od poz. 72, V od 84);
   `sanity_warrior_ttk.csv` to tylko kontrola skali (wiersze 70/80 zakładają stopień o 2–4 poziomy wcześniej, różnica
   dotyczy wyłącznie Ciężkiego Cięcia, nie ataku podstawowego). Bez zmian liczb w S2; tabelę kontrolną poprawi S12.

Żadna z decyzji nie zmienia odczucia gry poza specem, więc nie idą do właściciela.

## Poza zakresem S2a

- **S2b:** test na A31 (logcat `[ArDamage]`, nagranie/zrzut) po zamknięciu #31 i w kolejce A31 w #33 — `UNVERIFIED`.
- **Podpięcie FPP:** `FppCombatController` jest w PR #32 (#31, jeszcze nie na `main`). Po jego merge'u wystarczy jedna linia
  w `ArEncounterSceneController.Start`: `FppCombat.HitDetected += Combat.HandleHitDetected;` (podpis zgodny:
  `Action<ArEncounterMob, int>`). Do tego czasu trafienia dostarcza test (symulacja) — w buildzie bez #31 nie ma ataku.
- Ataki moba, HP gracza, kara za śmierć (S3), łup (S4), rozdawanie punktów (S6), zapis postępu i autorytet serwera (S11).
- Trafienie w słaby punkt: sesja to obsługuje (`weakPoint`), ale `HitDetected` z #31 nie podaje jeszcze strefy trafienia.

## Weryfikacja

- `dotnet test tests/MetinAR.Gameplay.Tests/MetinAR.Gameplay.Tests.csproj` (CI `gameplay-core-tests`).
- Unity 6000.6.4f1 batchmode na LuzMen (LOCAL SLOT #33): kompilacja, potem
  `-executeMethod MetinAR.Editor.ArDamageSimulatedHitTest.Run` (11 testów Play Mode, JUnit XML).
