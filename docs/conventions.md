# Code conventions

Layout and naming the owner expects in `backend/`. Follow them when adding a type, folder or project. Where a check
enforces a rule, the check is the source of truth.

## Domain (`ProfitFox.Domain`)

- Areas: `MarketData` (candles and their import), `Strategies` (strategies, composite nodes and their versions),
  `Bots` (a strategy set up to trade: parameter values, instrument, interval, balance), `Backtests` (a run of a bot on
  stored candles and its results), `ExchangeAccounts` (keys of an exchange account a bot trades with) and `LiveTrading`
  (a bot on the exchange: `LiveBot` with its status and lease, its positions, orders and fills; `ClientOrderId`, the id an
  order carries to the exchange; `Reconciliation` and `StartFunds`, the pure checks against the exchange).
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
  `BacktestReplay`, its observers, `EngineVersion`); the strategy engine itself stays in `Services/Strategies/Execution`,
  with the position of a run (`Position`, `PositionStatus`, `ClosedPosition`) until positions get storage of their own.
  Replay observers see each placed order with the number of its position, each confirmed cancel and each fill; the
  details they collect are value objects in `ValueObjects/Backtests` (`BacktestPosition`, `BacktestOrder`,
  `BacktestFill`).
- `Exceptions` holds domain exceptions: a broken domain rule that the caller reports to the user
  (`GraphValidationException`, `NodeInUseException`, `BotsAffectedException`).
- The domain knows no wire codes and no vendor names: enums, not strings. Codes are mapped at the boundaries:
  API and storage codes in Application, vendor codes in the vendor adapter.

## Application (`ProfitFox.Application`)

- CQRS on MediatR, one folder per use case: `<Area>/Commands/<Name>/<Name>Command` and `<Name>CommandHandler`,
  `<Area>/Queries/<Name>/<Name>Query`, `<Name>QueryHandler` and `<Name>Result` (`Bots/Commands/CreateBot`,
  `Backtests/Commands/RunBacktest`, `Backtests/Queries/GetBacktestResults`).
- A command returns nothing or an id. Invalid input raises `ValidationFailedException` with errors keyed by parameter
  name. A query may return a result that carries errors: `ValidateStrategyQuery` and `ValidateNodeQuery` run the
  library operation of a save without saving and return a `ValidationReport`.
- `Strategies/NodeSettingSpecs` is the one table of simple node settings (`side`, `size`, `operation`, `type`): reading a
  definition in `StrategyJson` and the node catalog both use it. A new setting is added there and nowhere else.
- `Ports/<Area>` holds ports to external systems and mechanisms (`IMarketHistoryClient`, `IInstrumentRulesProvider`, `IInstrumentCatalog`,
  `IExchangeKeyInspector`, `Security/ISecretProtector`). Port names say what the port does, never which
  vendor implements it. `IInstrumentCatalog.ListAsync(category)` returns every instrument the exchange lists in a market
  category (`ListedInstrument`: symbol, base coin, quote coin). `SearchInstrumentsQuery(category, query, limit)` filters that
  list with the pure function `InstrumentSearch.Find`: a case-insensitive substring (spaces around the query are ignored) of the symbol or a coin, symbols that start
  with the query first, then by symbol ordinally, cut to `limit` (1 to 100, 20 by default). Its result carries field errors
  like `GetCandlesQuery`.
- `Exceptions` holds `BaseApplicationException` and its subclasses, also vendor-neutral (`ExchangeException`,
  `ExchangeKeyRejectedException`).
- `ExchangeAccounts` holds the commands and queries of exchange accounts and `ExchangeKeyRules`, the pure check of what a key may do. The secret of an account is decrypted only in the query
  that calls the exchange with it, and a bot refers to its account by id (`BotAccount` carries the categories the key
  trades in into the checks of the bot).
- `Transactions/ITransactionExecutor` runs a unit of work in one transaction: its writes commit together or not at all,
  a unit inside a unit is refused, and `EnsureNoTransaction` guards a call that must not hold a transaction open (the
  exchange).
