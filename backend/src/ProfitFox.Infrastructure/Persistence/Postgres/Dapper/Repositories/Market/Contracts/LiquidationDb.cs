namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

/// <summary>A row of <c>liquidations</c> without its pair.</summary>
internal sealed class LiquidationDb
{
    public DateTimeOffset Time { get; init; }

    public string Side { get; init; } = null!;

    public decimal Price { get; init; }

    public decimal Quantity { get; init; }
}