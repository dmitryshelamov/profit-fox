namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

internal sealed class MarketSnapshotRowDb
{
    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public DateTimeOffset ComputedAt { get; init; }

    public decimal? Price { get; init; }

    public decimal? Change5m { get; init; }

    public decimal? Change15m { get; init; }

    public decimal? Change1h { get; init; }

    public decimal? Change4h { get; init; }

    public decimal? Change24h { get; init; }

    public decimal? Turnover1h { get; init; }

    public decimal? Turnover24h { get; init; }

    public decimal? OpenInterest { get; init; }

    public decimal? OpenInterestChange1h { get; init; }

    public decimal? OpenInterestChange24h { get; init; }

    public decimal? FundingRate { get; init; }

    public decimal? Spread { get; init; }

    public bool Incomplete { get; init; }
}