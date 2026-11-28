using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     The rows of <c>trades</c>, <c>trade_orders</c> and <c>trade_fills</c> and their columns: states, sides, reasons and
///     kinds as words, the timeframe by its name.
/// </summary>
internal static class TradeRows
{
    public const string Holding = "('pending', 'open', 'closing', 'error')";

    public const string TradeColumns =
        """
        t.id AS "Id", t.idea_id AS "IdeaId", t.symbol AS "Symbol", t.side AS "Side", t.strategy_id AS "StrategyId", t.strategy_name AS "Strategy",
        t.strategy_version AS "Version", t.timeframe AS "Timeframe", t.idea_moment AS "IdeaMoment", t.until AS "Until", t.idea_entry AS "IdeaEntry",
        t.account_id AS "AccountId", t.account_name AS "AccountName", t.amount AS "Amount", t.leverage AS "Leverage", t.click_price AS "ClickPrice",
        t.quantity AS "Quantity", t.stop AS "Stop", t.stop_source AS "StopSource", t.target AS "Target", t.state AS "State", t.reason AS "Reason",
        t.close_reason AS "CloseReason", t.entered AS "Entered", t.held AS "Held", t.entry_price AS "EntryPrice", t.exit_price AS "ExitPrice",
        t.fees AS "Fees", t.funding AS "Funding", t.result AS "Result", t.entry_wait_seconds AS "EntryWaitSeconds", t.stop_attempts AS "StopAttempts",
        t.stop_failures AS "StopFailures", t.unrealised AS "Unrealised", t.marked_at AS "MarkedAt", t.flat_since AS "FlatSince",
        t.created_at AS "CreatedAt", t.opened_at AS "OpenedAt", t.closed_at AS "ClosedAt"
        """;

    public const string OrderColumns =
        """
        trade_id AS "TradeId", number AS "Number", purpose AS "Purpose", reason AS "Reason", quantity AS "Quantity", status AS "Status",
        attempts AS "Attempts", next_attempt_at AS "NextAttemptAt", exchange_order_id AS "ExchangeOrderId", refusal AS "Refusal", created_at AS "CreatedAt"
        """;

    public const string FillColumns =
        """
        exec_id AS "ExecId", trade_id AS "TradeId", side AS "Side", price AS "Price", quantity AS "Quantity", fee AS "Fee", kind AS "Kind",
        reason AS "Reason", time AS "Time"
        """;

    public const string InsertTrade =
        """
        INSERT INTO trades (id, idea_id, symbol, side, strategy_id, strategy_name, strategy_version, timeframe, idea_moment, until, idea_entry,
                            account_id, account_name, amount, leverage, click_price, quantity, stop, stop_source, target, state, reason,
                            entry_wait_seconds, stop_attempts, created_at)
        VALUES (@Id, @IdeaId, @Symbol, @Side, @StrategyId, @Strategy, @Version, @Timeframe, @IdeaMoment, @Until, @IdeaEntry,
                @AccountId, @AccountName, @Amount, @Leverage, @ClickPrice, @Quantity, @Stop, @StopSource, @Target, @State, @Reason,
                @EntryWaitSeconds, @StopAttempts, @CreatedAt)
        """;

    public const string UpdateTrade =
        """
        UPDATE trades SET state = @State, reason = @Reason, close_reason = @CloseReason, entered = @Entered, held = @Held, entry_price = @EntryPrice,
                          exit_price = @ExitPrice, fees = @Fees, funding = @Funding, result = @Result, stop_failures = @StopFailures,
                          unrealised = @Unrealised, marked_at = @MarkedAt, flat_since = @FlatSince, opened_at = @OpenedAt, closed_at = @ClosedAt
        WHERE id = @Id
        """;

