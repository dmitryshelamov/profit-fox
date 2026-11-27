# Design

## Context

Мотивация — в proposal.md. Образец — `profit-fox-market-data` на коммите `14a441d`: `docs/conventions.md` (разделы Domain,
Application, Infrastructure, Presentation, Tests) и дерево `backend/src`, `backend/tests`. Образец только читается.

Что есть в коде на `release/0.1.0` (`882accb`):

- **Domain** (102 файла): `ValueObjects/<Тема>`, `Services/<Тема>`, `Exceptions`. Типов с идентичностью нет: всё — записи
  (`record`) и статические службы.
- **Application** (50 файлов): сценарии — классы с несколькими методами: `HypothesisCatalog` (4 метода), `SignalLibrary` (6),
  `RunScheduler` (2), `RunBoard` (4), `HypothesisRunner` (2), `RunExecutor` (1), `SignalPreview` (1). Порты хранения —
  `IHypothesisStore`, `ISignalStore`, `IRunQueue`, `IRunLedger` в `Ports/<Тема>` вместе с записями `RunRecord`,
  `StoredHypothesis`, `HypothesisKey`, `Decision`, `FinishedRun`, `LedgerResult`, `Renewal`, `RunAssignment`, `SignalRead`,
  перечислением `CancelOutcome` и константами `RunState`, `RunKinds`. Порты сервиса данных — `IHistorySource`,
  `IInstrumentCatalog`.
- **Infrastructure** (17 файлов): `HypothesisStore`, `SignalStore`, `RunQueue`, `RunLedger` — открытые классы, каждый вызов
  сам открывает соединение из `NpgsqlDataSource` и сам целиком проходит в своей транзакции; SQL записан в методах, кроме
  `LedgerSql`. Регистрация — `BenchInfrastructure.AddBenchDatabase`; клиент сервиса данных регистрируют `Program.cs`
  Presentation и Worker одинаковыми строками.
- **Presentation**: контроллеры получают классы-сценарии через конструктор, `InstrumentsController` — порт
  `IInstrumentCatalog`. Свои записи запросов и ответов (`Contracts/<Тема>`) задают документ OpenAPI, из которого
  сгенерирован `frontend/src/api/generated/schema.d.ts`.
- **Worker**: `RunBackgroundService` зовёт `RunExecutor.RunNext` в цикле.
- **Тесты**: 251 unit и 162 интеграционных (замер `align-structure-with-market-data`). `Tests.Unit` ссылается на
  Infrastructure ради `InstrumentCatalogTests`. Пять интеграционных тестов создают `HypothesisRunner` руками и зовут
  `Run` без очереди. `Tests.Shared` пуст.

Ограничения, которые задаёт сам код:

- Очередь прогонов держит несколько Worker'ов на разных машинах: взять прогон, продлить аренду, отменить — по одному
  оператору SQL со временем базы (`RunQueue`), а строка журнала и конец прогона пишутся одним шагом под блокировкой
  (`RunLedger.Decide`). Модель «прочитал сущность, изменил, сохранил» это сломала бы.
- Документ OpenAPI называет записи ответов Presentation; любое их переименование меняет `schema.d.ts`.

## Goals / Non-Goals

**Goals:**

- Domain, Application, Infrastructure, Presentation и тесты устроены по правилам `conventions.md` образца: сущности и
  репозитории в Domain, команды и запросы MediatR с обработчиками в Application, `*Repository`/`*Db` в Infrastructure,
  `ISender` в контроллерах и службе Worker'а.
- Каждая группа задач собирается без предупреждений и проходит все тесты; число тестов не падает ниже базы.
- Документ OpenAPI и `schema.d.ts` побайтно те же; `EngineVersions.json` не тронут.

**Non-Goals:**

- Менять поведение: маршруты, тела ответов и коды, схему базы, порядок и атомарность операций очереди и журнала, расчёт.
- Править `ProfitFox.Cli`, `frontend/`, `deploy/`, `docker-compose*.yml`.
- Брать из образца то, чему здесь нет применения (решение 8).

## Decisions

### 1. Что становится сущностью

Правило образца: сущность живёт и меняется под своей идентичностью (`Instrument` обновляется и снимается с торгов);
неизменный факт, который сохраняют точка за точкой, — значение со своим репозиторием (`OpenInterestPoint` и
`IOpenInterestRepository`). По этому правилу:

