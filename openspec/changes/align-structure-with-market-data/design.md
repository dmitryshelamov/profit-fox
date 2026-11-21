# Design

## Context

Мотивация — в proposal.md. Образец — `profit-fox-market-data` на коммите `14a441d` (ветка
`docs/archive-speed-up-history-reads`): `docs/conventions.md` (разделы Domain, Application, Infrastructure, Presentation,
Files and code style, Tests), `backend/Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets`,
`backend/.globalconfig`, `.editorconfig` в корне и дерево `backend/src`, `backend/tests`. Образец только читается.

Что есть в коде на момент предложения (`f5bc916`):

- `backend/src`: `ProfitFox.Domain` (102 файла `.cs`), `ProfitFox.Application` (50), `ProfitFox.Infrastructure` (17 и два
  `.proto`), `ProfitFox.Presentation` (42), `ProfitFox.Worker` (3), `ProfitFox.Cli` (15). Папки внутри слоёв — по темам.
- `backend/tests`: `ProfitFox.Tests.Unit` (35 файлов `.cs`, эталоны в `Fixtures/Verdict`, `Graph/Fixtures/H0001`,
  `Trades/Fixtures/Outcomes`), `ProfitFox.Tests.Integration` (30 файлов `.cs`, эталоны в `Fixtures`), `ProfitFox.Tests.Shared`
  (пустой проект). `ProfitFox.Tests.Unit` ссылается и на `ProfitFox.Infrastructure`: `InstrumentCatalogTests` проверяет
  разбор каталога сервиса данных.
- Каждый `.csproj` сам задаёт `TargetFramework` (`net10.0`), `ImplicitUsings`, `Nullable`, а проекты тестов ещё `LangVersion`
  и `IsPackable`; версии пакетов записаны в `.csproj`. `.editorconfig` и `.globalconfig` нет, анализаторы стиля в сборке не
  работают.
- В Domain, Application и Infrastructure нет MediatR, сущностей с идентичностью, доменных событий и репозиториев домена:
  сценарии — классы с несколькими методами (`HypothesisCatalog`, `SignalLibrary`, `RunBoard`), порты хранения — интерфейсы
  Application (`IHypothesisStore`, `ISignalStore`, `IRunLedger`, `IRunQueue`).

Решение владельца от 2026-11-21: глубина — «Папки и файлы сборки». Общие файлы сборки и раскладка папок как в образце,
тесты зеркалят слои, код не меняется, кроме `namespace` и `using`. MediatR, команды и запросы с обработчиками и перенос
портов в `Domain/Repositories` — вне работы.

## Goals / Non-Goals

**Goals:**

- Файлы сборки и правило ссылок слоёв — как в образце; `.csproj` почти пустые.
- Внутри каждого слоя папка называет сначала вид типа, потом тему; пространство имён совпадает с папкой.
- Тесты лежат по той же раскладке, что код, который они проверяют.
- Каждый шаг собирается без предупреждений и проходит все 413 тестов; перенос проверяется глазами как «переехало и сменило
  `namespace` и `using`».

**Non-Goals:**

- Переписывать сценарии в команды MediatR, дробить классы, переименовывать типы и члены.
- Переносить порты хранения в Domain, переносить тесты между проектами.
- Менять поведение, маршруты API, схему OpenAPI, схему базы.

## Decisions

### 1. Общие файлы сборки — как в образце

| Файл | Что в нём |
|---|---|
| `backend/Directory.Build.props` | `TargetFramework` `net10.0`, `LangVersion` `latest`, `Nullable`, `ImplicitUsings`, `IsPackable` `false`; `EnforceCodeStyleInBuild`, `GenerateDocumentationFile` с `NoWarn` `CS1591`, `WarningsAsErrors` `NU1903;NU1904` — слово в слово из образца |
| `backend/Directory.Packages.props` | `ManagePackageVersionsCentrally` и версии всех пакетов по группам; версии — ровно нынешние |
| `backend/Directory.Build.targets` | правило ссылок слоёв (решение 2), текст цели — из образца |
| `backend/.globalconfig` | `IDE0005` (лишний `using`) — ошибка сборки, как в образце |
| `.editorconfig` в корне | копия образца; в первой строке и в комментарии про `dotnet format` имя репозитория и решения — `profit-fox` и `ProfitFox.slnx` |

`.globalconfig` лежит в `backend/`, а не в корне: так в образце, и SDK находит его вверх от каждого проекта. `.editorconfig`
— в корне, как в образце.

Из каждого `.csproj` уходят `TargetFramework`, `LangVersion`, `ImplicitUsings`, `Nullable`, `IsPackable` и атрибуты `Version` у
`PackageReference`; пустые `PropertyGroup` удаляются. Остаются свойства, которые есть только у проекта: `OutputType` и
`AssemblyName` `bench` у `ProfitFox.Cli`, `IncludeAssets`/`PrivateAssets` у `Grpc.Tools`, `Protobuf`, `Content`, `None`.

Версии пакетов — без изменений, ни одного нового пакета:

