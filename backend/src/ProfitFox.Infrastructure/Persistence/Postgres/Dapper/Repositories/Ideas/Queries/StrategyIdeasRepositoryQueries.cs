namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

/// <summary>
///     The statements on <c>strategy_ideas</c>, a row per strategy that has its ideas set.
/// </summary>
internal static class StrategyIdeasRepositoryQueries
{
    public const string Find = """SELECT strategy_id AS "StrategyId", enabled AS "Enabled", timeframes AS "Timeframes" FROM strategy_ideas WHERE strategy_id = @StrategyId""";

    public const string All = """SELECT strategy_id AS "StrategyId", enabled AS "Enabled", timeframes AS "Timeframes" FROM strategy_ideas""";

    public const string Set =
        """
        INSERT INTO strategy_ideas (strategy_id, enabled, timeframes, updated_at) VALUES (@StrategyId, @Enabled, @Timeframes, now())
        ON CONFLICT (strategy_id) DO UPDATE SET enabled = EXCLUDED.enabled, timeframes = EXCLUDED.timeframes, updated_at = now()
        """;
}