| Тип | Меняется после записи | Что это |
|---|---|---|
| прогон очереди (`RunRecord`) | да: ждёт → идёт → кончился, аренда, ход | сущность `Run` |
| сохранённый сигнал (`SignalDocument`) | да: описание, раскладка, версия, имя | сущность `SignalDocument` |
| версия гипотезы (`StoredHypothesis`) | нет: версии только добавляются | значение |
| строка журнала (`LedgerEntry`) | нет: триггер базы запрещает правку | значение |

Сущность — `sealed class` от `AggregateRoot`: свойства только для чтения или с `private set`, закрытый конструктор,
`Restore(...)` для репозитория. `AggregateRoot` и `IDomainEvent` копируются из образца слово в слово (меняется
`namespace`); событий пока никто не поднимает, как и в образце.

**`Run`** (`Entities/Runs`): 15 свойств `RunRecord` с теми же именами и типами; `Restore` со всеми значениями. `Create` у
него нет: прогон рождается оператором `IRunRepository.EnqueueAsync`, потому что время постановки и единственность среди
ждущих и идущих решает база. Переходы состояния остаются операторами репозитория (см. Context). В сущность переезжает то,
что сценарии сейчас считают по её полям: константа `MaxLostLeases` (из `RunExecutor`), `InterruptedTooOften`
(`LostLeases >= MaxLostLeases`), `IsQueued`, `IsOf(hypothesis, version, period, parity)` (сравнение из `RunBoard`).

**`SignalDocument`** (`Entities/Signals`, имя сохраняется): `Create(name, description, graph, layout)` даёт версию 1,
`Restore(name, description, graph, layout, version)` — для репозитория, `Rename(to)` заменяет `signal with { Name = to }`.
Версию по-прежнему решает репозиторий (`CreateAsync` и `ReplaceAsync` отдают сигнал, как он сохранён). Идентификатор
`uuid` строки `signals` домен по-прежнему не знает: API обращается к сигналу по имени.

Альтернатива — сделать сущностями и версию гипотезы со строкой журнала. Отвергнута: `LedgerEntry` собирается в
`HypothesisRunner` через `with` в четыре шага и создаётся инициализатором в 12 местах тестов правил журнала; класс с
закрытым конструктором на 21 значение переписал бы и расчёт строки, и эти тесты без выгоды — менять в строке нечего. Если
владельцу нужны и они — это решается до начала применения, задачи группы 3 вырастут.

### 2. Domain: что куда

Темы прежние. `Services` и `Exceptions` остаются: в образце их нет, потому что там нет расчёта и исключений домена.

| Было | Стало |
|---|---|
| — | `Entities/Base/AggregateRoot`, `Events/IDomainEvent` |
| `Application/Ports/Runs/RunRecord` | `Entities/Runs/Run` |
| `Application/Ports/Runs/CancelOutcome` | `Entities/Runs/Enums/CancelOutcome` |
| `Domain/ValueObjects/Signals/SignalDocument` | `Entities/Signals/SignalDocument` |
| `Application/Ports/Hypotheses/StoredHypothesis`, `HypothesisKey` | `ValueObjects/Hypotheses/` |
| `Application/Ports/Runs/Decision`, `FinishedRun`, `LedgerResult` | `ValueObjects/Ledger/` |
| `Application/Ports/Runs/Renewal`, `RunAssignment`, `RunState`, `RunKinds` | `ValueObjects/Runs/` |
| `Application/Ports/Signals/SignalRead` | `ValueObjects/Signals/` |
| `Application/Ports/Hypotheses/IHypothesisStore` | `Repositories/IHypothesisRepository` |
| `Application/Ports/Signals/ISignalStore` | `Repositories/ISignalRepository` |
| `Application/Ports/Runs/IRunQueue` | `Repositories/IRunRepository` |
| `Application/Ports/Runs/IRunLedger` | `Repositories/ILedgerRepository` |
| `Application/Exceptions/LeaseLostException`, `SignalLibraryException`, `SignalProblem` | `Domain/Exceptions/` |

Три исключения переезжают, потому что их бросает хранилище и называет в `<exception>` интерфейс репозитория, а Domain на
Application не ссылается. `HypothesisNotFoundException`, `HistoryUnavailableException`, `PeriodBoundaryException` остаются
в Application: их бросают обработчики и порты сервиса данных.