| Группа | Пакеты |
|---|---|
| ASP.NET Core | `Microsoft.AspNetCore.OpenApi` 10.0.13 |
| Hosting | `Microsoft.Extensions.Hosting` 10.0.13 |
| CLI | `System.CommandLine` 2.0.15, `Microsoft.Extensions.Configuration.EnvironmentVariables` 10.0.13, `Microsoft.Extensions.Configuration.Json` 10.0.13 |
| Infrastructure | `Dapper` 2.1.89, `FluentMigrator.Runner.Postgres` 8.0.1, `Npgsql` 10.0.3 |
| gRPC | `Google.Protobuf` 3.36.2, `Grpc.Net.Client` 2.85.0, `Grpc.Tools` 2.85.0, `Grpc.AspNetCore` 2.85.0 (сервер поддельного сервиса данных в тестах) |
| Testing | `coverlet.collector` 6.0.4, `Microsoft.AspNetCore.TestHost` 10.0.10, `Microsoft.Extensions.TimeProvider.Testing` 10.11.0, `Microsoft.NET.Test.Sdk` 17.14.0, `NUnit` 4.3.2, `NUnit.Analyzers` 4.7.0, `NUnit3TestAdapter` 5.0.0, `Testcontainers.PostgreSql` 4.16.0 |

Решение остаётся `ProfitFox.slnx`. Новые файлы добавляются в его папку `/common/` рядом с `.gitignore`, чтобы их было видно в
Rider.

### 2. Правило ссылок слоёв: списки равны нынешним ссылкам

| Проект | Может ссылаться на |
|---|---|
| `ProfitFox.Domain` | ни на что |
| `ProfitFox.Application` | `ProfitFox.Domain` |
| `ProfitFox.Infrastructure` | `ProfitFox.Application`, `ProfitFox.Domain` |
| `ProfitFox.Presentation` | `ProfitFox.Application`, `ProfitFox.Infrastructure` |
| `ProfitFox.Worker` | `ProfitFox.Application`, `ProfitFox.Infrastructure` |
| `ProfitFox.Cli` | ни на что |
| `ProfitFox.Tests.Shared` | `ProfitFox.Application`, `ProfitFox.Domain` (как в образце; сейчас ни на что не ссылается) |
| `ProfitFox.Tests.Unit` | `ProfitFox.Application`, `ProfitFox.Domain`, `ProfitFox.Infrastructure`, `ProfitFox.Tests.Shared` |
| `ProfitFox.Tests.Integration` | без списка, как в образце |

Расхождение с образцом: там `Tests.Unit` на Infrastructure не ссылается. Здесь ссылка есть (`InstrumentCatalogTests`), а
переносить тесты между проектами работа не должна, поэтому ссылка разрешена и записана в «Открыто после применения».

Правило проверяется на деле: лишняя ссылка Domain → Application должна уронить сборку сообщением правила; ссылка
откатывается.

### 3. Замечания анализаторов правятся, если не трогают логику

Пробная сборка с файлами решения 1 на `f5bc916` даёт 58 замечаний, все — без правки логики:

| Правило | Сколько | Что делается |
|---|---|---|
| `IDE0005` лишний `using` | 7 | строки удаляются (`BenchPresentation`, `SignalsController`, `RunLedgerTests` интеграционный, `BenchPresentationServer` ×2, `StartTests`, `EngineVersionTests`) |
| `IDE0007` `var` вместо явного типа | 48 | объявление переписывается на `var`: тип справа очевиден (`new`, приведение), компилятор выводит тот же тип |
| `IDE0008` явный тип вместо `var` | 1 | `PreviewHistoryCache`: `var` переписывается на явный тип |
| `CS1573` у параметра нет тега `<param>` | 2 | в документацию конструктора `MarketHistory` добавляются теги `<param>` для `source` и `hash` |

Правки идут отдельным коммитом после файлов сборки и до переноса папок, чтобы дифф переноса оставался чистым. Файлы правок
`IDE0007`/`IDE0008` перечисляются в сообщении коммита и в «Открыто после применения». Если при применении появится правило,
которое требует менять логику или открытые имена, код не правится: правило для этого места выключается с комментарием и
записывается в «Открыто после применения».

### 4. Раскладка «сначала вид, потом тема»

Имена видов — из образца. Тема — нынешняя папка (`Runs`, `Hypotheses`, `Signals`, `Graph`, ...), её имя не меняется.
Вложенные уровни, которых в образце нет (`Graph/Model`, `Graph/Reading`), сливаются в тему. Подпапки внутри темы — только
те, что называет `conventions.md`: `Enums`, `Base`, `Converters`, `Contracts`, `Queries`.

Пространство имён — путь папки: `ProfitFox.<Слой>.<Вид>.<Тема>`. Файлы переносятся через `git mv`.

Для видов, которых в образце нет, папка названа по тому же принципу:

| Слой | Вид | Почему так |
|---|---|---|
| Domain | `Services/<Тема>` | доменные службы и расчёт: чтение и проверка графа, узлы, исполнители, позиция, симулятор сделки, случайные входы, правила журнала и вердикта. В DDD это доменные службы; слово `Services` в образце не занято |
| Domain | `Exceptions` | как `Exceptions` в Application образца |
| Infrastructure | `Clients/MarketData` | клиент gRPC и REST сервиса данных; в образце внешние API лежат в `Exchanges/<Имя>`, а сервис данных не биржа. Его `Protos` — рядом, `Clients/MarketData/Protos` |
| Presentation | `Contracts/<Тема>`, `Contracts/<Тема>/Converters` | запросы и ответы API и их построение из типов Application; в образце API отдаёт `*Internal` из Application, своих контрактов у него нет. Имя — как у `Contracts` Application |
| Presentation | `HealthChecks`, `Formatters` | `DatabaseHealthCheck` (в образце `PostgresHealthCheck` лежит в Infrastructure, но перенос между проектами вне работы) и `PlainTextInputFormatter` |
| Worker | `BackgroundServices`, `Logging` | служба Worker'а — как `BackgroundServices` в Infrastructure образца; `LogWriter` — отдельно |
| Cli | `Commands/<Тема>`, `Contracts/<Тема>` | команды CLI и записи ответов API; `Api` (клиент API) остаётся |