- `LiveTrading` holds the commands of live trading (start, pause, resume, stop, close the position, claim and run a bot),
  `LiveTradingCodes`, and `Runtime/LiveBotSession`: one session keeps one bot for the take of its lease, takes its inputs
  one at a time from a queue, writes every change in one unit of work after checking the lease, and sends orders only
  once they are written. `Ports/LiveTrading` holds `IExchangeLedger` (REST reads of an account), `IOrderGateway` (orders
  and cancels, a refusal is an outcome) and `IExchangeStreams` (closed candles, order updates and fills, and a signal
  when a stream is back after a gap).
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
  (`BybitCodes`). `Exchanges/Retry` holds Polly pipelines. `BybitMarketDataClient` implements `IInstrumentCatalog` (spot in one
  answer, linear by following the cursor; one attempt per request). The registered `IInstrumentCatalog` is
  `Exchanges/CachedInstrumentCatalog` over it: a list per category is kept for 5 minutes on `TimeProvider`, a failure is not kept.
- `Exchanges/Bybit/BybitAccountClient` implements `IExchangeKeyInspector`: a typed `HttpClient` that builds a Bybit client
  with the key for one call and drops it, one attempt per call. `Security/AesGcmSecretProtector` encrypts secrets with the
  master key (AES-256-GCM, the account id as associated data).
- `Exchanges/Bybit/BybitStreams` implements `IExchangeStreams` as one singleton for the process, so the connections to
  Bybit follow the pairs and accounts, not the bots: one public socket client whose connections carry one kline topic
  per category, symbol and interval, and one private client per account key with the `order` and `execution` topics.
  The first subscriber of a topic subscribes it at Bybit, the others join; the last to leave takes it off. Every
  subscriber of a topic gets all its events and a restored connection; a session picks its own orders by `orderLinkId`.
- `Persistence/Postgres/Dapper/Repositories/<Area>` holds repositories, with `Contracts` (`*Db` rows), `Converters`
  (`*DbConverter`) and `Queries` (`*RepositoryQueries`, the SQL). `Persistence/Postgres/FluentMigrator/Migrations` holds
  raw-SQL migrations, `FluentMigrator/Extensions` holds `MigrateToLatestVersion`.
- `Persistence/Postgres/Dapper/Repositories/Strategies` also stores bots and strategy versions: they are saved with the
  library in one transaction. `Repositories/Backtests` holds backtests, their chunks (`backtest_chunks`) and results.
  A lease is a column pair `lease_owner`, `leased_until` on the row of the work; a write of leased work is conditional
  on `lease_owner`.
- A lease of a live bot also has an epoch, `lease_epoch`, grown by every claim: every write of a session locks the row of
  its bot with the owner and the epoch it took, so a Worker that lost the bot and took it again is told from its
  stalled self.
- `Persistence/Postgres/Transactions` implements the unit of work: `PostgresTransactionExecutor` opens the connection
  and the transaction, the scoped `PostgresTransactionAccessor.Current` hands them to repositories that write inside a
  unit. Only repositories of live trading use it; the older repositories open a connection per call and keep their
  transactions inside one method.
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

- The `Bybit` section (`BybitSettings`, bound in `InfrastructureRegistrar`): `RestAddress` sends REST calls to another
  address instead of the live Bybit, for end-to-end runs against a stub; plain `http://127.0.0.1:port` works. Unset, the
  client uses the live Bybit.

  ```json
  "Bybit": { "RestAddress": "http://127.0.0.1:5296" }
  ```

- The `ExchangeAccounts` section (`ExchangeAccountSettings`): `MasterKey`, 32 bytes in base64, encrypts the secrets of
  exchange accounts. Only `appsettings.Development.json` and `appsettings.Testing.json` of the API carry a key; any other
  environment sets `ExchangeAccounts__MasterKey`, or the API does not start. A Worker requires it only with
  `Trading:Enabled`, as it decrypts keys only to trade; its `appsettings.Development.json` and `appsettings.Testing.json`
  carry the same keys as the API's.
- The `Trading` section of the Worker (`TradingSettings`): `Enabled`, `false` by default. Only a trading Worker takes live
  bots (`LiveTradingBackgroundService`); a Worker on another machine that only runs backtests leaves it off. A local run
  never sends real orders: in Development a trading Worker does not start unless both `Bybit:RestAddress` and
  `Bybit:SocketAddress` point at a stub. `LeaseDuration` and `LeaseRenewal` are the lease of a live bot, apart from the
  `Worker` lease of chunks and imports: 5 minutes renewed each minute by default, the renewal shorter than the lease or
  a trading Worker does not start. `appsettings.Testing.json` keeps 30 and 10 seconds, so tests that wait for a lease
  to expire move the clock less.

  ```json
  "Trading": { "Enabled": false, "LeaseDuration": "00:05:00", "LeaseRenewal": "00:01:00" }
  ```

  ```json
  "ExchangeAccounts": { "MasterKey": "<openssl rand -base64 32>" }
  ```

