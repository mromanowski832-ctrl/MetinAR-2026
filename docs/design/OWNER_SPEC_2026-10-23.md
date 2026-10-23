METINAR 2026 — GŁÓWNA SPECYFIKACJA GRY I POLECENIE DLA KIEROWNIKA PRODUKCJI

Do: Grok — Kierownik produkcji MetinAR oraz wszystkich 7 agentów

Repozytorium: https://github.com/mromanowski832-ctrl/MetinAR-2026

Priorytet: P0 — doprowadzenie gry do rzeczywiście grywalnej wersji na telefonie.

1. WIZJA GRY — CZEGO OCZEKUJE WŁAŚCICIEL

MetinAR to mobilna gra MMORPG inspirowana klasycznym Metin2, łącząca jego klimat i rozwój postaci z eksploracją rzeczywistego świata, geolokalizacją GPS i walką w rozszerzonej rzeczywistości.

Nie tworzymy:

• Symulatora uderzania jednego pająka.
• Aplikacji demonstracyjnej z kilkoma przyciskami.
• Gry złożonej z kolorowych kostek, kul i walców.
• Samej mapy GPS z ikonami.
• Zestawu niezależnych modułów, których nie da się używać jako jednej gry.

Tworzymy:

• Pełnoprawne RPG rozwijane w kierunku MMORPG.
• Rozbudowane postacie, klasy, umiejętności i ekwipunek.
• Potwory, bossów, NPC, zadania i świat fantasy.
• Dynamiczną walkę z mieczem i animacjami.
• System EXP, poziomów 1–99 i rozwoju umiejętności.
• Kowala, sklepy, ulepszanie przedmiotów i drop.
• Mapę rzeczywistego świata z potworami i NPC.
• Walkę AR w widoku pierwszoosobowym.
• Trwały zapis postaci i ekonomię kontrolowaną przez serwer.

Gracz ma odczuwać klimat klasycznego orientalnego MMORPG, a nie technicznego prototypu.

Zachowujemy własne modele, tekstury, animacje, efekty, nazwy i kod. Nie kopiujemy chronionych zasobów Metin2.

2. DWIE PODSTAWOWE CZĘŚCI GRY

A. Tryb świata — GPS

Prawdziwy świat jest mapą gry.

Gracz:

1. Uruchamia grę.
2. Wybiera lub tworzy postać.
3. Widzi mapę rzeczywistej okolicy.
4. Widzi własną pozycję i awatara.
5. Odkrywa potwory, NPC, zadania i punkty zainteresowania.
6. Otrzymuje komunikat, np. „Trujący Pająk — 50 m”.
7. Podchodzi do przeciwnika.
8. Po wejściu w dozwolony zasięg może rozpocząć walkę AR.
9. Po walce otrzymuje EXP, talary i przedmioty.
10. Wraca na mapę i kontynuuje eksplorację.

Mapa musi mieć:

• Wskaźnik gracza i kierunku poruszania.
• Stylizowane oznaczenia potworów.
• Poziomy i kategorie przeciwników.
• Ikony NPC i punktów zadań.
• Odległości do obiektów.
• Informacje o dostępnym spotkaniu AR.
• Menu postaci, ekwipunku, umiejętności i zadań.
• Informacje o wydarzeniach w okolicy.

Moby pojawiają się dynamicznie. NPC mogą mieć stałe lokalizacje.

Bezpieczeństwo GPS jest obowiązkowe.

Nie umieszczamy spotkań na drogach, torach, wodzie, w niebezpiecznych miejscach ani na niedostępnych terenach prywatnych. Walka nie może wymagać chodzenia, biegania lub cofania się z telefonem przed oczami.

B. Tryb walki — AR/FPP

Po rozpoczęciu spotkania kamera telefonu pokazuje rzeczywiste otoczenie.

Na tym obrazie pojawiają się:

