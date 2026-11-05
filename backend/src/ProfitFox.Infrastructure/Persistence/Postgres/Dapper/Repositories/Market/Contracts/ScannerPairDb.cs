namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

internal sealed class ScannerPairDb
{
    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public bool Listed { get; init; }

    public DateTimeOffset? HistoryFrom { get; init; }

    public DateTimeOffset? CoveredUntil { get; init; }

    public DateTimeOffset? OiCoveredUntil { get; init; }

    public DateTimeOffset? FundingCoveredUntil { get; init; }

    public DateTimeOffset? ListedAt { get; init; }

    public decimal? TickSize { get; init; }
}