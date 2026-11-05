namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

internal sealed class MinuteAggregateDb
{
    public DateTimeOffset OpenTime { get; init; }

    public DateTimeOffset FirstMinute { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }
}