namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

/// <summary>A row of <c>open_interest</c> or <c>funding_rates</c>: the time of a point and its value.</summary>
internal sealed class MarketPointDb
{
    public DateTimeOffset Time { get; init; }

    public decimal Value { get; init; }
}