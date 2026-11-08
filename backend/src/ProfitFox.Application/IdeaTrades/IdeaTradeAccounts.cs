using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>The accounts the trades by ideas can go through: those of Bybit, until the ideas of MOEX come (change add-moex-ideas).</summary>
public static class IdeaTradeAccounts
{
    public const string NotBybit = "The trades by ideas go through a Bybit account.";

    public static bool CanTrade(ExchangeAccount account)
    {
        return account.Exchange == Exchange.Bybit;
    }
}