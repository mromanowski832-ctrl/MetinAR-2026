# METINAR-017 — Tryby gry (AR/FPP i mapa GPS) oraz model danych

> **Status: PROPOZYCJA do akceptacji właściciela (#40). Bez implementacji.** Indeks: [README.md](README.md).

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
| `ItemDefinition` | `id`, kategoria, slot, typ, statystyki bazowe vs iL, wymagania, stos, cena, ikona, prefab FPP |
| `AffixDefinition` | `id`, sloty, zakres wartości vs iL, limit |
| `RarityDefinition` | kolor, liczba affiksów, mnożnik, wagi dropu |
| `MobDefinition` | `id`, nazwa, rodzina, ranga, poziom lub reguła poziomu, agresja, aggro m, ataki, hitboxy, `dropTableId`, prefab |
| `DropTableDefinition` | talary, rzuty, pule z wagami |
| `NpcDefinition` / `ShopDefinition` | rola, oferta, ceny |

## 3. Dane gracza (stan)

```text
PlayerProfile { playerId, classId, pathId?, level, exp, statPointsAllocated{Might,Finesse,Spirit,Vigor},
                freeStatPoints, skillRanks{skillId→rank}, freeSkillPoints, skillLoadout[ATAK/AURA/1/2],
                hp, mp, talers, inventory[slotIndex→ItemStack], inventoryCapacity, equipment{EquipSlot→ItemInstance},
                lootStash[], questState{}, explorationDaily{dateUtc, metersCredited, cellsDiscovered}, revision }
ItemInstance  { instanceId (GUID), definitionId, itemLevel, rarity, temper, affixes[{affixId,value}], locked }
ItemStack     { definitionId, quantity }  // dla przedmiotów stosowalnych
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
| Handel, rynek, gildie, PvP | nie | **tak**, jeśli powstaną (Q8, Q9) | |
| Definicje (tabele) | tak | wersjonowane, wspólne | spójność |
