using Npgsql;

using ProfitFox.Application.Transactions;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Transactions;

internal sealed class PostgresTransactionExecutor(NpgsqlDataSource dataSource, PostgresTransactionAccessor accessor) : ITransactionExecutor
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        EnsureNoTransaction();

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        accessor.Set(new PostgresTransaction(connection, transaction));

        try
        {
            T result = await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        finally
        {
            accessor.Set(null);
        }
    }

    public void EnsureNoTransaction()
    {
        if (accessor.HasCurrent)
        {
            throw new InvalidOperationException("A unit of work runs.");
        }
    }
}