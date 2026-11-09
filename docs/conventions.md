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
- `MarketCategory` is the market an instrument trades on: `Spot` and `Linear` on Bybit, `Moex` (the shares of the main board TQBR of the
  Moscow Exchange, in roubles, the symbol a ticker such as `SBER`) on T-Invest. `Entities/ExchangeAccounts/Enums/Exchange` names the exchanges
  (`Bybit`, `TInvest`) and `MarketCategoryExtensions.Exchange()` tells the exchange of a category; the exchange is stored nowhere and never goes
  over the API. The intervals are a property of the category (`Application/MarketData/MarketDataCodes.IntervalsOf`): Bybit has all 13, `Moex`
  `1`, `3`, `5`, `15`, `30`, `60`, `120`, `240`; the texts of the errors of a category and of an interval are `MarketDataCodes.InvalidCategory`
  and `MarketDataCodes.InvalidInterval(category)`. `ListedInstrument` also carries the `Name` of a company and `ShortEnabled` for a share of MOEX,
  and `InstrumentSearch.Find` matches a query against the name as well.
- `ValueObjects/MarketData/SeriesCoverage(From, Until)` is the border of the coverage of a series of candles: inside it storage holds
  every candle the exchange gave, so a candle missing there was not traded. `Extend(from, to)` widens it by a range that touches or
  overlaps it and leaves a separate range alone; `Uncovered(ranges)` cuts the coverage out of the missing ranges; `Margin` (a minute) is how
  far behind the clock the border stays, since the exchange may still serve the candle that has just closed as unfinished. It is kept only for a
  market with sessions (`MarketCategoryExtensions.TradesAroundTheClock()` is false for `Moex`); Bybit has no nights and keeps none.
  `ISeriesCoverageRepository` reads and writes it (`candle_coverage`), and `ICandleRepository.UpsertAsync` with a coverage writes the
  candles and the border in one transaction.
- `ValueObjects/<Area>` holds value objects (`Instrument`, `Price`, `Volume`, `InstrumentRules`, `StrategySnapshot`,
  `ParameterSpec`, `SimulationSettings`, `FeeRates` — the fee rates of a category and the slippage the ideas count on fills that do not rest on the book).
- `Repositories` holds storage ports, named `I<Name>Repository` (`IStrategyLibraryRepository` for strategies, nodes,
  versions and bots; `IBacktestRepository`; `IIdeaEngineVersionRepository` for the start of a version of the rules of ideas). `Events` holds domain events.
- `Services/<Area>` holds domain logic that has no state of its own in the database, split into folders by purpose
  (`Services/Strategies/Nodes`, `BuiltIns`, `Templates`, `Validation`, `Execution`). Logic that belongs to an entity or a value object
  stays on it. `Services/Backtests` holds the exchange simulator and the metrics of a backtest (`ParameterGrid`,
  `BacktestReplay`, its observers, `EngineVersion`); the strategy engine itself stays in `Services/Strategies/Execution`,
  with the position of a run (`Position`, `PositionStatus`, `ClosedPosition`) until positions get storage of their own.
  Replay observers see each placed order with the number of its position, each confirmed cancel and each fill; the
  details they collect are value objects in `ValueObjects/Backtests` (`BacktestPosition`, `BacktestOrder`,
  `BacktestFill`).
- `Services/Strategies/Templates` holds the built-in strategy templates: `StrategyTemplateKind` (the id of a template in the catalog; a strategy made from one
  keeps none), `StrategyTemplate` (name, description and definition without a layout) and `BuiltInTemplates.All`, in the order the owner sees them. Their graphs
  are written in code with `GraphSketch` from `BuiltIns`, like the built-in nodes; the reference strategies EMA Short, Ladder and DCA are the same graphs as their test fixtures.
- `Market` is the area of the scanner: `Entities/Market` holds `ScannerPair` (the bounds of coverage, with the rules that move
  them, and `TradFi`, which `ScannerUniverse.Merge` refreshes from every read of the list) and `ScannerState`; `Entities/Market/Enums` holds
  `LiquidationSide` (the side of the liquidated position, not of the liquidation order: `long`, `short` in `Application/Market/MarketCodes`);
  `ValueObjects/Market` holds `MarketMinute`, `OpenInterestPoint`, `FundingRatePoint`, `MarketTicker`,
  `PairMetrics`, `MarketSnapshotRow`, `LiquidationEvent` (time, side, price, volume of a liquidation as the stream gives it) and
  `StreamBreak` (a stream, when it was lost and when it was back); `ILiquidationRepository` stores the events and
  `IStreamBreakRepository` the breaks of a stream whose missed events cannot be loaded again; `Services/Market/MarketMetrics` is the pure function of the metrics of a pair.
- `Ideas` is the area of trade ideas: `Entities/Ideas` holds `Idea` (a live idea, one open per `IdeaKey`) and the enums `IdeaSetup`,
  `IdeaType`, `IdeaSide`, `IdeaStatus`, `IdeaFeature` (a feature of the market an idea fired with, as the calibration and the autos name it, the burst among them), `IdeaBadge` (▲ ≈ ▼ of a bucket or an idea by the interval of its expectancy, `IdeaBadges.Of`), `RiskRewardBand` (a band of the reward to risk of «Уровни», `RiskRewardBands.Of`), `CalibrationScope` (all cases or those after the rules changed); `ValueObjects/Ideas` holds `IdeaCandle` (a minute or a candle of an interval, its volume the
  turnover), `MinuteAggregate`, `SetupThresholds`, `SetupSignal`, `SetupReading`, `SetupBase` (what the setups of an interval are measured against
  until a candle closes), `CandleSetupInput`, `RsiState`, `VolatilityPercentiles`, `Zone`, `ZoneRules`, `IdeaPlan`, `IdeaContext`,
  `IdeaTrack` (with the exit rule the idea fired under), `SignalPath` (the plan of a signal of the replay and the first touches of its entry, stop, ladder
  +0.5 … +5 R and zone, and its closes at the grid), `IdeaSignal` (a signal with its pair, setup and path), `IdeaBucketKey`, `IdeaBucket`, `BucketStatistics`
  (the numbers of the model with the own ones of the bucket beside, the weight of the parent and «мало своих данных»; with the signals, the share of entries and the expectancy per signal of «Откат» and «Стоп-вход»), `IdeaVolatility`, `IdeaShares` (with the break-even), `IdeaNumbers` (the own numbers of an idea: its costs in R, the probability of its bucket times the multiplier of the band of «Уровни», the expectancy of the bucket without costs less the costs with its interval and badge, and the expectancy on 1 USDT), `RiskRewardCurve` (the cases and targets of the bands of the reward to risk of «Уровни» of a set interval × side × category, `RiskRewardBandCount`, and the multiplier of a band with its fallbacks), `PairSetup`,
  `IdeaOutcome` (how a case ended, as the calibration reads it), `CalibrationReport` (with its scope, the rows by own data and the comparison of the models), `CalibrationRow`, `CalibrationOwnRow` (a row by own data with its Brier score), `CalibrationModel` and `CalibrationComparison` (the old model against the new one and the model with features), `CalibrationDifference` (the paired Brier score of the model with features less that of the new one with its clustered interval), `IdeaFeatures` (a drop of the pair over a day, a drop of BTC over 4 hours and a high funding rate on the minute a signal fired, from the numbers the signal and the idea keep, and a burst: at least `BurstThreshold` distinct pairs of its side on its interval with a signal in its minute (5 min 60, 15 min 25, 1 h 8, 4 h 4), which a live idea keeps as `Idea.BurstPairs` and a signal of the replay is given by the pass of the statistics; one of the 16 levels of its bucket the model with features splits it by), `CalibrationSlice` (a row of the breakdown of a report by interval or by type), `IdeaExitRule` (the lifetime, the target in R and the
  entry window of a type × interval, and which values it can take) and `IdeaExitRules` (the rules in force, the default for a type × interval never applied), `BlockSums` (the entries of an exit rule in a block of days and their total R), `RuleSums` (those of a rule of the grid in the bodies and the tails of the blocks and on the selection and the check), `OutOfSampleWindow` (the window of the out-of-sample check: the selection, the check and its end the horizon of the longest rule of the grid before now, the horizon of a rule, and the part and the block of a signal; `OutOfSamplePart`) and `Overfitting` (the splits of CSCV that counted and the overfit ones).
  `Services/Ideas` holds the pure functions: `Atr` (Wilder, 14), `Rsi` (Wilder, 14, over exactly 100 candles), `IdeaCandles` (candles of an interval from minutes, a minute without
  trades flat at the close before it), `SetupDetector` (the setups of a move), `CandleSetupDetector` (the candle setups), `SetupTypes`
  (the types of ideas of each setup), `ZoneFinder`, `IdeaRules`, `IdeaTracker`, `CaseResult` (with `GrossR`, the result at the prices of the plan without costs), `IdeaCosts` (the costs of an idea in R from its plan and the fees of its category), `IdeaNumbersCalculator` (the own numbers of an idea),
  `BucketStatisticsCalculator` (from the counts, ΣR and ΣR², so a window of cases can add and take them; the old model, kept for the comparison
  of the calibration and the screen «Срок жизни и цель»), `ClusteredSums` (the sums of a group of cases by the hour UTC they fired in, which a
  case can be added to and removed from, and the own numbers with the clustering), `BucketModel` (the numbers of a bucket shrunk to its parent
  «тип × ТФ × сторона × категория» and the parent to its category by DerSimonian–Laird; `Estimate`, `Level`, `Spreads`, `Root`, `ParentModel`,
  `Parent`), `CalibrationCalculator` (the walk-forward check of the buckets by the model on cases they had not seen, one walk in the order of time), `IdeaMoney`, `Volatility`, `IdeasEngineVersion`, `ExitRuleGrid` (the lifetimes and windows of an
  interval up to 7 days, a time up to the grid), `SignalPathTracker` (steps a path by a minute, by the touches of `IdeaTracker`), `ExitOutcomes`
  (the outcome of a path by a rule as an `IdeaTrack`, and the signals of a key that count as one open idea at a time), `ExitRuleGridPass` (the grid of
  rules of the out-of-sample check, lifetimes × targets at the window of the candidate, and the entries and total R of every rule of it in the
  bodies and the tails of 6 blocks and on the selection and the check, in one step a signal, the keys in 8 fixed parts added up in order, stopped
  at the next key when cancelled) and `OverfittingProbability` (the best rule on the selection and the probability of overfitting of the grid by
  CSCV, 20 splits of the 6 blocks, twins as one rule, the tail of a block before a block of the other half left out).
