# Specyfikacja właściciela 2026-10-23 03:37 — STRESZCZENIE MENEDŻERA (wcześniejsze, zastąpione przez #96)

> **WCZEŚNIEJSZA WERSJA — ZASTĄPIONA.** Właściciel przysłał o 03:48 i zatwierdził o 03:50 główną specyfikację
> „METINAR 2026 — GŁÓWNA SPECYFIKACJA GRY I POLECENIE DLA KIEROWNIKA PRODUKCJI” (§1–20), dosłownie w PR #96:
> https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/96 (plik `docs/design/OWNER_SPEC_2026-10-23.md`, commit `2bb0a72`).
> **Tam, gdzie ten plik różni się od głównej specyfikacji, rozstrzyga główna specyfikacja** (np. pętla ETAP I ma 22 kroki,
> nie 26; Aura ma 9 kroków, nie 7; etapy I–V; dźwięk, reakcje i ataki potworów oraz dopracowanie HUD należą do ETAPU II).
> Ten plik zostaje wyłącznie jako zapis wcześniejszej wiadomości właściciela z 03:37 (szczegóły, których główna specyfikacja
> nie zmienia). Lista różnic: [SPEC_CONFLICTS_2026-10-23.md](SPEC_CONFLICTS_2026-10-23.md) §0.
>
> **Status:** wiążąca decyzja właściciela (czat, 2026-10-23 03:37, „METINAR / METIN2AR — PEŁNA SPECYFIKACJA GRY I POLECENIE PRODUKCYJNE DLA GROKA”, sekcje 1–24).
> **To NIE jest tekst dosłowny.** To wierne streszczenie spisane przez kierownika produkcji.
> Nadrzędna wobec starszych dokumentów, m.in. założenia o śledzeniu prawdziwej dłoni w `docs/PROJECT_BRIEF.md`.
> Cytaty w cudzysłowach są dosłowne.

## Zasady obowiązkowe
1. Zachowaj istniejący poprawny kod.
2. Przed zleceniem zadań sprawdź rzeczywisty stan repo.
3. Nie zastępuj systemów niekompatybilnymi wersjami.
4. Nie przedstawiaj prototypu jako produktu finalnego.
5. Nie proś ponownie o zatwierdzenie podjętych decyzji.
6. Dziel pracę tak, żeby boty pracowały równolegle.
7. Błędy zgłaszaj od razu, z przyczyną i propozycją naprawy.
8. Nic nie jest gotowe bez właściwych testów.
9. „Nie używaj nielegalnie skopiowanych zasobów Metin2.”
10. Głównym wynikiem jest działająca gra na fizycznym telefonie.

## Wizja i tryby
Mapa świata oparta na prawdziwym GPS, z potworami, NPC i POI. Gracz dochodzi do bezpiecznego punktu spotkania, walczy w AR w widoku z pierwszej osoby, a nagrody zapisują się w postaci.

- **Tryb A, mapa:** GPS, obrót i zoom, awatar, ikony, odległość, nazwa, poziom i agresywność, strefy bezpieczne, panele. Bez joysticka. Tryb symulacji deweloperskiej oddzielony od nagród produkcyjnych.
- **Tryb B, AR/FPP:** kamera, potwór zakotwiczony w przestrzeni, prawa dłoń z bronią, lewa dłoń lub tarcza, przedramiona, VFX, paski HP i MP, przyciski. Brak śledzenia prawdziwej dłoni (zastępuje założenie z PROJECT_BRIEF).

## Grafika i FPP
- Styl mrocznego orientalnego fantasy, stylizowane tekstury malowane ręcznie.
- Pełny miecz (ostrze, jelec, rękojeść, głowica, metal) i opancerzone rękawice.
- Bryły prymitywne nigdy nie przechodzą odbioru wizualnego. Concept art się nie liczy. Zasoby muszą być oryginalne.
- Układ FPP: oddech w spoczynku, miecz po prawej, wolny środek ekranu, kadr na pionowy ekran, profesjonalny rig.
- **1H:** prawa dłoń trzyma miecz, lewa ma tarczę i wykonuje gesty.
- **2H:** obie dłonie, ciężej i wolniej, bez tarczy.
- Zmiana ekwipunku zmienia model, układ dłoni, animacje, zasięg, obrażenia i dźwięki.

## Combo (#82)
- **1H:** cios 1 L→P, cios 2 P→L, cios 3 finisher z lewej albo z góry. Każdy cios ma własną animację, dźwięk, okno trafienia i reakcję moba.
- **2H:** te same trzy ciosy z zamachem, szerszym łukiem, silniejszą reakcją oraz własnym zasięgiem i obrażeniami.
- Tapnięcie w oknie kontynuuje combo, brak tapnięcia wraca do ciosu 1, a combo kończy się po ciosie 3.
- Przytrzymanie ATAK uruchamia umiejętność ładowaną, jeśli jest odblokowana i przypisana, bez ciosów combo.
- Czasy i mnożniki są w konfiguracji.
- Trafienie liczy się tylko z rzeczywistej pozycji ostrza, maks. 1 trafienie na zamach na cel.

