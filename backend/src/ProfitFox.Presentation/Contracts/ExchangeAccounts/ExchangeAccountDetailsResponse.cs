using ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccount;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>An account with the bots that trade with it.</summary>
public sealed record ExchangeAccountDetailsResponse(
    Guid Id,
    string Name,
    string Environment,
    string ApiKey,
    long Uid,
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
            account.Environment,
            account.ApiKey,
            account.Uid,
            account.Categories,
            account.CreatedAt,
            [.. result.Bots.Select(bot => new AccountBotResponse(bot.Id, bot.Name))]);
    }
}