using ProfitFox.Domain.Entities.ExchangeAccounts;

namespace ProfitFox.Presentation.Contracts.Bots;

/// <summary>The exchange account a bot trades with.</summary>
public sealed record BotAccountResponse(Guid Id, string Name)
{
    public static BotAccountResponse? Of(ExchangeAccount? account)
    {
        return account is null ? null : new BotAccountResponse(account.Id, account.Name);
    }
}