### 5. Domain: значения, службы, исключения

Правило, по которому файл попадает в папку:

- `ValueObjects/<Тема>` — записи (`record`, `record struct`) и классы, которые только держат проверенные при создании данные
  (`Signal`, `TradingStrategy`, `ReadResult<T>`, `NodeKind`, `MinutePath`, `CompiledGraph`), а также статические классы,
  которые только называют или разбирают значения (`Durations`, `Timeframes`, `RunStatus`, `EngineVersion`).
- `ValueObjects/<Тема>/Enums` — перечисления и чистые переводы перечисления в текст и обратно (`PinTypes`), как
  `Entities/<Тема>/Enums` в образце. В `Entities` их не положить: сущностей нет.
- `Services/<Тема>` — всё, что считает: статические правила и алгоритмы, исполнители и прогоны графа с состоянием, узлы,
  позиция пары, хэш истории, генератор случайных чисел. Абстрактная база узлов `Node` — в `Services/Nodes/Base`, как
  `<Тема>/Base` в образце.
- `Exceptions` — `HypothesisException`, `LedgerException`.

`Entities`, `Repositories` и `Events` не создаются: таких типов нет, а `AggregateRoot` и `IDomainEvent` образца — новый код.

Таблица `ProfitFox.Domain`:

| Было | Стало | Файлы |
|---|---|---|
| `Execution/` | `ValueObjects/Execution/` (`ProfitFox.Domain.ValueObjects.Execution`) | `ClosedCandle`, `CompiledGraph`, `CompiledNode`, `ExitDistance`, `FundingPayment`, `InputSource`, `PinValue`, `SignalEvent`, `StrategyStep` |
| `Execution/` | `Services/Execution/` (`ProfitFox.Domain.Services.Execution`) | `GraphRun`, `SignalExecutor`, `StrategyExecutor`, `StrategyRun` |
| `Graph/` | `ValueObjects/Graph/Enums/` (`ProfitFox.Domain.ValueObjects.Graph.Enums`) | `EntrySide` |
| `Graph/` | `ValueObjects/Graph/` (`ProfitFox.Domain.ValueObjects.Graph`) | `Signal`, `StrategyEntry`, `TradingStrategy` |
| `Graph/` | `Services/Graph/` (`ProfitFox.Domain.Services.Graph`) | `StrategyGraph` |
| `Graph/Model/` | `ValueObjects/Graph/Enums/` (`ProfitFox.Domain.ValueObjects.Graph.Enums`) | `PinType`, `PinTypes` |
| `Graph/Model/` | `ValueObjects/Graph/` (`ProfitFox.Domain.ValueObjects.Graph`) | `Durations`, `GraphDefinition`, `GraphValue`, `NodeDefinition`, `OutputDefinition`, `ParameterDefinition`, `PinReference`, `WireDefinition` |
| `Graph/Reading/` | `ValueObjects/Graph/Enums/` (`ProfitFox.Domain.ValueObjects.Graph.Enums`) | `GraphErrorCode` |
| `Graph/Reading/` | `ValueObjects/Graph/` (`ProfitFox.Domain.ValueObjects.Graph`) | `GraphError`, `ReadResult` |
| `Graph/Reading/` | `Services/Graph/` (`ProfitFox.Domain.Services.Graph`) | `GraphJson`, `GraphValidator` |
| `Hypotheses/` | `ValueObjects/Hypotheses/` (`ProfitFox.Domain.ValueObjects.Hypotheses`) | `Hypothesis`, `Timeframes` |
| `Hypotheses/` | `ValueObjects/Hypotheses/Enums/` (`ProfitFox.Domain.ValueObjects.Hypotheses.Enums`) | `Measure`, `TimeBlock` |
| `Hypotheses/` | `Services/Hypotheses/` (`ProfitFox.Domain.Services.Hypotheses`) | `HypothesisReader` |
| `Hypotheses/` | `Exceptions/` (`ProfitFox.Domain.Exceptions`) | `HypothesisException` |
| `Ledger/` | `ValueObjects/Ledger/` (`ProfitFox.Domain.ValueObjects.Ledger`) | `LedgerEntry`, `RunStatus` |
| `Ledger/` | `Services/Ledger/` (`ProfitFox.Domain.Services.Ledger`) | `LedgerRules` |
| `Ledger/` | `Exceptions/` (`ProfitFox.Domain.Exceptions`) | `LedgerException` |
| `MarketData/` | `ValueObjects/MarketData/` (`ProfitFox.Domain.ValueObjects.MarketData`) | `HistoryCandle`, `HistoryPayment`, `HistoryRead`, `Instrument`, `TimeRange` |
| `MarketData/` | `Services/MarketData/` (`ProfitFox.Domain.Services.MarketData`) | `Coverage`, `HistoryHash` |
| `Nodes/` | `ValueObjects/Nodes/` (`ProfitFox.Domain.ValueObjects.Nodes`) | `NodeContext`, `NodeKind`, `PinDeclaration`, `SettingDeclaration` |
| `Nodes/` | `Services/Nodes/Base/` (`ProfitFox.Domain.Services.Nodes.Base`) | `Node` |
| `Nodes/` | `Services/Nodes/` (`ProfitFox.Domain.Services.Nodes`) | `NodeCatalog` |
| `Nodes/Data/` | `Services/Nodes/Data/` (`ProfitFox.Domain.Services.Nodes.Data`) | все |
| `Nodes/Logic/` | `Services/Nodes/Logic/` (`ProfitFox.Domain.Services.Nodes.Logic`) | все |
| `Nodes/Position/` | `Services/Nodes/Position/` (`ProfitFox.Domain.Services.Nodes.Position`) | все |
| `Positions/` | `ValueObjects/Positions/` (`ProfitFox.Domain.ValueObjects.Positions`) | `Cycle` |
| `Positions/` | `Services/Positions/` (`ProfitFox.Domain.Services.Positions`) | `EntryPlan`, `PairPosition` |
| `Runs/` | `ValueObjects/Runs/` (`ProfitFox.Domain.ValueObjects.Runs`) | `EngineVersion`, `EventRow`, `RandomTrade`, `RunDesign`, `TradeFields` |
| `Runs/` | `ValueObjects/Runs/Enums/` (`ProfitFox.Domain.ValueObjects.Runs.Enums`) | `Period` |
| `Runs/` | `Services/Runs/` (`ProfitFox.Domain.Services.Runs`) | `Blocks`, `Evidence`, `MinutePaths`, `PairEvaluation`, `Periods`, `RandomEntries`, `SplitMix64` |
| `Signals/` | `ValueObjects/Signals/` (`ProfitFox.Domain.ValueObjects.Signals`) | `NodePosition`, `SignalDocument` |
| `Signals/` | `Services/Signals/` (`ProfitFox.Domain.Services.Signals`) | `PairCandles`, `PairEvents`, `SignalNames` |
| `Strategies/` | `Services/Strategies/` (`ProfitFox.Domain.Services.Strategies`) | все |
| `Trades/` | `ValueObjects/Trades/` (`ProfitFox.Domain.ValueObjects.Trades`) | `MinutePath`, `TradeCosts`, `TradeExit`, `TradeOutcome`, `TradePlan` |
| `Trades/` | `ValueObjects/Trades/Enums/` (`ProfitFox.Domain.ValueObjects.Trades.Enums`) | `ExitReason`, `TradeSide`, `TradeStatus` |
| `Trades/` | `Services/Trades/` (`ProfitFox.Domain.Services.Trades`) | `TradeSimulator` |
| `Verdicts/` | `ValueObjects/Verdicts/` (`ProfitFox.Domain.ValueObjects.Verdicts`) | `Verdict` |
| `Verdicts/` | `ValueObjects/Verdicts/Enums/` (`ProfitFox.Domain.ValueObjects.Verdicts.Enums`) | `VerdictKind` |
| `Verdicts/` | `Services/Verdicts/` (`ProfitFox.Domain.Services.Verdicts`) | `NormalDistribution`, `VerdictRules` |