• Potwór 3D stojący na wykrytym podłożu.
• Wirtualne dłonie i przedramiona postaci.
• Broń trzymana w dłoniach.
• Efekty umiejętności.
• Interfejs HP, MP i walki.
• Liczby obrażeń i reakcje przeciwnika.

Potwór pozostaje zakotwiczony w świecie AR. Nie może przesuwać się razem z kamerą.

Najważniejsze: gracz nie musi pokazywać swoich prawdziwych dłoni kamerze.

Sterowanie odbywa się przyciskami dotykowymi. Ruchy wykonują animowane wirtualne dłonie postaci.

To jest zatwierdzony kierunek projektu.

3. GRAFIKA — WYMAGANY STANDARD

Styl graficzny: orientalne dark fantasy MMORPG, inspirowane atmosferą klasycznych gier fantasy z początku XXI wieku, ale wykonane w jakości odpowiedniej dla współczesnej gry mobilnej.

Wymagania:

• Profesjonalnie wyglądające modele 3D.
• Czytelne sylwetki postaci i potworów.
• Tekstury fantasy, hand-painted lub stylizowane PBR.
• Zbroje, rękawice, broń i wyposażenie z detalami.
• Poprawne materiały metalu, skóry i tkanin.
• Animacje ze szkieletami.
• Efekty ostrzy, magii, ognia, energii i trucizny.
• Czytelne cienie oraz efekty kontaktu z podłożem.
• Optymalizacja modeli pod telefony.
• LOD i kontrola zużycia pamięci.

Nie akceptuję prostych geometrycznych brył jako docelowych modeli postaci i potworów.

Blockouty mogą służyć wyłącznie do programowania i testów. Muszą być oznaczone jako tymczasowe.

Modele postaci

Każda postać wymaga:

• Modelu bazowego 3D.
• Rigu.
• Animacji bezczynności.
• Animacji walki.
• Animacji umiejętności.
• Animacji otrzymania obrażeń.
• Animacji śmierci, jeśli dotyczy.
• Systemu podmieniania widocznego uzbrojenia.

Potwory

Potwory muszą mieć:

• Wygląd charakterystyczny dla swojej rodziny.
• Animację bezczynności.
• Chód lub inny sposób przemieszczania.
• Atak.
• Reakcję na trafienie.
• Śmierć.
• Efekty specjalne właściwe dla typu przeciwnika.

Każdy istotny efekt wizualny powinien być zsynchronizowany z animacją i dźwiękiem.

4. WALKA WOJOWNIKA — ABSOLUTNY PRIORYTET

Widok rąk

Gracz widzi dwie wirtualne dłonie z przedramionami.

W przypadku miecza jednoręcznego:

• Prawa ręka trzyma miecz.
• Lewa ręka pozostaje w naturalnej pozycji albo obsługuje tarczę.
• Miecz w bezczynności znajduje się głównie w prawej dolnej części ekranu.
• Nie przesłania całego przeciwnika.
• Dłonie poruszają się naturalnie.
• Broń pozostaje poprawnie połączona z dłonią.

Nie chcę dłoni wykonanych z kilku walców.

Potrzebne są modele z poprawnym rigiem, palcami, nadgarstkami i naturalnymi animacjami.

Miecz jednoręczny — 1H

Podstawowy atak składa się z trzech ciosów.

Combo 1H:

1. Cios pierwszy — zamach z lewej do prawej.
2. Cios drugi — zamach z prawej do lewej.
3. Cios trzeci — mocniejsze cięcie kończące.

Ataki muszą być płynnie połączone.

Każde kolejne naciśnięcie ATAK we właściwym oknie kontynuuje combo.

Gdy gracz nie naciśnie przycisku w wymaganym czasie, sekwencja wraca do pierwszego ciosu.

Trzeci cios kończy combo i odtwarzana jest animacja powrotu do pozycji bojowej.

Przytrzymanie ATAK przez około 0,6 s uruchamia odrębną umiejętność Ciężkie Cięcie, zgodnie z przyjętą dokumentacją.

Miecz dwuręczny — 2H