## Aura
Sekwencja 7 kroków: lewa dłoń do rękojeści, chwyt oburącz bez przenikania, impuls energii (błysk, fala, cząsteczki, światło, dźwięk), trwały niebiesko-złoty efekt na ostrzu, powrót do postawy. Bez obrażeń przy aktywacji.

## Klasy, umiejętności, rozwój
- Cztery klasy: Wojownik (Bastion, Rozłam), Ninja (Cień, Pustułka), Szaman (Korzeń, Nawałnica), Slajter (Popiół, Zmierzch). Nazw nie zmieniamy.
- 14 umiejętności na klasę, razem 56. Wybór ścieżki na 10 poziomie.
- Stopnie: Wprawa → Biegłość → Kunszt → Szczyt (Traktat Biegłości, Iskra Kunsztu, Pieczęć Szczytu), wg CLASSES_SKILLS.md.
- Maks. poziom 99. Statystyki Moc, Zwinność, Duch, Krzepa, 2 wolne punkty na poziom. Około 250 h do 99.
- EXP za chodzenie: limit 8 km dziennie, z anty-cheatem. Śmierć kosztuje 2% EXP bieżącego poziomu.

## Ekwipunek i przedmioty
- 10 slotów ekwipunku, 2 sloty mikstur, plecak 24 sloty rozszerzalny do 56.
- 5 rzadkości. Dropy: talary i przedmioty, łup osobisty, schowek łupu.
- Kowal: pełny NPC 3D z animacjami, ulepszanie +1 do +10 bez niszczenia przy porażce.
- Wędrowny Kupiec. Bez handlu między graczami w v1.

## Potwory
- Pełne modele. Trujący Pająk: poziom 5, 120 HP, 6 animacji i trucizna.
- Elity, bossowie i Runiczne Monolity później.

## Bezpieczeństwo GPS
- Wykluczenia: drogi, tory, woda, budynki, tereny prywatne i przemysłowe, z buforami i ostrożną walidacją.
- Nigdy nie wymagaj chodzenia podczas walki AR.
- Zabezpieczenia GPS wg CHARACTER_PROGRESSION.md.
- Lokalizacja tylko na pierwszym planie. „Nie dodawaj śledzenia w tle bez odrębnej decyzji produktowej i prawidłowego rozwiązania kwestii prywatności.”

## UI i audio
- **UI:** język polski, ciemne panele, HUD walki wg specyfikacji, panel postaci z 7 zakładkami, bez atrap przycisków.
- **Audio:** podpięte do prawdziwych zdarzeń. Osobna głośność SFX, muzyki i UI. Wibracje z możliwością wyłączenia. Sprawdzone licencje.

## Architektura
- Nie zaczynamy od zera. Systemy klienta: WorldMap, Location, AR Encounter, FPP Combat, Gameplay, Inventory, Progression, UI, Audio, Save/Sync, Networking.
- Obsługa utraty AR, wznawiania, braku internetu, odmowy uprawnień i nieobsługiwanych urządzeń.
- Backend jest prototypem. Nagrody nie mogą opierać się wyłącznie na deklaracjach klienta.
- Zapis OBOWIĄZKOWY: musi przetrwać zamknięcie aplikacji, restart, ponowne uruchomienie telefonu i ponowne logowanie.
- „Nie wystawiaj serwera developerskiego do internetu bez zabezpieczeń.”
- v1 tylko PvE.
- „Nie dodawaj systemów premium, sklepu za prawdziwe pieniądze ani płatności bez osobnej decyzji właściciela.”

## Pipeline zasobów i wydajność
- Foldery: Characters, Weapons, Animations, Materials, Textures.
- Każdy model wymaga: topologii, UV, tekstur, rigu, klipów, punktów mocowania, skali, prefabu, LOD i licencji.
- Kowal: 10–20 tys. trójkątów, tekstury 2K, LOD0/1/2.
- 30 FPS na A31 podczas walki, łącznie z combo. iOS NIEZWERYFIKOWANY do czasu testu.