### 6. Application: команды, запросы, контракты, порты, исключения

- Сценарий кладётся целиком, не дробится: меняет состояние — `Commands/<Тема>`, только читает — `Queries/<Тема>`. Что
  сценарий берёт на вход и что держит рядом (`RunOptions`, `RunProgress`, `RunReport`, `WorkerSettings`, `PreviewQuery`,
  кэш истории предпросмотра), лежит с ним.
- Уровня `<Имя>` из образца (`Commands/Candles/LoadCandles/`) нет: там это папка одной команды с обработчиком и результатом,
  здесь сценарий — один класс, и папка с его именем дала бы пространство имён, совпадающее с именем класса.
- `Contracts/<Тема>` — записи, которые отдаёт сценарий (`HypothesisAdded`, `RunResult`, `SignalEntry`, `PreviewOutcome`).
- `Ports/<Тема>` — интерфейсы портов и, по правилу `conventions.md`, записи, которые порт отдаёт или берёт и которых нет в
  домене (`StoredHypothesis`, `HypothesisKey`, `RunRecord`, `Renewal`, `Decision`, `CancelOutcome`, `SignalRead`), с
  константами их строк (`RunState`, `RunKinds`). Порты хранения остаются в Application, в `Domain/Repositories` не идут.
- `Exceptions` — исключения, которые порт или сценарий бросает, а вызывающий ловит; `SignalProblem` — причина
  `SignalLibraryException`, лежит с ним.
- Суффикс `Internal` образца не вводится: это переименование.

Таблица `ProfitFox.Application`:

