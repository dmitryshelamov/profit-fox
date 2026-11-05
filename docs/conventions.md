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
  and their pure domain extensions (`Interval`, `IntervalExtensions.CandleEnd`, `IntervalExtensions.CandleOpen`, the start of the candle that contains a moment, `BacktestStatus`); `Entities/Base` holds
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
- `Market` is the area of the scanner: `Entities/Market` holds `ScannerPair` (the bounds of coverage, with the rules that move
  them) and `ScannerState`; `ValueObjects/Market` holds `MarketMinute`, `OpenInterestPoint`, `FundingRatePoint`, `MarketTicker`,
  `PairMetrics` and `MarketSnapshotRow`; `Services/Market/MarketMetrics` is the pure function of the metrics of a pair.
- `Ideas` is the area of trade ideas: `Entities/Ideas` holds `Idea` (a live idea, one open per `IdeaKey`) and the enums `IdeaSetup`,
  `IdeaType`, `IdeaSide`, `IdeaStatus`; `ValueObjects/Ideas` holds `IdeaCandle` (a minute or a candle of an interval, its volume the
  turnover), `MinuteAggregate`, `SetupThresholds`, `SetupSignal`, `SetupReading`, `Zone`, `ZoneRules`, `IdeaPlan`, `IdeaContext`,
  `IdeaTrack`, `IdeaCase`, `IdeaBucketKey`, `IdeaBucketCounts`, `IdeaBucket`, `BucketStatistics`, `IdeaVolatility`, `IdeaShares` and `PairSetup`.
  `Services/Ideas` holds the pure functions: `Atr` (Wilder, 14), `IdeaCandles` (candles of an interval from minutes, a minute without
  trades flat at the close before it), `SetupDetector`, `ZoneFinder`, `IdeaRules`, `IdeaTracker`, `CaseResult`,
  `BucketStatisticsCalculator`, `IdeaMoney`, `Volatility` and `IdeasEngineVersion`.
- `SavedFilters` is the area of saved filters: `Entities/SavedFilters` holds `SavedFilter` and the enums `SavedFilterPlace`, `FilterField`,
  `FilterOperator`; `ValueObjects/SavedFilters` holds `FilterCondition`.
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
- `MarketData/CandleReadThrough` reads a period of closed candles from storage and fills what is missing with one request
  of `IMarketHistoryClient.GetCandlesOnceAsync` (a single attempt, at most 1000 candles), writing only the missing closed
  candles; an `ExchangeException` gives the stored candles and `Unavailable`. It is not a candle import: no job, no lease.
  `Bots/Queries/GetBotCandles` aligns and cuts the period of a bot's chart (`from` down to the start of its candle, `to` up
  to the start of the current one, 1000 candles at most) and reads it through.
- `MarketData/FormingCandles` holds the feeds of the current candle, which only the API uses: `FormingCandleFeeds` (a
  singleton) keeps one feed per series (`FormingSeries`, instrument and interval) however many connections watch it,
  subscribes `Ports/MarketData/IFormingCandleStreams` for the first viewer and takes it off after the last, and tells the
  viewers through `Ports/MarketData/IFormingCandleBroadcast`; its state is `Connecting`, `Live` or `NoData` (a lost
  connection, a refused or closed subscription, or 90 s of silence). `FormingCandleThrottle` lets one update a second through. A
  `FormingCandle` is only for showing: it is never stored and never reaches a strategy, so the Worker's code path takes
  closed candles from `IExchangeStreams` alone.
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
  once they are written; `Runtime/LiveBotReconciler` compares the books of a bot and of the bots that share its
  holding with the exchange and stops them all on a difference; `Runtime/LiveRunMapping` maps the stored position and
  orders of a bot to the snapshot of its run and back; `Runtime/LiveWarmUpHistory` reads the last closed candles a bot
  warms up its indicators with. `Ports/LiveTrading` holds `IExchangeLedger` (REST reads of an account), `IOrderGateway` (orders
  and cancels, a refusal is an outcome) and `IExchangeStreams` (closed candles, order updates and fills, and a signal
  when a stream is back after a gap).
