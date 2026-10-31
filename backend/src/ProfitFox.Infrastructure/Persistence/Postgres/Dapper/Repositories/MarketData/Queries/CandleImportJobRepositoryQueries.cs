using Dapper;

using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData.Queries;

internal static class CandleImportJobRepositoryQueries
{
    private const string InsertSql = """
                                     INSERT INTO candle_import_jobs
                                         (id, category, symbol, "interval", "from", "to", status, imported_candles, error, created_at, started_at, finished_at)
                                     VALUES
                                         (@Id, @Category, @Symbol, @Interval, @From, @To, @Status, @ImportedCandles, @Error, @CreatedAt, @StartedAt, @FinishedAt)
                                     """;

    private const string GetSql = """
                                  SELECT id, category, symbol, "interval", "from" AS "From", "to" AS "To", status, imported_candles AS "ImportedCandles", error,
                                      created_at AS "CreatedAt", started_at AS "StartedAt", finished_at AS "FinishedAt",
                                      stalled_restarts AS "StalledRestarts"
                                  FROM candle_import_jobs
                                  WHERE id = @Id
                                  """;

    private const string GetUnfinishedSql = """
                                            SELECT id, category, symbol, "interval", "from" AS "From", "to" AS "To", status, imported_candles AS "ImportedCandles", error,
                                                created_at AS "CreatedAt", started_at AS "StartedAt", finished_at AS "FinishedAt",
                                                stalled_restarts AS "StalledRestarts"
                                            FROM candle_import_jobs
                                            WHERE status IN (@Pending, @Running)
                                            ORDER BY created_at
                                            """;

    private const string UpdateSql = """
                                     UPDATE candle_import_jobs
                                     SET status = @Status, imported_candles = @ImportedCandles, error = @Error, started_at = @StartedAt, finished_at = @FinishedAt,
                                         stalled_restarts = @StalledRestarts
                                     WHERE id = @Id
                                     """;

    internal static CommandDefinition Insert(CandleImportJobDb job, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, job, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Get(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition(GetSql, new { Id = id }, cancellationToken: cancellationToken);
    }

    internal static CommandDefinition GetUnfinished(CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            GetUnfinishedSql,
            new { Pending = nameof(CandleImportStatus.Pending), Running = nameof(CandleImportStatus.Running) },
            cancellationToken: cancellationToken);
    }

    internal static CommandDefinition Update(CandleImportJobDb job, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpdateSql, job, cancellationToken: cancellationToken);
    }
}