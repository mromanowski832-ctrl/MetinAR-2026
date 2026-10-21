# Fixtures z PR #61 (maks. poziom 99)

Kopie bez zmian plików `docs/design/tables/` z gałęzi `docs/040b-max-level-99` (PR #61), commit
`1f466e9851c3b065ae729881e6f5000af1d9deec`: `exp_curve.csv`, `mob_level_stats.csv`, `sanity_warrior_ttk.csv`,
`classes_skills.json`.

Powód: właściciel ustalił maks. poziom 99 (2026-10-21), a tabele 1–99 są jeszcze tylko w otwartym PR #61.
Formuły są te same co w specu z PR #44, wartości poz. 1–60 identyczne. Testy METINAR-019 (#46) sprawdzają
kod względem tych kopii **oraz** względem aktualnych tabel w `docs/design/tables/` w repozytorium.

Po merge'u PR #61 te kopie mają być identyczne z `docs/design/tables/` (sprawdza to test
`Pr61Fixtures_MatchRepoTablesOnOverlap`, który porównuje wspólny zakres poziomów). Liczb tu nie edytujemy;
rozbieżności zgłaszamy w #41.
