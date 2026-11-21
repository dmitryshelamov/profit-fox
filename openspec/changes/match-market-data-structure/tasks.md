# Tasks

Команды бэкенда запускаются из `backend/`: `dotnet build ProfitFox.slnx --configuration Release` и `dotnet test ProfitFox.slnx`;
интеграционным тестам нужен Docker. Каждая группа кончается сборкой без ошибок и предупреждений, зелёным прогоном всех
тестов с числом не меньше базы и коммитом группы (коммит — по слову владельца). Во всём изменении не правятся ожидания
тестов, файлы эталонов, записи запросов и ответов Presentation, маршруты, текст SQL и расчёт в `Domain/Services`. Файлы,
которые переезжают без переписывания, переносятся через `git mv`.

«Сверка API» в проверках ниже: поднять Presentation в `Development`, сохранить `/openapi/v1.json` и сравнить с документом
базы побайтно; `npm run generate:api` во `frontend/` не меняет `src/api/generated/schema.d.ts`.

## 1. База и предложение

- [x] 1.1 Ветка `refactor/match-market-data-structure` от `release/0.1.0` (`882accb`). Снять и записать в раздел «База»
  этого файла: число unit и интеграционных тестов (`dotnet test`), число предупреждений сборки, SHA-256
  `frontend/src/api/generated/schema.d.ts`, сохранённый документ `/openapi/v1.json` и его SHA-256, SHA-256
  `backend/tests/ProfitFox.Tests.Integration/Fixtures/EngineVersions.json`. Проверить: раздел «База» заполнен,
  `openspec validate match-market-data-structure --strict` проходит.

## 2. Domain: основа, значения, репозитории

- [x] 2.1 Добавить `Entities/Base/AggregateRoot` и `Events/IDomainEvent` копией образца (design.md, решение 1). Проверить:
  файлы отличаются от образца только строкой `namespace` и `using`; сборка зелёная.
- [x] 2.2 Перенести из `Application/Ports` в Domain записи, перечисление и константы по таблице решения 2
  (`StoredHypothesis`, `HypothesisKey`, `Decision`, `FinishedRun`, `LedgerResult`, `Renewal`, `RunAssignment`, `RunState`,
  `RunKinds`, `SignalRead`, `CancelOutcome`) и исключения `LeaseLostException`, `SignalLibraryException`, `SignalProblem`.
  Проверить: в переехавших файлах изменились только `namespace` и `using`; сборка и все тесты зелёные.
- [x] 2.3 Перенести четыре порта хранения в `Domain/Repositories` с новыми именами и методами `…Async(…, cancellationToken)`
  по решению 2; хранилища Infrastructure реализуют новые интерфейсы, сценарии и тесты зовут новые имена. Добавить
  `HypothesisReader.Parse(StoredHypothesis)` и тест на текст чужой гипотезы в `HypothesisReaderTests`. Проверить: в
  `Application/Ports` остались только `MarketData/IHistorySource` и `IInstrumentCatalog`; `grep -rn "IHypothesisStore\|ISignalStore\|IRunQueue\|IRunLedger" backend` пуст; сборка и все тесты зелёные.

## 3. Domain: сущности

- [x] 3.1 `Entities/Runs/Run` вместо `RunRecord` (решение 1): закрытый конструктор, `Restore`, `MaxLostLeases`,
  `InterruptedTooOften`, `IsQueued`, `IsOf`. `RunRepository` (пока под старым именем класса) возвращает `Run.Restore(…)`;
  `RunBoard` и `RunExecutor` зовут члены сущности вместо своих сравнений. Добавить
  `Tests.Shared/Generators/Domain/Entities/Runs/RunGenerator` и `Tests.Unit/Domain/Entities/Runs/RunTests` (граница
  `MaxLostLeases`, `IsQueued` для трёх состояний, `IsOf` при расхождении каждого из четырёх полей). Проверить:
  `grep -rn "RunRecord" backend` пуст; сборка и все тесты зелёные, новые тесты в их числе.
- [x] 3.2 `Entities/Signals/SignalDocument` как сущность: `Create`, `Restore`, `Rename`; три места с `with` (решение 1)
  переходят на них. Добавить `…/Entities/Signals/SignalDocumentGenerator` и
  `Tests.Unit/Domain/Entities/Signals/SignalDocumentTests` (`Create` даёт версию 1, `Restore` ничего не меняет, `Rename`
  меняет только имя). Проверить: `grep -rn "new SignalDocument(" backend` находит только закрытый конструктор; сборка и
  все тесты зелёные.

