using System.Data;

using Dapper;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Queries;

/// <summary>
///     The statements on <c>hypothesis_versions</c>: a row per version of a hypothesis, added and never changed.
/// </summary>
internal static class HypothesisRepositoryQueries
{
    private const string Columns = """hypothesis_id AS "Id", version AS "Version", hash AS "Hash", text AS "Text", created_at AS "CreatedAt" """;

    private const string LatestSql =
        $"""
         SELECT DISTINCT ON (hypothesis_id) {Columns}
         FROM hypothesis_versions
         ORDER BY hypothesis_id, version DESC
         """;

    private const string FindSql =
        $"""
         SELECT {Columns}
         FROM hypothesis_versions
         WHERE hypothesis_id = @Id AND (@Version::integer IS NULL OR version = @Version)
         ORDER BY version DESC
         LIMIT 1
         """;

    private const string KeysSql = """SELECT hypothesis_id AS "Id", version AS "Version", hash AS "Hash" FROM hypothesis_versions ORDER BY hypothesis_id, version""";

    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@Key, 0))";

    private const string VersionOfHashSql = "SELECT version FROM hypothesis_versions WHERE hypothesis_id = @Id AND hash = @Hash";

    private const string InsertSql =
        """
        INSERT INTO hypothesis_versions (hypothesis_id, version, hash, text)
        SELECT @Id, COALESCE(max(version), 0) + 1, @Hash, @Text FROM hypothesis_versions WHERE hypothesis_id = @Id
        RETURNING version
        """;

    public static CommandDefinition Latest(CancellationToken cancellationToken)
    {
        return new CommandDefinition(LatestSql, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Find(string id, int? version, CancellationToken cancellationToken)
    {
        return new CommandDefinition(FindSql, new { Id = id, Version = version }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Keys(CancellationToken cancellationToken)
    {
        return new CommandDefinition(KeysSql, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The lock of the id <paramref name="id" /> to the end of the transaction.
    /// </summary>
    public static CommandDefinition Lock(string id, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(LockSql, new { Key = $"hypothesis:{id}" }, transaction, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The version of the hypothesis <paramref name="id" /> whose text has the hash <paramref name="hash" />, if there is one.
    /// </summary>
    public static CommandDefinition VersionOfHash(string id, string hash, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(VersionOfHashSql, new { Id = id, Hash = hash }, transaction, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Adds the next version of the hypothesis <paramref name="id" /> and gives its number.
    /// </summary>
    public static CommandDefinition Insert(string id, string hash, string text, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(InsertSql, new { Id = id, Hash = hash, Text = text }, transaction, cancellationToken: cancellationToken);
    }
}