Dla broni dwuręcznej:

• Obie dłonie trzymają rękojeść.
• Nie można używać tarczy.
• Animacje są cięższe i wolniejsze.
• Zamachy mają większy zakres.
• Uderzenia są mocniejsze.
• Combo składa się również z trzech ciosów.
• Ruchy różnią się od jednoręcznej broni.

Nie wolno uznać dwóch rodzajów broni za gotowe przez zastosowanie tej samej animacji z inną wartością obrażeń.

Trafienie

Trafienie musi zależeć od:

• Rzeczywistego zasięgu wirtualnej broni.
• Aktualnego położenia ostrza.
• Okna aktywnego ataku.
• Hitboxa przeciwnika.
• Reguł celności i obrażeń.
• Stanu przeciwnika.

Jedno uderzenie nie może wielokrotnie naliczać obrażeń temu samemu celowi, chyba że odrębna umiejętność wyraźnie to przewiduje.

Potwór poza zasięgiem nie otrzymuje obrażeń.

Reakcja na trafienie

Trafienie ma być wyraźnie odczuwalne:

• Animacja uderzenia mieczem.
• Dźwięk zamachu.
• Dźwięk trafienia.
• Reakcja potwora.
• Liczba obrażeń.
• Efekt wizualny kontaktu ostrza z przeciwnikiem.
• Niewielka wibracja telefonu, jeśli urządzenie ją obsługuje.
• Krótkie, subtelne efekty wzmacniające odczucie uderzenia.

Trafienie krytyczne wymaga wyraźniejszej informacji wizualnej.

Nie przesadzamy z potrząsaniem kamerą AR.

5. AURA — CHARAKTERYSTYCZNA ANIMACJA

To jest szczególnie ważny element wizji właściciela.

Stan początkowy:

• Prawa ręka trzyma miecz.
• Lewa ręka jest swobodna.

Po naciśnięciu AURA:

1. Prawa ręka unosi miecz.
2. Lewa ręka wykonuje animowany ruch w kierunku rękojeści.
3. Lewa dłoń chwyta miecz.
4. Obie ręce stabilizują broń.
5. Rozpoczyna się efekt ładowania energii.
6. Energia rozchodzi się wokół ostrza.
7. Następuje wyraźny błysk aktywacji.
8. Na mieczu pozostaje efekt energetyczny.
9. Postać wraca do pozycji pozwalającej kontynuować walkę.

Efekt powinien obejmować światło, cząsteczki, smugi energii i dźwięk aktywacji.

Animacja rąk musi wyglądać naturalnie — nie może polegać na nagłym przestawieniu ich pozycji.

W gotowym systemie AURA aktywuje przypisane wzmocnienie klasy. Sam gest nie zadaje bezpośrednich obrażeń.

Dokładne działanie, koszt i czas trwania wynikają z danych konkretnej umiejętności.

6. KLASY POSTACI

W grze występują cztery klasy.

WOJOWNIK

Walka wręcz, wytrzymałość i silne ataki.

Ścieżki:

• Bastion — tarcza, blokowanie i kontrataki.
• Rozłam — broń dwuręczna, silne cięcia i obrażenia obszarowe.

NINJA

Szybkość, precyzja i obrażenia krytyczne.

Ścieżki:

• Cień — sztylety, szybkie ataki, ukrycie.
• Pustułka — łuk, precyzyjne strzały i pułapki.

SZAMAN

Magia, leczenie i efekty żywiołów.

Ścieżki:

• Korzeń — leczenie, ochrona i oczyszczanie.
• Nawałnica — wiatr, pioruny i ofensywna magia.

SLAJTER

Wojownik-mag korzystający z mrocznej energii.

Ścieżki:

• Popiół — zaklęte ostrze i kradzież życia.
• Zmierzch — klątwy, ataki magiczne i przyzwania.

Nazwa Slajter jest zatwierdzona. Nie należy jej samodzielnie zmieniać.

Rozwój klas

Każda klasa ma:

