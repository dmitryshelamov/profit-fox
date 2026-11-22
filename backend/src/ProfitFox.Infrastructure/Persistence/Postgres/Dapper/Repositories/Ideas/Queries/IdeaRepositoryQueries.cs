namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

/// <summary>
///     The statements on <c>ideas</c>: a row per idea, added at its birth and changed as its cycle is counted.
/// </summary>
internal static class IdeaRepositoryQueries
{
    public const string Columns =
        """
        i.id AS "Id", i.strategy_id AS "StrategyId", COALESCE(s.name, i.strategy_name) AS "StrategyName", i.strategy_version AS "StrategyVersion",
        i.timeframe AS "Timeframe", i.symbol AS "Symbol", i.side AS "Side", i.moment AS "Moment", i.size AS "Size", i.after_minutes AS "AfterMinutes",
        i.until AS "Until", i.stop AS "Stop", i.target AS "Target", i.candle_close AS "CandleClose", i.labels AS "Labels", i.state AS "State",
        i.entry_raw AS "EntryRaw", i.entry_price AS "EntryPrice", i.entry_time AS "EntryTime", i.stop_price AS "StopPrice", i.target_price AS "TargetPrice",
        i.funding_paid AS "FundingPaid", i.funding_until AS "FundingUntil", i.last_close AS "LastClose", i.computed_until AS "ComputedUntil", i.now_result AS "NowResult", i.now_at AS "NowAt",
        i.exit_time AS "ExitTime", i.exit_reason AS "ExitReason", i.ambiguous AS "Ambiguous", i.exit_price AS "ExitPrice", i.result AS "Result",
        i.result_usdt AS "ResultUsdt", i.excluded AS "Excluded"
        """;

    // The name of an idea is that of its strategy now, or the one it was born with when the strategy is gone.
    public const string From = "FROM ideas i LEFT JOIN strategies s ON s.id = i.strategy_id";

    public const string Insert =
        """
        INSERT INTO ideas (id, strategy_id, strategy_name, strategy_version, timeframe, symbol, side, moment, size, after_minutes, until, stop, target,
                           candle_close, labels, state, now_result, now_at)
        VALUES (@Id, @StrategyId, @StrategyName, @StrategyVersion, @Timeframe, @Symbol, @Side, @Moment, @Size, @AfterMinutes, @Until, @Stop, @Target,
                @CandleClose, @Labels, @State, @NowResult, @NowAt)
        ON CONFLICT DO NOTHING
        """;

    public const string Save =
        """
        UPDATE ideas SET state = @State, entry_raw = @EntryRaw, entry_price = @EntryPrice, entry_time = @EntryTime, stop_price = @StopPrice,
                         target_price = @TargetPrice, funding_paid = @FundingPaid, funding_until = @FundingUntil, last_close = @LastClose, computed_until = @ComputedUntil,
                         now_result = @NowResult, now_at = @NowAt, exit_time = @ExitTime, exit_reason = @ExitReason, ambiguous = @Ambiguous,
                         exit_price = @ExitPrice, result = @Result, result_usdt = @ResultUsdt, excluded = @Excluded
        WHERE id = @Id
        """;

    public const string Open = $"SELECT {Columns} {From} WHERE i.state = 'open' ORDER BY i.moment, i.id";

    public const string Find = $"SELECT {Columns} {From} WHERE i.id = @Id";
}
