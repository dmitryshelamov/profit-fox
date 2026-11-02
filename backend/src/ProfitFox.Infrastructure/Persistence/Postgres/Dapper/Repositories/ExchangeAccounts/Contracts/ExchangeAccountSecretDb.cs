namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.ExchangeAccounts.Contracts;

internal sealed class ExchangeAccountSecretDb
{
    public required byte[] Nonce { get; init; }

    public required byte[] Ciphertext { get; init; }

    public required byte[] Tag { get; init; }
}