- `IdeaTrades` is the area of trades by ideas (one entry with a stop and a target the exchange stands): `Entities/IdeaTrades` holds `IdeaTrade`
  (its levels, size, status, the totals the exchange counted of its entry and exits, its result in money and in R, what it is worth at a price (`ResultAt`), the time it opened (`OpenedAt`, the first fill of the entry), the promise of its idea at the entry, the features of its idea and how its idea ended), `IdeaTradeOrder` (an entry or
  the market close of the owner, saved before it goes to the exchange, like `LiveOrder`), `IdeaTradeChild` (the stop or the target the exchange made, by
  its exchange id), `IdeaTradeFill`, `IdeaTradeForcedShare` (the part of a liquidation or an auto-deleveraging of the exchange that closes a trade) and `IdeaTradingSettings`, and the enums `IdeaTradeStatus`, `IdeaTradeLeg`, `IdeaTradeOrderRole`, `IdeaTradeRefusal`,
  `AccountAccess`; `ValueObjects/IdeaTrades` holds `IdeaTradeOrderId` (the id of an order of a trade, `i` + 25 base-36 digits + `-` + number, which never
  reads as a `ClientOrderId`), `IdeaTradeSize`, `IdeaTradingLimits`, `IdeaTradeEntry`, `IdeaTradeAccount` (with `LinearSymbol`, the leverage of the symbol of `linear` on the account, its position and its open orders), `OpenIdeaTrades`, `TradeHolding`, `LegTotals`,
  `Protection`, `LeasedIdeaTrade`, `ChangedIdeaTrade`, `IdeaPromise` (the badge, probability and expectancy of the idea itself from the entry price and the cases of its bucket over 90 days, and the break-even and the reward to risk after the fees from the entry price, kept as they were when the trade was entered), `TradedIdeaOutcome` (the status, time, result in R and exit price an idea closed with, kept on the trade so the clean-up of closed ideas does not take it; named apart from `ValueObjects/Ideas/IdeaOutcome`, which is a case of the calibration), `ClosedIdea` (an idea that closed and its outcome, to be written on its trades), `IdeaJournalTotals` (the pure totals of the closed trades of a journal: the counts by outcome (at the target, at the stop, manually, at expiry, cancelled, failed), the money, the fees, the mean result in R with its 95 % interval, the mean time to the outcome, `PromiseComparison` and `IdeaComparison`); `Services/IdeaTrades` holds the pure `IdeaTradeSizing` (the volume the amount buys, rounded down to the step,
  with the loss at the stop and the profit at the target), `IdeaTradeRefusals` (every reason an entry is refused, with `StopTooFar`, the stop at half the way to the liquidation at a leverage, which `PaperEntry` checks too) and `ForcedFillShares` (splits a liquidation or an auto-deleveraging between the trades of the side it closes by what they hold, with `ForcedFillHolder` and `ForcedFillShare`). `Reconciliation.Check` and `StartFunds.Enough` take the
  trades next to the bots.
- `IdeaAutos` is the area of the autos that take ideas by themselves: `Entities/IdeaAutos` holds `IdeaAuto` (a named auto with its account, amount, mode, conditions, place in the list and `Budget`, the cap of the sum of the amounts of its open trades, none for no budget; soft-deleted with `DeletedAt`; `MaxPerMinutePerSide` and `MaxOpenPerSide`, the most new trades of a side it takes in a minute and the most open trades of a side, real and paper, none for no limit), `PaperIdeaTrade` (an `IdeaTrade` of an auto on paper with the auto and `SteppedUntil`, the open time of the next minute to step it by), `IdeaAutoEntry` (the request of an auto with money to enter by an idea, with the account, amount and lowest reward to risk of the minute of the idea, the place of the auto and the order of the idea) and `IdeaAutoRefusal` (an idea that fit an auto and was not taken, with the copy of the idea, every reason and the detail of the first); `Entities/IdeaAutos/Enums` holds `IdeaAutoMode` (`Off`, `Paper`, `Live`) and `FeatureCondition` (`Any`, `With`, `Without`: what an auto asks of a feature of the market of an idea); `ValueObjects/IdeaAutos` holds `IdeaAutoConditions`, `AutoRefusal`, `RefusalDetail`, `PairHolder`, `AutoBooks` (the trades an auto is checked against), `PaperEntryCheck`, `DailyLossState`, `LimitSkip` (the ideas of a side a limit of an auto skipped in a minute: the first, the limit, how many) and `AutoMinute` (what the autos did in a minute, written with the ideas of the minute); `Services/IdeaAutos` holds the pure functions: `AutoConditions` (whether an idea fits the conditions, the features of the market among them), `AutoMinutePlan` (the order an auto takes the ideas of a minute in, by the expectancy of the idea on 1 USDT of the amount then the net reward to risk, ideas without an expectancy last, and its two limits over one minute: an idea over a limit is skipped and counted, one `LimitSkip` a side), `PaperEntry` (the checks of a paper entry), `PaperFill` (the fill of a paper trade by the closed minutes, and its close at the exit price of its idea when the idea expires), `AutoRefusals` (the pair held, the budget of the auto and of all the autos, the open trades of a side, the daily stop), `AutoBudget` (what the open trades take of the budget of an auto, its real trades on any account and its paper ones, and of the budget of all the autos, the real trades of every auto; a trade takes its `Amount`, the amount after the volume was rounded, and a new entry is checked with the amount it asks for) and `DailyLossStop`. `IdeaTrade.AutoId` is the auto that took a trade (none for a manual one); `IdeaTradingSettings` also holds the three guards of the autos with money, `DailyLossLimit`, `MaxSameDirection` and `AutosBudget`, none while off; `IdeaTradeRefusal` also has the reasons only an auto is given (`DailyStop`, `TooManySameDirection`, `PairTaken`, `Late`, `AutoOff`, `RulesUnknown`, `EntryError`, `BudgetTaken`, `AutosBudgetTaken`, `MinuteLimit`, `SideLimit`); `IdeaTradeRefusals.DetailOf` tells the numbers of a reason. `Market` pairs (`ScannerPair`) and `ListedInstrument` also carry the step of the volume and the minimums of the volume and amount of an order (`QuantityStep`, `MinQuantity`, `MinAmount`).
- `SavedFilters` is the area of saved filters: `Entities/SavedFilters` holds `SavedFilter` and the enums `SavedFilterPlace`, `FilterField`,
  `FilterOperator`; `ValueObjects/SavedFilters` holds `FilterCondition`.
