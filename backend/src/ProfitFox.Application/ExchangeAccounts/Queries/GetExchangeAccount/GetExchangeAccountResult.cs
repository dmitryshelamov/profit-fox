using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccount;

/// <summary>An account with the bots that trade with it.</summary>
public sealed record GetExchangeAccountResult(ExchangeAccount Account, IReadOnlyList<Bot> Bots);