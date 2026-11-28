using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Accounts;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Trades;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Trades;

/// <summary>
///     What the tests of the trades put in the test database: an account, and a trade with its entry order written as the
///     entry writes them.
/// </summary>
internal static class TradeStore
{
    public static async Task<ExchangeAccount> Account(string name = "Основной", long uid = 104857321, string apiKey = "key-main")
    {
        var account = new ExchangeAccount(Guid.NewGuid(), name, uid, apiKey, DateTimeOffset.UtcNow.AddTicks(-(DateTimeOffset.UtcNow.Ticks % TimeSpan.TicksPerMicrosecond)));
        await BenchServices.Get<IExchangeAccountRepository>().AddAsync(account, new ProtectedSecret([1], [2], [3]), CancellationToken.None);
        return account;
    }

    /// <summary>
    ///     A trade of the account written with its entry order; null when the store refused it.
    /// </summary>
    public static async Task<Trade?> Open(Trade trade, int maxOpen = 100, decimal maxTotal = 100000m)
    {
        TradeOrder entry = TradeGenerator.Order(trade, 1, TradeOrderPurpose.Entry);
        return await BenchServices.Get<ITradeRepository>().OpenAsync(trade, entry, maxOpen, maxTotal, CancellationToken.None) is null ? trade : null;
    }
}
