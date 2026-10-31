namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

internal sealed class CandleDb
{
    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string Interval { get; init; }

    public DateTimeOffset OpenTime { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }

    public decimal Turnover { get; init; }
}