## 4. Infrastructure: репозитории и регистрация

- [x] 4.1 Переименовать хранилища и их строки по таблице решения 5, сделать репозитории `internal sealed`, открыть
  внутренние типы для `ProfitFox.Tests.Integration`; вынести SQL в `Queries/<Имя>RepositoryQueries` и перевод строк в
  `Converters/<Имя>DbConverter`. Переименовать интеграционные тесты за классами. Проверить: `git diff` показывает
  операторы SQL перенесёнными без правки текста; `HypothesisRepositoryTests`, `SignalRepositoryTests`,
  `RunRepositoryTests`, `LedgerRepositoryTests` и `WorkerTests` зелёные; сборка без предупреждений.
- [x] 4.2 `InfrastructureRegistrar.AddInfrastructure`, `PostgresRegistrar`, `MarketDataRegistrar`,
  `FluentMigrator/BenchMigrations` (решение 5); `Program` Presentation и Worker и `BenchPresentation` зовут
  `AddInfrastructure` и теряют свои строки регистрации клиента сервиса данных и базы; тестовые хосты подменяют порты
  после основной регистрации. Проверить: `grep -rn "AddBenchDatabase\|GrpcChannel.ForAddress" backend/src` находит
  только регистраторы Infrastructure; запуск Presentation без `MarketData:GrpcUrl` падает сообщением «The setting
  MarketData:GrpcUrl is not set.»; `StartTests` и все тесты зелёные.

## 5. Application на MediatR, по темам

Каждая задача группы переводит одну тему целиком: команды, запросы, обработчики, контракты `*Internal` с конвертерами,
контроллер или службу, которые её зовут, и тесты темы; класс-сценарий темы удаляется в той же задаче.

- [x] 5.1 Подключить `MediatR` 12.5.0 (`Directory.Packages.props`, `ProfitFox.Application.csproj`), `InternalsVisibleTo`
  для `ProfitFox.Tests.Unit`, `Infrastructure/Mediatr/MediatrRegistrar` и его вызов из `AddInfrastructure`. Если `ILogger`
  не виден в Application — добавить `Microsoft.Extensions.Logging.Abstractions` версии `Microsoft.Extensions.Hosting`.
  Проверить: `dotnet list ProfitFox.slnx package` показывает новые пакеты только в Application; сборка и все тесты
  зелёные.
- [x] 5.2 Гипотезы: `AddHypothesisCommand`, `GetHypothesesQuery`, `HypothesisInternal`; `HypothesesController` на
  `ISender`; `HypothesisCatalog` удалён, его чтение версии — в обработчиках через `HypothesisReader.Parse(stored)`
  (решение 3). Проверить: `HypothesesTests` и тесты репозитория гипотез зелёные; сверка API без расхождений.
- [x] 5.3 Сигналы: четыре команды, два запроса, `Commands/Signals/Base`, `SignalInternal`, `SignalEntryInternal` с
  конвертером; `SignalsController` на `ISender`; `SignalLibrary` удалён; `SignalLibraryTests` разложены по
  `<Обработчик>Tests`, `MemorySignalStore` становится `MemorySignalRepository`. Проверить: число тестов сигналов не
  меньше, чем было в `SignalLibraryTests`, имена тестов те же; `SignalsTests` зелёные; сверка API без расхождений.
- [x] 5.4 Прогоны со стороны API: `EnqueueRunCommand`, `CancelRunCommand`, четыре запроса `RunBoard`, `RunInternal`,
  `LedgerEntryInternal`, `FinishedRunInternal`, `RunDetailsInternal`, `RunAvailabilityInternal` с конвертерами и тестами
  конвертеров (каждое из полей доходит до `*Internal`); `RunsController` и конвертеры `Contracts/Runs/Converters` на
  `*Internal`; `RunScheduler` и `RunBoard` удалены. Проверить: `RunsTests`, `HypothesesTests` зелёные; сверка API без
  расхождений.
