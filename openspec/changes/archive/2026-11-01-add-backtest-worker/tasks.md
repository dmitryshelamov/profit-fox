# Tasks

Новая логика — строго red → green → refactor (см. `CLAUDE.md`): сначала падающий тест, потом минимальный код. Каждая
задача закрыта, только когда build, тесты и `dotnet format --verify-no-changes` проходят.

## 1. Соглашения и каркас Worker'а

- [x] 1.1 Добавить в `docs/conventions.md` проект `ProfitFox.Worker`, `InfrastructureRegistrar.ConfigureWorker`, секцию
  настроек `Worker`, таблицу `backtest_chunks` в `Repositories/Backtests` и `WorkerHost` в `Tests.Integration` (design,
  решения 1, 9, 11); проверить, что документ описывает каждую новую папку и тип из дизайна
- [x] 1.2 Создать проект `backend/src/ProfitFox.Worker` (generic host, Serilog из конфигурации, `appsettings*.json`) и
  добавить его в `ProfitFox.sln`; разделить `InfrastructureRegistrar` на `Configure` и `ConfigureWorker`, фоновые
  сервисы перенести в `ConfigureWorker`; проверить `dotnet build backend/ProfitFox.sln`
- [x] 1.3 `WorkerSettings` и проверка при запуске «продление меньше аренды»; integration-тест «Продление не короче
  аренды»: Worker не запускается, в логе событие `Fatal`
- [x] 1.4 Лог Worker'а по правилам API; integration-тесты «Сбой куска в Production» (JSON-строка `Error` с
  идентификатором, Worker берёт следующую работу) и «Ошибка запуска Worker'а»

## 2. Работа уходит из API в Worker

- [x] 2.1 `WorkerHost` в `Tests.Integration` на базе, `FakeBybitHandler` и `FakeTimeProvider` фабрики;
  `ProfitFoxFactory` по умолчанию запускает один `WorkerHost`, `RunImports = false` заменить на «без Worker'а»;
  проверить, что все нынешние integration-тесты зелёные
- [x] 2.2 Удалить `ICandleImportQueue`, `IBacktestQueue`, `CandleImportQueue`, `BacktestQueue`, команды
  `RequeueInterrupted*` и их сервисы запуска; Worker опрашивает `Pending` раз в `Worker:PollInterval`; integration-тесты
  «Без Worker'а» и «Worker запустили позже» для бэктеста и задачи загрузки
- [x] 2.3 Ручной повтор задачи загрузки только возвращает её в `Pending`; integration-тест «Повтор упавшей задачи» с
  Worker'ом зелёный

## 3. Аренда задач загрузки

- [x] 3.1 Миграция: `candle_import_jobs.lease_owner`, `leased_until`, переименование `stalled_restarts` в `lost_leases`;
  integration-тест миграции на базе с задачей `Running` (аренда пустая, счётчик сохранён)
- [x] 3.2 Захват задачи под `pg_advisory_xact_lock`, продление, запись прогресса только при своей аренде;
  integration-тесты «Две задачи и два Worker'а» и «Порядок после сбоя»
- [x] 3.3 Перехват истёкшей аренды и `lost_leases`: прибавка без сохранённых свечей, `Failed` на третьей, обнуление при
  ручном повторе; unit-тесты на `CandleImportJob` и integration-тесты «Worker упал во время загрузки», «Задача без
  продвижения», «Задача с продвижением», «Ручной повтор сбрасывает отсчёт»
- [x] 3.4 Переписать сценарии `RestartTests` для загрузок в тесты перехвата аренды; проверить, что тестов на
  повторную постановку при старте не осталось

## 4. Куски бэктеста

- [x] 4.1 Деление сетки на куски по `ChunkSize`; unit-тест «Деление на куски» (`12` комбинаций по `5` → `0–4`, `5–9`,
  `10–11`)
- [x] 4.2 Миграция `backtest_chunks`, удаление `backtests.stalled_restarts`, перевод бэктестов `Running` в
  `LoadingCandles`; integration-тест миграции: бэктест `Running` с пятью результатами становится `LoadingCandles` и
  сохраняет результаты
- [x] 4.3 Шаг загрузки без аренды (design, решение 5): создание задачи загрузки одной транзакцией при пустом
  `import_job_id`, `Failed` при упавшей загрузке, переход в `Running` с кусками условно по `LoadingCandles`;
  integration-тесты «Свечей не хватает», «Загрузка упала», «Период до листинга» и тест двух одновременных шагов
  (одна задача загрузки, один набор кусков)
