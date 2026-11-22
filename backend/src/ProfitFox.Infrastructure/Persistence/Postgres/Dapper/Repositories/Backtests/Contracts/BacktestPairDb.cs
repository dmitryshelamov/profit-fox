namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

/// <summary>
///     A row of <c>backtest_set_pairs</c>.
/// </summary>
internal sealed record BacktestPairDb
{
    public string BacktestId { get; init; } = "";

    public int Number { get; init; }

    public string Pair { get; init; } = "";

    public string Numbers { get; init; } = "{}";
}
