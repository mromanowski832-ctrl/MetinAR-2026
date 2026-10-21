# METINAR-017 — Tryby gry (AR/FPP i mapa GPS) oraz model danych

> **Status: ZAAKCEPTOWANE przez właściciela 2026-10-21 (#40; maks. poziom 99, pozostałe propozycje domyślne). Bez implementacji.** Indeks: [README.md](README.md).

## 1. Co dzieje się w którym trybie

| System | Mapa GPS (`WorldMapPrototype`) | Spotkanie AR / FPP (`ArEncounterPrototype`) |
|---|---|---|
| Ruch gracza | realny spacer (GPS na pierwszym planie) | brak ruchu w grze; kamera i ręce FPP |
| EXP | eksploracja (dystans, komórki), zadania | zabicie moba |
| Talary | zadania, skrzynki odkrycia, sklep | łup z moba |
| Moby | `Wander` / `Alert` / `Aggro` + bramka pewności GPS, lista na radarze | pełna walka, AI `Telegraph/Attack/Recover` |
| NPC | sklep, kowal, trener, zadania (zasięg 30 m) | brak |
| Ekwipunek / postać | pełne panele: inwentarz, postać, umiejętności, hartowanie u NPC | tylko szybkie sloty mikstur, podgląd HP/MP |
| Umiejętności | przypisywanie do slotów, rozdawanie punktów | użycie: ATAK, AURA, 2 sloty, BLOK/UNIK |
| HUD | portret, poziom, HP/MP, moneta, 4 × 25% EXP, radar | HP/MP gracza, pasek HP moba, przyciski walki |
| Śmierć | — | powrót na mapę z 30% HP, kara wg Q4 |
| Pauza / utrata AR | — | anulowanie akcji, mob też wstrzymany |

Przejścia: mapa → AR przez istniejący `ArEncounterHandoff` / `ArEncounterRequest` (dziś: nazwa, poziom, HP,
agresja, powód). Do rozszerzenia o `mobDefinitionId`, `encounterId`/nonce z serwera (#22) i snapshot postaci gracza.
AR → mapa: wynik spotkania (`Victory`, `Defeat`, `Fled`, `Cancelled`) + łup do potwierdzenia.

## 2. Dane statyczne (definicje)

Propozycja: **kanoniczne tabele JSON** w repo (dziś szkice w [tables/](tables/README.md)), importowane w edytorze
do `ScriptableObject`, a serwer czyta te same pliki. Jedno źródło prawdy dla klienta i serwera.

| Definicja (`ScriptableObject`) | Główne pola |
|---|---|
| `BalanceConfig` | stałe formuł (HP/poziom, mitigation 50 + 8×L, limity krytyka/bloku/odporności, rozrzut) |
| `ExpCurve` | `expToNext[1..59]` lub parametry formuły |
| `ClassDefinition` | `id`, statystyki startowe, przyrosty, broń, druga ręka, ścieżki |
| `SkillDefinition` | `id`, klasa/ścieżka, poziom odblokowania, rodzaj, cel, skalowanie, koszt, odnowienie, sterowanie AR |
| `SkillTierDefinition` | `rankIndex` 1–29, `tierId` (practice/proficiency/artistry/apex), `tierRank`, sposób podniesienia, szansa, odstęp h, koszt EXP %, min. poziom, mnożniki ([tables/skill_tiers.csv](tables/skill_tiers.csv)) |
| `ItemDefinition` | `id`, kategoria, slot, typ, statystyki bazowe vs iL, wymagania, stos, cena, ikona, prefab FPP |
| `AffixDefinition` | `id`, sloty, zakres wartości vs iL, limit |
| `RarityDefinition` | kolor, liczba affiksów, mnożnik, wagi dropu |
| `MobDefinition` | `id`, nazwa, rodzina, ranga, poziom lub reguła poziomu, agresja, aggro m, ataki, hitboxy, `dropTableId`, prefab |
| `DropTableDefinition` | talary, rzuty, pule z wagami |
| `NpcDefinition` / `ShopDefinition` | rola, oferta, ceny |

## 3. Dane gracza (stan)

```text
PlayerProfile { playerId, classId, pathId?, level, exp, statPointsAllocated{Might,Finesse,Spirit,Vigor},
                freeStatPoints, skills{skillId→SkillProgress}, freeSkillPoints, skillLoadout[ATAK/AURA/1/2],
                hp, mp, talers, inventory[slotIndex→ItemStack], inventoryCapacity, equipment{EquipSlot→ItemInstance},
                lootStash[], questState{}, explorationDaily{dateUtc, metersCredited, cellsDiscovered}, revision }
ItemInstance  { instanceId (GUID), definitionId, itemLevel, rarity, temper, affixes[{affixId,value}], locked }
ItemStack     { definitionId, quantity }  // dla przedmiotów stosowalnych
SkillProgress { skillId, rankIndex (1–29), tierId, tierRank, pointsSpent, attemptsTotal,
                lastAttemptAtUtc?, nextAttemptAtUtc? }   // czasy UTC ustawia tylko serwer
SkillTierAttempt { attemptId (GUID), skillId, fromRankIndex, itemId, expCost, success, serverTimeUtc }  // log serwera
```

Zgodne z kierunkiem #39 (statystyki zmienia tylko serwer, `revision` do konfliktów). W trybie offline/prototypu
profil może być zapisany lokalnie (JSON), oznaczony jako niezweryfikowany.

## 4. Co docelowo wymaga serwera (flaga, bez przesądzania)

Decyzje architektury należą do właściciela/kontrolera (Q1 z PR #27, #39). Tutaj tylko oznaczenie ryzyka:

| Obszar | Lokalnie w prototypie? | Docelowo serwer? | Dlaczego |
|---|---|---|---|
| Rozstrzyganie pojedynczego ciosu | tak | **do decyzji** (np. walidacja podsumowania walki) | opóźnienie sieci w AR |
| Wynik spotkania, EXP, talary | tak (niezweryfikowane) | **tak** | główne miejsce oszustw |
| Losowanie łupu | tak, z ziarnem | **tak** | duplikacja / manipulacja |
| Kredyt za dystans i komórki GPS | podgląd | **tak** | fałszywe GPS |
| Spawny i odrodzenia | tak (prototyp) | **tak** (#22) | bezpieczeństwo miejsc, spójność |
| Ekwipunek, hartowanie (RNG), sklep | tak | **tak** | ekonomia |
| Próby stopni umiejętności (RNG, odstęp `nextAttemptAtUtc`, koszt EXP) | tak (niezweryfikowane) | **tak** | zmiana zegara telefonu, powtarzanie prób |
| Handel, rynek, gildie, PvP | nie | **tak**, jeśli powstaną (Q8, Q9) | |
| Definicje (tabele) | tak | wersjonowane, wspólne | spójność |

## 5. Ekran „Napisy” (Credits) i atrybucja licencji

Decyzja właściciela (2026-10-21): dozwolone jest audio na licencji **CC-BY-4.0** pod warunkiem podania autora.
CC-BY wymaga widocznej atrybucji w grze, dlatego gra dostaje ekran **Napisy** (`CreditsScreen`).

### 5.1 Gdzie jest dostępny

- **Menu główne → Napisy** oraz **Ustawienia → Informacje → Napisy i licencje** (ten sam ekran).
- Dostępny bez logowania i bez połączenia z siecią (dane wbudowane w build), z mapy GPS przez Ustawienia.
- Nie jest dostępny w trakcie walki AR (pauza → wyjście na mapę), żeby nie przerywać spotkania.
- Zawartość przewijana, pionowo, czytelna na A31; linki do źródeł/licencji jako tekst (opcjonalnie otwierane w przeglądarce za potwierdzeniem).

### 5.2 Źródło danych i format

- **Źródło prawdy dla audio:** rejestr `client/Assets/MetinAR/Audio/LICENSES.md` (METINAR-018, PR #43): kolumny
  `File`, `Origin`, `Author`, `License`, `Source`, `SHA-256`. W przyszłości analogiczne rejestry dla innych assetów
  zewnętrznych (modele, tekstury, czcionki, ikony; METINAR-009 `ASSET_SOURCE.txt`) i bibliotek kodu.
- Dla wierszy `third_party` z `CC-BY-4.0` rejestr musi zawierać dane atrybucji w stylu „TASL”: **tytuł** utworu,
  **autor**, **źródło** (URL), **licencja** (nazwa + URL) i **zmiany** (np. „przycięto, znormalizowano”).
  Propozycja: dodać do rejestru kolumny `Title`, `Attribution` (gotowy tekst do wyświetlenia) i `Changes`
  albo trzymać je w osobnym pliku obok — decyzja techniczna w etapie S13.
- **Build:** narzędzie edytora generuje z rejestrów jeden plik `credits.json` (zasób w buildzie, np. `Resources`/`StreamingAssets`):

```json
{
  "version": 1,
  "generatedFromSha": "<commit>",
  "sections": [
    { "id": "audio", "titlePl": "Dźwięk", "entries": [
      { "title": "<tytuł>", "author": "<autor>", "license": "CC-BY-4.0",
        "licenseUrl": "https://creativecommons.org/licenses/by/4.0/", "sourceUrl": "<url>",
        "changes": "<zmiany lub brak>", "files": ["SFX/..."] } ] },
    { "id": "project", "titlePl": "Twórcy MetinAR", "entries": [ ] }
  ]
}
```

- Ekran pokazuje: twórców projektu (właściciel, zespół), potem sekcje assetów zewnętrznych pogrupowane wg autora.
  Pliki `MetinAR-Owned` są zbiorczo opisane jako własne; `CC0-1.0` wymieniane dobrowolnie (uprzejmość, nie wymóg).

### 5.3 Bramka builda (atrybucja wymagana)

- **FAIL** (test w `tests/AssetPipeline.Tests` i krok przed buildem Android): asset `CC-BY-*` bez autora, źródła,
  URL licencji albo tekstu atrybucji; plik w `credits.json` niezgodny z rejestrem; licencja spoza listy dozwolonej.
- **FAIL:** asset zewnętrzny w buildzie bez wiersza w rejestrze (już dziś dla audio w `AudioRepositoryTests`).
- **WARN:** `CC0-1.0` bez autora (dozwolone, ale zalecane), wpis w rejestrze, którego plik nie trafia do buildu.
- Raport bramki dołączany do raportu QA (METINAR-011). Brak `credits.json` w APK = FAIL w audycie APK.