• 2 umiejętności wspólne.
• 6 umiejętności pierwszej ścieżki.
• 6 umiejętności drugiej ścieżki.

Razem 14 umiejętności na klasę, czyli 56 umiejętności w całej grze.

Gracz odblokowuje wybraną ścieżkę na poziomie 10.

Kolejność implementacji:

1. Wojownik.
2. Slajter.
3. Ninja.
4. Szaman.

Szczegółowe nazwy, wartości, koszty i odnowienia zostały już zapisane w docs/design/CLASSES_SKILLS.md i danych projektu.

Nie projektować tych systemów od nowa.

7. POZIOMY, EXP I STATYSTYKI

Maksymalny poziom: 99.

Planowany czas osiągnięcia poziomu 99 to około 250 godzin aktywnej gry. To cel balansowy, nie obietnica rzeczywistego czasu każdego gracza.

Główne statystyki:

• Moc.
• Zwinność.
• Duch.
• Krzepa.

Każdy awans daje automatyczne przyrosty klasowe oraz 2 wolne punkty do rozdania.

Postać posiada:

• HP.
• MP.
• Atak fizyczny.
• Atak magiczny.
• Obronę.
• Celność.
• Unik.
• Szansę trafienia krytycznego.
• Odporności.

Doświadczenie pochodzi z:

• Zabijania potworów.
• Zadań.
• Eksploracji świata GPS.

Zatwierdzone założenia:

• Poziom 10 po około 3–4 godzinach.
• Brak możliwości przekroczenia poziomu 99.
• Cztery wskaźniki EXP, po 25% poziomu każdy, z częściowym wypełnieniem.
• Brak utraty poziomu po śmierci.
• Kara śmierci: 2% EXP bieżącego poziomu.

Wszystkie formuły muszą być zgodne z CHARACTER_PROGRESSION.md.

8. SYSTEM UMIEJĘTNOŚCI

Umiejętności przechodzą przez cztery stopnie:

Wprawa → Biegłość → Kunszt → Szczyt.

Rozwój odbywa się przez:

• Punkty umiejętności.
• Traktaty Biegłości.
• Iskry Kunsztu.
• Pieczęcie Szczytu.

System uwzględnia:

• Wymagany poziom postaci.
• Prawdopodobieństwo powodzenia.
• Koszt EXP.
• Odstępy pomiędzy próbami.
• Zmniejszanie czasu odnowienia.
• Rosnącą skuteczność umiejętności.

Stopnie zaawansowane muszą być rozliczane przez serwer.

Nie można uzależniać wyniku ulepszenia od manipulacji zegarem telefonu.

9. EKWIPUNEK I PRZEDMIOTY

Początkowy ekwipunek: 24 miejsca.

Maksymalny ekwipunek: 56 miejsc.

Sloty wyposażenia:

1. Broń.
2. Druga ręka.
3. Hełm.
4. Zbroja.
5. Rękawice.
6. Buty.
7. Kolczyki.
8. Naszyjnik.
9. Bransoleta.
10. Pierścień.

Dodatkowo dwa szybkie sloty na przedmioty użytkowe.

W grze muszą znajdować się:

• Miecze jednoręczne.
• Miecze dwuręczne.
• Topory.
• Sztylety.
• Łuki.
• Różdżki.
• Tarcze i przedmioty drugiej ręki.
• Zbroje.
• Biżuteria.
• Mikstury.
• Materiały do ulepszania.
• Przedmioty związane z zadaniami.

Rzadkość

Pięć poziomów:

• Zwykły.
• Niezwykły.
• Rzadki.
• Epicki.
• Legendarny.

Przedmioty różnią się statystykami i dodatkowymi właściwościami.

Zmiana broni musi aktualizować model widoczny podczas walki.

Zmiana rękawic powinna aktualizować wygląd wirtualnych dłoni.

Wymiana ekwipunku nie może powodować niespójności statystyk pomiędzy klientem a serwerem.

