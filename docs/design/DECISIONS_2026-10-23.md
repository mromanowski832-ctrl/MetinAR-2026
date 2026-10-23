# Decyzje 2026-10-23

Stan: 2026-10-23 04:10 (Europe/Warsaw). Źródło nadrzędne: główna specyfikacja właściciela, PR #96 (https://github.com/mromanowski832-ctrl/MetinAR-2026/pull/96),
plik `docs/design/OWNER_SPEC_2026-10-23.md` @ `2a039cd` (https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2a039cd8bad5eaa4547caddb4981c6b68429dfe6/docs/design/OWNER_SPEC_2026-10-23.md).
Decyzje są wiążące. Liczby nie są tu zmieniane: wartości zostają w szczegółowych dokumentach projektowych.

## D1. Tarcza wojownika (rozstrzyga sprzeczność K6) — decyzja właściciela, 04:05

Cytat właściciela:

> „Wojownik otrzymuje Lipowy Puklerz już na poziomie 1, zgodnie z zatwierdzonym ITEMS_COMBAT_SPEC.md.
> 1. Poziomy 1–9: Wojownik może używać miecza 1H i puklerza oraz podstawowego BLOKU.
> 2. Od poziomu 10 ścieżka Bastion rozwija obronę tarczą, kontry i umiejętności defensywne.
> 3. Ścieżka Rozłam nie korzysta z tarczy. Przy wyborze tej ścieżki założony puklerz wraca do ekwipunku.
> 4. Broń dwuręczna zawsze wyklucza tarczę.
> 5. Animacja AURY musi umożliwiać chwyt miecza obiema dłońmi, również gdy postać ma wyposażoną tarczę. Nie może dochodzić do przenikania modeli.”

Skutki w dokumentach:
- [CLASSES_SKILLS.md](CLASSES_SKILLS.md) §1 (kolumna „Druga ręka”), §2 (wybór ścieżki) i §4 (Wojownik) — zasady 1–4.
- [ITEMS_COMBAT_SPEC.md](ITEMS_COMBAT_SPEC.md) §2 (ograniczenia slotów) — zasady 3–4; `starter_buckler` (§7) bez zmian.
- Zasada 5: kryteria odbioru #93 (https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/93) i #91 (https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/91); spec §5 (https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2a039cd8bad5eaa4547caddb4981c6b68429dfe6/docs/design/OWNER_SPEC_2026-10-23.md#L215-L241).
- Blok tarczą: #94 (https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/94) odblokowany.

## D2. Budżet modeli NPC (rozstrzyga sprzeczność K7) — decyzja kierownika produkcji, standard techniczny

Zapis: #86, komentarz https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/86#issuecomment-6355602011.

- **Kowal i NPC pełnoprawne:** 10 000–20 000 trójkątów LOD0, tekstury do 2048², LOD0/1/2 — zgodnie z METINAR-009 §1.1 (https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/METINAR-009-ASSET-PIPELINE.md#L38) i streszczeniem polecenia właściciela 03:37.
- **NPC drugoplanowe:** budżet z [ITEMS_COMBAT_SPEC.md](ITEMS_COMBAT_SPEC.md) §8 (≤ 10 000, 1024², 1 LOD).
- Do której grupy należy Wędrowny Handlarz (asset z PR #45, 3 LOD): **BRAK DANYCH** — decyzja w #86 przy odbiorze assetu.

## D3. Bezpieczeństwo AR: walka bez chodzenia — polecenie właściciela 04:05, zatwierdzenie 04:10

Cytat właściciela:

> „Sprawdź też bezpieczeństwo: walka AR nie może wymagać fizycznego chodzenia z telefonem przed oczami. Komunikat „Podejdź” nie powinien zmuszać gracza do przemieszczania się w aktywnym widoku kamery.”

Podstawa w specyfikacji: §2A „Walka nie może wymagać chodzenia, biegania lub cofania się z telefonem przed oczami.” (https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2a039cd8bad5eaa4547caddb4981c6b68429dfe6/docs/design/OWNER_SPEC_2026-10-23.md#L67).

**Zatwierdzenie właściciela 04:10 — 8 zasad wiążących:**

1. Gracz przemieszcza się fizycznie wyłącznie podczas eksploracji mapy GPS.
2. Podczas walki AR nie musi chodzić, podchodzić ani cofać się.
3. Potwór jest stabilnie osadzony w przestrzeni AR, ale może samodzielnie poruszać się w ramach animacji i AI.
4. Jeśli potwór znajduje się poza zasięgiem miecza, powinien sam podejść do gracza.
5. Podejście musi być płynne i animowane, bez teleportowania modelu.
6. Potwór nie może podchodzić tak blisko, żeby zasłaniał cały ekran.
7. Po utracie śledzenia AR zatrzymujemy ruch przeciwnika i walkę. Po odzyskaniu śledzenia przywracamy prawidłowe położenie bez utraty HP i postępu spotkania.
8. Zasięg ataku musi odpowiadać rzeczywistej geometrii wirtualnego ostrza.

Ustalenia techniczne kierownika produkcji (zgodne z zasadami 1–8):
- Komunikaty „Podejdź”, „Podejdź bliżej” i „Odsuń się” znikają z widoku walki AR. Na mapie GPS podchodzenie do potwora zostaje (spec §2A, https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2a039cd8bad5eaa4547caddb4981c6b68429dfe6/docs/design/OWNER_SPEC_2026-10-23.md#L48-L49), przy wyłączonej kamerze.
- **Bez teleportu i bez ponownego stawiania potwora w trakcie walki** (zastępuje wcześniejszy wariant „Przesuń potwora bliżej”). Potwór poza zasięgiem podchodzi animowanym ruchem (stan `Approach` z [MOBS_NPC.md](MOBS_NPC.md) §4.2, aggro #74).
- Pozycja docelowa, odległość minimalna, histereza i prędkość: [AR_NO_WALK_ANALYSIS_2026-10-23.md](AR_NO_WALK_ANALYSIS_2026-10-23.md) (standoff 1,32 m, minimum 1,10 m, start podejścia 1,47 m, 0,6 m/s — wartości PROTOTYPE do testu A31).
- Kryterium odbioru (dosłownie): „stoję nieruchomo w bezpiecznym miejscu, rozpoczynam walkę AR, potwór podchodzi w zasięg, wykonuję combo, zadaję obrażenia i mogę zakończyć walkę bez fizycznego chodzenia” — wymagany fizyczny test na A31; testy jednostkowe i CI go nie zaliczają.

Zastępuje decyzję z #31 (18:58, „v0 mob stands still, the player walks up”) wdrożoną w PR #80 (https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/ee48cb99bf64fc542fcf3cda38b889dfc80581be/client/Assets/MetinAR/Scripts/ArEncounter/ArApproachPrompt.cs#L6-L16).
Zadanie: #97 (https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/97), P0 ETAP I. Sprzeczność K8 w [SPEC_CONFLICTS_2026-10-23.md](SPEC_CONFLICTS_2026-10-23.md).