    public const string UpsertOrder =
        """
        INSERT INTO trade_orders (trade_id, number, purpose, reason, quantity, status, attempts, next_attempt_at, exchange_order_id, refusal, created_at)
        VALUES (@TradeId, @Number, @Purpose, @Reason, @Quantity, @Status, @Attempts, @NextAttemptAt, @ExchangeOrderId, @Refusal, @CreatedAt)
        ON CONFLICT (trade_id, number) DO UPDATE SET reason = EXCLUDED.reason, quantity = EXCLUDED.quantity, status = EXCLUDED.status,
            attempts = EXCLUDED.attempts, next_attempt_at = EXCLUDED.next_attempt_at, exchange_order_id = EXCLUDED.exchange_order_id, refusal = EXCLUDED.refusal
        """;

    public const string InsertFill =
        """
        INSERT INTO trade_fills (exec_id, trade_id, side, price, quantity, fee, kind, reason, time)
        VALUES (@ExecId, @TradeId, @Side, @Price, @Quantity, @Fee, @Kind, @Reason, @Time)
        ON CONFLICT (exec_id) DO NOTHING
        """;

    public static object Insert(Trade trade)
    {
        return new
        {
            trade.Id,
            trade.IdeaId,
            trade.Symbol,
            Side = TradeNames.Of(trade.Side),
            trade.StrategyId,
            trade.Strategy,
            trade.Version,
            Timeframe = Timeframes.Name(trade.Timeframe),
            trade.IdeaMoment,
            trade.Until,
            trade.IdeaEntry,
            trade.AccountId,
            trade.AccountName,
            trade.Amount,
            trade.Leverage,
            trade.ClickPrice,
            trade.Quantity,
            trade.Stop,
            StopSource = TradeNames.Of(trade.StopSource),
            trade.Target,
            State = TradeNames.Of(trade.State),
            trade.Reason,
            EntryWaitSeconds = (int)trade.EntryWait.TotalSeconds,
            trade.StopAttempts,
            trade.CreatedAt
        };
    }

    public static object Update(Trade trade)
    {
        return new
        {
            trade.Id,
            State = TradeNames.Of(trade.State),
            trade.Reason,
            CloseReason = trade.CloseReason is { } reason ? TradeNames.Of(reason) : null,
            trade.Numbers.Entered,
            trade.Numbers.Held,
            trade.Numbers.EntryPrice,
            trade.Numbers.ExitPrice,
            trade.Numbers.Fees,
            trade.Numbers.Funding,
            trade.Result,
            trade.StopFailures,
            trade.Unrealised,
            trade.MarkedAt,
            trade.FlatSince,
            trade.OpenedAt,
            trade.ClosedAt
        };
    }

    public static object Of(TradeOrder order)
    {
        return new
        {
            order.TradeId,
            order.Number,
            Purpose = TradeNames.Of(order.Purpose),
            Reason = order.Reason is { } reason ? TradeNames.Of(reason) : null,
            order.Quantity,
            Status = TradeNames.Of(order.Status),
            order.Attempts,
            order.NextAttemptAt,
            order.ExchangeOrderId,
            order.Refusal,
            order.CreatedAt
        };
    }

    public static object Of(TradeFill fill)
    {
        return new
        {
            fill.ExecId,
            fill.TradeId,
            Side = TradeNames.Of(fill.Side),
            fill.Price,
            fill.Quantity,
            fill.Fee,
            Kind = TradeNames.Of(fill.Kind),
            Reason = fill.Reason is { } reason ? TradeNames.Of(reason) : null,
            fill.Time
        };
    }

    internal sealed class TradeRow
    {
        public Guid Id { get; init; }

        public Guid IdeaId { get; init; }

        public string Symbol { get; init; } = "";

        public string Side { get; init; } = "";

        public Guid StrategyId { get; init; }

        public string Strategy { get; init; } = "";

        public int Version { get; init; }

        public string Timeframe { get; init; } = "";

        public DateTimeOffset IdeaMoment { get; init; }

        public DateTimeOffset Until { get; init; }

        public decimal IdeaEntry { get; init; }

        public Guid? AccountId { get; init; }

        public string AccountName { get; init; } = "";

        public decimal Amount { get; init; }