10. KOWAL I ULEPSZANIE

Kowal jest normalnym NPC świata gry.

Gracz może podejść do jego lokalizacji i uruchomić interakcję.

System hartowania:

• Przedmioty ulepszane od +1 do +10.
• Wymagane talary i materiały.
• Szanse powodzenia zależne od poziomu ulepszenia.
• Porażka nie niszczy przedmiotu.
• Od +6 porażka może obniżać poziom ulepszenia o 1.
• Materiał ochronny może zapobiegać spadkowi.

Dodatkowo przekucie właściwości przedmiotów.

Wartości i szanse z ITEMS_COMBAT_SPEC.md.

Kowal wymaga własnego modelu 3D, animacji pracy z młotem, interfejsu i efektów ulepszania.

11. POTWORY, BOSSOWIE I NPC

Potwory

Każdy przeciwnik ma:

• Nazwę.
• Poziom.
• HP.
• Obrażenia.
• Obronę.
• Zachowanie AI.
• Zasięg ataku.
• Animacje.
• Efekty statusów.
• EXP.
• Talary.
• Tabelę dropu.

Przeciwnicy dzielą się na zwykłych, elitarnych i silniejszych strażników/bossów.

AI powinno obejmować:

• Bezczynność.
• Wykrycie gracza.
• Przygotowanie ataku.
• Atak.
• Odpoczynek po ataku.
• Otrzymanie obrażeń.
• Efekty specjalne.
• Śmierć.

Pierwszy potwór testowy to Trujący Pająk — poziom 5 i 120 HP w zaakceptowanym zestawie startowym.

Nie może on pozostać jedynym grywalnym przeciwnikiem w docelowej grze.

NPC

Wymagani są między innymi:

• Kowal.
• Wędrowny Handlarz.
• Trener umiejętności.
• Postacie przydzielające zadania.

Każdy NPC posiada nazwę, rolę, własny wygląd i interakcje.

System zadań

Docelowo:

• Zadania wprowadzające.
• Zadania fabularne.
• Zadania dzienne.
• Zadania eliminacji potworów.
• Zadania eksploracyjne.
• Zadania rozwoju klasy.
• Zadania zdobywania materiałów.

Nagrody obejmują EXP, talary i przedmioty.

System zadań musi zapamiętywać postęp po ponownym uruchomieniu gry.

12. HUD — INTERFEJS GRY

HUD powinien przypominać profesjonalny interfejs mobilnego RPG.

Podczas walki:

• Pasek HP gracza.
• Pasek MP.
• Nazwa i poziom przeciwnika.
• Pasek HP przeciwnika.
• Przycisk ATAK.
• Przycisk AURA.
• Dwa przyciski aktywnych umiejętności.
• BLOK lub UNIK zależnie od klasy.
• Dwa szybkie sloty mikstur.
• Informacje o odnowieniach.
• Liczby zadawanych obrażeń.

Poza walką:

• Poziom i EXP.
• Mapa.
• Ekwipunek.
• Postać i statystyki.
• Umiejętności.
• Zadania.
• Menu i ustawienia.

Interfejs ma być po polsku.

Przyciski muszą być dostosowane do ekranu dotykowego i bezpiecznego chwytu telefonu.

Nie zasłaniamy centralnego pola obserwacji potwora.

13. DŹWIĘK

W repozytorium istnieją już pliki efektów dźwiękowych.

Sam fakt dodania pliku WAV nie oznacza, że gra posiada dźwięk.

System ma rzeczywiście odtwarzać:

• Zamachy mieczem.
• Trafienia.
• Bloki.
• Umiejętności.
• Aktywację Aury.
• Odgłosy potworów.
• Śmierć potworów.
• Zbieranie łupu.
• Zakupy.
• Ulepszanie.
• Awans poziomu.
• Działania interfejsu.

Audio musi być powiązane ze zdarzeniami gry.

Wymagane są ustawienia głośności i kontrola licencji zasobów.

14. SERWER I ZAPIS POSTACI

