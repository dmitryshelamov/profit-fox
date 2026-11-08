using ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccount;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>An account with the bots that trade with it.</summary>
public sealed record ExchangeAccountDetailsResponse(
    Guid Id,
    string Name,
    string Exchange,
    string? ApiKey,
    long? Uid,
    string? AccountId,
    IReadOnlyList<string> Categories,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountBotResponse> Bots)
{
    public static ExchangeAccountDetailsResponse Of(GetExchangeAccountResult result)
    {
        var account = ExchangeAccountResponse.Of(result.Account);

        return new ExchangeAccountDetailsResponse(
            account.Id,
            account.Name,
            account.Exchange,
            account.ApiKey,
            account.Uid,
            account.AccountId,
            account.Categories,
            account.CreatedAt,
            [.. result.Bots.Select(bot => new AccountBotResponse(bot.Id, bot.Name))]);
    }
}