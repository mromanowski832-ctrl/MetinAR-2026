# METINAR-017 — Moby i NPC

> **Status: ZAAKCEPTOWANE przez właściciela 2026-10-21 (#40; maks. poziom 99, pozostałe propozycje domyślne). Bez implementacji.**
> Wszystkie liczby to **proponowane wartości startowe do balansowania**. Indeks: [README.md](README.md).
> Bez nazw, statystyk i modeli z Metin2. Moby i NPC to własne projekty (#30 pająk, #38 handlarz).

## 1. Rodziny mobów (archetypy)

| Rodzina (`MobFamily`) | Przykład | Rola w walce AR | Typ obrażeń | Wymagane animacje |
|---|---|---|---|---|
| `Arachnid` — pajęczaki | **Trujący Pająk** (#30) | szybki, trucizna przy trafieniu | fizyczne + trucizna | Idle, Walk, Attack, Hit, Death (+ Aggro) |
| `Beast` — bestie leśne | dzik, wilk (własne projekty) | szarża z telegrafem, odrzut | fizyczne | jw. + Run |
| `Wisp` — błędne ogniki mgieł | unoszący się ognik | pociski, unik od pierwszego ciosu | magiczne | Idle, Attack, Hit, Death (bez nóg) |
| `Golem` — kamienne | mały golem z gruzu | wolny, wysoka obrona, ogłuszenie | fizyczne | jw. + Stun |
| `Outlaw` — banici | rabuś z pałką | blok tarczą, uniki | fizyczne | humanoid (budżet NPC) |
| `Shade` — cienie | widmowy kształt | kradzież MP, mrok | mrok | Idle, Attack, Hit, Death |

Lista zestawu startowego zawiera **tylko Trującego Pająka**. Pozostałe rodziny to propozycja kolejnych assetów (etap 7 w #41).

## 2. Rangi

| Ranga (`MobRank`) | HP | Atak | EXP / talary | Drop przedmiotu | Na mapie |
|---|---|---|---|---|---|
| `Normal` — zwykły | ×1 | ×1 | ×1 | 18% | domyślnie |
| `Elite` — elitarny | ×3,5 | ×1,4 | ×3 | 60% | ramka, rzadszy |
| `Guardian` — strażnik strefy | ×12 | ×1,8 | ×10 | 100% × 2 | 1 na strefę, raz na dobę na gracza |
| `Overlord` — władca świata | — | — | — | — | **później**, wymaga serwera i gry wieloosobowej (Q15) |

Statystyki zwykłego moba wg poziomu `m` = 1–99 ([tables/mob_level_stats.csv](tables/mob_level_stats.csv)):
`HP = round5(30 + 11 × m^1.3)`, `Atk = round(6 + 3 × m^1.1)`, `Def = round(4 + 2,2 × m)`,
`EXP = round(20 + 10 × m^1.15)`. Kalibracja: poziom 5 daje **HP 120**, czyli wartości prototypu pająka bez zmian.
Przykłady: poz. 60 — HP 2 285, Atk 277, EXP 1 129; poz. 99 — HP 4 350, Atk 476, EXP 1 992.

## 3. Trujący Pająk w nowym systemie (zestaw startowy)

| Pole | Wartość | Źródło |
|---|---|---|
| `id` | `mob_venomous_spider` | |
| Nazwa | Trujący Pająk | prototyp (bez zmian) |
| Poziom | 5 | prototyp (bez zmian) |
| HP | 120 | prototyp (bez zmian) |
| Agresywny, zasięg aggro | tak, 12 m + `AggroConfidenceGate` | prototyp (bez zmian) |
| Atak / obrona | 24 / 15 | formuła §2 |
| Odstęp ataków | 2,4 s, telegraf 0,7 s (uniesione przednie odnóża) | propozycja |
| Trucizna | 25% szansy przy trafieniu: 4 obrażeń/s przez 4 s | propozycja |
| Słaby punkt | oczy/głowa: +15 pkt. proc. szansy krytycznej | propozycja |
| EXP / talary | 84 / 10–18 | formuła |
| Drop | [tables/drop_table_venomous_spider.json](tables/drop_table_venomous_spider.json) | propozycja |
| Odrodzenie | patrz §5 | |

## 4. Stany AI moba

### 4.1 Na mapie GPS

`Wander` (krąży w swojej komórce) → `Alert` (gracz w 2× zasięgu aggro; mob obraca się, HUD radar) →
`Aggro` (przejście bramki pewności GPS → prośba o spotkanie AR, jak dziś) → `Engaged` (trwa spotkanie AR,
mob niewidoczny dla innych w v1) → `Defeated` / `Escaped` → `Respawning`.
Pasywne moby (`aggressive = false`) zaczynają spotkanie tylko po dotknięciu na mapie i „Wejdź w AR”.

### 4.2 W spotkaniu AR

```text
Spawn → Approach → Engage ⇄ (Telegraph → Attack → Recover) → Death
                    ↘ Stagger / Stun ↗        ↘ Flee (tylko bestie, HP < 15%, 1×)
```

- `Spawn`: pojawienie się 2,5 m od kamery na wykrytym podłożu (zgodnie z METINAR-006).
- `Approach`: podejście na dystans walki 1,2 m (zasięg ostrza FPP ~0,94 m + margines).
- `Telegraph`: wyraźna zapowiedź ataku 0,6–0,9 s (animacja + dźwięk), okno na BLOK/UNIK/Ripostę.
- `Attack`: obrażenia w jednej klatce zdarzenia animacji, jeśli gracz nie zablokował/uniknął.
- `Recover`: 0,5–0,8 s **okno odsłonięcia** (bonus dla Ciosu w Splot itp.).
- `Stagger`: lekka reakcja na trafienie, przerywa `Telegraph` tylko przy krytyku lub umiejętności kontroli.
- Gracz nie może być zabity „poza ekranem”: mob atakuje tylko, gdy jest w polu widzenia kamery albo pokazuje
  wskaźnik kierunku na krawędzi ekranu przez cały telegraf.

## 5. Odrodzenia i strefy GPS

- Świat dzielimy na **komórki** (~200 m). Każda komórka ma pulę 0–3 mobów **osobistych** (każdy gracz ma własne,
  brak rywalizacji o moba w v1).
- **Poziom moba** = poziom gracza −2…+1 (strefy dopasowane do gracza), ograniczony do 1–99, z wyjątkiem strażników
  o stałym poziomie strefy (też 1–99). Decyzja Q12 (propozycja zaakceptowana).
- **Rodzina moba** z charakteru miejsca (park/las → bestie, pajęczaki; przy wodzie → ogniki, ale nigdy w wodzie;
  zabudowa → banici). Wymaga danych mapy (#20/#28).
- **Odrodzenie:** po pokonaniu 15 min (zwykły), 45 min (elitarny), strażnik raz na dobę na gracza. Maks. 6 mobów
  widocznych naraz w promieniu 300 m.
- Zasady bezpieczeństwa z PROJECT_BRIEF bez zmian: brak spawnów na drogach, torach, w wodzie, w budynkach,
  na terenie prywatnym, z buforami. Spawny ustala serwer (#22), klient tylko wyświetla.

## 6. NPC

| Typ (`NpcRole`) | Przykład | Funkcje | Lokalizacja |
|---|---|---|---|
| `Merchant` | **Wędrowny Handlarz** (#38) | sklep zwykłych przedmiotów, skup, sakwy, w prototypie też trener i wybór ścieżki | stała lokalizacja GPS |
| `Smith` | Kowal (wcześniejszy projekt z METINAR-009) | hartowanie, przekucie | stała |
| `Trainer` | trener klasy | punkty umiejętności, zmiana ścieżki | stała, później |
| `QuestGiver` | mieszkańcy | zadania dzienne i fabularne | stała |

Wymagane animacje NPC z pipeline'u: Idle, Walk, TurnLeft, TurnRight, Talk, Interact (+ opcjonalne Gesture, Hit, Death).
NPC nie walczą i nie da się ich zaatakować w v1. Zasięg interakcji: 30 m (propozycja) z bramką dokładności GPS.
