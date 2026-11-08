# Tasks

## 1. Конец жизни идеи в домене

- [x] 1.1 Red → green: `IdeaTrack.LifeEndsAt` — `EnteredAt + LifeCandles × CandleMinutes`, нет без входа; тесты в
  `IdeaTrackerTests` (или рядом с `IdeaTrack`) на 15m × 16 свечей от 10:00 → 14:00 и на идею без входа. Проверка:
  `dotnet test --filter IdeaTracker` зелёный, оба теста были красными
- [x] 1.2 Refactor: `IdeaTracker.Exit` сравнивает конец минуты с `LifeEndsAt`; поведение и тесты не меняются. Проверка:
  все тесты `IdeaTrackerTests` и `ExitOutcomesAgreementTests` зелёные

## 2. Чтение идей по id

- [ ] 2.1 `IIdeaRepository.GetManyAsync(IReadOnlyCollection<Guid> ids)` и его Dapper-реализация с запросом в
  `IdeaRepositoryQueries`: отдаёт хранящиеся идеи из списка, отсутствующие пропускает. Интеграционный тест в
  `IdeaRepositoryTests` (две идеи из трёх id, одна удалена). Проверка: тест сначала красный, потом зелёный

## 3. Конец идеи в журнале

- [ ] 3.1 Red → green: `GetIdeaJournalQueryHandler` дочитывает идеи сделок в работе (реальных и учебных) одним вызовом
  `GetManyAsync` и кладёт в `IdeaJournalTrade.IdeaEndsAt` `LifeEndsAt` активной идеи. Тесты в `GetIdeaJournalTests` по
  сценариям спеки `idea-trades-journal` «Конец идеи у сделки в работе»: активная идея, ждёт входа, закрылась раньше
  сделки, учебная; закрытые сделки без момента и без чтения идей; без сделок в работе идеи не читаются. Проверка: каждый
  тест был красным
- [ ] 3.2 `IdeaTradeResponse.IdeaEndsAt` (`ideaEndsAt`) и строка в его `<summary>`; контрактный/интеграционный тест
  журнала, если он сверяет поля ответа. Проверка: `dotnet build`, `dotnet test backend/ProfitFox.sln`,
  `dotnet format backend/ProfitFox.sln --verify-no-changes`

## 4. Колонка «До конца идеи»

- [ ] 4.1 Перегенерировать `frontend/src/shared/api/generated/schema.d.ts` (`npm run generate:api` против локального
  API) — в `IdeaTrade` появляется `ideaEndsAt`. Проверка: `npm run typecheck`
- [ ] 4.2 Red → green: `ideaLeftText(trade, now)` в `idea-trades/model/journal.ts` — «закрыта» при исходе идеи,
  обратный отсчёт по `ideaEndsAt` через `durationText` («< 1 мин» после конца), иначе «—»; тесты в `journal.test.ts` по
  сценариям спеки `idea-trades-interface`. Проверка: `npm test` — тесты были красными
- [ ] 4.3 Колонка «До конца идеи» после «В сделке» в `OpenTradesTable` на том же `useNow`; e2e-сценарий в
  `frontend/e2e/idea-trades.spec.ts` на таймер и «закрыта». Проверка: `npm run lint`, `npm run format:check`,
  `npm run typecheck`, `npm test`, `npm run build`, `npm run test:e2e`

## 5. Проверка целиком

- [ ] 5.1 Локально открыть «Сделки» с учебной сделкой по активной идее и убедиться, что колонка тикает и совпадает с
  моментом истечения идеи на её странице пары. Проверка: скриншот таблицы

## Workflow follow-up

- `/opsx:archive` перед pull request
- PR `feat(idea-trades): show the time left of the idea of an open trade`
