using System.Data;

using Dapper;

using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;

internal static class IdeaAutoEntryRepositoryQueries
{
    private const string Columns = """
                                   id, idea_id AS "IdeaId", auto_id AS "AutoId", account_id AS "AccountId", amount, min_risk_reward AS "MinRiskReward",
                                   rank, fired, minute, created_at AS "CreatedAt"
                                   """;

    public static CommandDefinition TryLock(CancellationToken cancellationToken)
    {
        return new CommandDefinition("SELECT pg_try_advisory_lock(hashtextextended('idea-autos:entries', 0))", cancellationToken: cancellationToken);
    }

    public static CommandDefinition Unlock(CancellationToken cancellationToken)
    {
        return new CommandDefinition("SELECT pg_advisory_unlock(hashtextextended('idea-autos:entries', 0))", cancellationToken: cancellationToken);
    }

    public static CommandDefinition GetNext(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_auto_entries ORDER BY minute, rank, fired, id LIMIT 1", cancellationToken: cancellationToken);
    }

    public static CommandDefinition Lock(Guid id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"SELECT {Columns} FROM idea_auto_entries WHERE id = @Id FOR UPDATE", new { Id = id }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Delete(Guid id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition("DELETE FROM idea_auto_entries WHERE id = @Id", new { Id = id }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Insert(IEnumerable<IdeaAutoEntryDb> entries, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            INSERT INTO idea_auto_entries (id, idea_id, auto_id, account_id, amount, min_risk_reward, rank, fired, minute, created_at)
            VALUES (@Id, @IdeaId, @AutoId, @AccountId, @Amount, @MinRiskReward, @Rank, @Fired, @Minute, @CreatedAt)
            """,
            entries,
            transaction,
            cancellationToken: cancellationToken);
    }

    // A request nobody took becomes the refusal «Не успел войти»; a row a Worker holds is skipped.
    public static CommandDefinition Expire(DateTimeOffset olderThan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """
            WITH due AS (
                SELECT id FROM idea_auto_entries WHERE created_at < @OlderThan ORDER BY id FOR UPDATE SKIP LOCKED),
            gone AS (
                DELETE FROM idea_auto_entries entry USING due WHERE entry.id = due.id RETURNING entry.idea_id, entry.auto_id)
            INSERT INTO idea_auto_refusals (id, idea_id, auto_id, mode, created_at, category, symbol, setup, interval, type, side, reasons, detail)
            SELECT gen_random_uuid(), gone.idea_id, gone.auto_id, 'live', @Now, idea.category, idea.symbol, idea.setup, idea."interval", idea.type, idea.side, ARRAY['late'], NULL
            FROM gone JOIN ideas idea ON idea.id = gone.idea_id
            ON CONFLICT (idea_id, auto_id) DO NOTHING
            """,
            new { OlderThan = olderThan, Now = now },
            cancellationToken: cancellationToken);
    }
}