- [x] 4.4 Захват куска с арендой, продление, сохранение пачки только при своей аренде и `Running`,
  `ON CONFLICT DO NOTHING`, прибавка вставленных строк; integration-тесты «Занятый кусок» и «Аренда перехвачена»
- [x] 4.5 Сдача последнего куска и завершение бэктеста отдельным оператором; integration-тест: два Worker'а
  одновременно сдают последние куски, бэктест `Succeeded` ровно один раз
- [x] 4.6 Порядок работы: перебор бэктестов от старых к новым до первой работы; integration-тесты «Очередь» и
  «Свободный Worker начинает следующий бэктест»
- [x] 4.7 Кэш свечей и снимка стратегии последнего бэктеста в Worker'е; unit-тест: второй кусок того же бэктеста не
  читает свечи из репозитория
- [x] 4.8 Скорость куска и сумма по кускам с действующей арендой; integration-тест «Прогресс двух Worker'ов»
- [x] 4.9 `lost_leases` куска: прибавка при перехвате, обнуление пачкой, `Failed` с `Backtest.InterruptedChunk` на
  третьей; integration-тесты «Worker упал во время прогона», «Кусок без продвижения», «Перезапуск всех Worker'ов»
- [x] 4.10 Отмена бэктеста, который считают два Worker'а: integration-тест — после следующего сохранения новых
  результатов нет, статус `Cancelled`
- [x] 4.11 Детерминизм: integration-тест «Тот же результат на разном числе Worker'ов» на эталонном Ladder и
  «Два Worker'а на одной базе» (каждая комбинация — ровно один результат, куски брали оба); переписать сценарии
  бэктестов в `RestartTests` в тесты перехвата

## 5. Версия движка

- [x] 5.1 `EngineVersion.Current` и singleton `EngineVersion` в DI; миграция `backtests.engine_version` (существующим
  `1`); создание бэктеста запоминает версию, чтение возвращает её; integration-тест «Бэктест создан» с версией движка
- [x] 5.2 Worker берёт шаги загрузки и куски только своей версии; integration-тест «Worker старой версии» с подменённой
  версией у `WorkerHost`
- [x] 5.3 Worker переводит незавершённые бэктесты старой версии в `Failed` с `Backtest.EngineVersionChanged`;
  integration-тест «Деплой новой версии движка посреди бэктеста»: пять результатов читаются
- [x] 5.4 `409` на детали результата бэктеста другой версии движка с обеими версиями в ответе; integration-тест
  «Детали бэктеста другой версии движка», список результатов читается
- [x] 5.5 Отпечаток эталонных результатов и `Fixtures/Backtests/engine-versions.json`; unit-тест сверяет последнюю
  запись с `EngineVersion.Current` и отпечатком, рост версий и неповторяемость отпечатков; проверить вручную, что
  изменение комиссии в эталонном наборе роняет тест

## 6. Образ, compose и документация

- [x] 6.1 `backend/src/ProfitFox.Worker/Dockerfile` (SDK .NET 10 → runtime .NET 10) и `docker-compose.worker.yml` с
  `.env` рядом; проверить `docker compose -f docker-compose.worker.yml build` и запуск Worker'а против локальной базы
  из `docker-compose.yml`
- [x] 6.2 README: локальный запуск API и Worker'а, запуск Worker'а на другой машине, порядок деплоя (API первым) и
  требования к Postgres на проде (LAN-интерфейс, `pg_hba` для `192.168.8.0/24`, пароль из переменной окружения);
  проверить, что команды из README выполняются как написано

## 7. Итоговая проверка

- [x] 7.1 `dotnet build backend/ProfitFox.sln`, `dotnet test backend/ProfitFox.sln`, тот же прогон с `TZ=Asia/Tokyo` и
  `dotnet format backend/ProfitFox.sln --verify-no-changes` проходят
- [x] 7.2 Пройти diff: каждая ветка и проверка нового кода требуется каким-то тестом; лишнее удалить или покрыть
  тестом сначала

## Workflow follow-up

- Владелец проверяет артефакты; правки — через `/opsx:update`.
- После `/opsx:apply` — `/opsx:archive` до pull request, чтобы код и `openspec/specs` попали в `main` одним коммитом.
- Деплой по Migration Plan в design.md: открыть Postgres на `soyo-01` для LAN, снять дамп, задеплоить API, затем
  Worker'ы — с отдельного разрешения владельца.
