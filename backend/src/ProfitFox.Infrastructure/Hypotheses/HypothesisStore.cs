using Dapper;
using Npgsql;
using ProfitFox.Application.Hypotheses;

namespace ProfitFox.Infrastructure.Hypotheses;

/// <summary>
///     The hypotheses in the table <c>hypothesis_versions</c> (spec hypothesis-bench «Заморозка гипотезы»): a row per version,
///     added and never changed.
/// </summary>
public sealed class HypothesisStore(NpgsqlDataSource database) : IHypothesisStore
{
    private const string Columns = """hypothesis_id AS "Id", version AS "Version", hash AS "Hash", text AS "Text", created_at AS "CreatedAt" """;

    public async Task<IReadOnlyList<StoredHypothesis>> Latest(CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        IEnumerable<HypothesisRow> rows = await connection.QueryAsync<HypothesisRow>(new CommandDefinition(
            $"""
             SELECT DISTINCT ON (hypothesis_id) {Columns}
             FROM hypothesis_versions
             ORDER BY hypothesis_id, version DESC
             """,
            cancellationToken: cancellation));
        return [.. rows.Select(Stored)];
    }

    public async Task<StoredHypothesis?> Find(string id, int? version, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        HypothesisRow? row = await connection.QuerySingleOrDefaultAsync<HypothesisRow>(new CommandDefinition(
            $"""
             SELECT {Columns}
             FROM hypothesis_versions
             WHERE hypothesis_id = @Id AND (@Version::integer IS NULL OR version = @Version)
             ORDER BY version DESC
             LIMIT 1
             """,
            new { Id = id, Version = version },
            cancellationToken: cancellation));
        return row is null ? null : Stored(row);
    }

    public async Task<IReadOnlyList<HypothesisKey>> Keys(CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        IEnumerable<HypothesisRow> rows = await connection.QueryAsync<HypothesisRow>(new CommandDefinition(
            """SELECT hypothesis_id AS "Id", version AS "Version", hash AS "Hash" FROM hypothesis_versions ORDER BY hypothesis_id, version""",
            cancellationToken: cancellation));
        return [.. rows.Select(row => new HypothesisKey(row.Id, row.Version, row.Hash))];
    }

    // Two texts of one id loaded together would both take the next number: the lock of the id makes the second one see the first.
    public async Task<(int Version, bool Created)> Add(string id, string hash, string text, CancellationToken cancellation)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellation);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellation);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@Key, 0))",
            new { Key = $"hypothesis:{id}" },
            transaction,
            cancellationToken: cancellation));
        int? existing = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT version FROM hypothesis_versions WHERE hypothesis_id = @Id AND hash = @Hash",
            new { Id = id, Hash = hash },
            transaction,
            cancellationToken: cancellation));
        if (existing is { } version)
        {
            return (version, false);
        }

        int next = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO hypothesis_versions (hypothesis_id, version, hash, text)
            SELECT @Id, COALESCE(max(version), 0) + 1, @Hash, @Text FROM hypothesis_versions WHERE hypothesis_id = @Id
            RETURNING version
            """,
            new { Id = id, Hash = hash, Text = text },
            transaction,
            cancellationToken: cancellation));
        await transaction.CommitAsync(cancellation);
        return (next, true);
    }

    private static StoredHypothesis Stored(HypothesisRow row)
    {
        return new StoredHypothesis(row.Id, row.Version, row.Hash, row.Text, row.CreatedAt);
    }
}