To jest element krytyczny.

Nie akceptuję gry, która kasuje postać, ekwipunek albo postęp po zamknięciu aplikacji.

Zapisywane muszą być:

• Profil gracza.
• Klasa.
• Poziom i EXP.
• Statystyki.
• Umiejętności i ich stopnie.
• Ekwipunek.
• Założone przedmioty.
• Talary.
• Materiały.
• Zadania.
• Postęp eksploracji.
• Wyniki zakończonych spotkań.

Obecny prototyp serwera korzysta z .NET 8 i SQLite.

Serwer pracujący na localhost nie jest rozwiązaniem umożliwiającym normalne korzystanie z gry na telefonie przez Internet.

Docelowo wymagamy:

• Dostępnego przez HTTPS backendu.
• Uwierzytelniania.
• Identyfikacji gracza.
• Trwałego zapisu.
• Synchronizacji z klientem.
• Walidacji nagród.
• Kontroli nadużyć GPS.
• Ochrony przed wielokrotnym naliczaniem łupu.
• Odporności na przerwanie i ponowienie żądania.
• Jasnego zachowania gry przy utracie połączenia.

Serwer jest autorytetem dla nagród, ekwipunku i postępu.

Tryb lokalnego prototypu może działać podczas testów, ale nie może udawać gotowego systemu online.

15. OPTYMALIZACJA I TELEFONY

Silnik: Unity.

Wersja używana w aktualnym projekcie: 6000.6.4f1.

Platformy:

• Android.
• iOS.

Pierwsze testy fizyczne prowadzone są na Samsungu Galaxy A31.

Docelowy minimalny cel pierwszego grywalnego wycinka:

• Stabilne 30 FPS na A31 podczas typowej walki.
• Brak awarii.
• Brak blokowania głównego wątku przez ciężkie operacje.
• Brak narastających wycieków pamięci.
• Stabilna kamera AR.
• Stabilne umiejscowienie przeciwnika.
• Poprawny powrót do gry po zminimalizowaniu aplikacji.

Na mocniejszych telefonach należy dążyć do 60 FPS.

Nie deklarować obsługi iOS bez fizycznego testu.

Nie deklarować 30 FPS na A31 na podstawie testów jednostkowych lub symulacji.

16. ZASADY PRACY SIEDMIU BOTÓW

Kierownik produkcji odpowiada za końcowy wynik, a nie za liczbę utworzonych dokumentów.

Zadania muszą być prowadzone równolegle tam, gdzie nie występują zależności.

Przykładowy podział odpowiedzialności:

Architektura i programowanie: Unity, gameplay, integracja, backend.

Modele 3D i animacje: rigowane dłonie, broń, potwory, NPC, animacje.

Walka i umiejętności: combo, obrażenia, hitboxy, Aura, AI przeciwników.

Świat GPS: mapa, geolokalizacja, spawny, bezpieczeństwo.

Interfejs i systemy RPG: HUD, ekwipunek, sklepy, rozwój postaci.

Audio i efekty: SFX, VFX, synchronizacja animacji.

QA i testy automatyczne: testy, regresja, APK, urządzenia i raportowanie błędów.

To podział funkcjonalny. Kierownik ma przypisać rzeczywiste zadania według dostępnych kompetencji agentów.

Zasady obowiązkowe

1. Przed nową implementacją sprawdzić main, aktywne branche, PR-y i issues.
2. Nie implementować ponownie gotowych systemów.
3. Nie zastępować zatwierdzonych decyzji własnymi pomysłami.
4. Nie tworzyć niepotrzebnych pytań o rzeczy już ustalone.
5. Nie wstrzymywać wszystkich botów dlatego, że jeden moduł oczekuje na decyzję.
6. Każdy nowy system musi zostać zintegrowany z grą.
7. Każdy błąd krytyczny zgłaszać natychmiast.
8. Nie uznawać zielonego CI za dowód działania gry na telefonie.
9. Nie oznaczać zadania jako DONE bez wymaganych dowodów.
10. Każdy bot odpowiada za działający rezultat, a nie tylko opis wykonanej pracy.

