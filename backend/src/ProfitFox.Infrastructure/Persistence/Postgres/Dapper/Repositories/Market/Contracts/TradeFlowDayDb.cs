namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

/// <summary>A row of <c>trade_flow_days</c>.</summary>
internal sealed class TradeFlowDayDb
{
    public string Category { get; init; } = null!;

    public string Symbol { get; init; } = null!;

    public DateTimeOffset Day { get; init; }

    public string Status { get; init; } = null!;

    public DateTimeOffset At { get; init; }
}