- `ExchangeAccount` is one flat record for both exchanges: `Exchange`, the `ApiKey` and `Uid` of a Bybit account, the `AccountId` (the
  number of the brokerage account) of a T-Invest account; the constructor keeps the fields of each exchange, and a T-Invest account
  trades in `Moex`. The secret of either (the API secret, the token) is kept apart, encrypted, and `IExchangeAccountRepository.AddAsync`
  refuses a second account of the same uid (Bybit) or account number (T-Invest).
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
  category (`ListedInstrument`: symbol, base coin, quote coin, and `TradFi` for a stock, an xStock, an ETF, a commodity or forex by the
  `symbolType` of Bybit). `SearchInstrumentsQuery(category, query, limit)` filters that
  list with the pure function `InstrumentSearch.Find`: a case-insensitive substring (spaces around the query are ignored) of the symbol or a coin, symbols that start
  with the query first, then by symbol ordinally, cut to `limit` (1 to 100, 20 by default). Its result carries field errors
  like `GetCandlesQuery`.
- `MarketData/CandleReadThrough` reads a period of closed candles from storage and fills what is missing with one request
  of `IMarketHistoryClient.GetCandlesOnceAsync` (a single attempt, at most 1000 candles), writing only the missing closed
  candles; an `ExchangeException` gives the stored candles and `Unavailable`. It is not a candle import: no job, no lease.
  On `moex` only the part outside the `SeriesCoverage` of the series is missing, and the coverage grows over the whole period asked
  for, never past the last closed candle less `SeriesCoverage.Margin`, in the transaction of the candles (inside the period everything
  is stored, covered or has just been asked for); `RunNextCandleImportCommandHandler` grows it over the job up to the end of each range
  once the range is loaded. A period apart from the coverage leaves it as it is. `RunNextBacktestWorkCommandHandler` takes the coverage
  out of the missing ranges of a backtest on such a market too, before it decides on an import and before it writes the gaps, and expects
  the stored candles plus the slots outside the coverage, so a covered series is loaded whole. A symbol is checked at the exchange of its
  category, and the error names it (`ExchangeCodes.DisplayName`: `Bybit`, `T-Invest`).
  `Bots/Queries/GetBotCandles` aligns and cuts the period of a bot's chart (`from` down to the start of its candle, `to` up
  to the start of the current one, 1000 candles at most) and reads it through.
- `MarketData/FormingCandles` holds the feeds of the current candle, which only the API uses: `FormingCandleFeeds` (a
  singleton) keeps one feed per series (`FormingSeries`, instrument and interval) however many connections watch it,
  subscribes `Ports/MarketData/IFormingCandleStreams` for the first viewer and takes it off after the last, and tells the
  viewers through `Ports/MarketData/IFormingCandleBroadcast`; its state is `Connecting`, `Live` or `NoData` (a lost
  connection, a refused or closed subscription, or 90 s of silence). The port is `RoutedFormingCandleStreams`, so a series of `moex` comes
  from the stream of the forming candles of T-Invest like a series of Bybit from Bybit, and a closed market is `NoData` by the silence.
  `FormingCandleThrottle` lets one update a second through. A
  `FormingCandle` is only for showing: it is never stored and never reaches a strategy, so the Worker's code path takes
  closed candles from `IExchangeStreams` alone.
- `Exceptions` holds `BaseApplicationException` and its subclasses, also vendor-neutral (`ExchangeException`,
  `ExchangeKeyRejectedException`).
- `ExchangeAccounts` holds the commands and queries of exchange accounts and `ExchangeKeyRules`, the pure check of what a key may do. The secret of an account is decrypted only in the query
  that calls the exchange with it, and a bot refers to its account by id (`BotAccount` carries the categories the key
  trades in into the checks of the bot). `ExchangeCodes` (`bybit`, `tinvest`) are the codes of the exchange in the API and in stored rows.
  `CreateExchangeAccountCommand` creates an account by its exchange: Bybit from the key and the secret as before, T-Invest from the
  token and the account number, reading the accounts of the token again through `Ports/ExchangeAccounts/IBrokerAccountReader`
  (`ListAccountsAsync(token)`, `GetPortfolioAsync(token, accountId)`) and taking only an account `BrokerAccountRules` has no problem
  with (`not_open`, `not_full_access`, `unsupported_type`; `BrokerAccountCodes` are the codes of the type, status and access of a
  `BrokerAccount`). `ListBrokerAccountsQuery(token)` lists the accounts of a token with the problem of each, storing nothing.
  `AccountBalance` carries its `Currency` (`USD` on Bybit, `RUB` on T-Invest); `GetExchangeAccountBalance` reads a Bybit account through
  `IExchangeKeyInspector` and a T-Invest account through `IBrokerAccountReader`.
- `Transactions/ITransactionExecutor` runs a unit of work in one transaction: its writes commit together or not at all,
  a unit inside a unit is refused, and `EnsureNoTransaction` guards a call that must not hold a transaction open (the
  exchange).
- `LiveTrading` holds the commands of live trading (start, pause, resume, stop, close the position, claim and run a bot; the start of a
  bot on `moex` is a conflict «Trading on MOEX is disabled» before any call to an exchange while `MoexTradingSettings.Enabled` (the
  `TInvest:Trading` setting of the API) is off, and otherwise checks the account of T-Invest at the broker through `IBrokerAccountReader`:
  the token still opens it, open and with full access, instead of the key and the hedge mode of Bybit; `LiveRequests.KeyAsync` builds the
  `ExchangeKey` by the exchange of the account, on T-Invest the number of the brokerage account with the token),
  `LiveTradingCodes`, and `Runtime/LiveBotSession`: one session keeps one bot for the take of its lease, takes its inputs
  one at a time from a queue, writes every change in one unit of work after checking the lease, and sends orders only
  once they are written; `Runtime/LiveBotReconciler` compares the books of a bot and of the bots that share its
  holding (on the spot the bots of its coin, on `linear` and `moex` those of its instrument) with the exchange and stops them all on a
  difference; `Runtime/LiveRunMapping` maps the stored position and
  orders of a bot to the snapshot of its run and back; `Runtime/LiveWarmUpHistory` reads the last closed candles a bot
  warms up its indicators with. `Ports/LiveTrading` holds `IExchangeLedger` (REST reads of an account, the leverage and the position of a symbol of `linear` in one read too, `LinearPosition`), `IOrderGateway` (orders
  and cancels and the leverage of a symbol, a refusal is an outcome) and `IExchangeStreams` (closed candles, order updates and fills, and a signal
  when a stream is back after a gap).
- `IdeaTrades` holds the trades by ideas: `IdeaTradeAccounts` (the accounts the trades can go through: those of Bybit, until the ideas of MOEX come;
  the settings of trading by ideas and an auto refuse any other on the save, and the entry gives it no key and asks Bybit nothing), `IdeaTradeEntryCalculator` (`PrepareAsync` asks the exchange and reads the settings and the buckets, `EvaluateAsync` sizes the
  trade, names the reasons by the books of the account and builds the promise of the idea with `IdeaView.Of`, so the entry can be made again under the lock of the account), `EntryLeverage` (sets the leverage of the settings on the symbol of `linear` under the lock of the account, after the checks, for a manual entry and the entry of an auto), `IdeaTradeCodes` (statuses, roles,
  legs, times in force and the reason codes), the commands `CreateIdeaTrade`, `CancelIdeaTrade`, `CloseIdeaTrade` and `SaveIdeaTradingSettings`, the queries
  `GetIdeaEntry`, `GetIdeaJournal` (the open trades with the last price of their symbols from `IMarketHistoryClient`, one request a symbol and none on an error; a page of the closed ones by the time they closed; the totals of all the closed ones of the filter; filters, paging and field errors like `GetIdeas`, filtered and cut in memory) and `GetIdeaTradingSettings`, `IdeaJournalTrade` (a trade with its account name and, when open, its worth at the last price), and `Runtime/IdeaTradeSession`: one session keeps one trade for the take of its lease as a bot's
  session does (one queue of inputs, one unit of work per change after the lease check, orders sent once written), sends its entry, follows the entry and the
  stop and the target the exchange made (told by `parentOrderLinkId` on `linear`; the spot waits for the check of decision 9), cancels the entry at the end
  of its time or on request, closes by the market on request, and compares its books with the exchange after a catch-up. `Runtime/IdeaTradeLeases` is the loop of
  the leases of the trades of a Worker. `LiveTrading/Runtime/OrderSending` decides what a session does with the answer of the exchange to a sent order, for
  bots and trades alike.
