namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

/// <summary>
///     The enabled strategies and labels with the latest version numbers, in one command of two answers (task 18.13).
/// </summary>
internal static class IdeaSourceRepositoryQueries
{
    public const string Sources =
        """
        SELECT s.id AS "StrategyId", s.name AS "Name", s.version AS "Version", i.timeframes AS "Timeframes"
        FROM strategy_ideas i JOIN strategies s ON s.id = i.strategy_id
        WHERE i.enabled AND cardinality(i.timeframes) > 0;
        SELECT g.id AS "SignalId", g.name AS "Name", g.version AS "Version", l.text AS "Text", l.timeframe AS "Timeframe"
        FROM signal_labels l JOIN signals g ON g.id = l.signal_id
        WHERE l.enabled;
        """;
}
