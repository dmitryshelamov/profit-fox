namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

internal sealed class BacktestDb
{
    public Guid Id { get; init; }

    public Guid BotId { get; init; }

    public Guid StrategyId { get; init; }

    public int StrategyVersion { get; init; }

    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Interval { get; init; }

    public DateTimeOffset From { get; init; }

    public DateTimeOffset To { get; init; }

    /// <summary>Values and ranges of the parameters as JSON, the same document the API takes.</summary>
    public required string Parameters { get; init; }

    public decimal Balance { get; init; }

    public decimal LimitFeePercent { get; init; }

    public decimal MarketFeePercent { get; init; }

    public decimal SlippagePercent { get; init; }

    public required string LimitFillMode { get; init; }

    public decimal TickSize { get; init; }

    public decimal QuantityStep { get; init; }

    public decimal MinQuantity { get; init; }

    public decimal MinAmount { get; init; }

    public required string SearchMode { get; init; }

    public required string Status { get; init; }

    public string? Error { get; init; }

    public Guid? ImportJobId { get; init; }

    public DateTimeOffset? ActualStart { get; init; }

    /// <summary>Parts of the period without candles, as a JSON array of <c>{ "From": …, "To": … }</c>.</summary>
    public required string Gaps { get; init; }

    public long LoadedCandles { get; init; }

    public long ExpectedCandles { get; init; }

    public long CalculatedCombinations { get; init; }

    public long TotalCombinations { get; init; }

    public decimal? Speed { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public int EngineVersion { get; init; }
}