using System.Data;

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

    // The columns of a live idea beside the ones a case shares: the price step and the exit rule it fired under.
    private const string OwnColumns = """tick, life_candles AS "LifeCandles", target_r AS "TargetR", entry_candles AS "EntryCandles" """;

    // A second open idea of a key conflicts with the partial unique index and is left out; RETURNING names the ones added.
    private const string InsertSql = """
                                     INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward, status,
                                         status_at, entered_at, exit_price, move, volume_ratio, engine_version, tick, life_candles, target_r, entry_candles)
                                     VALUES (@Id, @Category, @Symbol, @Setup, @Type, @Interval, @Side, @FiredAt, @Entry, @Stop, @Target, @RiskReward, @Status,
                                         @StatusAt, @EnteredAt, @ExitPrice, @Move, @VolumeRatio, @EngineVersion, @Tick, @LifeCandles, @TargetR, @EntryCandles)
                                     ON CONFLICT (category, symbol, setup, type, "interval", side) WHERE status IN ('awaiting_entry', 'active') DO NOTHING
                                     RETURNING id
                                     """;

    private const string UpdateSql = """
                                     UPDATE ideas SET entry = @Entry, stop = @Stop, target = @Target, risk_reward = @RiskReward, status = @Status,
                                         status_at = @StatusAt, entered_at = @EnteredAt, exit_price = @ExitPrice
                                     WHERE id = @Id
                                     """;

    internal static CommandDefinition Insert(IdeaDb idea, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, idea, transaction, cancellationToken: cancellationToken);
    }

    // Dapper runs the statement once per row.
    internal static CommandDefinition Update(IEnumerable<IdeaDb> ideas, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpdateSql, ideas, transaction, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetOpen(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT id, {Columns}, {OwnColumns} FROM ideas WHERE status IN ('awaiting_entry', 'active') ORDER BY fired_at", cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetRecent(DateTimeOffset closedSince, string? category, string? symbol, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             SELECT id, {Columns}, {OwnColumns} FROM ideas
             WHERE (status IN ('awaiting_entry', 'active') OR status_at >= @ClosedSince)
                 AND (@Category::text IS NULL OR (category = @Category AND symbol = @Symbol))
             ORDER BY fired_at DESC
             """,
            new { ClosedSince = closedSince, Category = category, Symbol = symbol },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT id, {Columns}, {OwnColumns} FROM ideas WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition DeleteClosedBefore(DateTimeOffset closedBefore, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "DELETE FROM ideas WHERE status NOT IN ('awaiting_entry', 'active') AND status_at < @ClosedBefore",
            new { ClosedBefore = closedBefore },
            cancellationToken: cancellationToken);
    }
}