| Было | Стало | Файлы |
|---|---|---|
| `Hypotheses/` | `Commands/Hypotheses/` (`ProfitFox.Application.Commands.Hypotheses`) | `HypothesisCatalog` |
| `Hypotheses/` | `Contracts/Hypotheses/` (`ProfitFox.Application.Contracts.Hypotheses`) | `HypothesisAdded`, `HypothesisItem` |
| `Hypotheses/` | `Ports/Hypotheses/` (`ProfitFox.Application.Ports.Hypotheses`) | `IHypothesisStore`, `HypothesisKey`, `StoredHypothesis` |
| `Hypotheses/` | `Exceptions/` (`ProfitFox.Application.Exceptions`) | `HypothesisNotFoundException` |
| `MarketData/` | `Ports/MarketData/` (`ProfitFox.Application.Ports.MarketData`) | `IHistorySource`, `IInstrumentCatalog` |
| `MarketData/` | `Queries/MarketData/` (`ProfitFox.Application.Queries.MarketData`) | `MarketHistory` |
| `MarketData/` | `Exceptions/` (`ProfitFox.Application.Exceptions`) | `HistoryUnavailableException`, `PeriodBoundaryException` |
| `Preview/` | `Queries/Preview/` (`ProfitFox.Application.Queries.Preview`) | `SignalPreview`, `PreviewQuery`, `PreviewHistory`, `PreviewHistoryCache` |
| `Preview/` | `Contracts/Preview/` (`ProfitFox.Application.Contracts.Preview`) | `PreviewError`, `PreviewOutcome`, `SignalPreviewResult` |
| `Runs/` | `Commands/Runs/` (`ProfitFox.Application.Commands.Runs`) | `HypothesisRunner`, `LeaseKeeper`, `RunExecutor`, `RunScheduler`, `RunOptions`, `RunProgress`, `RunReport`, `WorkerSettings` |
| `Runs/` | `Queries/Runs/` (`ProfitFox.Application.Queries.Runs`) | `RunBoard` |
| `Runs/` | `Contracts/Runs/` (`ProfitFox.Application.Contracts.Runs`) | `PairData`, `RunAvailability`, `RunDetails`, `RunResult` |
| `Runs/` | `Ports/Runs/` (`ProfitFox.Application.Ports.Runs`) | `IRunLedger`, `IRunQueue`, `CancelOutcome`, `Decision`, `FinishedRun`, `LedgerResult`, `Renewal`, `RunAssignment`, `RunKinds`, `RunRecord`, `RunState` |
| `Runs/` | `Exceptions/` (`ProfitFox.Application.Exceptions`) | `LeaseLostException` |
| `Signals/` | `Commands/Signals/` (`ProfitFox.Application.Commands.Signals`) | `SignalLibrary` |
| `Signals/` | `Contracts/Signals/` (`ProfitFox.Application.Contracts.Signals`) | `SignalEntry` |
| `Signals/` | `Ports/Signals/` (`ProfitFox.Application.Ports.Signals`) | `ISignalStore`, `SignalRead` |
| `Signals/` | `Exceptions/` (`ProfitFox.Application.Exceptions`) | `SignalLibraryException`, `SignalProblem` |

### 7. Infrastructure: база и клиент сервиса данных

- Хранилища — `Persistence/Postgres/Dapper/Repositories/<Тема>`, как репозитории образца, со своими подпапками: строки
  таблиц — `Contracts`, тексты запросов — `Queries` (`LedgerSql`), переводы — `Converters` (`SignalJson`). Имена
  (`HypothesisStore`, `RunLedger`, `*Row`) не меняются.
- Миграция — `Persistence/Postgres/FluentMigrator/Migrations`, имя файла прежнее. Регистрация базы `BenchInfrastructure` и
  версия схемы `BenchSchema` — `Persistence/Postgres`, как `PostgresRegistrar` образца.
- Сервис данных — `Clients/MarketData` (решение 4). В `.proto` меняется только `csharp_namespace`:
  `ProfitFox.Infrastructure.Clients.MarketData.Protos`; путь `Protobuf Include` в `.csproj` — за папкой.
- `BackgroundServices` и `Observability` нет: таких типов в Infrastructure нет.

Таблица `ProfitFox.Infrastructure`:

| Было | Стало | Файлы |
|---|---|---|
| `Hypotheses/` | `Persistence/Postgres/Dapper/Repositories/Hypotheses/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses`) | `HypothesisStore` |
| `Hypotheses/` | `Persistence/Postgres/Dapper/Repositories/Hypotheses/Contracts/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Contracts`) | `HypothesisRow` |
| `Ledger/` | `Persistence/Postgres/Dapper/Repositories/Ledger/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger`) | `RunLedger` |
| `Ledger/` | `Persistence/Postgres/Dapper/Repositories/Ledger/Contracts/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Contracts`) | `FinishedRow`, `LedgerRow` |
| `Ledger/` | `Persistence/Postgres/Dapper/Repositories/Ledger/Queries/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger.Queries`) | `LedgerSql` |
| `Runs/` | `Persistence/Postgres/Dapper/Repositories/Runs/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs`) | `RunQueue` |
| `Runs/` | `Persistence/Postgres/Dapper/Repositories/Runs/Contracts/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs.Contracts`) | `RunRow` |
| `Signals/` | `Persistence/Postgres/Dapper/Repositories/Signals/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals`) | `SignalStore` |
| `Signals/` | `Persistence/Postgres/Dapper/Repositories/Signals/Contracts/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Contracts`) | `SignalRow` |
| `Signals/` | `Persistence/Postgres/Dapper/Repositories/Signals/Converters/` (`ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals.Converters`) | `SignalJson` |
| `MarketData/` | `Clients/MarketData/` (`ProfitFox.Infrastructure.Clients.MarketData`) | все |
| `MarketData/Protos/` | `Clients/MarketData/Protos/` | все |
| `Persistence/` | `Persistence/Postgres/` (`ProfitFox.Infrastructure.Persistence.Postgres`) | все |
| `Persistence/Migrations/` | `Persistence/Postgres/FluentMigrator/Migrations/` (`ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations`) | все |

