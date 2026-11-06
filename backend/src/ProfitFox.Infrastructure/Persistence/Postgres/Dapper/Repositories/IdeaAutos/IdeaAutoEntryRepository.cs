using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos.Queries;
using ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.IdeaAutos;

internal sealed class IdeaAutoEntryRepository(NpgsqlDataSource dataSource, PostgresTransactionAccessor accessor) : IIdeaAutoEntryRepository
{
    public async Task<IAsyncDisposable?> TryLockEntriesAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        if (await connection.ExecuteScalarAsync<bool>(IdeaAutoEntryRepositoryQueries.TryLock(cancellationToken)))
        {
            return new EntriesLock(connection);
        }

        await connection.DisposeAsync();

        return null;
    }

    public async Task<IdeaAutoEntry?> GetNextAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IdeaAutoEntryDb? row = await connection.QuerySingleOrDefaultAsync<IdeaAutoEntryDb>(IdeaAutoEntryRepositoryQueries.GetNext(cancellationToken));

        return row is null ? null : IdeaAutoDbConverter.ToDomain(row);
    }

    public async Task<IdeaAutoEntry?> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        IdeaAutoEntryDb? row = await current.Connection.QuerySingleOrDefaultAsync<IdeaAutoEntryDb>(IdeaAutoEntryRepositoryQueries.Lock(id, current.Transaction, cancellationToken));

        return row is null ? null : IdeaAutoDbConverter.ToDomain(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        PostgresTransaction current = accessor.Current;
        await current.Connection.ExecuteAsync(IdeaAutoEntryRepositoryQueries.Delete(id, current.Transaction, cancellationToken));
    }

    public async Task<int> ExpireAsync(DateTimeOffset olderThan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(IdeaAutoEntryRepositoryQueries.Expire(olderThan, now, cancellationToken));
    }

    // The lock belongs to the connection: it goes with the unlock, and with the connection if that fails.
    private sealed class EntriesLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await connection.ExecuteScalarAsync(IdeaAutoEntryRepositoryQueries.Unlock(CancellationToken.None));
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}