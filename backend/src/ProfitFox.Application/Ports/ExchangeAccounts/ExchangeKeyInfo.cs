using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.Ports.ExchangeAccounts;

/// <summary>What the exchange tells about an API key: whose account it opens and what it may do there.</summary>
public sealed record ExchangeKeyInfo(long Uid, bool IsReadOnly, bool IsUnifiedAccount, bool CanWithdraw, IReadOnlySet<MarketCategory> TradableCategories);