- [x] 5.5 Прогоны со стороны Worker'а: `RunNextCommand` с `RunNextResult`, `ExecuteRunCommand` с `ExecuteRunResult`,
  `StopRunCommand`, `Commands/Runs/Base`; обработчики пишут через `ILogger<T>`; `RunBackgroundService` шлёт
  `RunNextCommand` в своей области; `HypothesisRunner`, `RunExecutor`, `RunOptions`, `RunResult` удалены; пять
  интеграционных тестов прогона без очереди шлют `ExecuteRunCommand` (решение 7). Проверить: `WorkerTests`,
  `ParityThroughWorkerTests`, `HypothesisRunTests`, `OverlapRunTests`, `EngineVersionTests`, `LeaseKeeperTests`,
  `RunReportTests` зелёные; `EngineVersions.json` совпадает с базой по SHA-256.
- [x] 5.6 Предпросмотр и инструменты: `PreviewSignalQuery` с обработчиком, `PreviewOutcomeInternal`,
  `PreviewErrorInternal`, `SignalPreviewInternal`, `GetInstrumentsQuery` с `InstrumentInternal`; `PreviewController` и
  `InstrumentsController` на `ISender`; `SignalPreview` и `PreviewQuery` удалены. Проверить: `PreviewTests`,
  `PreviewCacheTests` зелёные; сверка API без расхождений; `grep -rn "IInstrumentCatalog\|IHistorySource" backend/src/ProfitFox.Presentation`
  пуст.
- [x] 5.7 Проверить слой целиком: в `Application/Commands` и `Queries` нет открытых классов-сценариев
  (`grep -rnE "class (HypothesisCatalog|SignalLibrary|RunScheduler|RunBoard|HypothesisRunner|RunExecutor|SignalPreview)" backend`
  пуст), каждый контроллер, кроме `NodesController`, получает в конструкторе только `ISender`, раскладка
  `Application` совпадает с таблицами решений 3 и 4.

## 6. Тесты и документация

- [x] 6.1 Перенести `InstrumentCatalogTests` в `Tests.Integration/Infrastructure/Clients/MarketData`, убрать ссылку
  `Tests.Unit` на Infrastructure из проекта и из списка `Directory.Build.targets` с его комментарием. Проверить:
  временная ссылка `Tests.Unit` → Infrastructure роняет сборку сообщением правила слоёв, ссылка откатывается; все тесты
  зелёные.
- [x] 6.2 Переписать раздел «Слои» `README.md` под новое устройство: сущности и репозитории Domain, команды и запросы
  Application, `*Repository` Infrastructure, `ISender` в точках входа, что из образца не взято (решение 8). Проверить:
  каждая папка и каждое имя типа, названные в разделе, есть в дереве `backend/src`.

## 7. Сквозная проверка

- [x] 7.1 Сборка `Release` без предупреждений; все тесты зелёные, число не меньше базы и ни один тест базы не пропал
  (сравнить списки `dotnet test --list-tests` с учётом переименований задач 4.1, 5.3 и 6.1); проверка однострочных
  `<summary>` из `ci.yml` пуста.
- [x] 7.2 Сверка API с базой; `npm run build`, `npm run lint`, `npm test` во `frontend/` зелёные.
- [x] 7.3 Образы: `docker build` по `backend/src/ProfitFox.Presentation/Dockerfile` и `backend/src/ProfitFox.Worker/Dockerfile`
  из корня проходит.
- [ ] 7.4 Вживую на dev-запуске (`bench-db`, `bench-api`, `bench-worker`, `frontend` из `.claude/launch.json`, сервис
  данных — тот, что в настройках разработки): открыть список гипотез, сделать предпросмотр сигнала, поставить прогон и дождаться
  его строки в журнале, поставить второй и отменить. Проверить: все четыре шага проходят, в журнале Worker'а есть строки
  хода прогона.

## Открыто после применения

Сюда записывается всё, что при применении разошлось с design.md.

- Задача 2.3: `IRunRepository` в Domain отдаёт `RunRecord`, а Domain на Application не ссылается, поэтому `RunRecord` переехал
  через `git mv` в `Domain/Entities/Runs/RunRecord.cs` уже в 2.3, без правки текста. Задача 3.1 заменяет его сущностью `Run`;
  конечное устройство не меняется.