Методы репозиториев сохраняют глаголы — это слова предметной области (взять, продлить, решить) — и получают суффикс
`Async` и параметр `cancellationToken`, как в образце: `LatestAsync`, `FindAsync`, `KeysAsync`, `AddAsync`;
`NamesAsync`, `ExistsAsync`, `ReadAsync`, `CreateAsync`, `ReplaceAsync`, `RenameAsync`, `DeleteAsync`; `EnqueueAsync`,
`ClaimAsync`, `RenewAsync`, `CancelAsync`, `ActiveAsync`, `FindAsync`; `EntriesAsync`, `RecentAsync`, `FindAsync`,
`DecideAsync`. Параметры и возвращаемые значения те же, с заменой `RunRecord` на `Run`.

`HypothesisReader` получает `Parse(StoredHypothesis)`: разбор текста версии и проверка, что текст — этой гипотезы
(сейчас это `HypothesisCatalog.Parse`). Проверка чистая, а нужна трём обработчикам.

### 3. Application: команды и запросы вместо классов-сценариев

MediatR 12.5.0, как в образце. Команда — `sealed record : IRequest<TResult>` в папке `Commands/<Тема>/<Имя>/` рядом с
обработчиком и результатом. Запрос и обработчик — рядом в `Queries/<Тема>/`. Обработчики — `internal sealed`; проект
открывает внутренние типы для `ProfitFox.Tests.Unit` (`InternalsVisibleTo`), как образец. Команда отдаёт именованный
результат, не `bool`.

Команды:

| Было | Стало (`Commands/…`) | Результат |
|---|---|---|
| `HypothesisCatalog.Add` | `Hypotheses/AddHypothesis/AddHypothesisCommand(Text)` | `AddHypothesisResult(Id, Name, Version, Created)` вместо `HypothesisAdded` |
| `SignalLibrary.Create` | `Signals/CreateSignal/CreateSignalCommand(Name, Description, Graph, Layout)` | `SignalInternal` |
| `SignalLibrary.Replace` | `Signals/ReplaceSignal/ReplaceSignalCommand(Name, Description, Graph, Layout)` | `SignalInternal` |
| `SignalLibrary.Rename` | `Signals/RenameSignal/RenameSignalCommand(Name, To)` | `SignalInternal` |
| `SignalLibrary.Delete` | `Signals/DeleteSignal/DeleteSignalCommand(Name)` | нет |
| `RunScheduler.Enqueue` | `Runs/EnqueueRun/EnqueueRunCommand(Hypothesis, Version, Period, Parity)` | `RunInternal` |
| `RunScheduler.Cancel` | `Runs/CancelRun/CancelRunCommand(RunId)` | `CancelOutcome` |
| `RunExecutor.RunNext` | `Runs/RunNext/RunNextCommand` | `RunNextResult { Ran, NothingToTake }` вместо `bool` |
| `HypothesisRunner.Run` + `RunOptions` | `Runs/ExecuteRun/ExecuteRunCommand` (поля `RunOptions` и `RunProgress? Progress`) | `ExecuteRunResult` вместо `RunResult` |
| `HypothesisRunner.Stop` | `Runs/StopRun/StopRunCommand` (поля `RunOptions`, `Status`, `Reason`) | нет |

Запросы:

| Было | Стало (`Queries/…`) | Результат |
|---|---|---|
| `HypothesisCatalog.List` | `Hypotheses/GetHypothesesQuery` | `IReadOnlyList<HypothesisInternal>` |
| `SignalLibrary.List` | `Signals/GetSignalsQuery` | `IReadOnlyList<SignalEntryInternal>` |
| `SignalLibrary.Read` | `Signals/GetSignalQuery(Name)` | `SignalInternal` |
| `RunBoard.Active` | `Runs/GetActiveRunsQuery` | `IReadOnlyList<RunInternal>` |
| `RunBoard.Finished` | `Runs/GetFinishedRunsQuery(Limit)` | `IReadOnlyList<FinishedRunInternal>` |
| `RunBoard.Find` | `Runs/GetRunQuery(RunId)` | `RunDetailsInternal?` |
| `RunBoard.Availability` | `Runs/GetRunAvailabilityQuery(Hypotheses)` | словарь `RunAvailabilityInternal` по id гипотезы |
| `SignalPreview.Run` + `PreviewQuery` | `Preview/PreviewSignalQuery(Instrument, Timeframe, Signal)` | `PreviewOutcomeInternal` |
| `IInstrumentCatalog` из контроллера | `MarketData/GetInstrumentsQuery` | `IReadOnlyList<InstrumentInternal>` |

