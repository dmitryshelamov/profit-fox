namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>
///     A row of <c>ideas</c>.
/// </summary>
internal sealed class IdeaDb
{
    public Guid Id { get; init; }

    /// <summary>
    ///     The stored score, equal to the one by the weights now (design.md, decision 2).
    /// </summary>
    public decimal Score { get; init; }

    public Guid StrategyId { get; init; }

    public string StrategyName { get; init; } = "";

    public int StrategyVersion { get; init; }

    public string Timeframe { get; init; } = "";

    public string Symbol { get; init; } = "";

    public string Side { get; init; } = "";

    public DateTimeOffset Moment { get; init; }

    public decimal Size { get; init; }

    public int AfterMinutes { get; init; }

    public DateTimeOffset Until { get; init; }

    public double? Stop { get; init; }

    public double? Target { get; init; }

    public decimal CandleClose { get; init; }

    public string[] Labels { get; init; } = [];

    public string State { get; init; } = "";

    public double? EntryRaw { get; init; }

    public double? EntryPrice { get; init; }

    public DateTimeOffset? EntryTime { get; init; }

    public double? StopPrice { get; init; }

    public double? TargetPrice { get; init; }

    public double FundingPaid { get; init; }

    public DateTimeOffset? FundingUntil { get; init; }

    public double? LastClose { get; init; }

    public DateTimeOffset? ComputedUntil { get; init; }

    public double? NowResult { get; init; }

    public DateTimeOffset? NowAt { get; init; }

    public DateTimeOffset? ExitTime { get; init; }

    public string? ExitReason { get; init; }

    public bool Ambiguous { get; init; }

    public double? ExitPrice { get; init; }

    public double? Result { get; init; }

    public double? ResultUsdt { get; init; }

    public string? Excluded { get; init; }
}
