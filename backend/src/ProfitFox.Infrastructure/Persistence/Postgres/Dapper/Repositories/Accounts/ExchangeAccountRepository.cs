using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Accounts;

/// <summary>
///     The accounts of the exchange in <c>exchange_accounts</c> (change add-exchange-accounts, design.md, decision 3). A taken
///     name or UID is told by the unique index it breaks; the encrypted secret is read only by <see cref="SecretAsync" />.
/// </summary>
internal sealed class ExchangeAccountRepository(NpgsqlDataSource database) : IExchangeAccountRepository
{
    private const string UniqueViolation = "23505";
    private const string NameIndex = "exchange_accounts_name_ux";
    private const string UidIndex = "exchange_accounts_uid_ux";

    private const string Columns = """id AS "Id", name AS "Name", uid AS "Uid", api_key AS "ApiKey", created_at AS "CreatedAt", margin_mode AS "MarginMode", feed AS "Feed", feed_changed_at AS "FeedChangedAt", fills_cursor_at AS "FillsCursorAt" """;

    public async Task<IReadOnlyList<ExchangeAccount>> AllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<Row>(new CommandDefinition($"SELECT {Columns} FROM exchange_accounts ORDER BY created_at, id", cancellationToken: cancellationToken))).Select(row => row.ToDomain())];
    }

    public async Task<ExchangeAccount?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        Row? row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition($"SELECT {Columns} FROM exchange_accounts WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<bool> AddAsync(ExchangeAccount account, ProtectedSecret secret, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO exchange_accounts (id, name, api_key, secret_nonce, secret_ciphertext, secret_tag, uid, created_at, margin_mode)
                VALUES (@Id, @Name, @ApiKey, @Nonce, @Ciphertext, @Tag, @Uid, @CreatedAt, @MarginMode)
                """,
                new { account.Id, account.Name, account.ApiKey, secret.Nonce, secret.Ciphertext, secret.Tag, account.Uid, account.CreatedAt, account.MarginMode },
                cancellationToken: cancellationToken));
            return true;
        }
        catch (PostgresException exception) when (Taken(exception))
        {
            return false;
        }
    }

    public async Task<bool> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("UPDATE exchange_accounts SET name = @Name WHERE id = @Id", new { Id = id, Name = name }, cancellationToken: cancellationToken));
            return true;
        }
        catch (PostgresException exception) when (Taken(exception))
        {
            return false;
        }
    }

    public async Task<bool> SetMarginModeAsync(Guid id, string? marginMode, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition("UPDATE exchange_accounts SET margin_mode = @MarginMode WHERE id = @Id", new { Id = id, MarginMode = marginMode }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task SetFeedAsync(Guid id, string? feed, DateTimeOffset at, string worker, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TraderFence.CheckAsync(connection, transaction, worker, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE exchange_accounts SET feed = @Feed, feed_changed_at = @At WHERE id = @Id AND feed IS DISTINCT FROM @Feed",
            new { Id = id, Feed = feed, At = at },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetFillsCursorAsync(Guid id, DateTimeOffset at, string worker, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TraderFence.CheckAsync(connection, transaction, worker, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("UPDATE exchange_accounts SET fills_cursor_at = @At WHERE id = @Id", new { Id = id, At = at }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition("DELETE FROM exchange_accounts WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool?> DeleteIdleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@Key)", new { Key = TraderFence.EntryLock }, transaction, cancellationToken: cancellationToken));
        int removed = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM exchange_accounts a
            WHERE a.id = @Id AND NOT EXISTS (SELECT 1 FROM trades t WHERE t.account_id = a.id AND t.state IN ('pending', 'open', 'closing', 'error'))
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        bool? answer = removed == 1
            ? true
            : await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM exchange_accounts WHERE id = @Id)", new { Id = id }, transaction, cancellationToken: cancellationToken))
                ? null
                : false;
        await transaction.CommitAsync(cancellationToken);
        return answer;
    }

    public async Task<ProtectedSecret?> SecretAsync(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        SecretRow? row = await connection.QuerySingleOrDefaultAsync<SecretRow>(new CommandDefinition(
            """SELECT secret_nonce AS "Nonce", secret_ciphertext AS "Ciphertext", secret_tag AS "Tag" FROM exchange_accounts WHERE id = @Id""",
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : new ProtectedSecret(row.Nonce, row.Ciphertext, row.Tag);
    }

    private static bool Taken(PostgresException exception)
    {
        return exception.SqlState == UniqueViolation && exception.ConstraintName is NameIndex or UidIndex;
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = "";

        public long Uid { get; init; }

        public string ApiKey { get; init; } = "";

        public DateTimeOffset CreatedAt { get; init; }

        public string? MarginMode { get; init; }

        public string? Feed { get; init; }

        public DateTimeOffset? FeedChangedAt { get; init; }

        public DateTimeOffset? FillsCursorAt { get; init; }

        public ExchangeAccount ToDomain()
        {
            return new ExchangeAccount(Id, Name, Uid, ApiKey, CreatedAt, MarginMode, Feed, FeedChangedAt, FillsCursorAt);
        }
    }

    private sealed class SecretRow
    {
        public byte[] Nonce { get; init; } = [];

        public byte[] Ciphertext { get; init; } = [];

        public byte[] Tag { get; init; } = [];
    }
}
