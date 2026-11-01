# Code conventions

Layout and naming the owner expects in `backend/`. Follow them when adding a type, folder or project. Where a check
enforces a rule, the check is the source of truth.

## Domain (`ProfitFox.Domain`)

- Areas: `MarketData` (candles and their import), `Strategies` (strategies, composite nodes and their versions),
  `Bots` (a strategy set up to trade: parameter values, instrument, interval, balance) and `Backtests` (a run of a bot on
  stored candles and its results).
- `Entities/<Area>` holds entities (`Candle`, `CandleImportJob`, `Bot`, `Backtest`, `BacktestChunk`); `Entities/<Area>/Enums` holds enums
  and their pure domain extensions (`Interval`, `IntervalExtensions.CandleEnd`, `BacktestStatus`); `Entities/Base` holds
  `AggregateRoot`. A bot is part of the `StrategyLibrary` aggregate: it must stay valid against the latest version of its
  strategy.
- `ValueObjects/<Area>` holds value objects (`Instrument`, `Price`, `Volume`, `InstrumentRules`, `StrategySnapshot`,
  `ParameterSpec`, `SimulationSettings`).
- `Repositories` holds storage ports, named `I<Name>Repository` (`IStrategyLibraryRepository` for strategies, nodes,
  versions and bots; `IBacktestRepository`). `Events` holds domain events.
- `Services/<Area>` holds domain logic that has no state of its own in the database, split into folders by purpose
  (`Services/Strategies/Nodes`, `BuiltIns`, `Validation`, `Execution`). Logic that belongs to an entity or a value object
  stays on it. `Services/Backtests` holds the exchange simulator and the metrics of a backtest (`ParameterGrid`,
  `BacktestReplay`, its observers, `EngineVersion`); the strategy engine itself stays in `Services/Strategies/Execution`.
- `Exceptions` holds domain exceptions: a broken domain rule that the caller reports to the user
  (`GraphValidationException`, `NodeInUseException`, `BotsAffectedException`).
- The domain knows no wire codes and no vendor names: enums, not strings. Codes are mapped at the boundaries:
  API and storage codes in Application, vendor codes in the vendor adapter.

## Application (`ProfitFox.Application`)

- CQRS on MediatR, one folder per use case: `<Area>/Commands/<Name>/<Name>Command` and `<Name>CommandHandler`,
  `<Area>/Queries/<Name>/<Name>Query`, `<Name>QueryHandler` and `<Name>Result` (`Bots/Commands/CreateBot`,
  `Backtests/Commands/RunBacktest`, `Backtests/Queries/GetBacktestResults`).
- A command returns nothing or an id. Invalid input raises `ValidationFailedException` with errors keyed by parameter
  name. A query may return a result that carries errors.
- `Ports/<Area>` holds ports to external systems and mechanisms (`IMarketHistoryClient`, `IInstrumentRulesProvider`). Port names say what the port does, never which vendor implements it.
- `Exceptions` holds `BaseApplicationException` and its subclasses, also vendor-neutral (`ExchangeException`).
- `Workers` holds what the Worker needs to share work with other Workers: `WorkerSettings`, `WorkerIdentity` (the
  lease owner of this process) and `LeaseKeeper`, which renews a lease while the work runs. Work the Worker takes is a
  command that returns whether it found any (`RunNextCandleImportCommand`, `RunNextBacktestWorkCommand`).
- Parsing of API input lives here: `MarketDataCodes`, `StrategyCodes` and `BacktestCodes` for codes, `ApiTimestamps`
  for times, `StrategyJson` for strategy and composite node definitions and strategy versions. A JSON document that is
  both the API body and the stored `jsonb` value has one codec here, used by Presentation and Infrastructure alike.
  Times in the API carry an offset or `Z`; without one, parsers assume the server's time zone.

## Infrastructure (`ProfitFox.Infrastructure`)

- `InfrastructureRegistrar` is the only public entry point: `Configure` for the API, `ConfigureWorker` for the Worker.
  Both call internal registrars such as `Mediatr/MediatrRegistrar` and `Persistence/Postgres/PostgresRegistrar`; only
  `ConfigureWorker` registers the background services that run work.
- `Exchanges/<Vendor>` holds adapters over vendor SDKs, including the mapping of domain values to SDK types
  (`BybitCodes`). `Exchanges/Retry` holds Polly pipelines.
- `Persistence/Postgres/Dapper/Repositories/<Area>` holds repositories, with `Contracts` (`*Db` rows), `Converters`
  (`*DbConverter`) and `Queries` (`*RepositoryQueries`, the SQL). `Persistence/Postgres/FluentMigrator/Migrations` holds
  raw-SQL migrations, `FluentMigrator/Extensions` holds `MigrateToLatestVersion`.
- `Persistence/Postgres/Dapper/Repositories/Strategies` also stores bots and strategy versions: they are saved with the
  library in one transaction. `Repositories/Backtests` holds backtests, their chunks (`backtest_chunks`) and results.
  A lease is a column pair `lease_owner`, `leased_until` on the row of the work; a write of leased work is conditional
  on `lease_owner`.
