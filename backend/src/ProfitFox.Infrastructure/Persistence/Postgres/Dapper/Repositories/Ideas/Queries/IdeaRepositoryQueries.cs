using Dapper;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

internal static class IdeaRepositoryQueries
{
    internal const string Columns = """
                                    category, symbol, setup, type, "interval", side, fired_at AS "FiredAt", entry, stop, target, risk_reward AS "RiskReward",
                                    status, status_at AS "StatusAt", entered_at AS "EnteredAt", exit_price AS "ExitPrice", move, volume_ratio AS "VolumeRatio",
                                    engine_version AS "EngineVersion"
                                    """;

    // A second open idea of a key conflicts with the partial unique index and is left out; RETURNING names the ones added.
    private const string InsertSql = """
                                     INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward, status,
                                         status_at, entered_at, exit_price, move, volume_ratio, engine_version)
                                     VALUES (@Id, @Category, @Symbol, @Setup, @Type, @Interval, @Side, @FiredAt, @Entry, @Stop, @Target, @RiskReward, @Status,
                                         @StatusAt, @EnteredAt, @ExitPrice, @Move, @VolumeRatio, @EngineVersion)
                                     ON CONFLICT (category, symbol, setup, type, "interval", side) WHERE status IN ('awaiting_entry', 'active') DO NOTHING
                                     RETURNING id
                                     """;

    private const string UpdateSql = """
                                     UPDATE ideas SET entry = @Entry, stop = @Stop, target = @Target, risk_reward = @RiskReward, status = @Status,
                                         status_at = @StatusAt, entered_at = @EnteredAt, exit_price = @ExitPrice
                                     WHERE id = @Id
                                     """;

    internal static CommandDefinition Insert(IdeaDb idea, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, idea, cancellationToken: cancellationToken);
    }

    // Dapper runs the statement once per row.
    internal static CommandDefinition Update(IEnumerable<IdeaDb> ideas, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpdateSql, ideas, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetOpen(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT id, {Columns} FROM ideas WHERE status IN ('awaiting_entry', 'active') ORDER BY fired_at", cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetRecent(DateTimeOffset closedSince, string? category, string? symbol, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             SELECT id, {Columns} FROM ideas
             WHERE (status IN ('awaiting_entry', 'active') OR status_at >= @ClosedSince)
                 AND (@Category::text IS NULL OR (category = @Category AND symbol = @Symbol))
             ORDER BY fired_at DESC
             """,
            new { ClosedSince = closedSince, Category = category, Symbol = symbol },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT id, {Columns} FROM ideas WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }
}