Что не становится запросом:

- `HypothesisCatalog.Read` — его зовут только другие сценарии. Обработчики `EnqueueRun`, `ExecuteRun`, `StopRun` сами
  берут версию из репозитория, бросают `HypothesisNotFoundException` и зовут `HypothesisReader.Parse(stored)`.
- `HypothesisCatalog.Keys` — его зовут только тесты хранилища; они зовут `IHypothesisRepository.KeysAsync`.
- `MarketHistory`, `PreviewHistory`, `PreviewHistoryCache` — не сценарии, а чтение истории с границей периода и память
  предпросмотра; остаются открытыми классами в `Queries/MarketData` и `Queries/Preview`.

Что рядом с командами: `Runs/RunNext/` — `LeaseKeeper`, `WorkerSettings`; `Runs/ExecuteRun/` — `RunProgress`, `RunReport`,
`PairData`. Общий код обработчиков одной темы — в `Commands/<Тема>/Base/` (в образце там общая база команд темы):
`Signals/Base` — проверка имени и графа и исключения «нет такого», «имя занято» (сейчас закрытые методы `SignalLibrary`);
`Runs/Base` — строка журнала прогона, её запись без вердикта и проверка правил журнала (сейчас закрытые методы
`HypothesisRunner`).

`RunNextCommandHandler` шлёт `ExecuteRunCommand` и `StopRunCommand` через `ISender`. Альтернатива — общий внутренний
класс расчёта, который зовут оба обработчика, — отвергнута: расчёт прогона без очереди нужен тестам (пять интеграционных
зовут его напрямую) и должен остаться открытой командой.

Обработчики пишут ход прогона через `ILogger<T>` той же строкой и тем же уровнем, что сейчас даёт `LogWriter`
(`LogInformation("{Line}", text)`); параметр `TextWriter log` уходит. `LogWriter` в Worker остаётся для
`BenchSchema.WaitFor`.

### 4. Контракты `*Internal`

`Contracts/<Тема>` держит то, что запрос или команда отдаёт и API показывает, с именем `<Имя>Internal`; конвертеры из
типов домена — в `Contracts/<Тема>/Converters` (`ToInternal`).

| Было | Стало |
|---|---|
| `HypothesisItem` | `HypothesisInternal` |
| `SignalEntry` | `SignalEntryInternal` |
| отдавался `SignalDocument` | `SignalInternal` + `SignalInternalConverter` |
| отдавался `RunRecord` | `RunInternal` + `RunInternalConverter` |
| отдавался `LedgerEntry` | `LedgerEntryInternal` + `LedgerEntryInternalConverter` |
| отдавался `FinishedRun` | `FinishedRunInternal(Entry, Parity)` |
| `RunDetails` | `RunDetailsInternal(Run, Entry, Report)` |
| `RunAvailability` | `RunAvailabilityInternal` |
| `PreviewOutcome`, `PreviewError`, `SignalPreviewResult` | `PreviewOutcomeInternal`, `PreviewErrorInternal`, `SignalPreviewInternal` |
| отдавался `Instrument` | `MarketData/InstrumentInternal` + конвертер |
| `HypothesisAdded`, `RunResult`, `PairData` | уходят к своим командам (решение 3) |

Вложенные значения домена без своей идентичности остаются как есть внутри `*Internal`: `GraphDefinition`, `NodePosition`,
`GraphError`, `HistoryCandle`, `TimeRange`. Образец переводит и вложенные; здесь это дало бы копию проверенного графа и
десятков тысяч свечей предпросмотра ради имени типа.

Цена решения: строка журнала теперь описана трижды — `LedgerEntry` (домен), `LedgerEntryInternal` (Application),
`RunResultResponse` (API), по 21 полю. В образце слоёв два, потому что API отдаёт `*Internal` как есть; здесь записи
ответов Presentation задают документ OpenAPI и остаются. Альтернатива — отдавать из запросов типы домена, как сейчас, —
отвергнута как расхождение с образцом, которое владелец просил убрать.