## Obowiązkowy test na fizycznym telefonie (26 kroków)
1. Uruchom aplikację.
2. Utwórz lub wczytaj wojownika.
3. Otwórz mapę świata.
4. Pobierz prawdziwą pozycję GPS.
5. Pokaż potwora w bezpiecznym miejscu.
6. Pokaż nazwę, poziom i odległość.
7. Sprawdź możliwość wejścia do AR.
8. Uruchom kamerę.
9. Wykryj podłoże.
10. Stabilnie zakotwicz model potwora.
11. Pokaż wirtualne dłonie i miecz.
12. Wykonaj pełne combo 1H.
13. Aktywuj Aurę.
14. Pokaż efekt magiczny na broni.
15. Sprawdź prawdziwe obrażenia.
16. Przyjmij atak potwora.
17. Pokonaj przeciwnika.
18. Przyznaj EXP i talary.
19. Wylosuj przedmiot.
20. Dodaj go do ekwipunku.
21. Załóż nową broń.
22. Sprawdź zmianę modelu i statystyk.
23. Wróć na mapę GPS.
24. Zamknij aplikację.
25. Uruchom ją ponownie.
26. Potwierdź zachowanie poziomu, waluty i przedmiotów.

Dowody dla każdego testu: commit, ID buildu, PASS/FAIL, logi, wideo lub zrzuty ekranu, problemy i kto je naprawia. „Nie oznaczaj testu jako PASS, jeśli został uruchomiony wyłącznie na symulatorze lub w testach czystego C#.”

## Etapy
1. **Fundament:** aplikacja Android, mapa, GPS, stabilne AR, zakotwiczony mob, dłonie i miecz, prawdziwe obrażenia, śmierć, nagrody, ekwipunek, trwały zapis, podstawowe dźwięki, wszystko działające razem.
2. **Jakość walki wojownika:** finalne dłonie i miecze, rig, combo 1H i 2H, Aura, blok tarczą, reakcje na trafienie, VFX, umiejętności wojownika.
3. RPG.
4. Świat.
5. Klasy w kolejności Wojownik, Slajter, Ninja, Szaman.
6. Wydanie. „Publikacja produkcyjna wymaga mojej osobnej akceptacji.”

## Role botów
- Kierownik: plan, przydziały, zależności, kolejność integracji.
- Developer: logika gry, integracja Unity, walka, postać, UI.
- Zespół 3D: modele, rig, animacje, VFX.
- Backend: API, zapis, konta i synchronizacja, integralność danych.
- QA: testy i odbiór.
- Pozostali według kompetencji: mapa/GPS, gameplay, audio, UI albo integracja.
- Osobne gałęzie i PR. Jeden integrator dla wspólnych obszarów.

## #82, sekcja 21, źródła i polecenie
- **#82:** kierunek ROZSTRZYGNIĘTY. „Nie przygotowuj ponownie dziesięciu pytań dotyczących tych samych podstaw.” Logika combo i testy mogą ruszyć od razu.
  - „Wcześniejsze odrębne blokady, w tym HOLD dla #30, pozostają w mocy do ich wyraźnego zniesienia.”
  - Bez kopiowania zewnętrznych modeli. Odbioru wizualnego się nie pomija.
- **Sekcja 21, obecnie nieakceptowalne:**
  1. brak audio;
  2. brak połączenia klienta z serwerem;
  3. brak trwałego zapisu;
  4. testy przedstawiane jako dowód gotowości;
  5. słabe modele i animacje.

  Każdy punkt sprawdzić na najnowszym commicie. Jeśli naprawiony, pokazać dowód. Jeśli nie, przydzielić zadanie.
- **Źródła prawdy:** docs/PROJECT_BRIEF.md, METINAR-FPP-001.md, design/README, OPEN_QUESTIONS_PLAN, CHARACTER_PROGRESSION, CLASSES_SKILLS, ITEMS_COMBAT_SPEC, COMBAT_PHYSICS, MOBS_NPC, ECONOMY, MODES_DATA_MODEL, qa/DEVICE-ACCEPTANCE-LOOP-V0.md. Nowsza, jednoznaczna decyzja właściciela wygrywa, a dokumenty się aktualizuje.
- **Sekcja 24, co robić teraz:**
  - pobrać main i otwarte PR, porównać ze specyfikacją;
  - ustalić, co naprawdę działa, co częściowo, a czego brak;
  - oddzielić krytyczne problemy od jakości i nowej zawartości;
  - zadania równoległe dla 7 botów;
  - najpierw działająca pętla na telefonie, potem weryfikacja na urządzeniu;
  - wszystko dokumentować commitami, PR i testami;
  - właścicielowi raportować tylko kluczowe decyzje, krytyczne blokady i realny postęp.

  „Rozpocznij od audytu aktualnego stanu i przygotowania konkretnych zadań implementacyjnych dla zespołu. Nie czekaj z niezależnymi, już zatwierdzonymi pracami na zakończenie dyskusji o elementach, które mogą być realizowane później.”