- `BackgroundServices` holds hosted services; a loop over items catches each item's failure, otherwise one exception
  stops the host. A startup step that must finish before requests are served is an `IHostedService` with the work in
  `StartAsync`, not a `BackgroundService` (`StrategyVersionsInitializationService`, `BacktestInitializationService`).
  A Worker loop asks for the next work and waits `Worker:PollInterval` when there is none.
- Classes are `internal`; test projects see them through `InternalsVisibleTo`. Migrations stay `public`: FluentMigrator
  finds only public migration classes.

## Settings

- Service settings live in `appsettings.json`, one section per area; a test-only value goes to `appsettings.Testing.json`.
  Application declares a settings record (`BacktestSettings`), Infrastructure binds the section to it in
  `InfrastructureRegistrar`; handlers take the record, never `IConfiguration`.
- The `Backtests` section: the limit of combinations, the number of threads and the default fee rates in percent by
  market category.

  ```json
  "Backtests": {
    "MaxCombinations": 10000,
    "MaxParallelism": null,
    "DefaultFees": {
      "spot": { "Limit": 0.1, "Market": 0.1 },
      "linear": { "Limit": 0.02, "Market": 0.055 }
    }
  }
  ```

  Without `MaxParallelism`, a backtest uses `Environment.ProcessorCount − 1` threads, at least one.

- The `Worker` section, in the Worker's `appsettings.json` only: how often a Worker polls the database, how long a lease
  lasts and how often it is renewed (shorter than the lease, or the Worker does not start), and the combinations in a
  chunk of a backtest.

  ```json
  "Worker": {
    "PollInterval": "00:00:01",
    "LeaseDuration": "00:00:30",
    "LeaseRenewal": "00:00:10",
    "ChunkSize": 100
  }
  ```

## Presentation (`ProfitFox.Presentation`)

- `Controllers` holds controllers only (`BotsController`, `BacktestsController`); request and response types go to
  `Contracts/<Area>` (`Contracts/Bots`, `Contracts/Backtests`).
- Mark required contract members with `[Required]`, not the C# `required` keyword: a missing JSON property is then
  reported under its own name, not under `$`.
- Application services are registered only through `InfrastructureRegistrar.Configure`. `Program` calls it and
  `MigrateToLatestVersion` and configures the host itself: controllers, Serilog, the OpenAPI document and Swagger UI,
  and the middleware pipeline.
- Log levels and format live in the `Serilog` section of `appsettings*.json`, not in code: JSON by default, text in
  `appsettings.Development.json`.

## Worker (`ProfitFox.Worker`)

- A generic host with no HTTP. `Program.Configure` adds Serilog and `InfrastructureRegistrar.ConfigureWorker` to the host
  builder; `Main` builds and runs it, and a test host calls `Configure` too. The Worker never migrates the database: the
  API does.
- Log levels and format follow the same rules as the API, in the Worker's own `appsettings*.json`.

## Files

- One top-level type per file, named after the type. A type used only by its enclosing class may stay nested in it.
- Namespaces follow folders.

## Tests

- `Tests.Unit` mirrors the `src` folders. `Tests.Integration` covers the database and HTTP with Testcontainers.
  `ProfitFoxFactory` runs the service in the `Testing` environment: test-only settings go to
  `ProfitFox.Presentation/appsettings.Testing.json`, the connection string comes from the container. Next to the API it
  starts `Workers` instances of `WorkerHost` (one by default, none to keep work `Pending`) on the same database, fake
  Bybit and clock; a test may start more `WorkerHost`s itself. Both hosts validate that every registered service resolves.
  A Worker waits its poll interval on the fake clock, so a test that waits for work moves the clock.
  `Tests.Shared` holds AutoFixture generators under `Generators/Base` and
  `Generators/Domain/{Entity,ValueObject}/<Area>`, each a static `<Type>Generator.Generate(...)`. `Builders/<Area>` holds
  fluent builders for structures a generator cannot randomize meaningfully (`GraphBuilder` for strategy graphs), and
  `Fixtures/<Area>` holds reference documents as embedded resources (the reference strategies EMA, EMA Short, DCA, Ladder;
  reference candle sets for backtests in `Fixtures/Backtests`).
- Ports are replaced with NSubstitute mocks; there are no hand-written fakes. Hand-written stubs exist only for HTTP
  transport (`StubHttpHandler`, `FakeBybitHandler`), because NSubstitute cannot replace the protected `SendAsync`.
- Assertions use Shouldly.
- Generators randomize only values the test does not check. Reference data (decimal precision, period bounds, interval
  codes) stays explicit in the test.
- A vendor SDK that builds its own `HttpClient` is replaced in tests as a whole client. `OutboundHttpGuard` sends any
  request a test failed to replace to a dead proxy, so it fails instead of reaching the exchange.