### 5. Infrastructure

| Было | Стало |
|---|---|
| `HypothesisStore` | `HypothesisRepository` |
| `SignalStore` | `SignalRepository` |
| `RunQueue` | `RunRepository` |
| `RunLedger` | `LedgerRepository` |
| `HypothesisRow`, `SignalRow`, `RunRow`, `LedgerRow`, `FinishedRow` | `HypothesisDb`, `SignalDb`, `RunDb`, `LedgerEntryDb`, `FinishedRunDb` |
| SQL в методах хранилищ, `LedgerSql` | `Queries/<Имя>RepositoryQueries` у каждого репозитория |
| перевод строки в тип домена в хранилищах и `LedgerSql.Entry` | `Converters/<Имя>DbConverter` (`ToDomain`) |
| `BenchInfrastructure.AddBenchDatabase` | `InfrastructureRegistrar.AddInfrastructure(services, configuration)` |
| — | `Mediatr/MediatrRegistrar`, `Persistence/Postgres/PostgresRegistrar`, `Clients/MarketData/MarketDataRegistrar` |
| `BenchInfrastructure.MigrateBench` | `Persistence/Postgres/FluentMigrator/BenchMigrations.MigrateBench` |

Папки `Repositories/<Тема>` и `SignalJson` в `Converters` остаются. Репозитории становятся `internal sealed`; проект
открывает внутренние типы для `ProfitFox.Tests.Integration`, как образец. Текст SQL, порядок операторов и границы
транзакций не меняются: оператор переезжает в `Queries` как есть.

Регистраторы внутренние, открыт только `AddInfrastructure`. `MarketDataRegistrar` читает `MarketData:GrpcUrl` и
`MarketData:RestUrl`, `PostgresRegistrar` — `ConnectionStrings:Bench`; незаданная настройка, как сейчас, останавливает
запуск сообщением «The setting … is not set.» при регистрации. `InfrastructureRegistrar` регистрирует ещё
`TimeProvider.System` и `PreviewHistoryCache` (один на процесс). Репозитории остаются одиночками: они держат только
`NpgsqlDataSource`. Тестовые хосты, как сейчас, подменяют порты сервиса данных регистрацией после основной.

### 6. Presentation и Worker

Контроллеры получают `ISender`; `InstrumentsController` шлёт `GetInstrumentsQuery`. Записи запросов и ответов, имена
маршрутов, атрибуты `ProducesResponseType` и проверка полей запроса в `RunsController` не трогаются. Конвертеры
`Contracts/<Тема>/Converters` принимают `*Internal` вместо типов домена. Блоки `catch` контроллеров остаются: они строят
тела ошибок, которые записаны в API.

`NodesController` и `SignalsController.Validate` зовут чистые функции домена без хранилища; запрос MediatR им ничего не
даёт, они остаются как есть.

`BenchPresentation` остаётся (его зовут `Program` и тестовый сервер), но регистрирует только своё: контроллеры, OpenAPI,
проверку базы; зависимости даёт `AddInfrastructure`. `RunBackgroundService` на каждый шаг открывает область и шлёт
`RunNextCommand`, как службы образца (`scopes.SendAsync`); при `NothingToTake` ждёт `PollInterval`. `Program` Worker'а
регистрирует `WorkerSettings`, службу и зовёт `AddInfrastructure`.

### 7. Тесты

- `Tests.Unit` проверяет только Domain и Application: ссылка на Infrastructure уходит из проекта и из списка в
  `Directory.Build.targets`; `InstrumentCatalogTests` переезжает в
  `Tests.Integration/Infrastructure/Clients/MarketData`. Это закрывает пункт «Открыто после применения» прошлого изменения.
- Тесты сценариев раскладываются по обработчикам в `Tests.Unit/Application/Commands/<Тема>/<Обработчик>Tests.cs` и
  `…/Queries/<Тема>/`, как в образце (`SyncBybitInstrumentsCommandHandlerTests`). Тест идёт к обработчику, поведение
  которого проверяет; имя теста и ожидания не меняются.
- Интеграционные тесты хранилищ переименовываются за классами (`HypothesisRepositoryTests`, …). Пять тестов прогона без
  очереди собирают поставщик служб через `AddInfrastructure` с подменой портов сервиса данных и шлют `ExecuteRunCommand`.