        public int Leverage { get; init; }

        public decimal ClickPrice { get; init; }

        public decimal Quantity { get; init; }

        public decimal Stop { get; init; }

        public string StopSource { get; init; } = "";

        public decimal? Target { get; init; }

        public string State { get; init; } = "";

        public string? Reason { get; init; }

        public string? CloseReason { get; init; }

        public decimal Entered { get; init; }

        public decimal Held { get; init; }

        public decimal? EntryPrice { get; init; }

        public decimal? ExitPrice { get; init; }

        public decimal Fees { get; init; }

        public decimal Funding { get; init; }

        public decimal? Result { get; init; }

        public int EntryWaitSeconds { get; init; }

        public int StopAttempts { get; init; }

        public int StopFailures { get; init; }

        public decimal? Unrealised { get; init; }

        public DateTimeOffset? MarkedAt { get; init; }

        public DateTimeOffset? FlatSince { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? OpenedAt { get; init; }

        public DateTimeOffset? ClosedAt { get; init; }

        public double? IdeaResult { get; init; }

        public Trade ToDomain()
        {
            var trade = new Trade
            {
                Id = Id,
                IdeaId = IdeaId,
                Symbol = Symbol,
                Side = TradeNames.Side(Side),
                StrategyId = StrategyId,
                Strategy = Strategy,
                Version = Version,
                Timeframe = Timeframes.TryParse(Timeframe, out TimeSpan timeframe) ? timeframe : throw new InvalidOperationException($"{Timeframe} is not a timeframe."),
                IdeaMoment = IdeaMoment,
                Until = Until,
                IdeaEntry = IdeaEntry,
                AccountId = AccountId,
                AccountName = AccountName,
                Amount = Amount,
                Leverage = Leverage,
                ClickPrice = ClickPrice,
                Quantity = Quantity,
                Stop = Stop,
                StopSource = TradeNames.Source(StopSource),
                Target = Target,
                EntryWait = TimeSpan.FromSeconds(EntryWaitSeconds),
                StopAttempts = StopAttempts,
                StopFailures = StopFailures,
                Unrealised = Unrealised,
                MarkedAt = MarkedAt,
                FlatSince = FlatSince,
                CreatedAt = CreatedAt
            };
            var numbers = new TradeResult(Entered, Held, EntryPrice, ExitPrice, Fees, Funding, Result ?? 0m, false, null);
            return trade.Restore(TradeNames.State(State), Reason, TradeNames.Reason(CloseReason), numbers, Result, OpenedAt, ClosedAt);
        }
    }

    internal sealed class OrderRow
    {
        public Guid TradeId { get; init; }

        public int Number { get; init; }

        public string Purpose { get; init; } = "";

        public string? Reason { get; init; }

        public decimal Quantity { get; init; }

        public string Status { get; init; } = "";

        public int Attempts { get; init; }

        public DateTimeOffset NextAttemptAt { get; init; }

        public string? ExchangeOrderId { get; init; }

        public string? Refusal { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public TradeOrder ToDomain()
        {
            return new TradeOrder(TradeId, Number, TradeNames.Purpose(Purpose), TradeNames.Reason(Reason), Quantity, TradeNames.Status(Status), Attempts, NextAttemptAt, ExchangeOrderId, Refusal, CreatedAt);
        }
    }

    internal sealed class FillRow
    {
        public string ExecId { get; init; } = "";

        public Guid TradeId { get; init; }

        public string Side { get; init; } = "";

        public decimal Price { get; init; }

        public decimal Quantity { get; init; }

        public decimal Fee { get; init; }

        public string Kind { get; init; } = "";

        public string? Reason { get; init; }

        public DateTimeOffset Time { get; init; }

        public TradeFill ToDomain()
        {
            return new TradeFill(ExecId, TradeId, TradeNames.Side(Side), Price, Quantity, Fee, TradeNames.Kind(Kind), TradeNames.Reason(Reason), Time);
        }
    }
}
