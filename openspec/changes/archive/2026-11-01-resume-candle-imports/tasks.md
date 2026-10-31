# Tasks

Новая логика — строго red → green → refactor (см. `CLAUDE.md`): сначала падающий тест, потом минимальный код. Каждая
задача закрывается, когда `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln` и
`dotnet format backend/ProfitFox.sln --verify-no-changes` проходят.

## 1. Недостающие диапазоны в домене и хранилище

- [x] 1.1 Доменная функция недостающих диапазонов в `IntervalExtensions` (решение 1); unit-тесты на эталонных данных:
  пустое хранилище — весь период, полное покрытие — пусто, пропуски внутри, «голова» до первой и «хвост» после последней
  свечи, `from` вне сетки, интервалы `60`, `W`, `M` (сценарий «Календарный интервал» из спека)
- [x] 1.2 `ICandleRepository.GetOpenTimesAsync` и SQL в `CandleRepositoryQueries`; интеграционный тест на TimescaleDB:
  open time только своей серии (категория, символ, интервал) в `[from, to)` по возрастанию

## 2. Недоступность биржи в адаптере

- [x] 2.1 `ExchangeUnavailableException : ExchangeException` в `Application/Exceptions`; адаптер Bybit бросает его при
  исчерпанных повторах временной ошибки страницы, `ExchangeResilienceExecutor` — при разомкнутой цепи (решение 3);
  unit-тесты адаптера на оба случая и на то, что окончательный отказ Bybit остаётся `ExchangeException`; `503` при
  проверке символа не меняется (существующие тесты `CandleImportsApiTests` зелёные)
- [x] 2.2 Отмена в адаптере (решение 3): unit-тест через `StubHttpHandler` с отменённым токеном ожидает
  `OperationCanceledException` из `GetCandlePagesAsync`. Если тест красный, потому что Bybit.Net вернул ошибку
  `CancellationRequested`, `Unwrap` бросает `OperationCanceledException`; если зелёный сразу — кода не добавлять, тест
  остаётся как фиксация поведения SDK

## 3. Выполнение задачи

- [x] 3.1 `RunCandleImport` загружает только недостающие диапазоны (решение 2); unit-тесты с моками: порт истории
  вызывается только для диапазонов без свечей; полностью загруженный выровненный период в прошлом — `Succeeded` с нулём
  свечей и без вызовов порта; незакрытая свеча в конце периода — один вызов порта за хвост, ничего не сохранено,
  `Succeeded` с нулём; свеча, которую порт вернул вне недостающего диапазона, но внутри периода, не сохраняется и не
  считается; `ImportedCandles` — число свечей, сохранённых задачей; существующие тесты `RunCandleImportTests` переписаны
  под новое поведение и зелёные
- [x] 3.2 Автоповтор при `ExchangeUnavailableException` с паузами 1, 5, 15, 30, 60 с через `TimeProvider` (решение 3);
  unit-тесты с `FakeTimeProvider`: биржа восстановилась — `Succeeded`, а новая попытка заново вычисляет недостающие
  диапазоны; недоступность после пятой паузы подряд — `Failed` с её текстом; страница с сохранённой свечой начинает
  паузы заново, и больше пяти разрозненных недоступностей с продвижением между ними дают `Succeeded` (сценарий
  «Разрозненные сбои»); `ExchangeException` и прочие ошибки — сразу `Failed` без пауз; задача во время пауз `Running`, а
  `StartedAt` не меняется
- [x] 3.3 Отмена через `CancellationToken` не переводит задачу в `Failed` и пробрасывается: ошибки ловятся с фильтром
  `when (!cancellationToken.IsCancellationRequested)` (решения 3 и 4); unit-тесты: после отмены последнее сохранённое
  состояние задачи — `Running`; отмена сразу после сохранения страницы всё равно сохраняет задачу с новым
  `ImportedCandles` (обновление с `CancellationToken.None`, решение 2)

## 4. Возобновление после перезапуска