- Задача 5.5: `RunNextCommand` несёт настройки Worker'а — `RunNextCommand(WorkerSettings Settings)`, обработчик не берёт
  `WorkerSettings` из контейнера. MediatR регистрирует все обработчики сборки и в API, а запуск в `Development` проверяет, что
  каждый можно построить (`ValidateOnBuild`); `WorkerSettings` регистрирует только `Program` Worker'а, и API падал при старте.
  Привязать секцию `Worker` в `AddInfrastructure` нельзя без нового пакета (`Microsoft.Extensions.Configuration.Binder` в
  Infrastructure не приходит). Привязка осталась в `Program` Worker'а, как в решении 6; `RunBackgroundService` шлёт
  `new RunNextCommand(settings)`. Добавлен `Tests.Integration/Infrastructure/InfrastructureRegistrarTests`: поставщик служб
  `AddInfrastructure` строится с `ValidateOnBuild`.
- Задача 5.5: `RunNextResult` — перечисление `{ Ran, NothingToTake }`, как записано в таблице решения 3.
- Задача 5.2: у `AddHypothesisResult` нет хэша, как в таблице решения 3. Тесты, которые сравнивали хэш из `HypothesisAdded`,
  берут его из `IHypothesisRepository.FindAsync` или `HypothesisReader.Parse` того же текста; ожидаемые значения те же.
  `A_hypothesis_that_is_not_there_is_refused_by_name` в `HypothesisRepositoryTests` шлёт `EnqueueRunCommand` (чтения версии
  без прогона больше нет, решение 3).
- Решение 7: интеграционные тесты шлют команды и запросы через поставщик служб `AddInfrastructure` с подменой портов сервиса
  данных (`Tests.Integration/BenchServices`); Application открывает внутренние типы только для `Tests.Unit`. Тесты сигналов
  в `Tests.Unit` зовут обработчики напрямую через `SignalRequests` над `MemorySignalRepository`.
- Решение 6, по слову владельца 2026-11-21 во время группы 7: `BenchPresentation` удалён, `Program.Main` Presentation собирает
  всё сам, как `Program.cs` образца (`AddInfrastructure`, маршруты, контроллеры, OpenAPI, проверка базы, миграции, фронтенд,
  `/health`); порядок регистрации и маршрутов прежний, документ OpenAPI побайтно тот же. Тестовый сервер — `BenchTestApplication
  : WebApplicationFactory<Program>`, как `MarketDataTestApplication` образца: окружение `Testing`, строка базы и `wwwroot`
  через `UseSetting`/`UseWebRoot`, подделка сервиса данных в `ConfigureTestServices`. Владелец разрешил новый пакет
  `Microsoft.AspNetCore.Mvc.Testing` 10.0.14 (версия образца) в `Tests.Integration`; `Microsoft.AspNetCore.TestHost` поднят
  с 10.0.10 до 10.0.14 вместе с ним. `BenchStart.Migrate` ушёл: API, как `Program`, всегда мигрирует при старте.
  `Health_is_not_ok_while_the_database_does_not_answer` поэтому стартует на новой базе и удаляет её после старта
  (`BenchDatabase.DropDatabase`); ожидание (503) то же.

## База

Снято 2026-11-21 на `refactor/match-market-data-structure` от `release/0.1.0` (`882accb`), до любых правок кода.

- Тесты (`dotnet test ProfitFox.slnx`): 251 unit, 162 интеграционных, все зелёные; `--list-tests` даёт 413 имён.
- Сборка `Release` (`--no-incremental`): 0 предупреждений, 0 ошибок.
- `frontend/src/api/generated/schema.d.ts`: SHA-256 `56f993fcfd20a65f52df17cb56bfe0460173333c55cfba7913250153a631a3c5`;
  `openapi-typescript` по документу базы даёт тот же файл.
- `/openapi/v1.json` (Presentation в `Development`): 54001 байт, SHA-256
  `475cc910333b2a2cdd8a3df24bb524b66416cb9136258addef99e07ad35b6ded`. Документ и список тестов сохранены вне репозитория, в
  папке заметок сессии (`match-market-data-structure-base/openapi.json`, `list.txt`).
- `backend/tests/ProfitFox.Tests.Integration/Fixtures/EngineVersions.json`: SHA-256
  `f94ec0a7a01577bf6151c1db94412b6e5371f3b5f9bbec4ef49165facef954b9`.

## Workflow follow-up

- Коммиты, отправка ветки и PR в `release/0.1.0` — по слову владельца на каждый шаг.
- После слияния — архивировать изменение (`openspec archive match-market-data-structure`).
