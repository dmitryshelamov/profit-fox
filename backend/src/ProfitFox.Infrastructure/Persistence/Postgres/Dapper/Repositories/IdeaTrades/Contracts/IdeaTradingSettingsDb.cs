namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaTrades.Contracts;

internal sealed class IdeaTradingSettingsDb
{
    public Guid? AccountId { get; init; }

    public decimal Amount { get; init; }

    public decimal MaxAmount { get; init; }

    public int MaxOpenTrades { get; init; }

    public decimal MaxTotalRisk { get; init; }

    public decimal MinRiskReward { get; init; }

    public decimal? DailyLossLimit { get; init; }

    public int? MaxSameDirection { get; init; }

    public decimal? AutosBudget { get; init; }
}