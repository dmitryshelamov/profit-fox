# Tasks

## 1. Домен: исход с ценой и закрытие по сроку

- [x] 1.1 `TradedIdeaOutcome` получает `ExitPrice`; `TradedIdeaOutcome.Of` берёт `track.ExitPrice`: тест в `IdeaOutcomesTests`/`TradedIdeaOutcomeTests` красный → зелёный
- [x] 1.2 `IdeaTradeStatus.ClosedAtExpiry`, `IdeaTradeLeg.Expiry`, нога `Expiry` у `IdeaTrade` (`Exited`, `Settled`, `Result`): тест `IdeaTradeTests` «выход ногой Expiry закрывает сделку по сроку с результатом» красный → зелёный
- [x] 1.3 `PaperFill.Expire`: открытая сделка с исходом «Истекла» с ценой закрывается по `ExitFill` с комиссией taker и временем `StatusAt`; сценарий «Идея истекла» спеки (0.5 по 100, выход 101.00 → 100.9495) в `PaperFillTests` красный → зелёный
- [x] 1.4 `PaperFill.Step`: стоп и цель в минуте истечения главнее; сделка, оставшаяся открытой при `StatusAt <= OpenTime + 1 мин`, закрывается по сроку; истечение позади курсора закрывает до минуты; исход без цены и «Ждёт входа» не трогаются — по тесту на каждый сценарий спеки «Закрытие по сроку» и «после перерыва», тест `Trade_has_no_lifetime…` заменяется
- [x] 1.5 `IdeaJournalTotals`: `Expired` и `ClosedAtExpiry` в `settled`; сценарий «Закрытые по сроку в итогах учебных» в `IdeaJournalTotalsTests` красный → зелёный

## 2. Сканер: исходы минуты на сделках в памяти

- [x] 2.1 `LiveAutos.RunAsync(end, born, births, outcomes)` кладёт исходы на `working` по `IdeaId` до `StepAsync`; тест `LiveAutosTests` «идея истекла в минуту — сделка закрыта по сроку в эту минуту» красный → зелёный
- [x] 2.2 `LiveIdeas.RunAsync` считает `Outcomes(changed)` до вызова автоматов и передаёт их; `NoAutos` и тесты `LiveIdeasTests` обновлены; тест «сделка закрыта по сроку в минуту истечения идеи» через `LiveIdeas` красный → зелёный
- [x] 2.3 `LiveAutos.StepAsync`: сделка пары вне `market` с исходом «Истекла» с ценой закрывается `PaperFill.Expire` без минут; тест «Пара снята с торгов» красный → зелёный
- [x] 2.4 Перезапуск: `StartOver` + исход в хранилище → сделка закрыта по сроку до шага минут; тест в `LiveAutosTests` красный → зелёный

## 3. Хранилище и API

- [ ] 3.1 Миграция `AddPaperTradeExpiryMigration` (колонки `expiry_*`, `idea_exit_price` в обеих таблицах, новый `CK_idea_paper_trades_status`, дозапись цены из `ideas`); интеграционный тест: исход без цены до миграции получает `exit_price` идеи
- [ ] 3.2 `IdeaTradeDb`, конвертеры и запросы (`PaperTradeQueries` Columns/Insert/Update, `WriteOutcomes` обеих таблиц, `IdeaTradeRepositoryQueries`) читают и пишут `expiry_*` и `idea_exit_price`; `IdeaTradeCodes` знает `closed_at_expiry`; интеграционные тесты репозиториев: учебная сделка «по сроку» и исход с ценой читаются обратно такими же
- [ ] 3.3 `IdeaJournalTotalsResponse.expired`; интеграционный тест `GET /api/idea-trades`: закрытая по сроку учебная сделка приходит с `closed_at_expiry`, результатом и временем до исхода, итоги учебных считают «по сроку»
- [ ] 3.4 `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` зелёные

## 4. Frontend

- [ ] 4.1 `npm run generate:api` при запущенном API: `schema.d.ts` с `expired` в итогах; `statusLabel` «Закрыта по сроку»; тест `status.test.ts` красный → зелёный
- [ ] 4.2 Строка итогов «по цели · по стопу · вручную · по сроку» с долями от четырёх; `journal.test.ts` и фикстуры (`expired`) красный → зелёный
- [ ] 4.3 e2e `idea-trades.spec.ts`: закрытая по сроку учебная сделка в таблице с подписью и в итогах; `npm run lint`, `format:check`, `typecheck`, `test`, `build` зелёные

## Workflow follow-up

- Архивировать change перед PR (`/opsx:archive`), чтобы спеки и код ушли одним squash-коммитом.
- После деплоя проверить на проде: 18 открытых учебных сделок `paper-test` закрыты «по сроку» около 08:05 UTC, бюджет автомата свободен, новые учебные сделки появляются.