Jeżeli zadanie zostało formalnie zatrzymane przez właściciela, nie zdejmować blokady samowolnie. Pozostałe zatwierdzone prace prowadzić dalej.

17. OBECNE PROBLEMY DO ROZWIĄZANIA

Według dotychczasowych raportów produkcyjnych:

P0 — Audio

Pliki dźwiękowe znajdują się w projekcie, ale wymagane jest sprawdzenie, czy zdarzenia gry rzeczywiście je odtwarzają.

P0 — Backend

Istnieje lokalny backend, lecz nie jest to jeszcze publicznie dostępny serwer gry.

P0 — Zapis

Trzeba zapewnić i zweryfikować trwały zapis całego postępu.

P0 — Grafika

Prototypowe bryły nie spełniają oczekiwań dotyczących modeli i animacji.

P0 — Walka

Priorytetem jest prawdziwe combo 1H i 2H, animacje dłoni, Aura, reakcje przeciwników i poprawne naliczanie obrażeń.

P0 — Test na telefonie

Wyniki testów automatycznych nie zastępują uruchomienia kompletnej gry na urządzeniu fizycznym.

W pierwszej kolejności należy zweryfikować, które z tych problemów nadal występują w najnowszym commicie. Nie wolno opierać aktualnego statusu wyłącznie na starszym audycie.

18. KOLEJNOŚĆ PRODUKCJI

ETAP I — PIERWSZA KOMPLETNA PĘTLA GRY

Zbudować jeden działający scenariusz:

1. Uruchamiam grę.
2. Wczytuję postać Wojownika.
3. Widzę mapę GPS.
4. Otrzymuję informację o pobliskim potworze.
5. Podchodzę do niego.
6. Wybieram rozpoczęcie walki.
7. Włącza się kamera AR.
8. Pająk stoi stabilnie na rzeczywistym podłożu.
9. Widzę animowane dłonie i miecz.
10. Wykonuję trzyciosowe combo.
11. Przeciwnik traci HP.
12. Aktywuję Aurę.
13. Widzę animację dwóch rąk i energii.
14. Zabijam przeciwnika.
15. Otrzymuję EXP, talary i przedmiot.
16. Przedmiot pojawia się w ekwipunku.
17. Mogę założyć zdobyty przedmiot.
18. Mogę skorzystać ze sklepu NPC.
19. Wracam na mapę.
20. Zamykam aplikację.
21. Uruchamiam ją ponownie.
22. Mój poziom, przedmioty i talary nadal istnieją.

Dopóki ten scenariusz nie działa na fizycznym telefonie, nie mamy ukończonego pierwszego grywalnego wycinka.

ETAP II — JAKOŚĆ WALKI

• Ukończyć profesjonalne modele dłoni.
• Ukończyć miecze 1H i 2H.
• Zintegrować animacje combo.
• Ukończyć animację Aury.
• Dodać poprawne efekty trafień.
• Dodać reakcje i ataki potworów.
• Dodać dźwięk.
• Dopracować HUD.

ETAP III — ROZBUDOWA RPG

• Rozwój postaci.
• Umiejętności Wojownika.
• Ekwipunek i rzadkość przedmiotów.
• Kowal i ulepszanie.
• Zadania.
• Więcej potworów.
• Kolejni NPC.
• Pierwsze trudniejsze spotkania.

ETAP IV — KOLEJNE KLASY

• Slajter.
• Ninja.
• Szaman.
• Własne animacje i uzbrojenie dla każdej klasy.
• Dokończenie wszystkich 56 umiejętności.

ETAP V — PEŁNY ŚWIAT I PRZYGOTOWANIE WYDANIA

