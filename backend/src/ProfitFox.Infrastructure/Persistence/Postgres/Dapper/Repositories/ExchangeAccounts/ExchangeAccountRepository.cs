using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Contracts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Converters;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Queries;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts;

internal sealed class ExchangeAccountRepository(NpgsqlDataSource dataSource) : IExchangeAccountRepository
{
    public async Task<bool> AddAsync(ExchangeAccount account, EncryptedSecret secret, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(ExchangeAccountRepositoryQueries.Insert(ExchangeAccountDbConverter.FromDomain(account), secret, cancellationToken)) == 1;
    }

    public async Task<ExchangeAccount?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        ExchangeAccountDb? account = await connection.QuerySingleOrDefaultAsync<ExchangeAccountDb>(ExchangeAccountRepositoryQueries.Account(id, cancellationToken));

        return account is null ? null : ExchangeAccountDbConverter.ToDomain(account);
    }

    public async Task<IReadOnlyList<ExchangeAccount>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return [.. (await connection.QueryAsync<ExchangeAccountDb>(ExchangeAccountRepositoryQueries.Accounts(cancellationToken))).Select(ExchangeAccountDbConverter.ToDomain)];
    }

    public async Task<EncryptedSecret?> GetSecretAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        ExchangeAccountSecretDb? secret = await connection.QuerySingleOrDefaultAsync<ExchangeAccountSecretDb>(ExchangeAccountRepositoryQueries.Secret(id, cancellationToken));

        return secret is null ? null : new EncryptedSecret(secret.Nonce, secret.Ciphertext, secret.Tag);
    }

    public async Task<bool> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(ExchangeAccountRepositoryQueries.Rename(id, name, cancellationToken)) == 1;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            return await connection.ExecuteAsync(ExchangeAccountRepositoryQueries.Delete(id, cancellationToken)) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ExchangeAccountInUseException();
        }
    }
}