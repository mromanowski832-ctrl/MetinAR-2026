# Errata 1 do audytu MetinAR z 2026-10-21

Audyt `AUDIT_2026-10-21.md` i aktualizacja `AUDIT_2026-10-21_UPDATE_1.md` zostają bez zmian, jako zapis stanu z chwili ich powstania. Poniżej są poprawki według stanu na 2026-10-21 15:35 (UTC+2). Źródła: uwagi QA w [#26 6317267033](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/26#issuecomment-6317267033) i decyzje Kierownika w [#33 6317288720](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/33#issuecomment-6317288720).

| # | Miejsce | Było | Jest |
|---|---|---|---|
| E1 | `AUDIT_2026-10-21.md`, P-08, kolumna kryterium (ok. l. 309) | „3 kolejne przebiegi AR smoke PASS” | Zamknięcie #66 wymaga **10** kolejnych przebiegów bez T09, jeśli poprawka wykazuje przyczynę (log i test regresji odtwarzający błąd), albo **20** przy poprawce na ślepo. Decyzja: [#66 6317288348](https://github.com/mromanowski832-ctrl/MetinAR-2026/issues/66#issuecomment-6317288348). Uzasadnienie: przy 4/18 przedział ufności 95% dla częstości to ok. 6–48%. |
| E2 | `UPDATE_1` §3, testy kotwicy pająka | A1–A6 | **S3-A1…S3-A6**, żeby nie myliły się z punktami edytora A1–A6 w `docs/qa/METINAR-031-FPP-ACCEPTANCE.md` (zmiana w #77, `1dcda01`). |
| E3 | `UPDATE_1` §5, wiersz „Walidator APK” | status PLAN, „ARCore i strażnik dziś nie istnieją (N-01)” | Strażnik platformy Android i walidator 11/11 rejestracji ARCore są na main w `f3714a1` (#32, `cd1eed1`). ApkAudit z liczeniem rejestracji ARCore jest w PR #75 (status DO INTEGRACJI). Do czasu scalenia #75 rejestracje liczy się ręcznie (DEVICE-ACCEPTANCE-LOOP-V0 P3). |
| E4 | `UPDATE_1` §5, wyjątki na telefonie | „wyjątek na telefonie = FAIL” | Dziś skrypt device smoke daje FAIL tylko przy crashu, ANR albo fatal. Wyjątki `MetinAR.*` widać jedynie jako WARN `unityErrors`, więc reguła działa przez ręczny przegląd (L14). Zatwierdzono zmianę: device smoke ma dawać FAIL przy wyjątku z ramką `MetinAR.` (właściciel zadania: QA). |
| E5 | Nowy fakt | — | APK QA z `e18df69` ma 0/11 rejestracji ARCore (zbudowany bez `-buildTarget Android`, ta sama przyczyna co w `f194aa9`). Ma zakaz instalacji na telefonie. |