• Rozbudowany system spawnów.
• Więcej rodzin przeciwników.
• Bossowie i wydarzenia.
• Dalsze zadania.
• Ochrona przed oszustwami GPS.
• Stabilny backend.
• Testy obciążeniowe.
• Optymalizacja Android/iOS.
• Testy wydajności i bezpieczeństwa.
• Przygotowanie do dystrybucji.

PvP, handel między graczami i pełne funkcje multiplayer nie należą do zatwierdzonego zakresu v1. Nie wdrażać ich kosztem podstawowej gry.

19. DOKUMENTACJA NADRZĘDNA

W repozytorium istnieją zatwierdzone materiały. Trzeba je czytać i respektować.

Najważniejsze:

• docs/PROJECT_BRIEF.md
• docs/design/README.md
• docs/design/CHARACTER_PROGRESSION.md
• docs/design/CLASSES_SKILLS.md
• docs/design/COMBAT_PHYSICS.md
• docs/design/ITEMS_COMBAT_SPEC.md
• docs/design/MOBS_NPC.md
• docs/design/ECONOMY.md
• docs/design/MODES_DATA_MODEL.md
• docs/design/OPEN_QUESTIONS_PLAN.md
• docs/METINAR-FPP-001.md
• docs/qa/DEVICE-ACCEPTANCE-LOOP-V0.md

Combo Wojownika opisuje również GitHub issue #82.

Gdy ten dokument streszcza istniejące zatwierdzone liczby i mechaniki, szczegółowe pliki projektowe stanowią źródło dokładnych wartości. Jeżeli wystąpi rzeczywista sprzeczność, zgłosić ją z odnośnikami do obu źródeł — nie zgadywać.

20. POLECENIE KOŃCOWE DLA GROKA

Przestań traktować MetinAR jak eksperyment techniczny.

Twoim zadaniem jest zarządzanie produkcją gry i doprowadzenie wszystkich agentów do wspólnego, działającego rezultatu.

Nie chcę kolejnych 10 pytań o wygląd trzeciego ciosu, pozycję miecza ani standardowe zachowanie przycisku, skoro kierunek został już ustalony.

Podejmuj samodzielnie rutynowe decyzje techniczne, przestrzegając zatwierdzonej specyfikacji.

Od razu:

1. Sprawdź aktualny stan całego repozytorium.
2. Ustal, co działa naprawdę, co istnieje tylko w kodzie, a co jest prototypem graficznym.
3. Porównaj implementację z niniejszą specyfikacją.
4. Wyznacz braki blokujące pierwszy grywalny wycinek.
5. Rozdziel niezależne zadania pomiędzy agentów.
6. Ustal zależności i kolejność integracji.
7. Pilnuj kompilacji, testów i zgodności z główną gałęzią.
8. Przekazuj do testów jeden spójny build, a nie przypadkowe zestawy niezintegrowanych funkcji.
9. Raportuj błędy, blokady i faktyczny stan wykonania bez ukrywania niepowodzeń.
10. Dostarczaj mierzalne efekty widoczne podczas gry na telefonie.

Definicja ukończenia zadania:

• Kod jest zintegrowany.
• Testy właściwe dla zadania przechodzą.
• Kompilacja Unity jest poprawna.
• Gra uruchamia się na urządzeniu.
• Funkcja działa zgodnie ze specyfikacją.
• Dostępne są dowody działania.
• Właściciel może ocenić rezultat.

Nie przedstawiaj samego commita, testów 740/740, dokumentacji albo wygenerowanego modelu jako dowodu ukończenia całej funkcji.

Produkt końcowy ma przypominać prawdziwe fantasy MMORPG w rozszerzonej rzeczywistości, z rozwijaniem postaci, ekwipunkiem, potworami, zadaniami, animowaną walką i własnym światem GPS.

To jest główny kierunek całej produkcji MetinAR. Nie zmieniaj go bez decyzji właściciela.

Pierwszy raport, którego oczekuję: aktualny stan realizacji specyfikacji, konkretne braki P0, przypisani wykonawcy i plan doprowadzenia pierwszej pełnej pętli gry do odbioru na telefonie.