- `Scanner` holds the scanner: `ScannerSettings`, `ScannerMarket` (memory of the last day of every pair, the snapshot from it),
  `Universe/ScannerUniverse` (the list of pairs), `Backfill/ScannerBackfill` (the last day, the gaps, the history, funding),
  `Minute/ScannerMinuteCycle` (tickers, the point of open interest, the snapshot) and `ScannerSession`, which runs them while the
  scanner holds the stream. `Ports/Market` holds `IMarketDataFeed` (REST of public data, through the scanner's own rate limit),
  `IMarketStreams` (minute candles in batches) and `IScannerJournal`. `Market/Queries` holds the queries of the market API
  (`GetMarket`, `GetMarketPair`, `GetMarketCandles`, `GetMarketSeries`); `Market/MarketOrder` is the order of the rows of «Рынок»
  (a column and a direction, rows without a value last, equal values by category code and symbol, ordinally); `MarketData/ChartCandles` is the period of a chart the
  bot and the pair share. `Paging` holds `PageRequest` (the number from 1 and the size 50, 100 or 200 a client asked for, with the field errors
  `page` and `pageSize`; `Slice` gives a `Page<T>`, the last page when the number is past the end), `OrderDirection` and its codes `asc` and `desc`.
  `GetIdeas` and `GetMarket` filter, order and cut the page in memory, and `GetMarket` also counts the pairs for the chips; neither returns a whole list.
- `Scanner/Ideas` holds the ideas of the scanner: `IdeasSettings` (the `Ideas` section, with `ThresholdSettings` and `ThresholdOverride`),
  `IdeaRuleSet`, `IdeaSeries` (at most 220 closed candles of a pair on an interval and the minutes of the forming one), `IdeaEngine` (the
  minutes of a pair one by one: steps the open ideas, reads the setups, makes the ideas of the setups that fire; the same for live and
  replay), `LiveIdeas` (the warm-ups and the minute of live ideas after the snapshot) and `IdeasReplay` (the replay of history pair by pair
  and day by day, the buckets every 10 minutes, the clean-up of other versions). `Scanner/ScannerPace` tells the replay whether the
  snapshot came late.
- `Ideas` holds `IdeaCodes` (codes of setups, types, sides, statuses), `IdeaView` (an idea with its buckets), `IdeaLevels` (what the API
  measures a pair by: price, spread, ATR, zones, window), `IdeaOrder` (the order of the ideas of «Идеи» by a column; setup, type, side and
  status go in the order of their enums, which is the order of the lists on the screen, not of the Russian names) and the queries of the ideas
  API (`GetIdeas`, `GetIdea`, `GetIdeaBuckets`, `GetPairIdeas`, `GetZones`, `GetOwnIdea`). `SavedFilters` holds `SavedFilterFields`, the one
  catalog of fields with their units and operators, `SavedFilterJson`, the codec of conditions for the API and the stored `jsonb`,
  `SavedFilterRules`, which checks a filter by the catalog (`ParseConditions` checks the conditions alone, as `FitSavedFilter` needs),
  `SavedFilterMatch`, the one place where a filter is applied to a pair (`Pair`, which skips the conditions on idea fields) and to an idea
  (`Idea`, which takes the market fields from the row of its pair and fails without one), and the commands and queries of saved filters.
- `Workers` holds what the Worker needs to share work with other Workers: `WorkerSettings`, `WorkerIdentity` (the
  lease owner of this process) and `LeaseKeeper`, which renews a lease while the work runs. Work the Worker takes is a
  command that returns whether it found any (`RunNextCandleImportCommand`, `RunNextBacktestWorkCommand`).
- Parsing of API input lives here: `MarketDataCodes`, `StrategyCodes` and `BacktestCodes` for codes, `ApiTimestamps`
  for times, `StrategyJson` for strategy and composite node definitions and strategy versions. A JSON document that is
  both the API body and the stored `jsonb` value has one codec here, used by Presentation and Infrastructure alike.
  Times in the API carry an offset or `Z`; without one, parsers assume the server's time zone.

## Infrastructure (`ProfitFox.Infrastructure`)

- `InfrastructureRegistrar` is the only public entry point: `Configure` for the API, `ConfigureWorker` for the Worker,
  `ConfigureScanner` for the scanner.
  Both call internal registrars such as `Mediatr/MediatrRegistrar` and `Persistence/Postgres/PostgresRegistrar`; only
  `ConfigureWorker` registers the background services that run work.
- `Observability` holds the metrics of the services: `MetricsSettings` (the `Metrics` section) and
  `MetricsRegistrar.Configure(configuration, services, serviceName)`, which `Configure`, `ConfigureWorker` and `ConfigureScanner`
  call with `api`, `worker` and `scanner`. With `Metrics:OtlpEndpoint` set, OpenTelemetry pushes the meters it lists over OTLP
  (`http/protobuf`, cumulative) every `Metrics:ExportInterval`; the name becomes `service.name`, which Prometheus turns into the
  `job` label. Without it no meter provider is registered and the instruments record into nothing. Instruments are made with
  `System.Diagnostics.Metrics` through `IMeterFactory`, never with OpenTelemetry types.
- The metrics of Bybit REST live next to the adapters, on the meter `ProfitFox.Bybit`: `Exchanges/Bybit/BybitRestMetricsHandler`,
  a `DelegatingHandler` that `ConfigureHttpClientDefaults` puts on every client of `IHttpClientFactory`, counts each attempt to
  the Bybit REST address of `BybitSettings` (retries included) as `profitfox.bybit.rest.requests` by `bybit.endpoint` (the path),
  `http.response.status_code` and `bybit.outcome` (`ok`, `rate_limited` for HTTP 403, 429 or `retCode` 10006, `error`); requests
  to other hosts pass uncounted. It hands the `X-Bapi-Limit` headers to `Exchanges/Bybit/BybitLimitHeadroom`, a singleton whose
  gauges `profitfox.bybit.rest.limit` and `profitfox.bybit.rest.limit_remaining` give the last limit of an endpoint and its
  lowest remainder since the previous collection, which takes it; so the meter has one reader, the exporter.
- A new HTTP client to Bybit is made only through `IHttpClientFactory` (`AddHttpClient`), or its requests are missing from the
  metrics.
- `Exchanges/<Vendor>` holds adapters over vendor SDKs, including the mapping of domain values to SDK types
  (`BybitCodes`). `Exchanges/Retry` holds Polly pipelines. `BybitMarketDataClient` implements `IInstrumentCatalog` (spot in one
  answer, linear by following the cursor; one attempt per request). The registered `IInstrumentCatalog` is
  `Exchanges/CachedInstrumentCatalog` over it: a list per category is kept for 5 minutes on `TimeProvider`, a failure is not kept.
- `Exchanges/ScannerRestGate` is the one way of the scanner's REST requests: a token bucket of `Scanner:RestRequestsPerSecond`
  on `TimeProvider`, the limit read again at each fill, one attempt with the ladder of retries for a transient failure, none for
  `ExchangeRateLimitedException` (HTTP 429, code 10006, HTTP 403), after which no request goes for a pause: `Scanner:RestPause` after
  HTTP 403 (a ban of the IP, `TransientExchangeException.IpBanned`), and after HTTP 429 or code 10006 a ladder kept in memory, from
  `Scanner:RestLimitPause` doubling with each such answer in a row up to `Scanner:RestPause`, started again by a request that went
  through. The end of every pause goes to `scanner_state` and only ever moves later, in memory and in the row, so a limit after a ban
  never shortens it. `Exchanges/Bybit/BybitPublicDataClient`
  implements `IMarketDataFeed` through it; `Exchanges/Bybit/BybitMarketStreams` implements `IMarketStreams` on a socket client of its
  own, apart from `BybitStreams`.
- `Exchanges/Bybit/BybitAccountClient` implements `IExchangeKeyInspector`: a typed `HttpClient` that builds a Bybit client
  with the key for one call and drops it, one attempt per call. `Security/AesGcmSecretProtector` encrypts secrets with the
  master key (AES-256-GCM, the account id as associated data).
- `Exchanges/Bybit/BybitStreams` implements `IExchangeStreams` as one singleton for the process, so the connections to
  Bybit follow the pairs and accounts, not the bots: one public socket client whose connections carry one kline topic
  per category, symbol and interval, and one private client per account key with the `order` and `execution` topics.
  The first subscriber of a topic subscribes it at Bybit, the others join; the last to leave takes it off. Every
  subscriber of a topic gets all its events and a restored connection (a listener of `IFormingCandleStreams`, the same
  singleton, gets every kline update instead of the closed candles only, and also a lost and a closed connection); a
  session picks its own orders by `orderLinkId`. A fill carries its instrument (none for a market the system does not
  trade): a fill of an order that belongs to no bot, on the instrument of a session, makes the session compare the books
  after a pause.
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
- A transaction that locks both rows of a bot takes `bots` first, then `live_bots`, each in its own statement, never
  relying on the order inside one statement or CTE. A change of the owner (`LockAsync`) and a save of the library lock
  the `bots` row `FOR UPDATE`; a write of a session (`LockLeasedAsync`) takes it `FOR KEY SHARE` before the `live_bots`
  row, since the foreign key of a new `live_positions` row takes that share later anyway. A session that fails several
  bots after a reconciliation locks them one bot after another in the order of the library, `created_at, id`.
- `Repositories/Market` holds `scanner_state` (one row: the lease of the stream, the snapshot time, the progress of history,
  the end of the REST pause), `scanner_pairs`, `open_interest` (a hypertable), `funding_rates` and `market_snapshot`, which the
  holder replaces in one transaction that locks the row of the state with its lease.
- `Repositories/Ideas` holds `ideas` (one open idea per key kept by a partial unique index; the ideas made and moved on a minute are
  written in one transaction), `idea_cases` (a hypertable, written once per key, fired time and version), `idea_buckets`, `idea_replay`
  (the cursor of a pair per version with the ideas of the replay open at it) and `idea_volatility`;
  `Repositories/SavedFilters` holds `saved_filters`. `market_snapshot.setups` (`jsonb`, interval code → setup codes) is written after the
  snapshot under the same lease. `CandleRepository.GetAggregatedAsync` joins stored minutes into candles of an interval with `time_bucket`.
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

- The `Metrics` section (`MetricsSettings`, bound in `MetricsRegistrar`): `OtlpEndpoint`, the full URL of the metrics path of an
  OTLP receiver, empty by default, so tests and local runs push nothing; `ExportInterval`, 5 seconds by default. Nothing is in
  `appsettings.json`: production sets `Metrics__OtlpEndpoint` (Prometheus on soyo-03, `http://192.168.8.142:9090/api/v1/otlp/v1/metrics`)
  in the environment of `api`, `worker` and `scanner`.

  ```json
  "Metrics": { "OtlpEndpoint": "http://127.0.0.1:9090/api/v1/otlp/v1/metrics", "ExportInterval": "00:00:05" }
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

- The `Scanner` section, in the scanner's `appsettings.json` only (`ScannerSettings`): the symbols it keeps (empty keeps the whole
  market; Development keeps three), the days of history, the REST requests per second, the pause after a ban of the IP and the first
  pause after a rate limit, how often it reads the list of pairs, the delay of the snapshot after a minute, the lease of the stream
  (the renewal shorter than the lease, or the scanner does not start) and the topics on one connection of the stream.
  `config/scanner.json` under the content root, when it exists, overrides every other source, environment included, and is read again
  on each change: on the server it is a folder mounted into the container, so a setting changes without a restart. Only
  `ScannerRestGate` follows such a change, through `IOptionsMonitor<ScannerSettings>`, from the next second; the rest take
  `ScannerSettings` once at the start.

  ```json
  "Scanner": {
    "Symbols": [],
    "HistoryDays": 90,
    "RestRequestsPerSecond": 20,
    "RestPause": "00:10:00",
    "RestLimitPause": "00:00:30",
    "UniverseRefresh": "01:00:00",
    "SnapshotDelay": "00:00:10",
    "LeaseDuration": "00:00:30",
    "LeaseRenewal": "00:00:10",
    "TopicsPerConnection": 100
  }
  ```

- The `Ideas` section of the scanner (`IdeasSettings`), which the API reads too for its own idea and the zones: the thresholds of the
  setups, common to all pairs, with values of an interval's own by interval code, by which the live ideas and the replay both run; k of the
  stop buffer and the rules of the zones. The defaults are the starting values of decision 7, provisional until tuned by history. The scanner's
  `appsettings.json` also carries `Backtests:DefaultFees`, the fee rates its cases are counted with.

  ```json
  "Ideas": {
    "Thresholds": { "Candles": 6, "Move": 3.0, "Volume": 2.0, "EarlyMove": 1.5, "EarlyVolume": 3.0 },
    "ByInterval": { "240": { "Move": 2.5 } },
    "StopBuffer": 1.5,
    "ZoneMerge": 0.5,
    "ZoneStrength": 2
  }
  ```

## Presentation (`ProfitFox.Presentation`)

- `Controllers` holds controllers only (`BotsController`, `BacktestsController`, `InstrumentsController`: `GET /api/market-data/instruments?category&query&limit`
  → `[{ category, symbol, baseCoin, quoteCoin }]`; field errors 400, exchange unavailable 503); request and response types go to
  `Contracts/<Area>` (`Contracts/Bots`, `Contracts/Backtests`).
- `MarketController`: `GET /api/market` (one page of the snapshot and the state of the scanner: `category`, `search`, `setup`, `interval`,
  `savedFilter`, `sort`, `direction`, `page`, `pageSize`; the answer has `items`, `page`, `pageSize`, `total`, `pairs`, `categories` and `setups`,
  the counts of the chips), `GET /api/market/{category}/{symbol}` (a row;
  404 when the snapshot has no such pair), `.../candles?interval&from&to` (as the bot chart), `.../open-interest?from&to` and
  `.../funding?from&to` (linear only, from storage, at most 1000 points); contracts in `Contracts/Market`. It never calls Bybit
  but for the candles of a pair.
  `MarketController` also has `.../ideas` (the ideas of a pair), `.../zones?interval` (5, 15, 60, 240) and `POST .../own-idea`; the rows of
  `GET /api/market` and of a pair carry `setups`. `IdeasController`: `GET /api/ideas` (filters `setup`, `interval`, `type`, `side`,
  `category`, `nonNegativeExpectancy`, `savedFilter`; `sort`, `direction`, `page`, `pageSize`; the answer is `items`, `page`, `pageSize` and
  `total`), `GET /api/ideas/{id}`, `GET /api/ideas/buckets`. `SavedFiltersController`: CRUD of
  `/api/saved-filters`, the catalog `GET /api/saved-filters/fields` and `POST /api/saved-filters/fit` (`{ conditions }` of a draft →
  `{ pairs, ideas }`, saves nothing).
- `Hubs` holds SignalR hubs. `BotChartHub` is the first: `/api/hubs/bot-chart` (under `/api`, so the proxy of the web image
  and of Vite carry it, Vite with `ws: true`). A connection calls `Subscribe(botId)`, which answers `{ state, candle }`
  (`connecting`, `live` or `noData`; the newest candle or `null`) or fails with `bot_not_found`, then receives
  `FormingCandle` (`openTime`, `open`, `high`, `low`, `close`, `volume` as decimal strings, `isClosed`) and `FormingStatus`
  (the state) until `Unsubscribe()` or the disconnect. The hub is not in the OpenAPI document; the frontend writes its
  message types itself. `BotChartBroadcast` implements `IFormingCandleBroadcast` over `IHubContext`, one group per series;
  `Program` registers it and SignalR, `InfrastructureRegistrar.Configure` registers `FormingCandleFeeds`, and the Worker has
  neither.
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

## Scanner (`ProfitFox.Scanner`)

- A generic host with no HTTP, like the Worker: `Program.Configure` adds Serilog and `InfrastructureRegistrar.ConfigureScanner`;
  a test host (`ScannerHost`) calls `Configure` too. It never migrates the database and never reads keys: public Bybit data only.
- `ScannerBackgroundService` waits for the lease of the stream and, while it holds it, runs a `ScannerSession` in a scope of its own;
  a second scanner opens no connection to Bybit.

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
  reference candle sets for backtests in `Fixtures/Backtests`; reference minutes and candles of the ideas and the fingerprints of
  `IdeasEngineVersion` in `Fixtures/Ideas`).
- Ports are replaced with NSubstitute mocks. Hand-written stubs exist only for transport and the exchange: HTTP
  (`StubHttpHandler`, `FakeBybitHandler`), because NSubstitute cannot replace the protected `SendAsync`; the Bybit v5
  streams on a local port (`FakeBybitSocketServer`); and `FakeExchange`, one exchange account in memory behind
  `IExchangeLedger`, `IOrderGateway` and `IExchangeStreams` for the live trading runtime, whose orders, fills and streams
  depend on each other as a mock cannot keep.
- Assertions use Shouldly.
- Metrics are read in tests with `MetricCollector` (`Microsoft.Extensions.Diagnostics.Testing`) over the `IMeterFactory` of a
  `ServiceCollection().AddMetrics()` or of the host; a gauge is collected with `RecordObservableInstruments`
  (`BybitRestMetricsHandlerTests`, `BybitLimitHeadroomTests`, `BybitRestMetricsTests`). `MetricsExportTests` runs a Worker against a
  local `HttpListener` that stands in for the OTLP receiver.
- Generators randomize only values the test does not check. Reference data (decimal precision, period bounds, interval
  codes) stays explicit in the test.
- A vendor SDK that builds its own `HttpClient` is replaced in tests as a whole client. `OutboundHttpGuard` sends any
  request a test failed to replace to a dead proxy, so it fails instead of reaching the exchange.

## Frontend (`frontend/`)

React, TypeScript and Vite; the layout follows ProfitGraph's frontend. Interface text is in Russian.

- `src/app` holds the router (TanStack Router) and the providers (TanStack Query). `src/pages/<screen>` holds one screen
  each (`strategies`, `strategy-editor`, `nodes`, `node-editor`, `bots`, `bot-editor`, `bot`, `backtest-setup`, `backtest`,
  `backtest-result`, `compare`, `accounts`, `account-new`, `account`, `market`, `market-pair`, `ideas`); a page never imports another page.
- `src/modules/<area>` holds a feature: `model` for pure functions, `ui` for components, `api.ts` for queries. Other
  code imports a module only through its `index.ts`. `modules/graph-editor/model` is the logic of the editor that can be
  wrong (definition ↔ canvas, pins, wire types, error paths, draft comparison): plain TypeScript without React.
- Modules of the bot section: `bots` (queries, `BotForm`, server errors by field, `parameterValues` from form text),
  `backtests` (queries with polling every 2 s while a backtest is unfinished, `BacktestForm`, status, ranges, grid,
  period, result columns, engine conflicts), `market-data` (instrument search for symbol suggestions, Bybit
  intervals), `exchange-accounts` (queries, `AccountForm`, `EnvironmentBadge`, `BalanceTable`, server problems of an
  account; the balance query is disabled and runs only on the page's action), `comparison` (the set of results on `useSyncExternalStore` over `sessionStorage`, the bar in
  `AppShell`, the mark checkbox), `market-chart` (`PriceEquityChart` and `EquityComparisonChart` on
  `lightweight-charts`, fill markers, candles per point; for the bot chart `BotCandleChart` with volume, fill arrows,
  dashed order lines and the current candle, over the pure `chartPaging`, `tradeMarkers`, `liveCandles`, `volumeBars` and
  `orderLines`, `useCandlePages` — the paging of a chart over a function that reads a page, which the bot and the pair use with their
  own endpoints — and `OpenInterestChart`, `FundingChart`), `market` (the query of one page of the snapshot, polled
  every 15 s and asked with the category, the search (after a pause of 300 ms), the setup chip and its interval, the saved filter, the sort and the page,
  the row, open interest and funding of a pair; the counts of the category buttons and of the chips come from the answer, nothing is filtered or sorted in the browser;
  `model` for the codes of the sort columns, the size of a page and the rest of the choice kept in the browser (`choice`), the age of the snapshot and
  compact numbers; `setups` for the setups, the interval of setups and the column «Сетап»; `MarketTable`, the banners and the mark of
  incomplete data), `ideas` (queries of one page of the ideas, of a pair's ideas, zones and the own idea; `model` for the labels, the codes of the sort columns, the badge against the
  baseline and «мало данных» (`stats`), profit and loss at the risk per idea and its storage in the browser (`riskStorage`), the groups of a
  pair's list (`groups`) and the levels drawn on the chart (`levels`); `IdeasTable`, `PairIdeasList`, `OwnIdeaBlock`), `saved-filters`
  (queries of filters and the catalog of fields, and the count of a draft, `fitQuery`; `model` for the text of conditions (`conditions`) and the
  server errors by field (`errors`); `FilterBar`, `FilterEditor`, whose counter «Подходит» is the answer of `POST /api/saved-filters/fit` for the ready
  conditions, after a pause of 300 ms; the API applies saved filters, the browser only names the chosen one). The lists «Идеи» and «Рынок» show one page: the number is
  in the address (`?page=`, a whole number from 1, otherwise the first page; a page past the end moves the address to the page the API gave), the size
  (50, 100 or 200) in the choice kept in the browser, and a change of any other choice goes back to the first page. `shared/ui/Pager` is the page buttons,
  the rows shown and the size; `shared/paging` holds the sizes; `shared/hooks/useDebounced` is the pause. `BotCandleChart` also takes optional price lines, zones (bands drawn by the series primitive `ZoneBandsPrimitive`)
  and the marker of a fired setup, from the pure `market-chart/model/ideaOverlay`; `shared/format/decimal` compares decimal strings
  without float math. `bots` also holds the bot chart's data: `useBotCandles` (pages going back, a minute-by-minute tail,
  start over when too far behind), `useFormingCandle` over `realtime/followFormingCandle` (the SignalR hub
  `/api/hubs/bot-chart`, which is not in OpenAPI: its message types are written by hand in `model/formingCandle.ts`,
  with the reducer and the channel text) and `chartIntervals` (the interval switcher; the `?interval=` of the bot route
  is read there, the bot's own interval never goes to the address). The router writes text search values as they are, so
  `?interval=5` is not quoted. `shared/format` shows decimal strings with thousands
  spacing without float math, and UTC dates. Decimal strings become numbers only in `market-chart/model`, for drawing.
- `src/shared/api` holds the only HTTP client (`openapi-fetch`) and the types generated from the API's OpenAPI document
  in `generated/schema.d.ts`. That file is committed: after changing an API contract, run the API and
  `npm run generate:api`, and commit the result with the change.
- Colors are theme tokens in `src/app/styles/index.css`, taken from the mockups; components use the tokens, not raw
  colors.
- Tests: Vitest next to the code (`*.test.ts(x)`). Pure functions follow red → green → refactor like the backend.
  Screens are tested through `renderApp` against `fakeApi`, which replaces `fetch` with a routing table; an unrouted
  request answers 404. The test setup replaces `lightweight-charts` with inert series and `@microsoft/signalr` with a silent
  connection; a test of the channel mocks `followFormingCandle` or the library itself. End-to-end tests in `e2e/` run Playwright against the real API through the `web` image.