### 8. Presentation, Worker, Cli

- `Controllers` остаются. `Program.cs` и `BenchPresentation.cs` (сборка служб API) — в корне проекта, как `Program.cs`
  образца и `InfrastructureRegistrar.cs` в корне его Infrastructure.
- `ExceptionHandlers`, `GrpcServices`, `Protos`, `Settings` не создаются: таких типов нет.
- В Worker и Cli образца нет; раскладка — по тому же принципу (решение 4), `Program.cs` и `CliApp.cs` — в корне, `Api` в Cli
  остаётся.

Таблица `ProfitFox.Presentation`:

| Было | Стало | Файлы |
|---|---|---|
| `./` | `HealthChecks/` (`ProfitFox.Presentation.HealthChecks`) | все |
| `Errors/` | `Contracts/Errors/` (`ProfitFox.Presentation.Contracts.Errors`) | `ErrorResponse`, `RequestError`, `RequestWire` |
| `Errors/` | `Contracts/Errors/Converters/` (`ProfitFox.Presentation.Contracts.Errors.Converters`) | `RequestErrors` |
| `Hypotheses/` | `Contracts/Hypotheses/` (`ProfitFox.Presentation.Contracts.Hypotheses`) | `HypothesisAddedResponse`, `HypothesisResponse`, `RunAvailabilityResponse` |
| `Hypotheses/` | `Formatters/` (`ProfitFox.Presentation.Formatters`) | `PlainTextInputFormatter` |
| `Instruments/` | `Contracts/Instruments/` (`ProfitFox.Presentation.Contracts.Instruments`) | все |
| `Nodes/` | `Contracts/Nodes/` (`ProfitFox.Presentation.Contracts.Nodes`) | `NodeKindResponse`, `PinResponse`, `SettingResponse` |
| `Nodes/` | `Contracts/Nodes/Converters/` (`ProfitFox.Presentation.Contracts.Nodes.Converters`) | `NodeCatalogResponse`, `NodeTitles` |
| `Preview/` | `Contracts/Preview/` (`ProfitFox.Presentation.Contracts.Preview`) | `PreviewCandle`, `PreviewEvent`, `PreviewRange`, `PreviewRequest`, `PreviewResponse` |
| `Preview/` | `Contracts/Preview/Converters/` (`ProfitFox.Presentation.Contracts.Preview.Converters`) | `PreviewResponses` |
| `Runs/` | `Contracts/Runs/` (`ProfitFox.Presentation.Contracts.Runs`) | `CreateRunRequest`, `RunConflictResponse`, `RunResponse`, `RunResultResponse` |
| `Runs/` | `Contracts/Runs/Converters/` (`ProfitFox.Presentation.Contracts.Runs.Converters`) | `RunResponses` |
| `Signals/` | `Contracts/Signals/` (`ProfitFox.Presentation.Contracts.Signals`) | `CreateSignalRequest`, `RenameSignalRequest`, `ReplaceSignalRequest`, `SignalListItem`, `SignalResponse`, `ValidateGraphRequest`, `ValidationResponse` |
| `Signals/` | `Contracts/Signals/Converters/` (`ProfitFox.Presentation.Contracts.Signals.Converters`) | `SignalResponses` |

Таблица `ProfitFox.Worker`:

| Было | Стало | Файлы |
|---|---|---|
| `./` | `BackgroundServices/` (`ProfitFox.Worker.BackgroundServices`) | `RunBackgroundService` |
| `./` | `Logging/` (`ProfitFox.Worker.Logging`) | `LogWriter` |

Таблица `ProfitFox.Cli`:

| Было | Стало | Файлы |
|---|---|---|
| `Hypotheses/` | `Commands/Hypotheses/` (`ProfitFox.Cli.Commands.Hypotheses`) | `AddHypothesisCommand` |
| `Hypotheses/` | `Contracts/Hypotheses/` (`ProfitFox.Cli.Contracts.Hypotheses`) | `HypothesisAddedDto` |
| `Runs/` | `Commands/Runs/` (`ProfitFox.Cli.Commands.Runs`) | `CancelCommand`, `RunCommand`, `RunsCommand` |
| `Runs/` | `Contracts/Runs/` (`ProfitFox.Cli.Contracts.Runs`) | `CreateRunDto`, `RunDto`, `RunResultDto` |

### 9. Граница расчёта после переноса

Решение 2 `merge-bench-and-bot` открывало внутренности расчёта только остальному Domain и требовало, чтобы код Domain вне
папок `Graph`, `Nodes`, `Execution`, `Trades`, `Positions` пользовался открытыми входами. После переноса этих пяти папок нет,
и правило пишется по именам: `internal`-типы расчёта — `CompiledGraph`, `CompiledNode`, `InputSource`, `NodeContext`, `Node`
и узлы `Services/Nodes/*`, `GraphJson`, `GraphValidator`, `StrategyRun`. Модификаторы доступа не меняются. Правило на ревью:
к этим типам обращаются только файлы, которые обращались к ним до переноса.

### 10. Тесты зеркалят слои

