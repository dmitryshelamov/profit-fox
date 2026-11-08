using Dapper;

using ProfitFox.Domain.ValueObjects.ExchangeAccounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Contracts;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Queries;

internal static class ExchangeAccountRepositoryQueries
{
    // The secret columns are left out: only GetSecret reads them. A conflict is the uid of a Bybit account or the account number of
    // a T-Invest account stored already (the partial unique indexes of the table).
    private const string SelectSql = """
                                     SELECT id, name, exchange, api_key AS "ApiKey", uid, account_id AS "AccountId", categories, created_at AS "CreatedAt"
                                     FROM exchange_accounts
                                     """;

    private const string InsertSql = """
                                     INSERT INTO exchange_accounts (id, name, exchange, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, account_id, categories, created_at)
                                     VALUES (@Id, @Name, @Exchange, @ApiKey, @Nonce, @Ciphertext, @Tag, @Uid, @AccountId, @Categories, @CreatedAt)
                                     ON CONFLICT DO NOTHING
                                     """;

    public static CommandDefinition Insert(ExchangeAccountDb account, EncryptedSecret secret, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertSql,
            new
            {
                account.Id,
                account.Name,
                account.Exchange,
                account.ApiKey,
                secret.Nonce,
                secret.Ciphertext,
                secret.Tag,
                account.Uid,
                account.AccountId,
                account.Categories,
                account.CreatedAt
            },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Account(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectSql} WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }

    // The id breaks ties so the order is stable.
    public static CommandDefinition Accounts(CancellationToken cancellationToken)
    {
        return new CommandDefinition($"{SelectSql} ORDER BY name, id", cancellationToken: cancellationToken);
    }

    public static CommandDefinition Secret(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            """SELECT secret_nonce AS "Nonce", secret_ciphertext AS "Ciphertext", secret_tag AS "Tag" FROM exchange_accounts WHERE id = @Id""",
            new { Id = id },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition Rename(Guid id, string name, CancellationToken cancellationToken)
    {
        return new CommandDefinition("UPDATE exchange_accounts SET name = @Name WHERE id = @Id", new { Id = id, Name = name }, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Delete(Guid id, CancellationToken cancellationToken)
    {
        return new CommandDefinition("DELETE FROM exchange_accounts WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken);
    }
}