## Presentation (`ProfitFox.Presentation`)

- `Controllers` holds controllers only (`BotsController`, `BacktestsController`, `InstrumentsController`: `GET /api/market-data/instruments?category&query&limit`
  → `[{ category, symbol, baseCoin, quoteCoin }]`; field errors 400, exchange unavailable 503); request and response types go to
  `Contracts/<Area>` (`Contracts/Bots`, `Contracts/Backtests`).
- Mark required contract members with `[Required]`, not the C# `required` keyword: a missing JSON property is then
  reported under its own name, not under `$`.
- An endpoint the frontend calls declares every answer with `[ProducesResponseType]`: the frontend takes its API types
  from the OpenAPI document, and an undeclared answer has no type there.
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
- Ports are replaced with NSubstitute mocks. Hand-written stubs exist only for transport and the exchange: HTTP
  (`StubHttpHandler`, `FakeBybitHandler`), because NSubstitute cannot replace the protected `SendAsync`; the Bybit v5
  streams on a local port (`FakeBybitSocketServer`); and `FakeExchange`, one exchange account in memory behind
  `IExchangeLedger`, `IOrderGateway` and `IExchangeStreams` for the live trading runtime, whose orders, fills and streams
  depend on each other as a mock cannot keep.
- Assertions use Shouldly.
- Generators randomize only values the test does not check. Reference data (decimal precision, period bounds, interval
  codes) stays explicit in the test.
- A vendor SDK that builds its own `HttpClient` is replaced in tests as a whole client. `OutboundHttpGuard` sends any
  request a test failed to replace to a dead proxy, so it fails instead of reaching the exchange.

## Frontend (`frontend/`)

React, TypeScript and Vite; the layout follows ProfitGraph's frontend. Interface text is in Russian.

- `src/app` holds the router (TanStack Router) and the providers (TanStack Query). `src/pages/<screen>` holds one screen
  each (`strategies`, `strategy-editor`, `nodes`, `node-editor`, `bots`, `bot-editor`, `bot`, `backtest-setup`, `backtest`,
  `backtest-result`, `compare`, `accounts`, `account-new`, `account`); a page never imports another page.
- `src/modules/<area>` holds a feature: `model` for pure functions, `ui` for components, `api.ts` for queries. Other
  code imports a module only through its `index.ts`. `modules/graph-editor/model` is the logic of the editor that can be
  wrong (definition ↔ canvas, pins, wire types, error paths, draft comparison): plain TypeScript without React.
- Modules of the bot section: `bots` (queries, `BotForm`, server errors by field, `parameterValues` from form text),
  `backtests` (queries with polling every 2 s while a backtest is unfinished, `BacktestForm`, status, ranges, grid,
  period, result columns, engine conflicts), `market-data` (instrument search for symbol suggestions, Bybit
  intervals), `exchange-accounts` (queries, `AccountForm`, `EnvironmentBadge`, `BalanceTable`, server problems of an
  account; the balance query is disabled and runs only on the page's action), `comparison` (the set of results on `useSyncExternalStore` over `sessionStorage`, the bar in
  `AppShell`, the mark checkbox), `market-chart` (`PriceEquityChart` and `EquityComparisonChart` on
  `lightweight-charts`, fill markers, candles per point). `shared/format` shows decimal strings with thousands
  spacing without float math, and UTC dates. Decimal strings become numbers only in `market-chart/model`, for drawing.
- `src/shared/api` holds the only HTTP client (`openapi-fetch`) and the types generated from the API's OpenAPI document
  in `generated/schema.d.ts`. That file is committed: after changing an API contract, run the API and
  `npm run generate:api`, and commit the result with the change.
- Colors are theme tokens in `src/app/styles/index.css`, taken from the mockups; components use the tokens, not raw
  colors.
- Tests: Vitest next to the code (`*.test.ts(x)`). Pure functions follow red → green → refactor like the backend.
  Screens are tested through `renderApp` against `fakeApi`, which replaces `fetch` with a routing table; an unrouted
  request answers 404. End-to-end tests in `e2e/` run Playwright against the real API through the `web` image.