- `IdeaAutos` holds the autos: the commands `CreateIdeaAuto`, `SaveIdeaAuto`, `SetIdeaAutoMode`, `ReorderIdeaAutos` and `DeleteIdeaAuto`, the queries `GetIdeaAutos` (the list with the totals of each auto and what its open trades take of its budget: `IdeaAutoSummary`), `GetIdeaAuto`, `GetIdeaAutoRefusals` (a page from the newest, `today` or `7d`, one auto or all) and `GetIdeaGuards` (`IdeaGuardsState`: the open trades, their risk, the daily stop, the open trades of each side and what the real trades of the autos take of the budget of all the autos, against their limits), `IdeaAutoInput` with `IdeaAutoValidation` (the checks of a save, errors by the name of the field), `IdeaAutoCodes` (the modes `off`, `paper`, `live`), `DailyLossReader` (the daily stop over the closed real trades), `AutoEntry` (an auto with money for `IdeaTradeEntryCalculator.EvaluateAsync`: it replaces the lowest reward to risk of the settings with the one of the request and adds the checks of the auto to the reasons) and `Commands/RunNextAutoEntry`, which a trading Worker runs: under the session lock of the requests it takes the earliest request and refuses it as late (over 60 seconds) or as off (the auto is deleted or not «С деньгами»), or makes the entry as `POST /api/idea-trades` does, under the lock of the account and of the creation of trades.
- `Scanner` holds the scanner: `ScannerSettings`, `ScannerMarket` (memory of the last day of every pair and its last funding settlement, the snapshot from it),
  `Universe/ScannerUniverse` (the list of pairs), `Backfill/ScannerBackfill` (the last day, the gaps, the history, funding),
  `Minute/ScannerMinuteCycle` (tickers, the point of open interest, the snapshot) and `ScannerSession`, which runs them while the
  scanner holds the stream. The session subscribes the liquidations of the linear pairs with the candles and takes both off together; a lane
  writes them in batches, and every break of their stream goes to `stream_breaks` as `liquidations`: a lost connection until it is back, a
  batch that failed to be written, the time between taking a subscription off and making it again, and, when the session starts, the time
  from the last stored event (none without one). `Ports/Market` holds `IMarketDataFeed` (REST of public data, through the scanner's own rate limit),
  `IMarketStreams` (minute candles in batches; the liquidations of linear pairs, with the times a connection was lost and back) and `IScannerJournal`. `Market/Queries` holds the queries of the market API
  (`GetMarket`, `GetMarketPair`, `GetMarketCandles`, `GetMarketSeries`); `Market/MarketOrder` is the order of the rows of «Рынок»
  (a column and a direction, rows without a value last, equal values by category code and symbol, ordinally); `MarketData/ChartCandles` is the period of a chart the
  bot and the pair share. `Paging` holds `PageRequest` (the number from 1 and the size 50, 100 or 200 a client asked for, with the field errors
  `page` and `pageSize`; `Slice` gives a `Page<T>`, the last page when the number is past the end), `OrderDirection` and its codes `asc` and `desc`.
  `GetIdeas` and `GetMarket` filter, order and cut the page in memory, and `GetMarket` also counts the pairs for the chips; neither returns a whole list.
- `Scanner/Ideas/LiveAutos` is what the autos do in a minute of the scanner: it reads the autos that are on every minute, gives the ideas born in the minute to each in the order of the list, each auto taking them in the order of `AutoMinutePlan` within its limits (a request carries its place in that order, and the ideas a limit skipped are one refusal of the auto, the side and the minute), checks and opens the paper trades (`PaperEntry`, `PaperFill`) or writes the requests of the autos with money, steps the open paper trades by the closed minutes of their pairs (up to the end of the coverage of the pair, by the stored minutes when the memory no longer holds them) and returns the `AutoMinute` that `LiveIdeas` writes in the transaction of the ideas; once an hour it deletes the refusals older than 7 days and turns the requests no Worker took into refusals «Не успел войти».
- `Scanner/Ideas` holds the ideas of the scanner: `IdeasSettings` (the `Ideas` section, with `ThresholdSettings` and `ThresholdOverride`),
  `IdeaRuleSet`, `IdeaSeries` (at most 220 closed candles of a pair on an interval and the minutes of the forming one, and the `SetupBase` it keeps until
  a candle closes), `IdeaEngine` (the
  minutes of a pair one by one: steps the open ideas, reads the setups, makes the ideas of the setups that fire by the exit rules in force,
  with the features of the market: the change of the pair over a day from its own minutes, the change of BTC and the funding rate from
  `IdeaInputs`; in a replay it keeps no ideas and gives every signal), `LiveIdeas` (the warm-ups and the minute of live ideas after the snapshot; counts the distinct pairs of a side on an interval among the ideas of the minute into their `BurstPairs` before the autos and storage; reads the exit
  rules every minute and puts a type × interval whose rule changed into `IdeaRecomputes`; the ideas that closed on the minute go to storage with their
  outcome for the trades made of them, and `CatchUpTradeOutcomesAsync` writes it once at the start of the session for the trades that missed it), `IdeasReplay` (the replay of history pair by pair and day
  by day, with the minutes of BTC and the funding settlements a day reads for the features of its signals, the paths of the signals open at the cursor, the clean-up of other versions, which looks for their signals only while `IIdeaReplayRepository.HasOtherVersionsAsync` tells a cursor of one is left;
  `StartAsync` starts the turn of the pair furthest behind among those no turn runs, up to `Ideas:ReplayTurns` at once, the closes of BTC of a day shared by the turns for
  every day of the history window (`Scanner:HistoryDays` and two more), kept once BTC is covered to the end of the day, and the funding of a pair read once a turn; the end of a turn goes to `IScannerJournal.Replayed` with its days and the time of the engine, of the reads and of the writes), `IdeaOutcomes` (one pass over the signals by key and firing:
  the clustered sums of the buckets, their parents and their categories, after costs and without them, the buckets of the model with the expectancy of a second
  run of it over the results without costs, the curves of «Уровни» by the reward to risk over 90 days and the outcomes of the calibration, `IdeaOutcomesResult`
  with the roots of the categories of both runs, `IdeaRoots`; the burst of a case from the count of the pairs of its minute it is given) and `IdeaStatistics` (the buckets and the calibration from that
  pass, the whole one with the counts of the pairs by minute of `IIdeaSignalRepository.CountPairsByMinuteAsync` (counted a week of signals at a time, by hashing, since one grouping over the signals of a replay did not end on prod), whole once an hour in a lane of `ScannerSession`, a type × interval after its rule changed in another, so it never waits for the whole one; that
  one shrinks to the roots of the categories of the last whole pass and writes nothing before the first; the start of the version, written when
  the session takes the lease, and the rules applied tell the calibration after the rules changed). `Scanner/ScannerPace` tells the
  replay whether the snapshot came late.
- `Ideas` holds `IdeaCodes` (codes of setups, types, sides, statuses), `IdeaView` (an idea with its buckets and its own numbers, `IdeaNumbers`, which every reader of the probability, the expectancy and the badge of an idea takes), `IdeaLevels` (what the API
  measures a pair by: price, spread, ATR, zones, window), `IdeaOrder` (the order of the ideas of «Идеи» by a column; setup, type, side and
  status go in the order of their enums, which is the order of the lists on the screen, not of the Russian names) and the queries of the ideas
  API (`GetIdeas`, `GetIdea`, `GetIdeaBuckets`, `GetIdeaCalibration`, `GetPairIdeas`, `GetZones`, `GetOwnIdea`, `GetExitRules`, `GetExitRulePreview`, `GetExitRuleOutOfSample`) and
  the command `ApplyExitRule`, with `ExitRuleRequests` (a rule asked for by codes, checked against the grid) and `ExitRulePreviews` (the preview of a
  candidate rule: «было → станет» over 90 and 30 days, the expectancy at every lifetime of the grid and the curve of MFE, `ExitRulePreview`) and `ExitRuleOutOfSamples` (the out-of-sample check of a candidate: the selection 90 to 30 days ago and the check over the last 30 days of the candidate and of the best rule of the grid, clustered, and the probability of overfitting, `ExitRuleOutOfSample`). `SavedFilters` holds `SavedFilterFields`, the one
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
- The metrics of the scanner live where they are counted, on the meter `ProfitFox.Scanner`: `Application/Scanner/ScannerMetrics`, a
  singleton whose counter `profitfox.scanner.liquidations` grows by the rows of liquidations storage added, by `side` (`long`, `short`);
  an event storage already had is not counted. `MetricsRegistrar` lists both meters.
- A new HTTP client to Bybit is made only through `IHttpClientFactory` (`AddHttpClient`), or its requests are missing from the
  metrics.
- `Exchanges/TInvest` is the adapter of T-Invest Invest API on the generated gRPC clients of the NuGet package `Tinkoff.InvestApi`
  (`Tinkoff.InvestApi.V1`, without the facade of the SDK): `TInvestSettings` (the `TInvest` section: `Endpoint`, only the official host or a
  test host under `.invalid`; `AppName`; `RequestTimeout`; `Limits` a minute by group of methods; `Certificates`), `TInvestChannel` (the one
  `GrpcChannel` of the process behind `ITInvestCallInvokers`, which tests replace with `Tests.Shared/TInvest/MockTInvestApi`),
  `TInvestCertificates` (the pinned chain of the server certificate: the `.cer` files of `Exchanges/TInvest/Certificates`, copied to the
  output, by the SHA-256 thumbprints of the settings; the chain is built for server authentication from the certificates the server
  presented and the pinned ones, without a download, and its second element must be the pinned Sub CA itself, not another Sub CA of
  the same root; empty thumbprints pin nothing), `TInvestCall` (every unary call: `TInvestRequestLimiter`
  → `authorization: Bearer` and `x-app-name` → deadline → `TInvestMetrics` → the exceptions of Application; `OnceAsync` is one attempt,
  `WithRetriesAsync` the ladder of `ExchangeResilienceExecutor`; `RESOURCE_EXHAUSTED` pauses the group of the token until `x-ratelimit-reset`
  and is `ExchangeRateLimitedException`, `NOT_FOUND` is `TInvestNotFoundException`, a refusal carries the error code of the status detail and
  never the text of the broker; a call cancelled by its own token is an `OperationCanceledException`, neither counted nor translated, and the
  channel throws it itself with `ThrowOperationCanceledOnCancellation`), `TInvestRequestLimiter` (a sliding window of a minute per token, kept by its SHA-256 fingerprint, and `TInvestMethodGroup`,
  `TInvestMethods` names the group of a service; every limit of the settings is above zero, or the registrar refuses them), `TInvestValues` (`Quotation` and `MoneyValue` ↔ `decimal` as `units + nano / 1e9`, timestamps in UTC),
  `TInvestCodes` (the `CandleInterval` of an `Interval` and the longest window of one request of candles: 1 and 3 min a day, 5 min a week,
  15 and 30 min three weeks, the hours 90 days), `TInvestTokenSource` (the token of the oldest T-Invest account of storage, kept 5 minutes;
  without an account `ExchangeUnavailableException` and no request), `TInvestShares` (a ticker resolved into a share of TQBR with `ShareBy`,
  kept an hour; a share the system trades is in roubles, trades through the API and has a positive lot and price step),
  `TInvestMarketDataClient` (`IMarketHistoryClient`, `IInstrumentRulesProvider` and `IInstrumentCatalog` for `Moex`: the catalog from
  `Shares(BASE)`, the rules lot = step = minimum and no minimum amount, the last prices by uid, the candles window by window from the end of
  the period with the source `Exchange` and no limit, complete ones only, the volume lots × lot and the turnover volume × (high + low + close) / 3)
  and `TInvestAccountClient` (`IBrokerAccountReader`: `GetAccounts`, `GetPortfolio` in roubles: every position with a quantity as a row, a currency by its ISO code (the first three letters of its
  ticker) worth its quantity at the current price in roubles, the roubles always a row, the total from `total_amount_portfolio`).
  The ports of trading on T-Invest: `TInvestOrderIds` (`ITInvestOrderIds`, the table `moex_orders` on a connection of its own for every
  call, outside any unit of work: a random request id written under the client id of an order before its first send, every send of the
  order with it, the exchange id added after; an order whose request id has no row is of nobody), `TInvestOrderStates` (an `ExchangeOrder`
  of an `OrderState`: the volume lots × lot, the value the sum of the stages, the fee `executed_commission` in roubles; the stages as fills
  without a fee; `Complete` whether the stages add up to the lots executed), `TInvestOrderReads` (the state by request or exchange id,
  `null` on `NOT_FOUND`), `TInvestTradingStatuses` (`MayPlaceAsync`: the trading status and the corridor of the order book of a share,
  kept 5 s, `TInvestPlacement`; `NextSessionStartAsync` and `TradingDayEndAsync` by `TradingSchedules` of the exchange of the share
  (`TInvestShare.Exchange`), kept to the end of the UTC day: the intervals of the exchange count, those of the dealer do not, a day with
  another type tells none; a session that started within the last minute is the one opening now), `TInvestOrderGateway`
  (`IOrderGateway`: an order in lots, a limit one `DAY` and a market one without a price, sent only while `MayPlaceAsync` takes it; a
  closed session is `Transient` with `RetryAfter` until the next session, or a minute without a schedule or in the first minute of a
  session, the corridor a minute; the refusals by the code of `TInvestRefusedException` (`30057` an order taken, read by its request id); a cancel by
  the request id, `AlreadyDone` for an order without one, one T-Invest does not know or one that ended), `TInvestLedger`
  (`IExchangeLedger`: the free roubles less those blocked from `GetPositions`, the shares with those blocked, the active orders by
  `GetOrders` told by their request ids, an order and its stages by `GetOrderState`, the fee rate `deal_commission / initial_order_amount`
  of `GetOrderPrice` for a limit buy of at least 100 000 ₽, `ExchangeUnavailableException` without a last price of the share or an amount
  of the trial order, no hedge, no orders of a parent) and `TInvestStreams` (`IExchangeStreams` and
  `IFormingCandleStreams`: one `OrderStateStream` an account with pings, opened again at once with the token of a listener whose token
  of the account differs (the owner changed it), whose message is a signal: the state of an order of a bot is read
  by its request id up to 5 times a second apart while T-Invest does not know it or its stages disagree, a filled one without its
  commission again at 5, 15, 30 and 60 s, and an order of nobody that traded is a fill of nobody on its share; a message whose order does
  not read (T-Invest, the table of the ids or the share) counts as `error` of the stream and tells every listener of the account
  `restored`, so the sessions read their orders themselves; two `MarketDataStream`s of
  the process with the token of `TInvestTokenSource`, of the closed candles (`waiting_close`) and of the forming ones, a series subscribed
  once for all its listeners and taken off by the last; a stream that ends, breaks, is silent for 90 s or fails on a message (a candle it
  cannot tell, a listener that threw) opens again after 1, 2, 5, 10, then 30 s, subscribes its series again and tells every listener
  `restored` (the forming ones `lost` first); a minute after the end of the
  trading day of a share its listeners of closed candles are told `restored`). `TInvestCall` throws `TInvestRefusedException` with the
  code of the detail for a refusal (`TInvestNotFoundException` for `NOT_FOUND`).
  The metrics live on the meter `ProfitFox.TInvest`: the counter `profitfox.tinvest.requests` {`group`, `method`, `outcome`} (a stream
  counts its open as `ok` or `error`, its break as `lost`, and a message of orders that does not read as `error`) and the gauges
  `profitfox.tinvest.limit` and `profitfox.tinvest.limit_remaining` {`group`} from the headers `x-ratelimit-limit` and `x-ratelimit-remaining`;
  `MetricsRegistrar` lists the meter. No test and no local run calls the real T-Invest: the test hosts put `MockTInvestApi` behind
  `ITInvestCallInvokers`, and `appsettings.Testing.json` points the endpoint at `tinvest.invalid`.
- `Exchanges/Routing` holds `RoutedMarketHistoryClient`, `RoutedInstrumentRulesProvider` and `RoutedInstrumentCatalog`: the registered
  read ports, each choosing Bybit or T-Invest by `Category.Exchange()`; the adapters of both exchanges are registered keyed by
  `Exchange`, and `CachedInstrumentCatalog` stays over the routed catalog, caching the list of `moex` on the same terms. The ports of
  trading are routed the same way and registered as the routers only: `RoutedOrderGateway` and `RoutedExchangeLedger` by
  `ExchangeKey.Exchange`, `RoutedExchangeStreams` by the key for an account and by the category for the candles, and
  `RoutedFormingCandleStreams` by the category; `BybitOrderGateway` and `BybitLedger` (typed clients of `IHttpClientFactory`),
  `TInvestOrderGateway` and `TInvestLedger` are keyed by `Exchange`, and the singletons `BybitStreams` and `TInvestStreams` are keyed
  for both interfaces of streams. A test that replaces a port whole with `FakeExchange` removes the router and its keyed
  implementations together.
- `Exchanges/<Vendor>` holds adapters over vendor SDKs, including the mapping of domain values to SDK types
  (`BybitCodes`). `Exchanges/Retry` holds Polly pipelines: `ExchangeResilienceExecutor` (the ladder of retries and the circuit breaker) is
  registered keyed by `Exchange`, one instance per exchange, so an exchange that does not answer never opens the circuit of the other; a
  consumer takes it with `[FromKeyedServices(Exchange.Bybit)]` or `[FromKeyedServices(Exchange.TInvest)]`. `BybitMarketDataClient` implements `IInstrumentCatalog` (spot in one
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
  own, apart from `BybitStreams`, and the liquidations (`allLiquidation`; Bybit's side is the side of the liquidated position:
  `Buy` → `Long`, `Sell` → `Short`) on another, so a break of one stream does not touch the other.
- `Exchanges/Bybit/BybitAccountClient` implements `IExchangeKeyInspector`: a typed `HttpClient` that builds a Bybit client
  with the key for one call and drops it, one attempt per call. `Security/AesGcmSecretProtector` encrypts secrets with the
  master key (AES-256-GCM, the account id as associated data).
- `Exchanges/Bybit/BybitStreams` implements `IExchangeStreams` of Bybit as one singleton for the process, so the connections to
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
- `Repositories/IdeaTrades` holds `idea_trades` (the lease columns as `live_bots`; the columns of the promise are written once by the `INSERT`, the columns `idea_status`, `idea_status_at` and `idea_result` only by the scanner in `IdeaRepository.SaveAsync`, without a new `updated_at`, and the save of a Worker lists neither), `idea_trade_orders`, `idea_trade_children` (the stop and the target the exchange
  made, by exchange id), `idea_trade_fills`, `idea_trade_forced_shares` (the shares of one liquidation or auto-deleveraging, by exec id and trade, each applied once) and `idea_trading_settings` (one row, written by the first save; the defaults come from the code). The bot session
  asks `IsChildAsync` for an execution it does not know. A trade has no foreign key to the idea: the replay cleans up old ideas.
- `Repositories/IdeaAutos` holds `idea_autos` (the auto, the unique name among those not deleted, the account a restricting foreign key), `idea_paper_trades` (the columns of a trade but the lease, the orders and the manual leg, and `stepped_until`; no foreign key to the idea or the account), `idea_auto_entries` (the requests, unique by idea and auto, taken in the order of `minute, rank, fired, id`) and `idea_auto_refusals` (unique by idea and auto, kept 7 days); `IdeaAutoEntryRepository.TryLockEntriesAsync` is the session advisory lock `idea-autos:entries` on a connection of its own. `IdeaRepository.SaveAsync` with an `AutoMinute` locks the lease of the stream first, then writes the ideas, the outcomes (on the paper trades too) and what the autos did with the ideas storage took. `idea_trades.auto_id` has no foreign key; `idea_autos.budget` is the budget of an auto, `idea_trading_settings` holds the three guards (`autos_budget` the budget of all the autos).
- `Repositories/Market` holds `scanner_state` (one row: the lease of the stream, the snapshot time, the progress of history,
  the end of the REST pause), `scanner_pairs`, `open_interest` (a hypertable), `funding_rates` and `market_snapshot`, which the
  holder replaces in one transaction that locks the row of the state with its lease. `liquidations` is a hypertable of daily
  chunks, unique by the whole event (`INSERT … ON CONFLICT DO NOTHING`, the rows inserted counted by side), compressed by
  `category, symbol` after 7 days by a policy of TimescaleDB and never deleted, the first compression in the project;
  `stream_breaks` holds the breaks of a stream by its name and the time it was lost.
- `Repositories/Ideas` holds `ideas` (one open idea per key kept by a partial unique index; the ideas made and moved on a minute are
  written in one transaction), `idea_cases` (a hypertable of the signals with their paths, `IdeaSignalRepository`: a batch goes in by binary copy and onto
  its key, fired time and version, a path written again goes on; read one by one in the order of key and firing; the outcome columns of version 4 stay
  empty), `idea_exit_rules` (one row per type × interval applied), `idea_buckets` (with the expectancy without costs), `idea_risk_reward_curves` (the cases and targets of a band
  of «Уровни» per version, interval, side and category, replaced with the buckets in one transaction by `IdeaBucketRepository`), `idea_replay`
  (the cursor of a pair per version with the paths of the replay open at it), `idea_volatility` and `idea_calibration` (one `jsonb` report per
  version, window, category and scope, replaced whole like the buckets), `idea_engine_versions` (when the scanner first took the lease with a
  version of the rules, `IdeaEngineVersionRepository`, written once by `ON CONFLICT DO NOTHING`);
  `Repositories/SavedFilters` holds `saved_filters`. `market_snapshot.setups` (`jsonb`, interval code → setup codes) is written after the
  snapshot under the same lease. `CandleRepository.GetAggregatedAsync` joins stored minutes into candles of an interval with `time_bucket`.
- `Persistence/Postgres/Transactions` implements the unit of work: `PostgresTransactionExecutor` opens the connection
  and the transaction, the scoped `PostgresTransactionAccessor.Current` hands them to repositories that write inside a
  unit. Only repositories of live trading use it; the older repositories open a connection per call and keep their
  transactions inside one method.
- `BackgroundServices/IdeaAutoEntryBackgroundService` is the lane of a trading Worker (registered with `Trading:Enabled` only) that runs `RunNextAutoEntryCommand` after the poll interval or at once after a request. The Worker also reads `Backtests:DefaultFees` (in its `appsettings.json`), which the entry of an auto is sized with.
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
  market category. Every category has its `Limit` and `Market` (`BacktestSettings.CategoriesWithoutFees` is empty, or the service
  does not start); the rates of `moex` (0.05 / 0.05, the tariff «Трейдер») are provisional until the owner reads `GetOrderPrice`,
  and its `Slippage` is a stand-in until the ideas of MOEX come.

  ```json
  "Backtests": {
    "MaxCombinations": 10000,
    "MaxParallelism": null,
    "DefaultFees": {
      "spot": { "Limit": 0.1, "Market": 0.1 },
      "linear": { "Limit": 0.02, "Market": 0.055 },
      "moex": { "Limit": 0.05, "Market": 0.05 }
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

- The `TInvest` section (`TInvestSettings`, bound in `InfrastructureRegistrar`), in the `appsettings.json` of the API, the Worker and the
  scanner: the endpoint of the live Invest API (the registrar refuses any other host but one under `.invalid`, which the tests use), the
  app name, the timeout of a call, the requests a minute of each group of methods with one token in this process (the `MarketData` budget
  is 100 for the API, 150 for the Worker and 50 for the scanner, below the 300 of the token together) and the certificates the server
  chains to: the folder of the `.cer` files and the SHA-256 thumbprints of the Russian Trusted Root CA and Sub CA (empty pins nothing).
  No secret: the token is in the database.

  ```json
  "TInvest": {
    "Endpoint": "https://invest-public-api.tbank.ru:443",
    "AppName": "profit-fox",
    "RequestTimeout": "00:00:10",
    "Limits": { "Users": 50, "Instruments": 150, "MarketData": 100, "Operations": 100, "Orders": 50 },
    "Certificates": { "Directory": "Exchanges/TInvest/Certificates", "RootThumbprint": "", "SubThumbprint": "" }
  }
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
  environment sets `ExchangeAccounts__MasterKey`, or the API does not start. Every Worker requires it too: a trading one
  decrypts the keys of the bots, and any one decrypts the token of T-Invest for an import of `moex`; its
  `appsettings.Development.json` and `appsettings.Testing.json` carry the same keys as the API's. The scanner gets none.
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
    "Thresholds": {
      "Candles": 6, "Move": 3.0, "Volume": 2.0, "EarlyMove": 1.5, "EarlyVolume": 3.0,
      "BreakoutMargin": 0.1, "BreakoutVolume": 1.5, "SweepMargin": 0.2, "SqueezeCandles": 20, "ClimaxVolume": 4.0, "Wick": 1.0,
      "QuietVolume": 3.0, "QuietBody": 0.3, "BiasRatio": 1.5, "FlatMove": 0.5, "Overbought": 70, "Oversold": 30
    },
    "ByInterval": { "240": { "Move": 2.5 } },
    "StopBuffer": 1.5,
    "ZoneMerge": 0.5,
    "ZoneStrength": 2,
    "ReplayTurns": 2
  }
  ```

  `ReplayTurns` is how many turns of the replay of history run at once, each on a pair of its own; prod lowers it with `Ideas__ReplayTurns`.

## Presentation (`ProfitFox.Presentation`)

- `ExchangeAccountsController`: `POST /api/exchange-accounts` takes `exchange` (`bybit` by default, with `apiKey` and `apiSecret`; `tinvest`
  with `token` and `accountId`), the answers of an account carry `exchange` and `accountId` with `apiKey` and `uid` nullable;
  `POST /api/exchange-accounts/tinvest/accounts` `{ token }` lists the accounts of a token (`[{ id, name, type, status, access, problem }]`,
  a token T-Invest rejects is a 400 on `token`); `GET .../{id}/balance` gives `currency`, `totalEquity` and `coins` with their `value` in it.
  No endpoint logs a body: the request logging writes the method, the path and the status only.
- `Controllers` holds controllers only (`BotsController`, `BacktestsController`, `InstrumentsController`: `GET /api/market-data/instruments?category&query&limit`
  → `[{ category, symbol, baseCoin, quoteCoin }]`; field errors 400, exchange unavailable 503); request and response types go to
  `Contracts/<Area>` (`Contracts/Bots`, `Contracts/Backtests`).
- `MarketController`: `GET /api/market` (one page of the snapshot and the state of the scanner: `category`, `search`, `setup`, `interval`,
  `savedFilter`, `sort`, `direction`, `page`, `pageSize`; the answer has `items`, `page`, `pageSize`, `total`, `pairs`, `categories` and `setups`,
  the counts of the chips, and `replay`, the progress of the replay of ideas of `IdeasEngineVersion.Current` that `IIdeaReplayRepository.ProgressAsync` counts on reading, given the first day of the history window: a pair is done when it is at most a day behind the day of its coverage), `GET /api/market/{category}/{symbol}` (a row;
  404 when the snapshot has no such pair), `.../candles?interval&from&to` (as the bot chart), `.../open-interest?from&to` and
  `.../funding?from&to` (linear only, from storage, at most 1000 points); contracts in `Contracts/Market`. It never calls Bybit
  but for the candles of a pair.
  `MarketController` also has `.../ideas` (the ideas of a pair), `.../zones?interval` (5, 15, 60, 240) and `POST .../own-idea`; the rows of
  `GET /api/market` and of a pair carry `setups`. `IdeasController`: `GET /api/ideas` (filters `setup`, `interval`, `type`, `side`,
  `category`, `nonNegativeExpectancy`, `activeOnly`, `hideStocks` (the ideas of non-crypto pairs of the scanner), `savedFilter`; `sort`, `direction`, `page`, `pageSize`; the answer is `items`, `page`, `pageSize` and
  `total`), `GET /api/ideas/{id}`, `GET /api/ideas/buckets`, `GET /api/ideas/calibration` (`days` 90 or 30, `category` linear, spot or none, `scope` all or fresh; the answer has `owns` and `comparison`). `IdeaExitRulesController`: `GET /api/ideas/exit-rules`
  (every type × interval with its grid), `GET /api/ideas/exit-rules/preview` (`type`, `interval`, `life`, `target`, `window`; 400 by the name of a parameter the
  type has no use for or a value off the grid) and `PUT /api/ideas/exit-rules/{type}/{interval}` (`{ life, target, window }` → the rule with its time). `SavedFiltersController`: CRUD of
  `/api/saved-filters`, the catalog `GET /api/saved-filters/fields` and `POST /api/saved-filters/fit` (`{ conditions }` of a draft →
  `{ pairs, ideas }`, saves nothing).
- `StrategyTemplatesController`: `GET /api/strategy-templates` → `[{ code, name, description, direction }]` (`long` or `short`, 14 templates in a fixed order),
  `GET /api/strategy-templates/{code}` → `{ code, name, description, definition }` (404 for an unknown code). The `definition` has an empty layout and goes
  to `POST /api/strategies` as it is: a strategy made from a template is an ordinary strategy; its codes are in `StrategyCodes`, the queries `GetStrategyTemplates`
  and `GetStrategyTemplate` in Application.
- `IdeaTradesController`: `GET /api/idea-trades` (the journal: `open`, `closed` as `items`, `page`, `pageSize` and `total`, and `totals`; filters `setup`, `symbol`, `side`, `badge`, `days`, `page`, `pageSize`), `POST` (`{ ideaId, accountId?, risk? }` → 201 `{ id }`, or 409 with `reasons`, codes of what stops the
  entry), `POST .../{id}/cancel` and `.../close` (204; 409 when the status does not allow it). `IdeasController` also has `GET /api/ideas/{id}/entry?accountId&risk`
  (the entry as it would be made now, reading the price of the pair from Bybit; places nothing). `IdeaTradingSettingsController`: `GET` and `PUT /api/idea-trading/settings`.
  Numbers of these contracts are decimal strings.
- `IdeaAutosController`: `GET` and `POST /api/idea-autos` (the list with totals; 201 `{ id }`), `GET`, `PUT` and `DELETE /api/idea-autos/{id}`, `PUT .../{id}/mode` `{ mode }`, `PUT /api/idea-autos/order` `{ ids }` (400 on `ids` unless it holds every auto of the list once) and `GET /api/idea-autos/refusals?period=today|7d&autoId&page&pageSize`; a wrong field is a 400 on its name. `IdeaTradingSettingsController` also has `GET /api/idea-trading/guards`. The journal `GET /api/idea-trades` also takes `origin` (`manual`, `auto`, `paper`) and `autoId`, gives each trade its `origin`, `autoId` and `autoName`, and `paperTotals` beside `totals`.
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
  `IntegrationEnvironment` starts one container with one test database, migrated once (`IntegrationEnvironment.ConnectionString`,
  `MarketDataDatabase.Open()`); `ResetDatabaseAttribute` empties its tables before every test and writes back the rows the
  migrations write. A test of a migration takes an empty database of its own (`CreateEmptyDatabaseAsync`); a test that makes a
  table of its own drops it.
  `ProfitFoxFactory` runs the service in the `Testing` environment: test-only settings go to
  `ProfitFox.Presentation/appsettings.Testing.json`, the connection string comes from the container. Next to the API it
  starts `Workers` instances of `WorkerHost` (one by default, none to keep work `Pending`) on the same database, fake
  Bybit and clock; a test may start more `WorkerHost`s itself. Both hosts validate that every registered service resolves.
  A Worker waits its poll interval on the fake clock, so a test that waits for work moves the clock.
  `Tests.Shared` holds AutoFixture generators under `Generators/Base` and
  `Generators/Domain/{Entity,ValueObject}/<Area>`, each a static `<Type>Generator.Generate(...)`. `Builders/<Area>` holds
  fluent builders for structures a generator cannot randomize meaningfully (`GraphBuilder` for strategy graphs), and
  `Fixtures/<Area>` holds reference documents as embedded resources (the reference strategies EMA, EMA Short, DCA, Ladder;
  reference candle sets for backtests in `Fixtures/Backtests`: the hourly set of Bybit and `sber-hourly`, two trading days of a share
  of MOEX with the night between them empty; reference minutes and candles of the ideas and the fingerprints of
  `IdeasEngineVersion` in `Fixtures/Ideas`).
- `Fixtures/IdeaAutos` holds the reference minutes of the paper trades (a long enters at 100 with the stop at 98 and the target at 104; the pullback sets wait for a limit at 100) with the answers written out in `PaperFillTests`; `Generators/Domain/Entity/IdeaAutos` makes an auto.
- Ports are replaced with NSubstitute mocks. Hand-written stubs exist only for transport and the exchange: HTTP
  (`StubHttpHandler`, `FakeBybitHandler`), because NSubstitute cannot replace the protected `SendAsync`; the Bybit v5
  streams on a local port (`FakeBybitSocketServer`); the gRPC server of T-Invest in process (`Tests.Shared/TInvest/MockTInvestApi`, a
  `CallInvoker` with handlers by method name that records every call with its metadata and fails a method with the status a test asks for,
  and answers the server and duplex streams from a `MockTInvestStream` script, `OnServerStream` and `OnDuplex`: each call opens a
  `MockTInvestStreamCall` with what the client wrote on it, which the test sends answers to, completes or fails with a status);
  and `FakeExchange`, one exchange account in memory behind
  `IExchangeLedger`, `IOrderGateway` and `IExchangeStreams` for the live trading runtime, whose orders, fills and streams
  depend on each other as a mock cannot keep.
  `FakeExchange` also makes the stop and the target of an entry with a stop and a target on `linear` (no client id, `ParentClientId`, a stop kind), fires them at a
  price (`TriggerChild`) and cancels them by exchange id.
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
  `backtest-result`, `compare`, `accounts`, `account-new`, `account`, `market`, `market-pair`, `ideas`, `ideas-calibration`, `ideas-exit-rules`, `idea-trades` (the journal, `/idea-trades`, with `?page=` for the closed trades and `?autoId=` read once into the filter, and the settings of trading by ideas, `/idea-trades/settings`), `idea-autos` (the list of the autos, `/idea-trades/autos`), `idea-auto` (the form of an auto, `/idea-trades/autos/new` and `/idea-trades/autos/{id}`)); a page never imports another page.
- `src/modules/<area>` holds a feature: `model` for pure functions, `ui` for components, `api.ts` for queries. Other
  code imports a module only through its `index.ts`. `modules/graph-editor/model` is the logic of the editor that can be
  wrong (definition ↔ canvas, pins, wire types, error paths, draft comparison): plain TypeScript without React.
- Modules of the bot section: `bots` (queries, `BotForm`, server errors by field, `parameterValues` from form text),
  `backtests` (queries with polling every 2 s while a backtest is unfinished, `BacktestForm`, status, ranges, grid,
  period, result columns, engine conflicts), `market-data` (instrument search for symbol suggestions, the intervals: `intervals` of Bybit, `intervalsOf(category)`
  (eight for `moex`, without `D`) and `marketCategories` with the type `MarketCategory`, `'spot' | 'linear' | 'moex'`),
  `exchange-accounts` (queries, `AccountForm` with the switch of the exchange: Bybit takes a key and a secret, T-Invest a token and
  one of the accounts «Показать счета» lists through `listBrokerAccounts`, an account with a `problem` cannot be chosen and shows why
  (`model/brokerAccounts`: the labels of the type, status and problem); a refusal of the server clears the secret, the token and the
  accounts, a changed token drops the accounts read with the old one, and while the accounts are read or the account is saved the
  switch of the exchange and the token are locked, so a late answer finds the form as it was; `exchangeName`, `EnvironmentBadge`, `BalanceTable` with the currency of the answer, server problems of an
  account; the balance query is disabled and runs only on the page's action), `comparison` (the set of results on `useSyncExternalStore` over `sessionStorage`, the bar in
  `AppShell`, the mark checkbox), `market-chart` (`PriceEquityChart` and `EquityComparisonChart` on
  `lightweight-charts`, fill markers, candles per point; for the bot chart `BotCandleChart` with volume, fill arrows,
  dashed order lines and the current candle, over the pure `chartPaging`, `tradeMarkers`, `liveCandles`, `volumeBars` and
  `orderLines`, `useCandlePages` — the paging of a chart over a function that reads a page, which the bot and the pair use with their
  own endpoints — and `OpenInterestChart`, `FundingChart`), `market` (the query of one page of the snapshot, polled
  every 15 s and asked with the category, the search (after a pause of 300 ms), the setup chip and its interval, the saved filter, the sort and the page,
  the row, open interest and funding of a pair; the counts of the category buttons and of the chips come from the answer, nothing is filtered or sorted in the browser;
  `model` for the codes of the sort columns, the size of a page and the rest of the choice kept in the browser (`choice`), the age of the snapshot and
  compact numbers; `setups` for the setups, the interval of setups and the column «Сетап»; `MarketTable`, the banners (the history, the pause of REST, the replay of ideas while
  `done < total`, stale data) and the mark of incomplete data), `ideas` (queries of one page of the ideas, of a pair's ideas, zones, the own idea, the calibration, the exit rules and the preview of a candidate
  rule (asked 300 ms after its last change), and the apply of a rule; `model` for the labels, the codes of the sort columns, the
  probability, the badge and the expectancy of the idea with its money on 100 USDT and the hints of what they are made of, «мало данных», the break-even rounded up and the shares of the outcomes (`stats`), the colour of a difference, the verdict of the Brier
  score and the names of the rows of the calibration (`calibration`), profit and loss at the risk per idea and its storage in the browser (`riskStorage`), the groups of a
  pair's list (`groups`), the levels drawn on the chart (`levels`) and the exit rules (`exitRules`: a time up to the grid, the duration of candles, the
  warning of a fit, the equality of two rules); `IdeasTable`, `PairIdeasList`, `OwnIdeaBlock`, and `LifeChart` and `MfeChart`, plain SVG of the screen
  «Срок жизни и цель», which turn decimal strings into numbers only to draw them), `idea-trades` (queries of the settings, of the entry of an idea (polled every 5 s while its window is open), of the journal (`ideaJournalQuery`: the open trades, a page of the closed ones and the totals, polled every 15 s and asked with the setup, the pair (after a pause of 300 ms), the side, the badge, the period and the page; the old page stays while a new one loads) and the entry, cancel and close; `model` for the labels of statuses and of the reasons, the fields of the settings, the labels of the journal (`journal`: the time in a trade, the promise, the outcome of the idea, the totals and the two comparisons) and the choice kept in the browser (`choice`, `profit-fox.idea-journal.choice`: the filters and the size of a page); `EntryDialog`, `OpenTradesTable` (with the question before a cancel or a close), `ClosedTradesTable`, `JournalTotals`, `SettingsPanel` (with the guards of the autos and their state), and the filters «Кто взял» (`origin`) and «Автомат» (`autoId`) of the journal), `idea-autos` (queries of the autos, an auto, the guards and a page of the refusals, the save, the mode, the order (moved at once) and the delete; `model` for the labels of the modes, the conditions and the reasons, the badge «не хуже выбранного», the form ↔ the body and the fields changed (`autoForm`, with the budget), the text of the detail of a refusal by the kind of its reason (`refusal`), the order after a drag (`order`) and the totals (`totals`: the closed trades, the budget taken «занято X из Y USDT» and the parts of the line of the guards); `AutosTable`, `AutoForm`, `GuardsStrip`, `DailyStopBanner` and `RefusalsBlock`, which «Сделки» and «Торговля идеями» take from the module), `saved-filters`
  (queries of filters and the catalog of fields, and the count of a draft, `fitQuery`; `model` for the text of conditions (`conditions`) and the
  server errors by field (`errors`); `FilterBar`, `FilterEditor`, whose counter «Подходит» is the answer of `POST /api/saved-filters/fit` for the ready
  conditions, after a pause of 300 ms; the API applies saved filters, the browser only names the chosen one). The lists «Идеи», «Рынок» and the closed trades of «Журнал» show one page: the number is
  in the address (`?page=`, a whole number from 1, otherwise the first page; a page past the end moves the address to the page the API gave), the size
  (50, 100 or 200) in the choice kept in the browser, and a change of any other choice goes back to the first page. `shared/ui/Pager` is the page buttons,
  the rows shown and the size; `shared/paging` holds the sizes; `shared/hooks/useDebounced` is the pause. `BotCandleChart` also takes optional price lines, zones (bands drawn by the series primitive `ZoneBandsPrimitive`)
  and the marker of a fired setup, from the pure `market-chart/model/ideaOverlay`; `shared/format/decimal` compares decimal strings
  without float math. `bots` also holds the bot chart's data: `useBotCandles` (pages going back, a minute-by-minute tail,
  start over when too far behind), `useFormingCandle` over `realtime/followFormingCandle` (the SignalR hub
  `/api/hubs/bot-chart`, which is not in OpenAPI: its message types are written by hand in `model/formingCandle.ts`,
  with the reducer and the channel text) and `chartIntervals` (the interval switcher, `chartIntervals(own, category)` and
  `shownInterval(asked, own, category)` over `intervalsOf(category)`, so a bot on `moex` is offered no `D`; the `?interval=` of the bot route
  is read there, the bot's own interval never goes to the address). Texts about an exchange that does not answer name «биржа», not Bybit,
  since the data of a share come from T-Invest. The router writes text search values as they are, so
  `?interval=5` is not quoted. `shared/format` shows decimal strings with thousands
  spacing without float math, and UTC dates. Decimal strings become numbers only in `market-chart/model`, for drawing.
- «+ Новая стратегия» opens `pages/strategies/NewStrategyDialog`: an empty canvas or one of the built-in templates (`strategyTemplatesQuery`, the cards show name,
  idea and direction). A template is read with `fetchStrategyTemplate` and written as the draft of a new strategy under a new `newDraftId()` (`shared/id`, built on
  `crypto.getRandomValues`, since the app is served over plain HTTP); from there it is an ordinary new strategy, saved by the first save.
- `src/shared/api` holds the only HTTP client (`openapi-fetch`) and the types generated from the API's OpenAPI document
  in `generated/schema.d.ts`. That file is committed: after changing an API contract, run the API and
  `npm run generate:api`, and commit the result with the change.
- Colors are theme tokens in `src/app/styles/index.css`, taken from the mockups; components use the tokens, not raw
  colors.
- Tests: Vitest next to the code (`*.test.ts(x)`). Pure functions follow red → green → refactor like the backend.
  Screens are tested through `renderApp` against `fakeApi`, which replaces `fetch` with a routing table; an unrouted
  request answers 404. The test setup replaces `lightweight-charts` with inert series and `@microsoft/signalr` with a silent
  connection; a test of the channel mocks `followFormingCandle` or the library itself. End-to-end tests in `e2e/` run Playwright against the real API through the `web` image.