- `Tests.Unit/Domain/<Вид>/<Тема>`, `Tests.Unit/Application/<Вид>/<Тема>`; тест разбора каталога сервиса данных —
  `Tests.Unit/Infrastructure/Clients/MarketData` (проект не меняется, решение 2).
- `Tests.Integration/Infrastructure/...`, `Tests.Integration/Presentation/...`, `Tests.Integration/Application/Commands/Runs`
  (прогон гипотезы с базой, как `Tests.Integration/Application/Commands` образца), `Tests.Integration/Worker/...`,
  `Tests.Integration/Cli`.
- Помощники лежат с тестами, которые ими пользуются (`Graphs`, `History`, `Paths`, `Designs`, `FakeHistorySource`,
  `MemorySignalStore`). Общие помощники интеграционных тестов — в корне проекта (`BenchRepository`) и в папках
  `Presentation` (`BenchPresentationServer`, `BenchStart`, `LedgerLines`), `Worker` (`Poll`, `WorkerHost`), `MarketData`
  (поддельный сервис данных), как `Http`, `Sockets`, `Time` в корне интеграционных тестов образца.
- `BenchDatabase.cs` — `[SetUpFixture]` NUnit на всю сборку: он действует только на своё пространство имён и вложенные,
  поэтому `namespace` остаётся `ProfitFox.Tests.Integration`. Файл переезжает в корень проекта, и папка с пространством
  имён совпадают.
- Папки эталонов едут со своими тестами, их содержимое не меняется: `Fixtures/Verdict` →
  `Domain/Services/Verdicts/Fixtures/Verdict`, `Graph/Fixtures/H0001` → `Domain/Services/Execution/Fixtures/H0001` (её
  читают `H0001ParityTests` и `H0001StrategyParityTests`), `Trades/Fixtures/Outcomes` →
  `Domain/Services/Trades/Fixtures/Outcomes`. Строки путей в `VerdictRulesTests`, `H0001ParityTests`,
  `H0001StrategyParityTests`, `OutcomeParityTests` поправляются. `Tests.Integration/Fixtures` с `EngineVersions.json` не
  трогается: её читают два теста из одной папки, и она остаётся в корне проекта.

Таблица `ProfitFox.Tests.Unit`:

| Было | Стало | Файлы |
|---|---|---|
| `Execution/` | `Domain/Services/Execution/` (`ProfitFox.Tests.Unit.Domain.Services.Execution`) | `ExecutionTests`, `H0001ParityTests`, `H0001StrategyParityTests`, `History`, `StrategyExecutionTests` |
| `Execution/` | `Domain/Services/Nodes/` (`ProfitFox.Tests.Unit.Domain.Services.Nodes`) | `NodeTests` |
| `Graph/Fixtures/H0001/` | `Domain/Services/Execution/Fixtures/H0001/` | все |
| `Graph/` | `Domain/Services/Graph/` (`ProfitFox.Tests.Unit.Domain.Services.Graph`) | все |
| `Hypotheses/` | `Domain/Services/Hypotheses/` (`ProfitFox.Tests.Unit.Domain.Services.Hypotheses`) | все |
| `Ledger/` | `Domain/Services/Ledger/` (`ProfitFox.Tests.Unit.Domain.Services.Ledger`) | все |
| `MarketData/` | `Domain/Services/MarketData/` (`ProfitFox.Tests.Unit.Domain.Services.MarketData`) | `CoverageTests` |
| `MarketData/` | `Application/Queries/MarketData/` (`ProfitFox.Tests.Unit.Application.Queries.MarketData`) | `FakeHistorySource`, `MarketHistoryTests` |
| `MarketData/` | `Infrastructure/Clients/MarketData/` (`ProfitFox.Tests.Unit.Infrastructure.Clients.MarketData`) | `InstrumentCatalogTests` |
| `Nodes/` | `Domain/Services/Nodes/` (`ProfitFox.Tests.Unit.Domain.Services.Nodes`) | все |
| `Positions/` | `Domain/Services/Positions/` (`ProfitFox.Tests.Unit.Domain.Services.Positions`) | все |
| `Runs/` | `Domain/Services/Runs/` (`ProfitFox.Tests.Unit.Domain.Services.Runs`) | `Designs`, `PairEvaluationTests`, `RunRulesTests` |
| `Runs/` | `Application/Commands/Runs/` (`ProfitFox.Tests.Unit.Application.Commands.Runs`) | `LeaseKeeperTests`, `RunReportTests` |
| `Signals/` | `Domain/Services/Signals/` (`ProfitFox.Tests.Unit.Domain.Services.Signals`) | `PairEventsTests` |
| `Signals/` | `Application/Commands/Signals/` (`ProfitFox.Tests.Unit.Application.Commands.Signals`) | `MemorySignalStore`, `SignalLibraryTests` |
| `Strategies/` | `Domain/Services/Strategies/` (`ProfitFox.Tests.Unit.Domain.Services.Strategies`) | все |
| `Trades/Fixtures/Outcomes/` | `Domain/Services/Trades/Fixtures/Outcomes/` | все |
| `Trades/` | `Domain/Services/Trades/` (`ProfitFox.Tests.Unit.Domain.Services.Trades`) | все |
| `Fixtures/Verdict/` | `Domain/Services/Verdicts/Fixtures/Verdict/` | все |
| `Verdicts/` | `Domain/Services/Verdicts/` (`ProfitFox.Tests.Unit.Domain.Services.Verdicts`) | все |

