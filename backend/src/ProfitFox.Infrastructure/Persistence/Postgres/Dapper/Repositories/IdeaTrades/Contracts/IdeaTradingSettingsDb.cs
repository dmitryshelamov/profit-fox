namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

internal sealed class IdeaTradingSettingsDb
{
    public Guid? AccountId { get; init; }

    public decimal Risk { get; init; }

    public decimal MaxAmount { get; init; }

    public int MaxOpenTrades { get; init; }

    public decimal MaxTotalRisk { get; init; }

    public decimal MinRiskReward { get; init; }
}