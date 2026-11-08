using ProfitFox.Application.Ports.ExchangeAccounts;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>The accounts a T-Invest token opens and the portfolio of one of them, in roubles; the token is the one the user sent or the one of the stored account.</summary>
internal sealed class TInvestAccountClient(TInvestCall call) : IBrokerAccountReader
{
    private const string Roubles = "RUB";

    public async Task<IReadOnlyList<BrokerAccount>> ListAccountsAsync(string token, CancellationToken cancellationToken)
    {
        GetAccountsResponse response = await call.OnceAsync(token, invoker => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest()), cancellationToken);

        return [.. response.Accounts.Select(account => new BrokerAccount(account.Id, account.Name, Type(account.Type), Status(account.Status), Access(account.AccessLevel)))];
    }

    /// <summary>
    ///     The total of the portfolio and every position with a non-zero quantity, the most valuable first: a currency by its ISO code (the
    ///     first three letters of its ticker, <c>USD000UTSTOM</c> → <c>USD</c>) worth its quantity at the current price in roubles, the
    ///     roubles themselves worth their quantity, always as a row. The roubles of the answer count every currency and are not shown.
    /// </summary>
    public async Task<AccountBalance> GetPortfolioAsync(string token, string accountId, CancellationToken cancellationToken)
    {
        var request = new PortfolioRequest { AccountId = accountId, Currency = PortfolioRequest.Types.CurrencyRequest.Rub };
        PortfolioResponse portfolio = await call.OnceAsync(token, invoker => new OperationsService.OperationsServiceClient(invoker).GetPortfolioAsync(request), cancellationToken);
        List<CoinBalance> positions = [.. portfolio.Positions.Where(position => position.Quantity.ToDecimal() != 0m).Select(Row)];

        if (positions.All(position => position.Coin != Roubles))
        {
            positions.Add(new CoinBalance(Roubles, 0m, 0m));
        }

        return new AccountBalance(Roubles, portfolio.TotalAmountPortfolio?.ToDecimal() ?? 0m, [.. positions.OrderByDescending(coin => coin.Value)]);
    }

    private static CoinBalance Row(PortfolioPosition position)
    {
        decimal quantity = position.Quantity.ToDecimal();

        if (position.InstrumentType != "currency")
        {
            return new CoinBalance(position.Ticker, quantity, quantity * (position.CurrentPrice?.ToDecimal() ?? 0m));
        }

        string code = position.Ticker.Length >= 3 ? position.Ticker[..3].ToUpperInvariant() : position.Ticker;

        return new CoinBalance(code, quantity, code == Roubles ? quantity : quantity * (position.CurrentPrice?.ToDecimal() ?? 0m));
    }

    private static BrokerAccountType Type(AccountType type)
    {
        return type switch
        {
            AccountType.Tinkoff => BrokerAccountType.Brokerage,
            AccountType.TinkoffIis => BrokerAccountType.Iis,
            _ => BrokerAccountType.Other
        };
    }

    private static BrokerAccountStatus Status(AccountStatus status)
    {
        return status switch
        {
            AccountStatus.Open => BrokerAccountStatus.Open,
            AccountStatus.New => BrokerAccountStatus.New,
            _ => BrokerAccountStatus.Closed
        };
    }

    private static BrokerAccountAccess Access(AccessLevel access)
    {
        return access switch
        {
            AccessLevel.AccountAccessLevelFullAccess => BrokerAccountAccess.Full,
            AccessLevel.AccountAccessLevelReadOnly => BrokerAccountAccess.ReadOnly,
            _ => BrokerAccountAccess.None
        };
    }
}