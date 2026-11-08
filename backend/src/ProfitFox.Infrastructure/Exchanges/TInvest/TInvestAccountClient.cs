using ProfitFox.Application.Ports.ExchangeAccounts;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>The accounts a T-Invest token opens and the portfolio of one of them, in roubles; the token is the one the user sent or the one of the stored account.</summary>
internal sealed class TInvestAccountClient(TInvestCall call) : IBrokerAccountReader
{
    public async Task<IReadOnlyList<BrokerAccount>> ListAccountsAsync(string token, CancellationToken cancellationToken)
    {
        GetAccountsResponse response = await call.OnceAsync(token, invoker => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest()), cancellationToken);

        return [.. response.Accounts.Select(account => new BrokerAccount(account.Id, account.Name, Type(account.Type), Status(account.Status), Access(account.AccessLevel)))];
    }

    /// <summary>The total of the portfolio, the roubles on the account as one row and every other position with a non-zero quantity, the most valuable first.</summary>
    public async Task<AccountBalance> GetPortfolioAsync(string token, string accountId, CancellationToken cancellationToken)
    {
        var request = new PortfolioRequest { AccountId = accountId, Currency = PortfolioRequest.Types.CurrencyRequest.Rub };
        PortfolioResponse portfolio = await call.OnceAsync(token, invoker => new OperationsService.OperationsServiceClient(invoker).GetPortfolioAsync(request), cancellationToken);
        decimal roubles = portfolio.TotalAmountCurrencies?.ToDecimal() ?? 0m;
        IEnumerable<CoinBalance> positions = portfolio.Positions
            .Where(position => position.InstrumentType != "currency" && position.Quantity.ToDecimal() != 0m)
            .Select(position => new CoinBalance(position.Ticker, position.Quantity.ToDecimal(), position.Quantity.ToDecimal() * (position.CurrentPrice?.ToDecimal() ?? 0m)));

        return new AccountBalance(
            "RUB",
            portfolio.TotalAmountPortfolio?.ToDecimal() ?? 0m,
            [.. positions.Append(new CoinBalance("RUB", roubles, roubles)).OrderByDescending(coin => coin.Value)]);
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