- [x] 4.1 Поле `CandleImportJob.InterruptionsWithoutProgress`, миграция колонки `interruptions_without_progress
  integer NOT NULL DEFAULT 0`, чтение и запись в `CandleImportJobRepository`; генератор `CandleImportJobGenerator`
  учитывает поле; интеграционные тесты: колонка есть после миграции, значение сохраняется и читается
- [x] 4.2 `RunCandleImport` обнуляет счётчик в обновлении задачи после страницы хотя бы с одной сохранённой свечой
  (решение 2); unit-тест: страница со свечами обнуляет счётчик, пустая — не меняет
- [x] 4.3 `ICandleImportJobRepository.GetUnfinishedAsync` вместо `FailUnfinishedAsync`; интеграционный тест с явно
  разными `CreatedAt`: задачи `Pending` и `Running` по `CreatedAt`, без `Succeeded` и `Failed`
- [x] 4.4 Команда `RequeueInterruptedImports` вместо `FailInterruptedImports`, её вызывает
  `CandleImportInitializationService` (решение 4); константа `InterruptedByRestart` переезжает в её обработчик;
  unit-тесты: `Running` → `Pending` со счётчиком +1 и в очереди, `Pending` в очереди без изменений, порядок постановки —
  по `CreatedAt` (сценарий «Порядок возобновления»), третье прерывание без продвижения — `Failed` с
  `Interrupted by service restart.`, `FinishedAt` = текущий момент и не в очереди; `FailInterruptedImports` и его тесты
  удалены
- [x] 4.5 Интеграционный тест перезапуска (`RestartTests`) переписан под сценарий «Перезапуск во время загрузки»: задача
  `Running` после старта сервиса докачивает только недостающее и завершается `Succeeded`
- [x] 4.6 `CandleImportBackgroundService` ловит ошибки задачи с фильтром `when (!stoppingToken.IsCancellationRequested)`
  и обновляет комментарий о судьбе задачи после остановки (решение 4); unit-тест в
  `CandleImportBackgroundServiceTests`: отмена при остановке не логируется как ошибка задачи

## 5. Ручной повтор

- [x] 5.1 `NotFoundException` и `ConflictException` в `Application/Exceptions`, команда `RetryCandleImport` (решение 5);
  unit-тесты: `Failed` → `Pending` с пустыми `Error` и `FinishedAt`, обнулённым счётчиком и в очереди (сценарий «Ручной
  повтор сбрасывает отсчёт прерываний»); `Pending`, `Running`, `Succeeded` — `ConflictException` без изменения задачи;
  неизвестная задача — `NotFoundException`
- [x] 5.2 `POST /api/market-data/candle-imports/{id}/retry` в `CandleImportsController`; интеграционные тесты API:
  `202` с `Location` и задачей, `404` для неизвестной, `409` для задачи не в `Failed`

## 6. Сквозная проверка

- [x] 6.1 `FakeBybitHandler` отдаёт свечи по `start`/`end` запроса kline, а не из очереди страниц, и ведёт журнал
  запросов kline (решение 7); существующие интеграционные тесты переведены на него и зелёные
- [x] 6.2 Сквозные тесты (`EndToEndTests`) на подменённом Bybit: повторная задача на загруженный выровненный период в
  прошлом — `Succeeded` без запросов kline, сохранённые свечи не изменились; задача на частично загруженный период
  запрашивает только недостающие диапазоны (по журналу запросов); `Failed` из-за недоступности Bybit после `retry`
  докачивает недостающее
- [x] 6.3 Пройти по диффу: каждая ветка и проверка нового кода требуются каким-то тестом (`CLAUDE.md`); обновить
  `docs/conventions.md`, если раскладка из решения 6 добавила новое правило

## Workflow follow-up

- Ревью артефактов владельцем; правки — через `/opsx:update`.
- `/opsx:archive` перед pull request, чтобы код и обновлённая `openspec/specs/market-data-import` попали в `main` одним
  squash-коммитом.
