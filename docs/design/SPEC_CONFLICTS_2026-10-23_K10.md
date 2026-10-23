# SPEC_CONFLICTS 2026-10-23 — pozycja K10 (do dołączenia)

Stan: 2026-10-23 (Europe/Warsaw). Ten plik zawiera **tylko jedną nową pozycję, K10**, w formacie tabeli
„## 1. Rzeczywiste sprzeczności (K, K-R)” z `docs/design/SPEC_CONFLICTS_2026-10-23.md`.

Plik `SPEC_CONFLICTS_2026-10-23.md` nie jest jeszcze na `main`. Jest tylko na gałęzi `docs/owner-spec-2026-10-23`
(PR #84, otwarty, head `0f027a1`). Żeby nie edytować cudzej gałęzi i nie wywołać konfliktu scalania, K10 trafia tutaj.
Po scaleniu #84 wiersz poniżej należy przenieść na koniec tabeli w §1 tamtego pliku (po K9), bez zmian w K1–K9,
a ten plik zostawić jako odnośnik albo usunąć decyzją właściciela.

Oznaczenia jak w pliku głównym: **K-R** — sprzeczność między dwoma plikami repo, której specyfikacja nie rozstrzyga →
**do decyzji właściciela, nie zgadujemy**.

## 1. Rzeczywiste sprzeczności (K, K-R)

| # | Źródło A (repo @ e1bdf80) | Źródło B | Na czym polega sprzeczność | Typ | Rozstrzygnięcie / potrzebna zmiana |
|---|---|---|---|---|---|
| K10 | [tables/shop_starter_merchant.csv L14](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/tables/shop_starter_merchant.csv#L14): `treatise_proficiency_<classId>` w sklepie **Wędrownego Handlarza**, kategoria **`consumable`**, stos 20, cena `round5(60 × L)` min. 1200, 2/dobę | [tables/skill_tier_items.csv L2](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/tables/skill_tier_items.csv#L2): ten sam id jako **przedmiot stopnia umiejętności** (`tierId` = `proficiency`), źródła `drop\|quest_daily\|shop_trainer`, stos 20; tak samo [ITEMS_COMBAT_SPEC.md L58](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/ITEMS_COMBAT_SPEC.md#L58) (grupa `SkillTier`) i [ECONOMY.md L35](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/ECONOMY.md#L35) („u trenera”); [ITEMS_COMBAT_SPEC.md L103](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/e1bdf800f87d25f98d9363fb9fe45248fad822aa/docs/design/ITEMS_COMBAT_SPEC.md#L103) podaje „trener / Wędrowny Handlarz” | (1) kategoria: materiał eksploatacyjny (`consumable`) czy osobna grupa przedmiotów stopni (`SkillTier`); (2) kto sprzedaje: Wędrowny Handlarz (tabela sklepu) czy Trener umiejętności (`shop_trainer`, ECONOMY L35). Specyfikacja wymienia tylko „Traktaty Biegłości” ([spec §7 L349](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2a039cd8bad5eaa4547caddb4981c6b68429dfe6/docs/design/OWNER_SPEC_2026-10-23.md#L349)) i obu NPC osobno ([spec §11 L474–475](https://github.com/mromanowski832-ctrl/MetinAR-2026/blob/2a039cd8bad5eaa4547caddb4981c6b68429dfe6/docs/design/OWNER_SPEC_2026-10-23.md#L474-L475)), bez rozstrzygnięcia | K-R | **Do decyzji właściciela.** Zgodne w obu: id, stos 20, cena, limit 2/dobę, od poz. 20. Kontrakt SaveGame #71 nie zakłada żadnej wersji: sprawdza tylko id i stos 20; kategoria traktatu nie wpływa na żadną regułę zapisu (reguły używają tylko `equipment` i `material`, a traktat nie jest żadnym z nich w obu wersjach). Po decyzji poprawić jedną z tabel |
