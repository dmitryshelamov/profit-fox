using System.Data;

using Dapper;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;

internal static class IdeaAutoRefusalRepositoryQueries
{
    private const string Columns = """
                                   id, idea_id AS "IdeaId", auto_id AS "AutoId", mode, created_at AS "CreatedAt", category, symbol, setup, interval, type, side,
                                   reasons, detail::text AS detail
                                   """;

    private const string InsertSql = """
                                     INSERT INTO idea_auto_refusals (id, idea_id, auto_id, mode, created_at, category, symbol, setup, interval, type, side, reasons, detail)
                                     VALUES (@Id, @IdeaId, @AutoId, @Mode, @CreatedAt, @Category, @Symbol, @Setup, @Interval, @Type, @Side, @Reasons, @Detail::jsonb)
                                     ON CONFLICT (idea_id, auto_id) DO NOTHING
                                     """;

    public static CommandDefinition Insert(IdeaAutoRefusalDb refusal, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, refusal, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition InsertMany(IEnumerable<IdeaAutoRefusalDb> refusals, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, refusals, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetPage(DateTimeOffset since, Guid? autoId, int skip, int take, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            $"""
             SELECT {Columns} FROM idea_auto_refusals
             WHERE created_at >= @Since AND (@AutoId::uuid IS NULL OR auto_id = @AutoId)
             ORDER BY created_at DESC, id
             OFFSET @Skip LIMIT @Take
             """,
            new { Since = since, AutoId = autoId, Skip = skip, Take = take },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Count(DateTimeOffset since, Guid? autoId, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "SELECT count(*)::int FROM idea_auto_refusals WHERE created_at >= @Since AND (@AutoId::uuid IS NULL OR auto_id = @AutoId)",
            new { Since = since, AutoId = autoId },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition CountByAuto(DateTimeOffset since, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            "SELECT auto_id AS \"AutoId\", count(*)::int AS \"Count\" FROM idea_auto_refusals WHERE created_at >= @Since GROUP BY auto_id",
            new { Since = since },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition DeleteBefore(DateTimeOffset before, CancellationToken cancellationToken)
    {
        return new CommandDefinition("DELETE FROM idea_auto_refusals WHERE created_at < @Before", new { Before = before }, cancellationToken: cancellationToken);
    }
}