- `Tests.Shared/Generators/Domain/Entities/Runs/RunGenerator` и `…/Signals/SignalDocumentGenerator`: статические классы с
  методом `Generate…`, параметры по умолчанию дают годный объект. Нужны, потому что у сущностей закрытый конструктор.
- Новые тесты: сущности (`Tests.Unit/Domain/Entities/…`) и конвертеры `*Internal`.

### 8. Что из образца не берётся

| Что | Почему |
|---|---|
| `Persistence/Postgres/Transactions` и соединение на область | каждый вызов репозитория уже целый в своей транзакции; исполнитель появится с первым обработчиком, которому нужно сохранить двумя вызовами |
| `Persistence/Queries/Handlers` | на все запросы отвечают репозитории, обработчики лежат рядом с запросами в Application; своему SQL запроса пока нет |
| `ExceptionHandlers/ApplicationExceptionHandler` | тела ошибок разные по маршрутам и записаны в OpenAPI; общий обработчик изменил бы их |
| `PostgresHealthCheck` в Infrastructure | `DatabaseHealthCheck` остаётся в Presentation: перенос потянул бы пакет проверок в Infrastructure и Worker |
| NSubstitute | нынешние подделки (`MemorySignalStore` → `MemorySignalRepository`, `FakeHistorySource`) держат состояние между вызовами, заглушка этого не даёт; новый пакет не добавляется |
| имена портов `I<Биржа><Что>Client` | `IHistorySource` и `IInstrumentCatalog` — порты сервиса данных, не биржи; имена остаются |
| перечисления вместо строк (`RunState`, `RunStatus`, период и вид прогона) | это правка модели и её чтения из базы, а не раскладки; отдельное изменение |
| `Guid` как идентификатор | идентификаторы прогонов и гипотез — строки в базе и в API |
| `scripts/verify-format.sh`, хук `pre-commit` | не взяты и прошлым изменением; к устройству слоёв не относятся |

## Risks / Trade-offs

- [Переписывается весь Application разом, проверять долго] → группы задач идут по темам, каждая кончается сборкой и всеми
  тестами; расчёт не правится, а `EngineVersionTests` и эталоны `H0001` ловят любое расхождение чисел.
- [Сущности — классы: равенство по ссылке, `with` не работает] → в коде `with` для них стоит в трёх местах (решение 1);
  тесты, сравнивающие объект целиком, сравнивают поля или `*Internal`. Беглый поиск по тестам сравнения целых
  `RunRecord` и `SignalDocument` не нашёл; полный список даст сборка группы 3.
- [Смена порядка или границ операций очереди при переносе SQL] → операторы переезжают без правки текста; `RunQueueTests`
  (327 строк) и `WorkerTests` (386) проверяют аренду, перехват и отмену на настоящей базе.
- [Документ OpenAPI изменится из-за побочной правки контроллера] → до начала снимается документ и SHA-256 `schema.d.ts`,
  после каждой группы с контроллерами сравнивается.
- [Обработчик шлёт команду через `ISender` (`RunNext` → `ExecuteRun`)] → область одна, зависимости — одиночки; это
  записано как намеренное в решении 3.
- [Имена категорий журнала меняются] → текст строк тот же; в `deploy/`, `docker-compose*.yml` и `.github/` имён
  `HypothesisRunner` и `RunExecutor` нет, то есть по категории ничего не настроено.
- [Тройное описание строки журнала (решение 4)] → принято ради совпадения с образцом; конвертеры покрыты тестами.
- [Другие ветки с правками Application] → изменение сливается раньше, чем начинается следующая работа в бэкенде.

## Migration Plan

1. Ветка `refactor/match-market-data-structure` от `release/0.1.0` (`882accb`).
2. Группы задач по порядку tasks.md; после каждой — сборка, все тесты, сверка OpenAPI там, где трогались контроллеры.
3. Слияние в `release/0.1.0` одним PR. Миграций базы нет, порядок выкладки API и Worker не важен: формат строк базы и
   API тот же.
4. Откат — возврат коммита слияния.

## Open Questions

- Нужен ли в этом репозитории свой `docs/conventions.md` по образцу или хватает раздела «Слои» в README.
- Когда переводить строки статусов, периода и вида прогона в перечисления (решение 8).
