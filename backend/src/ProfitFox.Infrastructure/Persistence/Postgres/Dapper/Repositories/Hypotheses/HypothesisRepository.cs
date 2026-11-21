using Dapper;

using Npgsql;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Hypotheses;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Hypotheses;

/// <summary>
///     The hypotheses in the table <c>hypothesis_versions</c> (spec hypothesis-bench «Заморозка гипотезы»): a row per version,
///     added and never changed.
/// </summary>
internal sealed class HypothesisRepository(NpgsqlDataSource database) : IHypothesisRepository
{
    public async Task<IReadOnlyList<StoredHypothesis>> LatestAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<HypothesisDb> rows = await connection.QueryAsync<HypothesisDb>(HypothesisRepositoryQueries.Latest(cancellationToken));
        return [.. rows.Select(HypothesisDbConverter.ToDomain)];
    }

    public async Task<StoredHypothesis?> FindAsync(string id, int? version, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        HypothesisDb? row = await connection.QuerySingleOrDefaultAsync<HypothesisDb>(HypothesisRepositoryQueries.Find(id, version, cancellationToken));
        return row is null ? null : HypothesisDbConverter.ToDomain(row);
    }

    public async Task<IReadOnlyList<HypothesisKey>> KeysAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        IEnumerable<HypothesisDb> rows = await connection.QueryAsync<HypothesisDb>(HypothesisRepositoryQueries.Keys(cancellationToken));
        return [.. rows.Select(HypothesisDbConverter.ToKey)];
    }

    // Two texts of one id loaded together would both take the next number: the lock of the id makes the second one see the first.
    public async Task<(int Version, bool Created)> AddAsync(string id, string hash, string text, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(HypothesisRepositoryQueries.Lock(id, transaction, cancellationToken));
        int? existing = await connection.ExecuteScalarAsync<int?>(HypothesisRepositoryQueries.VersionOfHash(id, hash, transaction, cancellationToken));
        if (existing is { } version)
        {
            return (version, false);
        }

        int next = await connection.ExecuteScalarAsync<int>(HypothesisRepositoryQueries.Insert(id, hash, text, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return (next, true);
    }
}