Таблица `ProfitFox.Tests.Integration`:

| Было | Стало | Файлы |
|---|---|---|
| `Database/` | корень проекта (`ProfitFox.Tests.Integration`) | `BenchDatabase`, `BenchRepository` |
| `Database/` | `Infrastructure/Persistence/Postgres/Dapper/Repositories/Hypotheses/` (`ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses`) | `HypothesisStoreTests` |
| `Database/` | `Infrastructure/Persistence/Postgres/Dapper/Repositories/Ledger/` (`ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ledger`) | `RunLedgerTests` |
| `Database/` | `Infrastructure/Persistence/Postgres/Dapper/Repositories/Runs/` (`ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Runs`) | `RunQueueTests` |
| `Database/` | `Infrastructure/Persistence/Postgres/Dapper/Repositories/Signals/` (`ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Signals`) | `SignalStoreTests` |
| `Database/` | `Infrastructure/Persistence/Postgres/FluentMigrator/` (`ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.FluentMigrator`) | `MigrationTests` |
| `MarketData/` | `Infrastructure/Clients/MarketData/` (`ProfitFox.Tests.Integration.Infrastructure.Clients.MarketData`) | все |
| `Presentation/` | `Presentation/Controllers/` (`ProfitFox.Tests.Integration.Presentation.Controllers`) | все |
| `Runs/` | `Application/Commands/Runs/` (`ProfitFox.Tests.Integration.Application.Commands.Runs`) | все |
| `Workers/` | `Worker/BackgroundServices/` (`ProfitFox.Tests.Integration.Worker.BackgroundServices`) | `ParityThroughWorkerTests`, `WorkerTests` |
| `Workers/` | `Worker/` (`ProfitFox.Tests.Integration.Worker`) | `Poll`, `WorkerHost` |

### 11. Как правятся `using` и полные имена

- Для каждого файла старые `using ProfitFox.…` заменяются пространствами имён, куда разошлись типы, плюс пространства имён,
  которые файл раньше видел без `using` (своё и родительские). Затем лишние удаляет `IDE0005`. `using` сортируются:
  `System.*` первыми, затем по алфавиту, одной группой, как сейчас; `using static` — последними.
- Полное имя типа в коде меняется вместе с пространством имён. Таких строк восемь, все в тестах: `FakeCandleFeed`,
  `FakeFundingFeed`, `PreviewTests` ×2 (`…Protos.*`), `CliTests`, `HypothesesTests`, `RunsTests` (`HypothesisReader`),
  `RunReportTests` (`TimeBlock`).
- Если после слияния тем имя стало неоднозначным, это чинится `using`-псевдонимом или полным именем в месте использования;
  типы и папки ради этого не переименовываются.

### 12. Что не переименовывается и не меняется

Имена типов и членов, модификаторы доступа, адреса API и имена операций OpenAPI, таблицы и столбцы базы, номер миграции,
ключи настроек, команда `bench`, `EngineVersion.Number`, `EngineVersions.json`, содержимое папок `Fixtures`,
`docker-compose*.yml`, `deploy/`, `frontend/`, `bench/`. `Dockerfile` и `.claude/launch.json` ссылаются на пути проектов,
которые не меняются, поэтому не правятся; `.github/workflows/ci.yml` — тоже.

## Risks / Trade-offs

- [Перенос незаметно меняет логику] → ожидания тестов не правятся, число тестов равно базе, `EngineVersionTests` и
  `H0001ParityTests` зелёные; скрипт сравнивает каждый перенесённый `.cs` с исходным без строк `using` и `namespace`.
- [Тип из своего нового пространства имён тихо заслоняет тип из `using`] → слияние тем собирает в одно пространство имён
  типы нескольких старых. Повторяющиеся имена типов в решении проверяются списком до переноса; неоднозначность компилятор
  ловит сам.
- [Схема OpenAPI берёт имя из пространства имён] → `schema.d.ts` после `generate:api` обязан совпасть с базой; иначе это
  находка, а не повод править фронтенд.
- [`[SetUpFixture]` перестаёт действовать на тесты] → `namespace` `BenchDatabase` прежний; 162 интеграционных теста —
  проверка.
- [Анализаторы стиля в сборке мешают будущим правкам] → это и есть цель образца; правила с уровнем `error` — те же, что у
  владельца в Rider.
- [Широкий дифф конфликтует с параллельными ветками] → других веток с кодом бэкенда нет; изменение сливается сразу после
  `merge-bench-and-bot`.
- [В образе нет `.editorconfig`] → `Dockerfile` копирует только `backend/`, поэтому при сборке образа правила стиля не
  проверяются; их проверяет CI. Сборке образа это не мешает.

## Migration Plan

1. Шаги применения — группы tasks.md, каждая кончается зелёной сборкой, зелёными тестами и коммитом.
2. Схема базы не меняется, миграций нет.
3. Пул-реквест и слияние — по слову владельца, после `merge-bench-and-bot`.
4. Откат — возврат коммита слияния.

## Open Questions

- Нужны ли позже MediatR, команды и запросы с обработчиками и порты хранения в `Domain/Repositories`, как в образце.
- Нужны ли `scripts/verify-format.sh` и хук `pre-commit` образца.
- Переносить ли `InstrumentCatalogTests` в интеграционные тесты, чтобы `Tests.Unit` не ссылался на Infrastructure.
