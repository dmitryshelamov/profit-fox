namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Backtests.Contracts;

/// <summary>
///     A row of <c>backtest_sets</c>: the numbers are null until the backtest is done.
/// </summary>
internal sealed record BacktestSetDb
{
    public string BacktestId { get; init; } = "";

    public int Number { get; init; }

    public string Timeframe { get; init; } = "";

    public string Values { get; init; } = "[]";

    public string? Numbers { get; init; }
}
