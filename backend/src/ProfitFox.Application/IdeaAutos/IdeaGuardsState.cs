using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos;

/// <summary>
///     The guards over the real trades as they stand: the open trades against their limit, the risk of the open trades against its limit, the daily stop
///     (the sum of the day, its limit, when it was reached and when the autos enter again), and the open longs and shorts against the limit of one side.
///     and what the open real trades of the autos take of the budget of all the autos. A limit that is off is none.
/// </summary>
public sealed record IdeaGuardsState(
    int OpenTrades,
    int MaxOpenTrades,
    decimal OpenRisk,
    decimal MaxTotalRisk,
    DailyLossState DailyLoss,
    int OpenLongs,
    int OpenShorts,
    int? MaxSameDirection,
    decimal AutosBudgetUsed,
    decimal? AutosBudget);